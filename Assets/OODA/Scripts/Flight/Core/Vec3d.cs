using System;

namespace Ooda.Flight.Core
{
    /// <summary>
    /// A double-precision vector for the flight core. The core works in the OODA tool's frame:
    /// feet, x east, y north, z up (right-handed). Unity conversion lives in UnityFrame.
    /// </summary>
    [Serializable]
    public struct Vec3d
    {
        public double x, y, z;

        public Vec3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }

        public static readonly Vec3d Zero = new Vec3d(0, 0, 0);
        public static readonly Vec3d Up = new Vec3d(0, 0, 1);

        public static Vec3d operator +(Vec3d a, Vec3d b) => new Vec3d(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3d operator -(Vec3d a, Vec3d b) => new Vec3d(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3d operator -(Vec3d a) => new Vec3d(-a.x, -a.y, -a.z);
        public static Vec3d operator *(Vec3d a, double k) => new Vec3d(a.x * k, a.y * k, a.z * k);
        public static Vec3d operator *(double k, Vec3d a) => new Vec3d(a.x * k, a.y * k, a.z * k);
        public static Vec3d operator /(Vec3d a, double k) => new Vec3d(a.x / k, a.y / k, a.z / k);

        public static double Dot(Vec3d a, Vec3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vec3d Cross(Vec3d a, Vec3d b) =>
            new Vec3d(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public Vec3d Normalized { get { double m = Length; return m > 1e-12 ? this / m : Zero; } }

        /// <summary>This vector with its part along unit vector n removed, renormalised (the "carry the frame across" step).</summary>
        public Vec3d SquareTo(Vec3d n)
        {
            Vec3d p = this - n * Dot(this, n);
            return p.Normalized;
        }

        public override string ToString() => $"({x:F3}, {y:F3}, {z:F3})";
    }
}
