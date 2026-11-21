// (not in pbrt-v4) cpu/causticbounds.cs
//
// Bounds of the caustics of specular triangles (Fan, Wang, Wang, Li, Guo, Yan, Guo and Guo, "Bernstein Bounds for Caustics",
// ACM Transactions on Graphics 44(4), 2025; from the paper, no code of its authors), for one specular vertex, a point light
// and triangles that receive.
//
// Before the render, for every point light, every specular triangle and every receiving triangle: the light that the specular
// triangle reflects or refracts lands on the plane of the receiver at barycentric coordinates that are rational functions of
// the coordinates (u, v) on the specular triangle (the paper's equation 9): the direction that leaves the specular vertex,
// without its length, is a polynomial for a reflection, and for a refraction a polynomial but for one square root, which is
// put between a line and that line plus a remainder (the remainder variable of the paper, equations 12 and 13). A rational
// function lies between the smallest and the largest ratio of the Bernstein coefficients of its numerator and denominator
// when those of the denominator have one sign (equation 3): this gives a box on the receiver that holds the caustic of a
// piece of the specular triangle (the position bound). Pieces whose box is wide, or whose denominator changes sign, are cut
// in four, up to a depth. The boxes are kept by receiving triangle, in a grid over its coordinates.
//
// During the render a point of a receiving triangle asks for the specular triangles whose boxes hold it, each with a weight;
// the caller takes each with a probability that follows its weight and solves for the chains in it (SpecularPolynomial).
//
// Not as in the paper: the weight is the solid angle that the piece subtends at the light over the area of its box (an
// estimate of the irradiance), not the paper's bound of the irradiance by implicit differentiation (its section 4.4); a
// probability that is not zero where the box holds the point keeps the estimate without bias whatever the weight. Two
// specular vertices, and the hierarchy over the triangles, are not here.

using System;
using System.Collections.Generic;

namespace pbrt
{
    using static pbrt;

    // BiPolynomial Definition: a polynomial of (u, v) of degree at most 6 in each, c[i * 7 + j] the coefficient of u^i v^j
    public sealed class BiPolynomial
    {
        public const int Size = 7;
        public double[] c = new double[Size * Size];

        public static BiPolynomial Linear(double constant, double u, double v)
        {
            BiPolynomial p = new BiPolynomial();
            p.c[0] = constant;
            p.c[Size] = u;
            p.c[1] = v;
            return p;
        }

        public static BiPolynomial operator +(BiPolynomial a, BiPolynomial b)
        {
            BiPolynomial r = new BiPolynomial();
            for (int i = 0; i < Size * Size; ++i)
            {
                r.c[i] = a.c[i] + b.c[i];
            }
            return r;
        }

        public static BiPolynomial operator -(BiPolynomial a, BiPolynomial b)
        {
            BiPolynomial r = new BiPolynomial();
            for (int i = 0; i < Size * Size; ++i)
            {
                r.c[i] = a.c[i] - b.c[i];
            }
            return r;
        }

        public static BiPolynomial operator *(BiPolynomial a, double s)
        {
            BiPolynomial r = new BiPolynomial();
            for (int i = 0; i < Size * Size; ++i)
            {
                r.c[i] = a.c[i] * s;
            }
            return r;
        }

        public static BiPolynomial operator *(BiPolynomial a, BiPolynomial b)
        {
            BiPolynomial r = new BiPolynomial();
            for (int i = 0; i < Size; ++i)
            {
                for (int j = 0; j < Size; ++j)
                {
                    double x = a.c[i * Size + j];
                    if (x == 0)
                    {
                        continue;
                    }
                    for (int k = 0; i + k < Size; ++k)
                    {
                        for (int l = 0; j + l < Size; ++l)
                        {
                            r.c[(i + k) * Size + j + l] += x * b.c[k * Size + l];
                        }
                    }
                }
            }
            return r;
        }

