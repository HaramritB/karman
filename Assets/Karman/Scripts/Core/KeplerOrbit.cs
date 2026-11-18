using System;

namespace Karman.Core
{
    /// <summary>Position and velocity relative to a central body, inertial frame, SI units.</summary>
    [Serializable]
    public struct StateVector
    {
        public Vector3d position;
        public Vector3d velocity;
        public StateVector(Vector3d r, Vector3d v) { position = r; velocity = v; }
        public static StateVector operator +(StateVector a, StateVector b) => new StateVector(a.position + b.position, a.velocity + b.velocity);
        public static StateVector operator -(StateVector a, StateVector b) => new StateVector(a.position - b.position, a.velocity - b.velocity);
    }

    /// <summary>
    /// Classical (osculating) Keplerian elements about a body with gravitational parameter mu.
    /// Angles in radians. Supports elliptic (e &lt; 1) and hyperbolic (e &gt; 1) orbits; for
    /// hyperbolic orbits semiMajorAxis is negative. Parabolic orbits are treated as e = 1 ± 1e-9.
    /// </summary>
    [Serializable]
    public struct KeplerOrbit
    {
        public double mu;
        public double semiMajorAxis;
        public double eccentricity;
        public double inclination;
        public double longitudeOfAscendingNode;
        public double argumentOfPeriapsis;
        public double meanAnomalyAtEpoch;
        public double epoch;                 // simulation seconds

        const double TwoPi = 2.0 * Math.PI;

        public bool IsHyperbolic => eccentricity >= 1.0;
        public double SemiLatusRectum => semiMajorAxis * (1 - eccentricity * eccentricity);
        public double PeriapsisRadius => semiMajorAxis * (1 - eccentricity);
        public double ApoapsisRadius => IsHyperbolic ? double.PositiveInfinity : semiMajorAxis * (1 + eccentricity);
        public double MeanMotion => Math.Sqrt(mu / Math.Abs(semiMajorAxis * semiMajorAxis * semiMajorAxis));
        public double Period => IsHyperbolic ? double.PositiveInfinity : TwoPi / MeanMotion;
        public double SpecificEnergy => -mu / (2 * semiMajorAxis);

        public static KeplerOrbit FromElements(double mu, double a, double e, double i, double raan, double argp, double meanAnomaly, double epoch)
            => new KeplerOrbit { mu = mu, semiMajorAxis = a, eccentricity = e, inclination = i, longitudeOfAscendingNode = raan, argumentOfPeriapsis = argp, meanAnomalyAtEpoch = meanAnomaly, epoch = epoch };

        /// <summary>Builds elements from a state vector. Reference plane is the frame's XY plane.</summary>
        public static KeplerOrbit FromState(StateVector s, double mu, double epoch)
        {
            Vector3d r = s.position, v = s.velocity;
            double rm = r.magnitude;
            Vector3d h = Vector3d.Cross(r, v);
            double hm = h.magnitude;
            Vector3d n = Vector3d.Cross(Vector3d.unitZ, h);
            double nm = n.magnitude;
            Vector3d evec = Vector3d.Cross(v, h) / mu - r / rm;
            double e = evec.magnitude;
            if (Math.Abs(e - 1.0) < 1e-9) e = e < 1 ? 1 - 1e-9 : 1 + 1e-9;
            double energy = v.sqrMagnitude / 2 - mu / rm;
            double a = -mu / (2 * energy);
            double inc = Math.Acos(Clamp(h.z / hm));

            double raan = 0, argp, nu;
            const double tiny = 1e-11;
            bool equatorial = nm < tiny * hm;
            bool circular = e < 1e-10;
            if (!equatorial)
            {
                raan = Math.Atan2(n.y, n.x);
                if (raan < 0) raan += TwoPi;
            }
            if (circular)
            {
                // Argument of latitude (or true longitude) carried by true anomaly.
                argp = 0;
                Vector3d refDir = equatorial ? Vector3d.unitX : n / nm;
                nu = SignedAngle(refDir, r, h);
            }
            else
            {
                Vector3d refDir = equatorial ? Vector3d.unitX : n / nm;
                argp = SignedAngle(refDir, evec, h);
                nu = SignedAngle(evec, r, h);
            }
            double M = TrueToMean(nu, e);
            return FromElements(mu, a, e, inc, raan, argp, M, epoch);
        }

        public double MeanAnomalyAt(double time)
        {
            double M = meanAnomalyAtEpoch + MeanMotion * (time - epoch);
            if (!IsHyperbolic) { M %= TwoPi; if (M < 0) M += TwoPi; }
            return M;
        }

