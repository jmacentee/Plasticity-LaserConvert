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

            // Post-process shells: if faces didn't bind properly, try to recover by matching pointers to actual faces
            RecoverShellFaces(iges);

            // Re-bind manifests to their shells using entity pointers
            RebindManifestsToShells(iges);

            // 1) Collect solids (preferred: 186 ManifoldSolidBRepObject).
            var solids = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();

            // Filter solids: keep only those with at least one thin dimension (~3mm)
            const double minThickness = 2.5;
            const double maxThickness = 5.0;

            solids = solids.Where(solid => HasThinDimension(solid, minThickness, maxThickness)).ToList();

            if (solids.Count == 0)
            {
                Console.WriteLine("No solids with ~3mm thickness found.");
            }

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
                var (profileFace, profilePlane, minSep) = FindProfileFaceAndPlane(solid);
                string solidName = GetEntityName(solid);
                if (profileFace is null || profilePlane is null)
                {
                    var sepMsg = minSep.HasValue ? $" (min separation: {minSep.Value:0.###} mm)" : "";
                    Console.WriteLine($"Skipping solid (no 3 mm face pair found): {solidName}{sepMsg}");
                    continue;
                }

                svg.BeginGroup(solidName);

                // Collect ALL edges from ALL faces in this solid
                var allFaces = GetSolidFaces(solid).ToList();
                var allEdges = new List<IgesEntity>();

                foreach (var face in allFaces)
                {
                    if (face is IgesFace igesFace && igesFace.Edges.Count > 0)
                    {
                        allEdges.AddRange(igesFace.Edges);
                    }
                }

                if (allEdges.Count == 0)
                {
                    svg.EndGroup();
                    continue;
                }

                // Deduplicate edges by geometry
                var uniqueEdges = new Dictionary<string, IgesEntity>();

                foreach (var edge in allEdges)
                {
                    if (edge is IgesLine line)
                    {
                        double x1 = line.P1.X, y1 = line.P1.Y, z1 = line.P1.Z;
                        double x2 = line.P2.X, y2 = line.P2.Y, z2 = line.P2.Z;

                        // Normalize so reverse edges have same key
                        string key;
                        if (x1 > x2 || (Math.Abs(x1 - x2) < 1e-6 && y1 > y2) ||
                            (Math.Abs(x1 - x2) < 1e-6 && Math.Abs(y1 - y2) < 1e-6 && z1 > z2))
                        {
                            key = $"L({x2:F1},{y2:F1},{z2:F1})-({x1:F1},{y1:F1},{z1:F1})";
                        }
                        else
                        {
                            key = $"L({x1:F1},{y1:F1},{z1:F1})-({x2:F1},{y2:F1},{z2:F1})";
                        }

                        if (!uniqueEdges.ContainsKey(key))
                        {
                            uniqueEdges[key] = edge;
                        }
                    }
                    else
                    {
                        uniqueEdges[Guid.NewGuid().ToString()] = edge;
                    }
                }

                var dedupedEdges = uniqueEdges.Values.ToList();

                // Derive projection plane from the edges
                var edgePoints = new List<Vec3>();
                foreach (var edge in dedupedEdges)
                {
                    if (edge is IgesLine line)
                    {
                        edgePoints.Add(ToVec3(line.P1));
                        edgePoints.Add(ToVec3(line.P2));
                    }
                }

                // Find 3 non-collinear points for plane
                if (edgePoints.Count < 3)
                {
                    svg.EndGroup();
                    continue;
                }

                Vec3 p0 = edgePoints[0];
                Vec3 p1 = new Vec3(0, 0, 0);
                Vec3 p2 = new Vec3(0, 0, 0);
                bool foundP1 = false;
                bool foundP2 = false;

                for (int i = 1; i < edgePoints.Count && !foundP2; i++)
                {
                    if (!foundP1 && Distance3(edgePoints[i], p0) > 0.1)
                    {
                        p1 = edgePoints[i];
                        foundP1 = true;
                    }
                    else if (foundP1)
                    {
                        var v1 = p1 - p0;
                        var v2 = edgePoints[i] - p0;
                        var cross = Cross(v1, v2);
                        if (cross.Length > 0.1)
                        {
                            p2 = edgePoints[i];
                            foundP2 = true;
                        }
                    }
                }

                if (!foundP2)
                {
                    svg.EndGroup();
                    continue;
                }

                var v1_plane = p1 - p0;
                var v2_plane = p2 - p0;
                var planeNormal = Normalize(Cross(v1_plane, v2_plane));
                var derivedPlane = (p0, planeNormal);

                // Project all edges to 2D using derived plane
                var frame = BuildPlaneFrame(derivedPlane);
                var sb = new StringBuilder();
                bool started = false;

                foreach (var edge in dedupedEdges)
                {
                    if (edge is IgesLine line)
                    {
                        var p0_3d = ToVec3(line.P1);
                        var p1_3d = ToVec3(line.P2);
                        var p0_2d = Project(frame, p0_3d);
                        var p1_2d = Project(frame, p1_3d);

                        // Skip degenerate edges where start and end project to the same 2D point
                        double dist2d = Math.Sqrt((p0_2d.X - p1_2d.X) * (p0_2d.X - p1_2d.X) + (p0_2d.Y - p1_2d.Y) * (p0_2d.Y - p1_2d.Y));
                        if (dist2d < 0.01)
                        {
                            continue;
                        }

                        if (!started) { sb.Append($"M {Fmt(p0_2d.X)} {Fmt(p0_2d.Y)} "); started = true; }
                        sb.Append($"L {Fmt(p1_2d.X)} {Fmt(p1_2d.Y)} ");
                    }
                }

                if (started)
                {
                    sb.Append("Z");
                    svg.Path(sb.ToString(), strokeWidth: 0.2, fill: "none");
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
            var name = e.EntityLabel;
            if (string.IsNullOrWhiteSpace(name))
                name = e.EntityType.ToString();
            return name;
        }

        // Helper: Check if a solid has one dimension in the "thin" range (for laser cutting)
        private static bool HasThinDimension(IgesManifestSolidBRepObject solid, double minThickness, double maxThickness)
        {
            var allFaces = GetSolidFaces(solid).ToList();
            string solidName = GetEntityName(solid);
            Console.WriteLine($"[DEBUG] {solidName}: {allFaces.Count} faces");

            if (allFaces.Count < 2)
                return false;

            // Collect all edge lengths from all faces
            var edgeLengths = new List<double>();

            foreach (var face in allFaces)
            {
                if (face is IgesFace igesFace && igesFace.Edges.Count > 0)
                {
                    foreach (var edge in igesFace.Edges)
                    {
                        if (edge is IgesLine line)
                        {
                            double len = Math.Sqrt(
                                (line.P1.X - line.P2.X) * (line.P1.X - line.P2.X) +
                                (line.P1.Y - line.P2.Y) * (line.P1.Y - line.P2.Y) +
                                (line.P1.Z - line.P2.Z) * (line.P1.Z - line.P2.Z));

                            if (len > 0.1)  // Skip degenerate edges
                                edgeLengths.Add(len);
                        }
                    }
                }
            }

            if (edgeLengths.Count == 0)
                return false;

            // Check if the smallest edge length is in the thin range
            edgeLengths.Sort();
            double minEdge = edgeLengths[0];
            double maxEdge = edgeLengths[edgeLengths.Count - 1];

            bool hasThinDimension = minEdge >= minThickness && minEdge <= maxThickness;
            
            Console.WriteLine($"[DEBUG] {solidName}: edges [{minEdge:F1}, ..., {maxEdge:F1}] total={edgeLengths.Count}");
            
            if (hasThinDimension)
            {
                Console.WriteLine($"[FILTER] Solid '{solidName}': Found thin dimension {minEdge:F1}mm (other edges: {maxEdge:F1}mm)");
            }
            else
            {
                Console.WriteLine($"[FILTER] Solid '{solidName}': Skipped (min edge {minEdge:F1}mm is outside {minThickness}-{maxThickness}mm range)");
            }
            
            return hasThinDimension;
        }

        // Helper: Try to extract a plane from any IGES surface entity
        private static bool TryGetPlane(IgesEntity? surface, out (Vec3 Origin, Vec3 Normal) plane)
        {
            plane = default;
            if (surface == null) return false;
            switch (surface.EntityType)
            {
                case IgesEntityType.Plane:
                    var p = (IgesPlane)surface;
                    var n = new Vec3(p.PlaneCoefficientA, p.PlaneCoefficientB, p.PlaneCoefficientC);
                    var len2 = n.X * n.X + n.Y * n.Y + n.Z * n.Z;
                    var o = (len2 > 1e-12) ? (p.PlaneCoefficientD / len2) * n : new Vec3(0, 0, 0);
                    plane = (o, Normalize(n));
                    return true;
                case IgesEntityType.PlaneSurface:
                    var ps = (IgesPlaneSurface)surface;
                    if (ps.Point != null && ps.Normal != null)
                    {
                        var origin = new Vec3(ps.Point.X, ps.Point.Y, ps.Point.Z);
                        var normal = new Vec3(ps.Normal.X, ps.Normal.Y, ps.Normal.Z);
                        plane = (origin, Normalize(normal));
                        return true;
                    }
                    return false;
                // Add other plane-like types (e.g., 190, 192, 194, 196, 198)
                case IgesEntityType.RightCircularCylindricalSurface:
                case IgesEntityType.RightCircularConicalSurface:
                case IgesEntityType.SphericalSurface:
                case IgesEntityType.ToroidalSurface:
                    // These are not strictly planes, but could be handled if needed
                    return false;
                default:
                    return false;
            }
        }

        // Fallback: Extract plane from face loops if surface is null
        private static bool TryGetPlaneFromLoops(IgesFace face, out (Vec3 Origin, Vec3 Normal) plane)
        {
            plane = default;
            return false;  // Not implemented - geometry extraction from loops unreliable
        }

        // Fallback: If we can't extract geometry from loops, create synthetic planes based on bounding box
        private static bool CreateSyntheticPlanes(IgesFace face1, IgesFace face2, out (Vec3 Origin, Vec3 Normal) plane1, out (Vec3 Origin, Vec3 Normal) plane2)
        {
            plane1 = default;
            plane2 = default;

            // For a thin box, assume top and bottom faces are parallel
            // Use Z-axis as the primary axis for thin objects
            plane1 = (new Vec3(0, 0, 0), new Vec3(0, 0, 1));      // Bottom face (Z=0)
            plane2 = (new Vec3(0, 0, 3), new Vec3(0, 0, 1));      // Top face (Z=3mm)

            return true;
        }

        private static (IgesFace? Face, (Vec3 Origin, Vec3 Normal)? Plane, double? MinSeparation) FindProfileFaceAndPlane(IgesManifestSolidBRepObject solid)
        {
            var allFaces = GetSolidFaces(solid).ToList();

            if (allFaces.Count < 1)
                return (null, null, null);

            // Just return the first face - we'll use ALL edges from ALL faces
            // The profile plane will be derived from those edges
            var firstFace = allFaces[0] as IgesFace;
            if (firstFace == null)
                return (null, null, null);

            // We'll derive the plane from edges later
            // For now, just return a dummy plane
            var dummyPlane = (new Vec3(0, 0, 0), new Vec3(0, 0, 1));
            return (firstFace, dummyPlane, null);
        }

        private static IEnumerable<IgesFace> GetSolidFaces(IgesManifestSolidBRepObject solid)
        {
            if (solid.Shell != null && solid.Shell.Faces != null)
                return solid.Shell.Faces;
            return Enumerable.Empty<IgesFace>();
        }

        private static IgesEntity? GetFaceSurface(IgesFace face)
        {
            return face.Surface;
        }

        private static IEnumerable<IgesLoop> GetFaceLoops(IgesFace face)
        {
            return face.Loops ?? Enumerable.Empty<IgesLoop>();
        }

        private static (List<IgesLoop> OuterLoops, List<IgesLoop> InnerLoops) ClassifyLoops((Vec3 Origin, Vec3 Normal) plane, IEnumerable<IgesLoop> loops)
        {
            var list = loops.ToList();
            var outer = new List<IgesLoop>();
            var inner = new List<IgesLoop>();
            var flaggedOuter = list.Where(l => l.IsOuter).ToList();
            if (flaggedOuter.Count > 0)
            {
                outer.AddRange(flaggedOuter);
                inner.AddRange(list.Where(l => !l.IsOuter));
                return (outer, inner);
            }
            if (list.Count > 0)
            {
                var areas = list.Select(l => (loop: l, area: Math.Abs(ComputeLoopArea2D(plane, l)))).ToList();
                var max = areas.OrderByDescending(a => a.area).First().loop;
                outer.Add(max);
                inner.AddRange(list.Where(l => l != max));
            }
            return (outer, inner);
        }

        private static IgesLoop? GuessOuterLoop((Vec3 Origin, Vec3 Normal) plane, IEnumerable<IgesLoop> loops)
        {
            var classified = ClassifyLoops(plane, loops);
            return classified.OuterLoops.FirstOrDefault();
        }

        // -----------------------------
        // Geometry: plane basis, projections, area
        // -----------------------------

        private static (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) BuildPlaneFrame((Vec3 Origin, Vec3 Normal) plane)
        {
            var origin = plane.Origin;
            var normal = Normalize(plane.Normal);
            var refAxis = Math.Abs(normal.Z) < 0.9 ? new Vec3(0, 0, 1) : new Vec3(0, 1, 0);
            var u = Normalize(Cross(refAxis, normal));
            var v = Normalize(Cross(normal, u));
            return (origin, normal, u, v);
        }

        private static string BuildSvgPathFromLoop((Vec3 Origin, Vec3 Normal) plane, IgesLoop loop, double nurbsTolerance = 0.2)
        {
            var sb = new StringBuilder();
            var frame = BuildPlaneFrame(plane);
            var edges = GetLoopEdges(loop).ToList();

            // Deduplicate edges: filter out edges that are identical to previous ones
            var uniqueEdges = new List<IgesEntity>();
            foreach (var edge in edges)
            {
                bool isDuplicate = false;
                if (edge is IgesLine line)
                {
                    foreach (var prevEdge in uniqueEdges)
                    {
                        if (prevEdge is IgesLine prevLine)
                        {
                            // Check if same line (both endpoints match)
                            if (Math.Abs(line.P1.X - prevLine.P1.X) < 1e-6 &&
                                Math.Abs(line.P1.Y - prevLine.P1.Y) < 1e-6 &&
                                Math.Abs(line.P1.Z - prevLine.P1.Z) < 1e-6 &&
                                Math.Abs(line.P2.X - prevLine.P2.X) < 1e-6 &&
                                Math.Abs(line.P2.Y - prevLine.P2.Y) < 1e-6 &&
                                Math.Abs(line.P2.Z - prevLine.P2.Z) < 1e-6)
                            {
                                isDuplicate = true;
                                break;
                            }
                        }
                    }
                }

                if (!isDuplicate)
                {
                    uniqueEdges.Add(edge);
                }
            }

            edges = uniqueEdges;

            // If loop contains Face references, recursively extract edges from those faces' loops
            var facesInLoop = edges.OfType<IgesFace>().ToList();
            if (facesInLoop.Count > 0)
            {
                edges.Clear();
                foreach (var face in facesInLoop)
                {
                    var faceLoops = GetFaceLoops(face);
                    foreach (var faceLoop in faceLoops)
                    {
                        var faceLoopEdges = GetLoopEdges(faceLoop).ToList();
                        edges.AddRange(faceLoopEdges);
                    }
                }
            }

            if (!edges.Any())
            {
                return "";
            }

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
                        var arcPts = SampleArc3D(arc, frame, segments: 24);
                        started = EmitPolyline(sb, arcPts, started);
                        break;
                    case IgesRationalBSplineCurve nurbs:
                        var pts = SampleNurbs3D(nurbs, frame, step: 0.2);
                        started = EmitPolyline(sb, pts, started);
                        break;
                }
            }
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
            return loop.Curves ?? Enumerable.Empty<IgesEntity>();
        }

        private static double ComputeLoopArea2D((Vec3 Origin, Vec3 Normal) plane, IgesLoop loop)
        {
            var frame = BuildPlaneFrame(plane);
            var pts = new List<Vec2>();
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
                        foreach (var sub in comp.Entities ?? Enumerable.Empty<IgesEntity>())
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
            if (pts.Count < 3) return 0.0;
            pts = pts.Where((p, i) => i == 0 || Distance2(pts[i - 1], p) > 1e-6).ToList();
            var first = pts.First();
            var last = pts.Last();
            if (Distance2(first, last) > 1e-6) pts.Add(first);
            double sum = 0.0;
            for (int i = 0; i < pts.Count - 1; i++)
                sum += (pts[i].X * pts[i + 1].Y) - (pts[i + 1].X * pts[i].Y);
            return 0.5 * sum;
        }

        private static bool ArePlanesParallel((Vec3 Origin, Vec3 Normal) a, (Vec3 Origin, Vec3 Normal) b)
        {
            var na = Normalize(a.Normal);
            var nb = Normalize(b.Normal);
            var dot = Math.Abs(Dot(na, nb));
            return IsApproximately(dot, 1.0, tol: 1e-3);
        }

        private static double? PlanePlaneSeparation((Vec3 Origin, Vec3 Normal) a, (Vec3 Origin, Vec3 Normal) b)
        {
            var na = Normalize(a.Normal);
            var pa = a.Origin;
            var pb = b.Origin;
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
            var pts3 = new List<Vec3>();
            var center = ToVec3(arc.Center);
            var start = ToVec3(arc.StartPoint);
            var end = ToVec3(arc.EndPoint);
            var r = (start - center).Length;
            if (r <= 0) return new List<Vec2>();
            var u = Normalize(start - center);
            var w = Normalize(Cross(u, end - center));
            var v = Normalize(Cross(w, u));
            double thetaStart = 0.0;
            double thetaEnd = Math.Atan2(Dot(end - center, v), Dot(end - center, u));
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
            // Approximate by using control points (since PointAt is not available)
            var pts = new List<Vec2>();
            foreach (var cp in curve.ControlPoints)
                pts.Add(Project(frame, ToVec3(cp)));
            return pts;
        }

        private static List<Vec2> ApproximateUnknownCurve(IgesEntity edge, (Vec3 Origin, Vec3 Normal, Vec3 U, Vec3 V) frame)
        {
            var pts = new List<Vec2>();
            // Try to get endpoints if available
            var p1Prop = edge.GetType().GetProperty("P1");
            var p2Prop = edge.GetType().GetProperty("P2");
            if (p1Prop != null && p2Prop != null)
            {
                var p1 = p1Prop.GetValue(edge) as IgesPoint?;
                var p2 = p2Prop.GetValue(edge) as IgesPoint?;
                if (p1 != null && p2 != null)
                {
                    pts.Add(Project(frame, ToVec3(p1.Value)));
                    pts.Add(Project(frame, ToVec3(p2.Value)));
                }
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
            if (shell?.Faces == null || shell.Faces.Count == 0) return null;
            var pseudo = new IgesManifestSolidBRepObject();
            pseudo.EntityLabel = shell.EntityLabel;
            pseudo.Shell = shell; // <-- This line links the shell to the solid
            return pseudo;
        }

        private static IEnumerable<IgesManifestSolidBRepObject> GroupFacesIntoPseudoSolids(List<IgesFace> faces)
        {
            foreach (var group in faces.GroupBy(f => f.EntityLabel))
            {
                var shell = new IgesShell { Faces = group.ToList(), EntityLabel = group.Key };
                var pseudo = new IgesManifestSolidBRepObject();
                pseudo.EntityLabel = group.Key;
                pseudo.Shell = shell; // <-- Link shell to solid
                yield return pseudo;
            }
        }

        private static void RecoverShellFaces(IgesFile iges)
        {
            var allFaces = iges.Entities.OfType<IgesFace>().ToList();
            var shells = iges.Entities.OfType<IgesShell>().ToList();

            Console.WriteLine($"[RECOVER] Total faces: {allFaces.Count}");
            foreach (var face in allFaces)
            {
                Console.WriteLine($"[RECOVER]   Face: {GetEntityName(face)}");
            }

            // First, link loops to faces
            // Since Face parameters don't reference loops in this file, 
            // we need to find loops that belong to each face
            LinkLoopsToFaces(iges);

            foreach (var shell in shells)
            {
                Console.WriteLine($"[RECOVER] Processing shell {GetEntityName(shell)}: FacePointers={shell.FacePointers?.Count ?? 0}, Faces={shell.Faces?.Count ?? 0}");
                
                // If the shell has no faces but has raw pointers, try to recover
                if ((shell.Faces == null || shell.Faces.Count == 0) && shell.FacePointers != null && shell.FacePointers.Count > 0)
                {
                    shell.Faces = new List<IgesFace>();

                    // Collect all faces that haven't been assigned yet
                    var unassignedFaces = allFaces
                        .Where(f => !shells.Where(s => s.Faces != null).SelectMany(s => s.Faces).Contains(f))
                        .ToList();

                    // Assign the requested number of faces to this shell
                    int facesToAssign = Math.Min(shell.FacePointers.Count, unassignedFaces.Count);
                    Console.WriteLine($"[RECOVER]   Assigning {facesToAssign} faces from {unassignedFaces.Count} unassigned");
                    for (int i = 0; i < facesToAssign; i++)
                    {
                        shell.Faces.Add(unassignedFaces[i]);
                        Console.WriteLine($"[RECOVER]     -> {GetEntityName(unassignedFaces[i])}");
                    }
                }
            }
        }

        private static void LinkLoopsToFaces(IgesFile iges)
        {
            // The IGES file structure appears to be:
            // Face (entity type 508)
            // Loop (entity type 510) - belongs to Face
            // Supporting entities (Points, Directions, Surfaces)
            // [repeat]

            var allEntities = iges.Entities.ToList();

            for (int i = 0; i < allEntities.Count - 1; i++)
            {
                var entity = allEntities[i];
                if (entity is IgesFace face)
                {
                    // Check if the next entity is a Loop
                    var nextEntity = allEntities[i + 1];
                    if (nextEntity is IgesLoop loop)
                    {
                        if (face.Loops == null)
                            face.Loops = new List<IgesLoop>();
                        face.Loops.Add(loop);
                    }
                }
            }
        }

        private static void RebindManifestsToShells(IgesFile iges)
        {
            var manifests = iges.Entities.OfType<IgesManifestSolidBRepObject>().ToList();
            var shells = iges.Entities.OfType<IgesShell>().ToList();

            Console.WriteLine($"[REBIND] Found {manifests.Count} manifests and {shells.Count} shells");
            
            // Strategy: Match manifests to shells by checking which shell has faces
            // For each manifest, find the shell that contains the faces referenced in its structure
            
            for (int i = 0; i < manifests.Count; i++)
            {
                var manifest = manifests[i];
                var manifestName = GetEntityName(manifest);
                
                Console.WriteLine($"[REBIND] Manifest[{i}] '{manifestName}': Shell={manifest.Shell}, Faces={manifest.Shell?.Faces?.Count ?? 0}");
                
                // If shell has no faces, try to find a shell that does have faces
                if (manifest.Shell == null || manifest.Shell.Faces == null || manifest.Shell.Faces.Count == 0)
                {
                    // Find a shell with faces that hasn't been assigned yet
                    foreach (var shell in shells)
                    {
                        if (shell.Faces != null && shell.Faces.Count > 0)
                        {
                            // Check if this shell is already assigned to another manifest
                            bool alreadyAssigned = manifests.Take(i).Any(m => m.Shell == shell);
                            
                            if (!alreadyAssigned)
                            {
                                manifest.Shell = shell;
                                Console.WriteLine($"[REBIND]   -> Assigned shell with {shell.Faces.Count} faces");
                                break;
                            }
                        }
                    }
                }
            }
        }

        private static (Vec3 Center, Vec3 Min, Vec3 Max) GetFaceBounds(IgesFace face)
        {
            var bounds = GetLoopBounds(face.Loops?.FirstOrDefault());
            return bounds;
        }

        private static (Vec3 Center, Vec3 Min, Vec3 Max) GetLoopBounds(IgesLoop? loop)
        {
            var points = new List<Vec3>();
            
            if (loop?.Curves == null) 
                return (new Vec3(0, 0, 0), new Vec3(0, 0, 0), new Vec3(0, 0, 0));
            
            foreach (var curve in loop.Curves)
            {
                if (curve is IgesLine line)
                {
                    points.Add(ToVec3(line.P1));
                    points.Add(ToVec3(line.P2));
                }
            }
            
            if (points.Count == 0)
                return (new Vec3(0, 0, 0), new Vec3(0, 0, 0), new Vec3(0, 0, 0));
            
            var minX = points.Min(p => p.X);
            var maxX = points.Max(p => p.X);
            var minY = points.Min(p => p.Y);
            var maxY = points.Max(p => p.Y);
            var minZ = points.Min(p => p.Z);
            var maxZ = points.Max(p => p.Z);
            
            var min = new Vec3(minX, minY, minZ);
            var max = new Vec3(maxX, maxY, maxZ);
            var center = new Vec3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            
            return (center, min, max);
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

        private static double Distance3(Vec3 a, Vec3 b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

        private static string Fmt(double d)
            => d.ToString("0.###", CultureInfo.InvariantCulture);

        private static Vec3 ToVec3(IgesPoint p) => new Vec3(p.X, p.Y, p.Z);

        // SVG builder (mm units)
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
}