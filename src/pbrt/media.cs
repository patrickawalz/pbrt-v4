// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// media.h, media.cpp
//
// The concrete media and majorant iterators are subclasses of Medium / RayMajorantIterator
// (base/medium.cs). C++ code that is templated on the concrete medium type (SampleT_maj<M>) is
// generic over IMajorantMedium<TIter>, which each concrete medium implements.
//
// NanoVDBMedium uses the NanoVDB port in ext/openvdb/nanovdb (uncompressed .nvdb files only).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Media Function Definitions
        private struct MeasuredSS
        {
            public string name;
            public RGB sigma_prime_s, sigma_a;  // mm^-1
            public MeasuredSS(string name, RGB sigma_prime_s, RGB sigma_a)
            {
                this.name = name;
                this.sigma_prime_s = sigma_prime_s;
                this.sigma_a = sigma_a;
            }
        }

        private static readonly MeasuredSS[] SubsurfaceParameterTable = {
            // From "A Practical Model for Subsurface Light Transport"
            // Jensen, Marschner, Levoy, Hanrahan
            // Proc SIGGRAPH 2001
            new MeasuredSS("Apple", new RGB(2.29f, 2.39f, 1.97f), new RGB(0.0030f, 0.0034f, 0.046f)),
            new MeasuredSS("Chicken1", new RGB(0.15f, 0.21f, 0.38f), new RGB(0.015f, 0.077f, 0.19f)),
            new MeasuredSS("Chicken2", new RGB(0.19f, 0.25f, 0.32f), new RGB(0.018f, 0.088f, 0.20f)),
            new MeasuredSS("Cream", new RGB(7.38f, 5.47f, 3.15f), new RGB(0.0002f, 0.0028f, 0.0163f)),
            new MeasuredSS("Ketchup", new RGB(0.18f, 0.07f, 0.03f), new RGB(0.061f, 0.97f, 1.45f)),
            new MeasuredSS("Marble", new RGB(2.19f, 2.62f, 3.00f), new RGB(0.0021f, 0.0041f, 0.0071f)),
            new MeasuredSS("Potato", new RGB(0.68f, 0.70f, 0.55f), new RGB(0.0024f, 0.0090f, 0.12f)),
            new MeasuredSS("Skimmilk", new RGB(0.70f, 1.22f, 1.90f), new RGB(0.0014f, 0.0025f, 0.0142f)),
            new MeasuredSS("Skin1", new RGB(0.74f, 0.88f, 1.01f), new RGB(0.032f, 0.17f, 0.48f)),
            new MeasuredSS("Skin2", new RGB(1.09f, 1.59f, 1.79f), new RGB(0.013f, 0.070f, 0.145f)),
            new MeasuredSS("Spectralon", new RGB(11.6f, 20.4f, 14.9f), new RGB(0.00f, 0.00f, 0.00f)),
            new MeasuredSS("Wholemilk", new RGB(2.55f, 3.21f, 3.77f), new RGB(0.0011f, 0.0024f, 0.014f)),

            // From "Acquiring Scattering Properties of Participating Media by
            // Dilution",
            // Narasimhan, Gupta, Donner, Ramamoorthi, Nayar, Jensen
            // Proc SIGGRAPH 2006
            new MeasuredSS("Lowfat Milk", new RGB(0.89187f, 1.5136f, 2.532f), new RGB(0.002875f, 0.00575f, 0.0115f)),
            new MeasuredSS("Reduced Milk", new RGB(2.4858f, 3.1669f, 4.5214f), new RGB(0.0025556f, 0.0051111f, 0.012778f)),
            new MeasuredSS("Regular Milk", new RGB(4.5513f, 5.8294f, 7.136f), new RGB(0.0015333f, 0.0046f, 0.019933f)),
            new MeasuredSS("Espresso", new RGB(0.72378f, 0.84557f, 1.0247f), new RGB(4.7984f, 6.5751f, 8.8493f)),
            new MeasuredSS("Mint Mocha Coffee", new RGB(0.31602f, 0.38538f, 0.48131f), new RGB(3.772f, 5.8228f, 7.82f)),
            new MeasuredSS("Lowfat Soy Milk", new RGB(0.30576f, 0.34233f, 0.61664f), new RGB(0.0014375f, 0.0071875f, 0.035937f)),
            new MeasuredSS("Regular Soy Milk", new RGB(0.59223f, 0.73866f, 1.4693f), new RGB(0.0019167f, 0.0095833f, 0.065167f)),
            new MeasuredSS("Lowfat Chocolate Milk", new RGB(0.64925f, 0.83916f, 1.1057f), new RGB(0.0115f, 0.0368f, 0.1564f)),
            new MeasuredSS("Regular Chocolate Milk", new RGB(1.4585f, 2.1289f, 2.9527f), new RGB(0.010063f, 0.043125f, 0.14375f)),
            new MeasuredSS("Coke", new RGB(8.9053e-05f, 8.372e-05f, 0f), new RGB(0.10014f, 0.16503f, 0.2468f)),
            new MeasuredSS("Pepsi", new RGB(6.1697e-05f, 4.2564e-05f, 0f), new RGB(0.091641f, 0.14158f, 0.20729f)),
            new MeasuredSS("Sprite", new RGB(6.0306e-06f, 6.4139e-06f, 6.5504e-06f), new RGB(0.001886f, 0.0018308f, 0.0020025f)),
            new MeasuredSS("Gatorade", new RGB(0.0024574f, 0.003007f, 0.0037325f), new RGB(0.024794f, 0.019289f, 0.008878f)),
            new MeasuredSS("Chardonnay", new RGB(1.7982e-05f, 1.3758e-05f, 1.2023e-05f), new RGB(0.010782f, 0.011855f, 0.023997f)),
            new MeasuredSS("White Zinfandel", new RGB(1.7501e-05f, 1.9069e-05f, 1.288e-05f), new RGB(0.012072f, 0.016184f, 0.019843f)),
            new MeasuredSS("Merlot", new RGB(2.1129e-05f, 0f, 0f), new RGB(0.11632f, 0.25191f, 0.29434f)),
            new MeasuredSS("Budweiser Beer", new RGB(2.4356e-05f, 2.4079e-05f, 1.0564e-05f), new RGB(0.011492f, 0.024911f, 0.057786f)),
            new MeasuredSS("Coors Light Beer", new RGB(5.0922e-05f, 4.301e-05f, 0f), new RGB(0.006164f, 0.013984f, 0.034983f)),
            new MeasuredSS("Clorox", new RGB(0.0024035f, 0.0031373f, 0.003991f), new RGB(0.0033542f, 0.014892f, 0.026297f)),
            new MeasuredSS("Apple Juice", new RGB(0.00013612f, 0.00015836f, 0.000227f), new RGB(0.012957f, 0.023741f, 0.052184f)),
            new MeasuredSS("Cranberry Juice", new RGB(0.00010402f, 0.00011646f, 7.8139e-05f), new RGB(0.039437f, 0.094223f, 0.12426f)),
            new MeasuredSS("Grape Juice", new RGB(5.382e-05f, 0f, 0f), new RGB(0.10404f, 0.23958f, 0.29325f)),
            new MeasuredSS("Ruby Grapefruit Juice", new RGB(0.011002f, 0.010927f, 0.011036f), new RGB(0.085867f, 0.18314f, 0.25262f)),
            new MeasuredSS("White Grapefruit Juice", new RGB(0.22826f, 0.23998f, 0.32748f), new RGB(0.0138f, 0.018831f, 0.056781f)),
            new MeasuredSS("Shampoo", new RGB(0.0007176f, 0.0008303f, 0.0009016f), new RGB(0.014107f, 0.045693f, 0.061717f)),
            new MeasuredSS("Strawberry Shampoo", new RGB(0.00015671f, 0.00015947f, 1.518e-05f), new RGB(0.01449f, 0.05796f, 0.075823f)),
            new MeasuredSS("Head & Shoulders Shampoo", new RGB(0.023805f, 0.028804f, 0.034306f), new RGB(0.084621f, 0.15688f, 0.20365f)),
            new MeasuredSS("Lemon Tea Powder", new RGB(0.040224f, 0.045264f, 0.051081f), new RGB(2.4288f, 4.5757f, 7.2127f)),
            new MeasuredSS("Orange Powder", new RGB(0.00015617f, 0.00017482f, 0.0001762f), new RGB(0.001449f, 0.003441f, 0.007863f)),
            new MeasuredSS("Pink Lemonade Powder", new RGB(0.00012103f, 0.00013073f, 0.00012528f), new RGB(0.001165f, 0.002366f, 0.003195f)),
            new MeasuredSS("Cappuccino Powder", new RGB(1.8436f, 2.5851f, 2.1662f), new RGB(35.844f, 49.547f, 61.084f)),
            new MeasuredSS("Salt Powder", new RGB(0.027333f, 0.032451f, 0.031979f), new RGB(0.28415f, 0.3257f, 0.34148f)),
            new MeasuredSS("Sugar Powder", new RGB(0.00022272f, 0.00025513f, 0.000271f), new RGB(0.012638f, 0.031051f, 0.050124f)),
            new MeasuredSS("Suisse Mocha Powder", new RGB(2.7979f, 3.5452f, 4.3365f), new RGB(17.502f, 27.004f, 35.433f)),
            new MeasuredSS("Pacific Ocean Surface Water", new RGB(0.0001764f, 0.00032095f, 0.00019617f), new RGB(0.031845f, 0.031324f, 0.030147f))
        };

        public static bool GetMediumScatteringProperties(string name, ref Spectrum sigma_a, ref Spectrum sigma_s, Allocator alloc)
        {
            foreach (MeasuredSS mss in SubsurfaceParameterTable)
            {
                if (name == mss.name)
                {
                    sigma_a = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, mss.sigma_a);
                    sigma_s = new RGBUnboundedSpectrum(RGBColorSpace.sRGB, mss.sigma_prime_s);
                    return true;
                }
            }
            return false;
        }

        // SampleT_maj<ConcreteMedium>
        public static SampledSpectrum SampleT_maj<ConcreteMedium, MajorantIterator>(
            Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
            Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
            where ConcreteMedium : Medium, IMajorantMedium<MajorantIterator>
            where MajorantIterator : RayMajorantIterator
        {
            // Normalize ray direction and update _tMax_ accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);

            // Initialize _MajorantIterator_ for ray majorant sampling
            ConcreteMedium medium = (ConcreteMedium)ray.medium;
            MajorantIterator iter = medium.SampleRay(ray, tMax, lambda);

            // Generate ray majorant samples until termination
            SampledSpectrum T_maj = new SampledSpectrum(1f);
            bool done = false;
            while (!done)
            {
                // Get next majorant segment from iterator and sample it
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                    return T_maj;
                RayMajorantSegment sg = seg.value();
                // Handle zero-valued majorant for current segment
                if (sg.sigma_maj[0] == 0)
                {
                    Float dt = sg.tMax - sg.tMin;
                    // Handle infinite _dt_ for ray majorant segment
                    if (IsInf(dt))
                        dt = Float.MaxValue;

                    T_maj *= FastExp(-dt * sg.sigma_maj);
                    continue;
                }

                // Generate samples along current majorant segment
                Float tMin = sg.tMin;
                while (true)
                {
                    // Try to generate sample along current majorant segment
                    Float t = tMin + SampleExponential(u, sg.sigma_maj[0]);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        // Call callback function for sample within segment
                        T_maj *= FastExp(-(t - tMin) * sg.sigma_maj);
                        MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda);
                        if (!callback(ray.Apply(t), mp, sg.sigma_maj, T_maj))
                        {
                            // Returning out of doubly-nested while loop is not as good perf. wise
                            // on the GPU vs using "done" here.
                            done = true;
                            break;
                        }
                        T_maj = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        // Handle sample past end of majorant segment
                        Float dt = sg.tMax - tMin;
                        // Handle infinite _dt_ for ray majorant segment
                        if (IsInf(dt))
                            dt = Float.MaxValue;

                        T_maj *= FastExp(-dt * sg.sigma_maj);
                        break;
                    }
                }
            }
            return new SampledSpectrum(1f);
        }

        // SampleT_maj(): dispatch on the ray's medium type (ray.medium.Dispatch)
        // (_analogEvents_ is not in pbrt-v4: it says the callback picks absorption/scattering/null events itself, as
        // volpath's camera paths do, which lets decomposition tracking skip density lookups.)
        public static SampledSpectrum SampleT_maj(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                  Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback,
                                                  bool analogEvents = false)
        {
            Medium m = ray.medium;
            if (analogEvents && m is TextureMedium decomposed && decomposed.UsesDecomposition())
                return decomposed.SampleT_majDecomposed(ray, tMax, u, ref rng, lambda, callback);
            if (m is HomogeneousMedium)
                return SampleT_maj<HomogeneousMedium, HomogeneousMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            else if (m is GridMedium)
                return SampleT_maj<GridMedium, DDAMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            else if (m is RGBGridMedium)
                return SampleT_maj<RGBGridMedium, DDAMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            else if (m is CloudMedium)
                return SampleT_maj<CloudMedium, HomogeneousMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            else if (m is TextureMedium)
                return SampleT_maj<TextureMedium, RayMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            else if (m is NanoVDBMedium)
                return SampleT_maj<NanoVDBMedium, DDAMajorantIterator>(ray, tMax, u, ref rng, lambda, callback);
            LOG_FATAL("SampleT_maj(): ray has no medium");
            return default;
        }
    }


    // HGPhaseFunction Definition
    public sealed unsafe class HGPhaseFunction : PhaseFunction
    {
        // HGPhaseFunction Public Methods
        public HGPhaseFunction() { }
        public HGPhaseFunction(Float g) { this.g = g; }

        public override Float p(Vector3f wo, Vector3f wi) { return HenyeyGreenstein(Dot(wo, wi), g); }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            Float pdf;
            Vector3f wi = SampleHenyeyGreenstein(wo, g, u, &pdf);
            PhaseFunctionSample s = new PhaseFunctionSample();
            s.p = pdf;
            s.wi = wi;
            s.pdf = pdf;
            return s;
        }

        public override Float PDF(Vector3f wo, Vector3f wi) { return p(wo, wi); }

        public static string Name() { return "Henyey-Greenstein"; }

        public override string ToString()
        {
            return StringPrintf("[ HGPhaseFunction g: %f ]", g);
        }

        // HGPhaseFunction Private Members
        private Float g;
    }

    // MediumProperties Definition
    public struct MediumProperties
    {
        public SampledSpectrum sigma_a, sigma_s;
        public PhaseFunction phase;
        public SampledSpectrum Le;
        // (not in pbrt-v4) The majorant that light sampling's ratio tracking sees at this point, when it
        // differs from the _sigma_maj_ passed to the callback (decomposition tracking); zero otherwise.
        public SampledSpectrum sigma_maj_light;
        // (not in pbrt-v4) Set by media that use spectral tracking (Kutz et al. 2017): the majorant is the same
        // for all wavelengths and the integrator chooses events with probabilities that follow the path's
        // spectral throughput.
        public bool spectralTracking;
        // (not in pbrt-v4) Set by adaptive ratio tracking's camera-path sampling (Misso et al. 2023): the
        // density with which light sampling's adaptive ratio tracking would have produced this null vertex
        // and the stretch of ray before it, normalized like T_maj * sigma_maj.
        public bool adaptiveLight;
        // (not in pbrt-v4) Set by progressive null-tracking without clamping: the density may exceed the
        // majorant, so the null coefficient (majorant minus extinction) can be negative.
        public bool signedNull;
        // (not in pbrt-v4) Set by mixture sampling of the collision distances (Miller et al. 2019): the
        // per-wavelength ratio of the mixture's path density after and before this vertex.
        public bool mixture;
        public SampledSpectrum mixRatio;
        public bool adaptiveLight_;
        public SampledSpectrum lightDensity;
    }

    // HomogeneousMajorantIterator Definition
    public sealed unsafe class HomogeneousMajorantIterator : RayMajorantIterator
    {
        // HomogeneousMajorantIterator Public Methods
        public HomogeneousMajorantIterator() { called = true; }
        public HomogeneousMajorantIterator(Float tMin, Float tMax, SampledSpectrum sigma_maj)
        {
            seg = new RayMajorantSegment { tMin = tMin, tMax = tMax, sigma_maj = sigma_maj };
            called = false;
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            if (called)
                return default;
            called = true;
            return seg;
        }

        public override string ToString()
        {
            return StringPrintf("[ HomogeneousMajorantIterator seg: %s called: %s ]", seg, called);
        }

        private RayMajorantSegment seg;
        private bool called;
    }

    // MajorantGrid Definition
    public unsafe class MajorantGrid
    {
        // MajorantGrid Public Methods
        public MajorantGrid() { }
        public MajorantGrid(Bounds3f bounds, Point3i res, Allocator alloc)
        {
            this.bounds = bounds;
            this.voxels = new Float[res.x * res.y * res.z];
            this.res = res;
        }

        public Float Lookup(int x, int y, int z)
        {
            DCHECK(x >= 0 && x < res.x && y >= 0 && y < res.y && z >= 0 && z < res.z);
            return voxels[x + res.x * (y + res.y * z)];
        }
        public void Set(int x, int y, int z, Float v)
        {
            DCHECK(x >= 0 && x < res.x && y >= 0 && y < res.y && z >= 0 && z < res.z);
            voxels[x + res.x * (y + res.y * z)] = v;
        }

        public Bounds3f VoxelBounds(int x, int y, int z)
        {
            Point3f p0 = new Point3f((Float)x / res.x, (Float)y / res.y, (Float)z / res.z);
            Point3f p1 = new Point3f((Float)(x + 1) / res.x, (Float)(y + 1) / res.y, (Float)(z + 1) / res.z);
            return new Bounds3f(p0, p1);
        }

        // MajorantGrid Public Members
        public Bounds3f bounds;
        public Float[] voxels;
        public Point3i res;
    }

    [InlineArray(3)]
    internal struct Float3 { private Float _e0; }

    [InlineArray(3)]
    internal struct Int3 { private int _e0; }

    // DDAMajorantIterator Definition
    public sealed unsafe class DDAMajorantIterator : RayMajorantIterator
    {
        // DDAMajorantIterator Public Methods
        public DDAMajorantIterator() { }
        public DDAMajorantIterator(Ray ray, Float tMin, Float tMax, MajorantGrid grid, SampledSpectrum sigma_t)
        {
            Initialize(ray, tMin, tMax, grid, sigma_t);
        }

        // (not in pbrt-v4) Lets the hierarchical iterator reuse one iterator per grid level
        public void Initialize(Ray ray, Float tMin, Float tMax, MajorantGrid grid, SampledSpectrum sigma_t)
        {
            this.tMin = tMin;
            this.tMax = tMax;
            this.grid = grid;
            this.sigma_t = sigma_t;
            // Set up 3D DDA for ray through the majorant grid
            Vector3f diag = grid.bounds.Diagonal();
            Ray rayGrid = new Ray((Point3f)grid.bounds.Offset(ray.o), new Vector3f(ray.d.x / diag.x, ray.d.y / diag.y, ray.d.z / diag.z));
            Point3f gridIntersect = rayGrid.Apply(tMin);
            for (int axis = 0; axis < 3; ++axis)
            {
                // Initialize ray stepping parameters for _axis_
                // Compute current voxel for axis and handle negative zero direction
                voxel[axis] = (int)Clamp(gridIntersect[axis] * grid.res[axis], 0, grid.res[axis] - 1);
                deltaT[axis] = 1 / (std.abs(rayGrid.d[axis]) * grid.res[axis]);
                if (rayGrid.d[axis] == -0f)
                    rayGrid.d[axis] = 0f;

                if (rayGrid.d[axis] >= 0)
                {
                    // Handle ray with positive direction for voxel stepping
                    Float nextVoxelPos = (Float)(voxel[axis] + 1) / grid.res[axis];
                    nextCrossingT[axis] = tMin + (nextVoxelPos - gridIntersect[axis]) / rayGrid.d[axis];
                    step[axis] = 1;
                    voxelLimit[axis] = grid.res[axis];
                }
                else
                {
                    // Handle ray with negative direction for voxel stepping
                    Float nextVoxelPos = (Float)(voxel[axis]) / grid.res[axis];
                    nextCrossingT[axis] = tMin + (nextVoxelPos - gridIntersect[axis]) / rayGrid.d[axis];
                    step[axis] = -1;
                    voxelLimit[axis] = -1;
                }
            }
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            if (tMin >= tMax)
                return default;
            // Find _stepAxis_ for stepping to next voxel and exit point _tVoxelExit_
            int bits = ((nextCrossingT[0] < nextCrossingT[1] ? 1 : 0) << 2) + ((nextCrossingT[0] < nextCrossingT[2] ? 1 : 0) << 1) +
                       ((nextCrossingT[1] < nextCrossingT[2] ? 1 : 0));
            int[] cmpToAxis = cmpToAxisTable;
            int stepAxis = cmpToAxis[bits];
            Float tVoxelExit = std.min(tMax, nextCrossingT[stepAxis]);

            // Get _maxDensity_ for current voxel and initialize _RayMajorantSegment_, _seg_
            SampledSpectrum sigma_maj = sigma_t * grid.Lookup(voxel[0], voxel[1], voxel[2]);
            RayMajorantSegment seg = new RayMajorantSegment { tMin = tMin, tMax = tVoxelExit, sigma_maj = sigma_maj };
            lastVoxel = voxel[0] + grid.res.x * (voxel[1] + grid.res.y * voxel[2]);

            // Advance to next voxel in maximum density grid
            tMin = tVoxelExit;
            if (nextCrossingT[stepAxis] > tMax)
                tMin = tMax;
            voxel[stepAxis] += step[stepAxis];
            if (voxel[stepAxis] == voxelLimit[stepAxis])
                tMin = tMax;
            nextCrossingT[stepAxis] += deltaT[stepAxis];

            return seg;
        }

        public override string ToString()
        {
            return StringPrintf("[ DDAMajorantIterator tMin: %f tMax: %f sigma_t: %s " +
                                "nextCrossingT: [ %f %f %f ] deltaT: [ %f %f %f ] " +
                                "step: [ %d %d %d ] voxelLimit: [ %d %d %d ] voxel: [ %d %d %d ] " +
                                "grid: %s ]",
                                tMin, tMax, sigma_t, nextCrossingT[0], nextCrossingT[1], nextCrossingT[2], deltaT[0],
                                deltaT[1], deltaT[2], step[0], step[1], step[2], voxelLimit[0], voxelLimit[1],
                                voxelLimit[2], voxel[0], voxel[1], voxel[2], grid != null ? "(grid)" : "(nullptr)");
        }

        private static readonly int[] cmpToAxisTable = { 2, 1, 2, 1, 2, 2, 0, 0 };

        // (not in pbrt-v4) Index of the grid voxel that produced the segment returned by the last Next() call
        public int lastVoxel;

        // DDAMajorantIterator Private Members
        private SampledSpectrum sigma_t;
        private Float tMin = Infinity, tMax = -Infinity;
        private MajorantGrid grid;
        private Float3 nextCrossingT, deltaT;
        private Int3 step, voxelLimit, voxel;
    }

    // (not in pbrt-v4) A voxel of a hierarchical majorant grid that was refined: _sub_ is the finer grid covering
    // the voxel and _children_ holds, per sub-voxel, its own refinement (null for a leaf)
    public sealed class MajorantNode
    {
        public MajorantGrid sub;
        public MajorantNode[] children;
    }

    // (not in pbrt-v4) Traverses a hierarchical majorant grid: a DDA through the coarse grid that descends, with a nested
    // DDA over the ray segment, into every refined voxel
    public sealed unsafe class HierarchicalMajorantIterator : RayMajorantIterator
    {
        public HierarchicalMajorantIterator(Ray ray, Float tMin, Float tMax, MajorantGrid grid, MajorantNode[] nodes,
                                            SampledSpectrum sigma_t)
        {
            this.ray = ray;
            this.sigma_t = sigma_t;
            iterators[0] = new DDAMajorantIterator(ray, tMin, tMax, grid, sigma_t);
            levelNodes[0] = nodes;
            depth = 1;
        }

        public override pstd.optional<RayMajorantSegment> Next()
        {
            while (depth > 0)
            {
                pstd.optional<RayMajorantSegment> seg = iterators[depth - 1].Next();
                if (!seg)
                {
                    --depth;
                    continue;
                }
                MajorantNode node = levelNodes[depth - 1][iterators[depth - 1].lastVoxel];
                if (node == null)
                    return seg;
                RayMajorantSegment sg = seg.value();
                if (iterators[depth] == null)
                    iterators[depth] = new DDAMajorantIterator();
                iterators[depth].Initialize(ray, sg.tMin, sg.tMax, node.sub, sigma_t);
                levelNodes[depth] = node.children;
                ++depth;
            }
            return default;
        }

        public override string ToString()
        {
            return StringPrintf("[ HierarchicalMajorantIterator depth: %d ]", depth);
        }

        private Ray ray;
        private SampledSpectrum sigma_t;
        private int depth;
        private DDAMajorantIterator[] iterators = new DDAMajorantIterator[6];
        private MajorantNode[][] levelNodes = new MajorantNode[6][];
    }

    // IMajorantMedium: the "using MajorantIterator = ..." member type plus the typed SampleRay
    // that each C++ medium class provides; used by SampleT_maj<ConcreteMedium>().
    public interface IMajorantMedium<TIter> where TIter : RayMajorantIterator
    {
        MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda);
        TIter SampleRay(Ray ray, Float tMax, SampledWavelengths lambda);
    }

    // HomogeneousMedium Definition
    public sealed unsafe class HomogeneousMedium : Medium, IMajorantMedium<HomogeneousMajorantIterator>
    {
        // HomogeneousMedium Public Methods
        public HomogeneousMedium(Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale, Spectrum Le, Float LeScale, Float g,
                                 Allocator alloc)
        {
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            Le_spec = new DenselySampledSpectrum(Le, alloc);
            phase = new HGPhaseFunction(g);
            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);
            Le_spec.Scale(LeScale);
        }

        public override bool IsEmissive() { return Le_spec.MaxValue() > 0; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
            SampledSpectrum Le = Le_spec.Sample(lambda);
            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = phase, Le = Le };
        }

        public HomogeneousMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
            return new HomogeneousMajorantIterator(0, tMax, sigma_a + sigma_s);
        }

        // Medium::SampleRay(): "Iter *iter = buf.Alloc(...); *iter = medium->SampleRay(...)"
        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }
    
        public static HomogeneousMedium Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Spectrum sig_a = null, sig_s = null;
            string preset = parameters.GetOneString("preset", "");
            if (preset.Length != 0)
            {
                if (!GetMediumScatteringProperties(preset, ref sig_a, ref sig_s, alloc))
                    Warning(loc, $"Material preset \"{preset}\" not found.");
            }
            if (sig_a == null)
            {
                sig_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
                if (sig_a == null)
                    sig_a = new ConstantSpectrum(1f);
            }
            if (sig_s == null)
            {
                sig_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
                if (sig_s == null)
                    sig_s = new ConstantSpectrum(1f);
            }

            Spectrum Le = parameters.GetOneSpectrum("Le", null, SpectrumType.Illuminant, alloc);
            Float LeScale = parameters.GetOneFloat("Lescale", 1f);
            if (Le == null || Le.MaxValue() == 0)
                Le = new ConstantSpectrum(0f);
            else
                LeScale /= SpectrumToPhotometric(Le);

            Float sigmaScale = parameters.GetOneFloat("scale", 1f);
            Float g = parameters.GetOneFloat("g", 0.0f);

            return new HomogeneousMedium(sig_a, sig_s, sigmaScale, Le, LeScale, g, alloc);
        }

        // HomogeneousMedium Private Data
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec, Le_spec;
        private HGPhaseFunction phase;

        public override string ToString()
        {
            return StringPrintf("[ Homogeneous medium sigma_a_spec: %s sigma_s_spec: %s Le_spec: %s phase: %s ]",
                                sigma_a_spec, sigma_s_spec, Le_spec, phase);
        }
    }

    // GridMedium Definition
    public sealed unsafe class GridMedium : Medium, IMajorantMedium<DDAMajorantIterator>
    {
        // STAT_MEMORY_COUNTER("Memory/Volume grids", volumeGridBytes);
        [ThreadStatic] internal static long volumeGridBytes;
        private static StatRegisterer STATS_REGvolumeGridBytes = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Volume grids", volumeGridBytes);
            volumeGridBytes = 0;
        });

        public override bool IsEmissive() { return isEmissive; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            // Sample spectra for grid medium $\sigmaa$ and $\sigmas$
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

            // Scale scattering coefficients by medium density at _p_
            p = renderFromMedium.ApplyInverse(p);
            p = (Point3f)bounds.Offset(p);
            Float d = densityGrid.Lookup(p);
            sigma_a *= d;
            sigma_s *= d;

            // Compute grid emission _Le_ at _p_
            SampledSpectrum Le = new SampledSpectrum(0f);
            if (isEmissive)
            {
                Float scale = LeScale.Lookup(p);
                if (scale > 0)
                {
                    // Compute emitted radiance using _temperatureGrid_ or _Le_spec_
                    if (temperatureGrid)
                    {
                        Float temp = temperatureGrid.value().Lookup(p);
                        // Added after book publication: optionally offset and scale
                        // temperature based on user-supplied parameters. (Match
                        // NanoVDBMedium functionality.)
                        temp = (temp - temperatureOffset) * temperatureScale;
                        if (temp > 100f)
                            Le = scale * new BlackbodySpectrum(temp).Sample(lambda);
                    }
                    else
                        Le = scale * Le_spec.Sample(lambda);
                }
            }

            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = phase, Le = Le };
        }

        public DDAMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            DCHECK_LE(tMax, raytMax);

            // Sample spectra for grid medium $\sigmaa$ and $\sigmas$
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

            SampledSpectrum sigma_t = sigma_a + sigma_s;
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        // GridMedium Public Methods
        public GridMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale,
                          Float g, SampledGrid<Float> d, pstd.optional<SampledGrid<Float>> temperature,
                          Float temperatureScale, Float temperatureOffset, Spectrum Le, SampledGrid<Float> LeGrid,
                          Allocator alloc)
        {
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            densityGrid = d;
            phase = new HGPhaseFunction(g);
            temperatureGrid = temperature;
            this.temperatureScale = temperatureScale;
            this.temperatureOffset = temperatureOffset;
            Le_spec = new DenselySampledSpectrum(Le, alloc);
            LeScale = LeGrid;
            majorantGrid = new MajorantGrid(bounds, new Point3i(16, 16, 16), alloc);
            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);

            volumeGridBytes += (long)LeScale.BytesAllocated();
            volumeGridBytes += (long)densityGrid.BytesAllocated();
            if (temperatureGrid)
                volumeGridBytes += (long)temperatureGrid.value().BytesAllocated();

            isEmissive = temperatureGrid ? true : (Le_spec.MaxValue() > 0);

            // Initialize _majorantGrid_ for _GridMedium_
            for (int z = 0; z < majorantGrid.res.z; ++z)
                for (int y = 0; y < majorantGrid.res.y; ++y)
                    for (int x = 0; x < majorantGrid.res.x; ++x)
                    {
                        Bounds3f vb = majorantGrid.VoxelBounds(x, y, z);
                        majorantGrid.Set(x, y, z, densityGrid.MaxValue(vb));
                    }
        }
    
        public static GridMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                        Allocator alloc)
        {
            List<Float> density = parameters.GetFloatArray("density");
            List<Float> temperature = parameters.GetFloatArray("temperature");

            size_t nDensity;
            if (density.Count == 0)
                ErrorExit(loc, "No \"density\" value provided for grid medium.");
            nDensity = (size_t)density.Count;

            if (temperature.Count != 0)
                if (nDensity != (size_t)temperature.Count)
                    ErrorExit(loc, StringPrintf("Different number of samples (%d vs %d) provided for " +
                                                "\"density\" and \"temperature\".", nDensity, temperature.Count));

            int nx = parameters.GetOneInt("nx", 1);
            int ny = parameters.GetOneInt("ny", 1);
            int nz = parameters.GetOneInt("nz", 1);
            if (nDensity != (size_t)(nx * ny * nz))
                ErrorExit(loc, StringPrintf("Grid medium has %d density values; expected nx*ny*nz = %d", nDensity, nx * ny * nz));

            // Create Density Grid
            SampledGrid<Float> densityGrid = new SampledGrid<Float>(density.ToArray(), nx, ny, nz, alloc);

            pstd.optional<SampledGrid<Float>> temperatureGrid = default;
            if (temperature.Count != 0)
                temperatureGrid = new SampledGrid<Float>(temperature.ToArray(), nx, ny, nz, alloc);

            Spectrum Le = parameters.GetOneSpectrum("Le", null, SpectrumType.Illuminant, alloc);

            if (Le != null && temperature.Count != 0)
                ErrorExit(loc, "Both \"Le\" and \"temperature\" values were provided.");

            Float LeNorm = 1;
            if (Le == null || Le.MaxValue() == 0)
                Le = new ConstantSpectrum(0f);
            else
                LeNorm = 1 / SpectrumToPhotometric(Le);

            SampledGrid<Float> LeGrid = new SampledGrid<Float>(alloc);
            List<Float> LeScale = parameters.GetFloatArray("Lescale");

            if (LeScale.Count == 0)
                LeGrid = new SampledGrid<Float>(new Float[] { LeNorm }, 1, 1, 1, alloc);
            else
            {
                if (LeScale.Count != nx * ny * nz)
                    ErrorExit(StringPrintf("Expected %d x %d %d = %d values for \"Lescale\" but were " +
                                           "given %d.", nx, ny, nz, nx * ny * nz, LeScale.Count));
                for (int i = 0; i < nx * ny * nz; ++i)
                    LeScale[i] *= LeNorm;
                LeGrid = new SampledGrid<Float>(LeScale.ToArray(), nx, ny, nz, alloc);
            }

            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));

            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);
            Float sigmaScale = parameters.GetOneFloat("scale", 1f);

            Float temperatureOffset = parameters.GetOneFloat("temperatureoffset", parameters.GetOneFloat("temperaturecutoff", 0f));
            Float temperatureScale = parameters.GetOneFloat("temperaturescale", 1f);

            return new GridMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, sigmaScale, g, densityGrid,
                                  temperatureGrid, temperatureScale, temperatureOffset, Le, LeGrid, alloc);
        }

        // GridMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private SampledGrid<Float> densityGrid;
        private HGPhaseFunction phase;
        private pstd.optional<SampledGrid<Float>> temperatureGrid;
        private DenselySampledSpectrum Le_spec;
        private SampledGrid<Float> LeScale;
        private bool isEmissive;
        private Float temperatureScale, temperatureOffset;
        private MajorantGrid majorantGrid;

        public override string ToString()
        {
            return StringPrintf("[ GridMedium bounds: %s renderFromMedium: %s phase: %s " +
                                "LeScale: %s (grids elided) ]",
                                bounds, renderFromMedium, phase, LeScale);
        }
    }

    // RGBGridMedium Definition
    public sealed unsafe class RGBGridMedium : Medium, IMajorantMedium<DDAMajorantIterator>
    {

        public override bool IsEmissive() { return LeGrid && LeScale > 0; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            p = renderFromMedium.ApplyInverse(p);
            p = (Point3f)bounds.Offset(p);
            // Compute $\sigmaa$ and $\sigmas$ for _RGBGridMedium_
            SampledSpectrum sigma_a = sigmaScale * (sigma_aGrid ? sigma_aGrid.value().Lookup(p, lambda) : new SampledSpectrum(1f));
            SampledSpectrum sigma_s = sigmaScale * (sigma_sGrid ? sigma_sGrid.value().Lookup(p, lambda) : new SampledSpectrum(1f));

            // Find emitted radiance _Le_ for _RGBGridMedium_
            SampledSpectrum Le = new SampledSpectrum(0f);
            if (LeGrid && LeScale > 0)
            {
                Le = LeScale * LeGrid.value().Lookup(p, lambda, LeIlluminant);
            }

            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = phase, Le = Le };
        }

        public DDAMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            DCHECK_LE(tMax, raytMax);

            SampledSpectrum sigma_t = new SampledSpectrum(1);
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }
        // RGBGridMedium Public Methods
        public RGBGridMedium(Bounds3f bounds, Transform renderFromMedium, Float g,
                             pstd.optional<SampledGrid<RGBUnboundedVoxel>> rgbA,
                             pstd.optional<SampledGrid<RGBUnboundedVoxel>> rgbS, Float sigmaScale,
                             pstd.optional<SampledGrid<RGBIlluminantVoxel>> rgbLe, Float LeScale,
                             DenselySampledSpectrum LeIlluminant, Allocator alloc)
        {
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            phase = new HGPhaseFunction(g);
            sigma_aGrid = rgbA;
            sigma_sGrid = rgbS;
            this.sigmaScale = sigmaScale;
            majorantGrid = new MajorantGrid(bounds, new Point3i(16, 16, 16), alloc);
            LeGrid = rgbLe;
            this.LeScale = LeScale;
            this.LeIlluminant = LeIlluminant;
            if (LeGrid)
                CHECK((bool)sigma_aGrid);
            if (sigma_aGrid)
                GridMedium.volumeGridBytes += (long)sigma_aGrid.value().BytesAllocated();
            if (sigma_sGrid)
                GridMedium.volumeGridBytes += (long)sigma_sGrid.value().BytesAllocated();
            if (LeGrid)
                GridMedium.volumeGridBytes += (long)LeGrid.value().BytesAllocated();

            // Initialize _majorantGrid_ for _RGBGridMedium_
            for (int z = 0; z < majorantGrid.res.z; ++z)
                for (int y = 0; y < majorantGrid.res.y; ++y)
                    for (int x = 0; x < majorantGrid.res.x; ++x)
                    {
                        Bounds3f vb = majorantGrid.VoxelBounds(x, y, z);
                        // Initialize _majorantGrid_ voxel for RGB $\sigmaa$ and $\sigmas$
                        Float maxSigma_t = (sigma_aGrid ? sigma_aGrid.value().MaxValue(vb) : 1) +
                                           (sigma_sGrid ? sigma_sGrid.value().MaxValue(vb) : 1);
                        majorantGrid.Set(x, y, z, sigmaScale * maxSigma_t);
                    }
        }
    
        public static RGBGridMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                           Allocator alloc)
        {
            List<RGB> sigma_a = parameters.GetRGBArray("sigma_a");
            List<RGB> sigma_s = parameters.GetRGBArray("sigma_s");
            List<RGB> Le = parameters.GetRGBArray("Le");

            if (sigma_a.Count == 0 && sigma_s.Count == 0)
                ErrorExit(loc, "RGB grid requires \"sigma_a\" and/or \"sigma_s\" parameter values.");

            size_t nDensity;
            if (sigma_a.Count != 0)
            {
                nDensity = (size_t)sigma_a.Count;
                if (sigma_s.Count != 0 && nDensity != (size_t)sigma_s.Count)
                    ErrorExit(loc, StringPrintf("Different number of samples (%d vs %d) provided for \"sigma_a\" " +
                                                "and \"sigma_s\".", nDensity, sigma_s.Count));
            }
            else
                nDensity = (size_t)sigma_s.Count;

            if (Le.Count != 0 && sigma_a.Count == 0)
                ErrorExit(loc, "RGB grid requires \"sigma_a\" if \"Le\" value provided.");

            if (Le.Count != 0 && nDensity != (size_t)Le.Count)
                ErrorExit(StringPrintf("Expected %d values for \"Le\" parameter but were given %d.", nDensity, Le.Count));

            int nx = parameters.GetOneInt("nx", 1);
            int ny = parameters.GetOneInt("ny", 1);
            int nz = parameters.GetOneInt("nz", 1);
            if (nDensity != (size_t)(nx * ny * nz))
                ErrorExit(loc, StringPrintf("RGB grid medium has %d density values; expected nx*ny*nz = %d", nDensity, nx * ny * nz));

            pstd.optional<SampledGrid<RGBUnboundedVoxel>> sigma_aGrid = default, sigma_sGrid = default;
            pstd.optional<SampledGrid<RGBIlluminantVoxel>> LeGrid = default;

            if (sigma_a.Count != 0)
            {
                RGBColorSpace colorSpace = parameters.ColorSpace();
                List<RGBUnboundedVoxel> rgbSpectrumDensity = new List<RGBUnboundedVoxel>();
                foreach (RGB rgb in sigma_a)
                    rgbSpectrumDensity.Add(RGBUnboundedVoxel.FromRGB(colorSpace, rgb));
                sigma_aGrid = new SampledGrid<RGBUnboundedVoxel>(rgbSpectrumDensity.ToArray(), nx, ny, nz, alloc);
            }
            if (sigma_s.Count != 0)
            {
                RGBColorSpace colorSpace = parameters.ColorSpace();
                List<RGBUnboundedVoxel> rgbSpectrumDensity = new List<RGBUnboundedVoxel>();
                foreach (RGB rgb in sigma_s)
                    rgbSpectrumDensity.Add(RGBUnboundedVoxel.FromRGB(colorSpace, rgb));
                sigma_sGrid = new SampledGrid<RGBUnboundedVoxel>(rgbSpectrumDensity.ToArray(), nx, ny, nz, alloc);
            }
            if (Le.Count != 0)
            {
                RGBColorSpace colorSpace = parameters.ColorSpace();
                List<RGBIlluminantVoxel> rgbSpectrumDensity = new List<RGBIlluminantVoxel>();
                foreach (RGB rgb in Le)
                    rgbSpectrumDensity.Add(RGBIlluminantVoxel.FromRGB(colorSpace, rgb));
                LeGrid = new SampledGrid<RGBIlluminantVoxel>(rgbSpectrumDensity.ToArray(), nx, ny, nz, alloc);
            }

            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));
            Float LeScale = parameters.GetOneFloat("Lescale", 1f);
            Float g = parameters.GetOneFloat("g", 0f);
            Float sigmaScale = parameters.GetOneFloat("scale", 1f);

            return new RGBGridMedium(new Bounds3f(p0, p1), renderFromMedium, g, sigma_aGrid, sigma_sGrid, sigmaScale, LeGrid,
                                     LeScale, parameters.ColorSpace().illuminant, alloc);
        }

        // RGBGridMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private pstd.optional<SampledGrid<RGBIlluminantVoxel>> LeGrid;
        private Float LeScale;
        private DenselySampledSpectrum LeIlluminant;
        private HGPhaseFunction phase;
        private pstd.optional<SampledGrid<RGBUnboundedVoxel>> sigma_aGrid, sigma_sGrid;
        private Float sigmaScale;
        private MajorantGrid majorantGrid;

        public override string ToString()
        {
            return StringPrintf("[ RGBGridMedium bounds: %s renderFromMedium: %s phase: %s " +
                                "sigmaScale: %f LeScale: %f (grids elided) ]",
                                bounds, renderFromMedium, phase, sigmaScale, LeScale);
        }
    }

    // CloudMedium Definition
    public sealed unsafe class CloudMedium : Medium, IMajorantMedium<HomogeneousMajorantIterator>
    {
        // CloudMedium Public Methods
        public override string ToString()
        {
            return StringPrintf("[ CloudMedium bounds: %s renderFromMedium: %s phase: %s " +
                                "sigma_a_spec: %s sigma_s_spec: %s density: %f wispiness: %f " +
                                "frequency: %f ]",
                                bounds, renderFromMedium, phase, sigma_a_spec, sigma_s_spec, density, wispiness, frequency);
        }

        public CloudMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float g,
                           Float density, Float wispiness, Float frequency, Allocator alloc)
        {
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            phase = new HGPhaseFunction(g);
            this.density = density;
            this.wispiness = wispiness;
            this.frequency = frequency;
        }

        public override bool IsEmissive() { return false; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            // Compute sampled spectra for cloud $\sigmaa$ and $\sigmas$ at _p_
            Float density = Density(renderFromMedium.ApplyInverse(p));
            SampledSpectrum sigma_a = density * sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = density * sigma_s_spec.Sample(lambda);
            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = phase, Le = new SampledSpectrum(0f) };
        }

        public HomogeneousMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new HomogeneousMajorantIterator();
            DCHECK_LE(tMax, raytMax);

            // Compute $\sigmat$ bound for cloud medium and initialize majorant iterator
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);
            SampledSpectrum sigma_t = sigma_a + sigma_s;
            return new HomogeneousMajorantIterator(tMin, tMax, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        // CloudMedium Private Methods
        private Float Density(Point3f p)
        {
            Point3f pp = frequency * p;
            if (wispiness > 0)
            {
                // Perturb cloud lookup point _pp_ using noise
                Float vomega = 0.05f * wispiness, vlambda = 10f;
                for (int i = 0; i < 2; ++i)
                {
                    pp += vomega * DNoise(vlambda * pp);
                    vomega *= 0.5f;
                    vlambda *= 1.99f;
                }
            }
            // Sum scales of noise to approximate cloud density
            Float d = 0;
            Float omega = 0.5f, lambda = 1f;
            for (int i = 0; i < 5; ++i)
            {
                d += omega * Noise(lambda * pp);
                omega *= 0.5f;
                lambda *= 1.99f;
            }

            // Model decrease in density with altitude and return final cloud density
            d = Clamp((1 - p.y) * 4.5f * density * d, 0, 1);
            d += 2 * std.max<Float>(0, 0.5f - p.y);
            return Clamp(d, 0, 1);
        }

        // CloudMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private HGPhaseFunction phase;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private Float density, wispiness, frequency;
    
        public static CloudMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                         Allocator alloc)
        {
            Float density = parameters.GetOneFloat("density", 1);
            Float g = parameters.GetOneFloat("g", 0f);
            Float wispiness = parameters.GetOneFloat("wispiness", 1);
            Float frequency = parameters.GetOneFloat("frequency", 5);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);

            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));

            return new CloudMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, g, density, wispiness, frequency, alloc);
        }
    }

    // TextureMedium Definition (not in pbrt-v4)
    // A medium whose density is driven by a procedural FloatTexture evaluated at the medium-space point:
    // density(p) = max(0, densityOffset + densityScale * texture(frequency * p)). The majorant is the
    // per-voxel density bounds from FloatTexture.RegionBounds() in a MajorantGrid, traversed with the DDA.
    public sealed unsafe class TextureMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // TextureMedium Public Methods
        public override string ToString()
        {
            return StringPrintf("[ TextureMedium bounds: %s renderFromMedium: %s phase: %s " +
                                "sigma_a_spec: %s sigma_s_spec: %s texture: %s densityOffset: %f densityScale: %f ]",
                                bounds, renderFromMedium, phase, sigma_a_spec, sigma_s_spec, texture, densityOffset,
                                densityScale);
        }

        public TextureMedium(Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float g,
                             FloatTexture texture, Float densityOffset, Float densityScale, int majorantRes,
                             bool progressive, Float initialMajorant, int warmupPasses, bool decomposition,
                             bool spectral, bool adaptiveRatio, bool unclamped, bool residualRatio, bool mixture, Float mixtureLooseness,
                             Float mixtureWeight, Float boundsFloor, bool hierarchical, int hierarchyRes, int hierarchyDepth,
                             Float refineThreshold, bool kettunen, int kettunenPoints, Float kettunenSpacing, bool textureInRenderSpace,
                             string densityTextureName, string sigmaATextureName, string sigmaSTextureName, string gTextureName,
                             Allocator alloc)
        {
            this.sigmaATextureName = sigmaATextureName;
            this.sigmaSTextureName = sigmaSTextureName;
            this.gTextureName = gTextureName;
            this.warmupPasses = warmupPasses;
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            phase = new HGPhaseFunction(g);
            this.texture = texture;
            this.densityOffset = densityOffset;
            this.densityScale = densityScale;

            this.textureInRenderSpace = textureInRenderSpace;
            this.densityTextureName = densityTextureName;
            this.majorantRes = majorantRes;
            this.initialMajorant = initialMajorant;
            this.boundsFloor = boundsFloor;
            this.hierarchical = hierarchical;
            this.decompositionRequested = decomposition;
            this.progressive = progressive;
            this.spectral = spectral;
            this.mixture = mixture && progressive;
            this.mixtureLooseness = std.max<Float>(mixtureLooseness, 1);
            this.mixtureWeight = mixtureWeight;
            this.unclamped = (unclamped || this.mixture || boundsFloor > 0) && progressive;
            this.residualRatio = residualRatio && !spectral && !progressive;
            this.adaptiveRatio = (adaptiveRatio || this.residualRatio) && !spectral;

            this.kettunen = kettunen && !progressive;
            this.kettunenPoints = std.max(kettunenPoints, 1);
            this.kettunenSpacing = kettunenSpacing;
            this.hierarchyRes = std.max(hierarchyRes, 2);
            this.hierarchyDepth = std.min(hierarchyDepth, 4);
            this.refineThreshold = refineThreshold;

            if (HasCoefficientTextures() && (progressive || this.decompositionRequested || residualRatio))
                ErrorExit(new FileLoc(), "The \"texture\" medium's sigma_a/sigma_s textures can't be combined with progressive, " +
                                         "decomposition or residualratio tracking.");
            // A medium driven by named textures builds its majorants once the scene's textures exist
            if (texture != null && sigmaATextureName.Length == 0 && sigmaSTextureName.Length == 0 && gTextureName.Length == 0)
                BuildMajorants(alloc);
        }

        // (not in pbrt-v4) Whether the absorption or scattering coefficient is driven by a spectrum texture
        private bool HasCoefficientTextures() { return sigmaATextureName.Length != 0 || sigmaSTextureName.Length != 0; }

        // (not in pbrt-v4) Supplies the named textures (the density texture, and the textures that multiply sigma_a and
        // sigma_s and set g) to a medium created before the textures were, and builds its majorants
        public void ResolveTextures(NamedTextures textures, FileLoc loc)
        {
            if (densityTextureName.Length == 0 && !HasCoefficientTextures() && gTextureName.Length == 0)
                return;
            if (texture == null)
            {
                if (!textures.floatTextures.TryGetValue(densityTextureName, out FloatTexture named))
                    ErrorExit(loc, $"{densityTextureName}: float texture not defined for the \"texture\" medium.");
                texture = named;
            }
            if (sigmaATextureName.Length != 0 && !textures.unboundedSpectrumTextures.TryGetValue(sigmaATextureName, out sigmaATexture))
                ErrorExit(loc, $"{sigmaATextureName}: spectrum texture not defined for the \"texture\" medium.");
            if (sigmaSTextureName.Length != 0 && !textures.unboundedSpectrumTextures.TryGetValue(sigmaSTextureName, out sigmaSTexture))
                ErrorExit(loc, $"{sigmaSTextureName}: spectrum texture not defined for the \"texture\" medium.");
            if (gTextureName.Length != 0 && !textures.floatTextures.TryGetValue(gTextureName, out gTexture))
                ErrorExit(loc, $"{gTextureName}: float texture not defined for the \"texture\" medium.");
            BuildMajorants(new Allocator());
        }

        // (not in pbrt-v4) Upper bound of the factors by which the textures scale sigma_a and sigma_s over a region of
        // medium space: sigma_t <= density * (sigma_a * A + sigma_s * S) <= density * (sigma_a + sigma_s) * max(A, S)
        private Float CoefficientBound(Bounds3f region)
        {
            if (!HasCoefficientTextures())
                return 1;
            Float m = 0;
            if (sigmaATextureName.Length != 0)
                m = std.max(m, sigmaATexture.RegionMax(ToTextureSpace(region)));
            else
                m = 1;
            if (sigmaSTextureName.Length != 0)
                m = std.max(m, sigmaSTexture.RegionMax(ToTextureSpace(region)));
            else
                m = std.max<Float>(m, 1);
            return m;
        }

        private Bounds3f ToTextureSpace(Bounds3f region)
        {
            if (!textureInRenderSpace)
                return region;
            Bounds3f renderRegion = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                Point3f p = renderFromMedium.Apply(region.Corner(i));
                renderRegion = (i == 0) ? new Bounds3f(p) : Union(renderRegion, p);
            }
            return renderRegion;
        }

        // Bounds of the density texture over a region of medium space
        private void TextureRegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            if (textureInRenderSpace)
            {
                // The texture's mappings take render-space points
                Bounds3f renderRegion = new Bounds3f();
                for (int i = 0; i < 8; ++i)
                {
                    Point3f p = renderFromMedium.Apply(region.Corner(i));
                    renderRegion = (i == 0) ? new Bounds3f(p) : Union(renderRegion, p);
                }
                region = renderRegion;
            }
            texture.RegionBounds(region, out lo, out hi);
        }

        private void BuildMajorants(Allocator alloc)
        {
            // Bound the density in each majorant grid voxel using the texture's region bounds
            majorantGrid = new MajorantGrid(bounds, new Point3i(majorantRes, majorantRes, majorantRes), alloc);
            controlGrid = new Float[majorantRes * majorantRes * majorantRes];
            for (int z = 0; z < majorantGrid.res.z; ++z)
            {
                for (int y = 0; y < majorantGrid.res.y; ++y)
                {
                    for (int x = 0; x < majorantGrid.res.x; ++x)
                    {
                        Bounds3f vb = majorantGrid.VoxelBounds(x, y, z);
                        Bounds3f region = new Bounds3f(bounds.Lerp(vb.pMin), bounds.Lerp(vb.pMax));
                        TextureRegionBounds(region, out Float lo, out Float hi);
                        Float a = densityOffset + densityScale * lo;
                        Float b = densityOffset + densityScale * hi;
                        majorantGrid.Set(x, y, z, std.max<Float>(0, std.max(a, b)) * CoefficientBound(region));
                        Float voxelControl = std.max<Float>(0, std.min(a, b));
                        controlGrid[x + majorantGrid.res.x * (y + majorantGrid.res.y * z)] = voxelControl;
                        maxControl = std.max(maxControl, voxelControl);
                    }
                }
            }
            this.decomposition = decompositionRequested && !progressive && !spectral && !adaptiveRatio && maxControl > 0;

            // (not in pbrt-v4) Refine the majorant grid where the texture bound is loose
            if (hierarchical && !progressive && !decomposition && !residualRatio)
                topNodes = RefineMajorantGrid(majorantGrid, 1);

            // Progressive null-tracking (Misso et al. 2023): ignore the texture bounds, start every
            // voxel at a deliberately poor majorant and learn it from the density lookups.
            if (progressive)
            {
                // (not in pbrt-v4) With _boundsFloor_ > 0 the analytic texture bounds replace the poor initial majorant:
                // every voxel starts at (and never drops below) boundsFloor times its bound, so that the density over the
                // majorant is at most 1 / boundsFloor and the signed weights of unclamped tracking have bounded variance
                for (int i = 0; i < majorantGrid.voxels.Length; ++i)
                    majorantGrid.voxels[i] = boundsFloor > 0 ? boundsFloor * majorantGrid.voxels[i] : initialMajorant;
                pendingMajorants = (Float[])majorantGrid.voxels.Clone();
                lock (progressiveMedia)
                {
                    progressiveMedia.Add(this);
                }
            }
        }

        // (not in pbrt-v4) Replaces the voxels of _grid_ whose children's bounds are much tighter than their own with
        // finer grids, recursively. Returns, per voxel of _grid_, the refinement or null.
        private MajorantNode[] RefineMajorantGrid(MajorantGrid grid, int depth)
        {
            MajorantNode[] nodes = new MajorantNode[grid.voxels.Length];
            if (depth > hierarchyDepth)
                return nodes;
            int n = hierarchyRes;
            for (int z = 0; z < grid.res.z; ++z)
            {
                for (int y = 0; y < grid.res.y; ++y)
                {
                    for (int x = 0; x < grid.res.x; ++x)
                    {
                        Float parent = grid.Lookup(x, y, z);
                        if (parent <= 0)
                            continue;
                        Bounds3f vb = grid.VoxelBounds(x, y, z);
                        Bounds3f region = new Bounds3f(grid.bounds.Lerp(vb.pMin), grid.bounds.Lerp(vb.pMax));
                        MajorantGrid child = new MajorantGrid(region, new Point3i(n, n, n), null);
                        Float sum = 0;
                        for (int cz = 0; cz < n; ++cz)
                        {
                            for (int cy = 0; cy < n; ++cy)
                            {
                                for (int cx = 0; cx < n; ++cx)
                                {
                                    Bounds3f cb = child.VoxelBounds(cx, cy, cz);
                                    Bounds3f cr = new Bounds3f(region.Lerp(cb.pMin), region.Lerp(cb.pMax));
                                    TextureRegionBounds(cr, out Float lo, out Float hi);
                                    Float a = densityOffset + densityScale * lo;
                                    Float b = densityOffset + densityScale * hi;
                                    Float childMax = std.min(parent, std.max<Float>(0, std.max(a, b)) * CoefficientBound(cr));
                                    child.Set(cx, cy, cz, childMax);
                                    sum += childMax;
                                }
                            }
                        }
                        if (sum / (n * n * n) < refineThreshold * parent)
                        {
                            nodes[x + grid.res.x * (y + grid.res.y * z)] = new MajorantNode { sub = child, children = RefineMajorantGrid(child, depth + 1) };
                        }
                    }
                }
            }
            return nodes;
        }

        // (not in pbrt-v4) Number of majorant segments and density lookups since the last reset, for benchmarking
        public static long lookupCount;
        public static bool countLookups;

        // Progressive null-tracking passes: the majorants are fixed while a pass is rendered and the
        // maxima found by the density lookups take effect when it ends.
        public static bool AnyProgressive()
        {
            lock (progressiveMedia)
            {
                return progressiveMedia.Count > 0;
            }
        }

        // (not in pbrt-v4) Number of discarded passes rendered before the real ones, so that the biased early
        // passes of progressive null-tracking don't contribute to the image
        public Float[] DebugMajorants() { return (Float[])majorantGrid.voxels.Clone(); }

        public static int ProgressiveWarmupPasses()
        {
            int passes = 0;
            lock (progressiveMedia)
            {
                foreach (TextureMedium medium in progressiveMedia)
                    passes = std.max(passes, medium.warmupPasses);
            }
            return passes;
        }

        public static void EndProgressivePass()
        {
            lock (progressiveMedia)
            {
                foreach (TextureMedium medium in progressiveMedia)
                {
                    for (int i = 0; i < medium.majorantGrid.voxels.Length; ++i)
                        medium.majorantGrid.voxels[i] = std.max(medium.majorantGrid.voxels[i], medium.pendingMajorants[i]);
                }
            }
        }

        public override bool IsEmissive() { return false; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            if (countLookups)
                System.Threading.Interlocked.Increment(ref lookupCount);
            Point3f pMedium = renderFromMedium.ApplyInverse(p);
            Float density = std.max<Float>(0, densityOffset + densityScale * EvaluateTexture(p));
            if (progressive)
            {
                // Clamp the density to the current majorant (so the null density stays non-negative)
                // and record the lookup for the next pass (benign data race, as in the paper)
                // A point on a voxel boundary may be assigned to either neighboring voxel by the majorant
                // iterator, so it is treated as inside every voxel within a small tolerance of it.
                Vector3f o = bounds.Offset(pMedium);
                Float fx = o.x * majorantGrid.res.x, fy = o.y * majorantGrid.res.y, fz = o.z * majorantGrid.res.z;
                int x0 = std.min(std.max((int)std.floor(fx - 1e-3f), 0), majorantGrid.res.x - 1);
                int x1 = std.min(std.max((int)std.floor(fx + 1e-3f), 0), majorantGrid.res.x - 1);
                int y0 = std.min(std.max((int)std.floor(fy - 1e-3f), 0), majorantGrid.res.y - 1);
                int y1 = std.min(std.max((int)std.floor(fy + 1e-3f), 0), majorantGrid.res.y - 1);
                int z0 = std.min(std.max((int)std.floor(fz - 1e-3f), 0), majorantGrid.res.z - 1);
                int z1 = std.min(std.max((int)std.floor(fz + 1e-3f), 0), majorantGrid.res.z - 1);
                Float clamped = density;
                for (int vz = z0; vz <= z1; ++vz)
                {
                    for (int vy = y0; vy <= y1; ++vy)
                    {
                        for (int vx = x0; vx <= x1; ++vx)
                        {
                            int voxel = vx + majorantGrid.res.x * (vy + majorantGrid.res.y * vz);
                            if (density + progressiveEpsilon > pendingMajorants[voxel])
                                pendingMajorants[voxel] = density + progressiveEpsilon;
                            clamped = std.min(clamped, majorantGrid.voxels[voxel]);
                        }
                    }
                }
                // Without clamping the estimators use signed null coefficients and stay unbiased for any majorant
                if (!unclamped)
                    density = clamped;
            }
            SampledSpectrum sigma_a = density * sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = density * sigma_s_spec.Sample(lambda);
            PhaseFunction pointPhase = phase;
            if (sigmaATexture != null || sigmaSTexture != null || gTexture != null)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = textureInRenderSpace ? p : renderFromMedium.ApplyInverse(p);
                if (sigmaATexture != null)
                    sigma_a *= ClampZero(sigmaATexture.Evaluate(ctx, lambda));
                if (sigmaSTexture != null)
                    sigma_s *= ClampZero(sigmaSTexture.Evaluate(ctx, lambda));
                if (gTexture != null)
                    pointPhase = new HGPhaseFunction(std.min(std.max(gTexture.Evaluate(ctx), -.99f), .99f));
            }

            return new MediumProperties { sigma_a = sigma_a, sigma_s = sigma_s, phase = pointPhase, Le = new SampledSpectrum(0f),
                                          spectralTracking = spectral, signedNull = unclamped, mixture = false };
        }

        public RayMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            DCHECK_LE(tMax, raytMax);

            SampledSpectrum sigma_t = sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda);
            // Spectral tracking samples distances with one majorant for all wavelengths
            if (spectral)
                sigma_t = new SampledSpectrum(sigma_t.MaxComponentValue());
            if (topNodes != null)
                return new HierarchicalMajorantIterator(ray, tMin, tMax, majorantGrid, topNodes, sigma_t);
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        public bool UsesDecomposition() { return decomposition; }

        public bool UsesMixture() { return mixture; }

        public bool UsesKettunen() { return kettunen; }

        // Procedural textures take medium-space points; named textures keep their own mappings and take render-space
        // points
        private Float EvaluateTexture(Point3f p)
        {
            TextureEvalContext ctx = new TextureEvalContext();
            ctx.p = textureInRenderSpace ? p : renderFromMedium.ApplyInverse(p);
            return texture.Evaluate(ctx);
        }

        [ThreadStatic] private static Float[] kettunenTMin;
        [ThreadStatic] private static Float[] kettunenTMax;
        [ThreadStatic] private static Float[] kettunenCumulative;
        [ThreadStatic] private static Float[] kettunenRate;

        // (not in pbrt-v4) Unbiased ray-marching transmittance estimator (Kettunen et al. 2021). With a control
        // optical depth c that is independent of the estimates tau_i of the optical depth,
        //     exp(-tau) = exp(-c) sum_n prod_{i<=n} (c - tau_i) / n!
        // is unbiased for any c, and converges after a few terms when c is close to tau. Both c and every tau_i are
        // combs of _kettunenPoints_ evenly spaced, jointly shifted points distributed in proportion to the majorant,
        // and the series is truncated by Russian roulette. The cost is independent of the majorant's optical depth.
        public SampledSpectrum KettunenTransmittance(Ray ray, Float tMax, ref RNG rng, SampledWavelengths lambda)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = SampleRay(ray, tMax, lambda);
            if (kettunenTMin == null)
            {
                kettunenTMin = new Float[4096];
                kettunenTMax = new Float[4096];
                kettunenCumulative = new Float[4097];
                kettunenRate = new Float[4096];
            }
            int nSegments = 0;
            Float total = 0;
            kettunenCumulative[0] = 0;
            for (pstd.optional<RayMajorantSegment> seg = iter.Next(); seg; seg = iter.Next())
            {
                RayMajorantSegment sg = seg.value();
                Float rate = sg.sigma_maj[0];
                if (!(rate > 0) || nSegments == kettunenTMin.Length)
                    continue;
                kettunenTMin[nSegments] = sg.tMin;
                kettunenTMax[nSegments] = sg.tMax;
                kettunenRate[nSegments] = rate;
                total += rate * (sg.tMax - sg.tMin);
                kettunenCumulative[++nSegments] = total;
            }
            if (nSegments == 0)
                return new SampledSpectrum(1f);

            // The series converges only when the control is close to the optical depth, so with _kettunenSpacing_ > 0
            // the comb has at least one point per _kettunenSpacing_ of the majorant's optical depth (at most 256)
            int points = kettunenPoints;
            if (kettunenSpacing > 0)
                points = std.min(std.max(points, (int)std.ceil(total / kettunenSpacing)), 256);

            // Estimate the control and the optical depths
            SampledSpectrum c = KettunenOpticalDepth(ray, nSegments, total, points, ref rng, lambda);
            SampledSpectrum sum = new SampledSpectrum(1f), term = new SampledSpectrum(1f);
            for (int n = 1; n < 1000; ++n)
            {
                SampledSpectrum a = (c - KettunenOpticalDepth(ray, nSegments, total, points, ref rng, lambda)) / n;
                Float p = std.min<Float>(1, std.max<Float>(Abs(a).MaxComponentValue(), .05f));
                if (rng.Uniform<Float>() >= p)
                    break;
                term *= a / p;
                sum += term;
            }
            return FastExp(-c) * sum;
        }

        // Comb estimate of the optical depth: the points are evenly spaced in the majorant's optical depth, so each
        // sample's weight is the density's ratio to the majorant at the hero wavelength
        private SampledSpectrum KettunenOpticalDepth(Ray ray, int nSegments, Float total, int points, ref RNG rng, SampledWavelengths lambda)
        {
            SampledSpectrum ratioSum = new SampledSpectrum(0f);
            Float u = rng.Uniform<Float>();
            int segment = 0;
            for (int j = 0; j < points; ++j)
            {
                Float s = (j + u) / points * total;
                while (segment < nSegments - 1 && s >= kettunenCumulative[segment + 1])
                    ++segment;
                Float t = kettunenTMin[segment] + (s - kettunenCumulative[segment]) / kettunenRate[segment];
                t = std.min(t, kettunenTMax[segment]);
                MediumProperties mp = SamplePoint(ray.Apply(t), lambda);
                // sigma_t(x) / sigma_maj(x) at the hero wavelength
                ratioSum += (mp.sigma_a + mp.sigma_s) / (kettunenRate[segment] > 0 ? kettunenRate[segment] : 1f);
            }
            return ratioSum * (total / points);
        }


        // SampleT_maj() with mixture sampling of the collision distances (Miller et al. 2019, applied to progressive
        // null-tracking without clamping). The integrand's majorant is the loose one, _mixtureLooseness_ times the
        // majorant grid's, so that the weights stay bounded even where the learned grid majorant is too small.
        // For each call one of two ways of sampling collisions is picked at random: with the grid majorant itself
        // (the cheap, tight rate) or with the loose one. The path density is the mixture of both, which the
        // callback sees as _mp.mixRatio_, the per-wavelength ratio of the mixture density after and before each
        // vertex. _tailMixture_ is that ratio for the stretch after the last vertex.
        public SampledSpectrum SampleT_majMixture(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                  Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback,
                                                  out SampledSpectrum tailMixture)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = SampleRay(ray, tMax, lambda);
            bool useTight = rng.Uniform<Float>() < mixtureWeight;
            // Densities of the tight and loose techniques since the last vertex, per wavelength (their mixture
            // is 1 right after a vertex)
            SampledSpectrum densityTight = new SampledSpectrum(1f), densityLoose = new SampledSpectrum(1f);
            SampledSpectrum T_I = new SampledSpectrum(1f);
            tailMixture = new SampledSpectrum(1f);
            bool done = false;
            while (!done)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    tailMixture = mixtureWeight * densityTight + (1 - mixtureWeight) * densityLoose;
                    return T_I;
                }
                RayMajorantSegment sg = seg.value();
                SampledSpectrum tight = sg.sigma_maj;
                SampledSpectrum loose = mixtureLooseness * sg.sigma_maj;
                if (tight[0] == 0)
                    continue;
                Float tMin = sg.tMin;
                while (true)
                {
                    Float rate = useTight ? tight[0] : loose[0];
                    Float t = tMin + SampleExponential(u, rate);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        Float x = t - tMin;
                        SampledSpectrum survivalTight = FastExp(-x * tight), survivalLoose = FastExp(-x * loose);
                        T_I *= survivalLoose;
                        densityTight *= survivalTight * tight;
                        densityLoose *= survivalLoose * loose;
                        SampledSpectrum mixed = mixtureWeight * densityTight + (1 - mixtureWeight) * densityLoose;
                        MediumProperties mp = SamplePoint(ray.Apply(t), lambda);
                        mp.mixture = true;
                        mp.mixRatio = mixed;
                        bool keepGoing = callback(ray.Apply(t), mp, loose, T_I);
                        if (!keepGoing)
                        {
                            done = true;
                            break;
                        }
                        // Renormalize so that the mixture density is 1 right after the vertex
                        densityTight = SafeDiv(densityTight, mixed);
                        densityLoose = SafeDiv(densityLoose, mixed);
                        T_I = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        Float dt = sg.tMax - tMin;
                        if (IsInf(dt))
                            dt = Float.MaxValue;
                        densityTight *= FastExp(-dt * tight);
                        SampledSpectrum survivalLoose = FastExp(-dt * loose);
                        densityLoose *= survivalLoose;
                        T_I *= survivalLoose;
                        break;
                    }
                }
            }
            return new SampledSpectrum(1f);
        }

        public bool UsesAdaptiveRatio() { return adaptiveRatio; }

        // Majorant left over after removing the voxel's minimum extinction (the control medium of residual ratio
        // tracking), for the voxel that the majorant segment _sg_ lies in
        private SampledSpectrum ResidualMajorant(Ray ray, RayMajorantSegment sg)
        {
            Point3f p = ray.Apply(.5f * (sg.tMin + sg.tMax));
            Float controlDensity = ControlDensityAt(p);
            Float maxDensity = MajorantDensityAt(p);
            // The majorant is the voxel's maximum density times sigma_t, so what is left after removing the
            // control extinction is the same spectrum scaled by (max - min) / max
            if (controlDensity <= 0 || maxDensity <= 0)
                return sg.sigma_maj;
            return sg.sigma_maj * std.max<Float>(0, 1 - controlDensity / maxDensity);
        }

        // Adaptive ratio tracking (Misso et al. 2023, Algorithm 2) for shadow rays: free-flight distances are
        // sampled with the null coefficient found at the previous collision instead of with the majorant, so
        // regions where the medium is close to the majorant are crossed with few density lookups. The
        // per-wavelength sampling densities that the hero wavelength's sequence of collisions would have had
        // are tracked in _r_l_ (as the densities of light sampling's strategy) and the classical
        // path-sampling densities in _r_u_, as in volpath's ratio tracking.
        public void AdaptiveRatioTracking(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                          ref SampledSpectrum T_ray, ref SampledSpectrum r_l, ref SampledSpectrum r_u)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = SampleRay(ray, tMax, lambda);
            while (true)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                    return;
                RayMajorantSegment sg = seg.value();
                SampledSpectrum c = sg.sigma_maj;
                // Null coefficient of each wavelength's own sampling process, as of the last collision. With
                // residual ratio tracking, collisions are only sampled against what is left of the majorant
                // after the voxel's minimum extinction (the control medium), whose transmittance follows
                // analytically from the pdf ratios below.
                SampledSpectrum muMC = residualRatio ? ResidualMajorant(ray, sg) : c;
                Float tMin = sg.tMin;
                while (muMC[0] > 0)
                {
                    Float t = tMin + SampleExponential(u, muMC[0]);
                    u = rng.Uniform<Float>();
                    if (t >= sg.tMax)
                        break;
                    Float x = t - tMin;
                    SampledSpectrum survMajorant = FastExp(-x * c), survSampling = FastExp(-x * muMC);
                    Float pdf = muMC[0] * survSampling[0];
                    MediumProperties mp = SamplePoint(ray.Apply(t), lambda);
                    SampledSpectrum sigma_n = ClampZero(c - mp.sigma_a - mp.sigma_s);
                    T_ray *= survMajorant * sigma_n / pdf;
                    r_l *= muMC * survSampling / pdf;
                    r_u *= survMajorant * sigma_n / pdf;
                    if (!residualRatio)
                        muMC = sigma_n;
                    tMin = t;

                    // Possibly terminate transmittance computation using Russian roulette
                    SampledSpectrum Tr = T_ray / (r_l + r_u).Average();
                    if (Tr.MaxComponentValue() < 0.05f)
                    {
                        Float q = 0.75f;
                        if (rng.Uniform<Float>() < q)
                            T_ray = new SampledSpectrum(0.0f);
                        else
                            T_ray /= 1 - q;
                    }
                    if (!T_ray)
                        return;
                }
                // Rest of the segment without a collision
                Float dt = sg.tMax - tMin;
                if (IsInf(dt))
                    dt = Float.MaxValue;
                SampledSpectrum survM = FastExp(-dt * c), survS = FastExp(-dt * muMC);
                T_ray *= survM / survS[0];
                r_l *= survS / survS[0];
                r_u *= survM / survS[0];
            }
        }

        // SampleT_maj() for camera paths when light sampling uses adaptive ratio tracking: the same
        // majorant sampling as ordinary delta tracking, but each null vertex also gets the density that
        // light sampling's adaptive ratio tracking would have given it (_mp.lightDensity_), and the
        // light-sampling density of the stretch after the last vertex is returned in _tailLight_.
        public SampledSpectrum SampleT_majAdaptiveCamera(Ray ray, Float tMax, Float u, ref RNG rng,
                                                         SampledWavelengths lambda,
                                                         Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback,
                                                         out SampledSpectrum tailLight)
        {
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);
            RayMajorantIterator iter = SampleRay(ray, tMax, lambda);
            SampledSpectrum T_maj = new SampledSpectrum(1f);
            SampledSpectrum lightAccum = new SampledSpectrum(1f);
            tailLight = new SampledSpectrum(1f);
            bool done = false;
            while (!done)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                {
                    tailLight = lightAccum;
                    return T_maj;
                }
                RayMajorantSegment sg = seg.value();
                SampledSpectrum muMC = residualRatio ? ResidualMajorant(ray, sg) : sg.sigma_maj;
                if (sg.sigma_maj[0] == 0)
                {
                    Float dt = sg.tMax - sg.tMin;
                    if (IsInf(dt))
                        dt = Float.MaxValue;
                    T_maj *= FastExp(-dt * sg.sigma_maj);
                    lightAccum *= FastExp(-dt * muMC);
                    continue;
                }
                Float tMin = sg.tMin;
                while (true)
                {
                    Float t = tMin + SampleExponential(u, sg.sigma_maj[0]);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        T_maj *= FastExp(-(t - tMin) * sg.sigma_maj);
                        lightAccum *= FastExp(-(t - tMin) * muMC);
                        MediumProperties mp = SamplePoint(ray.Apply(t), lambda);
                        mp.adaptiveLight = true;
                        mp.lightDensity = lightAccum * muMC;
                        if (!callback(ray.Apply(t), mp, sg.sigma_maj, T_maj))
                        {
                            done = true;
                            break;
                        }
                        if (!residualRatio)
                            muMC = ClampZero(sg.sigma_maj - mp.sigma_a - mp.sigma_s);
                        T_maj = new SampledSpectrum(1f);
                        lightAccum = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        Float dt = sg.tMax - tMin;
                        if (IsInf(dt))
                            dt = Float.MaxValue;
                        T_maj *= FastExp(-dt * sg.sigma_maj);
                        lightAccum *= FastExp(-dt * muMC);
                        break;
                    }
                }
            }
            return new SampledSpectrum(1f);
        }


        // SampleT_maj() with analog decomposition tracking (camera paths only; shadow rays use ordinary ratio
        // tracking): each tentative collision is, with probability
        // controlDensity / (voxel majorant), a collision with the homogeneous control medium, which is
        // handled without a density lookup; otherwise it is a collision with the residual medium, whose
        // majorant is what is left over. The callback sees the control and residual media as two media with
        // their own majorants, so its event probabilities and weights are the ones of the combined medium.
        public SampledSpectrum SampleT_majDecomposed(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                                     Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            // Normalize ray direction and update _tMax_ accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);

            RayMajorantIterator iter = SampleRay(ray, tMax, lambda);
            SampledSpectrum sigma_a0 = sigma_a_spec.Sample(lambda), sigma_s0 = sigma_s_spec.Sample(lambda);
            SampledSpectrum sigma_t = sigma_a0 + sigma_s0;

            SampledSpectrum T_maj = new SampledSpectrum(1f);
            bool done = false;
            while (!done)
            {
                pstd.optional<RayMajorantSegment> seg = iter.Next();
                if (!seg)
                    return T_maj;
                RayMajorantSegment sg = seg.value();
                if (sg.sigma_maj[0] == 0)
                {
                    Float dt = sg.tMax - sg.tMin;
                    if (IsInf(dt))
                        dt = Float.MaxValue;
                    T_maj *= FastExp(-dt * sg.sigma_maj);
                    continue;
                }

                // The majorant segment lies in one grid voxel, which has its own control density
                Float controlDensity = ControlDensityAt(ray.Apply(.5f * (sg.tMin + sg.tMax)));
                SampledSpectrum sigma_c = controlDensity * sigma_t;
                MediumProperties control = new MediumProperties { sigma_a = controlDensity * sigma_a0,
                                                                  sigma_s = controlDensity * sigma_s0, phase = phase,
                                                                  Le = new SampledSpectrum(0f) };

                Float tMin = sg.tMin;
                while (true)
                {
                    Float t = tMin + SampleExponential(u, sg.sigma_maj[0]);
                    u = rng.Uniform<Float>();
                    if (t < sg.tMax)
                    {
                        T_maj *= FastExp(-(t - tMin) * sg.sigma_maj);
                        Point3f p = ray.Apply(t);
                        bool keepGoing;
                        if (rng.Uniform<Float>() < sigma_c[0] / sg.sigma_maj[0])
                        {
                            // Control collision: no lookup
                            keepGoing = callback(p, control, sigma_c, T_maj);
                        }
                        else
                        {
                            // Residual collision: look up the density and subtract the control part
                            Float density = std.max<Float>(0, densityOffset + densityScale * EvaluateTexture(p));
                            Float residual = std.max<Float>(0, density - controlDensity);
                            MediumProperties mp = new MediumProperties { sigma_a = residual * sigma_a_spec.Sample(lambda),
                                                                         sigma_s = residual * sigma_s_spec.Sample(lambda),
                                                                         phase = phase, Le = new SampledSpectrum(0f) };
                            // light sampling's shadow rays still see the whole majorant
                            mp.sigma_maj_light = sg.sigma_maj;
                            keepGoing = callback(p, mp, sg.sigma_maj - sigma_c, T_maj);
                        }
                        if (!keepGoing)
                        {
                            done = true;
                            break;
                        }
                        T_maj = new SampledSpectrum(1f);
                        tMin = t;
                    }
                    else
                    {
                        Float dt = sg.tMax - tMin;
                        if (IsInf(dt))
                            dt = Float.MaxValue;
                        T_maj *= FastExp(-dt * sg.sigma_maj);
                        break;
                    }
                }
            }
            return new SampledSpectrum(1f);
        }

        // TextureMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private HGPhaseFunction phase;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private FloatTexture texture;
        private MajorantGrid majorantGrid;
        private Float densityOffset, densityScale;
        private bool progressive, decomposition, spectral, adaptiveRatio, unclamped, residualRatio, mixture;
        private Float mixtureLooseness, mixtureWeight;
        private int warmupPasses;
        // Decomposition tracking (Kutz et al. 2017): a control density, constant within each majorant
        // grid voxel, that is a lower bound of the density there; collisions that fall in it need no
        // density lookup.
        private Float[] controlGrid;
        private Float maxControl;

        // Majorant density of the majorant grid voxel containing the point _p_ (in render space)
        private Float MajorantDensityAt(Point3f p)
        {
            Vector3f o = bounds.Offset(renderFromMedium.ApplyInverse(p));
            int vx = std.min(std.max((int)(o.x * majorantGrid.res.x), 0), majorantGrid.res.x - 1);
            int vy = std.min(std.max((int)(o.y * majorantGrid.res.y), 0), majorantGrid.res.y - 1);
            int vz = std.min(std.max((int)(o.z * majorantGrid.res.z), 0), majorantGrid.res.z - 1);
            return majorantGrid.Lookup(vx, vy, vz);
        }

        // Control density of the majorant grid voxel containing the point _p_ (in render space)
        private Float ControlDensityAt(Point3f p)
        {
            Vector3f o = bounds.Offset(renderFromMedium.ApplyInverse(p));
            int vx = std.min(std.max((int)(o.x * majorantGrid.res.x), 0), majorantGrid.res.x - 1);
            int vy = std.min(std.max((int)(o.y * majorantGrid.res.y), 0), majorantGrid.res.y - 1);
            int vz = std.min(std.max((int)(o.z * majorantGrid.res.z), 0), majorantGrid.res.z - 1);
            return controlGrid[vx + majorantGrid.res.x * (vy + majorantGrid.res.y * vz)];
        }
        private Float[] pendingMajorants;
        private MajorantNode[] topNodes;
        private bool kettunen;
        private bool textureInRenderSpace, hierarchical, decompositionRequested;
        private string densityTextureName, sigmaATextureName, sigmaSTextureName, gTextureName;
        private SpectrumTexture sigmaATexture, sigmaSTexture;
        private FloatTexture gTexture;
        private int majorantRes;
        private Float initialMajorant, boundsFloor;
        private int kettunenPoints;
        private Float kettunenSpacing;
        private int hierarchyRes, hierarchyDepth;
        private Float refineThreshold;
        private const Float progressiveEpsilon = 1e-3f;
        private static readonly List<TextureMedium> progressiveMedia = new List<TextureMedium>();

        public static TextureMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                           Allocator alloc)
        {
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);
            Float g = parameters.GetOneFloat("g", 0f);

            // A named float texture ("string densitytexture"), defined with the Texture directive, drives the density through
            // its own mapping, from render space. Otherwise a procedural density texture is mapped by scaling medium
            // space by _frequency_.
            string densityTextureName = parameters.GetOneString("densitytexture", "");
            // Named spectrum textures that multiply sigma_a and sigma_s, and a float texture for g (as in LuxCore's
            // heterogeneous volumes, whose absorption, scattering and asymmetry are textures)
            string sigmaATextureName = parameters.GetOneString("sigma_a_texture", "");
            string sigmaSTextureName = parameters.GetOneString("sigma_s_texture", "");
            string gTextureName = parameters.GetOneString("g_texture", "");
            string textureName = parameters.GetOneString("texture", "fbm");
            Float frequency = parameters.GetOneFloat("frequency", 1);
            int octaves = parameters.GetOneInt("octaves", 8);
            Float roughness = parameters.GetOneFloat("roughness", .5f);
            TextureMapping3D mapping = new PointTransformMapping(pbrt.Scale(frequency, frequency, frequency));
            FloatTexture texture = null;
            if (densityTextureName.Length != 0)
            {
                // resolved by ResolveTextures() once the scene's textures exist
            }
            else if (textureName == "fbm")
                texture = new FBmTexture(mapping, octaves, roughness);
            else if (textureName == "wrinkled")
                texture = new WrinkledTexture(mapping, octaves, roughness);
            else if (textureName == "windy")
                texture = new WindyTexture(mapping);
            else
                ErrorExit(loc, $"{textureName}: texture unknown for the \"texture\" medium.");

            Float densityOffset = parameters.GetOneFloat("densityoffset", 0f);
            Float densityScale = parameters.GetOneFloat("densityscale", 1f);
            int majorantRes = parameters.GetOneInt("majorantres", 16);
            bool progressive = parameters.GetOneBool("progressive", false);
            bool decomposition = parameters.GetOneBool("decomposition", false);
            bool spectral = parameters.GetOneBool("spectral", false);
            bool adaptiveRatio = parameters.GetOneBool("adaptiveratio", false);
            bool unclamped = parameters.GetOneBool("unclamped", false);
            bool residualRatio = parameters.GetOneBool("residualratio", false);
            bool mixture = parameters.GetOneBool("mixture", false);
            Float mixtureLooseness = parameters.GetOneFloat("mixturelooseness", 4f);
            Float mixtureWeight = parameters.GetOneFloat("mixtureweight", .5f);
            int warmupPasses = parameters.GetOneInt("warmuppasses", 0);
            Float initialMajorant = parameters.GetOneFloat("initialmajorant", 0.01f);
            Float boundsFloor = parameters.GetOneFloat("boundsfloor", 0f);
            if (parameters.GetOneBool("countlookups", false) && !countLookups)
            {
                countLookups = true;
                AppDomain.CurrentDomain.ProcessExit += (object sender, EventArgs e) => Console.Error.WriteLine($"DENSITY LOOKUPS {lookupCount}");
            }
            bool hierarchical = parameters.GetOneBool("hierarchical", false);
            int hierarchyRes = parameters.GetOneInt("hierarchyres", 4);
            int hierarchyDepth = parameters.GetOneInt("hierarchydepth", 2);
            Float refineThreshold = parameters.GetOneFloat("refinethreshold", .7f);
            bool kettunen = parameters.GetOneBool("kettunen", false);
            int kettunenPoints = parameters.GetOneInt("kettunenpoints", 4);
            Float kettunenSpacing = parameters.GetOneFloat("kettunenspacing", 0f);

            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));

            return new TextureMedium(new Bounds3f(p0, p1), renderFromMedium, sigma_a, sigma_s, g, texture, densityOffset,
                                     densityScale, majorantRes, progressive, initialMajorant, warmupPasses, decomposition, spectral, adaptiveRatio, unclamped, residualRatio, mixture, mixtureLooseness,
                                     mixtureWeight, boundsFloor, hierarchical, hierarchyRes, hierarchyDepth, refineThreshold, kettunen, kettunenPoints, kettunenSpacing, densityTextureName.Length != 0, densityTextureName, sigmaATextureName, sigmaSTextureName, gTextureName, alloc);
        }
    }

    // NanoVDBMedium Definition
    // NanoVDBBuffer Definition
    public sealed unsafe class NanoVDBBuffer : nanovdb.IGridBuffer<NanoVDBBuffer>
    {
        public static void ptrAssert(void* ptr, string msg, string file, int line, bool abort = true)
        {
            if (abort)
                LOG_FATAL(StringPrintf("%p: %s (%s:%d)", (IntPtr)ptr, msg, file, line));
            else
                LOG_ERROR(StringPrintf("%p: %s (%s:%d)", (IntPtr)ptr, msg, file, line));
        }

        public NanoVDBBuffer() { }
        public NanoVDBBuffer(Allocator alloc) { this.alloc = alloc; }
        public NanoVDBBuffer(ulong size, Allocator alloc = null)
        {
            this.alloc = alloc;
            init(size);
        }
        ~NanoVDBBuffer() { clear(); }

        public void init(ulong size)
        {
            if (size == bytesAllocated)
                return;
            if (bytesAllocated > 0)
                clear();
            if (size == 0)
                return;
            bytesAllocated = size;
            ptr = (byte*)System.Runtime.InteropServices.NativeMemory.AlignedAlloc((nuint)bytesAllocated, 128);
        }

        public byte* data() { return ptr; }
        public ulong size() { return bytesAllocated; }
        public bool empty() { return size() == 0; }

        public void clear()
        {
            if (ptr != null)
                System.Runtime.InteropServices.NativeMemory.AlignedFree(ptr);
            bytesAllocated = 0;
            ptr = null;
        }

        public static NanoVDBBuffer create(ulong size, NanoVDBBuffer context)
        {
            return new NanoVDBBuffer(size, context != null ? context.GetAllocator() : null);
        }

        public Allocator GetAllocator() { return alloc; }

        private Allocator alloc;
        private ulong bytesAllocated = 0;
        private byte* ptr = null;
    }

    public sealed unsafe partial class NanoVDBMedium : Medium, IMajorantMedium<DDAMajorantIterator>
    {
        // NanoVDBMedium Method Definitions
        private static nanovdb.GridHandle<NanoVDBBuffer> readGrid(string filename, string gridName, FileLoc loc,
                                                                  Allocator alloc)
        {
            NanoVDBBuffer buf = new NanoVDBBuffer(alloc);
            nanovdb.GridHandle<NanoVDBBuffer> grid = new nanovdb.GridHandle<NanoVDBBuffer>();
            try
            {
                grid = nanovdb.io.IO.readGrid<NanoVDBBuffer>(filename, gridName, 0 /* not verbose */, buf);
            }
            catch (Exception e) when (!(e is FatalException))
            {
                ErrorExit(StringPrintf("nanovdb: %s: %s", filename, e.Message));
            }

            if (grid)
            {
                if (!grid.gridMetaData().isFogVolume() && !grid.gridMetaData().isUnknown())
                    ErrorExit(loc, StringPrintf("%s: \"%s\" isn't a FogVolume grid?", filename, gridName));

                LOG_VERBOSE("%s: found %d \"%s\" voxels", filename, grid.gridMetaData().activeVoxelCount(), gridName);
            }

            return grid;
        }

        public override bool IsEmissive() { return !temperatureFloatGrid.IsNull && LeScale > 0; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            // Sample spectra for grid $\sigmaa$ and $\sigmas$
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

            // Scale scattering coefficients by medium density at _p_
            p = renderFromMedium.ApplyInverse(p);

            nanovdb.Vec3<float> pIndex = densityFloatGrid.worldToIndexF(new nanovdb.Vec3<float>((float)p.x, (float)p.y, (float)p.z));
            Float d = new nanovdb.SampleFromVoxels<nanovdb.NanoTree<float>, float>(densityFloatGrid.tree()).Apply(pIndex);

            return new MediumProperties { sigma_a = sigma_a * d, sigma_s = sigma_s * d, phase = phase, Le = Le(p, lambda) };
        }

        public DDAMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
                return new DDAMajorantIterator();
            DCHECK_LE(tMax, raytMax);

            // Sample spectra for grid $\sigmaa$ and $\sigmas$
            SampledSpectrum sigma_a = sigma_a_spec.Sample(lambda);
            SampledSpectrum sigma_s = sigma_s_spec.Sample(lambda);

            SampledSpectrum sigma_t = sigma_a + sigma_s;
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return buf.Alloc(SampleRay(ray, tMax, lambda));
        }

        // NanoVDBMedium Private Methods
        private SampledSpectrum Le(Point3f p, SampledWavelengths lambda)
        {
            if (temperatureFloatGrid.IsNull)
                return new SampledSpectrum(0f);
            nanovdb.Vec3<float> pIndex = temperatureFloatGrid.worldToIndexF(new nanovdb.Vec3<float>((float)p.x, (float)p.y, (float)p.z));
            Float temp = new nanovdb.SampleFromVoxels<nanovdb.NanoTree<float>, float>(temperatureFloatGrid.tree()).Apply(pIndex);
            temp = (temp - temperatureOffset) * temperatureScale;
            if (temp <= 100f)
                return new SampledSpectrum(0f);
            return LeScale * new BlackbodySpectrum(temp).Sample(lambda);
        }

        public NanoVDBMedium(Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float sigmaScale, Float g,
                             nanovdb.GridHandle<NanoVDBBuffer> dg, nanovdb.GridHandle<NanoVDBBuffer> tg, Float LeScale,
                             Float temperatureOffset, Float temperatureScale, Allocator alloc)
        {
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            phase = new HGPhaseFunction(g);
            majorantGrid = new MajorantGrid(new Bounds3f(), new Point3i(64, 64, 64), alloc);
            densityGrid = dg;
            temperatureGrid = tg;
            this.LeScale = LeScale;
            this.temperatureOffset = temperatureOffset;
            this.temperatureScale = temperatureScale;
            densityFloatGrid = densityGrid.grid<float>();

            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);

            nanovdb.BBox<double> bbox = densityFloatGrid.worldBBox();
            bounds = new Bounds3f(new Point3f((Float)bbox.min()[0], (Float)bbox.min()[1], (Float)bbox.min()[2]),
                                  new Point3f((Float)bbox.max()[0], (Float)bbox.max()[1], (Float)bbox.max()[2]));

            if (temperatureGrid)
            {
                temperatureFloatGrid = temperatureGrid.grid<float>();
                float minTemperature, maxTemperature;
                temperatureFloatGrid.tree().extrema(out minTemperature, out maxTemperature);
                LOG_VERBOSE("Max temperature: %f", maxTemperature);

                nanovdb.BBox<double> tbbox = temperatureFloatGrid.worldBBox();
                bounds = Union(bounds, new Bounds3f(new Point3f((Float)tbbox.min()[0], (Float)tbbox.min()[1], (Float)tbbox.min()[2]),
                                                    new Point3f((Float)tbbox.max()[0], (Float)tbbox.max()[1], (Float)tbbox.max()[2])));
            }

            majorantGrid.bounds = bounds;

            // Initialize majorantGrid
            LOG_VERBOSE("Starting nanovdb grid GetMaxDensityGrid()");

            int gridSize = majorantGrid.res.x * majorantGrid.res.y * majorantGrid.res.z;
            nanovdb.NanoGrid<float> dgrid = densityFloatGrid;
            MajorantGrid mg = majorantGrid;
            Bounds3f bnds = bounds;
            ParallelFor(0, gridSize, (long index) =>
            {
                // Indices into majorantGrid
                int x = (int)(index % mg.res.x);
                int y = (int)((index / mg.res.x) % mg.res.y);
                int z = (int)(index / (mg.res.x * mg.res.y));
                CHECK_EQ((int)index, x + mg.res.x * (y + mg.res.y * z));

                // World (aka medium) space bounds of this max grid cell
                Bounds3f wb = new Bounds3f(bnds.Lerp(new Point3f((Float)x / mg.res.x, (Float)y / mg.res.y, (Float)z / mg.res.z)),
                                           bnds.Lerp(new Point3f((Float)(x + 1) / mg.res.x, (Float)(y + 1) / mg.res.y,
                                                                 (Float)(z + 1) / mg.res.z)));

                // Compute corresponding NanoVDB index-space bounds in floating-point.
                nanovdb.Vec3<double> i0 = dgrid.worldToIndexF(new nanovdb.Vec3<double>(wb.pMin.x, wb.pMin.y, wb.pMin.z));
                nanovdb.Vec3<double> i1 = dgrid.worldToIndexF(new nanovdb.Vec3<double>(wb.pMax.x, wb.pMax.y, wb.pMax.z));

                // Now find integer index-space bounds, accounting for both
                // filtering and the overall index bounding box.
                nanovdb.CoordBBox ibbox = dgrid.indexBBox();
                Float delta = 1f;  // Filter slop
                int nx0 = std.max((int)(i0[0] - delta), ibbox.min()[0]);
                int nx1 = std.min((int)(i1[0] + delta), ibbox.max()[0]);
                int ny0 = std.max((int)(i0[1] - delta), ibbox.min()[1]);
                int ny1 = std.min((int)(i1[1] + delta), ibbox.max()[1]);
                int nz0 = std.max((int)(i0[2] - delta), ibbox.min()[2]);
                int nz1 = std.min((int)(i1[2] + delta), ibbox.max()[2]);

                // (see the FIXME in pbrt's media.cpp about overly conservative majorants)
                float maxValue = 0;
                nanovdb.ReadAccessor<float> accessor = dgrid.getAccessor();
                // Apparently nanovdb integer bounding boxes are inclusive on
                // the upper end...
                for (int nz = nz0; nz <= nz1; ++nz)
                    for (int ny = ny0; ny <= ny1; ++ny)
                        for (int nx = nx0; nx <= nx1; ++nx)
                            maxValue = std.max(maxValue, accessor.getValue(new nanovdb.Coord(nx, ny, nz)));

                // Only write into maxGrid once when we're done to minimize
                // cache thrashing..
                mg.Set(x, y, z, maxValue);
            });

            LOG_VERBOSE("Finished nanovdb grid GetMaxDensityGrid()");
            GC.KeepAlive(densityGrid);  // (grids are raw memory owned by the handles' buffers)
        }

        public override string ToString()
        {
            return StringPrintf("[ NanoVDBMedium bounds: %s LeScale: %f " +
                                "temperatureOffset: %f temperatureScale: %f (grids elided) ]",
                                bounds, LeScale, temperatureOffset, temperatureScale);
        }

        public static NanoVDBMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                           Allocator alloc)
        {
            string filename = ResolveFilename(parameters.GetOneString("filename", ""));
            if (filename.Length == 0)
                ErrorExit(loc, "Must supply \"filename\" to \"nanovdb\" medium.");

            string gridname = parameters.GetOneString("gridname", "density");
            nanovdb.GridHandle<NanoVDBBuffer> densityGrid = readGrid(filename, gridname, loc, alloc);
            if (!densityGrid)
                ErrorExit(loc, StringPrintf("%s: didn't find \"density\" grid.", filename));
            if (densityGrid.grid<float>().IsNull)  // (the C++ dereferences a null grid here)
                ErrorExit(loc, StringPrintf("%s: \"%s\" isn't a float grid.", filename, gridname));

            string temperaturename = parameters.GetOneString("temperaturename", "temperature");
            nanovdb.GridHandle<NanoVDBBuffer> temperatureGrid = readGrid(filename, temperaturename, loc, alloc);
            if (temperatureGrid && temperatureGrid.grid<float>().IsNull)
                ErrorExit(loc, StringPrintf("%s: \"%s\" isn't a float grid.", filename, temperaturename));

            Float LeScale = parameters.GetOneFloat("Lescale", 1f);
            Float temperatureOffset = parameters.GetOneFloat("temperatureoffset", parameters.GetOneFloat("temperaturecutoff", 0f));
            Float temperatureScale = parameters.GetOneFloat("temperaturescale", 1f);

            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
                sigma_a = new ConstantSpectrum(1f);
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
                sigma_s = new ConstantSpectrum(1f);
            Float sigmaScale = parameters.GetOneFloat("scale", 1f);

            return new NanoVDBMedium(renderFromMedium, sigma_a, sigma_s, sigmaScale, g, densityGrid, temperatureGrid, LeScale,
                                     temperatureOffset, temperatureScale, alloc);
        }

        // NanoVDBMedium Private Members
        private Bounds3f bounds;
        private Transform renderFromMedium;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private HGPhaseFunction phase;
        private MajorantGrid majorantGrid;
        private nanovdb.GridHandle<NanoVDBBuffer> densityGrid;
        private nanovdb.GridHandle<NanoVDBBuffer> temperatureGrid;
        private nanovdb.NanoGrid<float> densityFloatGrid;
        private nanovdb.NanoGrid<float> temperatureFloatGrid;
        private Float LeScale, temperatureOffset, temperatureScale;
    }

    // Medium Method Definitions
    public abstract unsafe partial class Medium
    {
        public static Medium Create(string name, ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc,
                                    Allocator alloc)
        {
            Medium m = null;
            if (name == "homogeneous")
                m = HomogeneousMedium.Create(parameters, loc, alloc);
            else if (name == "uniformgrid")
            {
                m = GridMedium.Create(parameters, renderFromMedium, loc, alloc);
            }
            else if (name == "rgbgrid")
            {
                m = RGBGridMedium.Create(parameters, renderFromMedium, loc, alloc);
            }
            else if (name == "cloud")
            {
                m = CloudMedium.Create(parameters, renderFromMedium, loc, alloc);
            }
            else if (name == "texture")
            {
                m = TextureMedium.Create(parameters, renderFromMedium, loc, alloc);
            }
            else if (name == "nanovdb")
            {
                m = NanoVDBMedium.Create(parameters, renderFromMedium, loc, alloc);
            }
            else
                ErrorExit(loc, $"{name}: medium unknown.");

            if (m == null)
                ErrorExit(loc, $"{name}: unable to create medium.");

            parameters.ReportUnused();
            return m;
        }
    }

}  // namespace pbrt
