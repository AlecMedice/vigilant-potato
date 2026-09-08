// -----------------------------------------------------------------------------
// CrewController — a person standing on a moving boat.
//
// This is the script that changed most when the game became "one boat, many
// crew". Each player used to drive their own vessel; now they walk around a
// shared one, and almost every hard problem here comes from the deck moving
// underneath them.
//
// HOW THE MOVING DECK IS HANDLED
// The crew NetworkObject is parented to the boat's NetworkObject on spawn, and
// its NetworkTransform replicates in LOCAL space. So "where I am" means "where I
// am on the deck". A player standing at the sonar console is at a constant local
// position, which is both correct (they do not slide when the boat turns) and
// cheap (a stationary player sends nothing at all).
//
// Movement is clamped analytically by DeckBounds rather than resolved by physics
// — see that file for why a CharacterController is the wrong tool on a deck doing
// nine knots.
//
// INPUT uses the legacy Input class deliberately: the new Input System needs an
// .inputactions asset, and this project ships no authored assets.
// -----------------------------------------------------------------------------

using System;
using System.Text;
using LochNess.Boot;
using LochNess.Core;
using LochNess.Vessel;
using LochNess.World;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.Player
{
    public sealed class CrewController : NetworkBehaviour
    {
        /// <summary>This machine's own crew member, or null before spawn.</summary>
        public static CrewController LocalCrew { get; private set; }

        /// <summary>Fired on the owner when the interaction prompt changes. Empty string clears it.</summary>
        public static event Action<string> OnPrompt;
        /// <summary>Fired on the owner when the held station changes. Null means no station.</summary>
        public static event Action<StationRole?> OnStationChanged;

        [Header("Movement")]
        [SerializeField] private float walkSpeed = 2.6f;
        [SerializeField] private float sprintSpeed = 4.1f;
        [Tooltip("How quickly the walk speed ramps. Low values feel like sea legs.")]
        [SerializeField] private float moveResponse = 12f;

        [Header("View")]
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        // ---- Replicated ------------------------------------------------------
        private readonly NetworkVariable<FixedString32Bytes> _displayName =
            new NetworkVariable<FixedString32Bytes>(default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        // Head pitch is replicated separately because NetworkTransform only carries
        // the root, and the root only yaws. Without this, remote crew all stare
        // dead ahead and you cannot tell who is looking over the side.
        private readonly NetworkVariable<float> _headPitch =
            new NetworkVariable<float>(0f,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Owner);

        // ---- Local -----------------------------------------------------------
        private Transform _body;
        private Transform _head;
        private TextMesh _nameTag;
        private Transform _viewRig;
        private Camera _camera;

        private float _yaw;
        private float _pitch;
        private Vector2 _velocity;
        private float _bobPhase;

        private StationRole? _station;
        private bool _cursorLocked;

        public bool IsAtStation => _station.HasValue;
        public StationRole? Station => _station;
        public string DisplayName => _displayName.Value.ToString();
        public Camera View => _camera;

        /// <summary>Called by CrewBuilder before the prefab is forged.</summary>
        public void Bind(Transform body, Transform head, TextMesh nameTag, Transform viewRig)
        {
            _body = body;
            _head = head;
            _nameTag = nameTag;
            _viewRig = viewRig;
        }

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            TintOilskins();

            _displayName.OnValueChanged += (_, value) => RefreshNameTag(value.ToString());
            RefreshNameTag(_displayName.Value.ToString());

            if (IsOwner)
            {
                LocalCrew = this;

                // The owner writes their own name. FixedString32Bytes is bounded in
                // BYTES, not characters — see Truncate below, which is a crash fix,
                // not a nicety.
                _displayName.Value = Truncate(GameSettings.DisplayName);

                BuildOwnerCamera();
                HideOwnBody();
                SetCursorLocked(true);

                _yaw = transform.localEulerAngles.y;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (LocalCrew == this)
            {
                LocalCrew = null;
                SetCursorLocked(false);
                OnPrompt?.Invoke(string.Empty);
                OnStationChanged?.Invoke(null);
            }
        }

        private void TintOilskins()
        {
            if (_body == null) return;
            Material oilskin = MeshKit.Solid(CrewBuilder.SlickerFor(OwnerClientId), 0.15f);

            foreach (Renderer renderer in _body.GetComponentsInChildren<Renderer>(true))
            {
                // Only the jacket and sleeves; trousers and boots stay dark.
                if (renderer.name == "Torso" || renderer.name.StartsWith("Arm")) renderer.sharedMaterial = oilskin;
            }
        }

        private void BuildOwnerCamera()
        {
            var go = new GameObject("Crew Camera");
            go.transform.SetParent(_viewRig, false);

            _camera = go.AddComponent<Camera>();
            _camera.nearClipPlane = 0.06f;
            _camera.farClipPlane = 900f;
            _camera.fieldOfView = 68f;
            _camera.tag = "MainCamera"; // so Camera.main resolves for the water grid

            go.AddComponent<AudioListener>();
            go.AddComponent<UnderwaterVeil>();
        }

        private void HideOwnBody()
        {
            // Keep the GameObjects alive (they still cast shadows onto the deck,
            // which is a surprisingly strong cue that you have a body) but stop the
            // owner rendering the inside of their own head.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform == _nameTag?.transform) { renderer.enabled = false; continue; }
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
        }

        private void RefreshNameTag(string value)
        {
            if (_nameTag == null) return;
            _nameTag.text = IsOwner ? string.Empty : value;
            _nameTag.color = CrewBuilder.SlickerFor(OwnerClientId);
        }

        // ---------------------------------------------------------------------
        // Frame
        // ---------------------------------------------------------------------

        private void Update()
        {
            if (IsOwner) OwnerUpdate(Time.deltaTime);
            else RemoteUpdate(Time.deltaTime);

            BillboardNameTag();
        }

        private void OwnerUpdate(float dt)
        {
            // Outside the hunt itself — the results screen, most importantly — hand the
            // cursor back and stop reading input entirely. Freeing the cursor from the
            // UI side alone would leave this script still consuming mouse deltas, so
            // the view would swing while the player tried to click a button.
            MatchState match = MatchState.Instance;
            if (match == null || match.Phase != GamePhase.Hunting)
            {
                if (_cursorLocked) SetCursorLocked(false);
                OnPrompt?.Invoke(string.Empty);
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape)) SetCursorLocked(!_cursorLocked);

            SyncStationFromServer();
            UpdateLook(dt);

            if (_station.HasValue) UpdateAtStation(dt);
            else UpdateWalking(dt);

            HandleInteraction();
        }

        private void RemoteUpdate(float dt)
        {
            // Smooth the replicated pitch: NetworkVariable updates arrive at the tick
            // rate, which is coarser than the frame rate, and an unsmoothed head snaps.
            if (_head != null)
            {
                float current = _head.localEulerAngles.x;
                if (current > 180f) current -= 360f;
                float next = Mathf.LerpAngle(current, _headPitch.Value, 1f - Mathf.Exp(-14f * dt));
                _head.localRotation = Quaternion.Euler(next, 0f, 0f);
            }
        }

        private void UpdateLook(float dt)
        {
            if (!_cursorLocked) return;

            float sensitivity = GameSettings.MouseSensitivity;
            float mx = Input.GetAxisRaw("Mouse X") * sensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * sensitivity * (GameSettings.InvertY ? 1f : -1f);

            _yaw += mx;
            _pitch = Mathf.Clamp(_pitch + my, minPitch, maxPitch);

            // Yaw is applied to the root IN BOAT-LOCAL SPACE, so a player facing the
            // bow keeps facing the bow as the boat turns — which is what standing on
            // a boat actually feels like.
            transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_viewRig != null) _viewRig.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(_pitch * 0.7f, 0f, 0f);

            // Guarded: assigning a NetworkVariable dirties it, and an unguarded write
            // here would send a head angle every frame the mouse is touched at all.
            float pitchToSend = _pitch * 0.7f;
            if (Mathf.Abs(_headPitch.Value - pitchToSend) > 0.4f) _headPitch.Value = pitchToSend;
        }

        private void UpdateWalking(float dt)
        {
            float forward = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float strafe = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            bool sprinting = Input.GetKey(KeyCode.LeftShift);

            var wish = new Vector2(strafe, forward);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= sprinting ? sprintSpeed : walkSpeed;

            _velocity = Vector2.Lerp(_velocity, wish, 1f - Mathf.Exp(-moveResponse * dt));

            Vector3 step = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(_velocity.x, 0f, _velocity.y) * dt;
            transform.localPosition = DeckBounds.Clamp(transform.localPosition + step);

            UpdateHeadBob(dt, _velocity.magnitude);
        }

        private void UpdateHeadBob(float dt, float speed)
        {
            if (_viewRig == null) return;

            // Two cues in one: a walking bob, plus a permanent slow sway so that
            // standing still on the water never feels like standing still on land.
            _bobPhase += dt * (2.6f + speed * 1.9f);
            float walkBob = Mathf.Sin(_bobPhase * 2f) * 0.014f * Mathf.Clamp01(speed / walkSpeed);
            float sway = Mathf.Sin(Time.time * 0.7f) * 0.012f;

            _viewRig.localPosition = new Vector3(0f, CrewBuilder.EyeHeight + walkBob + sway, 0.06f);
        }

        // ---------------------------------------------------------------------
        // Stations
        // ---------------------------------------------------------------------

        private void SyncStationFromServer()
        {
            var boat = BoatController.Instance;
            if (boat == null) { SetStation(null); return; }

            // The server's replicated occupancy is the truth, always. If the helm was
            // taken from us — say we disconnected and reconnected — this is where we
            // find out, rather than by continuing to send helm input into the void.
            if (boat.TryGetStationOf(OwnerClientId, out StationRole role)) SetStation(role);
            else SetStation(null);
        }

        private void SetStation(StationRole? role)
        {
            if (_station.Equals(role)) return;
            _station = role;
            OnStationChanged?.Invoke(role);
        }

        private void UpdateAtStation(float dt)
        {
            var boat = BoatController.Instance;
            if (boat == null) return;

            BoatStation station = boat.StationFor(_station.Value);
            if (station != null)
            {
                // Slide to the post rather than snapping, so taking the helm reads as
                // stepping up to it.
                transform.localPosition = Vector3.Lerp(transform.localPosition,
                    station.LocalStandPosition, 1f - Mathf.Exp(-9f * dt));
            }

            switch (_station.Value)
            {
                case StationRole.Helm:
                    DriveBoat(boat);
                    break;

                case StationRole.Sonar:
                    if (Input.GetKeyDown(KeyCode.Space)) SonarSet.Instance?.PingServerRpc();
                    break;

                case StationRole.Watch:
                    if (Input.GetKeyDown(KeyCode.Space)) SonarSet.Instance?.ReportVisualServerRpc();
                    break;
            }
        }

        private float _lastThrottle = float.NaN;
        private float _lastRudder = float.NaN;

        private void DriveBoat(BoatController boat)
        {
            float throttle = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float rudder = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

            // Send only on change. Helm input is held for seconds at a time, so an
            // unconditional per-frame RPC would be almost entirely redundant traffic.
            if (Mathf.Approximately(throttle, _lastThrottle) && Mathf.Approximately(rudder, _lastRudder)) return;

            _lastThrottle = throttle;
            _lastRudder = rudder;
            boat.SetHelmInputServerRpc(throttle, rudder);
        }

        private void HandleInteraction()
        {
            var boat = BoatController.Instance;
            if (boat == null) return;

            if (_station.HasValue)
            {
                OnPrompt?.Invoke(PromptForHeldStation());
                if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Q))
                {
                    ClearHelmCache();
                    boat.ReleaseStationServerRpc();
                }
                return;
            }

            BoatStation nearest = FindStationInReach(boat);
            if (nearest == null) { OnPrompt?.Invoke(string.Empty); return; }

            // CanClaim rather than IsOccupied: a post held by the AI crewmate is
            // available to a person, and the prompt must agree with what the server
            // will actually allow.
            if (!boat.CanClaim(nearest.Role, OwnerClientId))
            {
                OnPrompt?.Invoke($"{nearest.Label} — occupied");
                return;
            }

            OnPrompt?.Invoke($"[E] {nearest.Verb}");
            if (Input.GetKeyDown(KeyCode.E)) boat.ClaimStationServerRpc(nearest.Role);
        }

        private string PromptForHeldStation()
        {
            switch (_station.Value)
            {
                case StationRole.Helm: return "W/S throttle   A/D rudder   [E] stand down";
                case StationRole.Sonar: return "[Space] ping   [E] stand down";
                default: return "[Space] log a sighting   [E] stand down";
            }
        }

        private void ClearHelmCache()
        {
            // Zero the helm on the way out, or the boat keeps the last order forever.
            if (_station == StationRole.Helm)
            {
                BoatController.Instance?.SetHelmInputServerRpc(0f, 0f);
            }
            _lastThrottle = float.NaN;
            _lastRudder = float.NaN;
        }

        private BoatStation FindStationInReach(BoatController boat)
        {
            BoatStation best = null;
            float bestDistance = float.MaxValue;
            Vector3 here = transform.localPosition;

            var stations = boat.Stations;
            for (int i = 0; i < stations.Count; i++)
            {
                BoatStation station = stations[i];
                if (station == null) continue;

                float d = Vector3.Distance(here, station.LocalStandPosition);
                if (d <= station.InteractRadius && d < bestDistance)
                {
                    bestDistance = d;
                    best = station;
                }
            }
            return best;
        }

        // ---------------------------------------------------------------------
        // Odds and ends
        // ---------------------------------------------------------------------

        private void BillboardNameTag()
        {
            if (_nameTag == null || IsOwner) return;

            Camera view = LocalCrew != null ? LocalCrew.View : Camera.main;
            if (view == null) return;

            // Face the viewer, but stay upright — a tag that rolls with the boat is
            // unreadable in a swell.
            Vector3 toViewer = _nameTag.transform.position - view.transform.position;
            toViewer.y = 0f;
            if (toViewer.sqrMagnitude > 0.0001f) _nameTag.transform.rotation = Quaternion.LookRotation(toViewer, Vector3.up);
        }

        private void SetCursorLocked(bool locked)
        {
            _cursorLocked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// Cut a name down until it fits FixedString32Bytes.
        ///
        /// The capacity is 29 BYTES, not 29 characters. Truncating by character count
        /// throws at spawn for any name with an accent, a CJK glyph or an emoji, and
        /// the throw happens inside NGO's serialiser where the cause is not obvious.
        /// Whole code points are dropped at a time so a surrogate pair is never split.
        /// </summary>
        private const int FixedString32Capacity = 29;

        private static FixedString32Bytes Truncate(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) value = "Skipper";
            value = value.Trim();

            while (value.Length > 0 && Encoding.UTF8.GetByteCount(value) > FixedString32Capacity)
            {
                int drop = value.Length >= 2
                           && char.IsLowSurrogate(value[value.Length - 1])
                           && char.IsHighSurrogate(value[value.Length - 2]) ? 2 : 1;
                value = value.Substring(0, value.Length - drop);
            }

            return new FixedString32Bytes(value.Length > 0 ? value : "Skipper");
        }
    }
}
