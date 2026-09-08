// -----------------------------------------------------------------------------
// CrewBuilder — a crew member's body, camera rig and name tag.
//
// The body exists mainly so OTHER players see someone at the sonar console rather
// than a floating name. It is blocky on purpose: with no rig and no animation, a
// stylised figure reads better than a detailed one that never moves its legs.
//
// The owner's own body renderers are switched off in CrewController — in first
// person you would otherwise be looking at the inside of your own skull.
// -----------------------------------------------------------------------------

using LochNess.Boot;
using LochNess.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

namespace LochNess.Player
{
    public static class CrewBuilder
    {
        public const float EyeHeight = 1.62f;

        /// <summary>Oilskins. Indexed by client id so each crew member is a different colour.</summary>
        private static readonly Color[] Slickers =
        {
            new Color(0.78f, 0.36f, 0.10f), // orange
            new Color(0.85f, 0.76f, 0.20f), // yellow
            new Color(0.20f, 0.42f, 0.62f), // blue
            new Color(0.35f, 0.50f, 0.28f)  // green
        };

        public static Color SlickerFor(ulong clientId) => Slickers[(int)(clientId % (ulong)Slickers.Length)];

        /// <summary>Build the crew prefab. Returned INACTIVE, ready to forge.</summary>
        public static GameObject Build()
        {
            var root = new GameObject("Crew");
            root.SetActive(false);

            var netObject = root.AddComponent<NetworkObject>();

            // Owner-authoritative, and crucially IN LOCAL SPACE.
            //
            // Crew are parented to the boat, so what gets replicated is their position
            // ON THE DECK, not in the loch. Two consequences, both essential:
            //   * a player standing still stays exactly still relative to the deck no
            //     matter how the boat pitches or turns, with no correction jitter;
            //   * position updates stop being dominated by the boat's own motion, so
            //     they compress to almost nothing while people stand at their posts.
            var transform = root.AddComponent<Net.ClientNetworkTransform>();
            transform.InLocalSpace = true;
            transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
            transform.Interpolate = true;

            var body = BuildBody(root.transform);
            var head = BuildHead(root.transform);
            var tag = BuildNameTag(root.transform);
            var rig = BuildCameraMount(root.transform);

            var controller = root.AddComponent<CrewController>();
            controller.Bind(body, head, tag, rig);

            return root;
        }

        private static Transform BuildBody(Transform parent)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(parent, false);

            var oilskin = MeshKit.Solid(Slickers[0], 0.15f);
            var dark = MeshKit.Solid(new Color(0.14f, 0.15f, 0.17f), 0.1f);

            MeshKit.Part(body, "Torso", MeshKit.Box(new Vector3(0.48f, 0.66f, 0.28f)),
                oilskin, new Vector3(0f, 1.08f, 0f));
            MeshKit.Part(body, "Hips", MeshKit.Box(new Vector3(0.42f, 0.22f, 0.26f)),
                dark, new Vector3(0f, 0.68f, 0f));
            MeshKit.Part(body, "Leg L", MeshKit.Box(new Vector3(0.17f, 0.62f, 0.20f)),
                dark, new Vector3(-0.12f, 0.31f, 0f));
            MeshKit.Part(body, "Leg R", MeshKit.Box(new Vector3(0.17f, 0.62f, 0.20f)),
                dark, new Vector3(0.12f, 0.31f, 0f));
            MeshKit.Part(body, "Arm L", MeshKit.Box(new Vector3(0.14f, 0.58f, 0.18f)),
                oilskin, new Vector3(-0.31f, 1.06f, 0.02f));
            MeshKit.Part(body, "Arm R", MeshKit.Box(new Vector3(0.14f, 0.58f, 0.18f)),
                oilskin, new Vector3(0.31f, 1.06f, 0.02f));

            return body;
        }

        private static Transform BuildHead(Transform parent)
        {
            // Separate from the body so it can pitch with the player's view — it is
            // the only cue telling the rest of the crew where a shipmate is looking.
            var head = new GameObject("Head").transform;
            head.SetParent(parent, false);
            head.localPosition = new Vector3(0f, 1.5f, 0f);

            MeshKit.Part(head, "Skull", MeshKit.Ellipsoid(new Vector3(0.12f, 0.15f, 0.13f), 6, 8),
                MeshKit.Solid(new Color(0.72f, 0.58f, 0.48f)), new Vector3(0f, 0.06f, 0f));
            MeshKit.Part(head, "Cap", MeshKit.Box(new Vector3(0.26f, 0.09f, 0.27f)),
                MeshKit.Solid(new Color(0.12f, 0.13f, 0.15f)), new Vector3(0f, 0.17f, 0f));

            return head;
        }

        private static Text BuildNameTag(Transform parent)
        {
            return UIKit.WorldLabel(parent, new Vector3(0f, 2.08f, 0f), string.Empty,
                                    new Color(0.90f, 0.88f, 0.80f));
        }

        private static Transform BuildCameraMount(Transform parent)
        {
            // Empty mount; the camera itself is created by the owner only, so remote
            // crew never carry an unused Camera component.
            var rig = new GameObject("View").transform;
            rig.SetParent(parent, false);
            rig.localPosition = new Vector3(0f, EyeHeight, 0.06f);
            return rig;
        }
    }
}
