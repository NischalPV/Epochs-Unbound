using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.WorldGen
{
    [CreateAssetMenu(menuName = "Epochs Unbound/World Settings")]
    public sealed class WorldSettings : ScriptableObject
    {
        [Header("Generation")]
        public uint Seed = 12345;
        [Min(16)] public float ChunkSize = 256f;
        [Tooltip("Quads per chunk side.")]
        [Range(8, 254)] public int Resolution = 64;
        public float HeightScale = 260f;
        [Range(0f, 1f)] public float SeaLevel01 = 0.3f;
        public float Frequency = 0.0011f;
        [Range(1, 10)] public int Octaves = 6;
        public float MoistureFrequency = 0.0006f;

        [Header("Streaming")]
        [Min(1)] public int MinLoadRadius = 3;
        [Min(1)] public int MaxLoadRadius = 12;
        [Tooltip("Extra rings kept loaded beyond the load radius before unloading.")]
        [Min(0)] public int UnloadMargin = 2;
        [Min(1)] public int ChunksPerFrame = 8;

        public Material TerrainMaterial;

        public TerrainParams ToParams()
        {
            var rng = new Unity.Mathematics.Random(Seed == 0 ? 1u : Seed);
            return new TerrainParams
            {
                HeightOffset = rng.NextFloat2(-10000f, 10000f),
                MoistureOffset = rng.NextFloat2(-10000f, 10000f),
                Frequency = Frequency,
                MoistureFrequency = MoistureFrequency,
                HeightScale = HeightScale,
                SeaLevel01 = SeaLevel01,
                Octaves = Octaves,
            };
        }
    }
}
