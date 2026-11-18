using System;

namespace Karman.Core
{
    public struct AtmosphereSample
    {
        public double Pressure;     // Pa
        public double Density;      // kg/m^3
        public double Temperature;  // K
        public double SpeedOfSound; // m/s
    }

    /// <summary>
    /// U.S. Standard Atmosphere 1976 (0–86 km, seven layers, geopotential altitude) with an exponential tail to the
    /// 140 km simulation cutoff. Validated against the published tables in the tests.
    /// </summary>
    public static class EarthAtmosphere
    {
        const double R = 287.053, Gamma = 1.4, G0 = 9.80665, Re = 6_356_766; // US76 constants
        public const double Cutoff = 140_000;
        static readonly double[] Hb = { 0, 11_000, 20_000, 32_000, 47_000, 51_000, 71_000, 84_852 };
        static readonly double[] Lb = { -0.0065, 0, 0.001, 0.0028, 0, -0.0028, -0.002 };
        static readonly double[] Tb = { 288.15, 216.65, 216.65, 228.65, 270.65, 270.65, 214.65, 186.946 };
        static readonly double[] Pb = { 101_325, 22_632.06, 5_474.889, 868.0187, 110.9063, 66.93887, 3.956420, 0.3734 };

        public static AtmosphereSample Sample(double geometricAltitude)
        {
            var s = new AtmosphereSample();
            if (geometricAltitude >= Cutoff) { s.Temperature = 186.9; s.SpeedOfSound = Math.Sqrt(Gamma * R * s.Temperature); return s; }
            double z = Math.Max(geometricAltitude, -500);
            double h = Re * z / (Re + z); // geopotential
            if (h < Hb[7])
            {
                int i = 0;
                while (i < 6 && h >= Hb[i + 1]) i++;
                double dh = h - Hb[i];
                if (Lb[i] == 0)
                {
                    s.Temperature = Tb[i];
                    s.Pressure = Pb[i] * Math.Exp(-G0 * dh / (R * Tb[i]));
                }
                else
                {
                    s.Temperature = Tb[i] + Lb[i] * dh;
                    s.Pressure = Pb[i] * Math.Pow(Tb[i] / s.Temperature, G0 / (R * Lb[i]));
                }
            }
            else
            {
                // Above 86 km: isothermal-ish exponential tail (scale height grows with temperature), fading to 0 at cutoff.
                s.Temperature = 186.9 + (z - 86_000) * 0.002;
                double H = R * s.Temperature / G0;
                s.Pressure = Pb[7] * Math.Exp(-(h - Hb[7]) / H) * Math.Max(0, (Cutoff - z) / (Cutoff - 86_000));
            }
            s.Density = s.Pressure / (R * s.Temperature);
            s.SpeedOfSound = Math.Sqrt(Gamma * R * s.Temperature);
            return s;
        }

        /// <summary>Axisymmetric launcher drag coefficient vs Mach (typical rocket curve: transonic rise, supersonic decay).</summary>
        public static double DragCoefficient(double mach)
        {
            if (mach < 0.8) return 0.30;
            if (mach < 1.1) return 0.30 + (mach - 0.8) / 0.3 * 0.45;   // to 0.75
            if (mach < 1.5) return 0.75 - (mach - 1.1) / 0.4 * 0.15;   // to 0.60
            if (mach < 4.0) return 0.60 - (mach - 1.5) / 2.5 * 0.25;   // to 0.35
            return 0.35;
        }
    }
}
