using Karman.Simulation;
using UnityEngine;

namespace Karman.Flight
{
    /// <summary>Pad exhaust cloud: billowing smoke/steam pushed out of the flame trench while the vehicle is low and burning.</summary>
    [DefaultExecutionOrder(115)]
    public sealed class LaunchEffects : MonoBehaviour
    {
        public ParticleSystem smoke;
        public float activeBelow = 400f;   // m above the pad

        void LateUpdate()
        {
            var v = SimulationController.Instance?.ActiveVessel;
            if (v == null || smoke == null || v.Trajectory == null) return;
            bool on = v.CurrentThrust > 0 && v.Trajectory.Altitude - v.StandoffHeight - v.padHeight < activeBelow && v.IsLaunchFromPad;
            var em = smoke.emission;
            float height = (float)(v.Trajectory.Altitude - v.StandoffHeight - v.padHeight);
            em.rateOverTime = on ? Mathf.Lerp(260f, 30f, Mathf.Clamp01(height / activeBelow)) : 0f;
            if (on && !smoke.isPlaying) smoke.Play();
        }
    }
}
