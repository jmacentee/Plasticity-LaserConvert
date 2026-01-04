using System;
using System.Collections.Generic;
using IxMilia.Step.Items;

namespace LaserConvertProcess
{
    /// <summary>
    /// Evaluates curve geometry to sample points along curves (B-splines, circles, etc.)
    /// </summary>
    internal static class CurveEvaluator
    {
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
                // No curve geometry - just use start point
                if (startVertex?.Location != null)
                {
                    var pt = startVertex.Location;
                    points.Add((pt.X, pt.Y, pt.Z));
                }
                return points;
            }

            // Check if it's a B-spline curve
            if (curve is StepBSplineCurveWithKnots bspline)
            {
                points = SampleBSplineCurve(bspline, samplesPerCurve);
                if (!orientation)
                {
                    points.Reverse();
                }
                return points;
            }

            // Check if it's a circle (arc)
            if (curve is StepCircle circle)
            {
                points = SampleCircleArc(circle, startVertex, endVertex, samplesPerCurve);
                if (!orientation)
                {
                    points.Reverse();
                }
                return points;
            }

            // Check if it's an ellipse (arc)
            if (curve is StepEllipse ellipse)
            {
                points = SampleEllipseArc(ellipse, startVertex, endVertex, samplesPerCurve);
                if (!orientation)
                {
                    points.Reverse();
                }
                return points;
            }

            // For line segments or other unknown curves, just return start point
            // (end point will be start of next edge)
            if (startVertex?.Location != null)
            {
                var pt = orientation ? startVertex.Location : endVertex?.Location ?? startVertex.Location;
                points.Add((pt.X, pt.Y, pt.Z));
            }

