// (not in pbrt-v4) sdfmesh.cs
//
// Triangle meshes as signed distance fields (what Octane calls vectorizing a mesh). SdfMeshDistance is the exact field: the distance
// to the closest point of the mesh found with a bounding volume hierarchy, signed by the angle weighted pseudonormal of the closest
// feature (Baerentzen and Aanaes 2005), which is exact for a closed, consistently oriented mesh. SdfGrid is a field sampled on a
// regular grid (baked from the exact one, or from any other field) with a cubic or a linear reconstruction.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // SdfMeshDistance Definition
    public sealed class SdfMeshDistance : SdfNode
    {
        // The mesh is given by its vertices and the three indices of every triangle (counter clockwise seen from outside)
        public SdfMeshDistance(SdfVector[] vertices, int[] indices)
        {
            this.vertices = vertices;
            int triangleCount = indices.Length / 3;
            this.indices = indices;
            faceNormals = new SdfVector[triangleCount];
            vertexNormals = new SdfVector[vertices.Length];
            Dictionary<long, SdfVector> edgeSums = new Dictionary<long, SdfVector>();
            for (int t = 0; t < triangleCount; ++t)
            {
                int i0 = indices[3 * t], i1 = indices[3 * t + 1], i2 = indices[3 * t + 2];
                SdfVector p0 = vertices[i0], p1 = vertices[i1], p2 = vertices[i2];
                SdfVector n = SdfVector.Cross(p1 - p0, p2 - p0);
                double length = n.Length();
                SdfVector faceNormal = length > 0 ? n / length : new SdfVector(0, 0, 0);
                faceNormals[t] = faceNormal;
                AddEdge(edgeSums, i0, i1, faceNormal);
                AddEdge(edgeSums, i1, i2, faceNormal);
                AddEdge(edgeSums, i2, i0, faceNormal);
                vertexNormals[i0] = vertexNormals[i0] + Angle(p1 - p0, p2 - p0) * faceNormal;
                vertexNormals[i1] = vertexNormals[i1] + Angle(p2 - p1, p0 - p1) * faceNormal;
                vertexNormals[i2] = vertexNormals[i2] + Angle(p0 - p2, p1 - p2) * faceNormal;
            }
            edgeNormals = edgeSums;
            BuildHierarchy();
        }

        private static double Angle(SdfVector a, SdfVector b)
        {
            double denominator = a.Length() * b.Length();
            if (denominator == 0)
            {
                return 0;
            }
            return Math.Acos(Math.Min(Math.Max(SdfVector.Dot(a, b) / denominator, -1), 1));
        }

        private static long EdgeKey(int i, int j)
        {
            return i < j ? (((long)i) << 32) | (uint)j : (((long)j) << 32) | (uint)i;
        }

        private static void AddEdge(Dictionary<long, SdfVector> sums, int i, int j, SdfVector faceNormal)
        {
            long key = EdgeKey(i, j);
            sums.TryGetValue(key, out SdfVector current);
            sums[key] = current + faceNormal;
        }

        // The bounds of the mesh vertices
        public void VertexBounds(out SdfVector lower, out SdfVector upper)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, z0 = double.MaxValue;
            double x1 = double.MinValue, y1 = double.MinValue, z1 = double.MinValue;
            for (int i = 0; i < vertices.Length; ++i)
            {
                x0 = Math.Min(x0, vertices[i].x);
                y0 = Math.Min(y0, vertices[i].y);
                z0 = Math.Min(z0, vertices[i].z);
                x1 = Math.Max(x1, vertices[i].x);
                y1 = Math.Max(y1, vertices[i].y);
                z1 = Math.Max(z1, vertices[i].z);
            }
            lower = new SdfVector(x0, y0, z0);
            upper = new SdfVector(x1, y1, z1);
        }

        // The hierarchy: nodes in depth first order; a leaf holds triangleCount triangles from firstTriangle of the ordered list
        private struct HierarchyNode
        {
            public double x0, y0, z0, x1, y1, z1;
            public int secondChild;
            public int firstTriangle;
            public int triangleCount;
        }

        private void BuildHierarchy()
        {
            int triangleCount = indices.Length / 3;
            order = new int[triangleCount];
            SdfVector[] centroids = new SdfVector[triangleCount];
            for (int t = 0; t < triangleCount; ++t)
            {
                order[t] = t;
                centroids[t] = (vertices[indices[3 * t]] + vertices[indices[3 * t + 1]] + vertices[indices[3 * t + 2]]) / 3.0;
            }
            nodes = new List<HierarchyNode>();
            Build(0, triangleCount, centroids);
        }

        private int Build(int begin, int end, SdfVector[] centroids)
        {
            HierarchyNode node = new HierarchyNode();
            node.x0 = node.y0 = node.z0 = double.MaxValue;
            node.x1 = node.y1 = node.z1 = double.MinValue;
            double cx0 = double.MaxValue, cy0 = double.MaxValue, cz0 = double.MaxValue;
            double cx1 = double.MinValue, cy1 = double.MinValue, cz1 = double.MinValue;
            for (int i = begin; i < end; ++i)
            {
                int t = order[i];
                for (int k = 0; k < 3; ++k)
                {
                    SdfVector v = vertices[indices[3 * t + k]];
                    node.x0 = Math.Min(node.x0, v.x);
                    node.y0 = Math.Min(node.y0, v.y);
                    node.z0 = Math.Min(node.z0, v.z);
                    node.x1 = Math.Max(node.x1, v.x);
                    node.y1 = Math.Max(node.y1, v.y);
                    node.z1 = Math.Max(node.z1, v.z);
                }
                cx0 = Math.Min(cx0, centroids[t].x);
                cy0 = Math.Min(cy0, centroids[t].y);
                cz0 = Math.Min(cz0, centroids[t].z);
                cx1 = Math.Max(cx1, centroids[t].x);
                cy1 = Math.Max(cy1, centroids[t].y);
                cz1 = Math.Max(cz1, centroids[t].z);
            }
            int index = nodes.Count;
            nodes.Add(node);
            if (end - begin <= 4)
            {
                node.firstTriangle = begin;
                node.triangleCount = end - begin;
                nodes[index] = node;
                return index;
            }
            // Split at the median of the centroids along the longest axis of the centroid bounds
            double ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
            int axis = ex >= ey && ex >= ez ? 0 : (ey >= ez ? 1 : 2);
            Array.Sort(order, begin, end - begin, Comparer<int>.Create((int a, int b) =>
            {
                return centroids[a][axis].CompareTo(centroids[b][axis]);
            }));
            int middle = (begin + end) / 2;
            Build(begin, middle, centroids);
            node.secondChild = Build(middle, end, centroids);
            nodes[index] = node;
            return index;
        }

        // The squared distance from p to a node's box
        private static double BoxDistanceSquared(HierarchyNode node, SdfVector p)
        {
            double dx = Math.Max(Math.Max(node.x0 - p.x, 0), p.x - node.x1);
            double dy = Math.Max(Math.Max(node.y0 - p.y, 0), p.y - node.y1);
            double dz = Math.Max(Math.Max(node.z0 - p.z, 0), p.z - node.z1);
            return dx * dx + dy * dy + dz * dz;
        }

        // The closest point of a triangle to p and the feature it lies on: 0 the face, 1..3 the vertices, 4..6 the edges ab, bc, ca
        private static SdfVector ClosestOnTriangle(SdfVector p, SdfVector a, SdfVector b, SdfVector c, out int feature)
        {
            SdfVector ab = b - a, ac = c - a, ap = p - a;
            double d1 = SdfVector.Dot(ab, ap), d2 = SdfVector.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0)
            {
                feature = 1;
                return a;
            }
            SdfVector bp = p - b;
            double d3 = SdfVector.Dot(ab, bp), d4 = SdfVector.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3)
            {
                feature = 2;
                return b;
            }
            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
            {
                feature = 4;
                return a + ab * (d1 / (d1 - d3));
            }
            SdfVector cp = p - c;
            double d5 = SdfVector.Dot(ab, cp), d6 = SdfVector.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6)
            {
                feature = 3;
                return c;
            }
            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
            {
                feature = 6;
                return a + ac * (d2 / (d2 - d6));
            }
            double va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                feature = 5;
                return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            }
            double denominator = 1 / (va + vb + vc);
            feature = 0;
            return a + ab * (vb * denominator) + ac * (vc * denominator);
        }

        public override double Evaluate(SdfVector p)
        {
            double best = double.MaxValue;
            int bestTriangle = -1, bestFeature = 0;
            SdfVector bestPoint = new SdfVector(0, 0, 0);
            int[] stack = new int[64];
            int stackSize = 0;
            stack[stackSize++] = 0;
            while (stackSize > 0)
            {
                int nodeIndex = stack[--stackSize];
                HierarchyNode node = nodes[nodeIndex];
                if (BoxDistanceSquared(node, p) >= best)
                {
                    continue;
                }
                if (node.triangleCount > 0)
                {
                    for (int i = 0; i < node.triangleCount; ++i)
                    {
                        int t = order[node.firstTriangle + i];
                        SdfVector q = ClosestOnTriangle(p, vertices[indices[3 * t]], vertices[indices[3 * t + 1]],
                                                        vertices[indices[3 * t + 2]], out int feature);
                        double distanceSquared = (q - p).LengthSquared();
                        if (distanceSquared < best)
                        {
                            best = distanceSquared;
                            bestTriangle = t;
                            bestFeature = feature;
                            bestPoint = q;
                        }
                    }
                }
                else
                {
                    // Visit the nearer child first (it is pushed last)
                    int first = nodeIndex + 1, second = node.secondChild;
                    if (BoxDistanceSquared(nodes[first], p) > BoxDistanceSquared(nodes[second], p))
                    {
                        int swap = first;
                        first = second;
                        second = swap;
                    }
                    stack[stackSize++] = second;
                    stack[stackSize++] = first;
                }
            }
            if (bestTriangle < 0)
            {
                return double.MaxValue;
            }
            SdfVector pseudonormal = faceNormals[bestTriangle];
            int i0 = indices[3 * bestTriangle], i1 = indices[3 * bestTriangle + 1], i2 = indices[3 * bestTriangle + 2];
            if (bestFeature == 1)
            {
                pseudonormal = vertexNormals[i0];
            }
            else if (bestFeature == 2)
            {
                pseudonormal = vertexNormals[i1];
            }
            else if (bestFeature == 3)
            {
                pseudonormal = vertexNormals[i2];
            }
            else if (bestFeature == 4)
            {
                pseudonormal = edgeNormals[EdgeKey(i0, i1)];
            }
            else if (bestFeature == 5)
            {
                pseudonormal = edgeNormals[EdgeKey(i1, i2)];
            }
            else if (bestFeature == 6)
            {
                pseudonormal = edgeNormals[EdgeKey(i2, i0)];
            }
            double distance = Math.Sqrt(best);
            return SdfVector.Dot(p - bestPoint, pseudonormal) < 0 ? -distance : distance;
        }

        private readonly SdfVector[] vertices;
        private readonly int[] indices;
        private readonly SdfVector[] faceNormals, vertexNormals;
        private readonly Dictionary<long, SdfVector> edgeNormals;
        private List<HierarchyNode> nodes;
        private int[] order;
    }

    // SdfGrid Definition
    public sealed class SdfGrid : SdfNode
    {
        // values holds (resolution.x + 1) * (resolution.y + 1) * (resolution.z + 1) samples at the corners of the cells of the box
        public SdfGrid(SdfVector lower, SdfVector upper, int resolutionX, int resolutionY, int resolutionZ, float[] values,
                       bool cubic)
        {
            this.lower = lower;
            this.upper = upper;
            countX = resolutionX + 1;
            countY = resolutionY + 1;
            countZ = resolutionZ + 1;
            this.values = values;
            this.cubic = cubic;
            spacing = new SdfVector((upper.x - lower.x) / resolutionX, (upper.y - lower.y) / resolutionY,
                                    (upper.z - lower.z) / resolutionZ);
        }

        // Samples a field at the grid points, in parallel
        public static SdfGrid Bake(SdfNode field, SdfVector lower, SdfVector upper, int resolutionX, int resolutionY,
                                   int resolutionZ, bool cubic)
        {
            int cx = resolutionX + 1, cy = resolutionY + 1, cz = resolutionZ + 1;
            float[] values = new float[cx * cy * cz];
            double sx = (upper.x - lower.x) / resolutionX, sy = (upper.y - lower.y) / resolutionY,
                   sz = (upper.z - lower.z) / resolutionZ;
            pbrt.ParallelFor(0, cz, (int z) =>
            {
                for (int y = 0; y < cy; ++y)
                {
                    for (int x = 0; x < cx; ++x)
                    {
                        values[x + cx * (y + cy * z)] =
                            (float)field.Evaluate(new SdfVector(lower.x + x * sx, lower.y + y * sy, lower.z + z * sz));
                    }
                }
            });
            return new SdfGrid(lower, upper, resolutionX, resolutionY, resolutionZ, values, cubic);
        }

        private double Sample(int x, int y, int z)
        {
            x = Math.Min(Math.Max(x, 0), countX - 1);
            y = Math.Min(Math.Max(y, 0), countY - 1);
            z = Math.Min(Math.Max(z, 0), countZ - 1);
            return values[x + countX * (y + countY * z)];
        }

        private static void CubicWeights(double t, double[] w)
        {
            double t2 = t * t, t3 = t2 * t;
            w[0] = -0.5 * t3 + t2 - 0.5 * t;
            w[1] = 1.5 * t3 - 2.5 * t2 + 1;
            w[2] = -1.5 * t3 + 2 * t2 + 0.5 * t;
            w[3] = 0.5 * t3 - 0.5 * t2;
        }

        public override double Evaluate(SdfVector p)
        {
            // Outside the box the field continues as the distance to the box added to the border value
            double cx = Math.Min(Math.Max(p.x, lower.x), upper.x);
            double cy = Math.Min(Math.Max(p.y, lower.y), upper.y);
            double cz = Math.Min(Math.Max(p.z, lower.z), upper.z);
            double outside = new SdfVector(p.x - cx, p.y - cy, p.z - cz).Length();
            double gx = (cx - lower.x) / spacing.x, gy = (cy - lower.y) / spacing.y, gz = (cz - lower.z) / spacing.z;
            int ix = Math.Min((int)gx, countX - 2), iy = Math.Min((int)gy, countY - 2), iz = Math.Min((int)gz, countZ - 2);
            double fx = gx - ix, fy = gy - iy, fz = gz - iz;
            double value;
            if (cubic)
            {
                double[] wx = new double[4], wy = new double[4], wz = new double[4];
                CubicWeights(fx, wx);
                CubicWeights(fy, wy);
                CubicWeights(fz, wz);
                value = 0;
                for (int k = 0; k < 4; ++k)
                {
                    double plane = 0;
                    for (int j = 0; j < 4; ++j)
                    {
                        double row = wx[0] * Sample(ix - 1, iy + j - 1, iz + k - 1) + wx[1] * Sample(ix, iy + j - 1, iz + k - 1) +
                                     wx[2] * Sample(ix + 1, iy + j - 1, iz + k - 1) + wx[3] * Sample(ix + 2, iy + j - 1, iz + k - 1);
                        plane += wy[j] * row;
                    }
                    value += wz[k] * plane;
                }
            }
            else
            {
                double c00 = Sample(ix, iy, iz) * (1 - fx) + Sample(ix + 1, iy, iz) * fx;
                double c10 = Sample(ix, iy + 1, iz) * (1 - fx) + Sample(ix + 1, iy + 1, iz) * fx;
                double c01 = Sample(ix, iy, iz + 1) * (1 - fx) + Sample(ix + 1, iy, iz + 1) * fx;
                double c11 = Sample(ix, iy + 1, iz + 1) * (1 - fx) + Sample(ix + 1, iy + 1, iz + 1) * fx;
                value = (c00 * (1 - fy) + c10 * fy) * (1 - fz) + (c01 * (1 - fy) + c11 * fy) * fz;
            }
            return value + outside;
        }

        // The reconstruction can overshoot a little between the samples of a distance
        public override double Lipschitz()
        {
            return cubic ? 1.15 : 1.0;
        }

        private readonly SdfVector lower, upper, spacing;
        private readonly int countX, countY, countZ;
        private readonly float[] values;
        private readonly bool cubic;
    }

    // SdfMeshLoader Definition
    public static class SdfMeshLoader
    {
        // Reads a PLY file and makes its signed distance field: exact for a resolution of 0 or less, otherwise baked on a grid with
        // that many cells along the longest side of the mesh box, which is padded on every side by padding times that side
        public static SdfNode Load(string filename, int resolution, double padding)
        {
            TriQuadMesh mesh = TriQuadMesh.ReadPLY(pbrt.ResolveFilename(filename));
            mesh.ConvertToOnlyTriangles();
            if (mesh.triIndices.empty())
            {
                pbrt.ErrorExit(filename + ": no triangles in the mesh.");
            }
            SdfVector[] vertices = new SdfVector[mesh.p.size()];
            for (int i = 0; i < vertices.Length; ++i)
            {
                vertices[i] = new SdfVector(mesh.p[i].x, mesh.p[i].y, mesh.p[i].z);
            }
            int[] indices = new int[mesh.triIndices.size()];
            for (int i = 0; i < indices.Length; ++i)
            {
                indices[i] = mesh.triIndices[i];
            }
            return FromTriangles(vertices, indices, resolution, padding);
        }

        public static SdfNode FromTriangles(SdfVector[] vertices, int[] indices, int resolution, double padding)
        {
            SdfMeshDistance exact = new SdfMeshDistance(vertices, indices);
            if (resolution <= 0)
            {
                return exact;
            }
            exact.VertexBounds(out SdfVector lower, out SdfVector upper);
            double side = Math.Max(upper.x - lower.x, Math.Max(upper.y - lower.y, upper.z - lower.z));
            double pad = padding * side;
            lower = new SdfVector(lower.x - pad, lower.y - pad, lower.z - pad);
            upper = new SdfVector(upper.x + pad, upper.y + pad, upper.z + pad);
            double longest = Math.Max(upper.x - lower.x, Math.Max(upper.y - lower.y, upper.z - lower.z));
            int rx = Math.Max(2, (int)Math.Ceiling(resolution * (upper.x - lower.x) / longest));
            int ry = Math.Max(2, (int)Math.Ceiling(resolution * (upper.y - lower.y) / longest));
            int rz = Math.Max(2, (int)Math.Ceiling(resolution * (upper.z - lower.z) / longest));
            return SdfGrid.Bake(exact, lower, upper, rx, ry, rz, true);
        }
    }

}  // namespace pbrt
