// -----------------------------------------------------------------------------
// BoatController — the shared vessel: motion, buoyancy, and who is standing where.
//
// AUTHORITY
// The boat is simulated on the SERVER and replicated. The helmsman does not move
// it; they send helm input and receive the result. That costs the driver a little
// latency, and it is worth it: everyone else is standing on this object, so a
// client that could move it directly could throw the entire crew into the loch.
// It also means there is exactly one hull simulation in existence, which is why
// nobody ever desynchronises from the deck they are standing on.
//
// STATIONS
// Occupancy is three replicated client ids, arbitrated by the server. A claim is a
// request, never an assertion — two players pressing E on the helm in the same
// frame is a race the server resolves, and the loser is simply told no.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Boot;
using LochNess.Sim;
using LochNess.World;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.Vessel
{
    public sealed class BoatController : NetworkBehaviour
    {
        /// <summary>Sentinel for an unoccupied station. Client ids are never this.</summary>
        public const ulong Nobody = ulong.MaxValue;

        /// <summary>
        /// Pseudo-client id for the AI crewmate, so it can hold a station through the
        /// same replicated occupancy the players use rather than a parallel mechanism.
        /// Real client ids count up from zero and will never collide with this.
        /// </summary>
        public const ulong CrewmateId = ulong.MaxValue - 1;

        /// <summary>The vessel, on whichever peer is asking. There is only ever one.</summary>
        public static BoatController Instance { get; private set; }

        [Header("Handling")]
        [SerializeField] private float maxForwardSpeed = 9f;
        [SerializeField] private float maxReverseSpeed = 3f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float passiveDrag = 3f;
        [SerializeField] private float turnRateDegrees = 55f;

        [Header("Buoyancy")]
        [Tooltip("Longitudinal separation of the fore and aft buoyancy probes.")]
        [SerializeField] private float probeLength = 6f;
        [SerializeField] private float probeBeam = 2f;
        [Tooltip("How quickly the hull settles into the swell. Low values feel heavy.")]
        [SerializeField] private float trimResponse = 2.4f;

        // ---- Replicated state ------------------------------------------------
        // Speed is replicated (rather than derived on each peer) because the HUD
        // gauge, the wake, and the AI crewmate's dialogue all read it, and they must
        // agree with the server's hull simulation to the metre per second.
        private readonly NetworkVariable<float> _speed = new NetworkVariable<float>(0f);
        private readonly NetworkVariable<float> _heading = new NetworkVariable<float>(0f);

        private readonly NetworkVariable<ulong> _helmOccupant = new NetworkVariable<ulong>(Nobody);
        private readonly NetworkVariable<ulong> _sonarOccupant = new NetworkVariable<ulong>(Nobody);
        private readonly NetworkVariable<ulong> _watchOccupant = new NetworkVariable<ulong>(Nobody);

        // ---- Server-only -------------------------------------------------------
        private HullState _hull;
        private HullTuning _tuning;
        private float _throttleInput;
        private float _rudderInput;

        private BoatStation[] _stations = new BoatStation[0];
        private Transform _wake;
        private Vector3 _wakeBaseScale = Vector3.one;

        public float Speed => _speed.Value;
        public float HeadingDegrees => _heading.Value;
        public IReadOnlyList<BoatStation> Stations => _stations;

        /// <summary>Called by BoatBuilder before the prefab is forged.</summary>
        public void Bind(BoatStation[] stations, Transform wake)
        {
            _stations = stations ?? new BoatStation[0];
            _wake = wake;
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (_wake != null) _wakeBaseScale = _wake.localScale;

            if (IsServer)
            {
                _tuning = new HullTuning
                {
                    MaxForwardSpeed = maxForwardSpeed,
                    MaxReverseSpeed = maxReverseSpeed,
                    Acceleration = acceleration,
                    PassiveDrag = passiveDrag,
                    TurnRateDegrees = turnRateDegrees,
                    FullRudderSpeed = 4f,
                    StationaryRudder = 0.25f
                };
                _hull = new HullState { Speed = 0f, HeadingDegrees = transform.eulerAngles.y };

                _helmOccupant.Value = Nobody;
                _sonarOccupant.Value = Nobody;
                _watchOccupant.Value = Nobody;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------------
        // Station occupancy
        // ---------------------------------------------------------------------

        public ulong OccupantOf(StationRole role)
        {
            switch (role)
            {
                case StationRole.Helm: return _helmOccupant.Value;
                case StationRole.Sonar: return _sonarOccupant.Value;
                default: return _watchOccupant.Value;
            }
        }

        public bool IsOccupied(StationRole role) => OccupantOf(role) != Nobody;

        /// <summary>
        /// Whether `clientId` may take this post. A station held by the AI crewmate is
        /// always claimable by a person: she is there to be useful, not to lock a
        /// solo player out of the station they want.
        /// </summary>
        public bool CanClaim(StationRole role, ulong clientId)
        {
            ulong occupant = OccupantOf(role);
            return occupant == Nobody || occupant == clientId || occupant == CrewmateId;
        }

        /// <summary>The station this client currently holds, if any.</summary>
        public bool TryGetStationOf(ulong clientId, out StationRole role)
        {
            if (_helmOccupant.Value == clientId) { role = StationRole.Helm; return true; }
            if (_sonarOccupant.Value == clientId) { role = StationRole.Sonar; return true; }
            if (_watchOccupant.Value == clientId) { role = StationRole.Watch; return true; }
            role = StationRole.Helm;
            return false;
        }

        public BoatStation StationFor(StationRole role)
        {
            for (int i = 0; i < _stations.Length; i++)
            {
                if (_stations[i] != null && _stations[i].Role == role) return _stations[i];
            }
            return null;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ClaimStationServerRpc(StationRole role, ServerRpcParams parameters = default)
        {
            ulong clientId = parameters.Receive.SenderClientId;
            ClaimStation(role, clientId);
        }

        /// <summary>Server-side claim. Also used directly by the AI crewmate, which has no RPC path.</summary>
        public bool ClaimStation(StationRole role, ulong clientId)
        {
            if (!IsServer) return false;

            // Already someone else's post. The request simply fails; the caller's UI
            // keeps showing "occupied" because that is what the replicated value says.
            // The AI crewmate is the exception — see CanClaim.
            if (!CanClaim(role, clientId)) return false;

            // A crew member holds at most one station, so taking a new one vacates the old.
            if (TryGetStationOf(clientId, out StationRole previous) && previous != role)
            {
                SetOccupant(previous, Nobody);
                if (previous == StationRole.Helm) ZeroHelm();
            }

            SetOccupant(role, clientId);
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ReleaseStationServerRpc(ServerRpcParams parameters = default)
        {
            ReleaseAllFor(parameters.Receive.SenderClientId);
        }

        /// <summary>Vacate whatever this client holds. Called on disconnect too, which is
        /// the case that matters: a helmsman who drops out must not leave the boat locked.</summary>
        public void ReleaseAllFor(ulong clientId)
        {
            if (!IsServer) return;
            if (_helmOccupant.Value == clientId) { _helmOccupant.Value = Nobody; ZeroHelm(); }
            if (_sonarOccupant.Value == clientId) _sonarOccupant.Value = Nobody;
            if (_watchOccupant.Value == clientId) _watchOccupant.Value = Nobody;
        }

        private void SetOccupant(StationRole role, ulong clientId)
        {
            switch (role)
            {
                case StationRole.Helm: _helmOccupant.Value = clientId; break;
                case StationRole.Sonar: _sonarOccupant.Value = clientId; break;
                default: _watchOccupant.Value = clientId; break;
            }
        }

        private void ZeroHelm()
        {
            _throttleInput = 0f;
            _rudderInput = 0f;
        }

        // ---------------------------------------------------------------------
        // Helm input
        // ---------------------------------------------------------------------

        [ServerRpc(RequireOwnership = false)]
        public void SetHelmInputServerRpc(float throttle, float rudder, ServerRpcParams parameters = default)
        {
            // Authority check: only the client actually holding the helm may steer.
            // Without this, any client could drive the boat by sending this RPC.
            if (_helmOccupant.Value != parameters.Receive.SenderClientId) return;
            SetHelmInput(throttle, rudder);
        }

        /// <summary>Server-side helm input. Used by the AI crewmate when it is driving.</summary>
        public void SetHelmInput(float throttle, float rudder)
        {
            if (!IsServer) return;
            _throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            _rudderInput = Mathf.Clamp(rudder, -1f, 1f);
        }

        // ---------------------------------------------------------------------
        // Simulation
        // ---------------------------------------------------------------------

        private void Update()
        {
            if (IsServer) ServerStep(Time.deltaTime);
            UpdateWake();
        }

        private void ServerStep(float dt)
        {
            Vector3 position = transform.position;

            // Hull dynamics live in the portable layer; this is just plumbing.
            Vec3 delta = HullModel.Step(ref _hull, _tuning, _throttleInput, _rudderInput, dt);
            Vec3 next = position.ToSim() + delta;

            HullModel.ConstrainToBasin(ref next, ref _hull, LochBuilder.BasinRadiusX, LochBuilder.BasinRadiusZ, 14f);

            // ---- Sit the hull in the swell -----------------------------------
            // Four probes: fore, aft, port, starboard. Averaging them gives heave;
            // differencing them gives pitch and roll. This is a kinematic
            // approximation, not buoyancy physics, and it is the right trade here —
            // a Rigidbody hull would need its own replication strategy and would
            // fight the crew's local-space transforms.
            float heading = _hull.HeadingDegrees;
            Vector3 forward = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, heading, 0f) * Vector3.right;
            Vector3 centre = new Vector3(next.X, 0f, next.Z);

            float half = probeLength * 0.5f;
            Vector3 fore = centre + forward * half;
            Vector3 aft = centre - forward * half;
            Vector3 port = centre - right * probeBeam;
            Vector3 starboard = centre + right * probeBeam;

            float hFore = WaterSurface.SampleHeight(fore.x, fore.z);
            float hAft = WaterSurface.SampleHeight(aft.x, aft.z);
            float hPort = WaterSurface.SampleHeight(port.x, port.z);
            float hStarboard = WaterSurface.SampleHeight(starboard.x, starboard.z);

            float heave = (hFore + hAft + hPort + hStarboard) * 0.25f;
            float pitch = -Mathf.Atan2(hFore - hAft, probeLength) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(hStarboard - hPort, probeBeam * 2f) * Mathf.Rad2Deg;

            // Under power the bow lifts a little. Small, but it is most of what sells
            // "this boat is working" from the helm.
            pitch -= Mathf.Clamp(_hull.Speed / Mathf.Max(0.01f, maxForwardSpeed), -0.4f, 1f) * 2.2f;

            Vector3 target = new Vector3(next.X, heave, next.Z);
            Quaternion targetRotation = Quaternion.Euler(pitch, heading, roll);

            transform.position = target;
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                                                  1f - Mathf.Exp(-trimResponse * dt));

            // Only write when the value has actually moved. NetworkVariable assignment
            // marks the variable dirty, and these two would otherwise be resent at the
            // full tick rate for the entire hunt, including while tied up and idle.
            if (Mathf.Abs(_speed.Value - _hull.Speed) > 0.02f) _speed.Value = _hull.Speed;
            if (Mathf.Abs(Mathf.DeltaAngle(_heading.Value, heading)) > 0.15f) _heading.Value = heading;
        }

        private void UpdateWake()
        {
            if (_wake == null) return;

            // Runs on every peer from the replicated speed, so remote players see the
            // same wash the helmsman does.
            float t = Mathf.Clamp01(Mathf.Abs(_speed.Value) / Mathf.Max(0.01f, maxForwardSpeed));
            _wake.gameObject.SetActive(t > 0.04f);
            _wake.localScale = new Vector3(
                _wakeBaseScale.x * Mathf.Lerp(0.5f, 1.25f, t),
                _wakeBaseScale.y,
                _wakeBaseScale.z * Mathf.Lerp(0.2f, 1f, t));
        }

        // ---------------------------------------------------------------------
        // Spawning helpers
        // ---------------------------------------------------------------------

        /// <summary>
        /// Where the Nth crew member should appear, in boat-local space. Spread along
        /// the afterdeck so a full crew does not spawn inside one another.
        /// </summary>
        public static Vector3 CrewSpawnLocal(int index)
        {
            float side = (index % 2 == 0) ? -1f : 1f;
            float back = (index / 2) * 1.6f;
            return new Vector3(side * 1.0f, BoatBuilder.DeckY + 0.05f, -0.4f - back);
        }
    }
}
