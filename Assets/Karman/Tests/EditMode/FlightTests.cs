using System;
using System.Linq;
using Karman.Core;
using Karman.Core.Vehicles;
using NUnit.Framework;

namespace Karman.Tests
{
    public class AtmosphereTests
    {
        // U.S. Standard Atmosphere 1976 table values.
        [TestCase(0, 101325, 1.2250, 288.15)]
        [TestCase(5000, 54048, 0.73643, 255.68)]
        [TestCase(11000, 22700, 0.36480, 216.78)]
        [TestCase(20000, 5529.3, 0.088910, 216.65)]
        [TestCase(30000, 1197.0, 0.018410, 226.51)]
        [TestCase(50000, 79.779, 0.0010269, 270.65)]
        [TestCase(70000, 5.2209, 0.000082829, 219.59)]
        public void MatchesUS1976(double z, double p, double rho, double t)
        {
            var s = EarthAtmosphere.Sample(z);
            Assert.AreEqual(p, s.Pressure, p * 0.01, "pressure");
            Assert.AreEqual(rho, s.Density, rho * 0.01, "density");
            Assert.AreEqual(t, s.Temperature, 0.5, "temperature");
        }

        [Test]
        public void VacuumAboveCutoff()
        {
            Assert.AreEqual(0, EarthAtmosphere.Sample(150_000).Pressure);
            Assert.Less(EarthAtmosphere.Sample(100_000).Pressure, 0.1);
            Assert.AreEqual(340.3, EarthAtmosphere.Sample(0).SpeedOfSound, 0.5);
        }
    }

    public class FlightVehicleTests
    {
        static readonly Func<string, PartDefinition> L = PartLibrary.Lookup(PartLibrary.CreateStarter());

        static Craft TwoStage()
        {
            var c = new Craft();
            var upper = c.Add(L("tank.kerolox.37x16"), -1, null, null);
            var mvac = c.Add(L("engine.kr1v"), upper.Id, "bottom", "top");
            var dec = c.Add(L("decoupler.37"), mvac.Id, "bottom", "top");
            var lower = c.Add(L("tank.kerolox.37x32"), dec.Id, "bottom", "top");
            c.Add(L("engine.kr1.cluster9"), lower.Id, "bottom", "top");
            AutoStaging.Apply(c, L);
            return c;
        }

        [Test]
        public void StagingBurnAndSeparationMatchAnalysis()
        {
            var craft = TwoStage();
            var v = new FlightVehicle(craft, L);
            double expected = StagingAnalysis.Analyse(craft, L).TotalDeltaV;
            Assert.AreEqual(0, v.BurningEngines().Count);
            v.Stage(); // ignite cluster
            Assert.AreEqual(1, v.BurningEngines().Count);
            double dv = 0, dt = 0.05;
            for (int guard = 0; guard < 200_000; guard++)
            {
                if (v.BurningEngines().Count == 0) { if (v.NextStage < 0) break; v.Stage(); continue; }
                double m = v.Mass;
                double f = v.Burn(1, dt, 0, out _, out _);
                dv += f / m * dt;
            }
            TestContext.WriteLine($"Integrated Δv {dv:F1} m/s vs analysis {expected:F1} m/s; final mass {v.Mass:F0} kg");
            Assert.AreEqual(expected, dv, expected * 3e-3);
            Assert.AreEqual(2, v.AttachedParts.Count());
        }

        [Test]
        public void SeparationDetachesLowerStage()
        {
            var v = new FlightVehicle(TwoStage(), L);
            double m0 = v.Mass;
            v.Stage();
            var detached = v.Stage();
            Assert.AreEqual(3, detached.Count);
            Assert.Less(v.Mass, m0 * 0.35);
        }

        [Test]
        public void CapsuleOnlyCraftHasLitManoeuvringEngine()
        {
            var c = new Craft(); c.Add(L("pod.gemini"), -1, null, null); AutoStaging.Apply(c, L);
            var v = new FlightVehicle(c, L);
            Assert.AreEqual(1, v.BurningEngines().Count);
            Assert.AreEqual(267.2, v.Analyse().TotalDeltaV, 0.5);
        }
    }
}
