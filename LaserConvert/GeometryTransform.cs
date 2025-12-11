using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserConvert
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

        /// <summary>
        /// Find the shortest distance between any two vertices on different faces.
        /// Returns the pair of vertices defining the thin dimension.
        /// </summary>
        public static (Vec3 Point1, Vec3 Point2, double Distance) FindThinDimension(
            List<(double X, double Y, double Z)> allVertices)
        {
            var verts = allVertices.Select(v => new Vec3(v.X, v.Y, v.Z)).Distinct().ToList();
            
            double minDist = double.MaxValue;
            Vec3 p1 = default, p2 = default;
            
            for (int i = 0; i < verts.Count; i++)
            {
                for (int j = i + 1; j < verts.Count; j++)
                {
                    var dist = (verts[j] - verts[i]).Length;
                    if (dist > 0.1 && dist < minDist)  // Ignore coincident points
                    {
                        minDist = dist;
                        p1 = verts[i];
                        p2 = verts[j];
                    }
                }
            }
            
            return (p1, p2, minDist);
        }

        /// <summary>
        /// Create a rotation matrix that aligns the given vector with the Z axis.
        /// </summary>
        private static double[,] CreateRotationMatrix(Vec3 fromVector)
        {
            var from = fromVector.Normalize();
            var to = new Vec3(0, 0, 1);  // Target: Z axis
            
            // If already aligned, return identity
            if (Math.Abs(Vec3.Dot(from, to) - 1.0) < 1e-6)
            {
                return new[,]
                {
                    { 1.0, 0.0, 0.0 },
                    { 0.0, 1.0, 0.0 },
                    { 0.0, 0.0, 1.0 }
                };
            }
            
            // If opposite, need 180° rotation
            if (Math.Abs(Vec3.Dot(from, to) + 1.0) < 1e-6)
            {
                // Find perpendicular axis
                var perp = Math.Abs(from.X) < 0.9 ? new Vec3(1, 0, 0) : new Vec3(0, 1, 0);
                var axis = Vec3.Cross(from, perp).Normalize();
                return RotationMatrixAroundAxis(axis, Math.PI);
            }
            
            // General case: rotate around cross product axis
            var rotAxis = Vec3.Cross(from, to).Normalize();
            var angle = Math.Acos(Math.Max(-1, Math.Min(1, Vec3.Dot(from, to))));
            
            return RotationMatrixAroundAxis(rotAxis, angle);
        }

        /// <summary>
        /// Create rotation matrix around arbitrary axis using Rodrigues' formula.
        /// </summary>
        private static double[,] RotationMatrixAroundAxis(Vec3 axis, double angle)
        {
            axis = axis.Normalize();
            var c = Math.Cos(angle);
            var s = Math.Sin(angle);
            var t = 1.0 - c;
            
            var ux = axis.X;
            var uy = axis.Y;
            var uz = axis.Z;
            
            return new[,]
            {
                {
                    t * ux * ux + c,
                    t * ux * uy - s * uz,
                    t * ux * uz + s * uy
                },
                {
                    t * ux * uy + s * uz,
                    t * uy * uy + c,
                    t * uy * uz - s * ux
                },
                {
                    t * ux * uz - s * uy,
                    t * uy * uz + s * ux,
                    t * uz * uz + c
                }
            };
        }

        /// <summary>
        /// Apply a 3x3 rotation matrix to a point.
        /// </summary>
        private static Vec3 RotatePoint(Vec3 p, double[,] matrix)
        {
            return new Vec3(
                matrix[0, 0] * p.X + matrix[0, 1] * p.Y + matrix[0, 2] * p.Z,
                matrix[1, 0] * p.X + matrix[1, 1] * p.Y + matrix[1, 2] * p.Z,
                matrix[2, 0] * p.X + matrix[2, 1] * p.Y + matrix[2, 2] * p.Z
            );
        }

        /// <summary>
        /// Rotate all vertices so the thin dimension aligns with Z axis.
        /// Returns the rotated vertices and the rotation matrix.
        /// </summary>
        public static (List<Vec3> RotatedVertices, double[,] RotationMatrix) RotateToAlignThinDimension(
            List<(double X, double Y, double Z)> allVertices)
        {
            // Find the thin dimension
            var (p1, p2, dist) = FindThinDimension(allVertices);
            Console.WriteLine($"[TRANSFORM] Thin dimension: {p1.X:F1},{p1.Y:F1},{p1.Z:F1} -> {p2.X:F1},{p2.Y:F1},{p2.Z:F1} ({dist:F1}mm)");
            
            // Create rotation matrix to align this vector with Z axis
            var thinVector = (p2 - p1);
            var rotMatrix = CreateRotationMatrix(thinVector);
            
            // Apply rotation to all vertices
            var rotatedVerts = allVertices
                .Select(v => RotatePoint(new Vec3(v.X, v.Y, v.Z), rotMatrix))
                .ToList();
            
            return (rotatedVerts, rotMatrix);
        }

        /// <summary>
        /// Find which face vertices have the maximum Z coordinate after rotation.
        /// </summary>
        public static List<Vec3> FindTopFace(List<Vec3> rotatedVertices, List<int> faceVertexIndices)
        {
            if (faceVertexIndices.Count == 0)
                return new List<Vec3>();
            
            var faceVerts = faceVertexIndices
                .Select(i => i < rotatedVertices.Count ? rotatedVertices[i] : default)
                .Where(v => v.Z > -1e10)  // Filter out invalid points
                .ToList();
            
            return faceVerts;
        }

        /// <summary>
        /// Apply TWO rotations:
        /// 1. Align thin dimension with Z axis
        /// 2. Align one edge of the top face with X axis (normalize to axis-aligned)
        /// Returns the doubly-rotated vertices ready for 2D SVG projection.
        /// </summary>
        public static List<Vec3> RotateAndNormalize(List<(double X, double Y, double Z)> vertices)
        {
            // Step 1: Rotate to align thin dimension with Z
            var (rotatedVertices, rotMatrix1) = RotateToAlignThinDimension(vertices);
            
            // Step 2: Find the top face and align one edge with X axis
            var maxZ = rotatedVertices.Max(v => v.Z);
            var topFaceVerts = rotatedVertices
                .Where(v => Math.Abs(v.Z - maxZ) < 1.0)
                .OrderBy(v => v.X).ThenBy(v => v.Y)
                .ToList();
            
            if (topFaceVerts.Count < 2)
            {
                return rotatedVertices;  // Can't normalize, return as-is
            }
            
            // Get the first edge of the top face
            var v0 = topFaceVerts[0];
            var v1 = topFaceVerts[1];
            
            // Vector along this edge
            var edgeVec = new Vec3(v1.X - v0.X, v1.Y - v0.Y, 0);  // Keep Z=0 (already on top face)
            
            if (edgeVec.Length < 0.01)
            {
                return rotatedVertices;  // Degenerate edge
            }
            
            // Normalize edge vector
            edgeVec = edgeVec.Normalize();
            
            // We want to rotate this edge to align with X axis (1, 0, 0)
            // This is a rotation in the XY plane only
            var angle = Math.Atan2(edgeVec.Y, edgeVec.X);
            
            // Create rotation matrix for Z-axis rotation (rotation around Z by -angle)
            var cos = Math.Cos(-angle);
            var sin = Math.Sin(-angle);
            
            var rotMatrix2 = new[,]
            {
                { cos,  -sin,  0 },
                { sin,   cos,  0 },
                { 0,     0,    1 }
            };
            
            Console.WriteLine($"[TRANSFORM] Normalizing edge to X-axis: angle={angle * 180 / Math.PI:F1}°");
            
            // Apply second rotation to all rotated vertices
            var normalizedVertices = rotatedVertices
                .Select(v => RotatePoint(v, rotMatrix2))
                .ToList();
            
            return normalizedVertices;
        }
    }
}
