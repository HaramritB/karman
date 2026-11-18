using UnityEditor;
using UnityEngine;

namespace Karman.EditorTools
{
    /// <summary>Import rules for KÁRMÁN art: planetary maps wrap in longitude, data maps stay linear.</summary>
    public sealed class KarmanAssetPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Karman/Art/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            ti.mipmapEnabled = true;
            ti.anisoLevel = 8;
            ti.filterMode = FilterMode.Trilinear;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = assetPath.Contains("/Planets/") ? 2048 : 8192;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.streamingMipmaps = true;
            bool data = file.Contains("normal") || file.Contains("specular") || file.Contains("height");
            ti.sRGBTexture = !data;
            if (file == "2k_earth_normal_map") ti.textureType = TextureImporterType.NormalMap;
            if (file.Contains("saturn_ring")) { ti.alphaIsTransparency = true; ti.wrapModeU = TextureWrapMode.Clamp; }
            if (file.Contains("stars")) { ti.mipmapEnabled = false; ti.wrapModeV = TextureWrapMode.Clamp; }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith("Assets/Karman/Art/Models/")) return;
            var mi = (ModelImporter)assetImporter;
            mi.useFileScale = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            mi.importAnimation = false;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
        }
    }
}