            return points;
        }

        /// <summary>
        /// Sample points along a B-spline curve using de Boor's algorithm.
        /// </summary>
        private static List<(double X, double Y, double Z)> SampleBSplineCurve(
            StepBSplineCurveWithKnots bspline,
            int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();

            if (bspline.ControlPointsList == null || bspline.ControlPointsList.Count == 0)
                return points;

            // Build the full knot vector by expanding multiplicities
            var knotVector = new List<double>();
            for (int i = 0; i < bspline.Knots.Count && i < bspline.KnotMultiplicities.Count; i++)
            {
                for (int j = 0; j < bspline.KnotMultiplicities[i]; j++)
                {
                    knotVector.Add(bspline.Knots[i]);
                }
            }

            if (knotVector.Count == 0)
                return points;

            int degree = bspline.Degree;
            int n = bspline.ControlPointsList.Count - 1; // n+1 control points

            // Parameter range
            double tMin = knotVector[degree];
            double tMax = knotVector[n + 1];

            // Sample the curve
            for (int i = 0; i <= numSamples; i++)
            {
                double t = tMin + (tMax - tMin) * i / numSamples;
                // Clamp t to valid range (avoid edge issues)
                if (i == numSamples) t = tMax - 1e-10;

                var pt = EvaluateBSpline(bspline.ControlPointsList, knotVector, degree, t);
                points.Add(pt);
            }

            return points;
        }

        /// <summary>
        /// Evaluate a B-spline curve at parameter t using de Boor's algorithm.
        /// </summary>
        private static (double X, double Y, double Z) EvaluateBSpline(
            List<StepCartesianPoint> controlPoints,
            List<double> knots,
            int degree,
            double t)
        {
            int n = controlPoints.Count - 1;

            // Find knot span
            int k = FindKnotSpan(n, degree, t, knots);

            // De Boor's algorithm
            var d = new (double X, double Y, double Z)[degree + 1];

            // Initialize with control points
            for (int j = 0; j <= degree; j++)
            {
                int idx = k - degree + j;
                if (idx >= 0 && idx < controlPoints.Count && controlPoints[idx] != null)
                {
                    d[j] = (controlPoints[idx].X, controlPoints[idx].Y, controlPoints[idx].Z);
                }
            }

            // De Boor recursion
            for (int r = 1; r <= degree; r++)
            {
                for (int j = degree; j >= r; j--)
                {
                    int i = k - degree + j;
                    double alpha = 0;
                    double denom = knots[i + degree - r + 1] - knots[i];
                    if (Math.Abs(denom) > 1e-10)
                    {
                        alpha = (t - knots[i]) / denom;
                    }

                    d[j] = (
                        (1 - alpha) * d[j - 1].X + alpha * d[j].X,
                        (1 - alpha) * d[j - 1].Y + alpha * d[j].Y,
                        (1 - alpha) * d[j - 1].Z + alpha * d[j].Z
                    );
                }
            }

            return d[degree];
        }

        /// <summary>
        /// Find the knot span index for parameter t.
        /// </summary>
        private static int FindKnotSpan(int n, int degree, double t, List<double> knots)
        {
            // Special case for t at the end
            if (t >= knots[n + 1])
                return n;

            // Binary search
            int low = degree;
            int high = n + 1;
            int mid = (low + high) / 2;

            while (t < knots[mid] || t >= knots[mid + 1])
            {
                if (t < knots[mid])
                    high = mid;
                else
                    low = mid;
                mid = (low + high) / 2;
            }

            return mid;
        }

        /// <summary>
        /// Sample points along a circular arc.
        /// </summary>
        private static List<(double X, double Y, double Z)> SampleCircleArc(
            StepCircle circle,
            StepVertexPoint startVertex,
            StepVertexPoint endVertex,
            int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();

            if (circle.Position == null)
                return points;

            // Get the circle center and axes from the placement
            var placement = circle.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                // Fallback to just endpoints
                if (startVertex?.Location != null)
                    points.Add((startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z));
                return points;
            }

            double cx = placement.Location?.X ?? 0;
            double cy = placement.Location?.Y ?? 0;
            double cz = placement.Location?.Z ?? 0;

            // Get axis directions (Z is normal, X is reference direction)
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

            double radius = circle.Radius;

            // Compute angles for start and end points
            double startAngle = 0;
            double endAngle = 2 * Math.PI;

            if (startVertex?.Location != null)
            {
                startAngle = ComputeAngleOnCircle(
                    startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z,
                    cx, cy, cz, refX, refY, refZ, yX, yY, yZ);
            }

            if (endVertex?.Location != null)
            {
                endAngle = ComputeAngleOnCircle(
                    endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z,
                    cx, cy, cz, refX, refY, refZ, yX, yY, yZ);
            }

            // Ensure we go in the positive direction
            while (endAngle <= startAngle)
                endAngle += 2 * Math.PI;

            // Sample the arc
            for (int i = 0; i <= numSamples; i++)
            {
                double t = (double)i / numSamples;
                double angle = startAngle + t * (endAngle - startAngle);

                double x = cx + radius * (Math.Cos(angle) * refX + Math.Sin(angle) * yX);
                double y = cy + radius * (Math.Cos(angle) * refY + Math.Sin(angle) * yY);
                double z = cz + radius * (Math.Cos(angle) * refZ + Math.Sin(angle) * yZ);

                points.Add((x, y, z));
            }

            return points;
        }

        /// <summary>
        /// Compute the angle of a point on a circle.
        /// </summary>
        private static double ComputeAngleOnCircle(
            double px, double py, double pz,
            double cx, double cy, double cz,
            double refX, double refY, double refZ,
            double yX, double yY, double yZ)
        {
            // Vector from center to point
            double dx = px - cx;
            double dy = py - cy;
            double dz = pz - cz;

            // Project onto circle plane (dot products with X and Y axes)
            double xComp = dx * refX + dy * refY + dz * refZ;
            double yComp = dx * yX + dy * yY + dz * yZ;

            return Math.Atan2(yComp, xComp);
        }

        /// <summary>
        /// Sample points along an ellipse arc.
        /// </summary>
        private static List<(double X, double Y, double Z)> SampleEllipseArc(
            StepEllipse ellipse,
            StepVertexPoint startVertex,
            StepVertexPoint endVertex,
            int numSamples)
        {
            var points = new List<(double X, double Y, double Z)>();

            if (ellipse.Position == null)
                return points;

            var placement = ellipse.Position as StepAxis2Placement3D;
            if (placement == null)
            {
                if (startVertex?.Location != null)
                    points.Add((startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z));
                return points;
            }

            double cx = placement.Location?.X ?? 0;
            double cy = placement.Location?.Y ?? 0;
            double cz = placement.Location?.Z ?? 0;

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

            double yX = ny * refZ - nz * refY;
            double yY = nz * refX - nx * refZ;
            double yZ = nx * refY - ny * refX;

            double a = ellipse.SemiAxis1;
            double b = ellipse.SemiAxis2;

            double startAngle = 0;
            double endAngle = 2 * Math.PI;

            if (startVertex?.Location != null)
            {
                startAngle = ComputeAngleOnEllipse(
                    startVertex.Location.X, startVertex.Location.Y, startVertex.Location.Z,
                    cx, cy, cz, refX, refY, refZ, yX, yY, yZ, a, b);
            }

            if (endVertex?.Location != null)
            {
                endAngle = ComputeAngleOnEllipse(
                    endVertex.Location.X, endVertex.Location.Y, endVertex.Location.Z,
                    cx, cy, cz, refX, refY, refZ, yX, yY, yZ, a, b);
            }

            while (endAngle <= startAngle)
                endAngle += 2 * Math.PI;

            for (int i = 0; i <= numSamples; i++)
            {
                double t = (double)i / numSamples;
                double angle = startAngle + t * (endAngle - startAngle);

                double x = cx + a * Math.Cos(angle) * refX + b * Math.Sin(angle) * yX;
                double y = cy + a * Math.Cos(angle) * refY + b * Math.Sin(angle) * yY;
                double z = cz + a * Math.Cos(angle) * refZ + b * Math.Sin(angle) * yZ;

                points.Add((x, y, z));
            }

            return points;
        }

        private static double ComputeAngleOnEllipse(
            double px, double py, double pz,
            double cx, double cy, double cz,
            double refX, double refY, double refZ,
            double yX, double yY, double yZ,
            double a, double b)
        {
            double dx = px - cx;
            double dy = py - cy;
            double dz = pz - cz;

            double xComp = (dx * refX + dy * refY + dz * refZ) / a;
            double yComp = (dx * yX + dy * yY + dz * yZ) / b;

            return Math.Atan2(yComp, xComp);
        }

        /// <summary>
        /// Check if a curve is a "complex" curve that requires sampling (not a simple line).
        /// </summary>
        public static bool IsCurvedGeometry(StepCurve curve)
        {
            if (curve == null)
                return false;

            return curve is StepBSplineCurveWithKnots ||
                   curve is StepCircle ||
                   curve is StepEllipse;
        }
    }
}
