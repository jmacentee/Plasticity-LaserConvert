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

            // Compute the 2D angles of start and end points relative to center
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

            // Determine if we should go CW or CCW in 2D
            // In 3D: if Clockwise=false, we go CCW when viewed from normal
            //        if Clockwise=true, we go CW when viewed from normal
            // 
            // If dot > 0: viewing from same side as normal, direction preserved
            // If dot < 0: viewing from opposite side, direction flipped
            bool goClockwiseIn2D;
            if (dot >= 0)
            {
                goClockwiseIn2D = Clockwise;
            }
            else
            {
                goClockwiseIn2D = !Clockwise;
            }

            // Compute the angular sweep in 2D
            double sweep2D;
            if (goClockwiseIn2D)
            {
                // CW: sweep from start to end going clockwise (decreasing angle)
                sweep2D = startAngle2D - endAngle2D;
                while (sweep2D <= 0) sweep2D += 2 * Math.PI;
            }
            else
            {
                // CCW: sweep from start to end going counter-clockwise (increasing angle)
                sweep2D = endAngle2D - startAngle2D;
                while (sweep2D <= 0) sweep2D += 2 * Math.PI;
            }

            // Large arc flag: true if we're traversing more than half the circle
            bool largeArc = sweep2D > Math.PI;

            // SVG sweep flag: 0 = CCW, 1 = CW
            bool sweepFlag = goClockwiseIn2D;

            return new ArcSegment2D
            {
                Start = start2D,
                End = end2D,
                Center = center2D,
                RadiusX = radius2D,
                RadiusY = radius2D,
                LargeArcFlag = largeArc,
                SweepFlag = sweepFlag
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
        /// Generate SVG path command for this segment (relative to current position).
        /// </summary>
        public abstract string ToSvgPathCommand();
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

        public override CurveSegment2D Rotate(double angle, double cx, double cy)
        {
            var newStart = RotatePoint(Start, angle, cx, cy);
            var newEnd = RotatePoint(End, angle, cx, cy);
            var newCenter = RotatePoint(Center, angle, cx, cy);
            
            return new ArcSegment2D
            {
                Start = newStart,
                End = newEnd,
                Center = newCenter,
                RadiusX = RadiusX,
                RadiusY = RadiusY,
                XAxisRotation = XAxisRotation + angle * 180 / Math.PI,
                LargeArcFlag = LargeArcFlag,
                SweepFlag = SweepFlag
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
                SweepFlag = SweepFlag
            };
        }

        public override string ToSvgPathCommand()
        {
            // Sample the arc as a series of line segments for more reliable output
            // This avoids SVG arc flag issues while still producing smooth curves
            var points = SampleArcPoints(16);  // 16 segments for smooth arcs
            
            if (points.Count <= 1)
            {
                // Fallback to straight line
                var dx = End.X - Start.X;
                var dy = End.Y - Start.Y;
                return $"l {dx:F3},{dy:F3}";
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
        
        /// <summary>
        /// Sample points along the arc.
        /// </summary>
        private List<(double X, double Y)> SampleArcPoints(int numSegments)
        {
            var points = new List<(double X, double Y)>();
            points.Add(Start);
            
            // Compute angles from center
            var startAngle = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
            var endAngle = Math.Atan2(End.Y - Center.Y, End.X - Center.X);
            
            // Determine angular sweep based on flags
            double sweep;
            if (SweepFlag)
            {
                // Clockwise - decreasing angle
                sweep = startAngle - endAngle;
                while (sweep <= 0) sweep += 2 * Math.PI;
                if (LargeArcFlag && sweep < Math.PI) sweep = 2 * Math.PI - sweep;
                if (!LargeArcFlag && sweep > Math.PI) sweep = 2 * Math.PI - sweep;
                sweep = -sweep;  // Negative for CW
            }
            else
            {
                // Counter-clockwise - increasing angle
                sweep = endAngle - startAngle;
                while (sweep <= 0) sweep += 2 * Math.PI;
                if (LargeArcFlag && sweep < Math.PI) sweep = 2 * Math.PI - sweep;
                if (!LargeArcFlag && sweep > Math.PI) sweep = 2 * Math.PI - sweep;
            }
            
            // Sample intermediate points
            for (int i = 1; i < numSegments; i++)
            {
                double t = (double)i / numSegments;
                double angle = startAngle + sweep * t;
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
