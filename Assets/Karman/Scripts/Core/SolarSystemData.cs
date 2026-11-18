using System.Collections.Generic;
using static Karman.Core.Constants;

namespace Karman.Core
{
    /// <summary>Planetary mean elements (JPL "Keplerian Elements for Approximate Positions of the Major Planets",
    /// E. M. Standish, Table 1, valid 1800-2050 AD). Units: au, degrees, and per-century rates; ecliptic/equinox J2000.</summary>
    public readonly struct PlanetMeanElements
    {
        public readonly double a, aDot, e, eDot, I, IDot, L, LDot, varpi, varpiDot, node, nodeDot;
        public PlanetMeanElements(double a, double aDot, double e, double eDot, double I, double IDot, double L, double LDot, double varpi, double varpiDot, double node, double nodeDot)
        { this.a = a; this.aDot = aDot; this.e = e; this.eDot = eDot; this.I = I; this.IDot = IDot; this.L = L; this.LDot = LDot; this.varpi = varpi; this.varpiDot = varpiDot; this.node = node; this.nodeDot = nodeDot; }
    }

    public static class SolarSystemData
    {
        public const string Sun = "sun", Mercury = "mercury", Venus = "venus", Earth = "earth", Moon = "moon",
            Mars = "mars", Jupiter = "jupiter", Saturn = "saturn", Uranus = "uranus", Neptune = "neptune";

        static CelestialBodyDefinition Body(string id, string name, string parent, double gm, double rKm, double eqKm, double rotDays,
            double poleRa, double poleDec, double w0, double tilt, float r, float g, float b)
            => new CelestialBodyDefinition
            {
                Id = id, DisplayName = name, ParentId = parent, GravitationalParameter = gm,
                MeanRadius = rKm * 1000, EquatorialRadius = eqKm * 1000, SiderealRotationPeriod = rotDays * SecondsPerDay,
                PoleRightAscension = poleRa * Deg2Rad, PoleDeclination = poleDec * Deg2Rad, PrimeMeridianAtJ2000 = w0 * Deg2Rad,
                AxialTilt = tilt * Deg2Rad, DisplayColor = new[] { r, g, b },
            };

        public static List<CelestialBodyDefinition> CreateDefinitions()
        {
            var earth = Body(Earth, "Earth", Sun, 3.986004418e14, 6371.0, 6378.137, 0.99726968, 0, 90, 0, 23.44, 0.35f, 0.55f, 1f);
            earth.Playable = true;
            earth.Atmosphere = new AtmosphereDefinition
            {
                SeaLevelPressure = 101_325, SeaLevelDensity = 1.225, ScaleHeight = 8_500, Height = 140_000,
                RayleighScaleHeight = 8_000, MieScaleHeight = 1_200,
            };
            var moon = Body(Moon, "Moon", Earth, 4.9028e12, 1737.4, 1738.1, 27.321661, 269.9949, 66.5392, 38.3213, 6.68, 0.75f, 0.75f, 0.72f);
            moon.TidallyLocked = true; moon.Playable = true;
            var venus = Body(Venus, "Venus", Sun, 3.24858592e14, 6051.8, 6051.8, -243.018, 272.76, 67.16, 160.20, 177.36, 0.95f, 0.85f, 0.6f);
            venus.Atmosphere = new AtmosphereDefinition { SeaLevelPressure = 9.2e6, SeaLevelDensity = 65, ScaleHeight = 15_900, Height = 250_000, RayleighScaleHeight = 15_900, MieScaleHeight = 15_900 };
            var mars = Body(Mars, "Mars", Sun, 4.282837e13, 3389.5, 3396.2, 1.025957, 317.269, 54.432, 176.630, 25.19, 0.85f, 0.45f, 0.3f);
            mars.Atmosphere = new AtmosphereDefinition { SeaLevelPressure = 610, SeaLevelDensity = 0.020, ScaleHeight = 11_100, Height = 125_000, RayleighScaleHeight = 11_100, MieScaleHeight = 11_100 };
            return new List<CelestialBodyDefinition>
            {
                Body(Sun, "Sun", null, 1.32712440018e20, 695_700, 695_700, 25.38, 286.13, 63.87, 84.176, 7.25, 1f, 0.9f, 0.7f),
                Body(Mercury, "Mercury", Sun, 2.2031868e13, 2439.4, 2440.5, 58.6462, 281.0103, 61.4155, 329.5988, 0.034, 0.6f, 0.58f, 0.55f),
                venus,
                earth,
                moon,
                mars,
                Body(Jupiter, "Jupiter", Sun, 1.26686534e17, 69_911, 71_492, 0.41354, 268.057, 64.495, 284.95, 3.13, 0.85f, 0.75f, 0.6f),
                Body(Saturn, "Saturn", Sun, 3.7931187e16, 58_232, 60_268, 0.44401, 40.589, 83.537, 38.90, 26.73, 0.9f, 0.82f, 0.6f),
                Body(Uranus, "Uranus", Sun, 5.793939e15, 25_362, 25_559, -0.71833, 257.311, -15.175, 203.81, 97.77, 0.6f, 0.85f, 0.9f),
                Body(Neptune, "Neptune", Sun, 6.836529e15, 24_622, 24_764, 0.67125, 299.36, 43.46, 249.978, 28.32, 0.35f, 0.5f, 1f),
            };
        }

        public static readonly Dictionary<string, PlanetMeanElements> MeanElements = new Dictionary<string, PlanetMeanElements>
        {
            [Mercury] = new PlanetMeanElements(0.38709927, 0.00000037, 0.20563593, 0.00001906, 7.00497902, -0.00594749, 252.25032350, 149472.67411175, 77.45779628, 0.16047689, 48.33076593, -0.12534081),
            [Venus] = new PlanetMeanElements(0.72333566, 0.00000390, 0.00677672, -0.00004107, 3.39467605, -0.00078890, 181.97909950, 58517.81538729, 131.60246718, 0.00268329, 76.67984255, -0.27769418),
            ["emb"] = new PlanetMeanElements(1.00000261, 0.00000562, 0.01671123, -0.00004392, -0.00001531, -0.01294668, 100.46457166, 35999.37244981, 102.93768193, 0.32327364, 0.0, 0.0),
            [Mars] = new PlanetMeanElements(1.52371034, 0.00001847, 0.09339410, 0.00007882, 1.84969142, -0.00813131, -4.55343205, 19140.30268499, -23.94362959, 0.44441088, 49.55953891, -0.29257343),
            [Jupiter] = new PlanetMeanElements(5.20288700, -0.00011607, 0.04838624, -0.00013253, 1.30439695, -0.00183714, 34.39644051, 3034.74612775, 14.72847983, 0.21252668, 100.47390909, 0.20469106),
            [Saturn] = new PlanetMeanElements(9.53667594, -0.00125060, 0.05386179, -0.00050991, 2.48599187, 0.00193609, 49.95424423, 1222.49362201, 92.59887831, -0.41897216, 113.66242448, -0.28867794),
            [Uranus] = new PlanetMeanElements(19.18916464, -0.00196176, 0.04725744, -0.00004397, 0.77263783, -0.00242939, 313.23810451, 428.48202785, 170.95427630, 0.40805281, 74.01692503, 0.04240589),
            [Neptune] = new PlanetMeanElements(30.06992276, 0.00026291, 0.00859048, 0.00005105, 1.77004347, 0.00035372, -55.12002969, 218.45945325, 44.96476227, -0.32241464, 131.78422574, -0.00508664),
        };
    }
}
