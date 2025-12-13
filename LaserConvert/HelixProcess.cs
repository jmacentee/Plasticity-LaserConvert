using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using Assimp;

namespace LaserConvert
{
    public static class HelixProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"[Assimp] Loading STEP file: {inputPath}");
                var context = new AssimpContext();
                var scene = context.ImportFile(inputPath, PostProcessSteps.Triangulate | PostProcessSteps.JoinIdenticalVertices);
                if (scene == null || scene.MeshCount == 0)
                {
                    Console.WriteLine("[Assimp] No geometry found in STEP file.");
                    return 1;
                }

                // Each mesh is a solid
                var meshes = new List<(string Name, Mesh Mesh)>();
                for (int i = 0; i < scene.MeshCount; i++)
                {
                    var mesh = scene.Meshes[i];
                    var name = !string.IsNullOrWhiteSpace(mesh.Name) ? mesh.Name : $"Solid{i + 1}";
                    meshes.Add((name, mesh));
                }
                Console.WriteLine($"[Assimp] Found {meshes.Count} solids");
                if (meshes.Count == 0)
                {
                    Console.WriteLine("[Assimp] No solids found in STEP file.");
                    return 0;
                }

                // Filter solids by thin dimension (3mm)
                const double minThickness = 2.5;
                const double maxThickness = 10.0;
                var thinSolids = new List<(string Name, Mesh Mesh, double[] Dims, int ThinAxis)>();
                foreach (var (name, mesh) in meshes)
                {
                    var dims = GetMeshDimensions(mesh);
                    var sortedDims = dims.Select((d, i) => (d, i)).OrderBy(x => x.d).ToArray();
                    var thin = sortedDims[0];
                    if (thin.d >= minThickness && thin.d <= maxThickness)
                    {
                        thinSolids.Add((name, mesh, dims, thin.i));
                        Console.WriteLine($"[Assimp] [FILTER] {name}: dimensions [{dims[0]:F1}, {dims[1]:F1}, {dims[2]:F1}] - PASS");
                    }
                    else
                    {
                        Console.WriteLine($"[Assimp] [FILTER] {name}: dimensions [{dims[0]:F1}, {dims[1]:F1}, {dims[2]:F1}] - FAIL");
                    }
                }
                if (thinSolids.Count == 0)
                {
                    Console.WriteLine("[Assimp] No thin solids found.");
                    return 0;
                }

                var svg = new SvgBuilder();
                foreach (var (name, mesh, dims, thinAxis) in thinSolids)
                {
                    svg.BeginGroup(name);
                    // 1. Rotate mesh so thin axis is Z
                    var transformedVerts = mesh.Vertices.Select(v => TransformToZ(v, thinAxis)).ToList();
                    // 2. Find the topmost face (max Z)
                    var topFaceIndices = FindTopFaceIndices(mesh, transformedVerts);
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
                    // 6. Holes: not implemented (Assimp mesh does not expose face holes directly)
                    svg.EndGroup();
                }
                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"[Assimp] Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assimp] Error: {ex.Message}");
                return 2;
            }
        }

        // Get bounding box dimensions (X, Y, Z) for mesh
        private static double[] GetMeshDimensions(Mesh mesh)
        {
            var xs = mesh.Vertices.Select(p => p.X);
            var ys = mesh.Vertices.Select(p => p.Y);
            var zs = mesh.Vertices.Select(p => p.Z);
            return new[] { xs.Max() - xs.Min(), ys.Max() - ys.Min(), zs.Max() - zs.Min() };
        }

        // Transform vertex so thin axis is Z
        private static Assimp.Vector3D TransformToZ(Assimp.Vector3D v, int thinAxis)
        {
            // thinAxis: 0=X, 1=Y, 2=Z
            if (thinAxis == 2) return v;
            if (thinAxis == 0) return new Assimp.Vector3D(v.Y, v.Z, v.X); // X->Z
            return new Assimp.Vector3D(v.X, v.Z, v.Y); // Y->Z
        }

        // Find the indices of the topmost face (max Z) in the mesh
        private static List<int> FindTopFaceIndices(Mesh mesh, List<Assimp.Vector3D> verts)
        {
            var tris = new List<(int[] Indices, double AvgZ)>();
            for (int i = 0; i < mesh.FaceCount; i++)
            {
                var face = mesh.Faces[i];
                if (face.IndexCount != 3) continue;
                var idx = new[] { face.Indices[0], face.Indices[1], face.Indices[2] };
                var zs = idx.Select(j => verts[j].Z).ToArray();
                tris.Add((idx, zs.Average()));
            }
            if (tris.Count == 0) return null;
            var maxZ = tris.Max(t => t.AvgZ);
            var topTris = tris.Where(t => Math.Abs(t.AvgZ - maxZ) < 0.1).ToList();
            if (topTris.Count == 0) return null;
            var indices = topTris.SelectMany(t => t.Indices).Distinct().ToList();
            return indices;
        }

        // Order perimeter vertices in 2D (X/Y) using nearest-neighbor
        private static List<Assimp.Vector3D> OrderPerimeter2D(List<Assimp.Vector3D> verts)
        {
            if (verts.Count < 3) return verts;
            var used = new HashSet<int>();
            var ordered = new List<Assimp.Vector3D> { verts[0] };
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
