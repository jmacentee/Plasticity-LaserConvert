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
                
                var thinSolids = new List<(string Name, List<(double X, double Y, double Z)> Vertices)>();
                
                foreach (var (name, faces) in solids)
                {
                    var vertices = StepTopologyResolver.ExtractVerticesFromFaces(faces, stepFile);
                    var dimensions = ComputeDimensions(vertices);
                    
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, vertices));
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
                
                foreach (var (name, vertices) in thinSolids)
                {
                    svg.BeginGroup(name);
                    
                    if (vertices.Count >= 3)
                    {
                        var points2D = vertices.Select(v => new Vec2(v.X, v.Y)).Distinct().ToList();
                        var pathData = BuildPathData(points2D);
                        if (!string.IsNullOrEmpty(pathData))
                        {
                            svg.Path(pathData, strokeWidth: 0.2, fill: "none");
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

        private static string BuildPathData(List<Vec2> points)
        {
            if (points.Count < 2)
                return "";
            
            var sb = new StringBuilder();
            sb.Append($"M {Fmt(points[0].X)} {Fmt(points[0].Y)} ");
            
            for (int i = 1; i < points.Count; i++)
            {
                sb.Append($"L {Fmt(points[i].X)} {Fmt(points[i].Y)} ");
            }
            
            sb.Append("Z");
            return sb.ToString();
        }

        // ========== Helper Types ==========

        private struct Vec2
        {
            public double X, Y;
            public Vec2(double x, double y) { X = x; Y = y; }
            public override bool Equals(object obj) => obj is Vec2 v && Math.Abs(X - v.X) < 1e-9 && Math.Abs(Y - v.Y) < 1e-9;
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private record Dimensions(double Width, double Height, double Depth)
        {
            public bool HasThinDimension(double minThickness, double maxThickness)
            {
                var sorted = new[] { Width, Height, Depth }.OrderBy(d => d).ToList();
                return sorted[0] >= minThickness && sorted[0] <= maxThickness;
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

        private static string Fmt(double d)
            => d.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
