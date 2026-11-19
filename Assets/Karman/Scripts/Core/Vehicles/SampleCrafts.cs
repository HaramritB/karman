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
        public static Craft LunarLander(Func<string, PartDefinition> L, bool withUpperStage, bool largeDescentTank = false)
        {
            var c = new Craft { Name = withUpperStage ? "Kármán Lunar (LEO stack)" : "Kármán Lunar Lander" };
            var pod = c.Add(L("pod.gemini"), -1, null, null);
            var smTank = c.Add(L("tank.hypergolic.30x1.5"), pod.Id, "bottom", "top");
            foreach (var r in smTank.Resources) r.Mass *= 0.6;
            var sps = c.Add(L("engine.sps"), smTank.Id, "bottom", "top");
            var sep = c.Add(L("decoupler.lm"), sps.Id, "bottom", "top");
            // Large tank: enough for lunar-orbit insertion plus the landing (Earth-launch mission profile).
            var dTank = c.Add(L(largeDescentTank ? "tank.hypergolic.30x3" : "tank.hypergolic.30x1.5"), sep.Id, "bottom", "top");
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

        /// <summary>
        /// Kármán V: three/five-core kerolox heavy launcher (side cores on radial separators) with a hydrolox
        /// lunar-transfer stage (4 × 3.7 × 10 m tanks, one HJ-2 engine) under the direct-ascent lunar lander.
        /// </summary>
        public static Craft HeavyLunar(Func<string, PartDefinition> L, int sideCores = 4)
        {
            var c = LunarLander(L, false, true);
            c.Name = "Kármán V (lunar mission)";
            var descent = c.Parts.Find(p => p.PartId == "lander.descent");
            var d1 = c.Add(L("decoupler.37"), descent.Id, "bottom", "top");
            var h1 = c.Add(L("tank.hydrolox.37x10"), d1.Id, "bottom", "top");
            var h2 = c.Add(L("tank.hydrolox.37x10"), h1.Id, "bottom", "top");
            var h3 = c.Add(L("tank.hydrolox.37x10"), h2.Id, "bottom", "top");
            var h4 = c.Add(L("tank.hydrolox.37x10"), h3.Id, "bottom", "top");
            var hEng = c.Add(L("engine.hj2"), h4.Id, "bottom", "top");
            var d2 = c.Add(L("decoupler.37"), hEng.Id, "bottom", "top");
            var coreTop = c.Add(L("tank.kerolox.37x16"), d2.Id, "bottom", "top");   // sustainer core: burns on after the side cores
            var core = c.Add(L("tank.kerolox.37x32"), coreTop.Id, "bottom", "top");
            c.Add(L("engine.kr1.cluster9"), core.Id, "bottom", "top");
            LayoutStack(c, L);
            float y = core.Position[1];
            int group = 7000 + sideCores;
            for (int k = 0; k < sideCores; k++)
            {
                double a = 2 * Math.PI * k / sideCores;
                float cx = (float)Math.Cos(a), cz = (float)Math.Sin(a);
                var rot = Yaw(a);
                var rd = c.Add(L("decoupler.radial"), core.Id, "", "outer");
                rd.Position = new[] { cx * 1.85f, y + 6f, cz * 1.85f }; rd.Rotation = rot; rd.SymmetryGroup = group;
                var side = c.Add(L("tank.kerolox.37x32"), rd.Id, "", "surface");
                float r = 1.85f + 0.36f + 1.85f;
                side.Position = new[] { cx * r, y, cz * r }; side.Rotation = rot; side.SymmetryGroup = group + 1;
                var eng = c.Add(L("engine.kr1.cluster9"), side.Id, "bottom", "top");
                var sideTop = c.Add(L("tank.kerolox.37x16"), side.Id, "top", "bottom");
                var nose = c.Add(L("nose.37"), sideTop.Id, "top", "bottom");
                eng.Position = new[] { cx * r, y - 16f - 1.6f, cz * r }; eng.SymmetryGroup = group + 2;
                sideTop.Position = new[] { cx * r, y + 16f + 8f, cz * r }; sideTop.SymmetryGroup = group + 4;
                nose.Position = new[] { cx * r, y + 16f + 16f + 3f, cz * r }; nose.SymmetryGroup = group + 3;
            }
            AutoStaging.Apply(c, L);
            return c;
        }

        /// <summary>Quaternion (x,y,z,w) for a yaw that points local +X along angle a in the XZ plane.</summary>
        static float[] Yaw(double a)
        {
            // Unity LookRotation(forward = Cross(n, up), up) with n = (cos a, 0, sin a) is a rotation of −a about +Y.
            double h = -a / 2;
            return new[] { 0f, (float)Math.Sin(h), 0f, (float)Math.Cos(h) };
        }
    }
}
