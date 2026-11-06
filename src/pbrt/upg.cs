// (not in pbrt-v4) cpu/upg.cs
//
// Unbiased photon gathering (UPG): Hao Qin, Xin Sun, Qiming Hou, Baining Guo, Kun Zhou, "Unbiased Photon Gathering for Light
// Transport Simulation", ACM Trans. Graph. (SIGGRAPH Asia) 34(6), 2015, combined with bidirectional path tracing by multiple
// importance sampling (paper Sec. 4). Everything about participating media is OUR OWN extension (the paper is surface-only);
// the vertex abstraction is the one of SmallUPBP (Krivanek et al., MIT licence: one vertex record for surface and medium
// vertices, one sum of strategy ratios with a merging term per vertex) and nothing of its code is used here, only the
// formulas of its MIS weights (see /docs/upg.md).
//
// The pieces:
//   UpgMis, UpgTerms, UpgMisQuery  the MIS terms of the gathering techniques, added to BDPT.MISSum
//   UpgBound                       the angular bound of the Bernoulli trials: a rectangle in the space of the random numbers
//   UpgPhotonGrid                  the fixed-radius photon search
//   UpgWalker                      the tentative rays (they are the walk of bdpt, stopped at its first vertex)
//   UpgIntegrator                  Integrator "upg"

using System;
using System.Collections.Generic;
using System.Threading;

namespace pbrt
{
    using static pbrt;

    // UpgStats Definition: counters for the benchmarks (the cost of the technique)
    public static class UpgStats
    {
        public const int HistogramSize = 102;
        public static long pairs, pairsEvaluated, trials, truncated, boundedPairs, shadowRays, photonsStored, gathers;
        public static long[] trialHistogram = new long[HistogramSize];
        public static long photonTests;
        // Where the time goes (Stopwatch ticks, summed over the threads)
        public static long ticksSearch, ticksShadow, ticksTrials, ticksBdpt;
        public static double secondsLight, secondsGrid, secondsEye;

        public static void Reset()
        {
            pairs = pairsEvaluated = trials = truncated = boundedPairs = shadowRays = photonsStored = gathers = photonTests = 0;
            for (int i = 0; i < HistogramSize; ++i)
            {
                trialHistogram[i] = 0;
            }
            secondsLight = secondsGrid = secondsEye = 0;
            ticksSearch = ticksShadow = ticksTrials = ticksBdpt = 0;
        }

        public static string Report()
        {
            double meanTrials = pairsEvaluated > 0 ? (double)trials / pairsEvaluated : 0;
            System.Text.StringBuilder histogram = new System.Text.StringBuilder();
            for (int i = 0; i < HistogramSize; ++i)
            {
                if (trialHistogram[i] != 0)
                {
                    histogram.Append(i).Append(':').Append(trialHistogram[i]).Append(' ');
                }
            }
            Console.Error.WriteLine("UPG trial histogram (number of trials: connections, the last bucket is " + (HistogramSize - 1) + " or more): " + histogram);
            double perTick = 1.0 / System.Diagnostics.Stopwatch.Frequency;
            Console.Error.WriteLine("UPG connection shadow rays " + shadowRays + "; thread-seconds: bdpt strategies " + (ticksBdpt * perTick) + ", photon search " +
                                    (ticksSearch * perTick) + ", connection shadow rays " + (ticksShadow * perTick) + ", Bernoulli trials " + (ticksTrials * perTick));
            return StringPrintf("UPG stats: gathers %d, photon tests %d, pairs in radius %d, connections evaluated %d, Bernoulli trials %d " +
                                "(mean %f per connection), truncated %d, bounded connections %d, photons %d, light %f s, grid %f s, eye %f s",
                                gathers, photonTests, pairs, pairsEvaluated, trials, (Float)meanTrials, truncated, boundedPairs,
                                photonsStored, (Float)secondsLight, (Float)secondsGrid, (Float)secondsEye);
        }
    }

    // UpgTerms Definition: the gathering techniques that share the sub-path densities of each BDPT strategy of one path
    public sealed class UpgTerms
    {
        // The number of light vertices j of a strategy: its coefficient is bdptCoefficient plus w[j], the sum over the gathering
        // techniques with the same densities of the sub-paths of (n_L p_c)^beta
        public Float[] w = new Float[64];
        public Float[] numeratorA = new Float[64];
        public Float[] numeratorB = new Float[64];
        public Float bdptCoefficient;
        public Float beta;
        public int count;
        public bool anyGathering;

        public void Reset(int n, Float beta)
        {
            count = n;
            this.beta = beta;
            for (int i = 0; i < n; ++i)
            {
                w[i] = 0;
                numeratorA[i] = 0;
                numeratorB[i] = 0;
            }
            bdptCoefficient = 1;
            anyGathering = false;
        }

        public Float Coefficient(int j)
        {
            return bdptCoefficient + (j >= 0 && j < count ? w[j] : 0);
        }

        // The ratio r of the densities of a BDPT strategy to the sample's, to the power beta, times the coefficient of the strategy
        public Float Scale(int j, Float r)
        {
            Float rb = beta == 1 ? r : (beta == 2 ? r * r : MathF.Pow(r, beta));
            return rb * Coefficient(j);
        }

        // The density^beta of a technique that took a sample: a BDPT strategy (variant 0), or a gathering technique that keeps the
        // vertex m (variant 1: the vertex of the light sub-path is kept and the camera sub-path shoots the tentative ray;
        // variant 2: the other way round)
        public Float Numerator(int variant, int m)
        {
            if (variant == 0)
            {
                return bdptCoefficient;
            }
            if (m < 0 || m >= count)
            {
                return 0;
            }
            return variant == 1 ? numeratorA[m] : numeratorB[m];
        }

        [ThreadStatic] static UpgTerms cache;

        public static UpgTerms Get()
        {
            if (cache == null)
            {
                cache = new UpgTerms();
            }
            return cache;
        }
    }

    // UpgMisQuery Definition: the technique that took the sample, as BDPT.MISWeight needs it
    public sealed class UpgMisQuery
    {
        public UpgMis mis;
        public int variant;
        public int m;
        public Float numerator;

        public UpgMisQuery(UpgMis mis, int variant, int m)
        {
            this.mis = mis;
            this.variant = variant;
            this.m = m;
        }

        public void Select(UpgTerms terms)
        {
            numerator = terms.Numerator(variant, m);
        }
    }

    // UpgMis Definition
    //
    // A path with k edges has vertices x_0 .. x_k. A gathering technique "keeps" the vertex x_m, 1 <= m <= k-1, and there are k-1 of
    // them (paper Sec. 4). For each m exactly one of two variants exists, chosen by the path (so every technique that can sample
    // the path agrees, which the weights need):
    //   variant 1 (paper: remove z_t'): the light sub-path x_0 .. x_m is kept with its photon x_m, the camera sub-path x_k .. x_{m+1}
    //     shoots the tentative ray from x_{m+1}. Its density is n_L p_BDPT(s = m+1) p_c, p_c = min(V p_E(x_m), 1) where p_E is
    //     the density (per area, or per volume for a medium vertex) of sampling x_m from x_{m+1} given x_{m+2}.
    //   variant 2 (paper: remove y_s'): the camera sub-path x_k .. x_m is kept, the light sub-path x_0 .. x_{m-1} shoots from
    //     x_{m-1}. Its density is n_L p_BDPT(s = m) p_c with the light direction density p_L(x_m) of x_m from x_{m-1}.
    // V is the measure of the merging neighbourhood: pi d^2 for a vertex on a surface, 4/3 pi d^3 for a vertex in a medium (the
    // ball of the volume), like the etaSurf / etaPP3D of UPBP.
    public sealed class UpgMis
    {
        public Float beta = 2;
        public Float lightPaths = 1;
        public Float radius = 1;
        public int minDepth = 2;
        public int maxDepth = 5;
        // The gathering techniques are preferred: a BDPT strategy only samples the paths that have no gathering technique
        public bool gatheringPreferred = false;

