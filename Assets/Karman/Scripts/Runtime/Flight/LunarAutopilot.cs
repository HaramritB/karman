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
    /// Landing and ascent assistant for airless bodies (G while orbiting or landed on the Moon). In orbit: retrograde
    /// deorbit burn to a 15 km periapsis, coast (time-warped), then <see cref="LandingGuidance"/> to touchdown. On the
    /// surface: separate the descent stage and fly <see cref="AscentGuidance"/> to a ~50 × 60 km orbit. The same guidance
    /// laws are verified by the headless lunar mission tests.
    /// </summary>
    public sealed class LunarAutopilot : MonoBehaviour
    {
        public VesselController vessel;
        public double deorbitPeriapsis = 15_000;

        public enum Phase { Off, Deorbit, Descent, Landed, Ascent, Orbit, Failed }
        public Phase State { get; private set; } = Phase.Off;
        public string Status => State == Phase.Descent && landing != null ? $"Descent · {landing.Phase}" : State == Phase.Ascent && ascent != null ? $"Ascent · {ascent.Phase}" : State.ToString();

        LandingGuidance landing;
        AscentGuidance ascent;
        double deorbitTarget;
        Vector3 smoothed;
        double lastSim;

        public bool Applicable => vessel != null && vessel.Trajectory != null && vessel.Trajectory.Reference.Definition.Atmosphere == null && vessel.Trajectory.Reference.Parent != null;

        public void Engage()
        {
            if (!Applicable) return;
            smoothed = vessel.Attitude * Vector3.up;
            lastSim = SimulationController.Instance.Time;
            if (vessel.Landed)
            {
                if (vessel.Vehicle.NextStage >= 0) vessel.StageNext(); // drop the descent stage, light the ascent engine
                ascent = new AscentGuidance { TargetApoapsis = 60_000, TargetPeriapsis = 45_000, HeadingDeg = 90, TurnStartSpeed = 15, TurnShapeAltitude = 6_000, InsertionSteeringAbove = 2_500, CoastAbove = 0 };
                State = Phase.Ascent;
            }
            else
            {
                var tr = vessel.Trajectory;
                if (tr.PeriapsisAltitude > deorbitPeriapsis + 2_000) State = Phase.Deorbit;
                else State = Phase.Descent;
                landing = new LandingGuidance();
                if (vessel.Vehicle.BurningEngines().Count == 0 && vessel.Vehicle.NextStage >= 0) vessel.StageNext();
            }
        }

        public void Disengage() { State = Phase.Off; vessel.AttitudeTarget = null; vessel.Throttle = 0; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame && Applicable)
            { if (State == Phase.Off || State == Phase.Landed || State == Phase.Orbit) Engage(); else Disengage(); }
            if (State == Phase.Off || State == Phase.Landed || State == Phase.Orbit || vessel == null) return;
            if (vessel.Crashed) { State = Phase.Failed; vessel.AttitudeTarget = null; return; }
            var sim = SimulationController.Instance;
            var tr = vessel.Trajectory;
            var body = tr.Reference;
            var s = tr.RelativeState;
            double simDt = Math.Max(Time.deltaTime, sim.Time - lastSim); lastSim = sim.Time;
            double maxAccel = MaxAccel();

            switch (State)
            {
                case Phase.Deorbit:
                {
                    Vector3 retro = Coords.ToUnity((-s.velocity).normalized);
                    Steer(retro, 6, simDt);
                    bool aligned = Vector3.Angle(vessel.Attitude * Vector3.up, retro) < 3;
                    if (sim.WarpIndex > 0) sim.SetWarp(0);
                    vessel.Throttle = aligned ? 1 : 0;
                    if (tr.PeriapsisAltitude <= deorbitPeriapsis) { vessel.Throttle = 0; State = Phase.Descent; }
                    break;
                }
                case Phase.Descent:
                {
                    double h = tr.Altitude - vessel.TerrainHeight - vessel.StandoffHeight;
                    Vector3d vSurf = tr.SurfaceVelocity;
                    landing.Update(s.position, vSurf, body.Mu, h, maxAccel, vessel.Landed, out double thr, out Vector3d dir);
                    if (landing.Phase == LandingGuidance.Mode.Coast)
                    {
                        // Coast to powered-descent initiation with time warp while high above the terrain.
                        int want = h > 60_000 ? 5 : h > 30_000 ? 3 : h > 18_000 ? 2 : 0;
                        if (sim.WarpIndex != want) sim.SetWarp(want);
                    }
                    else if (sim.WarpIndex > 0) sim.SetWarp(0);
                    Steer(Coords.ToUnity(dir), 20, simDt);
                    // Only thrust along the commanded direction to the extent the vehicle is pointing there.
                    float err = Vector3.Angle(vessel.Attitude * Vector3.up, Coords.ToUnity(dir));
                    vessel.Throttle = (float)thr * Mathf.Clamp01(Mathf.Cos(err * Mathf.Deg2Rad) * 1.15f - 0.15f);
                    if (vessel.Landed) { State = Phase.Landed; vessel.Throttle = 0; vessel.AttitudeTarget = null; vessel.Sas = SasMode.Stability; }
                    break;
                }
                case Phase.Ascent:
                {
                    ascent.Update(s.position, s.velocity, tr.SurfaceVelocity, body.Orientation.Column(2), body.Mu, body.Radius, maxAccel, out double thr, out Vector3d dir);
                    Steer(Coords.ToUnity(dir), 8, simDt);
                    vessel.Throttle = (float)thr;
                    if (ascent.Phase == AscentGuidance.Mode.CoastToApoapsis)
                    {
                        var o = tr.Orbit;
                        double tAp = o.IsHyperbolic ? 0 : o.TimeToTrueAnomaly(Math.PI, sim.Time);
                        int want = tAp > 600 ? 5 : tAp > 150 ? 3 : 0;
                        if (sim.WarpIndex != want) sim.SetWarp(want);
                    }
                    else if (sim.WarpIndex > SimulationController.MaxPhysicsWarpIndex) sim.SetWarp(0);
                    if (ascent.Phase == AscentGuidance.Mode.Complete) { State = Phase.Orbit; vessel.Throttle = 0; vessel.AttitudeTarget = null; vessel.Sas = SasMode.Prograde; sim.SetWarp(0); }
                    break;
                }
            }
        }

        void Steer(Vector3 target, float rateDeg, double dt)
        {
            smoothed = Vector3.RotateTowards(smoothed, target, rateDeg * Mathf.Deg2Rad * (float)dt, 0);
            vessel.AttitudeTarget = smoothed;
        }

        double MaxAccel()
        {
            var v = vessel.Vehicle;
            double t = 0;
            foreach (var id in v.BurningEngines()) t += v.Def(v.Craft.Get(id)).Engine.VacuumThrust;
            return t / Math.Max(1, v.Mass);
        }
    }
}
