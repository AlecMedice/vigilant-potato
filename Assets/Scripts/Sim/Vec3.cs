// -----------------------------------------------------------------------------
// Vec3 — a minimal 3-component vector for the engine-free simulation layer.
//
// WHY THIS EXISTS
// Everything under Assets/Scripts/Sim is deliberately free of any `UnityEngine`
// reference. That constraint buys two things:
//
//   1. The interesting logic (sonar resolution, the monster's threat model and
//      state machine, hull dynamics) is unit-testable without booting an engine.
//   2. If this project is ever rebuilt in another engine, this layer is a
//      mechanical translation rather than a redesign. It is the part that will
//      absorb the most tuning time, so it is the part worth protecting.
//
// The Unity layer converts at the boundary via SimInterop (Assets/Scripts/Boot).
// The field order and handedness match Unity's convention (left-handed, Y up,
// +Z forward) so that conversion stays a straight component copy.
// -----------------------------------------------------------------------------

using System;

namespace LochNess.Sim
{
    public struct Vec3 : IEquatable<Vec3>
    {
        public float X;
        public float Y;
        public float Z;

        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public static Vec3 Zero => new Vec3(0f, 0f, 0f);
        public static Vec3 Up => new Vec3(0f, 1f, 0f);
        public static Vec3 Forward => new Vec3(0f, 0f, 1f);

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(float s, Vec3 a) => a * s;
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(X * X + Y * Y + Z * Z);

        /// <summary>Length ignoring Y. Most of this game reasons on the water plane.</summary>
        public float FlatMagnitude => (float)Math.Sqrt(X * X + Z * Z);

        public Vec3 Flat => new Vec3(X, 0f, Z);

        /// <summary>Unit vector, or Zero for a degenerate input (never NaN).</summary>
        public Vec3 Normalised
        {
            get
            {
                float m = Magnitude;
                return m > 1e-6f ? new Vec3(X / m, Y / m, Z / m) : Zero;
            }
        }

        public static float Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        /// <summary>Distance on the water plane — used wherever depth should not count.</summary>
        public static float FlatDistance(Vec3 a, Vec3 b) => (a - b).FlatMagnitude;

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            t = SimMath.Clamp01(t);
            return new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        /// <summary>
        /// Rotate about the Y axis. Cheap substitute for a full quaternion — the
        /// sim layer only ever needs heading changes, never arbitrary rotation.
        /// </summary>
        public static Vec3 RotateY(Vec3 v, float degrees)
        {
            float r = degrees * SimMath.Deg2Rad;
            float c = (float)Math.Cos(r);
            float s = (float)Math.Sin(r);
            return new Vec3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        /// <summary>Compass bearing of this vector: degrees clockwise from +Z, 0..360.</summary>
        public float Bearing
        {
            get
            {
                float deg = (float)Math.Atan2(X, Z) * SimMath.Rad2Deg;
                return deg < 0f ? deg + 360f : deg;
            }
        }

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => (X.GetHashCode() * 397) ^ (Y.GetHashCode() * 31) ^ Z.GetHashCode();
        public override string ToString() => $"({X:0.0}, {Y:0.0}, {Z:0.0})";
    }
}
