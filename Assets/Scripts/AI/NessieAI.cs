// -----------------------------------------------------------------------------
// NessieAI — the quarry. Server-authoritative, three states, and hard to find.
//
// AUTHORITY
// She is simulated only on the host. Clients receive a replicated transform and
// nothing else — no destination, no threat value, no reasoning. That is not
// tidiness, it is the game: an opponent whose position clients can read is an
// opponent players can trivially cheat against, and this whole design exists to
// make finding her difficult.
//
// NETWORK VISIBILITY
// Replicating her transform to everyone would leak her position even with
// server-resolved sonar — a modified client just reads the NetworkTransform. So
// she is NetworkHidden from any client too far away to legitimately see her, with
// hysteresis on the boundary so a boat loitering at the edge does not cause a
// stream of spawn/despawn churn.
//
// The honest limitation, stated once: NGO cannot hide an object from the HOST's
// own client, because that client *is* the server. A cheating host can therefore
// see her. The client-side render gate below hides her visually, which defeats a
// casual host but not a determined one. The real fix is a dedicated server build,
// and that is the recommendation if this ever runs for strangers.
//
// The decision-making itself lives in Sim/QuarryBrain.cs, engine-free.
// -----------------------------------------------------------------------------

using LochNess.Boot;
using LochNess.Sim;
using LochNess.World;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.AI
{
    public sealed class NessieAI : NetworkBehaviour
    {
        /// <summary>The host's instance. Null on clients — deliberately.</summary>
        public static NessieAI ServerInstance { get; private set; }

        [Header("Swimming")]
        [SerializeField] private float turnRateDegrees = 42f;
        [SerializeField] private float pitchRateDegrees = 26f;
        [SerializeField] private float maxPitchDegrees = 34f;
        [Tooltip("How briskly she reaches her ordered speed. She is large; this is slow.")]
        [SerializeField] private float speedResponse = 1.1f;

        [Header("Depth")]
        [SerializeField] private float cruiseDepthMin = 11f;
        [SerializeField] private float cruiseDepthMax = 26f;
        [SerializeField] private float bedClearance = 3.2f;

        [Header("Surfacing")]
        [Tooltip("Seconds between opportunities to surface, when calm.")]
        [SerializeField] private float surfaceIntervalMin = 26f;
        [SerializeField] private float surfaceIntervalMax = 52f;
        [SerializeField] private float surfaceDurationMin = 6f;
        [SerializeField] private float surfaceDurationMax = 11f;

        [Header("Network visibility")]
        [Tooltip("Clients closer than this receive her transform at all.")]
        [SerializeField] private float visibilityRadius = 165f;
        [Tooltip("Must exceed visibilityRadius. The gap is the anti-churn hysteresis.")]
        [SerializeField] private float hideRadius = 205f;
        [Tooltip("Local render gate — mitigates the host-sees-everything limitation above.")]
        [SerializeField] private float renderRadius = 150f;

        // ---- Replicated ------------------------------------------------------
        // Only what the CLIENTS need in order to animate her correctly. Threat,
        // destination and timers stay on the server.
        private readonly NetworkVariable<byte> _state = new NetworkVariable<byte>((byte)QuarryState.Wandering);
        private readonly NetworkVariable<float> _swimSpeed = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<bool> _surfaced = new NetworkVariable<bool>(false);

        // ---- Server-only -----------------------------------------------------
        private QuarryBrain _brain;
        private Vector3 _destination;
        private float _speed;
        private float _repathTimer;
        private float _nextSurfaceAt;
        private float _surfaceUntil;
        private readonly Vec3[] _hunterScratch = new Vec3[8];
        private System.Random _rng;

        // ---- Visual ----------------------------------------------------------
        private Transform[] _spine = new Transform[0];
        private Transform[] _neck = new Transform[0];
        private float _swimPhase;
        private Renderer[] _renderers = new Renderer[0];

        public QuarryState State => (QuarryState)_state.Value;
        public bool IsSurfaced => _surfaced.Value;

        /// <summary>Her acoustic cross-section right now. Read by SonarSet on the server.</summary>
        public float SonarSignature { get; private set; } = 0.75f;

        /// <summary>Called by NessieBuilder before the prefab is forged.</summary>
        public void Bind(Transform[] spine, Transform[] neck)
        {
            _spine = spine ?? new Transform[0];
            _neck = neck ?? new Transform[0];
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            _renderers = GetComponentsInChildren<Renderer>(true);

            if (!IsServer) return;

            ServerInstance = this;
            _brain = new QuarryBrain(QuarryTuning.Default);
            _rng = new System.Random(System.Environment.TickCount ^ 0x5EA);

            _destination = transform.position;
            ScheduleNextSurfacing();
            PickWanderTarget();
        }

        public override void OnNetworkDespawn()
        {
            if (ServerInstance == this) ServerInstance = null;
        }

        /// <summary>
        /// Assigned to NetworkObject.CheckObjectVisibility BEFORE spawning, so she is
        /// never briefly visible to the whole session on the spawn tick.
        /// </summary>
        public bool ShouldBeVisibleTo(ulong clientId)
        {
            var net = NetworkManager.Singleton;
            if (net == null) return true;
            if (clientId == net.ServerClientId) return true; // cannot hide from the server

            Vector3 watcher;
            if (!TryGetClientPosition(clientId, out watcher)) return false;
            return Vector3.Distance(transform.position, watcher) <= visibilityRadius;
        }

        // ---------------------------------------------------------------------
        // Server simulation
        // ---------------------------------------------------------------------

        private void Update()
        {
            float dt = Time.deltaTime;
            if (IsServer) ServerStep(dt);
            Animate(dt);
            ApplyRenderGate();
        }

        private void ServerStep(float dt)
        {
            int hunters = GatherHunters();
            QuarryDecision decision = _brain.Tick(transform.position.ToSim(), _hunterScratch, hunters, dt);

            SonarSignature = decision.Signature;
            _state.Value = (byte)decision.State;

            if (decision.StateChanged) OnStateEntered(decision);
            if (decision.WantsNewHide) PickHideTarget();

            UpdateSurfacing(decision, dt);

            _repathTimer -= dt;
            if (_repathTimer <= 0f || ReachedDestination()) ChooseDestination(decision);

            Swim(decision, dt);
            UpdateVisibility();
        }

        private void OnStateEntered(QuarryDecision decision)
        {
            switch (decision.State)
            {
                case QuarryState.Fleeing:
                    // Bolt: a long run directly away from whatever spooked her, and no
                    // surfacing until she has settled again.
                    PickFleeTarget(decision.AwayFromThreat.ToUnity());
                    _surfaceUntil = 0f;
                    ScheduleNextSurfacing();
                    break;

                case QuarryState.Hiding:
                    PickHideTarget();
                    break;

                default:
                    PickWanderTarget();
                    break;
            }
        }

        private int GatherHunters()
        {
            // The crew are all standing on one boat, so for threat purposes the BOAT is
            // the hunter. Using individual crew positions would triple-count a full
            // crew standing together and make a four-player session terrify her.
            int count = 0;
            var boat = Vessel.BoatController.Instance;
            if (boat != null && count < _hunterScratch.Length)
            {
                _hunterScratch[count++] = boat.transform.position.ToSim();
            }
            return count;
        }

        private void UpdateSurfacing(QuarryDecision decision, float dt)
        {
            float now = Time.time;

            if (_surfaceUntil > now)
            {
                // A surfacing is abandoned the moment she is properly alarmed.
                if (decision.Threat >= _brain.Tuning.HideEnterThreshold)
                {
                    _surfaceUntil = 0f;
                    ScheduleNextSurfacing();
                }
            }
            else if (now >= _nextSurfaceAt
                     && decision.State == QuarryState.Wandering
                     && decision.Threat < 0.18f)
            {
                // The payoff. She only ever breaks the surface when she believes she is
                // alone, which is why a patient crew that stops pinging and drifts is
                // rewarded — and why it is a real decision to stop pinging.
                _surfaceUntil = now + Mathf.Lerp(surfaceDurationMin, surfaceDurationMax, (float)_rng.NextDouble());
                ScheduleNextSurfacing();
            }

            _surfaced.Value = _surfaceUntil > now;
        }

        private void ScheduleNextSurfacing()
        {
            _nextSurfaceAt = Time.time + Mathf.Lerp(surfaceIntervalMin, surfaceIntervalMax, (float)_rng.NextDouble());
        }

        private bool ReachedDestination() =>
            Vector3.Distance(transform.position, _destination) < 6f;

        private void ChooseDestination(QuarryDecision decision)
        {
            switch (decision.State)
            {
                case QuarryState.Fleeing: PickFleeTarget(decision.AwayFromThreat.ToUnity()); break;
                case QuarryState.Hiding: PickHideTarget(); break;
                default: PickWanderTarget(); break;
            }
        }

        private void PickWanderTarget()
        {
            Vector3 point = RandomBasinPoint(0.82f);
            point.y = -Mathf.Lerp(cruiseDepthMin, cruiseDepthMax, (float)_rng.NextDouble());
            _destination = ClampToWater(point);
            _repathTimer = 12f + (float)_rng.NextDouble() * 9f;
        }

        private void PickHideTarget()
        {
            // Go to ground: a short move to the nearest deep, dark part of the bed. She
            // does not travel far while hiding — that is what makes a methodical search
            // pattern beat a frantic one.
            Vector3 here = transform.position;
            float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
            float distance = 18f + (float)_rng.NextDouble() * 26f;

            Vector3 point = here + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            point.y = LochBuilder.SampleGround(point.x, point.z) + bedClearance;

            _destination = ClampToWater(point);
            _repathTimer = 7f + (float)_rng.NextDouble() * 6f;
        }

        private void PickFleeTarget(Vector3 away)
        {
            if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
            away.y = 0f;
            away = away.normalized;

            Vector3 point = transform.position + away * 130f;
            // Down as well as away — depth is her best cover and the sonar's falloff
            // does not care about it, but the bow watch's eyes very much do.
            point.y = Mathf.Min(-cruiseDepthMax, LochBuilder.SampleGround(point.x, point.z) + bedClearance + 6f);

            _destination = ClampToWater(point);
            _repathTimer = 3.5f;
        }

        private Vector3 RandomBasinPoint(float extent)
        {
            float angle = (float)_rng.NextDouble() * Mathf.PI * 2f;
            float radius = Mathf.Sqrt((float)_rng.NextDouble()) * extent; // uniform over the ellipse
            return new Vector3(
                Mathf.Cos(angle) * radius * LochBuilder.BasinRadiusX,
                0f,
                Mathf.Sin(angle) * radius * LochBuilder.BasinRadiusZ);
        }

        /// <summary>Keep a point inside the basin, off the bed, and under the surface.</summary>
        private Vector3 ClampToWater(Vector3 point)
        {
            float nx = point.x / (LochBuilder.BasinRadiusX * 0.9f);
            float nz = point.z / (LochBuilder.BasinRadiusZ * 0.9f);
            float d = nx * nx + nz * nz;
            if (d > 1f)
            {
                float scale = 1f / Mathf.Sqrt(d);
                point.x *= scale;
                point.z *= scale;
            }

            float bed = LochBuilder.SampleGround(point.x, point.z) + bedClearance;
            point.y = Mathf.Clamp(point.y, bed, -1.4f);
            return point;
        }

        private void Swim(QuarryDecision decision, float dt)
        {
            _speed = Mathf.MoveTowards(_speed, decision.DesiredSpeed, speedResponse * decision.DesiredSpeed * dt + 0.4f * dt);
            _swimSpeed.Value = _speed;

            Vector3 target = _destination;
            if (_surfaced.Value)
            {
                // Rise until the humps and neck are clear of the water, holding station
                // roughly where she is rather than continuing her transit.
                target = transform.position + transform.forward * 8f;
                target.y = -0.9f;
            }

            Vector3 toTarget = target - transform.position;
            if (toTarget.sqrMagnitude < 0.01f) return;

            // Yaw and pitch are steered separately and rate-limited. A fourteen-metre
            // animal that can turn on a sixpence looks like a fish in a bowl; the
            // limits are what give her mass.
            float desiredYaw = Mathf.Atan2(toTarget.x, toTarget.z) * Mathf.Rad2Deg;
            float flat = new Vector2(toTarget.x, toTarget.z).magnitude;
            float desiredPitch = Mathf.Clamp(-Mathf.Atan2(toTarget.y, Mathf.Max(0.1f, flat)) * Mathf.Rad2Deg,
                                             -maxPitchDegrees, maxPitchDegrees);

            Vector3 euler = transform.eulerAngles;
            float yaw = Mathf.MoveTowardsAngle(euler.y, desiredYaw, turnRateDegrees * dt);
            float pitch = Mathf.MoveTowardsAngle(euler.x, desiredPitch, pitchRateDegrees * dt);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 next = transform.position + transform.forward * (_speed * dt);

            // Hard floor and ceiling. The clamp is applied to the RESULT rather than
            // the destination because a steep pitch can otherwise drive her through
            // the bed between waypoints.
            float bed = LochBuilder.SampleGround(next.x, next.z) + bedClearance * 0.6f;
            float ceiling = _surfaced.Value ? 1.6f : -1.2f;
            next.y = Mathf.Clamp(next.y, bed, ceiling);

            if (!LochBuilder.InsideBasin(next.x, next.z, 12f))
            {
                // Bounced off the shore: turn her back toward open water rather than
                // letting her grind along the margin.
                next = transform.position;
                _destination = ClampToWater(RandomBasinPoint(0.5f) + Vector3.down * 18f);
                _repathTimer = 4f;
            }

            transform.position = next;
        }

        // ---------------------------------------------------------------------
        // Provocation — called by SonarSet on the server
        // ---------------------------------------------------------------------

        public void HearPing(float distance, float aggression)
        {
            if (!IsServer || _brain == null) return;
            _brain.HearPing(distance, aggression);
        }

        public void Provoke(float amount)
        {
            if (!IsServer || _brain == null) return;
            _brain.Provoke(amount);
        }

        // ---------------------------------------------------------------------
        // Visibility
        // ---------------------------------------------------------------------

        private float _visibilityTimer;

        private void UpdateVisibility()
        {
            // Four times a second is plenty: the boat's top speed is 9 m/s and the
            // hysteresis band is 40 m wide, so nothing can cross it between checks.
            _visibilityTimer -= Time.deltaTime;
            if (_visibilityTimer > 0f) return;
            _visibilityTimer = 0.25f;

            var net = NetworkManager.Singleton;
            if (net == null || NetworkObject == null || !NetworkObject.IsSpawned) return;

            foreach (ulong clientId in net.ConnectedClientsIds)
            {
                if (clientId == net.ServerClientId) continue;
                if (!TryGetClientPosition(clientId, out Vector3 watcher)) continue;

                float distance = Vector3.Distance(transform.position, watcher);
                bool visible = NetworkObject.IsNetworkVisibleTo(clientId);

                if (!visible && distance <= visibilityRadius) NetworkObject.NetworkShow(clientId);
                else if (visible && distance > hideRadius) NetworkObject.NetworkHide(clientId);
            }
        }

        private static bool TryGetClientPosition(ulong clientId, out Vector3 position)
        {
            position = Vector3.zero;
            var net = NetworkManager.Singleton;
            if (net == null || !net.ConnectedClients.TryGetValue(clientId, out NetworkClient client)) return false;

            var playerObject = client.PlayerObject;
            if (playerObject == null) return false;

            position = playerObject.transform.position;
            return true;
        }

        /// <summary>
        /// Local render gate. Purely visual, and purely a mitigation for the host case
        /// described at the top of this file — the host's client cannot be NetworkHidden
        /// from, so at least stop drawing her.
        /// </summary>
        private void ApplyRenderGate()
        {
            Camera view = Camera.main;
            if (view == null) return;

            bool shouldRender = Vector3.Distance(transform.position, view.transform.position) <= renderRadius;
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null && _renderers[i].enabled != shouldRender) _renderers[i].enabled = shouldRender;
            }
        }

        // ---------------------------------------------------------------------
        // Animation — runs on every peer, from replicated speed
        // ---------------------------------------------------------------------

        private void Animate(float dt)
        {
            float speed = _swimSpeed.Value;
            float effort = Mathf.Clamp01(speed / 9f);

            // Stroke rate and amplitude both rise with effort: a fleeing animal
            // thrashes, a hiding one barely moves. This is the only tell you get at a
            // distance about what she is doing.
            _swimPhase += dt * (0.7f + effort * 3.4f);
            float amplitude = Mathf.Lerp(3.5f, 13f, effort);

            for (int i = 0; i < _spine.Length; i++)
            {
                // The phase offset per segment is what makes it a travelling wave down
                // the body rather than the whole animal wagging.
                float wave = Mathf.Sin(_swimPhase * 2f - i * 0.62f);
                float taper = Mathf.Lerp(0.35f, 1.4f, i / Mathf.Max(1f, _spine.Length - 1f));
                _spine[i].localRotation = Quaternion.Euler(0f, wave * amplitude * taper, wave * 2.2f);
            }

            for (int i = 0; i < _neck.Length; i++)
            {
                float wave = Mathf.Sin(_swimPhase * 1.3f - i * 0.4f);
                _neck[i].localRotation = Quaternion.Euler(-11f + wave * 2.4f, wave * 3.2f, 0f);
            }
        }
    }
}
