using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IxMilia.Step;
using IxMilia.Step.Items;

namespace LaserConvert
{
    /// <summary>
    /// StepProcess handles STEP file parsing and SVG generation for thin solids.
    /// </summary>
    internal static class StepProcess
    {
        public static int Main(string inputPath, string outputPath)
        {
            try
            {
                Console.WriteLine($"Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                Console.WriteLine($"File loaded. Total items: {stepFile.Items.Count}");

                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                Console.WriteLine($"Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    Console.WriteLine("No solids found in STEP file.");
                    return 0;
                }

                const double minThickness = 2.5;
                const double maxThickness = 10.0;

                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, _, _, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, faces));
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

                var svg = new SvgBuilder();
                foreach (var (name, faces) in thinSolids)
                {
                    ProcessSolid(name, faces, stepFile, svg);
                }

                File.WriteAllText(outputPath, svg.Build());
                Console.WriteLine($"Wrote SVG: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return 2;
            }
        }

        /// <summary>
        /// Process a single solid by finding its main face and projecting to 2D.
        /// </summary>
        private static void ProcessSolid(string name, List<StepAdvancedFace> faces, StepFile stepFile, SvgBuilder svg)
        {
            svg.BeginGroup(name);

            // Find the face with most boundary vertices (this is the main surface face)
            StepAdvancedFace bestFace = null;
            int maxBoundaryVerts = 0;
            List<(double X, double Y, double Z)> bestOuterVerts = null;
            List<List<(double X, double Y, double Z)>> bestHoleVerts = null;
            
            foreach (var face in faces)
            {
                var (outerVerts, holeVerts) = StepTopologyResolver.ExtractFaceWithHoles(face, stepFile);
                if (outerVerts.Count > maxBoundaryVerts)
                {
                    maxBoundaryVerts = outerVerts.Count;
                    bestFace = face;
                    bestOuterVerts = outerVerts;
                    bestHoleVerts = holeVerts;
                }
            }
            
            if (bestFace == null || bestOuterVerts == null || bestOuterVerts.Count < 3)
            {
                Console.WriteLine($"[{name}] No valid face found");
                svg.EndGroup();
                return;
            }

            // Build a coordinate frame from the outer boundary
            // This same frame will be used for holes to preserve relative positions
            var frame = BuildCoordinateFrame(bestOuterVerts);
            
            // Project outer boundary using the frame
            var outer2D = ProjectWithFrame(bestOuterVerts, frame);
            
            // Project holes using the SAME frame
            var holes2D = bestHoleVerts?.Select(h => ProjectWithFrame(h, frame)).ToList() 
                ?? new List<List<(double X, double Y)>>();
			
            // Normalize to origin and round
            var minX = outer2D.Min(p => p.X);
            var minY = outer2D.Min(p => p.Y);
            
            var normalizedOuter = outer2D.Select(p => ((long)Math.Round(p.X - minX), (long)Math.Round(p.Y - minY))).ToList();
            var normalizedHoles = holes2D.Select(h => 
                h.Select(p => ((long)Math.Round(p.X - minX), (long)Math.Round(p.Y - minY))).ToList()
            ).ToList();

            // Remove consecutive duplicates
            normalizedOuter = RemoveConsecutiveDuplicates(normalizedOuter);

            // Order perimeter vertices
            var orderedOuter = OrderPerimeter(normalizedOuter);

            // Output to SVG
            if (orderedOuter.Count >= 3)
            {
                var outerPath = SvgPathBuilder.BuildPath(orderedOuter);
                svg.Path(outerPath, 0.2, "none", "#000");
                Console.WriteLine($"[SVG] {name}: Generated outline from {orderedOuter.Count} vertices");

                // Process holes
                foreach (var hole2D in normalizedHoles)
                {
                    var dedupedHole = RemoveConsecutiveDuplicates(hole2D);
                    if (dedupedHole.Count >= 3)
                    {
                        var orderedHole = OrderPerimeter(dedupedHole);
                        var holePath = SvgPathBuilder.BuildPath(orderedHole);
                        svg.Path(holePath, 0.2, "none", "#f00");
                    }
                }
            }

            svg.EndGroup();
        }

