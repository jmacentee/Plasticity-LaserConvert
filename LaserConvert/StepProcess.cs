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
                    
                    // Check if the shape is already axis-aligned to avoid unnecessary rotation
                    // ATTEMPTED: Apply rotMatrix1 to all shapes
                    // RESULT: KCBoxFlat (already correctly aligned) gets Y dimension collapsed to 3mm
                    // REASON: If shape is already aligned with thin dimension in Z, rotating collapses Y
                    // FIX: Detect if already aligned and skip rotation matrices
                    var minDimX = vertices.Max(v => v.X) - vertices.Min(v => v.X);
                    var minDimY = vertices.Max(v => v.Y) - vertices.Min(v => v.Y);
                    var minDimZ = vertices.Max(v => v.Z) - vertices.Min(v => v.Z);
                    var sortedDims = new[] { minDimX, minDimY, minDimZ }.OrderBy(d => d).ToList();
                    bool isAlreadyAligned = Math.Abs(minDimZ - sortedDims[0]) < 0.1; // Thin dimension is Z
                    
                    // Now apply these rotation matrices to ALL vertices
                    normalizedVertices = vertices
                        .Select(v => {
                            var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                            if (isAlreadyAligned)
                            {
                                // Already correctly aligned - no rotation needed
                                return vec;
                            }
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
                            // For complex shapes: pick the face that when extracted gives the MOST BOUNDARY VERTICES
                            // This indicates a complex outline with holes/tabs
                            // This works for both rotated shapes (KCBox) and axis-aligned shapes (KCBoxFlat)
                            Console.WriteLine($"[SVG] {name}: Searching {faces.Count} faces for face with most boundary vertices");
                            int maxBoundaryVerts = 0;
                            int bestFaceIdx = face1Idx;
                            
                            for (int i = 0; i < faces.Count; i++)
                            {
                                var (outerVerts, _) = StepTopologyResolver.ExtractFaceWithHoles(faces[i], stepFile);
                                if (outerVerts.Count > maxBoundaryVerts)
                                {
                                    maxBoundaryVerts = outerVerts.Count;
                                    bestFaceIdx = i;
                                }
                                
                                if (outerVerts.Count >= 10)  // Log verbose info for complex boundaries
                                    Console.WriteLine($"[SVG] {name}:   Face {i}: {outerVerts.Count} boundary verts");
                            }
                            
                            mainFace = faces[bestFaceIdx];
                            Console.WriteLine($"[SVG] {name}: Selected face with {maxBoundaryVerts} boundary vertices as main face");
                        }
                        else
                        {
                            // For simple shapes: first try to find a face with holes (multiple bounds)
                            // If found, use that. Otherwise use the top face from the thin pair
                            StepAdvancedFace faceWithHoles = null;
                            for (int i = 0; i < faces.Count; i++)
                            {
                                if (faces[i].Bounds?.Count > 1)
                                {
                                    faceWithHoles = faces[i];
                                    break;
                                }
                            }
                            
                            if (faceWithHoles != null)
                            {
                                mainFace = faceWithHoles;
                                Console.WriteLine($"[SVG] {name}: Found face with {faceWithHoles.Bounds.Count} bounds (has holes)");
                            }
                            else
                            {
                                // No face with holes found, use top face from thin pair
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
                        // For complex shapes, use corner vertex extraction to avoid degenerate edge topology
                        // For simple shapes, use traditional bound-based extraction
                        var (outerLoopVerts, holeLoops) = StepTopologyResolver.ExtractFaceWithHoles(mainFace, stepFile);

                        // FALLBACK for degenerate complex shapes:  
                        // If the extracted outline is degenerate (all vertices on a line or plane with zero extent),
                        // instead of falling back to ALL vertices, reorder the extracted vertices properly
                        // using Gift Wrapping (which preserves non-convex features)
                        if (faces.Count > 20 && outerLoopVerts.Count > 0)
                        {
                            var outX_before = outerLoopVerts.Max(v => v.Item1) - outerLoopVerts.Min(v => v.Item1);
                            var outY_before = outerLoopVerts.Max(v => v.Item2) - outerLoopVerts.Min(v => v.Item2);
                            var outZ_before = outerLoopVerts.Max(v => v.Item3) - outerLoopVerts.Min(v => v.Item3);
                            var minDim_before = Math.Min(Math.Min(outX_before, outY_before), outZ_before);
                            
                            if (minDim_before < 0.1)
                            {
                                // Degenerate face extraction - the vertices are in edge-loop order, not perimeter order
                                // Don't fall back to ALL vertices; instead, use Gift Wrapping to properly order
                                // the extracted vertices while preserving all of them
                                Console.WriteLine($"[SVG] {name}: Face extraction is edge-ordered (min dim {minDim_before:F3}), preserving all {outerLoopVerts.Count} extracted vertices");
                                // Keep outerLoopVerts as-is; we'll reorder them below using Gift Wrapping
                            }
                        }
                        
                        if (outerLoopVerts.Count >= 4)
                        {
                            Console.WriteLine($"[SVG] {name}: Extracted outer loop with {outerLoopVerts.Count} vertices from face bounds");
                            
                            // Transform outer loop vertices using the appropriate rotations
                            var outlineNormalizedVerts = outerLoopVerts
                                .Select(v => {
                                    var vec = new GeometryTransform.Vec3(v.X, v.Y, v.Z);
                                    var rot1 = ApplyMatrix(vec, rotMatrix1);
                                    if (faces.Count > 20)
                                        return rot1;  // Complex: skip rotMatrix2
                                    return ApplyMatrix(rot1, rotMatrix2);  // Simple: apply both
                                })
                                .ToList();

                            // Debug: check vertex ranges before projection
                            if (outlineNormalizedVerts.Count > 0)
                            {
                                var outX = outlineNormalizedVerts.Select(v => v.X);
                                var outY = outlineNormalizedVerts.Select(v => v.Y);
                                var outZ = outlineNormalizedVerts.Select(v => v.Z);
                                Console.WriteLine($"[SVG] {name}: Outline vertices after rotation - X:[{outX.Min():F1},{outX.Max():F1}] Y:[{outY.Min():F1},{outY.Max():F1}] Z:[{outZ.Min():F1},{outZ.Max():F1}]");
                            }
                            
                            // Project to 2D - for complex shapes where rotMatrix2 is skipped,
                            // we need to find which 2 axes actually contain the geometry
                            // ATTEMPTED: Always project X-Y
                            // RESULT: KCBoxFlat has geometry in X and Z, Y is collapsed to 3mm
                            // FIX: For complex shapes, find the two axes with largest variance
                            var rangeX = outlineNormalizedVerts.Max(v => v.X) - outlineNormalizedVerts.Min(v => v.X);
                            var rangeY = outlineNormalizedVerts.Max(v => v.Y) - outlineNormalizedVerts.Min(v => v.Y);
                            var rangeZ = outlineNormalizedVerts.Max(v => v.Z) - outlineNormalizedVerts.Min(v => v.Z);
                            
                            List<(double X, double Y)> pts2d_precise;
                            
                            // For ALL shapes, use the two axes with largest ranges
                            // This works uniformly for both simple and complex geometries
                            var ranges = new[] { ("X", rangeX), ("Y", rangeY), ("Z", rangeZ) }
                                .OrderByDescending(r => r.Item2)
                                .ToList();
                            

                            // Project using the two largest axes
                            if (ranges[0].Item1 == "X" && ranges[1].Item1 == "Y")
                            {
                                // X-Y projection
                                var minXv = outlineNormalizedVerts.Min(v => v.X);
                                var minYv = outlineNormalizedVerts.Min(v => v.Y);
                                pts2d_precise = outlineNormalizedVerts.Select(v => (v.X - minXv, v.Y - minYv)).ToList();
                            }
                            else if (ranges[0].Item1 == "X" && ranges[1].Item1 == "Z")
                            {
                                // X-Z projection (swap Z into Y for SVG)
                                var minXv = outlineNormalizedVerts.Min(v => v.X);
                                var minZv = outlineNormalizedVerts.Min(v => v.Z);
                                pts2d_precise = outlineNormalizedVerts.Select(v => (v.X - minXv, v.Z - minZv)).ToList();
                            }
                            else if (ranges[0].Item1 == "Y" && ranges[1].Item1 == "Z")
                            {
                                // Y-Z projection (swap to X-Y for SVG)
                                var minYv = outlineNormalizedVerts.Min(v => v.Y);
                                var minZv = outlineNormalizedVerts.Min(v => v.Z);
                                pts2d_precise = outlineNormalizedVerts.Select(v => (v.Y - minYv, v.Z - minZv)).ToList();
                            }
                            else
                            {
                                // Fallback: use X-Y
                                var minXv = outlineNormalizedVerts.Min(v => v.X);
                                var minYv = outlineNormalizedVerts.Min(v => v.Y);
                                pts2d_precise = outlineNormalizedVerts.Select(v => (v.X - minXv, v.Y - minYv)).ToList();
                            }


                            // For complex shapes, don't dedup - keep all 32+ vertices to preserve outline detail
                            // For simple shapes, dedup consecutive points at high precision
                            // ATTEMPTED: Dedup all shapes - loses vertices for complex shapes
                            // RESULT: KCBox 32 vertices -> 18 points -> jumps around
                            // REASON: Dedup after projection breaks sequential order
                            // FIX: For complex shapes (faces.Count > 20), don't dedup to preserve all boundary vertices
                            List<(double X, double Y)> dedup_precise;
                            if (faces.Count > 20)
                            {
                                // Complex shape: dedup by 2D projected position (remove duplicates from top/bottom faces)
                                // then keep all unique points to preserve outline detail
                                var unique2d = new Dictionary<string, (double X, double Y)>();
                                foreach (var p in pts2d_precise)
                                {
                                    var key = $"{Math.Round(p.X, 1):F1},{Math.Round(p.Y, 1):F1}";
                                    if (!unique2d.ContainsKey(key))
                                    {
                                        unique2d[key] = p;
                                    }
                                }
                                dedup_precise = unique2d.Values.ToList();
                                Console.WriteLine($"[SVG] {name}: Keeping {dedup_precise.Count} unique vertices (deduped from {pts2d_precise.Count}) for complex shape");
                            }
                            else
                            {
                                // Simple shape: dedup consecutive points
                                dedup_precise = new List<(double X, double Y)>();
                                foreach (var p in pts2d_precise)
                                {
                                    if (dedup_precise.Count == 0 || 
                                        Math.Abs(dedup_precise.Last().X - p.Item1) > 0.01 || 
                                        Math.Abs(dedup_precise.Last().Y - p.Item2) > 0.01)
                                    {
                                        dedup_precise.Add(p);
                                    }
                                }
                            }
                            

                            if (dedup_precise.Count >= 4)
                            {
                                // Round to integers
                                var dedup = dedup_precise
                                    .Select(p => ((long)Math.Round(p.X), (long)Math.Round(p.Y)))
                                    .ToList();
                                
                                // Try orthogonal ordering first, fallback to using dedup order
                                // For complex shapes, skip sophisticated orderings (Gift Wrapping, Convex Hull)
                                // and trust the dedup order which comes from edge-loop traversal
                                var orthogonalSort = SortPerimeterVertices2D(dedup);
                                List<(long X, long Y)> finalDedup;
                                
                                if (faces.Count > 20)
                                {
                                    // Complex shape with edge-ordered vertices:
                                    // The dedup order typically preserves the sequential perimeter traversal from edge loops
                                    // Don't use Gift Wrapping or other perimeter ordering - it will collapse non-convex features
                                    finalDedup = dedup;
                                    Console.WriteLine($"[SVG] {name}: Using dedup order directly for edge-ordered vertices ({dedup.Count} points)");
                                }
                                else if (orthogonalSort.Count == dedup.Count)
                                {
                                    // Orthogonal sort succeeded - use it for axis-aligned shapes
                                    finalDedup = orthogonalSort;
                                    Console.WriteLine($"[SVG] {name}: Orthogonal sort succeeded");
                                }
                                else
                                {
                                    // Orthogonal sort failed - use dedup order
                                    finalDedup = dedup;
                                    Console.WriteLine($"[SVG] {name}: Orthogonal sort failed, using dedup order (preserves {dedup.Count} vertices)");
                                }
                                
                                var faceOutlinePath = BuildPerimeterPath(finalDedup);
                                Console.WriteLine($"[SVG] {name}: Generated outline from {dedup_precise.Count} outline points -> {finalDedup.Count} ordered");
                                svg.Path(faceOutlinePath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                
                                // Render holes
                                var outlineMinX = outlineNormalizedVerts.Min(v => v.X);
                                var outlineMinY = outlineNormalizedVerts.Min(v => v.Y);
                                
                                foreach (var holeVerts3D in holeLoops)
                                {
                                    RenderSingleHole(svg, holeVerts3D, normalizedVertices, outlineMinX, outlineMinY, rotMatrix1, rotMatrix2, faces.Count > 20);
                                }
                                
                                svg.EndGroup();
                                continue;
                            }
                            else
                            {
                                Console.WriteLine($"[SVG] {name}: After dedup only {dedup_precise.Count} points, skipping solid");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[SVG] {name}: Could not extract outer loop, got {outerLoopVerts.Count} vertices, skipping solid");
                        }
                    }

                    // NO FALLBACK: Skip solids where we can't extract proper geometry
                    Console.WriteLine($"[SVG] {name}: Could not extract geometry, skipping solid");
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
            
            // atan2 in SVG coords: 0° = right (+X), 90° = down (+Y), 180° = left (-Y), 270° = up (-X)
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
            double[,] rotMatrix1, double[,] rotMatrix2, bool isComplexShape = false)
        {
            // UNIFIED APPROACH: Extract holes from mainFace bounds if available
            // If mainFace has multiple bounds, those are the holes
            if (mainFace != null && mainFace.Bounds?.Count > 1)
            {
                Console.WriteLine($"[SVG] Rendering holes: Face has {mainFace.Bounds.Count} bounds");
                
                var (_, holeLoopsOriginal) = StepTopologyResolver.ExtractFaceWithHoles(mainFace, stepFile);
                
                foreach (var holeVertices3D in holeLoopsOriginal)
                {
                    RenderSingleHole(svg, holeVertices3D, normalizedVertices, outlineMinX, outlineMinY, rotMatrix1, rotMatrix2, isComplexShape);
                }
            }
        }

        private static void RenderSingleHole(SvgBuilder svg, List<(double X, double Y, double Z)> holeVertices3D,
            List<GeometryTransform.Vec3> normalizedVertices, double outlineMinX, double outlineMinY,
            double[,] rotMatrix1, double[,] rotMatrix2, bool isComplexShape = false)
        {
            Console.WriteLine($"[SVG] Processing hole with {holeVertices3D.Count} vertices in 3D");
            
            if (holeVertices3D.Count < 3)
            {
                Console.WriteLine($"[SVG] Hole skipped (only {holeVertices3D.Count} vertices)");
                return;
            }
            
            // Transform hole vertices using the same rotation matrices as outline vertices
            var holeNormalizedVerts = holeVertices3D
                .Select(v => {
                    var vec = new GeometryTransform.Vec3(v.Item1, v.Item2, v.Item3);
                    var rot1 = ApplyMatrix(vec, rotMatrix1);
                    
                    // For complex shapes, don't apply rotMatrix2 (thin faces may not align properly)
                    // For simple shapes, apply both rotations just like outline vertices
                    if (isComplexShape)
                    {
                        return rot1;
                    }
                    var rot2 = ApplyMatrix(rot1, rotMatrix2);
                    return rot2;
                })
                .ToList();
            
            if (holeNormalizedVerts.Count < 3)
            {
                Console.WriteLine($"[SVG] Hole skipped after transformation");
                return;
            }
        
            // Calculate bounding box in all 3 dimensions
            var hx = holeNormalizedVerts.Min(v => v.X);
            var hx2 = holeNormalizedVerts.Max(v => v.X);
            var hy = holeNormalizedVerts.Min(v => v.Y);
            var hy2 = holeNormalizedVerts.Max(v => v.Y);
            var hz = holeNormalizedVerts.Min(v => v.Z);
            var hz2 = holeNormalizedVerts.Max(v => v.Z);
            
            // Find the two dominant dimensions (the ones that form the hole rectangle)
            var dimX = hx2 - hx;
            var dimY = hy2 - hy;
            var dimZ = hz2 - hz;
            
            // ATTEMPTED: Only use X and Y dimensions
            // RESULT: Holes oriented along Z axis get zero height (KCBoxFlat case)
            // REASON: For a flat plate with Z-oriented holes, X and Y are single values
            // FIX: Find the two largest dimension deltas - those form the hole rectangle
            var dims = new[] { 
                ("X", dimX, hx, hx2),
                ("Y", dimY, hy, hy2),
                ("Z", dimZ, hz, hz2)
            }.OrderByDescending(d => d.Item2).ToList();
            
            var dim1 = dims[0]; // Largest dimension
            var dim2 = dims[1]; // Second largest dimension
            
            var holeW = (long)Math.Round(dim1.Item2);
            var holeH = (long)Math.Round(dim2.Item2);
            
            // Position: Use X and Y for position (convert to outline-relative coordinates)
            // Map position based on which dimension is which
            var holeX = (long)Math.Round(hx - outlineMinX);
            var holeY = (long)Math.Round(hy - outlineMinY);
            
            Console.WriteLine($"[SVG] Hole 3D bounds: X:[{hx:F1},{hx2:F1}] Y:[{hy:F1},{hy2:F1}] Z:[{hz:F1},{hz2:F1}], normalized: ({holeX},{holeY}) {holeW}x{holeH}");
            
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

        private static List<(long X, long Y)> OrderComplexPerimeter(List<(long X, long Y)> vertices)
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
