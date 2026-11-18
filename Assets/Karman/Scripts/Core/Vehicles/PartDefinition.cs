using System;
using System.Collections.Generic;

namespace Karman.Core.Vehicles
{
    public enum PartCategory { Command, Propellant, Engine, Coupling, Structural, Aerodynamic, Utility }
    public enum Propellant { LiquidOxygen, Kerosene, LiquidHydrogen, Hypergolic, Solid }

    /// <summary>Attachment point in part-local space (metres, Unity-style Y up along the vehicle axis).</summary>
    [Serializable]
    public sealed class AttachNode
    {
        public string Id;
        public float[] Position = new float[3];
        public float[] Direction = { 0, 1, 0 };
        public float Diameter;
        public AttachNode() { }
        public AttachNode(string id, float y, float dirY, float diameter) { Id = id; Position = new[] { 0, y, 0f }; Direction = new[] { 0, dirY, 0f }; Diameter = diameter; }
    }

    [Serializable]
    public sealed class ResourceCapacity
    {
        public Propellant Type;
        public double Mass;  // kg at full load
        public ResourceCapacity() { }
        public ResourceCapacity(Propellant t, double m) { Type = t; Mass = m; }
    }

    /// <summary>Engine performance (vacuum/sea level), propellant mixture by mass fraction.</summary>
    [Serializable]
    public sealed class EngineDefinition
    {
        public double VacuumThrust, SeaLevelThrust;   // N
        public double VacuumIsp, SeaLevelIsp;         // s
        public double MinThrottle = 1;                 // 1 = not throttleable
        public double GimbalDegrees;
        public bool Solid;
        public List<ResourceCapacity> Mixture = new List<ResourceCapacity>(); // Mass = fraction (sums to 1)

        public double IspAt(double pressurePa)
        {
            double f = Math.Max(0, Math.Min(1, pressurePa / 101_325.0));
            return VacuumIsp + (SeaLevelIsp - VacuumIsp) * f;
        }
        public double ThrustAt(double pressurePa) => VacuumThrust * IspAt(pressurePa) / VacuumIsp;
    }

    /// <summary>Data-driven definition of a vehicle part. Physical values are SI and based on real hardware where noted.</summary>
    [Serializable]
    public sealed class PartDefinition
    {
        public string Id;
        public string Title;
        public string Description;
        public string Reference;           // real-world reference hardware, for documentation
        public PartCategory Category;
        public double DryMass;             // kg
        public float Diameter;             // m (top)
        public float Height;               // m
        public float CenterOfMassY;        // m, part-local
        public List<AttachNode> Nodes = new List<AttachNode>();
        public bool AllowSurfaceAttach;
        public float SurfaceAttachOffset;  // m from the attach surface to the part origin along its local +X
        public List<ResourceCapacity> Resources = new List<ResourceCapacity>();
        public EngineDefinition Engine;
        public bool IsDecoupler;
        public string ModelId;             // visual asset key
        public double DragArea;            // m², frontal

        public double WetMass { get { double m = DryMass; foreach (var r in Resources) m += r.Mass; return m; } }
        public AttachNode Node(string id) => Nodes.Find(n => n.Id == id);
    }
}
