// (not in pbrt-v4) shapes_nurbs.cs
//
// RiNuPatch and RiTrimCurve: a NURBS surface of any orders, knot vectors and weights, with trim curves, traced directly. The surface is split exactly
// into rational Bezier patches (one per knot span, by blossoming). A ray is the intersection of two planes, so the roots of the ray with a rational
// Bezier patch are the common roots of two polynomial surfaces in Bernstein form (the numerators of the plane equations); they are isolated by
// subdivision with the convex hull property and refined by Newton's method on the rational surface. The trim curves are NURBS curves in the
// parameter space, split in the same way into rational Bezier segments; a point is visible if the winding number of the loops around it is positive
// (the loops run counterclockwise around the visible regions, and clockwise around the holes), which is evaluated exactly by finding the roots of the
// segments with the horizontal line through the point.
//
// Sampling: the parameter domain is a grid of cells; a cell is chosen by the area of its visible part, a point is drawn uniformly in the cell and
// rejected if it is trimmed, so the density in the parameters of the cell is 1 / (the visible parameter area of the cell), which is computed from
// the exact intervals of the visible part on lines across the cell. The pdf of the area follows with the exact Jacobian |dP/du x dP/dv|.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // The pieces of a B-spline (any dimension of control points) between its distinct knots in [lo, hi], each as Bezier control points
    public static class BezierExtraction
    {
        public sealed class Piece
        {
            public double start, end;
            public double[] control;  // (degree + 1) * dim
        }

        public static string Validate(int order, double[] knots, int n)
        {
            if (order < 2)
            {
                return "the order must be at least 2";
            }
            if (n < order)
            {
                return "at least `order` control points are needed";
            }
            if (knots.Length != n + order)
            {
                return "the number of knots must be n + order";
            }
            for (int i = 1; i < knots.Length; ++i)
            {
                if (knots[i] < knots[i - 1])
                {
                    return "the knots must not decrease";
                }
            }
            if (!(knots[n] > knots[order - 1]))
            {
                return "the parameter range of the knots is empty";
            }
            return null;
        }

        public static List<Piece> Extract(int order, double[] knots, int n, double lo, double hi, double[] control, int dim)
        {
            int p = order - 1;
            List<Piece> pieces = new List<Piece>();
            double[] d = new double[(p + 1) * dim];
            for (int k = p; k < n; ++k)
            {
                if (!(knots[k] < knots[k + 1]))
                {
                    continue;
                }
                double a = Math.Max(knots[k], lo), b = Math.Min(knots[k + 1], hi);
                if (!(a < b))
                {
                    continue;
                }
                Piece piece = new Piece();
                piece.start = a;
                piece.end = b;
                piece.control = new double[(p + 1) * dim];
                for (int i = 0; i <= p; ++i)
                {
                    // the blossom at (a, ..., a, b, ..., b) with p - i copies of a
                    for (int j = 0; j <= p; ++j)
                    {
                        for (int c = 0; c < dim; ++c)
                        {
                            d[j * dim + c] = control[(k - p + j) * dim + c];
                        }
                    }
                    for (int r = 1; r <= p; ++r)
                    {
                        double t = r <= p - i ? a : b;
                        for (int j = p; j >= r; --j)
                        {
                            double left = knots[j + k - p], right = knots[j + 1 + k - r];
                            double alpha = right > left ? (t - left) / (right - left) : 0;
                            for (int c = 0; c < dim; ++c)
                            {
                                d[j * dim + c] = (1 - alpha) * d[(j - 1) * dim + c] + alpha * d[j * dim + c];
                            }
                        }
                    }
                    for (int c = 0; c < dim; ++c)
                    {
                        piece.control[i * dim + c] = d[p * dim + c];
                    }
                }
                pieces.Add(piece);
            }
            return pieces;
        }
    }

    public static class Bernstein
    {
        public static void Basis(int degree, double t, double[] b)
        {
            b[0] = 1;
            double s = 1 - t;
            for (int j = 1; j <= degree; ++j)
            {
                double saved = 0;
                for (int k = 0; k < j; ++k)
                {
                    double temp = b[k];
                    b[k] = saved + s * temp;
                    saved = t * temp;
                }
                b[j] = saved;
            }
        }

        public static double Evaluate(double[] c, int degree, double t)
        {
            double[] b = new double[degree + 1];
            Basis(degree, t, b);
            double sum = 0;
            for (int i = 0; i <= degree; ++i)
            {
                sum += c[i] * b[i];
            }
            return sum;
        }

        // The derivative of the polynomial with Bernstein coefficients c at t
        public static double Derivative(double[] c, int degree, double t)
        {
            if (degree == 0)
            {
                return 0;
            }
            double[] b = new double[degree];
            Basis(degree - 1, t, b);
            double sum = 0;
            for (int i = 0; i < degree; ++i)
            {
                sum += degree * (c[i + 1] - c[i]) * b[i];
            }
            return sum;
        }

        // The roots in [0, 1] of the polynomial with Bernstein coefficients c, isolated by the sign variations of the coefficients on subdivision
        public static void Roots(double[] c, int degree, List<double> roots)
        {
            Roots(c, degree, 0, 1, roots, 0);
            roots.Sort();
            for (int i = roots.Count - 1; i > 0; --i)
            {
                if (roots[i] - roots[i - 1] < 1e-13)
                {
                    roots.RemoveAt(i);
                }
            }
        }

        private static void Roots(double[] c, int degree, double t0, double t1, List<double> roots, int depth)
        {
            int variations = 0;
            int lastSign = 0;
            bool allZero = true;
            for (int i = 0; i <= degree; ++i)
            {
                int sign = c[i] > 0 ? 1 : (c[i] < 0 ? -1 : 0);
                if (sign != 0)
                {
                    allZero = false;
                    if (lastSign != 0 && sign != lastSign)
                    {
                        ++variations;
                    }
                    lastSign = sign;
                }
            }
            if (allZero)
            {
                return;
            }
            if (c[0] == 0)
            {
                roots.Add(t0);
            }
            if (c[degree] == 0 && degree > 0)
            {
                roots.Add(t1);
            }
            if (variations == 0)
            {
                return;
            }
            if (variations == 1 && c[0] * c[degree] < 0)
            {
                double lo = 0, hi = 1;
                bool increasing = c[degree] > 0;
                for (int it = 0; it < 80; ++it)
                {
                    double mid = 0.5 * (lo + hi);
                    double value = Evaluate(c, degree, mid);
                    if ((value > 0) == increasing)
                    {
                        hi = mid;
                    }
                    else
                    {
                        lo = mid;
                    }
                    if (hi - lo < 1e-16)
                    {
                        break;
                    }
                }
                roots.Add(t0 + 0.5 * (lo + hi) * (t1 - t0));
                return;
            }
            if (depth >= 48)
            {
                roots.Add(0.5 * (t0 + t1));
                return;
            }
            double[] left = new double[degree + 1], right = new double[degree + 1], work = (double[])c.Clone();
            left[0] = work[0];
            right[degree] = work[degree];
            for (int level = 1; level <= degree; ++level)
            {
                for (int i = 0; i <= degree - level; ++i)
                {
                    work[i] = 0.5 * (work[i] + work[i + 1]);
                }
                left[level] = work[0];
                right[degree - level] = work[degree - level];
            }
            double mid2 = 0.5 * (t0 + t1);
            Roots(left, degree, t0, mid2, roots, depth + 1);
            Roots(right, degree, mid2, t1, roots, depth + 1);
        }
    }

    // The trim curves of a surface, as rational Bezier segments in the parameter space
    public sealed class TrimRegion
    {
        private sealed class Segment
        {
            public int degree;
            public double[] x, y, w;  // homogeneous
            public double minU, maxU, minV, maxV;
        }

        private readonly List<Segment> segments = new List<Segment>();

        public bool IsEmpty
        {
            get { return segments.Count == 0; }
        }

        // One trim curve: control points (u, v, w) homogeneous (u and v multiplied by w), as RiTrimCurve takes them
        public void AddCurve(int order, double[] knots, int n, double[] uvw, double lo, double hi)
        {
            foreach (BezierExtraction.Piece piece in BezierExtraction.Extract(order, knots, n, lo, hi, uvw, 3))
            {
                Segment s = new Segment();
                s.degree = order - 1;
                s.x = new double[order];
                s.y = new double[order];
                s.w = new double[order];
                s.minU = s.minV = double.PositiveInfinity;
                s.maxU = s.maxV = double.NegativeInfinity;
                for (int i = 0; i < order; ++i)
                {
                    s.x[i] = piece.control[3 * i];
                    s.y[i] = piece.control[3 * i + 1];
                    s.w[i] = piece.control[3 * i + 2];
                    double u = s.x[i] / s.w[i], v = s.y[i] / s.w[i];
                    s.minU = Math.Min(s.minU, u);
                    s.maxU = Math.Max(s.maxU, u);
                    s.minV = Math.Min(s.minV, v);
                    s.maxV = Math.Max(s.maxV, v);
                }
                segments.Add(s);
            }
        }

        // The crossings of the horizontal line at v with the segments: the u of each and the sign of dv/dt there (the winding number of a point is the
        // sum of the signs of the crossings to its right)
        public void Crossings(double v, List<double> positions, List<int> signs)
        {
            positions.Clear();
            signs.Clear();
            List<double> roots = new List<double>();
            foreach (Segment s in segments)
            {
                if (v < s.minV || v > s.maxV)
                {
                    continue;
                }
                double[] g = new double[s.degree + 1];
                for (int i = 0; i <= s.degree; ++i)
                {
                    g[i] = s.y[i] - v * s.w[i];
                }
                roots.Clear();
                Bernstein.Roots(g, s.degree, roots);
                foreach (double t in roots)
                {
                    if (t < -1e-13 || t >= 1 - 1e-13)
                    {
                        continue;
                    }
                    double x = Bernstein.Evaluate(s.x, s.degree, t) / Bernstein.Evaluate(s.w, s.degree, t);
                    double slope = Bernstein.Derivative(g, s.degree, t);
                    positions.Add(x);
                    signs.Add(slope > 0 ? 1 : (slope < 0 ? -1 : 0));
                }
            }
        }

        public bool Inside(double u, double v)
        {
            if (segments.Count == 0)
            {
                return true;
            }
            List<double> positions = new List<double>();
            List<int> signs = new List<int>();
            Crossings(v, positions, signs);
            int winding = 0;
            for (int i = 0; i < positions.Count; ++i)
            {
                if (positions[i] > u)
                {
                    winding += signs[i];
                }
            }
            return winding > 0;
        }

        // The parts of [u0, u1] on the line at v that are visible, as pairs of ends
        public void Intervals(double v, double u0, double u1, List<double> ends)
        {
            ends.Clear();
            if (segments.Count == 0)
            {
                ends.Add(u0);
                ends.Add(u1);
                return;
            }
            List<double> positions = new List<double>();
            List<int> signs = new List<int>();
            Crossings(v, positions, signs);
            int[] order = new int[positions.Count];
            for (int i = 0; i < order.Length; ++i)
            {
                order[i] = i;
            }
            Array.Sort(order, (int a, int b) => positions[a].CompareTo(positions[b]));
            // winding number for u in the gap after the crossing k (ascending order) = the sum of the signs of the crossings k + 1 ...
            int total = 0;
            foreach (int s in signs)
            {
                total += s;
            }
            int winding = total;  // the gap before the first crossing
            double start = u0;
            bool inside = winding > 0;
            for (int k = 0; k <= order.Length; ++k)
            {
                double gapEnd = k < order.Length ? Math.Min(positions[order[k]], u1) : u1;
                double gapStart = Math.Max(start, u0);
                if (inside && gapEnd > gapStart)
                {
                    if (ends.Count > 0 && ends[ends.Count - 1] >= gapStart - 1e-15)
                    {
                        ends[ends.Count - 1] = gapEnd;
                    }
                    else
                    {
                        ends.Add(gapStart);
                        ends.Add(gapEnd);
                    }
                }
                if (k < order.Length)
                {
                    start = Math.Max(start, positions[order[k]]);
                    winding -= signs[order[k]];
                    inside = winding > 0;
                }
            }
        }

        // Whether the rectangle may meet a trim curve
        public bool MeetsBoundary(double u0, double u1, double v0, double v1)
        {
            foreach (Segment s in segments)
            {
                if (s.maxU >= u0 && s.minU <= u1 && s.maxV >= v0 && s.minV <= v1)
                {
                    return true;
                }
            }
            return false;
        }
    }

    // A NURBS surface split into rational Bezier cells
    public sealed class NurbsSurfaceData
    {
        public sealed class Cell
        {
            public double u0, u1, v0, v1;
            public double[] net;  // ((i * (qv + 1)) + j) * 4: homogeneous
            public double minX, minY, minZ, maxX, maxY, maxZ;
        }

        public int pu, pv;
        public double uMin, uMax, vMin, vMax;
        public List<Cell> cells = new List<Cell>();
        public double[] low = new double[3], high = new double[3];
        private readonly Dictionary<long, Cell> lookup = new Dictionary<long, Cell>();
        private List<double> uBreaks = new List<double>(), vBreaks = new List<double>();

        // homogeneous: ((i * nv) + j) * 4
        public string Build(int nu, int uOrder, double[] uKnots, int nv, int vOrder, double[] vKnots, double[] homogeneous, double uMin, double uMax, double vMin, double vMax)
        {
            string error = BezierExtraction.Validate(uOrder, uKnots, nu) ?? BezierExtraction.Validate(vOrder, vKnots, nv);
            if (error != null)
            {
                return error;
            }
            if (!(uMin >= uKnots[uOrder - 1] && uMax <= uKnots[nu] && uMin < uMax && vMin >= vKnots[vOrder - 1] && vMax <= vKnots[nv] && vMin < vMax))
            {
                return "the parameter ranges must lie within the knot ranges";
            }
            for (int i = 0; i < nu * nv; ++i)
            {
                if (!(homogeneous[4 * i + 3] > 0))
                {
                    return "the weights must be positive";
                }
            }
            this.uMin = uMin;
            this.uMax = uMax;
            this.vMin = vMin;
            this.vMax = vMax;
            pu = uOrder - 1;
            pv = vOrder - 1;
            // along u for every column j
            List<BezierExtraction.Piece>[] uPieces = new List<BezierExtraction.Piece>[nv];
            double[] column = new double[nu * 4];
            for (int j = 0; j < nv; ++j)
            {
                for (int i = 0; i < nu; ++i)
                {
                    for (int c = 0; c < 4; ++c)
                    {
                        column[i * 4 + c] = homogeneous[(i * nv + j) * 4 + c];
                    }
                }
                uPieces[j] = BezierExtraction.Extract(uOrder, uKnots, nu, uMin, uMax, column, 4);
            }
            int nuCells = uPieces[0].Count;
            double[] row = new double[nv * 4];
            // then along v for every u piece and index i
            List<List<BezierExtraction.Piece>[]> vPieces = new List<List<BezierExtraction.Piece>[]>();
            for (int a = 0; a < nuCells; ++a)
            {
                List<BezierExtraction.Piece>[] perI = new List<BezierExtraction.Piece>[pu + 1];
                for (int i = 0; i <= pu; ++i)
                {
                    for (int j = 0; j < nv; ++j)
                    {
                        for (int c = 0; c < 4; ++c)
                        {
                            row[j * 4 + c] = uPieces[j][a].control[i * 4 + c];
                        }
                    }
                    perI[i] = BezierExtraction.Extract(vOrder, vKnots, nv, vMin, vMax, row, 4);
                }
                vPieces.Add(perI);
            }
            int nvCells = vPieces[0][0].Count;
            low = new double[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
            high = new double[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
            for (int a = 0; a < nuCells; ++a)
            {
                for (int b = 0; b < nvCells; ++b)
                {
                    Cell cell = new Cell();
                    cell.u0 = uPieces[0][a].start;
                    cell.u1 = uPieces[0][a].end;
                    cell.v0 = vPieces[a][0][b].start;
                    cell.v1 = vPieces[a][0][b].end;
                    cell.net = new double[(pu + 1) * (pv + 1) * 4];
                    cell.minX = cell.minY = cell.minZ = double.PositiveInfinity;
                    cell.maxX = cell.maxY = cell.maxZ = double.NegativeInfinity;
                    for (int i = 0; i <= pu; ++i)
                    {
                        for (int j = 0; j <= pv; ++j)
                        {
                            for (int c = 0; c < 4; ++c)
                            {
                                cell.net[(i * (pv + 1) + j) * 4 + c] = vPieces[a][i][b].control[j * 4 + c];
                            }
                            double w = cell.net[(i * (pv + 1) + j) * 4 + 3];
                            double x = cell.net[(i * (pv + 1) + j) * 4] / w, y = cell.net[(i * (pv + 1) + j) * 4 + 1] / w, z = cell.net[(i * (pv + 1) + j) * 4 + 2] / w;
                            cell.minX = Math.Min(cell.minX, x);
                            cell.maxX = Math.Max(cell.maxX, x);
                            cell.minY = Math.Min(cell.minY, y);
                            cell.maxY = Math.Max(cell.maxY, y);
                            cell.minZ = Math.Min(cell.minZ, z);
                            cell.maxZ = Math.Max(cell.maxZ, z);
                        }
                    }
                    low[0] = Math.Min(low[0], cell.minX);
                    low[1] = Math.Min(low[1], cell.minY);
                    low[2] = Math.Min(low[2], cell.minZ);
                    high[0] = Math.Max(high[0], cell.maxX);
                    high[1] = Math.Max(high[1], cell.maxY);
                    high[2] = Math.Max(high[2], cell.maxZ);
                    cells.Add(cell);
                    lookup[(long)a * 1000003L + b] = cell;
                }
                if (a == 0)
                {
                    for (int b = 0; b < nvCells; ++b)
                    {
                        vBreaks.Add(vPieces[0][0][b].start);
                    }
                    vBreaks.Add(vPieces[0][0][nvCells - 1].end);
                }
                uBreaks.Add(uPieces[0][a].start);
            }
            uBreaks.Add(uPieces[0][nuCells - 1].end);
            return null;
        }

        public Cell Locate(double u, double v)
        {
            int a = Math.Max(0, Math.Min(uBreaks.Count - 2, UpperBound(uBreaks, u) - 1));
            int b = Math.Max(0, Math.Min(vBreaks.Count - 2, UpperBound(vBreaks, v) - 1));
            return lookup[(long)a * 1000003L + b];
        }

        private static int UpperBound(List<double> list, double value)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (list[mid] <= value)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }
            return lo;
        }

        // The position and the derivatives with respect to the local parameters (s, t) in [0, 1] of the cell
        public void EvaluateCell(Cell cell, double s, double t, double[] p, double[] ps, double[] pt)
        {
            double[] bu = new double[pu + 1], bv = new double[pv + 1], du = new double[pu + 1], dv = new double[pv + 1];
            Bernstein.Basis(pu, s, bu);
            Bernstein.Basis(pv, t, bv);
            if (pu > 0)
            {
                double[] lower = new double[pu];
                Bernstein.Basis(pu - 1, s, lower);
                for (int i = 0; i <= pu; ++i)
                {
                    du[i] = pu * ((i > 0 ? lower[i - 1] : 0) - (i < pu ? lower[i] : 0));
                }
            }
            if (pv > 0)
            {
                double[] lower = new double[pv];
                Bernstein.Basis(pv - 1, t, lower);
                for (int j = 0; j <= pv; ++j)
                {
                    dv[j] = pv * ((j > 0 ? lower[j - 1] : 0) - (j < pv ? lower[j] : 0));
                }
            }
            double n0 = 0, n1 = 0, n2 = 0, w = 0, nsx = 0, nsy = 0, nsz = 0, ws = 0, ntx = 0, nty = 0, ntz = 0, wt = 0;
            for (int i = 0; i <= pu; ++i)
            {
                for (int j = 0; j <= pv; ++j)
                {
                    int k = (i * (pv + 1) + j) * 4;
                    double b = bu[i] * bv[j], bs = du[i] * bv[j], bt = bu[i] * dv[j];
                    n0 += b * cell.net[k];
                    n1 += b * cell.net[k + 1];
                    n2 += b * cell.net[k + 2];
                    w += b * cell.net[k + 3];
                    nsx += bs * cell.net[k];
                    nsy += bs * cell.net[k + 1];
                    nsz += bs * cell.net[k + 2];
                    ws += bs * cell.net[k + 3];
                    ntx += bt * cell.net[k];
                    nty += bt * cell.net[k + 1];
                    ntz += bt * cell.net[k + 2];
                    wt += bt * cell.net[k + 3];
                }
            }
            p[0] = n0 / w;
            p[1] = n1 / w;
            p[2] = n2 / w;
            ps[0] = (nsx - p[0] * ws) / w;
            ps[1] = (nsy - p[1] * ws) / w;
            ps[2] = (nsz - p[2] * ws) / w;
            pt[0] = (ntx - p[0] * wt) / w;
            pt[1] = (nty - p[1] * wt) / w;
            pt[2] = (ntz - p[2] * wt) / w;
        }

        // The position and the derivatives with respect to u and v
        public void Evaluate(double u, double v, double[] p, double[] pu_, double[] pv_)
        {
            Cell cell = Locate(u, v);
            double s = (u - cell.u0) / (cell.u1 - cell.u0), t = (v - cell.v0) / (cell.v1 - cell.v0);
            EvaluateCell(cell, Math.Min(1, Math.Max(0, s)), Math.Min(1, Math.Max(0, t)), p, pu_, pv_);
            for (int c = 0; c < 3; ++c)
            {
                pu_[c] /= cell.u1 - cell.u0;
                pv_[c] /= cell.v1 - cell.v0;
            }
        }
    }

    public sealed unsafe class NurbsPatch : Shape
    {
        // The size (in the parameters of a Bezier cell) at which Newton's method takes over from the subdivision
        public static double LeafSize = 1.0 / 16;

        private readonly NurbsSurfaceData surface;
        private readonly TrimRegion trim;
        private readonly Transform renderFromObject, objectFromRender;
        private readonly bool reverseOrientation, transformSwapsHandedness;
        private readonly Bounds3f bounds;
        private readonly double size;
        private readonly int resolution;
        // the sampler (built when needed)
        private double[] cellWeights, cellCumulative, cellVisible;
        private double area;
        private readonly object sync = new object();

        public NurbsPatch(NurbsSurfaceData surface, TrimRegion trim, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation)
        {
            this.surface = surface;
            this.trim = trim;
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            transformSwapsHandedness = renderFromObject.SwapsHandedness();
            size = Math.Sqrt(Sq(surface.high[0] - surface.low[0]) + Sq(surface.high[1] - surface.low[1]) + Sq(surface.high[2] - surface.low[2]));
            resolution = 32;
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f point = renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? surface.high[0] : surface.low[0]),
                                                                   (Float)((corner & 2) != 0 ? surface.high[1] : surface.low[1]),
                                                                   (Float)((corner & 4) != 0 ? surface.high[2] : surface.low[2])));
                result = corner == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
        }

        private static double Sq(double x)
        {
            return x * x;
        }

        // (not in pbrt-v4) The surface for the diced displacement
        public IDicedSurface CreateDicedSurface()
        {
            return new NurbsDicedSurface(surface, trim);
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Intersection

        private struct Node
        {
            public double[] f1, f2;
            public double s0, s1, t0, t1;
        }

        private bool Trace(Ray ray, Float tMax, out double hitU, out double hitV, out double hitT, out double length, bool any)
        {
            hitU = hitV = hitT = 0;
            Point3f oo = objectFromRender.Apply(ray.o);
            Vector3f dd = objectFromRender.Apply(ray.d);
            length = Math.Sqrt((double)dd.x * dd.x + (double)dd.y * dd.y + (double)dd.z * dd.z);
            if (length == 0 || double.IsNaN(length))
            {
                return false;
            }
            double[] o = { oo.x, oo.y, oo.z };
            double[] d = { dd.x / length, dd.y / length, dd.z / length };
            double tMaxObject = float.IsPositiveInfinity(tMax) ? double.PositiveInfinity : tMax * length;
            double tMin = 1e-6 * size;
            // two unit vectors orthogonal to d and to each other
            double[] n1 = new double[3], n2 = new double[3];
            int axis = Math.Abs(d[0]) < Math.Abs(d[1]) ? (Math.Abs(d[0]) < Math.Abs(d[2]) ? 0 : 2) : (Math.Abs(d[1]) < Math.Abs(d[2]) ? 1 : 2);
            double[] e = new double[3];
            e[axis] = 1;
            n1[0] = d[1] * e[2] - d[2] * e[1];
            n1[1] = d[2] * e[0] - d[0] * e[2];
            n1[2] = d[0] * e[1] - d[1] * e[0];
            double l1 = Math.Sqrt(n1[0] * n1[0] + n1[1] * n1[1] + n1[2] * n1[2]);
            n1[0] /= l1;
            n1[1] /= l1;
            n1[2] /= l1;
            n2[0] = d[1] * n1[2] - d[2] * n1[1];
            n2[1] = d[2] * n1[0] - d[0] * n1[2];
            n2[2] = d[0] * n1[1] - d[1] * n1[0];

            bool found = false;
            double bestT = tMaxObject;
            foreach (NurbsSurfaceData.Cell cell in surface.cells)
            {
                if (!SlabTest(cell, o, d, size * 1e-9, tMin, bestT))
                {
                    continue;
                }
                if (TraceCell(cell, o, d, n1, n2, tMin, bestT, any, ref hitT, ref hitU, ref hitV))
                {
                    found = true;
                    bestT = hitT;
                    if (any)
                    {
                        return true;
                    }
                }
            }
            hitT = bestT;
            return found;
        }

        private static bool SlabTest(NurbsSurfaceData.Cell cell, double[] o, double[] d, double pad, double tMin, double tMax)
        {
            double t0 = tMin, t1 = tMax;
            double[] lo = { cell.minX - pad, cell.minY - pad, cell.minZ - pad }, hi = { cell.maxX + pad, cell.maxY + pad, cell.maxZ + pad };
            for (int a = 0; a < 3; ++a)
            {
                if (d[a] == 0)
                {
                    if (o[a] < lo[a] || o[a] > hi[a])
                    {
                        return false;
                    }
                    continue;
                }
                double near = (lo[a] - o[a]) / d[a], far = (hi[a] - o[a]) / d[a];
                if (near > far)
                {
                    double tmp = near;
                    near = far;
                    far = tmp;
                }
                t0 = Math.Max(t0, near);
                t1 = Math.Min(t1, far);
                if (t0 > t1)
                {
                    return false;
                }
            }
            return true;
        }

        private bool TraceCell(NurbsSurfaceData.Cell cell, double[] o, double[] d, double[] n1, double[] n2, double tMin, double tMax, bool any,
                               ref double bestT, ref double bestU, ref double bestV)
        {
            int pu = surface.pu, pv = surface.pv, count = (pu + 1) * (pv + 1);
            Node root = new Node();
            root.f1 = new double[count];
            root.f2 = new double[count];
            for (int k = 0; k < count; ++k)
            {
                double w = cell.net[4 * k + 3];
                double rx = cell.net[4 * k] - o[0] * w, ry = cell.net[4 * k + 1] - o[1] * w, rz = cell.net[4 * k + 2] - o[2] * w;
                root.f1[k] = n1[0] * rx + n1[1] * ry + n1[2] * rz;
                root.f2[k] = n2[0] * rx + n2[1] * ry + n2[2] * rz;
            }
            root.s0 = root.t0 = 0;
            root.s1 = root.t1 = 1;
            double scale = 0;
            for (int k = 0; k < count; ++k)
            {
                scale = Math.Max(scale, Math.Max(Math.Abs(root.f1[k]), Math.Abs(root.f2[k])));
            }
            double tolerance = 1e-12 * scale;
            bool found = false;
            Stack<Node> stack = new Stack<Node>();
            stack.Push(root);
            double[] p = new double[3], ps = new double[3], pt = new double[3];
            while (stack.Count > 0)
            {
                Node node = stack.Pop();
                if (Excludes(node.f1, count, tolerance) || Excludes(node.f2, count, tolerance))
                {
                    continue;
                }
                double ws = node.s1 - node.s0, wt = node.t1 - node.t0;
                if (ws <= LeafSize && wt <= LeafSize)
                {
                    double s = 0.5 * (node.s0 + node.s1), t = 0.5 * (node.t0 + node.t1);
                    bool converged = Newton(cell, o, d, n1, n2, ref s, ref t, p, ps, pt) &&
                                     s >= node.s0 - 0.5 * ws && s <= node.s1 + 0.5 * ws && t >= node.t0 - 0.5 * wt && t <= node.t1 + 0.5 * wt;
                    if (converged)
                    {
                        double hitT = d[0] * (p[0] - o[0]) + d[1] * (p[1] - o[1]) + d[2] * (p[2] - o[2]);
                        if (hitT > tMin && hitT < tMax)
                        {
                            double u = cell.u0 + s * (cell.u1 - cell.u0), v = cell.v0 + t * (cell.v1 - cell.v0);
                            if (trim.Inside(u, v))
                            {
                                found = true;
                                bestT = hitT;
                                bestU = u;
                                bestV = v;
                                tMax = hitT;
                                if (any)
                                {
                                    return true;
                                }
                            }
                        }
                        continue;
                    }
                    if (ws <= 1.0 / 1024 && wt <= 1.0 / 1024)
                    {
                        continue;
                    }
                }
                Node a = new Node(), b = new Node();
                bool alongU = ws >= wt;
                a.f1 = new double[count];
                a.f2 = new double[count];
                b.f1 = new double[count];
                b.f2 = new double[count];
                Split(node.f1, pu, pv, alongU, a.f1, b.f1);
                Split(node.f2, pu, pv, alongU, a.f2, b.f2);
                if (alongU)
                {
                    double mid = 0.5 * (node.s0 + node.s1);
                    a.s0 = node.s0; a.s1 = mid; a.t0 = node.t0; a.t1 = node.t1;
                    b.s0 = mid; b.s1 = node.s1; b.t0 = node.t0; b.t1 = node.t1;
                }
                else
                {
                    double mid = 0.5 * (node.t0 + node.t1);
                    a.s0 = node.s0; a.s1 = node.s1; a.t0 = node.t0; a.t1 = mid;
                    b.s0 = node.s0; b.s1 = node.s1; b.t0 = mid; b.t1 = node.t1;
                }
                stack.Push(a);
                stack.Push(b);
            }
            return found;
        }

        private static bool Excludes(double[] f, int count, double tolerance)
        {
            double low = f[0], high = f[0];
            for (int k = 1; k < count; ++k)
            {
                low = Math.Min(low, f[k]);
                high = Math.Max(high, f[k]);
            }
            return low > tolerance || high < -tolerance;
        }

        // de Casteljau subdivision at 1/2 of a net f[i * (pv + 1) + j]
        private static void Split(double[] f, int pu, int pv, bool alongU, double[] left, double[] right)
        {
            int n = alongU ? pu : pv, lines = alongU ? pv + 1 : pu + 1;
            double[] work = new double[n + 1];
            for (int line = 0; line < lines; ++line)
            {
                for (int i = 0; i <= n; ++i)
                {
                    work[i] = alongU ? f[i * (pv + 1) + line] : f[line * (pv + 1) + i];
                }
                if (alongU)
                {
                    left[0 * (pv + 1) + line] = work[0];
                    right[n * (pv + 1) + line] = work[n];
                }
                else
                {
                    left[line * (pv + 1)] = work[0];
                    right[line * (pv + 1) + n] = work[n];
                }
                for (int level = 1; level <= n; ++level)
                {
                    for (int i = 0; i <= n - level; ++i)
                    {
                        work[i] = 0.5 * (work[i] + work[i + 1]);
                    }
                    if (alongU)
                    {
                        left[level * (pv + 1) + line] = work[0];
                        right[(n - level) * (pv + 1) + line] = work[n - level];
                    }
                    else
                    {
                        left[line * (pv + 1) + level] = work[0];
                        right[line * (pv + 1) + n - level] = work[n - level];
                    }
                }
            }
        }

        private bool Newton(NurbsSurfaceData.Cell cell, double[] o, double[] d, double[] n1, double[] n2, ref double s, ref double t, double[] p, double[] ps, double[] pt)
        {
            for (int iteration = 0; iteration < 20; ++iteration)
            {
                surface.EvaluateCell(cell, s, t, p, ps, pt);
                double rx = p[0] - o[0], ry = p[1] - o[1], rz = p[2] - o[2];
                double f1 = n1[0] * rx + n1[1] * ry + n1[2] * rz, f2 = n2[0] * rx + n2[1] * ry + n2[2] * rz;
                double a = n1[0] * ps[0] + n1[1] * ps[1] + n1[2] * ps[2], b = n1[0] * pt[0] + n1[1] * pt[1] + n1[2] * pt[2];
                double c = n2[0] * ps[0] + n2[1] * ps[1] + n2[2] * ps[2], e = n2[0] * pt[0] + n2[1] * pt[1] + n2[2] * pt[2];
                double det = a * e - b * c;
                if (Math.Abs(f1) + Math.Abs(f2) < 1e-13 * size && iteration > 0)
                {
                    break;
                }
                if (det == 0 || double.IsNaN(det))
                {
                    return false;
                }
                double ds = (e * f1 - b * f2) / det, dt = (-c * f1 + a * f2) / det;
                double step = Math.Max(Math.Abs(ds), Math.Abs(dt));
                if (step > 0.5)
                {
                    ds *= 0.5 / step;
                    dt *= 0.5 / step;
                }
                s -= ds;
                t -= dt;
                s = Math.Min(1.05, Math.Max(-0.05, s));
                t = Math.Min(1.05, Math.Max(-0.05, t));
                if (Math.Abs(ds) + Math.Abs(dt) < 1e-14)
                {
                    break;
                }
            }
            if (s < -1e-7 || s > 1 + 1e-7 || t < -1e-7 || t > 1 + 1e-7)
            {
                return false;
            }
            s = Math.Min(1, Math.Max(0, s));
            t = Math.Min(1, Math.Max(0, t));
            surface.EvaluateCell(cell, s, t, p, ps, pt);
            double qx = p[0] - o[0], qy = p[1] - o[1], qz = p[2] - o[2];
            double g1 = n1[0] * qx + n1[1] * qy + n1[2] * qz, g2 = n2[0] * qx + n2[1] * qy + n2[2] * qz;
            return Math.Abs(g1) + Math.Abs(g2) < 1e-9 * size;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (!Trace(ray, tMax, out double u, out double v, out double t, out double length, false))
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromHit(ray, u, v);
            return new ShapeIntersection(intr, (Float)(t / length));
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            return Trace(ray, tMax, out double u, out double v, out double t, out double length, true);
        }

        // The object space position and the derivatives with respect to the normalized parameters, nudged away from a singular point
        private void Geometry(ref double u, ref double v, out double[] p, out double[] dpdu, out double[] dpdv)
        {
            p = new double[3];
            dpdu = new double[3];
            dpdv = new double[3];
            double du = surface.uMax - surface.uMin, dv = surface.vMax - surface.vMin;
            for (int attempt = 0; attempt < 4; ++attempt)
            {
                surface.Evaluate(u, v, p, dpdu, dpdv);
                for (int c = 0; c < 3; ++c)
                {
                    dpdu[c] *= du;
                    dpdv[c] *= dv;
                }
                double cx = dpdu[1] * dpdv[2] - dpdu[2] * dpdv[1], cy = dpdu[2] * dpdv[0] - dpdu[0] * dpdv[2], cz = dpdu[0] * dpdv[1] - dpdu[1] * dpdv[0];
                double l2 = cx * cx + cy * cy + cz * cz;
                double a = dpdu[0] * dpdu[0] + dpdu[1] * dpdu[1] + dpdu[2] * dpdu[2], b = dpdv[0] * dpdv[0] + dpdv[1] * dpdv[1] + dpdv[2] * dpdv[2];
                if (l2 > 1e-18 * a * b && a > 0 && b > 0)
                {
                    return;
                }
                double shrink = 1e-3 * (attempt + 1);
                u += (0.5 * (surface.uMin + surface.uMax) - u) * shrink;
                v += (0.5 * (surface.vMin + surface.vMax) - v) * shrink;
            }
        }

        private SurfaceInteraction InteractionFromHit(Ray ray, double u, double v)
        {
            Geometry(ref u, ref v, out double[] p, out double[] dpdu, out double[] dpdv);
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu[0], (Float)dpdu[1], (Float)dpdu[2]));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv[0], (Float)dpdv[1], (Float)dpdv[2]));
            Point2f uv = new Point2f((Float)((u - surface.uMin) / (surface.uMax - surface.uMin)), (Float)((v - surface.vMin) / (surface.vMax - surface.vMin)));
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new SurfaceInteraction(new Point3fi(pr, pError), uv, -ray.d, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), ray.time, flipNormal, 0);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Area and sampling

        // |dP/du x dP/dv| in render space, with respect to u and v themselves
        private double Jacobian(double u, double v)
        {
            double[] p = new double[3], pu = new double[3], pv = new double[3];
            surface.Evaluate(u, v, p, pu, pv);
            Vector3f a = renderFromObject.Apply(new Vector3f((Float)pu[0], (Float)pu[1], (Float)pu[2]));
            Vector3f b = renderFromObject.Apply(new Vector3f((Float)pv[0], (Float)pv[1], (Float)pv[2]));
            Vector3f c = pbrt.Cross(a, b);
            return Math.Sqrt((double)c.x * c.x + (double)c.y * c.y + (double)c.z * c.z);
        }

        private static readonly double[] GaussNodes = { -0.9324695142031521, -0.6612093864662645, -0.2386191860831969, 0.2386191860831969, 0.6612093864662645, 0.9324695142031521 };
        private static readonly double[] GaussWeights = { 0.1713244923791704, 0.3607615730481386, 0.4679139345726910, 0.4679139345726910, 0.3607615730481386, 0.1713244923791704 };

        // The visible parameter area of a cell of the grid, and the integral of the Jacobian over its visible part
        private void CellIntegrals(double u0, double u1, double v0, double v1, out double visible, out double areaIntegral)
        {
            visible = 0;
            areaIntegral = 0;
            bool boundary = trim.MeetsBoundary(u0, u1, v0, v1);
            if (!boundary && !trim.Inside(0.5 * (u0 + u1), 0.5 * (v0 + v1)))
            {
                return;
            }
            List<double> ends = new List<double>();
            if (!boundary)
            {
                LineIntegrals(v0, v1, u0, u1, ends, out visible, out areaIntegral);
                return;
            }
            // v is integrated adaptively (the visible length on a line is not smooth where a trim curve has a vertex or a tangent line), u exactly
            double cellParameterArea = (u1 - u0) * (v1 - v0);
            double center = Jacobian(0.5 * (u0 + u1), 0.5 * (v0 + v1));
            AdaptiveLines(v0, v1, u0, u1, ends, 0, 1e-11 * cellParameterArea, 1e-11 * cellParameterArea * Math.Max(center, 1e-300), out visible, out areaIntegral);
        }

        // Gauss-Legendre over [va, vb] of the visible length and of the integral of the Jacobian along the lines
        private void LineIntegrals(double va, double vb, double u0, double u1, List<double> ends, out double visible, out double areaIntegral)
        {
            visible = 0;
            areaIntegral = 0;
            for (int k = 0; k < GaussNodes.Length; ++k)
            {
                double v = 0.5 * (va + vb) + 0.5 * (vb - va) * GaussNodes[k];
                double wv = 0.5 * (vb - va) * GaussWeights[k];
                trim.Intervals(v, u0, u1, ends);
                for (int i = 0; i + 1 < ends.Count; i += 2)
                {
                    double a = ends[i], b = ends[i + 1];
                    visible += wv * (b - a);
                    double line = 0;
                    for (int m = 0; m < GaussNodes.Length; ++m)
                    {
                        line += 0.5 * (b - a) * GaussWeights[m] * Jacobian(0.5 * (a + b) + 0.5 * (b - a) * GaussNodes[m], v);
                    }
                    areaIntegral += wv * line;
                }
            }
        }

        private void AdaptiveLines(double va, double vb, double u0, double u1, List<double> ends, int depth, double toleranceVisible, double toleranceArea,
                                   out double visible, out double areaIntegral)
        {
            LineIntegrals(va, vb, u0, u1, ends, out double whole, out double wholeArea);
            double mid = 0.5 * (va + vb);
            LineIntegrals(va, mid, u0, u1, ends, out double leftVisible, out double leftArea);
            LineIntegrals(mid, vb, u0, u1, ends, out double rightVisible, out double rightArea);
            double split = leftVisible + rightVisible, splitArea = leftArea + rightArea;
            if (depth >= 16 || (Math.Abs(split - whole) <= toleranceVisible && Math.Abs(splitArea - wholeArea) <= toleranceArea))
            {
                visible = split;
                areaIntegral = splitArea;
                return;
            }
            AdaptiveLines(va, mid, u0, u1, ends, depth + 1, toleranceVisible, toleranceArea, out leftVisible, out leftArea);
            AdaptiveLines(mid, vb, u0, u1, ends, depth + 1, toleranceVisible, toleranceArea, out rightVisible, out rightArea);
            visible = leftVisible + rightVisible;
            areaIntegral = leftArea + rightArea;
        }

        private void BuildSampler()
        {
            lock (sync)
            {
                if (cellWeights != null)
                {
                    return;
                }
                int cells = resolution * resolution;
                double[] weights = new double[cells], visibleAreas = new double[cells];
                double sum = 0;
                for (int j = 0; j < resolution; ++j)
                {
                    for (int i = 0; i < resolution; ++i)
                    {
                        double u0 = surface.uMin + (surface.uMax - surface.uMin) * i / resolution, u1 = surface.uMin + (surface.uMax - surface.uMin) * (i + 1) / resolution;
                        double v0 = surface.vMin + (surface.vMax - surface.vMin) * j / resolution, v1 = surface.vMin + (surface.vMax - surface.vMin) * (j + 1) / resolution;
                        CellIntegrals(u0, u1, v0, v1, out visibleAreas[j * resolution + i], out weights[j * resolution + i]);
                        sum += weights[j * resolution + i];
                    }
                }
                double[] cumulative = new double[cells];
                double running = 0;
                for (int k = 0; k < cells; ++k)
                {
                    running += weights[k];
                    cumulative[k] = running;
                }
                area = sum;
                cellVisible = visibleAreas;
                cellCumulative = cumulative;
                cellWeights = weights;
            }
        }

        public override Float Area()
        {
            BuildSampler();
            return (Float)area;
        }

        // The density of the area at (u, v): the probability of the cell, over the visible parameter area of the cell and the Jacobian
        private double AreaDensity(double u, double v)
        {
            BuildSampler();
            int i = Math.Max(0, Math.Min(resolution - 1, (int)((u - surface.uMin) / (surface.uMax - surface.uMin) * resolution)));
            int j = Math.Max(0, Math.Min(resolution - 1, (int)((v - surface.vMin) / (surface.vMax - surface.vMin) * resolution)));
            int cell = j * resolution + i;
            double jacobian = Jacobian(u, v);
            if (!(cellVisible[cell] > 0) || !(jacobian > 0) || !(area > 0))
            {
                return 0;
            }
            return cellWeights[cell] / area / cellVisible[cell] / jacobian;
        }

        private Interaction InteractionAt(double u, double v)
        {
            Geometry(ref u, ref v, out double[] p, out double[] dpdu, out double[] dpdv);
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)dpdu[0], (Float)dpdu[1], (Float)dpdu[2]));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)dpdv[0], (Float)dpdv[1], (Float)dpdv[2]));
            Normal3f n = pbrt.Normalize((Normal3f)pbrt.Cross(pu, pv));
            if (reverseOrientation ^ transformSwapsHandedness)
            {
                n = -n;
            }
            Point2f uv = new Point2f((Float)((u - surface.uMin) / (surface.uMax - surface.uMin)), (Float)((v - surface.vMin) / (surface.vMax - surface.vMin)));
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new Interaction(new Point3fi(pr, pError), n, uv);
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            BuildSampler();
            if (!(area > 0))
            {
                return default;
            }
            double target = u.x * area;
            int low = 0, high = cellCumulative.Length - 1;
            while (low < high)
            {
                int mid = (low + high) >> 1;
                if (cellCumulative[mid] > target)
                {
                    high = mid;
                }
                else
                {
                    low = mid + 1;
                }
            }
            int cell = low;
            double start = cell == 0 ? 0 : cellCumulative[cell - 1];
            double remapped = cellWeights[cell] > 0 ? Math.Min((target - start) / cellWeights[cell], 1 - 1e-12) : 0.5;
            int ci = cell % resolution, cj = cell / resolution;
            double u0 = surface.uMin + (surface.uMax - surface.uMin) * ci / resolution, u1 = surface.uMin + (surface.uMax - surface.uMin) * (ci + 1) / resolution;
            double v0 = surface.vMin + (surface.vMax - surface.vMin) * cj / resolution, v1 = surface.vMin + (surface.vMax - surface.vMin) * (cj + 1) / resolution;
            // rejection in the cell: uniform points of the cell until one is visible, from the quasi-random sequence started at the sample
            double x = remapped, y = u.y;
            ulong state = ((ulong)BitConverter.SingleToInt32Bits(u.x) << 32 | (uint)BitConverter.SingleToInt32Bits(u.y)) * 0x9E3779B97F4A7C15UL + 0x1234567UL;
            for (int attempt = 0; attempt < 200; ++attempt)
            {
                double su = u0 + (u1 - u0) * Math.Min(x, 1 - 1e-12), sv = v0 + (v1 - v0) * Math.Min(y, 1 - 1e-12);
                if (trim.Inside(su, sv))
                {
                    double density = AreaDensity(su, sv);
                    if (!(density > 0))
                    {
                        return default;
                    }
                    return new ShapeSample(InteractionAt(su, sv), (Float)density);
                }
                // independent uniform points from here on (the first point is uniform in the cell and accepted with probability |visible| / |cell|, the others
                // are standard rejection sampling, so the accepted point is uniform in the visible part)
                x = NextUniform(ref state);
                y = NextUniform(ref state);
            }
            return default;
        }

        private static double NextUniform(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / 9007199254740992.0);
        }

        public override Float PDF(Interaction intr)
        {
            double u = surface.uMin + intr.uv.x * (surface.uMax - surface.uMin), v = surface.vMin + intr.uv.y * (surface.vMax - surface.vMin);
            return (Float)AreaDensity(u, v);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            Ray ray = ctx.SpawnRay(wi);
            if (!Trace(ray, Infinity, out double u, out double v, out double t, out double length, false))
            {
                return 0;
            }
            Interaction at = InteractionAt(u, v);
            return PatchSampling.AreaToSolidAngle(AreaDensity(u, v), at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return "[ NurbsPatch ]";
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Creation

        // "nupatch": nu, uorder, uknots, umin, umax, nv, vorder, vknots, vmin, vmax, P (x y z per point, u varying slowest as in RiNuPatch: P[i * nv + j]
        // is not what RenderMan uses: RiNuPatch lists the points with u varying fastest, P[j * nu + i]), "Pw" (x y z w homogeneous) or "w".
        // The trim curves (RiTrimCurve): "trimncurves" (the curves of each loop), "trimorder", "trimknots", "trimmin", "trimmax", "trimn", "trimu", "trimv",
        // "trimw" (homogeneous: u and v multiplied by w), for all the curves in order.
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> none = new pstd.vector<Shape>();
            int nu = parameters.GetOneInt("nu", 0), nv = parameters.GetOneInt("nv", 0);
            int uOrder = parameters.GetOneInt("uorder", 4), vOrder = parameters.GetOneInt("vorder", 4);
            List<Float> uk = parameters.GetFloatArray("uknots"), vk = parameters.GetFloatArray("vknots");
            double[] uKnots = new double[uk.Count], vKnots = new double[vk.Count];
            for (int i = 0; i < uKnots.Length; ++i)
            {
                uKnots[i] = uk[i];
            }
            for (int i = 0; i < vKnots.Length; ++i)
            {
                vKnots[i] = vk[i];
            }
            double[] homogeneous = new double[4 * nu * nv];
            List<Float> pw = parameters.GetFloatArray("Pw");
            if (pw.Count != 0)
            {
                if (pw.Count != 4 * nu * nv)
                {
                    pbrt.Error(loc, "nupatch: \"Pw\" must hold 4 * nu * nv values.");
                    return none;
                }
                for (int j = 0; j < nv; ++j)
                {
                    for (int i = 0; i < nu; ++i)
                    {
                        for (int c = 0; c < 4; ++c)
                        {
                            homogeneous[(i * nv + j) * 4 + c] = pw[4 * (j * nu + i) + c];
                        }
                    }
                }
            }
            else
            {
                List<Point3f> points = parameters.GetPoint3fArray("P");
                List<Float> weights = parameters.GetFloatArray("w");
                if (points.Count != nu * nv || (weights.Count != 0 && weights.Count != nu * nv))
                {
                    pbrt.Error(loc, "nupatch: \"P\" (and \"w\") must hold nu * nv values.");
                    return none;
                }
                for (int j = 0; j < nv; ++j)
                {
                    for (int i = 0; i < nu; ++i)
                    {
                        Point3f p = points[j * nu + i];
                        double w = weights.Count != 0 ? weights[j * nu + i] : 1;
                        homogeneous[(i * nv + j) * 4] = p.x * w;
                        homogeneous[(i * nv + j) * 4 + 1] = p.y * w;
                        homogeneous[(i * nv + j) * 4 + 2] = p.z * w;
                        homogeneous[(i * nv + j) * 4 + 3] = w;
                    }
                }
            }
            if (nu < 2 || nv < 2 || uKnots.Length != nu + uOrder || vKnots.Length != nv + vOrder)
            {
                pbrt.Error(loc, "nupatch: inconsistent nu, nv, uorder, vorder or knot vectors.");
                return none;
            }
            double uMin = parameters.GetOneFloat("umin", (Float)uKnots[uOrder - 1]), uMax = parameters.GetOneFloat("umax", (Float)uKnots[nu]);
            double vMin = parameters.GetOneFloat("vmin", (Float)vKnots[vOrder - 1]), vMax = parameters.GetOneFloat("vmax", (Float)vKnots[nv]);
            NurbsSurfaceData data = new NurbsSurfaceData();
            string error = data.Build(nu, uOrder, uKnots, nv, vOrder, vKnots, homogeneous, uMin, uMax, vMin, vMax);
            if (error != null)
            {
                pbrt.Error(loc, "nupatch: " + error + ".");
                return none;
            }
            TrimRegion trim = new TrimRegion();
            List<int> curvesPerLoop = parameters.GetIntArray("trimncurves");
            if (curvesPerLoop.Count != 0)
            {
                int total = 0;
                foreach (int c in curvesPerLoop)
                {
                    total += c;
                }
                List<int> orders = parameters.GetIntArray("trimorder"), counts = parameters.GetIntArray("trimn");
                List<Float> knots = parameters.GetFloatArray("trimknots"), mins = parameters.GetFloatArray("trimmin"), maxs = parameters.GetFloatArray("trimmax");
                List<Float> tu = parameters.GetFloatArray("trimu"), tv = parameters.GetFloatArray("trimv"), tw = parameters.GetFloatArray("trimw");
                if (tw.Count == 0)
                {
                    // non-rational trim curves: the weights are 1
                    for (int i = 0; i < tu.Count; ++i)
                    {
                        tw.Add(1);
                    }
                }
                if (orders.Count != total || counts.Count != total || mins.Count != total || maxs.Count != total)
                {
                    pbrt.Error(loc, "nupatch: \"trimorder\", \"trimn\", \"trimmin\" and \"trimmax\" must hold one value per trim curve.");
                    return none;
                }
                int knotCursor = 0, pointCursor = 0;
                for (int c = 0; c < total; ++c)
                {
                    int order = orders[c], n = counts[c];
                    if (knotCursor + n + order > knots.Count || pointCursor + n > tu.Count || tv.Count != tu.Count || tw.Count != tu.Count)
                    {
                        pbrt.Error(loc, "nupatch: trim curve data too short.");
                        return none;
                    }
                    double[] curveKnots = new double[n + order];
                    for (int i = 0; i < curveKnots.Length; ++i)
                    {
                        curveKnots[i] = knots[knotCursor + i];
                    }
                    double[] uvw = new double[3 * n];
                    for (int i = 0; i < n; ++i)
                    {
                        uvw[3 * i] = tu[pointCursor + i];
                        uvw[3 * i + 1] = tv[pointCursor + i];
                        uvw[3 * i + 2] = tw[pointCursor + i];
                    }
                    string curveError = BezierExtraction.Validate(order, curveKnots, n);
                    if (curveError != null)
                    {
                        pbrt.Error(loc, "nupatch: trim curve: " + curveError + ".");
                        return none;
                    }
                    trim.AddCurve(order, curveKnots, n, uvw, Math.Max(mins[c], curveKnots[order - 1]), Math.Min(maxs[c], curveKnots[n]));
                    knotCursor += n + order;
                    pointCursor += n;
                }
            }
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            shapes.push_back(new NurbsPatch(data, trim, renderFromObject, objectFromRender, reverseOrientation));
            return shapes;
        }
    }
}  // namespace pbrt
