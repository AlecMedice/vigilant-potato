// -----------------------------------------------------------------------------
// NessieBuilder — the animal's body, as a chain of segments.
//
// There is no rig and no animation clip here (both are authored assets). Instead
// the body is a chain of parented transforms, each rotated by a phase-shifted
// sine in NessieAI. A travelling wave down a chain is exactly what swimming looks
// like, so this reads as a living thing for about thirty lines of maths — and it
// is driven by her actual speed, so a fleeing animal thrashes and a hiding one
// barely stirs.
//
// The silhouette is the classic Surgeon's Photograph one: a small head on a long
// neck, a humped back, no visible limbs. It is what people expect, and half the
// value of a sighting is recognising it.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using LochNess.Boot;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace LochNess.AI
{
    public static class NessieBuilder
    {
        private static readonly Color Hide = new Color(0.11f, 0.14f, 0.13f);
        private static readonly Color Belly = new Color(0.19f, 0.20f, 0.17f);

        /// <summary>Build the monster. Returned INACTIVE, ready to forge.</summary>
        public static GameObject Build()
        {
            var root = new GameObject("Nessie");
            root.SetActive(false);

            var hideMaterial = MeshKit.Solid(Hide, 0.45f);
            var bellyMaterial = MeshKit.Solid(Belly, 0.35f);

            var body = BuildBody(root.transform, hideMaterial, bellyMaterial);
            var neck = BuildNeck(root.transform, hideMaterial);

            root.AddComponent<NetworkObject>();

            // Stock, server-authoritative NetworkTransform. There is no owner to hand
            // authority to and never should be: the entire hunt depends on the
            // monster's position being something only the server knows.
            var transform = root.AddComponent<NetworkTransform>();
            transform.InLocalSpace = false;
            transform.SyncScaleX = transform.SyncScaleY = transform.SyncScaleZ = false;
            transform.Interpolate = true;

            var ai = root.AddComponent<NessieAI>();
            ai.Bind(body, neck);

            return root;
        }

        /// <summary>
        /// The trunk: seven ellipsoids running aft from the shoulders, each a child of
        /// the one in front so a rotation at segment 2 carries everything behind it.
        /// </summary>
        private static Transform[] BuildBody(Transform root, Material hide, Material belly)
        {
            var segments = new List<Transform>();
            Transform parent = root;

            // (length back from the previous joint, half-width, half-height)
            var plan = new[]
            {
                new Vector3(0.0f, 1.05f, 0.95f),
                new Vector3(2.1f, 1.30f, 1.20f),
                new Vector3(2.3f, 1.25f, 1.15f),
                new Vector3(2.2f, 1.00f, 0.92f),
                new Vector3(2.0f, 0.72f, 0.66f),
                new Vector3(1.8f, 0.46f, 0.44f),
                new Vector3(1.6f, 0.22f, 0.30f)
            };

            for (int i = 0; i < plan.Length; i++)
            {
                var joint = new GameObject($"Spine{i}").transform;
                joint.SetParent(parent, false);
                joint.localPosition = new Vector3(0f, 0f, -plan[i].x);

                Vector3 radii = new Vector3(plan[i].y, plan[i].z, plan[i].x * 0.62f + 0.9f);
                MeshKit.Part(joint, $"Segment{i}", MeshKit.Ellipsoid(radii, 7, 10), hide, Vector3.zero);

                // The hump. Segment 1 and 2 carry a raised ridge — it is the part that
                // breaks the surface first and the part a witness would describe.
                if (i == 1 || i == 2)
                {
                    MeshKit.Part(joint, $"Hump{i}", MeshKit.Ellipsoid(new Vector3(0.7f, 0.62f, 1.5f), 6, 9),
                        hide, new Vector3(0f, plan[i].z * 0.62f, 0f));
                }

                if (i >= 1 && i <= 3)
                {
                    MeshKit.Part(joint, $"Flipper{i}L", MeshKit.Ellipsoid(new Vector3(0.16f, 0.42f, 0.95f), 5, 7),
                        belly, new Vector3(-plan[i].y * 0.95f, -0.35f, 0f), Quaternion.Euler(0f, 24f, -18f));
                    MeshKit.Part(joint, $"Flipper{i}R", MeshKit.Ellipsoid(new Vector3(0.16f, 0.42f, 0.95f), 5, 7),
                        belly, new Vector3(plan[i].y * 0.95f, -0.35f, 0f), Quaternion.Euler(0f, -24f, 18f));
                }

                segments.Add(joint);
                parent = joint;
            }

            return segments.ToArray();
        }

        /// <summary>The neck and head, rising forward from the shoulders.</summary>
        private static Transform[] BuildNeck(Transform root, Material hide)
        {
            var segments = new List<Transform>();
            Transform parent = root;

            var plan = new[]
            {
                new Vector3(1.3f, 0.52f, 0.30f),
                new Vector3(1.3f, 0.44f, 0.34f),
                new Vector3(1.2f, 0.38f, 0.36f),
                new Vector3(1.1f, 0.32f, 0.34f)
            };

            for (int i = 0; i < plan.Length; i++)
            {
                var joint = new GameObject($"Neck{i}").transform;
                joint.SetParent(parent, false);
                // Forward and up: the neck arcs rather than standing straight, which is
                // what makes the silhouette read as an animal and not a periscope.
                joint.localPosition = new Vector3(0f, plan[i].z, plan[i].x);
                joint.localRotation = Quaternion.Euler(-11f, 0f, 0f);

                MeshKit.Part(joint, $"NeckSeg{i}", MeshKit.Ellipsoid(new Vector3(plan[i].y, plan[i].y, 0.85f), 6, 8),
                    hide, Vector3.zero);

                segments.Add(joint);
                parent = joint;
            }

            // Head: small, blunt, low-set. Deliberately undersized against the neck.
            var head = new GameObject("Head").transform;
            head.SetParent(parent, false);
            head.localPosition = new Vector3(0f, 0.16f, 0.9f);

            MeshKit.Part(head, "Skull", MeshKit.Ellipsoid(new Vector3(0.34f, 0.30f, 0.62f), 6, 9), hide, Vector3.zero);
            MeshKit.Part(head, "Snout", MeshKit.Ellipsoid(new Vector3(0.20f, 0.18f, 0.34f), 5, 7),
                hide, new Vector3(0f, -0.04f, 0.55f));

            var eye = MeshKit.Emissive(new Color(0.72f, 0.64f, 0.28f));
            MeshKit.Part(head, "Eye L", MeshKit.Ellipsoid(new Vector3(0.07f, 0.07f, 0.07f), 4, 6), eye,
                new Vector3(-0.22f, 0.10f, 0.28f));
            MeshKit.Part(head, "Eye R", MeshKit.Ellipsoid(new Vector3(0.07f, 0.07f, 0.07f), 4, 6), eye,
                new Vector3(0.22f, 0.10f, 0.28f));

            segments.Add(head);
            return segments.ToArray();
        }
    }
}
