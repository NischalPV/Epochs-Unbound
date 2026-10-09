using System.Collections.Generic;
using EpochsUnbound.CameraControl;
using EpochsUnbound.Settlement;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.WorldGen
{
    /// <summary>
    /// Generates terrain chunks (mesh, colour texture, trees) around the camera focus on demand and unloads
    /// distant ones, so the world has no edge. Heights, colours and tree placement are computed in Burst jobs.
    /// Also keeps a sea-level water surface under the camera.
    /// </summary>
    public sealed class ChunkStreamer : MonoBehaviour
    {
        public WorldSettings Settings;
        public RtsCamera Rig;

        sealed class Chunk
        {
            public GameObject Go;
            public Mesh Mesh;
            public Texture2D Texture;
            public Matrix4x4[] Pines, Broadleaves;
            public float4[] Trees;   // xyz = base position, w = scale (negative = pine); kept to re-filter for clearings
        }

        readonly Dictionary<int2, Chunk> _loaded = new();
        readonly List<int2> _missing = new();
        readonly List<int2> _toUnload = new();
        readonly List<float3> _clearings = new();   // xz centre, z = radius (x, y=z of world, radius)
        TerrainParams _params;
        int _verts;
        Vector2[] _uvs;
        int[] _indices;
        MaterialPropertyBlock _mpb;
        Mesh _pineMesh, _broadleafMesh;
        Material _treeMaterial;
        Transform _water;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        public int LoadedCount => _loaded.Count;

        void Awake()
        {
            _params = Settings.ToParams();
            _verts = Settings.Resolution + 1;
            _mpb = new MaterialPropertyBlock();

            // UVs span 0..1 exactly per chunk so the tiling detail texture continues seamlessly across chunks.
            _uvs = new Vector2[_verts * _verts];
            for (int z = 0; z < _verts; z++)
            for (int x = 0; x < _verts; x++)
                _uvs[z * _verts + x] = new Vector2(x / (_verts - 1f), z / (_verts - 1f));

            int q = Settings.Resolution;
            _indices = new int[q * q * 6];
            for (int z = 0, t = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                int i = z * _verts + x;
                _indices[t++] = i; _indices[t++] = i + _verts; _indices[t++] = i + 1;
                _indices[t++] = i + 1; _indices[t++] = i + _verts; _indices[t++] = i + _verts + 1;
            }

            if (Settings.TreeMaterial != null)
            {
                _pineMesh = Models.PineTree();
                _broadleafMesh = Models.BroadleafTree();
                _treeMaterial = new Material(Settings.TreeMaterial) { enableInstancing = true };
                _treeMaterial.SetTexture(BaseMap, Models.CreatePalette());
            }

            if (Settings.WaterMaterial != null)
            {
                var water = new GameObject("Water");
                water.transform.SetParent(transform, false);
                var mesh = new Mesh { name = "WaterPlane" };
                mesh.SetVertices(new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) });
                mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
                mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
                water.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = water.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Settings.WaterMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _water = water.transform;
            }
        }

        void Update()
        {
            float size = Settings.ChunkSize;
            float3 focus = Rig.FocusPoint;
            int2 centre = ChunkCoord.FromWorld(focus.x, focus.z, size);
            int radius = math.clamp(Settings.MinLoadRadius + (int)(Rig.Distance * 1.5f / size), Settings.MinLoadRadius, Settings.MaxLoadRadius);

            // Unload chunks well outside the radius (margin avoids thrashing at the boundary).
            _toUnload.Clear();
            foreach (var kv in _loaded)
                if (ChunkCoord.Distance(kv.Key, centre) > radius + Settings.UnloadMargin)
                    _toUnload.Add(kv.Key);
            foreach (var c in _toUnload) Unload(c);

            // Load the nearest missing chunks, a few per frame.
            _missing.Clear();
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                var c = centre + new int2(x, z);
                if (!_loaded.ContainsKey(c)) _missing.Add(c);
            }
            if (_missing.Count > 0)
            {
                _missing.Sort((a, b) => math.lengthsq(a - centre).CompareTo(math.lengthsq(b - centre)));
                Generate(_missing.Count > Settings.ChunksPerFrame ? _missing.GetRange(0, Settings.ChunksPerFrame) : _missing);
            }

            // Fog and far clip follow the loaded area so the edge of the world is never visible.
            float view = radius * size;
            Rig.Camera.farClipPlane = Rig.Distance + view * 1.5f;
            RenderSettings.fogStartDistance = Rig.Distance + view * 0.4f;
            RenderSettings.fogEndDistance = Rig.Distance + view;

            if (_water != null)
            {
                _water.position = new Vector3(Mathf.Round(focus.x), 0f, Mathf.Round(focus.z));
                _water.localScale = Vector3.one * (view * 2.5f + Rig.Distance * 2f);
            }

            DrawTrees(centre);
        }

        void DrawTrees(int2 centre)
        {
            if (_treeMaterial == null) return;
            var rp = new RenderParams(_treeMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
            int r = Settings.TreeDrawRadius;
            for (int z = -r; z <= r; z++)
            for (int x = -r; x <= r; x++)
            {
                if (!_loaded.TryGetValue(centre + new int2(x, z), out var c)) continue;
                if (c.Pines.Length > 0) Graphics.RenderMeshInstanced(rp, _pineMesh, 0, c.Pines);
                if (c.Broadleaves.Length > 0) Graphics.RenderMeshInstanced(rp, _broadleafMesh, 0, c.Broadleaves);
            }
        }

        /// <summary>Removes trees around a point (a new building) now and whenever that chunk is regenerated.</summary>
        public void AddClearing(float3 position, float radius)
        {
            _clearings.Add(new float3(position.x, position.z, radius));
            foreach (var c in _loaded.Values) BuildTreeMatrices(c);
        }

        void BuildTreeMatrices(Chunk c)
        {
            var pines = new List<Matrix4x4>();
            var broad = new List<Matrix4x4>();
            foreach (var t in c.Trees)
            {
                bool cleared = false;
                foreach (var cl in _clearings)
                    if (math.distancesq(t.xz, cl.xy) < cl.z * cl.z) { cleared = true; break; }
                if (cleared) continue;
                float yaw = math.frac(math.sin(t.x * 12.9898f + t.z * 78.233f) * 43758.5453f) * 360f;
                var m = Matrix4x4.TRS(new Vector3(t.x, t.y - 0.2f, t.z), Quaternion.Euler(0, yaw, 0), Vector3.one * math.abs(t.w));
                (t.w < 0 ? pines : broad).Add(m);
            }
            // One instanced call draws at most 1023; a 9 m grid on a 256 m chunk gives at most 784 trees.
            c.Pines = pines.ToArray();
            c.Broadleaves = broad.ToArray();
        }

        void Generate(List<int2> coords)
        {
            int n = _verts * _verts;
            var jobs = new NativeArray<JobHandle>(coords.Count * 2, Allocator.Temp);
            var positions = new NativeArray<float3>[coords.Count];
            var normals = new NativeArray<float3>[coords.Count];
            var colours = new NativeArray<Color32>[coords.Count];
            var trees = new NativeList<float4>[coords.Count];

            for (int i = 0; i < coords.Count; i++)
            {
                var origin = ChunkCoord.Origin(coords[i], Settings.ChunkSize);
                positions[i] = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                normals[i] = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                colours[i] = new NativeArray<Color32>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                trees[i] = new NativeList<float4>(256, Allocator.TempJob);
                jobs[i * 2] = new ChunkJob
                {
                    P = _params,
                    Origin = origin,
                    Step = Settings.ChunkSize / Settings.Resolution,
                    Verts = _verts,
                    Positions = positions[i],
                    Normals = normals[i],
                    Colours = colours[i],
                }.Schedule(n, 256);
                jobs[i * 2 + 1] = new TreeJob
                {
                    P = _params,
                    Origin = origin,
                    ChunkSize = Settings.ChunkSize,
                    Seed = Settings.Seed,
                    Trees = trees[i],
                }.Schedule();
            }
            JobHandle.CompleteAll(jobs);

            for (int i = 0; i < coords.Count; i++)
            {
                var mesh = new Mesh { name = $"Chunk {coords[i].x},{coords[i].y}" };
                mesh.SetVertices(positions[i]);
                mesh.SetNormals(normals[i]);
                mesh.SetUVs(0, _uvs);
                mesh.SetIndices(_indices, MeshTopology.Triangles, 0);
                mesh.RecalculateBounds();

                var tex = new Texture2D(_verts, _verts, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                tex.SetPixelData(colours[i], 0);
                tex.Apply(false, true);

                var origin = ChunkCoord.Origin(coords[i], Settings.ChunkSize);
                var go = new GameObject(mesh.name);
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(origin.x, 0, origin.y);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = Settings.TerrainMaterial;
                // ponytail: per-chunk texture via property block opts out of SRP batching; move to a texture array or vertex-colour shader if draw calls matter.
                _mpb.SetTexture(BaseMap, tex);
                mr.SetPropertyBlock(_mpb);

                var chunk = new Chunk { Go = go, Mesh = mesh, Texture = tex, Trees = trees[i].AsArray().ToArray() };
                BuildTreeMatrices(chunk);
                _loaded.Add(coords[i], chunk);
                positions[i].Dispose();
                normals[i].Dispose();
                colours[i].Dispose();
                trees[i].Dispose();
            }
            jobs.Dispose();
        }

        void Unload(int2 coord)
        {
            var c = _loaded[coord];
            Destroy(c.Go);
            Destroy(c.Mesh);
            Destroy(c.Texture);
            _loaded.Remove(coord);
        }

        void OnDestroy()
        {
            foreach (var c in _loaded.Values)
            {
                Destroy(c.Mesh);
                Destroy(c.Texture);
            }
            _loaded.Clear();
        }

        [BurstCompile]
        struct ChunkJob : IJobParallelFor
        {
            public TerrainParams P;
            public float2 Origin;
            public float Step;
            public int Verts;
            [WriteOnly] public NativeArray<float3> Positions;
            [WriteOnly] public NativeArray<float3> Normals;
            [WriteOnly] public NativeArray<Color32> Colours;

            public void Execute(int i)
            {
                int x = i % Verts, z = i / Verts;
                float2 local = new float2(x, z) * Step;
                float2 world = Origin + local;
                float ground = TerrainSampler.GroundHeight(world, P);

                // Normals from the height function itself (not mesh neighbours) so chunk seams match.
                float hl = TerrainSampler.GroundHeight(world - new float2(Step, 0), P);
                float hr = TerrainSampler.GroundHeight(world + new float2(Step, 0), P);
                float hd = TerrainSampler.GroundHeight(world - new float2(0, Step), P);
                float hu = TerrainSampler.GroundHeight(world + new float2(0, Step), P);

                float3 normal = math.normalize(new float3(hl - hr, 2f * Step, hd - hu));
                Positions[i] = new float3(local.x, ground, local.y);   // real seabed; the water plane sits at y = 0
                Normals[i] = normal;
                Colours[i] = TerrainSampler.BiomeColour(world, ground, normal.y, P);
            }
        }

        /// <summary>Scatters trees on a jittered 9 m grid by climate-driven density. Deterministic per seed and cell.</summary>
        [BurstCompile]
        struct TreeJob : IJob
        {
            public TerrainParams P;
            public float2 Origin;
            public float ChunkSize;
            public uint Seed;
            public NativeList<float4> Trees;

            public void Execute()
            {
                const float cell = 9f;
                // Cells whose corner lies in this chunk, so neighbouring chunks never share or skip a cell.
                int2 first = (int2)math.ceil(Origin / cell), end = (int2)math.ceil((Origin + ChunkSize) / cell);
                for (int cz = first.y; cz < end.y; cz++)
                for (int cx = first.x; cx < end.x; cx++)
                {
                    var c = new int2(cx, cz);
                    var rng = new Unity.Mathematics.Random(math.hash(new int3(c, (int)Seed)) | 1u);
                    float2 pos = (float2)c * cell + rng.NextFloat2(0.1f, 0.9f) * cell;
                    float ground = TerrainSampler.GroundHeight(pos, P);
                    float density = TerrainSampler.TreeDensity(pos, ground, P, out bool pine);
                    if (rng.NextFloat() >= density) continue;
                    // Skip steep ground.
                    float slope = math.abs(TerrainSampler.GroundHeight(pos + new float2(2, 0), P) - ground) + math.abs(TerrainSampler.GroundHeight(pos + new float2(0, 2), P) - ground);
                    if (slope > 2.2f) continue;
                    float scale = rng.NextFloat(0.75f, 1.3f);
                    Trees.Add(new float4(pos.x, ground, pos.y, pine ? -scale : scale));
                }
            }
        }
    }
}
