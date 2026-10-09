using System.Collections.Generic;
using EpochsUnbound.CameraControl;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace EpochsUnbound.WorldGen
{
    /// <summary>
    /// Generates terrain chunks around the camera focus on demand and unloads distant ones,
    /// so the world has no edge. Heights and colours are computed in Burst jobs.
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
        }

        readonly Dictionary<int2, Chunk> _loaded = new();
        readonly List<int2> _missing = new();
        readonly List<int2> _toUnload = new();
        TerrainParams _params;
        int _verts;
        Vector2[] _uvs;
        int[] _indices;
        MaterialPropertyBlock _mpb;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        public int LoadedCount => _loaded.Count;

        void Awake()
        {
            _params = Settings.ToParams();
            _verts = Settings.Resolution + 1;
            _mpb = new MaterialPropertyBlock();

            // UVs hit texel centres so each vertex shows exactly its own colour.
            _uvs = new Vector2[_verts * _verts];
            for (int z = 0; z < _verts; z++)
            for (int x = 0; x < _verts; x++)
                _uvs[z * _verts + x] = new Vector2((x + 0.5f) / _verts, (z + 0.5f) / _verts);

            int q = Settings.Resolution;
            _indices = new int[q * q * 6];
            for (int z = 0, t = 0; z < q; z++)
            for (int x = 0; x < q; x++)
            {
                int i = z * _verts + x;
                _indices[t++] = i; _indices[t++] = i + _verts; _indices[t++] = i + 1;
                _indices[t++] = i + 1; _indices[t++] = i + _verts; _indices[t++] = i + _verts + 1;
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
        }

        void Generate(List<int2> coords)
        {
            int n = _verts * _verts;
            var jobs = new NativeArray<JobHandle>(coords.Count, Allocator.Temp);
            var positions = new NativeArray<float3>[coords.Count];
            var normals = new NativeArray<float3>[coords.Count];
            var colours = new NativeArray<Color32>[coords.Count];

            for (int i = 0; i < coords.Count; i++)
            {
                positions[i] = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                normals[i] = new NativeArray<float3>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                colours[i] = new NativeArray<Color32>(n, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                jobs[i] = new ChunkJob
                {
                    P = _params,
                    Origin = ChunkCoord.Origin(coords[i], Settings.ChunkSize),
                    Step = Settings.ChunkSize / Settings.Resolution,
                    Verts = _verts,
                    Positions = positions[i],
                    Normals = normals[i],
                    Colours = colours[i],
                }.Schedule(n, 256);
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

                _loaded.Add(coords[i], new Chunk { Go = go, Mesh = mesh, Texture = tex });
                positions[i].Dispose();
                normals[i].Dispose();
                colours[i].Dispose();
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
                float hl = TerrainSampler.SurfaceHeight(world - new float2(Step, 0), P);
                float hr = TerrainSampler.SurfaceHeight(world + new float2(Step, 0), P);
                float hd = TerrainSampler.SurfaceHeight(world - new float2(0, Step), P);
                float hu = TerrainSampler.SurfaceHeight(world + new float2(0, Step), P);

                float3 normal = math.normalize(new float3(hl - hr, 2f * Step, hd - hu));
                Positions[i] = new float3(local.x, math.max(0f, ground), local.y);
                Normals[i] = normal;
                Colours[i] = TerrainSampler.BiomeColour(world, ground, normal.y, P);
            }
        }
    }
}
