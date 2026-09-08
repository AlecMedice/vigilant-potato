// -----------------------------------------------------------------------------
// NetworkPrefabForge — registering network prefabs that were built at runtime.
//
// THE PROBLEM
// Netcode for GameObjects identifies a prefab by `NetworkObject.GlobalObjectIdHash`,
// a uint that the Editor computes when the prefab asset is serialised. Every peer
// must agree on it, or a spawn message names an object the receiver cannot build.
//
// A GameObject assembled at runtime has never been serialised, so its hash is 0,
// and `AddNetworkPrefab` rejects it. Since this project deliberately ships no
// prefab assets (see MeshKit.cs for why), the hash has to be supplied another way.
//
// THE SOLUTION
// Assign it directly, by reflection, from a deterministic hash of a string id.
// FNV-1a over an ASCII name gives the same uint on every machine and every run, so
// host and client agree without ever exchanging the mapping. This is the same
// mechanism NGO's own integration tests use to register prefabs they build in code.
//
// THE RISK, STATED PLAINLY
// `GlobalObjectIdHash` is internal. A future NGO release could rename it and this
// would break — loudly, at boot, with the message below, not subtly at runtime.
// Both the current and the historical field names are tried. If Netcode ever
// exposes a supported API for this, it should replace `AssignStableHash` entirely.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Reflection;
using LochNess.Sim;
using Unity.Netcode;
using UnityEngine;

namespace LochNess.Boot
{
    public static class NetworkPrefabForge
    {
        private static readonly List<GameObject> Templates = new List<GameObject>();
        private static FieldInfo _hashField;
        private static PropertyInfo _hashProperty;
        private static bool _hashMemberResolved;

        /// <summary>Deterministic id shared by every peer. Same string in, same uint out.</summary>
        public static uint HashFor(string id) => SimMath.Fnv1a(id);

        /// <summary>
        /// Turn a runtime-built hierarchy into a registered network prefab.
        ///
        /// The GameObject must already be INACTIVE — a prefab template that ticks is
        /// a prefab template that spawns wakes, coroutines and audio for an object
        /// nobody can see.
        /// </summary>
        /// <param name="manager">The NetworkManager to register with, before it starts.</param>
        /// <param name="id">Stable identifier, e.g. "lochness.boat". Never change these.</param>
        /// <param name="root">The assembled hierarchy.</param>
        /// <returns>The same object, now registered, or null if registration failed.</returns>
        public static GameObject Register(NetworkManager manager, string id, GameObject root)
        {
            if (manager == null || root == null) return null;

            if (root.activeSelf)
            {
                Debug.LogWarning($"[Forge] '{id}' was active when registered; deactivating.");
                root.SetActive(false);
            }

            var netObject = root.GetComponent<NetworkObject>();
            if (netObject == null) netObject = root.AddComponent<NetworkObject>();

            if (!AssignStableHash(netObject, HashFor(id)))
            {
                Debug.LogError(
                    $"[Forge] Could not set GlobalObjectIdHash on '{id}'. Netcode for GameObjects has " +
                    "probably renamed the field. Nothing will spawn until NetworkPrefabForge is updated.");
                return null;
            }

            Object.DontDestroyOnLoad(root);
            Templates.Add(root);

            manager.AddNetworkPrefab(root);
            return root;
        }

        /// <summary>Drop every template. Called on teardown so a reboot does not double-register.</summary>
        public static void Clear()
        {
            for (int i = 0; i < Templates.Count; i++)
            {
                if (Templates[i] != null) Object.Destroy(Templates[i]);
            }
            Templates.Clear();
        }

        private static bool AssignStableHash(NetworkObject netObject, uint hash)
        {
            if (!_hashMemberResolved)
            {
                _hashMemberResolved = true;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                _hashField = typeof(NetworkObject).GetField("GlobalObjectIdHash", flags)
                             ?? typeof(NetworkObject).GetField("m_GlobalObjectIdHash", flags);

                if (_hashField == null)
                {
                    // Netcode may expose it as a property instead. Only useful if it is
                    // writable — a getter alone cannot help us.
                    PropertyInfo candidate = typeof(NetworkObject).GetProperty("GlobalObjectIdHash", flags)
                                             ?? typeof(NetworkObject).GetProperty("PrefabIdHash", flags);
                    if (candidate != null && candidate.CanWrite) _hashProperty = candidate;
                }
            }

            if (_hashField != null)
            {
                _hashField.SetValue(netObject, hash);
                return true;
            }

            if (_hashProperty != null)
            {
                _hashProperty.SetValue(netObject, hash);
                return true;
            }

            return false;
        }
    }
}
