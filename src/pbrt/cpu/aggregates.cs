// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cpu/aggregates.h / cpu/aggregates.cpp

using System;
using System.Collections.Generic;
using System.Threading;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        public static Primitive CreateAccelerator(string name, List<Primitive> prims,
                                                  ParameterDictionary parameters)
        {
            Primitive accel = null;
            if (name == "bvh")
                accel = BVHAggregate.Create(prims, parameters);
            else if (name == "kdtree")
                accel = KdTreeAggregate.Create(prims, parameters);
            else
                ErrorExit(StringPrintf("%s: accelerator type unknown.", name));

            if (accel == null)
                ErrorExit(StringPrintf("%s: unable to create accelerator.", name));

            parameters.ReportUnused();
            return accel;
        }
    }


    // BVHAggregate Definition
    public sealed unsafe class BVHAggregate : Primitive
    {
        // STAT_MEMORY_COUNTER("Memory/BVH", treeBytes);
        // STAT_RATIO("BVH/Primitives per leaf node", totalPrimitives, totalLeafNodes);
        // STAT_COUNTER("BVH/Interior nodes", interiorNodes);
        // STAT_COUNTER("BVH/Leaf nodes", leafNodes);
        // STAT_PIXEL_COUNTER("BVH/Nodes visited", bvhNodesVisited);
        [ThreadStatic] static long treeBytes, totalPrimitives, totalLeafNodes, interiorNodes, leafNodes;
        [ThreadStatic] static long bvhNodesVisited, bvhNodesVisitedSum;
        static StatRegisterer BVH_PIXEL_REG = new StatRegisterer(
            (StatsAccumulator accum) =>
            {
                // report sum, since if disabled, it all just goes into var...
                accum.ReportCounter("BVH/Nodes visited", bvhNodesVisited + bvhNodesVisitedSum);
                bvhNodesVisitedSum = 0;
                bvhNodesVisited = 0;
            },
            (Point2i p, int counterIndex, PixelStatsAccumulator accum) =>
            {
                accum.ReportCounter(p, counterIndex, "BVH/Nodes visited", bvhNodesVisited);
                bvhNodesVisitedSum += bvhNodesVisited;
                bvhNodesVisited = 0;
            });
        static StatRegisterer STATS_REG = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/BVH", treeBytes);
            treeBytes = 0;
            accum.ReportRatio("BVH/Primitives per leaf node", totalPrimitives, totalLeafNodes);
            totalPrimitives = totalLeafNodes = 0;
            accum.ReportCounter("BVH/Interior nodes", interiorNodes);
            interiorNodes = 0;
            accum.ReportCounter("BVH/Leaf nodes", leafNodes);
            leafNodes = 0;
        });

        // MortonPrimitive Definition
        struct MortonPrimitive
        {
            public int primitiveIndex;
            public uint32_t mortonCode;
        }

        // LBVHTreelet Definition
        sealed class LBVHTreelet
        {
            public size_t startIndex, nPrimitives;
            public BVHBuildNode[] buildNodesArray;  // storage for the treelet's nodes
            public BVHBuildNode buildNodes;         // root of the emitted treelet
        }

        // BVHSplitBucket Definition
        struct BVHSplitBucket
        {
            public int count;
            public Bounds3f bounds;
        }

        // BVHPrimitive Definition
        struct BVHPrimitive
        {
            public BVHPrimitive(size_t primitiveIndex, Bounds3f bounds)
            {
                this.primitiveIndex = primitiveIndex;
                this.bounds = bounds;
            }
            public size_t primitiveIndex;
            public Bounds3f bounds;
            // BVHPrimitive Public Methods
            public Point3f Centroid() { return .5f * bounds.pMin + .5f * bounds.pMax; }
        }

        // BVHBuildNode Definition
        sealed class BVHBuildNode
        {
            // BVHBuildNode Public Methods
            public void InitLeaf(int first, int n, Bounds3f b)
            {
                firstPrimOffset = first;
                nPrimitives = n;
                bounds = b;
                children[0] = children[1] = null;
                ++leafNodes;
                ++totalLeafNodes;
                totalPrimitives += n;
            }

            public void InitInterior(int axis, BVHBuildNode c0, BVHBuildNode c1)
            {
                children[0] = c0;
                children[1] = c1;
                bounds = Union(c0.bounds, c1.bounds);
                splitAxis = axis;
                nPrimitives = 0;
                ++interiorNodes;
            }

            public Bounds3f bounds = new Bounds3f();
            public BVHBuildNode[] children = new BVHBuildNode[2];
            public int splitAxis, firstPrimOffset, nPrimitives;
        }

        // LinearBVHNode Definition (alignas(32) in C++)
        struct LinearBVHNode
        {
            public Bounds3f bounds;
            // union { int primitivesOffset; int secondChildOffset; }
            public int primitivesOffset;   // leaf
            public int secondChildOffset;  // interior
            public uint16_t nPrimitives;  // 0 -> interior node
            public uint8_t axis;          // interior node: xyz
        }

        // std::atomic<int> replacement
        sealed class AtomicInt
        {
            public int v;
            public int fetch_add(int n) { return Interlocked.Add(ref v, n) - n; }
            public int load() { return Volatile.Read(ref v); }
        }

        // BVHAggregate Public Types
        public enum SplitMethod { SAH, HLBVH, Middle, EqualCounts }

        // BVHAggregate Utility Functions
        static void RadixSort(ref MortonPrimitive[] v)
        {
            MortonPrimitive[] tempVector = new MortonPrimitive[v.Length];
            const int bitsPerPass = 6;
            const int nBits = 30;
            // static_assert((nBits % bitsPerPass) == 0, "Radix sort bitsPerPass must evenly divide nBits");
            const int nPasses = nBits / bitsPerPass;
            for (int pass = 0; pass < nPasses; ++pass)
            {
                // Perform one pass of radix sort, sorting _bitsPerPass_ bits
                int lowBit = pass * bitsPerPass;
                // Set in and out vector references for radix sort pass
                MortonPrimitive[] @in = ((pass & 1) != 0) ? tempVector : v;
                MortonPrimitive[] @out = ((pass & 1) != 0) ? v : tempVector;

                // Count number of zero bits in array for current radix sort bit
                const int nBuckets = 1 << bitsPerPass;
                int* bucketCount = stackalloc int[nBuckets];
                for (int i = 0; i < nBuckets; ++i)
                    bucketCount[i] = 0;
                const int bitMask = (1 << bitsPerPass) - 1;
                foreach (MortonPrimitive mp in @in)
                {
                    int bucket = (int)((mp.mortonCode >> lowBit) & bitMask);
                    CHECK_GE(bucket, 0);
                    CHECK_LT(bucket, nBuckets);
                    ++bucketCount[bucket];
                }

                // Compute starting index in output array for each bucket
                int* outIndex = stackalloc int[nBuckets];
                outIndex[0] = 0;
                for (int i = 1; i < nBuckets; ++i)
                    outIndex[i] = outIndex[i - 1] + bucketCount[i - 1];

                // Store sorted values in output array
                foreach (MortonPrimitive mp in @in)
                {
                    int bucket = (int)((mp.mortonCode >> lowBit) & bitMask);
                    @out[outIndex[bucket]++] = mp;
                }
            }
            // Copy final result from _tempVector_, if needed
            if ((nPasses & 1) != 0)
            {
                MortonPrimitive[] t = v;
                v = tempVector;
                tempVector = t;
            }
        }

        // std::partition (bidirectional iterator algorithm, as in libstdc++)
        static int partition(BVHPrimitive[] arr, int first, int last, Func<BVHPrimitive, bool> pred)
        {
            while (true)
            {
                while (true)
                {
                    if (first == last) return first;
                    else if (pred(arr[first])) ++first;
                    else break;
                }
                --last;
                while (true)
                {
                    if (first == last) return first;
                    else if (!pred(arr[last])) --last;
                    else break;
                }
                BVHPrimitive tmp = arr[first];
                arr[first] = arr[last];
                arr[last] = tmp;
                ++first;
            }
        }

        static int partition(BVHBuildNode[] arr, int first, int last, Func<BVHBuildNode, bool> pred)
        {
            while (true)
            {
                while (true)
                {
                    if (first == last) return first;
                    else if (pred(arr[first])) ++first;
                    else break;
                }
                --last;
                while (true)
                {
                    if (first == last) return first;
                    else if (!pred(arr[last])) --last;
                    else break;
                }
                BVHBuildNode tmp = arr[first];
                arr[first] = arr[last];
                arr[last] = tmp;
                ++first;
            }
        }

        // std::nth_element, following libstdc++'s introselect on an array range [first, last).
        // (If the depth limit is exhausted, the range is fully sorted instead of heap-selected.)
        static void nth_element(BVHPrimitive[] a, int first, int nth, int last, Func<BVHPrimitive, BVHPrimitive, bool> comp)
        {
            if (first == last || nth == last)
                return;
            int depthLimit = (31 - System.Numerics.BitOperations.LeadingZeroCount((uint)(last - first))) * 2;
            while (last - first > 3)
            {
                if (depthLimit == 0)
                {
                    Array.Sort(a, first, last - first, Comparer<BVHPrimitive>.Create((x, y) => comp(x, y) ? -1 : (comp(y, x) ? 1 : 0)));
                    return;
                }
                --depthLimit;
                // __unguarded_partition_pivot
                int mid = first + (last - first) / 2;
                // __move_median_to_first(first, first + 1, mid, last - 1)
                int A = first + 1, B = mid, C = last - 1;
                if (comp(a[A], a[B]))
                {
                    if (comp(a[B], a[C])) swapAt(a, first, B);
                    else if (comp(a[A], a[C])) swapAt(a, first, C);
                    else swapAt(a, first, A);
                }
                else if (comp(a[A], a[C])) swapAt(a, first, A);
                else if (comp(a[B], a[C])) swapAt(a, first, C);
                else swapAt(a, first, B);
                // __unguarded_partition(first + 1, last, first)
                int lo = first + 1, hi = last;
                int cut;
                while (true)
                {
                    while (comp(a[lo], a[first])) ++lo;
                    --hi;
                    while (comp(a[first], a[hi])) --hi;
                    if (!(lo < hi)) { cut = lo; break; }
                    swapAt(a, lo, hi);
                    ++lo;
                }
                if (cut <= nth) first = cut; else last = cut;
            }
            // __insertion_sort
            for (int i = first + 1; i < last; ++i)
            {
                BVHPrimitive val = a[i];
                int j = i;
                while (j > first && comp(val, a[j - 1]))
                {
                    a[j] = a[j - 1];
                    --j;
                }
                a[j] = val;
            }
        }

        static void swapAt(BVHPrimitive[] a, int i, int j)
        {
            BVHPrimitive t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        // BVHAggregate Method Definitions
        public BVHAggregate(List<Primitive> prims, int maxPrimsInNode = 1,
                            SplitMethod splitMethod = SplitMethod.SAH)
        {
            this.maxPrimsInNode = std.min(255, maxPrimsInNode);
            this.primitives = prims.ToArray();
            this.splitMethod = splitMethod;
            CHECK(primitives.Length != 0);
            // Build BVH from _primitives_
            // Initialize _bvhPrimitives_ array for primitives
            BVHPrimitive[] bvhPrimitives = new BVHPrimitive[primitives.Length];
            for (int i = 0; i < primitives.Length; ++i)
                bvhPrimitives[i] = new BVHPrimitive((size_t)i, primitives[i].Bounds());

            // Build BVH for primitives using _bvhPrimitives_
            // (Allocators are GC placeholders.)
            Allocator alloc = null;

            Primitive[] orderedPrims = new Primitive[primitives.Length];
            BVHBuildNode root;
            // Build BVH according to selected _splitMethod_
            AtomicInt totalNodes = new AtomicInt();
            if (splitMethod == SplitMethod.HLBVH)
            {
                root = buildHLBVH(alloc, bvhPrimitives, totalNodes, orderedPrims);
            }
            else
            {
                AtomicInt orderedPrimsOffset = new AtomicInt();
                root = buildRecursive(new pstd.span<BVHPrimitive>(bvhPrimitives),
                                      totalNodes, orderedPrimsOffset, orderedPrims);
                CHECK_EQ(orderedPrimsOffset.load(), orderedPrims.Length);
            }
            primitives = orderedPrims;

            // Convert BVH into compact representation in _nodes_ array
            bvhPrimitives = null;
            LOG_VERBOSE("BVH created with %d nodes for %d primitives (%.2f MB)",
                        totalNodes.load(), (int)primitives.Length,
                        (float)(totalNodes.load() * LinearBVHNodeBytes) / (1024.0f * 1024.0f));
            treeBytes += totalNodes.load() * LinearBVHNodeBytes + primitives.Length * 8;
            nodes = new LinearBVHNode[totalNodes.load()];
            int offset = 0;
            flattenBVH(root, ref offset);
            CHECK_EQ(totalNodes.load(), offset);
        }

        const int LinearBVHNodeBytes = 32;

        BVHBuildNode buildRecursive(pstd.span<BVHPrimitive> bvhPrimitives,
                                    AtomicInt totalNodes, AtomicInt orderedPrimsOffset,
                                    Primitive[] orderedPrims)
        {
            DCHECK_NE((int)bvhPrimitives.size(), 0);
            BVHBuildNode node = new BVHBuildNode();
            // Initialize _BVHBuildNode_ for primitive range
            Interlocked.Increment(ref totalNodes.v);
            // Compute bounds of all primitives in BVH node
            Bounds3f bounds = new Bounds3f();
            for (int i = 0; i < bvhPrimitives.length; ++i)
                bounds = Union(bounds, bvhPrimitives[i].bounds);

            if (bounds.SurfaceArea() == 0 || bvhPrimitives.size() == 1)
            {
                // Create leaf _BVHBuildNode_
                int firstPrimOffset = orderedPrimsOffset.fetch_add((int)bvhPrimitives.size());
                for (int i = 0; i < bvhPrimitives.length; ++i)
                {
                    int index = (int)bvhPrimitives[i].primitiveIndex;
                    orderedPrims[firstPrimOffset + i] = primitives[index];
                }
                node.InitLeaf(firstPrimOffset, (int)bvhPrimitives.size(), bounds);
                return node;
            }
            else
            {
                // Compute bound of primitive centroids and choose split dimension _dim_
                Bounds3f centroidBounds = new Bounds3f();
                for (int i = 0; i < bvhPrimitives.length; ++i)
                    centroidBounds = Union(centroidBounds, bvhPrimitives[i].Centroid());
                int dim = centroidBounds.MaxDimension();

                // Partition primitives into two sets and build children
                if (centroidBounds.pMax[dim] == centroidBounds.pMin[dim])
                {
                    // Create leaf _BVHBuildNode_
                    int firstPrimOffset = orderedPrimsOffset.fetch_add((int)bvhPrimitives.size());
                    for (int i = 0; i < bvhPrimitives.length; ++i)
                    {
                        int index = (int)bvhPrimitives[i].primitiveIndex;
                        orderedPrims[firstPrimOffset + i] = primitives[index];
                    }
                    node.InitLeaf(firstPrimOffset, (int)bvhPrimitives.size(), bounds);
                    return node;
                }
                else
                {
                    int mid = (int)bvhPrimitives.size() / 2;
                    BVHPrimitive[] arr = bvhPrimitives.arr;
                    int begin = bvhPrimitives.offset, end = bvhPrimitives.offset + bvhPrimitives.length;
                    // Partition primitives based on _splitMethod_
                    switch (splitMethod)
                    {
                        case SplitMethod.Middle:
                            {
                                // Partition primitives through node's midpoint
                                Float pmid = (centroidBounds.pMin[dim] + centroidBounds.pMax[dim]) / 2;
                                int midIter = partition(arr, begin, end, (BVHPrimitive pi) => pi.Centroid()[dim] < pmid);
                                mid = midIter - begin;
                                // For lots of prims with large overlapping bounding boxes, this
                                // may fail to partition; in that case do not break and fall through
                                // to EqualCounts.
                                if (midIter != begin && midIter != end)
                                    break;
                                goto case SplitMethod.EqualCounts;
                            }
                        case SplitMethod.EqualCounts:
                            {
                                // Partition primitives into equally sized subsets
                                mid = (int)bvhPrimitives.size() / 2;
                                nth_element(arr, begin, begin + mid, end,
                                            (BVHPrimitive a, BVHPrimitive b) => a.Centroid()[dim] < b.Centroid()[dim]);

                                break;
                            }
                        case SplitMethod.SAH:
                        default:
                            {
                                // Partition primitives using approximate SAH
                                if (bvhPrimitives.size() <= 2)
                                {
                                    // Partition primitives into equally sized subsets
                                    mid = (int)bvhPrimitives.size() / 2;
                                    nth_element(arr, begin, begin + mid, end,
                                                (BVHPrimitive a, BVHPrimitive b) => a.Centroid()[dim] < b.Centroid()[dim]);
                                }
                                else
                                {
                                    // Allocate _BVHSplitBucket_ for SAH partition buckets
                                    const int nBuckets = 12;
                                    BVHSplitBucket[] buckets = new BVHSplitBucket[nBuckets];
                                    for (int i = 0; i < nBuckets; ++i)
                                        buckets[i].bounds = new Bounds3f();

                                    // Initialize _BVHSplitBucket_ for SAH partition buckets
                                    for (int pi = 0; pi < bvhPrimitives.length; ++pi)
                                    {
                                        BVHPrimitive prim = bvhPrimitives[pi];
                                        int b = (int)(nBuckets * centroidBounds.Offset(prim.Centroid())[dim]);
                                        if (b == nBuckets)
                                            b = nBuckets - 1;
                                        DCHECK_GE(b, 0);
                                        DCHECK_LT(b, nBuckets);
                                        buckets[b].count++;
                                        buckets[b].bounds = Union(buckets[b].bounds, prim.bounds);
                                    }

                                    // Compute costs for splitting after each bucket
                                    const int nSplits = nBuckets - 1;
                                    Float[] costs = new Float[nSplits];
                                    // Partially initialize _costs_ using a forward scan over splits
                                    int countBelow = 0;
                                    Bounds3f boundBelow = new Bounds3f();
                                    for (int i = 0; i < nSplits; ++i)
                                    {
                                        boundBelow = Union(boundBelow, buckets[i].bounds);
                                        countBelow += buckets[i].count;
                                        costs[i] += countBelow * boundBelow.SurfaceArea();
                                    }

                                    // Finish initializing _costs_ using a backward scan over splits
                                    int countAbove = 0;
                                    Bounds3f boundAbove = new Bounds3f();
                                    for (int i = nSplits; i >= 1; --i)
                                    {
                                        boundAbove = Union(boundAbove, buckets[i].bounds);
                                        countAbove += buckets[i].count;
                                        costs[i - 1] += countAbove * boundAbove.SurfaceArea();
                                    }

                                    // Find bucket to split at that minimizes SAH metric
                                    int minCostSplitBucket = -1;
                                    Float minCost = Infinity;
                                    for (int i = 0; i < nSplits; ++i)
                                    {
                                        // Compute cost for candidate split and update minimum if
                                        // necessary
                                        if (costs[i] < minCost)
                                        {
                                            minCost = costs[i];
                                            minCostSplitBucket = i;
                                        }
                                    }
                                    // Compute leaf cost and SAH split cost for chosen split
                                    Float leafCost = bvhPrimitives.size();
                                    minCost = 1.0f / 2.0f + minCost / bounds.SurfaceArea();

                                    // Either create leaf or split primitives at selected SAH bucket
                                    if (bvhPrimitives.size() > (ulong)maxPrimsInNode || minCost < leafCost)
                                    {
                                        int splitBucket = minCostSplitBucket;
                                        Bounds3f cb = centroidBounds;
                                        int midIter = partition(arr, begin, end, (BVHPrimitive bp) =>
                                        {
                                            int b = (int)(nBuckets * cb.Offset(bp.Centroid())[dim]);
                                            if (b == nBuckets)
                                                b = nBuckets - 1;
                                            return b <= splitBucket;
                                        });
                                        mid = midIter - begin;
                                    }
                                    else
                                    {
                                        // Create leaf _BVHBuildNode_
                                        int firstPrimOffset =
                                            orderedPrimsOffset.fetch_add((int)bvhPrimitives.size());
                                        for (int i = 0; i < bvhPrimitives.length; ++i)
                                        {
                                            int index = (int)bvhPrimitives[i].primitiveIndex;
                                            orderedPrims[firstPrimOffset + i] = primitives[index];
                                        }
                                        node.InitLeaf(firstPrimOffset, (int)bvhPrimitives.size(), bounds);
                                        return node;
                                    }
                                }

                                break;
                            }
                    }

                    BVHBuildNode[] children = new BVHBuildNode[2];
                    // Recursively build BVHs for _children_
                    if (bvhPrimitives.size() > 128 * 1024)
                    {
                        // Recursively build child BVHs in parallel
                        int midCopy = mid;
                        pstd.span<BVHPrimitive> bvhCopy = bvhPrimitives;
                        ParallelFor(0, 2, (int i) =>
                        {
                            if (i == 0)
                                children[0] = buildRecursive(
                                    bvhCopy.subspan(0, (ulong)midCopy), totalNodes,
                                    orderedPrimsOffset, orderedPrims);
                            else
                                children[1] =
                                    buildRecursive(bvhCopy.subspan((ulong)midCopy),
                                                   totalNodes, orderedPrimsOffset, orderedPrims);
                        });
                    }
                    else
                    {
                        // Recursively build child BVHs sequentially
                        children[0] =
                            buildRecursive(bvhPrimitives.subspan(0, (ulong)mid),
                                           totalNodes, orderedPrimsOffset, orderedPrims);
                        children[1] =
                            buildRecursive(bvhPrimitives.subspan((ulong)mid),
                                           totalNodes, orderedPrimsOffset, orderedPrims);
                    }

                    node.InitInterior(dim, children[0], children[1]);
                }
            }

            return node;
        }

        BVHBuildNode buildHLBVH(Allocator alloc, BVHPrimitive[] bvhPrimitives,
                                AtomicInt totalNodes, Primitive[] orderedPrims)
        {
            // Compute bounding box of all primitive centroids
            Bounds3f bounds = new Bounds3f();
            foreach (BVHPrimitive prim in bvhPrimitives)
                bounds = Union(bounds, prim.Centroid());

            // Compute Morton indices of primitives
            MortonPrimitive[] mortonPrims = new MortonPrimitive[bvhPrimitives.Length];
            Bounds3f centroidBoundsAll = bounds;
            ParallelFor(0, bvhPrimitives.Length, (long i) =>
            {
                // Initialize _mortonPrims[i]_ for _i_th primitive
                const int mortonBits = 10;
                const int mortonScale = 1 << mortonBits;
                mortonPrims[i].primitiveIndex = (int)bvhPrimitives[i].primitiveIndex;
                Vector3f centroidOffset = centroidBoundsAll.Offset(bvhPrimitives[i].Centroid());
                Vector3f offset = centroidOffset * mortonScale;
                mortonPrims[i].mortonCode = EncodeMorton3(offset.x, offset.y, offset.z);
            });

            // Radix sort primitive Morton indices
            RadixSort(ref mortonPrims);

            // Create LBVH treelets at bottom of BVH
            // Find intervals of primitives for each treelet
            List<LBVHTreelet> treeletsToBuild = new List<LBVHTreelet>();
            for (size_t start = 0, end = 1; end <= (size_t)mortonPrims.Length; ++end)
            {
                uint32_t mask = 0b00111111111111000000000000000000;
                if (end == (size_t)mortonPrims.Length || ((mortonPrims[start].mortonCode & mask) !=
                                                          (mortonPrims[end].mortonCode & mask)))
                {
                    // Add entry to _treeletsToBuild_ for this treelet
                    size_t nPrimitives = end - start;
                    int maxBVHNodes = (int)(2 * nPrimitives - 1);
                    BVHBuildNode[] nodesArr = new BVHBuildNode[maxBVHNodes];
                    for (int k = 0; k < maxBVHNodes; ++k)
                        nodesArr[k] = new BVHBuildNode();
                    treeletsToBuild.Add(new LBVHTreelet { startIndex = start, nPrimitives = nPrimitives, buildNodesArray = nodesArr });

                    start = end;
                }
            }

            // Create LBVHs for treelets in parallel
            AtomicInt orderedPrimsOffset = new AtomicInt();
            ParallelFor(0, treeletsToBuild.Count, (int i) =>
            {
                // Generate _i_th LBVH treelet
                int nodesCreated = 0;
                const int firstBitIndex = 29 - 12;
                LBVHTreelet tr = treeletsToBuild[i];
                int nextNode = 0;
                tr.buildNodes = emitLBVH(tr.buildNodesArray, ref nextNode, bvhPrimitives, mortonPrims,
                                         (int)tr.startIndex, (int)tr.nPrimitives,
                                         ref nodesCreated, orderedPrims, orderedPrimsOffset, firstBitIndex);
                Interlocked.Add(ref totalNodes.v, nodesCreated);
            });

            // Create and return SAH BVH from LBVH treelets
            List<BVHBuildNode> finishedTreelets = new List<BVHBuildNode>(treeletsToBuild.Count);
            foreach (LBVHTreelet treelet in treeletsToBuild)
                finishedTreelets.Add(treelet.buildNodes);
            return buildUpperSAH(alloc, finishedTreelets.ToArray(), 0, finishedTreelets.Count, totalNodes);
        }

        BVHBuildNode emitLBVH(BVHBuildNode[] buildNodes, ref int nextNode,
                              BVHPrimitive[] bvhPrimitives,
                              MortonPrimitive[] mortonPrims, int mpOffset, int nPrimitives,
                              ref int totalNodes,
                              Primitive[] orderedPrims,
                              AtomicInt orderedPrimsOffset, int bitIndex)
        {
            CHECK_GT(nPrimitives, 0);
            if (bitIndex == -1 || nPrimitives < maxPrimsInNode)
            {
                // Create and return leaf node of LBVH treelet
                ++totalNodes;
                BVHBuildNode node = buildNodes[nextNode++];
                Bounds3f bounds = new Bounds3f();
                int firstPrimOffset = orderedPrimsOffset.fetch_add(nPrimitives);
                for (int i = 0; i < nPrimitives; ++i)
                {
                    int primitiveIndex = mortonPrims[mpOffset + i].primitiveIndex;
                    orderedPrims[firstPrimOffset + i] = primitives[primitiveIndex];
                    bounds = Union(bounds, bvhPrimitives[primitiveIndex].bounds);
                }
                node.InitLeaf(firstPrimOffset, nPrimitives, bounds);
                return node;
            }
            else
            {
                int mask = 1 << bitIndex;
                // Advance to next subtree level if there is no LBVH split for this bit
                if ((mortonPrims[mpOffset + 0].mortonCode & mask) ==
                    (mortonPrims[mpOffset + nPrimitives - 1].mortonCode & mask))
                    return emitLBVH(buildNodes, ref nextNode, bvhPrimitives, mortonPrims, mpOffset, nPrimitives,
                                    ref totalNodes, orderedPrims, orderedPrimsOffset, bitIndex - 1);

                // Find LBVH split point for this dimension
                uint32_t first0 = mortonPrims[mpOffset].mortonCode;
                int splitOffset = (int)FindInterval((size_t)nPrimitives, (int index) =>
                {
                    return ((first0 & mask) ==
                            (mortonPrims[mpOffset + index].mortonCode & mask));
                });
                ++splitOffset;
                CHECK_LE(splitOffset, nPrimitives - 1);
                CHECK_NE(mortonPrims[mpOffset + splitOffset - 1].mortonCode & mask,
                         mortonPrims[mpOffset + splitOffset].mortonCode & mask);

                // Create and return interior LBVH node
                totalNodes++;
                BVHBuildNode node = buildNodes[nextNode++];
                BVHBuildNode lbvh0 =
                    emitLBVH(buildNodes, ref nextNode, bvhPrimitives, mortonPrims, mpOffset, splitOffset, ref totalNodes,
                             orderedPrims, orderedPrimsOffset, bitIndex - 1);
                BVHBuildNode lbvh1 =
                    emitLBVH(buildNodes, ref nextNode, bvhPrimitives, mortonPrims, mpOffset + splitOffset,
                             nPrimitives - splitOffset, ref totalNodes, orderedPrims,
                             orderedPrimsOffset, bitIndex - 1);
                int axis = bitIndex % 3;
                node.InitInterior(axis, lbvh0, lbvh1);
                return node;
            }
        }

        int flattenBVH(BVHBuildNode node, ref int offset)
        {
            int nodeOffset = offset++;
            nodes[nodeOffset].bounds = node.bounds;
            if (node.nPrimitives > 0)
            {
                CHECK(node.children[0] == null && node.children[1] == null);
                CHECK_LT(node.nPrimitives, 65536);
                nodes[nodeOffset].primitivesOffset = node.firstPrimOffset;
                nodes[nodeOffset].nPrimitives = (uint16_t)node.nPrimitives;
            }
            else
            {
                // Create interior flattened BVH node
                nodes[nodeOffset].axis = (uint8_t)node.splitAxis;
                nodes[nodeOffset].nPrimitives = 0;
                flattenBVH(node.children[0], ref offset);
                nodes[nodeOffset].secondChildOffset = flattenBVH(node.children[1], ref offset);
            }
            return nodeOffset;
        }

        public override Bounds3f Bounds()
        {
            CHECK(nodes != null);
            return nodes[0].bounds;
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            if (nodes == null)
                return default;
            pstd.optional<ShapeIntersection> si = default;
            Vector3f invDir = new Vector3f(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
            int* dirIsNeg = stackalloc int[3];
            dirIsNeg[0] = invDir.x < 0 ? 1 : 0;
            dirIsNeg[1] = invDir.y < 0 ? 1 : 0;
            dirIsNeg[2] = invDir.z < 0 ? 1 : 0;
            // Follow ray through BVH nodes to find primitive intersections
            int toVisitOffset = 0, currentNodeIndex = 0;
            int* nodesToVisit = stackalloc int[64];
            int nodesVisited = 0;
            while (true)
            {
                ++nodesVisited;
                ref LinearBVHNode node = ref nodes[currentNodeIndex];
                // Check ray against BVH node
                if (node.bounds.IntersectP(ray.o, ray.d, tMax, invDir, dirIsNeg))
                {
                    if (node.nPrimitives > 0)
                    {
                        // Intersect ray with primitives in leaf BVH node
                        for (int i = 0; i < node.nPrimitives; ++i)
                        {
                            // Check for intersection with primitive in BVH node
                            pstd.optional<ShapeIntersection> primSi =
                                primitives[node.primitivesOffset + i].Intersect(ray, tMax);
                            if (primSi)
                            {
                                si = primSi;
                                tMax = si.value().tHit;
                            }
                        }
                        if (toVisitOffset == 0)
                            break;
                        currentNodeIndex = nodesToVisit[--toVisitOffset];
                    }
                    else
                    {
                        // Put far BVH node on _nodesToVisit_ stack, advance to near node
                        if (dirIsNeg[node.axis] != 0)
                        {
                            nodesToVisit[toVisitOffset++] = currentNodeIndex + 1;
                            currentNodeIndex = node.secondChildOffset;
                        }
                        else
                        {
                            nodesToVisit[toVisitOffset++] = node.secondChildOffset;
                            currentNodeIndex = currentNodeIndex + 1;
                        }
                    }
                }
                else
                {
                    if (toVisitOffset == 0)
                        break;
                    currentNodeIndex = nodesToVisit[--toVisitOffset];
                }
            }

            bvhNodesVisited += nodesVisited;
            return si;
        }

        public override bool IntersectP(Ray ray, Float tMax = Infinity)
        {
            if (nodes == null)
                return false;
            Vector3f invDir = new Vector3f(1.0f / ray.d.x, 1.0f / ray.d.y, 1.0f / ray.d.z);
            int* dirIsNeg = stackalloc int[3];
            dirIsNeg[0] = invDir.x < 0 ? 1 : 0;
            dirIsNeg[1] = invDir.y < 0 ? 1 : 0;
            dirIsNeg[2] = invDir.z < 0 ? 1 : 0;
            int* nodesToVisit = stackalloc int[64];
            int toVisitOffset = 0, currentNodeIndex = 0;
            int nodesVisited = 0;

            while (true)
            {
                ++nodesVisited;
                ref LinearBVHNode node = ref nodes[currentNodeIndex];
                if (node.bounds.IntersectP(ray.o, ray.d, tMax, invDir, dirIsNeg))
                {
                    // Process BVH node _node_ for traversal
                    if (node.nPrimitives > 0)
                    {
                        for (int i = 0; i < node.nPrimitives; ++i)
                        {
                            if (primitives[node.primitivesOffset + i].IntersectP(ray, tMax))
                            {
                                bvhNodesVisited += nodesVisited;
                                return true;
                            }
                        }
                        if (toVisitOffset == 0)
                            break;
                        currentNodeIndex = nodesToVisit[--toVisitOffset];
                    }
                    else
                    {
                        if (dirIsNeg[node.axis] != 0)
                        {
                            /// second child first
                            nodesToVisit[toVisitOffset++] = currentNodeIndex + 1;
                            currentNodeIndex = node.secondChildOffset;
                        }
                        else
                        {
                            nodesToVisit[toVisitOffset++] = node.secondChildOffset;
                            currentNodeIndex = currentNodeIndex + 1;
                        }
                    }
                }
                else
                {
                    if (toVisitOffset == 0)
                        break;
                    currentNodeIndex = nodesToVisit[--toVisitOffset];
                }
            }
            bvhNodesVisited += nodesVisited;
            return false;
        }

        BVHBuildNode buildUpperSAH(Allocator alloc, BVHBuildNode[] treeletRoots,
                                   int start, int end, AtomicInt totalNodes)
        {
            CHECK_LT(start, end);
            int nNodes = end - start;
            if (nNodes == 1)
                return treeletRoots[start];
            Interlocked.Increment(ref totalNodes.v);
            BVHBuildNode node = new BVHBuildNode();

            // Compute bounds of all nodes under this HLBVH node
            Bounds3f bounds = new Bounds3f();
            for (int i = start; i < end; ++i)
                bounds = Union(bounds, treeletRoots[i].bounds);

            // Compute bound of HLBVH node centroids, choose split dimension _dim_
            Bounds3f centroidBounds = new Bounds3f();
            for (int i = start; i < end; ++i)
            {
                Point3f centroid =
                    (treeletRoots[i].bounds.pMin + treeletRoots[i].bounds.pMax) * 0.5f;
                centroidBounds = Union(centroidBounds, centroid);
            }
            int dim = centroidBounds.MaxDimension();
            // FIXME: if this hits, what do we need to do?
            // Make sure the SAH split below does something... ?
            CHECK_NE(centroidBounds.pMax[dim], centroidBounds.pMin[dim]);

            // Allocate _BVHSplitBucket_ for SAH partition buckets
            const int nBuckets = 12;
            BVHSplitBucket[] buckets = new BVHSplitBucket[nBuckets];
            for (int i = 0; i < nBuckets; ++i)
                buckets[i].bounds = new Bounds3f();

            // Initialize _BVHSplitBucket_ for HLBVH SAH partition buckets
            for (int i = start; i < end; ++i)
            {
                Float centroid =
                    (treeletRoots[i].bounds.pMin[dim] + treeletRoots[i].bounds.pMax[dim]) *
                    0.5f;
                int b = (int)(nBuckets * ((centroid - centroidBounds.pMin[dim]) /
                                          (centroidBounds.pMax[dim] - centroidBounds.pMin[dim])));
                if (b == nBuckets)
                    b = nBuckets - 1;
                CHECK_GE(b, 0);
                CHECK_LT(b, nBuckets);
                buckets[b].count++;
                buckets[b].bounds = Union(buckets[b].bounds, treeletRoots[i].bounds);
            }

            // Compute costs for splitting after each bucket
            Float[] cost = new Float[nBuckets - 1];
            for (int i = 0; i < nBuckets - 1; ++i)
            {
                Bounds3f b0 = new Bounds3f(), b1 = new Bounds3f();
                int count0 = 0, count1 = 0;
                for (int j = 0; j <= i; ++j)
                {
                    b0 = Union(b0, buckets[j].bounds);
                    count0 += buckets[j].count;
                }
                for (int j = i + 1; j < nBuckets; ++j)
                {
                    b1 = Union(b1, buckets[j].bounds);
                    count1 += buckets[j].count;
                }
                cost[i] = .125f + (count0 * b0.SurfaceArea() + count1 * b1.SurfaceArea()) /
                                      bounds.SurfaceArea();
            }

            // Find bucket to split at that minimizes SAH metric
            Float minCost = cost[0];
            int minCostSplitBucket = 0;
            for (int i = 1; i < nBuckets - 1; ++i)
            {
                if (cost[i] < minCost)
                {
                    minCost = cost[i];
                    minCostSplitBucket = i;
                }
            }

            // Split nodes and create interior HLBVH SAH node
            Bounds3f cb = centroidBounds;
            int mid = partition(treeletRoots, start, end, (BVHBuildNode n) =>
            {
                Float centroid = (n.bounds.pMin[dim] + n.bounds.pMax[dim]) * 0.5f;
                int b = (int)(nBuckets * ((centroid - cb.pMin[dim]) /
                                          (cb.pMax[dim] - cb.pMin[dim])));
                if (b == nBuckets)
                    b = nBuckets - 1;
                CHECK_GE(b, 0);
                CHECK_LT(b, nBuckets);
                return b <= minCostSplitBucket;
            });
            CHECK_GT(mid, start);
            CHECK_LT(mid, end);
            BVHBuildNode c0 = this.buildUpperSAH(alloc, treeletRoots, start, mid, totalNodes);
            BVHBuildNode c1 = this.buildUpperSAH(alloc, treeletRoots, mid, end, totalNodes);
            node.InitInterior(dim, c0, c1);
            return node;
        }  // sizeof(LinearBVHNode) in pbrt (alignas(32))

        public static BVHAggregate Create(List<Primitive> prims, ParameterDictionary parameters)
        {
            string splitMethodName = parameters.GetOneString("splitmethod", "sah");
            BVHAggregate.SplitMethod splitMethod;
            if (splitMethodName == "sah")
                splitMethod = BVHAggregate.SplitMethod.SAH;
            else if (splitMethodName == "hlbvh")
                splitMethod = BVHAggregate.SplitMethod.HLBVH;
            else if (splitMethodName == "middle")
                splitMethod = BVHAggregate.SplitMethod.Middle;
            else if (splitMethodName == "equal")
                splitMethod = BVHAggregate.SplitMethod.EqualCounts;
            else
            {
                Warning(StringPrintf("BVH split method \"%s\" unknown.  Using \"sah\".", splitMethodName));
                splitMethod = BVHAggregate.SplitMethod.SAH;
            }

            int maxPrimsInNode = parameters.GetOneInt("maxnodeprims", 4);
            return new BVHAggregate(prims, maxPrimsInNode, splitMethod);
        }

        // BVHAggregate Private Members
        int maxPrimsInNode;
        Primitive[] primitives;
        SplitMethod splitMethod;
        LinearBVHNode[] nodes = null;
    }

    // KdTreeAggregate Definition
    public sealed unsafe class KdTreeAggregate : Primitive
    {
        // KdNodeToVisit Definition
        struct KdNodeToVisit
        {
            public int node;
            public Float tMin, tMax;
        }

        // KdTreeNode Definition (the C++ union is kept as separate fields)
        struct KdTreeNode
        {
            // KdTreeNode Methods
            public void InitLeaf(pstd.span<int> primNums, List<int> primitiveIndices)
            {
                flags = 3u | ((uint32_t)primNums.size() << 2);
                // Store primitive ids for leaf node
                if (primNums.size() == 0)
                    onePrimitiveIndex = 0;
                else if (primNums.size() == 1)
                    onePrimitiveIndex = primNums[0];
                else
                {
                    primitiveIndicesOffset = primitiveIndices.Count;
                    for (int i = 0; i < primNums.length; ++i)
                        primitiveIndices.Add(primNums[i]);
                }
            }

            public void InitInterior(int axis, int aboveChild, Float s)
            {
                split = s;
                flags = (uint32_t)(axis | (aboveChild << 2));
            }

            public Float SplitPos() { return split; }
            public int nPrimitives() { return (int)(flags >> 2); }
            public int SplitAxis() { return (int)(flags & 3); }
            public bool IsLeaf() { return (flags & 3) == 3; }
            public int AboveChild() { return (int)(flags >> 2); }

            // union {
            public Float split;                 // Interior
            public int onePrimitiveIndex;       // Leaf
            public int primitiveIndicesOffset;  // Leaf
            // };

            uint32_t flags;
        }

        // EdgeType Definition
        enum EdgeType { Start, End }

        // BoundEdge Definition
        struct BoundEdge
        {
            // BoundEdge Public Methods
            public BoundEdge(Float t, int primNum, bool starting)
            {
                this.t = t;
                this.primNum = primNum;
                type = starting ? EdgeType.Start : EdgeType.End;
            }

            public Float t;
            public int primNum;
            public EdgeType type;
        }

        // STAT_PIXEL_COUNTER("Kd-Tree/Nodes visited", kdNodesVisited);
        [ThreadStatic] static long kdNodesVisited, kdNodesVisitedSum;
        static StatRegisterer KD_PIXEL_REG = new StatRegisterer(
            (StatsAccumulator accum) =>
            {
                // report sum, since if disabled, it all just goes into var...
                accum.ReportCounter("Kd-Tree/Nodes visited", kdNodesVisited + kdNodesVisitedSum);
                kdNodesVisitedSum = 0;
                kdNodesVisited = 0;
            },
            (Point2i p, int counterIndex, PixelStatsAccumulator accum) =>
            {
                accum.ReportCounter(p, counterIndex, "Kd-Tree/Nodes visited", kdNodesVisited);
                kdNodesVisitedSum += kdNodesVisited;
                kdNodesVisited = 0;
            });

        public override Bounds3f Bounds() { return bounds; }

        // KdTreeAggregate Method Definitions
        public KdTreeAggregate(List<Primitive> p, int isectCost = 5, int traversalCost = 1,
                               Float emptyBonus = 0.5f, int maxPrims = 1, int maxDepth = -1)
        {
            this.isectCost = isectCost;
            this.traversalCost = traversalCost;
            this.maxPrims = maxPrims;
            this.emptyBonus = emptyBonus;
            this.primitives = p.ToArray();
            this.bounds = new Bounds3f();
            // Build kd-tree aggregate
            nextFreeNode = nAllocedNodes = 0;
            if (maxDepth <= 0)
                maxDepth = (int)std.round(8 + 1.3f * Log2Int((int64_t)primitives.Length));
            // Compute bounds for kd-tree construction
            List<Bounds3f> primBounds = new List<Bounds3f>(primitives.Length);
            foreach (Primitive prim in primitives)
            {
                Bounds3f b = prim.Bounds();
                bounds = Union(bounds, b);
                primBounds.Add(b);
            }

            // Allocate working memory for kd-tree construction
            BoundEdge[][] edges = new BoundEdge[3][];
            for (int i = 0; i < 3; ++i)
                edges[i] = new BoundEdge[2 * primitives.Length];

            int[] prims0 = new int[primitives.Length];
            int[] prims1 = new int[(maxDepth + 1) * primitives.Length];

            // Initialize _primNums_ for kd-tree construction
            int[] primNums = new int[primitives.Length];
            for (int i = 0; i < primitives.Length; ++i)
                primNums[i] = i;

            // Start recursive construction of kd-tree
            buildTree(0, bounds, primBounds, new pstd.span<int>(primNums), maxDepth, edges, new pstd.span<int>(prims0),
                      new pstd.span<int>(prims1), 0);
        }

        void buildTree(int nodeNum, Bounds3f nodeBounds,
                       List<Bounds3f> allPrimBounds,
                       pstd.span<int> primNums, int depth,
                       BoundEdge[][] edges, pstd.span<int> prims0,
                       pstd.span<int> prims1, int badRefines)
        {
            CHECK_EQ(nodeNum, nextFreeNode);
            // Get next free node from _nodes_ array
            if (nextFreeNode == nAllocedNodes)
            {
                int nNewAllocNodes = std.max(2 * nAllocedNodes, 512);
                KdTreeNode[] n = new KdTreeNode[nNewAllocNodes];
                if (nAllocedNodes > 0)
                    Array.Copy(nodes, n, nAllocedNodes);
                nodes = n;
                nAllocedNodes = nNewAllocNodes;
            }
            ++nextFreeNode;

            // Initialize leaf node if termination criteria met
            if ((int)primNums.size() <= maxPrims || depth == 0)
            {
                nodes[nodeNum].InitLeaf(primNums, primitiveIndices);
                return;
            }

            // Initialize interior node and continue recursion
            // Choose split axis position for interior node
            int bestAxis = -1, bestOffset = -1;
            Float bestCost = Infinity, leafCost = isectCost * (int)primNums.size();
            Float invTotalSA = 1 / nodeBounds.SurfaceArea();
            // Choose which axis to split along
            int axis = nodeBounds.MaxDimension();

            // Choose split along axis and attempt to partition primitives
            int retries = 0;
            int nPrimitives = (int)primNums.size();
        retrySplit:
            // Initialize edges for _axis_
            for (int i = 0; i < nPrimitives; ++i)
            {
                int pn = primNums[i];
                Bounds3f bounds = allPrimBounds[pn];
                edges[axis][2 * i] = new BoundEdge(bounds.pMin[axis], pn, true);
                edges[axis][2 * i + 1] = new BoundEdge(bounds.pMax[axis], pn, false);
            }
            // Sort _edges_ for _axis_
            Array.Sort(edges[axis], 0, 2 * nPrimitives, Comparer<BoundEdge>.Create((BoundEdge e0, BoundEdge e1) =>
            {
                // std::tie(e0.t, e0.type) < std::tie(e1.t, e1.type)
                if (e0.t < e1.t) return -1;
                if (e1.t < e0.t) return 1;
                if (e0.type < e1.type) return -1;
                if (e1.type < e0.type) return 1;
                return 0;
            }));

            // Compute cost of all splits for _axis_ to find best
            int nBelow = 0, nAbove = (int)primNums.size();
            for (int i = 0; i < 2 * (int)primNums.size(); ++i)
            {
                if (edges[axis][i].type == EdgeType.End)
                    --nAbove;
                Float edgeT = edges[axis][i].t;
                if (edgeT > nodeBounds.pMin[axis] && edgeT < nodeBounds.pMax[axis])
                {
                    // Compute child surface areas for split at _edgeT_
                    Vector3f d = nodeBounds.pMax - nodeBounds.pMin;
                    int otherAxis0 = (axis + 1) % 3, otherAxis1 = (axis + 2) % 3;
                    Float belowSA =
                        2 * (d[otherAxis0] * d[otherAxis1] +
                             (edgeT - nodeBounds.pMin[axis]) * (d[otherAxis0] + d[otherAxis1]));
                    Float aboveSA =
                        2 * (d[otherAxis0] * d[otherAxis1] +
                             (nodeBounds.pMax[axis] - edgeT) * (d[otherAxis0] + d[otherAxis1]));

                    // Compute cost for split at _i_th edge
                    Float pBelow = belowSA * invTotalSA, pAbove = aboveSA * invTotalSA;
                    Float eb = (nAbove == 0 || nBelow == 0) ? emptyBonus : 0;
                    Float cost = traversalCost +
                                 isectCost * (1 - eb) * (pBelow * nBelow + pAbove * nAbove);
                    // Update best split if this is lowest cost so far
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestAxis = axis;
                        bestOffset = i;
                    }
                }
                if (edges[axis][i].type == EdgeType.Start)
                    ++nBelow;
            }
            CHECK(nBelow == nPrimitives && nAbove == 0);

            // Try to split along another axis if no good splits were found
            if (bestAxis == -1 && retries < 2)
            {
                ++retries;
                axis = (axis + 1) % 3;
                goto retrySplit;
            }

            // Create leaf if no good splits were found
            if (bestCost > leafCost)
                ++badRefines;
            if ((bestCost > 4 * leafCost && nPrimitives < 16) || bestAxis == -1 ||
                badRefines == 3)
            {
                nodes[nodeNum].InitLeaf(primNums, primitiveIndices);
                return;
            }

            // Classify primitives with respect to split
            int n0 = 0, n1 = 0;
            for (int i = 0; i < bestOffset; ++i)
                if (edges[bestAxis][i].type == EdgeType.Start)
                    prims0[n0++] = edges[bestAxis][i].primNum;
            for (int i = bestOffset + 1; i < 2 * nPrimitives; ++i)
                if (edges[bestAxis][i].type == EdgeType.End)
                    prims1[n1++] = edges[bestAxis][i].primNum;

            // Recursively initialize kd-tree node's children
            Float tSplit = edges[bestAxis][bestOffset].t;
            Bounds3f bounds0 = nodeBounds, bounds1 = nodeBounds;
            bounds0.pMax[bestAxis] = bounds1.pMin[bestAxis] = tSplit;
            buildTree(nodeNum + 1, bounds0, allPrimBounds, prims0.subspan(0, (ulong)n0), depth - 1,
                      edges, prims0, prims1.subspan((ulong)n1), badRefines);
            int aboveChild = nextFreeNode;
            nodes[nodeNum].InitInterior(bestAxis, aboveChild, tSplit);
            buildTree(aboveChild, bounds1, allPrimBounds, prims1.subspan(0, (ulong)n1), depth - 1, edges,
                      prims0, prims1.subspan((ulong)n1), badRefines);
        }

        public override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float rayTMax = Infinity)
        {
            // Compute initial parametric range of ray inside kd-tree extent
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, rayTMax, &tMin, &tMax))
                return default;

            // Prepare to traverse kd-tree for ray
            Vector3f invDir = new Vector3f(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
            const int maxToVisit = 64;
            KdNodeToVisit[] toVisit = new KdNodeToVisit[maxToVisit];
            int toVisitIndex = 0;
            int nodesVisited = 0;

            // Traverse kd-tree nodes in order for ray
            pstd.optional<ShapeIntersection> si = default;
            int node = 0;
            while (node >= 0)
            {
                // Bail out if we found a hit closer than the current node
                if (rayTMax < tMin)
                    break;

                ++nodesVisited;
                if (!nodes[node].IsLeaf())
                {
                    // Visit kd-tree interior node
                    // Compute parametric distance along ray to split plane
                    int axis = nodes[node].SplitAxis();
                    Float tSplit = (nodes[node].SplitPos() - ray.o[axis]) * invDir[axis];

                    // Get node child pointers for ray
                    int firstChild, secondChild;
                    bool belowFirst = (ray.o[axis] < nodes[node].SplitPos()) ||
                                      (ray.o[axis] == nodes[node].SplitPos() && ray.d[axis] <= 0);
                    if (belowFirst)
                    {
                        firstChild = node + 1;
                        secondChild = nodes[node].AboveChild();
                    }
                    else
                    {
                        firstChild = nodes[node].AboveChild();
                        secondChild = node + 1;
                    }

                    // Advance to next child node, possibly enqueue other child
                    if (tSplit > tMax || tSplit <= 0)
                        node = firstChild;
                    else if (tSplit < tMin)
                        node = secondChild;
                    else
                    {
                        // Enqueue _secondChild_ in todo list
                        toVisit[toVisitIndex].node = secondChild;
                        toVisit[toVisitIndex].tMin = tSplit;
                        toVisit[toVisitIndex].tMax = tMax;
                        ++toVisitIndex;

                        node = firstChild;
                        tMax = tSplit;
                    }
                }
                else
                {
                    // Check for intersections inside leaf node
                    int nPrimitives = nodes[node].nPrimitives();
                    if (nPrimitives == 1)
                    {
                        Primitive p = primitives[nodes[node].onePrimitiveIndex];
                        // Check one primitive inside leaf node
                        pstd.optional<ShapeIntersection> primSi = p.Intersect(ray, rayTMax);
                        if (primSi)
                        {
                            si = primSi;
                            rayTMax = si.value().tHit;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < nPrimitives; ++i)
                        {
                            int index = primitiveIndices[nodes[node].primitiveIndicesOffset + i];
                            Primitive p = primitives[index];
                            // Check one primitive inside leaf node
                            pstd.optional<ShapeIntersection> primSi = p.Intersect(ray, rayTMax);
                            if (primSi)
                            {
                                si = primSi;
                                rayTMax = si.value().tHit;
                            }
                        }
                    }

                    // Grab next node to visit from todo list
                    if (toVisitIndex > 0)
                    {
                        --toVisitIndex;
                        node = toVisit[toVisitIndex].node;
                        tMin = toVisit[toVisitIndex].tMin;
                        tMax = toVisit[toVisitIndex].tMax;
                    }
                    else
                        break;
                }
            }
            kdNodesVisited += nodesVisited;
            return si;
        }

        public override bool IntersectP(Ray ray, Float raytMax = Infinity)
        {
            // Compute initial parametric range of ray inside kd-tree extent
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return false;

            // Prepare to traverse kd-tree for ray
            Vector3f invDir = new Vector3f(1 / ray.d.x, 1 / ray.d.y, 1 / ray.d.z);
            const int maxTodo = 64;
            KdNodeToVisit[] toVisit = new KdNodeToVisit[maxTodo];
            int toVisitIndex = 0;
            int nodesVisited = 0;
            int node = 0;
            while (node >= 0)
            {
                ++nodesVisited;
                if (nodes[node].IsLeaf())
                {
                    // Check for shadow ray intersections inside leaf node
                    int nPrimitives = nodes[node].nPrimitives();
                    if (nPrimitives == 1)
                    {
                        Primitive p = primitives[nodes[node].onePrimitiveIndex];
                        if (p.IntersectP(ray, raytMax))
                        {
                            kdNodesVisited += nodesVisited;
                            return true;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < nPrimitives; ++i)
                        {
                            int primitiveIndex =
                                primitiveIndices[nodes[node].primitiveIndicesOffset + i];
                            Primitive prim = primitives[primitiveIndex];
                            if (prim.IntersectP(ray, raytMax))
                            {
                                kdNodesVisited += nodesVisited;
                                return true;
                            }
                        }
                    }

                    // Grab next node to process from todo list
                    if (toVisitIndex > 0)
                    {
                        --toVisitIndex;
                        node = toVisit[toVisitIndex].node;
                        tMin = toVisit[toVisitIndex].tMin;
                        tMax = toVisit[toVisitIndex].tMax;
                    }
                    else
                        break;
                }
                else
                {
                    // Process kd-tree interior node

                    // Compute parametric distance along ray to split plane
                    int axis = nodes[node].SplitAxis();
                    Float tSplit = (nodes[node].SplitPos() - ray.o[axis]) * invDir[axis];

                    // Get node children pointers for ray
                    int firstChild, secondChild;
                    int belowFirst = ((ray.o[axis] < nodes[node].SplitPos()) ||
                                      (ray.o[axis] == nodes[node].SplitPos() && ray.d[axis] <= 0)) ? 1 : 0;
                    if (belowFirst != 0)
                    {
                        firstChild = node + 1;
                        secondChild = nodes[node].AboveChild();
                    }
                    else
                    {
                        firstChild = nodes[node].AboveChild();
                        secondChild = node + 1;
                    }

                    // Advance to next child node, possibly enqueue other child
                    if (tSplit > tMax || tSplit <= 0)
                        node = firstChild;
                    else if (tSplit < tMin)
                        node = secondChild;
                    else
                    {
                        // Enqueue _secondChild_ in todo list
                        toVisit[toVisitIndex].node = secondChild;
                        toVisit[toVisitIndex].tMin = tSplit;
                        toVisit[toVisitIndex].tMax = tMax;
                        ++toVisitIndex;
                        node = firstChild;
                        tMax = tSplit;
                    }
                }
            }
            kdNodesVisited += nodesVisited;
            return false;
        }

        public static KdTreeAggregate Create(List<Primitive> prims, ParameterDictionary parameters)
        {
            int isectCost = parameters.GetOneInt("intersectcost", 5);
            int travCost = parameters.GetOneInt("traversalcost", 1);
            Float emptyBonus = parameters.GetOneFloat("emptybonus", 0.5f);
            int maxPrims = parameters.GetOneInt("maxprims", 1);
            int maxDepth = parameters.GetOneInt("maxdepth", -1);
            return new KdTreeAggregate(prims, isectCost, travCost, emptyBonus,
                                       maxPrims, maxDepth);
        }

        // KdTreeAggregate Private Members
        int isectCost, traversalCost, maxPrims;
        Float emptyBonus;
        Primitive[] primitives;
        List<int> primitiveIndices = new List<int>();
        KdTreeNode[] nodes;
        int nAllocedNodes, nextFreeNode;
        Bounds3f bounds;
    }

}  // namespace pbrt
