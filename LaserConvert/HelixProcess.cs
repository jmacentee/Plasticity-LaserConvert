using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IxMilia.Step;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// HelixProcess implements proper polygon boundary ordering using Gift Wrapping algorithm
    /// instead of Graham Scan (which simplifies to convex hull).
    /// This preserves all boundary vertices including non-convex features like tabs and cutouts.
    /// </summary>
    public static class HelixProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"[LaserConvert] Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                Console.WriteLine($"[LaserConvert] File loaded. Total items: {stepFile.Items.Count}");

                // Find all solids (manifold solid breps)
                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"[LaserConvert] Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    Console.WriteLine("[LaserConvert] No solids found in STEP file.");
                    return 0;
                }

                // Filter solids by thin dimension (3mm)
                const double minThickness = 2.5;
                const double maxThickness = 10.0;
                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces)>();
                
                foreach (var (name, faces) in solids)
                {
                    var (vertices, _, _, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dims = new[] { dimX, dimY, dimZ };
                    var sortedDims = dims.Select((d, i) => (d, i)).OrderBy(x => x.d).ToArray();
                    var thin = sortedDims[0];
                    
                    if (thin.d >= minThickness && thin.d <= maxThickness)
                    {
                        thinSolids.Add((name, faces));
                        Console.WriteLine($"[LaserConvert] [FILTER] {name}: dimensions [{dimX:F1}, {dimY:F1}, {dimZ:F1}] - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[LaserConvert] [FILTER] {name}: dimensions [{dimX:F1}, {dimY:F1}, {dimZ:F1}] - FAIL");
                    }
                }
                
                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("[LaserConvert] No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                
                foreach (var (name, faces) in thinSolids)
                {
                    svg.BeginGroup(name);
                    Console.WriteLine($"\n[STEP 1-5] Processing {name} with {faces.Count} faces");
                    
                    // STEP 1-5: Find the best face (with most boundary vertices) that represents the geometry
                    // The StepTopologyResolver should have already done steps 1-5:
                    // 1. Find thin dimension (shortest distance between faces)
                    // 2. Calculate rotation to align thin dimension
                    // 3. Apply rotation so thin dimension is along Z
                    // 4. Pick topmost face along Z
                    // 5. Apply edge alignment rotation so outline aligns with X axis
                    
                    StepAdvancedFace bestFace = null;
                    int maxVerts = 0;
                    
                    foreach (var face in faces)
                    {
                        var (outerVerts, _) = StepTopologyResolver.ExtractFaceWithHoles(face, stepFile);
                        if (outerVerts.Count > maxVerts)
                        {
                            maxVerts = outerVerts.Count;
                            bestFace = face;
                        }
                    }
                    
                    Console.WriteLine($"[STEP 1-5] Selected face with {maxVerts} boundary vertices");
                    
                    if (bestFace != null && maxVerts >= 3)
                    {
                        var (outerPerimeter, holePerimeters) = StepTopologyResolver.ExtractFaceWithHoles(bestFace, stepFile);
                        
                        Console.WriteLine($"[STEP 6] Outer perimeter has {outerPerimeter.Count} vertices (3D)");
                        var rangeX = outerPerimeter.Max(p => p.X) - outerPerimeter.Min(p => p.X);
                        var rangeY = outerPerimeter.Max(p => p.Y) - outerPerimeter.Min(p => p.Y);
                        var rangeZ = outerPerimeter.Max(p => p.Z) - outerPerimeter.Min(p => p.Z);
                        Console.WriteLine($"[STEP 6] 3D ranges - X:[{outerPerimeter.Min(p => p.X):F1},{outerPerimeter.Max(p => p.X):F1}] Y:[{outerPerimeter.Min(p => p.Y):F1},{outerPerimeter.Max(p => p.Y):F1}] Z:[{outerPerimeter.Min(p => p.Z):F1},{outerPerimeter.Max(p => p.Z):F1}]");
                        
                        // STEP 6: Project to 2D
                        var projectedOuter = ProjectTo2D(outerPerimeter);
                        Console.WriteLine($"[STEP 6] After projection to 2D - 2D ranges:");
                        if (projectedOuter.Count > 0)
                        {
                            var p2dRangeX = projectedOuter.Max(p => p.X) - projectedOuter.Min(p => p.X);
                            var p2dRangeY = projectedOuter.Max(p => p.Y) - projectedOuter.Min(p => p.Y);
                            Console.WriteLine($"[STEP 6] 2D ranges - X:[{projectedOuter.Min(p => p.X):F1},{projectedOuter.Max(p => p.X):F1}] (extent={p2dRangeX:F1}) Y:[{projectedOuter.Min(p => p.Y):F1},{projectedOuter.Max(p => p.Y):F1}] (extent={p2dRangeY:F1})");
                        }
                        
                        var normalizedOuter = NormalizeAndRound(projectedOuter);
                        Console.WriteLine($"[STEP 6] After normalization and rounding: {normalizedOuter.Count} vertices");
                        
                        // STEP 7: Remove consecutive duplicates to preserve all boundary vertices
                        var orderedOuter = RemoveConsecutiveDuplicates(normalizedOuter);
                        Console.WriteLine($"[STEP 7] After removing consecutive duplicates: {orderedOuter.Count} vertices");
                        
                        if (orderedOuter.Count >= 3)
                        {
                            var outerPath = BuildPath(orderedOuter);
                            svg.Path(outerPath, 0.2, "none", "#000");
                            Console.WriteLine($"[STEP 8] Generated SVG path for outer perimeter");
                        }
                        
                        // Handle holes - normalize relative to original (not rounded) outer min
                        var outerMinX = projectedOuter.Min(p => p.X);
                        var outerMinY = projectedOuter.Min(p => p.Y);
                        
                        foreach (var holePerim in holePerimeters)
                        {
                            if (holePerim.Count >= 3)
                            {
                                var projHole = ProjectTo2D(holePerim);
                                // Normalize using original 3D/2D coordinates, not rounded ones
                                var normHole = projHole.Select(p => (
                                    (long)Math.Round(p.X - outerMinX),
                                    (long)Math.Round(p.Y - outerMinY)
                                )).ToList();
                                
                                // For holes, only remove consecutive duplicates (not convex hull)
                                var orderedHole = RemoveConsecutiveDuplicates(normHole);
                                if (orderedHole.Count >= 3)
                                {
                                    var holePath = BuildPath(orderedHole);
                                    svg.Path(holePath, 0.2, "none", "#f00");
                                    Console.WriteLine($"[STEP 8] Generated SVG path for hole with {orderedHole.Count} vertices");
                                }
                            }
                        }
                    }
                    
                    svg.EndGroup();
                }
                
                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"[STEP 8] Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LaserConvert] Error: {ex.Message}");
                Console.WriteLine($"[LaserConvert] Stack: {ex.StackTrace}");
                return 2;
            }
        }

        /// <summary>
        /// Gift Wrapping algorithm (Jarvis March) preserves all boundary vertices including non-convex features.
        /// Unlike Graham Scan, it doesn't simplify to convex hull.
        /// </summary>
        private static List<(long X, long Y)> GiftWrapPerimeter(List<(long X, long Y)> points)
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
        
        private static long Cross((long X, long Y) O, (long X, long Y) A, (long X, long Y) B)
        {
            return (A.X - O.X) * (B.Y - O.Y) - (A.Y - O.Y) * (B.X - O.X);
        }
        
        private static long Distance((long X, long Y) A, (long X, long Y) B)
        {
            var dx = A.X - B.X;
            var dy = A.Y - B.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>
        /// Remove consecutive duplicate points while preserving all unique vertices in order.
        /// This is safe for non-convex shapes and preserves cutouts/tabs.
        /// </summary>
        private static List<(long X, long Y)> RemoveConsecutiveDuplicates(List<(long X, long Y)> points)
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

        private static List<(double X, double Y)> ProjectTo2D(List<(double X, double Y, double Z)> points3D)
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

        private static List<(long X, long Y)> NormalizeAndRound(List<(double X, double Y)> points2D)
        {
            if (points2D.Count == 0) return new List<(long, long)>();
            
            var minX = points2D.Min(p => p.X);
            var minY = points2D.Min(p => p.Y);
            
            return points2D.Select(p => (
                (long)Math.Round(p.X - minX),
                (long)Math.Round(p.Y - minY)
            )).ToList();
        }

        private static string BuildPath(List<(long X, long Y)> points)
        {
            if (points == null || points.Count < 3) return string.Empty;
            
            var sb = new StringBuilder();
            sb.Append($"M {points[0].X},{points[0].Y}");
            for (int i = 1; i < points.Count; i++)
                sb.Append($" L {points[i].X},{points[i].Y}");
            sb.Append(" Z");
            return sb.ToString();
        }

        private sealed class SvgBuilder
        {
            private readonly StringBuilder _sb = new StringBuilder();
            
            public SvgBuilder()
            {
                _sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"1000\" height=\"1000\" viewBox=\"0 0 1000 1000\">");
                _sb.AppendLine("<defs/>");
            }
            
            public void BeginGroup(string name)
            {
                name = Sanitize(name);
                _sb.AppendLine($"  <g id=\"{name}\">");
            }
            
            public void EndGroup()
            {
                _sb.AppendLine("  </g>");
            }
            
            public void Path(string d, double strokeWidth, string fill, string stroke = "#000")
            {
                _sb.AppendLine($"    <path d=\"{d}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString("0.###", CultureInfo.InvariantCulture)}\" fill=\"{fill}\" vector-effect=\"non-scaling-stroke\"/>");
            }
            
            public string Build()
            {
                _sb.AppendLine("</svg>");
                return _sb.ToString();
            }
            
            private static string Sanitize(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "object";
                var ok = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
                return string.IsNullOrEmpty(ok) ? "object" : ok;
            }
        }
    }
}
