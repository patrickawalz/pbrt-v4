// (not in pbrt-v4) sdfsampling.cs
//
// Sampling the surface of a signed distance field uniformly by area, which is what an area light needs. The field is cut into a
// triangle mesh by marching tetrahedra over the cells that the surface can reach (cells are culled with the Lipschitz bound, so the
// cost follows the surface and not the volume), with the vertices on the edges moved onto the exact surface by root finding. The
// area of every triangle is measured again on a finer mesh whose points are projected onto the surface (Newton steps along the
// gradient), so the area converges as the subdivision grows and the density 1 / area is that of the true surface. A sample picks
// a triangle by its area, a point in the triangle uniformly, and projects the point onto the surface.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // SdfSurfaceSampler Definition
    public sealed class SdfSurfaceSampler
    {
        // renderFromObject is the 3x4 matrix (row major, 12 values) that takes object space to the space the area is measured in
        public SdfSurfaceSampler(SdfNode field, double lipschitz, SdfVector lower, SdfVector upper, double[] renderFromObject,
                                 int resolution, int subdivisions, double gradientStep)
        {
            this.field = field;
            this.lipschitz = lipschitz;
            this.lower = lower;
            this.upper = upper;
            this.renderFromObject = renderFromObject;
            this.gradientStep = gradientStep;
            this.subdivisions = Math.Max(1, subdivisions);
            Tessellate(Math.Max(4, resolution));
            MeasureAreas();
        }

        public double Area()
        {
            return totalArea;
        }

        public int TriangleCount()
        {
            return triangleAreas.Length;
        }

        // The triangles, nine object space coordinates each
        public double[] TriangleVertices()
        {
            return triangles;
        }

        private SdfVector ToRender(SdfVector p)
        {
            double[] m = renderFromObject;
            return new SdfVector(m[0] * p.x + m[1] * p.y + m[2] * p.z + m[3], m[4] * p.x + m[5] * p.y + m[6] * p.z + m[7],
                                 m[8] * p.x + m[9] * p.y + m[10] * p.z + m[11]);
        }

        // Moves p onto the surface by Newton steps along the gradient
        public SdfVector Project(SdfVector p)
        {
            double tolerance = 1e-12 * domainScale;
            for (int i = 0; i < 6; ++i)
            {
                double f = field.Evaluate(p);
                if (Math.Abs(f) < tolerance)
                {
                    break;
                }
                SdfVector g = field.Gradient(p, gradientStep);
                double lengthSquared = g.LengthSquared();
                if (lengthSquared == 0)
                {
                    break;
                }
                p = p - g * (f / lengthSquared);
            }
            return p;
        }

        // The point where the field vanishes between a (value fa) and b (value fb) of opposite signs (Illinois regula falsi)
        private SdfVector Crossing(SdfVector a, double fa, SdfVector b, double fb)
        {
            double tolerance = 1e-13 * domainScale;
            double ta = 0, tb = 1;
            double fl = fa, fr = fb;
            int side = 0;
            double t = 0.5;
            for (int i = 0; i < 60; ++i)
            {
                t = (fl * tb - fr * ta) / (fl - fr);
                SdfVector p = a + (b - a) * t;
                double f = field.Evaluate(p);
                if (Math.Abs(f) < tolerance)
                {
                    return p;
                }
                if (f * fr > 0)
                {
                    tb = t;
                    fr = f;
                    if (side == -1)
                    {
                        fl *= 0.5;
                    }
                    side = -1;
                }
                else
                {
                    ta = t;
                    fl = f;
                    if (side == 1)
                    {
                        fr *= 0.5;
                    }
                    side = 1;
                }
            }
            return a + (b - a) * t;
        }

        private struct Cell
        {
            public int x, y, z;
        }

        private static readonly int[][] Tetrahedra =
        {
            new int[] { 0, 1, 3, 7 }, new int[] { 0, 1, 5, 7 }, new int[] { 0, 2, 3, 7 },
            new int[] { 0, 2, 6, 7 }, new int[] { 0, 4, 5, 7 }, new int[] { 0, 4, 6, 7 }
        };

        private void Tessellate(int resolution)
        {
            double longest = Math.Max(upper.x - lower.x, Math.Max(upper.y - lower.y, upper.z - lower.z));
            domainScale = longest;
            double h = longest / resolution;
            int nx = Math.Max(1, (int)Math.Round((upper.x - lower.x) / h));
            int ny = Math.Max(1, (int)Math.Round((upper.y - lower.y) / h));
            int nz = Math.Max(1, (int)Math.Round((upper.z - lower.z) / h));
            double hx = (upper.x - lower.x) / nx, hy = (upper.y - lower.y) / ny, hz = (upper.z - lower.z) / nz;

            // Collect the cells the surface can reach by an octree descent over index ranges, culled by the Lipschitz bound
            List<Cell> cells = new List<Cell>();
            Stack<int[]> work = new Stack<int[]>();
            work.Push(new int[] { 0, nx, 0, ny, 0, nz });
            while (work.Count > 0)
            {
                int[] r = work.Pop();
                double wx = (r[1] - r[0]) * hx, wy = (r[3] - r[2]) * hy, wz = (r[5] - r[4]) * hz;
                SdfVector center = new SdfVector(lower.x + r[0] * hx + 0.5 * wx, lower.y + r[2] * hy + 0.5 * wy,
                                                 lower.z + r[4] * hz + 0.5 * wz);
                double radius = 0.5 * Math.Sqrt(wx * wx + wy * wy + wz * wz);
                if (Math.Abs(field.Evaluate(center)) > lipschitz * radius * 1.0000001)
                {
                    continue;
                }
                if (r[1] - r[0] == 1 && r[3] - r[2] == 1 && r[5] - r[4] == 1)
                {
                    cells.Add(new Cell { x = r[0], y = r[2], z = r[4] });
                    continue;
                }
                int mx = (r[0] + r[1] + 1) / 2, my = (r[2] + r[3] + 1) / 2, mz = (r[4] + r[5] + 1) / 2;
                int[] xs = r[1] - r[0] > 1 ? new int[] { r[0], mx, r[1] } : new int[] { r[0], r[1] };
                int[] ys = r[3] - r[2] > 1 ? new int[] { r[2], my, r[3] } : new int[] { r[2], r[3] };
                int[] zs = r[5] - r[4] > 1 ? new int[] { r[4], mz, r[5] } : new int[] { r[4], r[5] };
                for (int i = 0; i + 1 < xs.Length; ++i)
                {
                    for (int j = 0; j + 1 < ys.Length; ++j)
                    {
                        for (int k = 0; k + 1 < zs.Length; ++k)
                        {
                            work.Push(new int[] { xs[i], xs[i + 1], ys[j], ys[j + 1], zs[k], zs[k + 1] });
                        }
                    }
                }
            }

            // Marching tetrahedra in every cell, in parallel; cells with strongly varying normals (a crease, a corner, a small radius
            // of curvature) are cut into eight and processed again, down to AdaptiveDepth levels
            List<double[]> pieces = new List<double[]>();
            object gate = new object();
            int chunk = 64;
            int chunks = (cells.Count + chunk - 1) / chunk;
            SdfVector cellSize = new SdfVector((upper.x - lower.x) / nx, (upper.y - lower.y) / ny, (upper.z - lower.z) / nz);
            pbrt.ParallelFor(0, chunks, (int c) =>
            {
                List<double> output = new List<double>();
                for (int i = c * chunk; i < Math.Min(cells.Count, (c + 1) * chunk); ++i)
                {
                    Cell cell = cells[i];
                    ProcessCell(new SdfVector(lower.x + cell.x * cellSize.x, lower.y + cell.y * cellSize.y, lower.z + cell.z * cellSize.z),
                                cellSize, 0, output);
                }
                if (output.Count > 0)
                {
                    lock (gate)
                    {
                        pieces.Add(output.ToArray());
                    }
                }
            });
            int total = 0;
            foreach (double[] piece in pieces)
            {
                total += piece.Length;
            }
            triangles = new double[total];
            int offset = 0;
            foreach (double[] piece in pieces)
            {
                Array.Copy(piece, 0, triangles, offset, piece.Length);
                offset += piece.Length;
            }
            if (total == 0)
            {
                pbrt.ErrorExit("sdf: the surface was not found inside the bounds, so it cannot be sampled (an area light needs a surface).");
            }
        }

        private const int AdaptiveDepth = 3;
        private const double FlatnessCosine = 0.995;

        private void ProcessCell(SdfVector origin, SdfVector size, int depth, List<double> output)
        {
            SdfVector[] corner = new SdfVector[8];
            double[] value = new double[8];
            bool anyInside = false, anyOutside = false;
            for (int k = 0; k < 8; ++k)
            {
                corner[k] = new SdfVector(origin.x + (k & 1) * size.x, origin.y + ((k >> 1) & 1) * size.y, origin.z + ((k >> 2) & 1) * size.z);
                value[k] = field.Evaluate(corner[k]);
                if (value[k] < 0)
                {
                    anyInside = true;
                }
                else
                {
                    anyOutside = true;
                }
            }
            SdfVector center = origin + size * 0.5;
            double centerValue = field.Evaluate(center);
            if (Math.Abs(centerValue) > lipschitz * 0.5 * size.Length() * 1.0000001)
            {
                return;
            }
            if (centerValue < 0)
            {
                anyInside = true;
            }
            else
            {
                anyOutside = true;
            }
            if (!anyInside || !anyOutside)
            {
                return;
            }
            if (depth < AdaptiveDepth && !IsFlat(corner))
            {
                SdfVector half = size * 0.5;
                for (int k = 0; k < 8; ++k)
                {
                    ProcessCell(new SdfVector(origin.x + (k & 1) * half.x, origin.y + ((k >> 1) & 1) * half.y, origin.z + ((k >> 2) & 1) * half.z),
                                half, depth + 1, output);
                }
                return;
            }
            for (int t = 0; t < 6; ++t)
            {
                EmitTetrahedron(Tetrahedra[t], corner, value, output);
            }
        }

        // Whether the normals of the field at the corners of a cell stay within a few degrees of their mean
        private bool IsFlat(SdfVector[] corner)
        {
            SdfVector[] normal = new SdfVector[8];
            SdfVector sum = new SdfVector(0, 0, 0);
            for (int k = 0; k < 8; ++k)
            {
                normal[k] = field.Gradient(corner[k], gradientStep).Normalized();
                sum = sum + normal[k];
            }
            SdfVector mean = sum.Normalized();
            for (int k = 0; k < 8; ++k)
            {
                if (SdfVector.Dot(normal[k], mean) < FlatnessCosine)
                {
                    return false;
                }
            }
            return true;
        }

        private void EmitTetrahedron(int[] vertex, SdfVector[] corner, double[] value, List<double> output)
        {
            int insideCount = 0;
            for (int i = 0; i < 4; ++i)
            {
                if (value[vertex[i]] < 0)
                {
                    ++insideCount;
                }
            }
            if (insideCount == 0 || insideCount == 4)
            {
                return;
            }
            if (insideCount == 1 || insideCount == 3)
            {
                // The lone vertex (the inside one for 1, the outside one for 3) is cut off by one triangle
                bool loneInside = insideCount == 1;
                int lone = -1;
                for (int i = 0; i < 4; ++i)
                {
                    if ((value[vertex[i]] < 0) == loneInside)
                    {
                        lone = i;
                    }
                }
                SdfVector[] points = new SdfVector[3];
                int n = 0;
                for (int i = 0; i < 4; ++i)
                {
                    if (i != lone)
                    {
                        points[n++] = Crossing(corner[vertex[lone]], value[vertex[lone]], corner[vertex[i]], value[vertex[i]]);
                    }
                }
                Append(output, points[0], points[1], points[2]);
                return;
            }
            // Two inside, two outside: a quad between the four crossing edges
            int[] inside = new int[2], outside = new int[2];
            int a = 0, b = 0;
            for (int i = 0; i < 4; ++i)
            {
                if (value[vertex[i]] < 0)
                {
                    inside[a++] = vertex[i];
                }
                else
                {
                    outside[b++] = vertex[i];
                }
            }
            SdfVector p00 = Crossing(corner[inside[0]], value[inside[0]], corner[outside[0]], value[outside[0]]);
            SdfVector p01 = Crossing(corner[inside[0]], value[inside[0]], corner[outside[1]], value[outside[1]]);
            SdfVector p11 = Crossing(corner[inside[1]], value[inside[1]], corner[outside[1]], value[outside[1]]);
            SdfVector p10 = Crossing(corner[inside[1]], value[inside[1]], corner[outside[0]], value[outside[0]]);
            Append(output, p00, p01, p11);
            Append(output, p00, p11, p10);
        }

        private static void Append(List<double> output, SdfVector a, SdfVector b, SdfVector c)
        {
            output.Add(a.x); output.Add(a.y); output.Add(a.z);
            output.Add(b.x); output.Add(b.y); output.Add(b.z);
            output.Add(c.x); output.Add(c.y); output.Add(c.z);
        }

        private SdfVector Vertex(int triangle, int corner)
        {
            return new SdfVector(triangles[9 * triangle + 3 * corner], triangles[9 * triangle + 3 * corner + 1],
                                 triangles[9 * triangle + 3 * corner + 2]);
        }

        // The area of every triangle as the area of its subdivision with the points projected onto the surface
        private void MeasureAreas()
        {
            int count = triangles.Length / 9;
            triangleAreas = new double[count];
            int k = subdivisions;
            pbrt.ParallelFor(0, count, (int t) =>
            {
                SdfVector a = Vertex(t, 0), b = Vertex(t, 1), c = Vertex(t, 2);
                SdfVector[] row = new SdfVector[k + 1], next = new SdfVector[k + 1];
                double area = 0;
                for (int j = 0; j <= k; ++j)
                {
                    // Points (i, j) of the row j with i = 0 .. k - j
                    for (int i = 0; i <= k - j; ++i)
                    {
                        double u = (double)i / k, v = (double)j / k;
                        SdfVector p = a + (b - a) * u + (c - a) * v;
                        bool corner = (i == 0 && j == 0) || (i == k && j == 0) || (i == 0 && j == k);
                        next[i] = ToRender(corner ? p : Project(p));
                    }
                    if (j > 0)
                    {
                        for (int i = 0; i <= k - j; ++i)
                        {
                            area += TriangleArea(row[i], row[i + 1], next[i]);
                            if (i < k - j)
                            {
                                area += TriangleArea(row[i + 1], next[i + 1], next[i]);
                            }
                        }
                    }
                    SdfVector[] swap = row;
                    row = next;
                    next = swap;
                }
                triangleAreas[t] = area;
            });
            cumulativeAreas = new double[count];
            double sum = 0;
            for (int t = 0; t < count; ++t)
            {
                sum += triangleAreas[t];
                cumulativeAreas[t] = sum;
            }
            totalArea = sum;
        }

        private static double TriangleArea(SdfVector a, SdfVector b, SdfVector c)
        {
            return 0.5 * SdfVector.Cross(b - a, c - a).Length();
        }

        // A point of the surface (object space) uniform by the area measured in render space, from two random numbers in [0, 1)
        public SdfVector Sample(double u0, double u1)
        {
            double target = u0 * totalArea;
            int low = 0, high = cumulativeAreas.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (cumulativeAreas[middle] <= target)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            double begin = low > 0 ? cumulativeAreas[low - 1] : 0;
            double remapped = triangleAreas[low] > 0 ? Math.Min((target - begin) / triangleAreas[low], 1 - 1e-12) : 0;
            // Uniform in the triangle
            double root = Math.Sqrt(remapped);
            double b0 = 1 - root, b1 = u1 * root;
            SdfVector p = Vertex(low, 0) * b0 + Vertex(low, 1) * b1 + Vertex(low, 2) * (1 - b0 - b1);
            return Project(p);
        }

        private readonly SdfNode field;
        private readonly double lipschitz, gradientStep;
        private readonly SdfVector lower, upper;
        private readonly double[] renderFromObject;
        private readonly int subdivisions;
        private double domainScale;
        private double[] triangles, triangleAreas, cumulativeAreas;
        private double totalArea;
    }

}  // namespace pbrt
