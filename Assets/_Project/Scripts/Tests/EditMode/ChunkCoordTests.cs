using EpochsUnbound.WorldGen;
using NUnit.Framework;
using Unity.Mathematics;

namespace EpochsUnbound.Tests
{
    public class ChunkCoordTests
    {
        [TestCase(0f, 0f, 0, 0)]
        [TestCase(255.9f, 0f, 0, 0)]
        [TestCase(256f, 511.9f, 1, 1)]
        [TestCase(-0.1f, 0f, -1, 0)]
        [TestCase(-256f, -256.1f, -1, -2)]
        [TestCase(1_000_000f, -1_000_000f, 3906, -3907)]
        public void FromWorldFloorsTowardsNegativeInfinity(float x, float z, int cx, int cz)
        {
            Assert.AreEqual(new int2(cx, cz), ChunkCoord.FromWorld(x, z, 256f));
        }

        [Test]
        public void OriginRoundTrips()
        {
            var c = new int2(-7, 12);
            var o = ChunkCoord.Origin(c, 256f);
            Assert.AreEqual(c, ChunkCoord.FromWorld(o.x, o.y, 256f));
            Assert.AreEqual(c, ChunkCoord.FromWorld(o.x + 255f, o.y + 255f, 256f));
        }

        [Test]
        public void DistanceIsChebyshev()
        {
            Assert.AreEqual(3, ChunkCoord.Distance(new int2(0, 0), new int2(-3, 2)));
        }

        [Test]
        public void TerrainIsDeterministicForASeed()
        {
            var a = new TerrainParams { HeightOffset = 123f, Frequency = 0.001f, HeightScale = 200f, SeaLevel01 = 0.3f, Octaves = 5 };
            var p = new float2(1234.5f, -987.25f);
            Assert.AreEqual(TerrainSampler.GroundHeight(p, a), TerrainSampler.GroundHeight(p, a));
            Assert.GreaterOrEqual(TerrainSampler.SurfaceHeight(p, a), 0f);
        }
    }
}
