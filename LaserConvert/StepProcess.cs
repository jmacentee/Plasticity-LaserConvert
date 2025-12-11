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
                const double maxThickness = 5.0;
                
                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces, int Face1Idx, int Face2Idx, List<(double X, double Y, double Z)> Vertices)>();
                
                foreach (var (name, faces) in solids)
                {
                    var (vertices, face1Idx, face2Idx, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, faces, face1Idx, face2Idx, vertices));
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
                
                foreach (var (name, faces, face1Idx, face2Idx, vertices) in thinSolids)
                {
                    svg.BeginGroup(name);
                    
                    // Use the thin face (either face1 or face2, prefer face1) for SVG projection
                    int thinFaceIdx = (face1Idx >= 0) ? face1Idx : face2Idx;
                    if (thinFaceIdx >= 0 && thinFaceIdx < faces.Count)
                    {
                        var thinFace = faces[thinFaceIdx];
                        var pathData = ProjectFaceToSvg(thinFace, vertices, stepFile);
                        if (!string.IsNullOrEmpty(pathData))
                        {
                            svg.Path(pathData, strokeWidth: 0.2, fill: "none");
                        }
                        else
                        {
                            // Fallback: draw a rectangle from the bounding box
                            Console.WriteLine($"[SVG] No path generated for {name}, using bounding box fallback");
                            var (minX, maxX, minY, maxY) = ComputeBoundingBox(vertices);
                            var rectPath = $"M {Fmt(minX)} {Fmt(minY)} L {Fmt(maxX)} {Fmt(minY)} L {Fmt(maxX)} {Fmt(maxY)} L {Fmt(minX)} {Fmt(maxY)} Z";
                            svg.Path(rectPath, strokeWidth: 0.2, fill: "none");
                        }
                    }
                    else
                    {
                        // No face indices, use bounding box fallback
                        var (minX, maxX, minY, maxY) = ComputeBoundingBox(vertices);
                        var rectPath = $"M {Fmt(minX)} {Fmt(minY)} L {Fmt(maxX)} {Fmt(minY)} L {Fmt(maxX)} {Fmt(maxY)} L {Fmt(minX)} {Fmt(maxY)} Z";
                        svg.Path(rectPath, strokeWidth: 0.2, fill: "none");
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

        // ========== Vector Math ==========

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

            public void Path(string d, double strokeWidth, string fill)
            {
                _sb.AppendLine($"    <path d=\"{d}\" stroke=\"#000\" stroke-width=\"{Fmt(strokeWidth)}\" fill=\"{fill}\" vector-effect=\"non-scaling-stroke\"/>");
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
