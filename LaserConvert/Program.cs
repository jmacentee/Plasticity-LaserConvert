using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using IxMilia.Iges;
using IxMilia.Iges.Entities;

namespace LaserConvert
{
    class Program
    {
        struct Vec3 { public double X, Y, Z; public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; } }
        struct Vec2 { public double X, Y; public Vec2(double x, double y) { X = x; Y = y; } }

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LaserConvert <input.igs> <output.svg>");
                return;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            var points = new List<Vec3>();
            var lines = new List<(Vec3 p1, Vec3 p2)>();
            var arcs = new List<(Vec3 center, Vec3 start, Vec3 end)>();

            // Load IGES file
            var igesFile = IgesFile.Load(inputPath);

            foreach (var entity in igesFile.Entities)
            {
                if (entity is IgesLine line)
                {
                    var p1 = new Vec3(line.P1.X, line.P1.Y, line.P1.Z);
                    var p2 = new Vec3(line.P2.X, line.P2.Y, line.P2.Z);
                    lines.Add((p1, p2));
                    points.Add(p1);
                    points.Add(p2);
                }
                else if (entity is IgesCircularArc arc)
                {
                    var c = new Vec3(arc.Center.X, arc.Center.Y, arc.Center.Z);
                    var sp = new Vec3(arc.StartPoint.X, arc.StartPoint.Y, arc.StartPoint.Z);
                    var ep = new Vec3(arc.EndPoint.X, arc.EndPoint.Y, arc.EndPoint.Z);
                    arcs.Add((c, sp, ep));
                    points.Add(c);
                    points.Add(sp);
                    points.Add(ep);
                }
                // optionally: handle IgesBSplineCurve, etc.
            }

            if (points.Count == 0)
            {
                Console.WriteLine("No geometry found.");
                return;
            }

            // PCA to detect thickness axis
            var centroid = Mean(points);
            var axes = PCAAxes(points, centroid);
            var extents = axes.Select(a => AxisExtent(points, centroid, a)).ToArray();
            int thicknessIdx = PickThicknessAxis(extents, 3.0, 0.5);

            var w = axes[thicknessIdx];
            var (u, v) = OrthonormalPlaneBasis(axes, thicknessIdx);

            Vec2 Project(Vec3 p)
            {
                var d = Sub(p, centroid);
                return new Vec2(Dot(d, u), Dot(d, v));
            }

            // Build SVG
            var sb = new StringBuilder();
            sb.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' fill='none' stroke='black' stroke-width='0.1'>");

            foreach (var l in lines)
            {
                var p1 = Project(l.p1);
                var p2 = Project(l.p2);
                sb.AppendLine($"<line x1='{p1.X}' y1='{p1.Y}' x2='{p2.X}' y2='{p2.Y}' />");
            }

            foreach (var a in arcs)
            {
                var c2 = Project(a.center);
                var sp2 = Project(a.start);
                var ep2 = Project(a.end);

                double r = Math.Sqrt(Math.Pow(sp2.X - c2.X, 2) + Math.Pow(sp2.Y - c2.Y, 2));
                double startAngle = Math.Atan2(sp2.Y - c2.Y, sp2.X - c2.X);
                double endAngle = Math.Atan2(ep2.Y - c2.Y, ep2.X - c2.X);

                int largeArc = (Math.Abs(endAngle - startAngle) > Math.PI) ? 1 : 0;
                int sweep = (endAngle > startAngle) ? 1 : 0;

                sb.AppendLine($"<path d='M {sp2.X} {sp2.Y} A {r} {r} 0 {largeArc} {sweep} {ep2.X} {ep2.Y}' />");
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outputPath, sb.ToString());

