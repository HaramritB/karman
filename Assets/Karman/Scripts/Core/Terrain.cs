using System;
using System.Collections.Generic;

namespace Karman.Core
{
    /// <summary>Surface height of a body (metres above its reference sphere) for a body-fixed unit direction.</summary>
    public interface ITerrainHeight
    {
        double Height(Vector3d bodyFixedDir);
        /// <summary>Largest |height| the field can return, for culling and safety margins.</summary>
        double MaxAbsHeight { get; }
    }

    /// <summary>Per-body terrain registry; bodies without an entry are treated as smooth spheres.</summary>
    public static class TerrainRegistry
    {
        static readonly Dictionary<string, ITerrainHeight> map = new Dictionary<string, ITerrainHeight>();
        public static void Register(string bodyId, ITerrainHeight t) => map[bodyId] = t;
        public static ITerrainHeight Get(string bodyId) => map.TryGetValue(bodyId, out var t) ? t : null;
        public static double HeightAt(CelestialBody body, Vector3d relativePosition)
        {
            var t = Get(body.Id);
            if (t == null) return 0;
            Vector3d bf = body.Orientation.Transposed * relativePosition;
            return t.Height(bf.normalized);
        }
    }

    /// <summary>
    /// Lunar terrain: LOLA global DEM (CGI Moon Kit, 16 px/deg ≈ 1.9 km/pixel, bicubic-smoothed bilinear) plus
    /// deterministic procedural detail below the DEM resolution: fractal relief and three populations of simple
    /// bowl craters (depth/diameter 0.2, raised rims) at 4 km, 1 km and 250 m spacing. The same function feeds the
    /// terrain mesh and the landing physics, so what you see is what you touch.
    /// </summary>
    public sealed class LunarTerrain : ITerrainHeight
    {
        public const double ReferenceRadius = 1_737_400;
        readonly ushort[] dem;
        readonly int width, height;
        public bool Detail = true;
        public double MaxAbsHeight => 11_500;

        /// <param name="raw">uint16 LE samples, row 0 = +90° latitude, column 0 = 180°W.</param>
        public LunarTerrain(byte[] raw, int width, int height)
        {
            this.width = width; this.height = height;
            dem = new ushort[width * height];
            Buffer.BlockCopy(raw, 0, dem, 0, Math.Min(raw.Length, dem.Length * 2));
        }

        double Sample(int x, int y)
        {
            x = ((x % width) + width) % width;
            y = Math.Max(0, Math.Min(height - 1, y));
            return dem[y * width + x] * 0.5 - 10_000.0;
        }

        /// <summary>DEM height (m) by latitude/longitude in radians.</summary>
        public double DemHeight(double lat, double lon)
        {
            double u = (lon / (2 * Math.PI) + 0.5) * width - 0.5;
            double v = (0.5 - lat / Math.PI) * height - 0.5;
            int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
            double fx = u - x0, fy = v - y0;
            // Smoothstep weights remove the bilinear creases visible at low sun angles.
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            double a = Sample(x0, y0), b = Sample(x0 + 1, y0), c = Sample(x0, y0 + 1), d = Sample(x0 + 1, y0 + 1);
            return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy;
        }

        public double Height(Vector3d dir)
        {
            double lat = Math.Asin(Math.Max(-1, Math.Min(1, dir.z)));
            double lon = Math.Atan2(dir.y, dir.x);
            double h = DemHeight(lat, lon);
            if (!Detail) return h;
            Vector3d p = dir * ReferenceRadius;
            h += Fbm(p / 3000.0) * 45.0;
            h += Craters(p, 4000, 0.30, 400, 1500);
            h += Craters(p, 1000, 0.35, 90, 380);
            h += Craters(p, 250, 0.40, 18, 90);
            return h;
        }

        // ---------------------------------------------------------------- procedural detail

        static double Hash(long x, long y, long z, int k)
        {
            unchecked
            {
                ulong n = (ulong)(x * 73856093L ^ y * 19349663L ^ z * 83492791L ^ k * 2654435761L);
                n ^= n >> 33; n *= 0xff51afd7ed558ccdUL; n ^= n >> 33; n *= 0xc4ceb9fe1a85ec53UL; n ^= n >> 33;
                return (n & 0xFFFFFF) / (double)0x1000000;
            }
        }

        static double ValueNoise(Vector3d p)
        {
            long x0 = (long)Math.Floor(p.x), y0 = (long)Math.Floor(p.y), z0 = (long)Math.Floor(p.z);
            double fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
            double L(double a, double b, double t) => a + (b - a) * t;
            double c000 = Hash(x0, y0, z0, 0), c100 = Hash(x0 + 1, y0, z0, 0), c010 = Hash(x0, y0 + 1, z0, 0), c110 = Hash(x0 + 1, y0 + 1, z0, 0);
            double c001 = Hash(x0, y0, z0 + 1, 0), c101 = Hash(x0 + 1, y0, z0 + 1, 0), c011 = Hash(x0, y0 + 1, z0 + 1, 0), c111 = Hash(x0 + 1, y0 + 1, z0 + 1, 0);
            return L(L(L(c000, c100, fx), L(c010, c110, fx), fy), L(L(c001, c101, fx), L(c011, c111, fx), fy), fz) * 2 - 1;
        }

        static double Fbm(Vector3d p)
        {
            double s = 0, a = 1, norm = 0;
            for (int i = 0; i < 5; i++) { s += ValueNoise(p) * a; norm += a; p = p * 2.07; a *= 0.48; }
            return s / norm;
        }

        /// <summary>Sum of simple-crater profiles from a jittered 3D cell grid of the given spacing.</summary>
        static double Craters(Vector3d p, double cell, double probability, double rMin, double rMax)
        {
            long cx = (long)Math.Floor(p.x / cell), cy = (long)Math.Floor(p.y / cell), cz = (long)Math.Floor(p.z / cell);
            double h = 0;
            int level = (int)cell;
            for (long i = -1; i <= 1; i++)
            for (long j = -1; j <= 1; j++)
            for (long k = -1; k <= 1; k++)
            {
                long x = cx + i, y = cy + j, z = cz + k;
                if (Hash(x, y, z, level) > probability) continue;
                var c = new Vector3d((x + Hash(x, y, z, level + 1)) * cell, (y + Hash(x, y, z, level + 2)) * cell, (z + Hash(x, y, z, level + 3)) * cell);
                c = c.normalized * ReferenceRadius;  // crater centres lie on the surface
                double rc = rMin + (rMax - rMin) * Math.Pow(Hash(x, y, z, level + 4), 2.2);
                double d = (p - c).magnitude;
                double xr = d / rc;
                if (xr > 1.8) continue;
                double depth = 0.2 * 2 * rc;
                double bowl = xr < 1 ? depth * (xr * xr - 1) * 0.85 : 0;
                double rim = 0.045 * 2 * rc * Math.Exp(-Math.Pow((xr - 1) / 0.28, 2));
                h += bowl + rim;
            }
            return h;
        }
    }
}
