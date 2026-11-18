using System;
using System.Collections.Generic;
using System.Linq;

namespace Karman.Core.Vehicles
{
    public sealed class StageReport
    {
        public int Stage;
        public double DeltaV;          // m/s
        public double BurnTime;        // s
        public double StartMass, EndMass;
        public double Thrust;          // N at the analysed pressure, first segment
        public double Isp;             // effective, first segment
        public double StartTwr;        // at the analysed surface gravity
        public int EngineCount;
        public bool Jettisons;
    }

    public sealed class CraftAnalysis
    {
        public double TotalMass, DryMass, PropellantMass;
        public double[] CenterOfMass = new double[3];
        public double[] CenterOfThrust = new double[3];
        public double TotalDeltaV;
        public List<StageReport> Stages = new List<StageReport>();
    }

    /// <summary>
    /// Mass properties and per-stage Δv/TWR/burn time, by simulating the staging sequence: stages fire from the highest
    /// number down; a decoupler jettisons itself and its subtree; engines burn propellant pooled within their
    /// section (parts connected without crossing a decoupler) in their mixture ratio.
    /// </summary>
    public static class StagingAnalysis
    {
        public static CraftAnalysis Analyse(Craft craft, Func<string, PartDefinition> lookup, double pressurePa = 0, double gravity = Constants.StandardGravity)
        {
            var result = new CraftAnalysis();
            if (craft.RootId < 0) return result;

            // ---- mass properties
            double[] com = new double[3]; double[] cot = new double[3]; double thrustSum = 0;
            int firstStage = craft.MaxStage;
            foreach (var p in craft.Parts)
            {
                var d = lookup(p.PartId); if (d == null) continue;
                double prop = p.Resources.Sum(r => r.Mass);
                double m = d.DryMass + prop;
                result.TotalMass += m; result.DryMass += d.DryMass; result.PropellantMass += prop;
                var c = Transform(p, 0, d.CenterOfMassY, 0);
                for (int i = 0; i < 3; i++) com[i] += c[i] * m;
                if (d.Engine != null && p.Stage == firstStage)
                {
                    double f = d.Engine.ThrustAt(pressurePa);
                    for (int i = 0; i < 3; i++) cot[i] += p.Position[i] * f;
                    thrustSum += f;
                }
            }
            for (int i = 0; i < 3; i++)
            {
                result.CenterOfMass[i] = result.TotalMass > 0 ? com[i] / result.TotalMass : 0;
                result.CenterOfThrust[i] = thrustSum > 0 ? cot[i] / thrustSum : 0;
            }

            // ---- sections
            var section = new Dictionary<int, int>();
            AssignSections(craft, lookup, craft.RootId, 0, section, new[] { 1 });
            var pools = new Dictionary<(int, Propellant), double>();
            var attached = new HashSet<int>(craft.Parts.Select(p => p.Id));
            foreach (var p in craft.Parts)
                foreach (var r in p.Resources)
                {
                    var key = (section[p.Id], r.Type);
                    pools[key] = pools.TryGetValue(key, out var v) ? v + r.Mass : r.Mass;
                }
            var dryOf = craft.Parts.ToDictionary(p => p.Id, p => lookup(p.PartId)?.DryMass ?? 0);
            var active = new HashSet<int>();

            double Mass()
            {
                double m = 0;
                foreach (var id in attached) m += dryOf[id];
                var liveSections = new HashSet<int>(attached.Select(id => section[id]));
                foreach (var kv in pools) if (liveSections.Contains(kv.Key.Item1)) m += kv.Value;
                return m;
            }

            for (int s = craft.MaxStage; s >= 0; s--)
            {
                var report = new StageReport { Stage = s };
                foreach (var p in craft.Parts.Where(p => p.Stage == s && attached.Contains(p.Id)))
                {
                    var d = lookup(p.PartId);
                    if (d.IsDecoupler)
                    {
                        foreach (var q in craft.Subtree(p.Id)) { attached.Remove(q.Id); active.Remove(q.Id); }
                        report.Jettisons = true;
                    }
                    else if (d.Engine != null) active.Add(p.Id);
                }
                report.StartMass = Mass();
                // Engines the next stage will jettison: this stage ends when they burn out (e.g. strap-on boosters),
                // and any engine still burning carries over into the next stage.
                var jettisonedNext = new HashSet<int>();
                foreach (var dp in craft.Parts.Where(q => q.Stage == s - 1 && attached.Contains(q.Id) && lookup(q.PartId).IsDecoupler))
                    foreach (var q in craft.Subtree(dp.Id)) if (active.Contains(q.Id)) jettisonedNext.Add(q.Id);
                bool first = true;
                for (int guard = 0; guard < 64; guard++)
                {
                    var burning = active.Where(id => HasFuel(craft.Get(id), lookup, section, pools)).ToList();
                    if (burning.Count == 0) break;
                    if (jettisonedNext.Count > 0 && !jettisonedNext.Any(burning.Contains)) break;
                    double F = 0, mdot = 0;
                    var rates = new Dictionary<(int, Propellant), double>();
                    foreach (var id in burning)
                    {
                        var e = lookup(craft.Get(id).PartId).Engine;
                        double f = e.ThrustAt(pressurePa), isp = e.IspAt(pressurePa);
                        double md = f / (isp * Constants.StandardGravity);
                        F += f; mdot += md;
                        foreach (var mix in e.Mixture)
                        {
                            var key = (section[id], mix.Type);
                            rates[key] = (rates.TryGetValue(key, out var r) ? r : 0) + md * mix.Mass;
                        }
                    }
                    double t = double.MaxValue;
                    foreach (var kv in rates) t = Math.Min(t, pools[kv.Key] / kv.Value);
                    if (t <= 1e-9 || double.IsInfinity(t)) break;
                    double m0 = Mass();
                    double ispEff = F / (mdot * Constants.StandardGravity);
                    double m1 = m0 - mdot * t;
                    report.DeltaV += ispEff * Constants.StandardGravity * Math.Log(m0 / m1);
                    report.BurnTime += t;
                    if (first) { report.Thrust = F; report.Isp = ispEff; report.StartTwr = F / (m0 * gravity); report.EngineCount = burning.Count; first = false; }
                    foreach (var kv in rates) pools[kv.Key] = Math.Max(0, pools[kv.Key] - kv.Value * t);
                }
                report.EndMass = Mass();
                result.Stages.Add(report);
                result.TotalDeltaV += report.DeltaV;
            }
            return result;
        }

