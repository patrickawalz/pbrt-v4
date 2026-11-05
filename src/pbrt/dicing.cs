// (not in pbrt-v4) dicing.cs
//
// Diced (REYES-style) displacement of the parametric shapes. A shape with a "texture displacement" is not intersected analytically: its parameter
// domain is diced into a grid of micropolygons, every vertex is displaced along the surface normal, and the result is a pbrt triangle mesh (so the
// intersection, the area and the sampling of the triangles of pbrt-v4 apply unchanged). Shapes without a displacement never reach this file.
// See docs/displacement.md for the scene file interface and the decisions per shape.
//
// The surface is a set of patches, rectangles of the parameter plane (a quadric is one patch, a patch mesh one per patch, a NURBS surface one per
// Bezier cell, a subdivision surface one per limit patch). Every patch is diced with a rate per EDGE (the number of segments of each of its four
// sides, a function of the length of that side in render space only), so two patches that share a side agree on its vertices: the interior of a
// patch is a regular grid of nu x nv cells and the ring between the grid and the border is stitched to the border with a zipper of triangles.
// The vertices of all patches are then welded by position, the displacement of a welded vertex is evaluated once (on the normal that is the mean of
// the normals of the patches that meet there), which makes the mesh watertight. Normals are recomputed from the displaced triangles (angle weighted,
// split at creases), and trimmed surfaces are clipped on the micropolygons (marching triangles with bisection on the grid edges).

using System;
using System.Collections.Generic;

namespace pbrt
{
    // A parametric surface made of patches
    public interface IDicedSurface
    {
        int PatchCount { get; }

        // The rectangle of the parameter plane of the patch
        void PatchDomain(int patch, out double u0, out double u1, out double v0, out double v1);

        // The object space point, the unit normal along cross(dP/du, dP/dv) and the texture coordinates at the parameters (u, v) of the patch
        void Evaluate(int patch, double u, double v, out SdfVector p, out SdfVector n, out Point2f uv);

        // True if part of the domain is cut away (trim curves); the parameters must then be consistent across patches (one global (u, v))
        bool HasTrim { get; }

        bool IsVisible(int patch, double u, double v);

        // The face index (ptex) of the triangles of the patch
        int FaceIndex(int patch);
    }

    public sealed class DicingSettings
    {
        public FloatTexture displacement;
        public double scale = 1;
        public double offset = 0;
        // The conservative bound of the displacement the scene file promises (0: none); the dicer reports the vertices that exceed it
        public double bound = 0;
        // The length of an edge of a micropolygon in render space (edgelength * displacementEdgeScale)
        public double edgeLength = 1;
        // The largest number of segments of an edge
        public int maxEdgeRate = 1024;
        // The largest number of micropolygons of a shape (the edge length grows to fit)
        public long maxMicropolygons = 8000000;
        // The angle (degrees) between the normals of two triangles above which they do not share a vertex normal
        public double creaseAngle = 80;
        public Transform renderFromObject;
        public bool reverseOrientation;
        public string shapeName = "";

