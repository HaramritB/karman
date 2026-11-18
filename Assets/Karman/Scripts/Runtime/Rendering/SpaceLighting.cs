using System;
using Karman.Core;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Rendering
{
    /// <summary>
    /// Drives the sun directional light from the true Sun direction at the focus point, applies umbra/penumbra
    /// from any body (eclipses), adds Earth-reflected fill light, and keeps the solar glare at the Sun.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class SpaceLighting : MonoBehaviour
    {
        public Light sunLight;
        public Light planetShineLight;
        public float sunIntensity = 3.2f;
        public float earthShineMax = 0.35f;
        public Transform glare;
        public float glareAngularSize = 0.12f; // radians of the glare quad
        [Range(0, 1)] public float Illumination { get; private set; } = 1;

        void LateUpdate()
        {
            var sim = SimulationController.Instance;
            if (sim == null) return;
            var sys = sim.System;
            Vector3d focus = sim.ActiveVessel != null && sim.ActiveVessel.Trajectory != null ? sim.ActiveVessel.Trajectory.HeliocentricPosition : RenderFrame.Origin;
            Vector3d toSun = sys.Sun.Position - focus;
            double sunDist = toSun.magnitude;
            Vector3d sunDirSim = toSun / sunDist;

            // Eclipse: angular overlap of the solar disc with each occulting body.
            double sunAngular = Math.Asin(Math.Min(1, sys.Sun.Radius / sunDist));
            double light = 1;
            foreach (var b in sys.Bodies)
            {
                if (b == sys.Sun) continue;
                Vector3d toB = b.Position - focus;
                double dB = toB.magnitude;
                if (dB > sunDist || dB < b.Radius) continue;
                double bodyAngular = Math.Asin(Math.Min(1, b.Radius / dB));
                double sep = Vector3d.Angle(toB, toSun);
                if (sep >= bodyAngular + sunAngular) continue;
                double frac = sep <= bodyAngular - sunAngular ? 1 : 1 - (sep - (bodyAngular - sunAngular)) / (2 * sunAngular);
                light = Math.Min(light, 1 - Math.Max(0, Math.Min(1, frac)));
            }
            Illumination = (float)light;

            if (sunLight)
            {
                sunLight.transform.rotation = Quaternion.LookRotation(-Coords.ToUnity(sunDirSim));
                sunLight.intensity = sunIntensity * Illumination;
                sunLight.enabled = Illumination > 0.001f;
            }

            if (planetShineLight)
            {
                var earth = sys.Get(SolarSystemData.Earth);
                Vector3d toEarth = earth.Position - focus;
                double dE = toEarth.magnitude;
                Vector3d eHat = toEarth / dE;
                // Fraction of the visible Earth disc that is sunlit, times its solid-angle weight.
                double phase = 0.5 * (1 + Vector3d.Dot(-eHat, (sys.Sun.Position - earth.Position).normalized));
                double solid = Math.Pow(Math.Min(1, earth.Radius / dE), 2);
                planetShineLight.transform.rotation = Quaternion.LookRotation(-Coords.ToUnity(eHat));
                planetShineLight.intensity = (float)(earthShineMax * phase * solid);
            }

            if (glare)
            {
                Vector3 p = RenderFrame.Project(sys.Sun.Position, out _);
                float d = p.magnitude;
                glare.position = p * 0.995f;
                glare.rotation = Quaternion.LookRotation(p.normalized);
                glare.localScale = Vector3.one * (2f * d * Mathf.Tan(glareAngularSize * 0.5f)) * 2f;
            }
        }
    }
}
