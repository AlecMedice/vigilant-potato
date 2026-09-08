// -----------------------------------------------------------------------------
// BoatBuilder — assembles the survey launch, in code.
//
// A 14-metre working boat: open afterdeck, wheelhouse aft of amidships, sonar
// console beside the helm, and a bow you can stand right up in. The geometry is
// blunt but the *proportions* are doing real work — the wheelhouse has to be big
// enough for two people to stand in and see each other, and the walk from the bow
// to the helm has to take long enough that choosing to leave your post costs you
// something.
//
// See MeshKit.cs for why none of this is a prefab asset.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Boot;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace LochNess.Vessel
{
    public static class BoatBuilder
    {
        public const float DeckY = 0f;        // local Y of the deck plates
        public const float GunwaleY = 1.3f;   // local Y of the rail cap
        public const float HalfBeam = 2.2f;

        private static readonly Color HullColour = new Color(0.13f, 0.16f, 0.19f);
        private static readonly Color DeckColour = new Color(0.28f, 0.24f, 0.19f);
        private static readonly Color TrimColour = new Color(0.55f, 0.42f, 0.16f);
        private static readonly Color CabinColour = new Color(0.20f, 0.23f, 0.24f);

        /// <summary>Build the vessel hierarchy. Returned object is INACTIVE, ready to forge.</summary>
        public static GameObject Build()
        {
            var root = new GameObject("Survey Launch");
            root.SetActive(false); // build cold — see NetworkPrefabForge

            BuildHull(root.transform);
            BuildWheelhouse(root.transform);
            BuildRails(root.transform);
            BuildMast(root.transform);
            var stations = BuildStations(root.transform);
            var wake = BuildWake(root.transform);

            var netObject = root.AddComponent<NetworkObject>();
            netObject.AlwaysReplicateAsRoot = true;

            // Server-authoritative: the vessel's motion is simulated once, on the
            // host, and replicated. The driver only sends helm input. This costs the
            // driver a little input latency and buys immunity from a client
            // teleporting the boat everyone else is standing on.
            var transform = root.AddComponent<NetworkTransform>();
            transform.InLocalSpace = false;
            transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
            transform.Interpolate = true;

            var controller = root.AddComponent<BoatController>();
            controller.Bind(stations, wake);

            // The sonar set is a second NetworkBehaviour on the same NetworkObject. It
            // belongs to the vessel rather than to the operator: the transducer is in
            // the hull, and whoever is standing at the console is just working it.
            root.AddComponent<SonarSet>();

            return root;
        }

        private static void BuildHull(Transform parent)
        {
            // (z, half-beam, gunwale y, keel y) — a fine entry forward, full amidships,
            // and a transom aft.
            var stations = new List<Vector4>
            {
                new Vector4(-7.0f, 1.85f, GunwaleY, -0.95f),
                new Vector4(-4.0f, 2.15f, GunwaleY, -1.15f),
                new Vector4( 0.0f, HalfBeam, GunwaleY, -1.20f),
                new Vector4( 3.5f, 1.95f, GunwaleY + 0.08f, -1.05f),
                new Vector4( 5.6f, 1.25f, GunwaleY + 0.16f, -0.72f),
                new Vector4( 7.1f, 0.16f, GunwaleY + 0.24f, -0.16f)
            };

            MeshKit.Part(parent, "Hull", MeshKit.Loft(stations), MeshKit.Solid(HullColour, 0.25f), Vector3.zero);

            // Deck plates, laid slightly proud of the hull's interior so there is no
            // z-fighting where they meet the sides.
            MeshKit.Part(parent, "Deck", MeshKit.Box(new Vector3(4.15f, 0.12f, 13.6f)),
                MeshKit.Solid(DeckColour, 0.05f), new Vector3(0f, DeckY - 0.06f, -0.2f));

            // A boot-top stripe. Purely so the hull reads as a made object.
            MeshKit.Part(parent, "Boot Top", MeshKit.Box(new Vector3(4.5f, 0.14f, 13.2f)),
                MeshKit.Solid(TrimColour, 0.3f), new Vector3(0f, GunwaleY - 0.12f, -0.2f));
        }

        private static void BuildWheelhouse(Transform parent)
        {
            var cabin = MeshKit.Solid(CabinColour, 0.2f);
            var glass = MeshKit.Translucent(new Color(0.35f, 0.45f, 0.48f, 0.35f));

            // Aft, port and starboard walls. The front is left open so the helmsman
            // has an unobstructed view forward and other crew can see in.
            MeshKit.Part(parent, "Cabin Aft", MeshKit.Box(new Vector3(3.6f, 2.1f, 0.14f)),
                cabin, new Vector3(0f, DeckY + 1.05f, -6.0f));
            MeshKit.Part(parent, "Cabin Port", MeshKit.Box(new Vector3(0.14f, 2.1f, 4.2f)),
                cabin, new Vector3(-1.78f, DeckY + 1.05f, -3.95f));
            MeshKit.Part(parent, "Cabin Starboard", MeshKit.Box(new Vector3(0.14f, 2.1f, 4.2f)),
                cabin, new Vector3(1.78f, DeckY + 1.05f, -3.95f));
            MeshKit.Part(parent, "Cabin Roof", MeshKit.Box(new Vector3(3.8f, 0.12f, 4.4f)),
                cabin, new Vector3(0f, DeckY + 2.16f, -3.95f));

            // A windscreen brow above the open front, so the roof is not floating.
            MeshKit.Part(parent, "Windscreen", MeshKit.Box(new Vector3(3.6f, 0.55f, 0.1f)),
                glass, new Vector3(0f, DeckY + 1.85f, -1.9f));

            // The helm console and the sonar console.
            MeshKit.Part(parent, "Helm Console", MeshKit.Box(new Vector3(1.5f, 0.95f, 0.5f)),
                MeshKit.Solid(new Color(0.16f, 0.17f, 0.18f)), new Vector3(0.35f, DeckY + 0.48f, -2.65f));
            MeshKit.Part(parent, "Wheel", MeshKit.Cylinder(0.34f, 0.07f, 14),
                MeshKit.Solid(new Color(0.30f, 0.20f, 0.11f), 0.4f),
                new Vector3(0.35f, DeckY + 1.18f, -2.75f), Quaternion.Euler(72f, 0f, 0f));

            MeshKit.Part(parent, "Sonar Console", MeshKit.Box(new Vector3(1.1f, 1.05f, 0.6f)),
                MeshKit.Solid(new Color(0.15f, 0.16f, 0.17f)), new Vector3(-1.15f, DeckY + 0.52f, -4.6f));
            // The scope itself glows — it is the one warm thing in the wheelhouse and
            // it is how you find the station in the dark.
            MeshKit.Part(parent, "Scope Face", MeshKit.Cylinder(0.28f, 0.05f, 16),
                MeshKit.Emissive(new Color(0.28f, 0.72f, 0.62f)),
                new Vector3(-1.15f, DeckY + 1.06f, -4.6f), Quaternion.Euler(18f, 0f, 0f));
        }

        private static void BuildRails(Transform parent)
        {
            var rail = MeshKit.Solid(new Color(0.42f, 0.44f, 0.45f), 0.5f, 0.6f);
            var stanchion = MeshKit.Cylinder(0.035f, 1.0f, 6);

            // Stanchions down both sides of the open foredeck, with a cap rail.
            for (float z = -1.2f; z <= 6.2f; z += 1.25f)
            {
                float beam = Mathf.Lerp(HalfBeam - 0.12f, 0.5f, Mathf.InverseLerp(3.5f, 7f, z));
                MeshKit.Part(parent, "Stanchion", stanchion, rail, new Vector3(-beam, GunwaleY + 0.5f, z));
                MeshKit.Part(parent, "Stanchion", stanchion, rail, new Vector3(beam, GunwaleY + 0.5f, z));
            }

            MeshKit.Part(parent, "Cap Rail Port", MeshKit.Box(new Vector3(0.06f, 0.06f, 7.6f)),
                rail, new Vector3(-1.75f, GunwaleY + 1.0f, 2.5f), Quaternion.Euler(0f, -9f, 0f));
            MeshKit.Part(parent, "Cap Rail Starboard", MeshKit.Box(new Vector3(0.06f, 0.06f, 7.6f)),
                rail, new Vector3(1.75f, GunwaleY + 1.0f, 2.5f), Quaternion.Euler(0f, 9f, 0f));
        }

        private static void BuildMast(Transform parent)
        {
            var steel = MeshKit.Solid(new Color(0.38f, 0.39f, 0.40f), 0.55f, 0.7f);
            MeshKit.Part(parent, "Mast", MeshKit.Cylinder(0.09f, 3.4f, 8), steel,
                new Vector3(0f, DeckY + 3.9f, -4.4f));

            // Masthead lamp. A real light, not just emissive geometry: on a fogged
            // loch at dusk it is the only thing that makes the deck readable.
            var lampGo = new GameObject("Masthead Lamp");
            lampGo.transform.SetParent(parent, false);
            lampGo.transform.localPosition = new Vector3(0f, DeckY + 5.5f, -4.4f);

            var lamp = lampGo.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 26f;
            lamp.intensity = 2.4f;
            lamp.color = new Color(1f, 0.93f, 0.78f);
            lamp.shadows = LightShadows.None; // point-light shadows are not worth the cost here

            MeshKit.Part(parent, "Lamp Glass", MeshKit.Ellipsoid(new Vector3(0.16f, 0.2f, 0.16f), 6, 8),
                MeshKit.Emissive(new Color(1f, 0.94f, 0.8f)), new Vector3(0f, DeckY + 5.5f, -4.4f));
        }

        private static BoatStation[] BuildStations(Transform parent)
        {
            var stations = new BoatStation[3];
            stations[0] = MakeStation(parent, StationRole.Helm, new Vector3(0.35f, DeckY, -3.5f), 0f);
            stations[1] = MakeStation(parent, StationRole.Sonar, new Vector3(-1.15f, DeckY, -5.35f), 0f);
            stations[2] = MakeStation(parent, StationRole.Watch, new Vector3(0f, DeckY, 5.4f), 0f);
            return stations;
        }

        private static BoatStation MakeStation(Transform parent, StationRole role, Vector3 local, float yaw)
        {
            var go = new GameObject($"Station.{role}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            var station = go.AddComponent<BoatStation>();
            station.Configure(role, role == StationRole.Watch ? 2.4f : 1.9f);
            return station;
        }

        private static Transform BuildWake(Transform parent)
        {
            // Two translucent quads dragged behind the transom. Scaled by speed in
            // BoatController — the only feedback that tells a helmsman, from inside
            // the wheelhouse, how much way the boat is carrying.
            var wake = new GameObject("Wake").transform;
            wake.SetParent(parent, false);
            wake.localPosition = new Vector3(0f, 0.06f, -7.4f);

            var foam = MeshKit.Translucent(new Color(0.78f, 0.85f, 0.86f, 0.30f));
            MeshKit.Part(wake, "Wake Port", MeshKit.Box(new Vector3(1.1f, 0.02f, 9f)), foam,
                new Vector3(-1.0f, 0f, -4.3f), Quaternion.Euler(0f, -7f, 0f));
            MeshKit.Part(wake, "Wake Starboard", MeshKit.Box(new Vector3(1.1f, 0.02f, 9f)), foam,
                new Vector3(1.0f, 0f, -4.3f), Quaternion.Euler(0f, 7f, 0f));

            return wake;
        }
    }
}
