using System;

namespace Karman.Core
{
    /// <summary>
    /// Simulation time is seconds of Terrestrial Time (TT) past J2000.0 (2000-01-01 12:00:00 TT).
    /// UT1 is approximated as TT - DeltaT with a fixed DeltaT (good to ~1 s for 2020-2030).
    /// </summary>
    public static class TimeSystem
    {
        public const double DeltaTSeconds = 69.2;

        public static double JulianDateTT(double secondsSinceJ2000) => Constants.JD_J2000 + secondsSinceJ2000 / Constants.SecondsPerDay;
        public static double CenturiesTT(double secondsSinceJ2000) => secondsSinceJ2000 / (Constants.SecondsPerDay * Constants.DaysPerJulianCentury);

        /// <summary>Converts a UTC calendar date to simulation seconds (TT past J2000).</summary>
        public static double FromUtc(DateTime utc)
        {
            var j2000Utc = new DateTime(2000, 1, 1, 11, 58, 55, 816, DateTimeKind.Utc); // J2000 TT expressed in UTC
            double s = (utc - j2000Utc).TotalSeconds;
            // TT-UTC was 64.184 s at J2000 and is 69.184 s since 2017; correct for the 5 leap seconds.
            double extraLeap = utc >= new DateTime(2017, 1, 1) ? 5 : utc >= new DateTime(2015, 7, 1) ? 4 : utc >= new DateTime(2012, 7, 1) ? 3 : utc >= new DateTime(2009, 1, 1) ? 2 : utc >= new DateTime(2006, 1, 1) ? 1 : 0;
            return s + extraLeap;
        }

        public static DateTime ToUtc(double secondsSinceJ2000)
        {
            var j2000Utc = new DateTime(2000, 1, 1, 11, 58, 55, 816, DateTimeKind.Utc);
            var approx = j2000Utc.AddSeconds(secondsSinceJ2000);
            double extraLeap = approx >= new DateTime(2017, 1, 1) ? 5 : approx >= new DateTime(2015, 7, 1) ? 4 : approx >= new DateTime(2012, 7, 1) ? 3 : approx >= new DateTime(2009, 1, 1) ? 2 : approx >= new DateTime(2006, 1, 1) ? 1 : 0;
            return approx.AddSeconds(-extraLeap);
        }

        /// <summary>Greenwich Mean Sidereal Time in radians (IAU 1982 expression, UT1 ≈ TT - ΔT).</summary>
        public static double GreenwichMeanSiderealTime(double secondsSinceJ2000)
        {
            double d = (secondsSinceJ2000 - DeltaTSeconds) / Constants.SecondsPerDay;
            double t = d / Constants.DaysPerJulianCentury;
            double gmstDeg = 280.46061837 + 360.98564736629 * d + 0.000387933 * t * t - t * t * t / 38710000.0;
            gmstDeg %= 360.0;
            if (gmstDeg < 0) gmstDeg += 360.0;
            return gmstDeg * Constants.Deg2Rad;
        }
    }
}
