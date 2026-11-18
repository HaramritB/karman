using System;
using System.Linq;
using Karman.Core;
using Karman.Core.Vehicles;
using Karman.Persistence;
using NUnit.Framework;

namespace Karman.Tests
{
    public class VehicleTests
    {
        static readonly System.Collections.Generic.List<PartDefinition> Lib = PartLibrary.CreateStarter();
        static readonly Func<string, PartDefinition> L = PartLibrary.Lookup(Lib);
        const double g0 = Constants.StandardGravity;

        static double Prop(PartDefinition d) => d.Resources.Sum(r => r.Mass);

        /// <summary>Upper tank + vacuum engine, decoupler, lower tank + 9-engine cluster.</summary>
        static Craft TwoStage()
        {
            var c = new Craft { Name = "Test Two-Stage" };
            var upper = c.Add(L("tank.kerolox.37x16"), -1, null, null);
            var mvac = c.Add(L("engine.kr1v"), upper.Id, "bottom", "top"); mvac.Stage = 0;
            var dec = c.Add(L("decoupler.37"), upper.Id, "bottom", "top"); dec.Stage = 0;
            var lower = c.Add(L("tank.kerolox.37x32"), dec.Id, "bottom", "top");
            var cluster = c.Add(L("engine.kr1.cluster9"), lower.Id, "bottom", "top"); cluster.Stage = 1;
            return c;
        }

        [Test]
        public void TankCapacityFollowsGeometryAndDensity()
        {
            var t = L("tank.kerolox.37x32");
            double lox = t.Resources.First(r => r.Type == Propellant.LiquidOxygen).Mass;
            double rp1 = t.Resources.First(r => r.Type == Propellant.Kerosene).Mass;
            Assert.AreEqual(PartLibrary.KeroloxOF, lox / rp1, 0.01);
            double volume = lox / PartLibrary.RhoLox + rp1 / PartLibrary.RhoRp1;
            Assert.AreEqual(Math.PI * 3.7 * 3.7 / 4 * 32 * 0.92, volume, 0.5);
            TestContext.WriteLine($"3.7 x 32 m kerolox tank: {(lox + rp1) / 1000:F1} t propellant, {t.DryMass:F0} kg dry");
        }

        [Test]
        public void TwoStageDeltaVMatchesRocketEquation()
        {
            var craft = TwoStage();
            var a = StagingAnalysis.Analyse(craft, L);
            Assert.AreEqual(2, a.Stages.Count);
            double mAll = craft.Parts.Sum(p => L(p.PartId).WetMass);
            double lowerProp = Prop(L("tank.kerolox.37x32"));
            double dv1 = 311 * g0 * Math.Log(mAll / (mAll - lowerProp));
            double mUpper = L("tank.kerolox.37x16").WetMass + L("engine.kr1v").DryMass;
            double dv0 = 348 * g0 * Math.Log(mUpper / (mUpper - Prop(L("tank.kerolox.37x16"))));
            var s1 = a.Stages[0]; var s0 = a.Stages[1];
            TestContext.WriteLine($"Stage 1 Δv {s1.DeltaV:F1} (expected {dv1:F1}), TWR {s1.StartTwr:F2}, burn {s1.BurnTime:F1}s; Stage 0 Δv {s0.DeltaV:F1} (expected {dv0:F1}), burn {s0.BurnTime:F1}s; total {a.TotalDeltaV:F0} m/s");
            Assert.AreEqual(1, s1.Stage);
            Assert.AreEqual(dv1, s1.DeltaV, dv1 * 2e-3);
            Assert.AreEqual(dv0, s0.DeltaV, dv0 * 2e-3);
            Assert.IsTrue(s0.Jettisons);
            Assert.AreEqual(mUpper, s0.StartMass, 1.0);
        }

        [Test]
        public void BurnTimeMatchesMassFlow()
        {
            var craft = TwoStage();
            var a = StagingAnalysis.Analyse(craft, L);
            double mdot = 9 * 914_000 / (311 * g0);
            Assert.AreEqual(Prop(L("tank.kerolox.37x32")) / mdot, a.Stages[0].BurnTime, 1.0);
        }

        [Test]
        public void SeaLevelThrustLowersTwrAndIsp()
        {
            var craft = TwoStage();
            var vac = StagingAnalysis.Analyse(craft, L, 0);
            var sl = StagingAnalysis.Analyse(craft, L, 101_325);
            Assert.Less(sl.Stages[0].StartTwr, vac.Stages[0].StartTwr);
            Assert.AreEqual(282, sl.Stages[0].Isp, 0.01);
        }

