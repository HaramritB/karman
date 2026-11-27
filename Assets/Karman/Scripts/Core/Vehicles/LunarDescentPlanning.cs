using System;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Timing of a deorbit burn so that a landing on an airless body ends at a site with a chosen Sun elevation.
    /// Angles are measured in the orbit plane from the projection of the Sun direction, increasing along the motion.
    /// </summary>
    public static class LunarDescentPlanning
    {
        /// <summary>Sun elevation (degrees) above the local horizontal at position <paramref name="r"/> (body-centred).</summary>
        public static double SunElevationDeg(Vector3d r, Vector3d toSun)
            => Math.Asin(Math.Max(-1, Math.Min(1, Vector3d.Dot(r.normalized, toSun.normalized)))) * Constants.Rad2Deg;

        /// <summary>
        /// Degrees of orbit (0–360) still to fly before starting the deorbit burn, so that the point reached
        /// <paramref name="descentArcDeg"/> further along the orbit has the Sun <paramref name="sunElevationDeg"/> above
        /// the horizon. Two sites qualify (local morning and afternoon); the one reached first is used. If the Sun is
        /// too far out of the orbit plane for that elevation to occur, the best-lit point of the orbit is targeted.
        /// </summary>
        public static double DegreesToDeorbitPoint(Vector3d r, Vector3d v, Vector3d toSun, double sunElevationDeg, double descentArcDeg)
        {
            Vector3d h = Vector3d.Cross(r, v).normalized;
            Vector3d sun = toSun.normalized;
            double outOfPlane = Vector3d.Dot(sun, h);
            Vector3d inPlane = sun - h * outOfPlane;
            double cosBeta = inPlane.magnitude;                    // cos of the Sun's angle out of the orbit plane
            if (cosBeta < 1e-9) return 0;                          // Sun on the orbit normal: every site is on the terminator
            Vector3d x = inPlane / cosBeta, y = Vector3d.Cross(h, x);  // y is 90° ahead of x along the motion
            double phiNow = Math.Atan2(Vector3d.Dot(r, y), Vector3d.Dot(r, x)) * Constants.Rad2Deg;

            // sin(elevation) = cos(phi) * cos(beta)  →  the two sites are at ±phiSite.
            double c = Math.Sin(sunElevationDeg * Constants.Deg2Rad) / cosBeta;
            double phiSite = Math.Acos(Math.Max(-1, Math.Min(1, c))) * Constants.Rad2Deg;

            double best = double.MaxValue;
            foreach (double site in new[] { -phiSite, phiSite })
            {
                double wait = Wrap360(site - descentArcDeg - phiNow);
                if (wait < best) best = wait;
            }
            return best;
        }

        /// <summary>
        /// Orbit arc (degrees) flown between a deorbit burn and touchdown: half an orbit down to periapsis, plus the
        /// braking arc from there. The braking arc is the distance needed to cancel the periapsis speed with a constant
        /// thrust while the mass falls (rocket equation), a fraction <paramref name="horizontalFraction"/> of the thrust
        /// acting against the velocity (the rest holds the vehicle up):
        /// s = (vₑ/a₀)·(v − η·vₑ·(1 − e^(−v/(η·vₑ)))).
        /// </summary>
        /// <param name="orbitRadius">radius at the deorbit burn (m)</param>
        /// <param name="periapsisRadius">periapsis radius after the burn (m)</param>
        /// <param name="initialAccel">thrust / mass at the start of braking (m/s²)</param>
        /// <param name="exhaustVelocity">Isp·g₀ of the braking engine (m/s)</param>
        public static double DescentArcDeg(double mu, double orbitRadius, double periapsisRadius, double initialAccel, double exhaustVelocity,
                                           double horizontalFraction = DefaultHorizontalFraction)
        {
            double v = Math.Sqrt(mu * (2 / periapsisRadius - 2 / (orbitRadius + periapsisRadius)));
            if (initialAccel <= 0 || exhaustVelocity <= 0) return 180;
            double ve = exhaustVelocity * horizontalFraction;
            double burnFraction = 1 - Math.Exp(-v / ve);           // share of the initial mass burnt while braking
            double s = exhaustVelocity / initialAccel * (v - ve * burnFraction);
            return 180 + s / periapsisRadius * Constants.Rad2Deg;
        }

        /// <summary>
        /// Share of the thrust that brakes horizontally in <see cref="LandingGuidance"/>'s braking phase, fitted to
        /// the simulated descent of the stock lander (see LunarMissionTests.DeorbitPointGivesSunlitLandingSite).
        /// </summary>
        public const double DefaultHorizontalFraction = 0.85;

        static double Wrap360(double deg)
        {
            deg %= 360;
            return deg < 0 ? deg + 360 : deg;
        }
    }
}
