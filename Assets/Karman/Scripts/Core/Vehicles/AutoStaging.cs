using System;
using System.Collections.Generic;
using System.Linq;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Default staging: the deepest stack section ignites first; strap-on engines on radial separators ignite with
    /// their parent section and are jettisoned in the following stage; a stack separator fires together with the
    /// ignition of the section above it. Stage numbers are compacted (highest fires first).
    /// </summary>
    public static class AutoStaging
    {
        public static void Apply(Craft craft, Func<string, PartDefinition> lookup)
        {
            if (craft.RootId < 0) return;
            var depth = new Dictionary<int, int>();
            Walk(craft, lookup, craft.RootId, 0, depth);
            var raw = new Dictionary<int, int>();
            foreach (var p in craft.Parts)
            {
                var d = lookup(p.PartId);
                int s = depth[p.Id];
                if (d.IsDecoupler)
                {
                    bool radial = d.Id == "decoupler.radial" || (p.ParentNode ?? "") == "";
                    raw[p.Id] = radial ? 2 * s - 1 : 2 * (s - 1); // radial: one step after its section ignites
                }
                else if (d.Engine != null) raw[p.Id] = 2 * s;
            }
            // Pod manoeuvring engines are never auto-staged: they are operated by the player in orbit.
            foreach (var p in craft.Parts) if (lookup(p.PartId).Id == "pod.gemini") raw.Remove(p.Id);
            var distinct = raw.Values.Distinct().OrderBy(v => v).ToList();
            foreach (var p in craft.Parts) p.Stage = raw.TryGetValue(p.Id, out var v) ? distinct.IndexOf(v) : -1;
        }

        static void Walk(Craft craft, Func<string, PartDefinition> lookup, int id, int s, Dictionary<int, int> depth)
        {
            var p = craft.Get(id); var d = lookup(p.PartId);
            bool stackDecoupler = d.IsDecoupler && d.Id != "decoupler.radial" && p.ParentId >= 0;
            int mine = stackDecoupler ? s + 1 : s;
            depth[id] = mine;
            foreach (var c in craft.Children(id)) Walk(craft, lookup, c.Id, mine, depth);
        }
    }
}