        static bool HasFuel(PartInstance p, Func<string, PartDefinition> lookup, Dictionary<int, int> section, Dictionary<(int, Propellant), double> pools)
        {
            var e = lookup(p.PartId).Engine;
            if (e.Mixture.Count == 0) return false;
            foreach (var mix in e.Mixture)
                if (!pools.TryGetValue((section[p.Id], mix.Type), out var v) || v <= 1e-9) return false;
            return true;
        }

        static void AssignSections(Craft craft, Func<string, PartDefinition> lookup, int id, int current, Dictionary<int, int> map, int[] next)
        {
            var p = craft.Get(id); if (p == null) return;
            var d = lookup(p.PartId);
            int sec = d != null && d.IsDecoupler && p.ParentId >= 0 ? next[0]++ : current;
            map[id] = sec;
            foreach (var c in craft.Children(id)) AssignSections(craft, lookup, c.Id, sec, map, next);
        }

        static double[] Transform(PartInstance p, double x, double y, double z)
        {
            // Rotate (x,y,z) by quaternion p.Rotation then translate.
            double qx = p.Rotation[0], qy = p.Rotation[1], qz = p.Rotation[2], qw = p.Rotation[3];
            double tx = 2 * (qy * z - qz * y), ty = 2 * (qz * x - qx * z), tz = 2 * (qx * y - qy * x);
            return new[]
            {
                p.Position[0] + x + qw * tx + (qy * tz - qz * ty),
                p.Position[1] + y + qw * ty + (qz * tx - qx * tz),
                p.Position[2] + z + qw * tz + (qx * ty - qy * tx),
            };
        }
    }
}
