using System;

namespace Karman.Core.Vehicles
{
    /// <summary>Reference vehicles used by scenarios, the VAB sample button and the headless tests.</summary>
    public static class SampleCrafts
    {
        public static void LayoutStack(Craft c, Func<string, PartDefinition> L)
        {
            foreach (var p in c.Parts)
            {
                if (p.ParentId < 0 || string.IsNullOrEmpty(p.ParentNode)) continue;
                var parent = c.Get(p.ParentId);
                var pn = L(parent.PartId).Node(p.ParentNode); var cn = L(p.PartId).Node(p.Node);
                if (pn == null || cn == null) continue;
                p.Position = new[] { 0f, parent.Position[1] + pn.Position[1] - cn.Position[1], 0f };
            }
        }

        /// <summary>
        /// Direct-ascent lunar lander: Gemini capsule + service module (60% load: enough for lunar ascent and
        /// trans-Earth injection) on a descent stage with an extra hypergolic tank (≈2.7 km/s for the descent).
        /// </summary>
        public static Craft LunarLander(Func<string, PartDefinition> L, bool withUpperStage)
        {
            var c = new Craft { Name = withUpperStage ? "Kármán Lunar (LEO stack)" : "Kármán Lunar Lander" };
            var pod = c.Add(L("pod.gemini"), -1, null, null);
            var smTank = c.Add(L("tank.hypergolic.30x1.5"), pod.Id, "bottom", "top");
            foreach (var r in smTank.Resources) r.Mass *= 0.6;
            var sps = c.Add(L("engine.sps"), smTank.Id, "bottom", "top");
            var sep = c.Add(L("decoupler.lm"), sps.Id, "bottom", "top");
            var dTank = c.Add(L("tank.hypergolic.30x1.5"), sep.Id, "bottom", "top");
            var descent = c.Add(L("lander.descent"), dTank.Id, "bottom", "top");
            if (withUpperStage)
            {
                // Trans-lunar stage: kerolox tank + vacuum engine, separated under the descent stage.
                var dec = c.Add(L("decoupler.37"), descent.Id, "bottom", "top");
                var tank = c.Add(L("tank.kerolox.37x16"), dec.Id, "bottom", "top");
                c.Add(L("engine.kr1v"), tank.Id, "bottom", "top");
            }
            LayoutStack(c, L);
            AutoStaging.Apply(c, L);
            return c;
        }
    }
}
