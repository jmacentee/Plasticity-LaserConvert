using System;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// Unused helper methods preserved for future reference or potential refactoring.
    /// These methods were part of earlier implementation attempts and may be useful
    /// for alternative approaches or advanced features.
    /// </summary>
    internal static class StepUnusedHelpers
    {
        /// <summary>
        /// Order vertices around a perimeter using Graham scan (convex hull algorithm).
        /// Works reliably for both convex and non-convex polygons when vertices are on the boundary.
        /// NOT USED: Current implementation uses extraction order which preserves non-convex features.
        /// </summary>
        public static List<GeometryTransform.Vec3> OrderPerimeterVertices(List<GeometryTransform.Vec3> vertices)
        {
            if (vertices.Count < 3)
                return vertices;
            
            // Remove duplicates
            var uniqueVerts = vertices
                .GroupBy(v => ((long)Math.Round(v.X * 100), (long)Math.Round(v.Y * 100)))
                .Select(g => g.First())
                .ToList();
            
            if (uniqueVerts.Count < 3)
                return uniqueVerts;
            
            // Find the point with lowest Y (top-left in SVG coords where Y increases downward)
            int minIdx = 0;
            for (int i = 1; i < uniqueVerts.Count; i++)
            {
                if (uniqueVerts[i].Y < uniqueVerts[minIdx].Y ||
                    (Math.Abs(uniqueVerts[i].Y - uniqueVerts[minIdx].Y) < 0.01 && 
                     uniqueVerts[i].X < uniqueVerts[minIdx].X))
                {
                    minIdx = i;
                }
            }
            
            var start = uniqueVerts[minIdx];
            
            // Sort all other points by polar angle with respect to start point
            var others = uniqueVerts.Where((v, i) => i != minIdx).ToList();
            others.Sort((a, b) => {
                var dax = a.X - start.X;
                var day = a.Y - start.Y;
                var dbx = b.X - start.X;
                var dby = b.Y - start.Y;
                
                // Cross product (dax * dby - day * dbx)
                var cross = dax * dby - day * dbx;
                
                if (Math.Abs(cross) > 0.01)
                    return cross > 0 ? -1 : 1;  // -1 means a comes first (counter-clockwise)
                
                // If collinear, sort by distance
                var distA = dax * dax + day * day;
                var distB = dbx * dbx + dby * dby;
                return distA.CompareTo(distB);
            });
            
            // Build result with start point first
            var result = new List<GeometryTransform.Vec3> { start };
            result.AddRange(others);
            
            return result;
        }
        
        /// <summary>
        /// Compute the angle for clockwise ordering (right=0, down=90, left=180, up=270 degrees).
        /// NOT USED: Current implementation uses extraction order.
        /// </summary>
        public static double ComputeAngleForClockwise(GeometryTransform.Vec3 from, GeometryTransform.Vec3 to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            
            // atan2 in SVG coords: 0° = right (+X), 90° = down (+Y), 180° = left (-Y), 270° = up (-X)
            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            
            // Normalize to [0, 360)
            if (angle < 0)
                angle += 360;
            
            return angle;
        }

        /// <summary>
        /// Check if a normalized vertex is close to an original 3D vertex.
        /// NOT USED: Preserved for potential debugging or validation.
        /// </summary>
        public static bool IsCloseToOriginal(GeometryTransform.Vec3 normVert, (double X, double Y, double Z) orig3D, 
            double[,] rotMatrix1, double[,] rotMatrix2)
        {
            // Apply rotations to the original 3D vertex
            var orig = new GeometryTransform.Vec3(orig3D.X, orig3D.Y, orig3D.Z);
            var rot1 = StepHelpers.ApplyMatrix(orig, rotMatrix1);
            var rot2 = StepHelpers.ApplyMatrix(rot1, rotMatrix2);
            
            // Check if close (within rounding tolerance)
            return Math.Abs(normVert.X - rot2.X) < 0.5 &&
                   Math.Abs(normVert.Y - rot2.Y) < 0.5 &&
                   Math.Abs(normVert.Z - rot2.Z) < 0.5;
        }

        /// <summary>
        /// Build an orthogonal loop in 2D by ordering points around edges.
        /// NOT USED: Current implementation uses extraction order.
        /// </summary>
        public static string BuildOrthogonalLoop2D(List<(long X, long Y)> pts)
        {
            if (pts == null || pts.Count == 0) return string.Empty;
            var xs = pts.Select(p => p.X).ToList();
            var ys = pts.Select(p => p.Y).ToList();
            long minX = xs.Min();
            long maxX = xs.Max();
            long minY = ys.Min();
            long maxY = ys.Max();
            int tol = 1;
            var bottom = pts.Where(p => Math.Abs(p.Y - minY) <= tol).OrderBy(p => p.X).ToList();
            var right = pts.Where(p => Math.Abs(p.X - maxX) <= tol).OrderBy(p => p.Y).ToList();
            var top = pts.Where(p => Math.Abs(p.Y - maxY) <= tol).OrderByDescending(p => p.X).ToList();
            var left = pts.Where(p => Math.Abs(p.X - minX) <= tol).OrderByDescending(p => p.Y).ToList();
            var walk = new List<(long X, long Y)>();
            void AppendUnique(IEnumerable<(long X,long Y)> seq)
            {
                foreach (var p in seq)
                {
                    if (walk.Count == 0 || walk.Last().X != p.X || walk.Last().Y != p.Y)
                        walk.Add(p);
                }
            }
            AppendUnique(bottom);
            AppendUnique(right);
            AppendUnique(top);
            AppendUnique(left);
            if (walk.Count > 0 && (walk.First().X != walk.Last().X || walk.First().Y != walk.Last().Y))
                walk.Add(walk.First());
            return SvgPathBuilder.BuildPerimeterPath(walk);
        }

        /// <summary>
        /// Sort perimeter vertices using orthogonal nearest-neighbor.
        /// NOT USED: Current implementation uses extraction order.
        /// </summary>
        public static List<(long X, long Y)> SortPerimeterVertices2D(List<(long X, long Y)> pts)
        {
            if (pts == null || pts.Count < 3)
                return new List<(long X, long Y)> ();
            
            // For orthogonal shapes, use pure greedy nearest-neighbor on orthogonal edges
            // Start from bottom-left corner
            
            int startIdx = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].Y < pts[startIdx].Y || 
                    (pts[i].Y == pts[startIdx].Y && pts[i].X < pts[startIdx].X))
                {
                    startIdx = i;
                }
            }

            var result = new List<(long X, long Y)>();
            var visited = new HashSet<int> { startIdx };
            int current = startIdx;
            result.Add(pts[current]);
            bool firstStep = true;

            // Greedy nearest-neighbor with right-bias on first step
            while (visited.Count < pts.Count)
            {
                var (curX, curY) = pts[current];
                int next = -1;
                long minDist = long.MaxValue;
                
                for (int i = 0; i < pts.Count; i++)
                {
                    if (visited.Contains(i))
                        continue;
                    
                    var (vx, vy) = pts[i];
                    
                    // Must share X or Y coordinate (orthogonal)
                    if (vx == curX || vy == curY)
                    {
                        long dist = Math.Abs(vx - curX) + Math.Abs(vy - curY);
                        
                        // On first step, strongly prefer moving right
                        if (firstStep && vy == curY && vx > curX)
                            dist -= 10000;  // Big bonus for first step going right
                        
                        if (dist < minDist)
                        {
                            minDist = dist;
                            next = i;
                        }
                    }
                }
                
                if (next == -1)
                    break;
                
                current = next;
                visited.Add(current);
                result.Add(pts[current]);
                firstStep = false;
            }

            return result;
        }

        /// <summary>
        /// Gift Wrapping algorithm (Jarvis March) - preserves all boundary vertices including non-convex features.
        /// Unlike Graham Scan, it doesn't simplify to convex hull.
        /// NOT USED: Current implementation uses extraction order which already preserves boundary.
        /// </summary>
        public static List<(long X, long Y)> GiftWrapPerimeter(List<(long X, long Y)> points)
        {
            if (points.Count < 3) return points;
            
            // Remove duplicates
            var unique = points.GroupBy(p => (p.X, p.Y))
                .Select(g => g.First())
                .ToList();
            
            if (unique.Count < 3) return unique;
            
            // Find leftmost (and lowest if tie) point
            int leftmost = 0;
            for (int i = 1; i < unique.Count; i++)
            {
                if (unique[i].X < unique[leftmost].X ||
                    (unique[i].X == unique[leftmost].X && unique[i].Y < unique[leftmost].Y))
                {
                    leftmost = i;
                }
            }
            
            var hull = new List<(long X, long Y)>();
            int current = leftmost;
            
            do
            {
                hull.Add(unique[current]);
                
                // Find the point that makes the smallest counter-clockwise angle
                int next = (current + 1) % unique.Count;
                
                for (int i = 0; i < unique.Count; i++)
                {
                    if (i == current) continue;
                    
                    var cross = Cross(unique[current], unique[next], unique[i]);
                    if (cross < 0 || (cross == 0 && Distance(unique[current], unique[i]) > Distance(unique[current], unique[next])))
                    {
                        next = i;
                    }
                }
                
                current = next;
            } while (current != leftmost && hull.Count < unique.Count);
            
            return hull;
        }
        
        /// <summary>
        /// Cross product for 2D points (returns scalar).
        /// </summary>
        private static long Cross((long X, long Y) O, (long X, long Y) A, (long X, long Y) B)
        {
            return (A.X - O.X) * (B.Y - O.Y) - (A.Y - O.Y) * (B.X - O.X);
        }
        
        /// <summary>
        /// Squared distance between two 2D points.
        /// </summary>
        private static long Distance((long X, long Y) A, (long X, long Y) B)
        {
            var dx = A.X - B.X;
            var dy = A.Y - B.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Order complex perimeter vertices by angle around centroid.
        /// NOT USED: Current implementation uses extraction order.
        /// </summary>
        public static List<(long X, long Y)> OrderComplexPerimeter(List<(long X, long Y)> vertices)
        {
            if (vertices.Count < 3)
                return vertices;

            // Find the centroid (approximate center) of the shape
            long centerX = (vertices.Min(v => v.X) + vertices.Max(v => v.X)) / 2;
            long centerY = (vertices.Min(v => v.Y) + vertices.Max(v => v.Y)) / 2;

            // Sort by angle around the centroid
            var sorted = vertices
                .Select(v => (Vertex: v, Angle: Math.Atan2(v.Y - centerY, v.X - centerX)))
                .OrderBy(v => v.Angle)
                .Select(v => v.Vertex)
                .ToList();

            return sorted;
        }
    }
}
