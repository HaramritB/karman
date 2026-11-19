using System;
using Karman.Core.Vehicles;
using UnityEngine;

namespace Karman.Vehicles
{
    /// <summary>Maps part ModelIds to prefabs; shared by the VAB and flight.</summary>
    [CreateAssetMenu(menuName = "Karman/Part Model Catalog")]
    public sealed class PartModelCatalog : ScriptableObject
    {
        public string[] modelIds = Array.Empty<string>();
        public GameObject[] prefabs = Array.Empty<GameObject>();
        [Tooltip("Imported material names replaced by curated PBR materials.")]
        public string[] materialNames = Array.Empty<string>();
        public Material[] materials = Array.Empty<Material>();

        public GameObject Create(PartDefinition def, Transform parent)
        {
            if (def.ModelId == "none") { var empty = new GameObject(def.Title); empty.transform.SetParent(parent, false); return empty; }
            int i = Array.IndexOf(modelIds, def.ModelId);
            GameObject go = i >= 0 && prefabs[i] ? Instantiate(prefabs[i], parent) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent, false);
            go.name = def.Title;
            if (materialNames.Length > 0)
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        if (mats[k] == null) continue;
                        string n = mats[k].name.Replace(" (Instance)", "");
                        int idx = Array.FindIndex(materialNames, m => n == m || n.StartsWith(m + "."));
                        if (idx >= 0) { mats[k] = materials[idx]; changed = true; }
                    }
                    if (changed) r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
            return go;
        }
    }

    /// <summary>Hand-off from the VAB to the flight scene.</summary>
    public static class LaunchRequest
    {
        public static Craft Craft;
    }
}
