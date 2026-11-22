// (not in pbrt-v4) sdfcontour.cs
//
// Outlines in the plane for the signed distance fields of sdf.cs: closed contours made of straight segments, arcs of circles and
// quadratic Bezier curves, with the exact signed distance to them (negative inside), and the two ways of making a solid from one,
// after Inigo Quilez ("2D distance functions", and the extrusion and revolution of "distance functions"):
//
//   extrude(shape, halfHeight)   the prism over the outline in the plane z = 0, from -halfHeight to halfHeight along z
//   revolve(shape [, offset])    the outline, moved by the offset along x, turned about the y axis (a ring; the outline has
//                                to stay on the side x >= 0 after the offset for the distance to be exact)
//   lathe(shape)                 the solid of revolution of a profile: an open path that starts and ends on the y axis
//
// The distance to a contour is the distance to the closest of its pieces, and the sign comes from counting the pieces that a ray
// toward +x crosses, so a contour may be concave and may have holes (a second closed path inside the first), and the distance is
// exact on both sides of the outline. That carries over to the prism and, for an outline on one side of the axis, to the ring.
//
// The outlines: path("M x y L x y A x y bulge Q cx cy x y Z ...") with the commands of a drawing: M starts a path at a point, L
// draws a segment to a point, A an arc to a point with the bulge of a CAD polyline (the tangent of a quarter of the angle that
// the arc turns through, positive to the left, 1 for a half circle), Q a quadratic Bezier with its control point, Z closes the
// path. And by name: circle(r)  rect(hx, hy [, rounding])  polygon(x0, y0, x1, y1, ...)  ngon(sides, apothem)  octagon(apothem)
// cross(arm, halfWidth)  pie(halfAngle, r)  arc(halfAngle, r, halfWidth)  horseshoe(halfAngle, r, length, halfWidth)
// vesica(r, d), all about the origin and, where there is an opening, about the +y axis; angles in degrees. translate2(shape, x, y),
// rotate2(shape, degrees) and offset2(shape, d) move, turn and grow an outline. An outline on its own is the prism without ends.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace pbrt
{

    // SdfContour Definition
    public sealed class SdfContour
    {
        private enum PieceKind
        {
            Line,
            Arc,
            Curve
        }

        // A piece of the outline. A line goes from a to b; an arc is about the center c (stored in `c`) with a radius, a start angle
        // and a signed sweep; a curve is the quadratic Bezier from a to b with the control point c.
        private struct Piece
        {
            public PieceKind kind;
            public double ax, ay, bx, by, cx, cy;
            public double radius, start, sweep;
        }

        // A part of a piece along which y only rises or only falls, from (x0, y0) to (x1, y1): what the ray toward +x is tested
        // against. t0 and t1 are the angles of an arc or the parameters of a curve.
        private struct Span
        {
            public int piece;
            public double t0, t1, y0, y1;
        }

        // SdfContour Public Methods
        public void MoveTo(double x, double y)
        {
            startX = x;
            startY = y;
            currentX = x;
            currentY = y;
            open = false;
            if (pieces.Count == 0)
            {
                firstX = x;
                firstY = y;
            }
            ++paths;
        }

        public void LineTo(double x, double y)
        {
            if (x != currentX || y != currentY)
            {
                Piece piece = new Piece();
                piece.kind = PieceKind.Line;
                piece.ax = currentX;
                piece.ay = currentY;
                piece.bx = x;
                piece.by = y;
                Add(piece);
            }
            currentX = x;
            currentY = y;
            open = true;
        }

        // An arc to (x, y) that turns through 4 atan(bulge), to the left for a positive bulge
        public void ArcTo(double x, double y, double bulge)
        {
            double dx = x - currentX, dy = y - currentY, chord = Math.Sqrt(dx * dx + dy * dy);
            if (bulge == 0 || chord == 0)
            {
                LineTo(x, y);
                return;
            }
            // The center is off the middle of the chord, to its left for a positive bulge below 1
            double off = chord * (1 - bulge * bulge) / (4 * bulge);
            Piece piece = new Piece();
            piece.kind = PieceKind.Arc;
            piece.ax = currentX;
            piece.ay = currentY;
            piece.bx = x;
            piece.by = y;
            piece.cx = 0.5 * (currentX + x) - dy / chord * off;
            piece.cy = 0.5 * (currentY + y) + dx / chord * off;
            piece.radius = chord * (1 + bulge * bulge) / (4 * Math.Abs(bulge));
            piece.start = Math.Atan2(currentY - piece.cy, currentX - piece.cx);
            piece.sweep = 4 * Math.Atan(bulge);
            Add(piece);
            currentX = x;
            currentY = y;
            open = true;
        }

        public void CurveTo(double controlX, double controlY, double x, double y)
        {
            // A control point on the chord makes a segment
            double bx = currentX - 2 * controlX + x, by = currentY - 2 * controlY + y;
            double size = Math.Abs(x - currentX) + Math.Abs(y - currentY) + Math.Abs(controlX - currentX) + Math.Abs(controlY - currentY);
            if (bx * bx + by * by <= 1e-24 * size * size)
            {
                LineTo(x, y);
                return;
            }
            Piece piece = new Piece();
            piece.kind = PieceKind.Curve;
            piece.ax = currentX;
            piece.ay = currentY;
            piece.bx = x;
            piece.by = y;
            piece.cx = controlX;
            piece.cy = controlY;
            Add(piece);
            currentX = x;
            currentY = y;
            open = true;
        }

        public void Close()
        {
            LineTo(startX, startY);
            open = false;
        }

        // Whether every path of the outline is closed
        public bool IsClosed()
        {
            return pieces.Count > 0 && !open && unclosed == 0;
        }

        // Marks the end of the commands: a path that was left open is counted
        public void Finish()
        {
            if (open && (currentX != startX || currentY != startY))
            {
                ++unclosed;
            }
            open = false;
        }

        // The outline of a profile and its mirror image in the y axis, for a solid of revolution: the profile has to be one open
        // path from the axis to the axis. null if it is not.
        public SdfContour Mirrored()
        {
            if (paths != 1 || pieces.Count == 0 || Math.Abs(firstX) > 1e-9 || Math.Abs(currentX) > 1e-9)
            {
                return null;
            }
            SdfContour result = new SdfContour();
            result.MoveTo(0, firstY);
            foreach (Piece piece in pieces)
            {
                result.Append(piece, false);
            }
            for (int i = pieces.Count - 1; i >= 0; --i)
            {
                result.Append(pieces[i], true);
            }
            result.Close();
            result.Finish();
            result.offset = offset;
            return result;
        }

        // Draws a piece from the current point; mirrored draws its image in the y axis, backward
        private void Append(Piece piece, bool mirrored)
        {
            double bulge = piece.kind == PieceKind.Arc ? Math.Tan(0.25 * piece.sweep) : 0;
            if (!mirrored)
            {
                if (piece.kind == PieceKind.Line) LineTo(piece.bx, piece.by);
                else if (piece.kind == PieceKind.Arc) ArcTo(piece.bx, piece.by, bulge);
                else CurveTo(piece.cx, piece.cy, piece.bx, piece.by);
                return;
            }
            // The image runs from the image of b to the image of a; a mirror turns left into right and running it backward turns
            // it back, so the bulge keeps its sign
            if (piece.kind == PieceKind.Line) LineTo(-piece.ax, piece.ay);
            else if (piece.kind == PieceKind.Arc) ArcTo(-piece.ax, piece.ay, bulge);
            else CurveTo(-piece.cx, piece.cy, -piece.ax, piece.ay);
        }

        // The outline turned by the angle (radians) about the origin and then moved by (tx, ty)
        public SdfContour Transformed(double angle, double tx, double ty)
        {
            double c = Math.Cos(angle), s = Math.Sin(angle);
            SdfContour result = new SdfContour();
            double lastX = double.NaN, lastY = double.NaN;
            foreach (Piece piece in pieces)
            {
                double ax = c * piece.ax - s * piece.ay + tx, ay = s * piece.ax + c * piece.ay + ty;
                double bx = c * piece.bx - s * piece.by + tx, by = s * piece.bx + c * piece.by + ty;
                if (piece.ax != lastX || piece.ay != lastY)
                {
                    result.Finish();
                    result.MoveTo(ax, ay);
                }
                if (piece.kind == PieceKind.Line) result.LineTo(bx, by);
                else if (piece.kind == PieceKind.Arc) result.ArcTo(bx, by, Math.Tan(0.25 * piece.sweep));
                else result.CurveTo(c * piece.cx - s * piece.cy + tx, s * piece.cx + c * piece.cy + ty, bx, by);
                lastX = piece.bx;
                lastY = piece.by;
            }
            result.open = false;
            result.unclosed = unclosed;
            result.offset = offset;
            return result;
        }

        // The outline grown by the distance d (shrunk by a negative one)
        public SdfContour Grown(double d)
        {
            SdfContour result = Transformed(0, 0, 0);
            result.offset = offset + d;
            return result;
        }

        // The offset moves the outline outward: the distance is then exact on the side that the outline moved toward
        public bool IsExactOutside()
        {
            return offset >= 0;
        }

        public bool IsExactInside()
        {
            return offset <= 0;
        }

        // The box of the outline
        public void Bounds(out double lowerX, out double lowerY, out double upperX, out double upperY)
        {
            lowerX = minX - Math.Max(offset, 0);
            lowerY = minY - Math.Max(offset, 0);
            upperX = maxX + Math.Max(offset, 0);
            upperY = maxY + Math.Max(offset, 0);
        }

        // The signed distance of (px, py) to the outline, negative inside, and its gradient
        public double Distance(double px, double py, out double gx, out double gy)
        {
            double best = double.PositiveInfinity, cx = 0, cy = 0;
            for (int i = 0; i < pieces.Count; ++i)
            {
                Closest(pieces[i], px, py, ref best, ref cx, ref cy);
            }
            bool inside = false;
            for (int i = 0; i < spans.Count; ++i)
            {
                Span span = spans[i];
                if ((span.y0 > py) != (span.y1 > py) && Crossing(pieces[span.piece], span, py) > px)
                {
                    inside = !inside;
                }
            }
            double distance = Math.Sqrt(best);
            if (distance > 0)
            {
                double sign = inside ? -1 : 1;
                gx = sign * (px - cx) / distance;
                gy = sign * (py - cy) / distance;
            }
            else
            {
                gx = 0;
                gy = 1;
            }
            return (inside ? -distance : distance) - offset;
        }

        public double Distance(double px, double py)
        {
            return Distance(px, py, out double gx, out double gy);
        }

        // SdfContour Private Methods
        private void Add(Piece piece)
        {
            int index = pieces.Count;
            pieces.Add(piece);
            Include(piece.ax, piece.ay);
            Include(piece.bx, piece.by);
            if (piece.kind == PieceKind.Line)
            {
                AddSpan(index, 0, 1, piece.ay, piece.by);
            }
            else if (piece.kind == PieceKind.Arc)
            {
                // Cut where the arc is level (at the top and the bottom of its circle), and widen the box by the four points of
                // the circle that the arc passes
                double direction = piece.sweep > 0 ? 1 : -1, end = piece.start + piece.sweep, from = piece.start;
                double fromY = piece.ay;
                for (int quarter = -8; quarter <= 8; ++quarter)
                {
                    double angle = 0.5 * Math.PI * quarter;
                    if ((angle - piece.start) * direction <= 0 || (end - angle) * direction <= 0)
                    {
                        continue;
                    }
                    Include(piece.cx + piece.radius * Math.Cos(angle), piece.cy + piece.radius * Math.Sin(angle));
                }
                // The level points in the order that the arc meets them
                for (int step = -4; step <= 4; ++step)
                {
                    double angle = 0.5 * Math.PI + Math.PI * direction * step;
                    if ((angle - from) * direction <= 0 || (end - angle) * direction <= 0)
                    {
                        continue;
                    }
                    double level = piece.cy + piece.radius * Math.Sin(angle);
                    AddSpan(index, from, angle, fromY, level);
                    from = angle;
                    fromY = level;
                }
                AddSpan(index, from, end, fromY, piece.by);
            }
            else
            {
                Include(piece.cx, piece.cy);
                // Cut where the curve is level
                double denominator = piece.ay - 2 * piece.cy + piece.by;
                double level = denominator != 0 ? (piece.ay - piece.cy) / denominator : -1;
                if (level > 0 && level < 1)
                {
                    double y = (1 - level) * (1 - level) * piece.ay + 2 * level * (1 - level) * piece.cy + level * level * piece.by;
                    AddSpan(index, 0, level, piece.ay, y);
                    AddSpan(index, level, 1, y, piece.by);
                }
                else
                {
                    AddSpan(index, 0, 1, piece.ay, piece.by);
                }
            }
        }

        private void AddSpan(int piece, double t0, double t1, double y0, double y1)
        {
            Span span = new Span();
            span.piece = piece;
            span.t0 = t0;
            span.t1 = t1;
            span.y0 = y0;
            span.y1 = y1;
            spans.Add(span);
        }

        private void Include(double x, double y)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        // The x at which a span is at the height y
        private static double Crossing(Piece piece, Span span, double y)
        {
            if (piece.kind == PieceKind.Line)
            {
                return piece.ax + (y - piece.ay) * (piece.bx - piece.ax) / (piece.by - piece.ay);
            }
            if (piece.kind == PieceKind.Arc)
            {
                // The span is on the right or on the left half of its circle
                double dy = y - piece.cy, across = Math.Sqrt(Math.Max(piece.radius * piece.radius - dy * dy, 0));
                return piece.cx + (Math.Cos(0.5 * (span.t0 + span.t1)) > 0 ? across : -across);
            }
            // y(t) = (a - 2 c + b) t^2 + 2 (c - a) t + a, with one root in the span
            double qa = piece.ay - 2 * piece.cy + piece.by, qb = 2 * (piece.cy - piece.ay), qc = piece.ay - y;
            double t;
            if (Math.Abs(qa) < 1e-14 * (Math.Abs(qb) + Math.Abs(qc)))
            {
                t = -qc / qb;
            }
            else
            {
                double root = Math.Sqrt(Math.Max(qb * qb - 4 * qa * qc, 0));
                // The stable pair of roots
                double q = -0.5 * (qb + (qb < 0 ? -root : root));
                double first = q / qa, second = q != 0 ? qc / q : first;
                double middle = 0.5 * (span.t0 + span.t1);
                t = Math.Abs(first - middle) <= Math.Abs(second - middle) ? first : second;
            }
            t = Math.Min(Math.Max(t, span.t0), span.t1);
            return (1 - t) * (1 - t) * piece.ax + 2 * t * (1 - t) * piece.cx + t * t * piece.bx;
        }

        // The closest point of a piece to (px, py), kept if it is closer than the best so far (best is the squared distance)
        private static void Closest(Piece piece, double px, double py, ref double best, ref double cx, ref double cy)
        {
            if (piece.kind == PieceKind.Line)
            {
                double ex = piece.bx - piece.ax, ey = piece.by - piece.ay;
                double t = Math.Min(Math.Max(((px - piece.ax) * ex + (py - piece.ay) * ey) / (ex * ex + ey * ey), 0), 1);
                Keep(piece.ax + ex * t, piece.ay + ey * t, px, py, ref best, ref cx, ref cy);
                return;
            }
            if (piece.kind == PieceKind.Arc)
            {
                // The point of the circle toward (px, py) if the arc passes it, and the two ends
                Keep(piece.ax, piece.ay, px, py, ref best, ref cx, ref cy);
                Keep(piece.bx, piece.by, px, py, ref best, ref cx, ref cy);
                double dx = px - piece.cx, dy = py - piece.cy, length = Math.Sqrt(dx * dx + dy * dy);
                if (length > 0)
                {
                    double turn = Math.Atan2(dy, dx) - piece.start;
                    // Into the range of the sweep: [0, 2 pi) for a positive one, (-2 pi, 0] for a negative one
                    turn -= 2 * Math.PI * Math.Floor(turn / (2 * Math.PI));
                    if (piece.sweep < 0 && turn > 0)
                    {
                        turn -= 2 * Math.PI;
                    }
                    if (Math.Abs(turn) <= Math.Abs(piece.sweep))
                    {
                        Keep(piece.cx + piece.radius * dx / length, piece.cy + piece.radius * dy / length, px, py, ref best, ref cx, ref cy);
                    }
                }
                return;
            }
            // The curve is a + 2 t A + t^2 B; the closest point is a root of B.B t^3 + 3 A.B t^2 + (2 A.A + D.B) t + D.A = 0 with
            // D = a - p, or an end
            double Ax = piece.cx - piece.ax, Ay = piece.cy - piece.ay;
            double Bx = piece.ax - 2 * piece.cx + piece.bx, By = piece.ay - 2 * piece.cy + piece.by;
            double Dx = piece.ax - px, Dy = piece.ay - py;
            double bb = Bx * Bx + By * By;
            double kx = (Ax * Bx + Ay * By) / bb, ky = (2 * (Ax * Ax + Ay * Ay) + Dx * Bx + Dy * By) / (3 * bb), kz = (Dx * Ax + Dy * Ay) / bb;
            // With t = s - kx: s^3 + 3 p s + q = 0
            double p = ky - kx * kx, q = kx * (2 * kx * kx - 3 * ky) + kz;
            double discriminant = q * q + 4 * p * p * p;
            if (discriminant >= 0)
            {
                double root = Math.Sqrt(discriminant);
                double s = Math.Cbrt(0.5 * (root - q)) + Math.Cbrt(0.5 * (-root - q));
                KeepCurve(piece, s - kx, px, py, ref best, ref cx, ref cy);
            }
            else
            {
                double z = Math.Sqrt(-p);
                double angle = Math.Acos(Math.Min(Math.Max(-q / (2 * z * z * z), -1), 1));
                KeepCurve(piece, 2 * z * Math.Cos(angle / 3) - kx, px, py, ref best, ref cx, ref cy);
                KeepCurve(piece, 2 * z * Math.Cos((angle - 2 * Math.PI) / 3) - kx, px, py, ref best, ref cx, ref cy);
                KeepCurve(piece, 2 * z * Math.Cos((angle - 4 * Math.PI) / 3) - kx, px, py, ref best, ref cx, ref cy);
            }
            Keep(piece.ax, piece.ay, px, py, ref best, ref cx, ref cy);
            Keep(piece.bx, piece.by, px, py, ref best, ref cx, ref cy);
        }

        private static void KeepCurve(Piece piece, double t, double px, double py, ref double best, ref double cx, ref double cy)
        {
            t = Math.Min(Math.Max(t, 0), 1);
            Keep((1 - t) * (1 - t) * piece.ax + 2 * t * (1 - t) * piece.cx + t * t * piece.bx,
                 (1 - t) * (1 - t) * piece.ay + 2 * t * (1 - t) * piece.cy + t * t * piece.by, px, py, ref best, ref cx, ref cy);
        }

        private static void Keep(double x, double y, double px, double py, ref double best, ref double cx, ref double cy)
        {
            double distance = (px - x) * (px - x) + (py - y) * (py - y);
            if (distance < best)
            {
                best = distance;
                cx = x;
                cy = y;
            }
        }

        // SdfContour Private Members
        private readonly List<Piece> pieces = new List<Piece>();
        private readonly List<Span> spans = new List<Span>();
        private double startX, startY, currentX, currentY, firstX, firstY, offset;
        private double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        private double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        private bool open;
        private int paths, unclosed;
    }

    // SdfShapes2D Definition: the outlines that have a name
    public static class SdfShapes2D
    {
        // The path of a drawing: M x y, L x y, A x y bulge, Q cx cy x y, Z. null with a message if it cannot be read.
        public static SdfContour Path(string text, out string error)
        {
            SdfContour contour = new SdfContour();
            string[] words = text.Split(new char[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            int position = 0;
            bool started = false;
            error = null;
            while (position < words.Length)
            {
                string command = words[position++];
                int count = command == "M" || command == "L" ? 2 : (command == "A" ? 3 : (command == "Q" ? 4 : (command == "Z" ? 0 : -1)));
                if (count < 0)
                {
                    error = "unknown path command \"" + command + "\"";
                    return null;
                }
                double[] v = new double[4];
                for (int i = 0; i < count; ++i)
                {
                    if (position >= words.Length || !double.TryParse(words[position], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    {
                        error = "path command " + command + " takes " + count + " numbers";
                        return null;
                    }
                    ++position;
                }
                if (command != "M" && !started)
                {
                    error = "a path starts with M";
                    return null;
                }
                if (command == "M")
                {
                    contour.Finish();
                    contour.MoveTo(v[0], v[1]);
                    started = true;
                }
                else if (command == "L")
                {
                    contour.LineTo(v[0], v[1]);
                }
                else if (command == "A")
                {
                    contour.ArcTo(v[0], v[1], v[2]);
                }
                else if (command == "Q")
                {
                    contour.CurveTo(v[0], v[1], v[2], v[3]);
                }
                else
                {
                    contour.Close();
                }
            }
            contour.Finish();
            return contour;
        }

        public static SdfContour Circle(double radius)
        {
            SdfContour contour = new SdfContour();
            contour.MoveTo(radius, 0);
            contour.ArcTo(-radius, 0, 1);
            contour.ArcTo(radius, 0, 1);
            contour.Finish();
            return contour;
        }

        public static SdfContour Rectangle(double hx, double hy, double rounding)
        {
            SdfContour contour = new SdfContour();
            if (rounding <= 0)
            {
                contour.MoveTo(hx, -hy);
                contour.LineTo(hx, hy);
                contour.LineTo(-hx, hy);
                contour.LineTo(-hx, -hy);
                contour.Close();
            }
            else
            {
                // A quarter circle at each corner
                double quarter = Math.Tan(Math.PI / 8), r = Math.Min(rounding, Math.Min(hx, hy));
                contour.MoveTo(hx, -hy + r);
                contour.LineTo(hx, hy - r);
                contour.ArcTo(hx - r, hy, quarter);
                contour.LineTo(-hx + r, hy);
                contour.ArcTo(-hx, hy - r, quarter);
                contour.LineTo(-hx, -hy + r);
                contour.ArcTo(-hx + r, -hy, quarter);
                contour.LineTo(hx - r, -hy);
                contour.ArcTo(hx, -hy + r, quarter);
            }
            contour.Finish();
            return contour;
        }

        // The polygon through the points (x0, y0, x1, y1, ...)
        public static SdfContour Polygon(double[] coordinates)
        {
            SdfContour contour = new SdfContour();
            contour.MoveTo(coordinates[0], coordinates[1]);
            for (int i = 2; i + 1 < coordinates.Length; i += 2)
            {
                contour.LineTo(coordinates[i], coordinates[i + 1]);
            }
            contour.Close();
            contour.Finish();
            return contour;
        }

        // The regular polygon whose sides are at the distance apothem from the origin, with a side at the bottom
        public static SdfContour RegularPolygon(int sides, double apothem)
        {
            double radius = apothem / Math.Cos(Math.PI / sides);
            double[] coordinates = new double[2 * sides];
            for (int i = 0; i < sides; ++i)
            {
                double angle = -0.5 * Math.PI + Math.PI / sides + 2 * Math.PI * i / sides;
                coordinates[2 * i] = radius * Math.Cos(angle);
                coordinates[2 * i + 1] = radius * Math.Sin(angle);
            }
            return Polygon(coordinates);
        }

        // A cross of two bars of the half length arm and the half width halfWidth
        public static SdfContour Cross(double arm, double halfWidth)
        {
            double a = arm, w = halfWidth;
            return Polygon(new double[] { a, -w, a, w, w, w, w, a, -w, a, -w, w, -a, w, -a, -w, -w, -w, -w, -a, w, -a, w, -w });
        }

        // The sector of a disc within the half angle (radians) of the +y axis
        public static SdfContour Pie(double halfAngle, double radius)
        {
            SdfContour contour = new SdfContour();
            contour.MoveTo(0, 0);
            contour.LineTo(radius * Math.Sin(halfAngle), radius * Math.Cos(halfAngle));
            contour.ArcTo(-radius * Math.Sin(halfAngle), radius * Math.Cos(halfAngle), Math.Tan(0.5 * halfAngle));
            contour.Close();
            contour.Finish();
            return contour;
        }

        // The part of a ring of the given radius and half width within the half angle (radians) of the +y axis, with round ends
        public static SdfContour Arc(double halfAngle, double radius, double halfWidth)
        {
            double s = Math.Sin(halfAngle), c = Math.Cos(halfAngle), outer = radius + halfWidth, inner = radius - halfWidth;
            double bulge = Math.Tan(0.5 * halfAngle);
            SdfContour contour = new SdfContour();
            contour.MoveTo(outer * s, outer * c);
            contour.ArcTo(-outer * s, outer * c, bulge);
            contour.ArcTo(-inner * s, inner * c, 1);
            contour.ArcTo(inner * s, inner * c, -bulge);
            contour.ArcTo(outer * s, outer * c, 1);
            contour.Finish();
            return contour;
        }

        // The same part of a ring with flat ends, and a straight leg of the given length at each end
        public static SdfContour Horseshoe(double halfAngle, double radius, double length, double halfWidth)
        {
            double s = Math.Sin(halfAngle), c = Math.Cos(halfAngle), outer = radius + halfWidth, inner = radius - halfWidth;
            double bulge = Math.Tan(0.5 * halfAngle);
            SdfContour contour = new SdfContour();
            contour.MoveTo(outer * s, outer * c);
            contour.ArcTo(-outer * s, outer * c, bulge);
            // The left leg runs on along the tangent (-c, -s), the right one along (c, -s)
            contour.LineTo(-outer * s - length * c, outer * c - length * s);
            contour.LineTo(-inner * s - length * c, inner * c - length * s);
            contour.LineTo(-inner * s, inner * c);
            contour.ArcTo(inner * s, inner * c, -bulge);
            contour.LineTo(inner * s + length * c, inner * c - length * s);
            contour.LineTo(outer * s + length * c, outer * c - length * s);
            contour.Close();
            contour.Finish();
            return contour;
        }

        // The lens that two discs of the radius r share, their centers at the distance d to either side of the origin on x
        public static SdfContour Vesica(double radius, double d)
        {
            double tip = Math.Sqrt(Math.Max(radius * radius - d * d, 0));
            // Each side turns through twice the angle of a tip as seen from the center of its disc
            double bulge = Math.Tan(0.5 * Math.Atan2(tip, d));
            SdfContour contour = new SdfContour();
            contour.MoveTo(0, -tip);
            contour.ArcTo(0, tip, bulge);
            contour.ArcTo(0, -tip, bulge);
            contour.Finish();
            return contour;
        }
    }

    // SdfOutlineNode Definition: an outline as a field, the prism over it without ends along z
    public sealed class SdfOutlineNode : SdfNode
    {
        public SdfOutlineNode(SdfContour contour)
        {
            this.contour = contour;
        }

        public SdfContour Contour()
        {
            return contour;
        }

        public override bool IsExactOutside()
        {
            return contour.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return contour.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            contour.Distance(p.x, p.y, out double gx, out double gy);
            return new SdfVector(gx, gy, 0);
        }

        public override double Evaluate(SdfVector p)
        {
            return contour.Distance(p.x, p.y);
        }

        private readonly SdfContour contour;
    }

    // SdfExtrude Definition: the prism over an outline from -halfHeight to halfHeight along z
    public sealed class SdfExtrude : SdfNode
    {
        public SdfExtrude(SdfContour contour, double halfHeight)
        {
            this.contour = contour;
            this.halfHeight = halfHeight;
        }

        public override bool IsExactOutside()
        {
            return contour.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return contour.IsExactInside();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            contour.Bounds(out double lowerX, out double lowerY, out double upperX, out double upperY);
            lower = new SdfVector(lowerX, lowerY, -halfHeight);
            upper = new SdfVector(upperX, upperY, halfHeight);
            grows = contour.IsExactOutside();
            return true;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double wx = contour.Distance(p.x, p.y, out double gx, out double gy), wy = Math.Abs(p.z) - halfHeight;
            double sign = p.z < 0 ? -1 : 1;
            if (Math.Max(wx, wy) <= 0)
            {
                return wx > wy ? new SdfVector(gx, gy, 0) : new SdfVector(0, 0, sign);
            }
            double ox = Math.Max(wx, 0), oy = Math.Max(wy, 0), length = Math.Sqrt(ox * ox + oy * oy);
            return new SdfVector(ox * gx / length, ox * gy / length, oy * sign / length);
        }

        public override double Evaluate(SdfVector p)
        {
            double wx = contour.Distance(p.x, p.y), wy = Math.Abs(p.z) - halfHeight;
            double ox = Math.Max(wx, 0), oy = Math.Max(wy, 0);
            return Math.Min(Math.Max(wx, wy), 0) + Math.Sqrt(ox * ox + oy * oy);
        }

        private readonly SdfContour contour;
        private readonly double halfHeight;
    }

    // SdfRevolve Definition: an outline, moved by the offset along x, turned about the y axis. `whole` says that the outline is
    // its own mirror image in the axis (a lathe profile), which makes the distance exact although the outline reaches the axis.
    public sealed class SdfRevolve : SdfNode
    {
        public SdfRevolve(SdfContour contour, double offset, bool whole)
        {
            this.contour = contour;
            this.offset = offset;
            contour.Bounds(out double lowerX, out lowerY, out double upperX, out upperY);
            reach = Math.Max(upperX + offset, 0);
            // An outline that crosses the axis shows only its part on the side x >= 0, and the distance to the hidden part is wrong
            oneSided = whole || lowerX + offset >= 0;
        }

        public override bool IsExactOutside()
        {
            return oneSided && contour.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return oneSided && contour.IsExactInside();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(-reach, lowerY, -reach);
            upper = new SdfVector(reach, upperY, reach);
            grows = oneSided && contour.IsExactOutside();
            return true;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double length = Math.Sqrt(p.x * p.x + p.z * p.z);
            contour.Distance(length - offset, p.y, out double gx, out double gy);
            return length > 0 ? new SdfVector(gx * p.x / length, gy, gx * p.z / length) : new SdfVector(gx, gy, 0);
        }

        public override double Evaluate(SdfVector p)
        {
            return contour.Distance(Math.Sqrt(p.x * p.x + p.z * p.z) - offset, p.y);
        }

        private readonly SdfContour contour;
        private readonly double offset, reach, lowerY, upperY;
        private readonly bool oneSided;
    }
}
