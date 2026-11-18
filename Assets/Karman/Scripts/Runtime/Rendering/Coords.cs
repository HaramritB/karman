using System;
using Karman.Core;
using UnityEngine;

namespace Karman.Rendering
{
    /// <summary>
    /// Maps the right-handed simulation frame (X = vernal equinox, Z = Earth's north pole) to Unity's
    /// left-handed Y-up frame by swapping Y and Z. Unity world axes stay inertially fixed; only the
    /// origin moves (see <see cref="RenderFrame"/>).
    /// </summary>
    public static class Coords
    {
        public static Vector3 ToUnity(Vector3d v) => new Vector3((float)v.x, (float)v.z, (float)v.y);
        public static Vector3d ToSim(Vector3 v) => new Vector3d(v.x, v.z, v.y);

        /// <summary>Converts a body-fixed→inertial rotation into a Unity rotation for an object whose local
        /// axes use the same Y/Z swap (local +Y = body north pole, local +X = prime meridian).</summary>
        public static Quaternion ToUnity(Matrix3d m)
        {
            Vector3 objZ = ToUnity(m.Column(1));
            Vector3 objY = ToUnity(m.Column(2));
            return Quaternion.LookRotation(objZ, objY);
        }
    }

    /// <summary>
    /// Camera-relative rendering. <see cref="Origin"/> is the heliocentric simulation position that maps to Unity's
    /// world origin each frame (the camera sits there). Positions beyond <see cref="ScaledSpaceStart"/> are pulled
    /// in logarithmically and scaled uniformly, which preserves angular size and depth order while keeping every
    /// object inside a float-friendly depth range.
    /// </summary>
    public static class RenderFrame
    {
        public static Vector3d Origin;
        public const double ScaledSpaceStart = 5.0e7;   // m
        public const float FarClip = 2.0e8f;

        public static double Compress(double d) => d <= ScaledSpaceStart ? d : ScaledSpaceStart * (1.0 + 0.1 * Math.Log(d / ScaledSpaceStart));

        public static Vector3 Project(Vector3d heliocentric, out float scale)
        {
            Vector3d rel = heliocentric - Origin;
            double d = rel.magnitude;
            if (d < 1e-6) { scale = 1; return Vector3.zero; }
            double dc = Compress(d);
            scale = (float)(dc / d);
            return Coords.ToUnity(rel * (dc / d));
        }

        /// <summary>Unscaled camera-relative position, valid for nearby objects (vessels).</summary>
        public static Vector3 Local(Vector3d heliocentric) => Coords.ToUnity(heliocentric - Origin);
    }
}
