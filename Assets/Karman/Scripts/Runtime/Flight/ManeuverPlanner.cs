using System;
using Karman.Core;
using Karman.Rendering;
using Karman.Simulation;
using UnityEngine;

namespace Karman.Flight
{
    /// <summary>
    /// Mission-planning state for the active vessel: one maneuver node, the predicted patched-conic trajectory before
    /// and after it, the selected target body, and the node-execution autopilot (warp to the burn, steer along the
    /// node's Δv vector, cut off when the remaining Δv is spent).
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ManeuverPlanner : MonoBehaviour
    {
        public static ManeuverPlanner Instance { get; private set; }
        public VesselController vessel;

        public ManeuverNode Node { get; private set; }
        public CelestialBody Target { get; set; }
        public TrajectoryPrediction Current { get; private set; }
        public TrajectoryPrediction Planned { get; private set; }
        public bool Executing { get; private set; }
        public double BurnTime { get; private set; }
        public double RemainingDeltaV { get; private set; }
        public string Status { get; private set; } = "";
        public event Action Changed;

        Vector3d burnVector;      // inertial Δv at the node (fixed during execution)
        double deliveredDv, dvAtStart;
        bool burning;
        float nextPredict;
        int predictVersion, predictedVersion = -1;

        void Awake() => Instance = this;

        /// <summary>World direction of the node's burn (null without a node).</summary>
        public Vector3? BurnDirection
        {
            get
            {
                if (Node == null) return null;
                if (Executing) return Coords.ToUnity(burnVector.normalized);
                var seg = Current?.SegmentAt(Node.UT);
                if (seg == null || Node.DeltaV < 1e-6) return null;
                return Coords.ToUnity(Node.DeltaVector(seg.Orbit.StateAt(Node.UT)).normalized);
            }
        }

        // ---------------------------------------------------------------- node editing

        public void CreateNode(double ut)
        {
            Node = new ManeuverNode { UT = Math.Max(ut, SimulationController.Instance.Time + 10) };
            Invalidate();
        }

        public void CreateNodeAtApsis(bool apoapsis)
        {
            var o = vessel.Trajectory.Orbit;
            double t = SimulationController.Instance.Time;
            if (apoapsis && o.IsHyperbolic) return;
            CreateNode(t + o.TimeToTrueAnomaly(apoapsis ? Math.PI : 0, t));
        }

        public void DeleteNode() { Node = null; Executing = false; Planned = null; if (vessel) vessel.AttitudeTarget = null; Invalidate(); }

        public void Adjust(double prograde, double normal, double radial, double dt)
        {
            if (Node == null || Executing) return;
            Node.Prograde += prograde; Node.Normal += normal; Node.Radial += radial;
            Node.UT = Math.Max(SimulationController.Instance.Time + 5, Node.UT + dt);
            Invalidate();
        }

        public void SetNode(ManeuverNode node) { Node = node; Invalidate(); }

        /// <summary>Circularise at the next apoapsis (Δv from vis-viva).</summary>
        public void PlanCircularisation()
        {
            var o = vessel.Trajectory.Orbit;
            if (o.IsHyperbolic) { Status = "Hyperbolic: no apoapsis"; return; }
            double t = SimulationController.Instance.Time;
            double ra = o.ApoapsisRadius;
            double dv = Math.Sqrt(o.mu / ra) - Math.Sqrt(o.mu * (2 / ra - 1 / o.semiMajorAxis));
            Node = new ManeuverNode { UT = t + o.TimeToTrueAnomaly(Math.PI, t), Prograde = dv };
            Status = $"Circularise at Ap: {dv:F1} m/s";
            Invalidate();
        }

        public void PlanLunarTransfer()
        {
            var sim = SimulationController.Instance;
            var tr = vessel.Trajectory;
            if (tr.Reference.Id != SolarSystemData.Earth) { Status = "Lunar transfer needs an Earth orbit"; Changed?.Invoke(); return; }
            var moon = sim.System.Get(SolarSystemData.Moon);
            var node = TransferPlanner.PlanLunarTransfer(sim.System, tr.Reference, moon, tr.RelativeState, sim.Time, 100_000);
            if (node == null) { Status = "No lunar encounter found within one orbit (plane too far from the Moon's?)"; Changed?.Invoke(); return; }
            Node = node; Target = moon;
            Status = $"Trans-lunar injection: {node.DeltaV:F0} m/s";
            Invalidate();
        }

        void Invalidate() { predictVersion++; nextPredict = 0; Changed?.Invoke(); }

        // ---------------------------------------------------------------- prediction

        void Update()
        {
            if (vessel == null || vessel.Trajectory == null) return;
            if (Time.unscaledTime >= nextPredict || predictVersion != predictedVersion) Predict();
            if (Executing) Execute();
        }

        void Predict()
        {
            var sim = SimulationController.Instance;
            var tr = vessel.Trajectory;
            if (vessel.Landed || tr.Altitude < 0) { Current = null; Planned = null; return; }
            nextPredict = Time.unscaledTime + 0.4f;
            predictedVersion = predictVersion;
            var o = tr.Orbit;
            double horizon = o.IsHyperbolic || o.ApoapsisRadius > tr.Reference.SphereOfInfluence * 0.5 ? 12 * 86400 : o.Period * 1.02;
            Current = TrajectoryPredictor.Predict(sim.System, tr.Reference, tr.RelativeState, sim.Time, horizon, Target);
            if (Node != null && !Executing)
            {
                if (Node.UT < sim.Time) { Node = null; Planned = null; Changed?.Invoke(); return; }
                var seg = Current.SegmentAt(Node.UT);
                if (seg == null) return;
                var s = seg.Orbit.StateAt(Node.UT);
                var after = new StateVector(s.position, s.velocity + Node.DeltaVector(s));
                var po = KeplerOrbit.FromState(after, seg.Body.Mu, Node.UT);
                double ph = po.IsHyperbolic || po.ApoapsisRadius > seg.Body.SphereOfInfluence * 0.5 ? 12 * 86400 : po.Period * 1.02;
                Planned = TrajectoryPredictor.Predict(sim.System, seg.Body, after, Node.UT, ph, Target);
                EstimateBurn();
            }
        }

        void EstimateBurn()
        {
            var v = vessel.Vehicle;
            double thrust = vessel.CurrentMaxThrustEstimate();
            var a = v.Analyse(0);
            double isp = a.Stages.Count > 0 && a.Stages[0].Isp > 0 ? a.Stages[0].Isp : 300;
            BurnTime = ManeuverNode.BurnTime(Node.DeltaV, v.Mass, thrust, isp);
        }

        // ---------------------------------------------------------------- execution

        public void BeginExecution()
        {
            if (Node == null || Executing) return;
            var seg = Current?.SegmentAt(Node.UT);
            if (seg == null) return;
            burnVector = Node.DeltaVector(seg.Orbit.StateAt(Node.UT));
            deliveredDv = 0; dvAtStart = vessel.DeliveredDeltaV; burning = false;
            RemainingDeltaV = Node.DeltaV;
            Executing = true;
            Status = "Executing node";
            Changed?.Invoke();
        }

        void Execute()
        {
            var sim = SimulationController.Instance;
            double t = sim.Time;
            double start = Node.UT - BurnTime / 2;
            Vector3 dir = Coords.ToUnity(burnVector.normalized);
            vessel.AttitudeTarget = dir;
            if (t < start - 1)
            {
                vessel.Throttle = 0;
                double wait = start - t;
                int want = wait > 3600 ? 7 : wait > 600 ? 6 : wait > 180 ? 5 : wait > 60 ? 3 : wait > 20 ? 1 : 0;
                if (vessel.InAtmosphere) want = Math.Min(want, SimulationController.MaxPhysicsWarpIndex);
                if (sim.WarpIndex != want) sim.SetWarp(want);
                return;
            }
            if (sim.WarpIndex > 0) sim.SetWarp(0);
            // Wait until pointed within 5°.
            float err = Vector3.Angle(vessel.Attitude * Vector3.up, dir);
            if (!burning && err > 5) { vessel.Throttle = 0; return; }
            if (!burning) { burning = true; dvAtStart = vessel.DeliveredDeltaV; }
            deliveredDv = vessel.DeliveredDeltaV - dvAtStart;
            RemainingDeltaV = Node.DeltaV - deliveredDv;
            double maxAccel = vessel.CurrentMaxThrustEstimate() / vessel.Vehicle.Mass;
            vessel.Throttle = (float)Math.Max(0.05, Math.Min(1, RemainingDeltaV / Math.Max(0.1, maxAccel * 2)));
            if (vessel.Vehicle.BurningEngines().Count == 0 && vessel.Vehicle.NextStage >= 0 && deliveredDv > 0) vessel.StageNext();
            if (RemainingDeltaV <= 0.2 || vessel.Vehicle.PropellantRemaining <= 0)
            {
                vessel.Throttle = 0;
                vessel.AttitudeTarget = null;
                vessel.Sas = SasMode.Stability;
                Executing = false;
                Status = RemainingDeltaV <= 0.2 ? $"Burn complete (residual {Math.Max(0, RemainingDeltaV):F1} m/s)" : "Out of propellant";
                Node = null; Planned = null;
                Invalidate();
            }
        }
    }
}
