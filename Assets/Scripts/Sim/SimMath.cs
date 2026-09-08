// -----------------------------------------------------------------------------
// SimMath — the scalar helpers the engine-free layer needs.
//
// These duplicate a handful of UnityEngine.Mathf members on purpose: the Sim
// namespace must not reference the engine (see Vec3.cs for why). Keeping them
// in one file makes the surface obvious to whoever ports this later.
// -----------------------------------------------------------------------------

using System;

namespace LochNess.Sim
{
    public static class SimMath
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);

        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Abs(float v) => v < 0f ? -v : v;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Sign(float v) => v < 0f ? -1f : 1f;
        public static float Sqrt(float v) => (float)Math.Sqrt(v < 0f ? 0f : v);

        /// <summary>
        /// Move `current` toward `target` by at most `maxDelta`. Used everywhere a
        /// value must ramp rather than snap (throttle, rudder, threat).
        /// </summary>
        public static float MoveToward(float current, float target, float maxDelta)
        {
            float diff = target - current;
            if (Abs(diff) <= maxDelta) return target;
            return current + Sign(diff) * maxDelta;
        }

        /// <summary>Smallest signed angle from `a` to `b`, in the range (-180, 180].</summary>
        public static float DeltaAngle(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }

        /// <summary>Wrap a bearing into 0..360.</summary>
        public static float WrapDegrees(float deg)
        {
            deg %= 360f;
            return deg < 0f ? deg + 360f : deg;
        }

        /// <summary>
        /// Deterministic 32-bit FNV-1a hash. Used for stable network prefab ids and
        /// for seeding terrain noise so every peer builds an identical loch.
        /// </summary>
        public static uint Fnv1a(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return hash;
            }
        }

        /// <summary>
        /// Value noise in two dimensions, seeded and deterministic across machines.
        /// Terrain must be identical on every peer (it is never replicated), so this
        /// cannot use System.Random or any platform-dependent float path.
        /// </summary>
        public static float Noise2D(float x, float y, uint seed)
        {
            int xi = (int)Math.Floor(x);
            int yi = (int)Math.Floor(y);
            float xf = x - xi;
            float yf = y - yi;

            // Smoothstep the fractional part so cell boundaries are not visible.
            float u = xf * xf * (3f - 2f * xf);
            float v = yf * yf * (3f - 2f * yf);

            float a = Hash2D(xi, yi, seed);
            float b = Hash2D(xi + 1, yi, seed);
            float c = Hash2D(xi, yi + 1, seed);
            float d = Hash2D(xi + 1, yi + 1, seed);

            return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
        }

        /// <summary>Sum of octaves of Noise2D — gives terrain both bulk and detail.</summary>
        public static float FractalNoise(float x, float y, uint seed, int octaves, float lacunarity, float gain)
        {
            float sum = 0f;
            float amp = 1f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Noise2D(x * freq, y * freq, seed + (uint)i * 7919u) * amp;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return norm > 0f ? sum / norm : 0f;
        }

        /// <summary>Integer hash to a float in 0..1. Pure integer maths, so bit-identical everywhere.</summary>
        private static float Hash2D(int x, int y, uint seed)
        {
            unchecked
            {
                uint h = seed;
                h ^= (uint)x * 374761393u;
                h ^= (uint)y * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
