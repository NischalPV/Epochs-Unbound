using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace EpochsUnbound.Editor
{
    /// <summary>
    /// Import settings and material wiring for the third-party art that tools/fetch-art.ps1 downloads into
    /// Assets/_Project/ThirdParty. File names there are fixed by the script, so this code knows where to look.
    /// </summary>
    public sealed class ArtImport : AssetPostprocessor
    {
        public const string Root = "Assets/_Project/ThirdParty";
        const string Ground = Root + "/PolyHaven/Ground";
        const string SkyPath = Root + "/PolyHaven/Sky/sky.hdr";

        /// <summary>Ground layers in the terrain shader, with how much of each photo's own colour to keep.</summary>
        static readonly (string name, float photo)[] Layers =
        {
            ("Grass", 0.25f), ("Dirt", 0.4f), ("Sand", 0.6f), ("Rock", 0.6f), ("Snow", 0.8f),
        };

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root + "/PolyHaven/")) return;
            var importer = (TextureImporter)assetImporter;

            if (assetPath == SkyPath)
            {
                // Panoramic sky: full resolution, no mips (they seam where the panorama wraps).
                importer.maxTextureSize = 8192;
                importer.mipmapEnabled = false;
                importer.wrapModeU = TextureWrapMode.Repeat;
                importer.wrapModeV = TextureWrapMode.Clamp;
                return;
            }

            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            if (assetPath.EndsWith("_normal.jpg")) importer.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_rough.jpg")) importer.sRGBTexture = false;
        }

        /// <summary>Switches the terrain material to the layered ground shader and assigns whatever textures exist.</summary>
        public static void SetUpTerrain(Material mat)
        {
            var shader = Shader.Find("EpochsUnbound/Terrain");
            if (shader == null) { Debug.LogError("[ArtImport] Terrain shader not found"); return; }
            mat.shader = shader;
            foreach (var (name, photo) in Layers)
            {
                var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ground}/{name}_albedo.jpg");
                mat.SetTexture($"_{name}Albedo", albedo);
                mat.SetTexture($"_{name}Normal", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ground}/{name}_normal.jpg"));
                mat.SetTexture($"_{name}Rough", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Ground}/{name}_rough.jpg"));
                // Without a photo the layer is pure biome tint.
                mat.SetFloat($"_{name}Photo", albedo != null ? photo : 0f);
            }
            EditorUtility.SetDirty(mat);
        }

        /// <summary>Uses the downloaded sky panorama for the skybox and ambient light. False if there is none yet.</summary>
        public static bool SetUpSky(string materialPath)
        {
            var hdr = AssetDatabase.LoadAssetAtPath<Texture2D>(SkyPath);
            if (hdr == null) return false;

            var sky = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, materialPath);
            }
            sky.SetTexture("_MainTex", hdr);
            sky.SetFloat("_Mapping", 1f);      // latitude-longitude layout
            sky.EnableKeyword("_MAPPING_LATITUDE_LONGITUDE_LAYOUT");
            sky.SetFloat("_ImageType", 0f);    // full 360 degrees
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();
            return true;
        }
    }
}
