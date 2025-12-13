using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LaserConvert
{
    /// <summary>
    /// SVG builder for creating SVG documents with groups and paths.
    /// </summary>
    internal sealed class SvgBuilder
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

    /// <summary>
    /// SVG path building utilities.
    /// </summary>
    internal static class SvgPathBuilder
    {
        /// <summary>
        /// Build an SVG path from 2D perimeter points, connecting them in order.
        /// </summary>
        public static string BuildPath(List<(long X, long Y)> points)
        {
            if (points == null || points.Count < 3) return string.Empty;
            
            var sb = new StringBuilder();
            sb.Append($"M {points[0].X},{points[0].Y}");
            for (int i = 1; i < points.Count; i++)
                sb.Append($" L {points[i].X},{points[i].Y}");
            sb.Append(" Z");
            return sb.ToString();
        }

        /// <summary>
        /// Build an axis-aligned path from ordered loop by rounding and snapping to H/V segments.
        /// </summary>
        public static string BuildAxisAlignedPathFromOrderedLoop(List<GeometryTransform.Vec3> loop)
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

        /// <summary>
        /// Build an SVG path from 2D perimeter points, connecting them in order.
        /// </summary>
        public static string BuildPerimeterPath(List<(long X, long Y)> pts)
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
    }
}
