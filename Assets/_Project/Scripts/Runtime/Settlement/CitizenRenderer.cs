using System.Collections.Generic;
using EpochsUnbound.Simulation;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.Settlement
{
    /// <summary>
    /// Draws every citizen as a GPU-instanced capsule, interpolated between sim ticks.
    /// No GameObject per citizen, so tens of thousands are cheap.
    /// </summary>
    public sealed class CitizenRenderer : MonoBehaviour
    {
        public Material Material;
        public Color ChildColour = new(0.95f, 0.85f, 0.55f);
        public Color AdultColour = new(0.25f, 0.45f, 0.85f);
        public Color SelectedColour = new(1f, 0.9f, 0.1f);

        /// <summary>Selected citizens, drawn highlighted. Owned by <see cref="SettlementController"/>.</summary>
        public readonly List<Entity> Selected = new();

        Mesh _mesh;
        EntityQuery _query, _rules;
        SimTickSystemGroup _group;
        MaterialPropertyBlock _child, _adult, _selected;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            _query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<CitizenMotion>());
            _rules = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SettlementRules>());
            _group = world.GetExistingSystemManaged<SimTickSystemGroup>();
            _child = Block(ChildColour);
            _adult = Block(AdultColour);
            _selected = Block(SelectedColour);
        }

        static MaterialPropertyBlock Block(Color c)
        {
            var b = new MaterialPropertyBlock();
            b.SetColor(BaseColor, c);
            return b;
        }

        void LateUpdate()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _query.IsEmpty) return;
            var em = world.EntityManager;
            int adultAge = _rules.GetSingleton<SettlementRules>().AdultAgeTicks;

            var motions = _query.ToComponentDataArray<CitizenMotion>(Allocator.TempJob);
            var citizens = _query.ToComponentDataArray<Citizen>(Allocator.TempJob);
            var adults = new NativeList<Matrix4x4>(motions.Length, Allocator.TempJob);
            var children = new NativeList<Matrix4x4>(motions.Length / 2 + 1, Allocator.TempJob);
            new BuildMatricesJob
            {
                Motions = motions,
                Citizens = citizens,
                AdultAge = adultAge,
                CameraPos = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero,
                Alpha = (float)math.saturate(_group.Clock.Alpha),
                Adults = adults,
                Children = children,
            }.Run();

            Draw(adults.AsArray(), _adult);
            Draw(children.AsArray(), _child);

            if (Selected.Count > 0)
            {
                var sel = new NativeArray<Matrix4x4>(Selected.Count, Allocator.Temp);
                int n = 0;
                foreach (var e in Selected)
                    if (em.Exists(e) && em.HasComponent<CitizenMotion>(e))
                        sel[n++] = Matrix4x4.TRS(em.GetComponentData<CitizenMotion>(e).Position + new float3(0, 0.9f, 0), Quaternion.identity, new Vector3(0.6f, 1f, 0.6f));
                Draw(sel.GetSubArray(0, n), _selected);
                sel.Dispose();
            }

            motions.Dispose();
            citizens.Dispose();
            adults.Dispose();
            children.Dispose();
        }

        void Draw(NativeArray<Matrix4x4> matrices, MaterialPropertyBlock block)
        {
            var rp = new RenderParams(Material) { matProps = block, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
            for (int start = 0; start < matrices.Length; start += 1023)
                Graphics.RenderMeshInstanced(rp, _mesh, 0, matrices, math.min(1023, matrices.Length - start), start);
        }

        [BurstCompile]
        struct BuildMatricesJob : IJob
        {
            [ReadOnly] public NativeArray<CitizenMotion> Motions;
            [ReadOnly] public NativeArray<Citizen> Citizens;
            public int AdultAge;
            public float Alpha;
            public float3 CameraPos;
            public NativeList<Matrix4x4> Adults, Children;

            public void Execute()
            {
                for (int i = 0; i < Motions.Length; i++)
                {
                    var m = Motions[i];
                    float3 p = math.lerp(m.Previous, m.Position, Alpha);
                    bool adult = Citizens[i].AgeTicks >= AdultAge;
                    // Grow with distance beyond 60 m so citizens stay visible when zoomed out (not true scale).
                    float h = (adult ? 0.85f : 0.55f) * math.max(1f, math.distance(CameraPos, p) / 60f);
                    var mat = float4x4.TRS(p + new float3(0, h, 0), quaternion.identity, new float3(h * 0.5f, h, h * 0.5f));
                    (adult ? ref Adults : ref Children).Add(mat);
                }
            }
        }
    }
}
