using System;
using System.Collections.Generic;

namespace Karman.Core
{
    /// <summary>A planned impulsive burn at universal time UT, in the orbit-local frame of the body being orbited.</summary>
    [Serializable]
    public sealed class ManeuverNode
    {
        public double UT;
        public double Prograde, Normal, Radial; // m/s

        public double DeltaV => Math.Sqrt(Prograde * Prograde + Normal * Normal + Radial * Radial);

        /// <summary>Inertial Δv vector for the given state: prograde = v̂, normal = (r×v)^, radial-out = v̂×n̂.</summary>
        public Vector3d DeltaVector(StateVector s)
        {
            Vector3d pro = s.velocity.normalized;
            Vector3d nrm = Vector3d.Cross(s.position, s.velocity).normalized;
            Vector3d rad = Vector3d.Cross(pro, nrm);
            return pro * Prograde + nrm * Normal + rad * Radial;
        }

        /// <summary>Rocket-equation burn duration at constant thrust.</summary>
        public static double BurnTime(double dv, double mass, double thrust, double isp)
        {
            if (thrust <= 0 || isp <= 0) return double.PositiveInfinity;
            double mdot = thrust / (isp * Constants.StandardGravity);
            return mass * (1 - Math.Exp(-dv / (isp * Constants.StandardGravity))) / mdot;
        }
    }

    public enum SegmentEnd { Horizon, SoiExit, SoiEntry, Impact }

    public sealed class TrajectorySegment
    {
        public CelestialBody Body;
        public KeplerOrbit Orbit;
        public double Start, End;
        public SegmentEnd EndReason;
        public CelestialBody NextBody;
    }

    public sealed class TrajectoryPrediction
    {
        public readonly List<TrajectorySegment> Segments = new List<TrajectorySegment>();
        public CelestialBody EncounterBody;
        public double EncounterTime = double.NaN;
        public double EncounterPeriapsisAltitude = double.NaN;
        public double ClosestApproachTime = double.NaN;
        public double ClosestApproachDistance = double.PositiveInfinity;
        public bool Impacts;

        public TrajectorySegment SegmentAt(double t)
        {
            foreach (var s in Segments) if (t >= s.Start && t <= s.End) return s;
            return Segments.Count > 0 ? Segments[Segments.Count - 1] : null;
        }
    }

