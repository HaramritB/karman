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
        [Tooltip("Realtime probe at the focus point: sky and ground reflections on metal and paint near the surface.")]
        public ReflectionProbe skyProbe;
        public Color spaceAmbient = new Color(0.012f, 0.014f, 0.02f);

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
    
        /// <summary>
        /// Inside an atmosphere the sky lights everything: blue from above, a bright horizon band and warm ground
        /// bounce, all oriented to the local vertical (the world frame is camera-relative, so Unity's +Y is not up).
        /// The reflection probe is enabled only where there is a sky worth reflecting.
        /// </summary>
        void UpdateSkyLight(SolarSystem sys, Vector3d focus, Vector3d sunDirSim)
        {
            CelestialBody near = null; double bestAlt = double.MaxValue;
            foreach (var b in sys.Bodies)
            {
                if (b.Definition.Atmosphere == null) continue;
                double alt = (focus - b.Position).magnitude - b.Radius;
                if (alt < bestAlt) { bestAlt = alt; near = b; }
            }
            float air = near == null ? 0 : Mathf.Clamp01(1f - (float)(bestAlt / 70_000.0));
            air *= air;
            var sh = new UnityEngine.Rendering.SphericalHarmonicsL2();
            sh.AddAmbientLight(spaceAmbient);
            if (air > 0.001f)
            {
                Vector3d upSim = (focus - near.Position).normalized;
                float elev = (float)Vector3d.Dot(upSim, sunDirSim);
                float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.12f, 0.3f, elev)) * Illumination;
                float dusk = Mathf.Clamp01(1 - Mathf.Abs(elev) * 4) * day;
                Vector3 up = Coords.ToUnity(upSim).normalized;
                Color sky = Color.Lerp(new Color(0.20f, 0.33f, 0.58f), new Color(0.45f, 0.36f, 0.42f), dusk) * day * air;
                Color horizon = Color.Lerp(new Color(0.42f, 0.48f, 0.56f), new Color(0.62f, 0.42f, 0.30f), dusk) * day * air;
                Color ground = new Color(0.10f, 0.10f, 0.07f) * day * air;
                sh.AddAmbientLight(horizon * 0.55f);
                sh.AddDirectionalLight(up, sky, 1.6f);
                sh.AddDirectionalLight(-up, ground, 1.0f);
                RenderSettings.reflectionIntensity = Mathf.Lerp(0.35f, 1f, air * day);
            }
            else RenderSettings.reflectionIntensity = 0.35f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom;
            RenderSettings.ambientProbe = sh;
            if (skyProbe)
            {
                bool on = air > 0.01f;
                if (skyProbe.enabled != on) skyProbe.enabled = on;
                skyProbe.transform.position = Vector3.zero; // focus sits at the floating origin
            }
        }
}
}
