using System;
using Karman.Core;
using NUnit.Framework;

namespace Karman.Tests
{
    public class OrbitalMechanicsTests
    {
        const double MuEarth = 3.986004418e14;
        const double REarth = 6_371_000;

        static void AssertVec(Vector3d expected, Vector3d actual, double tol, string what)
        {
            double err = (expected - actual).magnitude;
            Assert.LessOrEqual(err, tol, $"{what}: |Δ| = {err:G6} (expected {expected}, got {actual})");
        }

        [TestCase(7_000_000, 0.001, 28.5, 40, 60, 10)]
        [TestCase(26_600_000, 0.74, 63.4, 270, 280, 200)]   // Molniya
        [TestCase(42_164_000, 0.0, 0.0, 0, 0, 33)]          // GEO, circular + equatorial
        [TestCase(-20_000_000, 1.35, 30, 15, 45, 0.5)]      // hyperbolic
        public void StateElementsRoundTrip(double a, double e, double iDeg, double raanDeg, double argpDeg, double nuDeg)
        {
            var orbit = KeplerOrbit.FromElements(MuEarth, a, e, iDeg * Constants.Deg2Rad, raanDeg * Constants.Deg2Rad,
                argpDeg * Constants.Deg2Rad, KeplerOrbit.TrueToMean(nuDeg * Constants.Deg2Rad, e), 0);
            var s = orbit.StateAt(0);
            var back = KeplerOrbit.FromState(s, MuEarth, 0);
            var s2 = back.StateAt(0);
            AssertVec(s.position, s2.position, 1e-3, "position");
            AssertVec(s.velocity, s2.velocity, 1e-6, "velocity");
            Assert.AreEqual(a, back.semiMajorAxis, Math.Abs(a) * 1e-9);
            Assert.AreEqual(e, back.eccentricity, 1e-9);
        }

        [Test]
        public void CircularSpeedAt400km()
        {
            double r = REarth + 400_000;
            double v = Math.Sqrt(MuEarth / r);
            Assert.AreEqual(7672.6, v, 1.0);
            var orbit = KeplerOrbit.FromState(new StateVector(new Vector3d(r, 0, 0), new Vector3d(0, v, 0)), MuEarth, 0);
            Assert.AreEqual(5544.9, orbit.Period, 1.0); // 2π√(r³/μ) ≈ 92.4 min
        }

        [Test]
        public void KeplerPropagationReturnsAfterOnePeriod()
        {
            var orbit = KeplerOrbit.FromElements(MuEarth, 8_000_000, 0.2, 0.5, 1, 2, 0.3, 0);
            var s0 = orbit.StateAt(0);
            var s1 = orbit.StateAt(orbit.Period);
            AssertVec(s0.position, s1.position, 1e-3, "position after one period");
        }

        [Test]
        public void RK4MatchesAnalyticKeplerOverOneOrbit()
        {
            var orbit = KeplerOrbit.FromElements(MuEarth, 6_771_000, 0.01, 0.9, 0.2, 0.4, 0, 0);
            var s = orbit.StateAt(0);
            double T = orbit.Period, dt = 1.0, t = 0;
            AccelerationFunc acc = (tt, r, v) => Integrators.PointMassGravity(r, MuEarth);
            int n = (int)Math.Round(T / dt); dt = T / n;
            for (int i = 0; i < n; i++) { s = Integrators.RK4(s, t, dt, acc); t += dt; }
            var exact = orbit.StateAt(t);
            double err = (exact.position - s.position).magnitude;
            TestContext.WriteLine($"RK4 dt=1s one-orbit position error: {err:E3} m");
            Assert.Less(err, 0.01);
        }

        [Test]
        public void EnergyAndAngularMomentumConservedOverTenOrbits()
        {
            var orbit = KeplerOrbit.FromElements(MuEarth, 7_500_000, 0.1, 0.5, 0, 0, 0, 0);
            var s = orbit.StateAt(0);
            double E0 = s.velocity.sqrMagnitude / 2 - MuEarth / s.position.magnitude;
            Vector3d h0 = Vector3d.Cross(s.position, s.velocity);
            AccelerationFunc acc = (tt, r, v) => Integrators.PointMassGravity(r, MuEarth);
            double dt = 2.0, t = 0, maxRelE = 0;
            int n = (int)(10 * orbit.Period / dt);
            var sv = s;
            for (int i = 0; i < n; i++)
            {
                sv = Integrators.VelocityVerlet(sv, t, dt, acc); t += dt;
                double E = sv.velocity.sqrMagnitude / 2 - MuEarth / sv.position.magnitude;
                maxRelE = Math.Max(maxRelE, Math.Abs((E - E0) / E0));
            }
            double hErr = (Vector3d.Cross(sv.position, sv.velocity) - h0).magnitude / h0.magnitude;
            TestContext.WriteLine($"Verlet dt=2s, 10 orbits: max |ΔE/E| = {maxRelE:E3}, |Δh|/h = {hErr:E3}");
            Assert.Less(maxRelE, 1e-6);
            Assert.Less(hErr, 1e-10);
        }

        [Test]
        public void HyperbolicEscapeKeepsExcessSpeed()
        {
            double r = REarth + 300_000;
            double vEsc = Math.Sqrt(2 * MuEarth / r);
            var s = new StateVector(new Vector3d(r, 0, 0), new Vector3d(0, vEsc + 1000, 0));
            var orbit = KeplerOrbit.FromState(s, MuEarth, 0);
            Assert.IsTrue(orbit.IsHyperbolic);
            double vInf = Math.Sqrt(-MuEarth / orbit.semiMajorAxis);
            var far = orbit.StateAt(30 * 86400);
            double vFar = far.velocity.magnitude;
            double expected = Math.Sqrt(vInf * vInf + 2 * MuEarth / far.position.magnitude);
            Assert.AreEqual(expected, vFar, 1e-3);
        }
    }
}
