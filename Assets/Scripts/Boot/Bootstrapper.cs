// -----------------------------------------------------------------------------
// Bootstrapper — the entry point. Press Play in any scene and the game starts.
//
// WHY IT SELF-INSTALLS
// [RuntimeInitializeOnLoadMethod] runs before the first scene loads, so the game
// does not depend on a particular scene being open, on objects being present in
// it, or on inspector references being wired up. Clone the repository, open it in
// Unity, press Play — that is the whole setup procedure, and it works from an
// empty scene.
//
// That property is the entire reason this project is built the way it is. Every
// authored asset is a thing that can be silently broken or forgotten; none of
// them can be verified without an Editor. Code can be compiled and checked
// (see tools/compilecheck), so the game is code.
//
// BOOT ORDER MATTERS:
//   1. The loch, because it is static and everything else is positioned against it.
//   2. NetworkManager, because prefabs register into it.
//   3. Prefabs, forged and registered before anything can start listening.
//   4. GameManager and the UI, which need the prefabs in hand.
// -----------------------------------------------------------------------------

using LochNess.AI;
using LochNess.Core;
using LochNess.Player;
using LochNess.UI;
using LochNess.Vessel;
using LochNess.World;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace LochNess.Boot
{
    public sealed class Bootstrapper : MonoBehaviour
    {
        private static Bootstrapper _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;

            var go = new GameObject("Loch Ness");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Bootstrapper>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;

            // 1. The world.
            var world = new GameObject("World").transform;
            world.SetParent(transform, false);
            LochBuilder.Build(world);

            // 2. Netcode.
            NetworkManager net = BuildNetworkManager();

            // 3. Session control and interface.
            //
            // Built BEFORE the prefabs, deliberately. Forging registers prefabs with
            // Netcode, and that is the one step here that can throw on an engine or
            // package version this was not written against. If it takes the UI down
            // with it the player gets a blank screen and no way to see what happened;
            // this way the menu is always up and the failure is a message on it.
            var manager = gameObject.AddComponent<GameManager>();
            gameObject.AddComponent<GameUI>();

            // 4. Prefabs, forged from code and registered by stable hash.
            try
            {
                GameObject crew = NetworkPrefabForge.Register(net, "lochness.crew", CrewBuilder.Build());
                GameObject boat = NetworkPrefabForge.Register(net, "lochness.boat", BoatBuilder.Build());
                GameObject crewmate = NetworkPrefabForge.Register(net, "lochness.crewmate", CrewmateBuilder.Build());
                GameObject nessie = NetworkPrefabForge.Register(net, "lochness.nessie", NessieBuilder.Build());
                GameObject match = NetworkPrefabForge.Register(net, "lochness.match", BuildMatchState());

                // NGO wants a player prefab registered even though GameManager spawns
                // crew by hand (see its approval callback for why CreatePlayerObject
                // is false).
                net.NetworkConfig.PlayerPrefab = crew;

                manager.Bind(crew, boat, crewmate, nessie, match);
                Debug.Log("[Boot] Ready.");
            }
            catch (System.Exception error)
            {
                // GameManager refuses to start a session with null prefabs and says so
                // on the title screen, so this is reported, not silently survived.
                Debug.LogError($"[Boot] Could not build the network prefabs: {error}");
            }
        }

        private NetworkManager BuildNetworkManager()
        {
            var go = new GameObject("Network Manager");
            go.transform.SetParent(transform, false);

            var net = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();

            net.NetworkConfig.NetworkTransport = transport;
            net.NetworkConfig.ConnectionApproval = true;
            net.NetworkConfig.TickRate = 30;

            // Scene management OFF. The game lives in one scene that every peer builds
            // for itself from the same seed, so there is nothing to synchronise — and
            // leaving it on would make clients wait on a scene-load handshake that
            // never comes.
            net.NetworkConfig.EnableSceneManagement = false;

            return net;
        }

        private static GameObject BuildMatchState()
        {
            // No geometry: it is pure replicated state. Built the same way as everything
            // else so it goes through the same registration path.
            var go = new GameObject("Match");
            go.SetActive(false);
            go.AddComponent<MatchState>();
            return go;
        }

        private void Start()
        {
            AdoptScene();
        }

        /// <summary>
        /// Take over the scene we happen to have booted into.
        ///
        /// Every Unity template ships a scene containing a Main Camera (with an
        /// AudioListener) and a Directional Light. This game builds its own of each,
        /// so leaving the scene's copies enabled means two cameras rendering the same
        /// view with undefined ordering, two directional lights washing out the dusk
        /// the atmosphere is carefully set up for, and a duplicate-AudioListener
        /// warning logged every single frame.
        ///
        /// Runs in Start rather than Awake: this object is created BeforeSceneLoad, so
        /// at Awake the scene's own objects do not exist yet.
        ///
        /// They are disabled, never destroyed — this is somebody else's scene, and it
        /// should be intact again when they stop playing.
        /// </summary>
        private void AdoptScene()
        {
            int cameras = 0, listeners = 0, lights = 0;

            foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                if (IsOurs(camera.transform) || !camera.enabled) continue;
                camera.enabled = false;
                cameras++;
            }

            foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (IsOurs(listener.transform) || !listener.enabled) continue;
                listener.enabled = false;
                listeners++;
            }

            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (IsOurs(light.transform) || !light.enabled) continue;
                light.enabled = false;
                lights++;
            }

            if (cameras + listeners + lights > 0)
            {
                Debug.Log($"[Boot] Took over the open scene: disabled {cameras} camera(s), " +
                          $"{listeners} audio listener(s) and {lights} light(s). They are restored on stop.");
            }
        }

        /// <summary>True if this transform belongs to the game's own boot hierarchy.</summary>
        private bool IsOurs(Transform candidate) => candidate != null && candidate.root == transform;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
