using System;
using EpochsUnbound.WorldGen;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.Settlement
{
    [Serializable]
    public sealed class BuildingDef
    {
        public string Name;
        public BuildingKind Kind;
        public int WoodCost;
        public Vector3 Size = new(8, 4, 8);
        public int Housing;
        public int Jobs;
        public ResourceKind Produces;
        [Tooltip("Output per worker per year before fertility / forest density.")]
        public float OutputPerWorkerPerYear;
        public Color Colour = Color.grey;
    }

    [CreateAssetMenu(menuName = "Epochs Unbound/Settlement Settings")]
    public sealed class SettlementSettings : ScriptableObject
    {
        [Header("Time")]
        [Tooltip("Sim ticks per game year. The sim runs 20 ticks/s at x1, so 8400 = a 7-minute year.")]
        public int TicksPerYear = 8400;
        [Tooltip("Sim ticks per day (1200 = 1 minute at x1); citizens work for WorkFraction of it.")]
        public int DayTicks = 1200;
        [Range(0.1f, 0.9f)] public float WorkFraction = 0.6f;

        [Header("Citizens (ages in life-years)")]
        [Tooltip("Citizens age this many life-years per game year. 7 with a 7-minute year = 1 life-year per minute at x1.")]
        [Min(0.01f)] public float LifeYearsPerGameYear = 7f;
        public int StartingCitizens = 10;
        public float AdultAge = 14f;
        public float LifespanMean = 60f;
        public float LifespanSpread = 12f;
        public float BirthsPerAdultPerYear = 0.2f;
        [Tooltip("Food eaten per citizen per year.")]
        public float FoodPerCitizenPerYear = 1f;
        [Tooltip("Births only happen while the store holds this many years of food per citizen.")]
        public float FoodReserveYears = 0.5f;
        [Tooltip("Years of starvation until death.")]
        public float StarveYears = 0.5f;
        public float WalkSpeed = 6f;

        [Header("Starting stock")]
        public float StartFood = 25f;
        public float StartWood = 200f;

        [Header("Developer")]
        [Tooltip("Enables F9: spawn 10,000 citizens and free food, ignoring housing and resources. For performance testing only.")]
        public bool DeveloperMode;

        [Header("Buildings")]
        public BuildingDef[] Buildings =
        {
            new() { Name = "Town Centre", Kind = BuildingKind.TownCentre, WoodCost = 0, Size = new(16, 8, 16), Housing = 10, Colour = new(0.75f, 0.68f, 0.55f) },
            new() { Name = "House", Kind = BuildingKind.House, WoodCost = 30, Size = new(6, 4, 6), Housing = 6, Colour = new(0.80f, 0.52f, 0.36f) },
            new() { Name = "Farm", Kind = BuildingKind.Farm, WoodCost = 40, Size = new(30, 0.6f, 30), Jobs = 5, Produces = ResourceKind.Food, OutputPerWorkerPerYear = 4f, Colour = new(0.85f, 0.78f, 0.35f) },
            new() { Name = "Lumber Camp", Kind = BuildingKind.LumberCamp, WoodCost = 40, Size = new(10, 4, 8), Jobs = 5, Produces = ResourceKind.Wood, OutputPerWorkerPerYear = 30f, Colour = new(0.45f, 0.30f, 0.18f) },
        };

        public BuildingDef Def(BuildingKind kind) => Array.Find(Buildings, b => b.Kind == kind);

        public SettlementRules ToRules()
        {
            float lifeYear = TicksPerYear / LifeYearsPerGameYear;
            return new SettlementRules
            {
            TicksPerYear = TicksPerYear,
            TicksPerLifeYear = lifeYear,
            AdultAgeTicks = (int)(AdultAge * lifeYear),
            LifespanMeanTicks = (int)(LifespanMean * lifeYear),
            LifespanSpreadTicks = (int)(LifespanSpread * lifeYear),
            BirthsPerAdultPerTick = BirthsPerAdultPerYear / TicksPerYear,
            FoodPerCitizenPerTick = FoodPerCitizenPerYear / TicksPerYear,
            FoodReservePerCitizen = FoodReserveYears * FoodPerCitizenPerYear,
            StarveHealthPerTick = 1f / (StarveYears * TicksPerYear),
            RecoverHealthPerTick = 2f / TicksPerYear,
            WalkSpeed = WalkSpeed,
            DayTicks = DayTicks,
            WorkFraction = WorkFraction,
            };
        }

        /// <summary>How well a site suits a building: farm fertility, lumber forest density, 1 otherwise. 0 = cannot build.</summary>
        public static float SiteYield(BuildingKind kind, float2 pos, in TerrainParams p)
        {
            switch (kind)
            {
                case BuildingKind.Farm:
                    return Fertility(BiomeAt(pos, p));
                case BuildingKind.LumberCamp:
                {
                    // Fraction of forest within ~80 m.
                    float forest = 0;
                    for (int z = -2; z <= 2; z++)
                    for (int x = -2; x <= 2; x++)
                    {
                        var b = BiomeAt(pos + new float2(x, z) * 40f, p);
                        forest += b == Biome.Forest ? 1f : b == Biome.Swamp || b == Biome.Tundra ? 0.4f : 0.1f;
                    }
                    forest /= 25f;
                    return forest < 0.2f ? 0f : forest;
                }
                default:
                    return 1f;
            }
        }

        public static float Fertility(Biome b) => b switch
        {
            Biome.Grassland => 1f,
            Biome.Savanna => 0.7f,
            Biome.Forest => 0.6f,
            Biome.Swamp => 0.5f,
            Biome.Beach => 0.25f,
            Biome.Tundra => 0.25f,
            Biome.Desert => 0.15f,
            _ => 0f,
        };

        static Biome BiomeAt(float2 pos, in TerrainParams p)
        {
            float h = TerrainSampler.GroundHeight(pos, p);
            return TerrainSampler.Classify(pos, h, 1f, p);
        }
    }
}
