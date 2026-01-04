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
        /// Start angle in radians.
        /// </summary>
        public double StartAngle { get; set; }

        /// <summary>
        /// End angle in radians.
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

            // Compute the radius in 2D
            var dx = start2D.X - center2D.X;
            var dy = start2D.Y - center2D.Y;
            var radius2D = Math.Sqrt(dx * dx + dy * dy);

            // Calculate the angle sweep from the original 3D arc
            // StartAngle and EndAngle are computed so EndAngle > StartAngle (CCW direction)
            var angleSweep = EndAngle - StartAngle;
            // Normalize to 0-2? range
            while (angleSweep <= 0) angleSweep += 2 * Math.PI;
            while (angleSweep > 2 * Math.PI) angleSweep -= 2 * Math.PI;

            // If the edge is reversed (Clockwise=true), we're actually traversing 
            // the complementary arc (2? - angleSweep)
            double actualSweep = Clockwise ? (2 * Math.PI - angleSweep) : angleSweep;
            
            // Large arc flag: true if the arc we're traversing is > 180 degrees
            var largeArc = actualSweep > Math.PI;

            // Determine if the projection flips the arc direction
            // The projection plane normal is U x V
            var frameNormalX = frame.UY * frame.VZ - frame.UZ * frame.VY;
            var frameNormalY = frame.UZ * frame.VX - frame.UX * frame.VZ;
            var frameNormalZ = frame.UX * frame.VY - frame.UY * frame.VX;

            // Dot product of arc normal with frame normal
            var dot = Normal.X * frameNormalX + Normal.Y * frameNormalY + Normal.Z * frameNormalZ;

            // Determine sweep direction in SVG
            // In SVG: sweep=0 is CCW, sweep=1 is CW
            // 
            // If dot > 0: arc normal aligns with view direction
            //   - direction is preserved
            //   - Clockwise=false (CCW in 3D) ? sweep=0 (CCW in SVG)
            //   - Clockwise=true (CW in 3D) ? sweep=1 (CW in SVG)
            //
            // If dot < 0: arc normal opposes view direction
            //   - direction is flipped
            //   - Clockwise=false (CCW in 3D) ? sweep=1 (CW in SVG)
            //   - Clockwise=true (CW in 3D) ? sweep=0 (CCW in SVG)
            
            bool sweepFlag;
            if (dot >= 0)
            {
                sweepFlag = Clockwise;
            }
            else
            {
                sweepFlag = !Clockwise;
            }

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
            
            // Rotation doesn't change the arc flags - it just rotates the whole arc
            // The large-arc and sweep flags remain the same
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
            // SVG arc: a rx ry x-axis-rotation large-arc-flag sweep-flag dx dy
            var dx = End.X - Start.X;
            var dy = End.Y - Start.Y;
            var largeArc = LargeArcFlag ? 1 : 0;
            var sweep = SweepFlag ? 1 : 0;
            return $"a {RadiusX:F3},{RadiusY:F3} {XAxisRotation:F0} {largeArc} {sweep} {dx:F3},{dy:F3}";
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
