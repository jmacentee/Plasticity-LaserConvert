using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserConvert
{
    /// <summary>
    /// Dimension tracking for solids with thin dimension detection.
    /// </summary>
    internal record Dimensions(double Width, double Height, double Depth)
    {
        public bool HasThinDimension(double minThickness, double maxThickness)
        {
            var sorted = new[] { Width, Height, Depth }.OrderBy(d => d).ToList();
            var smallestDim = sorted[0];
            var hasSmallDim = smallestDim >= minThickness && smallestDim <= maxThickness;
            var thinDims = sorted.Where(d => d >= minThickness && d <= maxThickness).Count();
            if (thinDims > 0)
            {
                return true;
            }
            return hasSmallDim;
        }
        public override string ToString() => $"[{Width:F1}, {Height:F1}, {Depth:F1}]";
    }

    /// <summary>
    /// 2D vector for projections.
    /// </summary>
    internal struct Vec2
    {
        public readonly double X, Y;
        public Vec2(double x, double y) { X = x; Y = y; }
    }

    /// <summary>
    /// 3D vector for transformations.
    /// </summary>
    internal struct Vec3
    {
        public readonly double X, Y, Z;
        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(s * a.X, s * a.Y, s * a.Z);
    }

    /// <summary>
    /// Equality comparer for Vec3 with tolerance.
    /// </summary>
    internal class Vec3Comparer : IEqualityComparer<GeometryTransform.Vec3>
    {
        public bool Equals(GeometryTransform.Vec3 a, GeometryTransform.Vec3 b)
        {
            return Math.Abs(a.X - b.X) < 0.01 && Math.Abs(a.Y - b.Y) < 0.01 && Math.Abs(a.Z - b.Z) < 0.01;
        }

        public int GetHashCode(GeometryTransform.Vec3 v)
        {
            return (Math.Round(v.X, 2), Math.Round(v.Y, 2), Math.Round(v.Z, 2)).GetHashCode();
        }
    }
}
