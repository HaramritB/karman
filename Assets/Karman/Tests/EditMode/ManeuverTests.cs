using System;
using Karman.Core;
using NUnit.Framework;

namespace Karman.Tests
{
    public class ManeuverTests
    {
        static readonly double T0 = TimeSystem.FromUtc(new DateTime(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc));

        static (SolarSystem sys, CelestialBody earth, StateVector s) Leo(double alt = 300_000, double incDeg = 28.6)
        {
            var sys = SolarSystem.CreateDefault(T0);
            var earth = sys.Get(SolarSystemData.Earth);
            double r = earth.Radius + alt, v = Math.Sqrt(earth.Mu / r), i = incDeg * Constants.Deg2Rad;
            return (sys, earth, new StateVector(new Vector3d(r, 0, 0), new Vector3d(0, v * Math.Cos(i), v * Math.Sin(i))));
        }

        [Test]
        public void ProgradeNodeMatchesVisViva()
        {
            var (sys, earth, s) = Leo();
            var node = new ManeuverNode { UT = T0, Prograde = 100 };
            var after = new StateVector(s.position, s.velocity + node.DeltaVector(s));
            var o = KeplerOrbit.FromState(after, earth.Mu, T0);
            double r = s.position.magnitude, v = s.velocity.magnitude + 100;
            double a = 1 / (2 / r - v * v / earth.Mu);
            Assert.AreEqual(2 * a - r, o.ApoapsisRadius, 1.0);
            Assert.AreEqual(r, o.PeriapsisRadius, 1.0);
        }

        [Test]
        public void NormalBurnChangesInclinationByTwoVSinHalfAngle()
        {
            var (sys, earth, s) = Leo(300_000, 28.6);
            double v = s.velocity.magnitude, di = 10 * Constants.Deg2Rad;
            // Pure rotation of the velocity by Δi about r: Δv = 2 v sin(Δi/2), split into normal and retrograde parts.
            var node = new ManeuverNode { UT = T0, Normal = -v * Math.Sin(di), Prograde = -v * (1 - Math.Cos(di)) };
            Assert.AreEqual(2 * v * Math.Sin(di / 2), node.DeltaV, 1e-6);
            var after = new StateVector(s.position, s.velocity + node.DeltaVector(s));
            double inc = Vector3d.Angle(Vector3d.Cross(after.position, after.velocity), Vector3d.unitZ) * Constants.Rad2Deg;
            Assert.AreEqual(18.6, inc, 1e-6);
            Assert.AreEqual(v, after.velocity.magnitude, 1e-6);
        }

        [Test]
        public void BurnTimeFollowsRocketEquation()
        {
            double t = ManeuverNode.BurnTime(1000, 10_000, 20_000, 300);
            double mdot = 20_000 / (300 * Constants.StandardGravity);
            double mf = 10_000 * Math.Exp(-1000 / (300 * Constants.StandardGravity));
            Assert.AreEqual((10_000 - mf) / mdot, t, 1e-9);
        }

        [Test]
        public void ClosedOrbitPredictionIsOneSegment()
        {
            var (sys, earth, s) = Leo();
            var p = TrajectoryPredictor.Predict(sys, earth, s, T0, 86400);
            Assert.AreEqual(1, p.Segments.Count);
            Assert.IsFalse(p.Impacts);
            Assert.AreEqual(SegmentEnd.Horizon, p.Segments[0].EndReason);
        }

        [Test]
        public void EscapeTrajectoryLeavesEarthSoi()
        {
            var (sys, earth, s) = Leo();
            var node = new ManeuverNode { UT = T0, Prograde = 3600 };
            var p = TrajectoryPredictor.Predict(sys, earth, new StateVector(s.position, s.velocity + node.DeltaVector(s)), T0, 30 * 86400, null);
            Assert.AreEqual(SegmentEnd.SoiExit, p.Segments[0].EndReason);
            Assert.AreSame(sys.Sun, p.Segments[1].Body);
        }

        [Test]
        public void DeorbitBurnPredictsImpact()
        {
            var (sys, earth, s) = Leo();
            var node = new ManeuverNode { UT = T0, Prograde = -150 };
            var p = TrajectoryPredictor.Predict(sys, earth, new StateVector(s.position, s.velocity + node.DeltaVector(s)), T0, 86400);
            Assert.IsTrue(p.Impacts);
        }

        [Test]
        public void LunarTransferPlannerFindsEncounterAndMatchesIntegration()
        {
            var (sys, earth, s) = Leo(300_000, 28.6);
            var moon = sys.Get(SolarSystemData.Moon);
            // Put the parking orbit in the Moon's plane so a coplanar injection exists.
            var moonState = Ephemeris.MoonGeocentricState(T0);
            Vector3d h = Vector3d.Cross(moonState.position, moonState.velocity).normalized;
            Vector3d rHat = Vector3d.Cross(h, moonState.position.normalized).normalized * -1;
            double r = earth.Radius + 300_000;
            var leo = new StateVector(rHat * r, Vector3d.Cross(h, rHat) * Math.Sqrt(earth.Mu / r));
            var node = TransferPlanner.PlanLunarTransfer(sys, earth, moon, leo, T0, 100_000);
            Assert.IsNotNull(node, "planner should find a lunar encounter");
            var orbit = KeplerOrbit.FromState(leo, earth.Mu, T0);
            var pred = TransferPlanner.PredictAfter(sys, earth, orbit, node, moon);
            TestContext.WriteLine($"TLI Δv {node.DeltaV:F1} m/s at T+{node.UT - T0:F0}s; Moon SOI entry after {(pred.EncounterTime - node.UT) / 3600:F1} h; periselene {pred.EncounterPeriapsisAltitude / 1000:F1} km");
            Assert.AreSame(moon, pred.EncounterBody);
            Assert.AreEqual(3130, node.DeltaV, 120);
            Assert.AreEqual(100, pred.EncounterPeriapsisAltitude / 1000, 30);

            // Cross-check the SOI entry time against the step-by-step trajectory used in flight.
            var traj = new SpacecraftTrajectory(sys, earth, orbit.StateAt(node.UT), node.UT);
            var st = traj.RelativeState;
            traj.SetState(earth, new StateVector(st.position, st.velocity + node.DeltaVector(st)), node.UT);
            double t = node.UT;
            while (traj.Reference == earth && t < node.UT + 10 * 86400) { t += 30; sys.SetTime(t); traj.Advance(30, Vector3d.zero); }
            TestContext.WriteLine($"Integrated SOI entry differs by {t - pred.EncounterTime:F0} s");
            Assert.AreSame(moon, traj.Reference);
            Assert.AreEqual(pred.EncounterTime, t, 60);
        }
    }
}
