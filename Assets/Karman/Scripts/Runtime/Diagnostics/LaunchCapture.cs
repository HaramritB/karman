using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Karman.Cameras;
using Karman.Flight;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Diagnostics
{
    /// <summary>
    /// Automated launch verification (player flag -karmanCapture, flight started from the VAB): engages the ascent
    /// assistant, logs telemetry at 2 Hz, captures screenshots at mission events and writes launch_report.txt.
    /// </summary>
    public sealed class LaunchCapture : MonoBehaviour
    {
        public OrbitCamera orbitCamera;
        public AscentAutopilot autopilot;

        IEnumerator Start()
        {
            string dir = CommandLine.Value("-karmanCapture");
            var sim = SimulationController.Instance;
            yield return null;
            var v = sim.ActiveVessel;
            if (string.IsNullOrEmpty(dir) || v == null || !v.IsLaunchFromPad) yield break;
            var csv = new StringBuilder("t,alt_m,surf_speed,orb_speed,vert_speed,ap_km,pe_km,mach,q_kpa,thrust_kN,mass_t,stage,phase\n");
            var report = new StringBuilder();
            v.Event += e => report.AppendLine($"T+{v.MissionElapsed:F1}s  {e}  (alt {v.Trajectory.Altitude / 1000:F2} km, v {v.Trajectory.SurfaceVelocity.magnitude:F0} m/s)");

            for (int i = 0; i < 60; i++) yield return null;
            orbitCamera.Focus(OrbitCamera.TargetKind.Vessel, null, v.Length * 1.3 + 15); orbitCamera.yaw = 200; orbitCamera.pitch = 6; orbitCamera.Snap();
            for (int i = 0; i < 30; i++) yield return null;
            Shot(dir, "30_on_pad");
            yield return null;
            orbitCamera.distance = v.Length * 4 + 80; orbitCamera.pitch = 3; orbitCamera.Snap();
            for (int i = 0; i < 20; i++) yield return null;
            Shot(dir, "31_pad_wide");
            yield return null;

            if (CommandLine.Has("-karmanPadOnly")) { yield return Wait(5); Application.Quit(); yield break; }
            autopilot.Engage();
            sim.SetWarp(SimulationController.MaxPhysicsWarpIndex); // 5× physics warp to keep the run short
            float next = 0; bool shotLiftoff = false, shotMaxQ = false, shotSep = false, shotSpace = false;
            int startStage = v.Vehicle.NextStage;
            float t0 = Time.realtimeSinceStartup;
            double maxQSeen = 0;
            while (autopilot.State != AscentAutopilot.Phase.Done && autopilot.State != AscentAutopilot.Phase.Failed && Time.realtimeSinceStartup - t0 < 1500)
            {
                if (Time.realtimeSinceStartup >= next)
                {
                    next = Time.realtimeSinceStartup + 0.5f;
                    var tr = v.Trajectory;
                    csv.AppendLine(string.Join(",", new[]
                    {
                        v.MissionElapsed.ToString("F1", CultureInfo.InvariantCulture), tr.Altitude.ToString("F0", CultureInfo.InvariantCulture),
                        tr.SurfaceVelocity.magnitude.ToString("F1", CultureInfo.InvariantCulture), tr.OrbitalSpeed.ToString("F1", CultureInfo.InvariantCulture),
                        tr.VerticalSpeed.ToString("F1", CultureInfo.InvariantCulture), (tr.ApoapsisAltitude / 1000).ToString("F2", CultureInfo.InvariantCulture),
                        (tr.PeriapsisAltitude / 1000).ToString("F2", CultureInfo.InvariantCulture), v.Mach.ToString("F2", CultureInfo.InvariantCulture),
                        (v.DynamicPressure / 1000).ToString("F2", CultureInfo.InvariantCulture), (v.CurrentThrust / 1000).ToString("F0", CultureInfo.InvariantCulture),
                        (v.Vehicle.Mass / 1000).ToString("F2", CultureInfo.InvariantCulture), v.Vehicle.NextStage.ToString(), autopilot.State.ToString(),
                    }));
                }
                if (sim.WarpIndex < SimulationController.MaxPhysicsWarpIndex && autopilot.State == AscentAutopilot.Phase.Ascent) sim.SetWarp(SimulationController.MaxPhysicsWarpIndex);
                if (!shotLiftoff && v.Launched && v.MissionElapsed > 4) { shotLiftoff = true; orbitCamera.distance = v.Length * 2.2 + 30; orbitCamera.yaw = 160; orbitCamera.pitch = -8; orbitCamera.Snap(); yield return Wait(10); Shot(dir, "32_liftoff"); }
                if (v.DynamicPressure > maxQSeen) maxQSeen = v.DynamicPressure;
                if (!shotMaxQ && v.Launched && v.DynamicPressure < maxQSeen * 0.97 && maxQSeen > 5000) { shotMaxQ = true; orbitCamera.yaw = 120; orbitCamera.pitch = 10; orbitCamera.Snap(); yield return Wait(8); Shot(dir, "33_max_q"); }
                if (!shotSep && v.Vehicle.NextStage < startStage - 1) { shotSep = true; yield return Wait(40); orbitCamera.distance = v.Length * 3 + 60; orbitCamera.yaw = 210; orbitCamera.pitch = 5; orbitCamera.Snap(); yield return Wait(10); Shot(dir, "34_after_separation"); }
                if (!shotSpace && v.Trajectory.Altitude > 120_000) { shotSpace = true; orbitCamera.distance = v.Length * 1.6 + 20; orbitCamera.yaw = 190; orbitCamera.pitch = 4; orbitCamera.Snap(); yield return Wait(10); Shot(dir, "35_upper_stage_space"); }
                yield return null;
            }
            yield return Wait(30);
            var o = v.Trajectory.Orbit;
            orbitCamera.distance = v.Length * 2.5 + 30; orbitCamera.yaw = 200; orbitCamera.pitch = 28; orbitCamera.Snap();
            yield return Wait(20);
            Shot(dir, "36_in_orbit");
            yield return Wait(4);
            report.AppendLine($"RESULT {autopilot.State}: Ap {v.Trajectory.ApoapsisAltitude / 1000:F1} km, Pe {v.Trajectory.PeriapsisAltitude / 1000:F1} km, inc {v.Trajectory.EquatorialInclination * Mathf.Rad2Deg:F2}°, e {o.eccentricity:F4}");
            report.AppendLine($"Max Q {v.MaxQ / 1000:F1} kPa; Δv remaining {v.Vehicle.Analyse().TotalDeltaV:F0} m/s; mass {v.Vehicle.Mass / 1000:F2} t; MET {v.MissionElapsed:F0} s");
            // Stability check: coast one full orbit on rails and confirm the periapsis is unchanged and above the atmosphere.
            double pe0 = v.Trajectory.PeriapsisAltitude;
            sim.AdvanceOnRails(o.Period);
            report.AppendLine($"After one orbit on rails: Pe {v.Trajectory.PeriapsisAltitude / 1000:F3} km (Δ {(v.Trajectory.PeriapsisAltitude - pe0):F3} m), altitude {v.Trajectory.Altitude / 1000:F1} km, crashed {v.Crashed}");
            File.WriteAllText(Path.Combine(dir, "launch_report.txt"), report.ToString());
            File.WriteAllText(Path.Combine(dir, "launch_telemetry.csv"), csv.ToString());
            yield return Wait(5);
            Application.Quit();
        }

        static IEnumerator Wait(int frames) { for (int i = 0; i < frames; i++) yield return null; }
        static void Shot(string dir, string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
    }
}
