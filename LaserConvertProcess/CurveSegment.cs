using System;
using System.Collections.Generic;

namespace LaserConvertProcess
{
    /// <summary>
    /// Represents a segment of a path that can be a line, arc, or bezier curve.
    /// </summary>
    internal abstract class CurveSegment
    {
        /// <summary>
        /// Starting point of the segment in 3D space.
        /// </summary>
        public (double X, double Y, double Z) Start { get; set; }

        /// <summary>
        /// Ending point of the segment in 3D space.
        /// </summary>
        public (double X, double Y, double Z) End { get; set; }

        /// <summary>
        /// Project this segment to 2D using the given coordinate frame.
        /// </summary>
        public abstract CurveSegment2D ProjectTo2D(
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame);
    }

    /// <summary>
    /// A straight line segment.
    /// </summary>
    internal class LineSegment : CurveSegment
    {
        public override CurveSegment2D ProjectTo2D(
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            return new LineSegment2D
            {
                Start = Project(Start, frame),
                End = Project(End, frame)
            };
        }

        private static (double X, double Y) Project((double X, double Y, double Z) p,
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            var dx = p.X - frame.OX;
            var dy = p.Y - frame.OY;
            var dz = p.Z - frame.OZ;
            var u = dx * frame.UX + dy * frame.UY + dz * frame.UZ;
            var v = dx * frame.VX + dy * frame.VY + dz * frame.VZ;
            return (u, v);
        }
    }

    /// <summary>
    /// A circular arc segment.
    /// </summary>
    internal class ArcSegment : CurveSegment
    {
        /// <summary>
        /// Center point of the arc in 3D space.
        /// </summary>
        public (double X, double Y, double Z) Center { get; set; }

        /// <summary>
        /// Radius of the arc.
        /// </summary>
        public double Radius { get; set; }

        /// <summary>
        /// Normal vector of the arc plane.
        /// </summary>
        public (double X, double Y, double Z) Normal { get; set; }

        /// <summary>
        /// Reference direction (X axis) of the arc plane.
        /// </summary>
        public (double X, double Y, double Z) RefDirection { get; set; }

        /// <summary>
        /// Start angle in radians (in the arc's local coordinate system).
        /// </summary>
        public double StartAngle { get; set; }

        /// <summary>
        /// End angle in radians (in the arc's local coordinate system).
        /// </summary>
        public double EndAngle { get; set; }

        /// <summary>
        /// True if the arc goes clockwise (when viewed from the normal direction).
        /// This is set based on the edge orientation in the STEP file.
        /// </summary>
        public bool Clockwise { get; set; }

        public override CurveSegment2D ProjectTo2D(
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            var start2D = Project(Start, frame);
            var end2D = Project(End, frame);
            var center2D = Project(Center, frame);

            // Compute the radius in 2D from the projected points
            var dxS = start2D.X - center2D.X;
            var dyS = start2D.Y - center2D.Y;
            var radius2D = Math.Sqrt(dxS * dxS + dyS * dyS);

            // Compute 2D angles
            var startAngle2D = Math.Atan2(dyS, dxS);
            var dxE = end2D.X - center2D.X;
            var dyE = end2D.Y - center2D.Y;
            var endAngle2D = Math.Atan2(dyE, dxE);

            // Determine if the projection flips the arc direction
            // The projection plane normal is U x V
            var frameNormalX = frame.UY * frame.VZ - frame.UZ * frame.VY;
            var frameNormalY = frame.UZ * frame.VX - frame.UX * frame.VZ;
            var frameNormalZ = frame.UX * frame.VY - frame.UY * frame.VX;

            // Dot product of arc normal with frame normal
            var dot = Normal.X * frameNormalX + Normal.Y * frameNormalY + Normal.Z * frameNormalZ;

            // Determine the direction in 2D
            // In 3D: Clockwise=false means CCW when viewed from normal
            // If dot > 0: normal aligns with view, direction preserved
            // If dot < 0: normal opposes view, direction flipped
            bool goClockwiseIn2D = (dot >= 0) ? Clockwise : !Clockwise;

            // Compute the angular sweep from start to end
            double angularSweep;
            if (goClockwiseIn2D)
            {
                // Clockwise: go from startAngle to endAngle in decreasing direction
                angularSweep = startAngle2D - endAngle2D;
                while (angularSweep <= 0) angularSweep += 2 * Math.PI;
                angularSweep = -angularSweep;  // Negative for clockwise
            }
            else
            {
                // Counter-clockwise: go from startAngle to endAngle in increasing direction
                angularSweep = endAngle2D - startAngle2D;
                while (angularSweep <= 0) angularSweep += 2 * Math.PI;
                // Positive for CCW
            }

            // Compute flags for SVG
            bool largeArc = Math.Abs(angularSweep) > Math.PI;
            bool sweepFlag = goClockwiseIn2D;  // SVG: sweep=1 is CW

            return new ArcSegment2D
            {
                Start = start2D,
                End = end2D,
                Center = center2D,
                RadiusX = radius2D,
                RadiusY = radius2D,
                LargeArcFlag = largeArc,
                SweepFlag = sweepFlag,
                AngularSweep = angularSweep
            };
        }

