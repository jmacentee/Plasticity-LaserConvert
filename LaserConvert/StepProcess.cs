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
    internal static class StepProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                Console.WriteLine($"File loaded. Total items: {stepFile.Items.Count}");

                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    Console.WriteLine("No solids found in STEP file.");
                    return 0;
                }

                const double minThickness = 2.5;
                const double maxThickness = 10.0;

                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces, int Face1Idx, int Face2Idx, List<(double X, double Y, double Z)> Vertices, double DimX, double DimY, double DimZ)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, face1Idx, face2Idx, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, faces, face1Idx, face2Idx, vertices, dimX, dimY, dimZ));
                        Console.WriteLine($"[FILTER] {name}: dimensions {dimensions} - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[FILTER] {name}: dimensions {dimensions} - FAIL");
                    }
                }

                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                foreach (var (name, faces, face1Idx, face2Idx, vertices, dimX, dimY, dimZ) in thinSolids)
                {
                    svg.BeginGroup(name);

                    // Compute rotation from thin face pair (guaranteed to align with thin dimension)
                    // For complex shapes, the FindThinDimension algorithm on all vertices may find spurious pairs
                    double[,] rotMatrix1 = null;
                    double[,] rotMatrix2 = null;
                    List<GeometryTransform.Vec3> normalizedVertices = null;
                    
                    if (faces.Count > 20 && face1Idx >= 0 && face1Idx < faces.Count && face2Idx >= 0 && face2Idx < faces.Count)
                    {
                        // For complex shapes: get rotation from the identified thin faces only
                        var thinFaceVerts = new List<(double X, double Y, double Z)>();
                        thinFaceVerts.AddRange(StepTopologyResolver.ExtractVerticesFromFace(faces[face1Idx], stepFile));
                        thinFaceVerts.AddRange(StepTopologyResolver.ExtractVerticesFromFace(faces[face2Idx], stepFile));
                        if (thinFaceVerts.Count > 0)
                        {
                            // Compute rotation matrices from thin faces
                            var (_, m1, m2) = GeometryTransform.RotateAndNormalizeWithMatrices(thinFaceVerts);
                            rotMatrix1 = m1;
                            rotMatrix2 = m2;
                            Console.WriteLine($"[SVG] {name}: Computing rotation from {thinFaceVerts.Count} vertices in thin faces");
                        }
                    }
                    
                    // If we didn't get rotation matrices, compute them from all vertices
                    if (rotMatrix1 == null || rotMatrix2 == null)
                    {
                        var (_, m1, m2) = GeometryTransform.RotateAndNormalizeWithMatrices(vertices);
                        rotMatrix1 = m1;
                        rotMatrix2 = m2;
                    }
                    
                    // Now apply these rotation matrices to ALL vertices
                    normalizedVertices = vertices
                        .Select(v => {
                            var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                            var rot1 = ApplyMatrix(vec, rotMatrix1);
                            // For complex shapes, skip the edge-alignment normalization (rotMatrix2)
                            // since the thin-face vertex set may not have enough top vertices for good alignment
                            if (faces.Count > 20)
                            {
                                return rot1;
                            }
                            var rot2 = ApplyMatrix(rot1, rotMatrix2);
                            return rot2;
                        })
                        .ToList();

                    if (normalizedVertices.Count < 4)
                    {
                        svg.EndGroup();
                        continue;
                    }

                    var maxZ = normalizedVertices.Max(v => v.Z);
                    var minZ = normalizedVertices.Min(v => v.Z);
                    var zRange = maxZ - minZ;
                    
                    // For complex shapes, we need to use ALL vertices since they represent
                    // the full geometry. If Z discrimination doesn't work well (too few vertices),
                    // use all normalized vertices as the projection source.
                    var topFaceVerts = normalizedVertices;
                    
                    Console.WriteLine($"[SVG] {name}: Z range [{minZ:F1}, {maxZ:F1}] (range={zRange:F1}), using all {topFaceVerts.Count} vertices for projection");
                    
                    // Debug: Print X and Y ranges too
                    var projMinX = topFaceVerts.Min(v => v.X);
                    var projMaxX = topFaceVerts.Max(v => v.X);
                    var projMinY = topFaceVerts.Min(v => v.Y);
                    var projMaxY = topFaceVerts.Max(v => v.Y);
                    Console.WriteLine($"[SVG] {name}: After rotation - X:[{projMinX:F1},{projMaxX:F1}] Y:[{projMinY:F1},{projMaxY:F1}]");

                    // If we have a usable thin face index, prefer using its bounds for outline/holes
                    StepAdvancedFace mainFace = null;
                    if (face1Idx >= 0 && face1Idx < faces.Count && face2Idx >= 0 && face2Idx < faces.Count)
                    {
                        if (faces.Count > 20)
                        {
                            // For complex shapes: find the face with the most complex outer loop (most vertices)
                            // This is more likely to be the actual main face with holes/tabs
                            Console.WriteLine($"[SVG] {name}: Searching {faces.Count} faces for main face with complex outline");
                            int maxVertexCount = 0;
                            int bestFaceIdx = face1Idx;
                            
                            for (int i = 0; i < faces.Count; i++)
                            {
                                var faceVerts = StepTopologyResolver.ExtractVerticesFromFace(faces[i], stepFile);
                                if (faceVerts.Count > maxVertexCount)
                                {
                                    maxVertexCount = faceVerts.Count;
                                    bestFaceIdx = i;
                                }
                            }
                            
                            mainFace = faces[bestFaceIdx];
                            Console.WriteLine($"[SVG] {name}: Selected face with {maxVertexCount} vertices as main face");
                        }
                        else
                        {
                            // For simple shapes: use the top face from the thin pair
                            var face1Verts = StepTopologyResolver.ExtractVerticesFromFace(faces[face1Idx], stepFile);
                            var face2Verts = StepTopologyResolver.ExtractVerticesFromFace(faces[face2Idx], stepFile);
                            
                            var face1RotatedZ = face1Verts
                                .Select(v => {
                                    var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                                    var rot1 = ApplyMatrix(vec, rotMatrix1);
                                    if (faces.Count > 20)
                                        return rot1;
                                    return ApplyMatrix(rot1, rotMatrix2);
                                })
                                .Average(v => v.Z);
                            
                            var face2RotatedZ = face2Verts
                                .Select(v => {
                                    var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                                    var rot1 = ApplyMatrix(vec, rotMatrix1);
                                    if (faces.Count > 20)
                                        return rot1;
                                    return ApplyMatrix(rot1, rotMatrix2);
                                })
                                .Average(v => v.Z);
                            
                            mainFace = face1RotatedZ > face2RotatedZ ? faces[face1Idx] : faces[face2Idx];
                        }
                    }
                    else if (face1Idx >= 0 && face1Idx < faces.Count)
                    {
                        mainFace = faces[face1Idx];
                    }
                    else if (face2Idx >= 0 && face2Idx < faces.Count)
                    {
                        mainFace = faces[face2Idx];
                    }
                    

                    // Try to extract the actual face outline from the main face
                    if (mainFace != null)
                    {
                        var faceOutlineVerts3D = StepTopologyResolver.ExtractOuterLoopVerticesFromFace(mainFace, stepFile);
                        
                        // Use face outline if we have enough vertices (indicates complexity worth preserving)
                        // OR if this is identified as a complex shape (>20 faces)
                        if (faceOutlineVerts3D.Count > 4 && (faceOutlineVerts3D.Count > 8 || faces.Count > 20))
                        {
                            // Standard case: face has explicit complex outline
                            Console.WriteLine($"[SVG] {name}: Face outline has {faceOutlineVerts3D.Count} 3D vertices (complex shape detected)");
                            
                            // Transform these vertices to normalized coordinates
                            var outlineNormalizedVerts = faceOutlineVerts3D
                                .Select(v => {
                                    var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                                    var rot1 = ApplyMatrix(vec, rotMatrix1);
                                    if (faces.Count > 20)
                                    {
                                        return rot1;
                                    }
                                    var rot2 = ApplyMatrix(rot1, rotMatrix2);
                                    return rot2;
                                })
                                .ToList();
                            
                            // Find top-face vertices (those at max Z)
                            var topZ = outlineNormalizedVerts.Max(v => v.Z);
                            var topVerts = outlineNormalizedVerts
                                .Where(v => Math.Abs(v.Z - topZ) < 1.5)
                                .ToList();
                            

                            if (topVerts.Count >= 4)
                            {
                                Console.WriteLine($"[SVG] {name}: Face outline has {topVerts.Count} top-face vertices");
                                
                                // Project to 2D
                                var minXv = topVerts.Min(v => v.X);
                                var minYv = topVerts.Min(v => v.Y);
                                var pts2d = topVerts
                                    .Select(v => ((long)Math.Round(v.X - minXv), (long)Math.Round(v.Y - minYv)))
                                    .ToList();
                                
                                // Remove consecutive duplicates
                                var dedup = new List<(long X, long Y)>();
                                foreach (var p in pts2d)
                                {
                                    if (dedup.Count == 0 || dedup.Last().X != p.Item1 || dedup.Last().Y != p.Item2)
                                        dedup.Add((p.Item1, p.Item2));
                                }
                        
                                if (dedup.Count >= 4)
                                {
                                    // Sort vertices by perimeter for complex shapes
                                    var sorted = SortPerimeterVertices2D(dedup);
                                    var faceOutlinePath = BuildPerimeterPath(sorted);
                                    Console.WriteLine($"[SVG] {name}: Generated face outline path from {dedup.Count} 2D points");
                                    svg.Path(faceOutlinePath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                    
                                    // Render holes
                                    var outlineMinX = topVerts.Min(v => v.X);
                                    var outlineMaxX = topVerts.Max(v => v.X);
                                    var outlineMinY = topVerts.Min(v => v.Y);
                                    var outlineMaxY = topVerts.Max(v => v.Y);
                                    
                                    RenderHoles(svg, mainFace, stepFile, normalizedVertices, outlineMinX, outlineMinY, rotMatrix1, rotMatrix2);
                                    
                                    svg.EndGroup();
                                    continue;
                                }
                                else
                                {
                                    Console.WriteLine($"[SVG] {name}: After dedup only {dedup.Count} points, using fallback");
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[SVG] {name}: Only {topVerts.Count} top verts (need 4+), using fallback");
                            }
                        }
                        else if (faces.Count > 20 && faceOutlineVerts3D.Count == 4)
                        {
                            // Complex shape but single face only has 4 vertices
                            // Use the TOP-LEVEL outline from all normalized vertices instead
                            Console.WriteLine($"[SVG] {name}: Complex shape but face only has {faceOutlineVerts3D.Count} vertices, using full projection outline");
                            
                            // Find top-level vertices - use a wider Z range to capture tabs/cutouts
                            var topZ = normalizedVertices.Max(v => v.Z);
                            var bottomZ = normalizedVertices.Min(v => v.Z);
                            var zRng = topZ - bottomZ;
                            // For complex shapes, use top 25% of height to capture all edge geometry
                            var zThreshold = topZ - (zRng * 0.25);
                            
                            var topVerts = normalizedVertices
                                .Where(v => v.Z >= zThreshold)
                                .Distinct(new Vec3Comparer())
                                .ToList();
                            
    
                            Console.WriteLine($"[SVG] {name}: Using top 25% of geometry: {topVerts.Count} vertices (Z >= {zThreshold:F1})");
                            
                            if (topVerts.Count >= 4)
                            {
                                // Project to 2D
                                var minXv = topVerts.Min(v => v.X);
                                var minYv = topVerts.Min(v => v.Y);
                                var pts2d = topVerts
                                    .Select(v => ((long)Math.Round(v.X - minXv), (long)Math.Round(v.Y - minYv)))
                                    .ToList();
                                
                                // Remove consecutive duplicates
                                var dedup = new List<(long X, long Y)>();
                                foreach (var p in pts2d)
                                {
                                    if (dedup.Count == 0 || dedup.Last().X != p.Item1 || dedup.Last().Y != p.Item2)
                                        dedup.Add((p.Item1, p.Item2));
                                }
                                
                                Console.WriteLine($"[SVG] {name}: After 2D projection and dedup: {dedup.Count} unique points");
                                
                                if (dedup.Count >= 4)
                                {
                                    // For many points, use OrderPerimeterVertices (convex hull) instead of orthogonal sort
                                    if (dedup.Count > 8)
                                    {
                                        Console.WriteLine($"[SVG] {name}: Using perimeter ordering for {dedup.Count} complex points");
                                        var vec3pts = dedup.Select(p => new GeometryTransform.Vec3(p.X, p.Y, 0)).ToList();
                                        var ordered = OrderPerimeterVertices(vec3pts);
                                        var outlinePathData = BuildPerimeterPath(ordered.Select(v => ((long)v.X, (long)v.Y)).ToList());
                                        svg.Path(outlinePathData, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                    }
                                    else
                                    {
                                        // For 4-8 points, use orthogonal sort
                                        var sorted = SortPerimeterVertices2D(dedup);
                                        var outlinePathData = BuildPerimeterPath(sorted);
                                        svg.Path(outlinePathData, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                    }
                                    Console.WriteLine($"[SVG] {name}: Generated outline path");
                                    
                                    // Render holes from main face
                                    var outlineMinX = topVerts.Min(v => v.X);
                                    var outlineMaxX = topVerts.Max(v => v.X);
                                    var outlineMinY = topVerts.Min(v => v.Y);
                                    var outlineMaxY = topVerts.Max(v => v.Y);
                                    
                                    RenderHoles(svg, mainFace, stepFile, normalizedVertices, outlineMinX, outlineMinY, rotMatrix1, rotMatrix2);
                                    
                                    svg.EndGroup();
                                    continue;
                                }
                            }
                        }
                    }

                    // Fallback: use simple bounding box for all other cases
                    var minX = topFaceVerts.Min(v => v.X);
                    var maxX = topFaceVerts.Max(v => v.X);
                    var minY = topFaceVerts.Min(v => v.Y);
                    var maxY = topFaceVerts.Max(v => v.Y);

                    var rectWidth = (long)Math.Round(maxX - minX);
                    var rectHeight = (long)Math.Round(maxY - minY);
                    Console.WriteLine($"[SVG] {name}: Bounding box: {rectWidth} x {rectHeight}");

                    var pathData = $"M 0 0 L {rectWidth} 0 L {rectWidth} {rectHeight} L 0 {rectHeight} Z";
                    svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#000");

                    // Render holes
                    RenderHoles(svg, mainFace, stepFile, normalizedVertices, minX, minY, rotMatrix1, rotMatrix2);

                    svg.EndGroup();
                }

                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        }

        private static Dimensions ComputeDimensions(List<(double X, double Y, double Z)> points)
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

        private record Dimensions(double Width, double Height, double Depth)
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

        // Build axis-aligned path from ordered loop by rounding and snapping to H/V segments
        private static string BuildAxisAlignedPathFromOrderedLoop(List<GeometryTransform.Vec3> loop)
        {
            if (loop == null || loop.Count < 3) return string.Empty;
            var pts = loop.Select(v => (X: (long)Math.Round(v.X), Y: (long)Math.Round(v.Y))).ToList();
            var minX = pts.Min(p => p.X);
            var minY = pts.Min(p => p.Y);
            pts = pts.Select(p => (p.X - minX, p.Y - minY)).ToList();

            var sb = new StringBuilder();
            sb.Append($"M {pts[0].Item1},{pts[0].Item2}");
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1];
                var b = pts[i];
                if (b.Item1 == a.Item1)
                {
                    sb.Append($" L {b.Item1},{b.Item2}");
                }
                else if (b.Item2 == a.Item2)
                {
                    sb.Append($" L {b.Item1},{b.Item2}");
                }
                else
                {
                    // Snap to nearest axis by choosing dominant delta
                    var dx = Math.Abs(b.Item1 - a.Item1);
                    var dy = Math.Abs(b.Item2 - a.Item2);
                    if (dx >= dy)
                    {
                        sb.Append($" L {b.Item1},{a.Item2}");
                        sb.Append($" L {b.Item1},{b.Item2}");
                    }
                    else
                    {
                        sb.Append($" L {a.Item1},{b.Item2}");
                        sb.Append($" L {b.Item1},{b.Item2}");
                    }
                }
            }
            sb.Append(" Z");
            return sb.ToString();
        }

        private struct Vec2
        {
            public readonly double X, Y;
            public Vec2(double x, double y) { X = x; Y = y; }
        }

        private struct Vec3
        {
            public readonly double X, Y, Z;
            public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }
            public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
            public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec3 operator *(double s, Vec3 a) => new Vec3(s * a.X, s * a.Y, s * a.Z);
        }

        private static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        private static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X
        );
        private static Vec3 Normalize(Vec3 v)
        {
            var len = v.Length;
            return len > 1e-12 ? (1.0 / len) * v : v;
        }

        private static (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) BuildProjectionFrame(List<(double X, double Y, double Z)> vertices)
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

        private static Vec2 Project((Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, Vec3 point)
        {
            var rel = point - frame.Origin;
            return new Vec2(Dot(rel, frame.U), Dot(rel, frame.V));
        }

        private static StepEdgeLoop GetEdgeLoopFromBound(StepFaceBound bound)
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

        private static string BuildOrthogonalLoop2D(List<(long X, long Y)> pts)
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
            return BuildPerimeterPath(walk);
        }

        /// <summary>
        /// Build an SVG path from 2D perimeter points, connecting them in order.
        /// </summary>
        private static string BuildPerimeterPath(List<(long X, long Y)> pts)
        {
            if (pts == null || pts.Count < 3)
                return string.Empty;
            
            var sb = new StringBuilder();
            sb.Append($"M {pts[0].X},{pts[0].Y}");
            
            for (int i = 1; i < pts.Count; i++)
            {
                var current = pts[i];
                sb.Append($" L {current.X},{current.Y}");
            }
            
            sb.Append(" Z");
            return sb.ToString();
        }

        /// <summary>
        /// Order vertices around a perimeter using Graham scan (convex hull algorithm).
        /// Works reliably for both convex and non-convex polygons when vertices are on the boundary.
        /// </summary>
        private static List<GeometryTransform.Vec3> OrderPerimeterVertices(List<GeometryTransform.Vec3> vertices)
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
        
        /// <summary>Compute the angle for clockwise ordering (right=0, down=90, left=180, up=270 degrees).</summary>
        private static double ComputeAngleForClockwise(GeometryTransform.Vec3 from, GeometryTransform.Vec3 to)
        {
            var dx = to.X - from.X;
            var dy = to.Y - from.Y;
            
            // atan2 in SVG coords: 0° = right (+X), 90° = down (+Y), 180° = left (-X), 270° = up (-Y)
            var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            
            // Normalize to [0, 360)
            if (angle < 0)
                angle += 360;
            
            return angle;
        }

        private static bool IsCloseToOriginal(GeometryTransform.Vec3 normVert, (double X, double Y, double Z) orig3D, 
            double[,] rotMatrix1, double[,] rotMatrix2)
        {
            // Apply rotations to the original 3D vertex
            var orig = new GeometryTransform.Vec3(orig3D.X, orig3D.Y, orig3D.Z);
            var rot1 = ApplyMatrix(orig, rotMatrix1);
            var rot2 = ApplyMatrix(rot1, rotMatrix2);
            
            // Check if close (within rounding tolerance)
            return Math.Abs(normVert.X - rot2.X) < 0.5 &&
                   Math.Abs(normVert.Y - rot2.Y) < 0.5 &&
                   Math.Abs(normVert.Z - rot2.Z) < 0.5;
        }
        
        /// <summary>
        /// Unified hole rendering for any solid with holes.
        /// Extracts holes from the solid's geometry by finding faces that represent holes.
        /// </summary>
        private static void RenderHoles(SvgBuilder svg, StepAdvancedFace mainFace, StepFile stepFile,
            List<GeometryTransform.Vec3> normalizedVertices, double outlineMinX, double outlineMinY,
            double[,] rotMatrix1, double[,] rotMatrix2)
        {
            // UNIFIED APPROACH: Extract holes from mainFace bounds if available
            // If mainFace has multiple bounds, those are the holes
            if (mainFace != null && mainFace.Bounds?.Count > 1)
            {
                Console.WriteLine($"[SVG] Rendering holes: Face has {mainFace.Bounds.Count} bounds");
                
                var (_, holeLoopsOriginal) = StepTopologyResolver.ExtractFaceWithHoles(mainFace, stepFile);
                
                foreach (var holeVertices3D in holeLoopsOriginal)
                {
                    RenderSingleHole(svg, holeVertices3D, normalizedVertices, outlineMinX, outlineMinY, rotMatrix1, rotMatrix2);
                }
            }
        }

        private static void RenderSingleHole(SvgBuilder svg, List<(double X, double Y, double Z)> holeVertices3D,
            List<GeometryTransform.Vec3> normalizedVertices, double outlineMinX, double outlineMinY,
            double[,] rotMatrix1, double[,] rotMatrix2)
        {
            Console.WriteLine($"[SVG] Processing hole with {holeVertices3D.Count} vertices in 3D");
            
            if (holeVertices3D.Count < 3)
            {
                Console.WriteLine($"[SVG] Hole skipped (only {holeVertices3D.Count} vertices)");
                return;
            }
            
            // Transform hole vertices directly using the rotation matrices
            var holeNormalizedVerts = holeVertices3D
                .Select(v => {
                    var vec = new GeometryTransform.Vec3(v.Item1, v.Item2, v.Item3);
                    // Apply both rotations like we do for other vertices
                    var rot1Result = new GeometryTransform.Vec3(
                        rotMatrix1[0, 0] * vec.X + rotMatrix1[0, 1] * vec.Y + rotMatrix1[0, 2] * vec.Z,
                        rotMatrix1[1, 0] * vec.X + rotMatrix1[1, 1] * vec.Y + rotMatrix1[1, 2] * vec.Z,
                        rotMatrix1[2, 0] * vec.X + rotMatrix1[2, 1] * vec.Y + rotMatrix1[2, 2] * vec.Z
                    );
                    
                    // Don't apply rotMatrix2 for complex shapes (it might not be valid)
                    return rot1Result;
                })
                .ToList();
            
            if (holeNormalizedVerts.Count < 3)
            {
                Console.WriteLine($"[SVG] Hole skipped after transformation");
                return;
            }
        
            var hx = holeNormalizedVerts.Min(v => v.X);
            var hx2 = holeNormalizedVerts.Max(v => v.X);
            var hy = holeNormalizedVerts.Min(v => v.Y);
            var hy2 = holeNormalizedVerts.Max(v => v.Y);
            var holeX = (long)Math.Round(hx - outlineMinX);
            var holeY = (long)Math.Round(hy - outlineMinY);
            var holeW = (long)Math.Round(hx2 - hx);
            var holeH = (long)Math.Round(hy2 - hy);
            
            Console.WriteLine($"[SVG] Hole 3D bounds: X:[{hx:F1},{hx2:F1}] Y:[{hy:F1},{hy2:F1}], normalized: ({holeX},{holeY}) {holeW}x{holeH}");
            
            if (holeW > 1 && holeH > 1)
            {
                var holePath = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                svg.Path(holePath, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                Console.WriteLine($"[SVG] Added hole path");
            }
            else
            {
                Console.WriteLine($"[SVG] Hole too small ({holeW}x{holeH})");
            }
        }
        
        private static GeometryTransform.Vec3 ApplyMatrix(GeometryTransform.Vec3 v, double[,] matrix)
        {
            return new GeometryTransform.Vec3(
                matrix[0, 0] * v.X + matrix[0, 1] * v.Y + matrix[0, 2] * v.Z,
                matrix[1, 0] * v.X + matrix[1, 1] * v.Y + matrix[1, 2] * v.Z,
                matrix[2, 0] * v.X + matrix[2, 1] * v.Y + matrix[2, 2] * v.Z
            );
        }

        private class Vec3Comparer : IEqualityComparer<GeometryTransform.Vec3>
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

        private static List<(long X, long Y)> SortPerimeterVertices2D(List<(long X, long Y)> pts)
        {
            if (pts == null || pts.Count < 3)
                return new List<(long X, long Y)>();

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
    }
}
