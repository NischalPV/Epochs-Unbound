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
    /// Draws every citizen as a GPU-instanced low-poly person (four shirt colours), interpolated between sim
    /// ticks, facing the way they walk. No GameObject per citizen, so tens of thousands are cheap.
    /// </summary>
    public sealed class CitizenRenderer : MonoBehaviour
    {
        public Material Material;
        public Color SelectedColour = new(1f, 0.9f, 0.1f);

        /// <summary>Selected citizens, drawn highlighted. Owned by <see cref="SettlementController"/>.</summary>
        public readonly List<Entity> Selected = new();

        readonly Mesh[] _meshes = new Mesh[4];
        Material _material;
        EntityQuery _query, _rules;
        SimTickSystemGroup _group;
        MaterialPropertyBlock _plain, _selected;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        void Start()
        {
            for (int i = 0; i < _meshes.Length; i++) _meshes[i] = Models.Citizen(Models.Shirts[i]);
            _material = new Material(Material) { enableInstancing = true };
            _material.SetTexture(BaseMap, Models.CreatePalette());

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            _query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Citizen>(), ComponentType.ReadOnly<CitizenMotion>());
            _rules = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SettlementRules>());
            _group = world.GetExistingSystemManaged<SimTickSystemGroup>();
            _plain = new MaterialPropertyBlock();
            _plain.SetColor(BaseColor, Color.white);
            _selected = new MaterialPropertyBlock();
            _selected.SetColor(BaseColor, SelectedColour);
        }

        void LateUpdate()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated || _query.IsEmpty) return;
            var em = world.EntityManager;
            float3 camPos = Camera.main != null ? (float3)Camera.main.transform.position : float3.zero;

            var motions = _query.ToComponentDataArray<CitizenMotion>(Allocator.TempJob);
            var citizens = _query.ToComponentDataArray<Citizen>(Allocator.TempJob);
            var v0 = new NativeList<Matrix4x4>(motions.Length / 4 + 1, Allocator.TempJob);
            var v1 = new NativeList<Matrix4x4>(motions.Length / 4 + 1, Allocator.TempJob);
            var v2 = new NativeList<Matrix4x4>(motions.Length / 4 + 1, Allocator.TempJob);
            var v3 = new NativeList<Matrix4x4>(motions.Length / 4 + 1, Allocator.TempJob);
            new BuildMatricesJob
            {
                Motions = motions,
                Citizens = citizens,
                AdultAge = _rules.GetSingleton<SettlementRules>().AdultAgeTicks,
                CameraPos = camPos,
                Alpha = (float)math.saturate(_group.Clock.Alpha),
                Time = Time.time,
                V0 = v0, V1 = v1, V2 = v2, V3 = v3,
            }.Run();

            Draw(_meshes[0], v0.AsArray(), _plain);
            Draw(_meshes[1], v1.AsArray(), _plain);
            Draw(_meshes[2], v2.AsArray(), _plain);
            Draw(_meshes[3], v3.AsArray(), _plain);

            if (Selected.Count > 0)
            {
                var sel = new NativeArray<Matrix4x4>(Selected.Count, Allocator.Temp);
                int n = 0;
                foreach (var e in Selected)
                {
                    if (!em.Exists(e) || !em.HasComponent<CitizenMotion>(e)) continue;
                    float3 p = em.GetComponentData<CitizenMotion>(e).Position;
                    float s = 1.15f * math.max(1f, math.distance(camPos, p) / 60f);
                    sel[n++] = Matrix4x4.TRS(p, Quaternion.identity, Vector3.one * s);
                }
                Draw(_meshes[0], sel.GetSubArray(0, n), _selected);
                sel.Dispose();
            }

            motions.Dispose();
            citizens.Dispose();
            v0.Dispose(); v1.Dispose(); v2.Dispose(); v3.Dispose();
        }

        void Draw(Mesh mesh, NativeArray<Matrix4x4> matrices, MaterialPropertyBlock block)
        {
            var rp = new RenderParams(_material) { matProps = block, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
            for (int start = 0; start < matrices.Length; start += 1023)
                Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, math.min(1023, matrices.Length - start), start);
        }

        [BurstCompile]
        struct BuildMatricesJob : IJob
        {
            [ReadOnly] public NativeArray<CitizenMotion> Motions;
            [ReadOnly] public NativeArray<Citizen> Citizens;
            public int AdultAge;
            public float Alpha, Time;
            public float3 CameraPos;
            public NativeList<Matrix4x4> V0, V1, V2, V3;

            public void Execute()
            {
                for (int i = 0; i < Motions.Length; i++)
                {
                    var m = Motions[i];
                    var c = Citizens[i];
                    float3 p = math.lerp(m.Previous, m.Position, Alpha);
                    float2 step = m.Position.xz - m.Previous.xz;
                    bool moving = math.lengthsq(step) > 1e-6f;

                    // Face the walking direction; idle citizens keep a fixed per-citizen heading.
                    float yaw = moving ? math.atan2(step.x, step.y) : (c.Id * 2.399f) % (2f * math.PI);
                    // Children are smaller; everyone grows with distance beyond 60 m so they stay visible zoomed out.
                    float scale = (c.AgeTicks >= AdultAge ? 1f : 0.65f) * math.max(1f, math.distance(CameraPos, p) / 60f);
                    if (moving) p.y += math.abs(math.sin(Time * 9f + c.Id)) * 0.07f * scale; // walking bob

                    Matrix4x4 mat = float4x4.TRS(p, quaternion.RotateY(yaw), new float3(scale));
                    switch (c.Id & 3)
                    {
                        case 0: V0.Add(mat); break;
                        case 1: V1.Add(mat); break;
                        case 2: V2.Add(mat); break;
                        default: V3.Add(mat); break;
                    }
                }
            }
        }
    }
}
