using IxMilia.Step;
using IxMilia.Step.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LaserConvert
{
    /// <summary>
    /// StepProcess handles STEP file parsing and SVG generation for thin solids.
    /// </summary>
    internal static class StepProcess
    {
        private static bool _debugMode = false;
        
        public static int Main(string inputPath, string outputPath, bool debugMode = false)
        {
            _debugMode = debugMode;
            
            try
            {
                Console.WriteLine($"Loading STEP file: {inputPath}");
                var stepFile = StepFile.Load(inputPath);
                DebugLog($"File loaded. Total items: {stepFile.Items.Count}");

                var solids = StepTopologyResolver.GetAllSolids(stepFile);
                DebugLog($"Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    DebugLog("No solids found in STEP file.");
                    return 0;
                }

                const double minThickness = 2.5;
                const double maxThickness = 10.0;

                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, _, _, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile, debugMode);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    if (dimensions.HasThinDimension(minThickness, maxThickness))
                    {
                        thinSolids.Add((name, faces));
                        DebugLog($"[FILTER] {name}: dimensions {dimensions} - PASS");
                    }
                    else
                    {
                        DebugLog($"Warning! [FILTER] {name}: dimensions {dimensions} - FAIL",true);
                    }
                }

                if (thinSolids.Count == 0)
                {
                    DebugLog($"Warning! No thin solids found.",true);
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

        private static void DebugLog(string message, bool Always = false)
        {
            if (_debugMode || Always)
            {
                Console.WriteLine(message);
            }
        }

        /// <summary>
        /// Process a single solid by finding its main face and projecting to 2D.
        /// </summary>
        private static void ProcessSolid(string name, List<StepAdvancedFace> faces, StepFile stepFile, SvgBuilder svg)
        {
            svg.BeginGroup(name);

            // Find the face with the LARGEST projected 2D area (this is the main surface face)
            // This correctly handles wall-type shapes where we want the large flat face, not thin edges
            StepAdvancedFace bestFace = null;
            double maxProjectedArea = 0;
            List<(double X, double Y, double Z)> bestOuterVerts = null;
            List<List<(double X, double Y, double Z)>> bestHoleVerts = null;
            
            foreach (var face in faces)
            {
                var (outerVerts, holeVerts) = StepTopologyResolver.ExtractFaceWithHoles(face, stepFile, _debugMode);
                if (outerVerts.Count < 3)
                    continue;
                
                // Compute the 2D bounding box area of this face
                // This represents the "flat" area when projected to its natural plane
                var projectedArea = ComputeProjectedArea(outerVerts);
                
                if (projectedArea > maxProjectedArea)
                {
                    maxProjectedArea = projectedArea;
                    bestFace = face;
                    bestOuterVerts = outerVerts;
                    bestHoleVerts = holeVerts;
                }
            }
            
            if (bestFace == null || bestOuterVerts == null || bestOuterVerts.Count < 3)
            {
                DebugLog($"[{name}] No valid face found");
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
			
            // Check if the shape needs axis-alignment rotation
            // This handles cases where the shape's natural edges are at 45-degree angles
            double alignmentAngle = ComputeAxisAlignmentAngle(outer2D);
            if (Math.Abs(alignmentAngle) > 0.01) // More than ~0.5 degrees
            {
                DebugLog($"[ALIGN] {name}: Rotating by {alignmentAngle * 180 / Math.PI:F1} degrees for axis alignment");
                outer2D = RotatePoints2D(outer2D, alignmentAngle);
                holes2D = holes2D.Select(h => RotatePoints2D(h, alignmentAngle)).ToList();
            }
            
            // Normalize to origin and round
            var minX = outer2D.Min(p => p.X);
            var minY = outer2D.Min(p => p.Y);
            
            // Use MidpointRounding.AwayFromZero to ensure consistent rounding (e.g., 2.5 -> 3, not 2)
            var normalizedOuter = outer2D.Select(p => (
                (long)Math.Round(p.X - minX, MidpointRounding.AwayFromZero), 
                (long)Math.Round(p.Y - minY, MidpointRounding.AwayFromZero)
            )).ToList();
            var normalizedHoles = holes2D.Select(h => 
                h.Select(p => (
                    (long)Math.Round(p.X - minX, MidpointRounding.AwayFromZero), 
                    (long)Math.Round(p.Y - minY, MidpointRounding.AwayFromZero)
                )).ToList()
            ).ToList();

            // Remove consecutive duplicates (but preserve order!)
            normalizedOuter = RemoveConsecutiveDuplicates(normalizedOuter);

            // DO NOT REORDER - vertices are already in correct edge order from STEP file
            var orderedOuter = normalizedOuter;

            // Output to SVG
            if (orderedOuter.Count >= 3)
            {
                var outerPath = SvgPathBuilder.BuildPath(orderedOuter);
                svg.Path(outerPath, 0.2, "none", "#9600c8");
                DebugLog($"[SVG] {name}: Generated outline from {orderedOuter.Count} vertices");

                // Process holes - also preserve their original order
                foreach (var hole2D in normalizedHoles)
                {
                    var dedupedHole = RemoveConsecutiveDuplicates(hole2D);
                    if (dedupedHole.Count >= 3)
                    {
                        var holePath = SvgPathBuilder.BuildPath(dedupedHole);
                        svg.Path(holePath, 0.2, "none", "#960000");
                    }
                }
            }

            svg.EndGroup();
        }

        /// <summary>
        /// Compute the angle needed to align the shape's dominant edges with the X or Y axis.
        /// Returns the rotation angle in radians.
        /// </summary>
        private static double ComputeAxisAlignmentAngle(List<(double X, double Y)> points)
        {
            if (points.Count < 3)
                return 0;
            
            // Find the longest edges and their angles
            var edgeAngles = new List<(double Length, double Angle)>();
            int n = points.Count;
            
            for (int i = 0; i < n; i++)
            {
                var p1 = points[i];
                var p2 = points[(i + 1) % n];
                
                double dx = p2.X - p1.X;
                double dy = p2.Y - p1.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                
                if (length < 1.0) // Skip very short edges
                    continue;
                
                // Compute angle of this edge (0 to 180 degrees, normalized)
                double angle = Math.Atan2(dy, dx);
                
                // Normalize angle to 0-90 range (we don't care about direction)
                while (angle < 0) angle += Math.PI;
                while (angle >= Math.PI) angle -= Math.PI;
                if (angle > Math.PI / 2) angle = Math.PI - angle;
                
                edgeAngles.Add((length, angle));
            }
            
            if (edgeAngles.Count == 0)
                return 0;
            
            // Check if most edges are already axis-aligned (within a few degrees)
            const double axisThreshold = 5.0 * Math.PI / 180.0; // 5 degrees
            int axisAlignedCount = edgeAngles.Count(e => 
                Math.Abs(e.Angle) < axisThreshold || 
                Math.Abs(e.Angle - Math.PI / 2) < axisThreshold);
            
            // If more than 60% of edges are already axis-aligned, don't rotate
            if (axisAlignedCount > edgeAngles.Count * 0.6)
                return 0;
            
            // Find the dominant angle (weighted by edge length)
            // Check if there's a consistent diagonal angle (like 45 degrees)
            const double diagonalAngle = Math.PI / 4; // 45 degrees
            const double diagonalThreshold = 10.0 * Math.PI / 180.0; // 10 degrees tolerance
            
            double diagonalWeight = 0;
            double totalWeight = 0;
            double weightedDiagonalAngle = 0;
            
            foreach (var (length, angle) in edgeAngles)
            {
                totalWeight += length;
                
                // Check if this edge is near 45 degrees
                if (Math.Abs(angle - diagonalAngle) < diagonalThreshold)
                {
                    diagonalWeight += length;
                    weightedDiagonalAngle += angle * length;
                }
            }
            
            // If significant portion of edges are at ~45 degrees, rotate to align
            if (diagonalWeight > totalWeight * 0.3 && diagonalWeight > 0)
            {
                double avgDiagonalAngle = weightedDiagonalAngle / diagonalWeight;
                // Rotate to make this angle become 0 (horizontal) or 90 (vertical)
                // We want to rotate by the angle itself to make it horizontal
                return -avgDiagonalAngle;
            }
            
            return 0;
        }

        /// <summary>
        /// Rotate 2D points around the centroid by the given angle.
        /// </summary>
        private static List<(double X, double Y)> RotatePoints2D(List<(double X, double Y)> points, double angle)
        {
            if (Math.Abs(angle) < 0.0001 || points.Count == 0)
                return points;
            
            // Compute centroid
            double cx = points.Average(p => p.X);
            double cy = points.Average(p => p.Y);
            
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            
            return points.Select(p =>
            {
                double dx = p.X - cx;
                double dy = p.Y - cy;
                double rx = dx * cos - dy * sin + cx;
                double ry = dx * sin + dy * cos + cy;
                return (rx, ry);
            }).ToList();
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
        /// Remove consecutive duplicate points while preserving order.
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
            
            // Also check if last point equals first (closing loop) - remove if so
            if (result.Count > 2 && result.Last() == result[0])
            {
                result.RemoveAt(result.Count - 1);
            }
            
            // Remove collinear points (points that lie on a straight line between neighbors)
            result = RemoveCollinearPoints(result);
            
            return result;
        }

        /// <summary>
        /// Remove points that are collinear with their neighbors (they don't contribute to the shape).
        /// This handles cases where the STEP file has extra edge subdivisions.
        /// </summary>
        private static List<(long X, long Y)> RemoveCollinearPoints(List<(long X, long Y)> points)
        {
            if (points.Count <= 3) return points.ToList();
            
            var result = new List<(long X, long Y)>();
            int n = points.Count;
            
            for (int i = 0; i < n; i++)
            {
                var prev = points[(i - 1 + n) % n];
                var curr = points[i];
                var next = points[(i + 1) % n];
                
                // Check if curr is collinear with prev and next
                // Cross product of (curr-prev) and (next-curr) should be 0 for collinear points
                long dx1 = curr.X - prev.X;
                long dy1 = curr.Y - prev.Y;
                long dx2 = next.X - curr.X;
                long dy2 = next.Y - curr.Y;
                
                long cross = dx1 * dy2 - dy1 * dx2;
                
                // Keep the point if it's NOT collinear (cross product != 0)
                if (cross != 0)
                {
                    result.Add(curr);
                }
            }
            
            return result.Count >= 3 ? result : points.ToList();
        }

        /// <summary>
        /// Compute the projected 2D area of a set of 3D vertices.
        /// Projects the vertices onto the best-fit plane and computes the polygon area.
        /// </summary>
        private static double ComputeProjectedArea(List<(double X, double Y, double Z)> vertices)
        {
            if (vertices.Count < 3)
                return 0;
            
            // Build coordinate frame for this face
            var frame = BuildCoordinateFrame(vertices);
            
            // Project to 2D
            var projected = ProjectWithFrame(vertices, frame);
            
            // Compute polygon area using shoelace formula
            double area = 0;
            int n = projected.Count;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += projected[i].X * projected[j].Y;
                area -= projected[j].X * projected[i].Y;
            }
            
            return Math.Abs(area) / 2.0;
        }
    }
}