        public const int None = 0, KeepLightVertex = 1, KeepCameraVertex = 2;

        public Float Measure(Vertex v)
        {
            if (v.type == VertexType.Medium)
            {
                return (Float)(4.0 / 3.0) * Pi * radius * radius * radius;
            }
            return Pi * radius * radius;
        }

        // Whether the vertex can be connected as the kept vertex or the shooter of a gathering technique
        public static bool Connectible(Vertex v, bool ignoreDelta)
        {
            if (v == null || (v.type != VertexType.Surface && v.type != VertexType.Medium))
            {
                return false;
            }
            if (!ignoreDelta && v.delta)
            {
                return false;
            }
            return v.IsConnectible();
        }

        // A memory of the last glossiness values of the thread: every strategy of a sample asks for the same few (vertex, direction)
        // pairs, and each costs several BSDF density evaluations
        struct GlossinessEntry
        {
            public Vertex vertex;
            public Point3f position;
            public Point3f to;
            public Float value;
        }

        [ThreadStatic] static GlossinessEntry[] glossinessCache;

        // How much the scattering at v depends on the direction: the peak of its density around the mirror direction (or straight
        // through) for the direction of arrival toward the point to
        public static Float Glossiness(Vertex v, Point3f to)
        {
            if (v == null || !(bool)v.bsdf)
            {
                return 0;
            }
            if (glossinessCache == null)
            {
                glossinessCache = new GlossinessEntry[512];
            }
            Point3f position = v.p();
            int hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v) * 31 + BitConverter.SingleToInt32Bits(to.x) * 17 +
                       BitConverter.SingleToInt32Bits(to.y) * 7 + BitConverter.SingleToInt32Bits(to.z);
            int slot = (hash ^ (hash >> 9)) & 511;
            GlossinessEntry entry = glossinessCache[slot];
            if (entry.vertex == v && entry.to.x == to.x && entry.to.y == to.y && entry.to.z == to.z && entry.position.x == position.x &&
                entry.position.y == position.y && entry.position.z == position.z)
            {
                return entry.value;
            }
            Float value = ComputeGlossiness(v, to);
            entry.vertex = v;
            entry.position = position;
            entry.to = to;
            entry.value = value;
            glossinessCache[slot] = entry;
            return value;
        }

        static Float ComputeGlossiness(Vertex v, Point3f to)
        {
            Vector3f wo = to - v.p();
            if (LengthSquared(wo) == 0)
            {
                return 0;
            }
            wo = Normalize(wo);
            Vector3f woLocal = v.bsdf.RenderToLocal(wo);
            Vector3f mirror = v.bsdf.LocalToRender(new Vector3f(-woLocal.x, -woLocal.y, woLocal.z));
            Float peak = Math.Max(v.bsdf.PDF(wo, mirror), v.bsdf.PDF(wo, -wo));
            if (v.type == VertexType.Medium)
            {
                peak = Math.Max(peak, v.bsdf.PDF(wo, wo));
            }
            return peak;
        }

        // The variant of the technique that keeps x_m of a path with k edges, from the vertices x_{m-2} .. x_{m+2} (null where there
        // is none): 0 if there is none. retainedIgnoresDelta: the kept vertex is a connection vertex of a sample (the flag of a
        // vertex that is connected is not a property of the path).
        public int Variant(int k, int m, Vertex xmm2, Vertex xmm1, Vertex xm, Vertex xp1, Vertex xp2, bool retainedIgnoresDelta)
        {
            int depth = k - 1;
            if (k < 3 || m < 1 || m > k - 1 || depth < minDepth || depth > maxDepth)
            {
                return None;
            }
            bool keptOk = Connectible(xm, retainedIgnoresDelta);
            if (!keptOk)
            {
                return None;
            }
            bool a = m <= k - 2 && Connectible(xp1, false);
            bool b = m >= 2 && Connectible(xmm1, false);
            if (a && b)
            {
                // The paper's Appendix B: remove the vertex of the less glossy reflection (here: the shooter of the less glossy one)
                Point3f toXp1 = xp2 != null ? xp2.p() : xp1.p() + (xp1.p() - xm.p());
                Point3f toXmm1 = xmm2 != null ? xmm2.p() : xmm1.p() + (xmm1.p() - xm.p());
                Float glossA = Glossiness(xp1, toXp1);
                Float glossB = Glossiness(xmm1, toXmm1);
                return glossA <= glossB ? KeepLightVertex : KeepCameraVertex;
            }
            if (a)
            {
                return KeepLightVertex;
            }
            if (b)
            {
                return KeepCameraVertex;
            }
            return None;
        }

        static Vertex At(Vertex[] lightVertices, Vertex[] cameraVertices, int s, int t, int i)
        {
            if (i < 0 || i > s + t - 1)
            {
                return null;
            }
            return i < s ? lightVertices[i] : cameraVertices[t - 1 - (i - s)];
        }

        // The probability of the gathering technique that keeps the vertex: the approximation p_c = min(V p, 1) of the paper's
        // Eq. 24, with p the density of sampling the kept vertex from the shooter, per area or per volume
        Float Probability(Vertex kept, Float density)
        {
            if (!(density > 0))
            {
                return 0;
            }
            return Math.Min(Measure(kept) * density, 1);
        }

