using System;

namespace Karman.Core
{
    /// <summary>
    /// Astronomical state of one vessel (layer 1 of the physics stack). Holds a double-precision state relative to
    /// its current reference body and advances it either by numerical integration (when forces act) or exactly
    /// on Kepler rails (coasting / time warp). Handles sphere-of-influence transitions (patched conics).
    /// </summary>
    public sealed class SpacecraftTrajectory
    {
        public CelestialBody Reference { get; private set; }
        public StateVector RelativeState { get; private set; }
        public double Time { get; private set; }
        public int SoiTransitions { get; private set; }
        public event Action<CelestialBody, CelestialBody> ReferenceChanged;

        /// <summary>Maximum integration substep in seconds when thrusting.</summary>
        public double MaxSubstep = 0.5;

        readonly SolarSystem system;
        KeplerOrbit cachedOrbit;
        bool orbitDirty = true;

        public SpacecraftTrajectory(SolarSystem system, CelestialBody reference, StateVector relativeState, double time)
        {
            this.system = system; Reference = reference; RelativeState = relativeState; Time = time;
        }

        public Vector3d HeliocentricPosition => Reference.Position + RelativeState.position;
        public Vector3d HeliocentricVelocity => Reference.Velocity + RelativeState.velocity;

        public KeplerOrbit Orbit
        {
            get
            {
                if (orbitDirty) { cachedOrbit = KeplerOrbit.FromState(RelativeState, Reference.Mu, Time); orbitDirty = false; }
                return cachedOrbit;
            }
        }

        public void SetState(CelestialBody reference, StateVector rel, double time)
        {
            Reference = reference; RelativeState = rel; Time = time; orbitDirty = true;
        }

        /// <summary>
        /// Advance to time t + dt. <paramref name="thrustAcceleration"/> is a non-gravitational acceleration
        /// (inertial frame, m/s²) held constant over the step; zero means coast on rails.
        /// The solar system must already be set to the end time (bodies are on rails).
        /// </summary>
        public void Advance(double dt, Vector3d thrustAcceleration)
        {
            if (dt <= 0) return;
            if (thrustAcceleration.sqrMagnitude == 0)
            {
                // Exact Kepler propagation about the reference body.
                var orbit = Orbit;
                double tEnd = Time + dt;
                if (orbit.IsHyperbolic || orbit.eccentricity < 0.999999)
                    RelativeState = orbit.StateAt(tEnd);
                Time = tEnd;
                // Keep cached orbit valid (elements unchanged on rails).
            }
            else
            {
                int n = Math.Max(1, (int)Math.Ceiling(dt / MaxSubstep));
                double h = dt / n;
                double mu = Reference.Mu;
                Vector3d aT = thrustAcceleration;
                AccelerationFunc acc = (tt, r, v) => Integrators.PointMassGravity(r, mu) + aT;
                var s = RelativeState;
                for (int i = 0; i < n; i++) { s = Integrators.RK4(s, Time, h, acc); Time += h; }
                RelativeState = s;
                orbitDirty = true;
            }
            CheckSphereOfInfluence();
        }

        /// <summary>Re-express the state relative to whichever body now dominates (patched conics).</summary>
        public bool CheckSphereOfInfluence()
        {
            var helio = HeliocentricPosition;
            CelestialBody next = Reference;
            if (Reference.Parent != null && RelativeState.position.sqrMagnitude > Reference.SphereOfInfluence * Reference.SphereOfInfluence)
                next = Reference.Parent;
            else
            {
                foreach (var c in Reference.Children)
                    if ((helio - c.Position).sqrMagnitude < c.SphereOfInfluence * c.SphereOfInfluence) { next = c; break; }
            }
            if (next == Reference) return false;
            var old = Reference;
            var helioState = new StateVector(helio, HeliocentricVelocity);
            RelativeState = helioState - next.HeliocentricState;
            Reference = next;
            orbitDirty = true;
            SoiTransitions++;
            ReferenceChanged?.Invoke(old, next);
            return true;
        }

        // ------------- telemetry helpers -------------
        public double Altitude => RelativeState.position.magnitude - Reference.Radius;
        public double OrbitalSpeed => RelativeState.velocity.magnitude;
        public double ApoapsisAltitude => Orbit.ApoapsisRadius - Reference.Radius;
        public double PeriapsisAltitude => Orbit.PeriapsisRadius - Reference.Radius;

        /// <summary>Inclination relative to the reference body's equator, radians.</summary>
        public double EquatorialInclination
        {
            get
            {
                Vector3d h = Vector3d.Cross(RelativeState.position, RelativeState.velocity);
                Vector3d pole = Reference.Orientation.Column(2);
                return Vector3d.Angle(h, pole);
            }
        }

        /// <summary>Surface-relative velocity (subtracting body rotation), inertial frame.</summary>
        public Vector3d SurfaceVelocity
        {
            get
            {
                Vector3d pole = Reference.Orientation.Column(2);
                double omega = 2 * Math.PI / Reference.Definition.SiderealRotationPeriod;
                return RelativeState.velocity - Vector3d.Cross(pole * omega, RelativeState.position);
            }
        }

        public double VerticalSpeed => Vector3d.Dot(RelativeState.velocity, RelativeState.position.normalized);

        /// <summary>Geodetic-ish (spherical) latitude/longitude in the body-fixed frame, radians.</summary>
        public void LatLon(out double lat, out double lon)
        {
            Vector3d bf = Reference.Orientation.Transposed * RelativeState.position;
            lat = Math.Asin(bf.z / bf.magnitude);
            lon = Math.Atan2(bf.y, bf.x);
        }
    }
}
