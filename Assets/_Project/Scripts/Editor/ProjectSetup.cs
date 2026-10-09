using EpochsUnbound.CameraControl;
using EpochsUnbound.Simulation;
using EpochsUnbound.WorldGen;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

            var matPath = $"{Root}/Materials/Terrain.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetFloat("_Smoothness", 0.05f);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            world.TerrainMaterial = mat;
            EditorUtility.SetDirty(world);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.80f, 0.88f);

            var light = Object.FindAnyObjectByType<Light>();
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            light.shadows = LightShadows.Soft;

            var camGo = Camera.main.gameObject;
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

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectSetup] Main scene built at " + ScenePath);
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
