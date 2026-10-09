using Unity.Core;
using Unity.Entities;

namespace EpochsUnbound.Simulation
{
    /// <summary>Singleton holding the current simulation tick.</summary>
    public struct SimTime : IComponentData
    {
        public long Tick;
    }

    /// <summary>
    /// All deterministic gameplay systems go in this group. It runs zero or more times per frame,
    /// once per <see cref="SimClock"/> tick, with SystemAPI.Time reporting the fixed tick delta.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class SimTickSystemGroup : ComponentSystemGroup
    {
        SimClockRateManager _rate;

        public SimClock Clock
        {
            get => _rate.Clock;
            set => _rate.Clock = value;
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            var time = EntityManager.CreateEntity(typeof(SimTime));
            EntityManager.SetName(time, "SimTime");
            _rate = new SimClockRateManager(new SimClock(20), time);
            RateManager = _rate;
        }

        sealed class SimClockRateManager : IRateManager
        {
            public SimClock Clock;
            readonly Entity _time;
            bool _inFrame, _pushedTime;

            public SimClockRateManager(SimClock clock, Entity time)
            {
                Clock = clock;
                _time = time;
            }

            public float Timestep
            {
                get => (float)Clock.TickSeconds;
                set => throw new System.NotSupportedException("Replace SimTickSystemGroup.Clock to change the tick rate.");
            }

            public bool ShouldGroupUpdate(ComponentSystemGroup group)
            {
                if (_pushedTime)
                {
                    group.World.PopTime();
                    _pushedTime = false;
                }

                if (!_inFrame)
                {
                    Clock.Accumulate(group.World.Time.DeltaTime);
                    _inFrame = true;
                }

                if (!Clock.TryStep())
                {
                    _inFrame = false;
                    return false;
                }

                group.World.PushTime(new TimeData(Clock.SimSeconds, (float)Clock.TickSeconds));
                _pushedTime = true;
                group.EntityManager.SetComponentData(_time, new SimTime { Tick = Clock.Tick });
                return true;
            }
        }
    }
}