            Console.WriteLine($"Projected {lines.Count} lines and {arcs.Count} arcs into 2D outline → {outputPath}");
        }

        // ---------- Geometry helpers ----------
        static Vec3 Add(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        static Vec3 Scale(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        static double Norm(Vec3 a) => Math.Sqrt(Dot(a, a));
        static Vec3 Normalize(Vec3 a) { var n = Norm(a); return n > 1e-12 ? Scale(a, 1.0 / n) : new Vec3(0, 0, 0); }
        static Vec3 Mean(List<Vec3> pts) { var sum = new Vec3(0, 0, 0); foreach (var p in pts) sum = Add(sum, p); return Scale(sum, 1.0 / pts.Count); }

        static Vec3[] PCAAxes(List<Vec3> pts, Vec3 c)
        {
            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts) { var d = Sub(p, c); xx += d.X * d.X; xy += d.X * d.Y; xz += d.X * d.Z; yy += d.Y * d.Y; yz += d.Y * d.Z; zz += d.Z * d.Z; }
            var e0 = Normalize(new Vec3(1, 0, 0));
            var e1 = Normalize(new Vec3(0, 1, 0));
            var e2 = Normalize(new Vec3(0, 0, 1));
            e0 = PowerIter(xx, xy, xz, yy, yz, zz, e0);
            e1 = Orthogonalize(PowerIter(xx, xy, xz, yy, yz, zz, e1), e0);
            e2 = Orthogonalize(PowerIter(xx, xy, xz, yy, yz, zz, e2), e0, e1);
            var axes = new[] { e0, e1, e2 };
            var vars = axes.Select(a => VarianceAlong(pts, c, a)).ToArray();
            return axes.Zip(vars, (a, v) => (a, v)).OrderByDescending(t => t.v).Select(t => t.a).ToArray();
        }

        static Vec3 PowerIter(double xx, double xy, double xz, double yy, double yz, double zz, Vec3 v)
        {
            var w = v;
            for (int i = 0; i < 10; i++)
            {
                var x = xx * w.X + xy * w.Y + xz * w.Z;
                var y = xy * w.X + yy * w.Y + yz * w.Z;
                var z = xz * w.X + yz * w.Y + zz * w.Z;
                w = Normalize(new Vec3(x, y, z));
            }
            return w;
        }

        static Vec3 Orthogonalize(Vec3 v, params Vec3[] bases)
        {
            foreach (var b in bases) v = Sub(v, Scale(b, Dot(v, b)));
            return Normalize(v);
        }

        static double VarianceAlong(List<Vec3> pts, Vec3 c, Vec3 a)
        {
            double s = 0; foreach (var p in pts) { var d = Sub(p, c); var t = Dot(d, a); s += t * t; }
            return s / pts.Count;
        }

        static (double min, double max) RangeAlong(List<Vec3> pts, Vec3 c, Vec3 a)
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            foreach (var p in pts) { var t = Dot(Sub(p, c), a); if (t < min) min = t; if (t > max) max = t; }
            return (min, max);
        }

        static double AxisExtent(List<Vec3> pts, Vec3 c, Vec3 a)
        {
            var r = RangeAlong(pts, c, a);
            return r.max - r.min;
        }

        static int PickThicknessAxis(double[] extents, double thickness, double tol)
        {
            int closestIdx = 0;
            double bestDiff = double.PositiveInfinity;
            for (int i = 0; i < 3; i++)
            {
                double diff = Math.Abs(extents[i] - thickness);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    closestIdx = i;
                }
            }
            if (bestDiff <= tol)
                return closestIdx;

            // Fallback: smallest axis is assumed thickness
            double minVal = extents[0];
            int minIdx = 0;
            for (int i = 1; i < 3; i++)
            {
                if (extents[i] < minVal)
                {
                    minVal = extents[i];
                    minIdx = i;
                }
            }
            return minIdx;
        }

        static (Vec3 u, Vec3 v) OrthonormalPlaneBasis(Vec3[] axes, int thicknessIdx)
        {
            // w = thickness axis; build u,v spanning the footprint plane
            var w = axes[thicknessIdx];
            var others = new List<Vec3>();
            for (int i = 0; i < 3; i++)
                if (i != thicknessIdx) others.Add(axes[i]);

            var u = Normalize(others[0]);
            var v = Normalize(Cross(w, u));

            // Ensure orthogonality
            if (Norm(v) < 1e-9)
            {
                u = Normalize(Cross(v, w));
                v = Normalize(Cross(w, u));
            }

            return (u, v);
        }
    }
}