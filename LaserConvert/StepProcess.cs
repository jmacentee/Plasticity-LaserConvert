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

                    // Rotate vertices to align thin dimension with Z and normalize one edge to X
                    var normalizedVertices = GeometryTransform.RotateAndNormalize(vertices);
                    if (normalizedVertices.Count < 4)
                    {
                        svg.EndGroup();
                        continue;
                    }

                    var maxZ = normalizedVertices.Max(v => v.Z);
                    var minZ = normalizedVertices.Min(v => v.Z);
                    var topPlaneVerts = normalizedVertices.Where(v => Math.Abs(v.Z - maxZ) < 1.0).ToList();
                    var bottomPlaneVerts = normalizedVertices.Where(v => Math.Abs(v.Z - minZ) < 1.0).ToList();
                    var topFaceVerts = (topPlaneVerts.Count >= bottomPlaneVerts.Count) ? topPlaneVerts : bottomPlaneVerts;

                    Console.WriteLine($"[SVG] {name}: Z range [{minZ:F1}, {maxZ:F1}], top={topPlaneVerts.Count} verts, bottom={bottomPlaneVerts.Count} verts");
                    Console.WriteLine($"[SVG] {name}: Found {topFaceVerts.Count} vertices on top face");

                    // If we have a usable thin face index, prefer using its bounds for outline/holes
                    StepAdvancedFace mainFace = null;
                    if (face1Idx >= 0 && face1Idx < faces.Count)
                    {
                        mainFace = faces[face1Idx];
                    }
                    else if (face2Idx >= 0 && face2Idx < faces.Count)
                    {
                        mainFace = faces[face2Idx];
                    }

                    bool complex = topFaceVerts.Count > 8 || (mainFace?.Bounds?.Count ?? 0) > 0;
                    if (mainFace != null && complex)
                    {
                        // Build projection frame from all vertices of the main face
                        var mainFaceAllVerts = StepTopologyResolver.ExtractVerticesFromFace(mainFace, stepFile);
                        var frame = BuildProjectionFrame(mainFaceAllVerts);
                        
                        // If single bound and likely rectangular, prefer normalized top-face bbox from rotated vertices
                        if ((mainFace.Bounds?.Count ?? 0) == 1)
                        {
                            var maxZnf = normalizedVertices.Max(v => v.Z);
                            var topVertsNf = normalizedVertices.Where(v => Math.Abs(v.Z - maxZnf) < 1.0).ToList();
                            if (topVertsNf.Count >= 4)
                            {
                                var minXn = topVertsNf.Min(v => v.X);
                                var maxXn = topVertsNf.Max(v => v.X);
                                var minYn = topVertsNf.Min(v => v.Y);
                                var maxYn = topVertsNf.Max(v => v.Y);
                                var w = (long)Math.Round(maxXn - minXn);
                                var h = (long)Math.Round(maxYn - minYn);
                                if (w < h)
                                {
                                    // Prefer longer side along X-axis
                                    var tmp = w;
                                    w = h;
                                    h = tmp;
                                }
                                var rectPath = $"M 0 0 L {w} 0 L {w} {h} L 0 {h} Z";
                                svg.Path(rectPath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                svg.EndGroup();
                                continue;
                            }
                        }
                        
                        // Helper: project a bound to ordered 2D points
                        List<(double X, double Y)> ProjectBoundPoints(StepFaceBound b)
                        {
                            var edgeLoop = GetEdgeLoopFromBound(b);
                            var pts2D = new List<(double, double)>();
                            if (edgeLoop?.EdgeList == null) return pts2D;
                            foreach (var orientedEdge in edgeLoop.EdgeList)
                            {
                                var edgeCurve = orientedEdge?.EdgeElement;
                                if (edgeCurve == null) continue;
                                StepVertexPoint startVertex = null, endVertex = null;
                                foreach (var prop in edgeCurve.GetType().GetProperties())
                                {
                                    if (prop.Name.Contains("EdgeStart") || prop.Name.Contains("Start"))
                                        startVertex = prop.GetValue(edgeCurve) as StepVertexPoint;
                                    if (prop.Name.Contains("EdgeEnd") || prop.Name.Contains("End"))
                                        endVertex = prop.GetValue(edgeCurve) as StepVertexPoint;
                                }
                                if (startVertex?.Location != null)
                                {
                                    var s3 = new Vec3(startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z);
                                    var s2 = Project(frame, s3);
                                    pts2D.Add((s2.X, s2.Y));
                                }
                                if (endVertex?.Location != null)
                                {
                                    var e3 = new Vec3(endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z);
                                    var e2 = Project(frame, e3);
                                    pts2D.Add((e2.X, e2.Y));
                                }
                            }
                            return pts2D;
                        }
                        
                        // Project outer and holes
                        var outerPts = ProjectBoundPoints(mainFace.Bounds[0]);
                        if (outerPts.Count >= 4)
                        {
                            // Normalize: translate so min x/y becomes 0,0; round to whole mm
                            var minOX = outerPts.Min(p => p.X);
                            var minOY = outerPts.Min(p => p.Y);
                            var normOuter = outerPts.Select(p => ((long)Math.Round(p.X - minOX), (long)Math.Round(p.Y - minOY))).ToList();

                            // If single bound and rectangle, render clean bbox path
                            if (mainFace.Bounds.Count == 1)
                            {
                                var xs = normOuter.Select(p => p.Item1).ToList();
                                var ys = normOuter.Select(p => p.Item2).ToList();
                                var minXr = xs.Min();
                                var maxXr = xs.Max();
                                var minYr = ys.Min();
                                var maxYr = ys.Max();
                                var uniqueCorners = new HashSet<(long, long)>(normOuter);
                                if (uniqueCorners.Count == 4 && minXr == 0 && minYr == 0)
                                {
                                    var rectPath = $"M 0 0 L {maxXr} 0 L {maxXr} {maxYr} L 0 {maxYr} Z";
                                    svg.Path(rectPath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                                    svg.EndGroup();
                                    continue;
                                }
                            }

                            // Build path following point order; snap near-axis segments
                            string BuildPath(List<(long X, long Y)> pts)
                            {
                                var sbp = new StringBuilder();
                                if (pts.Count == 0) return string.Empty;
                                sbp.Append($"M {pts[0].X},{pts[0].Y}");
                                for (int i = 1; i < pts.Count; i++)
                                {
                                    var a = pts[i - 1];
                                    var b = pts[i];
                                    if (a.X == b.X || a.Y == b.Y)
                                    {
                                        sbp.Append($" L {b.X},{b.Y}");
                                    }
                                    else
                                    {
                                        var dx = Math.Abs(b.X - a.X);
                                        var dy = Math.Abs(b.Y - a.Y);
                                        if (dx >= dy)
                                        {
                                            sbp.Append($" L {b.X},{a.Y}");
                                            sbp.Append($" L {b.X},{b.Y}");
                                        }
                                        else
                                        {
                                            sbp.Append($" L {a.X},{b.Y}");
                                            sbp.Append($" L {b.X},{b.Y}");
                                        }
                                    }
                                }
                                sbp.Append(" Z");
                                return sbp.ToString();
                            }
                            var outerPath = BuildPath(normOuter);
                            svg.Path(outerPath, strokeWidth: 0.2, fill: "none", stroke: "#000");

                            // Holes: subsequent bounds
                            for (int bi = 1; bi < mainFace.Bounds.Count; bi++)
                            {
                                var holePts = ProjectBoundPoints(mainFace.Bounds[bi]);
                                if (holePts.Count < 3) continue;
                                var normHole = holePts.Select(p => ((long)Math.Round(p.X - minOX), (long)Math.Round(p.Y - minOY))).ToList();
                                var holePath = BuildPath(normHole);
                                svg.Path(holePath, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                            }

                            svg.EndGroup();
                            continue;
                        }
                    }

                    // Fallback: rectangular bounds for simple faces
                    var minX = topFaceVerts.Min(v => v.X);
                    var maxX = topFaceVerts.Max(v => v.X);
                    var minY = topFaceVerts.Min(v => v.Y);
                    var maxY = topFaceVerts.Max(v => v.Y);

                    var rectWidth = (long)Math.Round(maxX - minX);
                    var rectHeight = (long)Math.Round(maxY - minY);
                    Console.WriteLine($"[SVG] {name}: Normalized top face bounds: {rectWidth} x {rectHeight}");
                    Console.WriteLine($"[SVG] {name}: Outer bounds X:[{minX:F1},{maxX:F1}] Y:[{minY:F1},{maxY:F1}]");

                    var pathData = $"M 0 0 L {rectWidth} 0 L {rectWidth} {rectHeight} L 0 {rectHeight} Z";
                    svg.Path(pathData, strokeWidth: 0.2, fill: "none", stroke: "#000");

                    // Render holes using face bounds of the selected main face
                    if (mainFace != null)
                    {
                        var (_, holeLoops) = StepTopologyResolver.ExtractFaceWithHoles(mainFace, stepFile);
                        foreach (var hole3D in holeLoops)
                        {
                            var holeRotNorm = GeometryTransform.RotateAndNormalize(hole3D);
                            if (holeRotNorm.Count < 3) continue;
                            var hx = holeRotNorm.Min(v => v.X);
                            var hx2 = holeRotNorm.Max(v => v.X);
                            var hy = holeRotNorm.Min(v => v.Y);
                            var hy2 = holeRotNorm.Max(v => v.Y);
                            var holeX = (long)Math.Round(hx - minX);
                            var holeY = (long)Math.Round(hy - minY);
                            var holeW = (long)Math.Round(hx2 - hx);
                            var holeH = (long)Math.Round(hy2 - hy);
                            if (holeW > 1 && holeH > 1)
                            {
                                var holePath = $"M {holeX} {holeY} L {holeX + holeW} {holeY} L {holeX + holeW} {holeY + holeH} L {holeX} {holeY + holeH} Z";
                                svg.Path(holePath, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                            }
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
    }
}
