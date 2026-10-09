using EpochsUnbound.Simulation;
using NUnit.Framework;

namespace EpochsUnbound.Tests
{
    public class SimClockTests
    {
        static int Run(SimClock c, double seconds, int frames)
        {
            int ticks = 0;
            for (int f = 0; f < frames; f++)
            {
                c.Accumulate(seconds / frames);
                while (c.TryStep()) ticks++;
            }
            return ticks;
        }

        [Test]
        public void OneSecondAtTwentyHzIsTwentyTicks_RegardlessOfFrameRate()
        {
            Assert.AreEqual(20, Run(new SimClock(20), 1.0, 60));
            Assert.AreEqual(20, Run(new SimClock(20), 1.0, 144));
            Assert.AreEqual(20, Run(new SimClock(20), 1.0, 20));
        }

        [Test]
        public void PauseStopsTicks()
        {
            var c = new SimClock(20) { Paused = true };
            Assert.AreEqual(0, Run(c, 1.0, 60));
            Assert.AreEqual(0, c.Tick);
        }

        [Test]
        public void SpeedScalesTicks()
        {
            var c = new SimClock(20) { Speed = 4f };
            Assert.AreEqual(80, Run(c, 1.0, 60));
            Assert.AreEqual(4.0, c.SimSeconds, 1e-9);
        }

        [Test]
        public void LongFrameIsCappedAtMaxSteps()
        {
            var c = new SimClock(20, maxStepsPerFrame: 5);
            Assert.AreEqual(5, Run(c, 10.0, 1));
        }
    }
}
