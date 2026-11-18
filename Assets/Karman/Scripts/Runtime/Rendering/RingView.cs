using UnityEngine;

namespace Karman.Rendering
{
    /// <summary>Builds a planetary ring mesh at runtime (radii in planet radii).</summary>
    [RequireComponent(typeof(MeshFilter))]
    public sealed class RingView : MonoBehaviour
    {
        public float innerRadius = 1.24f;   // Saturn D/C ring inner edge ≈ 74,500 km / 60,268 km
        public float outerRadius = 2.27f;   // A ring outer edge ≈ 136,775 km
        void Awake() => GetComponent<MeshFilter>().sharedMesh = SphereMeshFactory.Ring(innerRadius, outerRadius);
    }
}