        // The Bernstein coefficients (degree 6 in each variable) of the polynomial on the box [u0, u1] x [v0, v1]
        public void Bernstein(double u0, double u1, double v0, double v1, double[] b)
        {
            Array.Copy(c, b, Size * Size);
            // u = u0 + (u1 - u0) s, then v likewise: a shift and a scale of the powers
            for (int j = 0; j < Size; ++j)
            {
                for (int k = 0; k < Size - 1; ++k)
                {
                    for (int i = Size - 2; i >= k; --i)
                    {
                        b[i * Size + j] += u0 * b[(i + 1) * Size + j];
                    }
                }
                double scale = 1;
                for (int i = 0; i < Size; ++i)
                {
                    b[i * Size + j] *= scale;
                    scale *= u1 - u0;
                }
            }
            for (int i = 0; i < Size; ++i)
            {
                for (int k = 0; k < Size - 1; ++k)
                {
                    for (int j = Size - 2; j >= k; --j)
                    {
                        b[i * Size + j] += v0 * b[i * Size + j + 1];
                    }
                }
                double scale = 1;
                for (int j = 0; j < Size; ++j)
                {
                    b[i * Size + j] *= scale;
                    scale *= v1 - v0;
                }
            }
            // From the powers to the Bernstein basis: b_i = sum over k <= i of C(i, k) / C(n, k) a_k, in each variable
            double[] line = new double[Size];
            for (int j = 0; j < Size; ++j)
            {
                for (int i = 0; i < Size; ++i)
                {
                    line[i] = b[i * Size + j];
                }
                for (int i = 0; i < Size; ++i)
                {
                    double sum = 0;
                    for (int k = 0; k <= i; ++k)
                    {
                        sum += Ratio[i * Size + k] * line[k];
                    }
                    b[i * Size + j] = sum;
                }
            }
            for (int i = 0; i < Size; ++i)
            {
                for (int j = 0; j < Size; ++j)
                {
                    line[j] = b[i * Size + j];
                }
                for (int j = 0; j < Size; ++j)
                {
                    double sum = 0;
                    for (int k = 0; k <= j; ++k)
                    {
                        sum += Ratio[j * Size + k] * line[k];
                    }
                    b[i * Size + j] = sum;
                }
            }
        }

        static double Binomial(int n, int k)
        {
            double r = 1;
            for (int i = 1; i <= k; ++i)
            {
                r = r * (n - k + i) / i;
            }
            return r;
        }

        static double[] MakeRatio()
        {
            double[] ratio = new double[Size * Size];
            for (int i = 0; i < Size; ++i)
            {
                for (int k = 0; k <= i; ++k)
                {
                    ratio[i * Size + k] = Binomial(i, k) / Binomial(Size - 1, k);
                }
            }
            return ratio;
        }

        static readonly double[] Ratio = MakeRatio();
    }

    // CausticBounds Definition
    public sealed unsafe class CausticBounds
    {
        // What is known of a mesh: whether it has a specular reflection, the index under its normal over the index above it
        // if it refracts (0 if not), and whether it receives (a BSDF with no specular lobe)
        public sealed class MeshKind
        {
            public bool reflects, receives;
            public Float eta;
        }

        // A specular triangle whose caustic can reach a point of a receiver
        public struct Caster
        {
            public TriangleMesh mesh;
            public int triangle;
            public bool reflection;
            public Float weight;
        }

        struct Entry
        {
            public float u0, u1, v0, v1, weight;
            public TriangleMesh mesh;
            public int triangle;
            public bool reflection;
        }

        sealed class Receiver
        {
            public List<Entry> entries = new List<Entry>();
            public List<int>[] cells = new List<int>[Resolution * Resolution];
        }

        const int Resolution = 32;
        const int DeepestCut = 3;

        // CausticBounds Public Methods
        public CausticBounds(Point3f light, List<TriangleMesh> meshes, Dictionary<TriangleMesh, MeshKind> kinds)
        {
            this.light = light;
            this.kinds = kinds;
            foreach (TriangleMesh casterMesh in meshes)
            {
                MeshKind kind = kinds[casterMesh];
                if (!kind.reflects && kind.eta == 0)
                {
                    continue;
                }
                for (int triangle = 0; triangle < casterMesh.nTriangles; ++triangle)
                {
                    foreach (bool reflection in new bool[] { true, false })
                    {
                        if (reflection ? !kind.reflects : kind.eta == 0)
                        {
                            continue;
                        }
                        Beam beam = new Beam(light, casterMesh, triangle, reflection, kind.eta);
                        if (!beam.valid)
                        {
                            continue;
                        }
                        foreach (TriangleMesh receiverMesh in meshes)
                        {
                            if (!kinds[receiverMesh].receives)
                            {
                                continue;
                            }
                            for (int r = 0; r < receiverMesh.nTriangles; ++r)
                            {
                                Cut(beam, receiverMesh, r, 0, 1, 0, 1, 0);
                            }
                        }
                    }
                }
            }
        }

        public long Count()
        {
            long count = 0;
            foreach (Receiver receiver in receivers.Values)
            {
                count += receiver.entries.Count;
            }
            return count;
        }

