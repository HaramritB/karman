using System;
using System.IO;
using System.Linq;
using Karman.Core;
using Karman.Core.Vehicles;
using NUnit.Framework;

namespace Karman.Tests
{
    /// <summary>
    /// Headless lunar mission legs with the game's own models: real LOLA terrain, FlightVehicle propulsion/staging,
    /// LandingGuidance, AscentGuidance, the trans-Earth planner, and Earth entry with heating, lift and parachute.
    /// </summary>
    public class LunarMissionTests
    {
        static readonly Func<string, PartDefinition> L = PartLibrary.Lookup(PartLibrary.CreateStarter());
        static readonly double T0 = TimeSystem.FromUtc(new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        static LunarTerrain terrain;

        [OneTimeSetUp]
        public void LoadTerrain()
        {
            if (terrain != null) return;
            terrain = new LunarTerrain(File.ReadAllBytes("Assets/Karman/Data/Moon/moon_dem16.bytes"), 5760, 2880);
            TerrainRegistry.Register(SolarSystemData.Moon, terrain);
        }

        /// <summary>Return vehicle (Gemini + service module) on a descent stage with an extra hypergolic tank.</summary>
        public static Craft LunarLander() => SampleCrafts.LunarLander(L, false);

        static (SolarSystem sys, CelestialBody moon) Moon() { var sys = SolarSystem.CreateDefault(T0); return (sys, sys.Get(SolarSystemData.Moon)); }

        static StateVector CircularLunarOrbit(CelestialBody moon, double alt, double incDeg)
        {
            double r = moon.Radius + alt, v = Math.Sqrt(moon.Mu / r), i = incDeg * Constants.Deg2Rad;
            Vector3d pole = moon.Orientation.Column(2), x = moon.Orientation.Column(0), y = moon.Orientation.Column(1);
            Vector3d vdir = y * Math.Cos(i) + pole * Math.Sin(i);
            return new StateVector(x * r, vdir * v);
        }

        static double MaxAccel(FlightVehicle veh)
        {
            double t = 0;
            foreach (var id in veh.BurningEngines()) t += L(veh.Craft.Get(id).PartId).Engine.VacuumThrust;
            return t / veh.Mass;
        }

        public struct LandingResult { public bool Landed, Crashed; public double VVert, VHoriz, Time, PropLeft, DvUsed, Lat, Lon, TerrainH; public string Log; public FlightVehicle Vehicle; public SpacecraftTrajectory Traj; public SolarSystem Sys; }

        public static LandingResult Land()
        {
            var (sys, moon) = Moon();
            var veh = new FlightVehicle(LunarLander(), L);
            veh.Stage(); // light the descent engine (stage 1)
            veh.VerticalExtent(out float bottom, out _);
            double standoff = veh.CenterOfMass[1] - bottom;
            var s0 = CircularLunarOrbit(moon, 100_000, 10);
            var traj = new SpacecraftTrajectory(sys, moon, s0, T0);
            var log = new System.Text.StringBuilder();
            double t = 0, m0 = veh.Mass;
            // Deorbit: retrograde burn lowering periapsis to 15 km (half an orbit later).
            {
                double r = s0.position.magnitude, rp = moon.Radius + 15_000;
                double vNeed = Math.Sqrt(moon.Mu * (2 / r - 2 / (r + rp)));
                double dv = s0.velocity.magnitude - vNeed;
                double used = 0;
                while (used < dv)
                {
                    double m = veh.Mass, f = veh.Burn(1, 0.1, 0, out _, out _);
                    used += f / m * 0.1;
                    traj.Advance(0.1, (rr, vv) => -vv.normalized * (f / m), 0.1);
                    t += 0.1;
                }
                log.AppendLine($"Deorbit Δv {dv:F1} m/s; Pe {traj.PeriapsisAltitude / 1000:F1} km");
            }
            var guide = new LandingGuidance();
            Vector3d att = (-traj.RelativeState.velocity).normalized;
            while (t < 8000)
            {
                var s = traj.RelativeState;
                double h = s.position.magnitude - moon.Radius - terrain.Height((moon.Orientation.Transposed * s.position).normalized) - standoff;
                guide.Update(s.position, s.velocity, moon.Mu, h, MaxAccel(veh), false, out double thr, out Vector3d dir);
                double dt = guide.Phase == LandingGuidance.Mode.Coast && h > 30_000 ? 5 : 0.05;
                double ang = Vector3d.Angle(att, dir), step = 10 * Constants.Deg2Rad * dt;
                att = ang <= step ? dir : (att * Math.Sin(ang - step) + dir * Math.Sin(step)).normalized;
                double m = veh.Mass, f = veh.Burn(thr, dt, 0, out _, out _);
                var aT = att * (f / m);
                traj.Advance(dt, (rr, vv) => aT, dt);
                t += dt;
                var s2 = traj.RelativeState;
                double hNew = s2.position.magnitude - moon.Radius - terrain.Height((moon.Orientation.Transposed * s2.position).normalized) - standoff;
                if (((int)(t / 0.05)) % 1200 == 0) log.AppendLine($"T+{t:F0} {guide.Phase} h {hNew:F0} vs {Vector3d.Dot(s2.velocity, s2.position.normalized):F1} vh {(s2.velocity - s2.position.normalized * Vector3d.Dot(s2.velocity, s2.position.normalized)).magnitude:F1} thr {thr:F2} m {veh.Mass:F0}");
                if (hNew <= 0)
                {
                    double vs = -Vector3d.Dot(s2.velocity, s2.position.normalized);
                    double vh = (s2.velocity - s2.position.normalized * Vector3d.Dot(s2.velocity, s2.position.normalized)).magnitude;
                    var bf = (moon.Orientation.Transposed * s2.position).normalized;
                    bool ok = vs <= 3.0 && vh <= 2.5;
                    return new LandingResult
                    {
                        Landed = ok, Crashed = !ok, VVert = vs, VHoriz = vh, Time = t, PropLeft = veh.PropellantRemaining, Lat = Math.Asin(bf.z) * Constants.Rad2Deg,
                        Lon = Math.Atan2(bf.y, bf.x) * Constants.Rad2Deg, TerrainH = terrain.Height(bf), Log = log.ToString(), Vehicle = veh, Traj = traj, Sys = sys,
                        DvUsed = 311 * Constants.StandardGravity * Math.Log(m0 / veh.Mass),
                    };
                }
            }
            return new LandingResult { Log = log.ToString() };
        }

        [Test]
        public void TerrainMatchesLolaRange()
        {
            double min = double.MaxValue, max = double.MinValue;
            for (double lat = -89; lat <= 89; lat += 1) for (double lon = -180; lon < 180; lon += 1)
            { double h = terrain.DemHeight(lat * Constants.Deg2Rad, lon * Constants.Deg2Rad); min = Math.Min(min, h); max = Math.Max(max, h); }
            TestContext.WriteLine($"DEM 1° grid range {min:F0} .. {max:F0} m");
            Assert.Less(min, -6000); Assert.Greater(max, 8000);
            // Tycho's floor (~43.3°S, 11.2°W) lies well below the surrounding highlands.
            double floor = terrain.DemHeight(-43.3 * Constants.Deg2Rad, -11.36 * Constants.Deg2Rad);
            double rim = terrain.DemHeight(-41.9 * Constants.Deg2Rad, -11.36 * Constants.Deg2Rad);
            TestContext.WriteLine($"Tycho floor {floor:F0} m, north rim area {rim:F0} m");
            Assert.Less(floor, rim - 1500);
        }

        [Test]
        public void PoweredDescentToSoftTouchdown()
        {
            var r = Land();
            TestContext.WriteLine($"Touchdown {r.VVert:F2} m/s vertical, {r.VHoriz:F2} m/s horizontal at {r.Lat:F2}°, {r.Lon:F2}° (terrain {r.TerrainH:F0} m) after {r.Time:F0} s; descent Δv {r.DvUsed:F0} m/s; propellant left {r.PropLeft:F0} kg\n{r.Log}");
            Assert.IsTrue(r.Landed, "soft landing");
        }

        [Test]
        public void AscentReturnAndEntry()
        {
            var land = Land();
            Assume.That(land.Landed, "needs a landing first");
            var sys = land.Sys; var moon = sys.Get(SolarSystemData.Moon); var earth = sys.Get(SolarSystemData.Earth);
            var veh = land.Vehicle;
            veh.Stage(); // separate the descent stage, light the service engine
            Assert.AreEqual(1, veh.BurningEngines().Count);
            var traj = land.Traj;
            // Start from rest on the surface (moving with the Moon's slow rotation).
            var s = traj.RelativeState;
            Vector3d pole = moon.Orientation.Column(2); double omega = 2 * Math.PI / moon.Definition.SiderealRotationPeriod;
            traj.SetState(moon, new StateVector(s.position, Vector3d.Cross(pole * omega, s.position)), traj.Time);
            var guide = new AscentGuidance { TargetApoapsis = 60_000, TargetPeriapsis = 45_000, HeadingDeg = 90, TurnStartSpeed = 15, TurnShapeAltitude = 6_000, InsertionSteeringAbove = 2_500, CoastAbove = 0 };
            double t = 0, dt = 0.05;
            Vector3d att = s.position.normalized;
            double m0 = veh.Mass;
            while (t < 3000 && guide.Phase != AscentGuidance.Mode.Complete)
            {
                var st = traj.RelativeState;
                var vSurf = st.velocity - Vector3d.Cross(pole * omega, st.position);
                guide.Update(st.position, st.velocity, vSurf, pole, moon.Mu, moon.Radius, MaxAccel(veh), out double thr, out Vector3d dir);
                double ang = Vector3d.Angle(att, dir), step = 8 * Constants.Deg2Rad * dt;
                att = ang <= step ? dir : (att * Math.Sin(ang - step) + dir * Math.Sin(step)).normalized;
                double m = veh.Mass, f = veh.Burn(thr, dt, 0, out _, out _);
                var aT = att * (f / m);
                traj.Advance(thr > 0 ? dt : 1, (rr, vv) => aT, dt);
                t += thr > 0 ? dt : 1;
                Assert.Greater(traj.Altitude - terrain.Height((moon.Orientation.Transposed * traj.RelativeState.position).normalized), -1, "ascent hit terrain");
            }
            var o = traj.Orbit;
            double ascentDv = 314 * Constants.StandardGravity * Math.Log(m0 / veh.Mass);
            TestContext.WriteLine($"Lunar orbit {o.PeriapsisRadius / 1000 - 1737.4:F1} × {o.ApoapsisRadius / 1000 - 1737.4:F1} km after {t:F0} s, ascent Δv {ascentDv:F0} m/s, propellant left {veh.PropellantRemaining:F0} kg");
            Assert.AreEqual(AscentGuidance.Mode.Complete, guide.Phase);
            Assert.Greater(o.PeriapsisRadius - moon.Radius, 30_000);

            // Trans-Earth injection.
            sys.SetTime(traj.Time);
            var node = TransferPlanner.PlanReturnToEarth(sys, moon, earth, traj.RelativeState, traj.Time, 45_000);
            Assert.IsNotNull(node, "return planner");
            var orbit = traj.Orbit;
            double perigee = TransferPlanner.EarthPerigee(sys, moon, earth, orbit, node);
            TestContext.WriteLine($"TEI {node.DeltaV:F0} m/s; Earth perigee {perigee / 1000:F1} km");
            Assert.AreEqual(45, perigee / 1000, 15);
            Assert.Less(node.DeltaV, 314 * Constants.StandardGravity * Math.Log(veh.Mass / (veh.Mass - veh.PropellantRemaining)), "enough Δv for TEI");

            // Propagate the post-burn conic to Earth entry interface (120 km).
            var pred = TransferPlanner.PredictAfter(sys, moon, orbit, node, null, 8 * 86400);
            var earthSeg = pred.Segments.First(sg => sg.Body == earth);
            double nuEI = -TrajectoryPredictor.TrueAnomalyAtRadius(earthSeg.Orbit, earth.Radius + 120_000);
            double tEI = earthSeg.Start + earthSeg.Orbit.TimeToTrueAnomaly(nuEI, earthSeg.Start);
            var ei = earthSeg.Orbit.StateAt(tEI);
            double gamma = Math.Asin(Vector3d.Dot(ei.velocity.normalized, ei.position.normalized)) * Constants.Rad2Deg;
            TestContext.WriteLine($"Entry interface: {ei.velocity.magnitude:F0} m/s, flight-path angle {gamma:F2}°, {(tEI - node.UT) / 3600:F1} h after TEI");

            // Entry: capsule only (service module jettisoned).
            var capsule = new Craft { Name = "Capsule" }; capsule.Add(L("pod.gemini"), -1, null, null);
            var cveh = new FlightVehicle(capsule, L) { ParachuteArmed = true };
            var e = Entry(sys, earth, ei, tEI, cveh);
            TestContext.WriteLine($"Entry: peak {e.peakG:F1} g, peak heat flux {e.peakQ / 1e6:F2} MW/m², heat load {e.load / 1e6:F0} MJ/m² of {L("pod.gemini").HeatShieldCapacity / 1e6:F0}; chute at {e.chuteAlt / 1000:F1} km; splashdown {e.touch:F1} m/s after {e.time:F0} s; downrange {e.range / 1000:F0} km");
            Assert.Less(e.load, L("pod.gemini").HeatShieldCapacity, "heat shield survives");
            Assert.Less(e.peakG, 12, "survivable deceleration");
            Assert.IsTrue(e.chuteAlt > 0, "parachute deployed");
            Assert.Less(e.touch, 12, "safe splashdown");
        }

        public static (double peakG, double peakQ, double load, double chuteAlt, double touch, double time, double range) Entry(SolarSystem sys, CelestialBody earth, StateVector ei, double tEI, FlightVehicle veh)
        {
            var traj = new SpacecraftTrajectory(sys, earth, ei, tEI);
            Vector3d pole = earth.Orientation.Column(2); double omega = 2 * Math.PI / earth.Definition.SiderealRotationPeriod;
            double t = 0, peakG = 0, peakQ = 0, chuteAlt = -1; Vector3d start = ei.position;
            var shield = veh.HeatShield; var chute = veh.Parachute;
            while (t < 4000)
            {
                var s = traj.RelativeState;
                double alt = s.position.magnitude - earth.Radius;
                var air = EarthAtmosphere.Sample(alt);
                Vector3d vRel = s.velocity - Vector3d.Cross(pole * omega, s.position);
                if (veh.ParachuteArmed && !veh.ParachuteDeployed && alt < chute.ParachuteDeployAltitude && vRel.magnitude < chute.ParachuteMaxDeploySpeed) { veh.ParachuteDeployed = true; chuteAlt = alt; }
                double dtc = alt > 130_000 ? 2 : 0.05;
                double chuteCdA = veh.ParachuteCdA(dtc);
                double m = veh.Mass;
                Vector3d drag = FlightEnvironment.DragAcceleration(s.position, s.velocity, pole, omega, air, m, veh.FrontalArea, veh.DragCoefficientOverride, chuteCdA);
                double g = drag.magnitude / Constants.StandardGravity;
                double bank = veh.ParachuteDeployed ? 0 : FlightEnvironment.EntryBank(Vector3d.Dot(s.velocity, s.position.normalized), g, alt);
                Vector3d lift = veh.ParachuteDeployed ? Vector3d.zero : FlightEnvironment.LiftAcceleration(s.position, s.velocity, pole, omega, drag, veh.LiftToDrag, bank);
                double q = FlightEnvironment.StagnationHeatFlux(air.Density, vRel.magnitude, shield.NoseRadius);
                double dt = alt > 130_000 ? 2 : 0.05;
                veh.HeatLoad += q * dt; peakQ = Math.Max(peakQ, q); peakG = Math.Max(peakG, (drag + lift).magnitude / Constants.StandardGravity);
                traj.Advance(dt, (rr, vv) => FlightEnvironment.DragAcceleration(rr, vv, pole, omega, air, m, veh.FrontalArea, veh.DragCoefficientOverride, chuteCdA) + lift, dt);
                t += dt;
                if (traj.Altitude <= 0)
                {
                    var vr = traj.RelativeState.velocity - Vector3d.Cross(pole * omega, traj.RelativeState.position);
                    return (peakG, peakQ, veh.HeatLoad, chuteAlt, vr.magnitude, t, Vector3d.Angle(start, traj.RelativeState.position) * earth.Radius);
                }
            }
            return (peakG, peakQ, veh.HeatLoad, chuteAlt, 999, t, 0);
        }
    }
}
