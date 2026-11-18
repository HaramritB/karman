using System;

namespace Karman.Core
{
    public delegate Vector3d AccelerationFunc(double time, Vector3d position, Vector3d velocity);

    public static class Integrators
    {
        /// <summary>Classical 4th-order Runge–Kutta step for r'' = a(t, r, v).</summary>
        public static StateVector RK4(StateVector s, double t, double dt, AccelerationFunc a)
        {
            Vector3d r = s.position, v = s.velocity;
            Vector3d k1v = a(t, r, v), k1r = v;
            Vector3d k2v = a(t + dt / 2, r + k1r * (dt / 2), v + k1v * (dt / 2)), k2r = v + k1v * (dt / 2);
            Vector3d k3v = a(t + dt / 2, r + k2r * (dt / 2), v + k2v * (dt / 2)), k3r = v + k2v * (dt / 2);
            Vector3d k4v = a(t + dt, r + k3r * dt, v + k3v * dt), k4r = v + k3v * dt;
            return new StateVector(
                r + (k1r + 2 * k2r + 2 * k3r + k4r) * (dt / 6),
                v + (k1v + 2 * k2v + 2 * k3v + k4v) * (dt / 6));
        }

        /// <summary>Symplectic velocity-Verlet (leapfrog) step for position-only accelerations; bounded energy error.</summary>
        public static StateVector VelocityVerlet(StateVector s, double t, double dt, AccelerationFunc a)
        {
            Vector3d a0 = a(t, s.position, s.velocity);
            Vector3d vHalf = s.velocity + a0 * (dt / 2);
            Vector3d r1 = s.position + vHalf * dt;
            Vector3d a1 = a(t + dt, r1, vHalf);
            return new StateVector(r1, vHalf + a1 * (dt / 2));
        }

        public static Vector3d PointMassGravity(Vector3d r, double mu)
        {
            double r2 = r.sqrMagnitude;
            double rm = Math.Sqrt(r2);
            return r * (-mu / (r2 * rm));
        }

        /// <summary>J2 oblateness perturbation for a body whose pole is the frame's +Z axis.</summary>
        public static Vector3d J2Perturbation(Vector3d r, double mu, double j2, double equatorialRadius)
        {
            double r2 = r.sqrMagnitude, rm = Math.Sqrt(r2);
            double z2 = r.z * r.z / r2;
            double k = 1.5 * j2 * mu * equatorialRadius * equatorialRadius / (r2 * r2 * rm);
            return new Vector3d(k * r.x * (5 * z2 - 1), k * r.y * (5 * z2 - 1), k * r.z * (5 * z2 - 3));
        }
    }
}
