using Karman.Core;
using UnityEngine;

namespace Karman.Rendering
{
    /// <summary>Registers body terrain height fields before any simulation or rendering code runs.</summary>
    [DefaultExecutionOrder(-300)]
    public sealed class TerrainLoader : MonoBehaviour
    {
        public TextAsset moonDem;        // moon_dem16.bytes (LOLA 16 px/deg, uint16)
        public int moonWidth = 5760, moonHeight = 2880;

        void Awake()
        {
            if (moonDem != null && TerrainRegistry.Get(SolarSystemData.Moon) == null)
                TerrainRegistry.Register(SolarSystemData.Moon, new LunarTerrain(moonDem.bytes, moonWidth, moonHeight));
        }
    }
}