        private static (double X, double Y) Project((double X, double Y, double Z) p,
            (double OX, double OY, double OZ, double UX, double UY, double UZ, double VX, double VY, double VZ) frame)
        {
            var dx = p.X - frame.OX;
            var dy = p.Y - frame.OY;
            var dz = p.Z - frame.OZ;
            var u = dx * frame.UX + dy * frame.UY + dz * frame.UZ;
            var v = dx * frame.VX + dy * frame.VY + dz * frame.VZ;
            return (u, v);
        }
    }

    /// <summary>
    /// Base class for 2D curve segments (after projection).
    /// </summary>
    internal abstract class CurveSegment2D
    {
        public (double X, double Y) Start { get; set; }
        public (double X, double Y) End { get; set; }

        /// <summary>
        /// Apply rotation around a center point.
        /// </summary>
        public abstract CurveSegment2D Rotate(double angle, double cx, double cy);

        /// <summary>
        /// Apply translation.
        /// </summary>
        public abstract CurveSegment2D Translate(double dx, double dy);

        /// <summary>
        /// Generate SVG path command for this segment (polyline version).
        /// </summary>
        public abstract string ToSvgPathCommand();
        
        /// <summary>
        /// Generate SVG path command using true curves (arc version).
        /// </summary>
        public abstract string ToSvgArcCommand();
    }

    /// <summary>
    /// A 2D line segment.
    /// </summary>
    internal class LineSegment2D : CurveSegment2D
    {
        public override CurveSegment2D Rotate(double angle, double cx, double cy)
        {
            return new LineSegment2D
            {
                Start = RotatePoint(Start, angle, cx, cy),
                End = RotatePoint(End, angle, cx, cy)
            };
        }

        public override CurveSegment2D Translate(double dx, double dy)
        {
            return new LineSegment2D
            {
                Start = (Start.X + dx, Start.Y + dy),
                End = (End.X + dx, End.Y + dy)
            };
        }

        public override string ToSvgPathCommand()
        {
            // Use relative line command for consistency
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            return $"l {dx:F3},{dy:F3}";
        }
        
        public override string ToSvgArcCommand()
        {
            // Lines are the same in both versions
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            return $"l {dx:F3},{dy:F3}";
        }

