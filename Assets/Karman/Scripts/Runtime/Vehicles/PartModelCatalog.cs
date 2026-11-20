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
        [Tooltip("HDRP decal materials: vertical KÁRMÁN wordmark on long core tanks, mission emblem on mid tanks.")]
        public Material wordmarkDecal, emblemDecal;

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
            AddDecals(def, go);
            return go;
        }

        /// <summary>Livery: projected decals that follow the part (HDRP decal projectors parented to it).</summary>
        void AddDecals(PartDefinition def, GameObject go)
        {
            float r = def.Diameter / 2f;
            if (def.ModelId == "tank_32" && wordmarkDecal != null)
                foreach (float side in new[] { 1f, -1f })
                    Decal(go.transform, wordmarkDecal, new Vector3(0, 2f, side * r), new Vector3(0, 0, -side), new Vector3(16f, 2.5f, 1.4f));
            if (def.ModelId == "tank_16" && emblemDecal != null)
                Decal(go.transform, emblemDecal, new Vector3(0, 1.5f, r), Vector3.back, new Vector3(2.6f, 2.6f, 1.4f));
        }

        static void Decal(Transform parent, Material m, Vector3 pos, Vector3 inward, Vector3 size)
        {
            var d = new GameObject("Decal " + m.name);
            d.transform.SetParent(parent, false);
            d.transform.localPosition = pos;
            // Texture u runs along the vehicle axis (vertical lettering): right = up, forward = into the tank.
            d.transform.localRotation = Quaternion.LookRotation(inward, Vector3.Cross(inward, Vector3.up));
            var p = d.AddComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>();
            p.material = m; p.size = size; p.pivot = Vector3.zero;
            p.startAngleFade = 55f; p.endAngleFade = 80f;
            p.fadeFactor = 1f;
        }
    }

    /// <summary>Hand-off from the VAB to the flight scene.</summary>
    public static class LaunchRequest
    {
        public static Craft Craft;
    }
}
