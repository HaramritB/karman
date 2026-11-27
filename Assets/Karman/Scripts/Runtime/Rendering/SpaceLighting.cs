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
        /// <summary>Sunlight reaching the active vessel (1 = full Sun, 0 = umbra). Shown in the HUD.</summary>
        [Range(0, 1)] public float Illumination { get; private set; } = 1;
        /// <summary>Sunlight reaching the camera's surroundings, which is what the scene lights use.</summary>
        [Range(0, 1)] public float SceneIllumination { get; private set; } = 1;
        [Tooltip("Realtime probe at the focus point: sky and ground reflections on metal and paint near the surface.")]
        public ReflectionProbe skyProbe;
        [Tooltip("Soft fill from the camera direction in space, so a vessel on its shadowed side is never pitch black.")]
        public Light cameraFill;
        public float cameraFillLux = 2500f;
        public Color spaceAmbient = new Color(0.012f, 0.014f, 0.02f);

        void LateUpdate()
        {
            var sim = SimulationController.Instance;
            if (sim == null) return;
            var sys = sim.System;
            // The lights serve whatever HDRP lights near the camera (the vessel, or a body's terrain when the camera
            // looks at it from afar), so they are evaluated at the camera. The HUD's sunlight readout is the vessel's.
            Vector3d focus = RenderFrame.Origin;
            bool hasVessel = sim.ActiveVessel != null && sim.ActiveVessel.Trajectory != null;
            Illumination = hasVessel ? (float)SunlightAt(sys, sim.ActiveVessel.Trajectory.HeliocentricPosition) : 1f;
            SceneIllumination = (float)SunlightAt(sys, focus);
            Vector3d sunDirSim = (sys.Sun.Position - focus).normalized;

            if (sunLight)
            {
                sunLight.transform.rotation = Quaternion.LookRotation(-Coords.ToUnity(sunDirSim));
                sunLight.intensity = sunIntensity * SceneIllumination;
                sunLight.enabled = SceneIllumination > 0.001f;
            }

            UpdateSkyLight(sys, focus, sunDirSim);

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
    
        /// <summary>Fraction of the solar disc visible from a point: angular overlap of the Sun with each occulting body.</summary>
        static double SunlightAt(SolarSystem sys, Vector3d point)
        {
            Vector3d toSun = sys.Sun.Position - point;
            double sunDist = toSun.magnitude;
            double sunAngular = Math.Asin(Math.Min(1, sys.Sun.Radius / sunDist));
            double light = 1;
            foreach (var b in sys.Bodies)
            {
                if (b == sys.Sun) continue;
                Vector3d toB = b.Position - point;
                double dB = toB.magnitude;
                if (dB > sunDist || dB < b.Radius) continue;
                double bodyAngular = Math.Asin(Math.Min(1, b.Radius / dB));
                double sep = Vector3d.Angle(toB, toSun);
                if (sep >= bodyAngular + sunAngular) continue;
                double frac = sep <= bodyAngular - sunAngular ? 1 : 1 - (sep - (bodyAngular - sunAngular)) / (2 * sunAngular);
                light = Math.Min(light, 1 - Math.Max(0, Math.Min(1, frac)));
            }
            return light;
        }

        /// <summary>
        /// HDRP: ambient light and reflections come from the physically based sky (see PlanetSky), so this only
        /// publishes the photometric scale of the self-lit shaders and the Sun direction. The scale is the luminance
        /// (nits) of a white Lambertian surface facing the Sun, E / π: those shaders output reflectance relative to
        /// that surface, which puts them on the same footing as everything HDRP lights with the Sun.
        /// </summary>
        void UpdateSkyLight(SolarSystem sys, Vector3d focus, Vector3d sunDirSim)
        {
            Shader.SetGlobalFloat("_KarmanUnlitScale", sunIntensity / Mathf.PI);
            Vector3 d = Coords.ToUnity(sunDirSim).normalized;
            Shader.SetGlobalVector("_KarmanSunDirWS", new Vector4(d.x, d.y, d.z, SceneIllumination));
            if (skyProbe) skyProbe.enabled = false;
            if (cameraFill)
            {
                var cam = Camera.main;
                if (cam) cameraFill.transform.rotation = Quaternion.LookRotation(cam.transform.forward + cam.transform.up * 0.3f);
                // Fade in above the dense atmosphere (the sky lights the shadows lower down).
                var earth = sys.Get(SolarSystemData.Earth);
                double alt = (focus - earth.Position).magnitude - earth.Radius;
                float k = Mathf.Clamp01((float)((alt - 20_000) / 40_000));
                bool nearEarth = alt < 5e6;
                cameraFill.intensity = cameraFillLux * (nearEarth ? k : 1f);
                cameraFill.enabled = cameraFill.intensity > 1f;
            }
        }
    }
}
