using System.Collections;
using System.Diagnostics;
using EpochsUnbound.CameraControl;
using EpochsUnbound.Settlement;
using EpochsUnbound.Simulation;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace EpochsUnbound.Tests
{
    public class SettlementPlayTests
    {
        [UnityTest]
        public IEnumerator TownCentreSettlesAndTenThousandCitizensRun()
        {
            SceneManager.LoadScene("Main");
            yield return null;
            yield return null;

            var controller = Object.FindAnyObjectByType<SettlementController>();
            var rig = Object.FindAnyObjectByType<RtsCamera>();
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var terrain = controller.World.ToParams();
            var tcDef = controller.Settings.Def(BuildingKind.TownCentre);

            // Place the town centre near the spawn point, the way a player would.
            var placed = SettlementOps.PlaceResult.Water;
            for (int i = 0; i < 400 && placed != SettlementOps.PlaceResult.Ok; i++)
            {
                float a = i * 2.4f, r = 10f * math.sqrt(i);
                var p = rig.FocusPoint + new float3(math.cos(a) * r, 0, math.sin(a) * r);
                placed = SettlementOps.TryPlace(em, controller.Settings, tcDef, p, terrain, out _);
            }
            Assert.AreEqual(SettlementOps.PlaceResult.Ok, placed, "no valid town centre site near spawn");

            // The town centre is a construction site: the starting citizens build it, then move in.
            var group0 = Unity.Entities.World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimTickSystemGroup>();
            group0.Clock.Speed = 8f;
            var colony = SettlementOps.GetColony(em);
            float deadline = Time.realtimeSinceStartup + 40f;
            while (!em.GetComponentData<Building>(colony.TownCentre).Built && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(em.GetComponentData<Building>(colony.TownCentre).Built, "starting citizens should finish the town centre");
            yield return Wait(0.5f);
            colony = SettlementOps.GetColony(em);
            Assert.AreEqual(controller.Settings.StartingCitizens, colony.Population);
            Assert.AreEqual(0, colony.Homeless, "starting citizens live in the finished town centre");

            // Stress test: 10k citizens at x8 speed.
            var tc = em.GetComponentData<Building>(colony.TownCentre);
            SettlementOps.SpawnAdults(em, 10000, tc.Position, 400f, wander: true);
            colony = SettlementOps.GetColony(em);
            colony.Food += 100000f;
            SettlementOps.SetColony(em, colony);
            var group = Unity.Entities.World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<SimTickSystemGroup>();
            group.Clock.Speed = 8f;
            yield return Wait(3f); // warm-up (Burst compilation, first frames)

            group.Clock.Paused = true;
            int pf = 0;
            var psw = Stopwatch.StartNew();
            while (psw.Elapsed.TotalSeconds < 2) { pf++; yield return null; }
            UnityEngine.Debug.Log($"[Perf] paused (render + UI only): {psw.Elapsed.TotalMilliseconds / pf:0.00} ms/frame");
            group.Clock.Paused = false;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            foreach (var sys in new[] { typeof(CitizenLifeSystem), typeof(CensusSystem), typeof(AssignmentSystem), typeof(CitizenMotionSystem), typeof(EconomySystem), typeof(BirthSystem) })
            {
                var h = world.GetExistingSystem(sys);
                var s1 = Stopwatch.StartNew();
                for (int k = 0; k < 20; k++) h.Update(world.Unmanaged);
                UnityEngine.Debug.Log($"[Perf] {sys.Name}: {s1.Elapsed.TotalMilliseconds / 20:0.000} ms/tick");
            }

            long ticks0 = group.Clock.Tick;
            int frames = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 5) { frames++; yield return null; }
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            long ticks = group.Clock.Tick - ticks0;
            UnityEngine.Debug.Log($"[Perf] {SettlementOps.GetColony(em).Population} citizens: {ms:0.00} ms/frame ({1000 / ms:0} fps), {ticks / sw.Elapsed.TotalSeconds:0} ticks/s at x8");
            Assert.Greater(SettlementOps.GetColony(em).Population, 10000);
        }

        static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }
    }
}
