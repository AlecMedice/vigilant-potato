// ---------------------------------------------------------------------------------------------
//  PlayerController.cs
//  Role : Owner-authoritative boat movement, first-person free-look, and the SONAR tool.
//
//  DESIGN
//  --------------------------------------------------------------------------------------------
//  The brief allowed "first-person or boat-based". We do both: the player drives a small research
//  launch (throttle + rudder, no strafing, no turn-on-the-spot) while the camera free-looks
//  independently of the hull. That is what makes the sonar loop work - you steer one way and
//  sweep the water another.
//
//  AUTHORITY SPLIT (the important part)
//  --------------------------------------------------------------------------------------------
//    Movement  : OWNER authoritative (ClientNetworkTransform) so steering is latency-free.
//                The server runs a plausibility check and logs impossible speeds.
//    Sonar     : SERVER authoritative, always. The client sends "I pinged"; the server decides
//                what came back. A client therefore cannot fabricate a contact, cannot bypass the
//                cooldown, and - crucially - never receives Nessie's position unless the server
//                chose to reveal it. Combined with the distance-based network visibility in
//                NessieAI, this closes the classic "read the monster's NetworkTransform" wallhack.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace LochNess
{
    /// <summary>What a sonar return most likely is. Sent as a byte to keep pings tiny.</summary>
    public enum SonarContactKind : byte
    {
        Unknown = 0,
        Biological = 1,   // Nessie, or a decoy that reads like her.
        FishShoal = 2,
        Wreckage = 3,
        Thermocline = 4   // A temperature layer - pure red herring.
    }

    /// <summary>
    /// One blip on the sonar display.
    /// <para>
    /// Deliberately POLAR and lossy: bearing + range + confidence, never a world position. The
    /// player has to interpret an anomaly rather than read a waypoint, and a packet sniffer learns
    /// no more than the player does.
    /// </para>
    /// </summary>
    public struct SonarContact : INetworkSerializable
    {
        public float Bearing;   // Degrees clockwise from world +Z, 0..360.
        public float Range;     // Metres from the ping origin.
        public float Clarity;   // 0..1 confidence. Drives blip size/alpha on the HUD.
        public byte Kind;       // SonarContactKind, as a byte for wire size.

        public SonarContactKind KindEnum => (SonarContactKind)Kind;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Bearing);
            serializer.SerializeValue(ref Range);
            serializer.SerializeValue(ref Clarity);
            serializer.SerializeValue(ref Kind);
        }
    }

    /// <summary>
    /// SERVER-ONLY sonar resolution, shared by the human player and the AI assistant so both
    /// "hear" the loch through exactly the same model. Keeping it static and stateless makes it
    /// trivially unit-testable and guarantees the assistant can never out-sense a human.
    /// </summary>
    public static class Sonar
    {
        /// <summary>
        /// Resolves a ping into contacts.
        /// </summary>
        /// <param name="origin">World position of the transducer.</param>
        /// <param name="range">Maximum detection range in metres.</param>
        /// <param name="seed">Seed for the decoy generator - keeps a single ping reproducible.</param>
        /// <param name="results">Reused buffer. Cleared on entry.</param>
        /// <param name="nessieClarity">Clarity of the real Nessie return, or 0 if she was missed.</param>
        /// <param name="nessieDistance">Distance to Nessie, or float.PositiveInfinity if not detected.</param>
        public static void Resolve(Vector3 origin, float range, int seed, List<SonarContact> results,
                                   out float nessieClarity, out float nessieDistance)
        {
            results.Clear();
            nessieClarity = 0f;
            nessieDistance = float.PositiveInfinity;

            // Seeded RNG: the same ping always resolves the same way. Uses a local instance rather
            // than UnityEngine.Random so we never disturb global random state used by gameplay.
            var rng = new System.Random(seed);

            // ---- The real target -----------------------------------------------------------
            NessieAI nessie = NessieAI.ServerInstance;
            if (nessie != null && nessie.IsSpawned)
            {
                Vector3 toNessie = nessie.transform.position - origin;
                float distance = toNessie.magnitude;

                if (distance <= range)
                {
                    // Falloff is quadratic-ish so the last third of the range is genuinely murky.
                    float falloff = 1f - Mathf.Clamp01(distance / range);
                    falloff *= falloff;

                    // Nessie's own behaviour changes how loud she is: hiding is quiet, fleeing is loud.
                    float clarity = Mathf.Clamp01(falloff * nessie.SonarSignature);

                    // A little jitter so identical pings are not identical readings.
                    clarity = Mathf.Clamp01(clarity * (0.85f + 0.3f * (float)rng.NextDouble()));

                    if (clarity > 0.05f)
                    {
                        nessieClarity = clarity;
                        nessieDistance = distance;

                        results.Add(new SonarContact
                        {
                            Bearing = BearingOf(toNessie),
                            // Range is reported with error proportional to uncertainty, so a weak
                            // contact is genuinely ambiguous instead of "weak but pixel-perfect".
                            Range = distance * (1f + (float)(rng.NextDouble() - 0.5) * 0.2f * (1f - clarity)),
                            Clarity = clarity,
                            Kind = (byte)SonarContactKind.Biological
                        });
                    }
                }
            }

            // ---- Decoys --------------------------------------------------------------------
            // Without clutter the tool degenerates into a compass. These give the player something
            // to misread, and give Nessie somewhere to hide in the noise.
            int decoyCount = 1 + rng.Next(0, 3);
            for (int i = 0; i < decoyCount; i++)
            {
                float decoyRange = range * (0.15f + 0.8f * (float)rng.NextDouble());
                float decoyFalloff = 1f - Mathf.Clamp01(decoyRange / range);

                SonarContactKind kind = (rng.Next(0, 3)) switch
                {
                    0 => SonarContactKind.FishShoal,
                    1 => SonarContactKind.Wreckage,
                    _ => SonarContactKind.Thermocline
                };

                // Decoys top out below a confirmable reading, so clutter can mislead but can never
                // hand the crew a false win.
                float decoyClarity = Mathf.Clamp01(decoyFalloff * (0.25f + 0.35f * (float)rng.NextDouble()));

                results.Add(new SonarContact
                {
                    Bearing = (float)(rng.NextDouble() * 360.0),
                    Range = decoyRange,
                    Clarity = decoyClarity,
                    Kind = (byte)kind
                });
            }
        }

        /// <summary>World-space compass bearing of a direction, 0..360 degrees clockwise from +Z.</summary>
        public static float BearingOf(Vector3 direction)
        {
            float bearing = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            return bearing < 0f ? bearing + 360f : bearing;
        }
    }

    /// <summary>Player boat: movement, look, and the sonar tool.</summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class PlayerController : NetworkBehaviour
    {
        // -----------------------------------------------------------------------------------------
        //  Local-player hooks for the HUD
        // -----------------------------------------------------------------------------------------

        /// <summary>The boat this application drives. Null until our own player object spawns.</summary>
        public static PlayerController LocalPlayer { get; private set; }

        /// <summary>Raised on the local client when a sonar sweep returns. Hook a HUD to this.</summary>
        public static event Action<SonarContact[]> OnLocalSonarContacts;

        /// <summary>Normalised 0..1 sonar recharge for the local client (1 = ready).</summary>
        public static event Action<float> OnLocalSonarCharge;

        /// <summary>Raised locally with radio chatter from the AI assistant or the server.</summary>
        public static event Action<string> OnLocalRadioMessage;

        // -----------------------------------------------------------------------------------------
        //  Inspector
        // -----------------------------------------------------------------------------------------

        [Header("Scene References")]
        [Tooltip("Yaw/pitch pivot the camera hangs off. Child of the boat root.")]
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener playerAudioListener;
        [Tooltip("Hull mesh root. Bobbed and rolled LOCALLY - never networked.")]
        [SerializeField] private Transform boatVisual;
        [SerializeField] private AudioSource sonarAudioSource;
        [SerializeField] private AudioClip sonarPingClip;

        [Header("Hull")]
        [SerializeField] private float maxForwardSpeed = 9f;
        [SerializeField] private float maxReverseSpeed = 3f;
        [SerializeField] private float acceleration = 6f;
        [SerializeField] private float passiveDrag = 3f;
        [SerializeField] private float turnRateDegrees = 55f;
        [Tooltip("Boats steer with water flow over the rudder. Below this speed the rudder is weak.")]
        [SerializeField] private float fullRudderSpeed = 4f;
        [Tooltip("Fraction of full rudder available at a standstill (bow thruster).")]
        [Range(0f, 1f)][SerializeField] private float stationaryRudder = 0.25f;

        [Header("Water")]
        [SerializeField] private float waterLevel = 0f;
        [Tooltip("How hard the hull is pushed back to the waterline.")]
        [SerializeField] private float buoyancyStiffness = 6f;
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float bobFrequency = 0.7f;
        [SerializeField] private float rollAmplitude = 2.5f;

        [Header("Look")]
        [SerializeField] private float pitchMin = -60f;
        [SerializeField] private float pitchMax = 70f;
        [Tooltip("How far the head can turn off the bow before the hull has to follow.")]
        [SerializeField] private float freeLookYawLimit = 120f;

        [Header("Sonar")]
        [SerializeField] private float sonarRange = 120f;
        [SerializeField] private float sonarCooldown = 3f;
        [Tooltip("Clarity at or above this counts as a CONFIRMED sighting.")]
        [Range(0f, 1f)][SerializeField] private float confirmClarity = 0.65f;
        [Tooltip("Confirmations also require being at least this close, so long-range luck cannot win the hunt.")]
        [SerializeField] private float confirmRange = 60f;
        [Tooltip("How much this ping agitates Nessie. Active sonar is loud - it finds her AND warns her.")]
        [Range(0f, 1f)][SerializeField] private float pingAggression = 0.55f;

        [Header("Anti-cheat (server side)")]
        [Tooltip("Multiple of max speed tolerated before the server logs a warning. Covers legitimate spikes.")]
        [SerializeField] private float speedToleranceFactor = 1.8f;

        // -----------------------------------------------------------------------------------------
        //  Replicated state
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// Owner-write NetworkVariable: the player names themselves, everyone reads it. This is the
        /// one piece of player state where owner authority is correct - the server has no better
        /// source for it, and a bad value is cosmetic only.
        /// </summary>
        private readonly NetworkVariable<FixedString32Bytes> _displayName =
            new NetworkVariable<FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        /// <summary>Server-write: confirmed sightings scored by this player. Cannot be forged.</summary>
        private readonly NetworkVariable<int> _sightings = new NetworkVariable<int>(0);

        public string DisplayName => _displayName.Value.ToString();
        public int Sightings => IsSpawned ? _sightings.Value : 0;

        // -----------------------------------------------------------------------------------------
        //  Runtime
        // -----------------------------------------------------------------------------------------

        private CharacterController _controller;

        // Owner-side movement state.
        private float _currentSpeed;
        private float _lookYaw;
        private float _lookPitch;
        private float _nextLocalPingTime;
        private bool _inputEnabled;

        // Server-side sonar/anti-cheat state (one instance per player object on the host).
        private double _nextServerPingTime;
        private Vector3 _lastServerPosition;
        private bool _hasServerPositionSample;
        private float _nextSpeedWarningTime;

        // Reused so a ping does not allocate a list every time.
        private static readonly List<SonarContact> ServerContactBuffer = new List<SonarContact>(8);
        private readonly ulong[] _singleTargetBuffer = new ulong[1];

        // Legacy "Mouse X" is already frame-normalised; the Input System reports raw pixels, so the
        // new-input path needs scaling to land in the same ballpark.
        private const float NewInputLookScale = 0.05f;

        // -----------------------------------------------------------------------------------------
        //  Lifecycle
        // -----------------------------------------------------------------------------------------

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // Exactly one camera and one AudioListener may be live per client. Every remote boat
            // must have both switched off or Unity spams warnings and audio goes wrong.
            bool owned = IsOwner;

            if (playerCamera != null) playerCamera.enabled = owned;
            if (playerAudioListener != null) playerAudioListener.enabled = owned;

            if (owned)
            {
                LocalPlayer = this;

                // The menu normally loads these at boot, but a build that starts straight into a
                // session must not read defaults - EnsureLoaded is idempotent.
                GameSettings.EnsureLoaded();

                // Owner-write NetworkVariable: safe here precisely because we are the owner.
                _displayName.Value = TruncateForFixedString(GameSettings.PlayerName);

                _lookYaw = 0f;
                _lookPitch = 0f;
                _nextLocalPingTime = 0f;

                if (GameManager.Instance != null)
                {
                    GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
                    ApplyPhase(GameManager.Instance.CurrentPhase);
                }
                else
                {
                    SetInputEnabled(true);
                }
            }

            if (IsServer)
            {
                _lastServerPosition = transform.position;
                _hasServerPositionSample = true;
                _nextServerPingTime = 0d;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner)
            {
                if (GameManager.Instance != null) GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
                SetInputEnabled(false);
                if (LocalPlayer == this) LocalPlayer = null;
            }
        }

        /// <summary>base.OnDestroy() is required by NetworkBehaviour - do not remove it.</summary>
        public override void OnDestroy()
        {
            if (LocalPlayer == this) LocalPlayer = null;
            base.OnDestroy();
        }

        private void HandlePhaseChanged(GamePhase phase) => ApplyPhase(phase);

        private void ApplyPhase(GamePhase phase)
        {
            // Driving is only allowed during the hunt; the results screen hands the cursor back.
            SetInputEnabled(phase == GamePhase.Hunting);
        }

        private void SetInputEnabled(bool enabled)
        {
            if (!IsOwner) return;

            _inputEnabled = enabled;
            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
        }

        // -----------------------------------------------------------------------------------------
        //  Update
        // -----------------------------------------------------------------------------------------

        private void Update()
        {
            if (!IsSpawned) return;

            // Cosmetic hull bob runs on EVERY peer for EVERY boat. It is applied to the visual
            // child, not the networked root, so it costs zero bandwidth and cannot desync anything.
            ApplyLocalHullMotion();

            if (IsOwner) UpdateOwner();
            if (IsServer) UpdateServerValidation();
        }

        private void UpdateOwner()
        {
            float dt = Time.deltaTime;

            // Always publish sonar charge so the HUD keeps ticking even when input is locked.
            float charge = sonarCooldown <= 0f
                ? 1f
                : Mathf.Clamp01(1f - ((_nextLocalPingTime - Time.time) / sonarCooldown));
            OnLocalSonarCharge?.Invoke(charge);

            if (!_inputEnabled)
            {
                // Coast to a stop rather than freezing mid-water when the menu opens. Zero throttle
                // makes ApplyHullMotion decelerate at passiveDrag and keeps buoyancy running.
                ApplyHullMotion(0f, dt);
                return;
            }

            Vector2 move = ReadMove();
            Vector2 look = ReadLook();

            UpdateLook(look, dt);
            ApplyHullMotion(move.x, dt, move.y);

            if (ReadSonarPressed() && Time.time >= _nextLocalPingTime)
            {
                // Predictive local cooldown for responsive UI; the server enforces the real one.
                _nextLocalPingTime = Time.time + sonarCooldown;
                RequestSonarPingServerRpc();
            }
        }

        /// <summary>Free-look: the head turns inside a cone around the bow, independent of steering.</summary>
        private void UpdateLook(Vector2 look, float dt)
        {
            if (cameraPivot == null) return;

            float sensitivity = Mathf.Max(0.01f, GameSettings.MouseSensitivity);
            float invert = GameSettings.InvertLookY ? -1f : 1f;

#if ENABLE_INPUT_SYSTEM
            look *= NewInputLookScale;
#endif

            _lookYaw = Mathf.Clamp(_lookYaw + look.x * sensitivity, -freeLookYawLimit, freeLookYawLimit);
            _lookPitch = Mathf.Clamp(_lookPitch - look.y * sensitivity * invert, pitchMin, pitchMax);

            cameraPivot.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
        }

        /// <summary>Throttle, rudder and buoyancy. Owner-side only; replicated by ClientNetworkTransform.</summary>
        private void ApplyHullMotion(float steer, float dt, float throttle = 0f)
        {
            // Throttle -> target speed, with separate forward and astern limits.
            float targetSpeed = throttle >= 0f ? throttle * maxForwardSpeed : throttle * maxReverseSpeed;
            float rate = Mathf.Approximately(throttle, 0f) ? passiveDrag : acceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * dt);

            // Rudder authority scales with water flowing over it: a drifting boat barely steers.
            float flow = Mathf.Clamp01(Mathf.Abs(_currentSpeed) / Mathf.Max(0.01f, fullRudderSpeed));
            float rudder = Mathf.Lerp(stationaryRudder, 1f, flow);

            // Astern, the rudder works the other way round - the same as a real launch.
            float direction = _currentSpeed < -0.05f ? -1f : 1f;
            float yawDelta = steer * turnRateDegrees * rudder * direction * dt;
            if (!Mathf.Approximately(yawDelta, 0f)) transform.Rotate(0f, yawDelta, 0f, Space.World);

            // Buoyancy: a spring back to the waterline instead of gravity, so the hull floats.
            float verticalError = waterLevel - transform.position.y;
            float verticalSpeed = Mathf.Clamp(verticalError * buoyancyStiffness, -5f, 5f);

            Vector3 velocity = transform.forward * _currentSpeed + Vector3.up * verticalSpeed;
            _controller.Move(velocity * dt);
        }

        /// <summary>Purely cosmetic swell. Runs on all peers, networked to none of them.</summary>
        private void ApplyLocalHullMotion()
        {
            if (boatVisual == null) return;

            // Phase-offset per boat so a fleet does not bob in lockstep.
            float phase = Time.time * bobFrequency * Mathf.PI * 2f + (NetworkObjectId * 0.7f);
            float heave = Mathf.Sin(phase) * bobAmplitude;
            float roll = Mathf.Sin(phase * 0.5f) * rollAmplitude;

            boatVisual.localPosition = new Vector3(0f, heave, 0f);
            boatVisual.localRotation = Quaternion.Euler(0f, 0f, roll);
        }

        /// <summary>
        /// SERVER: sanity-check the owner's replicated movement. Owner authority means we trust the
        /// client for responsiveness, but we still watch. A shipping build would correct or kick;
        /// for the slice we log, which is enough to catch a broken build or an obvious cheat.
        /// </summary>
        private void UpdateServerValidation()
        {
            if (!_hasServerPositionSample)
            {
                _lastServerPosition = transform.position;
                _hasServerPositionSample = true;
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            float observed = Vector3.Distance(transform.position, _lastServerPosition) / dt;
            _lastServerPosition = transform.position;

            float ceiling = Mathf.Max(maxForwardSpeed, maxReverseSpeed) * speedToleranceFactor;

            // Throttle the log: a hitching client would otherwise flood the console.
            if (observed > ceiling && Time.time >= _nextSpeedWarningTime)
            {
                _nextSpeedWarningTime = Time.time + 2f;
                Debug.LogWarning($"[PlayerController] Client {OwnerClientId} moved at {observed:F1} m/s " +
                                 $"(ceiling {ceiling:F1}). Possible packet burst or tampering.");
            }
        }

        // -----------------------------------------------------------------------------------------
        //  SONAR - server authoritative
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// The client asks for a ping. It sends no position and no result - only intent.
        /// </summary>
        [ServerRpc(RequireOwnership = true)]
        private void RequestSonarPingServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;

            // Authoritative cooldown. A modified client can spam this RPC; the server simply
            // ignores the extras, so cheating buys nothing.
            double now = NetworkManager.ServerTime.Time;
            if (now < _nextServerPingTime) return;
            _nextServerPingTime = now + sonarCooldown;

            // A ping is loud. Everyone nearby sees and hears it - including other crews.
            BroadcastPingEffectClientRpc(transform.position);

            SonarContact[] contacts = ResolvePingOnServer(transform.position, sonarRange, senderId);

            // Results go ONLY to the requester. Other clients never receive this data at all.
            // Reusing one array is safe because NGO serialises the target list synchronously
            // inside the generated RPC call - nothing holds a reference past this statement.
            _singleTargetBuffer[0] = senderId;
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = _singleTargetBuffer }
            };
            ReceiveSonarResultClientRpc(contacts, target);
        }

        /// <summary>
        /// SERVER: shared ping resolution used by the player. Also scores confirmations and tells
        /// Nessie she has been swept, which is what closes the detect/evade loop.
        /// </summary>
        internal SonarContact[] ResolvePingOnServer(Vector3 origin, float range, ulong scoringClientId)
        {
            // Only ever reached from a ServerRpc today, but this method writes a server-authority
            // NetworkVariable - an accidental client-side call would throw at runtime, so refuse.
            if (!IsServer) return Array.Empty<SonarContact>();

            int seed = unchecked((int)(NetworkManager.ServerTime.Tick * 73856093) ^ (int)scoringClientId);

            Sonar.Resolve(origin, range, seed, ServerContactBuffer, out float clarity, out float distance);

            NessieAI nessie = NessieAI.ServerInstance;
            if (nessie != null)
            {
                // Active sonar always agitates her, whether or not it resolved a usable contact.
                // Strength scales with how well she was actually illuminated.
                nessie.NotifyPinged(origin, pingAggression * Mathf.Max(0.2f, clarity));
            }

            if (clarity >= confirmClarity && distance <= confirmRange)
            {
                _sightings.Value++; // Server-write NetworkVariable - legal here, we are the server.
                GameManager.Instance?.NotifyConfirmedSighting(scoringClientId);
            }

            return ServerContactBuffer.ToArray();
        }

        /// <summary>Delivers the sweep to the requesting client only.</summary>
        [ClientRpc]
        private void ReceiveSonarResultClientRpc(SonarContact[] contacts, ClientRpcParams rpcParams = default)
        {
            // Never hand a null array to the HUD - an empty sweep is a legitimate result.
            OnLocalSonarContacts?.Invoke(contacts ?? Array.Empty<SonarContact>());
        }

        /// <summary>
        /// Cosmetic ping ring + audio, broadcast to EVERY client so that pinging gives your own
        /// position away to the rest of the loch. <paramref name="origin"/> is the anchor a ping-ring
        /// VFX should be spawned at once art exists.
        /// </summary>
        [ClientRpc]
        private void BroadcastPingEffectClientRpc(Vector3 origin)
        {
            if (sonarAudioSource != null && sonarPingClip != null)
            {
                sonarAudioSource.PlayOneShot(sonarPingClip);
            }
        }

        /// <summary>SERVER: pushes a radio line from the AI assistant to one client.</summary>
        internal void SendRadioMessage(string message)
        {
            if (!IsServer || !IsSpawned) return;

            _singleTargetBuffer[0] = OwnerClientId;
            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = _singleTargetBuffer }
            };
            RadioMessageClientRpc(message, target);
        }

        [ClientRpc]
        private void RadioMessageClientRpc(string message, ClientRpcParams rpcParams = default)
        {
            OnLocalRadioMessage?.Invoke(message);
        }

        // -----------------------------------------------------------------------------------------
        //  Input abstraction
        //  Compiles against whichever input backend the project is configured for, so the slice
        //  runs on a fresh project regardless of the Active Input Handling setting.
        // -----------------------------------------------------------------------------------------

        /// <summary>x = rudder (-1..1), y = throttle (-1..1).</summary>
        private static Vector2 ReadMove()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                    - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            float y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f)
                    - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        private static Vector2 ReadLook()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse == null ? Vector2.zero : mouse.delta.ReadValue();