        [Test]
        public void SolidBoosterBurnsOnlyItsOwnPropellant()
        {
            var c = new Craft { Name = "SRB test" };
            var core = c.Add(L("tank.kerolox.37x16"), -1, null, null);
            var eng = c.Add(L("engine.kr1"), core.Id, "bottom", "top"); eng.Stage = 1;
            var rd = c.Add(L("decoupler.radial"), core.Id, "", "outer"); rd.Stage = 0;
            var srb = c.Add(L("srb.gem"), rd.Id, "", "top"); srb.Stage = 1;
            var a = StagingAnalysis.Analyse(c, L);
            var first = a.Stages[0];
            Assert.AreEqual(2, first.EngineCount);
            // Stage ends when the strap-on burns out; the core engine carries over into the separation stage.
            Assert.AreEqual(L("srb.gem").Resources[0].Mass / (1_850_000 / (272 * g0)), first.BurnTime, 0.01);
            Assert.Greater(a.Stages[1].DeltaV, 0);
            Assert.IsTrue(a.Stages[1].Jettisons);
        }

        [Test]
        public void MassPropertiesAndCentreOfMass()
        {
            var craft = TwoStage();
            // Stack along -Y: place parts like the VAB would.
            float y = 0;
            foreach (var p in craft.Parts) { var d = L(p.PartId); p.Position[1] = y - d.Height / 2; y -= d.Height; }
            var a = StagingAnalysis.Analyse(craft, L);
            Assert.AreEqual(craft.Parts.Sum(p => L(p.PartId).WetMass), a.TotalMass, 1e-6);
            Assert.Less(a.CenterOfMass[1], 0);
            Assert.Less(a.CenterOfThrust[1], a.CenterOfMass[1]);
        }

        [Test]
        public void CraftJsonRoundTrip()
        {
            var craft = TwoStage();
            craft.Parts[1].Position = new[] { 0.5f, -9f, 0.25f };
            string json = CraftSerializer.ToJson(craft);
            var back = CraftSerializer.FromJson(json);
            Assert.AreEqual(craft.Name, back.Name);
            Assert.AreEqual(craft.Parts.Count, back.Parts.Count);
            Assert.AreEqual(Craft.CurrentFormatVersion, back.FormatVersion);
            Assert.AreEqual(-9f, back.Parts[1].Position[1]);
            Assert.AreEqual(craft.Parts[3].Resources[0].Mass, back.Parts[3].Resources[0].Mass);
            var a1 = StagingAnalysis.Analyse(craft, L); var a2 = StagingAnalysis.Analyse(back, L);
            Assert.AreEqual(a1.TotalDeltaV, a2.TotalDeltaV, 1e-9);
        }

        [Test]
        public void RemoveSubtreeIsUndoable()
        {
            var craft = TwoStage();
            var dec = craft.Parts.First(p => p.PartId == "decoupler.37");
            var removed = craft.RemoveSubtree(dec.Id);
            Assert.AreEqual(3, removed.Count);
            Assert.AreEqual(2, craft.Parts.Count);
            craft.Parts.AddRange(removed);
            Assert.AreEqual(5, craft.Parts.Count);
        }
    }
}

namespace Karman.Tests
{
    public class AutoStagingTests
    {
        static readonly System.Func<string, PartDefinition> L = PartLibrary.Lookup(PartLibrary.CreateStarter());

        [Test]
        public void TwoStageWithBoosters()
        {
            var c = new Craft();
            var upper = c.Add(L("tank.kerolox.37x16"), -1, null, null);
            var mvac = c.Add(L("engine.kr1v"), upper.Id, "bottom", "top");
            var dec = c.Add(L("decoupler.37"), mvac.Id, "bottom", "top");
            var lower = c.Add(L("tank.kerolox.37x32"), dec.Id, "bottom", "top");
            var cluster = c.Add(L("engine.kr1.cluster9"), lower.Id, "bottom", "top");
            var rd = c.Add(L("decoupler.radial"), lower.Id, "", "outer");
            var srb = c.Add(L("srb.gem"), rd.Id, "", "top");
            AutoStaging.Apply(c, L);
            // Fire order: [cluster + SRB] → [radial sep] → [stack sep + MVac]
            Assert.AreEqual(cluster.Stage, srb.Stage);
            Assert.AreEqual(cluster.Stage - 1, rd.Stage);
            Assert.AreEqual(dec.Stage, mvac.Stage);
            Assert.AreEqual(rd.Stage - 1, dec.Stage);
            Assert.AreEqual(0, dec.Stage);
            Assert.AreEqual(-1, upper.Stage);
            var a = StagingAnalysis.Analyse(c, L);
            Assert.AreEqual(3, a.Stages.Count);
            Assert.Greater(a.Stages[0].StartTwr, 1.0);
        }
    }
}
