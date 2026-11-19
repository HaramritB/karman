using System;
using System.Collections.Generic;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Starter part catalogue. Performance figures are modelled on published data for the referenced hardware
    /// (names are generic). Tank capacities follow from geometry and propellant densities.
    /// </summary>
    public static class PartLibrary
    {
        public const double RhoLox = 1141, RhoRp1 = 810, RhoLh2 = 70.85, RhoHypergolic = 1180, RhoSolid = 1800;
        public const double KeroloxOF = 2.36, HydroloxOF = 5.88;
        const double TankStructureFraction = 0.045;   // Al-Li stage tank dry mass / propellant

        static List<ResourceCapacity> Mix(Propellant ox, double of, Propellant fuel) =>
            new List<ResourceCapacity> { new ResourceCapacity(ox, of / (1 + of)), new ResourceCapacity(fuel, 1 / (1 + of)) };

        static PartDefinition Tank(string id, string title, float d, float h, Propellant ox, double of, Propellant fuel, double rhoOx, double rhoFuel, string reference)
        {
            double volume = Math.PI * d * d / 4 * h * 0.92;  // usable fraction after domes/ullage
            double fOx = of / (1 + of), fFuel = 1 / (1 + of);
            double total = volume / (fOx / rhoOx + fFuel / rhoFuel);
            return new PartDefinition
            {
                Id = id, Title = title, Category = PartCategory.Propellant, Diameter = d, Height = h, Reference = reference,
                Description = $"{volume:F0} m³ usable; {total / 1000:F1} t {ox}/{fuel} at O/F {of:F2}.",
                DryMass = Math.Round(total * TankStructureFraction), ModelId = $"tank_{h:0}",
                Nodes = { new AttachNode("top", h / 2, 1, d), new AttachNode("bottom", -h / 2, -1, d) },
                Resources = { new ResourceCapacity(ox, Math.Round(total * fOx)), new ResourceCapacity(fuel, Math.Round(total * fFuel)) },
                AllowSurfaceAttach = true, SurfaceAttachOffset = d / 2, DragArea = Math.PI * d * d / 4,
            };
        }

        static PartDefinition HyperTank(string id, string title, float h)
        {
            const float d = 3.05f;
            double mass = Math.PI * d * d / 4 * h * 0.92 * RhoHypergolic;
            return new PartDefinition
            {
                Id = id, Title = title, Category = PartCategory.Propellant, Diameter = d, Height = h, ModelId = h > 2 ? "tank_s30" : "tank_s15",
                Reference = "Service-module style Aerozine 50/N2O4 tankage (single pooled resource)",
                Description = $"{mass / 1000:F1} t hypergolic propellant.", DryMass = Math.Round(mass * 0.05),
                Nodes = { new AttachNode("top", h / 2, 1, d), new AttachNode("bottom", -h / 2, -1, d) },
                Resources = { new ResourceCapacity(Propellant.Hypergolic, Math.Round(mass)) }, AllowSurfaceAttach = true,
            };
        }

        public static List<PartDefinition> CreateStarter()
        {
            var parts = new List<PartDefinition>();

            parts.Add(new PartDefinition
            {
                Id = "pod.gemini", Title = "Gemini two-crew spacecraft", Category = PartCategory.Command,
                Reference = "NASA Gemini (1965–66): 3,790 kg, 5.8 m × 3.05 m, OAMS hypergolic manoeuvring",
                Description = "Reentry module and adapter with orbital attitude and manoeuvring system.",
                DryMass = 3430, Diameter = 3.05f, Height = 5.8f, ModelId = "gemini",
                Nodes = { new AttachNode("bottom", -2.9f, -1, 3.05f) },
                Resources = { new ResourceCapacity(Propellant.Hypergolic, 360) },
                Engine = new EngineDefinition { NozzleExitDiameter = 0.3f, VacuumThrust = 890, SeaLevelThrust = 490, VacuumIsp = 273, SeaLevelIsp = 150, MinThrottle = 0, Mixture = { new ResourceCapacity(Propellant.Hypergolic, 1) } },
                DragArea = 7.3, DragCoefficient = 1.25,
                // Lunar-rated ablative shield (Apollo-class integrated load) and a ~26 m reefed ringsail main chute (Cd·A ≈ 480 m²).
                HeatShieldCapacity = 450e6, NoseRadius = 1.6, LiftToDrag = 0.25, ParachuteDragArea = 480, ParachuteMaxDeploySpeed = 110, ParachuteDeployAltitude = 6_000,
            });

            parts.Add(new PartDefinition
            {
                Id = "lander.descent", Title = "Lunar descent stage", Category = PartCategory.Propellant,
                Reference = "Apollo LM descent stage: 2,034 kg dry, 8,248 kg Aerozine 50/N2O4, DPS 45 kN throttleable 10–60%+, Isp 311 s",
                Description = "Four-legged descent stage with a deep-throttling engine.", DryMass = 2034, Diameter = 9.03f, Height = 3.31f, ModelId = "lm_descent",
                Nodes = { new AttachNode("top", 1.582f, 1, 4.2f), new AttachNode("bottom", -1.656f, -1, 4.2f) },
                Resources = { new ResourceCapacity(Propellant.Hypergolic, 8248) },
                Engine = new EngineDefinition { NozzleExitDiameter = 1.5f, VacuumThrust = 45_040, SeaLevelThrust = 20_000, VacuumIsp = 311, SeaLevelIsp = 140, MinThrottle = 0.10, GimbalDegrees = 6, Mixture = { new ResourceCapacity(Propellant.Hypergolic, 1) } },
                LandingLegs = true, FootprintRadius = 4.2f, MaxTouchdownSpeed = 3.0, DragArea = 13,
            });
            parts.Add(new PartDefinition
            {
                Id = "lander.ascent", Title = "Lunar ascent stage", Category = PartCategory.Command,
                Reference = "Apollo LM ascent stage: 2,445 kg dry, 2,376 kg propellant, APS 15.6 kN fixed, Isp 311 s",
                Description = "Pressurised crew cabin with a fixed-thrust ascent engine.", DryMass = 2445, Diameter = 4.3f, Height = 4.11f, ModelId = "lm_ascent",
                Nodes = { new AttachNode("bottom", -1.748f, -1, 4.2f), new AttachNode("top", 2.05f, 1, 0.8f) },
                Resources = { new ResourceCapacity(Propellant.Hypergolic, 2376) },
                Engine = new EngineDefinition { NozzleExitDiameter = 0.9f, VacuumThrust = 15_570, SeaLevelThrust = 7_000, VacuumIsp = 311, SeaLevelIsp = 140, MinThrottle = 1, Mixture = { new ResourceCapacity(Propellant.Hypergolic, 1) } },
            });
            parts.Add(new PartDefinition
            {
                Id = "decoupler.lm", Title = "Stage separation bolts", Category = PartCategory.Coupling, IsDecoupler = true,
                Reference = "Explosive nuts/guillotine interstage separation", DryMass = 40, Diameter = 3.0f, Height = 0.05f, ModelId = "none",
                Nodes = { new AttachNode("top", 0.025f, 1, 3.0f), new AttachNode("bottom", -0.025f, -1, 3.0f) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.sps", Title = "SP-91 service engine", Category = PartCategory.Engine,
                Reference = "AJ10-137 (Apollo SPS) class: 91.2 kN, Isp 314 s, pressure-fed hypergolic, 293 kg",
                Description = "Restartable service-module engine.", DryMass = 293, Diameter = 2.15f, Height = 4.15f, CenterOfMassY = 1.0f, ModelId = "engine_vacuum",
                Nodes = { new AttachNode("top", 2.07f, 1, 1.0f), new AttachNode("bottom", -2.07f, -1, 3.0f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 2.4f, VacuumThrust = 91_190, SeaLevelThrust = 40_000, VacuumIsp = 314, SeaLevelIsp = 160, MinThrottle = 1, GimbalDegrees = 6, Mixture = { new ResourceCapacity(Propellant.Hypergolic, 1) } },
            });

            parts.Add(Tank("tank.kerolox.37x8", "Kerolox tank 3.7 × 8 m", 3.7f, 8f, Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene, RhoLox, RhoRp1, "Falcon 9-class 3.66 m Al-Li tankage"));
            parts.Add(Tank("tank.kerolox.37x16", "Kerolox tank 3.7 × 16 m", 3.7f, 16f, Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene, RhoLox, RhoRp1, "Falcon 9-class 3.66 m Al-Li tankage"));
            parts.Add(Tank("tank.kerolox.37x32", "Kerolox tank 3.7 × 32 m", 3.7f, 32f, Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene, RhoLox, RhoRp1, "Falcon 9 first-stage length class"));
            parts.Add(Tank("tank.hydrolox.37x10", "Hydrolox tank 3.7 × 10 m", 3.7f, 10f, Propellant.LiquidOxygen, HydroloxOF, Propellant.LiquidHydrogen, RhoLox, RhoLh2, "Centaur-class balloon tank (heavier, insulated)"));

            parts.Add(HyperTank("tank.hypergolic.30x1.5", "Hypergolic tank 3.05 × 1.5 m", 1.5f));
            parts.Add(HyperTank("tank.hypergolic.30x3", "Hypergolic tank 3.05 × 3 m", 3f));

            parts.Add(new PartDefinition
            {
                Id = "engine.kr1", Title = "KR-1 kerolox engine", Category = PartCategory.Engine,
                Reference = "Merlin 1D-class: 845 kN SL / 914 kN vac, Isp 282 / 311 s, 470 kg, 40–100% throttle",
                Description = "Gas-generator kerosene/LOX booster engine.", DryMass = 470, Diameter = 1.25f, Height = 2.9f, CenterOfMassY = 0.5f, ModelId = "engine_booster",
                Nodes = { new AttachNode("top", 1.45f, 1, 1.25f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 0.92f, VacuumThrust = 914_000, SeaLevelThrust = 845_000, VacuumIsp = 311, SeaLevelIsp = 282, MinThrottle = 0.4, GimbalDegrees = 5, Mixture = Mix(Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.kr1.cluster9", Title = "KR-1 nine-engine cluster", Category = PartCategory.Engine,
                Reference = "Octaweb-class: 9 × Merlin 1D, 7.6 MN SL / 8.2 MN vac",
                Description = "Nine KR-1 engines on a 3.7 m thrust structure.", DryMass = 9 * 470 + 900, Diameter = 3.7f, Height = 3.2f, CenterOfMassY = 0.6f, ModelId = "engine_cluster9",
                Nodes = { new AttachNode("top", 1.6f, 1, 3.7f) },
                Engine = new EngineDefinition { VacuumThrust = 9 * 914_000, SeaLevelThrust = 9 * 845_000, VacuumIsp = 311, SeaLevelIsp = 282, MinThrottle = 0.4, GimbalDegrees = 5, Mixture = Mix(Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.kr1v", Title = "KR-1V vacuum engine", Category = PartCategory.Engine,
                Reference = "Merlin Vacuum-class: 981 kN, Isp 348 s, large radiatively cooled nozzle extension",
                Description = "Upper-stage kerolox engine; poor sea-level performance (flow separation).", DryMass = 490, Diameter = 3.0f, Height = 4.9f, CenterOfMassY = 1.2f, ModelId = "engine_vacuum",
                Nodes = { new AttachNode("top", 2.45f, 1, 1.6f), new AttachNode("bottom", -2.45f, -1, 3.7f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 3.0f, VacuumThrust = 981_000, SeaLevelThrust = 420_000, VacuumIsp = 348, SeaLevelIsp = 150, MinThrottle = 0.39, GimbalDegrees = 5, Mixture = Mix(Propellant.LiquidOxygen, KeroloxOF, Propellant.Kerosene) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.h2", Title = "HL-10 hydrolox engine", Category = PartCategory.Engine,
                Reference = "RL10B-2-class expander cycle: 110 kN, Isp 465.5 s, 301 kg",
                Description = "High-efficiency upper-stage engine.", DryMass = 301, Diameter = 2.15f, Height = 4.15f, CenterOfMassY = 1.0f, ModelId = "engine_vacuum",
                Nodes = { new AttachNode("top", 2.07f, 1, 1.0f), new AttachNode("bottom", -2.07f, -1, 3.7f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 2.1f, VacuumThrust = 110_100, SeaLevelThrust = 40_000, VacuumIsp = 465.5, SeaLevelIsp = 170, MinThrottle = 1, GimbalDegrees = 4, Mixture = Mix(Propellant.LiquidOxygen, HydroloxOF, Propellant.LiquidHydrogen) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.hj2", Title = "HJ-2 hydrolox engine", Category = PartCategory.Engine,
                Reference = "J-2X-class gas-generator hydrolox engine: 1,307 kN vac, Isp 448 s, 2,470 kg, restartable",
                Description = "High-thrust upper-stage hydrogen engine (orbit insertion and trans-lunar injection).", DryMass = 2470, Diameter = 3.05f, Height = 4.7f, CenterOfMassY = 1.0f, ModelId = "engine_vacuum",
                Nodes = { new AttachNode("top", 2.35f, 1, 2.0f), new AttachNode("bottom", -2.35f, -1, 3.7f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 3.0f, VacuumThrust = 1_307_000, SeaLevelThrust = 1_000_000, VacuumIsp = 448, SeaLevelIsp = 300, MinThrottle = 0.8, GimbalDegrees = 4, Mixture = Mix(Propellant.LiquidOxygen, HydroloxOF, Propellant.LiquidHydrogen) },
            });
            parts.Add(new PartDefinition
            {
                Id = "engine.h2.cluster4", Title = "HL-10 four-engine cluster", Category = PartCategory.Engine,
                Reference = "Four RL10B-2-class engines on a 3.7 m thrust structure: 440 kN, Isp 465.5 s",
                Description = "High-energy upper-stage propulsion for lunar transfer.", DryMass = 4 * 301 + 220, Diameter = 3.7f, Height = 4.3f, CenterOfMassY = 0.8f, ModelId = "engine_cluster4",
                Nodes = { new AttachNode("top", 2.15f, 1, 3.7f), new AttachNode("bottom", -2.15f, -1, 3.7f) },
                Engine = new EngineDefinition { NozzleExitDiameter = 1.6f, VacuumThrust = 4 * 110_100, SeaLevelThrust = 4 * 40_000, VacuumIsp = 465.5, SeaLevelIsp = 170, MinThrottle = 1, GimbalDegrees = 4, Mixture = Mix(Propellant.LiquidOxygen, HydroloxOF, Propellant.LiquidHydrogen) },
            });
            parts.Add(new PartDefinition
            {
                Id = "srb.gem", Title = "GS-63 solid booster", Category = PartCategory.Engine,
                Reference = "GEM 63-class: 1.6 m × 20 m, ~44 t propellant, ~1.66 MN, Isp ~245 s SL",
                Description = "Strap-on solid rocket motor. Cannot be throttled or shut down.", DryMass = 5100, Diameter = 1.6f, Height = 20f, CenterOfMassY = 0f, ModelId = "srb",
                Nodes = { new AttachNode("top", 10f, 1, 1.6f) }, AllowSurfaceAttach = true, SurfaceAttachOffset = 0.8f,
                Resources = { new ResourceCapacity(Propellant.Solid, 44_200) },
                Engine = new EngineDefinition { NozzleExitDiameter = 1.2f, VacuumThrust = 1_850_000, SeaLevelThrust = 1_663_000, VacuumIsp = 272, SeaLevelIsp = 245, MinThrottle = 1, Solid = true, Mixture = { new ResourceCapacity(Propellant.Solid, 1) } },
                DragArea = 2.0,
            });
            parts.Add(new PartDefinition
            {
                Id = "decoupler.37", Title = "Stack separator 3.7 m", Category = PartCategory.Coupling, IsDecoupler = true,
                Reference = "Pneumatic pusher separation system", DryMass = 350, Diameter = 3.7f, Height = 0.4f, ModelId = "decoupler",
                Nodes = { new AttachNode("top", 0.2f, 1, 3.7f), new AttachNode("bottom", -0.2f, -1, 3.7f) },
            });
            parts.Add(new PartDefinition
            {
                Id = "decoupler.radial", Title = "Radial separator", Category = PartCategory.Coupling, IsDecoupler = true,
                Reference = "Strap-on booster attach/separation struts", DryMass = 120, Diameter = 0.5f, Height = 1.2f, ModelId = "radial_decoupler",
                AllowSurfaceAttach = true, Nodes = { new AttachNode("outer", 0, 1, 0.5f) },
            });
            parts.Add(new PartDefinition
            {
                Id = "adapter.37to30", Title = "Interstage adapter 3.7 → 3.05 m", Category = PartCategory.Structural,
                Reference = "Conical composite interstage", DryMass = 420, Diameter = 3.05f, Height = 1.5f, ModelId = "adapter",
                Nodes = { new AttachNode("top", 0.75f, 1, 3.05f), new AttachNode("bottom", -0.75f, -1, 3.7f) },
            });
            parts.Add(new PartDefinition
            {
                Id = "interstage.37", Title = "Interstage 3.7 × 4 m", Category = PartCategory.Structural,
                Reference = "Carbon-composite interstage housing a vacuum engine", DryMass = 1100, Diameter = 3.7f, Height = 4f, ModelId = "interstage",
                Nodes = { new AttachNode("top", 2f, 1, 3.7f), new AttachNode("bottom", -2f, -1, 3.7f) },
            });
            parts.Add(new PartDefinition
            {
                Id = "nose.37", Title = "Ogive nose cone 3.7 m", Category = PartCategory.Aerodynamic,
                Reference = "Tangent-ogive aeroshell", DryMass = 650, Diameter = 3.7f, Height = 6f, CenterOfMassY = -1f, ModelId = "nose",
                Nodes = { new AttachNode("bottom", -3f, -1, 3.7f) },
            });
            return parts;
        }

        public static Func<string, PartDefinition> Lookup(IEnumerable<PartDefinition> parts)
        {
            var map = new Dictionary<string, PartDefinition>();
            foreach (var p in parts) map[p.Id] = p;
            return id => map.TryGetValue(id, out var d) ? d : null;
        }
    }
}
