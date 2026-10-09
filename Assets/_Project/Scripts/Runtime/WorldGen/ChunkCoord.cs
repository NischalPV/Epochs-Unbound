using Unity.Mathematics;

namespace EpochsUnbound.WorldGen
{
    /// <summary>Maths between world positions (XZ plane) and integer chunk coordinates.</summary>
    public static class ChunkCoord
    {
        public static int2 FromWorld(float x, float z, float chunkSize) =>
            (int2)math.floor(new float2(x, z) / chunkSize);

        public static float2 Origin(int2 chunk, float chunkSize) => (float2)chunk * chunkSize;

        /// <summary>Chebyshev (square ring) distance between two chunks.</summary>
        public static int Distance(int2 a, int2 b) => math.cmax(math.abs(a - b));
    }
}
