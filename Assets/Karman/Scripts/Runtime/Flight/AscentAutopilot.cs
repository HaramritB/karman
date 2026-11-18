using System;
using Karman.Core;
using Karman.Core.Vehicles;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Karman.Flight
{
    /// <summary>
    /// Optional ascent assistant (G). Drives the same controls a player has (throttle, staging, attitude target)
    /// from <see cref="AscentGuidance"/>, the guidance law that the headless pad-to-orbit tests verify.
    /// It time-warps through the coast to apoapsis when the vehicle is above the atmosphere.
    /// </summary>
    public sealed class AscentAutopilot : MonoBehaviour
    {
        public VesselController vessel;
        public double targetApoapsis = 190_000;
        public double targetPeriapsis = 170_000;
        public double headingDeg = 90;
        public float slewRateDegPerSimSecond = 3f;

        public enum Phase { Off, Ascent, Coast, Circularise, Done, Failed }
        public Phase State { get; private set; } = Phase.Off;
        public string Status => guidance != null && State != Phase.Off ? guidance.Phase.ToString() : State.ToString();
        AscentGuidance guidance;
        double stageCooldown;
        Vector3 smoothed;
        double lastSimTime;

        public void Engage()
        {
            guidance = new AscentGuidance { TargetApoapsis = targetApoapsis, TargetPeriapsis = targetPeriapsis, HeadingDeg = headingDeg };
            State = Phase.Ascent;
            smoothed = vessel.Attitude * Vector3.up;
            lastSimTime = SimulationController.Instance.Time;
            if (vessel.Vehicle.BurningEngines().Count == 0 && vessel.Vehicle.NextStage >= 0) vessel.StageNext();
        }

        public void Disengage() { State = Phase.Off; guidance = null; vessel.AttitudeTarget = null; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) { if (State == Phase.Off || State == Phase.Done) Engage(); else Disengage(); }
            if (guidance == null || vessel == null || vessel.Trajectory == null) return;
            var sim = SimulationController.Instance;
            if (vessel.Crashed) { State = Phase.Failed; vessel.AttitudeTarget = null; guidance = null; return; }

            var tr = vessel.Trajectory;
            var s = tr.RelativeState;
            var body = tr.Reference;
            double maxAccel = MaxAccel();
            guidance.Update(s.position, s.velocity, tr.SurfaceVelocity, body.Orientation.Column(2), body.Mu, body.Radius, maxAccel, out double throttle, out Vector3d dir);

            // Auto-staging when a lit engine burns out (or nothing is lit yet).
            double simDt = Math.Max(0, sim.Time - lastSimTime); lastSimTime = sim.Time;
            stageCooldown -= simDt;
            if (vessel.Vehicle.NextStage >= 0 && stageCooldown <= 0 && throttle > 0)
            {
                bool anyLit = false, anyDead = false;
                var burning = vessel.Vehicle.BurningEngines();
                foreach (var id in vessel.Vehicle.LitEngines) { if (!vessel.Vehicle.Attached.Contains(id)) continue; anyLit = true; if (!burning.Contains(id)) anyDead = true; }
                if (!anyLit || anyDead) { vessel.StageNext(); stageCooldown = 1.0; }
            }

            vessel.Throttle = (float)throttle;
            Vector3 target = Coords.ToUnity(dir);
            smoothed = Vector3.RotateTowards(smoothed, target, slewRateDegPerSimSecond * Mathf.Deg2Rad * (float)Math.Max(simDt, Time.deltaTime), 0);
            vessel.AttitudeTarget = smoothed;

            switch (guidance.Phase)
            {
                case AscentGuidance.Mode.CoastToApoapsis:
                {
                    State = Phase.Coast;
                    var o = tr.Orbit;
                    double tAp = o.IsHyperbolic ? 0 : o.TimeToTrueAnomaly(Math.PI, sim.Time);
                    double rAp = o.ApoapsisRadius;
                    double dv = Math.Max(0, Math.Sqrt(o.mu / rAp) - Math.Sqrt(Math.Max(0, o.mu * (2 / rAp - 1 / o.semiMajorAxis))));
                    double lead = dv / Math.Max(0.05, maxAccel) * 0.5 + 90;
                    // Warp through the coast once out of the atmosphere; come back to real time before the burn.
                    int want = vessel.InAtmosphere ? Math.Min(sim.WarpIndex, SimulationController.MaxPhysicsWarpIndex)
                             : tAp > lead + 600 ? 6 : tAp > lead + 120 ? 5 : tAp > lead ? 3 : 0;
                    if (want != sim.WarpIndex) sim.SetWarp(want);
                    break;
                }
                case AscentGuidance.Mode.Circularise:
                    State = Phase.Circularise;
                    if (sim.WarpIndex > SimulationController.MaxPhysicsWarpIndex) sim.SetWarp(0);
                    break;
                case AscentGuidance.Mode.Complete:
                    State = Phase.Done;
                    vessel.Throttle = 0;
                    vessel.AttitudeTarget = null;
                    vessel.Sas = SasMode.Prograde;
                    sim.SetWarp(0);
                    guidance = null;
                    break;
                default:
                    State = Phase.Ascent;
                    break;
            }
        }

        double MaxAccel()
        {
            var v = vessel.Vehicle;
            double t = 0;
            foreach (var id in v.BurningEngines()) t += v.Def(v.Craft.Get(id)).Engine.ThrustAt(vessel.Air.Pressure);
            if (t > 0) return t / v.Mass;
            var a = v.Analyse(vessel.Air.Pressure);
            foreach (var st in a.Stages) if (st.Thrust > 0) return st.Thrust / st.StartMass;
            return 0.1;
        }
    }
}
