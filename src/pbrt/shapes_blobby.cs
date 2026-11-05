// (not in pbrt-v4) shapes_blobby.cs
//
// RiBlobby: the surface f(p) = threshold (0.421 in RenderMan) of a field that is built by a small program of leaves (constant, ellipsoid, segment) and
// operations (add, multiply, maximum, minimum, subtract, divide, negate, identity). The inside is where f is larger than the threshold.
//
//   Shape "blobby" "integer nleaf" N "integer code" [...] "float floats" [...] ("float threshold" 0.421)
//   (RenderMan spellings: "nleaf", "code", "flt" / "floats"; the strings of RiBlobby are not used)
//
//   code:   1000 c            constant, floats[c]
//           1001 m            ellipsoid, the 16 floats from m are the matrix that takes the unit sphere to the blob (RenderMan row vector convention:
//                             the translation is in floats m + 12 .. m + 14); the field is 1 - 22/9 r^2 + 17/9 r^4 - 4/9 r^6 for r < 1 and 0 outside
//           1002 s            segment, 23 floats from s: x0 y0 z0 x1 y1 z1 radius and then the 16 floats of a matrix as above; r is the distance to
//                             the segment, divided by the radius, in the space of the segment
//           0 n i1 .. in      add          1 n i1 .. in   multiply     2 n i1 .. in   maximum     3 n i1 .. in   minimum
//           4 2 i1 i2         subtract     5 2 i1 i2      divide (x / 0 is 0)          6 1 i      negate         7 1 i     identity
//   The leaves come first (their values are numbered 0 .. nleaf - 1 in the order of the code), the value of the k-th operation is numbered nleaf + k,
//   the field is the value of the last instruction.
//
// Intersection: the ray is cut to the union of the supports of the leaves, and the sign changes of f - threshold along it are isolated by subdivision
// with interval arithmetic of the program (the leaves have exact bounds over an interval of the ray: the ellipsoid by a quadratic, the segment by the
// convexity of the distance), so no root is missed unless two of them are closer than 2^-12 of the length of the cut ray and have no sign change
// between them; the root itself is found by the Illinois method.
//
// Sampling: exactly uniform in area without a mesh. A random line (a direction uniform on the sphere, a point uniform in the disk of the bounding sphere
// perpendicular to it) crosses the surface at points where it enters the inside; the integral of the cosine over the directions that enter at a point is
// pi for every point, so choosing a random one of K slots and taking the entering crossing in that slot (rejecting the line when there is none) has the same
// density at every point of the surface. K is at least the largest number of entering crossings that a line can have (the number of leaves is a safe
// guess for sums of blobs; when it is exceeded the surplus crossings can not be chosen). The area follows from Crofton's formula, 4 pi R^2 times the
// mean number of entering crossings, with a fixed number of lines of a Halton sequence (relative error of a few 0.1 %, or give "float area").

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed class BlobbyField
    {
        public const int Constant = 0, Ellipsoid = 1, Segment = 2;

        private sealed class Instruction
        {
            public int code;
            public int[] operands;
        }

        public readonly double threshold;
        private readonly int leafCount;
        private readonly int[] kind;
        private readonly double[] constant;
        private readonly double[][] inverse;     // the inverse of the matrix of the leaf, 16 doubles (row vector convention)
        private readonly double[][] segment;     // x0 y0 z0 x1 y1 z1 radius
        private readonly Instruction[] operations;
        private readonly double[] low = new double[3], high = new double[3];
        private readonly bool hasSupport;

        public bool HasSupport
        {
            get { return hasSupport; }
        }

        public double[] Low
        {
            get { return low; }
        }

        public double[] High
        {
            get { return high; }
        }

        public int LeafCount
        {
            get { return leafCount; }
        }

        public static BlobbyField Parse(int nleaf, int[] code, float[] floats, double threshold, out string error)
        {
            try
            {
                error = null;
                return new BlobbyField(nleaf, code, floats, threshold);
            }
            catch (ArgumentException e)
            {
                error = e.Message;
                return null;
            }
        }

        public BlobbyField(int nleaf, int[] code, float[] floats, double threshold)
        {
            this.threshold = threshold;
            leafCount = nleaf;
            kind = new int[nleaf];
            constant = new double[nleaf];
            inverse = new double[nleaf][];
            segment = new double[nleaf][];
            List<Instruction> ops = new List<Instruction>();
            int leaf = 0, position = 0;
            double[] bmin = { double.MaxValue, double.MaxValue, double.MaxValue }, bmax = { -double.MaxValue, -double.MaxValue, -double.MaxValue };
            bool any = false;
            while (position < code.Length)
            {
                int op = code[position++];
                if (op >= 1000)
                {
                    if (!(ops.Count == 0))
                    {
                        throw new ArgumentException("blobby: the leaves must come before the operations.");
                    }
                    if (leaf >= nleaf || position >= code.Length)
                    {
                        throw new ArgumentException("blobby: more leaves in \"code\" than \"nleaf\", or a leaf without its index.");
                    }
                    int index = code[position++];
                    if (op == 1000)
                    {
                        if (index < 0 || index >= floats.Length)
                        {
                            throw new ArgumentException("blobby: the index of a constant is outside of the floats.");
                        }
                        kind[leaf] = Constant;
                        constant[leaf] = floats[index];
                    }
                    else if (op == 1001 || op == 1002)
                    {
                        int need = op == 1001 ? 16 : 23;
                        if (index < 0 || index + need > floats.Length)
                        {
                            throw new ArgumentException("blobby: a leaf needs " + need + " floats from its index.");
                        }
                        int m = op == 1001 ? index : index + 7;
                        double[] matrix = new double[16];
                        for (int i = 0; i < 16; ++i)
                        {
                            matrix[i] = floats[m + i];
                        }
                        double[] inv = Invert(matrix);
                        if (inv == null)
                        {
                            throw new ArgumentException("blobby: the matrix of a leaf is singular.");
                        }
                        inverse[leaf] = inv;
                        double[] lo = new double[3], hi = new double[3];
                        if (op == 1001)
                        {
                            kind[leaf] = Ellipsoid;
                            for (int axis = 0; axis < 3; ++axis)
                            {
                                double extent = Math.Sqrt(matrix[axis] * matrix[axis] + matrix[4 + axis] * matrix[4 + axis] + matrix[8 + axis] * matrix[8 + axis]);
                                lo[axis] = matrix[12 + axis] - extent;
                                hi[axis] = matrix[12 + axis] + extent;
                            }
                        }
                        else
                        {
                            kind[leaf] = Segment;
                            double[] s = new double[7];
                            for (int i = 0; i < 7; ++i)
                            {
                                s[i] = floats[index + i];
                            }
                            if (!(s[6] > 0))
                            {
                                throw new ArgumentException("blobby: the radius of a segment must be positive.");
                            }
                            segment[leaf] = s;
                            for (int axis = 0; axis < 3; ++axis)
                            {
                                lo[axis] = double.MaxValue;
                                hi[axis] = -double.MaxValue;
                            }
                            for (int corner = 0; corner < 8; ++corner)
                            {
                                double[] q = new double[3];
                                for (int axis = 0; axis < 3; ++axis)
                                {
                                    double min = Math.Min(s[axis], s[3 + axis]) - s[6], max = Math.Max(s[axis], s[3 + axis]) + s[6];
                                    q[axis] = ((corner >> axis) & 1) != 0 ? max : min;
                                }
                                for (int axis = 0; axis < 3; ++axis)
                                {
                                    double v = q[0] * matrix[axis] + q[1] * matrix[4 + axis] + q[2] * matrix[8 + axis] + matrix[12 + axis];
                                    lo[axis] = Math.Min(lo[axis], v);
                                    hi[axis] = Math.Max(hi[axis], v);
                                }
                            }
                        }
                        for (int axis = 0; axis < 3; ++axis)
                        {
                            bmin[axis] = Math.Min(bmin[axis], lo[axis]);
                            bmax[axis] = Math.Max(bmax[axis], hi[axis]);
                        }
                        any = true;
                    }
                    else
                    {
                        throw new ArgumentException("blobby: unknown leaf code " + op + ".");
                    }
                    ++leaf;
                    continue;
                }
                if (op < 0 || op > 7)
                {
                    throw new ArgumentException("blobby: unknown operation code " + op + ".");
                }
                if (position >= code.Length)
                {
                    throw new ArgumentException("blobby: an operation without its operands.");
                }
                int count = code[position++];
                int expected = op == 4 || op == 5 ? 2 : op == 6 || op == 7 ? 1 : -1;
                if (count < 1 || (expected > 0 && count != expected) || position + count > code.Length)
                {
                    throw new ArgumentException("blobby: wrong number of operands for operation " + op + ".");
                }
                Instruction ins = new Instruction();
                ins.code = op;
                ins.operands = new int[count];
                for (int i = 0; i < count; ++i)
                {
                    int operand = code[position++];
                    if (operand < 0 || operand >= nleaf + ops.Count)
                    {
                        throw new ArgumentException("blobby: an operand refers to a value that does not exist yet.");
                    }
                    ins.operands[i] = operand;
                }
                ops.Add(ins);
            }
            if (leaf != nleaf)
            {
                throw new ArgumentException("blobby: \"nleaf\" is " + nleaf + " but the code has " + leaf + " leaves.");
            }
            operations = ops.ToArray();
            hasSupport = any;
            for (int axis = 0; axis < 3; ++axis)
            {
                low[axis] = any ? bmin[axis] : 0;
                high[axis] = any ? bmax[axis] : 0;
            }
        }

        private static double[] Invert(double[] m)
        {
            double[,] a = new double[4, 8];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    a[i, j] = m[4 * i + j];
                    a[i, 4 + j] = i == j ? 1 : 0;
                }
            }
            for (int column = 0; column < 4; ++column)
            {
                int pivot = column;
                for (int i = column + 1; i < 4; ++i)
                {
                    if (Math.Abs(a[i, column]) > Math.Abs(a[pivot, column]))
                    {
                        pivot = i;
                    }
                }
                if (Math.Abs(a[pivot, column]) < 1e-300)
                {
                    return null;
                }
                if (pivot != column)
                {
                    for (int j = 0; j < 8; ++j)
                    {
                        double tmp = a[column, j];
                        a[column, j] = a[pivot, j];
                        a[pivot, j] = tmp;
                    }
                }
                double scale = 1 / a[column, column];
                for (int j = 0; j < 8; ++j)
                {
                    a[column, j] *= scale;
                }
                for (int i = 0; i < 4; ++i)
                {
                    if (i != column)
                    {
                        double factor = a[i, column];
                        for (int j = 0; j < 8; ++j)
                        {
                            a[i, j] -= factor * a[column, j];
                        }
                    }
                }
            }
            double[] result = new double[16];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    result[4 * i + j] = a[i, 4 + j];
                }
            }
            return result;
        }

        // The profile of the ellipsoid and the segment as a function of the squared distance s = r^2 in units of the radius
        public static double Profile(double s)
        {
            if (s >= 1)
            {
                return 0;
            }
            return 1 + s * (-22.0 / 9 + s * (17.0 / 9 - s * 4.0 / 9));
        }

        // The ray in the space of each leaf
        public sealed class RayData
        {
            public double[] o, d;           // 3 per leaf
            public double tMin, tMax;       // the cut ray
            public double[] ox = new double[3], dx = new double[3];
        }

        public RayData Prepare(double[] o, double[] d)
        {
            RayData rd = new RayData();
            rd.o = new double[3 * leafCount];
            rd.d = new double[3 * leafCount];
            for (int axis = 0; axis < 3; ++axis)
            {
                rd.ox[axis] = o[axis];
                rd.dx[axis] = d[axis];
            }
            for (int l = 0; l < leafCount; ++l)
            {
                if (kind[l] == Constant)
                {
                    continue;
                }
                double[] m = inverse[l];
                for (int axis = 0; axis < 3; ++axis)
                {
                    rd.o[3 * l + axis] = o[0] * m[axis] + o[1] * m[4 + axis] + o[2] * m[8 + axis] + m[12 + axis];
                    rd.d[3 * l + axis] = d[0] * m[axis] + d[1] * m[4 + axis] + d[2] * m[8 + axis];
                }
            }
            return rd;
        }

        // The interval of the ray in which the leaf can be nonzero (conservative for the segment), false if there is none
        private bool Support(RayData rd, int l, out double t0, out double t1)
        {
            t0 = 0;
            t1 = 0;
            double ox = rd.o[3 * l], oy = rd.o[3 * l + 1], oz = rd.o[3 * l + 2], dx = rd.d[3 * l], dy = rd.d[3 * l + 1], dz = rd.d[3 * l + 2];
            double radius = 1;
            if (kind[l] == Segment)
            {
                double[] s = segment[l];
                ox -= 0.5 * (s[0] + s[3]);
                oy -= 0.5 * (s[1] + s[4]);
                oz -= 0.5 * (s[2] + s[5]);
                radius = s[6] + 0.5 * Math.Sqrt((s[3] - s[0]) * (s[3] - s[0]) + (s[4] - s[1]) * (s[4] - s[1]) + (s[5] - s[2]) * (s[5] - s[2]));
            }
            double a = dx * dx + dy * dy + dz * dz, b = ox * dx + oy * dy + oz * dz, c = ox * ox + oy * oy + oz * oz - radius * radius;
            if (a == 0)
            {
                return false;
            }
            double disc = b * b - a * c;
            if (disc < 0)
            {
                return false;
            }
            double root = Math.Sqrt(disc);
            t0 = (-b - root) / a;
            t1 = (-b + root) / a;
            return true;
        }

        // The union of the supports along the ray, clipped to [0, tMax]; false if the ray misses all of them
        public bool Cut(RayData rd, double tMax)
        {
            double lo = double.MaxValue, hi = -double.MaxValue;
            for (int l = 0; l < leafCount; ++l)
            {
                if (kind[l] != Constant && Support(rd, l, out double t0, out double t1))
                {
                    lo = Math.Min(lo, t0);
                    hi = Math.Max(hi, t1);
                }
            }
            rd.tMin = Math.Max(0, lo);
            rd.tMax = Math.Min(tMax, hi);
            return rd.tMin < rd.tMax;
        }

        private double LeafValue(RayData rd, int l, double t)
        {
            double px = rd.o[3 * l] + t * rd.d[3 * l], py = rd.o[3 * l + 1] + t * rd.d[3 * l + 1], pz = rd.o[3 * l + 2] + t * rd.d[3 * l + 2];
            return LeafAt(l, px, py, pz);
        }

        private double LeafAt(int l, double px, double py, double pz)
        {
            if (kind[l] == Constant)
            {
                return constant[l];
            }
            if (kind[l] == Ellipsoid)
            {
                return Profile(px * px + py * py + pz * pz);
            }
            double[] s = segment[l];
            double distance2 = DistanceSquared(s, px, py, pz, out double nx, out double ny, out double nz);
            return Profile(distance2 / (s[6] * s[6]));
        }

        private static double DistanceSquared(double[] s, double px, double py, double pz, out double nx, out double ny, out double nz)
        {
            double ex = s[3] - s[0], ey = s[4] - s[1], ez = s[5] - s[2];
            double length2 = ex * ex + ey * ey + ez * ez;
            double u = length2 > 0 ? ((px - s[0]) * ex + (py - s[1]) * ey + (pz - s[2]) * ez) / length2 : 0;
            u = Math.Min(1, Math.Max(0, u));
            nx = px - (s[0] + u * ex);
            ny = py - (s[1] + u * ey);
            nz = pz - (s[2] + u * ez);
            return nx * nx + ny * ny + nz * nz;
        }

        private unsafe double Program(Span<double> values)
        {
            int total = leafCount;
            for (int k = 0; k < operations.Length; ++k)
            {
                Instruction ins = operations[k];
                int[] r = ins.operands;
                double v;
                switch (ins.code)
                {
                    case 0:
                        v = 0;
                        for (int i = 0; i < r.Length; ++i)
                        {
                            v += values[r[i]];
                        }
                        break;
                    case 1:
                        v = 1;
                        for (int i = 0; i < r.Length; ++i)
                        {
                            v *= values[r[i]];
                        }
                        break;
                    case 2:
                        v = values[r[0]];
                        for (int i = 1; i < r.Length; ++i)
                        {
                            v = Math.Max(v, values[r[i]]);
                        }
                        break;
                    case 3:
                        v = values[r[0]];
                        for (int i = 1; i < r.Length; ++i)
                        {
                            v = Math.Min(v, values[r[i]]);
                        }
                        break;
                    case 4:
                        v = values[r[0]] - values[r[1]];
                        break;
                    case 5:
                        v = values[r[1]] == 0 ? 0 : values[r[0]] / values[r[1]];
                        break;
                    case 6:
                        v = -values[r[0]];
                        break;
                    default:
                        v = values[r[0]];
                        break;
                }
                values[total++] = v;
            }
            return values[total - 1];
        }

        // The field at a point of the space of the object
        public unsafe double Value(double x, double y, double z)
        {
            Span<double> values = stackalloc double[leafCount + operations.Length + 1];
            for (int l = 0; l < leafCount; ++l)
            {
                if (kind[l] == Constant)
                {
                    values[l] = constant[l];
                }
                else
                {
                    double[] m = inverse[l];
                    values[l] = LeafAt(l, x * m[0] + y * m[4] + z * m[8] + m[12], x * m[1] + y * m[5] + z * m[9] + m[13], x * m[2] + y * m[6] + z * m[10] + m[14]);
                }
            }
            return Program(values);
        }

        // The gradient of the field (central differences with the step h)
        public void Gradient(double x, double y, double z, double h, out double gx, out double gy, out double gz)
        {
            gx = (Value(x + h, y, z) - Value(x - h, y, z)) / (2 * h);
            gy = (Value(x, y + h, z) - Value(x, y - h, z)) / (2 * h);
            gz = (Value(x, y, z + h) - Value(x, y, z - h)) / (2 * h);
        }

        private unsafe double AlongRay(RayData rd, double t)
        {
            Span<double> values = stackalloc double[leafCount + operations.Length + 1];
            for (int l = 0; l < leafCount; ++l)
            {
                values[l] = kind[l] == Constant ? constant[l] : LeafValue(rd, l, t);
            }
            return Program(values) - threshold;
        }

        // The interval of a leaf over [ta, tb]
        private void LeafInterval(RayData rd, int l, double ta, double tb, out double lo, out double hi)
        {
            if (kind[l] == Constant)
            {
                lo = hi = constant[l];
                return;
            }
            double ox = rd.o[3 * l], oy = rd.o[3 * l + 1], oz = rd.o[3 * l + 2], dx = rd.d[3 * l], dy = rd.d[3 * l + 1], dz = rd.d[3 * l + 2];
            double smin, smax;
            if (kind[l] == Ellipsoid)
            {
                double a = dx * dx + dy * dy + dz * dz, b = ox * dx + oy * dy + oz * dz, c = ox * ox + oy * oy + oz * oz;
                double sa = a * ta * ta + 2 * b * ta + c, sb = a * tb * tb + 2 * b * tb + c;
                smax = Math.Max(sa, sb);
                smin = Math.Min(sa, sb);
                if (a > 0)
                {
                    double tm = -b / a;
                    if (tm > ta && tm < tb)
                    {
                        smin = Math.Min(smin, c - b * b / a);
                    }
                }
                smin = Math.Max(0, smin);
            }
            else
            {
                double[] s = segment[l];
                double r2 = s[6] * s[6];
                double pax = ox + ta * dx, pay = oy + ta * dy, paz = oz + ta * dz;
                double pbx = ox + tb * dx, pby = oy + tb * dy, pbz = oz + tb * dz;
                double ha2 = DistanceSquared(s, pax, pay, paz, out double ax, out double ay, out double az);
                double hb2 = DistanceSquared(s, pbx, pby, pbz, out double bx, out double by, out double bz);
                double ha = Math.Sqrt(ha2), hb = Math.Sqrt(hb2);
                smax = Math.Max(ha2, hb2);
                double dl = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                double sla = ha > 0 ? (ax * dx + ay * dy + az * dz) / ha : -dl, slb = hb > 0 ? (bx * dx + by * dy + bz * dz) / hb : dl;
                double minimum;
                if (ha == 0 || hb == 0)
                {
                    minimum = 0;
                }
                else if (sla >= 0)
                {
                    minimum = ha;
                }
                else if (slb <= 0)
                {
                    minimum = hb;
                }
                else
                {
                    // the tangents at both ends of a convex function meet below it
                    double te = ((hb - slb * tb) - (ha - sla * ta)) / (sla - slb);
                    minimum = Math.Max(0, ha + sla * (te - ta));
                }
                smin = minimum * minimum;
                smax = smax / r2;
                smin = smin / r2;
                lo = Profile(smax);
                hi = Profile(smin);
                return;
            }
            lo = Profile(smax);
            hi = Profile(smin);
        }

        // The interval of the program over [ta, tb], minus the threshold
        private unsafe void ProgramInterval(RayData rd, double ta, double tb, out double lo, out double hi)
        {
            int n = leafCount + operations.Length + 1;
            Span<double> vl = stackalloc double[n];
            Span<double> vh = stackalloc double[n];
            for (int l = 0; l < leafCount; ++l)
            {
                LeafInterval(rd, l, ta, tb, out vl[l], out vh[l]);
            }
            int total = leafCount;
            for (int k = 0; k < operations.Length; ++k)
            {
                Instruction ins = operations[k];
                int[] r = ins.operands;
                double l0, h0;
                switch (ins.code)
                {
                    case 0:
                        l0 = 0;
                        h0 = 0;
                        for (int i = 0; i < r.Length; ++i)
                        {
                            l0 += vl[r[i]];
                            h0 += vh[r[i]];
                        }
                        break;
                    case 1:
                        l0 = vl[r[0]];
                        h0 = vh[r[0]];
                        for (int i = 1; i < r.Length; ++i)
                        {
                            Multiply(l0, h0, vl[r[i]], vh[r[i]], out l0, out h0);
                        }
                        break;
                    case 2:
                        l0 = vl[r[0]];
                        h0 = vh[r[0]];
                        for (int i = 1; i < r.Length; ++i)
                        {
                            l0 = Math.Max(l0, vl[r[i]]);
                            h0 = Math.Max(h0, vh[r[i]]);
                        }
                        break;
                    case 3:
                        l0 = vl[r[0]];
                        h0 = vh[r[0]];
                        for (int i = 1; i < r.Length; ++i)
                        {
                            l0 = Math.Min(l0, vl[r[i]]);
                            h0 = Math.Min(h0, vh[r[i]]);
                        }
                        break;
                    case 4:
                        l0 = vl[r[0]] - vh[r[1]];
                        h0 = vh[r[0]] - vl[r[1]];
                        break;
                    case 5:
                        if (vl[r[1]] <= 0 && vh[r[1]] >= 0)
                        {
                            l0 = double.NegativeInfinity;
                            h0 = double.PositiveInfinity;
                        }
                        else
                        {
                            Multiply(vl[r[0]], vh[r[0]], 1 / vh[r[1]], 1 / vl[r[1]], out l0, out h0);
                        }
                        break;
                    case 6:
                        l0 = -vh[r[0]];
                        h0 = -vl[r[0]];
                        break;
                    default:
                        l0 = vl[r[0]];
                        h0 = vh[r[0]];
                        break;
                }
                vl[total] = l0;
                vh[total] = h0;
                ++total;
            }
            lo = vl[total - 1] - threshold;
            hi = vh[total - 1] - threshold;
        }

        private static void Multiply(double al, double ah, double bl, double bh, out double lo, out double hi)
        {
            double p0 = al * bl, p1 = al * bh, p2 = ah * bl, p3 = ah * bh;
            if (double.IsNaN(p0)) { p0 = 0; }
            if (double.IsNaN(p1)) { p1 = 0; }
            if (double.IsNaN(p2)) { p2 = 0; }
            if (double.IsNaN(p3)) { p3 = 0; }
            lo = Math.Min(Math.Min(p0, p1), Math.Min(p2, p3));
            hi = Math.Max(Math.Max(p0, p1), Math.Max(p2, p3));
        }

        // The roots of f - threshold along the ray in its cut interval, in order; entering[i] is true where the ray goes into the inside. At most max roots.
        public int Roots(RayData rd, double[] roots, bool[] entering, int max)
        {
            int count = 0;
            double range = rd.tMax - rd.tMin;
            if (!(range > 0) || max <= 0)
            {
                return 0;
            }
            double fa = AlongRay(rd, rd.tMin), fb = AlongRay(rd, rd.tMax);
            Search(rd, rd.tMin, rd.tMax, fa, fb, range / 4096, range / 67108864.0, roots, entering, max, ref count);
            return count;
        }

        private bool Search(RayData rd, double ta, double tb, double fa, double fb, double coarse, double fine, double[] roots, bool[] entering, int max,
                            ref int count)
        {
            ProgramInterval(rd, ta, tb, out double lo, out double hi);
            if (hi < 0 || lo > 0)
            {
                return false;
            }
            bool inA = fa > 0, inB = fb > 0;
            double width = tb - ta;
            if (width <= coarse && inA != inB)
            {
                roots[count] = Refine(rd, ta, tb, fa, fb);
                entering[count] = inB;
                ++count;
                return count >= max;
            }
            if (width <= fine)
            {
                return false;
            }
            double tm = 0.5 * (ta + tb);
            double fm = AlongRay(rd, tm);
            if (Search(rd, ta, tm, fa, fm, coarse, fine, roots, entering, max, ref count))
            {
                return true;
            }
            return Search(rd, tm, tb, fm, fb, coarse, fine, roots, entering, max, ref count);
        }

        // The Illinois method on a bracket whose ends are on different sides of the threshold
        private double Refine(RayData rd, double a, double b, double fa, double fb)
        {
            int side = 0;
            double c = a;
            for (int iteration = 0; iteration < 100; ++iteration)
            {
                double denominator = fb - fa;
                c = denominator != 0 ? (fb * a - fa * b) / denominator : 0.5 * (a + b);
                if (!(c > a && c < b))
                {
                    c = 0.5 * (a + b);
                }
                double fc = AlongRay(rd, c);
                if (fc == 0 || b - a <= 1e-15 * Math.Max(1, Math.Abs(c)))
                {
                    return c;
                }
                if ((fc > 0) == (fb > 0))
                {
                    b = c;
                    fb = fc;
                    if (side == -1)
                    {
                        fa *= 0.5;
                    }
                    side = -1;
                }
                else
                {
                    a = c;
                    fa = fc;
                    if (side == 1)
                    {
                        fb *= 0.5;
                    }
                    side = 1;
                }
                if (Math.Abs(fc) < 1e-14)
                {
                    return c;
                }
            }
            return c;
        }
    }

    public sealed unsafe class BlobbyShape : Shape
    {
        private readonly BlobbyField field;
        private readonly Transform renderFromObject, objectFromRender;
        private readonly bool reverseOrientation, transformSwapsHandedness;
        private readonly Bounds3f bounds;
        private readonly double[] center = new double[3];
        private readonly double radius, size;
        private readonly int slots;
        private readonly object sync = new object();
        private double area = -1;
        private readonly double givenArea;
        public static int AreaLines = 65536;

        public BlobbyShape(BlobbyField field, Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, double givenArea)
        {
            this.field = field;
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.givenArea = givenArea;
            transformSwapsHandedness = renderFromObject.SwapsHandedness();
            Bounds3f result = new Bounds3f();
            for (int corner = 0; corner < 8; ++corner)
            {
                Point3f point = renderFromObject.Apply(new Point3f((Float)((corner & 1) != 0 ? field.High[0] : field.Low[0]),
                                                                   (Float)((corner & 2) != 0 ? field.High[1] : field.Low[1]),
                                                                   (Float)((corner & 4) != 0 ? field.High[2] : field.Low[2])));
                result = corner == 0 ? new Bounds3f(point) : pbrt.Union(result, point);
            }
            Vector3f pad = new Vector3f(1e-4f, 1e-4f, 1e-4f) * pbrt.Length(result.pMax - result.pMin);
            bounds = new Bounds3f(result.pMin - pad, result.pMax + pad);
            center[0] = 0.5 * ((double)bounds.pMin.x + bounds.pMax.x);
            center[1] = 0.5 * ((double)bounds.pMin.y + bounds.pMax.y);
            center[2] = 0.5 * ((double)bounds.pMin.z + bounds.pMax.z);
            radius = 0.5 * pbrt.Length(bounds.pMax - bounds.pMin);
            size = Math.Sqrt(Sq(field.High[0] - field.Low[0]) + Sq(field.High[1] - field.Low[1]) + Sq(field.High[2] - field.Low[2]));
            slots = Math.Max(4, Math.Min(64, 2 * field.LeafCount));
        }

        private static double Sq(double x)
        {
            return x * x;
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        // The roots of the ray (render space) in its cut interval
        private int Trace(Ray ray, double tMax, double[] roots, bool[] entering, int max)
        {
            if (!field.HasSupport)
            {
                return 0;
            }
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            if (!bounds.IntersectP(ray.o, ray.d, (Float)Math.Min(tMax, float.MaxValue)))
            {
                return 0;
            }
            BlobbyField.RayData rd = field.Prepare(new double[] { o.x, o.y, o.z }, new double[] { d.x, d.y, d.z });
            if (!field.Cut(rd, tMax))
            {
                return 0;
            }
            return field.Roots(rd, roots, entering, max);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            double[] roots = new double[1];
            bool[] entering = new bool[1];
            // the first root with t > 0 (the cut starts at 0, a root at 0 exactly is on the origin and is skipped by the offset of the ray)
            if (Trace(ray, tMax, roots, entering, 1) == 0)
            {
                return default;
            }
            double t = roots[0];
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            double[] p = { o.x + t * d.x, o.y + t * d.y, o.z + t * d.z };
            SurfaceInteraction intr = InteractionFromPoint(p, -ray.d, ray.time);
            return new ShapeIntersection(intr, (Float)t);
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            double[] roots = new double[1];
            bool[] entering = new bool[1];
            return Trace(ray, tMax, roots, entering, 1) > 0;
        }

        // The outward unit normal of the object space and its (u, v)
        private void Local(double[] p, out double[] n, out Point2f uv)
        {
            field.Gradient(p[0], p[1], p[2], 1e-5 * size, out double gx, out double gy, out double gz);
            double l = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            n = l > 0 ? new double[] { -gx / l, -gy / l, -gz / l } : new double[] { 0, 0, 1 };
            double cx = 0.5 * (field.Low[0] + field.High[0]), cy = 0.5 * (field.Low[1] + field.High[1]), cz = 0.5 * (field.Low[2] + field.High[2]);
            double rx = p[0] - cx, ry = p[1] - cy, rz = p[2] - cz;
            double phi = Math.Atan2(ry, rx);
            if (phi < 0)
            {
                phi += 2 * Math.PI;
            }
            double rr = Math.Sqrt(rx * rx + ry * ry + rz * rz);
            double theta = rr > 0 ? Math.Acos(Math.Min(1, Math.Max(-1, rz / rr))) : 0;
            uv = new Point2f((Float)(phi / (2 * Math.PI)), (Float)(theta / Math.PI));
        }

        private SurfaceInteraction InteractionFromPoint(double[] p, Vector3f wo, Float time)
        {
            Local(p, out double[] n, out Point2f uv);
            // tangents with dpdu x dpdv along the outward normal
            double ax = Math.Abs(n[2]) < 0.9 ? 0 : 1, ay = 0, az = Math.Abs(n[2]) < 0.9 ? 1 : 0;
            double ux = ay * n[2] - az * n[1], uy = az * n[0] - ax * n[2], uz = ax * n[1] - ay * n[0];
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            ux *= size / ul;
            uy *= size / ul;
            uz *= size / ul;
            double vx = n[1] * uz - n[2] * uy, vy = n[2] * ux - n[0] * uz, vz = n[0] * uy - n[1] * ux;
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            Vector3f pu = renderFromObject.Apply(new Vector3f((Float)ux, (Float)uy, (Float)uz));
            Vector3f pv = renderFromObject.Apply(new Vector3f((Float)vx, (Float)vy, (Float)vz));
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new SurfaceInteraction(new Point3fi(pr, pError), uv, wo, pu, pv, new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), time, flipNormal, 0);
        }

        private Interaction InteractionAt(double[] p)
        {
            Local(p, out double[] n, out Point2f uv);
            Point3f pr = renderFromObject.Apply(new Point3f((Float)p[0], (Float)p[1], (Float)p[2]));
            Normal3f nr = pbrt.Normalize(renderFromObject.Apply(new Normal3f((Float)n[0], (Float)n[1], (Float)n[2])));
            if (reverseOrientation)
            {
                nr = -nr;
            }
            Vector3f pError = pbrt.gamma(8) * pbrt.Abs(new Vector3f(pr.x, pr.y, pr.z));
            return new Interaction(new Point3fi(pr, pError), nr, uv);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Lines through the bounding sphere

        private static double RadicalInverse(int b, ulong i)
        {
            double inverse = 1.0 / b, f = inverse, result = 0;
            while (i > 0)
            {
                result += f * (i % (ulong)b);
                i /= (ulong)b;
                f *= inverse;
            }
            return result;
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

        // The line of the numbers (a direction from the first two, a point of the disk from the other two); the origin is before the bounding sphere
        private Ray LineFrom(double a, double b, double c, double e)
        {
            double z = 1 - 2 * a, r = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = 2 * Math.PI * b;
            double wx = r * Math.Cos(phi), wy = r * Math.Sin(phi), wz = z;
            double hx = Math.Abs(wx) < 0.9 ? 1 : 0, hy = Math.Abs(wx) < 0.9 ? 0 : 1;
            double ux = wy * 0 - wz * hy, uy = wz * hx - wx * 0, uz = wx * hy - wy * hx;
            double ul = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            ux /= ul;
            uy /= ul;
            uz /= ul;
            double vx = wy * uz - wz * uy, vy = wz * ux - wx * uz, vz = wx * uy - wy * ux;
            double rho = radius * Math.Sqrt(c), psi = 2 * Math.PI * e;
            double s = rho * Math.Cos(psi), q = rho * Math.Sin(psi);
            double ox = center[0] + s * ux + q * vx - radius * wx, oy = center[1] + s * uy + q * vy - radius * wy, oz = center[2] + s * uz + q * vz - radius * wz;
            return new Ray(new Point3f((Float)ox, (Float)oy, (Float)oz), new Vector3f((Float)wx, (Float)wy, (Float)wz));
        }

        // The entering crossings of the line, at most the slots
        private int Entering(Ray line, double[] roots, bool[] entering, double[] hits)
        {
            int n = Trace(line, 2 * radius, roots, entering, roots.Length);
            int count = 0;
            for (int i = 0; i < n; ++i)
            {
                if (entering[i])
                {
                    hits[count++] = roots[i];
                }
            }
            return count;
        }

        public override Float Area()
        {
            if (givenArea > 0)
            {
                return (Float)givenArea;
            }
            lock (sync)
            {
                if (area < 0)
                {
                    double[] roots = new double[256];
                    bool[] entering = new bool[256];
                    double[] hits = new double[256];
                    double sum = 0;
                    for (int i = 0; i < AreaLines; ++i)
                    {
                        Ray line = LineFrom(RadicalInverse(2, (ulong)i + 1), RadicalInverse(3, (ulong)i + 1), RadicalInverse(5, (ulong)i + 1), RadicalInverse(7, (ulong)i + 1));
                        sum += Entering(line, roots, entering, hits);
                    }
                    area = 4 * Math.PI * radius * radius * sum / AreaLines;
                }
                return (Float)area;
            }
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            double a = Area();
            if (!(a > 0) || !field.HasSupport)
            {
                return default;
            }
            ulong state = ((ulong)BitConverter.SingleToInt32Bits(u.x) << 32 | (uint)BitConverter.SingleToInt32Bits(u.y)) * 0x9E3779B97F4A7C15UL + 0x51ED27UL;
            double[] roots = new double[256];
            bool[] entering = new bool[256];
            double[] hits = new double[256];
            for (int attempt = 0; attempt < 200000; ++attempt)
            {
                Ray line = LineFrom(NextUniform(ref state), NextUniform(ref state), NextUniform(ref state), NextUniform(ref state));
                int slot = (int)(NextUniform(ref state) * slots);
                int count = Entering(line, roots, entering, hits);
                if (slot < count)
                {
                    Point3f o = objectFromRender.Apply(line.o);
                    Vector3f d = objectFromRender.Apply(line.d);
                    double t = hits[slot];
                    double[] p = { o.x + t * d.x, o.y + t * d.y, o.z + t * d.z };
                    return new ShapeSample(InteractionAt(p), (Float)(1 / a));
                }
            }
            return default;
        }

        public override Float PDF(Interaction intr)
        {
            double a = Area();
            return a > 0 ? (Float)(1 / a) : 0;
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return PatchSampling.ToSolidAngle(Sample(u), ctx);
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            double a = Area();
            if (!(a > 0))
            {
                return 0;
            }
            Ray ray = ctx.SpawnRay(wi);
            double[] roots = new double[1];
            bool[] entering = new bool[1];
            if (Trace(ray, Infinity, roots, entering, 1) == 0)
            {
                return 0;
            }
            Point3f o = objectFromRender.Apply(ray.o);
            Vector3f d = objectFromRender.Apply(ray.d);
            double[] p = { o.x + roots[0] * d.x, o.y + roots[0] * d.y, o.z + roots[0] * d.z };
            Interaction at = InteractionAt(p);
            return PatchSampling.AreaToSolidAngle(1 / a, at.n, at.p(), ctx, wi);
        }

        public override string ToString()
        {
            return "[ BlobbyShape ]";
        }
    }

    public static class Blobbies
    {
        public static pstd.vector<Shape> Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, ParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            pstd.vector<Shape> shapes = new pstd.vector<Shape>();
            List<int> code = parameters.GetIntArray("code");
            List<Float> floats = parameters.GetFloatArray("floats");
            if (floats.Count == 0)
            {
                floats = parameters.GetFloatArray("flt");
            }
            int nleaf = parameters.GetOneInt("nleaf", -1);
            if (nleaf < 0 || code.Count == 0)
            {
                pbrt.Error(loc, "blobby: \"nleaf\" and \"code\" are required.");
                return shapes;
            }
            string error;
            BlobbyField field = BlobbyField.Parse(nleaf, code.ToArray(), floats.ToArray(), parameters.GetOneFloat("threshold", 0.421f), out error);
            if (field == null)
            {
                pbrt.Error(loc, error);
                return shapes;
            }
            if (!field.HasSupport)
            {
                pbrt.Error(loc, "blobby: no ellipsoid or segment, so there is nothing to bound.");
                return shapes;
            }
            shapes.push_back(new BlobbyShape(field, renderFromObject, objectFromRender, reverseOrientation, parameters.GetOneFloat("area", 0)));
            return shapes;
        }
    }
}  // namespace pbrt
