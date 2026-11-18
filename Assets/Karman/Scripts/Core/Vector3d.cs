using System;

namespace Karman.Core
{
    /// <summary>Double-precision 3-vector used for all astronomical state. Right-handed, SI units.</summary>
    [Serializable]
    public struct Vector3d : IEquatable<Vector3d>
    {
        public double x, y, z;

        public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vector3d zero = new Vector3d(0, 0, 0);
        public static readonly Vector3d unitX = new Vector3d(1, 0, 0);
        public static readonly Vector3d unitY = new Vector3d(0, 1, 0);
        public static readonly Vector3d unitZ = new Vector3d(0, 0, 1);

        public double sqrMagnitude => x * x + y * y + z * z;
        public double magnitude => Math.Sqrt(x * x + y * y + z * z);
        public Vector3d normalized { get { double m = magnitude; return m > 0 ? this / m : zero; } }

        public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3d operator -(Vector3d a) => new Vector3d(-a.x, -a.y, -a.z);
        public static Vector3d operator *(Vector3d a, double s) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator *(double s, Vector3d a) => new Vector3d(a.x * s, a.y * s, a.z * s);
        public static Vector3d operator /(Vector3d a, double s) => new Vector3d(a.x / s, a.y / s, a.z / s);

        public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3d Cross(Vector3d a, Vector3d b) =>
            new Vector3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static double Distance(Vector3d a, Vector3d b) => (a - b).magnitude;
        public static double Angle(Vector3d a, Vector3d b)
        {
            double d = Dot(a.normalized, b.normalized);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, d)));
        }
        public static Vector3d Lerp(Vector3d a, Vector3d b, double t) => a + (b - a) * t;

        public bool Equals(Vector3d o) => x == o.x && y == o.y && z == o.z;
        public override bool Equals(object obj) => obj is Vector3d o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:G6}, {y:G6}, {z:G6})";
    }

    /// <summary>Row-major double 3x3 matrix for frame rotations.</summary>
    [Serializable]
    public struct Matrix3d
    {
        public double m00, m01, m02, m10, m11, m12, m20, m21, m22;

        public static readonly Matrix3d identity = new Matrix3d { m00 = 1, m11 = 1, m22 = 1 };

        public static Matrix3d RotationX(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Matrix3d { m00 = 1, m11 = c, m12 = -s, m21 = s, m22 = c };
        }
        public static Matrix3d RotationY(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Matrix3d { m00 = c, m02 = s, m11 = 1, m20 = -s, m22 = c };
        }
        public static Matrix3d RotationZ(double a)
        {
            double c = Math.Cos(a), s = Math.Sin(a);
            return new Matrix3d { m00 = c, m01 = -s, m10 = s, m11 = c, m22 = 1 };
        }

        public static Vector3d operator *(Matrix3d m, Vector3d v) => new Vector3d(
            m.m00 * v.x + m.m01 * v.y + m.m02 * v.z,
            m.m10 * v.x + m.m11 * v.y + m.m12 * v.z,
            m.m20 * v.x + m.m21 * v.y + m.m22 * v.z);

        public static Matrix3d operator *(Matrix3d a, Matrix3d b) => new Matrix3d
        {
            m00 = a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20,
            m01 = a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21,
            m02 = a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22,
            m10 = a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20,
            m11 = a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21,
            m12 = a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22,
            m20 = a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20,
            m21 = a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21,
            m22 = a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22,
        };

        public Matrix3d Transposed => new Matrix3d
        {
            m00 = m00, m01 = m10, m02 = m20,
            m10 = m01, m11 = m11, m12 = m21,
            m20 = m02, m21 = m12, m22 = m22,
        };

        public Vector3d Column(int i) => i == 0 ? new Vector3d(m00, m10, m20) : i == 1 ? new Vector3d(m01, m11, m21) : new Vector3d(m02, m12, m22);
    }
}
