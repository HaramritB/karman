using Karman.Core;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Karman.Rendering
{
    /// <summary>
    /// Keeps HDRP's physically based sky (and its aerial perspective and volumetric clouds) centred on Earth. The world is
    /// rendered around a floating origin, so Earth's centre moves every frame; HDRP takes it in kilometres.
    /// Near the Moon the sky simply sees Earth far away, so space stays black with stars.
    /// </summary>
    [DefaultExecutionOrder(125)]
    public sealed class PlanetSky : MonoBehaviour
    {
        public Volume volume;
        VisualEnvironment env;
        VolumetricClouds clouds;
        Fog fog;

        void Start()
        {
            if (volume == null) volume = GetComponent<Volume>();
            if (volume == null || volume.sharedProfile == null) return;
            volume.sharedProfile.TryGet(out env);
            volume.sharedProfile.TryGet(out clouds);
            volume.sharedProfile.TryGet(out fog);
        }

        void LateUpdate()
        {
            var sim = SimulationController.Instance;
            if (sim == null || env == null) return;
            var earth = sim.System.Get(SolarSystemData.Earth);
            Vector3d c = earth.Position - RenderFrame.Origin;
            Vector3 km = Coords.ToUnity(c * 0.001);
            env.planetCenter.Override(km);
            env.planetRadius.Override((float)(earth.Radius * 0.001));
            double alt = c.magnitude - earth.Radius;
            // Clouds and height fog only matter inside the lower atmosphere.
            if (clouds != null) clouds.enable.Override(alt < 60_000);
            if (fog != null) fog.enabled.Override(alt < 150_000);
            if (Time.frameCount % 60 == 10)
            {
                var sun = RenderSettings.sun != null ? RenderSettings.sun : FindAnyObjectByType<Light>();
                var cam = Camera.main;
                Debug.Log($"[PlanetSky] alt {alt:F0} m centre {km} camPos {(cam ? cam.transform.position : Vector3.zero)} sun {(sun ? $"{sun.name} {sun.intensity} {sun.lightUnit} {sun.color} dir {sun.transform.forward} en {sun.enabled}" : "none")}");
            }
        }
    }
}
