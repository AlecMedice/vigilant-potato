// -----------------------------------------------------------------------------
// LochBuilder — generates the loch, its shores, and the weather.
//
// The terrain is NOT replicated. Every peer generates it from the same integer
// seed using SimMath's deterministic value noise, so host and clients stand on
// identical ground without a byte crossing the wire. That also means the seed is
// the one thing that must never diverge — it is a compile-time constant here.
//
// SHAPE
// The basin is an ellipse. Inside it the bed falls away on a cosine profile to
// `maxDepth`; outside it the ground climbs into hills. Real Loch Ness is a
// glacial trench — steep sides, flat floor, and startlingly deep — and that shape
// matters for gameplay: the monster has vertical room to hide in, and the shore is
// close enough on both beams to serve as a landmark when you are lost in fog.
// -----------------------------------------------------------------------------

using LochNess.Boot;
using LochNess.Sim;
using UnityEngine;

namespace LochNess.World
{
    public static class LochBuilder
    {
        /// <summary>Same seed on every machine, or peers stand on different ground.</summary>
        public const uint TerrainSeed = 0x10C4E55u;

        // Basin dimensions in metres. The playable water is the ellipse; terrain
        // extends past it so there is a visible far shore rather than an edge.
        public const float BasinRadiusX = 300f;
        public const float BasinRadiusZ = 175f;
        public const float TerrainRadiusX = 520f;
        public const float TerrainRadiusZ = 380f;
        public const float MaxDepth = 62f;

        private const int GridX = 150;
        private const int GridZ = 110;

        /// <summary>Build terrain, water, sky and lighting under `parent`.</summary>
        public static void Build(Transform parent)
        {
            BuildTerrain(parent);
            BuildWater(parent);
            BuildAtmosphere(parent);
        }

        /// <summary>
        /// Bed height at a world position. Public because the monster uses it to
        /// avoid swimming through the lochbed, and spawn logic uses it to avoid
        /// placing anything inside a hillside.
        /// </summary>
        public static float SampleGround(float x, float z)
        {
            float nx = x / BasinRadiusX;
            float nz = z / BasinRadiusZ;
            float r = Mathf.Sqrt(nx * nx + nz * nz);

            // Large-scale roughness, sampled in world units so it is resolution-independent.
            float noise = SimMath.FractalNoise(x * 0.004f, z * 0.004f, TerrainSeed, 4, 2.1f, 0.5f);

            if (r < 1f)
            {
                // Underwater: cosine profile from full depth at the centre to zero at
                // the shoreline, roughened slightly so the bed is not a smooth bowl.
                float bed = -MaxDepth * Mathf.Cos(r * Mathf.PI * 0.5f);
                return bed + (noise - 0.5f) * 6f * (1f - r);
            }

            // Above water: climbing ground, steep at first then rolling into hills.
            float climb = (r - 1f);
            float hill = Mathf.Min(climb * 46f, 12f + climb * 22f);
            return hill + (noise - 0.5f) * 14f * Mathf.Min(1f, climb * 3f);
        }

        /// <summary>True if a point on the water plane is inside the playable basin.</summary>
        public static bool InsideBasin(float x, float z, float margin = 0f)
        {
            float nx = x / Mathf.Max(1f, BasinRadiusX - margin);
            float nz = z / Mathf.Max(1f, BasinRadiusZ - margin);
            return nx * nx + nz * nz <= 1f;
        }

