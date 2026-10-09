using EpochsUnbound.WorldGen;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace EpochsUnbound.Settlement
{
    /// <summary>Main-thread operations on the settlement: setup, placement, spawning and player orders.</summary>
    // ponytail: player commands apply immediately; queue them onto the next sim tick when lockstep multiplayer or replays need it.
    public static class SettlementOps
    {
        /// <summary>Starts a new colony, clearing any previous one (the ECS world outlives scene loads).</summary>
        public static Entity CreateColony(EntityManager em, SettlementSettings settings, TerrainParams? terrain)
        {
            foreach (var type in new[] { ComponentType.ReadOnly<Colony>(), ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<Building>() })
            {
                using var old = em.CreateEntityQuery(type);
                em.DestroyEntity(old);
            }

            var e = em.CreateEntity();
            em.SetName(e, "Colony");
            em.AddComponentData(e, settings.ToRules());
            em.AddComponentData(e, new Colony { Food = settings.StartFood, Wood = settings.StartWood, NextCitizenId = 1 });
            if (terrain.HasValue) em.AddComponentData(e, new TerrainConfig { Params = terrain.Value });
            return e;
        }

        static T Single<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            return q.GetSingleton<T>();
        }

        public static Colony GetColony(EntityManager em) => Single<Colony>(em);

        public static void SetColony(EntityManager em, Colony c)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Colony>());
            em.SetComponentData(q.GetSingletonEntity(), c);
        }

        public static Entity SpawnCitizen(EntityManager em, ref Colony colony, in SettlementRules rules, float3 at, int ageTicks)
        {
            var e = em.CreateEntity(typeof(Citizen), typeof(CitizenMotion));
            InitCitizen(em, e, ref colony, rules, at, ageTicks);
            return e;
        }

        static void InitCitizen(EntityManager em, Entity e, ref Colony colony, in SettlementRules rules, float3 at, int ageTicks)
        {
            uint id = colony.NextCitizenId++;
            var rng = SimRandom.For(id, 1);
            int lifespan = rules.LifespanMeanTicks + (int)(rng.NextFloat(-1f, 1f) * rules.LifespanSpreadTicks);
            em.SetComponentData(e, new Citizen
            {
                Id = id,
                AgeTicks = ageTicks,
                LifespanTicks = math.max(lifespan, ageTicks + (int)rules.TicksPerLifeYear),
                Health = 1f,
            });
            var p = at + new float3(rng.NextFloat(-6f, 6f), 0, rng.NextFloat(-6f, 6f));
            em.SetComponentData(e, new CitizenMotion { Position = p, Previous = p, Target = p });
        }

        /// <summary>Spawns adults around a point in bulk (starting citizens, debug stress test).
        /// With <paramref name="wander"/> each walks to a random point, which exercises movement and terrain sampling.</summary>
        public static void SpawnAdults(EntityManager em, int count, float3 at, float spread, bool wander = false)
        {
            var colony = GetColony(em);
            var rules = Single<SettlementRules>(em);
            var entities = em.CreateEntity(em.CreateArchetype(typeof(Citizen), typeof(CitizenMotion)), count, Allocator.Temp);
            foreach (var e in entities)
            {
                var rng = SimRandom.For(colony.NextCitizenId, 2);
                int age = rules.AdultAgeTicks + rng.NextInt(0, (int)(10 * rules.TicksPerLifeYear));
                var offset = rng.NextFloat2Direction() * math.sqrt(rng.NextFloat()) * spread;
                InitCitizen(em, e, ref colony, rules, at + new float3(offset.x, 0, offset.y), age);
                if (wander) OrderMove(em, e, at + new float3(rng.NextFloat(-spread, spread), 0, rng.NextFloat(-spread, spread)));
            }
            SetColony(em, colony);
        }

        public enum PlaceResult { Ok, NeedTownCentre, OnlyOneTownCentre, NotEnoughWood, Water, TooSteep, Overlaps, PoorSite }

        public static PlaceResult CanPlace(EntityManager em, BuildingDef def, float3 pos, in TerrainParams terrain, out float siteYield)
        {
            siteYield = 0;
            var colony = GetColony(em);
            bool hasTc = em.Exists(colony.TownCentre);
            if (def.Kind == BuildingKind.TownCentre && hasTc) return PlaceResult.OnlyOneTownCentre;
            if (def.Kind != BuildingKind.TownCentre && !hasTc) return PlaceResult.NeedTownCentre;
            if (colony.Wood < def.WoodCost) return PlaceResult.NotEnoughWood;

            // Footprint must be dry and roughly flat.
            float hx = def.Size.x * 0.5f, hz = def.Size.z * 0.5f;
            float min = float.MaxValue, max = float.MinValue;
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
            {
                float h = TerrainSampler.GroundHeight(pos.xz + new float2(x * hx, z * hz), terrain);
                if (h < 0.5f) return PlaceResult.Water;
                min = math.min(min, h);
                max = math.max(max, h);
            }
            if (max - min > math.max(1.5f, math.max(def.Size.x, def.Size.z) * 0.12f)) return PlaceResult.TooSteep;

            float radius = Radius(def);
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<Building>());
            using var buildings = q.ToComponentDataArray<Building>(Allocator.Temp);
            foreach (var b in buildings)
                if (math.distance(b.Position.xz, pos.xz) < b.Radius + radius) return PlaceResult.Overlaps;

            siteYield = SettlementSettings.SiteYield(def.Kind, pos.xz, terrain);
            return siteYield <= 0f ? PlaceResult.PoorSite : PlaceResult.Ok;
        }

        public static float Radius(BuildingDef def) => math.length(new float2(def.Size.x, def.Size.z)) * 0.5f;

        /// <summary>Validates, pays and creates the building. A town centre also brings the starting citizens.</summary>
        public static PlaceResult TryPlace(EntityManager em, SettlementSettings settings, BuildingDef def, float3 pos, in TerrainParams terrain, out Entity building)
        {
            building = Entity.Null;
            var result = CanPlace(em, def, pos, terrain, out float siteYield);
            if (result != PlaceResult.Ok) return result;

            building = CreateBuilding(em, def, pos, siteYield, Single<SettlementRules>(em));
            var colony = GetColony(em);
            colony.Wood -= def.WoodCost;
            if (def.Kind == BuildingKind.TownCentre) colony.TownCentre = building;
            SetColony(em, colony);

            if (def.Kind == BuildingKind.TownCentre)
                SpawnAdults(em, settings.StartingCitizens, pos, def.Size.x);
            return PlaceResult.Ok;
        }

        public static Entity CreateBuilding(EntityManager em, BuildingDef def, float3 pos, float siteYield, in SettlementRules rules)
        {
            var e = em.CreateEntity();
            em.SetName(e, def.Name);
            em.AddComponentData(e, new Building
            {
                Kind = def.Kind,
                Position = pos,
                Radius = Radius(def),
                Housing = def.Housing,
                JobSlots = def.Jobs,
                Produces = def.Produces,
                OutputPerWorkerPerTick = def.OutputPerWorkerPerYear * siteYield / rules.TicksPerYear,
            });
            return e;
        }

        /// <summary>Direct order: walk to a point and stay there (leaves any job).</summary>
        public static void OrderMove(EntityManager em, Entity citizen, float3 to)
        {
            if (!em.HasComponent<Citizen>(citizen)) return;
            var c = em.GetComponentData<Citizen>(citizen);
            c.Task = CitizenTask.Ordered;
            c.Job = Entity.Null;
            em.SetComponentData(citizen, c);
            var m = em.GetComponentData<CitizenMotion>(citizen);
            m.Target = to;
            em.SetComponentData(citizen, m);
        }

        /// <summary>Direct order: work at a building. Fails if it has no free job slot or the citizen is a child.</summary>
        public static bool OrderWork(EntityManager em, Entity citizen, Entity building)
        {
            if (!em.HasComponent<Citizen>(citizen) || !em.HasComponent<Building>(building)) return false;
            var b = em.GetComponentData<Building>(building);
            var c = em.GetComponentData<Citizen>(citizen);
            var rules = Single<SettlementRules>(em);
            if (c.Job == building) { c.Task = CitizenTask.Idle; em.SetComponentData(citizen, c); return true; }
            if (c.AgeTicks < rules.AdultAgeTicks || b.Workers >= b.JobSlots) return false;
            b.Workers++; // reserve the slot until the next census
            em.SetComponentData(building, b);
            c.Job = building;
            c.Task = CitizenTask.Idle;
            em.SetComponentData(citizen, c);
            return true;
        }
    }
}
