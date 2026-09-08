// -----------------------------------------------------------------------------
// GameManager — session lifecycle: starting, joining, spawning, tearing down.
//
// A PLAIN MonoBehaviour, not a NetworkBehaviour. It has to exist on the title
// screen before any networking is running, and it has to outlive the session so
// that leaving a hunt returns you to a menu rather than a dead scene. The
// replicated per-hunt state lives in MatchState, which is spawned and despawned
// around it.
//
// SINGLE PLAYER IS A HOST BOUND TO LOOPBACK.
// There is no offline code path. Solo play starts a host listening on 127.0.0.1
// with an AI crewmate for company, which means the solo game and the multiplayer
// game are the same game — the same RPCs, the same authority model, the same
// spawn order. Every bug found solo is a bug found in multiplayer. A separate
// offline path would double the surface area and halve the testing.
//
// SPAWN ORDER MATTERS AND IS NOT ARBITRARY:
//   1. the vessel, because crew are parented to it;
//   2. MatchState, because the sonar reports sightings into it;
//   3. crew, as clients connect;
//   4. the monster LAST, because her spawn-time visibility check measures distance
//      to each client's crew member — spawn her first and she is hidden from
//      everyone until the next visibility sweep.
// -----------------------------------------------------------------------------

using System;
using System.Collections;
using System.Text;
using LochNess.AI;
using LochNess.Vessel;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace LochNess.Core
{
    public enum SessionMode : byte { None, SinglePlayer, Host, Client }

    public sealed class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>Human-readable progress for the menu. Also the place errors surface.</summary>
        public static event Action<string> OnStatus;
        public static event Action<SessionMode> OnSessionModeChanged;

        [SerializeField] private ushort defaultPort = 7777;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private float clientConnectTimeout = 10f;
        [SerializeField] private string buildVersion = "0.2.0-vertical-slice";

        // Prefab templates, handed over by Bootstrapper after forging.
        private GameObject _crewPrefab;
        private GameObject _boatPrefab;
        private GameObject _crewmatePrefab;
        private GameObject _nessiePrefab;
        private GameObject _matchPrefab;

        private NetworkManager _net;
        private SessionMode _mode = SessionMode.None;
        private Coroutine _connectWatchdog;
        private int _spawnedCrewCount;

        public SessionMode Mode => _mode;
        public bool InSession => _mode != SessionMode.None;

        /// <summary>
        /// NetworkManager.Singleton, resolved lazily. Deliberately NOT
        /// NetworkBehaviour.NetworkManager — that property is only valid once an
        /// object is spawned, and the title screen needs this long before then.
        /// </summary>
        private NetworkManager Net
        {
            get
            {
                if (_net == null) _net = NetworkManager.Singleton;
                return _net;
            }
        }

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>Called once by Bootstrapper with the forged prefabs.</summary>
        public void Bind(GameObject crew, GameObject boat, GameObject crewmate, GameObject nessie, GameObject match)
        {
            _crewPrefab = crew;
            _boatPrefab = boat;
            _crewmatePrefab = crewmate;
            _nessiePrefab = nessie;
            _matchPrefab = match;
        }

        // ---------------------------------------------------------------------
        // Starting and joining
        // ---------------------------------------------------------------------

        public bool StartSinglePlayer()
        {
            if (!Prepare(SessionMode.SinglePlayer)) return false;

            // Loopback only: a solo session must not accidentally be reachable from
            // the network just because it is implemented as a host.
            Configure("127.0.0.1", "127.0.0.1");

            if (!Net.StartHost()) { Fail("Could not start the local session."); return false; }
            Report("Casting off...");
            return true;
        }

        public bool StartHost()
        {
            if (!Prepare(SessionMode.Host)) return false;

            Configure("0.0.0.0", "0.0.0.0");

            if (!Net.StartHost()) { Fail("Could not open the session."); return false; }
            Report($"Hosting on port {defaultPort}. Waiting for crew...");
            return true;
        }

        public bool StartClient(string address)
        {
            if (!Prepare(SessionMode.Client)) return false;

            if (string.IsNullOrWhiteSpace(address)) address = "127.0.0.1";
            Configure(address.Trim(), "0.0.0.0");

            // The payload is checked in the approval callback on the host. Mismatched
            // builds produce a clear refusal rather than a desync ten minutes in.
            Net.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(buildVersion);

            if (!Net.StartClient()) { Fail("Could not reach that address."); return false; }

            Report($"Connecting to {address}...");
            _connectWatchdog = StartCoroutine(WatchConnect());
            return true;
        }

        private bool Prepare(SessionMode mode)
        {
            if (Net == null) { Fail("Netcode is not initialised."); return false; }
            if (InSession) { Fail("Already in a session."); return false; }
            if (_crewPrefab == null || _boatPrefab == null) { Fail("Prefabs were not forged."); return false; }

            _mode = mode;
            _spawnedCrewCount = 0;
            OnSessionModeChanged?.Invoke(_mode);

            Net.ConnectionApprovalCallback = ApproveConnection;
            Net.OnServerStarted += HandleServerStarted;
            Net.OnClientConnectedCallback += HandleClientConnected;
            Net.OnClientDisconnectCallback += HandleClientDisconnected;
            return true;
        }

        private void Configure(string address, string listenAddress)
        {
            var transport = Net.GetComponent<UnityTransport>();
            if (transport != null) transport.SetConnectionData(address, defaultPort, listenAddress);
            Net.NetworkConfig.ConnectionApproval = true;
        }

        // ---------------------------------------------------------------------
        // Approval
        // ---------------------------------------------------------------------

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
                                       NetworkManager.ConnectionApprovalResponse response)
        {
            // CreatePlayerObject is false on purpose. NGO would otherwise spawn the crew
            // member during approval, before the vessel exists — and a crew member with
            // nothing to be parented to falls straight through the world.
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (Net.ConnectedClientsIds.Count >= maxPlayers)
            {
                response.Approved = false;
                response.Reason = "The boat is full.";
                return;
            }

            // The host approves its own connection with an empty payload; only remote
            // clients carry a version string.
            if (request.ClientNetworkId != Net.LocalClientId)
            {
                string version = request.Payload != null && request.Payload.Length > 0
                    ? Encoding.UTF8.GetString(request.Payload)
                    : string.Empty;

                if (version != buildVersion)
                {
                    response.Approved = false;
                    response.Reason = $"Build mismatch — host is running {buildVersion}.";
                    return;
                }
            }

            response.Approved = true;
        }

        // ---------------------------------------------------------------------
        // Server-side spawning
        // ---------------------------------------------------------------------

        private void HandleServerStarted()
        {
            // 1. The vessel. Everything else hangs off it.
            Vector3 start = new Vector3(0f, 0f, -60f);
            GameObject boat = Instantiate(_boatPrefab, start, Quaternion.Euler(0f, 20f, 0f));
            boat.SetActive(true);
            boat.GetComponent<NetworkObject>().Spawn();

            // 2. Match state.
            GameObject match = Instantiate(_matchPrefab);
            match.SetActive(true);
            match.GetComponent<NetworkObject>().Spawn();

            // 3. The monster, well away from the boat's start so the first ping is not
            //    a gift. She goes last so her visibility check has crew to measure from
            //    — although on the host's own start there are none yet, which is why
            //    NessieAI also sweeps visibility four times a second.
            Vector3 lair = new Vector3(140f, -34f, 70f);
            GameObject nessie = Instantiate(_nessiePrefab, lair, Quaternion.Euler(0f, 210f, 0f));
            nessie.SetActive(true);

            var nessieObject = nessie.GetComponent<NetworkObject>();
            var nessieAi = nessie.GetComponent<NessieAI>();
            // Set BEFORE spawning: this makes her invisible to distant clients from the
            // very first replicated frame, rather than visible for one tick.
            nessieObject.CheckObjectVisibility = nessieAi.ShouldBeVisibleTo;
            nessieObject.Spawn();

            // 4. In solo play, someone to work the sonar while you steer.
            if (_mode == SessionMode.SinglePlayer && _crewmatePrefab != null)
            {
                GameObject mate = Instantiate(_crewmatePrefab);
                mate.SetActive(true);
                mate.GetComponent<NetworkObject>().Spawn();
                mate.GetComponent<NetworkObject>().TrySetParent(boat.GetComponent<NetworkObject>(), false);
            }

            MatchState.Server?.BeginHunt();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (clientId == Net.LocalClientId)
            {
                if (_connectWatchdog != null) { StopCoroutine(_connectWatchdog); _connectWatchdog = null; }
                Report("Aboard.");
            }

            if (!Net.IsServer) return;
            SpawnCrewFor(clientId);
        }

        private void SpawnCrewFor(ulong clientId)
        {
            var boat = BoatController.Instance;
            if (boat == null || _crewPrefab == null) return;

            Vector3 local = BoatController.CrewSpawnLocal(_spawnedCrewCount++);

            GameObject crew = Instantiate(_crewPrefab, boat.transform.TransformPoint(local), boat.transform.rotation);
            crew.SetActive(true);

            var netObject = crew.GetComponent<NetworkObject>();
            netObject.SpawnAsPlayerObject(clientId);

            // Parent AFTER spawning. NGO replicates the parent change to every client,
            // and from here the crew member's NetworkTransform carries local-space
            // coordinates — i.e. a position on the deck. See CrewController.
            netObject.TrySetParent(boat.NetworkObject, false);
            crew.transform.localPosition = local;
            crew.transform.localRotation = Quaternion.identity;
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (Net != null && Net.IsServer)
            {
                // Critical: a helmsman who drops out must not leave the helm locked for
                // the rest of the hunt.
                BoatController.Instance?.ReleaseAllFor(clientId);
            }

            if (clientId != Net.LocalClientId) return;

            // We were the one who dropped.
            string reason = Net.DisconnectReason;
            Report(string.IsNullOrEmpty(reason) ? "Disconnected." : reason);
            Teardown();
        }

        private IEnumerator WatchConnect()
        {
            float deadline = Time.realtimeSinceStartup + clientConnectTimeout;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (Net == null || Net.IsConnectedClient) yield break;
                yield return null;
            }

            // NGO gives no failure callback for "the host never answered", so the
            // timeout is ours to run — without it the menu sits on "Connecting..."
            // for ever.
            Report("No answer from that address.");
            Teardown();
        }

        // ---------------------------------------------------------------------
        // Leaving
        // ---------------------------------------------------------------------

        public void Leave()
        {
            Report("Heading in.");
            Teardown();
        }

        private void Teardown()
        {
            if (_connectWatchdog != null) { StopCoroutine(_connectWatchdog); _connectWatchdog = null; }

            if (Net != null)
            {
                Net.OnServerStarted -= HandleServerStarted;
                Net.OnClientConnectedCallback -= HandleClientConnected;
                Net.OnClientDisconnectCallback -= HandleClientDisconnected;
                Net.ConnectionApprovalCallback = null;

                // Shutdown is checked rather than assumed: a failed StartClient leaves
                // NGO not listening, and calling Shutdown then logs a warning.
                if (Net.IsListening || Net.IsClient || Net.IsServer) Net.Shutdown();
            }

            _mode = SessionMode.None;
            _spawnedCrewCount = 0;
            OnSessionModeChanged?.Invoke(_mode);
        }

        private void Report(string message) => OnStatus?.Invoke(message);

        private void Fail(string message)
        {
            Debug.LogWarning($"[Session] {message}");
            Report(message);

            // Teardown, not just a mode reset. Prepare() subscribed to NetworkManager's
            // callbacks; leaving them attached after a failed start means the next
            // attempt subscribes a second time and HandleServerStarted spawns a second
            // boat, monster and MatchState.
            Teardown();
        }
    }
}
