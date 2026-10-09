using System.Collections.Generic;
using EpochsUnbound.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.Tests
{
    public class TerrainTests
    {
        static TerrainParams Params(uint seed)
        {
            var s = ScriptableObject.CreateInstance<WorldSettings>();
            s.Seed = seed;
            var p = s.ToParams();
            Object.DestroyImmediate(s);
            return p;
        }

        // 200 km x 200 km sampled every 2 km.
        static IEnumerable<float2> Grid()
        {
            for (int z = -50; z < 50; z++)
            for (int x = -50; x < 50; x++)
                yield return new float2(x, z) * 2000f;
        }

        [Test]
        public void SameSeedSameTerrain_DifferentSeedDifferentTerrain()
        {
            var a = Params(1);
            var b = Params(2);
            var pos = new float2(1234.5f, -987.25f);
            Assert.AreEqual(TerrainSampler.GroundHeight(pos, a), TerrainSampler.GroundHeight(pos, Params(1)));

            int differing = 0;
            foreach (var g in Grid())
                if (math.abs(TerrainSampler.GroundHeight(g, a) - TerrainSampler.GroundHeight(g, b)) > 1f) differing++;
            Assert.Greater(differing, 5000);
        }

        [TestCase(12345u)]
        [TestCase(1u)]
        [TestCase(987654u)]
        public void SeedProducesOceansLandPlainsAndMountains(uint seed)
        {
            var p = Params(seed);
            int ocean = 0, land = 0, plains = 0, mountains = 0;
            foreach (var g in Grid())
            {
                float h = TerrainSampler.GroundHeight(g, p);
                if (h < 0) { ocean++; continue; }
                land++;
                if (h < p.PlainsHeight + p.HillHeight * 0.5f) plains++;
                if (h > p.MountainHeight * 0.3f) mountains++;
            }
            float oceanShare = ocean / 10000f;
            Assert.That(oceanShare, Is.InRange(0.2f, 0.8f), "ocean share");
            Assert.Greater(plains, land * 0.35f, "plains should be the common landform");
            Assert.Greater(mountains, 20, "expected some mountain ranges");
        }

        [TestCase(12345u)]
        [TestCase(1u)]
        public void SeedProducesManyBiomes(uint seed)
        {
            var p = Params(seed);
            var seen = new HashSet<Biome>();
            foreach (var g in Grid())
                seen.Add(TerrainSampler.Classify(g, TerrainSampler.GroundHeight(g, p), 1f, p));
            Assert.GreaterOrEqual(seen.Count, 7, "biomes seen: " + string.Join(", ", seen));
        }

        [TestCase(12345u)]
        [TestCase(1u)]
        public void ColdLowlandsAreTundraNotSnow(uint seed)
        {
            var p = Params(seed);
            int cold = 0;
            foreach (var g in Grid())
            {
                float h = TerrainSampler.GroundHeight(g, p);
                if (h < 3f || h > p.PlainsHeight) continue;
                TerrainSampler.Climate(g, h, p, out float t, out _);
                if (t >= 0.33f) continue;
                cold++;
                Assert.AreEqual(Biome.Tundra, TerrainSampler.Classify(g, h, 1f, p), $"at {g}");
                Color32 c = TerrainSampler.BiomeColour(g, h, 1f, p);
                Assert.Less((c.r + c.g + c.b) / 3f, 200f, $"lowland at {g} is near-white");
            }
            Assert.Greater(cold, 10, "expected some cold lowland");
        }

        [TestCase(12345u)]
        [TestCase(1u)]
        public void SpawnIsOnLandNearTheCoast(uint seed)
        {
            var p = Params(seed);
            var spawn = TerrainSampler.FindSpawn(p);
            Assert.IsTrue(TerrainSampler.IsGoodSpawn(spawn, p), $"no good spawn found, got {spawn}");
            Assert.Greater(TerrainSampler.GroundHeight(spawn, p), 0f);
        }
    }
}
