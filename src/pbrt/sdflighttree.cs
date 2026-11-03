// (not in pbrt-v4) sdflighttree.cs
//
// Importance sampling of the triangles that SdfSurfaceSampler cuts the surface into, for an SDF that is an area light. The triangles
// (in render space, with their measured areas and outward unit normals) are the leaves of a binary tree; every node keeps its bounds,
// its area and the cone of its normals. A sample walks down from the root and takes each child with a probability proportional to
// its importance for the receiving point p: the area of the child times the largest cosine that an emitter in the child can have
// toward p (the bounding cone of the normals widened by the angle the bounds subtend at p, folded so that either side counts, since the
// light may be two sided), over the squared distance to the bounds (Conty Estevez and Kulla 2018, without the receiver's own cosine).
// The probability of a leaf is the product along the path, so the density of any triangle at any receiving point is known, which the
// multiple importance sampling of the other strategies needs.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // SdfLightTree Definition
    public sealed class SdfLightTree
    {
        // SdfLightTree Public Methods

        // vertices holds the nine render space coordinates of each triangle, normals the three of its outward unit normal
        public SdfLightTree(double[] vertices, double[] areas, double[] normals)
        {
            this.vertices = vertices;
            this.areas = areas;
            this.normals = normals;
            int count = areas.Length;
            order = new int[count];
            SdfVector[] centers = new SdfVector[count];
            for (int t = 0; t < count; ++t)
            {
                order[t] = t;
                centers[t] = (Vertex(t, 0) + Vertex(t, 1) + Vertex(t, 2)) / 3.0;
            }
            leafOfTriangle = new int[count];
            nodes = new List<TreeNode>(2 * count);
            Build(0, count, centers, -1);
        }

        // Picks a triangle for the receiving point p from u in [0, 1): its probability, and the rest of u, again uniform in [0, 1)
        public int Sample(SdfVector p, double u, out double probability, out double remainder)
        {
            probability = 1;
            int index = 0;
            while (nodes[index].triangle < 0)
            {
                TreeNode node = nodes[index];
                double left = Importance(nodes[node.left], p), right = Importance(nodes[node.right], p);
                double sum = left + right;
                double chooseLeft = sum > 0 && !double.IsInfinity(sum) && !double.IsNaN(sum) ? left / sum : 0.5;
                if (u < chooseLeft)
                {
                    u = chooseLeft > 0 ? u / chooseLeft : 0;
                    probability *= chooseLeft;
                    index = node.left;
                }
                else
                {
                    u = chooseLeft < 1 ? (u - chooseLeft) / (1 - chooseLeft) : 0;
                    probability *= 1 - chooseLeft;
                    index = node.right;
                }
                u = Math.Min(Math.Max(u, 0), 1 - 1e-12);
            }
            remainder = u;
            return nodes[index].triangle;
        }

        // The probability that Sample picks the triangle for the receiving point p
        public double Probability(SdfVector p, int triangle)
        {
            double probability = 1;
            int index = leafOfTriangle[triangle];
            while (nodes[index].parent >= 0)
            {
                TreeNode parent = nodes[nodes[index].parent];
                double left = Importance(nodes[parent.left], p), right = Importance(nodes[parent.right], p);
                double sum = left + right;
                double chooseLeft = sum > 0 && !double.IsInfinity(sum) && !double.IsNaN(sum) ? left / sum : 0.5;
                probability *= parent.left == index ? chooseLeft : 1 - chooseLeft;
                index = nodes[index].parent;
            }
            return probability;
        }

        // The triangle closest to the point x
        public int Closest(SdfVector x)
        {
            double best = double.MaxValue;
            int bestTriangle = 0;
            Span<int> stack = stackalloc int[128];
            int stackSize = 0;
            stack[stackSize++] = 0;
            while (stackSize > 0)
            {
                int index = stack[--stackSize];
                TreeNode node = nodes[index];
                if (BoxDistanceSquared(node, x) >= best)
                {
                    continue;
                }
                if (node.triangle >= 0)
                {
                    SdfVector q = SdfMeshDistance.ClosestOnTriangle(x, Vertex(node.triangle, 0), Vertex(node.triangle, 1),
                                                                    Vertex(node.triangle, 2), out int feature);
                    double distanceSquared = (q - x).LengthSquared();
                    if (distanceSquared < best)
                    {
                        best = distanceSquared;
                        bestTriangle = node.triangle;
                    }
                }
                else
                {
                    int first = node.left, second = node.right;
                    if (BoxDistanceSquared(nodes[first], x) > BoxDistanceSquared(nodes[second], x))
                    {
                        first = node.right;
                        second = node.left;
                    }
                    stack[stackSize++] = second;
                    stack[stackSize++] = first;
                }
            }
            return bestTriangle;
        }

        // SdfLightTree Private Methods
        private struct TreeNode
        {
            public double x0, y0, z0, x1, y1, z1;
            public double ax, ay, az, coneAngle, area;
            public int left, right, parent, triangle;
        }

        private SdfVector Vertex(int triangle, int corner)
        {
            return new SdfVector(vertices[9 * triangle + 3 * corner], vertices[9 * triangle + 3 * corner + 1],
                                 vertices[9 * triangle + 3 * corner + 2]);
        }

        private int Build(int begin, int end, SdfVector[] centers, int parent)
        {
            int index = nodes.Count;
            TreeNode node = new TreeNode();
            node.parent = parent;
            node.triangle = -1;
            node.x0 = node.y0 = node.z0 = double.MaxValue;
            node.x1 = node.y1 = node.z1 = double.MinValue;
            double cx0 = double.MaxValue, cy0 = double.MaxValue, cz0 = double.MaxValue;
            double cx1 = double.MinValue, cy1 = double.MinValue, cz1 = double.MinValue;
            for (int i = begin; i < end; ++i)
            {
                int t = order[i];
                for (int k = 0; k < 3; ++k)
                {
                    SdfVector v = Vertex(t, k);
                    node.x0 = Math.Min(node.x0, v.x);
                    node.y0 = Math.Min(node.y0, v.y);
                    node.z0 = Math.Min(node.z0, v.z);
                    node.x1 = Math.Max(node.x1, v.x);
                    node.y1 = Math.Max(node.y1, v.y);
                    node.z1 = Math.Max(node.z1, v.z);
                }
                cx0 = Math.Min(cx0, centers[t].x);
                cy0 = Math.Min(cy0, centers[t].y);
                cz0 = Math.Min(cz0, centers[t].z);
                cx1 = Math.Max(cx1, centers[t].x);
                cy1 = Math.Max(cy1, centers[t].y);
                cz1 = Math.Max(cz1, centers[t].z);
            }
            nodes.Add(node);
            if (end - begin == 1)
            {
                int t = order[begin];
                node.triangle = t;
                node.area = areas[t];
                node.ax = normals[3 * t];
                node.ay = normals[3 * t + 1];
                node.az = normals[3 * t + 2];
                node.coneAngle = 0;
                nodes[index] = node;
                leafOfTriangle[t] = index;
                return index;
            }
            double ex = cx1 - cx0, ey = cy1 - cy0, ez = cz1 - cz0;
            int axis = ex >= ey && ex >= ez ? 0 : (ey >= ez ? 1 : 2);
            Array.Sort(order, begin, end - begin, Comparer<int>.Create((int a, int b) =>
            {
                return centers[a][axis].CompareTo(centers[b][axis]);
            }));
            int middle = (begin + end) / 2;
            node.left = Build(begin, middle, centers, index);
            node.right = Build(middle, end, centers, index);
            TreeNode left = nodes[node.left], right = nodes[node.right];
            node.area = left.area + right.area;
            // The mean normal and the cone that holds the normals of both children
            SdfVector mean = new SdfVector(left.area * left.ax + right.area * right.ax, left.area * left.ay + right.area * right.ay,
                                           left.area * left.az + right.area * right.az);
            if (mean.LengthSquared() > 0)
            {
                mean = mean.Normalized();
            }
            else
            {
                mean = new SdfVector(left.ax, left.ay, left.az);
            }
            node.ax = mean.x;
            node.ay = mean.y;
            node.az = mean.z;
            double angleLeft = AngleBetween(mean, new SdfVector(left.ax, left.ay, left.az)) + left.coneAngle;
            double angleRight = AngleBetween(mean, new SdfVector(right.ax, right.ay, right.az)) + right.coneAngle;
            node.coneAngle = Math.Min(Math.Max(angleLeft, angleRight), Math.PI);
            nodes[index] = node;
            return index;
        }

        private static double AngleBetween(SdfVector a, SdfVector b)
        {
            return Math.Acos(Math.Min(Math.Max(SdfVector.Dot(a, b), -1), 1));
        }

        private static double BoxDistanceSquared(TreeNode node, SdfVector p)
        {
            double dx = Math.Max(Math.Max(node.x0 - p.x, 0), p.x - node.x1);
            double dy = Math.Max(Math.Max(node.y0 - p.y, 0), p.y - node.y1);
            double dz = Math.Max(Math.Max(node.z0 - p.z, 0), p.z - node.z1);
            return dx * dx + dy * dy + dz * dz;
        }

        // The importance of a node for the receiving point p
        private static double Importance(TreeNode node, SdfVector p)
        {
            SdfVector center = new SdfVector(0.5 * (node.x0 + node.x1), 0.5 * (node.y0 + node.y1), 0.5 * (node.z0 + node.z1));
            double halfDiagonal = 0.5 * new SdfVector(node.x1 - node.x0, node.y1 - node.y0, node.z1 - node.z0).Length();
            double distanceSquared = Math.Max(BoxDistanceSquared(node, p), 0.0625 * 4 * halfDiagonal * halfDiagonal);
            SdfVector toPoint = p - center;
            double distanceToCenter = toPoint.Length();
            double cosine;
            if (distanceToCenter <= halfDiagonal || distanceToCenter == 0)
            {
                // The point is inside the bounds: any direction is possible
                cosine = 1;
            }
            else
            {
                SdfVector axis = new SdfVector(node.ax, node.ay, node.az);
                double theta = AngleBetween(axis, toPoint / distanceToCenter);
                // Either side of the surface can emit
                double folded = Math.Min(theta, Math.PI - theta);
                double boundsAngle = Math.Asin(Math.Min(halfDiagonal / distanceToCenter, 1));
                double reduced = Math.Max(0, folded - node.coneAngle - boundsAngle);
                // Keep a little probability for grazing directions
                cosine = Math.Max(Math.Cos(reduced), 0.01);
            }
            return node.area * cosine / distanceSquared;
        }

        // SdfLightTree Private Members
        private readonly double[] vertices, areas, normals;
        private readonly int[] order, leafOfTriangle;
        private readonly List<TreeNode> nodes;
    }

}  // namespace pbrt
