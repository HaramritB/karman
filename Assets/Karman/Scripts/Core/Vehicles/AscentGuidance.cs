using System;

namespace Karman.Core.Vehicles
{
    /// <summary>
    /// Closed-loop ascent guidance shared by the in-game ascent assistant and the headless ascent tests.
    /// Vertical rise → square-root gravity-turn pitch program → closed-loop climb that keeps the apoapsis a growing
    /// time ahead until it reaches the target → coast to apoapsis → circularisation burn that holds vertical speed
    /// near zero until the periapsis reaches the target. Engines are shut down if the target apoapsis is reached while
    /// still in the dense atmosphere, to avoid drag losses.
    /// </summary>
    public sealed class AscentGuidance
    {
        public double TargetApoapsis = 190_000;   // m altitude
        public double TargetPeriapsis = 170_000;  // m altitude
        public double HeadingDeg = 90;
        public double TurnStartSpeed = 60;        // m/s surface speed
        public double TurnShapeAltitude = 48_000; // m: pitch program reaches ~88° from vertical here
        public double InsertionSteeringAbove = 42_000;
        public double CoastAbove = 75_000;

        public enum Mode { Vertical, GravityTurn, AtmosphereCoast, Climb, CoastToApoapsis, Circularise, Complete }
        public Mode Phase { get; private set; } = Mode.Vertical;

        /// <param name="maxAccel">Thrust/mass available right now (m/s²), for timing the circularisation burn.</param>
        public void Update(Vector3d r, Vector3d v, Vector3d surfaceVelocity, Vector3d pole, double mu, double bodyRadius, double maxAccel,
                           out double throttle, out Vector3d direction)
        {
            Vector3d up = r.normalized;
            Vector3d east = Vector3d.Cross(pole, up).normalized;
            Vector3d north = Vector3d.Cross(up, east);
            double h = HeadingDeg * Constants.Deg2Rad;
            Vector3d headingDir = east * Math.Sin(h) + north * Math.Cos(h);
            double alt = r.magnitude - bodyRadius;
            var orbit = KeplerOrbit.FromState(new StateVector(r, v), mu, 0);
            double ap = orbit.IsHyperbolic ? double.PositiveInfinity : orbit.ApoapsisRadius - bodyRadius;
            double pe = orbit.PeriapsisRadius - bodyRadius;
            double vs = Vector3d.Dot(v, up);
            Vector3d horiz = v - up * vs;
            horiz = horiz.sqrMagnitude > 1 ? horiz.normalized : headingDir;
            double tAp = orbit.IsHyperbolic ? 1e5 : (vs >= 0 ? orbit.TimeToTrueAnomaly(Math.PI, 0) : -30);
            throttle = 1;

            // ---- transitions
            switch (Phase)
            {
                case Mode.Vertical:
                    if (surfaceVelocity.magnitude > TurnStartSpeed && alt > 300) Phase = Mode.GravityTurn;
                    break;
                case Mode.GravityTurn:
                    if (ap >= TargetApoapsis) Phase = alt < CoastAbove ? Mode.AtmosphereCoast : Mode.CoastToApoapsis;
                    else if (alt > InsertionSteeringAbove) Phase = Mode.Climb;
                    break;
                case Mode.AtmosphereCoast:
                    if (ap < TargetApoapsis - 5_000) Phase = alt > InsertionSteeringAbove ? Mode.Climb : Mode.GravityTurn;
                    else if (alt >= CoastAbove) Phase = Mode.CoastToApoapsis;
                    break;
                case Mode.Climb:
                    if (ap >= TargetApoapsis) Phase = pe >= TargetPeriapsis ? Mode.Complete : Mode.CoastToApoapsis;
                    break;
                case Mode.CoastToApoapsis:
                {
                    if (ap < TargetApoapsis - 5_000 && alt < CoastAbove) { Phase = Mode.Climb; break; }
                    double rAp = orbit.IsHyperbolic ? r.magnitude : orbit.ApoapsisRadius;
                    double dv = Math.Max(0, Math.Sqrt(mu / rAp) - Math.Sqrt(Math.Max(0, mu * (2 / rAp - 1 / orbit.semiMajorAxis))));
                    double burn = dv / Math.Max(0.05, maxAccel);
                    if (tAp <= Math.Max(8, burn * 0.5) || vs < 0) Phase = Mode.Circularise;
                    break;
                }
                case Mode.Circularise:
                    // Periapsis cannot exceed the current altitude: accept a periapsis at (almost) the altitude we hold.
                    if (pe >= Math.Min(TargetPeriapsis, alt - 3_000) && pe > CoastAbove + 60_000 * (CoastAbove > 0 ? 1 : 0) || pe >= TargetPeriapsis
                        || (!orbit.IsHyperbolic && orbit.eccentricity < 0.0015 && pe > 140_000)) Phase = Mode.Complete;
                    break;
            }

            // ---- commands
            switch (Phase)
            {
                case Mode.Vertical:
                    direction = up;
                    break;
                case Mode.GravityTurn:
                {
                    double f = Math.Max(0, Math.Min(1, (alt - 300) / TurnShapeAltitude));
                    double pitchFromVertical = Math.Min(88, Math.Max(3, 90 * Math.Sqrt(f))) * Constants.Deg2Rad;
                    direction = (up * Math.Cos(pitchFromVertical) + headingDir * Math.Sin(pitchFromVertical)).normalized;
                    break;
                }
                case Mode.AtmosphereCoast:
                case Mode.CoastToApoapsis:
                    throttle = 0;
                    direction = horiz;
                    break;
                case Mode.Climb:
                {
                    // Keep the apoapsis ahead; the further below target it is, the further ahead (steeper climb).
                    double goal = 45 + 75 * (1 - Math.Max(0, Math.Min(1, ap / TargetApoapsis)));
                    double phiDeg = Math.Max(-5, Math.Min(45, 0.8 * (goal - tAp)));
                    direction = (horiz * Math.Cos(phiDeg * Constants.Deg2Rad) + up * Math.Sin(phiDeg * Constants.Deg2Rad)).normalized;
                    break;
                }
                case Mode.Circularise:
                {
                    // Burn horizontally, steering the vertical speed to zero so the altitude holds while speed builds.
                    double phiDeg = Math.Max(-10, Math.Min(35, -vs * 0.15));
                    direction = (horiz * Math.Cos(phiDeg * Constants.Deg2Rad) + up * Math.Sin(phiDeg * Constants.Deg2Rad)).normalized;
                    double vCirc = Math.Sqrt(mu / r.magnitude);
                    double deficit = vCirc - Vector3d.Dot(v, horiz);
                    throttle = deficit < 40 ? 0.35 : 1;
                    break;
                }
                default:
                    throttle = 0;
                    direction = v.normalized;
                    break;
            }
        }
    }
}
