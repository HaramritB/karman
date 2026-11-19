using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Karman.Cameras;
using Karman.Core;
using Karman.Map;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Diagnostics
{
    /// <summary>
    /// Automated verification run: <c>-karmanCapture &lt;dir&gt;</c> steps through scripted views, measures frame
    /// times, writes screenshots + a JSON/markdown report, then quits. Used for milestone evidence.
    /// </summary>
    public sealed class AutoCapture : MonoBehaviour
    {
        public OrbitCamera orbitCamera;
        public MapView mapView;
        double burnStartProp, burnStartAp; string burnLog;

        struct Shot { public string name; public Action setup; public Action after; }

        void Start()
        {
            string dir = CommandLine.Value("-karmanCapture");
            if (string.IsNullOrEmpty(dir) || CommandLine.Has("-karmanPlan")) { enabled = false; return; }
            if (SimulationController.Instance.ActiveVessel != null && SimulationController.Instance.ActiveVessel.IsLaunchFromPad) { enabled = false; return; }
            Directory.CreateDirectory(dir);
            StartCoroutine(Run(dir));
        }

        IEnumerator Run(string dir)
        {
            var sim = SimulationController.Instance;
            yield return null; yield return null;
            var shots = new List<Shot>
            {
                new Shot { name = "01_vessel_closeup", setup = () => Vessel(16, 35, 8) },
                new Shot { name = "02_vessel_earth_limb", setup = () => Vessel(45, 165, 4) },
                new Shot { name = "03_vessel_wide", setup = () => Vessel(900, 200, 22) },
                new Shot { name = "03b_prograde_burn", setup = () => { Vessel(28, 215, 10); sim.ActiveVessel.Sas = Karman.Flight.SasMode.Prograde; sim.ActiveVessel.Throttle = 1; burnStartProp = sim.ActiveVessel.Vehicle.PropellantRemaining; burnStartAp = sim.ActiveVessel.Trajectory.ApoapsisAltitude; },
                           after = () => { var v = sim.ActiveVessel; v.Throttle = 0; burnLog = $"\"burn\": {{\"propellantUsedKg\": {burnStartProp - v.Vehicle.PropellantRemaining:F3}, \"apoapsisGainM\": {v.Trajectory.ApoapsisAltitude - burnStartAp:F1}, \"thrustN\": {v.CurrentMaxThrust:F0}" + "},"; } },
                new Shot { name = "04_earth_terminator", setup = () => Body(SolarSystemData.Earth, 2.6e7, 125, 18) },
                new Shot { name = "05_earth_nightside", setup = () => Body(SolarSystemData.Earth, 1.5e7, 35, 25) },
                new Shot { name = "06_moon", setup = () => Body(SolarSystemData.Moon, 6.0e6, 150, 10) },
                new Shot { name = "07_map_earth_system", setup = () => Map("vessel", 3.0e7, 20, 60) },
                new Shot { name = "08_map_earth_moon", setup = () => Map(SolarSystemData.Earth, 1.2e9, 20, 55) },
                new Shot { name = "09_map_inner_solar", setup = () => Map(SolarSystemData.Sun, 7.0e11, 20, 50) },
                new Shot { name = "10_map_solar_system", setup = () => Map(SolarSystemData.Sun, 1.1e13, 20, 45) },
            };
            var report = new StringBuilder();
            report.AppendLine("{");
            report.AppendLine($"  \"device\": \"{SystemInfo.deviceModel} / {SystemInfo.graphicsDeviceName} / {SystemInfo.graphicsDeviceType}\",");
            report.AppendLine($"  \"resolution\": \"{Screen.width}x{Screen.height}\",");
            report.AppendLine($"  \"utc\": \"{TimeSystem.ToUtc(sim.Time):O}\",");
            report.AppendLine("  \"shots\": [");
            for (int s = 0; s < shots.Count; s++)
            {
                var shot = shots[s];
                shot.setup();
                orbitCamera.Snap();
                for (int i = 0; i < 45; i++) yield return null;     // settle (texture streaming, smoothing)
                float t0 = Time.realtimeSinceStartup; int frames = 0; float worst = 0;
                while (Time.realtimeSinceStartup - t0 < 2.0f) { float f = Time.unscaledDeltaTime; worst = Mathf.Max(worst, f); frames++; yield return null; }
                float avgMs = (Time.realtimeSinceStartup - t0) * 1000f / frames;
                string path = Path.Combine(dir, shot.name + ".png");
                ScreenCapture.CaptureScreenshot(path);
                yield return null; yield return null;
                shot.after?.Invoke();
                var v = sim.ActiveVessel;
                string comma = s < shots.Count - 1 ? "," : "";
                report.Append("    {").Append($"\"name\": \"{shot.name}\", \"avgFrameMs\": {avgMs:F2}, \"fps\": {1000f / avgMs:F1}, \"worstFrameMs\": {worst * 1000f:F1}, ")
                      .Append($"\"altKm\": {v.Trajectory.Altitude / 1000:F3}, \"apKm\": {v.Trajectory.ApoapsisAltitude / 1000:F3}, \"peKm\": {v.Trajectory.PeriapsisAltitude / 1000:F3}")
                      .AppendLine("}" + comma);
                Debug.Log($"[KarmanCapture] {shot.name}: {avgMs:F2} ms ({1000f / avgMs:F1} fps)");
            }
            report.AppendLine("  ],");
            if (burnLog != null) report.AppendLine("  " + burnLog);

            // Orbit propagation check inside the running game: one full period on rails at high warp.
            var tr = sim.ActiveVessel.Trajectory;
            double period = tr.Orbit.Period;
            Vector3d r0 = tr.RelativeState.position; double sma0 = tr.Orbit.semiMajorAxis;
            sim.AdvanceOnRails(period);
            double closure = (tr.RelativeState.position - r0).magnitude;
            report.AppendLine($"  \"onRailsPeriodSeconds\": {period:F3}, \"onRailsClosureMetres\": {closure:F6}, \"semiMajorAxisDriftMetres\": {Math.Abs(tr.Orbit.semiMajorAxis - sma0):F6}");
            report.AppendLine("}");
            File.WriteAllText(Path.Combine(dir, "report.json"), report.ToString());
            Debug.Log("[KarmanCapture] done");
            yield return null;
            Application.Quit();
        }

        void Vessel(double dist, float yaw, float pitch)
        {
            if (MapView.IsActive) mapView.SetActive(false);
            orbitCamera.Focus(OrbitCamera.TargetKind.Vessel, null, dist);
            orbitCamera.yaw = yaw; orbitCamera.pitch = pitch;
        }

        void Body(string id, double dist, float yaw, float pitch)
        {
            if (MapView.IsActive) mapView.SetActive(false);
            orbitCamera.Focus(OrbitCamera.TargetKind.Body, id, dist);
            orbitCamera.yaw = yaw; orbitCamera.pitch = pitch;
        }

        void Map(string id, double dist, float yaw, float pitch)
        {
            if (!MapView.IsActive) mapView.SetActive(true);
            mapView.SetView(id, dist, yaw, pitch);
        }
    }
}
