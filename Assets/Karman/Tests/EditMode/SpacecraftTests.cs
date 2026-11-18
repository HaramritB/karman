using System;
using Karman.Core;
using NUnit.Framework;

namespace Karman.Tests
{
    public class SpacecraftTests
    {
        static readonly double T0 = (2461041.5 - Constants.JD_J2000) * Constants.SecondsPerDay;

        [Test]
        public void StableLowEarthOrbitOnRails()
        {
            var sys = SolarSystem.CreateDefault(T0);
            var earth = sys.Get(SolarSystemData.Earth);
            double r = earth.Radius + 300_000;
            var traj = new SpacecraftTrajectory(sys, earth, new StateVector(new Vector3d(r, 0, 0), new Vector3d(0, Math.Sqrt(earth.Mu / r), 0)), T0);
            double pe0 = traj.PeriapsisAltitude;
            for (int i = 0; i < 1000; i++) { sys.SetTime(T0 + (i + 1) * 60); traj.Advance(60, Vector3d.zero); }
            Assert.AreSame(earth, traj.Reference);
            Assert.AreEqual(300_000, traj.Altitude, 1.0);
            Assert.AreEqual(pe0, traj.PeriapsisAltitude, 1e-3);
        }

        [Test]
        public void ProgradeBurnRaisesApoapsisAndMatchesRocketEquation()
        {
            var sys = SolarSystem.CreateDefault(T0);
            var earth = sys.Get(SolarSystemData.Earth);
            double r = earth.Radius + 300_000;
            var traj = new SpacecraftTrajectory(sys, earth, new StateVector(new Vector3d(r, 0, 0), new Vector3d(0, Math.Sqrt(earth.Mu / r), 0)), T0);
            var prop = new PropulsionState
            {
                DryMass = 3430, Propellant = 360, PropellantCapacity = 360, EngineCount = 2,
                Engine = new EngineSpec { Name = "OAMS", VacuumThrust = 445, VacuumIsp = 273, SeaLevelIsp = 150 },
            };
            double dvExpected = prop.DeltaV();
            double v0 = traj.OrbitalSpeed;
            double dvIntegrated = 0, dt = 0.1, t = T0;
            while (prop.Propellant > 0)
            {
                double m = prop.Mass;
                double thrust = prop.Burn(1, dt);
                double a = thrust / m;
                dvIntegrated += a * dt;
                t += dt; sys.SetTime(t);
                traj.Advance(dt, traj.RelativeState.velocity.normalized * a);
            }
            TestContext.WriteLine($"Δv rocket equation {dvExpected:F2} m/s, integrated {dvIntegrated:F2} m/s, apoapsis {traj.ApoapsisAltitude / 1000:F1} km");
            Assert.AreEqual(dvExpected, dvIntegrated, dvExpected * 2e-3);
            Assert.Greater(traj.ApoapsisAltitude, 800_000);
            Assert.Greater(traj.OrbitalSpeed, v0);
        }

        [Test]
        public void EntersLunarSphereOfInfluence()
        {
            var sys = SolarSystem.CreateDefault(T0);
            var earth = sys.Get(SolarSystemData.Earth); var moon = sys.Get(SolarSystemData.Moon);
            // Start 70,000 km from the Moon, moving toward it relative to the Moon.
            Vector3d dir = (earth.Position - moon.Position).normalized;
            var rel = new StateVector(moon.Position + dir * 70_000_000 - earth.Position, moon.Velocity - earth.Velocity - dir * 1500);
            var traj = new SpacecraftTrajectory(sys, earth, rel, T0);
            Vector3d before = traj.HeliocentricPosition;
            bool changed = false;
            traj.ReferenceChanged += (a, b) => changed = b == moon;
            double t = T0;
            for (int i = 0; i < 400 && !changed; i++) { t += 30; sys.SetTime(t); traj.Advance(30, Vector3d.zero); }
            Assert.IsTrue(changed, "vessel should transition into the Moon's SOI");
            Assert.AreSame(moon, traj.Reference);
            Assert.Less(traj.RelativeState.position.magnitude, moon.SphereOfInfluence);
        }
    }
}
