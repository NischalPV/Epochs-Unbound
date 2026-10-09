using EpochsUnbound.Settlement;
using EpochsUnbound.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.Tests
{
    /// <summary>Runs the real settlement systems in an isolated world on the fixed tick (no terrain: flat y = 0).</summary>
    public class SettlementTests
    {
        World _world;
        SimTickSystemGroup _group;
        SettlementSettings _settings;
        EntityManager Em => _world.EntityManager;
        int Year => _settings.TicksPerYear;

        [SetUp]
        public void SetUp()
        {
            _world = new World("SettlementTest");
            _group = _world.GetOrCreateSystemManaged<SimTickSystemGroup>();
            foreach (var t in new[] { typeof(CitizenLifeSystem), typeof(CensusSystem), typeof(AssignmentSystem), typeof(CitizenMotionSystem), typeof(EconomySystem), typeof(BirthSystem) })
                _group.AddSystemToUpdateList(_world.CreateSystem(t));
            _group.SortSystems();
            _group.Clock = new SimClock(20, 1);
            _settings = ScriptableObject.CreateInstance<SettlementSettings>();
            _settings.TicksPerYear = 600;          // short year keeps tests fast; rules are per year so behaviour is the same
            _settings.LifeYearsPerGameYear = 1f;   // ages in game years, so timing assertions read simply
            SettlementOps.CreateColony(Em, _settings, null);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_settings);
        }

        void Tick(int n)
        {
            for (int i = 0; i < n; i++)
            {
                _world.SetTime(new TimeData(0, 0.05f));
                _group.Update();
            }
        }

        Colony Colony => SettlementOps.GetColony(Em);

        void SetFood(float food)
        {
            var c = Colony;
            c.Food = food;
            SettlementOps.SetColony(Em, c);
        }

        Entity Build(BuildingKind kind, float3 at)
        {
            var e = SettlementOps.CreateBuilding(Em, _settings.Def(kind), at, 1f, _settings.ToRules());
            if (kind == BuildingKind.TownCentre)
            {
                var c = Colony;
                c.TownCentre = e;
                SettlementOps.SetColony(Em, c);
            }
            return e;
        }

        /// <summary>Town centre at the origin with 10 adults (TC houses exactly 10).</summary>
        Entity StartColony(float food)
        {
            var tc = Build(BuildingKind.TownCentre, float3.zero);
            SettlementOps.SpawnAdults(Em, 10, float3.zero, 10f);
            SetFood(food);
            return tc;
        }

        [Test]
        public void DefaultTimingsAtX1()
        {
            var s = ScriptableObject.CreateInstance<SettlementSettings>();
            var r = s.ToRules();
            const int ticksPerMinute = 20 * 60;
            Assert.AreEqual(7f, s.TicksPerYear / (float)ticksPerMinute, "game year = 7 min");
            Assert.AreEqual(14f, r.AdultAgeTicks / (float)ticksPerMinute, 0.01f, "adult at life-age 14 = 14 min");
            Assert.AreEqual(60f, r.LifespanMeanTicks / (float)ticksPerMinute, 0.01f, "mean lifespan 60 = 1 hour");
            Assert.AreEqual(3.5f, 1f / r.StarveHealthPerTick / ticksPerMinute, 0.01f, "starvation kills in half a game year");
            Object.DestroyImmediate(s);
        }

        [Test]
        public void IdleAdultsFillJobQuotasExactly()
        {
            StartColony(100f);
            var farm = Build(BuildingKind.Farm, new float3(30, 0, 0));
            Tick(2);
            Assert.AreEqual(5, Em.GetComponentData<Building>(farm).Workers);
            Assert.AreEqual(5, Colony.Employed);
            Assert.AreEqual(10, Colony.Population);
            Assert.AreEqual(0, Colony.Homeless);
        }

        [Test]
        public void DirectMoveOrderOverridesAutoAssignment()
        {
            StartColony(100f);
            var farm = Build(BuildingKind.Farm, new float3(30, 0, 0));
            Tick(2);

            Entity worker = Entity.Null;
            using (var q = Em.CreateEntityQuery(typeof(Citizen)))
            using (var all = q.ToEntityArray(Allocator.Temp))
                foreach (var e in all)
                    if (Em.GetComponentData<Citizen>(e).Job == farm) { worker = e; break; }

            var target = new float3(-40, 0, 25);
            SettlementOps.OrderMove(Em, worker, target);
            Tick(Year / 2);

            var c = Em.GetComponentData<Citizen>(worker);
            Assert.AreEqual(CitizenTask.Ordered, c.Task);
            Assert.AreEqual(Entity.Null, c.Job, "ordered citizen must not be re-assigned automatically");
            Assert.Less(math.distance(Em.GetComponentData<CitizenMotion>(worker).Position, target), 0.01f);
            Assert.AreEqual(5, Em.GetComponentData<Building>(farm).Workers, "another idle adult fills the freed slot");

            Assert.IsTrue(SettlementOps.OrderWork(Em, worker, Build(BuildingKind.LumberCamp, new float3(0, 0, -30))));
            Tick(1);
            Assert.AreEqual(CitizenTask.ToWork, Em.GetComponentData<Citizen>(worker).Task);
        }

        [Test]
        public void FoodIsEatenAtOnePerCitizenPerYear()
        {
            StartColony(100f);
            Tick(Year);
            Assert.AreEqual(10, Colony.Population, "full housing: no births, adults too young to die");
            Assert.AreEqual(90f, Colony.Food, 0.05f);
        }

        [Test]
        public void FarmWorkersProduceMoreThanTheyEat()
        {
            StartColony(50f);
            Build(BuildingKind.Farm, new float3(20, 0, 0));
            Build(BuildingKind.Farm, new float3(-20, 0, 0));
            Tick(_settings.DayTicks);
            Assert.Greater(Colony.Food, 50f);
            Assert.IsFalse(Colony.Starving);
        }

        [Test]
        public void StarvationKillsAfterStarveYears()
        {
            StartColony(0f);
            int starveTicks = (int)(_settings.StarveYears * Year);
            Tick(starveTicks - 5);
            Assert.AreEqual(10, Colony.Population, "still alive just before the limit");
            Tick(10);
            Assert.AreEqual(0, Colony.Population);
            Assert.AreEqual(10, Colony.Deaths);
        }

        [Test]
        public void OldAgeKillsCitizens()
        {
            StartColony(100000f);
            Tick((int)((_settings.LifespanMean + _settings.LifespanSpread) * Year));
            Assert.AreEqual(10, Colony.Deaths, "every founder dies of old age within mean + spread");
        }

        [Test]
        public void BirthsFillFreeHousingAndStop()
        {
            StartColony(1000f);
            Build(BuildingKind.House, new float3(15, 0, 15));   // +6 housing
            for (int y = 0; y < 12; y++)
            {
                Tick(Year);
                Assert.LessOrEqual(Colony.Population, 16, "never more citizens than housing");
            }
            Assert.AreEqual(16, Colony.Population);
            Assert.AreEqual(6, Colony.Births);
        }

        [Test]
        public void NoBirthsWithoutAFoodReserve()
        {
            StartColony(2f);                                    // reserve needs 10 * 0.5 = 5
            Build(BuildingKind.House, new float3(15, 0, 15));
            Tick(Year / 6);
            Assert.AreEqual(0, Colony.Births);
        }

        [Test]
        public void SameInputsGiveIdenticalResults()
        {
            float3 Run()
            {
                StartColony(200f);
                Build(BuildingKind.House, new float3(15, 0, 15));
                Build(BuildingKind.Farm, new float3(-25, 0, 10));
                Tick(Year * 3);
                float3 sum = 0;
                using var q = Em.CreateEntityQuery(typeof(CitizenMotion));
                using var motions = q.ToComponentDataArray<CitizenMotion>(Allocator.Temp);
                foreach (var m in motions) sum += m.Position;
                return sum + new float3(Colony.Population, Colony.Food, Colony.Births);
            }

            var first = Run();
            TearDown();
            SetUp();
            var second = Run();
            // Tolerance only because the editor may run a job managed once and Burst-compiled the next time.
            Assert.Less(math.distance(first, second), 0.01f, $"{first} vs {second}");
        }
    }
}
