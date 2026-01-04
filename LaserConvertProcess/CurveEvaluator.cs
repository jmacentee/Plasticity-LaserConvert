using System;
using System.Collections.Generic;
using IxMilia.Step.Items;

namespace LaserConvertProcess
{
    /// <summary>
    /// Evaluates curve geometry to extract curve segments (arcs, lines, beziers).
    /// </summary>
    internal static class CurveEvaluator
    {
        /// <summary>
        /// Extract a curve segment from a STEP curve.
        /// Returns a CurveSegment (Arc, Line, or sampled points for complex curves).
        /// </summary>
        public static CurveSegment ExtractCurveSegment(
            StepCurve curve,
            StepVertexPoint startVertex,
            StepVertexPoint endVertex,
            bool orientation)
        {
            if (curve == null || startVertex?.Location == null || endVertex?.Location == null)
            {
                // Return a simple line segment
                var start = startVertex?.Location;
                var end = endVertex?.Location;
                return new LineSegment
                {
                    Start = start != null ? (start.X, start.Y, start.Z) : (0, 0, 0),
                    End = end != null ? (end.X, end.Y, end.Z) : (0, 0, 0)
                };
            }

            var startPt = orientation 
                ? (startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z)
                : (endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z);
            var endPt = orientation 
                ? (endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z)
                : (startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z);

            // Check if it's a circle (arc)
            if (curve is StepCircle circle)
            {
                return ExtractCircleArcSegment(circle, startPt, endPt, orientation);
            }

            // For B-splines and other curves, return a line segment for now
            // (B-splines would need conversion to cubic beziers, which is complex)
            return new LineSegment
            {
                Start = startPt,
                End = endPt
            };
        }

        /// <summary>
        /// Extract arc segment from a StepCircle.
        /// </summary>
        private static ArcSegment ExtractCircleArcSegment(
            StepCircle circle,
            (double X, double Y, double Z) startPt,
            (double X, double Y, double Z) endPt,
            bool orientation)
        {
            var placement = circle.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                return new ArcSegment
                {
                    Start = startPt,
                    End = endPt,
                    Center = ((startPt.X + endPt.X) / 2, (startPt.Y + endPt.Y) / 2, (startPt.Z + endPt.Z) / 2),
                    Radius = circle.Radius,
                    Normal = (0, 0, 1),
                    RefDirection = (1, 0, 0),
                    StartAngle = 0,
                    EndAngle = Math.PI,
                    Clockwise = false
                };
            }

            double cx = placement.Location?.X ?? 0;
            double cy = placement.Location?.Y ?? 0;
            double cz = placement.Location?.Z ?? 0;

            // Get axis directions
            double nx = placement.Axis?.X ?? 0;
            double ny = placement.Axis?.Y ?? 0;
            double nz = placement.Axis?.Z ?? 1;
            double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nLen > 1e-10) { nx /= nLen; ny /= nLen; nz /= nLen; }

            double refX = placement.RefDirection?.X ?? 1;
            double refY = placement.RefDirection?.Y ?? 0;
            double refZ = placement.RefDirection?.Z ?? 0;
            double refLen = Math.Sqrt(refX * refX + refY * refY + refZ * refZ);
            if (refLen > 1e-10) { refX /= refLen; refY /= refLen; refZ /= refLen; }

            // Y direction = N x X
            double yX = ny * refZ - nz * refY;
            double yY = nz * refX - nx * refZ;
            double yZ = nx * refY - ny * refX;

            // Compute angles
            double startAngle = ComputeAngleOnCircle(
                startPt.X, startPt.Y, startPt.Z,
                cx, cy, cz, refX, refY, refZ, yX, yY, yZ);

            double endAngle = ComputeAngleOnCircle(
                endPt.X, endPt.Y, endPt.Z,
                cx, cy, cz, refX, refY, refZ, yX, yY, yZ);

            // Ensure we go in the positive direction (counter-clockwise in local coords)
            while (endAngle <= startAngle)
                endAngle += 2 * Math.PI;

            // The arc should go from start to end
            // If not oriented, the arc direction is reversed
            bool clockwise = !orientation;

