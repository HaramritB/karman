namespace Karman.Core
{
    public static class Constants
    {
        public const double G = 6.67430e-11;                 // m^3 kg^-1 s^-2 (CODATA 2018)
        public const double AU = 149_597_870_700.0;          // m (IAU 2012)
        public const double SecondsPerDay = 86_400.0;
        public const double DaysPerJulianCentury = 36_525.0;
        public const double JD_J2000 = 2_451_545.0;          // 2000-01-01 12:00 TT
        public const double Deg2Rad = System.Math.PI / 180.0;
        public const double Rad2Deg = 180.0 / System.Math.PI;
        public const double ObliquityJ2000 = 23.439_291_1 * Deg2Rad; // IAU 1976 mean obliquity at J2000
        public const double StandardGravity = 9.806_65;      // m/s^2, for Isp conversions
    }
}
