// ---------------------------------------------------------------------------------------------
//  AssistantAI.cs
//  Role : The single-player crewmate. A second research launch, driven by a NavMeshAgent, that
//         escorts the player, runs its own sonar sweeps and radios in what it finds.
//
//  WHY IT EXISTS
//  --------------------------------------------------------------------------------------------
//  The core loop is built around a crew triangulating one monster from several hulls. Solo, that
//  loop collapses - one boat, one bearing, no cross-reference. The assistant restores it: a second
//  sonar source at a known offset gives the player a second bearing to cut against their own.
//
//  DELIBERATE DESIGN LIMIT
//  --------------------------------------------------------------------------------------------
//  The assistant REPORTS contacts but never SCORES them. Confirmed sightings can only be earned by
//  a human ping (see PlayerController.ResolvePingOnServer). The companion is a sensor, not an
//  autopilot - otherwise solo play would beat itself while the player watched.
//
//  AUTHORITY
//  --------------------------------------------------------------------------------------------
//  Server-owned, exactly like Nessie. In single-player the server IS the local player, so this all
//  runs locally - but because it is written server-authoritatively it also works unchanged when a
//  host enables the companion in a multiplayer session.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace LochNess
{
    /// <summary>What the companion is currently doing. Byte-backed for a one-byte NetworkVariable.</summary>
    public enum AssistantState : byte
    {
        Following = 0,      // Holding station off the player's quarter.
        Sweeping = 1,       // Holding position and working the sonar.
        Investigating = 2,  // Running down a biological contact it picked up.
        Regrouping = 3      // Too far from the player - closing the gap, sensors off.
    }

    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(NavMeshAgent))]
    [DisallowMultipleComponent]
    public class AssistantAI : NetworkBehaviour
    {
        /// <summary>The server's companion. Null on clients and when solo mode is not active.</summary>
        public static AssistantAI ServerInstance { get; private set; }

        /// <summary>Raised on every peer when the replicated state changes. Hook animation/VFX here.</summary>
        public event Action<AssistantState> OnStateChanged;

        // -----------------------------------------------------------------------------------------
        //  Inspector
        // -----------------------------------------------------------------------------------------

        [Header("Scene References")]
        [Tooltip("Hull mesh root. Bobbed locally on every peer; never networked.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private AudioSource sonarAudioSource;
        [SerializeField] private AudioClip sonarPingClip;

        [Header("Station Keeping")]
        [Tooltip("How far off the player's stern the companion tries to sit.")]
        [SerializeField] private float followDistance = 18f;
        [Tooltip("Lateral offset so it sits off the quarter rather than directly astern.")]
        [SerializeField] private float followSideOffset = 10f;
        [Tooltip("Player must move at least this far before the station point is recomputed. Stops path thrash.")]
        [SerializeField] private float repathMoveThreshold = 6f;
        [Tooltip("Beyond this range from the player the companion abandons its task and closes up.")]
        [SerializeField] private float leashDistance = 90f;
        [SerializeField] private float regroupExitDistance = 35f;

        [Header("Movement")]
        [SerializeField] private float cruiseSpeed = 8f;
        [SerializeField] private float sprintSpeed = 11f;
        [SerializeField] private float turnSpeed = 140f;
        [SerializeField] private float acceleration = 10f;
        [SerializeField] private float navSampleRadius = 8f;

        [Header("Sonar")]
        [SerializeField] private float sonarRange = 110f;
        [Tooltip("Seconds between automatic sweeps. Slower than a human can ping, on purpose.")]
        [SerializeField] private float pingInterval = 6f;
        [Tooltip("Clarity needed before the companion calls a contact in over the radio.")]
        [Range(0f, 1f)][SerializeField] private float reportClarityThreshold = 0.35f;
        [Tooltip("How much a companion sweep agitates Nessie. Its sonar is quieter than a player's.")]
        [Range(0f, 1f)][SerializeField] private float pingAggression = 0.3f;

        [Header("Investigation")]
        [SerializeField] private float investigateTimeout = 25f;
        [Tooltip("Stops chasing a contact once it is this close to the estimated position.")]
        [SerializeField] private float investigateArriveDistance = 12f;
        [Tooltip("Minimum gap between radio calls so the companion is not a chatterbox.")]
        [SerializeField] private float radioCooldown = 8f;

        [Header("Water")]
        [SerializeField] private float bobAmplitude = 0.1f;
        [SerializeField] private float bobFrequency = 0.65f;
        [SerializeField] private float rollAmplitude = 2f;

        // -----------------------------------------------------------------------------------------
        //  Replicated state
        // -----------------------------------------------------------------------------------------

        private readonly NetworkVariable<AssistantState> _state =
            new NetworkVariable<AssistantState>(AssistantState.Following);

        /// <summary>Replicated so a HUD can show a "contact!" marker on the companion boat.</summary>
        private readonly NetworkVariable<bool> _hasActiveContact = new NetworkVariable<bool>(false);

        public AssistantState State => IsSpawned ? _state.Value : AssistantState.Following;
        public bool HasActiveContact => IsSpawned && _hasActiveContact.Value;

        // -----------------------------------------------------------------------------------------
        //  Server-only runtime
        // -----------------------------------------------------------------------------------------

        private NavMeshAgent _agent;

        private PlayerController _escortTarget;
        private float _nextEscortRefreshTime;

        private Vector3 _lastStationAnchor;     // Player position the current station point was built from.
        private bool _hasStationAnchor;
        private Vector3 _investigateTarget;
        private float _investigateStartedAt;
        private float _nextPingTime;
        private float _nextRadioTime;
        private float _stuckTimer;
        private float _stateEnteredAt;

        private readonly List<SonarContact> _contactBuffer = new List<SonarContact>(8);

        // Reused for every path query - see NessieAI for the same reasoning.
        private readonly NavMeshPath _pathBuffer = new NavMeshPath();

        /// <summary>Minimum dwell before the cosmetic Following/Sweeping flip is allowed.</summary>
        private const float StationStateDwell = 0.75f;

        // =========================================================================================
        //  Lifecycle
        // =========================================================================================

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
        }

        public override void OnNetworkSpawn()
        {
            _state.OnValueChanged += HandleStateReplicated;

            if (IsServer)
            {
                ServerInstance = this;
                ConfigureAgentForServer();

                // Explicit reset: the prefab is respawned per session and NetworkVariables do not
                // clear themselves between sessions.
                _state.Value = AssistantState.Following;
                _hasActiveContact.Value = false;

                _stateEnteredAt = Time.time;
                _nextPingTime = Time.time + pingInterval * 0.5f;
                _hasStationAnchor = false;
            }
            else
            {
                // Same rule as Nessie: on a client the agent would fight NetworkTransform.
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

        private void HandleStateReplicated(AssistantState previous, AssistantState current)
            => OnStateChanged?.Invoke(current);

        private void ConfigureAgentForServer()
        {
            if (_agent == null) return;

            _agent.enabled = true;
            _agent.speed = cruiseSpeed;
            _agent.angularSpeed = turnSpeed;
            _agent.acceleration = acceleration;
            _agent.autoBraking = true;          // A boat holding station should ease in, not slam to a stop.
            _agent.stoppingDistance = 2f;
            _agent.updateRotation = true;

            // Snap onto the water-surface NavMesh. Without this the agent is "off mesh" and every
            // SetDestination call fails silently.
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSampleRadius * 4f, _agent.areaMask))
            {
                _agent.Warp(hit.position);
            }
            else
            {
                Debug.LogError("[AssistantAI] Could not place the companion on the water NavMesh. " +
                               "Check that a NavMeshSurface for the boat Agent Type is baked over the loch.", this);
            }
        }

        // =========================================================================================
        //  Update
        // =========================================================================================

        private void Update()
        {
            if (!IsSpawned) return;

            ApplyLocalHullMotion(); // Cosmetic, every peer, zero bandwidth.

            if (!IsServer) return;

            ResolveEscortTarget();

            // With nobody to escort (player still loading, or disconnected) the companion simply
            // holds station rather than pathing to a null transform.
            if (_escortTarget == null) return;

            UpdateLeash();
            UpdateSonar();
            UpdateStateBehaviour();
        }

        /// <summary>
        /// Finds the human to escort. Refreshed periodically rather than cached forever so a
        /// respawned or late-joining player is picked up automatically.
        /// </summary>
        private void ResolveEscortTarget()
        {
            if (_escortTarget != null && _escortTarget.IsSpawned && Time.time < _nextEscortRefreshTime) return;

            _nextEscortRefreshTime = Time.time + 2f;

            var netManager = NetworkManager;
            if (netManager == null || !netManager.IsServer) return;

            // Solo has exactly one player; in a host session the companion escorts the host.
            foreach (var client in netManager.ConnectedClientsList)
            {
                if (client?.PlayerObject == null) continue;

                var controller = client.PlayerObject.GetComponent<PlayerController>();
                if (controller == null) continue;

                _escortTarget = controller;
                return;
            }

            _escortTarget = null;
        }

        // -----------------------------------------------------------------------------------------
        //  Leash
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Hard guarantee that the companion never strands itself chasing a contact across the loch.
        /// Enter and exit distances differ so it does not oscillate at the leash boundary.
        /// </summary>
        private void UpdateLeash()
        {
            float distance = Vector3.Distance(transform.position, _escortTarget.transform.position);

            if (_state.Value != AssistantState.Regrouping)
            {
                if (distance > leashDistance) EnterState(AssistantState.Regrouping);
            }
            else if (distance <= regroupExitDistance)
            {
                EnterState(AssistantState.Following);
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Sonar
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Automatic sweeps on a slow cadence. Runs the exact same <see cref="Sonar"/> resolution the
        /// player's tool uses, so the companion can never sense anything a human could not.
        /// </summary>
        private void UpdateSonar()
        {
            // Sensors are stowed while closing a big gap - it should be driving, not listening.
            if (_state.Value == AssistantState.Regrouping) return;
            if (Time.time < _nextPingTime) return;

            _nextPingTime = Time.time + pingInterval;

            int seed = unchecked((int)(NetworkManager.ServerTime.Tick * 19349663) ^ (int)NetworkObjectId);
            Sonar.Resolve(transform.position, sonarRange, seed, _contactBuffer,
                          out float clarity, out float distance);

            // Active sonar is active sonar: the companion's pings spook Nessie too. That is a real
            // cost to keeping it nearby, and it is meant to be felt.
            NessieAI nessie = NessieAI.ServerInstance;
            if (nessie != null) nessie.NotifyPinged(transform.position, pingAggression * Mathf.Max(0.2f, clarity));

            PlayPingEffectClientRpc();

            if (clarity < reportClarityThreshold || float.IsPositiveInfinity(distance))
            {
                // Nothing worth calling in. Drop the marker if we were holding a stale one.
                if (_state.Value != AssistantState.Investigating) _hasActiveContact.Value = false;
                return;
            }

            // Rebuild an approximate world position from the polar contact. Because the contact
            // carries the same range error the player sees, the companion chases the same
            // imperfect information the player does - it cannot cheat its way to her.
            SonarContact best = FindStrongestBiological(_contactBuffer);
            _investigateTarget = PolarToWorld(transform.position, best.Bearing, best.Range);

            _hasActiveContact.Value = true;
            _investigateStartedAt = Time.time;

            // If we were already investigating, the agent is holding a path to the PREVIOUS
            // estimate. Clear it so UpdateInvestigating repaths to the fresher contact instead of
            // stubbornly driving to a bearing that is now several sweeps out of date.
            if (_state.Value == AssistantState.Investigating
                && _agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.ResetPath();
            }

            EnterState(AssistantState.Investigating);

            Radio($"Contact! Bearing {Mathf.RoundToInt(best.Bearing):000}, " +
                  $"range {Mathf.RoundToInt(best.Range)}m. Moving to investigate.");
        }

        private static SonarContact FindStrongestBiological(List<SonarContact> contacts)
        {
            SonarContact best = default;
            float bestClarity = -1f;

            for (int i = 0; i < contacts.Count; i++)
            {
                if (contacts[i].KindEnum != SonarContactKind.Biological) continue;
                if (contacts[i].Clarity <= bestClarity) continue;

                bestClarity = contacts[i].Clarity;
                best = contacts[i];
            }

            return best;
        }

        /// <summary>Converts a polar sonar contact back into an approximate world position.</summary>
        private static Vector3 PolarToWorld(Vector3 origin, float bearingDegrees, float range)
        {
            float radians = bearingDegrees * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            return origin + direction * range;
        }

        // -----------------------------------------------------------------------------------------
        //  State behaviour
        // -----------------------------------------------------------------------------------------

        private void UpdateStateBehaviour()
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;

            UpdateStuckDetection();

            switch (_state.Value)
            {
                case AssistantState.Following:
                case AssistantState.Sweeping:
                    _agent.speed = cruiseSpeed;
                    UpdateStationKeeping();
                    break;

                case AssistantState.Investigating:
                    _agent.speed = cruiseSpeed;
                    UpdateInvestigating();
                    break;

                case AssistantState.Regrouping:
                    _agent.speed = sprintSpeed;
                    UpdateRegrouping();
                    break;
            }
        }

        /// <summary>
        /// Holds station off the player's quarter. The destination is only recomputed once the
        /// player has actually moved - repathing every frame would thrash the NavMesh query queue
        /// and make the companion twitch.
        /// </summary>
        private void UpdateStationKeeping()
        {
            Vector3 playerPos = _escortTarget.transform.position;

            bool anchorStale = !_hasStationAnchor
                            || Vector3.Distance(playerPos, _lastStationAnchor) > repathMoveThreshold
                            || _agent.pathStatus == NavMeshPathStatus.PathInvalid;

            // Toggle the cosmetic state so the HUD can tell "escorting" from "actively listening".
            // The dwell guard matters: without it, a boat hovering right on stoppingDistance would
            // flip state - and therefore write a NetworkVariable - several times a second.
            bool onStation = !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 1f;
            AssistantState desired = onStation ? AssistantState.Sweeping : AssistantState.Following;
            if (_state.Value != desired && Time.time - _stateEnteredAt >= StationStateDwell)
            {
                EnterState(desired);
            }

            if (!anchorStale) return;

            _lastStationAnchor = playerPos;
            _hasStationAnchor = true;

            Vector3 station = playerPos
                            - _escortTarget.transform.forward * followDistance
                            + _escortTarget.transform.right * followSideOffset;

            if (!TrySetDestination(station))
            {
                // Station point is on land or outside the mesh (player hugging the shore). Fall back
                // to a point straight behind, which is almost always still on open water.
                TrySetDestination(playerPos - _escortTarget.transform.forward * followDistance);
            }
        }

        private void UpdateInvestigating()
        {
            bool timedOut = Time.time - _investigateStartedAt > investigateTimeout;
            bool arrived = Vector3.Distance(transform.position, _investigateTarget) <= investigateArriveDistance;

            if (timedOut || arrived)
            {
                _hasActiveContact.Value = false;
                _hasStationAnchor = false; // Force a fresh station point on the way back.
                EnterState(AssistantState.Following);

                Radio(arrived
                    ? "Nothing on the surface here. Returning to station."
                    : "Lost the contact. Re-forming on you.");
                return;
            }

            // Repath periodically rather than every frame; the target does not move between pings.
            if (_agent.pathPending || _agent.hasPath) return;
            if (!TrySetDestination(_investigateTarget))
            {
                // The estimate landed on unreachable water - abandon rather than grind against it.
                _hasActiveContact.Value = false;
                EnterState(AssistantState.Following);
            }
        }

        private void UpdateRegrouping()
        {
            Vector3 playerPos = _escortTarget.transform.position;

            if (_agent.pathPending) return;

            // Straight at the player. stoppingDistance keeps it from ramming the hull.
            // hasPath is tested FIRST: destination is meaningless until a path exists.
            if (!_agent.hasPath || Vector3.Distance(_agent.destination, playerPos) > repathMoveThreshold)
            {
                TrySetDestination(playerPos);
            }
        }

        /// <summary>
        /// Commits a destination only when a COMPLETE path exists.
        /// <para>
        /// PathPartial is the trap here: SetDestination happily accepts an unreachable point, the
        /// agent drives to the closest reachable spot, and then reports "arrived" somewhere it
        /// never wanted to be. Validating up front lets the caller pick a fallback instead.
        /// </para>
        /// </summary>
        private bool TrySetDestination(Vector3 worldPoint)
        {
            if (_agent == null || !_agent.isOnNavMesh) return false;

            if (!NavMesh.SamplePosition(worldPoint, out NavMeshHit hit, navSampleRadius, _agent.areaMask))
            {
                return false;
            }

            if (!_agent.CalculatePath(hit.position, _pathBuffer)
                || _pathBuffer.status != NavMeshPathStatus.PathComplete)
            {
                return false;
            }

            _agent.SetPath(_pathBuffer);
            return true;
        }

        /// <summary>
        /// Detects an agent that wants to move but is not moving (wedged on geometry, or a path
        /// invalidated by a NavMesh change) and forces a fresh plan.
        /// </summary>
        private void UpdateStuckDetection()
        {
            bool wantsToMove = _agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + 0.5f;

            if (wantsToMove && _agent.velocity.sqrMagnitude < 0.05f) _stuckTimer += Time.deltaTime;
            else _stuckTimer = 0f;

            if (_stuckTimer < 2f) return;

            _stuckTimer = 0f;
            _agent.ResetPath();
            _hasStationAnchor = false; // Invalidate the cached anchor so station keeping recomputes.

            // Nudge onto the nearest valid mesh position in case we drifted off it entirely.
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navSampleRadius, _agent.areaMask))
            {
                _agent.Warp(hit.position);
            }
        }

        private void EnterState(AssistantState next)
        {
            if (!IsServer || _state.Value == next) return;

            _state.Value = next;   // Server-write NetworkVariable - legal, we are the server.
            _stateEnteredAt = Time.time;
            _stuckTimer = 0f;
        }

        // -----------------------------------------------------------------------------------------
        //  Radio + cosmetics
        // -----------------------------------------------------------------------------------------

        /// <summary>Sends a line to the escorted player, rate-limited so it stays readable.</summary>
        private void Radio(string message)
        {
            if (!IsServer || _escortTarget == null) return;
            if (Time.time < _nextRadioTime) return;

            _nextRadioTime = Time.time + radioCooldown;
            _escortTarget.SendRadioMessage($"[Crew] {message}");
        }

        [ClientRpc]
        private void PlayPingEffectClientRpc()
        {
            if (sonarAudioSource != null && sonarPingClip != null) sonarAudioSource.PlayOneShot(sonarPingClip);
        }

        /// <summary>Cosmetic swell, matched to the player boat. Local only - never networked.</summary>
        private void ApplyLocalHullMotion()
        {
            if (visualRoot == null) return;

            float phase = Time.time * bobFrequency * Mathf.PI * 2f + (NetworkObjectId * 0.7f);
            visualRoot.localPosition = new Vector3(0f, Mathf.Sin(phase) * bobAmplitude, 0f);
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(phase * 0.5f) * rollAmplitude);
        }

        // -----------------------------------------------------------------------------------------
        //  Editor aids
        // -----------------------------------------------------------------------------------------

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, sonarRange);

            if (!Application.isPlaying || _escortTarget == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_escortTarget.transform.position, leashDistance);

            if (_state.Value != AssistantState.Investigating) return;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _investigateTarget);
        }
    }
}