    /// <summary>
    /// Patched-conic trajectory prediction: Kepler conics about the dominant body, switched at sphere-of-influence
    /// boundaries. SOI exits are solved analytically; entries into child SOIs (e.g. the Moon) are found by sampling the
    /// child's ephemeris and bisecting to one second. Also reports the closest approach to a target body.
    /// </summary>
    public static class TrajectoryPredictor
    {
        public static TrajectoryPrediction Predict(SolarSystem sys, CelestialBody body, StateVector state, double t0,
                                                   double horizon, CelestialBody target = null, int maxTransitions = 4)
        {
            var result = new TrajectoryPrediction();
            double t = t0, tLimit = t0 + horizon;
            for (int k = 0; k <= maxTransitions && t < tLimit; k++)
            {
                var orbit = KeplerOrbit.FromState(state, body.Mu, t);
                double segEnd = tLimit;
                if (!orbit.IsHyperbolic && k == 0 && result.Segments.Count == 0) segEnd = Math.Min(tLimit, t + Math.Max(orbit.Period * 1.02, 1));
                var seg = new TrajectorySegment { Body = body, Orbit = orbit, Start = t, End = segEnd, EndReason = SegmentEnd.Horizon };

                // Impact with the body's surface (periapsis below the radius, heading there before other events).
                double tImpact = double.PositiveInfinity;
                if (orbit.PeriapsisRadius < body.Radius)
                {
                    double nuImpact = TrueAnomalyAtRadius(orbit, body.Radius);
                    if (!double.IsNaN(nuImpact)) tImpact = t + orbit.TimeToTrueAnomaly(-nuImpact, t);
                }

                // SOI exit (analytic).
                double tExit = double.PositiveInfinity;
                if (body.Parent != null && (orbit.IsHyperbolic || orbit.ApoapsisRadius > body.SphereOfInfluence))
                {
                    double nuExit = TrueAnomalyAtRadius(orbit, body.SphereOfInfluence);
                    if (!double.IsNaN(nuExit))
                    {
                        double dt = orbit.TimeToTrueAnomaly(nuExit, t);
                        if (orbit.IsHyperbolic && dt < 0) dt = double.PositiveInfinity;
                        tExit = t + dt;
                    }
                }

                // Child SOI entry (sampled).
                double searchEnd = Math.Min(segEnd, Math.Min(tExit, tImpact));
                CelestialBody entered = null; double tEntry = double.PositiveInfinity;
                foreach (var child in body.Children)
                {
                    double step = Math.Max(20, Math.Min(900, (orbit.IsHyperbolic ? 3600 : orbit.Period / 720)));
                    double prevD = double.NaN;
                    for (double ts = t; ts <= searchEnd; ts += step)
                    {
                        double d = (orbit.StateAt(ts).position - RelativeState(child, body, ts).position).magnitude;
                        if (target == child && d < result.ClosestApproachDistance) { result.ClosestApproachDistance = d; result.ClosestApproachTime = ts; }
                        if (d < child.SphereOfInfluence && ts > t + 1)
                        {
                            double lo = ts - step, hi = ts;
                            for (int it = 0; it < 40 && hi - lo > 0.5; it++)
                            {
                                double mid = (lo + hi) / 2;
                                double dm = (orbit.StateAt(mid).position - RelativeState(child, body, mid).position).magnitude;
                                if (dm < child.SphereOfInfluence) hi = mid; else lo = mid;
                            }
                            if (hi < tEntry) { tEntry = hi; entered = child; }
                            break;
                        }
                        prevD = d;
                    }
                }

                double tEvent = Math.Min(Math.Min(tExit, tEntry), Math.Min(tImpact, segEnd));
                seg.End = tEvent;
                result.Segments.Add(seg);
                if (tEvent == tImpact) { seg.EndReason = SegmentEnd.Impact; result.Impacts = true; break; }
                if (tEvent >= segEnd) break;

                CelestialBody next = tEvent == tEntry ? entered : body.Parent;
                seg.EndReason = tEvent == tEntry ? SegmentEnd.SoiEntry : SegmentEnd.SoiExit;
                seg.NextBody = next;
                var s = orbit.StateAt(tEvent);
                var bodyHelio = Ephemeris.HeliocentricState(body.Id, tEvent);
                var nextHelio = Ephemeris.HeliocentricState(next.Id, tEvent);
                state = new StateVector(s.position + bodyHelio.position - nextHelio.position, s.velocity + bodyHelio.velocity - nextHelio.velocity);
                body = next; t = tEvent;
                if (seg.EndReason == SegmentEnd.SoiEntry && result.EncounterBody == null)
                {
                    result.EncounterBody = next; result.EncounterTime = tEvent;
                    var o2 = KeplerOrbit.FromState(state, next.Mu, tEvent);
                    result.EncounterPeriapsisAltitude = o2.PeriapsisRadius - next.Radius;
                }
            }
            return result;
        }

        /// <summary>State of child relative to parent at time t, from the ephemerides.</summary>
        public static StateVector RelativeState(CelestialBody child, CelestialBody parent, double t)
        {
            if (child.Id == SolarSystemData.Moon && parent.Id == SolarSystemData.Earth) return Ephemeris.MoonGeocentricState(t);
            return Ephemeris.HeliocentricState(child.Id, t) - Ephemeris.HeliocentricState(parent.Id, t);
        }

        /// <summary>Outbound true anomaly (0..π) at which the conic reaches radius r, or NaN if never.</summary>
        public static double TrueAnomalyAtRadius(KeplerOrbit o, double r)
        {
            double p = o.SemiLatusRectum;
            double c = (p / r - 1) / o.eccentricity;
            if (o.eccentricity < 1e-9 || c > 1 || c < -1) return double.NaN;
            return Math.Acos(c);
        }
    }

