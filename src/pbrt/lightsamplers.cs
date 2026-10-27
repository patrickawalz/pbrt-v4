// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// lightsamplers.h / lightsamplers.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{

    // UniformLightSampler Definition
    public sealed unsafe class UniformLightSampler : LightSampler
    {
        // UniformLightSampler Public Methods
        public UniformLightSampler(pstd.span<Light> lights, Allocator alloc)
        {
            this.lights = new pstd.vector<Light>();
            for (ulong i = 0; i < lights.size(); ++i)
                this.lights.push_back(lights[i]);
        }

        public override pstd.optional<SampledLight> Sample(Float u)
        {
            if (lights.empty())
                return default;
            int lightIndex = std.min((int)(u * (Float)lights.size()), (int)lights.size() - 1);
            return new SampledLight { light = lights[(ulong)lightIndex], p = 1.0f / lights.size() };
        }

        public override Float PMF(Light light)
        {
            if (lights.empty())
                return 0;
            return 1.0f / lights.size();
        }

        public override pstd.optional<SampledLight> Sample(LightSampleContext ctx, Float u)
        {
            return Sample(u);
        }

        public override Float PMF(LightSampleContext ctx, Light light) { return PMF(light); }

        public override string ToString() { return "UniformLightSampler"; }

        // UniformLightSampler Private Members
        pstd.vector<Light> lights;
    }

    // PowerLightSampler Definition
    public sealed unsafe class PowerLightSampler : LightSampler
    {
        // PowerLightSampler Method Definitions
        public PowerLightSampler(pstd.span<Light> lights, Allocator alloc)
        {
            this.lights = new pstd.vector<Light>();
            for (ulong i = 0; i < lights.size(); ++i)
                this.lights.push_back(lights[i]);
            lightToIndex = new HashMap<Light, size_t>(alloc);
            aliasTable = new AliasTable(alloc);

            if (lights.empty())
                return;
            // Initialize _lightToIndex_ hash table
            for (size_t i = 0; i < lights.size(); ++i)
                lightToIndex.Insert(lights[i], i);

            // Compute lights' power and initialize alias table
            Float[] lightPower = new Float[lights.size()];
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            for (ulong i = 0; i < lights.size(); ++i)
            {
                Light light = lights[i];
                SampledSpectrum phi = pbrt.SafeDiv(light.Phi(lambda), lambda.PDF());
                lightPower[i] = phi.Average();
            }
            Float total = 0;
            for (int i = 0; i < lightPower.Length; ++i)
                total += lightPower[i];
            if (total == 0.0f)
                for (int i = 0; i < lightPower.Length; ++i)
                    lightPower[i] = 1.0f;
            aliasTable = new AliasTable(new pstd.span<Float>(lightPower), alloc);
        }

        public override pstd.optional<SampledLight> Sample(Float u)
        {
            if (aliasTable.size() == 0)
                return default;
            Float pmf;
            int lightIndex = aliasTable.Sample(u, &pmf);
            return new SampledLight { light = lights[(ulong)lightIndex], p = pmf };
        }

        public override Float PMF(Light light)
        {
            if (aliasTable.size() == 0)
                return 0;
            return aliasTable.PMF((int)lightToIndex[light]);
        }

        public override pstd.optional<SampledLight> Sample(LightSampleContext ctx, Float u)
        {
            return Sample(u);
        }

        public override Float PMF(LightSampleContext ctx, Light light) { return PMF(light); }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PowerLightSampler aliasTable: %s ]", aliasTable);
        }

        // PowerLightSampler Private Members
        pstd.vector<Light> lights;
        HashMap<Light, size_t> lightToIndex;
        AliasTable aliasTable;
    }

    // CompactLightBounds Definition
    public unsafe struct CompactLightBounds
    {
        // CompactLightBounds Public Methods
        public CompactLightBounds(LightBounds lb, Bounds3f allb)
        {
            w = new OctahedralVector(pbrt.Normalize(lb.w));
            phi = lb.phi;
            qCosTheta_o = QuantizeCos(lb.cosTheta_o);
            qCosTheta_e = QuantizeCos(lb.cosTheta_e);
            twoSided = lb.twoSided ? 1u : 0u;
            // Quantize bounding box into _qb_
            for (int c = 0; c < 3; ++c)
            {
                qb[0 * 3 + c] = (ushort)pstd.floor(QuantizeBounds(lb.bounds[0][c], allb.pMin[c], allb.pMax[c]));
                qb[1 * 3 + c] = (ushort)pstd.ceil(QuantizeBounds(lb.bounds[1][c], allb.pMin[c], allb.pMax[c]));
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf(
                "[ CompactLightBounds qb: [ [ %u %u %u ] [ %u %u %u ] ] w: %s (%s) phi: %f " +
                "qCosTheta_o: %u (%f) qCosTheta_e: %u (%f) twoSided: %u ]",
                (uint)qb[0], (uint)qb[1], (uint)qb[2], (uint)qb[3], (uint)qb[4], (uint)qb[5], w, (Vector3f)w, phi,
                qCosTheta_o, CosTheta_o(), qCosTheta_e, CosTheta_e(), twoSided);
        }

        public string ToString(Bounds3f allBounds)
        {
            return pbrt.StringPrintf(
                "[ CompactLightBounds b: %s qb: [ [ %u %u %u ] [ %u %u %u ] ] w: %s (%s) phi: %f " +
                "qCosTheta_o: %u (%f) qCosTheta_e: %u (%f) twoSided: %u ]",
                Bounds(allBounds), (uint)qb[0], (uint)qb[1], (uint)qb[2], (uint)qb[3], (uint)qb[4], (uint)qb[5], w,
                (Vector3f)w, phi, qCosTheta_o, CosTheta_o(), qCosTheta_e, CosTheta_e(), twoSided);
        }

        public bool TwoSided() { return twoSided != 0; }
        public Float CosTheta_o() { return 2 * (qCosTheta_o / 32767.0f) - 1; }
        public Float CosTheta_e() { return 2 * (qCosTheta_e / 32767.0f) - 1; }

        public Bounds3f Bounds(Bounds3f allb)
        {
            return new Bounds3f(
                new Point3f(pbrt.Lerp(qb[0 * 3 + 0] / 65535.0f, allb.pMin.x, allb.pMax.x),
                            pbrt.Lerp(qb[0 * 3 + 1] / 65535.0f, allb.pMin.y, allb.pMax.y),
                            pbrt.Lerp(qb[0 * 3 + 2] / 65535.0f, allb.pMin.z, allb.pMax.z)),
                new Point3f(pbrt.Lerp(qb[1 * 3 + 0] / 65535.0f, allb.pMin.x, allb.pMax.x),
                            pbrt.Lerp(qb[1 * 3 + 1] / 65535.0f, allb.pMin.y, allb.pMax.y),
                            pbrt.Lerp(qb[1 * 3 + 2] / 65535.0f, allb.pMin.z, allb.pMax.z)));
        }

        public Float Importance(Point3f p, Normal3f n, Bounds3f allb)
        {
            Bounds3f bounds = Bounds(allb);
            Float cosTheta_o = CosTheta_o(), cosTheta_e = CosTheta_e();
            // Return importance for light bounds at reference point
            // Compute clamped squared distance to reference point
            Point3f pc = (bounds.pMin + bounds.pMax) / 2;
            Float d2 = pbrt.DistanceSquared(p, pc);
            d2 = std.max(d2, pbrt.Length(bounds.Diagonal()) / 2);

            // Define cosine and sine clamped subtraction lambdas
            Func<Float, Float, Float, Float, Float> cosSubClamped =
                (Float sinTheta_a, Float cosTheta_a, Float sinTheta_b, Float cosTheta_b) =>
                {
                    if (cosTheta_a > cosTheta_b)
                        return 1;
                    return cosTheta_a * cosTheta_b + sinTheta_a * sinTheta_b;
                };

            Func<Float, Float, Float, Float, Float> sinSubClamped =
                (Float sinTheta_a, Float cosTheta_a, Float sinTheta_b, Float cosTheta_b) =>
                {
                    if (cosTheta_a > cosTheta_b)
                        return 0;
                    return sinTheta_a * cosTheta_b - cosTheta_a * sinTheta_b;
                };

            // Compute sine and cosine of angle to vector _w_, $\theta_\roman{w}$
            Vector3f wi = pbrt.Normalize(p - pc);
            Float cosTheta_w = pbrt.Dot((Vector3f)w, wi);
            if (twoSided != 0)
                cosTheta_w = std.abs(cosTheta_w);
            Float sinTheta_w = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta_w));

            // Compute $\cos\,\theta_\roman{\+b}$ for reference point
            Float cosTheta_b = pbrt.BoundSubtendedDirections(bounds, p).cosTheta;
            Float sinTheta_b = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta_b));

            // Compute $\cos\,\theta'$ and test against $\cos\,\theta_\roman{e}$
            Float sinTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta_o));
            Float cosTheta_x = cosSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
            Float sinTheta_x = sinSubClamped(sinTheta_w, cosTheta_w, sinTheta_o, cosTheta_o);
            Float cosThetap = cosSubClamped(sinTheta_x, cosTheta_x, sinTheta_b, cosTheta_b);
            if (cosThetap <= cosTheta_e)
                return 0;

            // Return final importance at reference point
            Float importance = phi * cosThetap / d2;
            pbrt.DCHECK_GE(importance, -1e-3f);
            // Account for $\cos\theta_\roman{i}$ in importance at surfaces
            if (n != new Normal3f(0, 0, 0))
            {
                Float cosTheta_i = pbrt.AbsDot(wi, n);
                Float sinTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta_i));
                Float cosThetap_i = cosSubClamped(sinTheta_i, cosTheta_i, sinTheta_b, cosTheta_b);
                importance *= cosThetap_i;
            }

            importance = std.max<Float>(importance, 0);
            return importance;
        }

        // CompactLightBounds Private Methods
        static uint QuantizeCos(Float c)
        {
            pbrt.CHECK(c >= -1 && c <= 1);
            return (uint)pstd.floor(32767.0f * ((c + 1) / 2));
        }

        static Float QuantizeBounds(Float c, Float min, Float max)
        {
            pbrt.CHECK(c >= min && c <= max);
            if (min == max)
                return 0;
            return 65535.0f * pbrt.Clamp((c - min) / (max - min), 0, 1);
        }

        // CompactLightBounds Private Members
        OctahedralVector w;
        Float phi;
        uint qCosTheta_o;  // : 15
        uint qCosTheta_e;  // : 15
        uint twoSided;     // : 1
        fixed ushort qb[6];  // qb[2][3]
    }

    // LightBVHNode Definition
    public unsafe struct LightBVHNode
    {
        // LightBVHNode Public Methods
        public static LightBVHNode MakeLeaf(uint lightIndex, CompactLightBounds cb)
        {
            LightBVHNode n = default;
            n.lightBounds = cb;
            n.childOrLightIndex = lightIndex;
            n.isLeaf = 1;
            return n;
        }

        public static LightBVHNode MakeInterior(uint child1Index, CompactLightBounds cb)
        {
            LightBVHNode n = default;
            n.lightBounds = cb;
            n.childOrLightIndex = child1Index;
            n.isLeaf = 0;
            return n;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf(
                "[ LightBVHNode lightBounds: %s childOrLightIndex: %d isLeaf: %d ]", lightBounds,
                childOrLightIndex, isLeaf);
        }

        // LightBVHNode Public Members
        public CompactLightBounds lightBounds;
        public uint childOrLightIndex;  // : 31
        public uint isLeaf;             // : 1
    }

    // BVHLightSampler Definition
    public sealed unsafe class BVHLightSampler : LightSampler
    {
        // STAT_MEMORY_COUNTER("Memory/Light BVH", lightBVHBytes);
        [ThreadStatic] static long lightBVHBytes;
        static StatRegisterer STATS_REG_BVH = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Light BVH", lightBVHBytes);
            lightBVHBytes = 0;
        });

        // STAT_INT_DISTRIBUTION("Integrator/Lights sampled per lookup", nLightsSampled);
        [ThreadStatic] static long nLightsSampledSum, nLightsSampledCount, nLightsSampledMin, nLightsSampledMax;
        static StatRegisterer STATS_REG_NLS = new StatRegisterer((StatsAccumulator accum) =>
        {
            if (nLightsSampledCount > 0)
                accum.ReportIntDistribution("Integrator/Lights sampled per lookup", nLightsSampledSum,
                                            nLightsSampledCount, nLightsSampledMin, nLightsSampledMax);
            nLightsSampledSum = nLightsSampledCount = nLightsSampledMin = nLightsSampledMax = 0;
        });

        // BVHLightSampler Method Definitions
        public BVHLightSampler(pstd.span<Light> lights, Allocator alloc)
        {
            this.lights = new pstd.vector<Light>();
            for (ulong i = 0; i < lights.size(); ++i)
                this.lights.push_back(lights[i]);
            infiniteLights = new pstd.vector<Light>();
            nodes = new pstd.vector<LightBVHNode>();
            lightToBitTrail = new HashMap<Light, uint32_t>(alloc);
            allLightBounds = new Bounds3f();

            // Initialize _infiniteLights_ array and light BVH
            List<(int, LightBounds)> bvhLights = new List<(int, LightBounds)>();
            for (size_t i = 0; i < lights.size(); ++i)
            {
                // Store $i$th light in either _infiniteLights_ or _bvhLights_
                Light light = lights[i];
                pstd.optional<LightBounds> lightBounds = light.Bounds();
                if (!lightBounds)
                    infiniteLights.push_back(light);
                else if (lightBounds.value().phi > 0)
                {
                    bvhLights.Add(((int)i, lightBounds.value()));
                    allLightBounds = pbrt.Union(allLightBounds, lightBounds.value().bounds);
                }
            }
            if (bvhLights.Count != 0)
                buildBVH(bvhLights, 0, bvhLights.Count, 0, 0);
            lightBVHBytes += (long)(nodes.size() * (ulong)sizeof(LightBVHNode) +
                                    lightToBitTrail.capacity() * sizeof(uint32_t) +
                                    this.lights.size() * 8UL + infiniteLights.size() * 8UL);
        }

        public override pstd.optional<SampledLight> Sample(LightSampleContext ctx, Float u)
        {
            // Compute infinite light sampling probability _pInfinite_
            Float pInfinite = (Float)(infiniteLights.size()) /
                              (Float)(infiniteLights.size() + (nodes.empty() ? 0UL : 1UL));

            if (u < pInfinite)
            {
                // Sample infinite lights with uniform probability
                u /= pInfinite;
                int index = std.min((int)(u * infiniteLights.size()), (int)infiniteLights.size() - 1);
                Float pmf = pInfinite / infiniteLights.size();
                return new SampledLight { light = infiniteLights[(ulong)index], p = pmf };
            }
            else
            {
                // Traverse light BVH to sample light
                if (nodes.empty())
                    return default;
                // Declare common variables for light BVH traversal
                Point3f p = ctx.p();
                Normal3f n = ctx.ns;
                u = std.min<Float>((u - pInfinite) / (1 - pInfinite), (Float)pbrt.OneMinusEpsilon);
                int nodeIndex = 0;
                Float pmf = 1 - pInfinite;

                while (true)
                {
                    // Process light BVH node for light sampling
                    LightBVHNode node = nodes[(ulong)nodeIndex];
                    if (node.isLeaf == 0)
                    {
                        // Compute light BVH child node importances
                        LightBVHNode child0 = nodes[(ulong)(nodeIndex + 1)];
                        LightBVHNode child1 = nodes[node.childOrLightIndex];
                        Float[] ci = {
                            child0.lightBounds.Importance(p, n, allLightBounds),
                            child1.lightBounds.Importance(p, n, allLightBounds)};
                        if (ci[0] == 0 && ci[1] == 0)
                            return default;

                        // Randomly sample light BVH child node
                        Float nodePMF;
                        int child = pbrt.SampleDiscrete(new pstd.span<Float>(ci), u, &nodePMF, &u);
                        pmf *= nodePMF;
                        nodeIndex = (child == 0) ? (nodeIndex + 1) : (int)node.childOrLightIndex;
                    }
                    else
                    {
                        // Confirm light has nonzero importance before returning light sample
                        if (nodeIndex > 0)
                            pbrt.DCHECK_GT(node.lightBounds.Importance(p, n, allLightBounds), 0);
                        if (nodeIndex > 0 || node.lightBounds.Importance(p, n, allLightBounds) > 0)
                            return new SampledLight { light = lights[node.childOrLightIndex], p = pmf };
                        return default;
                    }
                }
            }
        }

        public override Float PMF(LightSampleContext ctx, Light light)
        {
            // Handle infinite _light_ PMF computation
            if (!lightToBitTrail.HasKey(light))
                return 1.0f / (infiniteLights.size() + (nodes.empty() ? 0UL : 1UL));

            // Initialize local variables for BVH traversal for PMF computation
            uint32_t bitTrail = lightToBitTrail[light];
            Point3f p = ctx.p();
            Normal3f n = ctx.ns;
            // Compute infinite light sampling probability _pInfinite_
            Float pInfinite = (Float)(infiniteLights.size()) /
                              (Float)(infiniteLights.size() + (nodes.empty() ? 0UL : 1UL));

            Float pmf = 1 - pInfinite;
            int nodeIndex = 0;

            // Compute light's PMF by walking down tree nodes to the light
            while (true)
            {
                LightBVHNode node = nodes[(ulong)nodeIndex];
                if (node.isLeaf != 0)
                {
                    pbrt.DCHECK(light == lights[node.childOrLightIndex]);
                    return pmf;
                }
                // Compute child importances and update PMF for current node
                LightBVHNode child0 = nodes[(ulong)(nodeIndex + 1)];
                LightBVHNode child1 = nodes[node.childOrLightIndex];
                Float[] ci = { child0.lightBounds.Importance(p, n, allLightBounds),
                               child1.lightBounds.Importance(p, n, allLightBounds) };
                pbrt.DCHECK_GT(ci[bitTrail & 1], 0);
                pmf *= ci[bitTrail & 1] / (ci[0] + ci[1]);

                // Use _bitTrail_ to find next node index and update its value
                nodeIndex = ((bitTrail & 1) != 0) ? (int)node.childOrLightIndex : (nodeIndex + 1);
                bitTrail >>= 1;
            }
        }

        public override pstd.optional<SampledLight> Sample(Float u)
        {
            if (lights.empty())
                return default;
            int lightIndex = std.min((int)(u * lights.size()), (int)lights.size() - 1);
            return new SampledLight { light = lights[(ulong)lightIndex], p = 1.0f / lights.size() };
        }

        public override Float PMF(Light light)
        {
            if (lights.empty())
                return 0;
            return 1.0f / lights.size();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ BVHLightSampler nodes: %s ]", nodes);
        }

        // BVHLightSampler Private Methods
        (int, LightBounds) buildBVH(List<(int, LightBounds)> bvhLights, int start, int end, uint32_t bitTrail,
                                    int depth)
        {
            pbrt.DCHECK_LT(start, end);
            // Initialize leaf node if only a single light remains
            if (end - start == 1)
            {
                int nodeIndex = (int)nodes.size();
                CompactLightBounds cb = new CompactLightBounds(bvhLights[start].Item2, allLightBounds);
                int lightIndex = bvhLights[start].Item1;
                nodes.push_back(LightBVHNode.MakeLeaf((uint)lightIndex, cb));
                lightToBitTrail.Insert(lights[(ulong)lightIndex], bitTrail);
                return (nodeIndex, bvhLights[start].Item2);
            }

            // Choose split dimension and position using modified SAH
            // Compute bounds and centroid bounds for lights
            Bounds3f bounds = new Bounds3f(), centroidBounds = new Bounds3f();
            for (int i = start; i < end; ++i)
            {
                LightBounds lb = bvhLights[i].Item2;
                bounds = pbrt.Union(bounds, lb.bounds);
                centroidBounds = pbrt.Union(centroidBounds, lb.Centroid());
            }

            Float minCost = pbrt.Infinity;
            int minCostSplitBucket = -1, minCostSplitDim = -1;
            const int nBuckets = 12;
            for (int dim = 0; dim < 3; ++dim)
            {
                // Compute minimum cost bucket for splitting along dimension _dim_
                if (centroidBounds.pMax[dim] == centroidBounds.pMin[dim])
                    continue;
                // Compute _LightBounds_ for each bucket
                LightBounds[] bucketLightBounds = new LightBounds[nBuckets];
                for (int i = start; i < end; ++i)
                {
                    Point3f pc = bvhLights[i].Item2.Centroid();
                    int b = (int)(nBuckets * centroidBounds.Offset(pc)[dim]);
                    if (b == nBuckets)
                        b = nBuckets - 1;
                    pbrt.DCHECK_GE(b, 0);
                    pbrt.DCHECK_LT(b, nBuckets);
                    bucketLightBounds[b] = pbrt.Union(bucketLightBounds[b], bvhLights[i].Item2);
                }

                // Compute costs for splitting lights after each bucket
                Float[] cost = new Float[nBuckets - 1];
                for (int i = 0; i < nBuckets - 1; ++i)
                {
                    // Find _LightBounds_ for lights below and above bucket split
                    LightBounds b0 = default, b1 = default;
                    for (int j = 0; j <= i; ++j)
                        b0 = pbrt.Union(b0, bucketLightBounds[j]);
                    for (int j = i + 1; j < nBuckets; ++j)
                        b1 = pbrt.Union(b1, bucketLightBounds[j]);

                    // Compute final light split cost for bucket
                    cost[i] = EvaluateCost(b0, bounds, dim) + EvaluateCost(b1, bounds, dim);
                }

                // Find light split that minimizes SAH metric
                for (int i = 1; i < nBuckets - 1; ++i)
                {
                    if (cost[i] > 0 && cost[i] < minCost)
                    {
                        minCost = cost[i];
                        minCostSplitBucket = i;
                        minCostSplitDim = dim;
                    }
                }
            }

            // Partition lights according to chosen split
            int mid;
            if (minCostSplitDim == -1)
                mid = (start + end) / 2;
            else
            {
                int splitBucket = minCostSplitBucket, splitDim = minCostSplitDim;
                Bounds3f cb3 = centroidBounds;
                Func<(int, LightBounds), bool> pred = ((int, LightBounds) l) =>
                {
                    int b = (int)(nBuckets * cb3.Offset(l.Item2.Centroid())[splitDim]);
                    if (b == nBuckets)
                        b = nBuckets - 1;
                    pbrt.DCHECK_GE(b, 0);
                    pbrt.DCHECK_LT(b, nBuckets);
                    return b <= splitBucket;
                };
                // std::partition (bidirectional iterator algorithm)
                int first = start, last = end;
                while (true)
                {
                    while (true)
                    {
                        if (first == last) goto partitionDone;
                        else if (pred(bvhLights[first])) ++first;
                        else break;
                    }
                    --last;
                    while (true)
                    {
                        if (first == last) goto partitionDone;
                        else if (!pred(bvhLights[last])) --last;
                        else break;
                    }
                    (int, LightBounds) tmp = bvhLights[first];
                    bvhLights[first] = bvhLights[last];
                    bvhLights[last] = tmp;
                    ++first;
                }
            partitionDone:
                mid = first;
                if (mid == start || mid == end)
                    mid = (start + end) / 2;
                pbrt.DCHECK(mid > start && mid < end);
            }

            // Allocate interior _LightBVHNode_ and recursively initialize children
            int nodeIndex2 = (int)nodes.size();
            nodes.push_back(new LightBVHNode());
            pbrt.CHECK_LT(depth, 64);
            (int, LightBounds) child0 = buildBVH(bvhLights, start, mid, bitTrail, depth + 1);
            pbrt.DCHECK_EQ(nodeIndex2 + 1, child0.Item1);
            (int, LightBounds) child1 = buildBVH(bvhLights, mid, end, bitTrail | (1u << depth), depth + 1);

            // Initialize interior node and return node index and bounds
            LightBounds lb2 = pbrt.Union(child0.Item2, child1.Item2);
            CompactLightBounds cb2 = new CompactLightBounds(lb2, allLightBounds);
            nodes[(ulong)nodeIndex2] = LightBVHNode.MakeInterior((uint)child1.Item1, cb2);
            return (nodeIndex2, lb2);
        }

        Float EvaluateCost(LightBounds b, Bounds3f bounds, int dim)
        {
            // Evaluate direction bounds measure for _LightBounds_
            Float theta_o = std.acos(b.cosTheta_o), theta_e = std.acos(b.cosTheta_e);
            Float theta_w = std.min(theta_o + theta_e, Pi);
            Float sinTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(b.cosTheta_o));
            Float M_omega = 2 * Pi * (1 - b.cosTheta_o) +
                            Pi / 2 *
                                (2 * theta_w * sinTheta_o - std.cos(theta_o - 2 * theta_w) -
                                 2 * theta_o * sinTheta_o + b.cosTheta_o);

            // Return complete cost estimate for _LightBounds_
            Float Kr = pbrt.MaxComponentValue(bounds.Diagonal()) / bounds.Diagonal()[dim];
            return b.phi * M_omega * Kr * b.bounds.SurfaceArea();
        }

        // BVHLightSampler Private Members
        pstd.vector<Light> lights;
        pstd.vector<Light> infiniteLights;
        Bounds3f allLightBounds;
        pstd.vector<LightBVHNode> nodes;
        HashMap<Light, uint32_t> lightToBitTrail;
    }

    // ExhaustiveLightSampler Definition
    public sealed unsafe class ExhaustiveLightSampler : LightSampler
    {
        // ExhaustiveLightSampler Method Definitions
        public ExhaustiveLightSampler(pstd.span<Light> lights, Allocator alloc)
        {
            this.lights = new pstd.vector<Light>();
            for (ulong i = 0; i < lights.size(); ++i)
                this.lights.push_back(lights[i]);
            boundedLights = new pstd.vector<Light>();
            infiniteLights = new pstd.vector<Light>();
            lightBounds = new pstd.vector<LightBounds>();
            lightToBoundedIndex = new HashMap<Light, size_t>(alloc);

            for (ulong i = 0; i < lights.size(); ++i)
            {
                Light light = lights[i];
                pstd.optional<LightBounds> lb = light.Bounds();
                if (lb)
                {
                    lightToBoundedIndex.Insert(light, boundedLights.size());
                    lightBounds.push_back(lb.value());
                    boundedLights.push_back(light);
                }
                else
                    infiniteLights.push_back(light);
            }
        }

        public override pstd.optional<SampledLight> Sample(LightSampleContext ctx, Float u)
        {
            Float pInfinite = (Float)(infiniteLights.size()) /
                              (Float)(infiniteLights.size() + (!lightBounds.empty() ? 1UL : 0UL));

            // Note: shared with BVH light sampler...
            if (u < pInfinite)
            {
                u /= pInfinite;
                int index = std.min((int)(u * infiniteLights.size()), (int)infiniteLights.size() - 1);
                Float pdf = pInfinite * 1.0f / infiniteLights.size();
                return new SampledLight { light = infiniteLights[(ulong)index], p = pdf };
            }
            else
            {
                u = std.min<Float>((u - pInfinite) / (1 - pInfinite), (Float)pbrt.OneMinusEpsilon);

                uint64_t seed = pbrt.MixBits(pbrt.FloatToBits(u));
                WeightedReservoirSampler<Light> wrs = new WeightedReservoirSampler<Light>(seed);

                for (ulong i = 0; i < boundedLights.size(); ++i)
                    wrs.Add(boundedLights[i], lightBounds[i].Importance(ctx.p(), ctx.n));

                if (wrs.HasSample() == 0)
                    return default;

                Float pdf = (1.0f - pInfinite) * wrs.SampleProbability();
                return new SampledLight { light = wrs.GetSample(), p = pdf };
            }
        }

        public override Float PMF(LightSampleContext ctx, Light light)
        {
            if (!lightToBoundedIndex.HasKey(light))
                return 1.0f / (infiniteLights.size() + (!lightBounds.empty() ? 1UL : 0UL));

            Float importanceSum = 0;
            Float lightImportance = 0;
            for (ulong i = 0; i < boundedLights.size(); ++i)
            {
                Float importance = lightBounds[i].Importance(ctx.p(), ctx.n);
                importanceSum += importance;
                if (light == boundedLights[i])
                    lightImportance = importance;
            }
            Float pInfinite = (Float)(infiniteLights.size()) /
                              (Float)(infiniteLights.size() + (!lightBounds.empty() ? 1UL : 0UL));
            Float pdf = lightImportance / importanceSum * (1.0f - pInfinite);
            return pdf;
        }

        public override pstd.optional<SampledLight> Sample(Float u)
        {
            if (lights.empty())
                return default;

            int lightIndex = std.min((int)(u * lights.size()), (int)lights.size() - 1);
            return new SampledLight { light = lights[(ulong)lightIndex], p = 1.0f / lights.size() };
        }

        public override Float PMF(Light light)
        {
            if (lights.empty())
                return 0;
            return 1.0f / lights.size();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ExhaustiveLightSampler lightBounds: %s]", lightBounds);
        }

        pstd.vector<Light> lights, boundedLights, infiniteLights;
        pstd.vector<LightBounds> lightBounds;
        HashMap<Light, size_t> lightToBoundedIndex;
    }

    public abstract unsafe partial class LightSampler
    {
        public static LightSampler Create(string name, pstd.span<Light> lights, Allocator alloc)
        {
            if (name == "uniform")
                return new UniformLightSampler(lights, alloc);
            else if (name == "power")
                return new PowerLightSampler(lights, alloc);
            else if (name == "bvh")
                return new BVHLightSampler(lights, alloc);
            else if (name == "exhaustive")
                return new ExhaustiveLightSampler(lights, alloc);
            else
            {
                pbrt.Error(pbrt.StringPrintf("Light sample distribution type \"%s\" unknown. Using \"bvh\".", name));
                return new BVHLightSampler(lights, alloc);
            }
        }
    }

}  // namespace pbrt