        // The specular triangles whose boxes hold the point (u, v) of a receiving triangle, each once for a kind, with the
        // sum of the weights of its pieces
        public void Query(TriangleMesh mesh, int triangle, Point2f uv, List<Caster> casters)
        {
            casters.Clear();
            if (!receivers.TryGetValue((mesh, triangle), out Receiver receiver))
            {
                return;
            }
            int cu = Math.Clamp((int)(uv.x * Resolution), 0, Resolution - 1), cv = Math.Clamp((int)(uv.y * Resolution), 0, Resolution - 1);
            List<int> cell = receiver.cells[cu * Resolution + cv];
            if (cell == null)
            {
                return;
            }
            foreach (int index in cell)
            {
                Entry entry = receiver.entries[index];
                if (uv.x < entry.u0 || uv.x > entry.u1 || uv.y < entry.v0 || uv.y > entry.v1)
                {
                    continue;
                }
                bool merged = false;
                for (int i = 0; i < casters.Count; ++i)
                {
                    if (ReferenceEquals(casters[i].mesh, entry.mesh) && casters[i].triangle == entry.triangle && casters[i].reflection == entry.reflection)
                    {
                        Caster caster = casters[i];
                        caster.weight += entry.weight;
                        casters[i] = caster;
                        merged = true;
                        break;
                    }
                }
                if (!merged)
                {
                    Caster caster = new Caster();
                    caster.mesh = entry.mesh;
                    caster.triangle = entry.triangle;
                    caster.reflection = entry.reflection;
                    caster.weight = entry.weight;
                    casters.Add(caster);
                }
            }
        }

        public MeshKind KindOf(TriangleMesh mesh)
        {
            return kinds[mesh];
        }

        // The light that a specular triangle sends on: the point of the triangle and, for a reflection, the direction that
        // leaves it, as polynomials of (u, v); for a refraction the direction is P - sqrt(beta) n, with P, beta and n polynomials
        sealed class Beam
        {
            public bool valid, reflection;
            public TriangleMesh mesh;
            public int triangle;
            public BiPolynomial xx, xy, xz, nx, ny, nz, px, py, pz, beta;
            public double area, cosineOverDistanceSquared;

            public Beam(Point3f light, TriangleMesh mesh, int triangle, bool reflection, Float meshEta)
            {
                this.mesh = mesh;
                this.triangle = triangle;
                this.reflection = reflection;
                int i0 = mesh.vertexIndices[3 * triangle], i1 = mesh.vertexIndices[3 * triangle + 1], i2 = mesh.vertexIndices[3 * triangle + 2];
                Point3f p0 = mesh.p[i0], p1 = mesh.p[i1], p2 = mesh.p[i2];
                Vector3f ng = Cross(p1 - p0, p2 - p0);
                area = 0.5 * Length(ng);
                if (area == 0)
                {
                    return;
                }
                ng = Normalize(ng);
                Vector3f n0 = mesh.n != null ? (Vector3f)mesh.n[i0] : ng, n1 = mesh.n != null ? (Vector3f)mesh.n[i1] : ng,
                         n2 = mesh.n != null ? (Vector3f)mesh.n[i2] : ng;
                if (Dot(ng, n0) < 0)
                {
                    ng = -ng;
                }
                Point3f centre = p0 + ((p1 - p0) + (p2 - p0)) / 3;
                Vector3f toLight = light - centre;
                double side = Dot(toLight, ng);
                cosineOverDistanceSquared = Math.Abs(side) / Math.Pow(LengthSquared(toLight), 1.5);
                // (the normal against the light that arrives, as the formula of the refraction takes it)
                double flip = side > 0 ? 1 : -1;
                xx = BiPolynomial.Linear(p0.x, p1.x - p0.x, p2.x - p0.x);
                xy = BiPolynomial.Linear(p0.y, p1.y - p0.y, p2.y - p0.y);
                xz = BiPolynomial.Linear(p0.z, p1.z - p0.z, p2.z - p0.z);
                nx = BiPolynomial.Linear(flip * n0.x, flip * (n1.x - n0.x), flip * (n2.x - n0.x));
                ny = BiPolynomial.Linear(flip * n0.y, flip * (n1.y - n0.y), flip * (n2.y - n0.y));
                nz = BiPolynomial.Linear(flip * n0.z, flip * (n1.z - n0.z), flip * (n2.z - n0.z));
                BiPolynomial dx = BiPolynomial.Linear(p0.x - light.x, p1.x - p0.x, p2.x - p0.x);
                BiPolynomial dy = BiPolynomial.Linear(p0.y - light.y, p1.y - p0.y, p2.y - p0.y);
                BiPolynomial dz = BiPolynomial.Linear(p0.z - light.z, p1.z - p0.z, p2.z - p0.z);
                BiPolynomial nn = nx * nx + ny * ny + nz * nz, dn = dx * nx + dy * ny + dz * nz;
                if (reflection)
                {
                    // (n . n) d - 2 (d . n) n
                    px = nn * dx - (dn * nx) * 2;
                    py = nn * dy - (dn * ny) * 2;
                    pz = nn * dz - (dn * nz) * 2;
                }
                else
                {
                    // eta' ((n . n) d - (d . n) n) - sqrt(beta) n, eta' the index of the light's side over the other side
                    double etaPrime = side > 0 ? 1 / meshEta : meshEta;
                    px = (nn * dx - dn * nx) * etaPrime;
                    py = (nn * dy - dn * ny) * etaPrime;
                    pz = (nn * dz - dn * nz) * etaPrime;
                    BiPolynomial dd = dx * dx + dy * dy + dz * dz;
                    beta = (nn * dd) * (1 - etaPrime * etaPrime) + (dn * dn) * (etaPrime * etaPrime);
                }
                valid = true;
            }
        }

