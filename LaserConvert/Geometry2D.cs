using System;
using System.Collections.Generic;
using System.Linq;

namespace LaserConvert
{
    /// <summary>
    /// 2D computational geometry utilities for polygon ordering and perimeter reconstruction.
    /// All operations work in 2D space after projection from 3D.
    /// </summary>
    internal static class Geometry2D
    {
        /// <summary>
        /// Represents a 2D point with integer coordinates.
        /// </summary>
        public struct Point2D : IEquatable<Point2D>
        {
            public long X, Y;
            public Point2D(long x, long y) { X = x; Y = y; }
            public override string ToString() => $"({X},{Y})";
            public override bool Equals(object obj) => obj is Point2D p && Equals(p);
            public bool Equals(Point2D other) => X == other.X && Y == other.Y;
            public override int GetHashCode() => HashCode.Combine(X, Y);
            public static bool operator ==(Point2D a, Point2D b) => a.Equals(b);
            public static bool operator !=(Point2D a, Point2D b) => !a.Equals(b);
        }

        /// <summary>
        /// Gift Wrapping algorithm (Jarvis March) for finding polygon perimeter.
        /// Preserves all boundary vertices including non-convex features (tabs, cutouts).
        /// Unlike Graham Scan, does NOT simplify to convex hull.
        /// 
        /// Algorithm:
        /// 1. Start from leftmost point
        /// 2. Find next point by selecting one that makes smallest counter-clockwise turn
        /// 3. Repeat until back to start
        /// 4. This naturally includes concave vertices because we follow the actual boundary
        /// </summary>
        public static List<Point2D> GiftWrapPerimeter(List<Point2D> points)
        {
            if (points.Count < 3)
                return new List<Point2D>(points);

            // Remove duplicates
            var unique = points
                .Distinct()
                .ToList();

            if (unique.Count < 3)
                return unique;

            // Find leftmost point (and lowest if tie)
            int leftmost = 0;
            for (int i = 1; i < unique.Count; i++)
            {
                if (unique[i].X < unique[leftmost].X ||
                    (unique[i].X == unique[leftmost].X && unique[i].Y < unique[leftmost].Y))
                {
                    leftmost = i;
                }
            }

            var hull = new List<Point2D>();
            int current = leftmost;
            int iterations = 0;
            int maxIterations = unique.Count + 1;  // Prevent infinite loops

            do
            {
                hull.Add(unique[current]);

                // Find the point that makes the smallest counter-clockwise angle
                // Start by assuming next point is any other point
                int next = (current + 1) % unique.Count;

                for (int i = 0; i < unique.Count; i++)
                {
                    if (i == current)
                        continue;

                    // Calculate cross product to determine turn direction
                    // Positive = counter-clockwise (left turn)
                    // Zero = collinear
                    // Negative = clockwise (right turn)
                    var cross = CrossProduct(unique[current], unique[next], unique[i]);

                    if (cross < 0 || (cross == 0 && DistanceSquared(unique[current], unique[i]) > DistanceSquared(unique[current], unique[next])))
                    {
                        // unique[i] is more counter-clockwise than unique[next]
                        // OR they're collinear but unique[i] is farther (include all collinear points)
                        next = i;
                    }
                }

                current = next;
                iterations++;
            }
            while (current != leftmost && iterations < maxIterations);

            // Remove last point if it's the same as first (closing the loop)
            if (hull.Count > 1 && hull[hull.Count - 1] == hull[0])
            {
                hull.RemoveAt(hull.Count - 1);
            }

            Console.WriteLine($"[GEOMETRY2D] Gift Wrapping: {points.Count} input points -> {hull.Count} perimeter points ({iterations} iterations)");

            return hull;
        }

        /// <summary>
        /// Cross product of vectors (O->A) and (O->B).
        /// Positive = counter-clockwise turn from A to B around O
        /// Negative = clockwise turn
        /// Zero = collinear
        /// </summary>
        private static long CrossProduct(Point2D O, Point2D A, Point2D B)
        {
            return (A.X - O.X) * (B.Y - O.Y) - (A.Y - O.Y) * (B.X - O.X);
        }

        /// <summary>
        /// Squared distance between two points (avoids sqrt for comparisons).
        /// </summary>
        private static long DistanceSquared(Point2D A, Point2D B)
        {
            var dx = A.X - B.X;
            var dy = A.Y - B.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Convert double-precision (double X, double Y) to integer (long X, long Y).
        /// Normalizes by subtracting the minimum values so coordinates start at (0,0).
        /// </summary>
        public static List<Point2D> NormalizeAndRound(List<(double X, double Y)> points)
        {
            if (points.Count == 0)
                return new List<Point2D>();

            var minX = points.Min(p => p.X);
            var minY = points.Min(p => p.Y);

            return points
                .Select(p => new Point2D(
                    (long)Math.Round(p.X - minX),
                    (long)Math.Round(p.Y - minY)
                ))
                .ToList();
        }

        /// <summary>
        /// Build SVG path string from ordered 2D points.
        /// Points should already be in perimeter order (use GiftWrapPerimeter first).
        /// </summary>
        public static string BuildSvgPath(List<Point2D> points)
        {
            if (points.Count < 3)
                return string.Empty;

            var sb = new System.Text.StringBuilder();
            sb.Append($"M {points[0].X},{points[0].Y}");

            for (int i = 1; i < points.Count; i++)
            {
                sb.Append($" L {points[i].X},{points[i].Y}");
            }

            sb.Append(" Z");
            return sb.ToString();
        }
    }
}
