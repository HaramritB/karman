using System;

namespace Karman.Core
{
    /// <summary>A liquid rocket engine with pressure-dependent specific impulse.</summary>
    [Serializable]
    public sealed class EngineSpec
    {
        public string Name;
        public double VacuumThrust;     // N
        public double VacuumIsp;        // s
        public double SeaLevelIsp;      // s
        public double MinThrottle;      // 0..1
        public double GimbalRange;      // rad

        /// <summary>Isp at ambient pressure p (Pa), linearly interpolated between sea level and vacuum.</summary>
        public double IspAt(double pressurePa)
        {
            double f = Math.Max(0, Math.Min(1.5, pressurePa / 101_325.0));
            return Math.Max(1.0, VacuumIsp + (SeaLevelIsp - VacuumIsp) * f);
        }

        /// <summary>Thrust scales with Isp at constant mass flow.</summary>
        public double ThrustAt(double pressurePa) => VacuumThrust * IspAt(pressurePa) / VacuumIsp;
        public double MassFlowVacuum => VacuumThrust / (VacuumIsp * Constants.StandardGravity);
    }

    /// <summary>Variable-mass propulsion state for a single stage (Phase 1 spacecraft).</summary>
    [Serializable]
    public sealed class PropulsionState
    {
        public double DryMass;
        public double Propellant;
        public double PropellantCapacity;
        public EngineSpec Engine;
        public int EngineCount = 1;

        public double Mass => DryMass + Propellant;
        public double MaxThrust(double pressurePa) => Engine.ThrustAt(pressurePa) * EngineCount;

        /// <summary>Tsiolkovsky Δv remaining at the given ambient pressure.</summary>
        public double DeltaV(double pressurePa = 0) =>
            Engine.IspAt(pressurePa) * Constants.StandardGravity * Math.Log(Mass / DryMass);

        public double BurnTimeAtFullThrottle => Propellant / (Engine.MassFlowVacuum * EngineCount);

        /// <summary>Burns for dt at a throttle setting; returns the delivered thrust (N), consuming propellant.</summary>
        public double Burn(double throttle, double dt, double pressurePa = 0)
        {
            if (throttle <= 0 || Propellant <= 0) return 0;
            throttle = Math.Max(Engine.MinThrottle, Math.Min(1, throttle));
            double isp = Engine.IspAt(pressurePa);
            double thrust = Engine.VacuumThrust * EngineCount * throttle * isp / Engine.VacuumIsp;
            double mdot = thrust / (isp * Constants.StandardGravity);
            double used = mdot * dt;
            if (used > Propellant) { thrust *= Propellant / used; used = Propellant; }
            Propellant -= used;
            return thrust;
        }
    }
}