    /// <summary>Mission-planning helpers.</summary>
    public static class TransferPlanner
    {
        /// <summary>
        /// Plans a prograde trans-lunar injection from a near-circular Earth orbit: scans burn times over one orbit with a
        /// Hohmann-sized Δv, keeps those that reach the Moon's SOI, then refines Δv by bisection on the periselene
        /// altitude. Returns null if no encounter is found within the search window.
        /// </summary>
        public static ManeuverNode PlanLunarTransfer(SolarSystem sys, CelestialBody earth, CelestialBody moon, StateVector state,
                                                     double now, double targetPeriselene = 100_000)
        {
            var orbit = KeplerOrbit.FromState(state, earth.Mu, now);
            double r1 = (orbit.PeriapsisRadius + orbit.ApoapsisRadius) / 2;
            double r2 = Ephemeris.MoonGeocentricPosition(now).magnitude;
            double dvH = Math.Sqrt(earth.Mu / r1) * (Math.Sqrt(2 * r2 / (r1 + r2)) - 1);
            ManeuverNode best = null; double bestScore = double.MaxValue;
            double start = now + 120;
            // Coarse scan: burn time over one orbit × slightly-faster-than-Hohmann Δv; prefer short flights and a periselene near target.
            for (double ut = start; ut < start + orbit.Period * 1.05; ut += orbit.Period / 240)
            {
                foreach (double scale in new[] { 1.0, 1.006, 1.012, 1.018, 1.024, 1.03 })
                {
                    var node = new ManeuverNode { UT = ut, Prograde = dvH * scale };
                    var pred = PredictAfter(sys, earth, orbit, node, moon, 7 * 86400);
                    if (pred.EncounterBody != moon || pred.EncounterPeriapsisAltitude < 0 && false) continue;
                    double tofHours = (pred.EncounterTime - ut) / 3600;
                    double score = Math.Abs(pred.EncounterPeriapsisAltitude - targetPeriselene) / 1000 + tofHours * 2;
                    if (score < bestScore) { bestScore = score; best = node; }
                }
            }
            if (best == null) return null;
            // Fine: bracket the periselene target in Δv (then in time) and bisect.
            var refined = Refine(sys, earth, orbit, best, moon, targetPeriselene, true) ?? Refine(sys, earth, orbit, best, moon, targetPeriselene, false);
            return refined ?? best;
        }

        static ManeuverNode Refine(SolarSystem sys, CelestialBody earth, KeplerOrbit orbit, ManeuverNode seed, CelestialBody moon, double target, bool varyDv)
        {
            ManeuverNode Make(double x) => varyDv ? new ManeuverNode { UT = seed.UT, Prograde = seed.Prograde + x } : new ManeuverNode { UT = seed.UT + x, Prograde = seed.Prograde };
            double span = varyDv ? 10 : 90, step = varyDv ? 0.25 : 3;
            double prevX = double.NaN, prevF = double.NaN, bestLo = double.NaN, bestHi = double.NaN;
            for (double x = -span; x <= span; x += step)
            {
                double f = Score(sys, earth, orbit, Make(x), moon, target);
                if (!double.IsNaN(f) && !double.IsNaN(prevF) && Math.Sign(f) != Math.Sign(prevF))
                {
                    if (double.IsNaN(bestLo) || Math.Abs(x) < Math.Abs(bestHi)) { bestLo = prevX; bestHi = x; }
                }
                prevX = x; prevF = f;
            }
            if (double.IsNaN(bestLo)) return null;
            double lo = bestLo, hi = bestHi, flo = Score(sys, earth, orbit, Make(lo), moon, target);
            for (int i = 0; i < 30; i++)
            {
                double mid = (lo + hi) / 2, fm = Score(sys, earth, orbit, Make(mid), moon, target);
                if (double.IsNaN(fm)) break;
                if (Math.Sign(fm) == Math.Sign(flo)) { lo = mid; flo = fm; } else hi = mid;
            }
            return Make((lo + hi) / 2);
        }

        static double Score(SolarSystem sys, CelestialBody earth, KeplerOrbit orbit, ManeuverNode node, CelestialBody moon, double target)
        {
            var pred = PredictAfter(sys, earth, orbit, node, moon);
            return pred.EncounterBody == moon ? pred.EncounterPeriapsisAltitude - target : double.NaN;
        }

        public static TrajectoryPrediction PredictAfter(SolarSystem sys, CelestialBody body, KeplerOrbit orbit, ManeuverNode node, CelestialBody target, double horizon = 12 * 86400)
        {
            var s = orbit.StateAt(node.UT);
            var after = new StateVector(s.position, s.velocity + node.DeltaVector(s));
            return TrajectoryPredictor.Predict(sys, body, after, node.UT, horizon, target);
        }
    }
}
