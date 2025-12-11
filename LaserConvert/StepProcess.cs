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

                    bool complex = topFaceVerts.Count > 8 || (mainFace?.Bounds?.Count ?? 0) > 1;
                    if (complex && mainFace != null)
                    {
                        // Use STEP bounds: first is outer, rest are holes
                        var (outer3D, holeLoops3D) = StepTopologyResolver.ExtractFaceWithHoles(mainFace, stepFile);
                        if (outer3D.Count >= 4)
                        {
                            // Rotate outer/holes with same transform and render ordered edges
                            var outerRotNorm = GeometryTransform.RotateAndNormalize(outer3D);
                            var outerPath = BuildAxisAlignedPathFromOrderedLoop(outerRotNorm);
                            if (!string.IsNullOrEmpty(outerPath))
                            {
                                svg.Path(outerPath, strokeWidth: 0.2, fill: "none", stroke: "#000");
                            }

                            foreach (var hole3D in holeLoops3D)
                            {
                                if (hole3D.Count < 3) continue;
                                var holeRotNorm = GeometryTransform.RotateAndNormalize(hole3D);
                                var holePath = BuildAxisAlignedPathFromOrderedLoop(holeRotNorm);
                                if (!string.IsNullOrEmpty(holePath))
                                {
                                    svg.Path(holePath, strokeWidth: 0.2, fill: "none", stroke: "#FF0000");
                                }
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
    }
}
