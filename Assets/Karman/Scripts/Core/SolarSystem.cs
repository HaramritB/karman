using System;
using System.Collections.Generic;

namespace Karman.Core
{
    /// <summary>Runtime celestial body: definition + ephemeris-driven state at the current simulation time.</summary>
    public sealed class CelestialBody
    {
        public readonly CelestialBodyDefinition Definition;
        public CelestialBody Parent { get; internal set; }
        public readonly List<CelestialBody> Children = new List<CelestialBody>();

        /// <summary>Heliocentric (barycentre ≈ Sun) state in the inertial frame at <see cref="SolarSystem.Time"/>.</summary>
        public StateVector HeliocentricState { get; internal set; }
        /// <summary>Body-fixed → inertial rotation at the current time.</summary>
        public Matrix3d Orientation { get; internal set; } = Matrix3d.identity;

        public string Id => Definition.Id;
        public double Mu => Definition.GravitationalParameter;
        public double Radius => Definition.MeanRadius;
        public double SphereOfInfluence { get; internal set; } = double.PositiveInfinity;

        public CelestialBody(CelestialBodyDefinition def) { Definition = def; }

        public Vector3d Position => HeliocentricState.position;
        public Vector3d Velocity => HeliocentricState.velocity;

        /// <summary>State relative to the parent body (heliocentric for planets).</summary>
        public StateVector StateRelativeToParent => Parent == null ? default : HeliocentricState - Parent.HeliocentricState;

        public override string ToString() => Definition.DisplayName;
    }

    /// <summary>
    /// Owns all bodies and updates their states from analytical ephemerides. Bodies are on rails;
    /// vessels feel their gravity via patched conics (dominant body inside its sphere of influence).
    /// </summary>
    public sealed class SolarSystem
    {
        public readonly List<CelestialBody> Bodies = new List<CelestialBody>();
        readonly Dictionary<string, CelestialBody> byId = new Dictionary<string, CelestialBody>();
        public CelestialBody Sun { get; }
        public double Time { get; private set; }

        public SolarSystem(IEnumerable<CelestialBodyDefinition> defs, double startTime)
        {
            foreach (var d in defs) { var b = new CelestialBody(d); Bodies.Add(b); byId[d.Id] = b; }
            foreach (var b in Bodies)
            {
                if (b.Definition.ParentId != null && byId.TryGetValue(b.Definition.ParentId, out var p)) { b.Parent = p; p.Children.Add(b); }
            }
            Sun = byId[SolarSystemData.Sun];
            SetTime(startTime);
            foreach (var b in Bodies)
            {
                if (b.Parent == null) continue;
                double a = b.Id == SolarSystemData.Moon ? 384_400_000.0
                    : SolarSystemData.MeanElements.TryGetValue(b.Id == SolarSystemData.Earth ? "emb" : b.Id, out var el) ? el.a * Constants.AU
                    : (b.Position - b.Parent.Position).magnitude;
                b.SphereOfInfluence = a * Math.Pow(b.Mu / b.Parent.Mu, 0.4);
            }
        }

        public static SolarSystem CreateDefault(double startTime) => new SolarSystem(SolarSystemData.CreateDefinitions(), startTime);

        public CelestialBody Get(string id) => byId.TryGetValue(id, out var b) ? b : null;

        public void SetTime(double t)
        {
            Time = t;
            var earthState = Ephemeris.EarthHeliocentricState(t);
            foreach (var b in Bodies)
            {
                switch (b.Id)
                {
                    case SolarSystemData.Sun: b.HeliocentricState = default; break;
                    case SolarSystemData.Earth: b.HeliocentricState = earthState; break;
                    case SolarSystemData.Moon: b.HeliocentricState = earthState + Ephemeris.MoonGeocentricState(t); break;
                    default: b.HeliocentricState = Ephemeris.PlanetHeliocentricState(b.Id, t); break;
                }
            }
            foreach (var b in Bodies) b.Orientation = ComputeOrientation(b, t);
        }

        /// <summary>IAU-style body-fixed frame: Rz(α+90°)·Rx(90°−δ)·Rz(W). Earth uses GMST; the Moon faces Earth.</summary>
        Matrix3d ComputeOrientation(CelestialBody b, double t)
        {
            var d = b.Definition;
            if (b.Id == SolarSystemData.Earth) return Matrix3d.RotationZ(TimeSystem.GreenwichMeanSiderealTime(t));
            Matrix3d pole = Matrix3d.RotationZ(d.PoleRightAscension + Math.PI / 2) * Matrix3d.RotationX(Math.PI / 2 - d.PoleDeclination);
            double W;
            if (d.TidallyLocked && b.Parent != null)
            {
                // Prime meridian points at the parent (mean libration ignored).
                Vector3d toParentBody = pole.Transposed * (b.Parent.Position - b.Position);
                W = Math.Atan2(toParentBody.y, toParentBody.x);
            }
            else
            {
                W = d.PrimeMeridianAtJ2000 + 2 * Math.PI * (t / d.SiderealRotationPeriod);
                W %= 2 * Math.PI;
            }
            return pole * Matrix3d.RotationZ(W);
        }

        /// <summary>The body whose sphere of influence contains a heliocentric position (deepest in the hierarchy).</summary>
        public CelestialBody DominantBody(Vector3d heliocentricPosition)
        {
            CelestialBody best = Sun;
            bool descended = true;
            while (descended)
            {
                descended = false;
                foreach (var c in best.Children)
                {
                    if ((heliocentricPosition - c.Position).sqrMagnitude < c.SphereOfInfluence * c.SphereOfInfluence)
                    { best = c; descended = true; break; }
                }
            }
            return best;
        }
    }
}
