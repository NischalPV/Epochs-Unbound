using EpochsUnbound.Simulation;
using EpochsUnbound.WorldGen;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace EpochsUnbound.Settlement
{
    // Tick order: Life -> Census -> Assignment -> Motion -> Economy -> Birth. All run on the fixed sim tick.

    /// <summary>Ageing, health and death (old age or starvation).</summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [BurstCompile]
    public partial struct CitizenLifeSystem : ISystem
    {
        EntityQuery _citizens;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Colony>();
            state.RequireForUpdate<SettlementRules>();
            _citizens = SystemAPI.QueryBuilder().WithAll<Citizen>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var starving = SystemAPI.GetSingleton<Colony>().Starving;
            int before = _citizens.CalculateEntityCount();
            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            new LifeJob
            {
                Rules = SystemAPI.GetSingleton<SettlementRules>(),
                Starving = starving,
                Ecb = ecb.AsParallelWriter(),
            }.ScheduleParallel(_citizens, state.Dependency).Complete();
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
            // Re-fetch after playback: structural changes invalidate earlier component references.
            SystemAPI.GetSingletonRW<Colony>().ValueRW.Deaths += before - _citizens.CalculateEntityCount();
        }

        [BurstCompile]
        partial struct LifeJob : IJobEntity
        {
            public SettlementRules Rules;
            public bool Starving;
            public EntityCommandBuffer.ParallelWriter Ecb;

            void Execute(Entity e, [ChunkIndexInQuery] int key, ref Citizen c)
            {
                c.AgeTicks++;
                c.Health = Starving ? c.Health - Rules.StarveHealthPerTick : math.min(1f, c.Health + Rules.RecoverHealthPerTick);
                if (c.AgeTicks >= c.LifespanTicks || c.Health <= 0f)
                    Ecb.DestroyEntity(key, e);
            }
        }
    }

    /// <summary>Recounts residents and workers per building and colony totals.</summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [UpdateAfter(typeof(CitizenLifeSystem))]
    [BurstCompile]
    public partial struct CensusSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Colony>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            foreach (var b in SystemAPI.Query<RefRW<Building>>())
                b.ValueRW.Residents = b.ValueRW.Workers = b.ValueRW.Present = 0;

            var rules = SystemAPI.GetSingleton<SettlementRules>();
            var buildings = SystemAPI.GetComponentLookup<Building>();
            int population = 0, adults = 0, employed = 0, homeless = 0;
            foreach (var c in SystemAPI.Query<RefRO<Citizen>>())
            {
                population++;
                if (c.ValueRO.AgeTicks >= rules.AdultAgeTicks) adults++;
                if (buildings.HasComponent(c.ValueRO.Home)) buildings.GetRefRW(c.ValueRO.Home).ValueRW.Residents++;
                else homeless++;
                if (buildings.HasComponent(c.ValueRO.Job))
                {
                    employed++;
                    ref var job = ref buildings.GetRefRW(c.ValueRO.Job).ValueRW;
                    job.Workers++;
                    if (c.ValueRO.Task == CitizenTask.Working) job.Present++;
                }
            }

            int housing = 0, jobs = 0;
            foreach (var b in SystemAPI.Query<RefRO<Building>>())
            {
                housing += b.ValueRO.Housing;
                jobs += b.ValueRO.JobSlots;
            }

            ref var colony = ref SystemAPI.GetSingletonRW<Colony>().ValueRW;
            colony.Population = population;
            colony.Adults = adults;
            colony.Employed = employed;
            colony.Homeless = homeless;
            colony.Housing = housing;
            colony.Jobs = jobs;
        }
    }

    /// <summary>
    /// Automatic half of the hybrid control model: homeless citizens get the nearest free home,
    /// idle adults fill the nearest open job slot. Citizens under a direct order are skipped.
    /// </summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [UpdateAfter(typeof(CensusSystem))]
    [BurstCompile]
    public partial struct AssignmentSystem : ISystem
    {
        EntityQuery _buildings;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<Colony>();
            _buildings = SystemAPI.QueryBuilder().WithAll<Building>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var colony = SystemAPI.GetSingleton<Colony>();
            int adultsWithoutJob = colony.Adults - colony.Employed;
            if (colony.Homeless == 0 && (adultsWithoutJob <= 0 || colony.Employed >= colony.Jobs)) return;
            bool housingFree = colony.Homeless > 0 && colony.Population - colony.Homeless < colony.Housing;
            bool jobsFree = colony.Employed < colony.Jobs;
            if (!housingFree && !jobsFree) return;

            var entities = _buildings.ToEntityArray(Allocator.Temp);
            var data = _buildings.ToComponentDataArray<Building>(Allocator.Temp);
            var freeHomes = new NativeArray<int>(data.Length, Allocator.Temp);
            var freeJobs = new NativeArray<int>(data.Length, Allocator.Temp);
            for (int i = 0; i < data.Length; i++)
            {
                freeHomes[i] = data[i].Housing - data[i].Residents;
                freeJobs[i] = data[i].JobSlots - data[i].Workers;
            }

            int adultAge = SystemAPI.GetSingleton<SettlementRules>().AdultAgeTicks;
            var buildingLookup = SystemAPI.GetComponentLookup<Building>(true);
            foreach (var (c, m) in SystemAPI.Query<RefRW<Citizen>, RefRO<CitizenMotion>>())
            {
                float3 pos = m.ValueRO.Position;
                if (housingFree && !buildingLookup.HasComponent(c.ValueRO.Home))
                {
                    int i = Nearest(pos, data, freeHomes);
                    if (i >= 0) { c.ValueRW.Home = entities[i]; freeHomes[i]--; }
                }
                if (jobsFree && c.ValueRO.AgeTicks >= adultAge && c.ValueRO.Task != CitizenTask.Ordered && !buildingLookup.HasComponent(c.ValueRO.Job))
                {
                    int i = Nearest(pos, data, freeJobs);
                    if (i >= 0) { c.ValueRW.Job = entities[i]; freeJobs[i]--; }
                }
            }
        }

        static int Nearest(float3 pos, NativeArray<Building> data, NativeArray<int> free)
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < data.Length; i++)
            {
                if (free[i] <= 0) continue;
                float d = math.distancesq(pos.xz, data[i].Position.xz);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
    }

    /// <summary>Daily routine: walk to work during work hours, home otherwise; direct orders override.</summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [UpdateAfter(typeof(AssignmentSystem))]
    [BurstCompile]
    public partial struct CitizenMotionSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<SettlementRules>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rules = SystemAPI.GetSingleton<SettlementRules>();
            long tick = SystemAPI.TryGetSingleton(out SimTime time) ? time.Tick : 0;
            bool hasTerrain = SystemAPI.TryGetSingleton(out TerrainConfig terrain);
            new MotionJob
            {
                Buildings = SystemAPI.GetComponentLookup<Building>(true),
                WorkHours = tick % rules.DayTicks < rules.DayTicks * rules.WorkFraction,
                Step = rules.WalkSpeed * SystemAPI.Time.DeltaTime,
                HasTerrain = hasTerrain,
                Terrain = terrain.Params,
            }.ScheduleParallel(state.Dependency).Complete();
        }

        [BurstCompile]
        partial struct MotionJob : IJobEntity
        {
            [ReadOnly] public ComponentLookup<Building> Buildings;
            public bool WorkHours, HasTerrain;
            public float Step;
            public TerrainParams Terrain;

            void Execute(ref Citizen c, ref CitizenMotion m)
            {
                m.Previous = m.Position;
                bool toWork = false, toHome = false;
                if (c.Task != CitizenTask.Ordered)
                {
                    Entity dest = Entity.Null;
                    if (WorkHours && Buildings.HasComponent(c.Job)) { dest = c.Job; toWork = true; }
                    else if (Buildings.HasComponent(c.Home)) { dest = c.Home; toHome = true; }

                    if (dest == Entity.Null) m.Target = m.Position;
                    else
                    {
                        var b = Buildings[dest];
                        var jitter = SimRandom.For(c.Id, 7).NextFloat2(-1f, 1f) * b.Radius * 0.7f;
                        m.Target = b.Position + new float3(jitter.x, 0, jitter.y);
                    }
                }

                float2 delta = m.Target.xz - m.Position.xz;
                float dist = math.length(delta);
                bool arrived = dist <= Step;
                if (dist > 0f)
                {
                    float2 xz = arrived ? m.Target.xz : m.Position.xz + delta / dist * Step;
                    float y = HasTerrain ? TerrainSampler.SurfaceHeight(xz, Terrain) : 0f;
                    m.Position = new float3(xz.x, y, xz.y);
                }

                if (c.Task == CitizenTask.Ordered) return;
                c.Task = toWork ? (arrived ? CitizenTask.Working : CitizenTask.ToWork)
                       : toHome ? (arrived ? CitizenTask.AtHome : CitizenTask.ToHome)
                       : CitizenTask.Idle;
            }
        }
    }

    /// <summary>Production by workers present at their building, food consumption, starvation flag.</summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [UpdateAfter(typeof(CitizenMotionSystem))]
    [BurstCompile]
    public partial struct EconomySystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Colony>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var rules = SystemAPI.GetSingleton<SettlementRules>();
            ref var colony = ref SystemAPI.GetSingletonRW<Colony>().ValueRW;
            foreach (var b in SystemAPI.Query<RefRO<Building>>())
            {
                float output = b.ValueRO.Present * b.ValueRO.OutputPerWorkerPerTick;
                if (b.ValueRO.Produces == ResourceKind.Food) colony.Food += output;
                else if (b.ValueRO.Produces == ResourceKind.Wood) colony.Wood += output;
            }
            float eaten = colony.Population * rules.FoodPerCitizenPerTick;
            colony.Starving = colony.Food < eaten;
            colony.Food = math.max(0f, colony.Food - eaten);
        }
    }

    /// <summary>Births, limited by free housing and a food reserve.</summary>
    [UpdateInGroup(typeof(SimTickSystemGroup))]
    [UpdateAfter(typeof(EconomySystem))]
    public partial struct BirthSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<Colony>();

        public void OnUpdate(ref SystemState state)
        {
            var rules = SystemAPI.GetSingleton<SettlementRules>();
            var colonyEntity = SystemAPI.GetSingletonEntity<Colony>();
            var colony = SystemAPI.GetComponent<Colony>(colonyEntity);
            var buildings = SystemAPI.GetComponentLookup<Building>(true);
            if (!buildings.HasComponent(colony.TownCentre)) return;

            int freeHousing = colony.Housing - colony.Population;
            bool fed = colony.Food >= colony.Population * rules.FoodReservePerCitizen && !colony.Starving;
            if (freeHousing <= 0 || !fed) return;

            colony.BirthProgress += colony.Adults * rules.BirthsPerAdultPerTick;
            float3 at = buildings[colony.TownCentre].Position;
            while (colony.BirthProgress >= 1f && freeHousing > 0)
            {
                colony.BirthProgress -= 1f;
                freeHousing--;
                colony.Births++;
                SettlementOps.SpawnCitizen(state.EntityManager, ref colony, rules, at, 0);
            }
            state.EntityManager.SetComponentData(colonyEntity, colony);
        }
    }
}
