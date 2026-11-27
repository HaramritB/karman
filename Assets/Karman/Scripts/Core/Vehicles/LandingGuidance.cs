using System;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Powered-descent guidance for airless bodies (radar-altimeter style, height above the local terrain).
    /// Braking: kill horizontal velocity at the best available deceleration while holding a vertical-speed profile
    /// that spreads the remaining height over the estimated time-to-go. Terminal: vertical-speed schedule down to
    /// ~1.5 m/s at contact. Vertical acceleration is allocated first, the remainder brakes horizontally.
    /// </summary>
    public sealed class LandingGuidance
    {
        public double StartHeight = 15_500;      // m above terrain: powered descent initiation
        public double TouchdownSpeed = 1.5;      // m/s
        public double Tau = 2.5;                 // s velocity-error time constant
        public enum Mode { Coast, Braking, Terminal, Landed }
        public Mode Phase { get; private set; } = Mode.Coast;
        bool descended;   // the coast has been heading down, so a turn upward is the periapsis

        /// <param name="v">velocity relative to the rotating surface</param>
        public void Update(Vector3d r, Vector3d v, double mu, double heightAboveTerrain, double maxAccel, bool landed,
                           out double throttle, out Vector3d direction)
        {
            Vector3d up = r.normalized;
            double g = mu / r.sqrMagnitude;
            double vs = Vector3d.Dot(v, up);
            Vector3d vh = v - up * vs;
            double vhMag = vh.magnitude;
            if (landed) { Phase = Mode.Landed; throttle = 0; direction = up; return; }
            if (Phase == Mode.Coast)
            {
                // Powered descent starts StartHeight above the terrain, or at periapsis when the ground under it lies
                // lower than that (a 15 km periapsis over a basin floor never comes within StartHeight; waiting for the
                // height alone would let the vehicle coast straight back up to orbit).
                if (vs < -0.5) descended = true;
                bool atPeriapsis = descended && vs >= 0 && heightAboveTerrain < 3 * StartHeight;
                if (heightAboveTerrain < StartHeight || atPeriapsis) Phase = Mode.Braking;
            }
            if (Phase == Mode.Braking && vhMag < 3 && heightAboveTerrain < 400) Phase = Mode.Terminal;
            if (Phase == Mode.Coast) { throttle = 0; direction = (-v).normalized; return; }

            double aMax = Math.Max(1e-3, maxAccel);
            double h = Math.Max(0, heightAboveTerrain);
            double vsTarget;
            if (Phase == Mode.Braking)
            {
                double aH = Math.Sqrt(Math.Max(0, aMax * aMax * 0.85 * 0.85 - g * g));
                double tGo = vhMag / Math.Max(0.1, aH) + 10;
                vsTarget = -Math.Min(150, 1.25 * h / tGo);
                if (h < 1500) vsTarget = Math.Max(vsTarget, -Math.Max(TouchdownSpeed, h * 0.06));
            }
            else vsTarget = -Math.Max(TouchdownSpeed, Math.Min(25, h * 0.08));
            // Never descend faster than the engine can arrest within the remaining height (with margin).
            double netDecel = Math.Max(0.05, 0.7 * aMax - (g - vhMag * vhMag / r.magnitude));
            vsTarget = Math.Max(vsTarget, -Math.Max(TouchdownSpeed, Math.Sqrt(2 * netDecel * Math.Max(0, h - 20))));

            double aV = (vsTarget - vs) / Tau + g - vhMag * vhMag / r.magnitude;  // gravity less centripetal relief
            aV = Math.Max(0, Math.Min(aMax, aV));
            double aHAvail = Math.Sqrt(Math.Max(0, aMax * aMax - aV * aV));
            Vector3d aHcmd = vh * (-1.0 / Tau);                       // brake horizontal motion
            double aHmag = aHcmd.magnitude;
            if (aHmag > aHAvail) aHcmd = aHcmd * (aHAvail / aHmag);
            Vector3d a = up * aV + aHcmd;
            double am = a.magnitude;
            throttle = Math.Min(1, am / aMax);
            direction = am > 1e-6 ? a / am : up;
        }
    }
}