        // The "texture displacement" of the parameters of a shape, or null if the shape has no displacement
        public static DicingSettings Parse(string shapeName, ParameterDictionary parameters, IDictionary<string, FloatTexture> floatTextures,
                                           Transform renderFromObject, bool reverseOrientation, FileLoc loc)
        {
            string name = parameters.GetTexture("displacement");
            if (name.Length == 0)
            {
                return null;
            }
            if (!floatTextures.TryGetValue(name, out FloatTexture texture))
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: no such texture defined.", name));
            }
            DicingSettings settings = new DicingSettings();
            settings.shapeName = shapeName;
            settings.displacement = texture;
            settings.scale = parameters.GetOneFloat("displacementscale", 1);
            settings.offset = parameters.GetOneFloat("displacementoffset", 0);
            settings.bound = parameters.GetOneFloat("displacementbound", 0);
            settings.edgeLength = parameters.GetOneFloat("edgelength", 1.0f) * (double)pbrt.Options.displacementEdgeScale;
            settings.maxEdgeRate = Math.Max(1, parameters.GetOneInt("dicemaxrate", 1024));
            settings.maxMicropolygons = Math.Max(16, parameters.GetOneInt("dicemaxquads", 8000000));
            settings.creaseAngle = parameters.GetOneFloat("dicecreaseangle", 80);
            settings.renderFromObject = renderFromObject;
            settings.reverseOrientation = reverseOrientation;
            if (!(settings.edgeLength > 0))
            {
                pbrt.ErrorExit(loc, shapeName + ": \"edgelength\" must be positive.");
            }
            return settings;
        }
    }

    // The result of the dicing: the displaced triangles in object space
    public sealed class DicedMesh
    {
        public Point3f[] p;
        public Normal3f[] n;
        public Point2f[] uv;
        public int[] indices;
        public int[] faceIndices;
        // The statistics of the dicing
        public double maxDisplacement;
        public int boundViolations;
        public double effectiveEdgeLength;
        public long microPolygons;

        public int TriangleCount
        {
            get { return indices.Length / 3; }
        }
    }

    public static class Dicer
    {
        [ThreadStatic] private static long nDicedShapes, nDicedTriangles;
        private static StatRegisterer STATS_REG_DICING = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Geometry/Shapes with diced displacement", nDicedShapes);
            nDicedShapes = 0;
            accum.ReportCounter("Geometry/Triangles from diced displacement", nDicedTriangles);
            nDicedTriangles = 0;
        });

        private sealed class PatchGrid
        {
            public List<double> u = new List<double>();
            public List<double> v = new List<double>();
            public List<int> tris = new List<int>();
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Rates

        private static SdfVector Render(double[,] m, SdfVector p)
        {
            return new SdfVector(m[0, 0] * p.x + m[0, 1] * p.y + m[0, 2] * p.z + m[0, 3], m[1, 0] * p.x + m[1, 1] * p.y + m[1, 2] * p.z + m[1, 3],
                                 m[2, 0] * p.x + m[2, 1] * p.y + m[2, 2] * p.z + m[2, 3]);
        }

        private static double[,] MatrixOf(Transform t)
        {
            SquareMatrix4 s = t.GetMatrix();
            double[,] m = new double[4, 4];
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    m[i, j] = s[i][j];
                }
            }
            return m;
        }

        // The length in render space of the curve (ua, va) .. (ub, vb) of the patch, a polyline of 16 segments
        private static double CurveLength(IDicedSurface surface, int patch, double ua, double va, double ub, double vb, double[,] m)
        {
            const int segments = 16;
            double length = 0;
            SdfVector previous = default;
            for (int k = 0; k <= segments; ++k)
            {
                double t = (double)k / segments;
                surface.Evaluate(patch, ua + (ub - ua) * t, va + (vb - va) * t, out SdfVector p, out SdfVector n, out Point2f uv);
                SdfVector q = Render(m, p);
                if (k > 0)
                {
                    length += (q - previous).Length();
                }
                previous = q;
            }
            return length;
        }

        // The number of segments of a curve of the given length. The quotient is rounded to 1e-4 first so that the two patches that evaluate the same
        // side (in opposite directions, with round-off differences) get the same rate.
        private static int RateOf(double length, double edgeLength, int maxRate)
        {
            double quotient = Math.Round(length / edgeLength * 1e4) / 1e4;
            double rate = Math.Ceiling(quotient);
            if (!(rate >= 1))
            {
                return 1;
            }
            return rate > maxRate ? maxRate : (int)rate;
        }

        // bottom, right, top, left, nu, nv
        private static int[] PatchRates(IDicedSurface surface, int patch, double edgeLength, int maxRate, double[,] m)
        {
            surface.PatchDomain(patch, out double u0, out double u1, out double v0, out double v1);
            double vm = 0.5 * (v0 + v1), um = 0.5 * (u0 + u1);
            int rB = RateOf(CurveLength(surface, patch, u0, v0, u1, v0, m), edgeLength, maxRate);
            int rT = RateOf(CurveLength(surface, patch, u0, v1, u1, v1, m), edgeLength, maxRate);
            int rL = RateOf(CurveLength(surface, patch, u0, v0, u0, v1, m), edgeLength, maxRate);
            int rR = RateOf(CurveLength(surface, patch, u1, v0, u1, v1, m), edgeLength, maxRate);
            int mu = RateOf(CurveLength(surface, patch, u0, vm, u1, vm, m), edgeLength, maxRate);
            int mv = RateOf(CurveLength(surface, patch, um, v0, um, v1, m), edgeLength, maxRate);
            int nu = Math.Min(maxRate, Math.Max(2, Math.Max(Math.Max(rB, rT), mu)));
            int nv = Math.Min(maxRate, Math.Max(2, Math.Max(Math.Max(rL, rR), mv)));
            return new int[] { rB, rR, rT, rL, nu, nv };
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // The grid of a patch

        private static void Zipper(PatchGrid g, int[] outer, double[] sOuter, int[] inner, double[] sInner)
        {
            int i = 0, k = 0;
            while (i < outer.Length - 1 || k < inner.Length - 1)
            {
                bool advanceOuter;
                if (i == outer.Length - 1)
                {
                    advanceOuter = false;
                }
                else if (k == inner.Length - 1)
                {
                    advanceOuter = true;
                }
                else
                {
                    advanceOuter = sOuter[i + 1] <= sInner[k + 1];
                }
                if (advanceOuter)
                {
                    g.tris.Add(outer[i]);
                    g.tris.Add(outer[i + 1]);
                    g.tris.Add(inner[k]);
                    ++i;
                }
                else
                {
                    g.tris.Add(outer[i]);
                    g.tris.Add(inner[k + 1]);
                    g.tris.Add(inner[k]);
                    ++k;
                }
            }
        }

        private static int AddVertex(PatchGrid g, double u, double v)
        {
            g.u.Add(u);
            g.v.Add(v);
            return g.u.Count - 1;
        }

        private static PatchGrid BuildGrid(double u0, double u1, double v0, double v1, int[] rates)
        {
            int rB = rates[0], rR = rates[1], rT = rates[2], rL = rates[3], nu = rates[4], nv = rates[5];
            PatchGrid g = new PatchGrid();
            int c00 = AddVertex(g, u0, v0), c10 = AddVertex(g, u1, v0), c11 = AddVertex(g, u1, v1), c01 = AddVertex(g, u0, v1);
            double du = u1 - u0, dv = v1 - v0;
            int[] bottom = new int[rB + 1], right = new int[rR + 1], top = new int[rT + 1], left = new int[rL + 1];
            bottom[0] = c00;
            bottom[rB] = c10;
            for (int k = 1; k < rB; ++k)
            {
                bottom[k] = AddVertex(g, u0 + du * k / rB, v0);
            }
            right[0] = c10;
            right[rR] = c11;
            for (int k = 1; k < rR; ++k)
            {
                right[k] = AddVertex(g, u1, v0 + dv * k / rR);
            }
            top[0] = c01;
            top[rT] = c11;
            for (int k = 1; k < rT; ++k)
            {
                top[k] = AddVertex(g, u0 + du * k / rT, v1);
            }
            left[0] = c00;
            left[rL] = c01;
            for (int k = 1; k < rL; ++k)
            {
                left[k] = AddVertex(g, u0, v0 + dv * k / rL);
            }
            int[,] inner = new int[nu + 1, nv + 1];
            for (int j = 1; j < nv; ++j)
            {
                for (int i = 1; i < nu; ++i)
                {
                    inner[i, j] = AddVertex(g, u0 + du * i / nu, v0 + dv * j / nv);
                }
            }
            // The cells of the interior
            for (int j = 1; j < nv - 1; ++j)
            {
                for (int i = 1; i < nu - 1; ++i)
                {
                    int a = inner[i, j], b = inner[i + 1, j], c = inner[i + 1, j + 1], d = inner[i, j + 1];
                    if (((i + j) & 1) == 0)
                    {
                        g.tris.Add(a); g.tris.Add(b); g.tris.Add(c);
                        g.tris.Add(a); g.tris.Add(c); g.tris.Add(d);
                    }
                    else
                    {
                        g.tris.Add(a); g.tris.Add(b); g.tris.Add(d);
                        g.tris.Add(b); g.tris.Add(c); g.tris.Add(d);
                    }
                }
            }
            // The ring: the four sides, counter-clockwise in (u, v), each from the corner of the border to the corner of the interior grid
            {
                int[] outer = bottom;
                double[] so = new double[rB + 1];
                for (int k = 0; k <= rB; ++k) { so[k] = (double)k / rB; }
                int[] inn = new int[nu - 1];
                double[] si = new double[nu - 1];
                for (int i = 1; i < nu; ++i) { inn[i - 1] = inner[i, 1]; si[i - 1] = (double)i / nu; }
                Zipper(g, outer, so, inn, si);
            }
            {
                int[] outer = right;
                double[] so = new double[rR + 1];
                for (int k = 0; k <= rR; ++k) { so[k] = (double)k / rR; }
                int[] inn = new int[nv - 1];
                double[] si = new double[nv - 1];
                for (int j = 1; j < nv; ++j) { inn[j - 1] = inner[nu - 1, j]; si[j - 1] = (double)j / nv; }
                Zipper(g, outer, so, inn, si);
            }
            {
                int[] outer = new int[rT + 1];
                double[] so = new double[rT + 1];
                for (int k = 0; k <= rT; ++k) { outer[k] = top[rT - k]; so[k] = (double)k / rT; }
                int[] inn = new int[nu - 1];
                double[] si = new double[nu - 1];
                for (int m = 0; m < nu - 1; ++m) { inn[m] = inner[nu - 1 - m, nv - 1]; si[m] = (double)(m + 1) / nu; }
                Zipper(g, outer, so, inn, si);
            }
            {
                int[] outer = new int[rL + 1];
                double[] so = new double[rL + 1];
                for (int k = 0; k <= rL; ++k) { outer[k] = left[rL - k]; so[k] = (double)k / rL; }
                int[] inn = new int[nv - 1];
                double[] si = new double[nv - 1];
                for (int m = 0; m < nv - 1; ++m) { inn[m] = inner[1, nv - 1 - m]; si[m] = (double)(m + 1) / nv; }
                Zipper(g, outer, so, inn, si);
            }
            return g;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Trim: marching triangles on the micropolygons. A vertex on the trim curve is found by bisection on the edge of the micropolygon between a
        // visible and an invisible vertex, the edge is oriented by its parameters so that the neighbouring patches find the same point.

        private static PatchGrid Clip(IDicedSurface surface, int patch, PatchGrid g)
        {
            int count = g.u.Count;
            bool[] visible = new bool[count];
            for (int i = 0; i < count; ++i)
            {
                visible[i] = surface.IsVisible(patch, g.u[i], g.v[i]);
            }
            PatchGrid result = new PatchGrid();
            result.u.AddRange(g.u);
            result.v.AddRange(g.v);
            Dictionary<long, int> crossings = new Dictionary<long, int>();
            Func<int, int, int> crossing = (int a, int b) =>
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (crossings.TryGetValue(key, out int existing))
                {
                    return existing;
                }
                // lo -> hi in the order of the parameters
                int lo = a, hi = b;
                if (g.u[a] > g.u[b] || (g.u[a] == g.u[b] && g.v[a] > g.v[b]))
                {
                    lo = b;
                    hi = a;
                }
                bool loVisible = visible[lo];
                double ua = g.u[lo], va = g.v[lo], ub = g.u[hi], vb = g.v[hi];
                double ta = 0, tb = 1;
                for (int iteration = 0; iteration < 48; ++iteration)
                {
                    double tm = 0.5 * (ta + tb);
                    bool vis = surface.IsVisible(patch, ua + (ub - ua) * tm, va + (vb - va) * tm);
                    if (vis == loVisible)
                    {
                        ta = tm;
                    }
                    else
                    {
                        tb = tm;
                    }
                }
                double t = 0.5 * (ta + tb);
                int index = AddVertex(result, ua + (ub - ua) * t, va + (vb - va) * t);
                crossings[key] = index;
                return index;
            };
            for (int t = 0; t + 2 < g.tris.Count; t += 3)
            {
                int[] c = { g.tris[t], g.tris[t + 1], g.tris[t + 2] };
                int nVisible = (visible[c[0]] ? 1 : 0) + (visible[c[1]] ? 1 : 0) + (visible[c[2]] ? 1 : 0);
                if (nVisible == 3)
                {
                    result.tris.Add(c[0]); result.tris.Add(c[1]); result.tris.Add(c[2]);
                }
                else if (nVisible == 1)
                {
                    int s = visible[c[0]] ? 0 : (visible[c[1]] ? 1 : 2);
                    int a = c[s], b = c[(s + 1) % 3], d = c[(s + 2) % 3];
                    result.tris.Add(a); result.tris.Add(crossing(a, b)); result.tris.Add(crossing(d, a));
                }
                else if (nVisible == 2)
                {
                    int s = !visible[c[0]] ? 0 : (!visible[c[1]] ? 1 : 2);   // the invisible one
                    int d = c[s], a = c[(s + 1) % 3], b = c[(s + 2) % 3];
                    int xb = crossing(b, d), xa = crossing(d, a);
                    result.tris.Add(a); result.tris.Add(b); result.tris.Add(xb);
                    result.tris.Add(a); result.tris.Add(xb); result.tris.Add(xa);
                }
            }
            return result;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------
        // Welding: the groups of vertices that are the same point of the undisplaced surface

        private static int[] Weld(SdfVector[] points, out int groupCount)
        {
            int count = points.Length;
            double lowX = double.MaxValue, lowY = double.MaxValue, lowZ = double.MaxValue, highX = double.MinValue, highY = double.MinValue, highZ = double.MinValue;
            for (int i = 0; i < count; ++i)
            {
                lowX = Math.Min(lowX, points[i].x); highX = Math.Max(highX, points[i].x);
                lowY = Math.Min(lowY, points[i].y); highY = Math.Max(highY, points[i].y);
                lowZ = Math.Min(lowZ, points[i].z); highZ = Math.Max(highZ, points[i].z);
            }
            double diagonal = Math.Sqrt((highX - lowX) * (highX - lowX) + (highY - lowY) * (highY - lowY) + (highZ - lowZ) * (highZ - lowZ));
            if (!(diagonal > 0))
            {
                diagonal = 1;
            }
            double tolerance = 2e-7 * diagonal, cell = 16 * tolerance;
            int[] group = new int[count];
            Dictionary<ulong, int> heads = new Dictionary<ulong, int>();
            List<int> representative = new List<int>();
            List<int> next = new List<int>();
            groupCount = 0;
            int[] cx = new int[2], cy = new int[2], cz = new int[2];
            for (int i = 0; i < count; ++i)
            {
                SdfVector p = points[i];
                int nx = Cells(p.x - lowX, tolerance, cell, cx), ny = Cells(p.y - lowY, tolerance, cell, cy), nz = Cells(p.z - lowZ, tolerance, cell, cz);
                int found = -1;
                for (int a = 0; a < nx && found < 0; ++a)
                {
                    for (int b = 0; b < ny && found < 0; ++b)
                    {
                        for (int c = 0; c < nz && found < 0; ++c)
                        {
                            if (!heads.TryGetValue(Key(cx[a], cy[b], cz[c]), out int g))
                            {
                                continue;
                            }
                            for (; g >= 0; g = next[g])
                            {
                                SdfVector q = points[representative[g]];
                                if (Math.Abs(q.x - p.x) <= tolerance && Math.Abs(q.y - p.y) <= tolerance && Math.Abs(q.z - p.z) <= tolerance)
                                {
                                    found = g;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (found < 0)
                {
                    found = groupCount++;
                    representative.Add(i);
                    ulong key = Key((int)Math.Floor((p.x - lowX) / cell), (int)Math.Floor((p.y - lowY) / cell), (int)Math.Floor((p.z - lowZ) / cell));
                    heads.TryGetValue(key, out int head);
                    next.Add(heads.ContainsKey(key) ? head : -1);
                    heads[key] = found;
                }
                group[i] = found;
            }
            return group;
        }

        private static int Cells(double x, double tolerance, double cell, int[] cells)
        {
            int lo = (int)Math.Floor((x - tolerance) / cell), hi = (int)Math.Floor((x + tolerance) / cell);
            cells[0] = lo;
            if (hi != lo)
            {
                cells[1] = hi;
                return 2;
            }
            return 1;
        }

        private static ulong Key(int x, int y, int z)
        {
            return ((ulong)(uint)(x + 1) & 0x1FFFFF) << 42 | ((ulong)(uint)(y + 1) & 0x1FFFFF) << 21 | ((ulong)(uint)(z + 1) & 0x1FFFFF);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------------

        public static DicedMesh Dice(IDicedSurface surface, DicingSettings settings)
        {
            double[,] m = MatrixOf(settings.renderFromObject);
            int patchCount = surface.PatchCount;

            // The rates; a shape that would have too many micropolygons is diced with a longer edge
            double edgeLength = settings.edgeLength;
            int[][] rates = new int[patchCount][];
            for (int attempt = 0; attempt < 6; ++attempt)
            {
                double el = edgeLength;
                int[][] r = rates;
                pbrt.ParallelFor(0, patchCount, (int i) =>
                {
                    r[i] = PatchRates(surface, i, el, settings.maxEdgeRate, m);
                });
                double cells = 0;
                for (int i = 0; i < patchCount; ++i)
                {
                    cells += (double)rates[i][4] * rates[i][5];
                }
                if (cells <= settings.maxMicropolygons)
                {
                    break;
                }
                edgeLength *= Math.Max(1.05, Math.Sqrt(cells / settings.maxMicropolygons));
                if (attempt == 0)
                {
                    pbrt.Warning(settings.shapeName + ": diced displacement would make " + (long)cells + " micropolygons (more than \"dicemaxquads\" " +
                                 settings.maxMicropolygons + "); the edge length grows to fit.");
                }
            }

            // The grids of the patches
            PatchGrid[] grids = new PatchGrid[patchCount];
            pbrt.ParallelFor(0, patchCount, (int i) =>
            {
                surface.PatchDomain(i, out double u0, out double u1, out double v0, out double v1);
                PatchGrid g = BuildGrid(u0, u1, v0, v1, rates[i]);
                grids[i] = surface.HasTrim ? Clip(surface, i, g) : g;
            });

            // One list of vertices and of triangles
            int vertexCount = 0, indexCount = 0;
            int[] vertexStart = new int[patchCount], indexStart = new int[patchCount];
            for (int i = 0; i < patchCount; ++i)
            {
                vertexStart[i] = vertexCount;
                indexStart[i] = indexCount;
                vertexCount += grids[i].u.Count;
                indexCount += grids[i].tris.Count;
            }
            int[] patchOf = new int[vertexCount];
            double[] gu = new double[vertexCount], gv = new double[vertexCount];
            int[] tri = new int[indexCount];
            int[] triPatch = new int[indexCount / 3];
            for (int i = 0; i < patchCount; ++i)
            {
                PatchGrid g = grids[i];
                for (int k = 0; k < g.u.Count; ++k)
                {
                    patchOf[vertexStart[i] + k] = i;
                    gu[vertexStart[i] + k] = g.u[k];
                    gv[vertexStart[i] + k] = g.v[k];
                }
                for (int k = 0; k < g.tris.Count; ++k)
                {
                    tri[indexStart[i] + k] = vertexStart[i] + g.tris[k];
                }
                for (int k = 0; k < g.tris.Count / 3; ++k)
                {
                    triPatch[indexStart[i] / 3 + k] = i;
                }
            }
            grids = null;

            // The undisplaced surface
            SdfVector[] p0 = new SdfVector[vertexCount], n0 = new SdfVector[vertexCount];
            Point2f[] uv0 = new Point2f[vertexCount];
            pbrt.ParallelFor(0, vertexCount, (int i) =>
            {
                surface.Evaluate(patchOf[i], gu[i], gv[i], out p0[i], out n0[i], out uv0[i]);
            });

            // The welded vertices: one displacement, on the mean normal, for all the vertices of a group
            int groupCount;
            int[] group = Weld(p0, out groupCount);
            int[] representative = new int[groupCount];
            for (int i = 0; i < groupCount; ++i)
            {
                representative[i] = -1;
            }
            SdfVector[] normalSum = new SdfVector[groupCount];
            int[] members = new int[groupCount];
            for (int i = 0; i < vertexCount; ++i)
            {
                int gi = group[i];
                if (representative[gi] < 0)
                {
                    representative[gi] = i;
                }
                normalSum[gi] = normalSum[gi] + n0[i];
                ++members[gi];
            }
            double sign = settings.reverseOrientation ? -1 : 1;
            Point3f[] position = new Point3f[groupCount];
            double[] displacementOf = new double[groupCount];
            pbrt.ParallelFor(0, groupCount, (int g) =>
            {
                int r = representative[g];
                SdfVector n = n0[r];
                SdfVector sum = normalSum[g];
                if (sum.Length() > 0.2 * members[g])
                {
                    n = sum.Normalized();
                }
                SdfVector p = p0[r];
                TextureEvalContext ctx = default;
                ctx.p = new Point3f((Float)p.x, (Float)p.y, (Float)p.z);
                ctx.uv = uv0[r];
                ctx.n = new Normal3f((Float)n.x, (Float)n.y, (Float)n.z);
                double d = settings.scale * new UniversalTextureEvaluator().Apply(settings.displacement, ctx) + settings.offset;
                if (double.IsNaN(d) || double.IsInfinity(d))
                {
                    d = 0;
                }
                displacementOf[g] = d;
                SdfVector q = p + (sign * d) * n;
                position[g] = new Point3f((Float)q.x, (Float)q.y, (Float)q.z);
            });
            double maxDisplacement = 0;
            int violations = 0;
            for (int g = 0; g < groupCount; ++g)
            {
                double a = Math.Abs(displacementOf[g]);
                maxDisplacement = Math.Max(maxDisplacement, a);
                if (settings.bound > 0 && a > settings.bound * (1 + 1e-4))
                {
                    ++violations;
                }
            }
            if (violations > 0)
            {
                pbrt.Warning(settings.shapeName + ": " + violations + " diced vertices are displaced by more than \"displacementbound\" " + settings.bound +
                             " (largest displacement " + maxDisplacement + ").");
            }

            // The triangles on the welded vertices (the degenerate ones go: a collapsed edge at a pole, a zero area)
            int triangleCount = indexCount / 3;
            List<int> keep = new List<int>(triangleCount);
            SdfVector[] faceNormal = new SdfVector[triangleCount];
            for (int t = 0; t < triangleCount; ++t)
            {
                int a = group[tri[3 * t]], b = group[tri[3 * t + 1]], c = group[tri[3 * t + 2]];
                if (a == b || b == c || a == c)
                {
                    continue;
                }
                SdfVector pa = new SdfVector(position[a].x, position[a].y, position[a].z), pb = new SdfVector(position[b].x, position[b].y, position[b].z),
                          pc = new SdfVector(position[c].x, position[c].y, position[c].z);
                SdfVector cross = SdfVector.Cross(pb - pa, pc - pa);
                double length = cross.Length();
                if (!(length > 0))
                {
                    continue;
                }
                faceNormal[t] = cross / length;
                keep.Add(t);
            }

            // The corners of every group, then the vertices of the output: a group is split at a crease (the angle between the face normals) and where
            // the texture coordinates differ
            int[] cornerStart = new int[groupCount + 1];
            foreach (int t in keep)
            {
                for (int k = 0; k < 3; ++k)
                {
                    ++cornerStart[group[tri[3 * t + k]] + 1];
                }
            }
            for (int g = 0; g < groupCount; ++g)
            {
                cornerStart[g + 1] += cornerStart[g];
            }
            int[] fill = new int[groupCount];
            int[] corners = new int[cornerStart[groupCount]];   // 3 * t + k
            foreach (int t in keep)
            {
                for (int k = 0; k < 3; ++k)
                {
                    int g = group[tri[3 * t + k]];
                    corners[cornerStart[g] + fill[g]++] = 3 * t + k;
                }
            }
            double cosCrease = Math.Cos(Math.Min(179.0, Math.Max(0.0, settings.creaseAngle)) * Math.PI / 180);
            int[] outputOf = new int[3 * triangleCount];
            List<Point3f> outP = new List<Point3f>();
            List<Normal3f> outN = new List<Normal3f>();
            List<Point2f> outUv = new List<Point2f>();
            List<SdfVector> clusterNormal = new List<SdfVector>(), clusterSum = new List<SdfVector>();
            List<int> clusterOf = new List<int>();
            for (int g = 0; g < groupCount; ++g)
            {
                int begin = cornerStart[g], end = cornerStart[g + 1];
                if (begin == end)
                {
                    continue;
                }
                clusterNormal.Clear();
                clusterSum.Clear();
                clusterOf.Clear();
                for (int s = begin; s < end; ++s)
                {
                    int corner = corners[s], t = corner / 3, k = corner % 3;
                    SdfVector fn = faceNormal[t];
                    int cluster = -1;
                    for (int q = 0; q < clusterNormal.Count; ++q)
                    {
                        if (SdfVector.Dot(clusterNormal[q], fn) >= cosCrease)
                        {
                            cluster = q;
                            break;
                        }
                    }
                    if (cluster < 0)
                    {
                        cluster = clusterNormal.Count;
                        clusterNormal.Add(fn);
                        clusterSum.Add(new SdfVector(0, 0, 0));
                    }
                    // angle weighted
                    int ia = group[tri[3 * t + k]], ib = group[tri[3 * t + (k + 1) % 3]], ic = group[tri[3 * t + (k + 2) % 3]];
                    SdfVector e1 = new SdfVector(position[ib].x - position[ia].x, position[ib].y - position[ia].y, position[ib].z - position[ia].z);
                    SdfVector e2 = new SdfVector(position[ic].x - position[ia].x, position[ic].y - position[ia].y, position[ic].z - position[ia].z);
                    double cosAngle = SdfVector.Dot(e1, e2) / Math.Max(1e-300, e1.Length() * e2.Length());
                    double angle = Math.Acos(Math.Max(-1, Math.Min(1, cosAngle)));
                    clusterSum[cluster] = clusterSum[cluster] + angle * fn;
                    clusterOf.Add(cluster);
                }
                for (int cluster = 0; cluster < clusterNormal.Count; ++cluster)
                {
                    SdfVector sum = clusterSum[cluster];
                    SdfVector n = sum.Length() > 0 ? sum.Normalized() : clusterNormal[cluster];
                    int clusterStart = outP.Count;
                    for (int s = begin; s < end; ++s)
                    {
                        if (clusterOf[s - begin] != cluster)
                        {
                            continue;
                        }
                        int corner = corners[s];
                        Point2f uv = uv0[tri[corner]];
                        int vertex = -1;
                        for (int w = clusterStart; w < outP.Count; ++w)
                        {
                            double tol = 1e-6 * (1 + Math.Abs(uv.x) + Math.Abs(uv.y));
                            if (Math.Abs(outUv[w].x - uv.x) <= tol && Math.Abs(outUv[w].y - uv.y) <= tol)
                            {
                                vertex = w;
                                break;
                            }
                        }
                        if (vertex < 0)
                        {
                            vertex = outP.Count;
                            outP.Add(position[g]);
                            outN.Add(new Normal3f((Float)n.x, (Float)n.y, (Float)n.z));
                            outUv.Add(uv);
                        }
                        outputOf[corner] = vertex;
                    }
                }
            }

            DicedMesh mesh = new DicedMesh();
            mesh.p = outP.ToArray();
            mesh.n = outN.ToArray();
            mesh.uv = outUv.ToArray();
            mesh.indices = new int[3 * keep.Count];
            int[] faces = new int[keep.Count];
            bool anyFace = false;
            for (int i = 0; i < keep.Count; ++i)
            {
                int t = keep[i];
                mesh.indices[3 * i] = outputOf[3 * t];
                mesh.indices[3 * i + 1] = outputOf[3 * t + 1];
                mesh.indices[3 * i + 2] = outputOf[3 * t + 2];
                faces[i] = surface.FaceIndex(triPatch[t]);
                anyFace |= faces[i] != 0;
            }
            mesh.faceIndices = anyFace ? faces : null;
            mesh.maxDisplacement = maxDisplacement;
            mesh.boundViolations = violations;
            mesh.effectiveEdgeLength = edgeLength;
            mesh.microPolygons = triangleCount / 2;
            ++nDicedShapes;
            nDicedTriangles += keep.Count;
            return mesh;
        }

        // The triangles of pbrt-v4 for the diced mesh
        public static pstd.vector<Shape> CreateShapes(DicedMesh mesh, DicingSettings settings, Allocator alloc)
        {
            if (mesh.indices.Length == 0)
            {
                return new pstd.vector<Shape>();
            }
            TriangleMesh triangles = new TriangleMesh(settings.renderFromObject, settings.reverseOrientation, new pstd.vector<int>(mesh.indices),
                                                      new pstd.vector<Point3f>(mesh.p), new pstd.vector<Vector3f>(), new pstd.vector<Normal3f>(mesh.n),
                                                      new pstd.vector<Point2f>(mesh.uv),
                                                      mesh.faceIndices != null ? new pstd.vector<int>(mesh.faceIndices) : new pstd.vector<int>(), alloc);
            return Triangle.CreateTriangles(triangles, alloc);
        }
    }
}  // namespace pbrt
