// ---------------------------------------------------------------------------------------------
//  GameManager.cs
//  Project : Loch Ness - Vertical Slice
//  Role    : Session lifecycle, network bootstrap, match state, and authoritative AI spawning.
//
//  ARCHITECTURE NOTE - "One code path" principle
//  --------------------------------------------------------------------------------------------
//  Single-player is NOT a separate offline mode. It is a Netcode *host* bound to loopback
//  (127.0.0.1) that rejects every remote connection. This means:
//      * PlayerController, NessieAI and AssistantAI never branch on "am I offline?".
//      * Server-authority code is exercised constantly during solo playtests, so authority bugs
//        surface in single-player instead of ambushing us on the first multiplayer build.
//      * The UI transition between Single Player and Multiplayer is a one-line difference.
//  The cost is a loopback socket in solo play, which is invisible to the player and is bound to
//  127.0.0.1 so it is never reachable from the LAN.
//
//  PLACEMENT
//  --------------------------------------------------------------------------------------------
//  This component lives on an in-scene-placed NetworkObject inside the persistent "Bootstrap"
//  scene (build index 0). Bootstrap is never unloaded; the gameplay scene ("Loch") is loaded
//  ADDITIVELY through NetworkManager.SceneManager so that this manager - and the NetworkManager
//  itself - survive for the whole application lifetime.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LochNess
{
    /// <summary>How the local application joined the current session.</summary>
    public enum SessionMode : byte
    {
        None = 0,
        SinglePlayer = 1,
        Host = 2,
        Client = 3
    }

    /// <summary>
    /// Coarse match state. Replicated so late joiners immediately know what is happening.
    /// Explicit <c>byte</c> backing keeps the NetworkVariable payload at one byte.
    /// </summary>
    public enum GamePhase : byte
    {
        Menu = 0,      // No session. Title screen is visible.
        Loading = 1,   // Session up, gameplay scene streaming in.
        Hunting = 2,   // Core loop running.
        Results = 3    // Hunt over (timer expired or sighting quota met).
    }

    /// <summary>
    /// Central authority for session lifecycle and match state.
    /// <para>
    /// IMPORTANT: this is a <see cref="NetworkBehaviour"/> that must be usable *before* the network
    /// exists (the title screen calls <see cref="StartHost"/> on it). Every member is therefore
    /// split into two categories:
    /// </para>
    /// <list type="bullet">
    ///   <item>Session API (<see cref="StartSinglePlayer"/>, <see cref="StartHost"/>,
    ///         <see cref="StartClient"/>, <see cref="LeaveSession"/>) - safe to call at any time.</item>
    ///   <item>Replicated state (NetworkVariables) - only valid while <see cref="NetworkBehaviour.IsSpawned"/>
    ///         is true. Every accessor below guards on that.</item>
    /// </list>
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [DisallowMultipleComponent]
    public class GameManager : NetworkBehaviour
    {
        // -----------------------------------------------------------------------------------------
        //  Singleton
        // -----------------------------------------------------------------------------------------

        /// <summary>The one GameManager, available from the moment Bootstrap loads.</summary>
        public static GameManager Instance { get; private set; }

        // -----------------------------------------------------------------------------------------
        //  Inspector configuration
        // -----------------------------------------------------------------------------------------

        [Header("Scenes")]
        [Tooltip("Name of the gameplay scene, loaded ADDITIVELY on top of Bootstrap. Must be in Build Settings.")]
        [SerializeField] private string gameplaySceneName = "Loch";

        [Header("Networked Prefabs (must also be registered in NetworkManager > Network Prefabs)")]
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private NetworkObject nessiePrefab;
        [Tooltip("AI companion boat. Spawned in single-player, or in host mode when 'spawnAssistantInMultiplayer' is set.")]
        [SerializeField] private NetworkObject assistantPrefab;
        [SerializeField] private bool spawnAssistantInMultiplayer = false;

        [Header("Session")]
        [SerializeField] private ushort defaultPort = 7777;
        [Tooltip("Includes the host. Connection approval rejects anyone over this count.")]
        [SerializeField] private int maxPlayers = 4;
        [Tooltip("Clients whose build version string differs are rejected with a readable reason.")]
        [SerializeField] private string buildVersion = "0.1.0-vertical-slice";
        [Tooltip("Seconds before a client that never finishes connecting gives up and returns to the menu.")]
        [SerializeField] private float clientConnectTimeout = 10f;

        [Header("Match Rules")]
        [SerializeField] private float huntDurationSeconds = 480f;
        [Tooltip("Confirmed sonar contacts required (across the whole crew) to win the hunt.")]
        [SerializeField] private int targetSightings = 3;

        [Header("Spawn Volumes (world space, inside the gameplay scene)")]
        [Tooltip("Boats spawn on a ring around this point, on the water surface.")]
        [SerializeField] private Vector3 playerSpawnCenter = new Vector3(0f, 0f, 0f);
        [SerializeField] private float playerSpawnRadius = 10f;
        [Tooltip("Nessie spawns at a random point in this disc; she snaps herself to the lakebed NavMesh.")]
        [SerializeField] private Vector3 nessieSpawnCenter = new Vector3(0f, -10f, 60f);
        [SerializeField] private float nessieSpawnRadius = 40f;

        // -----------------------------------------------------------------------------------------
        //  Replicated match state
        //  Write permission is Server (the NGO default) for all of these. Nothing below is ever
        //  written from a client - every setter is behind an IsServer guard.
        // -----------------------------------------------------------------------------------------

        private readonly NetworkVariable<GamePhase> _phase =
            new NetworkVariable<GamePhase>(GamePhase.Menu);

        /// <summary>
        /// Absolute server-clock timestamp at which the current phase ends. We replicate a deadline
        /// instead of a ticking float: one write per phase change rather than one per tick, and
        /// clients derive a smooth countdown from <see cref="NetworkManager.ServerTime"/>, which NGO
        /// already keeps synchronised.
        /// </summary>
        private readonly NetworkVariable<double> _phaseEndServerTime =
            new NetworkVariable<double>(0d);

        private readonly NetworkVariable<int> _confirmedSightings =
            new NetworkVariable<int>(0);

        /// <summary>True when the hunt ended because the crew hit the sighting quota.</summary>
        private readonly NetworkVariable<bool> _huntSucceeded =
            new NetworkVariable<bool>(false);

        // -----------------------------------------------------------------------------------------
        //  Public read-only state (safe before spawn - never throws)
        // -----------------------------------------------------------------------------------------

        /// <summary>Local mirror of <see cref="_phase"/>, forced to Menu whenever there is no session.</summary>
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Menu;

        /// <summary>How this application entered the current session.</summary>
        public SessionMode CurrentMode { get; private set; } = SessionMode.None;

        public bool IsSinglePlayer => CurrentMode == SessionMode.SinglePlayer;
        public bool IsInSession => CurrentMode != SessionMode.None;

        public int ConfirmedSightings => IsSpawned ? _confirmedSightings.Value : 0;
        public int TargetSightings => targetSightings;
        public bool HuntSucceeded => IsSpawned && _huntSucceeded.Value;

        /// <summary>Seconds left in the hunt, derived from the synchronised server clock. 0 when idle.</summary>
        public float RemainingSeconds
        {
            get
            {
                if (!IsSpawned || CurrentPhase != GamePhase.Hunting || Net == null) return 0f;
                return Mathf.Max(0f, (float)(_phaseEndServerTime.Value - Net.ServerTime.Time));
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Events consumed by the UI layer
        // -----------------------------------------------------------------------------------------

        /// <summary>Raised on every peer when the match phase changes (including the local reset to Menu).</summary>
        public event Action<GamePhase> OnPhaseChanged;

        /// <summary>Raised when the local session mode changes (None -> Host, Client -> None, ...).</summary>
        public event Action<SessionMode> OnSessionModeChanged;

        /// <summary>Human-readable progress/error text for the title screen ("Connecting...", "Host refused: ...").</summary>
        public event Action<string> OnStatusMessage;

        /// <summary>Raised after teardown completes. The string is a reason, or null for a clean exit.</summary>
        public event Action<string> OnSessionEnded;

        /// <summary>Raised on all peers when the crew's confirmed-sighting count changes.</summary>
        public event Action<int, int> OnSightingsChanged; // (current, target)

        // -----------------------------------------------------------------------------------------
        //  Private runtime state
        // -----------------------------------------------------------------------------------------

        private NetworkManager _net;
        private NetworkObject _nessieInstance;
        private NetworkObject _assistantInstance;
        private Coroutine _connectTimeoutRoutine;
        private bool _isTearingDown;
        private bool _callbacksBound;
        private bool _sceneCallbacksBound;
        private bool _gameplaySceneReady;

        /// <summary>
        /// Lazily resolved NetworkManager. We do NOT use <see cref="NetworkBehaviour.NetworkManager"/>
        /// because that base property is only valid once the object is spawned, and the title screen
        /// needs to reach the NetworkManager long before that.
        /// </summary>
        private NetworkManager Net
        {
            get
            {
                if (_net == null) _net = NetworkManager.Singleton;
                return _net;
            }
        }

        // -----------------------------------------------------------------------------------------
        //  Unity lifecycle
        // -----------------------------------------------------------------------------------------

        private void Awake()
        {
            // Bootstrap is never unloaded, so a duplicate here means a second Bootstrap was loaded
            // by mistake. Destroy the newcomer rather than silently running two managers.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[GameManager] Duplicate instance destroyed.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            // Subscribed in Start (not Awake) so NetworkManager.Singleton is guaranteed to have been
            // assigned in its own Awake, regardless of script execution order inside Bootstrap.
            BindNetworkCallbacks();
        }

        private void Update()
        {
            // Only the server drives the match clock; clients render RemainingSeconds off ServerTime.
            if (!IsSpawned || !IsServer) return;
            if (CurrentPhase != GamePhase.Hunting) return;

            if (Net.ServerTime.Time >= _phaseEndServerTime.Value)
            {
                EndHunt(false);
            }
        }

        /// <summary>
        /// NetworkBehaviour.OnDestroy performs required internal cleanup. Forgetting
        /// <c>base.OnDestroy()</c> here is one of the most common NGO memory-leak bugs.
        /// </summary>
        public override void OnDestroy()
        {
            UnbindNetworkCallbacks();
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // -----------------------------------------------------------------------------------------
        //  NetworkBehaviour lifecycle
        // -----------------------------------------------------------------------------------------

        public override void OnNetworkSpawn()
        {
            // This object is scene-placed, so it is NOT destroyed between sessions. NetworkVariables
            // therefore retain last session's values. The server must explicitly reset them here,
            // otherwise session #2 starts on session #1's scoreboard.
            if (IsServer)
            {
                _phase.Value = GamePhase.Loading;
                _phaseEndServerTime.Value = 0d;
                _confirmedSightings.Value = 0;
                _huntSucceeded.Value = false;
            }

            _phase.OnValueChanged += HandlePhaseReplicated;
            _confirmedSightings.OnValueChanged += HandleSightingsReplicated;

            // Apply the value we already hold. On a client this runs after NGO has delivered the
            // initial NetworkVariable snapshot, so a late joiner lands in the correct phase.
            SetLocalPhase(_phase.Value);
            OnSightingsChanged?.Invoke(_confirmedSightings.Value, targetSightings);
        }

        public override void OnNetworkDespawn()
        {
            _phase.OnValueChanged -= HandlePhaseReplicated;
            _confirmedSightings.OnValueChanged -= HandleSightingsReplicated;

            // Once despawned there is no replicated truth left; fall back to the menu locally.
            SetLocalPhase(GamePhase.Menu);
        }

        private void HandlePhaseReplicated(GamePhase previous, GamePhase current) => SetLocalPhase(current);

        private void HandleSightingsReplicated(int previous, int current)
            => OnSightingsChanged?.Invoke(current, targetSightings);

        private void SetLocalPhase(GamePhase phase)
        {
            if (CurrentPhase == phase) return;
            CurrentPhase = phase;
            OnPhaseChanged?.Invoke(phase);
        }

        private void SetMode(SessionMode mode)
        {
            if (CurrentMode == mode) return;
            CurrentMode = mode;
            OnSessionModeChanged?.Invoke(mode);
        }

        // =========================================================================================
        //  SESSION API - callable from the title screen at any time
        // =========================================================================================

        /// <summary>
        /// Starts a solo hunt. Implemented as a host bound to loopback with remote connections
        /// refused, so all gameplay code runs through the identical server-authoritative path.
        /// </summary>
        public bool StartSinglePlayer()
        {
            if (!PrepareForStart(SessionMode.SinglePlayer)) return false;

            // Bind AND advertise on loopback only: the OS will not expose this socket to the LAN.
            ConfigureTransport("127.0.0.1", defaultPort, "127.0.0.1");

            if (!Net.StartHost())
            {
                FailStart("Could not start the local session.");
                return false;
            }

            Report("Starting solo hunt...");
            return true;
        }

        /// <summary>Hosts a listen-server session on all local interfaces.</summary>
        public bool StartHost(ushort port = 0)
        {
            if (!PrepareForStart(SessionMode.Host)) return false;

            // Listen on 0.0.0.0 so LAN clients can reach us; the first argument is only used when
            // this same NetworkManager acts as a client, so its value is irrelevant for a host.
            ConfigureTransport("127.0.0.1", port == 0 ? defaultPort : port, "0.0.0.0");

            if (!Net.StartHost())
            {
                FailStart("Could not open the host socket. Is the port already in use?");
                return false;
            }

            Report($"Hosting on port {(port == 0 ? defaultPort : port)}...");
            return true;
        }

        /// <summary>Joins a remote host by address.</summary>
        public bool StartClient(string address, ushort port = 0)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                Report("Enter a host address first.");
                return false;
            }

            if (!PrepareForStart(SessionMode.Client)) return false;

            ConfigureTransport(address.Trim(), port == 0 ? defaultPort : port, null);

            if (!Net.StartClient())
            {
                FailStart("Could not start the client.");
                return false;
            }

            // Bind scene callbacks NOW, not from OnClientConnectedCallback. NGO fires that callback
            // only AFTER synchronisation has already loaded the server's scenes, so binding there
            // misses the very OnLoadComplete we need in order to focus the gameplay scene.
            // SceneManager exists as soon as StartClient has initialised the NetworkManager.
            BindSceneCallbacks();

            // NGO surfaces a failed handshake as a disconnect, but a host that is simply unreachable
            // produces no callback at all - hence an explicit timeout so the UI can never hang.
            _connectTimeoutRoutine = StartCoroutine(ClientConnectTimeout());
            Report($"Connecting to {address.Trim()}...");
            return true;
        }

        /// <summary>Tears the session down and returns to the title screen. Safe to call twice.</summary>
        public void LeaveSession(string reason = null)
        {
            if (_isTearingDown) return;
            if (Net == null) return;
            if (!Net.IsListening && !Net.IsClient && CurrentMode == SessionMode.None) return;

            _isTearingDown = true;
            StartCoroutine(ShutdownRoutine(reason));
        }

        /// <summary>Quits the application (or exits play mode in the editor).</summary>
        public void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // -----------------------------------------------------------------------------------------
        //  Session start helpers
        // -----------------------------------------------------------------------------------------

        private bool PrepareForStart(SessionMode mode)
        {
            if (Net == null)
            {
                Report("NetworkManager is missing from the Bootstrap scene.");
                return false;
            }

            if (Net.IsListening || Net.IsClient || _isTearingDown)
            {
                Report("A session is already running.");
                return false;
            }

            BindNetworkCallbacks();

            _gameplaySceneReady = false;
            _sceneCallbacksBound = false;   // Previous session's subscriptions are gone with its SceneManager.
            SetMode(mode);

            // Connection approval is mandatory for us: it enforces the player cap, the build-version
            // check, and (critically) suppresses NGO's automatic player-object spawn so that we can
            // spawn boats only after the gameplay scene exists on the target client.
            Net.NetworkConfig.ConnectionApproval = true;
            Net.NetworkConfig.EnableSceneManagement = true;
            Net.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(buildVersion);

            return true;
        }

        private void FailStart(string message)
        {
            SetMode(SessionMode.None);
            Report(message);
            OnSessionEnded?.Invoke(message);
        }

        private void ConfigureTransport(string address, ushort port, string listenAddress)
        {
            var transport = Net.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[GameManager] UnityTransport component not found on the NetworkManager.", this);
                return;
            }

            if (string.IsNullOrEmpty(listenAddress)) transport.SetConnectionData(address, port);
            else transport.SetConnectionData(address, port, listenAddress);
        }

        private IEnumerator ClientConnectTimeout()
        {
            float deadline = Time.realtimeSinceStartup + clientConnectTimeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (Net == null) yield break;
                if (Net.IsConnectedClient) { _connectTimeoutRoutine = null; yield break; }
                yield return null;
            }

            _connectTimeoutRoutine = null;
            LeaveSession("Could not reach the host (timed out).");
        }

        // =========================================================================================
        //  NETWORK CALLBACKS
        // =========================================================================================

        private void BindNetworkCallbacks()
        {
            if (_callbacksBound || Net == null) return;

            Net.ConnectionApprovalCallback += ApproveConnection;
            Net.OnServerStarted += HandleServerStarted;
            Net.OnClientConnectedCallback += HandleClientConnected;
            Net.OnClientDisconnectCallback += HandleClientDisconnected;
            Net.OnTransportFailure += HandleTransportFailure;
            Net.OnServerStopped += HandleServerStopped;
            Net.OnClientStopped += HandleClientStopped;

            _callbacksBound = true;
        }

        private void UnbindNetworkCallbacks()
        {
            if (!_callbacksBound || _net == null) return;

            _net.ConnectionApprovalCallback -= ApproveConnection;
            _net.OnServerStarted -= HandleServerStarted;
            _net.OnClientConnectedCallback -= HandleClientConnected;
            _net.OnClientDisconnectCallback -= HandleClientDisconnected;
            _net.OnTransportFailure -= HandleTransportFailure;
            _net.OnServerStopped -= HandleServerStopped;
            _net.OnClientStopped -= HandleClientStopped;

            UnbindSceneCallbacks();
            _callbacksBound = false;
        }

        /// <summary>
        /// Idempotent. Three call sites race to get here first depending on host/client and on how
        /// quickly the handshake completes, so the flag - not the caller - guarantees single
        /// subscription. Without it a host subscribed twice (server start + local connect) and every
        /// scene event fired its handler twice.
        /// </summary>
        private void BindSceneCallbacks()
        {
            if (_sceneCallbacksBound) return;

            // NetworkManager.SceneManager only exists once the session is running.
            if (Net?.SceneManager == null) return;

            Net.SceneManager.OnLoadComplete += HandleSceneLoadCompleteLocal;
            Net.SceneManager.OnLoadEventCompleted += HandleSceneLoadEventCompleted;
            Net.SceneManager.OnSynchronizeComplete += HandleClientSynchronizeComplete;

            _sceneCallbacksBound = true;
        }

        private void UnbindSceneCallbacks()
        {
            if (!_sceneCallbacksBound) return;
            _sceneCallbacksBound = false;

            // Must run BEFORE NetworkManager.Shutdown(), which disposes SceneManager.
            if (_net?.SceneManager == null) return;

            _net.SceneManager.OnLoadComplete -= HandleSceneLoadCompleteLocal;
            _net.SceneManager.OnLoadEventCompleted -= HandleSceneLoadEventCompleted;
            _net.SceneManager.OnSynchronizeComplete -= HandleClientSynchronizeComplete;
        }

        /// <summary>
        /// Runs on the server for every incoming connection - including the host's own local client.
        /// </summary>
        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                                       NetworkManager.ConnectionApprovalResponse response)
        {
            // We spawn player boats ourselves, after the gameplay scene is confirmed loaded on the
            // target client. Letting NGO auto-create them here would drop boats into an empty scene.
            response.CreatePlayerObject = false;
            response.Pending = false;

            bool isLocalHostClient = request.ClientNetworkId == Net.LocalClientId;

            // The host always gets in, otherwise it would refuse to start its own session.
            if (isLocalHostClient)
            {
                response.Approved = true;
                return;
            }

            if (CurrentMode == SessionMode.SinglePlayer)
            {
                response.Approved = false;
                response.Reason = "This is a single-player session.";
                return;
            }

            if (Net.ConnectedClientsIds.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = $"Session is full ({maxPlayers}/{maxPlayers}).";
                return;
            }

            // Version gate. Mismatched builds serialise NetworkVariables differently and produce
            // baffling desyncs, so reject them at the door with a message the player can act on.
            string clientVersion = request.Payload != null && request.Payload.Length > 0
                ? Encoding.UTF8.GetString(request.Payload)
                : "<unknown>";

            if (!string.Equals(clientVersion, buildVersion, StringComparison.Ordinal))
            {
                response.Approved = false;
                response.Reason = $"Version mismatch. Host is {buildVersion}, you are {clientVersion}.";
                return;
            }

            response.Approved = true;
        }

        private void HandleServerStarted()
        {
            BindSceneCallbacks();

            // Stream the gameplay scene in ADDITIVELY so Bootstrap (NetworkManager + this manager
            // + the title canvas) stays resident. NGO replicates this load to every client and to
            // every client that joins later.
            var status = Net.SceneManager.LoadScene(gameplaySceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError($"[GameManager] Scene load failed: {status}", this);
                LeaveSession($"Could not load '{gameplaySceneName}' ({status}).");
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == Net.LocalClientId)
            {
                // Our own handshake succeeded - cancel the connect watchdog.
                if (_connectTimeoutRoutine != null)
                {
                    StopCoroutine(_connectTimeoutRoutine);
                    _connectTimeoutRoutine = null;
                }

                // Belt and braces: if synchronisation loaded the gameplay scene before our
                // callbacks were live, focus it here instead of waiting for a load that already ran.
                BindSceneCallbacks();
                TryFocusGameplayScene();

                Report(CurrentMode == SessionMode.Client ? "Connected." : "Session ready.");
            }
            else
            {
                Debug.Log($"[GameManager] Client {clientId} connected.");
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            // On a client, our own id disconnecting means the host dropped us (or went away).
            if (!Net.IsServer && clientId == Net.LocalClientId)
            {
                string reason = string.IsNullOrEmpty(Net.DisconnectReason)
                    ? "Disconnected from the host."
                    : Net.DisconnectReason;
                LeaveSession(reason);
                return;
            }

            if (!Net.IsServer) return;

            Debug.Log($"[GameManager] Client {clientId} disconnected.");

            // NGO despawns that client's player object automatically. In a fuller build this is
            // where a reconnect grace window or bot backfill would live.
        }

        private void HandleTransportFailure()
        {
            // Raised when the transport itself dies (socket error, cable pulled, adapter reset).
            LeaveSession("Network transport failure.");
        }

        private void HandleServerStopped(bool wasHost) => HandleLocalNetworkStopped();

        private void HandleClientStopped(bool wasHost) => HandleLocalNetworkStopped();

        /// <summary>
        /// Fires when NGO stops for any reason. If we did not initiate it (crash, kick, host quit)
        /// we still need to run our own teardown so the UI returns to the menu.
        /// </summary>
        private void HandleLocalNetworkStopped()
        {
            if (_isTearingDown) return;                 // Our own ShutdownRoutine is already running.
            if (CurrentMode == SessionMode.None) return; // Already idle.
            LeaveSession(string.IsNullOrEmpty(Net?.DisconnectReason) ? null : Net.DisconnectReason);
        }

        // =========================================================================================
        //  SCENE + SPAWN FLOW
        // =========================================================================================

        /// <summary>Per-peer scene load notification. Used locally to focus the gameplay scene.</summary>
        private void HandleSceneLoadCompleteLocal(ulong clientId, string sceneName, LoadSceneMode mode)
        {
            if (clientId != Net.LocalClientId || sceneName != gameplaySceneName) return;
            TryFocusGameplayScene();
        }

        /// <summary>
        /// Makes the additively loaded gameplay scene the ACTIVE scene, so runtime instantiation,
        /// lighting and skybox resolve against the loch rather than against Bootstrap. Idempotent,
        /// so it is safe to call from both the load callback and the connection callback.
        /// </summary>
        private void TryFocusGameplayScene()
        {
            var scene = SceneManager.GetSceneByName(gameplaySceneName);
            if (!scene.IsValid() || !scene.isLoaded) return;

            SceneManager.SetActiveScene(scene);
            _gameplaySceneReady = true;
        }

        /// <summary>
        /// Server-side: every client that was present has finished loading the gameplay scene.
        /// This is the earliest safe moment to spawn anything that must exist inside that scene.
        /// </summary>
        private void HandleSceneLoadEventCompleted(string sceneName, LoadSceneMode mode,
                                                   List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer || sceneName != gameplaySceneName) return;

            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                Debug.LogWarning($"[GameManager] {clientsTimedOut.Count} client(s) timed out loading '{sceneName}'.");
            }

            // Order matters: boats first. Nessie's spawn-time visibility check measures distance to
            // each client's PlayerObject, so spawning her before the boats exist would hide her from
            // everyone until the next visibility tick corrected it.
            if (clientsCompleted != null)
            {
                foreach (ulong clientId in clientsCompleted) EnsurePlayerSpawned(clientId);
            }

            SpawnHuntActors();
            BeginHunt();
        }

        /// <summary>
        /// Server-side: a late joiner has finished synchronising (scenes loaded, existing
        /// NetworkObjects replicated). Now - and only now - does their boat exist.
        /// </summary>
        private void HandleClientSynchronizeComplete(ulong clientId)
        {
            if (!IsServer) return;
            EnsurePlayerSpawned(clientId);
        }

        /// <summary>Spawns a boat for <paramref name="clientId"/> exactly once. Idempotent by design.</summary>
        private void EnsurePlayerSpawned(ulong clientId)
        {
            if (!IsServer || playerPrefab == null) return;
            if (!Net.ConnectedClients.TryGetValue(clientId, out var client)) return;
            if (client.PlayerObject != null) return; // Already has a boat.

            GetPlayerSpawnPoint(clientId, out Vector3 position, out Quaternion rotation);

            NetworkObject boat = Instantiate(playerPrefab, position, rotation);
            boat.SpawnAsPlayerObject(clientId);
        }

        /// <summary>Deterministic ring layout so two boats never spawn inside one another.</summary>
        private void GetPlayerSpawnPoint(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            int slot = 0;
            foreach (ulong id in Net.ConnectedClientsIds)
            {
                if (id == clientId) break;
                slot++;
            }

            float angle = (360f / Mathf.Max(1, maxPlayers)) * slot * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * playerSpawnRadius;

            position = playerSpawnCenter + offset;
            rotation = Quaternion.LookRotation((playerSpawnCenter - position).normalized, Vector3.up);
        }

        /// <summary>Server-side creation of Nessie and (in solo) the AI assistant.</summary>
        private void SpawnHuntActors()
        {
            if (!IsServer) return;

            if (_nessieInstance == null && nessiePrefab != null)
            {
                Vector2 disc = UnityEngine.Random.insideUnitCircle * nessieSpawnRadius;
                Vector3 spawn = nessieSpawnCenter + new Vector3(disc.x, 0f, disc.y);

                // NessieAI snaps itself onto the lakebed NavMesh in OnNetworkSpawn, so an approximate
                // point is fine here. Keeping the NavMesh query inside the AI keeps this manager
                // free of navigation concerns.
                _nessieInstance = Instantiate(nessiePrefab, spawn, Quaternion.identity);
                _nessieInstance.Spawn();
            }

            bool wantAssistant = CurrentMode == SessionMode.SinglePlayer || spawnAssistantInMultiplayer;
            if (wantAssistant && _assistantInstance == null && assistantPrefab != null)
            {
                Vector3 spawn = playerSpawnCenter + new Vector3(playerSpawnRadius * 0.6f, 0f, -playerSpawnRadius * 0.6f);
                _assistantInstance = Instantiate(assistantPrefab, spawn, Quaternion.identity);
                _assistantInstance.Spawn();
            }
        }

        // =========================================================================================
        //  MATCH FLOW
        // =========================================================================================

        private void BeginHunt()
        {
            if (!IsServer || _phase.Value == GamePhase.Hunting) return;

            _confirmedSightings.Value = 0;
            _huntSucceeded.Value = false;
            _phaseEndServerTime.Value = Net.ServerTime.Time + huntDurationSeconds;
            _phase.Value = GamePhase.Hunting;
        }

        private void EndHunt(bool succeeded)
        {
            if (!IsServer || _phase.Value == GamePhase.Results) return;

            _huntSucceeded.Value = succeeded;
            _phase.Value = GamePhase.Results;
        }

        /// <summary>
        /// SERVER ONLY. Called by the sonar system when a ping resolves Nessie clearly enough to
        /// count. Clients never call this directly - they send a sonar request and the server
        /// decides what the return was worth, so score cannot be forged.
        /// </summary>
        public void NotifyConfirmedSighting(ulong clientId)
        {
            if (!IsServer || !IsSpawned) return;
            if (_phase.Value != GamePhase.Hunting) return;

            _confirmedSightings.Value++;

            if (_confirmedSightings.Value >= targetSightings) EndHunt(true);
        }

        // =========================================================================================
        //  TEARDOWN
        // =========================================================================================

        private IEnumerator ShutdownRoutine(string reason)
        {
            if (_connectTimeoutRoutine != null)
            {
                StopCoroutine(_connectTimeoutRoutine);
                _connectTimeoutRoutine = null;
            }

            if (!string.IsNullOrEmpty(reason)) Report(reason);

            // Despawn what we spawned. On a clean host exit this is tidy; when NGO has already
            // stopped underneath us (host quit, transport failure) the objects are gone and this
            // just clears our stale references.
            DespawnIfValid(ref _nessieInstance);
            DespawnIfValid(ref _assistantInstance);

            UnbindSceneCallbacks();

            if (Net != null && (Net.IsListening || Net.IsClient)) Net.Shutdown();

            // Shutdown is asynchronous. Unloading the gameplay scene before NGO has finished
            // tearing down produces spurious "NetworkObject destroyed while spawned" errors.
            float guard = Time.realtimeSinceStartup + 5f;
            while (Net != null && Net.ShutdownInProgress && Time.realtimeSinceStartup < guard)
            {
                yield return null;
            }

            yield return UnloadGameplaySceneRoutine();

            // Restore a sane menu context regardless of how we got here.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 1f;

            _gameplaySceneReady = false;
            SetLocalPhase(GamePhase.Menu);
            SetMode(SessionMode.None);

            _isTearingDown = false;

            OnSessionEnded?.Invoke(reason);
        }

        private IEnumerator UnloadGameplaySceneRoutine()
        {
            var scene = SceneManager.GetSceneByName(gameplaySceneName);
            if (!scene.IsValid() || !scene.isLoaded) yield break;

            // Bootstrap must be active before unloading, otherwise Unity has no active scene left.
            var bootstrap = SceneManager.GetSceneAt(0);
            if (bootstrap.IsValid() && bootstrap != scene) SceneManager.SetActiveScene(bootstrap);

            var op = SceneManager.UnloadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
        }

        private void DespawnIfValid(ref NetworkObject instance)
        {
            // Unity's overloaded == reports destroyed objects as null, which covers the case where
            // NGO's own shutdown already reclaimed them.
            if (instance == null) return;

            if (instance.IsSpawned)
            {
                // Despawn is server-only; a non-server here means NGO is mid-teardown and owns the
                // cleanup. Calling Despawn or Destroy from a client would throw or desync.
                if (IsServer) instance.Despawn(true);
            }
            else
            {
                Destroy(instance.gameObject);
            }

            instance = null;
        }

        // -----------------------------------------------------------------------------------------
        //  Utility
        // -----------------------------------------------------------------------------------------

        private void Report(string message)
        {
            Debug.Log($"[GameManager] {message}");
            OnStatusMessage?.Invoke(message);
        }

        /// <summary>True once the local peer has the gameplay scene loaded and focused.</summary>
        public bool IsGameplaySceneReady => _gameplaySceneReady;
    }
}
