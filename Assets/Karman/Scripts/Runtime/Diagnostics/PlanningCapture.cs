using System.Collections;
using System.IO;
using System.Text;
using Karman.Cameras;
using Karman.Core;
using Karman.Flight;
using Karman.Map;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Diagnostics
{
    /// <summary>
    /// Automated Phase 4 check (-karmanCapture dir -karmanPlan, orbit sandbox): plan a node, compare the predicted
    /// orbit with the orbit achieved by the node-execution autopilot, then plan a trans-lunar injection and capture the
    /// predicted Moon encounter on the map.
    /// </summary>
    public sealed class PlanningCapture : MonoBehaviour
    {
        public OrbitCamera orbitCamera;
        public MapView mapView;
        public ManeuverPlanner planner;

        IEnumerator Start()
        {
            string dir = CommandLine.Value("-karmanCapture");
            if (string.IsNullOrEmpty(dir) || !CommandLine.Has("-karmanPlan")) yield break;
            var sim = SimulationController.Instance;
            yield return Wait(30);
            var v = sim.ActiveVessel;
            var log = new StringBuilder();

            // 1. Node at apoapsis, +40 m/s prograde.
            planner.CreateNodeAtApsis(true);
            planner.Adjust(40, 0, 0, 0);
            yield return Wait(20);
            var planned = planner.Planned.Segments[0].Orbit;
            double predAp = planned.ApoapsisRadius - v.Trajectory.Reference.Radius, predPe = planned.PeriapsisRadius - v.Trajectory.Reference.Radius;
            log.AppendLine($"Node: +40 m/s prograde at Ap, burn {planner.BurnTime:F1} s; predicted Ap {predAp / 1000:F3} km, Pe {predPe / 1000:F3} km");
            mapView.SetActive(true);
            mapView.SetView("vessel", 2.6e7, 30, 62);
            yield return Wait(40);
            Shot(dir, "40_map_node");
            yield return null;
            mapView.SetActive(false);

            // 2. Execute and compare.
            planner.BeginExecution();
            float t0 = Time.realtimeSinceStartup;
            bool shotBurn = false;
            while (planner.Executing && Time.realtimeSinceStartup - t0 < 400)
            {
                if (!shotBurn && v.CurrentThrust > 0) { shotBurn = true; orbitCamera.Focus(OrbitCamera.TargetKind.Vessel, null, 24); orbitCamera.yaw = 200; orbitCamera.pitch = 12; orbitCamera.Snap(); yield return Wait(15); Shot(dir, "41_node_burn"); }
                yield return null;
            }
            var o = v.Trajectory.Orbit;
            double ap = o.ApoapsisRadius - v.Trajectory.Reference.Radius, pe = o.PeriapsisRadius - v.Trajectory.Reference.Radius;
            log.AppendLine($"Executed: {planner.Status}; achieved Ap {ap / 1000:F3} km (Δ {(ap - predAp):F0} m), Pe {pe / 1000:F3} km (Δ {(pe - predPe):F0} m)");

            // 3. Trans-lunar injection plan and encounter.
            planner.PlanLunarTransfer();
            yield return Wait(30);
            var pred = planner.Planned;
            log.AppendLine(pred != null && pred.EncounterBody != null
                ? $"TLI plan: {planner.Node.DeltaV:F0} m/s; Moon SOI in {(pred.EncounterTime - sim.Time) / 3600:F1} h; periselene {pred.EncounterPeriapsisAltitude / 1000:F1} km"
                : $"TLI plan: {planner.Status}");
            mapView.SetActive(true);
            mapView.SetView(SolarSystemData.Earth, 9.0e8, 25, 60);
            yield return Wait(45);
            Shot(dir, "42_map_lunar_transfer");
            yield return null;
            mapView.SetView(SolarSystemData.Moon, 9.0e7, 25, 55);
            yield return Wait(45);
            Shot(dir, "43_map_lunar_encounter");
            yield return Wait(3);
            File.WriteAllText(Path.Combine(dir, "planning_report.txt"), log.ToString());
            Application.Quit();
        }

        static IEnumerator Wait(int frames) { for (int i = 0; i < frames; i++) yield return null; }
        static void Shot(string dir, string name) => ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
    }
}
