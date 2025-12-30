using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserConvertProcess
{
    /// <summary>
    /// 3D geometry transformation utilities.
    /// Handles rotation and projection of solids to align thin dimension with Z axis.
    /// </summary>
    internal static class GeometryTransform
    {
        public struct Vec3
        {
            public double X, Y, Z;
            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3 operator *(double s, Vec3 a) => new Vec3(s * a.X, s * a.Y, s * a.Z);
            public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );
            public Vec3 Normalize()
            {
                var len = Length;
                return len > 1e-12 ? (1.0 / len) * this : this;
            }
        }

        
       

       
    }
}
