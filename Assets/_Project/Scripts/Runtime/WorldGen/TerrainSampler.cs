using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.WorldGen
{
    /// <summary>Blittable copy of <see cref="WorldSettings"/> for Burst jobs.</summary>
    public struct TerrainParams
    {
        public float2 ContinentOffset, WarpOffset, RangeOffset, RidgeOffset, DetailOffset, TemperatureOffset, MoistureOffset;
        public float ContinentFrequency, WarpFrequency, WarpStrength, RangeFrequency, RidgeFrequency, DetailFrequency, ClimateFrequency;
        public float OceanDepth, PlainsHeight, HillHeight, MountainHeight;
    }

    public enum Biome : byte { Ocean, Beach, Desert, Savanna, Grassland, Forest, Swamp, Tundra, Snow, Mountain }

    /// <summary>
    /// Pure, Burst-compatible terrain functions. Sea level is y = 0.
    /// Layers, largest first: domain-warped continents (oceans vs land), mountain-range regions with
    /// ridged chains, rolling-hill regions, otherwise plains. Climate (temperature, moisture) picks the biome.
    /// </summary>
    public static class TerrainSampler
    {
        /// <summary>Raw ground height in metres; negative values are under water.</summary>
        public static float GroundHeight(float2 world, in TerrainParams p)
        {
            float2 w = Warp(world, p);
            float c = Continentalness(w, p);

            if (c < 0f)
            {
                // Shallow shelf near the coast, deep ocean further out.
                float depth = math.pow(math.saturate(-c / 0.45f), 0.8f);
                return -p.OceanDepth * depth - 0.5f;
            }

            float inland = math.smoothstep(0f, 0.25f, c);
            float plains = inland * p.PlainsHeight * math.saturate(c * 2f);

            float hillMask = math.smoothstep(0.05f, 0.45f, Fbm(w * (p.RangeFrequency * 2.3f) + p.DetailOffset * 0.37f, 2));
            float hills = (Fbm(world * p.DetailFrequency + p.DetailOffset, 4) * 0.5f + 0.5f) * hillMask * inland * p.HillHeight;

            float rangeMask = math.smoothstep(0.2f, 0.5f, Fbm(w * p.RangeFrequency + p.RangeOffset, 3)) * math.smoothstep(0.08f, 0.3f, c);
            float mountains = Ridged(w * p.RidgeFrequency + p.RidgeOffset, 5) * rangeMask * p.MountainHeight;

            return 0.5f + plains + hills + mountains;
        }

        /// <summary>Visible surface height: ground or the water surface, whichever is higher.</summary>
        public static float SurfaceHeight(float2 world, in TerrainParams p) => math.max(0f, GroundHeight(world, p));

        /// <summary>Temperature and moisture in 0..1; temperature drops with altitude.</summary>
        public static void Climate(float2 world, float ground, in TerrainParams p, out float temperature, out float moisture)
        {
            temperature = math.saturate(Fbm(world * p.ClimateFrequency + p.TemperatureOffset, 3) * 1.2f + 0.58f)
                          - math.max(0f, ground) / p.MountainHeight * 0.7f;
            moisture = math.saturate(Fbm(world * (p.ClimateFrequency * 1.7f) + p.MoistureOffset, 3) * 1.3f + 0.5f);
        }

        /// <summary>Dominant biome at a point; <paramref name="upY"/> is the surface normal's Y (1 = flat).</summary>
        public static Biome Classify(float2 world, float ground, float upY, in TerrainParams p)
        {
            if (ground < 0f) return Biome.Ocean;
            Climate(world, ground, p, out float t, out float m);
            if (t < 0.06f && ground > p.MountainHeight * 0.2f) return Biome.Snow;
            if (upY < 0.8f || ground > p.MountainHeight * 0.45f) return Biome.Mountain;
            if (ground < 0.8f) return Biome.Beach;
            if (t < 0.33f) return Biome.Tundra;
            if (t < 0.66f) return m < 0.66f ? Biome.Grassland : Biome.Forest;
            return m < 0.33f ? Biome.Desert : m < 0.66f ? Biome.Savanna : Biome.Swamp;
        }

        /// <summary>Blended biome colour, so transitions between biomes are gradual.</summary>
        public static Color32 BiomeColour(float2 world, float ground, float upY, in TerrainParams p)
        {
            if (ground < 0f)
            {
                float depth = math.saturate(-ground / p.OceanDepth);
                // Seabed: sand in the shallows, dark silt deeper; the transparent water surface adds the blue.
                return ToColour(math.lerp(new float3(0.78f, 0.72f, 0.54f), new float3(0.16f, 0.2f, 0.2f), math.sqrt(depth)));
            }

            Climate(world, ground, p, out float t, out float m);

            // 3x3 climate table: rows cold / temperate / hot, columns dry / medium / wet.
            float3 cold = Lerp3(new float3(0.60f, 0.58f, 0.48f), new float3(0.45f, 0.50f, 0.38f), new float3(0.20f, 0.32f, 0.22f), m);
            float3 temperate = Lerp3(new float3(0.58f, 0.60f, 0.34f), new float3(0.40f, 0.58f, 0.25f), new float3(0.15f, 0.36f, 0.15f), m);
            float3 hot = Lerp3(new float3(0.88f, 0.77f, 0.52f), new float3(0.72f, 0.64f, 0.32f), new float3(0.27f, 0.35f, 0.20f), m);
            float3 col = Lerp3(cold, temperate, hot, t);

            col = math.lerp(col, new float3(0.86f, 0.80f, 0.60f), 1f - math.smoothstep(0.55f, 0.9f, ground));           // beach
            float rock = math.max(1f - math.smoothstep(0.72f, 0.88f, upY), math.smoothstep(0.35f, 0.5f, ground / p.MountainHeight));
            col = math.lerp(col, new float3(0.48f, 0.45f, 0.42f), rock);                                                 // rock
            col = math.lerp(col, new float3(0.95f, 0.96f, 0.98f), (1f - math.smoothstep(0.02f, 0.08f, t)) * math.smoothstep(0.12f, 0.25f, ground / p.MountainHeight)); // snow only on high ground; cold lowlands stay tundra
            // Patchy small-scale variation so large areas are not one flat colour.
            float patch = noise.snoise(world * 0.012f + p.DetailOffset * 3.1f) * 0.5f + noise.snoise(world * 0.05f + p.DetailOffset) * 0.25f;
            col *= 1f + patch * 0.12f;
            return ToColour(col);
        }

        /// <summary>Chance (0..1) of a tree on a 9 m cell, and whether it is a conifer. Forests follow climate.</summary>
        public static float TreeDensity(float2 world, float ground, in TerrainParams p, out bool pine)
        {
            pine = false;
            if (ground < 2f || ground > p.MountainHeight * 0.45f) return 0f;
            Climate(world, ground, p, out float t, out float m);
            if (t < 0.06f) return 0f;
            // Clumping: forests have glades, plains have the odd copse.
            float clump = math.saturate(noise.snoise(world * 0.006f + p.MoistureOffset * 1.7f) * 0.6f + 0.6f);
            if (t < 0.33f) { pine = true; return math.smoothstep(0.35f, 0.7f, m) * 0.75f * clump + 0.03f; }
            if (t < 0.66f)
            {
                pine = m > 0.8f && noise.snoise(world * 0.01f) > 0.3f;
                return math.smoothstep(0.5f, 0.75f, m) * 0.8f * clump + 0.02f;
            }
            return math.smoothstep(0.55f, 0.85f, m) * 0.5f * clump + 0.01f; // savanna: sparse; swamp: wetter, denser
        }

        /// <summary>
        /// Searches outward from the origin for flat land a short way from the sea, so the game opens on a coast.
        /// </summary>
        public static float2 FindSpawn(in TerrainParams p, float step = 500f, float maxRadius = 150_000f)
        {
            for (float r = 0; r <= maxRadius; r += step)
            {
                int samples = math.max(1, (int)(2 * math.PI * r / step));
                for (int i = 0; i < samples; i++)
                {
                    float a = i * 2f * math.PI / samples;
                    var pos = new float2(math.cos(a), math.sin(a)) * r;
                    if (IsGoodSpawn(pos, p)) return pos;
                }
            }
            return float2.zero;
        }

        public static bool IsGoodSpawn(float2 pos, in TerrainParams p)
        {
            float h = GroundHeight(pos, p);
            if (h < 3f || h > p.PlainsHeight + 10f) return false;
            Climate(pos, h, p, out float t, out _);
            if (t < 0.33f) return false;
            for (int d = 0; d < 8; d++)
            {
                float a = d * math.PI / 4f;
                if (GroundHeight(pos + new float2(math.cos(a), math.sin(a)) * 1500f, p) < 0f) return true;
            }
            return false;
        }

        /// <summary>Ray-marches the visible surface (water counts as surface). No colliders needed.</summary>
        public static bool Raycast(float3 origin, float3 dir, float maxDistance, in TerrainParams p, out float3 hit)
        {
            float prev = 0, t = 0;
            while (t < maxDistance)
            {
                float3 q = origin + dir * t;
                if (q.y <= SurfaceHeight(q.xz, p))
                {
                    for (int i = 0; i < 12; i++) // bisect between the last point above and the first below
                    {
                        float mid = (prev + t) * 0.5f;
                        float3 m = origin + dir * mid;
                        if (m.y <= SurfaceHeight(m.xz, p)) t = mid; else prev = mid;
                    }
                    hit = origin + dir * t;
                    return true;
                }
                prev = t;
                t += math.max(1f, t * 0.01f);
            }
            hit = default;
            return false;
        }

        static float2 Warp(float2 world, in TerrainParams p)
        {
            float2 q = world * p.WarpFrequency + p.WarpOffset;
            return world + p.WarpStrength * new float2(Fbm(q, 3), Fbm(q + new float2(31.7f, -17.3f), 3));
        }

        static float Continentalness(float2 warped, in TerrainParams p) =>
            Fbm(warped * p.ContinentFrequency + p.ContinentOffset, 4) * 1.4f + 0.06f;

        /// <summary>Fractal simplex noise, roughly -1..1.</summary>
        static float Fbm(float2 q, int octaves)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += noise.snoise(q) * amp;
                norm += amp;
                amp *= 0.5f;
                q = q * 2.03f + 7.1f;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal, 0..1: sharp crests along noise zero-lines give long mountain chains.</summary>
        static float Ridged(float2 q, int octaves)
        {
            float sum = 0, amp = 1, norm = 0, prev = 1;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - math.abs(noise.snoise(q));
                n *= n;
                sum += n * amp * prev;
                norm += amp;
                prev = n;
                amp *= 0.5f;
                q = q * 2.1f + 3.3f;
            }
            return sum / norm;
        }

        static float3 Lerp3(float3 a, float3 b, float3 c, float x)
        {
            x = math.saturate(x);
            return x < 0.5f ? math.lerp(a, b, math.smoothstep(0f, 1f, x * 2f)) : math.lerp(b, c, math.smoothstep(0f, 1f, x * 2f - 1f));
        }

        static Color32 ToColour(float3 c)
        {
            c = math.saturate(c) * 255f;
            return new Color32((byte)c.x, (byte)c.y, (byte)c.z, 255);
        }
    }
}
