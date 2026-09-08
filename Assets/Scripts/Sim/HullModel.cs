// -----------------------------------------------------------------------------
// HullModel — how a small displacement boat answers her helm.
//
// A boat is not a car, and the difference is the whole feel of driving one:
//
//   * A rudder does nothing without water flowing past it. Stop the engine and
//     you keep your heading whatever you do with the wheel. This is modelled by
//     scaling turn authority with speed, floored at `StationaryRudder` so that
//     backing-and-filling still works at a crawl.
//   * Going astern REVERSES the steering. Reverse a boat with the rudder over and
//     the stern walks the other way. Players who have handled a real boat expect
//     this; players who have not learn it in about ten seconds and enjoy it.
//   * There is no brake. Cutting the throttle means coasting to a stop against
//     drag, which is what makes creeping up on a contact take patience.
//
// Engine-free by design — see Vec3.cs.
// -----------------------------------------------------------------------------

namespace LochNess.Sim
{
    public struct HullTuning
    {
        public float MaxForwardSpeed;  // m/s at full ahead
        public float MaxReverseSpeed;  // m/s at full astern
        public float Acceleration;     // m/s^2 under power
        public float PassiveDrag;      // m/s^2 of deceleration with the engine at idle
        public float TurnRateDegrees;  // deg/s at full rudder authority
        public float FullRudderSpeed;  // speed at which the rudder reaches full authority
        public float StationaryRudder; // fraction of authority retained at a dead stop

        public static HullTuning Default => new HullTuning
        {
            MaxForwardSpeed = 9f,
            MaxReverseSpeed = 3f,
            Acceleration = 6f,
            PassiveDrag = 3f,
            TurnRateDegrees = 55f,
            FullRudderSpeed = 4f,
            StationaryRudder = 0.25f
        };
    }

    /// <summary>Mutable hull state. One of these per vessel, owned by the server.</summary>
    public struct HullState
    {
        public float Speed;         // m/s along the hull's forward axis; negative is astern
        public float HeadingDegrees; // compass heading, 0..360
    }

    public static class HullModel
    {
        /// <summary>
        /// Integrate one tick of helm input.
        /// </summary>
        /// <param name="state">Current hull state, updated in place.</param>
        /// <param name="tuning">Handling characteristics.</param>
        /// <param name="throttle">-1 (full astern) .. +1 (full ahead).</param>
        /// <param name="rudder">-1 (hard a-port) .. +1 (hard a-starboard).</param>
        /// <param name="dt">Delta time in seconds.</param>
        /// <returns>World-space displacement for this tick.</returns>
        public static Vec3 Step(ref HullState state, HullTuning tuning, float throttle, float rudder, float dt)
        {
            throttle = SimMath.Clamp(throttle, -1f, 1f);
            rudder = SimMath.Clamp(rudder, -1f, 1f);

            // ---- Speed ----------------------------------------------------------
            // Note the single call: an earlier version applied drag and then also
            // decelerated toward zero when input was released, which double-braked
            // the boat and made her feel like she was dragging an anchor.
            float targetSpeed = throttle >= 0f
                ? throttle * tuning.MaxForwardSpeed
                : throttle * tuning.MaxReverseSpeed;

            float rate = SimMath.Abs(throttle) > 0.01f ? tuning.Acceleration : tuning.PassiveDrag;
            state.Speed = SimMath.MoveToward(state.Speed, targetSpeed, rate * dt);

            // ---- Heading --------------------------------------------------------
            float flow = SimMath.Clamp01(SimMath.Abs(state.Speed) / SimMath.Max(0.01f, tuning.FullRudderSpeed));
            float authority = SimMath.Lerp(tuning.StationaryRudder, 1f, flow);
            float direction = state.Speed < -0.05f ? -1f : 1f; // stern walks the other way astern

            state.HeadingDegrees = SimMath.WrapDegrees(
                state.HeadingDegrees + rudder * tuning.TurnRateDegrees * authority * direction * dt);

            // ---- Displacement ---------------------------------------------------
            Vec3 forward = Vec3.RotateY(Vec3.Forward, state.HeadingDegrees);
            return forward * (state.Speed * dt);
        }

        /// <summary>
        /// Keep a vessel inside the loch. The basin is an ellipse; rather than
        /// stopping the boat dead at the shore (which feels like hitting glass) this
        /// bleeds off speed and nudges the position back inside, so grounding reads
        /// as running into shallows.
        /// </summary>
        public static void ConstrainToBasin(ref Vec3 position, ref HullState state, float radiusX, float radiusZ, float margin)
        {
            float rx = SimMath.Max(1f, radiusX - margin);
            float rz = SimMath.Max(1f, radiusZ - margin);
            float nx = position.X / rx;
            float nz = position.Z / rz;
            float d = nx * nx + nz * nz;
            if (d <= 1f) return;

            float scale = 1f / SimMath.Sqrt(d);
            position = new Vec3(position.X * scale, position.Y, position.Z * scale);
            state.Speed *= 0.4f; // grounding scrubs way off
        }
    }
}
