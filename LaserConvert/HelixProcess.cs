using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using HelixToolkit.Wpf; // HelixToolkit for STEP import and mesh/geometry
using System.Windows.Media.Media3D;

namespace LaserConvert
{
    public static class HelixProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"[Helix] Loading STEP file: {inputPath}");
                var importer = new HelixToolkit.Wpf.StepReader();
                var model3D = importer.Read(inputPath);
                if (model3D == null)
                {
                    Console.WriteLine("[Helix] No geometry found in STEP file.");
                    return 1;
                }

                // Traverse the Model3D tree to find all MeshGeometry3D objects (one per solid)
                var meshes = new List<(string Name, MeshGeometry3D Mesh)>();
                TraverseModel(model3D, meshes);
                Console.WriteLine($"[Helix] Found {meshes.Count} solids");
                if (meshes.Count == 0)
                {
                    Console.WriteLine("[Helix] No solids found in STEP file.");
                    return 0;
                }

                // Filter solids by thin dimension (3mm)
                const double minThickness = 2.5;
                const double maxThickness = 10.0;
                var thinSolids = new List<(string Name, MeshGeometry3D Mesh, double[] Dims, int ThinAxis)>();
                foreach (var (name, mesh) in meshes)
                {
                    var dims = GetMeshDimensions(mesh);
                    var sortedDims = dims.Select((d, i) => (d, i)).OrderBy(x => x.d).ToArray();
                    var thin = sortedDims[0];
                    if (thin.d >= minThickness && thin.d <= maxThickness)
                    {
                        thinSolids.Add((name, mesh, dims, thin.i));
                        Console.WriteLine($"[Helix] [FILTER] {name}: dimensions [{dims[0]:F1}, {dims[1]:F1}, {dims[2]:F1}] - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[Helix] [FILTER] {name}: dimensions [{dims[0]:F1}, {dims[1]:F1}, {dims[2]:F1}] - FAIL");
                    }
                }
                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("[Helix] No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                foreach (var (name, mesh, dims, thinAxis) in thinSolids)
                {
                    svg.BeginGroup(name);
                    // 1. Rotate mesh so thin axis is Z
                    var transform = GetAxisAlignmentTransform(mesh, thinAxis);
                    var transformedVerts = mesh.Positions.Select(p => transform.Transform(p)).ToList();
                    // 2. Find the topmost face (max Z)
                    var topFaceIndices = FindTopFaceIndices(mesh, transform);
                    if (topFaceIndices == null)
                    {
                        svg.EndGroup();
                        continue;
                    }
                    // 3. Get ordered perimeter of the top face
                    var topFaceVerts = topFaceIndices.Select(i => transformedVerts[i]).ToList();
                    var ordered = OrderPerimeter2D(topFaceVerts);
                    // 4. Normalize to 0,0 and round
                    var minX = ordered.Min(v => v.X);
                    var minY = ordered.Min(v => v.Y);
                    var pts2d = ordered.Select(v => ((long)Math.Round(v.X - minX), (long)Math.Round(v.Y - minY))).ToList();
                    // 5. Build SVG path
                    var path = BuildPerimeterPath(pts2d);
                    svg.Path(path, 0.2, "none", "#000");
                    // 6. Find and render holes (inner loops)
                    var holes = FindHoles(mesh, transform, topFaceIndices);
                    foreach (var hole in holes)
                    {
                        var hminX = hole.Min(v => v.X);
                        var hminY = hole.Min(v => v.Y);
                        var hpts2d = hole.Select(v => ((long)Math.Round(v.X - minX), (long)Math.Round(v.Y - minY))).ToList();
                        var hpath = BuildPerimeterPath(hpts2d);
                        svg.Path(hpath, 0.2, "none", "#FF0000");
                    }
                    svg.EndGroup();
                }
                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"[Helix] Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Helix] Error: {ex.Message}");
                return 2;
            }
        }

        // Recursively traverse Model3D tree to collect all MeshGeometry3D
        private static void TraverseModel(Model3D model, List<(string Name, MeshGeometry3D Mesh)> meshes)
        {
            if (model is GeometryModel3D geom && geom.Geometry is MeshGeometry3D mesh)
            {
                var name = geom.GetName() ?? $"Solid{meshes.Count + 1}";
                meshes.Add((name, mesh));
            }
            if (model is Model3DGroup group)
            {
                foreach (var child in group.Children)
                    TraverseModel(child, meshes);
            }
        }

        // Get bounding box dimensions (X, Y, Z) for mesh
        private static double[] GetMeshDimensions(MeshGeometry3D mesh)
        {
            var xs = mesh.Positions.Select(p => p.X);
            var ys = mesh.Positions.Select(p => p.Y);
            var zs = mesh.Positions.Select(p => p.Z);
            return new[] { xs.Max() - xs.Min(), ys.Max() - ys.Min(), zs.Max() - zs.Min() };
        }

        // Build a transform that aligns the thin axis to Z
        private static Transform3D GetAxisAlignmentTransform(MeshGeometry3D mesh, int thinAxis)
        {
            // thinAxis: 0=X, 1=Y, 2=Z
            if (thinAxis == 2) return Transform3D.Identity;
            // Build rotation matrix to swap thinAxis to Z
            if (thinAxis == 0)
            {
                // X->Z: rotate -90° about Y
                return new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 1, 0), -90));
            }
            else
            {
                // Y->Z: rotate 90° about X
                return new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 90));
            }
        }

        // Find the indices of the topmost face (max Z) in the mesh
        private static List<int> FindTopFaceIndices(MeshGeometry3D mesh, Transform3D transform)
        {
            // For each triangle, compute average Z after transform
            var tris = new List<(int[] Indices, double AvgZ)>();
            for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
            {
                var idx = new[] { mesh.TriangleIndices[i], mesh.TriangleIndices[i + 1], mesh.TriangleIndices[i + 2] };
                var zs = idx.Select(j => transform.Transform(mesh.Positions[j]).Z).ToArray();
                tris.Add((idx, zs.Average()));
            }
            if (tris.Count == 0) return null;
            var maxZ = tris.Max(t => t.AvgZ);
            // Get all triangles at maxZ (tolerance)
            var topTris = tris.Where(t => Math.Abs(t.AvgZ - maxZ) < 0.1).ToList();
            if (topTris.Count == 0) return null;
            // Collect all unique vertex indices from these triangles
            var indices = topTris.SelectMany(t => t.Indices).Distinct().ToList();
            return indices;
        }

        // Order perimeter vertices in 2D (X/Y) using nearest-neighbor
        private static List<Point3D> OrderPerimeter2D(List<Point3D> verts)
        {
            if (verts.Count < 3) return verts;
            var used = new HashSet<int>();
            var ordered = new List<Point3D> { verts[0] };
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

        // Find holes (inner loops) in the top face
        private static List<List<Point3D>> FindHoles(MeshGeometry3D mesh, Transform3D transform, List<int> topFaceIndices)
        {
            // This is a placeholder: HelixToolkit does not expose face topology directly.
            // For now, return empty (no holes). Advanced: use mesh adjacency to find inner loops.
            return new List<List<Point3D>>();
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
