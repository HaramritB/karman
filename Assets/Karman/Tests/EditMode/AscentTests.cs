using System;
using Karman.Core;
using Karman.Core.Vehicles;
using NUnit.Framework;

namespace Karman.Tests
{
    /// <summary>
    /// Headless pad-to-orbit flights with the game's own models: FlightVehicle propulsion and staging, US76 drag,
    /// Earth rotation, RK4 trajectory, and the AscentGuidance used by the in-game ascent assistant.
    /// </summary>
    public class AscentTests
    {
        static readonly Func<string, PartDefinition> L = PartLibrary.Lookup(PartLibrary.CreateStarter());
        static readonly double T0 = TimeSystem.FromUtc(new DateTime(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc));

        public static Craft Sample(bool boosters)
        {
            var c = new Craft { Name = boosters ? "Kármán I Heavy" : "Kármán I" };
            var pod = c.Add(L("pod.gemini"), -1, null, null);
            var adapter = c.Add(L("adapter.37to30"), pod.Id, "bottom", "top");
            var upper = c.Add(L("tank.kerolox.37x8"), adapter.Id, "bottom", "top");
            var mvac = c.Add(L("engine.kr1v"), upper.Id, "bottom", "top");
            var dec = c.Add(L("decoupler.37"), mvac.Id, "bottom", "top");
            var lower = c.Add(L("tank.kerolox.37x32"), dec.Id, "bottom", "top");
            c.Add(L("engine.kr1.cluster9"), lower.Id, "bottom", "top");
            if (boosters)
                for (int k = 0; k < 2; k++)
                {
                    var rd = c.Add(L("decoupler.radial"), lower.Id, "", "outer"); rd.Position = new[] { k == 0 ? 2.0f : -2.0f, -30f, 0 };
                    var srb = c.Add(L("srb.gem"), rd.Id, "", "top"); srb.Position = new[] { k == 0 ? 3.0f : -3.0f, -30f, 0 }; srb.SymmetryGroup = 99; rd.SymmetryGroup = 98;
                }
            AutoStaging.Apply(c, L);
            return c;
        }

        public struct AscentResult { public double ApKm, PeKm, IncDeg, MaxQ, TimeS, DvLeft; public bool Crashed; public string Log; }

