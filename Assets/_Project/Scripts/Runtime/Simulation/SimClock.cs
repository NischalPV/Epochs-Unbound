using System;

namespace EpochsUnbound.Simulation
{
    /// <summary>
    /// Fixed-step simulation clock. Real time is accumulated (scaled by speed) and consumed in
    /// whole ticks of <see cref="TickSeconds"/>, so the simulation never sees a variable delta.
    /// </summary>
    public sealed class SimClock
    {
        public readonly double TickSeconds;
        public readonly int MaxStepsPerFrame;

        public long Tick { get; private set; }
        public bool Paused { get; set; }
        public float Speed { get; set; } = 1f;

        double _accumulator;

        public SimClock(int ticksPerSecond, int maxStepsPerFrame = 8)
        {
            if (ticksPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
            TickSeconds = 1.0 / ticksPerSecond;
            MaxStepsPerFrame = Math.Max(1, maxStepsPerFrame);
        }

        public double SimSeconds => Tick * TickSeconds;

        /// <summary>Fraction of the next tick already accumulated, for render interpolation.</summary>
        public double Alpha => _accumulator / TickSeconds;

        /// <summary>Feed one frame of real time. Excess beyond MaxStepsPerFrame ticks is dropped to avoid a death spiral.</summary>
        public void Accumulate(double realSeconds)
        {
            if (Paused || realSeconds <= 0) return;
            _accumulator = Math.Min(_accumulator + realSeconds * Speed, TickSeconds * MaxStepsPerFrame);
        }

        /// <summary>Consumes one tick if enough time has accumulated.</summary>
        public bool TryStep()
        {
            // Small epsilon so 1/20 s accumulated 20 times still yields 20 ticks despite float error.
            if (_accumulator + 1e-9 < TickSeconds) return false;
            _accumulator = Math.Max(0, _accumulator - TickSeconds);
            Tick++;
            return true;
        }
    }
}