        // CausticBounds Private Methods
        // The piece [u0, u1] x [v0, v1] of a specular triangle against a receiving triangle: its box, kept, or cut in four
        void Cut(Beam beam, TriangleMesh receiverMesh, int r, double u0, double u1, double v0, double v1, int depth)
        {
            if (u0 + v0 >= 1)
            {
                return;
            }
            int i0 = receiverMesh.vertexIndices[3 * r], i1 = receiverMesh.vertexIndices[3 * r + 1], i2 = receiverMesh.vertexIndices[3 * r + 2];
            Point3f q0 = receiverMesh.p[i0];
            Vector3f f1 = receiverMesh.p[i1] - q0, f2 = receiverMesh.p[i2] - q0;
            double receiverArea = 0.5 * Length(Cross(f1, f2));
            if (receiverArea == 0)
            {
                return;
            }
            const int N = BiPolynomial.Size * BiPolynomial.Size;
            double[] b = new double[N];
            double slope = 0, offset = 0, remainder = 0;
            if (!beam.reflection)
            {
                // The square root between a line and the line plus a remainder, on the range of beta of the piece
                beam.beta.Bernstein(u0, u1, v0, v1, b);
                double lowest = double.PositiveInfinity, highest = double.NegativeInfinity;
                for (int i = 0; i < N; ++i)
                {
                    lowest = Math.Min(lowest, b[i]);
                    highest = Math.Max(highest, b[i]);
                }
                if (highest <= 0)
                {
                    // (all of the piece is totally reflected)
                    return;
                }
                lowest = Math.Max(lowest, 0);
                double rootLow = Math.Sqrt(lowest), rootHigh = Math.Sqrt(highest);
                slope = 1 / (rootLow + rootHigh);
                offset = rootLow - slope * lowest;
                remainder = 1 / (4 * slope) - offset;
            }
            double uLow = double.PositiveInfinity, uHigh = double.NegativeInfinity, vLow = double.PositiveInfinity, vHigh = double.NegativeInfinity;
            bool bounded = true, ahead = false;
            double[] bd = new double[N], bu = new double[N], bv = new double[N], bt = new double[N];
            BiPolynomial ox = beam.xx - BiPolynomial.Linear(q0.x, 0, 0), oy = beam.xy - BiPolynomial.Linear(q0.y, 0, 0), oz = beam.xz - BiPolynomial.Linear(q0.z, 0, 0);
            // ((x - q0) x f1): for the second coordinate and for the distance along the ray
            BiPolynomial sx = oy * f1.z - oz * f1.y, sy = oz * f1.x - ox * f1.z, sz = ox * f1.y - oy * f1.x;
            BiPolynomial distance = sx * f2.x + sy * f2.y + sz * f2.z;
            for (int end = 0; end < (beam.reflection ? 1 : 2) && bounded; ++end)
            {
                BiPolynomial tx = beam.px, ty = beam.py, tz = beam.pz;
                if (!beam.reflection)
                {
                    BiPolynomial root = beam.beta * slope + BiPolynomial.Linear(offset + end * remainder, 0, 0);
                    tx = tx - root * beam.nx;
                    ty = ty - root * beam.ny;
                    tz = tz - root * beam.nz;
                }
                // The ray from the specular vertex against the receiver's plane (Moeller and Trumbore): d x f2
                BiPolynomial hx = ty * f2.z - tz * f2.y, hy = tz * f2.x - tx * f2.z, hz = tx * f2.y - ty * f2.x;
                BiPolynomial denominator = hx * f1.x + hy * f1.y + hz * f1.z;
                BiPolynomial numeratorU = hx * ox + hy * oy + hz * oz;
                BiPolynomial numeratorV = sx * tx + sy * ty + sz * tz;
                denominator.Bernstein(u0, u1, v0, v1, bd);
                numeratorU.Bernstein(u0, u1, v0, v1, bu);
                numeratorV.Bernstein(u0, u1, v0, v1, bv);
                distance.Bernstein(u0, u1, v0, v1, bt);
                bool positive = bd[0] > 0;
                for (int i = 0; i < N; ++i)
                {
                    if (bd[i] == 0 || (bd[i] > 0) != positive)
                    {
                        bounded = false;
                        break;
                    }
                }
                if (!bounded)
                {
                    break;
                }
                for (int i = 0; i < N; ++i)
                {
                    uLow = Math.Min(uLow, bu[i] / bd[i]);
                    uHigh = Math.Max(uHigh, bu[i] / bd[i]);
                    vLow = Math.Min(vLow, bv[i] / bd[i]);
                    vHigh = Math.Max(vHigh, bv[i] / bd[i]);
                    ahead |= bt[i] / bd[i] > 0;
                }
            }
            if (bounded)
            {
                if (!ahead || uHigh < 0 || vHigh < 0 || uLow + vLow > 1)
                {
                    // (the caustic of the piece is behind the ray, or misses the receiver)
                    return;
                }
                if (depth < DeepestCut && Math.Max(uHigh - uLow, vHigh - vLow) > 0.25)
                {
                    bounded = false;
                }
            }
            if (!bounded && depth < DeepestCut)
            {
                double um = 0.5 * (u0 + u1), vm = 0.5 * (v0 + v1);
                Cut(beam, receiverMesh, r, u0, um, v0, vm, depth + 1);
                Cut(beam, receiverMesh, r, um, u1, v0, vm, depth + 1);
                Cut(beam, receiverMesh, r, u0, um, vm, v1, depth + 1);
                Cut(beam, receiverMesh, r, um, u1, vm, v1, depth + 1);
                return;
            }
            if (!bounded)
            {
                // (no bound at the deepest cut: the piece is taken to reach all of the receiver)
                uLow = 0;
                vLow = 0;
                uHigh = 1;
                vHigh = 1;
            }
            Entry entry = new Entry();
            entry.u0 = (float)Math.Max(uLow - 1e-4, 0);
            entry.u1 = (float)Math.Min(uHigh + 1e-4, 1);
            entry.v0 = (float)Math.Max(vLow - 1e-4, 0);
            entry.v1 = (float)Math.Min(vHigh + 1e-4, 1);
            entry.mesh = beam.mesh;
            entry.triangle = beam.triangle;
            entry.reflection = beam.reflection;
            // The solid angle of the piece at the light over the area of its box: an estimate of its irradiance
            double solidAngle = 2 * beam.area * (u1 - u0) * (v1 - v0) * beam.cosineOverDistanceSquared;
            double boxArea = 2 * receiverArea * Math.Max((entry.u1 - entry.u0) * (entry.v1 - entry.v0), 1e-6);
            entry.weight = (float)Math.Max(solidAngle / boxArea, 1e-30);
            if (!receivers.TryGetValue((receiverMesh, r), out Receiver receiver))
            {
                receiver = new Receiver();
                receivers[(receiverMesh, r)] = receiver;
            }
            int index = receiver.entries.Count;
            receiver.entries.Add(entry);
            int c0 = Math.Clamp((int)(entry.u0 * Resolution), 0, Resolution - 1), c1 = Math.Clamp((int)(entry.u1 * Resolution), 0, Resolution - 1);
            int d0 = Math.Clamp((int)(entry.v0 * Resolution), 0, Resolution - 1), d1 = Math.Clamp((int)(entry.v1 * Resolution), 0, Resolution - 1);
            for (int cu = c0; cu <= c1; ++cu)
            {
                for (int cv = d0; cv <= d1; ++cv)
                {
                    if (receiver.cells[cu * Resolution + cv] == null)
                    {
                        receiver.cells[cu * Resolution + cv] = new List<int>();
                    }
                    receiver.cells[cu * Resolution + cv].Add(index);
                }
            }
        }

        // CausticBounds Private Members
        public Point3f light;
        Dictionary<TriangleMesh, MeshKind> kinds;
        Dictionary<(TriangleMesh, int), Receiver> receivers = new Dictionary<(TriangleMesh, int), Receiver>();
    }

}  // namespace pbrt
