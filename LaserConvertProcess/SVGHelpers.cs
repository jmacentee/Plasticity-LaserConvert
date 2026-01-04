using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LaserConvertProcess
{
    /// <summary>
    /// SVG builder for creating SVG documents with groups and paths.
    /// Units are in millimeters for laser cutting applications.
    /// </summary>
    internal sealed class SvgBuilder
    {
        private readonly StringBuilder _sb = new StringBuilder();
        private readonly StringBuilder _content = new StringBuilder();
        
        public SvgBuilder()
        {
            // We'll build the header at the end when we know the content bounds
        }
        
        public void BeginGroup(string name)
        {
            name = Sanitize(name);
            _content.AppendLine($"  <g id=\"{name}\">");
        }
        
        public void EndGroup()
        {
            _content.AppendLine("  </g>");
        }
        
        public void Path(string d, double strokeWidth, string fill, string stroke = "#9600c8")
        {
            _content.AppendLine($"    <path d=\"{d}\" stroke=\"{stroke}\" stroke-width=\"{strokeWidth.ToString("0.###", CultureInfo.InvariantCulture)}\" fill=\"{fill}\" vector-effect=\"non-scaling-stroke\"/>");
        }
        
        public string Build()
        {
            // Use a large viewBox (1000x1000) but specify dimensions in mm
            // The viewBox coordinates are unitless and represent the coordinate system
            // The width/height with mm units tell Inkscape the physical size
            _sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"482mm\" height=\"266mm\" viewBox=\"0 0 482 266\">");
            _sb.AppendLine("<defs/>");
            _sb.Append(_content);
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
        /// Build an SVG path from 2D curve segments (lines and arcs).
        /// </summary>
        public static string BuildPathFromSegments(List<CurveSegment2D> segments)
        {
            if (segments == null || segments.Count == 0) return string.Empty;
            
            var sb = new StringBuilder();
            
            // Start at the first segment's start point
            var first = segments[0];
            sb.Append(FormatPoint("M", first.Start.X, first.Start.Y));
            
            // Add each segment
            foreach (var segment in segments)
            {
                sb.Append(" ");
                sb.Append(segment.ToSvgPathCommand());
            }
            
            // Close the path
            // Check if we need to close (if last end != first start)
            var last = segments[segments.Count - 1];
            var dx = Math.Abs(last.End.X - first.Start.X);
            var dy = Math.Abs(last.End.Y - first.Start.Y);
            if (dx > 0.001 || dy > 0.001)
            {
                // Add a line to close if not already closed
                sb.Append(" Z");
            }
            
            return sb.ToString();
        }

        /// <summary>
        /// Build an SVG path from 2D points (double precision).
        /// </summary>
        public static string BuildPathFromPoints(List<(double X, double Y)> points)
        {
            if (points == null || points.Count < 3) return string.Empty;
            
            var sb = new StringBuilder();
            sb.Append(FormatPoint("M", points[0].X, points[0].Y));
            for (int i = 1; i < points.Count; i++)
            {
                sb.Append(" ");
                sb.Append(FormatPoint("L", points[i].X, points[i].Y));
            }
            sb.Append(" Z");
            return sb.ToString();
        }

        private static string FormatPoint(string command, double x, double y)
        {
            return $"{command} {x.ToString("F3", CultureInfo.InvariantCulture)},{y.ToString("F3", CultureInfo.InvariantCulture)}";
        }
    }
}
