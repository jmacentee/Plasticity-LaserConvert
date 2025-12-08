/*
LaserConvert: IGES to SVG 2D Outline Converter
------------------------------------------------
This tool reads IGES CAD files, detects the thin (≈ 3 mm) extrusion axis using PCA, rotates each solid into its footprint plane, and outputs clean 2D SVG outlines for laser cutting. 
It preserves lines and arcs, and is designed for workflows with Glowforge and other laser cutters. Built on IxMilia.Iges (MIT licensed).

Usage:
    LaserConvert <input.igs> <output.svg>

Features:
- Parses IGES files using IxMilia.Iges
- Detects sheet thickness axis via PCA
- Projects geometry into footprint plane
- Outputs SVG with lines and arcs
*/
using IxMilia.Iges;
using IxMilia.Iges.Entities;
using System.Text;

namespace LaserConvert
{
    class Program
    {
        struct Vec3 { public double X, Y, Z; public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; } }
        struct Vec2 { public double X, Y; public Vec2(double x, double y) { X = x; Y = y; } }

        /// <summary>
        /// Projects a 3D point into the 2D footprint plane using the given centroid and axes.
        /// </summary>
        static Vec2 Project(Vec3 p, Vec3 centroid, Vec3 u, Vec3 v)
        {
            var d = Sub(p, centroid);
            return new Vec2(Dot(d, u), Dot(d, v));
        }

        /// <summary>
        /// Entry point. Loads IGES file, detects thickness axis, projects geometry into 2D, and writes SVG output.
        /// Groups lines/arcs by IGES Group entities.
        /// </summary>
        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LaserConvert <input.igs> <output.svg>");
                return;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            var igesFile = IgesFile.Load(inputPath);

            var sb = new StringBuilder();
            sb.AppendLine("<svg xmlns='http://www.w3.org/2000/svg' fill='none' stroke='black' stroke-width='0.1'>");

            // --- Collect all points from all lines/arcs (grouped and ungrouped) ---
            var allPoints = new List<Vec3>();
            var allEntities = new List<IgesEntity>();
            foreach (var ent in igesFile.Entities)
            {
                if (ent is IgesLine line)
                {
                    allPoints.Add(new Vec3(line.P1.X, line.P1.Y, line.P1.Z));
                    allPoints.Add(new Vec3(line.P2.X, line.P2.Y, line.P2.Z));
                    allEntities.Add(ent);
                }
                else if (ent is IgesCircularArc arc)
                {
                    allPoints.Add(new Vec3(arc.Center.X, arc.Center.Y, arc.Center.Z));
                    allPoints.Add(new Vec3(arc.StartPoint.X, arc.StartPoint.Y, arc.StartPoint.Z));
                    allPoints.Add(new Vec3(arc.EndPoint.X, arc.EndPoint.Y, arc.EndPoint.Z));
                    allEntities.Add(ent);
                }
            }
            if (allPoints.Count == 0)
            {
                sb.AppendLine("</svg>");
                File.WriteAllText(outputPath, sb.ToString());
                Console.WriteLine("No geometry found.");
                return;
            }
            // --- Compute global centroid and axes ---
            var globalCentroid = Mean(allPoints);
            var globalAxes = PCAAxes(allPoints, globalCentroid);
            var globalExtents = globalAxes.Select(a => AxisExtent(allPoints, globalCentroid, a)).ToArray();
            int globalThicknessIdx = PickThicknessAxis(globalExtents, 3.0, 0.5);
            var globalW = globalAxes[globalThicknessIdx];
            var (globalU, globalV) = OrthonormalPlaneBasis(globalAxes, globalThicknessIdx);

            // Find all IgesGroup entities (solids)
            var groups = igesFile.Entities.OfType<IgesGroup>().ToList();
            var usedEntities = new HashSet<IgesEntity>();
            int totalLines = 0, totalArcs = 0;

