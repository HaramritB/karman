using System.Collections;
using System.IO;
using System.Text;
using Karman.Cameras;
using Karman.Core;
using Karman.Flight;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Diagnostics
{
    /// <summary>
    /// Automated Phase 5 check (-karmanScenario lunar-orbit -karmanCapture dir): lunar orbit → powered descent →
    /// touchdown on the LOLA terrain → ascent to lunar orbit, with screenshots and lunar_report.txt.
    /// </summary>
    public sealed class LunarCapture : MonoBehaviour
    {
        public OrbitCamera orbitCamera;
        public LunarAutopilot autopilot;
        public ManeuverPlanner planner;

        IEnumerator Start()
        {
            string dir = CommandLine.Value("-karmanCapture");
            if (string.IsNullOrEmpty(dir) || CommandLine.Value("-karmanScenario") != "lunar-orbit") yield break;
            var sim = SimulationController.Instance;
            yield return Wait(30);
            var v = sim.ActiveVessel;
            var log = new StringBuilder();
            v.Event += e => log.AppendLine($"T+{v.MissionElapsed:F0}s  {e}");
            var quad = FindAnyObjectByType<TerrainQuadtree>();

            View(v.Length * 1.8 + 12, 200, 18); yield return Wait(60);
            Shot(dir, "50_lunar_orbit");
            View(v.Length * 1.8 + 12, 160, -8); yield return Wait(30);
            Shot(dir, "51_lunar_orbit_limb");

            autopilot.Engage();
            float t0 = Time.realtimeSinceStartup;
            bool s1 = false, s2 = false, s3 = false;
            double peakFps = 0;
            while (autopilot.State != LunarAutopilot.Phase.Landed && autopilot.State != LunarAutopilot.Phase.Failed && Time.realtimeSinceStartup - t0 < 1800)
            {
                double h = v.GroundClearance;
                if (!s1 && h < 9000 && autopilot.State == LunarAutopilot.Phase.Descent) { s1 = true; View(v.Length * 2 + 20, 140, 12); yield return Wait(25); Shot(dir, "52_powered_descent"); log.AppendLine($"Descent at {h:F0} m: {quad?.VisiblePatches} terrain patches visible, {quad?.BuiltPatches} built, {1f / Time.smoothDeltaTime:F0} fps"); }
                if (!s2 && h < 1500) { s2 = true; View(v.Length * 1.6 + 14, 120, 6); yield return Wait(25); Shot(dir, "53_low_gate"); log.AppendLine($"Low gate {h:F0} m: {quad?.VisiblePatches} patches, {1f / Time.smoothDeltaTime:F0} fps"); }
                if (!s3 && h < 150) { s3 = true; View(v.Length * 1.4 + 10, 100, 4); yield return Wait(15); Shot(dir, "54_terminal"); }
                yield return null;
            }
            log.AppendLine($"Descent result: {autopilot.State}; touchdown v={v.LastTouchdown.x:F2} m/s vertical, {v.LastTouchdown.y:F2} m/s horizontal, tilt {v.LastTouchdown.z:F1}°");
            v.Trajectory.LatLon(out double lat, out double lon);
            log.AppendLine($"Site {lat * Constants.Rad2Deg:F3}°, {lon * Constants.Rad2Deg:F3}°, terrain height {v.TerrainHeight:F0} m; propellant left {v.Vehicle.PropellantRemaining:F0} kg");
            yield return Wait(60);
            View(v.Length * 2.2 + 25, 150, 10); yield return Wait(40);
            Shot(dir, "55_landed");
            View(v.Length * 6 + 80, 200, 6); yield return Wait(40);
            Shot(dir, "56_landed_wide");

            if (autopilot.State == LunarAutopilot.Phase.Landed)
            {
                autopilot.Engage(); // ascent
                t0 = Time.realtimeSinceStartup;
                bool a1 = false;
                while (autopilot.State == LunarAutopilot.Phase.Ascent && Time.realtimeSinceStartup - t0 < 1500)
                {
                    if (!a1 && v.GroundClearance > 300) { a1 = true; View(v.Length * 2 + 25, 180, -4); yield return Wait(15); Shot(dir, "57_ascent"); }
                    yield return null;
                }
                var o = v.Trajectory.Orbit;
                log.AppendLine($"Ascent result: {autopilot.State}; orbit {(o.PeriapsisRadius - v.Trajectory.Reference.Radius) / 1000:F1} × {(o.ApoapsisRadius - v.Trajectory.Reference.Radius) / 1000:F1} km; propellant left {v.Vehicle.PropellantRemaining:F0} kg");
                View(v.Length * 2 + 15, 210, 20); yield return Wait(30);
                Shot(dir, "58_back_in_orbit");
            }
            File.WriteAllText(Path.Combine(dir, "lunar_report.txt"), log.ToString());
            yield return Wait(5);
            Application.Quit();
        }

        void View(double dist, float yaw, float pitch)
        {
            orbitCamera.Focus(OrbitCamera.TargetKind.Vessel, null, dist);
            orbitCamera.yaw = yaw; orbitCamera.pitch = pitch; orbitCamera.Snap();
        }

        static IEnumerator Wait(int frames) { for (int i = 0; i < frames; i++) yield return null; }
        static void Shot(string dir, string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
    }
}