        public static AscentResult Fly(Craft craft, double timeLimit = 5000)
        {
            var sys = SolarSystem.CreateDefault(T0);
            var earth = sys.Get(SolarSystemData.Earth);
            var veh = new FlightVehicle(craft, L);
            veh.VerticalExtent(out float bottom, out _);
            double standoff = veh.CenterOfMass[1] - bottom;
            double lat = 28.60822 * Constants.Deg2Rad, lon = -80.60411 * Constants.Deg2Rad;
            var upBf = new Vector3d(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
            Vector3d pole = earth.Orientation.Column(2);
            double omega = 2 * Math.PI / earth.Definition.SiderealRotationPeriod;
            Vector3d r0 = earth.Orientation * (upBf * (earth.Radius + 15 + standoff));
            var traj = new SpacecraftTrajectory(sys, earth, new StateVector(r0, Vector3d.Cross(pole * omega, r0)), T0);
            var guide = new AscentGuidance();
            Vector3d attitude = r0.normalized;
            bool landed = true;
            double t = 0, dt = 0.05, maxQ = 0, stageCooldown = 0;
            var log = new System.Text.StringBuilder();
            veh.Stage(); // ignition
            while (t < timeLimit)
            {
                var s = traj.RelativeState;
                double alt = s.position.magnitude - earth.Radius;
                var air = EarthAtmosphere.Sample(alt);
                Vector3d vSurf = s.velocity - Vector3d.Cross(pole * omega, s.position);
                double maxAccel = 0; foreach (var id in veh.BurningEngines()) maxAccel += L(veh.Craft.Get(id).PartId).Engine.ThrustAt(air.Pressure); maxAccel /= veh.Mass;
                if (maxAccel <= 0) { var an = veh.Analyse(air.Pressure); foreach (var st in an.Stages) if (st.Thrust > 0) { maxAccel = st.Thrust / st.StartMass; break; } }
                guide.Update(s.position, s.velocity, vSurf, pole, earth.Mu, earth.Radius, maxAccel, out double throttle, out Vector3d dir);
                if (guide.Phase == AscentGuidance.Mode.Complete) break;
                // Attitude slews toward the command at up to 3°/s (gimbal/RCS-limited).
                double ang = Vector3d.Angle(attitude, dir);
                double maxStep = 3 * Constants.Deg2Rad * dt;
                attitude = ang <= maxStep ? dir : (attitude * Math.Sin(ang - maxStep) + dir * Math.Sin(maxStep)).normalized;
                // Auto-staging on burnout.
                stageCooldown -= dt;
                if (veh.NextStage >= 0 && stageCooldown <= 0)
                {
                    var burning = veh.BurningEngines();
                    bool anyLit = false, anyDead = false;
                    foreach (var id in veh.LitEngines) { if (!veh.Attached.Contains(id)) continue; anyLit = true; if (!burning.Contains(id)) anyDead = true; }
                    if (!anyLit || anyDead) { veh.Stage(); stageCooldown = 1; log.AppendLine($"T+{t:F0}s stage → next {veh.NextStage}, alt {alt / 1000:F1} km, v {vSurf.magnitude:F0} m/s"); }
                }
                double m = veh.Mass;
                double thrust = veh.Burn(throttle, dt, air.Pressure, out _, out _);
                if (((int)(t / dt)) % 4000 == 0) { var oo = traj.Orbit; log.AppendLine($"T+{t:F0} {guide.Phase} alt {alt / 1000:F1} vs {Vector3d.Dot(s.velocity, s.position.normalized):F0} v {s.velocity.magnitude:F0} Ap {(oo.ApoapsisRadius - earth.Radius) / 1000:F0} Pe {(oo.PeriapsisRadius - earth.Radius) / 1000:F0} thr {thrust / 1000:F0}kN m {m / 1000:F1}t pitchErr {Vector3d.Angle(attitude, dir) * Constants.Rad2Deg:F1}"); }
                double area = veh.FrontalArea;
                Vector3d aT = attitude * (thrust / m);
                if (landed)
                {
                    if (thrust / m > earth.Mu / s.position.sqrMagnitude) landed = false;
                    else { t += dt; continue; }
                }
                maxQ = Math.Max(maxQ, 0.5 * air.Density * vSurf.sqrMagnitude);
                traj.Advance(dt, (r, v) =>
                {
                    Vector3d a = aT;
                    if (air.Density > 0)
                    {
                        Vector3d vr = v - Vector3d.Cross(pole * omega, r);
                        double sp = vr.magnitude;
                        a -= vr * (0.5 * air.Density * sp * EarthAtmosphere.DragCoefficient(sp / air.SpeedOfSound) * area / m);
                    }
                    return a;
                }, 0.05);
                t += dt;
                if (traj.Altitude < 0) return new AscentResult { Crashed = true, TimeS = t, Log = log.ToString() };
            }
            var o = traj.Orbit;
            return new AscentResult
            {
                ApKm = (o.ApoapsisRadius - earth.Radius) / 1000, PeKm = (o.PeriapsisRadius - earth.Radius) / 1000,
                IncDeg = traj.EquatorialInclination * Constants.Rad2Deg, MaxQ = maxQ, TimeS = t, DvLeft = veh.Analyse().TotalDeltaV, Log = log.ToString(),
            };
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SampleLauncherReachesStableOrbit(bool boosters)
        {
            var r = Fly(Sample(boosters));
            TestContext.WriteLine($"{(boosters ? "Heavy" : "Core")}: Ap {r.ApKm:F1} km, Pe {r.PeKm:F1} km, i {r.IncDeg:F2}°, max-Q {r.MaxQ / 1000:F1} kPa, T+{r.TimeS:F0}s, Δv left {r.DvLeft:F0} m/s\n{r.Log}");
            Assert.IsFalse(r.Crashed, "vehicle crashed");
            Assert.GreaterOrEqual(r.PeKm, 150, "periapsis above the atmosphere");
            Assert.Less(r.ApKm, 450);
            Assert.AreEqual(28.6, r.IncDeg, 1.5, "due-east launch from 28.6°N");
        }
    }
}
