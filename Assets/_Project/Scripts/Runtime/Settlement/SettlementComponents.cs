using EpochsUnbound.WorldGen;
using Unity.Entities;
using Unity.Mathematics;

namespace EpochsUnbound.Settlement
{
    public enum BuildingKind : byte { TownCentre, House, Farm, LumberCamp }
    public enum ResourceKind : byte { None, Food, Wood }

    public enum CitizenTask : byte
    {
        Idle,       // no job, or off duty with nowhere to go
        ToWork,
        Working,
        ToHome,
        AtHome,
        Ordered,    // player gave a direct move order; auto-assignment leaves them alone
    }

    /// <summary>Tuning copied from <see cref="SettlementSettings"/>. Rates are already per tick.</summary>
    public struct SettlementRules : IComponentData
    {
        public int TicksPerYear;
        public int AdultAgeTicks;
        public int LifespanMeanTicks, LifespanSpreadTicks;
        public float BirthsPerAdultPerTick;
        public float FoodPerCitizenPerTick;
        public float FoodReservePerCitizen;      // births need at least this much food per citizen in store
        public float StarveHealthPerTick, RecoverHealthPerTick;
        public float WalkSpeed;
        public int DayTicks;
        public float WorkFraction;
    }

    /// <summary>Colony-wide state. Singleton.</summary>
    public struct Colony : IComponentData
    {
        public float Food, Wood;
        public int Population, Adults, Housing, Jobs, Employed, Homeless;
        public bool Starving;
        public float BirthProgress;
        public uint NextCitizenId;
        public int Births, Deaths;
        public Entity TownCentre;
    }

    /// <summary>Terrain the simulation can sample (for citizen heights). Singleton, optional.</summary>
    public struct TerrainConfig : IComponentData
    {
        public TerrainParams Params;
    }

    public struct Building : IComponentData
    {
        public BuildingKind Kind;
        public float3 Position;
        public float Radius;
        public int Housing, JobSlots;
        public ResourceKind Produces;
        public float OutputPerWorkerPerTick;     // fertility / forest density already applied
        // Recounted every tick by CensusSystem.
        public int Residents, Workers, Present;
    }

    public struct Citizen : IComponentData
    {
        public uint Id;
        public int AgeTicks, LifespanTicks;
        public float Health;                     // 0..1
        public Entity Home, Job;
        public CitizenTask Task;
    }

    public struct CitizenMotion : IComponentData
    {
        public float3 Position, Previous, Target;
    }

    /// <summary>Hash-based deterministic randomness, independent of iteration order and threads.</summary>
    public static class SimRandom
    {
        public static Random For(uint id, uint salt) => new Random(math.hash(new uint2(id, salt)) | 1u);
    }
}
