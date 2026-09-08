// -----------------------------------------------------------------------------
// QuarryBrain — the monster's threat model and state machine, without an engine.
//
// Three states, as specified: Wandering, Hiding, Fleeing.
//
// The design problem this solves is oscillation. A naive threat variable with a
// single threshold makes the monster flicker between states every few frames,
// which reads as a bug even when it is technically correct. Three guards fix it:
//
//   * SEPARATE ENTER AND EXIT THRESHOLDS (hysteresis) — it takes more threat to
//     start fleeing than it takes to keep fleeing.
//   * A MINIMUM DWELL TIME — no state may be abandoned in under `MinStateSeconds`,
//     regardless of threat.
//   * FLEEING ALWAYS RESOLVES INTO HIDING, never straight back to Wandering. After
//     a chase she goes to ground; she does not casually resume her rounds. This
//     also gives the players a reward for pushing her: a period where she is slow
//     and quiet and findable, if they guessed the right direction.
// -----------------------------------------------------------------------------

namespace LochNess.Sim
{
    public enum QuarryState : byte
    {
        Wandering = 0,
        Hiding = 1,
        Fleeing = 2
    }

    /// <summary>Tunables, all in one struct so they can be swept in a test harness.</summary>
    public struct QuarryTuning
    {
        public float AwarenessRadius;      // m — beyond this, hunters are not noticed at all
        public float ThreatDecayPerSecond; // how fast she calms down
        public float HideEnterThreshold;
        public float HideExitThreshold;
        public float FleeEnterThreshold;
        public float FleeExitThreshold;
        public float MinStateSeconds;      // dwell guard
        public float MaxFleeSeconds;       // a chase cannot last forever

        public float WanderSpeed;
        public float HideSpeed;
        public float FleeSpeed;

        public float WanderSignature;      // acoustic cross-section per state
        public float HideSignature;
        public float FleeSignature;

        /// <summary>The values the vertical slice ships with. Starting points, not gospel.</summary>
        public static QuarryTuning Default => new QuarryTuning
        {
            AwarenessRadius = 55f,
            ThreatDecayPerSecond = 0.16f,
            HideEnterThreshold = 0.35f,
            HideExitThreshold = 0.15f,
            FleeEnterThreshold = 0.75f,
            FleeExitThreshold = 0.45f,
            MinStateSeconds = 2.5f,
            MaxFleeSeconds = 9f,
            WanderSpeed = 3.5f,
            HideSpeed = 1.6f,
            FleeSpeed = 9f,
            WanderSignature = 0.75f,
            HideSignature = 0.3f,
            FleeSignature = 1f
        };
    }

    /// <summary>What the brain decided this tick. The engine layer turns this into motion.</summary>
    public struct QuarryDecision
    {
        public QuarryState State;
        public bool StateChanged;
        public float Threat;
        public float DesiredSpeed;
        public float Signature;
        /// <summary>Direction to run from, valid when Fleeing. Zero if no hunter is known.</summary>
        public Vec3 AwayFromThreat;
        /// <summary>True on the tick a chase times out — the engine layer picks a bolthole.</summary>
        public bool WantsNewHide;
    }

    public sealed class QuarryBrain
    {
        public QuarryTuning Tuning;

        public QuarryState State { get; private set; }
        public float Threat { get; private set; }
        /// <summary>Seconds spent in the current state. Drives the dwell guard.</summary>
        public float TimeInState { get; private set; }

        private Vec3 _lastThreatDirection;

        public QuarryBrain(QuarryTuning tuning)
        {
            Tuning = tuning;
            State = QuarryState.Wandering;
        }

        /// <summary>A ping heard at `distance` metres. Additive, so repeated pings stack.</summary>
        public void HearPing(float distance, float aggression)
        {
            Threat = SimMath.Clamp01(Threat + SonarModel.PingThreat(distance, Tuning.AwarenessRadius, aggression));
        }

