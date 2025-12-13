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
    public static class HelixProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"[IxMilia.Step] Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                Console.WriteLine($"[IxMilia.Step] File loaded. Total items: {stepFile.Items.Count}");

                // Find all solids (manifold solid breps)
                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"[IxMilia.Step] Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    Console.WriteLine("[IxMilia.Step] No solids found in STEP file.");
                    return 0;
                }

                // Filter solids by thin dimension (3mm)
                const double minThickness = 2.5;
                const double maxThickness = 10.0;
                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces, int Face1Idx, int Face2Idx, List<(double X, double Y, double Z)> Vertices, double DimX, double DimY, double DimZ)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, face1Idx, face2Idx, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dims = new[] { dimX, dimY, dimZ };
                    var sortedDims = dims.Select((d, i) => (d, i)).OrderBy(x => x.d).ToArray();
                    var thin = sortedDims[0];
                    if (thin.d >= minThickness && thin.d <= maxThickness)
                    {
                        thinSolids.Add((name, faces, face1Idx, face2Idx, vertices, dimX, dimY, dimZ));
                        Console.WriteLine($"[IxMilia.Step] [FILTER] {name}: dimensions [{dimX:F1}, {dimY:F1}, {dimZ:F1}] - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[IxMilia.Step] [FILTER] {name}: dimensions [{dimX:F1}, {dimY:F1}, {dimZ:F1}] - FAIL");
                    }
                }
                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("[IxMilia.Step] No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                foreach (var (name, faces, face1Idx, face2Idx, vertices, dimX, dimY, dimZ) in thinSolids)
                {
                    svg.BeginGroup(name);
                    // Use only IxMilia.Step for geometry extraction and math
                    var minZ = vertices.Min(v => v.Z);
                    var maxZ = vertices.Max(v => v.Z);
                    var zRange = maxZ - minZ;
                    var topVerts = vertices.Where(v => Math.Abs(v.Z - maxZ) < 0.1).ToList();
                    if (topVerts.Count < 3)
                    {
                        svg.EndGroup();
                        continue;
                    }
                    // Order perimeter (simple nearest-neighbor for now)
                    var ordered = OrderPerimeter2D(topVerts);
                    var minX = ordered.Min(v => v.X);
                    var minY = ordered.Min(v => v.Y);
                    var pts2d = ordered.Select(v => ((long)Math.Round(v.X - minX), (long)Math.Round(v.Y - minY))).ToList();
                    var path = BuildPerimeterPath(pts2d);
                    svg.Path(path, 0.2, "none", "#000");
                    // Holes: not implemented here, but can be extracted from face bounds
                    svg.EndGroup();
                }
                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"[IxMilia.Step] Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[IxMilia.Step] Error: {ex.Message}");
                return 2;
            }
        }

        // Order perimeter vertices in 2D (X/Y) using nearest-neighbor
        private static List<(double X, double Y, double Z)> OrderPerimeter2D(List<(double X, double Y, double Z)> verts)
        {
            if (verts.Count < 3) return verts;
            var used = new HashSet<int>();
            var ordered = new List<(double X, double Y, double Z)> { verts[0] };
            used.Add(0);
            for (int i = 1; i < verts.Count; i++)
            {
                var last = ordered.Last();
                int next = -1;
                double minDist = double.MaxValue;
                for (int j = 0; j < verts.Count; j++)
                {
                    if (used.Contains(j)) continue;
                    var d = Math.Abs(verts[j].X - last.X) + Math.Abs(verts[j].Y - last.Y);
                    if (d < minDist)
                    {
                        minDist = d;
                        next = j;
                    }
                }
                if (next == -1) break;
                ordered.Add(verts[next]);
                used.Add(next);
            }
            return ordered;
        }

        // Build SVG path from ordered 2D points
        private static string BuildPerimeterPath(List<(long X, long Y)> pts)
        {
            if (pts == null || pts.Count < 3) return string.Empty;
            var sb = new StringBuilder();
            sb.Append($"M {pts[0].X},{pts[0].Y}");
            for (int i = 1; i < pts.Count; i++)
                sb.Append($" L {pts[i].X},{pts[i].Y}");
            sb.Append(" Z");
            return sb.ToString();
        }

        // Simple SVG builder (copied from StepProcess)
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
