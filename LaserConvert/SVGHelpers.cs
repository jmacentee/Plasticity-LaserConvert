using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LaserConvert
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
            _sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"1000mm\" height=\"1000mm\" viewBox=\"0 0 1000 1000\">");
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

       
    }
}
