// ---------------------------------------------------------------------------------------------
//  NessieAI.cs
//  Role : Server-authoritative monster. Wandering / Hiding / Fleeing state machine plus the
//         network-visibility rules that stop clients from simply reading her position.
//
//  AUTHORITY - the single most important decision in this project
//  --------------------------------------------------------------------------------------------
//  Nessie is owned by the SERVER and by nobody else:
//      * The state machine, NavMeshAgent and threat model execute only when IsServer is true.
//      * Clients receive a replicated position (stock server-authoritative NetworkTransform) and a
//        replicated NessieState for VFX/audio. They never simulate her.
//      * The NavMeshAgent is DISABLED on every client. Leaving it enabled is the classic NGO bug:
//        the agent and NetworkTransform both write the transform and fight each other, producing
//        a monster that stutters and teleports on every client except the host.
//
//  THE INFORMATION LEAK, AND THE FIX
//  --------------------------------------------------------------------------------------------
//  A replicated NetworkTransform is readable by anyone running a modified client - a wallhack that
//  trivially defeats a hide-and-seek game. We close it with distance-based NETWORK VISIBILITY:
//  Nessie is only spawned on clients whose boat is inside VisibilityRadius, so a distant client
//  receives no transform data at all. Sonar remains server-resolved on top of that.
//
//  Known residual: NGO cannot hide an object from the host's own client (it IS the server), so a
//  cheating HOST could still see her. We mitigate visually with a client-side render gate below,
//  and note the real fix - a dedicated server build - in the README. Host trust is unavoidable in
//  a listen-server topology.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LochNess
{
    /// <summary>Nessie's behavioural state. Byte-backed to keep the NetworkVariable one byte wide.</summary>
    public enum NessieState : byte
    {
        Wandering = 0,  // Unbothered. Cruises the loch on a lazy patrol.
        Hiding = 1,     // Suspicious. Slips into deep water and goes quiet - hard to detect.
        Fleeing = 2     // Spooked. Sprints away at speed - fast, but LOUD and easy to track.
    }

    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public class NessieAI : NetworkBehaviour
    {
        /// <summary>The server's Nessie. Null on clients and before spawn - always null-check.</summary>
        public static NessieAI ServerInstance { get; private set; }

        /// <summary>Raised on every peer when the replicated state changes. Hook VFX/audio here.</summary>
        public event Action<NessieState> OnStateChanged;

        // -----------------------------------------------------------------------------------------
        //  Inspector
        // -----------------------------------------------------------------------------------------

        [Header("Scene References")]
        [Tooltip("Child holding ONLY the meshes. Renderers under it are gated client-side by distance.")]
        [SerializeField] private Transform visualRoot;

        [Header("Roaming")]
        [Tooltip("Centre of her patrol area, in world space.")]
        [SerializeField] private Vector3 roamCenter = new Vector3(0f, -10f, 40f);
        [SerializeField] private float roamRadius = 90f;
        [Tooltip("How far a NavMesh sample may search for valid lakebed when a point misses.")]
        [SerializeField] private float navSampleRadius = 12f;
        [Tooltip("Candidate points evaluated when choosing somewhere to hide.")]
        [SerializeField] private int hideCandidateSamples = 10;

        [Header("Movement")]
        [SerializeField] private float wanderSpeed = 3.5f;
        [SerializeField] private float hideSpeed = 1.6f;
        [SerializeField] private float fleeSpeed = 9f;
        [SerializeField] private float turnSpeed = 120f;
        [SerializeField] private float acceleration = 8f;

        [Header("Depth (NavMeshAgent.baseOffset above the lakebed)")]
        [SerializeField] private float wanderDepthOffset = 6f;
        [Tooltip("Lower value = hugs the bottom = harder to see and quieter on sonar.")]
        [SerializeField] private float hideDepthOffset = 1.5f;
        [SerializeField] private float fleeDepthOffset = 4f;
        [SerializeField] private float depthChangeSpeed = 2f;

        [Header("Sonar Signature (how loud she is, 0..1)")]
        [SerializeField] private float wanderSignature = 0.75f;
        [SerializeField] private float hideSignature = 0.3f;
        [Tooltip("Fleeing is deliberately the loudest state - running is what gives her away.")]
        [SerializeField] private float fleeSignature = 1f;

        [Header("Threat Model")]
        [Tooltip("Boats closer than this raise threat continuously.")]
        [SerializeField] private float awarenessRadius = 55f;
        [SerializeField] private float threatDecayPerSecond = 0.16f;

        [Header("Transitions (hysteresis prevents state flicker)")]
        [Range(0f, 1f)][SerializeField] private float hideEnterThreshold = 0.35f;
        [Range(0f, 1f)][SerializeField] private float hideExitThreshold = 0.15f;
        [Range(0f, 1f)][SerializeField] private float fleeEnterThreshold = 0.75f;
        [Range(0f, 1f)][SerializeField] private float fleeExitThreshold = 0.45f;
        [Tooltip("Minimum time in a state before any transition is considered.")]
        [SerializeField] private float minStateDuration = 2.5f;
        [SerializeField] private float maxFleeDuration = 9f;

        [Header("Network Visibility")]
        [Tooltip("Clients closer than this get Nessie spawned. Keep it comfortably above sonar range.")]
        [SerializeField] private float visibilityRadius = 160f;
        [Tooltip("Multiplier on visibilityRadius before hiding again. Stops spawn/despawn churn at the boundary.")]
        [SerializeField] private float visibilityHysteresis = 1.25f;
        [SerializeField] private float visibilityUpdateInterval = 0.5f;
        [Tooltip("Client-side renderer cut-off. Slightly under visibilityRadius so it only bites on the host.")]
        [SerializeField] private float clientRenderRadius = 150f;

        // -----------------------------------------------------------------------------------------
        //  Replicated state
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Server-write, everyone-read. Note this only reaches clients who currently HAVE her
        /// spawned, so the visibility system doubles as the confidentiality boundary for it.
        /// </summary>
        private readonly NetworkVariable<NessieState> _state =
            new NetworkVariable<NessieState>(NessieState.Wandering);

        public NessieState State => IsSpawned ? _state.Value : NessieState.Wandering;

        // -----------------------------------------------------------------------------------------
        //  Server-only runtime
        // -----------------------------------------------------------------------------------------

        private NavMeshAgent _agent;

        private float _threat;                 // 0..1 accumulated alarm.
        private Vector3 _threatOrigin;         // Where the alarm came from - flee direction source.
        private bool _hasThreatOrigin;
        private float _stateEnteredAt;
        private float _nextRepathTime;
        private float _nextVisibilityUpdate;
        private float _stuckTimer;
        private float _targetDepthOffset;

        private readonly List<Transform> _hunterBuffer = new List<Transform>(8);

        // Reused across every path query. NavMeshPath is a managed wrapper around native memory;
        // allocating one per repath would produce steady GC pressure for no reason.
        private readonly NavMeshPath _pathBuffer = new NavMeshPath();

        // Headings tried, in order, when fleeing. Static so the array is shared by all instances.
        private static readonly float[] FleeFanAngles = { 0f, 30f, -30f, 60f, -60f, 100f, -100f };

        /// <summary>Area mask for NavMesh queries, safe to read before the agent is configured.</summary>
        private int AreaMask => _agent != null ? _agent.areaMask : NavMesh.AllAreas;

        // Client-side
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private bool _renderersVisible = true;

        /// <summary>
        /// SERVER-ONLY. How strongly she reflects sonar right now, 0..1. Read by <see cref="Sonar"/>.
        /// Deliberately not replicated: it would leak "she is hiding nearby" to every client.
        /// </summary>
        public float SonarSignature
        {
            get
            {
                return _state.Value switch
                {
                    NessieState.Hiding => hideSignature,
                    NessieState.Fleeing => fleeSignature,
                    _ => wanderSignature
                };
            }
        }

        // =========================================================================================
        //  Lifecycle
        // =========================================================================================

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _renderers = visualRoot != null
                ? visualRoot.GetComponentsInChildren<Renderer>(true)
                : GetComponentsInChildren<Renderer>(true);
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateReplicated;

            if (IsServer)
            {
                ServerInstance = this;

                ConfigureAgentForServer();

                _threat = 0f;
                _hasThreatOrigin = false;
                _stateEnteredAt = Time.time;
                _targetDepthOffset = wanderDepthOffset;

                // Reset explicitly: this prefab is respawned per session and NetworkVariables do not
                // reset themselves, so a fresh hunt must start from a known state.
                _state.Value = NessieState.Wandering;
                EnterState(NessieState.Wandering, force: true);

                // Evaluated by NGO for every client at spawn time AND whenever we call NetworkShow.
                // Late joiners therefore inherit the correct visibility with no extra work.
                NetworkObject.CheckObjectVisibility = ShouldBeVisibleTo;
            }
            else
            {
                // CRITICAL: without this the client-side agent fights NetworkTransform for the
                // transform and the monster jitters. Clients are pure observers.
                if (_agent != null) _agent.enabled = false;
            }

            OnStateChanged?.Invoke(_state.Value);
        }

        public override void OnNetworkDespawn()
        {
            _state.OnValueChanged -= HandleStateReplicated;
            if (ServerInstance == this) ServerInstance = null;
        }

        /// <summary>base.OnDestroy() is mandatory on a NetworkBehaviour.</summary>
        public override void OnDestroy()
        {
            if (ServerInstance == this) ServerInstance = null;
            base.OnDestroy();
        }

        private void HandleStateReplicated(NessieState previous, NessieState current)
            => OnStateChanged?.Invoke(current);

        private void ConfigureAgentForServer()
        {
            if (_agent == null) return;

            _agent.enabled = true;
            _agent.angularSpeed = turnSpeed;
            _agent.acceleration = acceleration;
            _agent.autoBraking = false;      // Keeps the swim continuous instead of stopping at each node.
            _agent.updateRotation = true;
            _agent.baseOffset = wanderDepthOffset;

            // GameManager drops her at an approximate point; Warp snaps her onto the lakebed
            // NavMesh. Without this, SetDestination silently no-ops and she never moves.
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSampleRadius * 4f, AreaMask))
            {
                _agent.Warp(hit.position);
            }
            else
            {
                Debug.LogError("[NessieAI] Could not place Nessie on the lakebed NavMesh. " +
                               "Check that a NavMeshSurface for her Agent Type is baked and that " +
                               "GameManager.nessieSpawnCenter is over it.", this);
            }
        }

        // =========================================================================================
        //  Update
        // =========================================================================================

        private void Update()
        {
            if (!IsSpawned) return;

            // Runs on every peer, host included - this is what keeps a cheating host from simply
            // looking at her through the water.
            UpdateClientRenderGate();

            if (!IsServer) return;

            float dt = Time.deltaTime;

            UpdateThreat(dt);
            UpdateStateMachine();
            UpdateDepth(dt);
            UpdateSteering();

            if (Time.time >= _nextVisibilityUpdate)
            {
                _nextVisibilityUpdate = Time.time + visibilityUpdateInterval;
                UpdateNetworkVisibility();
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Threat model
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Threat decays steadily, but proximity to a boat sets a continuous floor. That means
        /// simply parking on top of her keeps her agitated, while backing off lets her settle -
        /// which is the behaviour that makes the hunt readable to players.
        /// </summary>
        private void UpdateThreat(float dt)
        {
            _threat = Mathf.Max(0f, _threat - threatDecayPerSecond * dt);

            CollectHunters(_hunterBuffer);

            float proximityFloor = 0f;
            float nearestSqr = float.PositiveInfinity;
            Vector3 nearestPos = _threatOrigin;

            for (int i = 0; i < _hunterBuffer.Count; i++)
            {
                Transform hunter = _hunterBuffer[i];
                if (hunter == null) continue;

                float sqr = (hunter.position - transform.position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearestPos = hunter.position;
                }

                float distance = Mathf.Sqrt(sqr);
                if (distance < awarenessRadius)
                {
                    proximityFloor = Mathf.Max(proximityFloor, 1f - (distance / awarenessRadius));
                }
            }

            if (proximityFloor > 0f)
            {
                _threat = Mathf.Max(_threat, proximityFloor);
                _threatOrigin = nearestPos;
                _hasThreatOrigin = true;
            }

            _threat = Mathf.Clamp01(_threat);
        }

        /// <summary>
        /// SERVER: called by the sonar system when a sweep washes over her - by a player's tool or
        /// by the AI companion's. This is the feedback edge that turns detection into evasion:
        /// finding her is exactly what makes her run.
        /// </summary>
        public void NotifyPinged(Vector3 pingOrigin, float strength)
        {
            if (!IsServer) return;

            _threat = Mathf.Clamp01(_threat + Mathf.Clamp01(strength));
            _threatOrigin = pingOrigin;
            _hasThreatOrigin = true;
        }

        /// <summary>
        /// Everything that scares her: every connected player's boat, plus the AI assistant.
        /// Reading NetworkManager.ConnectedClientsList each tick (N is tiny) avoids the lifetime
        /// bugs that come with static registries surviving a session restart.
        /// </summary>
        private void CollectHunters(List<Transform> buffer)
        {
            buffer.Clear();

            var netManager = NetworkManager;
            if (netManager != null && netManager.IsServer)
            {
                foreach (var client in netManager.ConnectedClientsList)
                {
                    if (client?.PlayerObject != null) buffer.Add(client.PlayerObject.transform);
                }
            }

            AssistantAI assistant = AssistantAI.ServerInstance;
            if (assistant != null && assistant.IsSpawned) buffer.Add(assistant.transform);
        }

        // -----------------------------------------------------------------------------------------
        //  State machine
        // -----------------------------------------------------------------------------------------

        private void UpdateStateMachine()
        {
            float timeInState = Time.time - _stateEnteredAt;

            // A minimum dwell time plus separate enter/exit thresholds gives two independent
            // guards against oscillation - a monster that flickers between states reads as broken.
            if (timeInState < minStateDuration) return;

            switch (_state.Value)
            {
                case NessieState.Wandering:
                    if (_threat >= fleeEnterThreshold) EnterState(NessieState.Fleeing);
                    else if (_threat >= hideEnterThreshold) EnterState(NessieState.Hiding);
                    break;

                case NessieState.Hiding:
                    if (_threat >= fleeEnterThreshold) EnterState(NessieState.Fleeing);
                    else if (_threat <= hideExitThreshold) EnterState(NessieState.Wandering);
                    break;

                case NessieState.Fleeing:
                    // Fleeing always resolves into Hiding, never straight back to a lazy patrol:
                    // she has just been spooked, so she goes to ground and stays wary.
                    if (timeInState >= maxFleeDuration || _threat <= fleeExitThreshold)
                    {
                        EnterState(NessieState.Hiding);
                    }
                    break;
            }
        }

        private void EnterState(NessieState next, bool force = false)
        {
            if (!IsServer) return;
            if (!force && _state.Value == next) return;

            _state.Value = next;   // Server-write NetworkVariable - legal, we are the server.
            _stateEnteredAt = Time.time;
            _stuckTimer = 0f;
            _nextRepathTime = 0f;  // Force an immediate destination choice for the new state.

            switch (next)
            {
                case NessieState.Wandering:
                    if (_agent != null) _agent.speed = wanderSpeed;
                    _targetDepthOffset = wanderDepthOffset;
                    break;

                case NessieState.Hiding:
                    if (_agent != null) _agent.speed = hideSpeed;
                    _targetDepthOffset = hideDepthOffset;
                    break;

                case NessieState.Fleeing:
                    if (_agent != null) _agent.speed = fleeSpeed;
                    _targetDepthOffset = fleeDepthOffset;
                    break;
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Navigation
        // -----------------------------------------------------------------------------------------

        private void UpdateSteering()
        {
            // isOnNavMesh guards every agent call. Touching SetDestination on an off-mesh agent
            // logs an error every single frame and is the number-one source of NavMesh spam.
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            bool arrived = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f;

            // Stuck detection: an agent that is trying to move but is not moving needs a new plan.
            if (!arrived && _agent.velocity.sqrMagnitude < 0.05f) _stuckTimer += Time.deltaTime;
            else _stuckTimer = 0f;

            bool needsNewDestination = arrived
                                    || _stuckTimer > 1.5f
                                    || _agent.pathStatus == NavMeshPathStatus.PathInvalid
                                    || Time.time >= _nextRepathTime;

            if (!needsNewDestination) return;

            _stuckTimer = 0f;

            switch (_state.Value)
            {
                case NessieState.Wandering:
                    _nextRepathTime = Time.time + 6f;
                    SetDestinationSafely(PickWanderPoint());
                    break;

                case NessieState.Hiding:
                    _nextRepathTime = Time.time + 4f;
                    SetDestinationSafely(PickHidePoint());
                    break;

                case NessieState.Fleeing:
                    // Repath often while fleeing so she keeps reacting to a pursuing boat.
                    _nextRepathTime = Time.time + 1.5f;
                    SetDestinationSafely(PickFleePoint());
                    break;
            }
        }

        /// <summary>
        /// Commits a destination only if a COMPLETE path exists. A partial path would walk her into
        /// a dead end and strand her against geometry.
        /// </summary>
        private void SetDestinationSafely(Vector3 worldPoint)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;

            if (!NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, navSampleRadius, AreaMask))
            {
                return; // Nothing navigable nearby; the next repath tick will try again.
            }

            if (_agent.CalculatePath(hit.position, _pathBuffer)
                && _pathBuffer.status == NavMeshPathStatus.PathComplete)
            {
                _agent.SetPath(_pathBuffer);
            }
        }

        private Vector3 PickWanderPoint()
        {
            Vector2 disc = UnityEngine.Random.insideUnitCircle * roamRadius;
            return new Vector3(roamCenter.x + disc.x, roamCenter.y, roamCenter.z + disc.y);
        }

        /// <summary>
        /// Samples several lakebed points and keeps the one that maximises distance from every
        /// hunter. Deliberately a scored search rather than "run to the far corner" - she should
        /// look like she is picking cover, not fleeing to a fixed safe spot the players can learn.
        /// </summary>
        private Vector3 PickHidePoint()
        {
            CollectHunters(_hunterBuffer);

            Vector3 best = transform.position;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < hideCandidateSamples; i++)
            {
                Vector2 disc = UnityEngine.Random.insideUnitCircle * roamRadius;
                Vector3 candidate = new Vector3(roamCenter.x + disc.x, roamCenter.y, roamCenter.z + disc.y);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, AreaMask))
                    continue;

                // Score = distance to the nearest hunter, with a mild penalty for swimming far.
                float nearest = float.PositiveInfinity;
                for (int h = 0; h < _hunterBuffer.Count; h++)
                {
                    if (_hunterBuffer[h] == null) continue;
                    nearest = Mathf.Min(nearest, Vector3.Distance(hit.position, _hunterBuffer[h].position));
                }

                if (float.IsPositiveInfinity(nearest)) nearest = roamRadius; // No hunters: all points equal.

                float travelPenalty = Vector3.Distance(transform.position, hit.position) * 0.25f;
                float score = nearest - travelPenalty;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = hit.position;
                }
            }

            return best;
        }

        /// <summary>
        /// Straight away from the threat, with fan-out fallbacks. Open water is rarely a perfect
        /// escape lane, so if the direct heading is off-mesh we try progressively wider angles
        /// rather than giving up and stalling in place.
        /// </summary>
        private Vector3 PickFleePoint()
        {
            Vector3 away = _hasThreatOrigin
                ? (transform.position - _threatOrigin)
                : UnityEngine.Random.insideUnitSphere;

            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = transform.forward;
            away.Normalize();

            const float fleeDistance = 45f;

            for (int i = 0; i < FleeFanAngles.Length; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, FleeFanAngles[i], 0f) * away;
                Vector3 candidate = transform.position + direction * fleeDistance;

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, navSampleRadius, AreaMask))
                {
                    return hit.position;
                }
            }

            // Cornered against the shore: fall back to normal cover-seeking.
            return PickHidePoint();
        }

        /// <summary>Eases the swim depth so state changes read as diving/surfacing, not snapping.</summary>
        private void UpdateDepth(float dt)
        {
            if (_agent == null || !_agent.enabled) return;
            _agent.baseOffset = Mathf.MoveTowards(_agent.baseOffset, _targetDepthOffset, depthChangeSpeed * dt);
        }

        // -----------------------------------------------------------------------------------------
        //  Network visibility
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Evaluated by NGO at spawn and on NetworkShow. Distance is measured to the client's boat,
        /// so a client with no boat yet (still loading) simply does not receive her.
        /// </summary>
        private bool ShouldBeVisibleTo(ulong clientId)
        {
            var netManager = NetworkManager;
            if (netManager == null) return false;

            // The host's own client cannot be hidden from - NGO throws if we try.
            if (clientId == netManager.ServerClientId) return true;

            if (!netManager.ConnectedClients.TryGetValue(clientId, out var client)) return false;
            if (client.PlayerObject == null) return false;

            float distance = Vector3.Distance(client.PlayerObject.transform.position, transform.position);
            return distance <= visibilityRadius;
        }

        /// <summary>
        /// Periodically shows/hides her per client as boats move. CheckObjectVisibility alone only
        /// runs at spawn time, so dynamic visibility has to be driven explicitly.
        /// </summary>
        private void UpdateNetworkVisibility()
        {
            var netManager = NetworkManager;
            if (netManager == null || !netManager.IsServer || !IsSpawned) return;

            float hideRadius = visibilityRadius * Mathf.Max(1f, visibilityHysteresis);

            foreach (var client in netManager.ConnectedClientsList)
            {
                if (client == null) continue;

                ulong clientId = client.ClientId;

                // Never attempt to hide from the server's own client: NGO raises
                // VisibilityChangeException, and on a host that client is the host player.
                if (clientId == netManager.ServerClientId) continue;

                bool currentlyVisible = NetworkObject.IsNetworkVisibleTo(clientId);

                if (client.PlayerObject == null)
                {
                    if (currentlyVisible) NetworkObject.NetworkHide(clientId);
                    continue;
                }

                float distance = Vector3.Distance(client.PlayerObject.transform.position, transform.position);

                // Asymmetric thresholds: show at R, hide at R * hysteresis. Without the gap a boat
                // idling on the boundary would spawn and despawn her every half second.
                if (!currentlyVisible && distance <= visibilityRadius) NetworkObject.NetworkShow(clientId);
                else if (currentlyVisible && distance > hideRadius) NetworkObject.NetworkHide(clientId);
            }
        }

        /// <summary>
        /// Client-side renderer gate. For remote clients this is a near no-op (they only have her
        /// spawned when they are already in range); its real job is the HOST, which NGO cannot hide
        /// objects from. Toggling Renderer.enabled - not the GameObject - keeps the NavMeshAgent,
        /// colliders and NetworkTransform running untouched.
        /// </summary>
        private void UpdateClientRenderGate()
        {
            PlayerController localPlayer = PlayerController.LocalPlayer;

            bool shouldRender = localPlayer != null
                && Vector3.Distance(localPlayer.transform.position, transform.position) <= clientRenderRadius;

            if (shouldRender == _renderersVisible) return;
            _renderersVisible = shouldRender;

            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null) _renderers[i].enabled = shouldRender;
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Editor aids
        // -----------------------------------------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawWireSphere(roamCenter, roamRadius);

            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, awarenessRadius);

            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            Gizmos.DrawWireSphere(transform.position, visibilityRadius);
        }
    }
}