        public double TrueAnomalyAt(double time) => MeanToTrue(MeanAnomalyAt(time), eccentricity);

        public StateVector StateAt(double time) => StateFromTrueAnomaly(TrueAnomalyAt(time));

        public StateVector StateFromTrueAnomaly(double nu)
        {
            double e = eccentricity;
            double p = SemiLatusRectum;
            double r = p / (1 + e * Math.Cos(nu));
            double sqrtMuP = Math.Sqrt(mu / Math.Abs(p));
            var rPf = new Vector3d(r * Math.Cos(nu), r * Math.Sin(nu), 0);
            var vPf = new Vector3d(-sqrtMuP * Math.Sin(nu), sqrtMuP * (e + Math.Cos(nu)), 0);
            Matrix3d q = PerifocalToInertial;
            return new StateVector(q * rPf, q * vPf);
        }

        public Matrix3d PerifocalToInertial =>
            Matrix3d.RotationZ(longitudeOfAscendingNode) * Matrix3d.RotationX(inclination) * Matrix3d.RotationZ(argumentOfPeriapsis);

        public Vector3d Normal => PerifocalToInertial.Column(2);

        /// <summary>Time from epoch-relative 'time' until the orbit next reaches true anomaly nu (elliptic), or signed time for hyperbolic.</summary>
        public double TimeToTrueAnomaly(double nu, double time)
        {
            double Mt = TrueToMean(nu, eccentricity);
            double Mn = MeanAnomalyAt(time);
            double dM = Mt - Mn;
            if (!IsHyperbolic) { dM %= TwoPi; if (dM < 0) dM += TwoPi; }
            return dM / MeanMotion;
        }

        // ---------- anomaly conversions ----------

        public static double TrueToMean(double nu, double e)
        {
            if (e < 1)
            {
                double E = 2 * Math.Atan2(Math.Sqrt(1 - e) * Math.Sin(nu / 2), Math.Sqrt(1 + e) * Math.Cos(nu / 2));
                double M = E - e * Math.Sin(E);
                M %= TwoPi; if (M < 0) M += TwoPi;
                return M;
            }
            double H = 2 * Atanh(Math.Sqrt((e - 1) / (e + 1)) * Math.Tan(nu / 2));
            return e * Math.Sinh(H) - H;
        }

        public static double MeanToTrue(double M, double e)
        {
            if (e < 1)
            {
                double E = SolveKeplerElliptic(M, e);
                return 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
            }
            double H = SolveKeplerHyperbolic(M, e);
            return 2 * Math.Atan(Math.Sqrt((e + 1) / (e - 1)) * Math.Tanh(H / 2));
        }

        /// <summary>Solves M = E - e sin E with Newton iteration and a robust starting guess.</summary>
        public static double SolveKeplerElliptic(double M, double e)
        {
            M %= TwoPi; if (M > Math.PI) M -= TwoPi; else if (M < -Math.PI) M += TwoPi;
            double E = e < 0.8 ? M : (M >= 0 ? Math.PI : -Math.PI);
            for (int k = 0; k < 50; k++)
            {
                double f = E - e * Math.Sin(E) - M;
                double fp = 1 - e * Math.Cos(E);
                double dE = f / fp;
                E -= dE;
                if (Math.Abs(dE) < 1e-14) break;
            }
            return E;
        }

        /// <summary>Solves M = e sinh H - H.</summary>
        public static double SolveKeplerHyperbolic(double M, double e)
        {
            double H = Math.Abs(M) < 1e-12 ? 0 : Math.Sign(M) * Math.Log(2 * Math.Abs(M) / e + 1.8);
            for (int k = 0; k < 100; k++)
            {
                double f = e * Math.Sinh(H) - H - M;
                double fp = e * Math.Cosh(H) - 1;
                double dH = f / fp;
                H -= dH;
                if (Math.Abs(dH) < 1e-14 * Math.Max(1, Math.Abs(H))) break;
            }
            return H;
        }

        static double Atanh(double x) => 0.5 * Math.Log((1 + x) / (1 - x));
        static double Clamp(double x) => Math.Max(-1.0, Math.Min(1.0, x));

        static double SignedAngle(Vector3d from, Vector3d to, Vector3d axis)
        {
            double ang = Math.Atan2(Vector3d.Dot(Vector3d.Cross(from, to), axis.normalized), Vector3d.Dot(from, to));
            if (ang < 0) ang += TwoPi;
            return ang;
        }
    }
}
