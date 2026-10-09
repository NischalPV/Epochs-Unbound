using EpochsUnbound.Settlement;
using EpochsUnbound.WorldGen;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.Tests
{
    /// <summary>Placement rules on the real default terrain: costs, Town Centre limit, buildings resting on the ground.</summary>
    public class PlacementTests
    {
        World _world;
        SettlementSettings _settings;
        TerrainParams _terrain;
        float2 _spawn;
        EntityManager Em => _world.EntityManager;

        [SetUp]
        public void SetUp()
        {
            _world = new World("PlacementTest");
            _settings = ScriptableObject.CreateInstance<SettlementSettings>();
            var ws = ScriptableObject.CreateInstance<WorldSettings>();
            _terrain = ws.ToParams();
            Object.DestroyImmediate(ws);
            _spawn = TerrainSampler.FindSpawn(_terrain);
            SettlementOps.CreateColony(Em, _settings, _terrain);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
            Object.DestroyImmediate(_settings);
        }

        float Wood
        {
            get => SettlementOps.GetColony(Em).Wood;
            set
            {
                var c = SettlementOps.GetColony(Em);
                c.Wood = value;
                SettlementOps.SetColony(Em, c);
            }
        }

        /// <summary>Nearest spot to spawn where the building is allowed when money is no object.</summary>
        float3 FindSite(BuildingDef def)
        {
            float saved = Wood;
            Wood = 1e6f;
            try
            {
                for (int i = 0; i < 8000; i++)
                {
                    float a = i * 2.4f, r = 25f * math.sqrt(i);
                    var xz = _spawn + new float2(math.cos(a), math.sin(a)) * r;
                    var p = new float3(xz.x, TerrainSampler.SurfaceHeight(xz, _terrain), xz.y);
                    if (SettlementOps.CanPlace(Em, def, p, _terrain, out _) == SettlementOps.PlaceResult.Ok) return p;
                }
                Assert.Fail($"no site for {def.Name}");
                return default;
            }
            finally { Wood = saved; }
        }

        int BuildingCount()
        {
            using var q = Em.CreateEntityQuery(typeof(Building));
            return q.CalculateEntityCount();
        }

        Entity PlaceTownCentre()
        {
            var def = _settings.Def(BuildingKind.TownCentre);
            Assert.AreEqual(SettlementOps.PlaceResult.Ok, SettlementOps.TryPlace(Em, _settings, def, FindSite(def), _terrain, out var tc));
            return tc;
        }

        [Test]
        public void FirstTownCentreIsFreeAndASecondIsRefused()
        {
            float before = Wood;
            PlaceTownCentre();
            Assert.AreEqual(before, Wood);
            var def = _settings.Def(BuildingKind.TownCentre);
            var elsewhere = new float3(_spawn.x + 500f, 0, _spawn.y + 500f);
            Assert.AreEqual(SettlementOps.PlaceResult.OnlyOneTownCentre, SettlementOps.CanPlace(Em, def, elsewhere, _terrain, out _));
        }

        [TestCase(BuildingKind.House)]
        [TestCase(BuildingKind.Farm)]
        [TestCase(BuildingKind.LumberCamp)]
        public void CostIsCheckedAndDeducted(BuildingKind kind)
        {
            PlaceTownCentre();
            var def = _settings.Def(kind);
            Assert.Greater(def.WoodCost, 0);
            var site = FindSite(def);
            int count = BuildingCount();

            Wood = def.WoodCost - 1;
            Assert.AreEqual(SettlementOps.PlaceResult.NotEnoughWood, SettlementOps.TryPlace(Em, _settings, def, site, _terrain, out _));
            Assert.AreEqual(def.WoodCost - 1, Wood, "refused placement must not charge");
            Assert.AreEqual(count, BuildingCount());

            Wood = def.WoodCost + 5;
            Assert.AreEqual(SettlementOps.PlaceResult.Ok, SettlementOps.TryPlace(Em, _settings, def, site, _terrain, out _));
            Assert.AreEqual(5f, Wood, 1e-3f);
            Assert.AreEqual(count + 1, BuildingCount());
        }

        [Test]
        public void BuildingsRestOnTheTerrainWithoutFloating()
        {
            int checkedSites = 0;
            for (int i = 0; checkedSites < 40 && i < 4000; i++)
            {
                float a = i * 2.4f, r = 40f * math.sqrt(i);
                var xz = _spawn + new float2(math.cos(a), math.sin(a)) * r;
                if (TerrainSampler.GroundHeight(xz, _terrain) < 1f) continue;   // land only
                checkedSites++;
                foreach (var def in _settings.Buildings)
                {
                    var pos = new float3(xz.x, 0, xz.y);
                    SettlementOps.VisualExtent(def, pos, _terrain, out float bottom, out float top);
                    for (int z = 0; z <= 12; z++)
                    for (int x = 0; x <= 12; x++)
                    {
                        var p = xz + new float2(x / 12f - 0.5f, z / 12f - 0.5f) * new float2(def.Size.x, def.Size.z);
                        float ground = TerrainSampler.SurfaceHeight(p, _terrain);
                        Assert.LessOrEqual(bottom, ground + 0.05f, $"{def.Name} floats at {p}");
                        if (def.Kind != BuildingKind.Farm)
                            Assert.GreaterOrEqual(top, ground, $"{def.Name} buried at {p}");
                    }
                }
            }
            Assert.AreEqual(40, checkedSites);
        }

        [Test]
        public void DeveloperModeIsOffByDefault()
        {
            Assert.IsFalse(_settings.DeveloperMode);
        }
    }
}