#else
            return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
        }

        private static bool ReadSonarPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            return (kb != null && kb.spaceKey.wasPressedThisFrame)
                || (mouse != null && mouse.leftButton.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
#endif
        }

        // -----------------------------------------------------------------------------------------
        //  Helpers
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// FixedString32Bytes is bounded in BYTES (29 of UTF-8), not characters, and its
        /// constructor throws on overflow. Clamping by <c>string.Length</c> would still overflow on
        /// any multi-byte name - an accented or CJK nickname would hard-crash the spawn - so we
        /// trim by encoded byte count instead.
        /// </summary>
        private const int FixedString32Capacity = 29;

        private static FixedString32Bytes TruncateForFixedString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) value = "Skipper";
            value = value.Trim();

            while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > FixedString32Capacity)
            {
                // Drop a whole code point at a time. An emoji is a surrogate PAIR, so trimming one
                // char would leave a dangling high surrogate and mangle the final glyph.
                int drop = value.Length >= 2 && char.IsLowSurrogate(value[value.Length - 1])
                                             && char.IsHighSurrogate(value[value.Length - 2])
                    ? 2
                    : 1;

                value = value.Substring(0, value.Length - drop);
            }

            // A name made entirely of oversized glyphs can trim to nothing - never return empty.
            return new FixedString32Bytes(value.Length > 0 ? value : "Skipper");
        }
    }
}
