using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Karman.EditorTools
{
    /// <summary>Curated PBR materials built from the CC0 Poly Haven scans with the Karman/Triplanar shader.</summary>
    public static class PbrMaterials
    {
        const string Pbr = "Assets/Karman/Art/Textures/PBR";

        public static Texture2D Tex(string set, string map)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pbr}/{set}/{set}_{map}.jpg") ?? AssetDatabase.LoadAssetAtPath<Texture2D>($"{Pbr}/{set}/{set}_{map}.png");
            if (t == null) Debug.LogWarning($"[PbrMaterials] missing {set}_{map}");
            return t;
        }

        public static Material Triplanar(string name, string set, Color tint, float tiling, float metallic = 0, float roughness = 1,
                                         float normal = 1, float albedoStrength = 1, float desaturate = 0, bool objectSpace = false)
        {
            var m = KarmanSetup.Mat(name, "Karman/Triplanar");
            m.SetTexture("_BaseMap", Tex(set, "diff"));
            m.SetTexture("_BumpMap", Tex(set, "nor"));
            m.SetTexture("_ArmMap", Tex(set, "arm"));
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Tiling", tiling);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Roughness", roughness);
            m.SetFloat("_NormalStrength", normal);
            m.SetFloat("_AlbedoStrength", albedoStrength);
            m.SetFloat("_Desaturate", desaturate);
            m.SetFloat("_ObjectSpace", objectSpace ? 1 : 0);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Vehicle surface materials keyed by the material names the Blender part generator emits.</summary>
        public static (string[] names, Material[] mats) VehicleSet()
        {
            var map = new (string, Material)[]
            {
                ("PaintWhite", Triplanar("Part_PaintWhite", "metal_plate_02", new Color(0.93f, 0.93f, 0.92f), 2.4f, 0.0f, 0.55f, 0.35f, 0.12f, 1, true)),
                ("PaintGrey", Triplanar("Part_PaintGrey", "metal_plate_02", new Color(0.33f, 0.34f, 0.36f), 2.4f, 0.1f, 0.7f, 0.5f, 0.2f, 1, true)),
                ("CarbonBlack", Triplanar("Part_Carbon", "metal_plate_02", new Color(0.05f, 0.05f, 0.055f), 0.8f, 0.0f, 0.55f, 0.35f, 0.25f, 1, true)),
                ("Aluminium", Triplanar("Part_Aluminium", "metal_plate_02", new Color(0.85f, 0.86f, 0.88f), 1.2f, 1.0f, 0.45f, 0.4f, 0.4f, 1, true)),
                ("Steel", Triplanar("Part_Steel", "metal_plate_02", new Color(0.62f, 0.63f, 0.65f), 1.0f, 1.0f, 0.55f, 0.5f, 0.4f, 1, true)),
                ("CopperAlloy", Triplanar("Part_Copper", "metal_plate_02", new Color(0.78f, 0.46f, 0.30f), 0.6f, 1.0f, 0.6f, 0.6f, 0.3f, 1, true)),
                ("NozzleSoot", Triplanar("Part_Soot", "metal_plate_02", new Color(0.09f, 0.085f, 0.08f), 0.6f, 0.6f, 1.1f, 0.6f, 0.5f, 1, true)),
                ("NiobiumC103", Triplanar("Part_Niobium", "metal_plate_02", new Color(0.30f, 0.29f, 0.33f), 0.8f, 1.0f, 0.5f, 0.3f, 0.3f, 1, true)),
                ("GoldFoil", Triplanar("Part_Gold", "metal_plate_02", new Color(0.95f, 0.70f, 0.30f), 0.5f, 1.0f, 0.35f, 1.0f, 0.2f, 1, true)),
                ("CorkTPS", Triplanar("Part_Cork", "gravelly_sand", new Color(0.62f, 0.46f, 0.32f), 0.4f, 0.0f, 1.0f, 0.8f, 0.6f, 1, true)),
            };
            return (map.Select(m => m.Item1).ToArray(), map.Select(m => m.Item2).ToArray());
        }

        /// <summary>Adds URP screen-space ambient occlusion to the PC renderer (contact shading for pad, VAB, vehicles).</summary>
        public static void EnsureSsao()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if (data == null) return;
            var type = Type.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion, Unity.RenderPipelines.Universal.Runtime");
            if (type == null || data.rendererFeatures.Any(f => f != null && f.GetType() == type)) return;
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            feature.name = "SSAO";
            AssetDatabase.AddObjectToAsset(feature, data);
            data.rendererFeatures.Add(feature);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }
    }
}
