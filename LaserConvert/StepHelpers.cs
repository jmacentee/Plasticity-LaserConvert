using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// 3D geometric helper methods for rotations, projections, and transformations.
    /// </summary>
    internal static class StepHelpers
    {
        /// <summary>
        /// Apply a 3x3 rotation matrix to a 3D vector.
        /// </summary>
        public static GeometryTransform.Vec3 ApplyMatrix(GeometryTransform.Vec3 v, double[,] matrix)
        {
            return new GeometryTransform.Vec3(
                matrix[0, 0] * v.X + matrix[0, 1] * v.Y + matrix[0, 2] * v.Z,
                matrix[1, 0] * v.X + matrix[1, 1] * v.Y + matrix[1, 2] * v.Z,
                matrix[2, 0] * v.X + matrix[2, 1] * v.Y + matrix[2, 2] * v.Z
            );
        }

        /// <summary>
        /// Compute dot product of two 3D vectors.
        /// </summary>
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>
        /// Compute cross product of two 3D vectors.
        /// </summary>
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );

        /// <summary>
        /// Normalize a 3D vector to unit length.
        /// </summary>
        public static Vec3 Normalize(Vec3 v)
        {
            var len = v.Length;
            return len > 1e-12 ? (1.0 / len) * v : v;
        }

        /// <summary>
        /// Build a projection frame (coordinate system) from a set of 3D vertices.
        /// </summary>
        public static (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) BuildProjectionFrame(List<(double X, double Y, double Z)> vertices)
        {
            if (vertices.Count < 3)
                return (new Vec3(0, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));
            var origin = new Vec3(vertices.Average(v => v.X), vertices.Average(v => v.Y), vertices.Average(v => v.Z));
            var v0 = new Vec3(vertices[0].X, vertices[0].Y, vertices[0].Z) - origin;
            var v1 = new Vec3(0, 0, 0);
            for (int i = 1; i < vertices.Count; i++)
            {
                v1 = new Vec3(vertices[i].X, vertices[i].Y, vertices[i].Z) - origin;
                if (Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y + v1.Z * v1.Z) > 0.01) break;
            }
            var normal = Normalize(Cross(v0, v1));
            var u = Normalize(v0);
            var vvec = Normalize(Cross(normal, u));
            return (origin, normal, u, vvec);
        }

        /// <summary>
        /// Project a 3D point onto a 2D plane defined by a projection frame.
        /// </summary>
        public static Vec2 Project((Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, Vec3 point)
        {
            var rel = point - frame.Origin;
            return new Vec2(Dot(rel, frame.U), Dot(rel, frame.V));
        }

        /// <summary>
        /// Extract the edge loop from a face bound.
        /// </summary>
        public static StepEdgeLoop GetEdgeLoopFromBound(StepFaceBound bound)
        {
            var props = bound.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(StepEdgeLoop) || prop.PropertyType.Name.Contains("Loop"))
                {
                    return prop.GetValue(bound) as StepEdgeLoop;
                }
            }
            return null;
        }

        /// <summary>
        /// Project 3D points to 2D by selecting the two axes with largest ranges.
        /// </summary>
        public static List<(double X, double Y)> ProjectTo2D(List<(double X, double Y, double Z)> points3D)
        {
            if (points3D.Count == 0) return new List<(double, double)>();
            
            // Find ranges in each dimension
            var minX = points3D.Min(p => p.X);
            var maxX = points3D.Max(p => p.X);
            var minY = points3D.Min(p => p.Y);
            var maxY = points3D.Max(p => p.Y);
            var minZ = points3D.Min(p => p.Z);
            var maxZ = points3D.Max(p => p.Z);
            
            var rangeX = maxX - minX;
            var rangeY = maxY - minY;
            var rangeZ = maxZ - minZ;
            
            // Project by dropping the axis with smallest range
            var ranges = new[] { ("X", rangeX), ("Y", rangeY), ("Z", rangeZ) }
                .OrderByDescending(r => r.Item2)
                .ToArray();
            
            if (ranges[0].Item1 == "X" && ranges[1].Item1 == "Y")
                return points3D.Select(p => (p.X, p.Y)).ToList();
            else if (ranges[0].Item1 == "X" && ranges[1].Item1 == "Z")
                return points3D.Select(p => (p.X, p.Z)).ToList();
            else if (ranges[0].Item1 == "Y" && ranges[1].Item1 == "Z")
                return points3D.Select(p => (p.Y, p.Z)).ToList();
            else
                return points3D.Select(p => (p.X, p.Y)).ToList();
        }

        /// <summary>
        /// Normalize and round 2D points, shifting to origin.
        /// </summary>
        public static List<(long X, long Y)> NormalizeAndRound(List<(double X, double Y)> points2D)
        {
            if (points2D.Count == 0) return new List<(long, long)>();
            
            var minX = points2D.Min(p => p.X);
            var minY = points2D.Min(p => p.Y);
            
            return points2D.Select(p => (
                (long)Math.Round(p.X - minX),
                (long)Math.Round(p.Y - minY)
            )).ToList();
        }

        /// <summary>
        /// Normalize and round 2D points relative to outer bounds.
        /// </summary>
        public static List<(long X, long Y)> NormalizeAndRoundRelative(List<(double X, double Y)> points2D, double outerMinX, double outerMinY)
        {
            if (points2D.Count == 0) return new List<(long, long)>();
            
            return points2D.Select(p => (
                (long)Math.Round(p.X - outerMinX),
                (long)Math.Round(p.Y - outerMinY)
            )).ToList();
        }

        /// <summary>
        /// Remove consecutive duplicate points while preserving all unique vertices.
        /// </summary>
        public static List<(long X, long Y)> RemoveConsecutiveDuplicates(List<(long X, long Y)> points)
        {
            if (points.Count <= 1) return points;
            
            var result = new List<(long X, long Y)> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i] != points[i - 1])
                {
                    result.Add(points[i]);
                }
            }
            
            // Also check if last point equals first (closing loop)
            if (result.Count > 2 && result.Last() == result[0])
            {
                result.RemoveAt(result.Count - 1);
            }
            
            return result;
        }

        /// <summary>
        /// Order vertices by creating lower and upper monotone chains (without removing points).
        /// This preserves all vertices while creating a valid perimeter traversal.
        /// </summary>
        private static List<(long, long)> OrderPolygonByEdgeWalking(List<(long, long)> vertices)
        {
            if (vertices.Count <= 3)
                return null;

            var result = new List<(long, long)>();
            var used = new HashSet<int>();

            // Find leftmost-lowest point
            int current = 0;
            for (int i = 1; i < vertices.Count; i++)
            {
                if (vertices[i].Item1 < vertices[current].Item1 ||
                    (vertices[i].Item1 == vertices[current].Item1 && 
                     vertices[i].Item2 < vertices[current].Item2))
                {
                    current = i;
                }
            }

            // Build perimeter by always moving to nearest unvisited neighbor
            int start = current;
            while (used.Count < vertices.Count)
            {
                result.Add(vertices[current]);
                used.Add(current);

                if (used.Count >= vertices.Count)
                    break;

                // Find nearest unvisited neighbor
                int next = -1;
                long minDist = long.MaxValue;

                for (int i = 0; i < vertices.Count; i++)
                {
                    if (used.Contains(i))
                        continue;

                    long dx = vertices[i].Item1 - vertices[current].Item1;
                    long dy = vertices[i].Item2 - vertices[current].Item2;
                    long distSq = dx * dx + dy * dy;

                    if (distSq < minDist)
                    {
                        minDist = distSq;
                        next = i;
                    }
                }

                if (next == -1)
                    break;

                current = next;
            }

            // Return if we got all vertices in a cycle
            return used.Count == vertices.Count ? result : null;
        }

        /// <summary>
        /// Order vertices by polar angle from centroid (fallback method).
        /// </summary>
        private static List<(long, long)> OrderPolygonByPolarAngle(List<(long, long)> vertices)
        {
            if (vertices.Count <= 3)
                return new List<(long, long)>(vertices);

            // Calculate centroid
            long sumX = 0, sumY = 0;
            foreach (var v in vertices)
            {
                sumX += v.Item1;
                sumY += v.Item2;
            }
            double cx = (double)sumX / vertices.Count;
            double cy = (double)sumY / vertices.Count;

            // Sort by angle from centroid
            var sorted = vertices
                .OrderBy(v => Math.Atan2(v.Item2 - cy, v.Item1 - cx))
                .ToList();

            return sorted;
        }

        /// <summary>
        /// Order polygon perimeter vertices using edge-walking algorithm.
        /// Preserves all vertices by following the actual perimeter path.
        /// </summary>
        public static List<(long, long)> OrderPolygonPerimeter(List<(long, long)> vertices)
        {
            if (vertices.Count <= 3)
                return new List<(long, long)>(vertices);

            // Try edge-walking first - it works when vertices form a proper closed loop
            var edgeWalked = OrderPolygonByEdgeWalking(vertices);
            if (edgeWalked != null)
            {
                Console.WriteLine($"[ORDER] Used edge-walking perimeter reconstruction");
                return edgeWalked;
            }

            // Fallback to polar angle if edge-walking can't find a complete path
            Console.WriteLine($"[ORDER] Used fallback polar angle ordering");
            return OrderPolygonByPolarAngle(vertices);
        }

        /// <summary>
        /// Compute dimensions of a point set.
        /// </summary>
        public static Dimensions ComputeDimensions(List<(double X, double Y, double Z)> points)
        {
            if (points.Count == 0)
                return new Dimensions(0, 0, 0);

            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            var minZ = points.Min(p => p.Z);
            var maxZ = points.Max(p => p.Z);
            return new Dimensions(maxX - minX, maxY - minY, maxZ - minZ);
        }
    }
}
