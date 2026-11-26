// (not in pbrt-v4) simdbvh.cs
//
// SimdBVHAggregate: a bounding volume hierarchy of 8 children a node, traversed 8 boxes at a time with AVX, after the BVH8 of
// Intel Embree (Wald, Woop, Benthin, Johnson and Ernst, "Embree: A Kernel Framework for Efficient CPU Ray Tracing", SIGGRAPH
// 2014; the single-ray traversal of Embree's bvh8 with its binned SAH builder, the configuration Embree takes for incoherent
// rays on AVX2). Written from the papers and the published design, not from Embree's source.
//
// The file is self-contained: it needs nothing of the renderer but the Primitive interface. A scene takes it with
//
//     Accelerator "simdbvh"            ("integer maxnodeprims": the most primitives of a leaf, 4; up to 16)
//
// (one line of CreateAccelerator in cpu/aggregates.cs; and with it the objects that are instanced and the primitives of a shape,
// which pbrt always puts in a BVHAggregate of their own, are in a SimdBVHAggregate too: two lines of scene.cs).
//
// The node. The boxes of the 8 children are kept by coordinate (the 8 least x, the 8 least y, ... the 8 largest z: six vectors
// of 8 floats), then the 8 children: 256 bytes, four cache lines, at an address that is a multiple of 64. A child is a node
// (its number), or a leaf (the place of its first primitive and their count), or nothing (a box that no ray meets).
//
// The traversal. The ray is tested against the 8 boxes of a node at once: for each axis the near and the far planes of the
// boxes (which of the two vectors is the near one is the sign of the ray's direction, known before the walk), three
// subtractions and three products each side, the largest of the near distances against the least of the far ones. The children
// that are met go on the stack with their distances, the nearest on top, and one that lies beyond a hit found meanwhile is not
// opened. The far distances are widened by pbrt's bound (1 + 2 gamma(3)), so a ray is never lost between two boxes.
//
// The builder. The primitives of a node are split in two by the surface area heuristic on 32 bins of their centroids along
// each axis; then the part of the largest area is split again, and so on until the node has 8 parts or none is worth
// splitting (a part of no more than "maxnodeprims" primitives is a leaf when testing them all costs less than another node).
// The parts become the children, each built the same way; those of large nodes in parallel. Past a depth of 24 the splits are
// by the median, so the depth, and with it the stack of the walk, is bounded.
//
// Without AVX the same nodes are walked one box at a time.

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace pbrt
{
    // SimdBVHAggregate Definition
    public unsafe class SimdBVHAggregate : Primitive, IDisposable
    {
        // SimdBVHAggregate Public Methods
        public SimdBVHAggregate(List<Primitive> prims, int maxPrimsInNode = 4)
        {
            this.maxPrimsInNode = Math.Max(1, Math.Min(MaxLeafPrimitives, maxPrimsInNode));
            int count = prims.Count;
            primitives = new Primitive[count];
            if (count == 0)
            {
                return;
            }
            // The boxes and the centroids of the primitives
            primitiveBounds = new Bounds3f[count];
            centroidX = new Float[count];
            centroidY = new Float[count];
            centroidZ = new Float[count];
            order = new int[count];
            ParallelFor(0, count, (int i) =>
            {
                Bounds3f b = prims[i].Bounds();
                primitiveBounds[i] = b;
                centroidX[i] = 0.5f * (b.pMin.x + b.pMax.x);
                centroidY[i] = 0.5f * (b.pMin.y + b.pMax.y);
                centroidZ[i] = 0.5f * (b.pMin.z + b.pMax.z);
                order[i] = i;
            });
            BuildRange all = Gather(0, count);
            bounds = all.bounds;

            // The tree, then its nodes in one block of memory
            BuildNode root = Build(all, 0);
            nodeCount = Count(root);
            allocation = NativeMemory.AlignedAlloc((nuint)((long)nodeCount * NodeBytes), 64);
            nodes = (byte*)allocation;
            int next = 0;
            Flatten(root, ref next);
            for (int i = 0; i < count; ++i)
            {
                primitives[i] = prims[order[i]];
            }
            primitiveBounds = null;
            centroidX = null;
            centroidY = null;
            centroidZ = null;
            order = null;
        }

        public static SimdBVHAggregate Create(List<Primitive> prims, ParameterDictionary parameters)
        {
            return new SimdBVHAggregate(prims, parameters.GetOneInt("maxnodeprims", 4));
        }

        public int NodeCount
        {
            get { return nodeCount; }
        }

        public override Bounds3f Bounds()
        {
            return bounds;
        }

        // The nearest hit: the children that the ray meets are opened nearest first, and those beyond the hit so far not at all
        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<ShapeIntersection> si = default;
            if (nodeCount == 0)
            {
                return si;
            }
            RayData data = new RayData(ray);
            int* stackNode = stackalloc int[StackSize];
            Float* stackDistance = stackalloc Float[StackSize];
            Float* distances = stackalloc Float[8];
            int top = 0;
            int current = 0;
            while (true)
            {
                if (current >= 0)
                {
                    // A node: its 8 boxes at once
                    byte* node = nodes + (long)current * NodeBytes;
                    uint mask = Hits(node, in data, tMax, distances);
                    if (mask != 0)
                    {
                        int* children = (int*)(node + ChildrenOffset);
                        if ((mask & (mask - 1)) == 0)
                        {
                            // (one child: nothing to order)
                            current = children[BitOperations.TrailingZeroCount(mask)];
                            continue;
                        }
                        // The children that are met, on the stack by their distances: the farthest first
                        int first = top;
                        while (mask != 0)
                        {
                            int child = BitOperations.TrailingZeroCount(mask);
                            mask &= mask - 1;
                            Float distance = distances[child];
                            int at = top++;
                            for (; at > first && stackDistance[at - 1] < distance; --at)
                            {
                                stackDistance[at] = stackDistance[at - 1];
                                stackNode[at] = stackNode[at - 1];
                            }
                            stackDistance[at] = distance;
                            stackNode[at] = children[child];
                        }
                        current = stackNode[--top];
                        continue;
                    }
                }
                else
                {
                    // A leaf: its primitives
                    int leaf = ~current;
                    int offset = leaf >> LeafCountBits, count = (leaf & (MaxLeafPrimitives - 1)) + 1;
                    for (int i = 0; i < count; ++i)
                    {
                        pstd.optional<ShapeIntersection> primSi = primitives[offset + i].Intersect(ray, tMax);
                        if (primSi)
                        {
                            si = primSi;
                            tMax = si.value().tHit;
                        }
                    }
                }
                // The next of the stack that is not beyond the hit
                while (true)
                {
                    if (top == 0)
                    {
                        return si;
                    }
                    --top;
                    if (stackDistance[top] <= tMax)
                    {
                        current = stackNode[top];
                        break;
                    }
                }
            }
        }

        // Any hit: the children in the order they come, and the first hit ends the walk
        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            if (nodeCount == 0)
            {
                return false;
            }
            RayData data = new RayData(ray);
            int* stackNode = stackalloc int[StackSize];
            Float* distances = stackalloc Float[8];
            int top = 0;
            int current = 0;
            while (true)
            {
                if (current >= 0)
                {
                    byte* node = nodes + (long)current * NodeBytes;
                    uint mask = Hits(node, in data, tMax, distances);
                    if (mask != 0)
                    {
                        int* children = (int*)(node + ChildrenOffset);
                        current = children[BitOperations.TrailingZeroCount(mask)];
                        mask &= mask - 1;
                        while (mask != 0)
                        {
                            stackNode[top++] = children[BitOperations.TrailingZeroCount(mask)];
                            mask &= mask - 1;
                        }
                        continue;
                    }
                }
                else
                {
                    int leaf = ~current;
                    int offset = leaf >> LeafCountBits, count = (leaf & (MaxLeafPrimitives - 1)) + 1;
                    for (int i = 0; i < count; ++i)
                    {
                        if (primitives[offset + i].IntersectP(ray, tMax))
                        {
                            return true;
                        }
                    }
                }
                if (top == 0)
                {
                    return false;
                }
                current = stackNode[--top];
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SimdBVHAggregate()
        {
            Dispose(false);
        }

        // SimdBVHAggregate Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (allocation != null)
            {
                NativeMemory.AlignedFree(allocation);
                allocation = null;
                nodes = null;
                nodeCount = 0;
            }
        }

        // What of the ray the boxes are tested with: its origin and the inverse of its direction, 8 times each, and for each
        // axis which of the node's vectors hold the near planes and which the far
        private readonly struct RayData
        {
            public RayData(Ray ray)
            {
                // (a direction of zero along an axis: the least that has an inverse, so that no product is undefined)
                Float dx = Math.Abs(ray.d.x) < TinyDirection ? (ray.d.x < 0 ? -TinyDirection : TinyDirection) : ray.d.x;
                Float dy = Math.Abs(ray.d.y) < TinyDirection ? (ray.d.y < 0 ? -TinyDirection : TinyDirection) : ray.d.y;
                Float dz = Math.Abs(ray.d.z) < TinyDirection ? (ray.d.z < 0 ? -TinyDirection : TinyDirection) : ray.d.z;
                ox = ray.o.x;
                oy = ray.o.y;
                oz = ray.o.z;
                ix = 1 / dx;
                iy = 1 / dy;
                iz = 1 / dz;
                originX = Vector256.Create(ox);
                originY = Vector256.Create(oy);
                originZ = Vector256.Create(oz);
                inverseX = Vector256.Create(ix);
                inverseY = Vector256.Create(iy);
                inverseZ = Vector256.Create(iz);
                nearX = dx < 0 ? MaxXOffset : MinXOffset;
                nearY = dy < 0 ? MaxYOffset : MinYOffset;
                nearZ = dz < 0 ? MaxZOffset : MinZOffset;
                farX = MinXOffset + MaxXOffset - nearX;
                farY = MinYOffset + MaxYOffset - nearY;
                farZ = MinZOffset + MaxZOffset - nearZ;
            }

            public readonly Float ox, oy, oz, ix, iy, iz;
            public readonly Vector256<Float> originX, originY, originZ, inverseX, inverseY, inverseZ;
            public readonly int nearX, nearY, nearZ, farX, farY, farZ;
        }

        // The children of the node whose boxes the ray meets before tMax, as bits, and the distances at which it enters them
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Hits(byte* node, in RayData ray, Float tMax, Float* distances)
        {
            if (Avx.IsSupported)
            {
                Vector256<Float> nearX = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.nearX)), ray.originX), ray.inverseX);
                Vector256<Float> nearY = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.nearY)), ray.originY), ray.inverseY);
                Vector256<Float> nearZ = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.nearZ)), ray.originZ), ray.inverseZ);
                Vector256<Float> farX = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.farX)), ray.originX), ray.inverseX);
                Vector256<Float> farY = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.farY)), ray.originY), ray.inverseY);
                Vector256<Float> farZ = Avx.Multiply(Avx.Subtract(Avx.LoadAlignedVector256((Float*)(node + ray.farZ)), ray.originZ), ray.inverseZ);
                Vector256<Float> near = Avx.Max(Avx.Max(nearX, nearY), Avx.Max(nearZ, Vector256<Float>.Zero));
                Vector256<Float> far = Avx.Min(Avx.Multiply(Avx.Min(Avx.Min(farX, farY), farZ), Vector256.Create(FarWidening)), Vector256.Create(tMax));
                Avx.Store(distances, near);
                return (uint)Avx.MoveMask(Avx.CompareLessThanOrEqual(near, far));
            }
            // (without AVX: the same test, a box at a time)
            uint mask = 0;
            for (int i = 0; i < 8; ++i)
            {
                Float near = Math.Max(Math.Max((((Float*)(node + ray.nearX))[i] - ray.ox) * ray.ix, (((Float*)(node + ray.nearY))[i] - ray.oy) * ray.iy),
                                      Math.Max((((Float*)(node + ray.nearZ))[i] - ray.oz) * ray.iz, 0));
                Float far = Math.Min(Math.Min((((Float*)(node + ray.farX))[i] - ray.ox) * ray.ix, (((Float*)(node + ray.farY))[i] - ray.oy) * ray.iy),
                                     (((Float*)(node + ray.farZ))[i] - ray.oz) * ray.iz);
                far = Math.Min(far * FarWidening, tMax);
                distances[i] = near;
                mask |= near <= far ? 1u << i : 0u;
            }
            return mask;
        }

        // A run of the primitives (in `order`), with the box of their boxes and that of their centroids
        private struct BuildRange
        {
            public int first, count;
            public Bounds3f bounds, centroidBounds;
            // (not to be split again: a leaf)
            public bool leaf;
        }

        // A node as it is built: its children are ranges, each a leaf or a node
        private sealed class BuildNode
        {
            public int childCount;
            public BuildRange[] ranges = new BuildRange[8];
            public BuildNode[] children = new BuildNode[8];
        }

        private static Float HalfArea(Bounds3f b)
        {
            Vector3f d = b.pMax - b.pMin;
            return d.x * d.y + d.y * d.z + d.z * d.x;
        }

        // The range of `count` primitives from `first`: their boxes gathered
        private BuildRange Gather(int first, int count)
        {
            Float minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
            Float cMinX = Infinity, cMinY = Infinity, cMinZ = Infinity, cMaxX = -Infinity, cMaxY = -Infinity, cMaxZ = -Infinity;
            for (int i = first; i < first + count; ++i)
            {
                int p = order[i];
                Bounds3f b = primitiveBounds[p];
                minX = Math.Min(minX, b.pMin.x);
                minY = Math.Min(minY, b.pMin.y);
                minZ = Math.Min(minZ, b.pMin.z);
                maxX = Math.Max(maxX, b.pMax.x);
                maxY = Math.Max(maxY, b.pMax.y);
                maxZ = Math.Max(maxZ, b.pMax.z);
                cMinX = Math.Min(cMinX, centroidX[p]);
                cMinY = Math.Min(cMinY, centroidY[p]);
                cMinZ = Math.Min(cMinZ, centroidZ[p]);
                cMaxX = Math.Max(cMaxX, centroidX[p]);
                cMaxY = Math.Max(cMaxY, centroidY[p]);
                cMaxZ = Math.Max(cMaxZ, centroidZ[p]);
            }
            BuildRange range = new BuildRange();
            range.first = first;
            range.count = count;
            range.bounds = new Bounds3f(new Point3f(minX, minY, minZ), new Point3f(maxX, maxY, maxZ));
            range.centroidBounds = new Bounds3f(new Point3f(cMinX, cMinY, cMinZ), new Point3f(cMaxX, cMaxY, cMaxZ));
            return range;
        }

        // The range in two, by the surface area heuristic on bins of the centroids along the best axis (or by the median, where
        // the heuristic has nothing to go by or the tree is deep). False where the range is better left a leaf.
        private bool Split(BuildRange range, int depth, out BuildRange left, out BuildRange right)
        {
            left = default;
            right = default;
            if (range.count < 2)
            {
                return false;
            }
            int bestAxis = -1, bestBin = 0;
            Float bestCost = Infinity;
            Float* areas = stackalloc Float[Bins];
            int* counts = stackalloc int[Bins];
            Float* binBounds = stackalloc Float[6 * Bins];
            if (depth <= MedianDepth)
            {
                for (int axis = 0; axis < 3; ++axis)
                {
                    Float low = range.centroidBounds.pMin[axis], extent = range.centroidBounds.pMax[axis] - low;
                    if (!(extent > 0))
                    {
                        continue;
                    }
                    Float[] centroid = axis == 0 ? centroidX : axis == 1 ? centroidY : centroidZ;
                    Float scale = Bins * (1 - 1e-6f) / extent;
                    for (int b = 0; b < Bins; ++b)
                    {
                        counts[b] = 0;
                        binBounds[6 * b] = Infinity;
                        binBounds[6 * b + 1] = Infinity;
                        binBounds[6 * b + 2] = Infinity;
                        binBounds[6 * b + 3] = -Infinity;
                        binBounds[6 * b + 4] = -Infinity;
                        binBounds[6 * b + 5] = -Infinity;
                    }
                    for (int i = range.first; i < range.first + range.count; ++i)
                    {
                        int p = order[i];
                        int b = Math.Min(Bins - 1, (int)((centroid[p] - low) * scale));
                        Bounds3f box = primitiveBounds[p];
                        ++counts[b];
                        binBounds[6 * b] = Math.Min(binBounds[6 * b], box.pMin.x);
                        binBounds[6 * b + 1] = Math.Min(binBounds[6 * b + 1], box.pMin.y);
                        binBounds[6 * b + 2] = Math.Min(binBounds[6 * b + 2], box.pMin.z);
                        binBounds[6 * b + 3] = Math.Max(binBounds[6 * b + 3], box.pMax.x);
                        binBounds[6 * b + 4] = Math.Max(binBounds[6 * b + 4], box.pMax.y);
                        binBounds[6 * b + 5] = Math.Max(binBounds[6 * b + 5], box.pMax.z);
                    }
                    // From the right: the area times the count of the bins from b on
                    Float minX = Infinity, minY = Infinity, minZ = Infinity, maxX = -Infinity, maxY = -Infinity, maxZ = -Infinity;
                    int running = 0;
                    for (int b = Bins - 1; b > 0; --b)
                    {
                        if (counts[b] > 0)
                        {
                            minX = Math.Min(minX, binBounds[6 * b]);
                            minY = Math.Min(minY, binBounds[6 * b + 1]);
                            minZ = Math.Min(minZ, binBounds[6 * b + 2]);
                            maxX = Math.Max(maxX, binBounds[6 * b + 3]);
                            maxY = Math.Max(maxY, binBounds[6 * b + 4]);
                            maxZ = Math.Max(maxZ, binBounds[6 * b + 5]);
                            running += counts[b];
                        }
                        areas[b] = running > 0 ? running * ((maxX - minX) * (maxY - minY) + (maxY - minY) * (maxZ - minZ) + (maxZ - minZ) * (maxX - minX)) : Infinity;
                    }
                    // From the left: the split before the bin b
                    minX = Infinity;
                    minY = Infinity;
                    minZ = Infinity;
                    maxX = -Infinity;
                    maxY = -Infinity;
                    maxZ = -Infinity;
                    running = 0;
                    for (int b = 1; b < Bins; ++b)
                    {
                        if (counts[b - 1] > 0)
                        {
                            minX = Math.Min(minX, binBounds[6 * (b - 1)]);
                            minY = Math.Min(minY, binBounds[6 * (b - 1) + 1]);
                            minZ = Math.Min(minZ, binBounds[6 * (b - 1) + 2]);
                            maxX = Math.Max(maxX, binBounds[6 * (b - 1) + 3]);
                            maxY = Math.Max(maxY, binBounds[6 * (b - 1) + 4]);
                            maxZ = Math.Max(maxZ, binBounds[6 * (b - 1) + 5]);
                            running += counts[b - 1];
                        }
                        if (running == 0 || running == range.count)
                        {
                            continue;
                        }
                        Float cost = running * ((maxX - minX) * (maxY - minY) + (maxY - minY) * (maxZ - minZ) + (maxZ - minZ) * (maxX - minX)) + areas[b];
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            bestAxis = axis;
                            bestBin = b;
                        }
                    }
                }
            }
            // A leaf, where its primitives cost less to test than another node and its children (a node the cost of a primitive)
            Float area = HalfArea(range.bounds);
            if (range.count <= maxPrimsInNode && (bestAxis < 0 || !(area > 0) || range.count <= 1 + bestCost / area))
            {
                return false;
            }
            int middle;
            if (bestAxis >= 0)
            {
                // The primitives of the bins before the split to the front
                Float[] centroid = bestAxis == 0 ? centroidX : bestAxis == 1 ? centroidY : centroidZ;
                Float low = range.centroidBounds.pMin[bestAxis], scale = Bins * (1 - 1e-6f) / (range.centroidBounds.pMax[bestAxis] - low);
                int i = range.first, j = range.first + range.count - 1;
                while (i <= j)
                {
                    if (Math.Min(Bins - 1, (int)((centroid[order[i]] - low) * scale)) < bestBin)
                    {
                        ++i;
                    }
                    else
                    {
                        int swap = order[i];
                        order[i] = order[j];
                        order[j] = swap;
                        --j;
                    }
                }
                middle = i;
            }
            else
            {
                // The median along the widest axis of the centroids (or of the order they are in, where they are all one point)
                int axis = range.centroidBounds.MaxDimension();
                if (range.centroidBounds.pMax[axis] > range.centroidBounds.pMin[axis])
                {
                    Float[] centroid = axis == 0 ? centroidX : axis == 1 ? centroidY : centroidZ;
                    Float[] keys = new Float[range.count];
                    for (int i = 0; i < range.count; ++i)
                    {
                        keys[i] = centroid[order[range.first + i]];
                    }
                    Array.Sort(keys, order, range.first, range.count);
                }
                middle = range.first + range.count / 2;
            }
            left = Gather(range.first, middle - range.first);
            right = Gather(middle, range.first + range.count - middle);
            return true;
        }

        // The node of a range: the range in two, then the part of the largest area in two, until there are 8 parts or none is
        // to be split; and the children of the parts
        private BuildNode Build(BuildRange range, int depth)
        {
            BuildNode node = new BuildNode();
            node.ranges[0] = range;
            node.childCount = 1;
            while (node.childCount < 8)
            {
                int best = -1;
                Float largest = -1;
                for (int i = 0; i < node.childCount; ++i)
                {
                    if (!node.ranges[i].leaf && HalfArea(node.ranges[i].bounds) > largest)
                    {
                        largest = HalfArea(node.ranges[i].bounds);
                        best = i;
                    }
                }
                if (best < 0)
                {
                    break;
                }
                if (Split(node.ranges[best], depth, out BuildRange left, out BuildRange right))
                {
                    node.ranges[best] = left;
                    node.ranges[node.childCount++] = right;
                }
                else
                {
                    node.ranges[best].leaf = true;
                }
            }
            // (a part that was not tried, the node being full: is it a leaf?)
            for (int i = 0; i < node.childCount; ++i)
            {
                if (!node.ranges[i].leaf && !Split(node.ranges[i], depth + 1, out BuildRange left, out BuildRange right))
                {
                    node.ranges[i].leaf = true;
                }
            }
            if (range.count >= ParallelCount)
            {
                ParallelFor(0, node.childCount, (int i) =>
                {
                    if (!node.ranges[i].leaf)
                    {
                        node.children[i] = Build(node.ranges[i], depth + 1);
                    }
                });
            }
            else
            {
                for (int i = 0; i < node.childCount; ++i)
                {
                    if (!node.ranges[i].leaf)
                    {
                        node.children[i] = Build(node.ranges[i], depth + 1);
                    }
                }
            }
            return node;
        }

        private static int Count(BuildNode node)
        {
            int count = 1;
            for (int i = 0; i < node.childCount; ++i)
            {
                if (node.children[i] != null)
                {
                    count += Count(node.children[i]);
                }
            }
            return count;
        }

        // The node and those below it into the block, a node before its children; the number it got
        private int Flatten(BuildNode node, ref int next)
        {
            int index = next++;
            Float* boxes = (Float*)(nodes + (long)index * NodeBytes);
            int* children = (int*)(nodes + (long)index * NodeBytes + ChildrenOffset);
            for (int i = 0; i < 8; ++i)
            {
                if (i < node.childCount)
                {
                    Bounds3f b = node.ranges[i].bounds;
                    boxes[i] = b.pMin.x;
                    boxes[8 + i] = b.pMin.y;
                    boxes[16 + i] = b.pMin.z;
                    boxes[24 + i] = b.pMax.x;
                    boxes[32 + i] = b.pMax.y;
                    boxes[40 + i] = b.pMax.z;
                }
                else
                {
                    // (no child: a box that is inside out, which no ray meets)
                    boxes[i] = Infinity;
                    boxes[8 + i] = Infinity;
                    boxes[16 + i] = Infinity;
                    boxes[24 + i] = -Infinity;
                    boxes[32 + i] = -Infinity;
                    boxes[40 + i] = -Infinity;
                }
                children[i] = 0;
                // (the rest of the 256 bytes)
                children[8 + i] = 0;
            }
            for (int i = 0; i < node.childCount; ++i)
            {
                if (node.children[i] != null)
                {
                    int child = Flatten(node.children[i], ref next);
                    children[i] = child;
                }
                else
                {
                    children[i] = ~((node.ranges[i].first << LeafCountBits) | (node.ranges[i].count - 1));
                }
            }
            return index;
        }

        // SimdBVHAggregate Private Members
        // A node: 6 vectors of 8 floats (the least x, y, z and the largest x, y, z of its 8 children), then its 8 children
        private const int NodeBytes = 256;
        private const int MinXOffset = 0, MinYOffset = 32, MinZOffset = 64, MaxXOffset = 96, MaxYOffset = 128, MaxZOffset = 160;
        private const int ChildrenOffset = 192;
        // A leaf: ~((first << 4) | (count - 1))
        private const int LeafCountBits = 4;
        private const int MaxLeafPrimitives = 1 << LeafCountBits;
        private const int Bins = 32;
        // (below this depth the splits are by the median: at most 31 levels more, each of which leaves 7 on the stack)
        private const int MedianDepth = 24;
        private const int StackSize = 7 * (MedianDepth + 34) + 8;
        private const int ParallelCount = 16384;
        private const Float TinyDirection = 1e-30f;
        // 1 + 2 gamma(3), pbrt's bound of the rounding of the distances to a box
        private const Float FarWidening = 1.0000004f;

        private readonly Primitive[] primitives;
        private readonly int maxPrimsInNode;
        private readonly Bounds3f bounds;
        private void* allocation;
        private byte* nodes;
        private int nodeCount;
        // (while it is built)
        private Bounds3f[] primitiveBounds;
        private Float[] centroidX, centroidY, centroidZ;
        private int[] order;
    }
}
