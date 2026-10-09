using EpochsUnbound.CameraControl;
using EpochsUnbound.Settlement;
using EpochsUnbound.Simulation;
using EpochsUnbound.WorldGen;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EpochsUnbound.Editor
{
    /// <summary>
    /// Creates the settings assets, terrain material and Main scene. Safe to rerun: existing
    /// settings assets are kept, the scene is rebuilt. Batch: -executeMethod EpochsUnbound.Editor.ProjectSetup.Run
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Main.unity";

        [MenuItem("Epochs Unbound/Rebuild Main Scene")]
        public static void Run()
        {
            foreach (var dir in new[] { "Settings", "Materials", "Scenes", "Textures" })
                if (!AssetDatabase.IsValidFolder($"{Root}/{dir}"))
                    AssetDatabase.CreateFolder(Root, dir);

            var sim = LoadOrCreate<SimSettings>($"{Root}/Settings/SimSettings.asset");
            var world = LoadOrCreate<WorldSettings>($"{Root}/Settings/WorldSettings.asset");
            var cam = LoadOrCreate<CameraSettings>($"{Root}/Settings/CameraSettings.asset");

            var settlement = LoadOrCreate<SettlementSettings>($"{Root}/Settings/SettlementSettings.asset");

            var mat = LoadOrCreateMaterial("Terrain");
            var buildingMat = LoadOrCreateMaterial("Building");
            var citizenMat = LoadOrCreateMaterial("Citizen");
            citizenMat.enableInstancing = true;
            EditorUtility.SetDirty(citizenMat);
            // Terrain: tiling detail texture multiplied over the per-chunk biome colours.
            mat.SetTexture("_DetailAlbedoMap", TerrainDetailTexture());
            mat.SetTextureScale("_DetailAlbedoMap", new Vector2(40, 40)); // ~6.4 m per tile on a 256 m chunk
            mat.SetFloat("_DetailAlbedoMapScale", 1f);
            mat.EnableKeyword("_DETAIL_MULX2");
            EditorUtility.SetDirty(mat);

            world.TerrainMaterial = mat;
            world.TreeMaterial = citizenMat;
            world.WaterMaterial = WaterMaterial();
            EditorUtility.SetDirty(world);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.9f);

            var light = Object.FindAnyObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
            light.color = new Color(1f, 0.95f, 0.86f);
            light.intensity = 1.4f;
            light.shadows = LightShadows.Soft;

            var volume = new GameObject("Post Processing").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = PostProcessProfile();

            var camGo = Camera.main.gameObject;
            var camData = camGo.GetComponent<Camera>().GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rig = camGo.AddComponent<RtsCamera>();
            rig.Settings = cam;
            rig.World = world;

            var streamer = new GameObject("World").AddComponent<ChunkStreamer>();
            streamer.Settings = world;
            streamer.Rig = rig;

            var clock = new GameObject("Simulation").AddComponent<SimClockController>();
            clock.Settings = sim;
            clock.Streamer = streamer;

            var settlementGo = new GameObject("Settlement");
            var citizens = settlementGo.AddComponent<CitizenRenderer>();
            citizens.Material = citizenMat;
            var controller = settlementGo.AddComponent<SettlementController>();
            controller.Settings = settlement;
            controller.World = world;
            controller.Rig = rig;
            controller.Citizens = citizens;
            controller.BuildingMaterial = buildingMat;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Main scene built at " + ScenePath);
        }

        static Material WaterMaterial()
        {
            var water = LoadOrCreateMaterial("Water");
            // URP Lit, transparent alpha blend.
            water.SetFloat("_Surface", 1f);
            water.SetFloat("_Blend", 0f);
            water.SetFloat("_ZWrite", 0f);
            water.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            water.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            water.SetOverrideTag("RenderType", "Transparent");
            water.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            water.renderQueue = (int)RenderQueue.Transparent;
            water.SetColor("_BaseColor", new Color(0.08f, 0.3f, 0.42f, 0.78f));
            water.SetFloat("_Smoothness", 0.93f);
            water.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(water);
            return water;
        }

        /// <summary>Tileable grey noise around 0.5 (neutral for the x2 detail multiply): grass/soil grain.</summary>
        static Texture2D TerrainDetailTexture()
        {
            var path = $"{Root}/Textures/TerrainDetail.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;

            const int n = 512;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new float2(x, y) / n;
                float v = 0, amp = 0.5f;
                for (int o = 0, f = 8; o < 5; o++, f *= 2, amp *= 0.55f)
                    v += noise.pnoise(p * f, new float2(f, f)) * amp;
                float grain = noise.pnoise(p * 128f, new float2(128, 128)) * 0.08f;
                byte g = (byte)math.clamp((0.5f + v * 0.22f + grain) * 255f, 0f, 255f);
                px[y * n + x] = new Color32(g, g, g, 255);
            }
            tex.SetPixels32(px);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static VolumeProfile PostProcessProfile()
        {
            var path = $"{Root}/Settings/PostProcess.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile != null) return profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            T Add<T>() where T : VolumeComponent
            {
                var c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            Add<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var colour = Add<ColorAdjustments>();
            colour.postExposure.Override(0.45f);
            colour.contrast.Override(14f);
            colour.saturation.Override(12f);
            var bloom = Add<Bloom>();
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.25f);
            Add<Vignette>().intensity.Override(0.18f);
            var white = Add<WhiteBalance>();
            white.temperature.Override(6f);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static Material LoadOrCreateMaterial(string name)
        {
            var path = $"{Root}/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_Smoothness", 0.05f);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