            foreach (var group in groups)
            {
                string groupName = group.EntityLabel ?? $"group_{groups.IndexOf(group)}";
                var groupLines = new List<(Vec3 p1, Vec3 p2)>();
                var groupArcs = new List<(Vec3 center, Vec3 start, Vec3 end)>();

                // DEBUG: Print raw group parameters
                var groupType = group.GetType();
                var paramField = groupType.GetField("_parameters", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (paramField != null)
                {
                    var rawParams = paramField.GetValue(group) as System.Collections.IEnumerable;
                    Console.WriteLine($"Raw parameters for group {groupName}:");
                    if (rawParams != null)
                    {
                        foreach (var p in rawParams)
                            Console.WriteLine($"  {p}");
                    }
                }
                else
                {
                    Console.WriteLine($"No raw parameter field found for group {groupName}.");
                }

                // Only count geometry entities for output
                var geometryEntities = group.AssociatedEntities.Where(e => e is IgesLine || e is IgesCircularArc).ToList();
                Console.WriteLine($"Group {groupName} has {geometryEntities.Count} geometry entities.");
                foreach (var ent in geometryEntities)
                    Console.WriteLine($" Entity type: {ent.EntityType}, label: {ent.EntityLabel}");

                foreach (var ent in geometryEntities)
                {
                    if (ent is IgesLine line)
                    {
                        var p1 = new Vec3(line.P1.X, line.P1.Y, line.P1.Z);
                        var p2 = new Vec3(line.P2.X, line.P2.Y, line.P2.Z);
                        groupLines.Add((p1, p2));
                        totalLines++;
                    }
                    else if (ent is IgesCircularArc arc)
                    {
                        var c = new Vec3(arc.Center.X, arc.Center.Y, arc.Center.Z);
                        var sp = new Vec3(arc.StartPoint.X, arc.StartPoint.Y, arc.StartPoint.Z);
                        var ep = new Vec3(arc.EndPoint.X, arc.EndPoint.Y, arc.EndPoint.Z);
                        groupArcs.Add((c, sp, ep));
                        totalArcs++;
                    }
                    usedEntities.Add(ent);
                }

                // --- PCA and thickness axis for this group ---
                var groupPoints = groupLines.SelectMany(l => new[] { l.p1, l.p2 }).ToList();
                if (groupPoints.Count == 0) continue;

                var groupCentroid = Mean(groupPoints);
                var groupAxes = PCAAxes(groupPoints, groupCentroid);
                var groupExtents = groupAxes.Select(a => AxisExtent(groupPoints, groupCentroid, a)).ToArray();
                int thicknessIdx = PickThicknessAxis(groupExtents, 3.0, 0.5);
                var thicknessAxis = groupAxes[thicknessIdx];
                var (u, v) = OrthonormalPlaneBasis(groupAxes, thicknessIdx);

                var thicknessRange = RangeAlong(groupPoints, groupCentroid, thicknessAxis);
                double thicknessValue = Math.Abs(thicknessRange.max - thicknessRange.min);
                double thicknessPlane = thicknessRange.min + thicknessValue / 2.0;
                double tol = 0.2;

                // --- Filter lines to those in the footprint plane ---
                var footprintLines = groupLines
                    .Where(l =>
                        Math.Abs(Dot(Sub(l.p1, groupCentroid), thicknessAxis) - thicknessPlane) < tol &&
                        Math.Abs(Dot(Sub(l.p2, groupCentroid), thicknessAxis) - thicknessPlane) < tol)
                    .ToList();

                // --- Deduplicate lines (order-independent) ---
                var unique = new HashSet<(double, double, double, double)>();
                var dedupedLines = new List<(Vec3 p1, Vec3 p2)>();
                foreach (var l in footprintLines)
                {
                    var key = l.p1.X < l.p2.X || (l.p1.X == l.p2.X && l.p1.Y <= l.p2.Y)
                        ? (l.p1.X, l.p1.Y, l.p2.X, l.p2.Y)
                        : (l.p2.X, l.p2.Y, l.p1.X, l.p1.Y);
                    if (unique.Add(key))
                        dedupedLines.Add(l);
                }

                // --- Filter arcs to those in the footprint plane ---
                var dedupedArcs = groupArcs
                    .Where(a =>
                        Math.Abs(Dot(Sub(a.center, groupCentroid), thicknessAxis) - thicknessPlane) < tol &&
                        Math.Abs(Dot(Sub(a.start, groupCentroid), thicknessAxis) - thicknessPlane) < tol &&
                        Math.Abs(Dot(Sub(a.end, groupCentroid), thicknessAxis) - thicknessPlane) < tol)
                    .ToList();

                if (dedupedLines.Count == 0 && dedupedArcs.Count == 0) continue;
                sb.AppendLine($"<g id='{groupName.Replace("'", "_")}'>");
                foreach (var l in dedupedLines)
                {
                    var p1 = Project(l.p1, groupCentroid, u, v);
                    var p2 = Project(l.p2, groupCentroid, u, v);
                    sb.AppendLine($"<line x1='{p1.X}' y1='{p1.Y}' x2='{p2.X}' y2='{p2.Y}' />");
                }
                foreach (var a in dedupedArcs)
                {
                    var c2 = Project(a.center, groupCentroid, u, v);
                    var sp2 = Project(a.start, groupCentroid, u, v);
                    var ep2 = Project(a.end, groupCentroid, u, v);
                    double r = Math.Sqrt(Math.Pow(sp2.X - c2.X, 2) + Math.Pow(sp2.Y - c2.Y, 2));
                    double startAngle = Math.Atan2(sp2.Y - c2.Y, sp2.X - c2.X);
                    double endAngle = Math.Atan2(ep2.Y - c2.Y, ep2.X - c2.X);
                    int largeArc = (Math.Abs(endAngle - startAngle) > Math.PI) ? 1 : 0;
                    int sweep = (endAngle > startAngle) ? 1 : 0;
                    sb.AppendLine($"<path d='M {sp2.X} {sp2.Y} A {r} {r} 0 {largeArc} {sweep} {ep2.X} {ep2.Y}' />");
                }
                sb.AppendLine("</g>");
            }

            // Handle ungrouped entities as fallback
            var ungroupedLines = new List<(Vec3 p1, Vec3 p2)>();
            var ungroupedArcs = new List<(Vec3 center, Vec3 start, Vec3 end)>();
            foreach (var ent in igesFile.Entities)
            {
                if (usedEntities.Contains(ent)) continue;
                if (ent is IgesLine line)
                {
                    var p1 = new Vec3(line.P1.X, line.P1.Y, line.P1.Z);
                    var p2 = new Vec3(line.P2.X, line.P2.Y, line.P2.Z);
                    ungroupedLines.Add((p1, p2));
                    totalLines++;
                }
                else if (ent is IgesCircularArc arc)
                {
                    var c = new Vec3(arc.Center.X, arc.Center.Y, arc.Center.Z);
                    var sp = new Vec3(arc.StartPoint.X, arc.StartPoint.Y, arc.StartPoint.Z);
                    var ep = new Vec3(arc.EndPoint.X, arc.EndPoint.Y, arc.EndPoint.Z);
                    ungroupedArcs.Add((c, sp, ep));
                    totalArcs++;
                }
            }
            if (ungroupedLines.Count > 0 || ungroupedArcs.Count > 0)
            {
                sb.AppendLine("<g id='ungrouped'>");
                foreach (var l in ungroupedLines)
                {
                    var p1 = Project(l.p1, globalCentroid, globalU, globalV);
                    var p2 = Project(l.p2, globalCentroid, globalU, globalV);
                    sb.AppendLine($"<line x1='{p1.X}' y1='{p1.Y}' x2='{p2.X}' y2='{p2.Y}' />");
                }
                foreach (var a in ungroupedArcs)
                {
                    var c2 = Project(a.center, globalCentroid, globalU, globalV);
                    var sp2 = Project(a.start, globalCentroid, globalU, globalV);
                    var ep2 = Project(a.end, globalCentroid, globalU, globalV);
                    double r = Math.Sqrt(Math.Pow(sp2.X - c2.X, 2) + Math.Pow(sp2.Y - c2.Y, 2));
                    double startAngle = Math.Atan2(sp2.Y - c2.Y, sp2.X - c2.X);
                    double endAngle = Math.Atan2(ep2.Y - c2.Y, ep2.X - c2.X);
                    int largeArc = (Math.Abs(endAngle - startAngle) > Math.PI) ? 1 : 0;
                    int sweep = (endAngle > startAngle) ? 1 : 0;
                    sb.AppendLine($"<path d='M {sp2.X} {sp2.Y} A {r} {r} 0 {largeArc} {sweep} {ep2.X} {ep2.Y}' />");
                }
                sb.AppendLine("</g>");
            }

            sb.AppendLine("</svg>");
            File.WriteAllText(outputPath, sb.ToString());
            Console.WriteLine($"Projected {totalLines} lines and {totalArcs} arcs into 2D outline → {outputPath}");
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
            var w = axes[thicknessIdx];
            var others = new List<Vec3>();
            for (int i = 0; i < 3; i++)
                if (i != thicknessIdx) others.Add(axes[i]);

            var u = Normalize(others[0]);
            var v = Normalize(Cross(w, u));

            if (Norm(v) < 1e-9)
            {
                u = Normalize(Cross(v, w));
                v = Normalize(Cross(w, u));
            }

            return (u, v);
        }
    }
}