            return new ArcSegment
            {
                Start = startPt,
                End = endPt,
                Center = (cx, cy, cz),
                Radius = circle.Radius,
                Normal = (nx, ny, nz),
                RefDirection = (refX, refY, refZ),
                StartAngle = startAngle,
                EndAngle = endAngle,
                Clockwise = clockwise
            };
        }

        private static double ComputeAngleOnCircle(
            double px, double py, double pz,
            double cx, double cy, double cz,
            double refX, double refY, double refZ,
            double yX, double yY, double yZ)
        {
            double dx = px - cx;
            double dy = py - cy;
            double dz = pz - cz;

            double xComp = dx * refX + dy * refY + dz * refZ;
            double yComp = dx * yX + dy * yY + dz * yZ;

            return Math.Atan2(yComp, xComp);
        }

        /// <summary>
        /// Sample points along a curve. Returns the sampled 3D points.
        /// For line segments, returns just the endpoints.
        /// For curves (B-splines, circles), returns sampled points along the curve.
        /// </summary>
        public static List<(double X, double Y, double Z)> SampleCurve(
            StepCurve curve,
            StepVertexPoint startVertex,
            StepVertexPoint endVertex,
            bool orientation,
            int samplesPerCurve = 32)
        {
            var points = new List<(double X, double Y, double Z)>();

            if (curve == null)
            {
                if (startVertex?.Location != null)
                {
                    var pt = startVertex.Location;
                    points.Add((pt.X, pt.Y, pt.Z));
                }
                return points;
            }

            if (curve is StepBSplineCurveWithKnots bspline)
            {
                points = SampleBSplineCurve(bspline, samplesPerCurve);
                if (!orientation) points.Reverse();
                return points;
            }

            if (curve is StepCircle circle)
            {
                points = SampleCircleArc(circle, startVertex, endVertex, samplesPerCurve);
                if (!orientation) points.Reverse();
                return points;
            }

            if (curve is StepEllipse ellipse)
            {
                points = SampleEllipseArc(ellipse, startVertex, endVertex, samplesPerCurve);
                if (!orientation) points.Reverse();
                return points;
            }

            if (startVertex?.Location != null)
            {
                var pt = orientation ? startVertex.Location : endVertex?.Location ?? startVertex.Location;
                points.Add((pt.X, pt.Y, pt.Z));
            }

            return points;
        }

        private static List<(double X, double Y, double Z)> SampleBSplineCurve(
            StepBSplineCurveWithKnots bspline, int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();
            if (bspline.ControlPointsList == null || bspline.ControlPointsList.Count == 0)
                return points;

            var knotVector = new List<double>();
            for (int i = 0; i < bspline.Knots.Count && i < bspline.KnotMultiplicities.Count; i++)
                for (int j = 0; j < bspline.KnotMultiplicities[i]; j++)
                    knotVector.Add(bspline.Knots[i]);

            if (knotVector.Count == 0) return points;

            int degree = bspline.Degree;
            int n = bspline.ControlPointsList.Count - 1;
            double tMin = knotVector[degree];
            double tMax = knotVector[n + 1];

            for (int i = 0; i <= numSamples; i++)
            {
                double t = tMin + (tMax - tMin) * i / numSamples;
                if (i == numSamples) t = tMax - 1e-10;
                var pt = EvaluateBSpline(bspline.ControlPointsList, knotVector, degree, t);
                points.Add(pt);
            }
            return points;
        }

        private static (double X, double Y, double Z) EvaluateBSpline(
            List<StepCartesianPoint> controlPoints, List<double> knots, int degree, double t)
        {
            int n = controlPoints.Count - 1;
            int k = FindKnotSpan(n, degree, t, knots);
            var d = new (double X, double Y, double Z)[degree + 1];

            for (int j = 0; j <= degree; j++)
            {
                int idx = k - degree + j;
                if (idx >= 0 && idx < controlPoints.Count && controlPoints[idx] != null)
                    d[j] = (controlPoints[idx].X, controlPoints[idx].Y, controlPoints[idx].Z);
            }

            for (int r = 1; r <= degree; r++)
            {
                for (int j = degree; j >= r; j--)
                {
                    int i = k - degree + j;
                    double denom = knots[i + degree - r + 1] - knots[i];
                    double alpha = Math.Abs(denom) > 1e-10 ? (t - knots[i]) / denom : 0;
                    d[j] = ((1 - alpha) * d[j - 1].X + alpha * d[j].X,
                            (1 - alpha) * d[j - 1].Y + alpha * d[j].Y,
                            (1 - alpha) * d[j - 1].Z + alpha * d[j].Z);
                }
            }
            return d[degree];
        }

        private static int FindKnotSpan(int n, int degree, double t, List<double> knots)
        {
            if (t >= knots[n + 1]) return n;
            int low = degree, high = n + 1, mid = (low + high) / 2;
            while (t < knots[mid] || t >= knots[mid + 1])
            {
                if (t < knots[mid]) high = mid;
                else low = mid;
                mid = (low + high) / 2;
            }
            return mid;
        }

        private static List<(double X, double Y, double Z)> SampleCircleArc(
            StepCircle circle, StepVertexPoint startVertex, StepVertexPoint endVertex, int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();
            var placement = circle.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                if (startVertex?.Location != null)
                    points.Add((startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z));
                return points;
            }

            double cx = placement.Location?.X ?? 0, cy = placement.Location?.Y ?? 0, cz = placement.Location?.Z ?? 0;
            double nx = placement.Axis?.X ?? 0, ny = placement.Axis?.Y ?? 0, nz = placement.Axis?.Z ?? 1;
            double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nLen > 1e-10) { nx /= nLen; ny /= nLen; nz /= nLen; }

            double refX = placement.RefDirection?.X ?? 1, refY = placement.RefDirection?.Y ?? 0, refZ = placement.RefDirection?.Z ?? 0;
            double refLen = Math.Sqrt(refX * refX + refY * refY + refZ * refZ);
            if (refLen > 1e-10) { refX /= refLen; refY /= refLen; refZ /= refLen; }

            double yX = ny * refZ - nz * refY, yY = nz * refX - nx * refZ, yZ = nx * refY - ny * refX;
            double radius = circle.Radius;

            double startAngle = startVertex?.Location != null
                ? ComputeAngleOnCircle(startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z, cx, cy, cz, refX, refY, refZ, yX, yY, yZ)
                : 0;
            double endAngle = endVertex?.Location != null
                ? ComputeAngleOnCircle(endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z, cx, cy, cz, refX, refY, refZ, yX, yY, yZ)
                : 2 * Math.PI;

            while (endAngle <= startAngle) endAngle += 2 * Math.PI;

            for (int i = 0; i <= numSamples; i++)
            {
                double angle = startAngle + (endAngle - startAngle) * i / numSamples;
                points.Add((cx + radius * (Math.Cos(angle) * refX + Math.Sin(angle) * yX),
                            cy + radius * (Math.Cos(angle) * refY + Math.Sin(angle) * yY),
                            cz + radius * (Math.Cos(angle) * refZ + Math.Sin(angle) * yZ)));
            }
            return points;
        }

        private static List<(double X, double Y, double Z)> SampleEllipseArc(
            StepEllipse ellipse, StepVertexPoint startVertex, StepVertexPoint endVertex, int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();
            var placement = ellipse.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                if (startVertex?.Location != null)
                    points.Add((startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z));
                return points;
            }

            double cx = placement.Location?.X ?? 0, cy = placement.Location?.Y ?? 0, cz = placement.Location?.Z ?? 0;
            double nx = placement.Axis?.X ?? 0, ny = placement.Axis?.Y ?? 0, nz = placement.Axis?.Z ?? 1;
            double nLen = Math.Sqrt(nx * nx + ny * ny + nz * nz);
            if (nLen > 1e-10) { nx /= nLen; ny /= nLen; nz /= nLen; }

            double refX = placement.RefDirection?.X ?? 1, refY = placement.RefDirection?.Y ?? 0, refZ = placement.RefDirection?.Z ?? 0;
            double refLen = Math.Sqrt(refX * refX + refY * refY + refZ * refZ);
            if (refLen > 1e-10) { refX /= refLen; refY /= refLen; refZ /= refLen; }

            double yX = ny * refZ - nz * refY, yY = nz * refX - nx * refZ, yZ = nx * refY - ny * refX;
            double a = ellipse.SemiAxis1, b = ellipse.SemiAxis2;

            double startAngle = startVertex?.Location != null
                ? ComputeAngleOnEllipse(startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z, cx, cy, cz, refX, refY, refZ, yX, yY, yZ, a, b)
                : 0;
            double endAngle = endVertex?.Location != null
                ? ComputeAngleOnEllipse(endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z, cx, cy, cz, refX, refY, refZ, yX, yY, yZ, a, b)
                : 2 * Math.PI;

            while (endAngle <= startAngle) endAngle += 2 * Math.PI;

            for (int i = 0; i <= numSamples; i++)
            {
                double angle = startAngle + (endAngle - startAngle) * i / numSamples;
                points.Add((cx + a * Math.Cos(angle) * refX + b * Math.Sin(angle) * yX,
                            cy + a * Math.Cos(angle) * refY + b * Math.Sin(angle) * yY,
                            cz + a * Math.Cos(angle) * refZ + b * Math.Sin(angle) * yZ));
            }
            return points;
        }

        private static double ComputeAngleOnEllipse(
            double px, double py, double pz, double cx, double cy, double cz,
            double refX, double refY, double refZ, double yX, double yY, double yZ, double a, double b)
        {
            double dx = px - cx, dy = py - cy, dz = pz - cz;
            return Math.Atan2((dx * yX + dy * yY + dz * yZ) / b, (dx * refX + dy * refY + dz * refZ) / a);
        }

        /// <summary>
        /// Check if a curve is a "complex" curve that requires sampling (not a simple line).
        /// </summary>
        public static bool IsCurvedGeometry(StepCurve curve)
        {
            return curve is StepBSplineCurveWithKnots || curve is StepCircle || curve is StepEllipse;
        }
    }
}
