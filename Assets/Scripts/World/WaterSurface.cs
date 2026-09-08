// -----------------------------------------------------------------------------
// WaterSurface — the loch's skin, and the single source of truth for wave height.
//
// The same function drives three things, which is why it lives in one place:
//   * the visible mesh,
//   * the boat's buoyancy (so the hull sits IN the swell rather than through it),
//   * the spray and wake spawn heights.
//
// TIME SOURCE
// Waves are a pure function of position and time, never replicated. For every peer
// to see the same swell under the same hull, they must agree on `time` — so when a
// session is running we use NetworkManager's synchronised server time and fall back
// to local time only on the title screen. Without this, a client's water visually
// disagrees with the replicated hull height by up to half a wave.
// -----------------------------------------------------------------------------

using Unity.Netcode;
using UnityEngine;

namespace LochNess.World
{
    [RequireComponent(typeof(MeshFilter))]
    public sealed class WaterSurface : MonoBehaviour
    {
        public static WaterSurface Instance { get; private set; }

        [Header("Extent")]
        [SerializeField] private float sizeX = 1100f;
        [SerializeField] private float sizeZ = 800f;
        [SerializeField] private int columns = 80;
        [SerializeField] private int rows = 56;

        [Header("Swell")]
        [Tooltip("Amplitude in metres of the three summed wave trains.")]
        [SerializeField] private float amplitudeA = 0.22f;
        [SerializeField] private float amplitudeB = 0.13f;
        [SerializeField] private float amplitudeC = 0.07f;

        private Mesh _mesh;
        private Vector3[] _vertices;
        private Vector3[] _basePositions;

        /// <summary>
        /// Wave clock. Prefers the session's synchronised time so every peer's swell
        /// matches; falls back to local time before a session exists.
        /// </summary>
        public static float WaveTime
        {
            get
            {
                var net = NetworkManager.Singleton;
                if (net != null && net.IsListening) return (float)net.ServerTime.Time;
                return Time.time;
            }
        }

        /// <summary>
        /// Surface height at a world position. Static and allocation-free — it is
        /// called several times per frame per floating object.
        /// </summary>
        public static float SampleHeight(float x, float z)
        {
            return Instance != null ? Instance.Evaluate(x, z, WaveTime) : 0f;
        }

        /// <summary>Approximate surface normal, from finite differences of the height field.</summary>
        public static Vector3 SampleNormal(float x, float z, float step = 2f)
        {
            float hL = SampleHeight(x - step, z);
            float hR = SampleHeight(x + step, z);
            float hD = SampleHeight(x, z - step);
            float hU = SampleHeight(x, z + step);
            return new Vector3(hL - hR, 2f * step, hD - hU).normalized;
        }

        private void Awake()
        {
            Instance = this;
            BuildGrid();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void BuildGrid()
        {
            _mesh = new Mesh { name = "loch-surface" };
            _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            int vertsX = columns + 1;
            int vertsZ = rows + 1;
            _vertices = new Vector3[vertsX * vertsZ];
            _basePositions = new Vector3[_vertices.Length];
            var uvs = new Vector2[_vertices.Length];
            var triangles = new int[columns * rows * 6];

            for (int z = 0; z < vertsZ; z++)
            {
                for (int x = 0; x < vertsX; x++)
                {
                    int i = z * vertsX + x;
                    float px = (x / (float)columns - 0.5f) * sizeX;
                    float pz = (z / (float)rows - 0.5f) * sizeZ;
                    _basePositions[i] = new Vector3(px, 0f, pz);
                    _vertices[i] = _basePositions[i];
                    uvs[i] = new Vector2(px * 0.05f, pz * 0.05f);
                }
            }

            int t = 0;
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int i = z * vertsX + x;
                    triangles[t++] = i;
                    triangles[t++] = i + vertsX;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + vertsX;
                    triangles[t++] = i + vertsX + 1;
                }
            }

            _mesh.vertices = _vertices;
            _mesh.uv = uvs;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        private void Update()
        {
            float time = WaveTime;

            // The grid is centred on the camera in XZ so a modest mesh covers the
            // whole visible loch; the wave function is evaluated in world space, so
            // sliding the grid does not slide the waves.
            Camera view = Camera.main;
            if (view != null)
            {
                Vector3 p = view.transform.position;
                transform.position = new Vector3(Mathf.Round(p.x / 10f) * 10f, 0f, Mathf.Round(p.z / 10f) * 10f);
            }

            Vector3 origin = transform.position;
            for (int i = 0; i < _vertices.Length; i++)
            {
                Vector3 basePos = _basePositions[i];
                _vertices[i] = new Vector3(
                    basePos.x, Evaluate(basePos.x + origin.x, basePos.z + origin.z, time), basePos.z);
            }

            _mesh.vertices = _vertices;
            _mesh.RecalculateNormals();
        }

        /// <summary>
        /// Three summed sine trains at different headings and wavelengths. Not a
        /// physically-correct spectrum — just enough irregularity that the surface
        /// never reads as a repeating pattern at the scales the player sees.
        /// </summary>
        private float Evaluate(float x, float z, float time)
        {
            float h = Mathf.Sin(x * 0.075f + time * 0.9f) * amplitudeA;
            h += Mathf.Sin(z * 0.11f - time * 0.65f) * amplitudeB;
            h += Mathf.Sin((x * 0.6f + z * 0.8f) * 0.05f + time * 1.4f) * amplitudeC;
            return h;
        }
    }
}
