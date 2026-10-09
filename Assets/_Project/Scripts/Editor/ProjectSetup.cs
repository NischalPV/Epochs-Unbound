using EpochsUnbound.CameraControl;
using EpochsUnbound.Settlement;
using EpochsUnbound.Simulation;
using EpochsUnbound.WorldGen;
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
            foreach (var dir in new[] { "Settings", "Materials", "Scenes" })
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
            // Terrain: photo ground layers (from tools/fetch-art.ps1, when downloaded) over the per-chunk biome colours.
            ArtImport.SetUpTerrain(mat);

            world.TerrainMaterial = mat;
            world.TreeMaterial = citizenMat;
            world.WaterMaterial = WaterMaterial();
            EditorUtility.SetDirty(world);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.74f, 0.82f, 0.9f);
            if (!ArtImport.SetUpSky($"{Root}/Materials/Sky.mat"))
                Debug.Log("[ProjectSetup] No sky panorama yet: run tools/fetch-art.ps1 for the Poly Haven sky.");

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
