using Karman.Flight;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Audio
{
    /// <summary>
    /// Flight soundscape. Engine roar is loud, bright and distance-attenuated in air and becomes a muffled,
    /// low "felt through the structure" hum in vacuum; wind follows dynamic pressure; separations and touchdowns
    /// play transients. Volumes follow the Settings master volume.
    /// </summary>
    public sealed class FlightAudio : MonoBehaviour
    {
        AudioSource engineSrc, windSrc, oneShots;
        AudioLowPassFilter engineLp;
        VesselController vessel;

        void Start()
        {
            engineSrc = Source("Engine", ProceduralAudio.Engine, true);
            engineLp = engineSrc.gameObject.AddComponent<AudioLowPassFilter>();
            windSrc = Source("Wind", ProceduralAudio.Wind, true);
            oneShots = Source("Events", null, false);
            vessel = SimulationController.Instance.ActiveVessel;
            if (vessel != null) vessel.Event += OnEvent;
        }

        AudioSource Source(string name, AudioClip clip, bool loop)
        {
            var go = new GameObject("Audio " + name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0; s.volume = 0;
            if (loop) s.Play();
            return s;
        }

        void OnEvent(string e)
        {
            float master = Settings.QualitySettingsStore.MasterVolume;
            if (e.StartsWith("Separation") || e.StartsWith("Stage")) oneShots.PlayOneShot(ProceduralAudio.Thump, 0.8f * master);
            else if (e.StartsWith("Touchdown") || e.StartsWith("Impact")) oneShots.PlayOneShot(ProceduralAudio.Thud, master);
        }

        void Update()
        {
            if (vessel == null || vessel.Trajectory == null) return;
            float master = Settings.QualitySettingsStore.MasterVolume;
            var cam = Camera.main;
            float dist = cam ? Vector3.Distance(cam.transform.position, vessel.transform.position) : 50;
            float p = Mathf.Clamp01((float)(vessel.Air.Pressure / 101_325.0));
            float thrustFrac = vessel.CurrentMaxThrust > 0 ? (float)(vessel.CurrentThrust / vessel.CurrentMaxThrust) : 0;
            float power = Mathf.Clamp01((float)(vessel.CurrentThrust / 2e6)) * 0.5f + (vessel.CurrentThrust > 0 ? 0.5f : 0);
            // Air carries sound (with distance falloff); in vacuum only a structure-borne hum remains.
            float airborne = p * power / (1 + dist / (60 + vessel.Length * 4));
            float structure = (1 - p) * power * 0.18f;
            engineSrc.volume = Mathf.Clamp01(airborne + structure) * master;
            engineSrc.pitch = Mathf.Lerp(0.75f, 1.1f, thrustFrac) * Mathf.Lerp(0.8f, 1f, p);
            engineLp.cutoffFrequency = Mathf.Lerp(380f, 9000f, Mathf.Sqrt(p));
            float q = (float)(vessel.DynamicPressure / 40_000.0);
            windSrc.volume = Mathf.Clamp01(q) * 0.6f * master;
            windSrc.pitch = 0.8f + Mathf.Clamp01(q) * 0.6f;
        }
    }
}
