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

        public GameObject Create(PartDefinition def, Transform parent)
        {
            int i = Array.IndexOf(modelIds, def.ModelId);
            GameObject go = i >= 0 && prefabs[i] ? Instantiate(prefabs[i], parent) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(parent, false);
            go.name = def.Title;
            return go;
        }
    }

    /// <summary>Hand-off from the VAB to the flight scene.</summary>
    public static class LaunchRequest
    {
        public static Craft Craft;
    }
}
