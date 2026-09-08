// -----------------------------------------------------------------------------
// DeckBounds — where a crew member is allowed to stand, as pure geometry.
//
// WHY THIS ISN'T PHYSICS
// The obvious implementation is a CharacterController and box colliders. It does
// not work well here, for a specific reason: the deck is MOVING. Unity's physics
// queries run against a transform snapshot that is only synchronised at the
// physics step, so a capsule on a boat doing 9 m/s spends every frame resolving
// penetration against walls that have already moved. The symptoms are jitter at
// the rails and occasional expulsion through the transom.
//
// Since the deck is a known, fixed shape in the boat's local space, the honest
// solution is to clamp analytically in that space. It is exact, allocation-free,
// frame-rate independent, and identical on every peer — which also means a
// modified client cannot walk off the boat, because everyone runs this clamp.
//
// All coordinates are BOAT-LOCAL. See BoatBuilder for the matching geometry.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace LochNess.Vessel
{
    public static class DeckBounds
    {
        public const float SternLimit = -5.75f;  // aft bulkhead of the wheelhouse
        public const float BowLimit = 6.35f;     // inside of the stem
        public const float CabinFront = -1.85f;  // where the wheelhouse opening is

        private const float CabinHalfWidth = 1.58f;
        private const float DeckHalfWidth = 1.92f;
        private const float BowHalfWidth = 0.42f;
        private const float BowTaperStart = 3.4f;

        /// <summary>Half the walkable width at a given position along the boat.</summary>
        public static float HalfWidthAt(float z)
        {
            if (z <= CabinFront) return CabinHalfWidth;              // inside the wheelhouse
            if (z <= BowTaperStart) return DeckHalfWidth;            // open afterdeck
            return Mathf.Lerp(DeckHalfWidth, BowHalfWidth,           // hull narrows toward the stem
                              Mathf.InverseLerp(BowTaperStart, BowLimit, z));
        }

        /// <summary>
        /// Clamp a boat-local position onto the walkable deck, pushing out of the two
        /// consoles on the way. Y is forced to deck level: this slice has no falling,
        /// which removes a whole family of moving-platform bugs and costs nothing —
        /// the boat is railed all round.
        /// </summary>
        public static Vector3 Clamp(Vector3 local)
        {
            local.y = BoatBuilder.DeckY;
            local.z = Mathf.Clamp(local.z, SternLimit, BowLimit);

            float half = HalfWidthAt(local.z);
            local.x = Mathf.Clamp(local.x, -half, half);

            local = PushOutOfBox(local, new Vector2(0.35f, -2.65f), new Vector2(0.95f, 0.55f));  // helm console
            local = PushOutOfBox(local, new Vector2(-1.15f, -4.6f), new Vector2(0.75f, 0.62f));  // sonar console

            return local;
        }

        /// <summary>
        /// Eject a point from an axis-aligned obstacle along whichever axis is the
        /// shortest way out, so walking into a console slides you round it rather
        /// than stopping you dead.
        /// </summary>
        private static Vector3 PushOutOfBox(Vector3 local, Vector2 centre, Vector2 halfExtents)
        {
            float dx = local.x - centre.x;
            float dz = local.z - centre.y;
            if (Mathf.Abs(dx) >= halfExtents.x || Mathf.Abs(dz) >= halfExtents.y) return local;

            float outX = halfExtents.x - Mathf.Abs(dx);
            float outZ = halfExtents.y - Mathf.Abs(dz);

            if (outX < outZ) local.x = centre.x + Mathf.Sign(dx == 0f ? 1f : dx) * halfExtents.x;
            else local.z = centre.y + Mathf.Sign(dz == 0f ? 1f : dz) * halfExtents.y;

            return local;
        }

        /// <summary>True if the point is inside the wheelhouse — used to decide who can read the scope.</summary>
        public static bool InWheelhouse(Vector3 local) => local.z <= CabinFront + 0.2f;
    }
}