        private static void BuildTerrain(Transform parent)
        {
            var go = new GameObject("Loch Bed");
            go.transform.SetParent(parent, false);

            int vertsX = GridX + 1;
            int vertsZ = GridZ + 1;
            var vertices = new Vector3[vertsX * vertsZ];
            var uvs = new Vector2[vertices.Length];

            for (int z = 0; z < vertsZ; z++)
            {
                for (int x = 0; x < vertsX; x++)
                {
                    float px = (x / (float)GridX - 0.5f) * TerrainRadiusX * 2f;
                    float pz = (z / (float)GridZ - 0.5f) * TerrainRadiusZ * 2f;
                    int i = z * vertsX + x;
                    vertices[i] = new Vector3(px, SampleGround(px, pz), pz);
                    uvs[i] = new Vector2(x / (float)GridX, z / (float)GridZ);
                }
            }

            // Three submeshes by height band. Cheaper and more legible than one
            // texture-splatted material, and it needs no imported textures at all.
            var deep = new System.Collections.Generic.List<int>();
            var shallow = new System.Collections.Generic.List<int>();
            var shore = new System.Collections.Generic.List<int>();

            for (int z = 0; z < GridZ; z++)
            {
                for (int x = 0; x < GridX; x++)
                {
                    int i = z * vertsX + x;
                    int a = i, b = i + vertsX, c = i + 1, d = i + vertsX + 1;

                    float mean = (vertices[a].y + vertices[b].y + vertices[c].y + vertices[d].y) * 0.25f;
                    var target = mean < -14f ? deep : (mean < 1.2f ? shallow : shore);

                    target.Add(a); target.Add(b); target.Add(c);
                    target.Add(c); target.Add(b); target.Add(d);
                }
            }

            var mesh = new Mesh { name = "loch-bed", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.subMeshCount = 3;
            mesh.SetTriangles(deep, 0);
            mesh.SetTriangles(shallow, 1);
            mesh.SetTriangles(shore, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[]
            {
                MeshKit.Solid(new Color(0.07f, 0.09f, 0.10f)),  // silt and rock, near black
                MeshKit.Solid(new Color(0.16f, 0.17f, 0.13f)),  // peaty margin
                MeshKit.Solid(new Color(0.20f, 0.22f, 0.16f))   // heather and bracken
            };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static void BuildWater(Transform parent)
        {
            var go = new GameObject("Loch Surface");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>();

            var renderer = go.AddComponent<MeshRenderer>();
            // Very dark and only slightly translucent. Loch Ness water is stained
            // near-black with peat; you genuinely cannot see a metre down. That is a
            // gameplay fact as much as an artistic one — it is why sonar exists.
            renderer.sharedMaterial = MeshKit.Translucent(new Color(0.035f, 0.075f, 0.085f, 0.94f), 0.92f);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            go.AddComponent<WaterSurface>();
        }

        private static void BuildAtmosphere(Transform parent)
        {
            // A low, cold sun sitting just above the hills. Late afternoon in the
            // Highlands in autumn: enough light to steer by, not enough to see into
            // the water.
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(parent, false);
            sunGo.transform.rotation = Quaternion.Euler(14f, 138f, 0f);

            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.68f);
            sun.intensity = 0.85f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.6f;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.40f, 0.46f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.26f, 0.29f);
            RenderSettings.ambientGroundColor = new Color(0.06f, 0.07f, 0.08f);

            // Fog is doing heavy lifting here. It hides the terrain's edge, sells the
            // scale of the loch, and — most importantly — makes the far shore useless
            // as a navigation aid, so the sonar and the compass are what you steer by.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.42f, 0.47f, 0.50f);
            RenderSettings.fogDensity = 0.0042f;

            var skybox = new Material(Shader.Find("Skybox/Procedural"));
            if (skybox.shader != null)
            {
                if (skybox.HasProperty("_SkyTint")) skybox.SetColor("_SkyTint", new Color(0.42f, 0.48f, 0.54f));
                if (skybox.HasProperty("_GroundColor")) skybox.SetColor("_GroundColor", new Color(0.18f, 0.19f, 0.18f));
                if (skybox.HasProperty("_AtmosphereThickness")) skybox.SetFloat("_AtmosphereThickness", 1.55f);
                if (skybox.HasProperty("_Exposure")) skybox.SetFloat("_Exposure", 0.85f);
                RenderSettings.skybox = skybox;
                RenderSettings.sun = sun;
            }
        }
    }
}
