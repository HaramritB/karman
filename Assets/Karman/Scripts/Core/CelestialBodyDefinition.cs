using System;

namespace Karman.Core
{
    [Serializable]
    public sealed class AtmosphereDefinition
    {
        public double SeaLevelPressure;     // Pa
        public double SeaLevelDensity;      // kg/m^3
        public double ScaleHeight;          // m (exponential fallback model)
        public double Height;               // m, top of the simulated/rendered atmosphere
        public double RayleighScaleHeight;  // m, rendering
        public double MieScaleHeight;       // m, rendering
    }

    /// <summary>Static physical description of a body. Values from NASA planetary fact sheets and IAU WGCCRE 2015.</summary>
    [Serializable]
    public sealed class CelestialBodyDefinition
    {
        public string Id;
        public string DisplayName;
        public string ParentId;
        public double GravitationalParameter;   // m^3/s^2
        public double MeanRadius;               // m
        public double EquatorialRadius;         // m
        public double SiderealRotationPeriod;   // s (negative = retrograde)
        public double PoleRightAscension;       // rad, ICRF
        public double PoleDeclination;          // rad, ICRF
        public double PrimeMeridianAtJ2000;     // rad
        public double AxialTilt;                // rad, to orbit (informational)
        public bool TidallyLocked;
        public bool Playable;                   // full gameplay (terrain/collision) support
        public AtmosphereDefinition Atmosphere;
        public float[] DisplayColor = { 1, 1, 1 };

        public double Mass => GravitationalParameter / Constants.G;
        public double SurfaceGravity => GravitationalParameter / (MeanRadius * MeanRadius);
    }
}
