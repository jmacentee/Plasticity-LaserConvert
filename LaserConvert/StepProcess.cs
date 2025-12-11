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
                
                // Find MANIFOLD_SOLID_BREP entities or create pseudo-solids from faces
                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"Found {solids.Count} solids");
                
                if (solids.Count == 0)
                {
                    Console.WriteLine("No solids found in STEP file.");
                    return 0;
                }
                
                // Filter by thickness
                const double minThickness = 2.5;
                const double maxThickness = 10.0;  // Increased tolerance for rotated geometry
                
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
                
                // Generate SVG
                var svg = new SvgBuilder();
                
                foreach (var (name, faces, face1Idx, face2Idx, vertices, dimX, dimY, dimZ) in thinSolids)
                {
                    svg.BeginGroup(name);
                    
                    // Apply TWO rotations: align thin dim with Z, then align edge with X
                    var normalizedVertices = GeometryTransform.RotateAndNormalize(vertices);
                    
                    if (normalizedVertices.Count >= 4)
                    {
                        // Find the top face vertices (max Z after rotation - this should be the thin face)
                        var maxZ = normalizedVertices.Max(v => v.Z);
                        var minZ = normalizedVertices.Min(v => v.Z);
                        var zRange = Math.Abs(maxZ - minZ);
                        
                        // For complex shapes with many vertices, use all of them (they should all be on the thin face)
                        // For simple shapes, filter to just the top face
                        List<GeometryTransform.Vec3> topFaceVerts;
                        
                        if (normalizedVertices.Count > 20)
                        {
                            // Complex shape - use all vertices
                            topFaceVerts = normalizedVertices;
                            var minXc = topFaceVerts.Min(v => v.X);
                            var maxXc = topFaceVerts.Max(v => v.X);
                            var minYc = topFaceVerts.Min(v => v.Y);
                            var maxYc = topFaceVerts.Max(v => v.Y);
                            var minZc = topFaceVerts.Min(v => v.Z);
                            var maxZc = topFaceVerts.Max(v => v.Z);
                            Console.WriteLine($"[SVG] {name}: Complex shape with {normalizedVertices.Count} vertices");
                            Console.WriteLine($"[SVG]   After rotation - X:[{minXc:F1},{maxXc:F1}] Y:[{minYc:F1},{maxYc:F1}] Z:[{minZc:F1},{maxZc:F1}]");
                        }
                        else
                        {
                            // Simple shape - filter to top plane
                            var topCandidates = normalizedVertices
                                .Where(v => Math.Abs(v.Z - maxZ) < 1.5)
                                .ToList();
                            var bottomCandidates = normalizedVertices
                                .Where(v => Math.Abs(v.Z - minZ) < 1.5)
                                .ToList();
                            
                            topFaceVerts = (topCandidates.Count >= bottomCandidates.Count) ? topCandidates : bottomCandidates;
                            
                            Console.WriteLine($"[SVG] {name}: Z range [{minZ:F1}, {maxZ:F1}], top={topCandidates.Count} verts, bottom={bottomCandidates.Count} verts");
                        }
                        
                        if (topFaceVerts.Count >= 4)
                        {
                            Console.WriteLine($"[SVG] {name}: Found {topFaceVerts.Count} vertices on top face");
                            
                            // If we have more than 8 vertices (indicating complex shape with edge cutouts),
                            // extract the actual boundary path
                            if (topFaceVerts.Count > 8)
                            {
                                // For complex shapes, render using the two largest dimensions
                                // This handles cases where the shape ends up in different planes after rotation
                                var minXt = topFaceVerts.Min(v => v.X);
                                var maxXt = topFaceVerts.Max(v => v.X);
                                var minYt = topFaceVerts.Min(v => v.Y);
                                var maxYt = topFaceVerts.Max(v => v.Y);
                                var minZt = topFaceVerts.Min(v => v.Z);
                                var maxZt = topFaceVerts.Max(v => v.Z);
                                
                                var dX = maxXt - minXt;
                                var dY = maxYt - minYt;
                                var dZ = maxZt - minZt;
                                
                                Console.WriteLine($"[SVG] {name}: Dimensions - X:{dX:F1} Y:{dY:F1} Z:{dZ:F1}");
                                
                                // If the shape isn't primarily in X-Y plane, use Y-Z or X-Z instead
                                // by projecting to the plane with the two largest extents
                                List<GeometryTransform.Vec3> projectedVerts = topFaceVerts;
                                if (dZ > dX && dZ > dY)
                                {
                                    // Use Y-Z plane, swap Z→X for rendering
                                    projectedVerts = topFaceVerts
                                        .Select(v => new GeometryTransform.Vec3(v.Z, v.Y, v.X))
                                        .ToList();
                                    Console.WriteLine($"[SVG] {name}: Rendering in Y-Z plane (Z becomes X for output)");
                                }
                                else if (dZ > dX)
                                {
                                    // Use X-Z plane, swap Z→Y for rendering
                                    projectedVerts = topFaceVerts
                                        .Select(v => new GeometryTransform.Vec3(v.X, v.Z, v.Y))
                                        .ToList();
                                    Console.WriteLine($"[SVG] {name}: Rendering in X-Z plane (Z becomes Y for output)");
                                }
                                
                                var boundaryPath = ExtractBoundaryPath(projectedVerts, name);
                                if (!string.IsNullOrEmpty(boundaryPath))
                                {
                                    Console.WriteLine($"[SVG] {name}: Rendering complex boundary with {projectedVerts.Count} vertices");
                                    svg.Path(boundaryPath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                    // For complex shapes, try to detect interior holes (not edge-based cutouts)
                                    DetectAndRenderInteriorHolesOnly(svg, projectedVerts, name);
                                    svg.EndGroup();
                                    continue;
                                }
                            }
                            
                            // For simple shapes or shapes with holes, use bounding box
                            var minX = topFaceVerts.Min(v => v.X);
                            var maxX = topFaceVerts.Max(v => v.X);
                            var minY = topFaceVerts.Min(v => v.Y);
                            var maxY = topFaceVerts.Max(v => v.Y);
                            
                            var rectWidth = (long)Math.Round(maxX - minX);
                            var rectHeight = (long)Math.Round(maxY - minY);
                            
                            Console.WriteLine($"[SVG] {name}: Normalized top face bounds: {rectWidth} x {rectHeight}");
                            Console.WriteLine($"[SVG] {name}: Outer bounds X:[{minX:F1},{maxX:F1}] Y:[{minY:F1},{maxY:F1}]");
                            
                            // Outer boundary in BLACK (normalized to start at 0,0)
                            var pathData = $"M 0 0 L {rectWidth} 0 L {rectWidth} {rectHeight} L 0 {rectHeight} Z";
                            svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#000");
                            
                            // For simple shapes, look for holes by examining all normalized vertices
                            // Hole vertices will form a separate rectangular cluster inside the bounds
                            DetectAndRenderHolesFromVertices(svg, normalizedVertices, topFaceVerts, minX, minY, rectWidth, rectHeight);
                        }
                        else
                        {
                            Console.WriteLine($"{name}: Insufficient vertices for output ({topFaceVerts.Count} < 4)");
                        }
                    }
                    
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

        /// <summary>
        /// Compute bounding box in XY plane (ignoring Z).
        /// </summary>
        private static (double MinX, double MaxX, double MinY, double MaxY) ComputeBoundingBox(
            List<(double X, double Y, double Z)> vertices)
        {
            if (vertices.Count == 0)
                return (0, 0, 0, 0);
            
            var minX = vertices.Min(v => v.X);
            var maxX = vertices.Max(v => v.X);
            var minY = vertices.Min(v => v.Y);
            var maxY = vertices.Max(v => v.Y);
            
            return (minX, maxX, minY, maxY);
        }

        /// <summary>
        /// Project rotated face vertices to 2D SVG path by sorting them in order and normalizing to axis-aligned.
        /// Translates so minimum point is at (0,0) and rounds coordinates.
        /// </summary>
        private static string ProjectRotatedFaceToSvg(List<GeometryTransform.Vec3> faceVertices)
        {
            if (faceVertices.Count < 3)
                return "";
            
            // Find bounding box of all vertices
            var minX = faceVertices.Min(v => v.X);
            var maxX = faceVertices.Max(v => v.X);
            var minY = faceVertices.Min(v => v.Y);
            var maxY = faceVertices.Max(v => v.Y);
            
            var width = maxX - minX;
            var height = maxY - minY;
            
            // For axis-aligned output: create a rectangle from the bounding box
            // This works for rectangular faces (which we expect for laser cutting)
            var rectWidth = Math.Round(width);
            var rectHeight = Math.Round(height);
            
            Console.WriteLine($"[SVG] Normalized dimensions: {rectWidth} x {rectHeight}");
            
            // Create axis-aligned rectangle path starting at (0,0)
            var sb = new StringBuilder();
            sb.Append($"M 0 0 L {(long)rectWidth} 0 L {(long)rectWidth} {(long)rectHeight} L 0 {(long)rectHeight} Z");
            
            return sb.ToString();
        }

        /// <summary>
        /// Project a face's edge geometry to 2D SVG path data.
        /// Handles multiple loops (outer + holes/cutouts).
        /// </summary>
        private static string ProjectFaceToSvg(
            StepAdvancedFace face,
            List<(double X, double Y, double Z)> allVertices,
            StepFile stepFile)
        {
            if (face?.Bounds == null || face.Bounds.Count == 0)
                return "";
            
            // Build a 2D projection plane from the all vertices
            var frame = BuildProjectionFrame(allVertices);
            var sb = new StringBuilder();
            
            // Process each bound (loop) in the face
            // First bound is usually outer, rest are holes
            bool firstLoop = true;
            foreach (var bound in face.Bounds)
            {
                var loopPathData = ProjectLoopToSvg(bound, frame, stepFile);
                if (!string.IsNullOrEmpty(loopPathData))
                {
                    sb.Append(loopPathData);
                    if (!firstLoop)
                        sb.Append(" ");  // Separate multiple paths (holes)
                    firstLoop = false;
                }
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Project a single edge loop (face bound) to 2D SVG path.
        /// </summary>
        private static string ProjectLoopToSvg(StepFaceBound bound, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, StepFile stepFile)
        {
            // Get the edge loop from the bound
            var edgeLoop = GetEdgeLoopFromBound(bound);
            if (edgeLoop?.EdgeList == null || edgeLoop.EdgeList.Count == 0)
                return "";
            
            var sb = new StringBuilder();
            bool started = false;
            Vec2? lastPoint = null;
            
            // Process each oriented edge in the loop
            foreach (var orientedEdge in edgeLoop.EdgeList)
            {
                if (orientedEdge?.EdgeElement == null)
                    continue;
                
                var edgeCurve = orientedEdge.EdgeElement;
                
                // Extract start and end vertices
                StepVertexPoint startVertex = null;
                StepVertexPoint endVertex = null;
                
                // Try different property names for start/end
                var edgeProps = edgeCurve.GetType().GetProperties();
                foreach (var prop in edgeProps)
                {
                    if (prop.Name.Contains("EdgeStart") || prop.Name.Contains("Start"))
                        startVertex = prop.GetValue(edgeCurve) as StepVertexPoint;
                    if (prop.Name.Contains("EdgeEnd") || prop.Name.Contains("End"))
                        endVertex = prop.GetValue(edgeCurve) as StepVertexPoint;
                }
                
                // Project start point
                if (startVertex?.Location != null)
                {
                    var start3D = new Vec3(startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z);
                    var start2D = Project(frame, start3D);
                    
                    // Skip if same as last point (degenerate edge)
                    if (lastPoint == null || Distance(lastPoint.Value, start2D) > 0.01)
                    {
                        if (!started)
                        {
                            sb.Append($"M {Fmt(start2D.X)} {Fmt(start2D.Y)} ");
                            started = true;
                        }
                        lastPoint = start2D;
                    }
                }
                
                // Project end point
                if (endVertex?.Location != null)
                {
                    var end3D = new Vec3(endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z);
                    var end2D = Project(frame, end3D);
                    
                    // Skip if same as last point (degenerate edge)
                    if (lastPoint == null || Distance(lastPoint.Value, end2D) > 0.01)
                    {
                        sb.Append($"L {Fmt(end2D.X)} {Fmt(end2D.Y)} ");
                        lastPoint = end2D;
                    }
                }
            }
            
            if (started)
                sb.Append("Z");  // Close the path
            
            return sb.ToString();
        }

        private static double Distance(Vec2 a, Vec2 b)
        {
            return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }

        /// <summary>
        /// Build a 3D→2D projection frame from vertex cloud.
        /// </summary>
        private static (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) BuildProjectionFrame(List<(double X, double Y, double Z)> vertices)
        {
            if (vertices.Count < 3)
                return (new Vec3(0, 0, 0), new Vec3(0, 0, 1), new Vec3(1, 0, 0), new Vec3(0, 1, 0));
            
            // Find origin as centroid
            var origin = new Vec3(
                vertices.Average(v => v.X),
                vertices.Average(v => v.Y),
                vertices.Average(v => v.Z)
            );
            
            // Find two non-collinear vectors
            var v0 = new Vec3(vertices[0].X, vertices[0].Y, vertices[0].Z) - origin;
            var v1 = new Vec3(0, 0, 0);
            
            for (int i = 1; i < vertices.Count; i++)
            {
                v1 = new Vec3(vertices[i].X, vertices[i].Y, vertices[i].Z) - origin;
                if (v1.Length > 0.01)
                    break;
            }
            
            // Compute normal as cross product
            var normal = Normalize(Cross(v0, v1));
            
            // Build orthonormal basis
            var u = Normalize(v0);
            var v = Normalize(Cross(normal, u));
            
            return (origin, normal, u, v);
        }

        /// <summary>
        /// Project a 3D point onto the 2D frame.
        /// </summary>
        private static Vec2 Project((Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, Vec3 point)
        {
            var rel = point - frame.Origin;
            return new Vec2(Dot(rel, frame.U), Dot(rel, frame.V));
        }

        /// <summary>
        /// Get edge loop from a face bound using reflection.
        /// </summary>
        private static StepEdgeLoop GetEdgeLoopFromBound(StepFaceBound bound)
        {
            var props = bound.GetType().GetProperties();
            foreach (var prop in props)
            {
                if (prop.PropertyType == typeof(StepEdgeLoop) || 
                    prop.PropertyType.Name.Contains("Loop"))
                {
                    return prop.GetValue(bound) as StepEdgeLoop;
                }
            }
            return null;
        }

        // =--------- Vector Math =---------

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

        private static string Fmt(double d)
            => d.ToString("0.###", CultureInfo.InvariantCulture);

        // ========== Helper Types ==========

        private record Dimensions(double Width, double Height, double Depth)
        {
            public bool HasThinDimension(double minThickness, double maxThickness)
            {
                var sorted = new[] { Width, Height, Depth }.OrderBy(d => d).ToList();
                // Check if the SMALLEST dimension is in the thin range
                // This is rotation-independent
                var smallestDim = sorted[0];
                var hasSmallDim = smallestDim >= minThickness && smallestDim <= maxThickness;
                
                // Also accept if we have one dimension in thin range and the other two are much larger
                // (accounts for measurement/rounding errors in rotated geometry)
                var thinDims = sorted.Where(d => d >= minThickness && d <= maxThickness).Count();
                if (thinDims > 0)
                {
                    // We have at least one dimension in the thin range
                    return true;
                }
                
                return hasSmallDim;
            }
            public override string ToString() => $"[{Width:F1}, {Height:F1}, {Depth:F1}]";
        }

        // ========== SVG Builder ==========

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
                _sb.AppendLine($"    <path d=\"{d}\" stroke=\"{stroke}\" stroke-width=\"{Fmt(strokeWidth)}\" fill=\"{fill}\" vector-effect=\"non-scaling-stroke\"/>");
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

        private static void DetectAndRenderCutouts(SvgBuilder svg, List<StepAdvancedFace> faces, List<GeometryTransform.Vec3> normalizedVertices, StepFile stepFile, double outerMinX = double.NaN, double outerMinY = double.NaN)
        {
            // Find all vertices at the top/bottom Z plane
            var maxZ = normalizedVertices.Max(v => v.Z);
            var minZ = normalizedVertices.Min(v => v.Z);
            var zRange = Math.Abs(maxZ - minZ);
            var zThreshold = Math.Max(0.5, zRange * 0.02);
            
            var maxZverts = normalizedVertices.Where(v => Math.Abs(v.Z - maxZ) < zThreshold).Distinct().ToList();
            var minZverts = normalizedVertices.Where(v => Math.Abs(v.Z - minZ) < zThreshold).Distinct().ToList();
            
            var topVerts = (maxZverts.Count >= minZverts.Count) ? maxZverts : minZverts;
            
            Console.WriteLine($"[SVG] Total vertices: {normalizedVertices.Count}, Top plane vertices: {topVerts.Count}");
            
            if (topVerts.Count < 8)
            {
                Console.WriteLine($"[SVG] Insufficient top vertices ({topVerts.Count} < 8), no cutouts detected");
                return;
            }
            
            var centroidX = topVerts.Average(v => v.X);
            var centroidY = topVerts.Average(v => v.Y);
            
            // Find vertices by distance from centroid
            var distFromCenter = topVerts
                .Select(v => new
                {
                    V = v,
                    Dist = Math.Sqrt((v.X - centroidX) * (v.X - centroidX) + (v.Y - centroidY) * (v.Y - centroidY))
                })
                .ToList();
            
            var avgDist = distFromCenter.Average(d => d.Dist);
            var outerVerts = distFromCenter.Where(d => d.Dist >= avgDist * 0.8).Select(d => d.V).ToList();
            var innerVerts = distFromCenter.Where(d => d.Dist < avgDist * 0.8).Select(d => d.V).ToList();
            
            Console.WriteLine($"[SVG] Outer verts: {outerVerts.Count}, Inner verts: {innerVerts.Count}, Avg dist from center: {avgDist:F1}");
            
            if (outerVerts.Count > 0)
            {
                Console.WriteLine($"[SVG] Outer X range: [{outerVerts.Min(v => v.X):F1}, {outerVerts.Max(v => v.X):F1}], Y range: [{outerVerts.Min(v => v.Y):F1}, {outerVerts.Max(v => v.Y):F1}]");
            }
            if (innerVerts.Count > 0)
            {
                Console.WriteLine($"[SVG] Inner X range: [{innerVerts.Min(v => v.X):F1}, {innerVerts.Max(v => v.X):F1}], Y range: [{innerVerts.Min(v => v.Y):F1}, {innerVerts.Max(v => v.Y):F1}]");
            }
            
            // If we have potential hole vertices, render them as RED
            if (innerVerts.Count >= 4)
            {
                var holeMinX = innerVerts.Min(v => v.X);
                var holeMaxX = innerVerts.Max(v => v.X);
                var holeMinY = innerVerts.Min(v => v.Y);
                var holeMaxY = innerVerts.Max(v => v.Y);
                
                var holeW = (long)Math.Round(holeMaxX - holeMinX);
                var holeH = (long)Math.Round(holeMaxY - holeMinY);
                
                // Check if this looks like a rectangular hole (minimum size)
                if (holeW > 2 && holeH > 2)  // Minimum hole size
                {
                    long holeX, holeY;
                    
                    if (!double.IsNaN(outerMinX) && !double.IsNaN(outerMinY))
                    {
                        // We have explicit outer bounds - normalize relative to them
                        holeX = (long)Math.Round(holeMinX - outerMinX);
                        holeY = (long)Math.Round(holeMinY - outerMinY);
                        
                        // For holes, the position might be flipped depending on rotation direction
                        var actualHoleX = holeX;
                        var outerWidth = (long)Math.Round(outerVerts.Max(v => v.X) - outerMinX);
                        if (holeX > outerWidth / 2)
                        {
                            // Hole is on the right side, flip it to the left
                            actualHoleX = outerWidth - holeX - holeW;
                        }
                        
                        var pathData = $"M {actualHoleX} {holeY} L {actualHoleX + holeW} {holeY} L {actualHoleX + holeW} {holeY + holeH} L {actualHoleX} {holeY + holeH} Z";
                        svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                        Console.WriteLine($"[SVG] Rendered RED hole: {holeW} x {holeH} at ({actualHoleX}, {holeY})");
                    }
                    else if (outerVerts.Count > 0)
                    {
                        // No explicit outer bounds - use outer verts bounds for normalization
                        var outerMinXc = outerVerts.Min(v => v.X);
                        var outerMinYc = outerVerts.Min(v => v.Y);
                        
                        holeX = (long)Math.Round(holeMinX - outerMinXc);
                        holeY = (long)Math.Round(holeMinY - outerMinYc);
                        
                        var pathData = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                        svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                        Console.WriteLine($"[SVG] Rendered RED hole: {holeW} x {holeH} at ({holeX}, {holeY})");
                    }
                }
            }
        }
        
        /// <summary>
        /// Extract the actual boundary path from vertices.
        /// For complex shapes, build edges from the vertex pairs and trace the perimeter.
        /// </summary>
        private static string ExtractBoundaryPath(List<GeometryTransform.Vec3> vertices, string name)
        {
            if (vertices.Count < 4)
                return null;
            
            // Round all vertices
            var roundedVerts = new List<(long X, long Y)>();
            for (int i = 0; i < vertices.Count; i++)
            {
                roundedVerts.Add(((long)Math.Round(vertices[i].X), (long)Math.Round(vertices[i].Y)));
            }
            
            if (roundedVerts.Count < 4)
                return null;
            
            // Normalize to start at (0, 0)
            var minX = roundedVerts.Min(v => v.X);
            var minY = roundedVerts.Min(v => v.Y);
            
            var normalizedVerts = roundedVerts
                .Select(v => (v.X - minX, v.Y - minY))
                .ToList();
            
            // Find the correct perimeter order by starting at bottom-left and following the boundary
            var ordered = new List<(long X, long Y)>();
            var remaining = new HashSet<(long, long)>(normalizedVerts);
            
            // Start at bottom-left corner
            var current = remaining.OrderBy(v => (v.Item2, v.Item1)).First();
            ordered.Add(current);
            remaining.Remove(current);
            
            // Trace the perimeter using greedy nearest-neighbor
            while (remaining.Count > 0)
            {
                var neighbors = remaining
                    .Where(v => v.Item1 == current.Item1 || v.Item2 == current.Item2)
                    .ToList();
                
                if (neighbors.Count == 0)
                    break;  // Can't find next vertex
                
                // Choose the closest neighbor
                var next = neighbors.OrderBy(v => 
                    Math.Abs(v.Item1 - current.Item1) + Math.Abs(v.Item2 - current.Item2))
                    .First();
                
                ordered.Add(next);
                remaining.Remove(next);
                current = next;
            }
            
            // Build SVG path
            var sb = new StringBuilder();
            sb.Append($"M {ordered[0].X},{ordered[0].Y}");
            
            for (int i = 1; i < ordered.Count; i++)
            {
                sb.Append($" L {ordered[i].X},{ordered[i].Y}");
            }
            
            sb.Append(" Z");
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Detect and render potential interior holes (not cutouts on the edge) for complex shapes.
        /// This attempts to find clusters of vertices that form rectangular holes inside the shape.
        /// </summary>
        private static void DetectAndRenderInteriorHolesOnly(SvgBuilder svg, List<GeometryTransform.Vec3> projectedVerts, string name)
        {
            if (projectedVerts.Count < 12)
            {
                Console.WriteLine($"[SVG] {name}: Insufficient vertices for hole detection ({projectedVerts.Count} < 12)");
                return;
            }
            
            // Find bounding box of all vertices
            var minX = projectedVerts.Min(v => v.X);
            var maxX = projectedVerts.Max(v => v.X);
            var minY = projectedVerts.Min(v => v.Y);
            var maxY = projectedVerts.Max(v => v.Y);
            
            var width = maxX - minX;
            var height = maxY - minY;
            var minDim = Math.Min(width, height);
            
            // Centroid of all vertices
            var centroidX = projectedVerts.Average(v => v.X);
            var centroidY = projectedVerts.Average(v => v.Y);
            
            // Find vertices by distance from centroid
            var distFromCenter = projectedVerts
                .Select(v => new
                {
                    V = v,
                    Dist = Math.Sqrt((v.X - centroidX) * (v.X - centroidX) + (v.Y - centroidY) * (v.Y - centroidY))
                })
                .ToList();
            
            var avgDist = distFromCenter.Average(d => d.Dist);
            var outerVerts = distFromCenter.Where(d => d.Dist >= avgDist * 0.75).Select(d => d.V).ToList();
            var innerVerts = distFromCenter.Where(d => d.Dist < avgDist * 0.75).Select(d => d.V).ToList();
            
            Console.WriteLine($"[SVG] {name}: Outer verts: {outerVerts.Count}, Inner verts: {innerVerts.Count}, Avg dist: {avgDist:F1}");
            
            if (innerVerts.Count < 4)
            {
                Console.WriteLine($"[SVG] {name}: Insufficient inner vertices for holes");
                return;
            }
            
            // Cluster inner vertices into separate holes using agglomerative clustering
            var holeClusters = ClusterInnerVertices(innerVerts, minDim * 0.25);  // Increased from 0.10
            
            if (holeClusters.Count == 0)
            {
                Console.WriteLine($"[SVG] {name}: No hole clusters found");
                return;
            }
            
            Console.WriteLine($"[SVG] {name}: Found {holeClusters.Count} hole cluster(s)");
            
            var outerMinXc = outerVerts.Min(v => v.X);
            var outerMinYc = outerVerts.Min(v => v.Y);
            
            int holeIndex = 0;
            foreach (var holeVerts in holeClusters)
            {
                holeIndex++;
                var holeMinX = holeVerts.Min(v => v.X);
                var holeMaxX = holeVerts.Max(v => v.X);
                var holeMinY = holeVerts.Min(v => v.Y);
                var holeMaxY = holeVerts.Max(v => v.Y);
                
                // Distance from hole to each edge
                var distToLeftEdge = holeMinX - minX;
                var distToRightEdge = maxX - holeMaxX;
                var distToTopEdge = maxY - holeMaxY;
                var distToBottomEdge = holeMinY - minY;
                
                var minDistToEdge = Math.Min(Math.Min(distToLeftEdge, distToRightEdge), 
                                             Math.Min(distToTopEdge, distToBottomEdge));
                
                // Only treat as a hole if it's well-separated from the edges
                if (minDistToEdge < minDim * 0.10)  // Less than 10% away from edge
                {
                    Console.WriteLine($"[SVG] {name}: Hole #{holeIndex} too close to edges ({minDistToEdge:F1}), skipping");
                    continue;
                }
                
                var holeW = (long)Math.Round(holeMaxX - holeMinX);
                var holeH = (long)Math.Round(holeMaxY - holeMinY);
                
                if (holeW > 2 && holeH > 2)
                {
                    var holeX = (long)Math.Round(holeMinX - outerMinXc);
                    var holeY = (long)Math.Round(holeMinY - outerMinYc);
                    
                    var pathData = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                    svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                    Console.WriteLine($"[SVG] {name}: Rendered RED hole #{holeIndex}: {holeW} x {holeH} at ({holeX}, {holeY})");
                }
            }
        }
        
        /// <summary>
        /// Cluster vertices spatially using a simple agglomerative approach.
        /// Finds groups of vertices that are close together.
        /// </summary>
        private static List<List<GeometryTransform.Vec3>> ClusterInnerVertices(List<GeometryTransform.Vec3> innerVerts, double clusterDistance)
        {
            if (innerVerts.Count < 4)
                return new List<List<GeometryTransform.Vec3>>();
            
            var clusters = new List<List<GeometryTransform.Vec3>>();
            var visited = new HashSet<int>();
            
            // Start clustering from each unvisited vertex
            for (int i = 0; i < innerVerts.Count; i++)
            {
                if (visited.Contains(i))
                    continue;
                
                // Start a new cluster
                var cluster = new List<GeometryTransform.Vec3> { innerVerts[i] };
                visited.Add(i);
                
                // Grow the cluster by adding nearby vertices
                bool expanded = true;
                while (expanded)
                {
                    expanded = false;
                    for (int j = 0; j < innerVerts.Count; j++)
                    {
                        if (visited.Contains(j))
                            continue;
                        
                        // Check if this vertex is close to ANY vertex in the cluster
                        var closestDistInCluster = cluster.Min(cv => 
                            Math.Sqrt((innerVerts[j].X - cv.X) * (innerVerts[j].X - cv.X) + 
                                     (innerVerts[j].Y - cv.Y) * (innerVerts[j].Y - cv.Y)));
                        
                        if (closestDistInCluster <= clusterDistance)
                        {
                            cluster.Add(innerVerts[j]);
                            visited.Add(j);
                            expanded = true;
                        }
                    }
                }
                
                // Only keep clusters with minimum size
                if (cluster.Count >= 3)
                {
                    clusters.Add(cluster);
                }
            }
            
            return clusters;
        }

        /// <summary>
        /// For simple shapes, detect holes by looking at all faces and finding interior ones.
        /// A hole face will have 4 vertices that form a rectangle inside the main boundary.
        /// </summary>
        private static void DetectAndRenderHolesFromSeparateFaces(
            SvgBuilder svg,
            List<StepAdvancedFace> faces,
            double outerMinX, double outerMinY,
            long outerWidth, long outerHeight,
            StepFile stepFile)
        {
            // Skip if we don't have multiple faces
            if (faces.Count < 2)
                return;
            
            // For simple shapes, look for faces that are much smaller than the main body
            // These are likely hole faces
            foreach (var face in faces)
            {
                var faceVerts = StepTopologyResolver.ExtractVerticesFromFace(face, stepFile);
                
                if (faceVerts.Count < 4 || faceVerts.Count > 8)
                    continue;  // Holes typically have 4-8 vertices (rectangular)
                
                // Rotate this face using the same transformation
                var rotatedFaceVerts = GeometryTransform.RotateAndNormalize(faceVerts);
                
                if (rotatedFaceVerts.Count < 4)
                    continue;
                
                // Compute bounds of this face in XY
                var faceMinX = rotatedFaceVerts.Min(v => v.X);
                var faceMaxX = rotatedFaceVerts.Max(v => v.X);
                var faceMinY = rotatedFaceVerts.Min(v => v.Y);
                var faceMaxY = rotatedFaceVerts.Max(v => v.Y);
                var faceWidth = faceMaxX - faceMinX;
                var faceHeight = faceMaxY - faceMinY;
                
                // Skip if face is not significantly smaller than outer bounds  
                if (faceWidth >= outerWidth * 0.9 || faceHeight >= outerHeight * 0.9)
                    continue;  // This is probably the main body, not a hole
                
                // Skip very small faces
                if (faceWidth < 1 || faceHeight < 1)
                    continue;
                
                // Check if this face is inside the outer bounds
                bool isInside = (faceMinX >= outerMinX && faceMaxX <= outerMinX + outerWidth &&
                                faceMinY >= outerMinY && faceMaxY <= outerMinY + outerHeight);
                
                if (isInside)
                {
                    // This looks like a hole!
                    var holeX = (long)Math.Round(faceMinX - outerMinX);
                    var holeY = (long)Math.Round(faceMinY - outerMinY);
                    var holeW = (long)Math.Round(faceWidth);
                    var holeH = (long)Math.Round(faceHeight);
                    
                    if (holeW > 2 && holeH > 2)  // Minimum hole size
                    {
                        var pathData = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                        svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                        Console.WriteLine($"[SVG] Rendered RED hole: {holeW} x {holeH} at ({holeX}, {holeY})");
                    }
                }
            }
        }
        
        /// <summary>
        /// For simple shapes, detect holes by examining all normalized vertices.
        /// Hole vertices are those that are NOT part of the top face boundary but are inside its bounds.
        /// </summary>
        private static void DetectAndRenderHolesFromVertices(
            SvgBuilder svg,
            List<GeometryTransform.Vec3> allNormalizedVertices,
            List<GeometryTransform.Vec3> topFaceVertices,
            double outerMinX, double outerMinY,
            long outerWidth, long outerHeight)
        {
            Console.WriteLine($"[HOLE DEBUG] Total vertices: {allNormalizedVertices.Count}, Top face verts: {topFaceVertices.Count}");
            
            // Find vertices that are NOT in the top face (these are hole vertices)
            var topFaceSet = new HashSet<(double, double)>();
            foreach (var v in topFaceVertices)
            {
                topFaceSet.Add((Math.Round(v.X, 1), Math.Round(v.Y, 1)));
            }
            
            Console.WriteLine($"[HOLE DEBUG] Top face set size: {topFaceSet.Count}");
            
            var holeVertices = new List<GeometryTransform.Vec3>();
            foreach (var v in allNormalizedVertices)
            {
                if (!topFaceSet.Contains((Math.Round(v.X, 1), Math.Round(v.Y, 1))))
                {
                    // This vertex is not part of the main boundary
                    // Check if it's inside the bounds
                    if (v.X >= outerMinX && v.X <= outerMinX + outerWidth &&
                        v.Y >= outerMinY && v.Y <= outerMinY + outerHeight)
                    {
                        holeVertices.Add(v);
                    }
                }
            }
            
            Console.WriteLine($"[HOLE DEBUG] Found {holeVertices.Count} hole vertices");
            
            if (holeVertices.Count < 4)
            {
                return;  // Not enough vertices for a hole
            }
            
            // Compute bounding box of hole vertices
            var holeMinX = holeVertices.Min(v => v.X);
            var holeMaxX = holeVertices.Max(v => v.X);
            var holeMinY = holeVertices.Min(v => v.Y);
            var holeMaxY = holeVertices.Max(v => v.Y);
            
            var holeW = (long)Math.Round(holeMaxX - holeMinX);
            var holeH = (long)Math.Round(holeMaxY - holeMinY);
            
            Console.WriteLine($"[HOLE DEBUG] Hole bounds: {holeW} x {holeH}");
            
            if (holeW > 2 && holeH > 2)  // Minimum hole size
            {
                var holeX = (long)Math.Round(holeMinX - outerMinX);
                var holeY = (long)Math.Round(holeMinY - outerMinY);
                
                var pathData = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                Console.WriteLine($"[SVG] Rendered RED hole: {holeW} x {holeH} at ({holeX}, {holeY})");
            }
            else
            {
                Console.WriteLine($"[HOLE DEBUG] Hole too small: {holeW} x {holeH}");
            }
        }
    }
}
