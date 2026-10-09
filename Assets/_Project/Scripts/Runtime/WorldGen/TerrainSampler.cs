using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.WorldGen
{
    /// <summary>Blittable copy of <see cref="WorldSettings"/> for Burst jobs.</summary>
    public struct TerrainParams
    {
        public float2 HeightOffset, MoistureOffset;
        public float Frequency, MoistureFrequency, HeightScale, SeaLevel01;
        public int Octaves;
    }

    /// <summary>Pure, Burst-compatible terrain functions. Sea level is y = 0.</summary>
    public static class TerrainSampler
    {
        /// <summary>Raw ground height in metres; negative values are under water.</summary>
        public static float GroundHeight(float2 world, in TerrainParams p)
        {
            float2 q = world * p.Frequency + p.HeightOffset;
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < p.Octaves; i++)
            {
                sum += noise.snoise(q) * amp;
                norm += amp;
                amp *= 0.5f;
                q *= 2.03f;
            }
            float e = math.saturate(sum / norm * 0.5f + 0.5f); // 0..1
            e = math.pow(e, 1.4f);                             // flatter lowlands, sharper peaks
            return (e - p.SeaLevel01) * p.HeightScale;
        }

        /// <summary>Visible surface height: ground or the water surface, whichever is higher.</summary>
        public static float SurfaceHeight(float2 world, in TerrainParams p) => math.max(0f, GroundHeight(world, p));

        public static Color32 BiomeColour(float2 world, float ground, in TerrainParams p)
        {
            if (ground < 0f)
            {
                float depth = math.saturate(-ground / (p.HeightScale * 0.25f));
                return Lerp(new float3(0.20f, 0.48f, 0.62f), new float3(0.05f, 0.16f, 0.35f), depth);
            }

            float h = ground / (p.HeightScale * (1f - p.SeaLevel01)); // 0 at sea level, 1 at max height
            float moisture = noise.snoise(world * p.MoistureFrequency + p.MoistureOffset) * 0.5f + 0.5f;

            if (h < 0.015f) return Lerp(new float3(0.82f, 0.77f, 0.56f), new float3(0.76f, 0.70f, 0.50f), moisture); // beach
            if (h < 0.35f)
            {
                var dry = new float3(0.66f, 0.62f, 0.36f);
                var grass = new float3(0.36f, 0.56f, 0.24f);
                var forest = new float3(0.16f, 0.36f, 0.16f);
                return moisture < 0.5f ? Lerp(dry, grass, moisture * 2f) : Lerp(grass, forest, moisture * 2f - 1f);
            }
            if (h < 0.55f) return Lerp(new float3(0.30f, 0.40f, 0.22f), new float3(0.45f, 0.40f, 0.33f), (h - 0.35f) / 0.2f); // hills
            if (h < 0.75f) return Lerp(new float3(0.45f, 0.40f, 0.33f), new float3(0.52f, 0.52f, 0.52f), (h - 0.55f) / 0.2f); // rock
            return Lerp(new float3(0.75f, 0.75f, 0.78f), new float3(0.96f, 0.96f, 0.98f), math.saturate((h - 0.75f) / 0.1f)); // snow
        }

        static Color32 Lerp(float3 a, float3 b, float t)
        {
            var c = math.lerp(a, b, math.saturate(t)) * 255f;
            return new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255);
        }
    }
}
