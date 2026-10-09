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

        [Header("Geography (frequencies are 1 / feature size in metres)")]
        [Tooltip("Continents and oceans. 0.000025 = features about 40 km across.")]
        public float ContinentFrequency = 0.000025f;
        [Tooltip("Domain warp that bends coastlines and ranges.")]
        public float WarpFrequency = 0.00008f;
        public float WarpStrength = 4000f;
        [Tooltip("Regions that have mountain ranges.")]
        public float RangeFrequency = 0.00006f;
        [Tooltip("Spacing of individual ridges inside a range.")]
        public float RidgeFrequency = 0.00025f;
        [Tooltip("Rolling hills and small bumps.")]
        public float DetailFrequency = 0.0015f;
        [Tooltip("Temperature and moisture zones.")]
        public float ClimateFrequency = 0.00002f;

        [Header("Heights (metres)")]
        public float OceanDepth = 200f;
        public float PlainsHeight = 30f;
        public float HillHeight = 60f;
        public float MountainHeight = 900f;

        [Header("Streaming")]
        [Min(1)] public int MinLoadRadius = 3;
        [Min(1)] public int MaxLoadRadius = 12;
        [Tooltip("Extra rings kept loaded beyond the load radius before unloading.")]
        [Min(0)] public int UnloadMargin = 2;
        [Min(1)] public int ChunksPerFrame = 8;

        [Tooltip("Trees are drawn for chunks within this many chunks of the camera focus.")]
        [Min(0)] public int TreeDrawRadius = 4;

        public Material TerrainMaterial;
        [Tooltip("Instancing-enabled lit material; the tree palette texture is applied at runtime.")]
        public Material TreeMaterial;
        public Material WaterMaterial;

        public TerrainParams ToParams()
        {
            var rng = new Unity.Mathematics.Random(Seed == 0 ? 1u : Seed);
            return new TerrainParams
            {
                ContinentOffset = rng.NextFloat2(-1000f, 1000f),
                WarpOffset = rng.NextFloat2(-1000f, 1000f),
                RangeOffset = rng.NextFloat2(-1000f, 1000f),
                RidgeOffset = rng.NextFloat2(-1000f, 1000f),
                DetailOffset = rng.NextFloat2(-1000f, 1000f),
                TemperatureOffset = rng.NextFloat2(-1000f, 1000f),
                MoistureOffset = rng.NextFloat2(-1000f, 1000f),
                ContinentFrequency = ContinentFrequency,
                WarpFrequency = WarpFrequency,
                WarpStrength = WarpStrength,
                RangeFrequency = RangeFrequency,
                RidgeFrequency = RidgeFrequency,
                DetailFrequency = DetailFrequency,
                ClimateFrequency = ClimateFrequency,
                OceanDepth = OceanDepth,
                PlainsHeight = PlainsHeight,
                HillHeight = HillHeight,
                MountainHeight = MountainHeight,
            };
        }
    }
}