        /// <summary>Direct provocation — being seen, being rammed, a spotlight on her back.</summary>
        public void Provoke(float amount)
        {
            Threat = SimMath.Clamp01(Threat + amount);
        }

        /// <summary>
        /// Advance one tick.
        /// </summary>
        /// <param name="position">Where the quarry is now.</param>
        /// <param name="hunters">Hunter positions. May be empty.</param>
        /// <param name="hunterCount">How many entries of `hunters` are valid.</param>
        /// <param name="dt">Delta time in seconds.</param>
        public QuarryDecision Tick(Vec3 position, Vec3[] hunters, int hunterCount, float dt)
        {
            TimeInState += dt;

            // ---- Threat decay, with a proximity floor ---------------------------
            // Decay alone lets the monster relax while a boat sits directly on top of
            // her, which looks stupid. So threat may never fall below a floor set by
            // the nearest hunter: being crowded keeps her nervous even in silence.
            Threat = SimMath.Max(0f, Threat - Tuning.ThreatDecayPerSecond * dt);

            float nearest = float.MaxValue;
            Vec3 nearestPos = Vec3.Zero;
            for (int i = 0; i < hunterCount; i++)
            {
                float d = Vec3.Distance(position, hunters[i]);
                if (d < nearest) { nearest = d; nearestPos = hunters[i]; }
            }

            bool haveHunter = nearest < float.MaxValue;
            if (haveHunter && nearest < Tuning.AwarenessRadius)
            {
                float proximityFloor = 1f - SimMath.Clamp01(nearest / Tuning.AwarenessRadius);
                if (proximityFloor > Threat) Threat = proximityFloor;
                _lastThreatDirection = (position - nearestPos).Normalised;
            }

            // ---- Transitions ----------------------------------------------------
            QuarryState previous = State;
            bool dwellSatisfied = TimeInState >= Tuning.MinStateSeconds;
            bool wantsNewHide = false;

            switch (State)
            {
                case QuarryState.Wandering:
                    if (Threat >= Tuning.FleeEnterThreshold) Enter(QuarryState.Fleeing);
                    else if (Threat >= Tuning.HideEnterThreshold) Enter(QuarryState.Hiding);
                    break;

                case QuarryState.Hiding:
                    if (Threat >= Tuning.FleeEnterThreshold) Enter(QuarryState.Fleeing);
                    else if (dwellSatisfied && Threat < Tuning.HideExitThreshold) Enter(QuarryState.Wandering);
                    break;

                case QuarryState.Fleeing:
                    // A chase is bounded: either she has calmed below the exit
                    // threshold, or she has simply run for long enough. Both land in
                    // Hiding rather than Wandering.
                    bool calmed = dwellSatisfied && Threat < Tuning.FleeExitThreshold;
                    bool exhausted = TimeInState >= Tuning.MaxFleeSeconds;
                    if (calmed || exhausted)
                    {
                        Enter(QuarryState.Hiding);
                        wantsNewHide = true;
                    }
                    break;
            }

            return new QuarryDecision
            {
                State = State,
                StateChanged = State != previous,
                Threat = Threat,
                DesiredSpeed = SpeedFor(State),
                Signature = SignatureFor(State),
                AwayFromThreat = _lastThreatDirection,
                WantsNewHide = wantsNewHide
            };
        }

        private void Enter(QuarryState next)
        {
            if (State == next) return;
            State = next;
            TimeInState = 0f;
        }

        private float SpeedFor(QuarryState s)
        {
            switch (s)
            {
                case QuarryState.Hiding: return Tuning.HideSpeed;
                case QuarryState.Fleeing: return Tuning.FleeSpeed;
                default: return Tuning.WanderSpeed;
            }
        }

        private float SignatureFor(QuarryState s)
        {
            switch (s)
            {
                case QuarryState.Hiding: return Tuning.HideSignature;
                case QuarryState.Fleeing: return Tuning.FleeSignature;
                default: return Tuning.WanderSignature;
            }
        }
    }
}
