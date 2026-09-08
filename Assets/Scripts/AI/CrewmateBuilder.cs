// -----------------------------------------------------------------------------
// CrewmateBuilder — the AI crew member you sail with in single player.
//
// Same silhouette as a player, different oilskin and a fixed name, so that a solo
// player is visibly crewed rather than alone on a boat built for four. She is a
// networked object even in single player, because single player IS a host session
// (see GameManager) and there is no second code path to maintain.
// -----------------------------------------------------------------------------

using LochNess.Boot;
using LochNess.Player;
using LochNess.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

namespace LochNess.AI
{
    public static class CrewmateBuilder
    {
        public const string CrewmateName = "Morag";

        /// <summary>Build the AI crewmate. Returned INACTIVE, ready to forge.</summary>
        public static GameObject Build()
        {
            var root = new GameObject("Crewmate");
            root.SetActive(false);

            root.AddComponent<NetworkObject>();

            // Server-authoritative: she is simulated on the host like any other AI.
            var transform = root.AddComponent<NetworkTransform>();
            transform.InLocalSpace = true; // she stands on the deck, same as the crew
            transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
            transform.Interpolate = true;

            var body = BuildBody(root.transform);
            var tag = BuildNameTag(root.transform);

            var ai = root.AddComponent<CrewmateAI>();
            ai.Bind(body, tag);

            return root;
        }

        private static Transform BuildBody(Transform parent)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(parent, false);

            var oilskin = MeshKit.Solid(new Color(0.62f, 0.24f, 0.26f), 0.15f);
            var dark = MeshKit.Solid(new Color(0.14f, 0.15f, 0.17f), 0.1f);

            MeshKit.Part(body, "Torso", MeshKit.Box(new Vector3(0.46f, 0.64f, 0.27f)), oilskin, new Vector3(0f, 1.06f, 0f));
            MeshKit.Part(body, "Hips", MeshKit.Box(new Vector3(0.40f, 0.22f, 0.25f)), dark, new Vector3(0f, 0.67f, 0f));
            MeshKit.Part(body, "Leg L", MeshKit.Box(new Vector3(0.16f, 0.60f, 0.19f)), dark, new Vector3(-0.11f, 0.30f, 0f));
            MeshKit.Part(body, "Leg R", MeshKit.Box(new Vector3(0.16f, 0.60f, 0.19f)), dark, new Vector3(0.11f, 0.30f, 0f));
            MeshKit.Part(body, "Arm L", MeshKit.Box(new Vector3(0.13f, 0.56f, 0.17f)), oilskin, new Vector3(-0.30f, 1.04f, 0.02f));
            MeshKit.Part(body, "Arm R", MeshKit.Box(new Vector3(0.13f, 0.56f, 0.17f)), oilskin, new Vector3(0.30f, 1.04f, 0.02f));
            MeshKit.Part(body, "Head", MeshKit.Ellipsoid(new Vector3(0.12f, 0.15f, 0.13f), 6, 8),
                MeshKit.Solid(new Color(0.74f, 0.60f, 0.50f)), new Vector3(0f, 1.56f, 0f));
            MeshKit.Part(body, "Hat", MeshKit.Box(new Vector3(0.27f, 0.10f, 0.28f)),
                MeshKit.Solid(new Color(0.30f, 0.12f, 0.13f)), new Vector3(0f, 1.69f, 0f));

            return body;
        }

        private static Text BuildNameTag(Transform parent)
        {
            return UIKit.WorldLabel(parent, new Vector3(0f, 2.08f, 0f), CrewmateName,
                                    new Color(0.86f, 0.62f, 0.60f));
        }
    }
}
