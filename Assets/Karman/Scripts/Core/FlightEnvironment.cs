using System;

namespace Karman.Core
{
    /// <summary>Atmospheric forces and entry heating shared by the game and the headless tests.</summary>
    public static class FlightEnvironment
    {
        /// <summary>Aerodynamic drag acceleration (inertial) for position/velocity relative to a rotating body.</summary>
        public static Vector3d DragAcceleration(Vector3d r, Vector3d v, Vector3d pole, double omega, AtmosphereSample air,
                                                double mass, double frontalArea, double cdOverride, double parachuteCdA)
        {
            if (air.Density <= 0) return Vector3d.zero;
            Vector3d vRel = v - Vector3d.Cross(pole * omega, r);
            double speed = vRel.magnitude;
            if (speed < 1e-3) return Vector3d.zero;
            double cd = cdOverride > 0 ? cdOverride : EarthAtmosphere.DragCoefficient(speed / air.SpeedOfSound);
            double cdA = cd * frontalArea + parachuteCdA;
            return vRel * (-0.5 * air.Density * speed * cdA / mass);
        }

        /// <summary>
        /// Lift of a lifting capsule: magnitude L/D × |drag|, perpendicular to the air-relative velocity, rolled about
        /// the velocity by the bank angle from "lift up" (0 = away from the planet).
        /// </summary>
        public static Vector3d LiftAcceleration(Vector3d r, Vector3d v, Vector3d pole, double omega, Vector3d dragAccel, double liftToDrag, double bankRad)
        {
            if (liftToDrag <= 0) return Vector3d.zero;
            Vector3d vRel = v - Vector3d.Cross(pole * omega, r);
            Vector3d vHat = vRel.normalized;
            Vector3d up = r.normalized;
            Vector3d liftUp = (up - vHat * Vector3d.Dot(up, vHat));
            if (liftUp.sqrMagnitude < 1e-12) return Vector3d.zero;
            liftUp = liftUp.normalized;
            Vector3d side = Vector3d.Cross(vHat, liftUp);
            Vector3d dir = liftUp * Math.Cos(bankRad) + side * Math.Sin(bankRad);
            return dir * (dragAccel.magnitude * liftToDrag);
        }

        /// <summary>
        /// Simple entry bank law: full lift up while descending into the denser air, roll toward lift-down when
        /// climbing fast (prevents skip-out), and modulate to hold deceleration below the limit.
        /// </summary>
        public static double EntryBank(double verticalSpeed, double decelG, double altitude)
        {
            if (altitude > 95_000) return 0;
            if (verticalSpeed > 60) return Math.PI * 0.75;        // climbing: lift mostly down
            if (verticalSpeed > 0) return Math.PI * 0.5;          // level-ish: lift sideways
            return decelG > 6 ? 0 : (decelG < 3 && verticalSpeed < -150 ? 0 : Math.PI / 3);
        }

        /// <summary>Sutton–Graves convective stagnation-point heat flux, W/m² (k = 1.7415e-4 for Earth air).</summary>
        public static double StagnationHeatFlux(double density, double speed, double noseRadius)
        {
            if (density <= 0 || noseRadius <= 0) return 0;
            return 1.7415e-4 * Math.Sqrt(density / noseRadius) * speed * speed * speed;
        }
    }
}
