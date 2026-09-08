// -----------------------------------------------------------------------------
// SimInterop — the only place Vec3 and UnityEngine.Vector3 meet.
//
// Keeping the conversion in one file means the boundary between the portable
// simulation and the engine is auditable: grep for these extensions and you have
// the complete list of places the two worlds touch.
// -----------------------------------------------------------------------------

using LochNess.Sim;
using UnityEngine;

namespace LochNess.Boot
{
    public static class SimInterop
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vec3 ToSim(this Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
