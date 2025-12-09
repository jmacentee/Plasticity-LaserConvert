// .NET 10 Console App: LaserConvert
// Program.cs
//
// Goal:
// - Load an IGES file (metric, mm).
// - Find each thin solid (~3 mm thickness), regardless of orientation.
// - Identify the broad face pair (separated by ~3 mm); use one as the 2D profile plane.
// - Project its outer and inner loops (cutouts) to SVG.
// - Emit one <g> per solid, named after the object (entity name or fallback).
//
// Notes:
// - This code uses IxMilia.Iges (https://github.com/IxMilia/ixmilia) style APIs.
//   If your types differ, adjust class names/members accordingly.
// - Handles lines, arcs, and NURBS edges in loops; tessellates splines.
// - If a solid lacks 186 topology in the export, a fallback tries to group planar faces.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using IxMilia.Iges;
using IxMilia.Iges.Entities;

// Add IxMilia.Iges via NuGet.

namespace LaserConvert
{
    internal static class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: LaserConvert <input.igs> <output.svg>");
                return 1;
            }

            var inputPath = args[0];
            var outputPath = args[1];

            IgesFile iges;
            try
            {
                iges = IgesFile.Load(inputPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read IGES: {ex.Message}");
                return 2;
            }

            // 1) Collect solids (preferred: 186 ManifoldSolidBRepObject).
            var solids = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();

            // Fallback: some Plasticity exports may use shells/faces without a root 186.
            if (solids.Count == 0)
            {
                Console.WriteLine("No 186 solids found; attempting face-based fallback.");
                // Build pseudo-solids by grouping faces into shells (if 514 exists),
                // otherwise gather faces by proximity and parallelism.
                var shells = iges.Entities.OfType<IgesShell>().ToList();
                foreach (var shell in shells)
                {
                    var pseudo = BuildPseudoSolidFromShell(shell);
                    if (pseudo is not null)
                        solids.Add(pseudo);
                }

                // If still empty, try grouping standalone faces into pseudo solids by name or color/level.
                if (solids.Count == 0)
                {
                    var faces = iges.Entities.OfType<IgesFace>().ToList();
                    foreach (var group in GroupFacesIntoPseudoSolids(faces))
                        solids.Add(group);
                }

                if (solids.Count == 0)
                {
                    Console.WriteLine("No solids or shells/faces suitable for projection found.");
                    return 0;
                }
            }

            // 2) For each solid, find the pair of broad parallel faces separated by ~3 mm.
            //    Use the largest-area face as the profile plane.
            var svg = new SvgBuilder(mm: true);
            foreach (var solid in solids)
            {
                var (profileFace, profilePlane) = FindProfileFaceAndPlane(solid);
                if (profileFace is null || profilePlane is null)
                {
                    Console.WriteLine($"Skipping solid (no 3 mm face pair found): {GetEntityName(solid)}");
                    continue;
                }

                var groupName = GetEntityName(solid);
                svg.BeginGroup(groupName);

                // 3) Extract loops (outer + inner) from the chosen face and project to 2D.
                //    Loops reference edges; edges are built from curve entities: lines, arcs, splines, composites.
                var loops = GetFaceLoops(profileFace);
                // Identify the outer loop; the rest are holes. If no flag available, pick the loop with max area as outer.
                var classified = ClassifyLoops(profilePlane, loops);

                // 4) Project each loop to the plane basis and emit SVG paths.
                foreach (var loop in classified.OuterLoops)
                {
                    var path = BuildSvgPathFromLoop(profilePlane, loop);
                    if (path.Length > 0)
                        svg.Path(path, strokeWidth: 0.2, fill: "none"); // outline; laser viewers often prefer strokes only
                }
                foreach (var hole in classified.InnerLoops)
                {
                    var path = BuildSvgPathFromLoop(profilePlane, hole);
                    if (path.Length > 0)
                        svg.Path(path, strokeWidth: 0.2, fill: "none"); // holes are separate paths
                }

                svg.EndGroup();
            }

            // 5) Write SVG to disk.
            File.WriteAllText(outputPath, svg.Build());
            Console.WriteLine($"Wrote SVG: {outputPath}");
            return 0;
        }

        // -----------------------------
        // Topology helpers (IxMilia-style)
        // -----------------------------

        private static string GetEntityName(IgesEntity e)
        {
            // Prefer directory entry name; fallback to type+id.
            var name = e.EntityLabel;
            if (string.IsNullOrWhiteSpace(name))
                name = e.EntityType.ToString();
            return name;
        }

        private static (IgesFace? Face, IgesPlane? Plane) FindProfileFaceAndPlane(IgesManifestSolidBRepObject solid)
        {
            // Enumerate all planar faces; compute area; group by parallel planes; look for pairs ~3 mm apart.
            var planarFaces = new List<(IgesFace face, IgesPlane plane, double area)>();
            foreach (var face in GetSolidFaces(solid))
            {
                var surf = GetFaceSurface(face);
                if (surf is IgesPlane plane)
                {
                    // Approximate face area by tessellating its outer loop.
                    var loops = GetFaceLoops(face);
                    var outer = GuessOuterLoop(plane, loops);
                    if (outer is null) continue;
                    var area = Math.Abs(ComputeLoopArea2D(plane, outer));
                    planarFaces.Add((face, plane, area));
                }
            }

            if (planarFaces.Count < 2)
                return (null, null);

            // Sort by area descending to favor broad faces.
            var byArea = planarFaces.OrderByDescending(p => p.area).ToList();

            // Try to find a matching parallel face ~3 mm away.
            foreach (var (faceA, planeA, areaA) in byArea)
            {
                // Find parallel planes with opposite normals and distance ~3 mm.
                var candidates = byArea.Where(p => p.face != faceA && ArePlanesParallel(planeA, p.plane)).ToList();
                foreach (var (faceB, planeB, areaB) in candidates)
                {
                    var sep = PlanePlaneSeparation(planeA, planeB);
                    if (sep.HasValue && IsApproximately(sep.Value, 3.0, tol: 0.2)) // tolerance to handle export noise
                    {
                        // Choose the larger of the pair as the profile source (both are equivalent for outline).
                        var chosen = areaA >= areaB ? (faceA, planeA) : (faceB, planeB);
                        return (chosen.face, chosen.plane);
                    }
                }
            }

            // Fallback: just pick the largest planar face.
            var top = byArea.FirstOrDefault();
            return (top.face, top.plane);
        }

        private static IEnumerable<IgesFace> GetSolidFaces(IgesManifestSolidBRepObject solid)
        {
            // Typical topology: solid -> shells (514) -> faces (508).
            var faces = new List<IgesFace>();
            foreach (var shell in solid.Shells ?? Enumerable.Empty<IgesShell>())
            {
                faces.AddRange(shell.Faces ?? Enumerable.Empty<IgesFace>());
            }
            return faces;
        }

        private static IgesSurface? GetFaceSurface(IgesFace face)
        {
            // Faces carry a reference to the underlying surface entity (plane, cylinder, etc.).
            return face.Surface;
        }

        private static IEnumerable<IgesLoop> GetFaceLoops(IgesFace face)
        {
            return face.Loops ?? Enumerable.Empty<IgesLoop>();
        }

        private static (List<IgesLoop> OuterLoops, List<IgesLoop> InnerLoops) ClassifyLoops(IgesPlane plane, IEnumerable<IgesLoop> loops)
        {
            var list = loops.ToList();
            var outer = new List<IgesLoop>();
            var inner = new List<IgesLoop>();

            // Prefer an explicit property if available (IsOuter). Otherwise, use area sign/size heuristic.
            var flaggedOuter = list.Where(l => l.IsOuter).ToList();
            if (flaggedOuter.Count > 0)
            {
                outer.AddRange(flaggedOuter);
                inner.AddRange(list.Where(l => !l.IsOuter));
                return (outer, inner);
            }

            // Heuristic: largest absolute area loop is the outer boundary; others are holes (inner loops).
            if (list.Count > 0)
            {
                var areas = list.Select(l => (loop: l, area: Math.Abs(ComputeLoopArea2D(plane, l)))).ToList();
                var max = areas.OrderByDescending(a => a.area).First().loop;
                outer.Add(max);
                inner.AddRange(list.Where(l => l != max));
            }

            return (outer, inner);
        }

        private static IgesLoop? GuessOuterLoop(IgesPlane plane, IEnumerable<IgesLoop> loops)
        {
            var classified = ClassifyLoops(plane, loops);
            return classified.OuterLoops.FirstOrDefault();
        }

        // -----------------------------
        // Geometry: plane basis, projections, area
        // -----------------------------

        private static (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) BuildPlaneFrame(IgesPlane plane)
        {
            // IgesPlane typically provides a point on plane and a normal direction.
            var origin = ToVec3(plane.Location);
            var normal = Normalize(ToVec3(plane.Normal));

            // Build an orthonormal basis (U,V) on the plane.
            var refAxis = Math.Abs(normal.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            var u = Normalize(Cross(refAxis, normal));
            var v = Normalize(Cross(normal, u));
            return (origin, normal, u, v);
        }

        private static string BuildSvgPathFromLoop(IgesPlane plane, IgesLoop loop, double nurbsTolerance = 0.2)
        {
            var sb = new StringBuilder();
            var frame = BuildPlaneFrame(plane);

            // Convert loop edges into a 2D path; edges may be composites; walk in loop order.
            var edges = GetLoopEdges(loop);

            bool started = false;
            foreach (var edge in edges)
            {
                switch (edge)
                {
                    case IgesLine line:
                        var p0 = Project(frame, ToVec3(line.P1));
                        var p1 = Project(frame, ToVec3(line.P2));
                        if (!started) { sb.Append($"M {Fmt(p0.X)} {Fmt(p0.Y)} "); started = true; }
                        sb.Append($"L {Fmt(p1.X)} {Fmt(p1.Y)} ");
                        break;

                    case IgesCircularArc arc:
                        // IGES arc: center, radius, start/end angles in plane coordinates; but here arc lies in 3D.
                        var arcPts = SampleArc3D(arc, frame, segments: 24);
                        started = EmitPolyline(sb, arcPts, started);
                        break;

                    case IgesRationalBSplineCurve nurbs:
                        var pts = SampleNurbs3D(nurbs, frame, step: nurbsTolerance);
                        started = EmitPolyline(sb, pts, started);
                        break;

                    case IgesCompositeCurve comp:
                        foreach (var sub in comp.Curves)
                        {
                            // Recurse for sub-curves.
                            started = EmitCurve2D(sb, sub, frame, ref started, nurbsTolerance);
                        }
                        break;

                    default:
                        // Unknown edge type: attempt tessellation via bounding points if any.
                        var approx = ApproximateUnknownCurve(edge, frame);
                        started = EmitPolyline(sb, approx, started);
                        break;
                }
            }

            // Close path if it’s a loop.
            if (started) sb.Append("Z");
            return sb.ToString();
        }

        private static bool EmitCurve2D(StringBuilder sb, IgesEntity curve, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, ref bool started, double nurbsTolerance)
        {
            switch (curve)
            {
                case IgesLine line:
                    var p0 = Project(frame, ToVec3(line.P1));
                    var p1 = Project(frame, ToVec3(line.P2));
                    if (!started) { sb.Append($"M {Fmt(p0.X)} {Fmt(p0.Y)} "); started = true; }
                    sb.Append($"L {Fmt(p1.X)} {Fmt(p1.Y)} ");
                    return true;

                case IgesCircularArc arc:
                    var arcPts = SampleArc3D(arc, frame, segments: 24);
                    return EmitPolyline(sb, arcPts, started);

                case IgesRationalBSplineCurve nurbs:
                    var pts = SampleNurbs3D(nurbs, frame, step: nurbsTolerance);
                    return EmitPolyline(sb, pts, started);

                default:
                    var approx = ApproximateUnknownCurve(curve, frame);
                    return EmitPolyline(sb, approx, started);
            }
        }

        private static bool EmitPolyline(StringBuilder sb, List<Vec2> pts, bool started)
        {
            if (pts.Count == 0) return started;

            if (!started)
            {
                sb.Append($"M {Fmt(pts[0].X)} {Fmt(pts[0].Y)} ");
                started = true;
            }
            for (int i = 1; i < pts.Count; i++)
                sb.Append($"L {Fmt(pts[i].X)} {Fmt(pts[i].Y)} ");
            return started;
        }

        private static IEnumerable<IgesEntity> GetLoopEdges(IgesLoop loop)
        {
            // In a typical IGES topology, the loop references curves in sequence.
            // IxMilia exposes loop.Edges or loop.Curves; adapt if needed.
            if (loop.Curves is not null) return loop.Curves;
            if (loop.Edges is not null) return loop.Edges.Select(e => e.Curve);
            return Enumerable.Empty<IgesEntity>();
        }

        private static double ComputeLoopArea2D(IgesPlane plane, IgesLoop loop)
        {
            var frame = BuildPlaneFrame(plane);
            var pts = new List<Vec2>();

            // Build a polyline approximation of the loop.
            foreach (var edge in GetLoopEdges(loop))
            {
                switch (edge)
                {
                    case IgesLine line:
                        pts.Add(Project(frame, ToVec3(line.P1)));
                        pts.Add(Project(frame, ToVec3(line.P2)));
                        break;
                    case IgesCircularArc arc:
                        pts.AddRange(SampleArc3D(arc, frame, segments: 24));
                        break;
                    case IgesRationalBSplineCurve nurbs:
                        pts.AddRange(SampleNurbs3D(nurbs, frame, step: 0.5));
                        break;
                    case IgesCompositeCurve comp:
                        foreach (var sub in comp.Curves)
                        {
                            if (sub is IgesLine sl)
                            {
                                pts.Add(Project(frame, ToVec3(sl.P1)));
                                pts.Add(Project(frame, ToVec3(sl.P2)));
                            }
                            else if (sub is IgesCircularArc sa)
                            {
                                pts.AddRange(SampleArc3D(sa, frame, segments: 24));
                            }
                            else if (sub is IgesRationalBSplineCurve sn)
                            {
                                pts.AddRange(SampleNurbs3D(sn, frame, step: 0.5));
                            }
                        }
                        break;
                }
            }

            // Shoelace area on the polyline (ensure closure).
            if (pts.Count < 3) return 0.0;
            // Simple dedup of adjacent equal points
            pts = pts.Where((p, i) => i == 0 || Distance2(pts[i - 1], p) > 1e-6).ToList();
            var first = pts.First();
            var last = pts.Last();
            if (Distance2(first, last) > 1e-6) pts.Add(first);

            double sum = 0.0;
            for (int i = 0; i < pts.Count - 1; i++)
                sum += (pts[i].X * pts[i + 1].Y) - (pts[i + 1].X * pts[i].Y);
            return 0.5 * sum; // signed area
        }

        private static bool ArePlanesParallel(IgesPlane a, IgesPlane b)
        {
            var na = Normalize(ToVec3(a.Normal));
            var nb = Normalize(ToVec3(b.Normal));
            var dot = Math.Abs(Dot(na, nb));
            return IsApproximately(dot, 1.0, tol: 1e-3);
        }

        private static double? PlanePlaneSeparation(IgesPlane a, IgesPlane b)
        {
            // Distance between two parallel planes |(p_b - p_a) · n_a|
            var na = Normalize(ToVec3(a.Normal));
            var pa = ToVec3(a.Location);
            var pb = ToVec3(b.Location);
            if (!ArePlanesParallel(a, b)) return null;
            var d = Math.Abs(Dot(pb - pa, na));
            return d;
        }

        private static bool IsApproximately(double value, double target, double tol)
            => Math.Abs(value - target) <= tol;

        // -----------------------------
        // Curve sampling/projection
        // -----------------------------

        private static List<Vec2> SampleArc3D(IgesCircularArc arc, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, int segments)
        {
            // IxMilia arc exposes center, start, end in 3D; sample uniformly along angle.
            var pts3 = new List<Vec3>();
            var center = ToVec3(arc.Center);
            var start = ToVec3(arc.StartPoint);
            var end = ToVec3(arc.EndPoint);

            var r = (start - center).Length;
            if (r <= 0) return new List<Vec2>();

            // Build arc plane basis from arc’s plane normal approximated via (start-center, end-center).
            var u = Normalize(start - center);
            var w = Normalize(Cross(u, end - center));
            var v = Normalize(Cross(w, u));

            // Determine signed angle from start to end around w.
            double thetaStart = 0.0;
            double thetaEnd = Math.Atan2(Dot(end - center, v), Dot(end - center, u));
            // Normalize to ensure correct direction; assume minor arc
            var sweep = thetaEnd - thetaStart;
            if (sweep <= 0) sweep += 2.0 * Math.PI;

            for (int i = 0; i <= segments; i++)
            {
                var t = thetaStart + sweep * i / segments;
                var p = center + r * Math.Cos(t) * u + r * Math.Sin(t) * v;
                pts3.Add(p);
            }

            return pts3.Select(p => Project(frame, p)).ToList();
        }

        private static List<Vec2> SampleNurbs3D(IgesRationalBSplineCurve curve, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, double step)
        {
            // Uniform parameter sampling; adjust step for precision.
            var pts = new List<Vec2>();
            var t0 = curve.StartParameter;
            var t1 = curve.EndParameter;
            int samples = Math.Max(8, (int)Math.Ceiling((t1 - t0) / Math.Max(step, 1e-3)));
            for (int i = 0; i <= samples; i++)
            {
                double t = t0 + (t1 - t0) * i / samples;
                var p3 = ToVec3(curve.PointAt(t));
                pts.Add(Project(frame, p3));
            }
            return pts;
        }

        private static List<Vec2> ApproximateUnknownCurve(IgesEntity edge, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame)
        {
            // Best-effort: if entity supplies endpoints, use them; else empty.
            var pts = new List<Vec2>();
            switch (edge)
            {
                case IgesEdge e when e.StartPoint is not null && e.EndPoint is not null:
                    pts.Add(Project(frame, ToVec3(e.StartPoint)));
                    pts.Add(Project(frame, ToVec3(e.EndPoint)));
                    break;
            }
            return pts;
        }

        private static Vec2 Project((Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame, Vec3 p)
        {
            var rel = p - frame.Origin;
            return new Vec2(Dot(rel, frame.U), Dot(rel, frame.V));
        }

        // -----------------------------
        // Fallback pseudo-solid builders
        // -----------------------------

        private static IgesManifestSolidBRepObject? BuildPseudoSolidFromShell(IgesShell shell)
        {
            // Create a lightweight pseudo 186 wrapper around a shell, if useful.
            if (shell?.Faces is null || shell.Faces.Count == 0) return null;
            var pseudo = new IgesManifestSolidBRepObject
            {
                Shells = new List<IgesShell> { shell },
                EntityLabel = shell.EntityLabel
            };
            return pseudo;
        }

        private static IEnumerable<IgesManifestSolidBRepObject> GroupFacesIntoPseudoSolids(List<IgesFace> faces)
        {
            // Extremely simple grouping: by label (name). Adjust as needed.
            foreach (var group in faces.GroupBy(f => f.EntityLabel))
            {
                var shell = new IgesShell { Faces = group.ToList(), EntityLabel = group.Key };
                var pseudo = new IgesManifestSolidBRepObject
                {
                    Shells = new List<IgesShell> { shell },
                    EntityLabel = group.Key
                };
                yield return pseudo;
            }
        }

        // -----------------------------
        // Math
        // -----------------------------

        private readonly struct Vec2
        {
            public readonly double X, Y;
            public Vec2(double x, double y) { X = x; Y = y; }
        }

        private readonly struct Vec3
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
            if (len <= 1e-12) return v;
            return (1.0 / len) * v;
        }

        private static double Distance2(Vec2 a, Vec2 b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static string Fmt(double d)
            => d.ToString("0.###", CultureInfo.InvariantCulture);

        private static Vec3 ToVec3(IgesPoint p) => new Vec3(p.X, p.Y, p.Z);

        // -----------------------------
        // SVG builder (mm units)
        // -----------------------------

        private sealed class SvgBuilder
        {
            private readonly StringBuilder _sb = new StringBuilder();
            private bool _started;
            public SvgBuilder(bool mm)
            {
                _sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" version=\"1.1\" width=\"1000\" height=\"1000\" viewBox=\"0 0 1000 1000\">");
                _sb.AppendLine("<defs/>");
                _started = true;
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
                // Units are in mm by convention; viewers use mm when no unit suffix is present.
                _sb.AppendLine($"    <path d=\"{d}\" stroke=\"#000\" stroke-width=\"{Fmt(strokeWidth)}\" fill=\"{fill}\" vector-effect=\"non-scaling-stroke\"/>");
            }

            public string Build()
            {
                if (_started) _sb.AppendLine("</svg>");
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

    // -----------------------------
    // IxMilia-like topology shims
    // (If your exact API differs, adjust names/members below)
    // -----------------------------

    // Surfaces
    //public abstract class IgesSurface : IgesEntity { }
    //public class IgesPlane : IgesSurface
    //{
    //    public IgesPoint Location { get; set; } = new IgesPoint(0, 0, 0);
    //    public IgesVector Normal { get; set; } = new IgesVector(0, 0, 1);
    //}

    //// Curves
    //public class IgesCompositeCurve : IgesEntity
    //{
    //    public List<IgesEntity> Curves { get; set; } = new();
    //}

    
    //public class IgesShell : IgesEntity --514
    //{
    //    public List<IgesFace>? Faces { get; set; }
    //}

    //public class IgesFace : IgesEntity --508
    //{
    //    public IgesSurface? Surface { get; set; }
    //    public List<IgesLoop>? Loops { get; set; }
    //}

    //public class IgesLoop : IgesEntity --510
    //{
    //    public List<IgesEntity>? Curves { get; set; } // sequence of curve entities forming the loop
    //    public bool IsOuter { get; set; } // if available; else false and we’ll classify by area
    //}
}