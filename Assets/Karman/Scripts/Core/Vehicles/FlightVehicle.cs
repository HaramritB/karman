using System;
using System.Collections.Generic;
using System.Linq;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Runtime state of a multi-stage vehicle in flight: which parts are still attached, propellant per part,
    /// which engines are lit, and the next stage to fire. Propellant flows within sections (between separators);
    /// engines draw their mixture from their section, depleting tanks in proportion to their contents.
    /// Engines with no stage assigned (e.g. a capsule's manoeuvring system) light once all stages are spent.
    /// </summary>
    public sealed class FlightVehicle
    {
        public readonly Craft Craft;
        readonly Func<string, PartDefinition> lookup;
        public readonly HashSet<int> Attached;
        readonly Dictionary<int, int> section = new Dictionary<int, int>();
        readonly HashSet<int> lit = new HashSet<int>();
        public int NextStage { get; private set; }
        public event Action<List<int>> PartsDetached;

        public FlightVehicle(Craft craft, Func<string, PartDefinition> lookup)
        {
            Craft = craft.DeepClone();
            this.lookup = lookup;
            Attached = new HashSet<int>(Craft.Parts.Select(p => p.Id));
            AssignSections(Craft.RootId, 0, new[] { 1 });
            NextStage = Craft.MaxStage;
            bool anyStaged = Craft.Parts.Any(p => p.Stage >= 0 && lookup(p.PartId).Engine != null);
            if (!anyStaged) ActivateUnstagedEngines();
        }

        void AssignSections(int id, int cur, int[] next)
        {
            var p = Craft.Get(id); if (p == null) return;
            var d = lookup(p.PartId);
            int s = d.IsDecoupler && p.ParentId >= 0 ? next[0]++ : cur;
            section[id] = s;
            foreach (var c in Craft.Children(id)) AssignSections(c.Id, s, next);
        }

        void ActivateUnstagedEngines()
        {
            foreach (var p in Craft.Parts) if (Attached.Contains(p.Id) && p.Stage < 0 && lookup(p.PartId).Engine != null) lit.Add(p.Id);
        }

        public IEnumerable<PartInstance> AttachedParts => Craft.Parts.Where(p => Attached.Contains(p.Id));
        public IEnumerable<int> LitEngines => lit;
        public PartDefinition Def(PartInstance p) => lookup(p.PartId);

        public double Mass
        {
            get
            {
                double m = 0;
                foreach (var p in Craft.Parts) if (Attached.Contains(p.Id)) { m += lookup(p.PartId).DryMass; foreach (var r in p.Resources) m += r.Mass; }
                return m;
            }
        }

        /// <summary>Fire the next stage. Returns detached part ids (empty if nothing left to stage).</summary>
        public List<int> Stage()
        {
            var detached = new List<int>();
            if (NextStage < 0) return detached;
            int s = NextStage--;
            foreach (var p in Craft.Parts.Where(p => p.Stage == s && Attached.Contains(p.Id)).ToList())
            {
                var d = lookup(p.PartId);
                if (d.IsDecoupler)
                {
                    foreach (var q in Craft.Subtree(p.Id)) if (Attached.Remove(q.Id)) { detached.Add(q.Id); lit.Remove(q.Id); }
                }
                else if (d.Engine != null) lit.Add(p.Id);
            }
            if (NextStage < 0) ActivateUnstagedEngines();
            if (detached.Count > 0) PartsDetached?.Invoke(detached);
            return detached;
        }

        bool EngineHasFuel(int id)
        {
            var e = lookup(Craft.Get(id).PartId).Engine;
            foreach (var mix in e.Mixture) if (SectionAmount(section[id], mix.Type) <= 1e-6) return false;
            return e.Mixture.Count > 0;
        }

        public double SectionAmount(int sec, Propellant type)
        {
            double a = 0;
            foreach (var p in Craft.Parts) if (Attached.Contains(p.Id) && section[p.Id] == sec) foreach (var r in p.Resources) if (r.Type == type) a += r.Mass;
            return a;
        }

        void Draw(int sec, Propellant type, double amount)
        {
            double total = SectionAmount(sec, type);
            if (total <= 0) return;
            double f = Math.Min(1, amount / total);
            foreach (var p in Craft.Parts) if (Attached.Contains(p.Id) && section[p.Id] == sec) foreach (var r in p.Resources) if (r.Type == type) r.Mass *= 1 - f;
        }

        /// <summary>Engines that are lit and have propellant.</summary>
        public List<int> BurningEngines() => lit.Where(id => Attached.Contains(id) && EngineHasFuel(id)).ToList();

        /// <summary>Burn for dt. Solid motors ignore throttle. Returns total thrust (N) and sets the thrust-weighted Isp.</summary>
        public double Burn(double throttle, double dt, double pressurePa, out double isp, out double maxThrust)
        {
            double F = 0, mdotTotal = 0; maxThrust = 0;
            var burning = BurningEngines();
            var demand = new Dictionary<(int, Propellant), double>();
            foreach (var id in burning)
            {
                var e = lookup(Craft.Get(id).PartId).Engine;
                double thr = e.Solid ? 1 : (throttle <= 0 ? 0 : Math.Max(e.MinThrottle, Math.Min(1, throttle)));
                double f = e.ThrustAt(pressurePa) * thr;
                maxThrust += e.ThrustAt(pressurePa);
                double mdot = e.ThrustAt(pressurePa) * thr / (e.IspAt(pressurePa) * Constants.StandardGravity);
                F += f; mdotTotal += mdot;
                foreach (var mix in e.Mixture)
                {
                    var key = (section[id], mix.Type);
                    demand[key] = (demand.TryGetValue(key, out var v) ? v : 0) + mdot * mix.Mass * dt;
                }
            }
            // Scale down if any section runs dry within this step.
            double scale = 1;
            foreach (var kv in demand) { double have = SectionAmount(kv.Key.Item1, kv.Key.Item2); if (kv.Value > have) scale = Math.Min(scale, have / kv.Value); }
            foreach (var kv in demand) Draw(kv.Key.Item1, kv.Key.Item2, kv.Value * scale);
            isp = mdotTotal > 0 ? F / (mdotTotal * Constants.StandardGravity) : 0;
            return F * scale;
        }

        /// <summary>Δv remaining from the current state (vacuum or given pressure), using the same staging simulation as the VAB.</summary>
        public CraftAnalysis Analyse(double pressurePa = 0)
        {
            var c = new Craft { Name = Craft.Name, RootId = Craft.RootId };
            foreach (var p in Craft.Parts.Where(p => Attached.Contains(p.Id)))
            {
                var q = p.Clone();
                if (q.Stage > NextStage) q.Stage = -1; // already fired
                if (lit.Contains(q.Id) && lookup(q.PartId).Engine != null) q.Stage = NextStage + 1; // lit engines form the current stage
                c.Parts.Add(q);
            }
            if (c.Get(c.RootId) == null) return new CraftAnalysis();
            return StagingAnalysis.Analyse(c, lookup, pressurePa);
        }

        public double FrontalArea
        {
            get
            {
                double core = 0, side = 0;
                foreach (var p in AttachedParts)
                {
                    var d = lookup(p.PartId);
                    double a = Math.PI * d.Diameter * d.Diameter / 4;
                    bool radial = Math.Abs(p.Position[0]) + Math.Abs(p.Position[2]) > 0.5;
                    if (radial) { if (d.Category != PartCategory.Coupling) side += a; }
                    else core = Math.Max(core, a);
                }
                return core + side * 0.5; // strap-ons partly shielded
            }
        }

        public double MaxGimbalRadians
        {
            get { double g = 0; foreach (var id in lit) if (Attached.Contains(id)) g = Math.Max(g, lookup(Craft.Get(id).PartId).Engine.GimbalDegrees); return g * Constants.Deg2Rad; }
        }

        /// <summary>Centre of mass of attached parts in craft space (y component used for torque arms).</summary>
        public double[] CenterOfMass
        {
            get
            {
                double m = 0; var c = new double[3];
                foreach (var p in AttachedParts)
                {
                    var d = lookup(p.PartId);
                    double pm = d.DryMass + p.Resources.Sum(r => r.Mass);
                    for (int i = 0; i < 3; i++) c[i] += (p.Position[i] + (i == 1 ? d.CenterOfMassY : 0)) * pm;
                    m += pm;
                }
                for (int i = 0; i < 3; i++) c[i] = m > 0 ? c[i] / m : 0;
                return c;
            }
        }

        /// <summary>Lowest and highest points of the attached stack (craft-space y).</summary>
        public void VerticalExtent(out float bottom, out float top)
        {
            bottom = float.MaxValue; top = float.MinValue;
            foreach (var p in AttachedParts)
            {
                var d = lookup(p.PartId);
                float half = Math.Max(d.Height, d.Diameter) / 2;
                bottom = Math.Min(bottom, p.Position[1] - half); top = Math.Max(top, p.Position[1] + half);
            }
        }

        public double PropellantRemaining => AttachedParts.Sum(p => p.Resources.Sum(r => r.Mass));
    }
}