        private static (double X, double Y) RotatePoint((double X, double Y) p, double angle, double cx, double cy)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var dx = p.X - cx;
            var dy = p.Y - cy;
            return (dx * cos - dy * sin + cx, dx * sin + dy * cos + cy);
        }
    }

    /// <summary>
    /// A 2D arc segment.
    /// </summary>
    internal class ArcSegment2D : CurveSegment2D
    {
        public double RadiusX { get; set; }
        public double RadiusY { get; set; }
        public double XAxisRotation { get; set; }
        public bool LargeArcFlag { get; set; }
        public bool SweepFlag { get; set; }
        
        /// <summary>
        /// Center of the arc (used for rotation calculations).
        /// </summary>
        public (double X, double Y) Center { get; set; }
        
        /// <summary>
        /// The angular sweep in radians (positive = CCW, negative = CW).
        /// This is computed during projection and preserved through transformations.
        /// </summary>
        public double AngularSweep { get; set; }

        public override CurveSegment2D Rotate(double angle, double cx, double cy)
        {
            var newStart = RotatePoint(Start, angle, cx, cy);
            var newEnd = RotatePoint(End, angle, cx, cy);
            var newCenter = RotatePoint(Center, angle, cx, cy);
            
            // Angular sweep direction is preserved through rotation
            return new ArcSegment2D
            {
                Start = newStart,
                End = newEnd,
                Center = newCenter,
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                XAxisRotation = XAxisRotation + angle * 180 / Math.PI,
                LargeArcFlag = LargeArcFlag,
                SweepFlag = SweepFlag,
                AngularSweep = AngularSweep
            };
        }

        public override CurveSegment2D Translate(double dx, double dy)
        {
            return new ArcSegment2D
            {
                Start = (Start.X + dx, Start.Y + dy),
                End = (End.X + dx, End.Y + dy),
                Center = (Center.X + dx, Center.Y + dy),
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                XAxisRotation = XAxisRotation,
                LargeArcFlag = LargeArcFlag,
                SweepFlag = SweepFlag,
                AngularSweep = AngularSweep
            };
        }

        public override string ToSvgPathCommand()
        {
            // Handle near-zero sweep (degenerate arc) - use line instead
            if (Math.Abs(AngularSweep) < 0.01)
            {
                var dxLine = End.X - Start.X;
                var dyLine = End.Y - Start.Y;
                return $"l {dxLine:F3},{dyLine:F3}";
            }
            
            // Sample the arc as line segments - this produces correct output
            // Use high sampling for smooth curves
            int numSegments = Math.Max(16, (int)(Math.Abs(AngularSweep) * 180 / Math.PI / 3));
            numSegments = Math.Min(numSegments, 120);
            
            var points = SampleArcPoints(numSegments);
            
            if (points.Count <= 1)
            {
                var dxLine = End.X - Start.X;
                var dyLine = End.Y - Start.Y;
                return $"l {dxLine:F3},{dyLine:F3}";
            }
            
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < points.Count; i++)
            {
                var dx = points[i].X - points[i-1].X;
                var dy = points[i].Y - points[i-1].Y;
                if (i > 1) sb.Append(" ");
                sb.Append($"l {dx:F3},{dy:F3}");
            }
            return sb.ToString();
        }
        
        public override string ToSvgArcCommand()
        {
            // Handle near-zero sweep (degenerate arc) - use line instead
            if (Math.Abs(AngularSweep) < 0.01)
            {
                var dxLine = End.X - Start.X;
                var dyLine = End.Y - Start.Y;
                return $"l {dxLine:F3},{dyLine:F3}";
            }
            
            // Compute SVG arc flags from the angular sweep
            double absSweep = Math.Abs(AngularSweep);
            
            // Large arc flag: 1 if |sweep| > 180 degrees
            int largeArc = absSweep > Math.PI ? 1 : 0;
            
            // SVG sweep flag:
            // Our AngularSweep: positive = CCW (increasing angle in math coords)
            // But SVG has Y-down, so visually it's flipped
            // In SVG: sweep=1 means clockwise visually
            // So: AngularSweep > 0 (math CCW = visual CW) -> sweep=1
            //     AngularSweep < 0 (math CW = visual CCW) -> sweep=0
            int sweep = AngularSweep > 0 ? 1 : 0;
            
            // SVG arc endpoint (relative)
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            
            return $"a {RadiusX:F3},{RadiusY:F3} 0 {largeArc} {sweep} {dx:F3},{dy:F3}";
        }
        
        /// <summary>
        /// Sample points along the arc using the stored angular sweep.
        /// </summary>
        private List<(double X, double Y)> SampleArcPoints(int numSegments)
        {
            var points = new List<(double X, double Y)>();
            points.Add(Start);
            
            if (Math.Abs(AngularSweep) < 0.01)
            {
                points.Add(End);
                return points;
            }
            
            // Compute start angle from center
            var startAngle = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
            
            // Sample intermediate points using the angular sweep
            for (int i = 1; i < numSegments; i++)
            {
                double t = (double)i / numSegments;
                double angle = startAngle + AngularSweep * t;
                double x = Center.X + RadiusX * Math.Cos(angle);
                double y = Center.Y + RadiusY * Math.Sin(angle);
                points.Add((x, y));
            }
            
            points.Add(End);
            return points;
        }

        private static (double X, double Y) RotatePoint((double X, double Y) p, double angle, double cx, double cy)
        {
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var dx = p.X - cx;
            var dy = p.Y - cy;
            return (dx * cos - dy * sin + cx, dx * sin + dy * cos + cy);
        }
    }
}
