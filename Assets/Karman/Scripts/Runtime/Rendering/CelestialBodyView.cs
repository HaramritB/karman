using Karman.Core;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Rendering
{
    public enum BodyShading { Earth, Moon, Planet, Sun }

    /// <summary>Places one celestial body in camera-relative scaled space and feeds its shaders.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class CelestialBodyView : MonoBehaviour
    {
        public string bodyId;
        public BodyShading shading;
        public Renderer surface;
        public Renderer atmosphere;      // optional shell (radius = atmosphereTop / R in parent units)
        public Renderer rings;           // optional
        public float atmosphereHeight = 100_000f;
        [Tooltip("Cloud drift relative to the surface, degrees of longitude per simulated hour.")]
        public float cloudDriftDegPerHour = 0.6f;

        public CelestialBody Body { get; private set; }
        public float RenderScale { get; private set; } = 1;

        MaterialPropertyBlock block;
        static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        static readonly int EarthDirId = Shader.PropertyToID("_EarthDir");
        static readonly int EarthShineId = Shader.PropertyToID("_EarthShine");
        static readonly int CenterId = Shader.PropertyToID("_PlanetCenter");
        static readonly int RadiusId = Shader.PropertyToID("_PlanetRadius");
        static readonly int CloudOffsetId = Shader.PropertyToID("_CloudOffset");
        static readonly int AtmoTopId = Shader.PropertyToID("_AtmoTop");
        static readonly int BetaRId = Shader.PropertyToID("_BetaR");
        static readonly int BetaMId = Shader.PropertyToID("_BetaM");
        static readonly int HRId = Shader.PropertyToID("_ScaleHeightR");
        static readonly int HMId = Shader.PropertyToID("_ScaleHeightM");

        void Start()
        {
            block = new MaterialPropertyBlock();
            Body = SimulationController.Instance.System.Get(bodyId);
        }

        void LateUpdate()
        {
            var sim = SimulationController.Instance;
            if (Body == null || sim == null) return;
            Vector3 pos = RenderFrame.Project(Body.Position, out float scale);
            RenderScale = scale;
            float r = (float)(Body.Radius * scale);
            transform.SetPositionAndRotation(pos, Coords.ToUnity(Body.Orientation));
            transform.localScale = Vector3.one * r;

            Vector3 sunDir = Coords.ToUnity((sim.System.Sun.Position - Body.Position).normalized);
            block.Clear();
            block.SetVector(SunDirId, sunDir);
            block.SetVector(CenterId, pos);
            block.SetFloat(RadiusId, r);
            if (shading == BodyShading.Earth)
            {
                double hours = (sim.Time - sim.StartTime) / 3600.0;
                block.SetFloat(CloudOffsetId, (float)((hours * cloudDriftDegPerHour / 360.0) % 1.0));
            }
            if (shading == BodyShading.Moon)
            {
                var earth = sim.System.Get(SolarSystemData.Earth);
                Vector3d toEarth = (earth.Position - Body.Position).normalized;
                Vector3d toSunFromEarth = (sim.System.Sun.Position - earth.Position).normalized;
                // Earth's illuminated fraction as seen from the Moon drives earthshine.
                double phase = 0.5 * (1 + Vector3d.Dot(-toEarth, -toSunFromEarth) * -1);
                block.SetVector(EarthDirId, Coords.ToUnity(toEarth));
                block.SetFloat(EarthShineId, (float)(0.03 * phase));
            }
            if (surface) surface.SetPropertyBlock(block);
            if (rings) rings.SetPropertyBlock(block);

            if (atmosphere)
            {
                double R = Body.Radius;
                float top = (float)(1.0 + atmosphereHeight / R);
                atmosphere.transform.localScale = Vector3.one * top;
                block.SetFloat(AtmoTopId, top);
                block.SetVector(BetaRId, new Vector4((float)(5.802e-6 * R), (float)(13.558e-6 * R), (float)(33.1e-6 * R), 0));
                block.SetFloat(BetaMId, (float)(3.996e-6 * R));
                var atmo = Body.Definition.Atmosphere;
                block.SetFloat(HRId, (float)((atmo?.RayleighScaleHeight ?? 8000) / R));
                block.SetFloat(HMId, (float)((atmo?.MieScaleHeight ?? 1200) / R));
                atmosphere.SetPropertyBlock(block);
            }
        }
    }
}