        // The terms of the gathering techniques of the path that the strategy (s, t) connects, with the densities of its vertices
        // as BDPT.MISSum has made them (the reverse densities of the vertices next to the connection are the current ones)
        public UpgTerms Terms(Vertex[] lightVertices, Vertex[] cameraVertices, int s, int t)
        {
            UpgTerms terms = UpgTerms.Get();
            int k = s + t - 1;
            terms.Reset(k + 2, beta);
            if (k >= 3)
            {
                for (int m = 1; m <= k - 1; ++m)
                {
                    Vertex xm = At(lightVertices, cameraVertices, s, t, m);
                    int variant = Variant(k, m, At(lightVertices, cameraVertices, s, t, m - 2),
                                          At(lightVertices, cameraVertices, s, t, m - 1), xm,
                                          At(lightVertices, cameraVertices, s, t, m + 1),
                                          At(lightVertices, cameraVertices, s, t, m + 2), false);
                    if (variant == None)
                    {
                        continue;
                    }
                    bool lightOwned = m < s;
                    // The density of the kept vertex from the shooter's side: the camera direction for variant 1, the light
                    // direction for variant 2
                    Float density = variant == KeepLightVertex ? (lightOwned ? xm.pdfRev : xm.pdfFwd)
                                                               : (lightOwned ? xm.pdfFwd : xm.pdfRev);
                    Float pc = Probability(xm, density);
                    if (!(pc > 0))
                    {
                        continue;
                    }
                    Float a = lightPaths * pc;
                    Float ab = beta == 1 ? a : (beta == 2 ? a * a : MathF.Pow(a, beta));
                    terms.anyGathering = true;
                    if (variant == KeepLightVertex)
                    {
                        terms.w[m + 1] += ab;
                        terms.numeratorA[m] = ab;
                    }
                    else
                    {
                        terms.w[m] += ab;
                        terms.numeratorB[m] = ab;
                    }
                }
            }
            terms.bdptCoefficient = (gatheringPreferred && terms.anyGathering) ? 0 : 1;
            return terms;
        }
    }

    // UpgBound Definition
    //
    // The angular bound of the tentative rays (paper Sec. 3.4): the cone from the shooter that encloses the gathering ball. The
    // tentative ray is the BxDF's own Sample_f with its random numbers drawn from a rectangle of the unit square that contains
    // every number whose sample is in the cone, so the sample is the BxDF's sample conditioned on the rectangle, and
    // p_b = the area of the rectangle is analytic (it is not an estimate). The rectangle is found from the boundary of the cone,
    // mapped back through the sampling routine (diffuse reflection: the inverse of the concentric disk; rough conductor: the inverse
    // of the visible normal sampling), plus the images of the boundary of the sampling domain and a coarse grid; it grows by
    // a margin that covers the sampling of the boundary. A BxDF without such a map has the whole square (p_b = 1).
    public struct UpgBound
    {
        public Float u0Lo, u0Hi, u1Lo, u1Len;

        public static UpgBound Full
        {
            get
            {
                UpgBound b = new UpgBound();
                b.u0Lo = 0;
                b.u0Hi = 1;
                b.u1Lo = 0;
                b.u1Len = 1;
                return b;
            }
        }

        public Float Area()
        {
            return (u0Hi - u0Lo) * u1Len;
        }

        public bool IsFull()
        {
            return u0Lo <= 0 && u0Hi >= 1 && u1Len >= 1;
        }

        public Point2f Sample(Float a, Float b)
        {
            Float u0 = u0Lo + a * (u0Hi - u0Lo);
            Float u1 = u1Lo + b * u1Len;
            if (u1 >= 1)
            {
                u1 -= 1;
            }
            if (u0 >= 1)
            {
                u0 = 0.99999994f;
            }
            if (u1 >= 1 || u1 < 0)
            {
                u1 = Math.Min(Math.Max(u1, 0), 0.99999994f);
            }
            return new Point2f(u0, u1);
        }

        // Collects the numbers of the samples in the cone
        sealed class Builder
        {
            public Float u0Min = 2, u0Max = -1, u1Min = 2, u1Max = -1;
            public List<Float> u1s = new List<Float>();
            public int count = 0;
            public bool containsAxisPoint = false;

            public void Add(Float u0, Float u1)
            {
                u0Min = Math.Min(u0Min, u0);
                u0Max = Math.Max(u0Max, u0);
                u1Min = Math.Min(u1Min, u1);
                u1Max = Math.Max(u1Max, u1);
                u1s.Add(u1);
                ++count;
            }

            // The rectangle, with the margin; wrapU1: the second number is an angle (u1 = angle / 2 pi) whose interval may wrap
            public UpgBound Build(bool wrapU1)
            {
                if (count == 0)
                {
                    return Full;
                }
                UpgBound b = new UpgBound();
                Float size0 = u0Max - u0Min;
                Float margin0 = 0.03f * size0 + 1e-5f;
                b.u0Lo = Math.Max(0, u0Min - margin0);
                b.u0Hi = Math.Min(1, u0Max + margin0);
                if (!wrapU1)
                {
                    Float size1 = u1Max - u1Min;
                    Float margin1 = 0.03f * size1 + 1e-5f;
                    b.u1Lo = Math.Max(0, u1Min - margin1);
                    Float hi = Math.Min(1, u1Max + margin1);
                    b.u1Len = hi - b.u1Lo;
                    return b;
                }
                if (containsAxisPoint)
                {
                    b.u0Lo = 0;
                    b.u1Lo = 0;
                    b.u1Len = 1;
                    return b;
                }
                // The interval of the angle: the complement of the largest gap between the angles
                u1s.Sort();
                Float largestGap = 0;
                int gapEnd = 0;
                for (int i = 0; i < u1s.Count; ++i)
                {
                    Float prev = i == 0 ? u1s[u1s.Count - 1] - 1 : u1s[i - 1];
                    Float gap = u1s[i] - prev;
                    if (gap > largestGap)
                    {
                        largestGap = gap;
                        gapEnd = i;
                    }
                }
                Float length = 1 - largestGap;
                if (largestGap < 0.5f)
                {
                    b.u1Lo = 0;
                    b.u1Len = 1;
                    return b;
                }
                Float margin = 0.03f * length + 1e-5f;
                length += 2 * margin;
                if (length >= 1)
                {
                    b.u1Lo = 0;
                    b.u1Len = 1;
                    return b;
                }
                Float start = u1s[gapEnd] - margin;
                if (start < 0)
                {
                    start += 1;
                }
                b.u1Lo = start;
                b.u1Len = length;
                return b;
            }
        }

        static void Basis(Vector3f n, out Vector3f e1, out Vector3f e2)
        {
            Float sign = n.z >= 0 ? 1 : -1;
            Float a = -1 / (sign + n.z);
            Float b = n.x * n.y * a;
            e1 = new Vector3f(1 + sign * n.x * n.x * a, sign * b, -sign * n.x);
            e2 = new Vector3f(b, sign + n.y * n.y * a, -n.y);
        }

        const int BoundarySamples = 128;

        // The bound for a shooter whose scattering is the bsdf (with the direction of arrival wo), for the cone from origin around
        // the ball of the given radius at center
        public static UpgBound Compute(BSDF bsdf, Vector3f wo, Point3f origin, Point3f center, Float radius)
        {
            Vector3f toCenter = center - origin;
            Float distance = Length(toCenter);
            if (!(distance > radius * 1.001f))
            {
                return Full;
            }
            Vector3f axis = toCenter / distance;
            Float sinA = Math.Min(radius / distance, 1);
            Float cosA = MathF.Sqrt(Math.Max(0, 1 - sinA * sinA));
            BxDF bxdf = bsdf.GetBxDF();
            Vector3f woLocal = bsdf.RenderToLocal(wo);
            Vector3f axisLocal = bsdf.RenderToLocal(axis);
            if (bxdf is DiffuseBxDF)
            {
                return DiffuseBound(woLocal, axisLocal, cosA, sinA);
            }
            ConductorBxDF conductor = bxdf as ConductorBxDF;
            if (conductor != null && !conductor.IsSpecular())
            {
                if (woLocal.z < 0)
                {
                    // The sampler flips the visible normal into the upper hemisphere, so the sample for wo is minus the sample for -wo
                    woLocal = -woLocal;
                    axisLocal = -axisLocal;
                }
                return ConductorBound(conductor.Distribution(), woLocal, axisLocal, cosA, sinA);
            }
            return Full;
        }

        static UpgBound DiffuseBound(Vector3f woLocal, Vector3f axisLocal, Float cosA, Float sinA)
        {
            // The reflected direction is on the side of wo
            Vector3f c = new Vector3f(axisLocal.x, axisLocal.y, woLocal.z < 0 ? -axisLocal.z : axisLocal.z);
            Basis(c, out Vector3f e1, out Vector3f e2);
            Builder builder = new Builder();
            for (int i = 0; i < BoundarySamples; ++i)
            {
                Float phi = 2 * Pi * i / BoundarySamples;
                Vector3f w = cosA * c + sinA * (MathF.Cos(phi) * e1 + MathF.Sin(phi) * e2);
                if (w.z >= 0)
                {
                    Point2f u = InvertUniformDiskConcentricSample(new Point2f(w.x, w.y));
                    builder.Add(u.x, u.y);
                }
                // The horizon: the points of the unit circle that are in the cone
                Float theta = 2 * Pi * i / BoundarySamples;
                Vector3f h = new Vector3f(MathF.Cos(theta), MathF.Sin(theta), 0);
                if (Dot(h, c) >= cosA)
                {
                    Point2f u = InvertUniformDiskConcentricSample(new Point2f(h.x, h.y));
                    builder.Add(u.x, u.y);
                }
            }
            if (c.z > 0)
            {
                Point2f u = InvertUniformDiskConcentricSample(new Point2f(c.x, c.y));
                builder.Add(u.x, u.y);
            }
            if (c.z >= cosA)
            {
                builder.Add(0.5f, 0.5f);
            }
            return builder.Build(false);
        }

        static bool ConductorForward(TrowbridgeReitzDistribution distrib, Vector3f wo, Point2f u, Vector3f axis, Float cosA)
        {
            Vector3f wm = distrib.Sample_wm(wo, u);
            Vector3f wi = Reflect(wo, wm);
            if (!SameHemisphere(wo, wi))
            {
                return false;
            }
            return Dot(wi, axis) >= cosA;
        }

        // The numbers of the visible normal sampling that give the half vector of the direction wi; false if there are none
        static bool ConductorInverse(TrowbridgeReitzDistribution distrib, Vector3f wo, Vector3f wi, out Float u0, out Float u1)
        {
            u0 = 0;
            u1 = 0;
            Float ax = distrib.AlphaX(), ay = distrib.AlphaY();
            Vector3f wm = wo + wi;
            if (LengthSquared(wm) == 0)
            {
                return false;
            }
            wm = Normalize(wm);
            if (wm.z < 0)
            {
                wm = -wm;
            }
            Vector3f wh = Normalize(new Vector3f(ax * wo.x, ay * wo.y, wo.z));
            if (wh.z < 0)
            {
                wh = -wh;
            }
            Vector3f t1 = wh.z < 0.99999f ? Normalize(Cross(new Vector3f(0, 0, 1), wh)) : new Vector3f(1, 0, 0);
            Vector3f t2 = Cross(wh, t1);
            Vector3f nh = Normalize(new Vector3f(wm.x / ax, wm.y / ay, wm.z));
            Float px = Dot(nh, t1);
            Float pyWarped = Dot(nh, t2);
            Float pz = Dot(nh, wh);
            if (pz < 0)
            {
                return false;
            }
            Float s = (1 + wh.z) / 2;
            Float h = MathF.Sqrt(Math.Max(0, 1 - px * px));
            if (!(s > 0))
            {
                return false;
            }
            Float y0 = (pyWarped - (1 - s) * h) / s;
            if (Math.Abs(y0) > h * 1.0001f + 1e-6f)
            {
                return false;
            }
            u0 = Math.Min(px * px + y0 * y0, 1);
            Float angle = MathF.Atan2(y0, px);
            if (angle < 0)
            {
                angle += 2 * Pi;
            }
            u1 = Math.Min(angle / (2 * Pi), 0.99999994f);
            return true;
        }

        static UpgBound ConductorBound(TrowbridgeReitzDistribution distrib, Vector3f woLocal, Vector3f axisLocal, Float cosA, Float sinA)
        {
            if (!(woLocal.z > 0))
            {
                return Full;
            }
            Basis(axisLocal, out Vector3f e1, out Vector3f e2);
            Builder builder = new Builder();
            for (int i = 0; i < BoundarySamples; ++i)
            {
                Float phi = 2 * Pi * i / BoundarySamples;
                Vector3f w = cosA * axisLocal + sinA * (MathF.Cos(phi) * e1 + MathF.Sin(phi) * e2);
                if (w.z > 0 && ConductorInverse(distrib, woLocal, w, out Float u0, out Float u1))
                {
                    builder.Add(u0, u1);
                }
                // The horizon of the reflected direction
                Vector3f h = new Vector3f(MathF.Cos(phi), MathF.Sin(phi), 1e-6f);
                if (Dot(h, axisLocal) >= cosA && ConductorInverse(distrib, woLocal, Normalize(h), out Float v0, out Float v1))
                {
                    builder.Add(v0, v1);
                }
            }
            // The images of the boundary of the sampling domain (r = 1: the silhouette of the visible normals) that are in the cone
            for (int i = 0; i < BoundarySamples; ++i)
            {
                Float a = (i + 0.5f) / BoundarySamples;
                Point2f u = new Point2f(1, a);
                if (ConductorForward(distrib, woLocal, u, axisLocal, cosA))
                {
                    builder.Add(u.x, u.y);
                }
            }
            // The origin of the disk (r = 0) and a coarse grid for what the boundaries do not show
            if (ConductorForward(distrib, woLocal, new Point2f(0, 0.3f), axisLocal, cosA))
            {
                builder.containsAxisPoint = true;
                builder.Add(0, 0.3f);
            }
            const int grid = 9;
            for (int i = 0; i < grid; ++i)
            {
                for (int j = 0; j < grid; ++j)
                {
                    Point2f u = new Point2f((i + 0.5f) / grid, (j + 0.5f) / grid);
                    if (ConductorForward(distrib, woLocal, u, axisLocal, cosA))
                    {
                        builder.Add(u.x, u.y);
                    }
                }
            }
            return builder.Build(true);
        }
    }

    // UpgPhotonGrid Definition: the endpoints of the light sub-paths in a hash grid with cells of the gathering radius
    public sealed class UpgPhotonGrid
    {
        public Point3f[] positions;
        public int[] pathIndex;
        public int[] vertexIndex;
        int[] cellStart;
        int[] sortedPhotons;
        Float cellSize;
        Point3f origin;
        int hashSize;
        public int count;

        public void Build(List<Point3f> photonPositions, List<int> paths, List<int> vertices, Bounds3f bounds, Float radius)
        {
            count = photonPositions.Count;
            positions = photonPositions.ToArray();
            pathIndex = paths.ToArray();
            vertexIndex = vertices.ToArray();
            cellSize = Math.Max(radius, 1e-6f);
            origin = bounds.pMin;
            hashSize = NextPrime(Math.Max(count, 16));
            cellStart = new int[hashSize + 1];
            sortedPhotons = new int[count];
            int[] cellOf = new int[count];
            for (int i = 0; i < count; ++i)
            {
                int h = Cell(positions[i]);
                cellOf[i] = h;
                ++cellStart[h + 1];
            }
            for (int i = 0; i < hashSize; ++i)
            {
                cellStart[i + 1] += cellStart[i];
            }
            int[] fill = new int[hashSize];
            for (int i = 0; i < count; ++i)
            {
                int h = cellOf[i];
                sortedPhotons[cellStart[h] + fill[h]++] = i;
            }
        }

        static int NextPrime(int n)
        {
            if (n % 2 == 0)
            {
                ++n;
            }
            while (true)
            {
                bool prime = true;
                for (int d = 3; (long)d * d <= n; d += 2)
                {
                    if (n % d == 0)
                    {
                        prime = false;
                        break;
                    }
                }
                if (prime)
                {
                    return n;
                }
                n += 2;
            }
        }

        int CellHash(int ix, int iy, int iz)
        {
            uint h = (uint)(ix * 73856093) ^ (uint)(iy * 19349663) ^ (uint)(iz * 83492791);
            return (int)(h % (uint)hashSize);
        }

        int Cell(Point3f p)
        {
            return CellHash((int)MathF.Floor((p.x - origin.x) / cellSize), (int)MathF.Floor((p.y - origin.y) / cellSize),
                            (int)MathF.Floor((p.z - origin.z) / cellSize));
        }

        // Appends the photons within radius of p to found (each photon once, a hash cell can be visited twice)
        public void Query(Point3f p, Float radius, List<int> found, ref long tests)
        {
            found.Clear();
            Float r2 = radius * radius;
            int x0 = (int)MathF.Floor((p.x - radius - origin.x) / cellSize), x1 = (int)MathF.Floor((p.x + radius - origin.x) / cellSize);
            int y0 = (int)MathF.Floor((p.y - radius - origin.y) / cellSize), y1 = (int)MathF.Floor((p.y + radius - origin.y) / cellSize);
            int z0 = (int)MathF.Floor((p.z - radius - origin.z) / cellSize), z1 = (int)MathF.Floor((p.z + radius - origin.z) / cellSize);
            int lastHash0 = -1, lastHash1 = -1, lastHash2 = -1;
            for (int z = z0; z <= z1; ++z)
            {
                for (int y = y0; y <= y1; ++y)
                {
                    for (int x = x0; x <= x1; ++x)
                    {
                        int h = CellHash(x, y, z);
                        // Cells of the neighbourhood that hash to the same bucket are visited once
                        if (h == lastHash0 || h == lastHash1 || h == lastHash2)
                        {
                            continue;
                        }
                        lastHash2 = lastHash1;
                        lastHash1 = lastHash0;
                        lastHash0 = h;
                        for (int i = cellStart[h]; i < cellStart[h + 1]; ++i)
                        {
                            int photon = sortedPhotons[i];
                            ++tests;
                            if (LengthSquared(positions[photon] - p) < r2)
                            {
                                found.Add(photon);
                            }
                        }
                    }
                }
            }
            // (two different cells of the neighbourhood can share a bucket that is not adjacent in the loop: remove repeats)
            if (found.Count > 1)
            {
                found.Sort();
                int w = 1;
                for (int i = 1; i < found.Count; ++i)
                {
                    if (found[i] != found[w - 1])
                    {
                        found[w++] = found[i];
                    }
                }
                found.RemoveRange(w, found.Count - w);
            }
        }
    }

    // UpgWalker Definition: the Bernoulli trials. A tentative ray is the walk of bdpt from the shooter, stopped at its first vertex:
    // the same sampling of the BSDF (or the phase function), the same passage of the boundaries between media, the same collisions
    // in a medium, so the probability that it ends inside the ball is exactly the probability p_c that the sub-path has a vertex there.
    public sealed unsafe class UpgWalker
    {
        sealed class FirstVertexHook : PathWalkHook
        {
            public bool hit;
            public Point3f p;
            public int iterations;

            public override bool BeginIteration(WalkContext ctx)
            {
                return ++iterations <= 256;
            }

            public override bool BeginCollision(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                SampledSpectrum sigma_maj, SampledSpectrum T_maj)
            {
                return true;
            }

            public override bool BeginScatter(WalkContext ctx)
            {
                return true;
            }

            public override bool OnScatterVertex(WalkContext ctx, Medium medium, Point3f p, MediumProperties mp,
                                                 out MediumInteraction intr, out BSDF phaseBsdf)
            {
                hit = true;
                this.p = p;
                intr = null;
                phaseBsdf = default;
                return false;
            }

            public override void OnPhaseSampled(WalkContext ctx, MediumInteraction intr, Point3f p, BSDFSample ps)
            {
            }

            public override bool EndAfterMedium(WalkContext ctx, bool terminated)
            {
                return terminated;
            }

            public override void OnEscape(WalkContext ctx)
            {
            }

            public override void OnSurfaceHit(WalkContext ctx, SurfaceInteraction isect)
            {
            }

            public override bool OnBSDF(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf)
            {
                hit = true;
                p = isect.p();
                return false;
            }

            public override bool OnSurfaceScattered(WalkContext ctx, ShapeIntersection si, SurfaceInteraction isect, BSDF bsdf,
                                                    BSDFSample bs)
            {
                return false;
            }
        }

        FirstVertexHook hook = new FirstVertexHook();
        WalkContext ctx = new WalkContext();
        public Sampler sampler;
        public ScratchBuffer scratchBuffer = new ScratchBuffer(65536);

        public UpgWalker(Integrator integrator, Camera camera, Sampler sampler)
        {
            this.sampler = sampler;
            ctx.integrator = integrator;
            ctx.camera = camera;
            ctx.sampler = sampler;
            ctx.scratchBuffer = scratchBuffer;
        }

        // One Bernoulli trial: a tentative ray from the shooter, with the random numbers of its direction in the bound; true if its
        // first vertex is inside the ball
        public bool Trial(Vertex shooter, TransportMode mode, in UpgBound bound, Point3f center, Float radius2,
                          SampledWavelengths lambda)
        {
            Float uc = sampler.Get1D();
            Float ua = sampler.Get1D();
            Float ub = sampler.Get1D();
            Point2f u = bound.Sample(ua, ub);
            Interaction intr = shooter.GetInteraction();
            Vector3f wo = shooter.type == VertexType.Surface ? shooter.si.wo : shooter.mi.wo;
            pstd.optional<BSDFSample> bs = shooter.bsdf.Sample_f(wo, uc, u, mode, BxDFReflTransFlags.All);
            if (!bs || bs.value().IsSpecular())
            {
                return false;
            }
            RayDifferential ray = intr.SpawnRay(bs.value().wi);
            BoundaryStack stack = shooter.stack;
            if (shooter.type == VertexType.Surface)
            {
                MediumTracking.CrossSurface(ref stack, intr, ray.d);
            }
            ctx.lambda = lambda;
            ctx.ray = ray;
            ctx.stack = stack;
            ctx.mode = mode;
            SampledSpectrum one = new SampledSpectrum(1.0f);
            ctx.st = new PathState(one, new PolarisedLightSystem.Attenuation(one), new PolarisedLightSystem.Light(one),
                                   mode == TransportMode.Radiance, false, false);
            hook.hit = false;
            hook.iterations = 0;
            PathWalk.Run(ctx, hook);
            return hook.hit && LengthSquared(hook.p - center) < radius2;
        }

        // The estimate N_b / p_b of the reciprocal of the probability that the sub-path has a vertex in the ball (paper Eq. 15):
        // the number of trials up to and including the first success, over the area of the bound. maxTrials ends a run that is far
        // out in the tail (counted in UpgStats.truncated).
        public Float ReciprocalEstimate(Vertex shooter, TransportMode mode, in UpgBound bound, Point3f center, Float radius,
                                        SampledWavelengths lambda, int maxTrials, out int trialCount)
        {
            Float radius2 = radius * radius;
            int n = 0;
            bool success = false;
            while (n < maxTrials)
            {
                ++n;
                if (Trial(shooter, mode, bound, center, radius2, lambda))
                {
                    success = true;
                    break;
                }
            }
            scratchBuffer.Reset();
            trialCount = n;
            if (!success)
            {
                Interlocked.Increment(ref UpgStats.truncated);
            }
            return n / bound.Area();
        }
    }

    // A light sub-path of the batch
    public sealed class UpgLightPath
    {
        public Vertex[] vertices;
        public int count;
    }

    // UpgIntegrator Definition
    public sealed unsafe class UpgIntegrator : Integrator
    {
        // UpgIntegrator Public Methods
        public UpgIntegrator(Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, int maxDepth,
                             int lightPaths, Float radius, int minUpgDepth, int maxUpgDepth, Float misBeta, bool gatheringOnly,
                             bool bdptOnly, bool regularize, int seed, int maxTrials, bool angularBound)
            : base(aggregate, BDPTIntegrator.EmissionLightsForBDPT(lights))
        {
            this.camera = camera;
            this.samplerPrototype = sampler;
            this.maxDepth = maxDepth;
            this.bdptMaxDepth = maxDepth;
            this.regularize = regularize;
            this.seed = seed;
            this.maxTrials = maxTrials;
            this.angularBound = angularBound;
            this.bdptOnly = bdptOnly;
            this.lightSampler = new PowerLightSampler(TextureMedium.AddEmissionLights(lights).ToArray(), new Allocator());
            Bounds2i pixelBounds = camera.GetFilm().PixelBounds();
            this.lightPathCount = lightPaths > 0 ? lightPaths : pixelBounds.Area();
            Bounds3f sceneBounds = aggregate != null ? aggregate.Bounds() : new Bounds3f();
            this.sceneBounds = sceneBounds;
            Float diagonal = sceneBounds.pMax.x >= sceneBounds.pMin.x ? Length(sceneBounds.Diagonal()) : 1;
            this.radius = radius > 0 ? radius : 0.1f * diagonal / MathF.Sqrt(lightPathCount);
            mis = new UpgMis();
            mis.beta = misBeta;
            mis.lightPaths = lightPathCount;
            mis.radius = this.radius;
            mis.minDepth = minUpgDepth;
            mis.maxDepth = Math.Min(maxUpgDepth, maxDepth);
            mis.gatheringPreferred = gatheringOnly;
        }

        public UpgMis Mis { get { return mis; } }
        public Float Radius { get { return radius; } }
        public int MaxDepth { get { return maxDepth; } }
        public int LightPathCount { get { return lightPathCount; } }
        public LightSampler UpgLightSampler { get { return lightSampler; } }
        public Camera UpgCamera { get { return camera; } }

        // The gathering radius is not part of the expectation; the tests change it between renders
        public void SetRadius(Float newRadius)
        {
            radius = newRadius;
            mis.radius = newRadius;
        }

        public override string ToString()
        {
            return StringPrintf("[ UpgIntegrator maxDepth: %d lightPaths: %d radius: %f minUpgDepth: %d maxUpgDepth: %d beta: %f ]",
                                maxDepth, lightPathCount, radius, mis.minDepth, mis.maxDepth, mis.beta);
        }

        public static UpgIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate,
                                           List<Light> lights, FileLoc loc)
        {
            MediumTracking.RequireNoPerEntryVolumes("upg");
            if (!(camera is PerspectiveCamera))
            {
                ErrorExit("Only the \"perspective\" camera is currently supported with the \"upg\" integrator.");
            }
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            int lightPaths = parameters.GetOneInt("photons", 0);
            Float radius = parameters.GetOneFloat("radius", 0);
            int minDepth = parameters.GetOneInt("minupgdepth", 2);
            int maxUpgDepth = parameters.GetOneInt("maxupgdepth", maxDepth);
            Float beta = parameters.GetOneFloat("misbeta", 2);
            bool gatheringOnly = parameters.GetOneBool("upgonly", false);
            bool bdptOnly = parameters.GetOneBool("bdptonly", false);
            bool regularize = parameters.GetOneBool("regularize", false);
            int seed = parameters.GetOneInt("seed", Options.seed);
            int maxTrials = parameters.GetOneInt("maxtrials", 1 << 20);
            bool angularBound = parameters.GetOneBool("angularbound", true);
            if (parameters.GetOneBool("russianroulette", false))
            {
                ErrorExit(loc, "\"russianroulette\" is not supported by \"upg\" (the roulette of the camera sub-path changes the density of the vertex that the photons are gathered for).");
            }
            if (gatheringOnly && bdptOnly)
            {
                ErrorExit(loc, "\"upgonly\" and \"bdptonly\" exclude each other.");
            }
            return new UpgIntegrator(camera, sampler, aggregate, lights, maxDepth, lightPaths, radius, minDepth, maxUpgDepth, beta,
                                     gatheringOnly, bdptOnly, regularize, seed, maxTrials, angularBound);
        }

        // Light sub-paths of one iteration
        UpgLightPath[] lightPaths;
        UpgPhotonGrid grid;
        SampledWavelengths iterationLambda;
        int iteration;
        ThreadLocal<UpgWalker> walkers;
        ThreadLocal<List<int>> foundBuffers = new ThreadLocal<List<int>>(() => new List<int>());

        public override void Render()
        {
            Film film = camera.GetFilm();
            Bounds2i pixelBounds = film.PixelBounds();
            int nIterations = samplerPrototype.SamplesPerPixel();
            ProgressReporter progress = new ProgressReporter(nIterations, "Rendering", Options.quiet);
            ThreadLocal<ScratchBuffer> scratchBuffers = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer(65536));
            ThreadLocal<Sampler> samplers = new ThreadLocal<Sampler>(() => samplerPrototype.Clone());
            walkers = new ThreadLocal<UpgWalker>(() => new UpgWalker(this, camera, new IndependentSampler(1, seed ^ 0x5bd1e995)));
            UpgStats.Reset();

            for (int iter = 0; iter < nIterations; ++iter)
            {
                iteration = iter;
                // One set of wavelengths for the whole iteration: the photons and the camera sub-paths are connected
                // (stratified over the iterations, and rotated by a number of the seed: otherwise every seed would use the same
                // wavelengths and the mean over seeds would be the mean at those wavelengths, not over the spectrum)
                Float uLambda = (Float)0.5;
                if (!Options.disableWavelengthJitter)
                {
                    Float shift = (Float)(Hash(seed, 0x57ab1e) & 0xffffff) / 16777216.0f;
                    uLambda = RadicalInverse(1, (uint64_t)iter) + shift;
                    uLambda -= MathF.Floor(uLambda);
                }
                iterationLambda = film.SampleWavelengths(uLambda);
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                TraceLightPaths(iter);
                watch.Stop();
                UpgStats.secondsLight += watch.Elapsed.TotalSeconds;
                watch.Restart();
                BuildGrid();
                watch.Stop();
                UpgStats.secondsGrid += watch.Elapsed.TotalSeconds;
                watch.Restart();
                int iterCopy = iter;
                ParallelFor2D(pixelBounds, (Bounds2i tileBounds) =>
                {
                    ScratchBuffer scratchBuffer = scratchBuffers.Get();
                    Sampler sampler = samplers.Get();
                    foreach (Point2i pPixel in tileBounds)
                    {
                        sampler.StartPixelSample(pPixel, iterCopy);
                        EvaluatePixel(pPixel, iterCopy, sampler, scratchBuffer, pixelBounds);
                        scratchBuffer.Reset();
                    }
                });
                watch.Stop();
                UpgStats.secondsEye += watch.Elapsed.TotalSeconds;
                lightPaths = null;
                grid = null;
                progress.Update(1);
            }
            progress.Done();
            ImageMetadata metadata = new ImageMetadata();
            metadata.renderTimeSeconds = (float)progress.ElapsedSeconds();
            metadata.samplesPerPixel = nIterations;
            camera.InitMetadata(metadata);
            film.WriteImage(metadata, 1.0f / nIterations);
            if (Environment.GetEnvironmentVariable("UPG_STATS") == "1")
            {
                Console.Error.WriteLine(UpgStats.Report());
            }
        }

        void TraceLightPaths(int iter)
        {
            lightPaths = new UpgLightPath[lightPathCount];
            int iterCopy = iter;
            ThreadLocal<ScratchBuffer> scratch = new ThreadLocal<ScratchBuffer>(() => new ScratchBuffer(65536));
            ThreadLocal<IndependentSampler> samplerLocal = new ThreadLocal<IndependentSampler>(() => new IndependentSampler(1, seed));
            ParallelFor(0, lightPathCount, (long i) =>
            {
                IndependentSampler sampler = samplerLocal.Get();
                sampler.StartPixelSample(new Point2i((int)i, 0x3fffffff), iterCopy);
                Vertex[] temp = new Vertex[maxDepth + 1];
                for (int v = 0; v < temp.Length; ++v)
                {
                    temp[v] = new Vertex();
                }
                SampledWavelengths lambda = iterationLambda;
                int n = BDPT.GenerateLightSubpath(this, ref lambda, sampler, camera, scratch.Get(), maxDepth + 1, 0, lightSampler, temp,
                                                  regularize, false);
                UpgLightPath path = new UpgLightPath();
                path.count = n;
                path.vertices = new Vertex[n];
                Array.Copy(temp, path.vertices, n);
                lightPaths[i] = path;
                scratch.Get().Reset();
            });
        }

        void BuildGrid()
        {
            List<Point3f> positions = new List<Point3f>();
            List<int> paths = new List<int>();
            List<int> vertices = new List<int>();
            for (int i = 0; i < lightPathCount; ++i)
            {
                UpgLightPath path = lightPaths[i];
                for (int v = 1; v < path.count; ++v)
                {
                    Vertex vertex = path.vertices[v];
                    if (vertex.type != VertexType.Surface && vertex.type != VertexType.Medium)
                    {
                        continue;
                    }
                    positions.Add(vertex.p());
                    paths.Add(i);
                    vertices.Add(v);
                }
            }
            grid = new UpgPhotonGrid();
            Bounds3f bounds = sceneBounds;
            grid.Build(positions, paths, vertices, bounds, radius);
            Interlocked.Add(ref UpgStats.photonsStored, positions.Count);
        }

        static Vertex[] CloneVertices(Vertex[] source, int count)
        {
            Vertex[] copy = new Vertex[count];
            for (int i = 0; i < count; ++i)
            {
                copy[i] = source[i].Clone();
            }
            return copy;
        }

        // The pixel sample: the strategies of bdpt with the light sub-path of the pixel, and the gathering of the photons at the
        // vertices of the camera sub-path
        void EvaluatePixel(Point2i pPixel, int iter, Sampler sampler, ScratchBuffer scratchBuffer, Bounds2i pixelBounds)
        {
            Film film = camera.GetFilm();
            SampledWavelengths lambda = iterationLambda;
            CameraSample cameraSample = GetCameraSample(sampler, pPixel, film.GetFilter());
            pstd.optional<CameraRayDifferential> cameraRay = camera.GenerateRayDifferential(cameraSample, ref lambda);
            if (!cameraRay)
            {
                return;
            }
            RayDifferential ray = cameraRay.value().ray;
            Float rayDiffScale = Math.Max(0.125f, 1 / MathF.Sqrt((Float)sampler.SamplesPerPixel()));
            if (!Options.disablePixelJitter)
            {
                ray.ScaleDifferentials(rayDiffScale);
            }
            int pixelIndex = (pPixel.y - pixelBounds.pMin.y) * (pixelBounds.pMax.x - pixelBounds.pMin.x) + (pPixel.x - pixelBounds.pMin.x);

            Vertex[] cameraVertices = new Vertex[maxDepth + 2];
            for (int i = 0; i < cameraVertices.Length; ++i)
            {
                cameraVertices[i] = new Vertex();
            }
            int nCamera = BDPT.GenerateCameraSubpath(this, ray, ref lambda, sampler, scratchBuffer, maxDepth + 2, camera,
                                                     cameraVertices, regularize, false, null, null);
            UpgLightPath paired = lightPaths[pixelIndex % lightPathCount];
            Vertex[] lightVertices = CloneVertices(paired.vertices, paired.count);
            int nLight = paired.count;

            long bdptStart = System.Diagnostics.Stopwatch.GetTimestamp();
            SampledSpectrum L = new SampledSpectrum(0.0f);
            UpgMisQuery bdptQuery = bdptOnly ? null : new UpgMisQuery(mis, UpgMis.None, 0);
            for (int t = 1; t <= nCamera; ++t)
            {
                for (int s = 0; s <= nLight; ++s)
                {
                    int depth = t + s - 2;
                    if ((s == 1 && t == 1) || depth < 0 || depth > maxDepth)
                    {
                        continue;
                    }
                    pstd.optional<Point2f> pFilmNew = default;
                    Float misWeight = 0.0f;
                    SampledSpectrum Lpath = BDPT.ConnectBDPT(this, ref lambda, lightVertices, cameraVertices, s, t, lightSampler,
                                                            camera, sampler, ref pFilmNew, ref misWeight, null, -ray.d, bdptQuery);
                    if (t != 1)
                    {
                        L += Lpath;
                    }
                    else if ((bool)Lpath)
                    {
                        camera.GetFilm().AddSplat(pFilmNew.value(), Lpath, lambda);
                    }
                }
            }

            Interlocked.Add(ref UpgStats.ticksBdpt, System.Diagnostics.Stopwatch.GetTimestamp() - bdptStart);
            if (!bdptOnly)
            {
                L += GatherPhotons(pPixel, iter, pixelIndex, cameraVertices, nCamera, lambda);
            }

            L = cameraRay.value().weight * L;
            if (L.HasNaNs())
            {
                LOG_ERROR("Not-a-number radiance value returned for pixel (%d, %d), sample %d. Setting to black.", pPixel.x, pPixel.y, iter);
                L = new SampledSpectrum(0.0f);
            }
            else if (IsInf(L.y(lambda)))
            {
                LOG_ERROR("Infinite radiance value returned for pixel (%d, %d), sample %d. Setting to black.", pPixel.x, pPixel.y, iter);
                L = new SampledSpectrum(0.0f);
            }
            film.AddSample(pPixel, L, lambda, new VisibleSurface(), cameraSample.filterWeight);
        }

        // Algorithm 1 of the paper, lines 3 to 16, for one camera sub-path
        SampledSpectrum GatherPhotons(Point2i pPixel, int iter, int pixelIndex, Vertex[] cameraVertices, int nCamera,
                                      SampledWavelengths lambda)
        {
            SampledSpectrum total = new SampledSpectrum(0.0f);
            List<int> found = foundBuffers.Get();
            UpgWalker walker = walkers.Get();
            long tests = 0, pairsInRadius = 0, evaluated = 0, trialCount = 0, bounded = 0, shadow = 0, gathers = 0;
            long[] histogram = new long[UpgStats.HistogramSize];
            for (int e = 1; e < nCamera; ++e)
            {
                Vertex z = cameraVertices[e];
                if (z.type != VertexType.Surface && z.type != VertexType.Medium)
                {
                    continue;
                }
                ++gathers;
                long searchStart = System.Diagnostics.Stopwatch.GetTimestamp();
                grid.Query(z.p(), radius, found, ref tests);
                Interlocked.Add(ref UpgStats.ticksSearch, System.Diagnostics.Stopwatch.GetTimestamp() - searchStart);
                for (int f = 0; f < found.Count; ++f)
                {
                    int photon = found[f];
                    UpgLightPath path = lightPaths[grid.pathIndex[photon]];
                    int idx = grid.vertexIndex[photon];
                    ++pairsInRadius;
                    for (int keep = UpgMis.KeepLightVertex; keep <= UpgMis.KeepCameraVertex; ++keep)
                    {
                        SampledSpectrum c = EvaluatePair(walker, path, idx, photon, cameraVertices, e, keep, lambda, pPixel, iter, pixelIndex,
                                                         ref evaluated, ref trialCount, ref bounded, ref shadow, histogram);
                        total += c;
                    }
                }
            }
            Interlocked.Add(ref UpgStats.photonTests, tests);
            Interlocked.Add(ref UpgStats.pairs, pairsInRadius);
            Interlocked.Add(ref UpgStats.pairsEvaluated, evaluated);
            Interlocked.Add(ref UpgStats.trials, trialCount);
            Interlocked.Add(ref UpgStats.boundedPairs, bounded);
            Interlocked.Add(ref UpgStats.shadowRays, shadow);
            Interlocked.Add(ref UpgStats.gathers, gathers);
            for (int i = 0; i < histogram.Length; ++i)
            {
                if (histogram[i] != 0)
                {
                    Interlocked.Add(ref UpgStats.trialHistogram[i], histogram[i]);
                }
            }
            return total;
        }

        // The contribution of one photon and one camera vertex within the radius, for the candidate path that keeps the photon
        // (keep = 1) or the camera vertex (keep = 2): zero if that candidate is not the technique of its path
        SampledSpectrum EvaluatePair(UpgWalker walker, UpgLightPath path, int idx, int photonId, Vertex[] cameraVertices, int e, int keep,
                                     SampledWavelengths lambda, Point2i pPixel, int iter, int pixelIndex, ref long evaluated,
                                     ref long trialCount, ref long bounded, ref long shadow, long[] histogram)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            Vertex[] lv = path.vertices;
            int k = idx + e;
            int depth = k - 1;
            if (depth > maxDepth || idx < 1)
            {
                return zero;
            }
            Vertex xmm2 = idx >= 2 ? lv[idx - 2] : null;
            Vertex xmm1 = lv[idx - 1];
            Vertex photon = lv[idx];
            Vertex eye = cameraVertices[e];
            Vertex shooterAlongCamera = cameraVertices[e - 1];
            Vertex xp2 = e >= 2 ? cameraVertices[e - 2] : null;
            Vertex kept = keep == UpgMis.KeepLightVertex ? photon : eye;
            int variant = mis.Variant(k, idx, xmm2, xmm1, kept, shooterAlongCamera, xp2, true);
            if (variant != keep)
            {
                return zero;
            }

            int s, t;
            Vertex[] lightCopy;
            Vertex qs, pt, shooter;
            TransportMode shooterMode;
            Point3f center;
            if (keep == UpgMis.KeepLightVertex)
            {
                s = idx + 1;
                t = e;
                lightCopy = new Vertex[s];
                Array.Copy(lv, lightCopy, s);
                lightCopy[s - 1] = lv[s - 1].Clone();
                lightCopy[s - 2] = lv[s - 2].Clone();
                qs = lightCopy[s - 1];
                pt = cameraVertices[t - 1];
                shooter = pt;
                shooterMode = TransportMode.Radiance;
                center = photon.p();
            }
            else
            {
                s = idx;
                t = e + 1;
                lightCopy = new Vertex[s];
                Array.Copy(lv, lightCopy, s);
                lightCopy[s - 1] = lv[s - 1].Clone();
                if (s >= 2)
                {
                    lightCopy[s - 2] = lv[s - 2].Clone();
                }
                qs = lightCopy[s - 1];
                pt = cameraVertices[t - 1];
                shooter = qs;
                shooterMode = TransportMode.Importance;
                center = eye.p();
            }
            if (!qs.IsConnectible() || !pt.IsConnectible())
            {
                return zero;
            }
            // The connection, like BDPT.ConnectBDPT for s >= 2 and t >= 2
            SampledSpectrum connection = qs.beta * qs.f(pt, TransportMode.Importance) * pt.f(qs, TransportMode.Radiance) * pt.beta;
            if (!(bool)connection)
            {
                return zero;
            }
            connection *= BDPT.GeometryTerm(qs, pt);
            ++shadow;
            long shadowStart = System.Diagnostics.Stopwatch.GetTimestamp();
            connection *= TrSegments(qs.GetInteraction(), pt.GetInteraction(), qs.stack, lambda, out SampledSpectrum connRu,
                                     out SampledSpectrum connRl, out Medium _);
            Interlocked.Add(ref UpgStats.ticksShadow, System.Diagnostics.Stopwatch.GetTimestamp() - shadowStart);
            if (!(bool)connection)
            {
                return zero;
            }
            UpgMisQuery query = new UpgMisQuery(mis, keep, idx);
            Vertex sampledUnused = new Vertex();
            Float weight = BDPT.MISWeight(this, camera, lightCopy, cameraVertices, sampledUnused, s, t, lightSampler, connRu, connRl, query);
            if (!(weight > 0))
            {
                return zero;
            }

            // The reciprocal of the probability of the connection (paper Eq. 15), by Bernoulli trials from the shooter
            UpgBound bound = UpgBound.Full;
            if (angularBound)
            {
                Vector3f wo = shooter.type == VertexType.Surface ? shooter.si.wo : shooter.mi.wo;
                bound = UpgBound.Compute(shooter.bsdf, wo, shooter.p(), center, radius);
                if (!bound.IsFull())
                {
                    ++bounded;
                }
            }
            ulong streamHash = Hash(seed, iter, pixelIndex, e * 64 + keep, photonId);
            walker.sampler.StartPixelSample(new Point2i((int)(streamHash & 0x3fffffff), (int)((streamHash >> 32) & 0x3fffffff)),
                                            0);
            long trialStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Float reciprocal = walker.ReciprocalEstimate(shooter, shooterMode, bound, center, radius, lambda, maxTrials, out int trialsHere);
            Interlocked.Add(ref UpgStats.ticksTrials, System.Diagnostics.Stopwatch.GetTimestamp() - trialStart);
            ++evaluated;
            trialCount += trialsHere;
            ++histogram[Math.Min(trialsHere, UpgStats.HistogramSize - 1)];
            return connection * (weight * reciprocal / lightPathCount);
        }

        // UpgIntegrator Private Members
        Camera camera;
        Sampler samplerPrototype;
        int maxDepth;
        bool regularize, angularBound, bdptOnly;
        int seed, maxTrials, lightPathCount;
        Float radius;
        UpgMis mis;
        LightSampler lightSampler;
        Bounds3f sceneBounds;
    }
}