        /// <summary>
        /// Build a coordinate frame (origin, X axis, Y axis) from a set of 3D points.
        /// </summary>
        private static (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) BuildCoordinateFrame(
            List<(double X, double Y, double Z)> points)
        {
            if (points.Count < 3)
            {
                return (0, 0, 0, 1, 0, 0, 0, 1, 0);
            }
            
            var p0 = points[0];
            var p1 = points[1 % points.Count];
            var p2 = points[2 % points.Count];
            
            // Vector from p0 to p1 (first edge - this will become the X axis)
            var v1x = p1.X - p0.X;
            var v1y = p1.Y - p0.Y;
            var v1z = p1.Z - p0.Z;
            var v1len = Math.Sqrt(v1x * v1x + v1y * v1y + v1z * v1z);
            if (v1len < 1e-10) v1len = 1;
            
            // Vector from p0 to p2 
            var v2x = p2.X - p0.X;
            var v2y = p2.Y - p0.Y;
            var v2z = p2.Z - p0.Z;
            
            // Normal = v1 x v2
            var nx = v1y * v2z - v1z * v2y;
            var ny = v1z * v2x - v1x * v2z;
            var nz = v1x * v2y - v1y * v2x;
            var nlen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nlen < 1e-10)
            {
                // Degenerate case - use default frame
                return (p0.X, p0.Y, p0.Z, 1, 0, 0, 0, 1, 0);
            }
            nx /= nlen; ny /= nlen; nz /= nlen;
            
            // X axis = normalized v1
            var ux = v1x / v1len;
            var uy = v1y / v1len;
            var uz = v1z / v1len;
            
            // Y axis = normal x X (so it's perpendicular to both)
            var vx = ny * uz - nz * uy;
            var vy = nz * ux - nx * uz;
            var vz = nx * uy - ny * ux;
            var vlen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (vlen < 1e-10) vlen = 1;
            vx /= vlen; vy /= vlen; vz /= vlen;
            
            return (p0.X, p0.Y, p0.Z, ux, uy, uz, vx, vy, vz);
        }

        /// <summary>
        /// Project 3D points using a coordinate frame.
        /// </summary>
        private static List<(double X, double Y)> ProjectWithFrame(
            List<(double X, double Y, double Z)> points,
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            var result = new List<(double, double)>();
            foreach (var p in points)
            {
                // Vector from origin to this point
                var dx = p.X - frame.OX;
                var dy = p.Y - frame.OY;
                var dz = p.Z - frame.OZ;
                
                // Dot product with U and V axes
                var u = dx * frame.UX + dy * frame.UY + dz * frame.UZ;
                var v = dx * frame.VX + dy * frame.VY + dz * frame.VZ;
                
                result.Add((u, v));
            }
            return result;
        }

        /// <summary>
        /// Remove consecutive duplicate points.
        /// </summary>
        private static List<(long X, long Y)> RemoveConsecutiveDuplicates(List<(long X, long Y)> points)
        {
            if (points.Count <= 1) return points.ToList();
            
            var result = new List<(long X, long Y)> { points[0] };
            for (int i = 1; i < points.Count; i++)
            {
                if (points[i] != points[i - 1])
                {
                    result.Add(points[i]);
                }
            }
            
            if (result.Count > 2 && result.Last() == result[0])
            {
                result.RemoveAt(result.Count - 1);
            }
            
            return result;
        }

        /// <summary>
        /// Order perimeter vertices to form a proper closed polygon using nearest neighbor.
        /// </summary>
        private static List<(long X, long Y)> OrderPerimeter(List<(long X, long Y)> vertices)
        {
            if (vertices.Count <= 3)
            {
                return vertices.ToList();
            }
            
            var uniqueVerts = vertices.Distinct().ToList();
            if (uniqueVerts.Count <= 3)
            {
                return uniqueVerts;
            }

            var result = new List<(long X, long Y)>();
            var used = new HashSet<int>();

            // Start from bottom-left vertex
            int current = 0;
            for (int i = 1; i < uniqueVerts.Count; i++)
            {
                if (uniqueVerts[i].Y < uniqueVerts[current].Y ||
                    (uniqueVerts[i].Y == uniqueVerts[current].Y && 
                     uniqueVerts[i].X < uniqueVerts[current].X))
                {
                    current = i;
                }
            }

            while (used.Count < uniqueVerts.Count)
            {
                result.Add(uniqueVerts[current]);
                used.Add(current);

                if (used.Count >= uniqueVerts.Count)
                    break;

                int next = -1;
                long minDist = long.MaxValue;

                for (int i = 0; i < uniqueVerts.Count; i++)
                {
                    if (used.Contains(i))
                        continue;

                    long dx = uniqueVerts[i].X - uniqueVerts[current].X;
                    long dy = uniqueVerts[i].Y - uniqueVerts[current].Y;
                    long distSq = dx * dx + dy * dy;

                    if (distSq < minDist)
                    {
                        minDist = distSq;
                        next = i;
                    }
                }

                if (next == -1)
                    break;

                current = next;
            }

            return result;
        }
    }
}
