using System;
using static Karman.Core.Constants;

namespace Karman.Core
{
    /// <summary>
    /// Analytical ephemerides in the simulation inertial frame (ICRF-aligned, Earth mean equator and equinox of J2000).
    /// Planets: JPL/Standish mean elements (≈ arcminute accuracy 1800–2050).
    /// Moon: truncated Meeus ch. 47 (ELP-2000/82) series, ≈ 10″ in longitude, ≈ 30 km in distance.
    /// </summary>
    public static class Ephemeris
    {
        static readonly Matrix3d EclipticToEquatorial = Matrix3d.RotationX(ObliquityJ2000);
        public const double MoonEarthMassRatio = 4.9028e12 / 3.986004418e14;

        /// <summary>Heliocentric position of a planet (or "emb") in the equatorial frame, metres.</summary>
        public static Vector3d PlanetHeliocentricPosition(string id, double t)
        {
            var el = PlanetElements(id, t, out double mu);
            return EclipticToEquatorial * el.StateAt(t).position;
        }

        public static StateVector PlanetHeliocentricState(string id, double t)
        {
            var el = PlanetElements(id, t, out _);
            var s = el.StateAt(t);
            return new StateVector(EclipticToEquatorial * s.position, EclipticToEquatorial * s.velocity);
        }

        /// <summary>Osculating heliocentric elements (ecliptic J2000) for a planet at time t.</summary>
        public static KeplerOrbit PlanetElements(string id, double t, out double mu)
        {
            var m = SolarSystemData.MeanElements[id];
            double T = TimeSystem.CenturiesTT(t);
            double a = (m.a + m.aDot * T) * AU;
            double e = m.e + m.eDot * T;
            double I = (m.I + m.IDot * T) * Deg2Rad;
            double L = (m.L + m.LDot * T) * Deg2Rad;
            double varpi = (m.varpi + m.varpiDot * T) * Deg2Rad;
            double node = (m.node + m.nodeDot * T) * Deg2Rad;
            mu = 1.32712440018e20;
            double M = L - varpi;
            double argp = varpi - node;
            return KeplerOrbit.FromElements(mu, a, e, I, node, argp, M, t);
        }

        /// <summary>Geocentric Moon position, equatorial J2000 frame, metres.</summary>
        public static Vector3d MoonGeocentricPosition(double t)
        {
            double T = TimeSystem.CenturiesTT(t);
            double Lp = Norm(218.3164477 + 481267.88123421 * T);
            double D = Norm(297.8501921 + 445267.1114034 * T) * Deg2Rad;
            double M = Norm(357.5291092 + 35999.0502909 * T) * Deg2Rad;
            double Mp = Norm(134.9633964 + 477198.8675055 * T) * Deg2Rad;
            double F = Norm(93.2720950 + 483202.0175233 * T) * Deg2Rad;
            double E = 1 - 0.002516 * T - 0.0000074 * T * T;

            double sl = 6288774 * Math.Sin(Mp) + 1274027 * Math.Sin(2 * D - Mp) + 658314 * Math.Sin(2 * D)
                      + 213618 * Math.Sin(2 * Mp) - 185116 * E * Math.Sin(M) - 114332 * Math.Sin(2 * F)
                      + 58793 * Math.Sin(2 * D - 2 * Mp) + 57066 * E * Math.Sin(2 * D - M - Mp) + 53322 * Math.Sin(2 * D + Mp)
                      + 45758 * E * Math.Sin(2 * D - M) - 40923 * E * Math.Sin(M - Mp) - 34720 * Math.Sin(D)
                      - 30383 * E * Math.Sin(M + Mp) + 15327 * Math.Sin(2 * D - 2 * F) - 12528 * Math.Sin(Mp + 2 * F)
                      + 10980 * Math.Sin(Mp - 2 * F) + 10675 * Math.Sin(4 * D - Mp) + 10034 * Math.Sin(3 * Mp)
                      + 8548 * Math.Sin(4 * D - 2 * Mp) - 7888 * E * Math.Sin(2 * D + M - Mp) - 6766 * E * Math.Sin(2 * D + M)
                      - 5163 * Math.Sin(D - Mp) + 4987 * E * Math.Sin(D + M) + 4036 * E * Math.Sin(2 * D - M + Mp);

            double sr = -20905355 * Math.Cos(Mp) - 3699111 * Math.Cos(2 * D - Mp) - 2955968 * Math.Cos(2 * D)
                      - 569925 * Math.Cos(2 * Mp) + 48888 * E * Math.Cos(M) - 3149 * Math.Cos(2 * F)
                      + 246158 * Math.Cos(2 * D - 2 * Mp) - 152138 * E * Math.Cos(2 * D - M - Mp) - 170733 * Math.Cos(2 * D + Mp)
                      - 204586 * E * Math.Cos(2 * D - M) - 129620 * E * Math.Cos(M - Mp) + 108743 * Math.Cos(D)
                      + 104755 * E * Math.Cos(M + Mp) + 10321 * Math.Cos(2 * D - 2 * F) + 79661 * Math.Cos(Mp - 2 * F)
                      - 34782 * Math.Cos(4 * D - Mp) - 23210 * Math.Cos(3 * Mp) - 21636 * Math.Cos(4 * D - 2 * Mp)
                      + 24208 * E * Math.Cos(2 * D + M - Mp) + 30824 * E * Math.Cos(2 * D + M) - 8379 * Math.Cos(D - Mp)
                      - 16675 * E * Math.Cos(D + M) - 12831 * E * Math.Cos(2 * D - M + Mp);

            double sb = 5128122 * Math.Sin(F) + 280602 * Math.Sin(Mp + F) + 277693 * Math.Sin(Mp - F)
                      + 173237 * Math.Sin(2 * D - F) + 55413 * Math.Sin(2 * D - Mp + F) + 46271 * Math.Sin(2 * D - Mp - F)
                      + 32573 * Math.Sin(2 * D + F) + 17198 * Math.Sin(2 * Mp + F) + 9266 * Math.Sin(2 * D + Mp - F)
                      + 8822 * Math.Sin(2 * Mp - F) + 8216 * E * Math.Sin(2 * D - M - F) + 4324 * Math.Sin(2 * D - 2 * Mp - F)
                      + 4200 * Math.Sin(2 * D + Mp + F);

            // Ecliptic of date -> J2000 ecliptic: remove accumulated general precession in longitude.
            double lon = (Lp + sl / 1e6 - 1.396971 * T) * Deg2Rad;
            double lat = sb / 1e6 * Deg2Rad;
            double dist = (385000.56 + sr / 1000.0) * 1000.0;
            var ecl = new Vector3d(dist * Math.Cos(lat) * Math.Cos(lon), dist * Math.Cos(lat) * Math.Sin(lon), dist * Math.Sin(lat));
            return EclipticToEquatorial * ecl;
        }

        public static StateVector MoonGeocentricState(double t)
        {
            const double h = 30.0;
            var p0 = MoonGeocentricPosition(t);
            var v = (MoonGeocentricPosition(t + h) - MoonGeocentricPosition(t - h)) / (2 * h);
            return new StateVector(p0, v);
        }

        public static StateVector EarthHeliocentricState(double t)
        {
            var emb = PlanetHeliocentricState("emb", t);
            var moon = MoonGeocentricState(t);
            double k = MoonEarthMassRatio / (1 + MoonEarthMassRatio);
            return new StateVector(emb.position - moon.position * k, emb.velocity - moon.velocity * k);
        }

        static double Norm(double deg) { deg %= 360.0; return deg < 0 ? deg + 360.0 : deg; }
    }
}
