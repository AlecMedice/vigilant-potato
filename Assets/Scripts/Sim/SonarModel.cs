// -----------------------------------------------------------------------------
// SonarModel — how a sonar ping turns the world into a handful of blips.
//
// This is the heart of the game's uncertainty. The player never sees the monster
// on the scope; they see a *contact* whose bearing is roughly right, whose range
// is wrong in proportion to how weak the return is, and which may be a shoal of
// fish or a sunken tree. Making the honest reading of a bad contact into a skill
// is the whole point of the loop, so this model is deliberately noisy.
//
// Engine-free by design — see Vec3.cs.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace LochNess.Sim
{
    public enum ContactKind : byte
    {
        /// <summary>A false return: fish, weed, a drowned tree. Never scoreable.</summary>
        Clutter = 0,
        /// <summary>The quarry, but too faint to be sure of. Not scoreable.</summary>
        Anomaly = 1,
        /// <summary>The quarry, close and clear enough to log as a confirmed sighting.</summary>
        Confirmed = 2
    }

    /// <summary>One blip as it appears on the scope. Bearing/range are what the
    /// *operator* sees, which is not necessarily where the quarry actually is.</summary>
    public struct Contact
    {
        public float Bearing;   // degrees clockwise from world +Z, 0..360
        public float Range;     // metres from the ping origin, as reported
        public float Depth;     // metres below the surface, as reported (0 = at surface)
        public float Clarity;   // 0..1 — signal strength; drives brightness and confidence
        public ContactKind Kind;
    }

    /// <summary>Everything the resolver needs to know about the thing being hunted.</summary>
    public struct QuarrySnapshot
    {
        public Vec3 Position;
        /// <summary>0..1 acoustic cross-section. The monster's behaviour changes this:
        /// hiding against the lochbed returns far less than a full-speed run.</summary>
        public float Signature;
        public bool Exists;
    }

    public static class SonarModel
    {
        /// <summary>Below this clarity a return is indistinguishable from noise and is dropped.</summary>
        public const float NoiseFloor = 0.08f;

        /// <summary>
        /// Resolve a single ping.
        ///
        /// Called on the server ONLY. Clients receive the resulting contacts over the
        /// wire and never learn the quarry's true transform from this path — that is
        /// what stops the scope from becoming a wallhack.
        /// </summary>
        /// <param name="origin">World position of the transducer.</param>
        /// <param name="range">Maximum useful range of the set, in metres.</param>
        /// <param name="quarry">The monster, if one is alive.</param>
        /// <param name="confirmClarity">Clarity at or above which a return counts as a sighting.</param>
        /// <param name="confirmRange">Range at or below which a return may count as a sighting.</param>
        /// <param name="clutterCount">How many false returns this ping should invent.</param>
        /// <param name="rng">Caller-owned RNG so results are reproducible in tests.</param>
        /// <param name="results">Cleared and filled with the contacts. Never null.</param>
        /// <returns>True if this ping produced a confirmable sighting.</returns>
        public static bool Resolve(
            Vec3 origin,
            float range,
            QuarrySnapshot quarry,
            float confirmClarity,
            float confirmRange,
            int clutterCount,
            Random rng,
            List<Contact> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();

            bool confirmed = false;
            float strongestClutter = 0f;

            // ---- The real return ------------------------------------------------
            if (quarry.Exists)
            {
                float distance = Vec3.Distance(origin, quarry.Position);
                if (distance <= range)
                {
                    // Quadratic falloff. Linear felt far too generous in testing: the
                    // monster stayed legible right out to the edge of the set, which
                    // removed any reason to close the distance.
                    float falloff = 1f - SimMath.Clamp01(distance / range);
                    falloff *= falloff;

                    float clarity = SimMath.Clamp01(falloff * quarry.Signature);

                    // A little jitter so repeated pings on a stationary target do not
                    // return an identical number — that would read as a lock-on.
                    clarity = SimMath.Clamp01(clarity * (0.85f + 0.3f * (float)rng.NextDouble()));

                    if (clarity >= NoiseFloor)
                    {
                        Vec3 toQuarry = quarry.Position - origin;
                        bool scoreable = clarity >= confirmClarity && distance <= confirmRange;

                        // Bearing error and range error both scale with UNCERTAINTY
                        // (1 - clarity), so a strong return is trustworthy and a faint
                        // one sends you to the wrong patch of water.
                        float uncertainty = 1f - clarity;
                        float bearingError = (float)(rng.NextDouble() - 0.5) * 12f * uncertainty;
                        float rangeError = 1f + (float)(rng.NextDouble() - 0.5) * 0.2f * uncertainty;

                        results.Add(new Contact
                        {
                            Bearing = SimMath.WrapDegrees(toQuarry.Bearing + bearingError),
                            Range = distance * rangeError,
                            Depth = SimMath.Max(0f, -quarry.Position.Y),
                            Clarity = clarity,
                            Kind = scoreable ? ContactKind.Confirmed : ContactKind.Anomaly
                        });

                        confirmed = scoreable;
                        strongestClutter = clarity;
                    }
                }
            }

            // ---- Decoys ---------------------------------------------------------
            // Clutter is capped strictly below the real return (and below the confirm
            // threshold) so that a false contact can waste your time but can never be
            // mistaken for a confirmation. Without that cap the scope stops meaning
            // anything and players learn to ignore it.
            float clutterCeiling = SimMath.Min(confirmClarity - 0.05f, SimMath.Max(0.12f, strongestClutter - 0.05f));

            for (int i = 0; i < clutterCount; i++)
            {
                float clarity = 0.1f + (float)rng.NextDouble() * (clutterCeiling - 0.1f);
                if (clarity < NoiseFloor) continue;

                results.Add(new Contact
                {
                    Bearing = (float)rng.NextDouble() * 360f,
                    // Biased outward: near-field clutter is rarer and more alarming.
                    Range = range * (0.25f + (float)rng.NextDouble() * 0.7f),
                    Depth = (float)rng.NextDouble() * 40f,
                    Clarity = clarity,
                    Kind = ContactKind.Clutter
                });
            }

            return confirmed;
        }

        /// <summary>
        /// How loud a ping is to the thing being hunted. Pinging is not free: it is
        /// the main way the player gives their position away, which is what makes
        /// "should I ping again?" an actual decision.
        /// </summary>
        /// <param name="distance">Quarry's distance from the transducer.</param>
        /// <param name="awarenessRadius">Beyond this the ping is inaudible.</param>
        /// <param name="aggression">Scales how much threat one ping contributes.</param>
        public static float PingThreat(float distance, float awarenessRadius, float aggression)
        {
            if (distance >= awarenessRadius) return 0f;
            float proximity = 1f - SimMath.Clamp01(distance / awarenessRadius);
            return proximity * proximity * aggression;
        }
    }
}
