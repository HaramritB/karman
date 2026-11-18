using System;
using Karman.Core;
using NUnit.Framework;

namespace Karman.Tests
{
    /// <summary>Reference vectors: JPL Horizons, 2026-01-01 00:00 TT, ICRF, Sun- or Earth-centred, km.</summary>
    public class EphemerisTests
    {
        static readonly double T = (2461041.5 - Constants.JD_J2000) * Constants.SecondsPerDay;

        static void Compare(string name, Vector3d model, Vector3d horizonsKm, double maxAngleDeg, double maxRangeFrac)
        {
            var reference = horizonsKm * 1000.0;
            double ang = Vector3d.Angle(model, reference) * Constants.Rad2Deg;
            double range = Math.Abs(model.magnitude - reference.magnitude) / reference.magnitude;
            TestContext.WriteLine($"{name}: angular error {ang * 60:F2}′, range error {range * 100:F4}% ({(model - reference).magnitude / 1000:F0} km)");
            Assert.Less(ang, maxAngleDeg, $"{name} angle");
            Assert.Less(range, maxRangeFrac, $"{name} range");
        }

        [Test] public void Earth() => Compare("Earth", Ephemeris.EarthHeliocentricState(T).position, new Vector3d(-2.607213844577914E+07, 1.328317036504154E+08, 5.757989896238839E+07), 0.05, 1e-4);
        [Test] public void Mars() => Compare("Mars", Ephemeris.PlanetHeliocentricPosition(SolarSystemData.Mars, T), new Vector3d(5.094999446032593E+07, -1.881441184966180E+08, -8.767143592366028E+07), 0.1, 1e-3);
        [Test] public void Jupiter() => Compare("Jupiter", Ephemeris.PlanetHeliocentricPosition(SolarSystemData.Jupiter, T), new Vector3d(-2.534193453679105E+08, 6.754687036170200E+08, 2.956929149480466E+08), 0.2, 2e-3);
        [Test] public void Neptune() => Compare("Neptune", Ephemeris.PlanetHeliocentricPosition(SolarSystemData.Neptune, T), new Vector3d(4.468805898190681E+09, 1.128251730294690E+08, -6.506909900472178E+07), 0.2, 2e-3);
        [Test] public void MoonGeocentric() => Compare("Moon", Ephemeris.MoonGeocentricPosition(T), new Vector3d(1.443257275722619E+05, 2.895841575142238E+05, 1.601589229878832E+05), 0.1, 1e-3);

        [Test]
        public void EarthVelocity()
        {
            var v = Ephemeris.EarthHeliocentricState(T).velocity / 1000.0;
            var reference = new Vector3d(-2.978893116573639E+01, -4.951144771903349E+00, -2.146136369776051E+00);
            Assert.Less((v - reference).magnitude, 0.05, $"Earth velocity {v} vs {reference} km/s");
        }

        [Test]
        public void GmstAtJ2000()
        {
            // At 2000-01-01 12:00 UT1, GMST = 280.46061837°.
            double gmst = TimeSystem.GreenwichMeanSiderealTime(TimeSystem.DeltaTSeconds) * Constants.Rad2Deg;
            Assert.AreEqual(280.46061837, gmst, 1e-6);
        }

        [Test]
        public void UtcRoundTrip()
        {
            var utc = new DateTime(2026, 11, 18, 6, 0, 0, DateTimeKind.Utc);
            double t = TimeSystem.FromUtc(utc);
            Assert.AreEqual(utc, TimeSystem.ToUtc(t));
        }

        [Test]
        public void SpheresOfInfluence()
        {
            var sys = SolarSystem.CreateDefault(T);
            double moonSoi = sys.Get(SolarSystemData.Moon).SphereOfInfluence / 1000;
            double earthSoi = sys.Get(SolarSystemData.Earth).SphereOfInfluence / 1000;
            TestContext.WriteLine($"SOI Moon {moonSoi:F0} km, Earth {earthSoi:F0} km");
            Assert.AreEqual(66_100, moonSoi, 1_000);
            Assert.AreEqual(925_000, earthSoi, 10_000);
        }

        [Test]
        public void MoonFacesEarth()
        {
            var sys = SolarSystem.CreateDefault(T);
            var moon = sys.Get(SolarSystemData.Moon); var earth = sys.Get(SolarSystemData.Earth);
            Vector3d primeMeridian = moon.Orientation.Column(0);
            double ang = Vector3d.Angle(primeMeridian, earth.Position - moon.Position) * Constants.Rad2Deg;
            Assert.Less(ang, 7.0); // equals the pole-tilt projection; libration is ignored
        }
    }
}
