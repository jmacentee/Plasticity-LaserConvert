using IxMilia.Step;
using IxMilia.Step.Items;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LaserConvertProcess
{
    /// <summary>
    /// StepProcess handles STEP file parsing and SVG generation for thin solids.
    /// </summary>
    public static class StepProcess
    {
        /// <summary>
        /// Process STEP file contents and generate SVG output.
        /// </summary>
        /// <param name="fileContents">The contents of the STEP file as a string.</param>
        /// <param name="options">Processing options including thickness, tolerance, and message callback.</param>
        /// <returns>StepReturn containing the SVG output, return code, and messages.</returns>
        public static StepReturn Process(string fileContents, ProcessingOptions options)
        {
            StepReturn results = new StepReturn();
            try
            {
                DebugLog(results, options, "Parsing STEP file contents...", true);
                var stepFile = StepFile.Parse(fileContents);
                DebugLog(results, options, $"File loaded. Total items: {stepFile.Items.Count}");
                DebugLog(results, options, $"Processing with thickness={options.Thickness}mm, tolerance={options.ThicknessTolerance}mm (range: {options.MinThickness}-{options.MaxThickness}mm)");

                var solids = StepTopologyResolver.GetAllSolids(stepFile, options);
                DebugLog(results, options, $"Found {solids.Count} solids");
                if (solids.Count == 0)
                {
                    DebugLog(results, options, "No solids found in STEP file.");
                    return results;
                }

                var thinSolids = new List<(string Name, List<StepAdvancedFace> Faces)>();
                foreach (var (name, faces) in solids)
                {
                    var (vertices, _, _, dimX, dimY, dimZ) = StepTopologyResolver.ExtractVerticesAndFaceIndices(faces, stepFile, options);
                    var dimensions = new Dimensions(dimX, dimY, dimZ);
                    if (dimensions.HasThinDimension(options.MinThickness, options.MaxThickness))
                    {
                        thinSolids.Add((name, faces));
                        DebugLog(results, options, $"[FILTER] {name}: dimensions {dimensions} - PASS (target thickness: {options.Thickness}mm)");
                    }
                    else
                    {
                        DebugLog(results, options, $"Warning! [FILTER] {name}: dimensions {dimensions} - FAIL (target thickness: {options.Thickness}mm)", true);
                    }
                }

                if (thinSolids.Count == 0)
                {
                    DebugLog(results, options, $"Warning! No thin solids found matching thickness {options.Thickness}mm (+/- {options.ThicknessTolerance}mm).", true);
                    return results;
                }

                var svg = new SvgBuilder();
                foreach (var (name, faces) in thinSolids)
                {
                    ProcessSolid(results, name, faces, stepFile, svg, options);
                }

                results.ReturnCode = 1;
                results.SVGContents = svg.Build();
                return results;
            }
            catch (Exception ex)
            {
                DebugLog(results, options, $"Error: {ex.Message}", true);
                DebugLog(results, options, ex.StackTrace?.ToString() ?? "", true);
                results.ReturnCode = 2;
                return results;
            }
        }

        private static void DebugLog(StepReturn results, ProcessingOptions options, string message, bool always = false)
        {
            bool isDebugOnly = !always;
            
            // Always add to messages collection
            results.Messages.Add(new ProcessMessage(message, isDebugOnly));
            
            // Call the callback if provided and message should be shown
            if (options.OnMessage != null && (options.DebugMode || always))
            {
                options.OnMessage(message, isDebugOnly);
            }
        }

        /// <summary>
        /// Process a single solid by finding its main face and projecting to 2D.
        /// Uses curve segments to preserve arcs in the SVG output.
        /// </summary>
        private static void ProcessSolid(StepReturn results, string name, List<StepAdvancedFace> faces, StepFile stepFile, SvgBuilder svg, ProcessingOptions options)
        {
            svg.BeginGroup(name);

            // Find the PLANAR face with the LARGEST projected 2D area (this is the main surface face)
            // We only consider planar faces (StepPlane geometry) to avoid selecting cylindrical surfaces
            StepAdvancedFace bestFace = null;
            double maxProjectedArea = 0;
            List<CurveSegment> bestOuterSegments = null;
            List<List<CurveSegment>> bestHoleSegments = null;
            
            foreach (var face in faces)
            {
                // Only consider planar faces - skip cylindrical, conical, spherical, etc.
                if (!(face.FaceGeometry is StepPlane))
                {
                    continue;
                }
                
                // Extract curve segments (preserves arc geometry)
                var (outerSegments, holeSegments) = StepTopologyResolver.ExtractFaceWithHolesAsSegments(face, stepFile, options);
                if (outerSegments.Count < 2)
                    continue;
                
                // Compute the 2D bounding box area of this face from segment endpoints
                var outerPoints = outerSegments.Select(s => s.Start).ToList();
                var projectedArea = ComputeProjectedArea(outerPoints);
                
                DebugLog(results, options, $"[FACE] {name}: Planar face with {outerSegments.Count} segments, area={projectedArea:F1}");
                
                if (projectedArea > maxProjectedArea)
                {
                    maxProjectedArea = projectedArea;
                    bestFace = face;
                    bestOuterSegments = outerSegments;
                    bestHoleSegments = holeSegments;
                }
            }
            
            if (bestFace == null || bestOuterSegments == null || bestOuterSegments.Count < 2)
            {
                DebugLog(results, options, $"[{name}] No valid planar face found");
                svg.EndGroup();
                return;
            }

            // Build a coordinate frame from the outer boundary points
            var outerPoints3D = bestOuterSegments.Select(s => s.Start).ToList();
            var frame = BuildCoordinateFrame(outerPoints3D);
            
            // Project outer segments to 2D
            var outer2DSegments = bestOuterSegments.Select(s => s.ProjectTo2D(frame)).ToList();
            
            // Project hole segments to 2D
            var holes2DSegments = bestHoleSegments?.Select(h => 
                h.Select(s => s.ProjectTo2D(frame)).ToList()
            ).ToList() ?? new List<List<CurveSegment2D>>();
            
            // Check if the shape needs axis-alignment rotation
            var outer2DPoints = outer2DSegments.Select(s => s.Start).ToList();
            double alignmentAngle = ComputeAxisAlignmentAngle(outer2DPoints);
            if (Math.Abs(alignmentAngle) > 0.01)
            {
                DebugLog(results, options, $"[ALIGN] {name}: Rotating by {alignmentAngle * 180 / Math.PI:F1} degrees for axis alignment");
                double cx = outer2DPoints.Average(p => p.X);
                double cy = outer2DPoints.Average(p => p.Y);
                outer2DSegments = outer2DSegments.Select(s => s.Rotate(alignmentAngle, cx, cy)).ToList();
                holes2DSegments = holes2DSegments.Select(h => 
                    h.Select(s => s.Rotate(alignmentAngle, cx, cy)).ToList()
                ).ToList();
            }
            
            // Normalize to origin
            var allStartPoints = outer2DSegments.Select(s => s.Start).ToList();
            var minX = allStartPoints.Min(p => p.X);
            var minY = allStartPoints.Min(p => p.Y);
            
            outer2DSegments = outer2DSegments.Select(s => s.Translate(-minX, -minY)).ToList();
            holes2DSegments = holes2DSegments.Select(h => 
                h.Select(s => s.Translate(-minX, -minY)).ToList()
            ).ToList();
            
            // Build SVG path from segments using true SVG arc commands
            if (outer2DSegments.Count >= 2)
            {
                var outerPath = SvgPathBuilder.BuildPathFromSegmentsAsCurves(outer2DSegments);
                svg.Path(outerPath, 0.2, "none", "#9600c8");  // Purple for outer walls
                DebugLog(results, options, $"[SVG] {name}: Generated outline from {outer2DSegments.Count} curve segments");

                // Process holes - use red color for cutouts
                // Note: A hole can be a single segment (full circle)
                foreach (var holeSegments in holes2DSegments)
                {
                    if (holeSegments.Count >= 1)
                    {
                        var holePath = SvgPathBuilder.BuildPathFromSegmentsAsCurves(holeSegments);
                        if (!string.IsNullOrEmpty(holePath))
                        {
                            svg.Path(holePath, 0.2, "none", "#960000");  // Red for cutouts/holes
                            DebugLog(results, options, $"[SVG] {name}: Generated hole from {holeSegments.Count} curve segments");
                        }
                    }
                }
            }

            svg.EndGroup();
        }

        /// <summary>
        /// Build a coordinate frame from a StepPlane's own coordinate system.
        /// This ensures the projection is consistent with how curves are defined on the plane.
        /// </summary>
        private static (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) BuildCoordinateFrameFromPlane(StepPlane plane)
        {
            var placement = plane.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                return (0, 0, 0, 1, 0, 0, 0, 1, 0);
            }
            
            // Origin
            double ox = placement.Location?.X ?? 0;
            double oy = placement.Location?.Y ?? 0;
            double oz = placement.Location?.Z ?? 0;
            
            // Z axis (normal to plane)
            double nz_x = placement.Axis?.X ?? 0;
            double nz_y = placement.Axis?.Y ?? 0;
            double nz_z = placement.Axis?.Z ?? 1;
            double nzLen = Math.Sqrt(nz_x * nz_x + nz_y * nz_y + nz_z * nz_z);
            if (nzLen > 1e-10) { nz_x /= nzLen; nz_y /= nzLen; nz_z /= nzLen; }
            
            // X axis (RefDirection)
            double ux = placement.RefDirection?.X ?? 1;
            double uy = placement.RefDirection?.Y ?? 0;
            double uz = placement.RefDirection?.Z ?? 0;
            double uLen = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (uLen > 1e-10) { ux /= uLen; uy /= uLen; uz /= uLen; }
            
            // Y axis = Z cross X (ensures right-handed system)
            double vx = nz_y * uz - nz_z * uy;
            double vy = nz_z * ux - nz_x * uz;
            double vz = nz_x * uy - nz_y * ux;
            double vLen = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (vLen > 1e-10) { vx /= vLen; vy /= vLen; vz /= vLen; }
            
            return (ox, oy, oz, ux, uy, uz, vx, vy, vz);
        }

        /// <summary>
        /// Compute projected area using a specific frame.
        /// </summary>
        private static double ComputeProjectedAreaWithFrame(
            List<(double X, double Y, double Z)> vertices,
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            if (vertices.Count < 3)
                return 0;
            
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
