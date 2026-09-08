// -----------------------------------------------------------------------------
// MeshKit — procedural geometry and materials.
//
// WHY THE WHOLE GAME IS BUILT FROM CODE
// This project ships no .prefab or .asset files, and that is deliberate rather
// than lazy. Unity prefabs and scenes are YAML full of GUID references to
// package-internal scripts; they can only be authored correctly from inside the
// Editor. Anything generated blind would fail to import, and a broken prefab
// fails *silently* (a missing script, an empty slot) in a way that is miserable
// to debug.
//
// C#, by contrast, can be compiled and checked without an Editor. So every mesh,
// material, prefab and UI element in this game is constructed at runtime. The
// cost is that nothing is artist-editable yet; the benefit is that `git clone`
// followed by Play produces an actual game.
//
// All meshes are flat-shaded (no shared vertices across faces) which suits the
// stylised look and keeps the normal maths trivial.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace LochNess.Boot
{
    public static class MeshKit
    {
        // Materials are cached by colour so the whole loch shares a handful of them.
        private static readonly Dictionary<int, Material> MaterialCache = new Dictionary<int, Material>();
        private static Shader _opaqueShader;
        private static Shader _transparentShader;

        /// <summary>
        /// The project runs on the built-in render pipeline on purpose: URP without a
        /// configured pipeline asset renders every object magenta, and a pipeline
        /// asset is exactly the kind of file that cannot be authored outside the
        /// Editor. "Standard" is always present.
        /// </summary>
        private static Shader OpaqueShader
        {
            get
            {
                if (_opaqueShader == null)
                {
                    _opaqueShader = Shader.Find("Standard")
                                    ?? Shader.Find("Diffuse")
                                    ?? Shader.Find("Legacy Shaders/Diffuse");
                }
                return _opaqueShader;
            }
        }

        private static Shader TransparentShader
        {
            get
            {
                if (_transparentShader == null)
                {
                    _transparentShader = Shader.Find("Standard")
                                         ?? Shader.Find("Legacy Shaders/Transparent/Diffuse");
                }
                return _transparentShader;
            }
        }

        /// <summary>An opaque material of the given colour, shared between callers.</summary>
        public static Material Solid(Color colour, float smoothness = 0.1f, float metallic = 0f)
        {
            int key = colour.GetHashCode() ^ (Mathf.RoundToInt(smoothness * 100f) << 8) ^ (Mathf.RoundToInt(metallic * 100f) << 16);
            if (MaterialCache.TryGetValue(key, out Material cached) && cached != null) return cached;

            Material m = new Material(OpaqueShader);
            m.color = colour;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            MaterialCache[key] = m;
            return m;
        }

        /// <summary>
        /// A translucent material. Setting up alpha blending on the Standard shader
        /// requires poking the render state directly — the shader's "Rendering Mode"
        /// dropdown is an Editor-only convenience that just sets these values.
        /// </summary>
        public static Material Translucent(Color colour, float smoothness = 0.85f)
        {
            Material m = new Material(TransparentShader);
            m.color = colour;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.1f);
            if (m.HasProperty("_Mode"))
            {
                m.SetFloat("_Mode", 3f); // Transparent
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.EnableKeyword("_ALPHABLEND_ON");
                m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            return m;
        }

        /// <summary>An unlit, always-visible material — used for lamps and the scope glow.</summary>
        public static Material Emissive(Color colour)
        {
            Material m = new Material(OpaqueShader);
            m.color = colour;
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", colour * 1.6f);
            }
            return m;
        }

        // ---------------------------------------------------------------------
        // Primitives
        // ---------------------------------------------------------------------

        /// <summary>An axis-aligned box centred on the origin, flat shaded.</summary>
        public static Mesh Box(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] c =
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3( h.x, -h.y, -h.z),
                new Vector3( h.x, -h.y,  h.z), new Vector3(-h.x, -h.y,  h.z),
                new Vector3(-h.x,  h.y, -h.z), new Vector3( h.x,  h.y, -h.z),
                new Vector3( h.x,  h.y,  h.z), new Vector3(-h.x,  h.y,  h.z)
            };

            var builder = new Builder(24, 36);
            builder.Quad(c[0], c[1], c[2], c[3]); // bottom (wound for a downward normal)
            builder.Quad(c[7], c[6], c[5], c[4]); // top
            builder.Quad(c[4], c[5], c[1], c[0]); // -Z
            builder.Quad(c[6], c[7], c[3], c[2]); // +Z
            builder.Quad(c[7], c[4], c[0], c[3]); // -X
            builder.Quad(c[5], c[6], c[2], c[1]); // +X
            return builder.Build("box");
        }

        /// <summary>A capped cylinder along the Y axis, centred on the origin.</summary>
        public static Mesh Cylinder(float radius, float height, int segments = 12)
        {
            var b = new Builder(segments * 12, segments * 18);
            float half = height * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);

                b.Quad(p0 + Vector3.up * half, p1 + Vector3.up * half,
                       p1 + Vector3.down * half, p0 + Vector3.down * half);
                b.Tri(Vector3.up * half, p1 + Vector3.up * half, p0 + Vector3.up * half);
                b.Tri(Vector3.down * half, p0 + Vector3.down * half, p1 + Vector3.down * half);
            }
            return b.Build("cylinder");
        }

        /// <summary>A UV sphere scaled per-axis — the basis of Nessie's body segments.</summary>
        public static Mesh Ellipsoid(Vector3 radii, int rings = 8, int segments = 12)
        {
            var b = new Builder(rings * segments * 4, rings * segments * 6);
            for (int r = 0; r < rings; r++)
            {
                float t0 = r / (float)rings;
                float t1 = (r + 1) / (float)rings;
                float phi0 = t0 * Mathf.PI;
                float phi1 = t1 * Mathf.PI;

                for (int s = 0; s < segments; s++)
                {
                    float u0 = (s / (float)segments) * Mathf.PI * 2f;
                    float u1 = ((s + 1) / (float)segments) * Mathf.PI * 2f;
                    b.Quad(OnSphere(phi0, u0, radii), OnSphere(phi0, u1, radii),
                           OnSphere(phi1, u1, radii), OnSphere(phi1, u0, radii));
                }
            }
            return b.Build("ellipsoid");
        }

        private static Vector3 OnSphere(float phi, float theta, Vector3 radii) => new Vector3(
            Mathf.Sin(phi) * Mathf.Cos(theta) * radii.x,
            Mathf.Cos(phi) * radii.y,
            Mathf.Sin(phi) * Mathf.Sin(theta) * radii.z);

        /// <summary>
        /// Loft a closed shape through a series of cross-sections along +Z. This is
        /// what turns a list of "beam and depth at this station" numbers into a hull
        /// with a pointed bow and a transom, rather than a floating crate.
        /// </summary>
        /// <param name="stations">z position, half-beam, top height, bottom depth.</param>
        public static Mesh Loft(IReadOnlyList<Vector4> stations)
        {
            var b = new Builder(stations.Count * 24, stations.Count * 36);

            for (int i = 0; i < stations.Count - 1; i++)
            {
                Vector4 a = stations[i];
                Vector4 c = stations[i + 1];

                // Each station is a trapezoid: (-beam..+beam) x (bottom..top).
                Vector3 aTL = new Vector3(-a.y, a.z, a.x), aTR = new Vector3(a.y, a.z, a.x);
                Vector3 aBL = new Vector3(-a.y, a.w, a.x), aBR = new Vector3(a.y, a.w, a.x);
                Vector3 cTL = new Vector3(-c.y, c.z, c.x), cTR = new Vector3(c.y, c.z, c.x);
                Vector3 cBL = new Vector3(-c.y, c.w, c.x), cBR = new Vector3(c.y, c.w, c.x);

                b.Quad(aTL, aTR, cTR, cTL); // deck / sheer
                b.Quad(cBL, cBR, aBR, aBL); // bottom
                b.Quad(aBL, aTL, cTL, cBL); // port
                b.Quad(cBR, cTR, aTR, aBR); // starboard
            }

            // Cap the ends so the hull is watertight when seen from outside.
            Vector4 first = stations[0];
            Vector4 last = stations[stations.Count - 1];
            b.Quad(new Vector3(-first.y, first.w, first.x), new Vector3(first.y, first.w, first.x),
                   new Vector3(first.y, first.z, first.x), new Vector3(-first.y, first.z, first.x));
            b.Quad(new Vector3(-last.y, last.z, last.x), new Vector3(last.y, last.z, last.x),
                   new Vector3(last.y, last.w, last.x), new Vector3(-last.y, last.w, last.x));

            return b.Build("hull");
        }

        // ---------------------------------------------------------------------
        // Assembly helpers
        // ---------------------------------------------------------------------

        /// <summary>Create a child GameObject carrying a mesh, with no collider.</summary>
        public static GameObject Part(Transform parent, string name, Mesh mesh, Material material,
                                      Vector3 localPosition, Quaternion localRotation = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation == default ? Quaternion.identity : localRotation;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        /// <summary>
        /// Incremental mesh builder. Every face gets its own vertices so that normals
        /// are per-face; sharing vertices would smooth the hull chines away.
        /// </summary>
        private sealed class Builder
        {
            private readonly List<Vector3> _vertices;
            private readonly List<int> _triangles;
            private readonly List<Vector2> _uvs;

            public Builder(int vertexHint, int triangleHint)
            {
                _vertices = new List<Vector3>(vertexHint);
                _triangles = new List<int>(triangleHint);
                _uvs = new List<Vector2>(vertexHint);
            }

            public void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c);
                _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f)); _uvs.Add(new Vector2(0.5f, 1f));
                _triangles.Add(i); _triangles.Add(i + 1); _triangles.Add(i + 2);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = _vertices.Count;
                _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
                _uvs.Add(new Vector2(0f, 0f)); _uvs.Add(new Vector2(1f, 0f));
                _uvs.Add(new Vector2(1f, 1f)); _uvs.Add(new Vector2(0f, 1f));
                _triangles.Add(i); _triangles.Add(i + 1); _triangles.Add(i + 2);
                _triangles.Add(i); _triangles.Add(i + 2); _triangles.Add(i + 3);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name };
                if (_vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(_vertices);
                mesh.SetUVs(0, _uvs);
                mesh.SetTriangles(_triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
