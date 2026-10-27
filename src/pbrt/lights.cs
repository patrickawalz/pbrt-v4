// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// lights.h / lights.cpp

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // LightBounds Inline Functions
        public static LightBounds Union(LightBounds a, LightBounds b)
        {
            // If one _LightBounds_ has zero power, return the other
            if (a.phi == 0)
                return b;
            if (b.phi == 0)
                return a;

            // Find average direction and updated angles for _LightBounds_
            DirectionCone cone = Union(new DirectionCone(a.w, a.cosTheta_o), new DirectionCone(b.w, b.cosTheta_o));
            Float cosTheta_o = cone.cosTheta;
            Float cosTheta_e = std.min(a.cosTheta_e, b.cosTheta_e);

            // Return final _LightBounds_ union
            return new LightBounds(Union(a.bounds, b.bounds), cone.w, a.phi + b.phi, cosTheta_o, cosTheta_e,
                                   a.twoSided | b.twoSided);
        }

        public static string ToString(LightType lf)
        {
            switch (lf)
            {
                case LightType.DeltaPosition:
                    return "DeltaPosition";
                case LightType.DeltaDirection:
                    return "DeltaDirection,";
                case LightType.Area:
                    return "Area";
                case LightType.Infinite:
                    return "Infinite";
                default:
                    LOG_FATAL("Unhandled type");
                    return "";
            }
        }

        // LightType Inline Functions
        public static bool IsDeltaLight(LightType type)
        {
            return (type == LightType.DeltaPosition || type == LightType.DeltaDirection);
        }
    }


    // LightLiSample Definition
    public unsafe struct LightLiSample
    {
        public LightLiSample(SampledSpectrum L, Vector3f wi, Float pdf, Interaction pLight)
        {
            this.L = L; this.wi = wi; this.pdf = pdf; this.pLight = pLight;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LightLiSample L: %s wi: %s pdf: %f pLight: %s ]", L, wi, pdf, pLight);
        }

        public SampledSpectrum L;
        public Vector3f wi;
        public Float pdf;
        public Interaction pLight;
    }

    // LightLeSample Definition
    public unsafe struct LightLeSample
    {
        public LightLeSample(SampledSpectrum L, Ray ray, Float pdfPos, Float pdfDir)
        {
            this.L = L; this.ray = ray; this.intr = default; this.pdfPos = pdfPos; this.pdfDir = pdfDir;
        }
        public LightLeSample(SampledSpectrum L, Ray ray, Interaction intr, Float pdfPos, Float pdfDir)
        {
            this.L = L; this.ray = ray; this.intr = intr; this.pdfPos = pdfPos; this.pdfDir = pdfDir;
            pbrt.CHECK(this.intr.value().n != new Normal3f(0, 0, 0));
        }

        public Float AbsCosTheta(Vector3f w) { return intr ? pbrt.AbsDot(w, intr.value().n) : 1; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LightLeSample L: %s ray: %s intr: %s pdfPos: %f pdfDir: %f ]", L, ray,
                                        intr ? intr.value().ToString() : "(unset)", pdfPos, pdfDir);
        }

        public SampledSpectrum L;
        public Ray ray;
        public pstd.optional<Interaction> intr;
        public Float pdfPos, pdfDir;
    }

    // LightSampleContext Definition
    public unsafe struct LightSampleContext
    {
        // LightSampleContext Public Methods
        public LightSampleContext(SurfaceInteraction si)
        {
            pi = si.pi;
            n = si.n;
            ns = si.shading.n;
        }
        public LightSampleContext(Interaction intr)
        {
            pi = intr.pi;
            n = default;
            ns = default;
        }
        public LightSampleContext(Point3fi pi, Normal3f n, Normal3f ns)
        {
            this.pi = pi; this.n = n; this.ns = ns;
        }

        public Point3f p() { return (Point3f)pi; }

        public Point3fi pi;
        public Normal3f n, ns;
    }

    // LightBounds Definition
    public unsafe struct LightBounds
    {

        public Point3f Centroid() { return (bounds.pMin + bounds.pMax) / 2; }
        // LightBounds Public Methods
        public LightBounds(Bounds3f b, Vector3f w, Float phi, Float cosTheta_o, Float cosTheta_e, bool twoSided)
        {
            this.bounds = b;
            this.w = pbrt.Normalize(w);
            this.phi = phi;
            this.cosTheta_o = cosTheta_o;
            this.cosTheta_e = cosTheta_e;
            this.twoSided = twoSided;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LightBounds bounds: %s w: %s phi: %f " +
                                        "cosTheta_o: %f cosTheta_e: %f twoSided: %s ]",
                                        bounds, w, phi, cosTheta_o, cosTheta_e, twoSided);
        }

        public Float Importance(Point3f p, Normal3f n)
        {
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
            Float cosTheta_w = pbrt.Dot(w, wi);
            if (twoSided)
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

        public Bounds3f bounds;
        public Float phi;
        public Vector3f w;
        public Float cosTheta_o, cosTheta_e;
        public bool twoSided;
    }

    // LightBase Definition
    public abstract unsafe class LightBase : Light
    {
        // STAT_COUNTER("Scene/Lights", numLights);
        // STAT_COUNTER("Scene/AreaLights", numAreaLights);
        [ThreadStatic] internal static long numLights;
        [ThreadStatic] internal static long numAreaLights;
        static StatRegisterer STATS_REG_LIGHTS = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Scene/Lights", numLights);
            numLights = 0;
            accum.ReportCounter("Scene/AreaLights", numAreaLights);
            numAreaLights = 0;
        });

        // STAT_MEMORY_COUNTER("Memory/Light image and distributions", imageBytes);
        [ThreadStatic] internal static long imageBytes;
        static StatRegisterer STATS_REG_IMAGEBYTES = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Light image and distributions", imageBytes);
            imageBytes = 0;
        });

        public override LightType Type() { return type; }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            return new SampledSpectrum(0.0f);
        }

        public override SampledSpectrum L(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda)
        {
            return new SampledSpectrum(0.0f);
        }

        // LightBase Public Methods
        public LightBase(LightType type, Transform renderFromLight, MediumInterface mediumInterface)
        {
            this.type = type;
            this.mediumInterface = mediumInterface;
            this.renderFromLight = renderFromLight;
            ++numLights;
        }

        // (C++ repeats these two in every non-area light class.)
        public override void Preprocess(Bounds3f sceneBounds) { }

        public override void PDF_Le(Interaction intr, Vector3f w, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("Shouldn't be called for non-area lights");
        }

        protected string BaseToString()
        {
            return pbrt.StringPrintf("type: %s mediumInterface: %s renderFromLight: %s", pbrt.ToString(type),
                                        mediumInterface, renderFromLight);
        }

        protected static DenselySampledSpectrum LookupSpectrum(Spectrum s)
        {
            lock (spectrumCacheLock)
            {
                if (spectrumCache == null)
                    spectrumCache = new InternCache<DenselySampledSpectrum>();
            }
            Func<Allocator, DenselySampledSpectrum, DenselySampledSpectrum> create =
                (Allocator alloc, DenselySampledSpectrum ds) => new DenselySampledSpectrum(ds, alloc);
            return spectrumCache.Lookup(new DenselySampledSpectrum(s, (Allocator)null), create);
        }

        protected LightType type;
        protected Transform renderFromLight;
        protected MediumInterface mediumInterface;
        static InternCache<DenselySampledSpectrum> spectrumCache;
        static readonly object spectrumCacheLock = new object();
    }

    // PointLight Definition
    public sealed unsafe class PointLight : LightBase
    {
        // PointLight Public Methods
        public PointLight(Transform renderFromLight, MediumInterface mediumInterface, Spectrum I, Float scale)
            : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
        {
            this.I = LookupSpectrum(I);
            this.scale = scale;
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 0;
            *pdfDir = pbrt.UniformSpherePDF();
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f wi = pbrt.Normalize(p - ctx.p());
            SampledSpectrum Li = scale * I.Sample(lambda) / pbrt.DistanceSquared(p, ctx.p());
            return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) { return 0; }

        public override SampledSpectrum Phi(SampledWavelengths lambda) { return 4 * Pi * scale * I.Sample(lambda); }

        public override pstd.optional<LightBounds> Bounds()
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Float phi = 4 * Pi * scale * I.MaxValue();
            return new LightBounds(new Bounds3f(p, p), new Vector3f(0, 0, 1), phi, std.cos(Pi), std.cos(Pi / 2), false);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Ray ray = new Ray(p, pbrt.SampleUniformSphere(u1), time, mediumInterface.outside);
            return new LightLeSample(scale * I.Sample(lambda), ray, 1, pbrt.UniformSpherePDF());
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PointLight %s I: %s scale: %f ]", BaseToString(), I, scale);
        }

        public static PointLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters,
                                        RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
            Float sc = parameters.GetOneFloat("scale", 1);

            sc /= pbrt.SpectrumToPhotometric(I);

            Float phi_v = parameters.GetOneFloat("power", -1);
            if (phi_v > 0)
            {
                // For point lights, the power is 4pi*I
                Float k_e = 4 * Pi;
                sc *= phi_v / k_e;
            }

            Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
            Transform tf = pbrt.Translate(new Vector3f(from.x, from.y, from.z));
            Transform finalRenderFromLight = renderFromLight * tf;

            return new PointLight(finalRenderFromLight, new MediumInterface(medium), I, sc);
        }

        // PointLight Private Members
        DenselySampledSpectrum I;
        Float scale;
    }

    // DistantLight Definition
    public sealed unsafe class DistantLight : LightBase
    {
        // DistantLight Public Methods
        public DistantLight(Transform renderFromLight, Spectrum Lemit, Float scale)
            : base(LightType.DeltaDirection, renderFromLight, new MediumInterface())
        {
            this.Lemit = LookupSpectrum(Lemit);
            this.scale = scale;
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) { return 0; }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 1 / (Pi * sceneRadius * sceneRadius);
            *pdfDir = 0;
        }

        public override pstd.optional<LightBounds> Bounds() { return default; }

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c; Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Vector3f wi = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            Point3f pOutside = ctx.p() + wi * (2 * sceneRadius);
            return new LightLiSample(scale * Lemit.Sample(lambda), wi, 1, new Interaction(pOutside, (MediumInterface)null));
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return scale * Lemit.Sample(lambda) * Pi * pbrt.Sqr(sceneRadius);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Choose point on disk oriented toward infinite light direction
            Vector3f w = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            Frame wFrame = Frame.FromZ(w);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u1);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));

            // Compute _DistantLight_ light ray
            Ray ray = new Ray(pDisk + sceneRadius * w, -w, time);

            return new LightLeSample(scale * Lemit.Sample(lambda), ray, 1 / (Pi * pbrt.Sqr(sceneRadius)), 1);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DistantLight %s Lemit: %s scale: %f ]", BaseToString(), Lemit, scale);
        }

        public static DistantLight Create(Transform renderFromLight, ParameterDictionary parameters,
                                          RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Spectrum L = parameters.GetOneSpectrum("L", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
            Float sc = parameters.GetOneFloat("scale", 1);

            Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
            Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));

            Vector3f w = pbrt.Normalize(from - to);
            Vector3f v1, v2;
            pbrt.CoordinateSystem(w, &v1, &v2);
            SquareMatrix4 m = new SquareMatrix4(v1.x, v2.x, w.x, 0, v1.y, v2.y, w.y, 0, v1.z, v2.z, w.z, 0, 0, 0, 0, 1);
            Transform t = new Transform(m);
            Transform finalRenderFromLight = renderFromLight * t;

            // Scale the light spectrum to be equivalent to 1 nit
            sc /= pbrt.SpectrumToPhotometric(L);

            // Adjust scale to meet target illuminance value
            // Like for IBLs we measure illuminance as incident on an upward-facing patch.
            Float E_v = parameters.GetOneFloat("illuminance", -1);
            if (E_v > 0)
                sc *= E_v;

            return new DistantLight(finalRenderFromLight, L, sc);
        }

        // DistantLight Private Members
        DenselySampledSpectrum Lemit;
        Float scale;
        Point3f sceneCenter;
        Float sceneRadius;
    }

    // ProjectionLight Definition
    public sealed unsafe class ProjectionLight : LightBase
    {

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 0;
            // Transform ray direction to light space and reject invalid ones
            Vector3f w = pbrt.Normalize(renderFromLight.ApplyInverse(ray.d));
            if (w.z < hither)
            {
                *pdfDir = 0;
                return;
            }

            // Compute screen space point _ps_ and check containment
            Point3f ps = screenFromLight.Apply((Point3f)w);
            if (!pbrt.Inside(new Point2f(ps.x, ps.y), screenBounds))
            {
                *pdfDir = 0;
                return;
            }

            *pdfDir = distrib.PDF(new Point2f(ps.x, ps.y)) * screenBounds.Area() / (A * pbrt.Pow(3, pbrt.CosTheta(w)));
        }
        // ProjectionLight Method Definitions
        public ProjectionLight(Transform renderFromLight, MediumInterface mediumInterface, Image im,
                               RGBColorSpace imageColorSpace, Float scale, Float fov, Allocator alloc)
            : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
        {
            this.image = im;
            this.imageColorSpace = imageColorSpace;
            this.scale = scale;
            this.distrib = new PiecewiseConstant2D(alloc);

            // Initialize _ProjectionLight_ projection transform and screen bounds
            Float aspect = (Float)image.Resolution().x / (Float)image.Resolution().y;
            if (aspect > 1)
                screenBounds = new Bounds2f(new Point2f(-aspect, -1), new Point2f(aspect, 1));
            else
                screenBounds = new Bounds2f(new Point2f(-1, -1 / aspect), new Point2f(1, 1 / aspect));
            screenFromLight = pbrt.Perspective(fov, hither, 1e30f /* yon */);
            lightFromScreen = pbrt.Inverse(screenFromLight);

            // Compute projection image area _A_
            Float opposite = std.tan(pbrt.Radians(fov) / 2);
            A = 4 * pbrt.Sqr(opposite) * (aspect > 1 ? aspect : (1 / aspect));

            // Compute sampling distribution for _ProjectionLight_
            ImageChannelDesc channelDesc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            if (!channelDesc)
                pbrt.ErrorExit("Image used for ProjectionLight does not have R, G, B channels.");
            pbrt.CHECK_EQ(3, (int)channelDesc.size());
            pbrt.CHECK(channelDesc.IsIdentity());
            Transform lfs = lightFromScreen;
            Func<Point2f, Float> dwdA = (Point2f p) =>
            {
                Vector3f w = (Vector3f)lfs.Apply(new Point3f(p.x, p.y, 0));
                return pbrt.Pow(3, pbrt.CosTheta(pbrt.Normalize(w)));
            };
            Array2D<Float> d = image.GetSamplingDistribution(dwdA, screenBounds);
            distrib = new PiecewiseConstant2D(d, screenBounds);
            imageBytes += (long)(image.BytesUsed() + distrib.BytesUsed());
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            // Return sample for incident radiance from _ProjectionLight_
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f wi = pbrt.Normalize(p - ctx.p());
            // Compute _ProjectionLight's_ incident radiance _Li_
            Vector3f wl = renderFromLight.ApplyInverse(-wi);
            SampledSpectrum Li = I(wl, lambda) / pbrt.DistanceSquared(p, ctx.p());

            if (!Li)
                return default;
            return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false)
        {
            return 0.0f;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ProjectionLight %s scale: %f A: %f ]", BaseToString(), scale, A);
        }

        public SampledSpectrum I(Vector3f w, SampledWavelengths lambda)
        {
            // Discard directions behind projection light
            if (w.z < hither)
                return new SampledSpectrum(0.0f);

            // Project point onto projection plane and compute RGB
            Point3f ps = screenFromLight.Apply((Point3f)w);
            if (!pbrt.Inside(new Point2f(ps.x, ps.y), screenBounds))
                return new SampledSpectrum(0.0f);
            Point2f uv = (Point2f)screenBounds.Offset(new Point2f(ps.x, ps.y));
            RGB rgb = default;
            for (int c = 0; c < 3; ++c)
                rgb[c] = image.LookupNearestChannel(uv, c);

            // Return scaled wavelength samples corresponding to RGB
            RGBIlluminantSpectrum s = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
            return scale * s.Sample(lambda);
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            SampledSpectrum sum = new SampledSpectrum(0.0f);
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Point2f ps = screenBounds.Lerp(new Point2f((x + 0.5f) / image.Resolution().x,
                                                               (y + 0.5f) / image.Resolution().y));
                    Vector3f w = (Vector3f)lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0));
                    w = pbrt.Normalize(w);
                    Float dwdA = pbrt.Pow(3, pbrt.CosTheta(w));

                    RGB rgb = default;
                    for (int c = 0; c < 3; ++c)
                        rgb[c] = image.GetChannel(new Point2i(x, y), c);
                    RGBIlluminantSpectrum s = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
                    sum += s.Sample(lambda) * dwdA;
                }
            return scale * A * sum / (image.Resolution().x * image.Resolution().y);
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            Float sum = 0;
            for (int v = 0; v < image.Resolution().y; ++v)
                for (int u = 0; u < image.Resolution().x; ++u)
                    sum += std.max(image.GetChannel(new Point2i(u, v), 0),
                                   std.max(image.GetChannel(new Point2i(u, v), 1), image.GetChannel(new Point2i(u, v), 2)));
            Float phi = scale * sum / (image.Resolution().x * image.Resolution().y);

            Point3f pCorner = new Point3f(screenBounds.pMax.x, screenBounds.pMax.y, 0);
            Vector3f wCorner = pbrt.Normalize((Vector3f)lightFromScreen.Apply(pCorner));
            Float cosTotalWidth = pbrt.CosTheta(wCorner);

            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f w = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            return new LightBounds(new Bounds3f(p, p), w, phi, std.cos(0.0f), cosTotalWidth, false);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample point on projection light image plane
            Float pdf;
            Point2f ps = distrib.Sample(u1, &pdf);
            if (pdf == 0)
                return default;

            // Compute projection light ray and compute PDF
            Vector3f w = (Vector3f)lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0));
            Float cosTheta = pbrt.CosTheta(pbrt.Normalize(w));
            pbrt.CHECK_GT(cosTheta, 0);
            Float pdfDir = pdf * screenBounds.Area() / (A * pbrt.Pow(3, cosTheta));

            // Compute radiance for projection light ray
            Point2f p = (Point2f)screenBounds.Offset(ps);
            RGB rgb = default;
            for (int c = 0; c < 3; ++c)
                rgb[c] = image.LookupNearestChannel(p, c);
            SampledSpectrum L = scale * new RGBIlluminantSpectrum(imageColorSpace, rgb).Sample(lambda);

            Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), pbrt.Normalize(w), time,
                                                    mediumInterface.outside));
            return new LightLeSample(L, ray, 1, pdfDir);
        }

        public static ProjectionLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters,
                                             FileLoc loc, Allocator alloc)
        {
            Float scale = parameters.GetOneFloat("scale", 1);
            Float power = parameters.GetOneFloat("power", -1);
            Float fov = parameters.GetOneFloat("fov", 90.0f);

            string texname = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            if (texname.Length == 0)
                pbrt.ErrorExit(loc, "Must provide \"filename\" to \"projection\" light source");

            ImageAndMetadata imageAndMetadata = Image.Read(texname, alloc);
            if (imageAndMetadata.image.HasAnyInfinitePixels())
                pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                           "%s: image has infinite pixel values and so is not suitable as a light.",
                                           texname));
            if (imageAndMetadata.image.HasAnyNaNPixels())
                pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                           "%s: image has not-a-number pixel values and so is not suitable as a light.",
                                           texname));

            RGBColorSpace colorSpace = imageAndMetadata.metadata.GetColorSpace();
            ImageChannelDesc channelDesc = imageAndMetadata.image.GetChannelDesc(new string[] { "R", "G", "B" });
            if (!channelDesc)
                pbrt.ErrorExit(loc, "Image provided to \"projection\" light must have R, G, " +
                                       "and B channels.");
            Image image = imageAndMetadata.image.SelectChannels(channelDesc, alloc);

            scale /= pbrt.SpectrumToPhotometric(colorSpace.illuminant);

            if (power > 0)
            {
                // template: k_e = hemisphere...
                Bounds2f screenBounds;
                Float A;
                Transform lightFromScreen, screenFromLight;
                Float hither = 1e-3f;
                Float aspect = (Float)image.Resolution().x / (Float)image.Resolution().y;
                if (aspect > 1)
                    screenBounds = new Bounds2f(new Point2f(-aspect, -1), new Point2f(aspect, 1));
                else
                    screenBounds = new Bounds2f(new Point2f(-1, -1 / aspect), new Point2f(1, 1 / aspect));
                screenFromLight = pbrt.Perspective(fov, hither, 1e30f /* yon */);
                lightFromScreen = pbrt.Inverse(screenFromLight);

                Float opposite = std.tan(pbrt.Radians(fov) / 2);
                A = 4 * pbrt.Sqr(opposite) * (aspect > 1 ? aspect : (1 / aspect));

                Float sum = 0;
                RGB luminance = colorSpace.LuminanceVector();
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                    {
                        Point2f ps = screenBounds.Lerp(new Point2f((x + .5f) / image.Resolution().x,
                                                                   (y + .5f) / image.Resolution().y));
                        Vector3f w = (Vector3f)lightFromScreen.Apply(new Point3f(ps.x, ps.y, 0));
                        w = pbrt.Normalize(w);
                        Float dwdA = pbrt.Pow(3, w.z);

                        for (int c = 0; c < 3; ++c)
                            sum += image.GetChannel(new Point2i(x, y), c) * luminance[c] * dwdA;
                    }

                scale *= power / (A * sum / (image.Resolution().x * image.Resolution().y));
            }

            Transform flip = pbrt.Scale(1, -1, 1);
            Transform renderFromLightFlipY = renderFromLight * flip;

            return new ProjectionLight(renderFromLightFlipY, new MediumInterface(medium), image, colorSpace, scale,
                                       fov, alloc);
        }

        // ProjectionLight Private Members
        Image image;
        RGBColorSpace imageColorSpace;
        Float scale;
        Bounds2f screenBounds;
        Float hither = 1e-3f;
        Transform screenFromLight, lightFromScreen;
        Float A;
        PiecewiseConstant2D distrib;
    }

    // GoniometricLight Definition
    public sealed unsafe class GoniometricLight : LightBase
    {

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 0.0f;
            Vector3f wLight = pbrt.Normalize(renderFromLight.ApplyInverse(ray.d));
            Point2f uv = pbrt.EqualAreaSphereToSquare(wLight);
            *pdfDir = distrib.PDF(uv) / (4 * Pi);
        }
        // GoniometricLight Public Methods
        public GoniometricLight(Transform renderFromLight, MediumInterface mediumInterface, Spectrum Iemit, Float scale,
                                Image im, Allocator alloc)
            : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
        {
            this.Iemit = LookupSpectrum(Iemit);
            this.scale = scale;
            this.image = im;
            this.distrib = new PiecewiseConstant2D(alloc);
            pbrt.CHECK_EQ(1, image.NChannels());
            pbrt.CHECK_EQ(image.Resolution().x, image.Resolution().y);
            Array2D<Float> d = image.GetSamplingDistribution();
            distrib = new PiecewiseConstant2D(d);
            imageBytes += (long)(image.BytesUsed() + distrib.BytesUsed());
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f wi = pbrt.Normalize(p - ctx.p());
            SampledSpectrum L = I(renderFromLight.ApplyInverse(-wi), lambda) / pbrt.DistanceSquared(p, ctx.p());
            return new LightLiSample(L, wi, 1, new Interaction(p, mediumInterface));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false)
        {
            return 0.0f;
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            Float sumY = 0;
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    sumY += image.GetChannel(new Point2i(x, y), 0);
            return scale * Iemit.Sample(lambda) * 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            Float sumY = 0;
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    sumY += image.GetChannel(new Point2i(x, y), 0);
            Float phi = scale * Iemit.MaxValue() * 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);

            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            return new LightBounds(new Bounds3f(p, p), new Vector3f(0, 0, 1), phi, std.cos(Pi), std.cos(Pi / 2), false);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample direction and compute PDF
            Float pdf;
            Point2f uv = distrib.Sample(u1, &pdf);
            Vector3f wLight = pbrt.EqualAreaSquareToSphere(uv);
            Float pdfDir = pdf / (4 * Pi);

            Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), wLight, time, mediumInterface.outside));
            return new LightLeSample(I(wLight, lambda), ray, 1, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ GoniometricLight %s Iemit: %s scale: %f ]", BaseToString(), Iemit, scale);
        }

        public static GoniometricLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters,
                                              RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
            Float sc = parameters.GetOneFloat("scale", 1);

            Image image = new Image(alloc);
            string texname = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            if (texname.Length == 0)
                pbrt.Warning(loc, "No \"filename\" parameter provided for goniometric light.");
            else
            {
                ImageAndMetadata imageAndMetadata = Image.Read(texname, alloc);

                if (imageAndMetadata.image.HasAnyInfinitePixels())
                    pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                               "%s: image has infinite pixel values and so is not suitable as a light.",
                                               texname));
                if (imageAndMetadata.image.HasAnyNaNPixels())
                    pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                               "%s: image has not-a-number pixel values and so is not suitable as " +
                                               "a light.", texname));

                if (imageAndMetadata.image.Resolution().x != imageAndMetadata.image.Resolution().y)
                    pbrt.ErrorExit(pbrt.StringPrintf(
                        "%s: image resolution (%d, %d) is non-square. It's unlikely " +
                        "this is an equal-area environment map.", texname, imageAndMetadata.image.Resolution().x,
                        imageAndMetadata.image.Resolution().y));
                ImageChannelDesc rgbDesc = imageAndMetadata.image.GetChannelDesc(new string[] { "R", "G", "B" });
                ImageChannelDesc yDesc = imageAndMetadata.image.GetChannelDesc(new string[] { "Y" });

                if (rgbDesc)
                {
                    if (yDesc)
                        pbrt.ErrorExit(pbrt.StringPrintf("%s: has both \"R\", \"G\", and \"B\" or \"Y\" " +
                                                               "channels.", texname));
                    // Create a new Image with only a Y channel
                    image = new Image(imageAndMetadata.image.Format(), imageAndMetadata.image.Resolution(),
                                      new string[] { "Y" }, imageAndMetadata.image.Encoding(), alloc);
                    for (int y = 0; y < image.Resolution().y; ++y)
                        for (int x = 0; x < image.Resolution().x; ++x)
                            image.SetChannel(new Point2i(x, y), 0,
                                             imageAndMetadata.image.GetChannels(new Point2i(x, y), rgbDesc).Average());
                }
                else if (yDesc)
                    image = imageAndMetadata.image;
                else
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: has neither \"R\", \"G\", and \"B\" or \"Y\" " +
                                                                "channels.", texname));
            }

            sc /= pbrt.SpectrumToPhotometric(I);

            Float phi_v = parameters.GetOneFloat("power", -1);
            if (phi_v > 0)
            {
                // integrate over sphere of directions
                Float sumY = 0;
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                        sumY += image.GetChannel(new Point2i(x, y), 0);
                Float k_e = 4 * Pi * sumY / (image.Resolution().x * image.Resolution().y);
                sc *= phi_v / k_e;
            }

            // Swap y and z...
            SquareMatrix4 swapYZ = new SquareMatrix4(1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1);
            Transform t = new Transform(swapYZ);
            Transform finalRenderFromLight = renderFromLight * t;

            return new GoniometricLight(finalRenderFromLight, new MediumInterface(medium), I, sc, image, alloc);
        }

        public SampledSpectrum I(Vector3f w, SampledWavelengths lambda)
        {
            Point2f uv = pbrt.EqualAreaSphereToSquare(w);
            return scale * Iemit.Sample(lambda) * image.LookupNearestChannel(uv, 0);
        }

        // GoniometricLight Private Members
        DenselySampledSpectrum Iemit;
        Float scale;
        Image image;
        PiecewiseConstant2D distrib;
    }

    // DiffuseAreaLight Definition
    public sealed unsafe class DiffuseAreaLight : LightBase
    {
        public override void PDF_Le(Interaction intr, Vector3f w, Float* pdfPos, Float* pdfDir)
        {
            pbrt.CHECK_NE(intr.n, new Normal3f(0, 0, 0));
            *pdfPos = shape.PDF(intr);
            *pdfDir = twoSided ? (pbrt.CosineHemispherePDF(pbrt.AbsDot(intr.n, w)) / 2)
                               : pbrt.CosineHemispherePDF(pbrt.Dot(intr.n, w));
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            pbrt.LOG_FATAL("Shouldn't be called for area lights");
        }

        bool AlphaMasked(Interaction intr)
        {
            if (alpha == null)
                return false;
            Float a = new UniversalTextureEvaluator().Apply(alpha, new TextureEvalContext(intr));
            if (a >= 1)
                return false;
            if (a <= 0)
                return true;
            return pbrt.HashFloat(intr.p()) > a;
        }

        // DiffuseAreaLight Public Methods
        public DiffuseAreaLight(Transform renderFromLight, MediumInterface mediumInterface, Spectrum Le, Float scale,
                                Shape shape, FloatTexture alpha, Image im, RGBColorSpace imageColorSpace,
                                bool twoSided)
            : base(((Func<FloatTexture, LightType>)((FloatTexture alpha_) =>
            {
                // Special case handling for area lights with constant zero-valued alpha
                // textures to allow invisible area lights: we will null out the alpha
                // texture below so that as far as the DiffuseAreaLight is concerned, there
                // is no alpha texture and the light is fully emissive. However, such lights
                // will never be intersected by rays (because their associated primitives
                // still have the alpha texture), so we mark them as DeltaPosition lights
                // here so that MIS isn't used for direct illumination. Thus, light sampling
                // is the only strategy used and we get an unbiased (if potentially high
                // variance) estimate.

                FloatConstantTexture fc = alpha_ as FloatConstantTexture;
                if (fc != null && fc.Evaluate(new TextureEvalContext()) == 0)
                    return LightType.DeltaPosition;
                return LightType.Area;
            }))(alpha), renderFromLight, mediumInterface)
        {
            this.shape = shape;
            this.alpha = type == LightType.Area ? alpha : null;
            this.area = shape.Area();
            this.twoSided = twoSided;
            this.Lemit = LookupSpectrum(Le);
            this.scale = scale;
            this.image = im;
            this.imageColorSpace = imageColorSpace;

            ++numAreaLights;

            if (image)
            {
                ImageChannelDesc desc = image.GetChannelDesc(new string[] { "R", "G", "B" });
                if (!desc)
                    pbrt.ErrorExit("Image used for DiffuseAreaLight doesn't have R, G, B " +
                                      "channels.");
                pbrt.CHECK_EQ(3, (int)desc.size());
                pbrt.CHECK(desc.IsIdentity());
                pbrt.CHECK(imageColorSpace != null);
            }
            else
            {
                pbrt.CHECK(Le != null);
            }

            // Warn if light has transformation with non-uniform scale, though not
            // for Triangles or bilinear patches, since this doesn't matter for them.
            if (renderFromLight.HasScale() && !(shape is Triangle) && !(shape is BilinearPatch))
                pbrt.Warning("Scaling detected in rendering to light space transformation! " +
                                "The system has numerous assumptions, implicit and explicit, " +
                                "that this transform will have no scale factors in it. " +
                                "Proceed at your own risk; your image may have errors.");
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            // Sample point on shape for _DiffuseAreaLight_
            ShapeSampleContext shapeCtx = new ShapeSampleContext(ctx.pi, ctx.n, ctx.ns, 0 /* time */);
            pstd.optional<ShapeSample> ss = shape.Sample(shapeCtx, u);
            if (!ss || ss.value().pdf == 0 || pbrt.LengthSquared(ss.value().intr.p() - ctx.p()) == 0)
                return default;
            pbrt.DCHECK(!pbrt.IsNaN(ss.value().pdf));
            ss.value().intr.mediumInterface = mediumInterface;

            // Return _LightLiSample_ for sampled point on shape
            // Return no light sample if alpha test fails
            if (AlphaMasked(ss.value().intr))
                return default;

            // Return _LightLiSample_ for sampled point on shape
            Vector3f wi = pbrt.Normalize(ss.value().intr.p() - ctx.p());
            SampledSpectrum Le = L(ss.value().intr.p(), ss.value().intr.n, ss.value().intr.uv, -wi, lambda);
            if (!Le)
                return default;
            return new LightLiSample(Le, wi, ss.value().pdf, ss.value().intr);
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false)
        {
            ShapeSampleContext shapeCtx = new ShapeSampleContext(ctx.pi, ctx.n, ctx.ns, 0 /* time */);
            return shape.PDF(shapeCtx, wi);
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            SampledSpectrum L = new SampledSpectrum(0.0f);
            if (image)
            {
                // Compute average light image emission
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                    {
                        RGB rgb = default;
                        for (int c = 0; c < 3; ++c)
                            rgb[c] = image.GetChannel(new Point2i(x, y), c);
                        L += new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb)).Sample(lambda);
                    }
                L *= scale / (image.Resolution().x * image.Resolution().y);
            }
            else
                L = Lemit.Sample(lambda) * scale;
            return Pi * (twoSided ? 2 : 1) * area * L;
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            // Compute _phi_ for diffuse area light bounds
            Float phi = 0;
            if (image)
            {
                // Compute average _DiffuseAreaLight_ image channel value
                // Assume no distinction between colors for an RGB image
                for (int y = 0; y < image.Resolution().y; ++y)
                    for (int x = 0; x < image.Resolution().x; ++x)
                        for (int c = 0; c < 3; ++c)
                            phi += image.GetChannel(new Point2i(x, y), c);
                phi /= 3 * image.Resolution().x * image.Resolution().y;
            }
            else
                phi = Lemit.MaxValue();
            phi *= scale * area * Pi;

            DirectionCone nb = shape.NormalBounds();
            return new LightBounds(shape.Bounds(), nb.w, phi, nb.cosTheta, std.cos(Pi / 2), twoSided);
        }

        public override SampledSpectrum L(Point3f p, Normal3f n, Point2f uv, Vector3f w, SampledWavelengths lambda)
        {
            // Check for zero emitted radiance from point on area light
            if (!twoSided && pbrt.Dot(n, w) < 0)
                return new SampledSpectrum(0.0f);
            if (AlphaMasked(new Interaction(p, uv)))
                return new SampledSpectrum(0.0f);

            if (image)
            {
                // Return _DiffuseAreaLight_ emission using image
                RGB rgb = default;
                uv.y = 1 - uv.y;
                for (int c = 0; c < 3; ++c)
                    rgb[c] = image.BilerpChannel(uv, c);
                RGBIlluminantSpectrum spec = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
                return scale * spec.Sample(lambda);
            }
            else
                return scale * Lemit.Sample(lambda);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample a point on the area light's _Shape_
            pstd.optional<ShapeSample> ss = shape.Sample(u1);
            if (!ss)
                return default;
            ss.value().intr.time = time;
            ss.value().intr.mediumInterface = mediumInterface;

            // Return sampled point if alpha masks it
            if (AlphaMasked(ss.value().intr))
                return default;

            // Sample a cosine-weighted outgoing direction _w_ for area light
            Vector3f w;
            Float pdfDir;
            if (twoSided)
            {
                // Choose side of surface and sample cosine-weighted outgoing direction
                if (u2[0] < 0.5f)
                {
                    u2.x = std.min(u2[0] * 2, (Float)OneMinusEpsilon);
                    w = pbrt.SampleCosineHemisphere(u2);
                }
                else
                {
                    u2.x = std.min((u2[0] - 0.5f) * 2, (Float)OneMinusEpsilon);
                    w = pbrt.SampleCosineHemisphere(u2);
                    w.z *= -1;
                }
                pdfDir = pbrt.CosineHemispherePDF(std.abs(w.z)) / 2;
            }
            else
            {
                w = pbrt.SampleCosineHemisphere(u2);
                pdfDir = pbrt.CosineHemispherePDF(w.z);
            }

            if (pdfDir == 0)
                return default;
            // Return _LightLeSample_ for ray leaving area light
            Interaction intr = ss.value().intr;
            Frame nFrame = Frame.FromZ(intr.n);
            w = nFrame.FromLocal(w);
            SampledSpectrum Le = L(intr.p(), intr.n, intr.uv, w, lambda);
            return new LightLeSample(Le, new Ray(intr.SpawnRay(w)), intr, ss.value().pdf, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ DiffuseAreaLight %s Lemit: %s scale: %f shape: %s alpha: %s " +
                                        "twoSided: %s area: %f image: %s ]",
                                        BaseToString(), Lemit, scale, shape, alpha, twoSided ? "true" : "false", area,
                                        image);
        }

        // DiffuseAreaLight Private Members
        Shape shape;
        FloatTexture alpha;
        Float area;
        bool twoSided;
        DenselySampledSpectrum Lemit;
        Float scale;
        Image image;
        RGBColorSpace imageColorSpace;

        public static DiffuseAreaLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters,
                                              RGBColorSpace colorSpace, FileLoc loc, Allocator alloc, Shape shape,
                                              FloatTexture alphaTex)
        {
            Spectrum L = parameters.GetOneSpectrum("L", null, SpectrumType.Illuminant, alloc);
            Float scale = parameters.GetOneFloat("scale", 1);
            bool twoSided = parameters.GetOneBool("twosided", false);

            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            Image image = new Image(alloc);
            RGBColorSpace imageColorSpace = null;
            if (filename.Length != 0)
            {
                if (L != null)
                    pbrt.ErrorExit(loc, "Both \"L\" and \"filename\" specified for DiffuseAreaLight.");
                ImageAndMetadata im = Image.Read(filename, alloc);

                if (im.image.HasAnyInfinitePixels())
                    pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                               "%s: image has infinite pixel values and so is not suitable as a light.",
                                               filename));
                if (im.image.HasAnyNaNPixels())
                    pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                               "%s: image has not-a-number pixel values and so is not suitable as " +
                                               "a light.", filename));

                ImageChannelDesc channelDesc = im.image.GetChannelDesc(new string[] { "R", "G", "B" });
                if (!channelDesc)
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: Image provided to \"diffuse\" area light must have " +
                                                                "R, G, and B channels.", filename));
                image = im.image.SelectChannels(channelDesc, alloc);

                imageColorSpace = im.metadata.GetColorSpace();
            }
            else if (L == null)
                L = colorSpace.illuminant;

            // scale so that radiance is equivalent to 1 nit
            scale /= pbrt.SpectrumToPhotometric(L != null ? L : colorSpace.illuminant);

            Float phi_v = parameters.GetOneFloat("power", -1.0f);
            if (phi_v > 0)
            {
                // k_e is the emissive power of the light as defined by the spectral
                // distribution and texture and is used to normalize the emitted
                // radiance such that the user-defined power will be the actual power
                // emitted by the light.
                Float k_e = 1;

                if (image)
                {
                    // Get the appropriate luminance vector from the image colour space
                    RGB lum = imageColorSpace.LuminanceVector();
                    // we need to know which channels correspond to R, G and B
                    // we know that the channelDesc is valid as we would have exited in the
                    // block above otherwise
                    k_e = 0;
                    for (int y = 0; y < image.Resolution().y; ++y)
                        for (int x = 0; x < image.Resolution().x; ++x)
                        {
                            for (int c = 0; c < 3; ++c)
                                k_e += image.GetChannel(new Point2i(x, y), c) * lum[c];
                        }
                    k_e /= image.Resolution().x * image.Resolution().y;
                }

                k_e *= (twoSided ? 2 : 1) * shape.Area() * Pi;

                // now multiply up scale to hit the target power
                scale *= phi_v / k_e;
            }

            return new DiffuseAreaLight(renderFromLight, new MediumInterface(medium), L, scale, shape, alphaTex, image,
                                        imageColorSpace, twoSided);
        }
    }

    // UniformInfiniteLight Definition
    public sealed unsafe class UniformInfiniteLight : LightBase
    {

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c; Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfDir = pbrt.UniformSpherePDF();
            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
        }

        public override pstd.optional<LightBounds> Bounds() { return default; }
        // UniformInfiniteLight Method Definitions
        public UniformInfiniteLight(Transform renderFromLight, Spectrum Lemit, Float scale)
            : base(LightType.Infinite, renderFromLight, new MediumInterface())
        {
            this.Lemit = LookupSpectrum(Lemit);
            this.scale = scale;
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda) { return scale * Lemit.Sample(lambda); }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            if (allowIncompletePDF)
                return default;
            // Return uniform spherical sample for uniform infinite light
            Vector3f wi = pbrt.SampleUniformSphere(u);
            Float pdf = pbrt.UniformSpherePDF();
            return new LightLiSample(scale * Lemit.Sample(lambda), wi, pdf,
                                     new Interaction(ctx.p() + wi * (2 * sceneRadius), mediumInterface));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            if (allowIncompletePDF)
                return 0;
            return pbrt.UniformSpherePDF();
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return 4 * Pi * Pi * pbrt.Sqr(sceneRadius) * scale * Lemit.Sample(lambda);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample direction for uniform infinite light ray
            Vector3f w = pbrt.SampleUniformSphere(u1);

            // Compute infinite light sample ray
            Frame wFrame = Frame.FromZ(-w);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * -w, w, time);

            Float pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
            Float pdfDir = pbrt.UniformSpherePDF();
            return new LightLeSample(scale * Lemit.Sample(lambda), ray, pdfPos, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ UniformInfiniteLight %s Lemit: %s ]", BaseToString(), Lemit);
        }

        // UniformInfiniteLight Private Members
        DenselySampledSpectrum Lemit;
        Float scale;
        Point3f sceneCenter;
        Float sceneRadius;
    }

    // ImageInfiniteLight Definition
    public sealed unsafe class ImageInfiniteLight : LightBase
    {

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c; Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            Vector3f wl = -renderFromLight.ApplyInverse(ray.d);
            Float mapPDF = distribution.PDF(pbrt.EqualAreaSphereToSquare(wl));
            *pdfDir = mapPDF / (4 * Pi);
            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            Vector3f wLight = pbrt.Normalize(renderFromLight.ApplyInverse(ray.d));
            Point2f uv = pbrt.EqualAreaSphereToSquare(wLight);
            return ImageLe(uv, lambda);
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            // Find $(u,v)$ sample coordinates in infinite light texture
            Float mapPDF = 0;
            Point2f uv;
            if (allowIncompletePDF)
                uv = compensatedDistribution.Sample(u, &mapPDF);
            else
                uv = distribution.Sample(u, &mapPDF);
            if (mapPDF == 0)
                return default;

            // Convert infinite light sample point to direction
            Vector3f wLight = pbrt.EqualAreaSquareToSphere(uv);
            Vector3f wi = renderFromLight.Apply(wLight);

            // Compute PDF for sampled infinite light direction
            Float pdf = mapPDF / (4 * Pi);

            // Return radiance value for infinite light direction
            return new LightLiSample(ImageLe(uv, lambda), wi, pdf,
                                     new Interaction(ctx.p() + wi * (2 * sceneRadius), mediumInterface));
        }

        public override pstd.optional<LightBounds> Bounds() { return default; }

        // ImageInfiniteLight Private Methods
        SampledSpectrum ImageLe(Point2f uv, SampledWavelengths lambda)
        {
            RGB rgb = default;
            for (int c = 0; c < 3; ++c)
                rgb[c] = image.LookupNearestChannel(uv, c, WrapMode.OctahedralSphere);
            RGBIlluminantSpectrum spec = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
            return scale * spec.Sample(lambda);
        }
        // ImageInfiniteLight Method Definitions
        public ImageInfiniteLight(Transform renderFromLight, Image im, RGBColorSpace imageColorSpace, Float scale,
                                  string filename, Allocator alloc)
            : base(LightType.Infinite, renderFromLight, new MediumInterface())
        {
            this.image = im;
            this.imageColorSpace = imageColorSpace;
            this.scale = scale;
            this.distribution = new PiecewiseConstant2D(alloc);
            this.compensatedDistribution = new PiecewiseConstant2D(alloc);

            // Initialize sampling PDFs for image infinite area light
            ImageChannelDesc channelDesc = image.GetChannelDesc(new string[] { "R", "G", "B" });
            if (!channelDesc)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: image used for ImageInfiniteLight doesn't have R, G, B " +
                                                       "channels.", filename));
            pbrt.CHECK_EQ(3, (int)channelDesc.size());
            pbrt.CHECK(channelDesc.IsIdentity());
            if (image.Resolution().x != image.Resolution().y)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: image resolution (%d, %d) is non-square. It's unlikely " +
                                                       "this is an equal area environment map.",
                                                       filename, image.Resolution().x, image.Resolution().y));
            Array2D<Float> d = image.GetSamplingDistribution();
            Bounds2f domain = new Bounds2f(new Point2f(0, 0), new Point2f(1, 1));
            distribution = new PiecewiseConstant2D(d, domain, alloc);

            // Initialize compensated PDF for image infinite area light
            double sum = 0;
            for (int y = 0; y < d.YSize(); ++y)
                for (int x = 0; x < d.XSize(); ++x)
                    sum += d[x, y];
            Float average = (Float)(sum / (d.XSize() * d.YSize()));
            bool allZero = true;
            for (int y = 0; y < d.YSize(); ++y)
                for (int x = 0; x < d.XSize(); ++x)
                {
                    d[x, y] = std.max<Float>(d[x, y] - average, 0);
                    if (d[x, y] != 0)
                        allZero = false;
                }
            if (allZero)
                for (int y = 0; y < d.YSize(); ++y)
                    for (int x = 0; x < d.XSize(); ++x)
                        d[x, y] = 1;
            compensatedDistribution = new PiecewiseConstant2D(d, domain, alloc);
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            Vector3f wLight = renderFromLight.ApplyInverse(w);
            Point2f uv = pbrt.EqualAreaSphereToSquare(wLight);
            Float pdf = 0;
            if (allowIncompletePDF)
                pdf = compensatedDistribution.PDF(uv);
            else
                pdf = distribution.PDF(uv);
            return pdf / (4 * Pi);
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            // We're computing fluence, then converting to power...
            SampledSpectrum sumL = new SampledSpectrum(0.0f);

            int width = image.Resolution().x, height = image.Resolution().y;
            for (int v = 0; v < height; ++v)
            {
                for (int u = 0; u < width; ++u)
                {
                    RGB rgb = default;
                    for (int c = 0; c < 3; ++c)
                        rgb[c] = image.GetChannel(new Point2i(u, v), c, WrapMode.OctahedralSphere);
                    sumL += new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb)).Sample(lambda);
                }
            }
            // Integrating over the sphere, so 4pi for that.  Then one more for Pi
            // r^2 for the area of the disk receiving illumination...
            return 4 * Pi * Pi * pbrt.Sqr(sceneRadius) * scale * sumL / (width * height);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample infinite light image for ray direction
            Float mapPDF;
            Point2f uv = distribution.Sample(u1, &mapPDF);
            Vector3f wLight = pbrt.EqualAreaSquareToSphere(uv);
            Vector3f w = -renderFromLight.Apply(wLight);

            // Compute infinite light sample ray
            Frame wFrame = Frame.FromZ(-w);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * -w, w, time);

            // Compute PDF for sampled infinite light ray
            Float pdfDir = mapPDF / (4 * Pi);
            Float pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));

            return new LightLeSample(ImageLe(uv, lambda), ray, pdfPos, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ImageInfiniteLight %s scale: %f ]", BaseToString(), scale);
        }

        // ImageInfiniteLight Private Members
        Image image;
        RGBColorSpace imageColorSpace;
        Float scale;
        Point3f sceneCenter;
        Float sceneRadius;
        PiecewiseConstant2D distribution;
        PiecewiseConstant2D compensatedDistribution;
    }

    // PortalImageInfiniteLight Definition
    public sealed unsafe class PortalImageInfiniteLight : LightBase
    {

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c; Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            Vector3f w = -pbrt.Normalize(ray.d);
            Float duv_dw;
            pstd.optional<Point2f> uv = ImageFromRender(w, &duv_dw);
            if (!uv || duv_dw == 0)
            {
                *pdfPos = *pdfDir = 0;
                return;
            }
            Bounds2f b = new Bounds2f(new Point2f(0, 0), new Point2f(1, 1));
            Float pdf = distribution.PDF(uv.value(), b);

            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
            *pdfDir = pdf / duv_dw;
        }

        public override pstd.optional<LightBounds> Bounds() { return default; }

        // PortalImageInfiniteLight Private Methods
        pstd.optional<Point2f> ImageFromRender(Vector3f wRender, Float* duv_dw = null)
        {
            Vector3f w = portalFrame.ToLocal(wRender);
            if (w.z <= 0)
                return default;
            // Compute Jacobian determinant of mapping $\roman{d}(u,v)/\roman{d}\omega$ if needed
            if (duv_dw != null)
                *duv_dw = pbrt.Sqr(Pi) * (1 - pbrt.Sqr(w.x)) * (1 - pbrt.Sqr(w.y)) / w.z;

            // Compute image-space coordinates
            Float alpha = std.atan2(w.x, w.z), beta = std.atan2(w.y, w.z);
            pbrt.DCHECK(!pbrt.IsNaN(alpha + beta));
            return new Point2f(pbrt.Clamp((alpha + Pi / 2) / Pi, 0, 1), pbrt.Clamp((beta + Pi / 2) / Pi, 0, 1));
        }

        Vector3f RenderFromImage(Point2f uv, Float* duv_dw = null)
        {
            Float alpha = -Pi / 2 + uv[0] * Pi, beta = -Pi / 2 + uv[1] * Pi;
            Float x = std.tan(alpha), y = std.tan(beta);
            pbrt.DCHECK(!pbrt.IsInf(x) && !pbrt.IsInf(y));
            Vector3f w = pbrt.Normalize(new Vector3f(x, y, 1));
            // Compute Jacobian determinant of mapping $\roman{d}(u,v)/\roman{d}\omega$ if needed
            if (duv_dw != null)
                *duv_dw = pbrt.Sqr(Pi) * (1 - pbrt.Sqr(w.x)) * (1 - pbrt.Sqr(w.y)) / w.z;
            return portalFrame.FromLocal(w);
        }

        pstd.optional<Bounds2f> ImageBounds(Point3f p)
        {
            pstd.optional<Point2f> p0 = ImageFromRender(pbrt.Normalize(portal[0] - p));
            pstd.optional<Point2f> p1 = ImageFromRender(pbrt.Normalize(portal[2] - p));
            if (!p0 || !p1)
                return default;
            return new Bounds2f(p0.value(), p1.value());
        }

        Float Area()
        {
            return pbrt.Length(portal[1] - portal[0]) * pbrt.Length(portal[3] - portal[0]);
        }
        // PortalImageInfiniteLight Method Definitions
        public PortalImageInfiniteLight(Transform renderFromLight, Image equalAreaImage,
                                        RGBColorSpace imageColorSpace, Float scale, string filename,
                                        List<Point3f> p, Allocator alloc)
            : base(LightType.Infinite, renderFromLight, new MediumInterface())
        {
            this.image = new Image(alloc);
            this.imageColorSpace = imageColorSpace;
            this.scale = scale;
            this.filename = filename;
            this.distribution = new WindowedPiecewiseConstant2D(alloc);

            // Initialize sampling PDFs for image infinite area light
            ImageChannelDesc channelDesc = equalAreaImage.GetChannelDesc(new string[] { "R", "G", "B" });
            if (!channelDesc)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: image used for PortalImageInfiniteLight doesn't have R, " +
                                                       "G, B channels.", filename));
            pbrt.CHECK_EQ(3, (int)channelDesc.size());
            pbrt.CHECK(channelDesc.IsIdentity());

            if (equalAreaImage.Resolution().x != equalAreaImage.Resolution().y)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: image resolution (%d, %d) is non-square. It's unlikely " +
                                                       "this is an equal area environment map.", filename,
                                                       equalAreaImage.Resolution().x, equalAreaImage.Resolution().y));

            if (p.Count != 4)
                pbrt.ErrorExit(pbrt.StringPrintf("Expected 4 vertices for infinite light portal but given %d",
                                                       p.Count));
            for (int i = 0; i < 4; ++i)
                portal[i] = p[i];

            // Compute frame for portal
            Vector3f p01 = pbrt.Normalize(portal[1] - portal[0]);
            Vector3f p12 = pbrt.Normalize(portal[2] - portal[1]);
            Vector3f p32 = pbrt.Normalize(portal[2] - portal[3]);
            Vector3f p03 = pbrt.Normalize(portal[3] - portal[0]);
            // Do opposite edges have the same direction?
            if (std.abs(pbrt.Dot(p01, p32) - 1) > .001 || std.abs(pbrt.Dot(p12, p03) - 1) > .001)
                pbrt.Error("Infinite light portal isn't a planar quadrilateral");
            // Sides perpendicular?
            if (std.abs(pbrt.Dot(p01, p12)) > .001 || std.abs(pbrt.Dot(p12, p32)) > .001 ||
                std.abs(pbrt.Dot(p32, p03)) > .001 || std.abs(pbrt.Dot(p03, p01)) > .001)
                pbrt.Error("Infinite light portal isn't a planar quadrilateral");
            portalFrame = Frame.FromXY(p03, p01);

            // Resample environment map into rectified image
            image = new Image(PixelFormat.Float, equalAreaImage.Resolution(), new string[] { "R", "G", "B" },
                              equalAreaImage.Encoding(), alloc);
            Transform rfl = renderFromLight;
            Image imageLocal = image;
            pbrt.ParallelFor(0, image.Resolution().y, (int y) =>
            {
                for (int x = 0; x < imageLocal.Resolution().x; ++x)
                {
                    // Resample _equalAreaImage_ to compute rectified image pixel $(x,y)$
                    // Find $(u,v)$ coordinates in equal-area image for pixel
                    Point2f uv = new Point2f((x + 0.5f) / imageLocal.Resolution().x,
                                             (y + 0.5f) / imageLocal.Resolution().y);
                    Vector3f w = RenderFromImage(uv);
                    w = pbrt.Normalize(rfl.ApplyInverse(w));
                    Point2f uvEqui = pbrt.EqualAreaSphereToSquare(w);

                    for (int c = 0; c < 3; ++c)
                    {
                        Float v = equalAreaImage.BilerpChannel(uvEqui, c, WrapMode.OctahedralSphere);
                        imageLocal.SetChannel(new Point2i(x, y), c, v);
                    }
                }
            });

            // Initialize sampling distribution for portal image infinite light
            Func<Point2f, Float> duv_dw = (Point2f pp) =>
            {
                Float duv_dwv;
                RenderFromImage(pp, &duv_dwv);
                return duv_dwv;
            };
            Array2D<Float> d = image.GetSamplingDistribution(duv_dw, new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
            distribution = new WindowedPiecewiseConstant2D(d, alloc);
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            // We're really computing fluence, then converting to power, for what
            // that's worth..
            SampledSpectrum sumL = new SampledSpectrum(0.0f);

            for (int y = 0; y < image.Resolution().y; ++y)
            {
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    RGB rgb = default;
                    for (int c = 0; c < 3; ++c)
                        rgb[c] = image.GetChannel(new Point2i(x, y), c);

                    Point2f st = new Point2f((x + 0.5f) / image.Resolution().x, (y + 0.5f) / image.Resolution().y);
                    Float duv_dw;
                    RenderFromImage(st, &duv_dw);

                    sumL += new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb)).Sample(lambda) / duv_dw;
                }
            }

            // Integrating over the portal, so we have to multiply by the area
            return scale * Area() * sumL / (image.Resolution().x * image.Resolution().y);
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            pstd.optional<Point2f> uv = ImageFromRender(pbrt.Normalize(ray.d));
            pstd.optional<Bounds2f> b = ImageBounds(ray.o);
            if (!uv || !b || !pbrt.Inside(uv.value(), b.value()))
                return new SampledSpectrum(0.0f);

            return ImageLookup(uv.value(), lambda);
        }

        SampledSpectrum ImageLookup(Point2f uv, SampledWavelengths lambda)
        {
            RGB rgb = default;
            for (int c = 0; c < 3; ++c)
                rgb[c] = image.LookupNearestChannel(uv, c);
            RGBIlluminantSpectrum spec = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
            return scale * spec.Sample(lambda);
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            // Sample $(u,v)$ in potentially visible region of light image
            pstd.optional<Bounds2f> b = ImageBounds(ctx.p());
            if (!b)
                return default;
            Float mapPDF;
            pstd.optional<Point2f> uv = distribution.Sample(u, b.value(), &mapPDF);
            if (!uv)
                return default;

            // Convert portal image sample point to direction and compute PDF
            Float duv_dw;
            Vector3f wi = RenderFromImage(uv.value(), &duv_dw);
            if (duv_dw == 0)
                return default;
            Float pdf = mapPDF / duv_dw;
            pbrt.CHECK(!pbrt.IsInf(pdf));

            // Compute radiance for portal light sample and return _LightLiSample_
            SampledSpectrum L = ImageLookup(uv.value(), lambda);
            Point3f pl = ctx.p() + 2 * sceneRadius * wi;
            return new LightLiSample(L, wi, pdf, new Interaction(pl, mediumInterface));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            // Find image $(u,v)$ coordinates corresponding to direction _w_
            Float duv_dw;
            pstd.optional<Point2f> uv = ImageFromRender(w, &duv_dw);
            if (!uv || duv_dw == 0)
                return 0;

            // Return PDF for sampling $(u,v)$ from reference point
            pstd.optional<Bounds2f> b = ImageBounds(ctx.p());
            if (!b)
                return 0;
            Float pdf = distribution.PDF(uv.value(), b.value());
            return pdf / duv_dw;
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Sample $(u,v)$ in potentially visible region of light image
            Float mapPDF;
            Bounds2f b = new Bounds2f(new Point2f(0, 0), new Point2f(1, 1));
            pstd.optional<Point2f> uv = distribution.Sample(u1, b, &mapPDF);
            if (!uv)
                return default;

            // Convert infinite light sample point to direction
            Float duv_dw;
            Vector3f w = -RenderFromImage(uv.value(), &duv_dw);
            if (duv_dw == 0)
                return default;

            // Compute PDF for sampled infinite light direction
            Float pdfDir = mapPDF / duv_dw;

            // Compute infinite light sample ray
            Frame wFrame = Frame.FromZ(-w);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * -w, w, time);
            Float pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));

            SampledSpectrum L = ImageLookup(uv.value(), lambda);
            return new LightLeSample(L, ray, pdfPos, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PortalImageInfiniteLight %s filename:%s scale: %f portal: [ %s %s %s %s ] " +
                                        " portalFrame: %s ]",
                                        BaseToString(), filename, scale, portal[0], portal[1], portal[2], portal[3],
                                        portalFrame);
        }

        // PortalImageInfiniteLight Private Members
        Point3f[] portal = new Point3f[4];
        Frame portalFrame;
        Image image;
        WindowedPiecewiseConstant2D distribution;
        RGBColorSpace imageColorSpace;
        Float scale;
        Float sceneRadius;
        string filename;
        Point3f sceneCenter;
    }

    // SpotLight Definition
    public sealed unsafe class SpotLight : LightBase
    {

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            Float[] p = { 1 - cosFalloffStart, (cosFalloffStart - cosFalloffEnd) / 2 };
            *pdfPos = 0;
            // Find spotlight directional PDF based on $\cos\theta$
            Float cosTheta = pbrt.CosTheta(renderFromLight.ApplyInverse(ray.d));
            if (cosTheta >= cosFalloffStart)
                *pdfDir = pbrt.UniformConePDF(cosFalloffStart) * p[0] / (p[0] + p[1]);
            else
                *pdfDir = pbrt.SmoothStepPDF(cosTheta, cosFalloffEnd, cosFalloffStart) * p[1] /
                          ((p[0] + p[1]) * (2 * Pi));
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f wi = pbrt.Normalize(p - ctx.p());
            // Compute incident radiance _Li_ for _SpotLight_
            Vector3f wLight = pbrt.Normalize(renderFromLight.ApplyInverse(-wi));
            SampledSpectrum Li = I(wLight, lambda) / pbrt.DistanceSquared(p, ctx.p());

            if (!Li)
                return default;
            return new LightLiSample(Li, wi, 1, new Interaction(p, mediumInterface));
        }

        public SampledSpectrum I(Vector3f w, SampledWavelengths lambda)
        {
            return pbrt.SmoothStep(pbrt.CosTheta(w), cosFalloffEnd, cosFalloffStart) * scale * Iemit.Sample(lambda);
        }
        // SpotLight Method Definitions
        public SpotLight(Transform renderFromLight, MediumInterface mediumInterface, Spectrum Iemit, Float scale,
                         Float totalWidth, Float falloffStart)
            : base(LightType.DeltaPosition, renderFromLight, mediumInterface)
        {
            this.Iemit = LookupSpectrum(Iemit);
            this.scale = scale;
            this.cosFalloffEnd = std.cos(pbrt.Radians(totalWidth));
            this.cosFalloffStart = std.cos(pbrt.Radians(falloffStart));
            pbrt.CHECK_LE(falloffStart, totalWidth);
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) { return 0.0f; }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            return scale * Iemit.Sample(lambda) * 2 * Pi * ((1 - cosFalloffStart) + (cosFalloffStart - cosFalloffEnd) / 2);
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            Point3f p = renderFromLight.Apply(new Point3f(0, 0, 0));
            Vector3f w = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            Float phi = scale * Iemit.MaxValue() * 4 * Pi;
            Float cosTheta_e = std.cos(std.acos(cosFalloffEnd) - std.acos(cosFalloffStart));
            // Allow a little slop here to deal with fp round-off error in the computation of
            // cosTheta_p in the importance function.
            if (cosTheta_e == 1 && cosFalloffEnd != cosFalloffStart)
                cosTheta_e = 0.999f;
            return new LightBounds(new Bounds3f(p, p), w, phi, cosFalloffStart, cosTheta_e, false);
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            // Choose whether to sample spotlight center cone or falloff region
            Float[] p = { 1 - cosFalloffStart, (cosFalloffStart - cosFalloffEnd) / 2 };
            Float sectionPDF;
            int section = pbrt.SampleDiscrete(new pstd.span<Float>(p), u2[0], &sectionPDF);

            // Sample chosen spotlight region to generate ray direction
            Vector3f wLight;
            Float pdfDir;
            if (section == 0)
            {
                // Sample spotlight center cone
                wLight = pbrt.SampleUniformCone(u1, cosFalloffStart);
                pdfDir = pbrt.UniformConePDF(cosFalloffStart) * sectionPDF;
            }
            else
            {
                // Sample spotlight falloff region
                Float cosTheta = pbrt.SampleSmoothStep(u1[0], cosFalloffEnd, cosFalloffStart);
                pbrt.DCHECK(cosTheta >= cosFalloffEnd && cosTheta <= cosFalloffStart);
                Float sinTheta = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta));
                Float phi = u1[1] * 2 * Pi;
                wLight = pbrt.SphericalDirection(sinTheta, cosTheta, phi);
                pdfDir = pbrt.SmoothStepPDF(cosTheta, cosFalloffEnd, cosFalloffStart) * sectionPDF / (2 * Pi);
            }

            // Return sampled spotlight ray
            Ray ray = renderFromLight.Apply(new Ray(new Point3f(0, 0, 0), wLight, time, mediumInterface.outside));
            return new LightLeSample(I(wLight, lambda), ray, 1, pdfDir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpotLight %s Iemit: %s cosFalloffStart: %f cosFalloffEnd: %f ]",
                                        BaseToString(), Iemit, cosFalloffStart, cosFalloffEnd);
        }

        public static SpotLight Create(Transform renderFromLight, Medium medium, ParameterDictionary parameters,
                                       RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Spectrum I = parameters.GetOneSpectrum("I", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
            Float sc = parameters.GetOneFloat("scale", 1);

            Float coneangle = parameters.GetOneFloat("coneangle", 30.0f);
            Float conedelta = parameters.GetOneFloat("conedeltaangle", 5.0f);
            // Compute spotlight world to light transformation
            Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
            Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));

            Transform dirToZ = new Transform(Frame.FromZ(pbrt.Normalize(to - from)));
            Transform t = pbrt.Translate(new Vector3f(from.x, from.y, from.z)) * pbrt.Inverse(dirToZ);
            Transform finalRenderFromLight = renderFromLight * t;

            sc /= pbrt.SpectrumToPhotometric(I);

            Float phi_v = parameters.GetOneFloat("power", -1);
            if (phi_v > 0)
            {
                Float cosFalloffEnd = std.cos(pbrt.Radians(coneangle));
                Float cosFalloffStart = std.cos(pbrt.Radians(coneangle - conedelta));
                Float k_e = 2 * Pi * ((1 - cosFalloffStart) + (cosFalloffStart - cosFalloffEnd) / 2);
                sc *= phi_v / k_e;
            }

            return new SpotLight(finalRenderFromLight, new MediumInterface(medium), I, sc, coneangle,
                                 coneangle - conedelta);
        }

        // SpotLight Private Members
        DenselySampledSpectrum Iemit;
        Float scale, cosFalloffStart, cosFalloffEnd;
    }

    // Light factory functions (lights.cpp)
    public abstract unsafe partial class Light
    {
        public static Light Create(string name, ParameterDictionary parameters, Transform renderFromLight,
                                   CameraTransform cameraTransform, Medium outsideMedium, FileLoc loc, Allocator alloc)
        {
            Light light = null;
            if (name == "point")
                light = PointLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace(), loc, alloc);
            else if (name == "spot")
                light = SpotLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace(), loc, alloc);
            else if (name == "goniometric")
                light = GoniometricLight.Create(renderFromLight, outsideMedium, parameters, parameters.ColorSpace(), loc,
                                                alloc);
            else if (name == "projection")
                light = ProjectionLight.Create(renderFromLight, outsideMedium, parameters, loc, alloc);
            else if (name == "distant")
                light = DistantLight.Create(renderFromLight, parameters, parameters.ColorSpace(), loc, alloc);
            else if (name == "infinite")
            {
                RGBColorSpace colorSpace = parameters.ColorSpace();
                List<Spectrum> L = parameters.GetSpectrumArray("L", SpectrumType.Illuminant, alloc);
                Float scale = parameters.GetOneFloat("scale", 1);
                List<Point3f> portal = parameters.GetPoint3fArray("portal");
                string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
                Float E_v = parameters.GetOneFloat("illuminance", -1);

                if (L.Count == 0 && filename.Length == 0 && portal.Count == 0)
                {
                    // Scale the light spectrum to be equivalent to 1 nit
                    scale /= pbrt.SpectrumToPhotometric(colorSpace.illuminant);
                    if (E_v > 0)
                    {
                        // If the scene specifies desired illuminance, first calculate
                        // the illuminance from a uniform hemispherical emission
                        // of L_v then use this to scale the emission spectrum.
                        Float k_e = Pi;
                        scale *= E_v / k_e;
                    }

                    // Default: color space's std illuminant
                    light = new UniformInfiniteLight(renderFromLight, colorSpace.illuminant, scale);
                }
                else if (L.Count != 0 && portal.Count == 0)
                {
                    if (filename.Length != 0)
                        pbrt.ErrorExit(loc, "Can't specify both emission \"L\" and " +
                                               "\"filename\" with ImageInfiniteLight");
                    // Scale the light spectrum to be equivalent to 1 nit
                    scale /= pbrt.SpectrumToPhotometric(L[0]);

                    if (E_v > 0)
                    {
                        Float k_e = Pi;
                        scale *= E_v / k_e;
                    }

                    light = new UniformInfiniteLight(renderFromLight, L[0], scale);
                }
                else
                {
                    // Either an image was provided or it's "L" with a portal.
                    ImageAndMetadata imageAndMetadata = default;
                    if (filename.Length == 0)
                    {
                        // Create a uniform image from the L spectrum
                        if (!(L[0] is RGBIlluminantSpectrum))
                            pbrt.Warning(loc, "Converting non-RGB \"L\" parameter to RGB so that a " +
                                                 "portal light can be used.");

                        XYZ xyz = pbrt.SpectrumToXYZ(L[0]);
                        RGB rgb = RGBColorSpace.sRGB.ToRGB(xyz);

                        int res = 1;  // happily, this all just works.
                        imageAndMetadata.image = new Image(PixelFormat.Float, new Point2i(res, res),
                                                           new string[] { "R", "G", "B" });
                        imageAndMetadata.metadata = new ImageMetadata();
                        imageAndMetadata.metadata.colorSpace = RGBColorSpace.sRGB;
                        for (int y = 0; y < res; ++y)
                            for (int x = 0; x < res; ++x)
                                for (int c = 0; c < 3; ++c)
                                    imageAndMetadata.image.SetChannel(new Point2i(x, y), c, rgb[c]);
                    }
                    else
                    {
                        imageAndMetadata = Image.Read(filename, alloc);

                        if (imageAndMetadata.image.HasAnyInfinitePixels())
                            pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                                       "%s: image has infinite pixel values and so is not " +
                                                       "suitable as a light.", filename));
                        if (imageAndMetadata.image.HasAnyNaNPixels())
                            pbrt.ErrorExit(loc, pbrt.StringPrintf(
                                                       "%s: image has not-a-number pixel values and so is not " +
                                                       "suitable as a light.", filename));
                    }

                    RGBColorSpace imageColorSpace = imageAndMetadata.metadata.GetColorSpace();
                    ImageChannelDesc channelDesc = imageAndMetadata.image.GetChannelDesc(new string[] { "R", "G", "B" });
                    if (!channelDesc)
                        pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: image provided to \"infinite\" light must " +
                                                                    "have R, G, and B channels.", filename));

                    // Scale the light spectrum to be equivalent to 1 nit
                    scale /= pbrt.SpectrumToPhotometric(imageColorSpace.illuminant);

                    if (E_v > 0)
                    {
                        // upper hemisphere illuminance calculation for converting map to physical units
                        float illuminance = 0;
                        Image image = imageAndMetadata.image;
                        int res = image.Resolution().x;
                        RGB lum = imageAndMetadata.metadata.GetColorSpace().LuminanceVector();
                        for (int y = 0; y < image.Resolution().y; ++y)
                        {
                            float v = ((float)y + 0.5f) / (float)image.Resolution().y;
                            for (int x = 0; x < image.Resolution().x; ++x)
                            {
                                Float u = (x + 0.5f) / image.Resolution().x;
                                Vector3f w = pbrt.EqualAreaSquareToSphere(new Point2f(u, v));
                                // We could be more clever and compute the integral over the upper hemisphere...
                                if (w.z <= 0)
                                    continue;

                                ImageChannelValues values = image.GetChannels(new Point2i(x, y));
                                for (int c = 0; c < 3; ++c)
                                    illuminance += values[c] * lum[c] * pbrt.CosTheta(w);
                            }
                        }
                        // Integrating over the sphere, so 4pi for that.  Then one more for Pi
                        // r^2 for the area of the disk receiving illumination...
                        illuminance *= 2 * Pi / (image.Resolution().x * image.Resolution().y);

                        // scale
                        Float k_e = illuminance;
                        scale *= E_v / k_e;
                    }

                    Image imgSel = imageAndMetadata.image.SelectChannels(channelDesc, alloc);

                    if (portal.Count != 0)
                    {
                        // Transform portal into render space...
                        for (int i = 0; i < portal.Count; ++i)
                            portal[i] = cameraTransform.RenderFromWorld(portal[i]);

                        light = new PortalImageInfiniteLight(renderFromLight, imgSel, imageColorSpace, scale, filename,
                                                             portal, alloc);
                    }
                    else
                        light = new ImageInfiniteLight(renderFromLight, imgSel, imageColorSpace, scale, filename, alloc);
                }
            }
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: light type unknown.", name));

            if (light == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create light.", name));

            parameters.ReportUnused();
            return light;
        }

        public static Light CreateArea(string name, ParameterDictionary parameters, Transform renderFromLight,
                                       MediumInterface mediumInterface, Shape shape, FloatTexture alpha, FileLoc loc,
                                       Allocator alloc)
        {
            Light area = null;
            if (name == "diffuse")
                area = DiffuseAreaLight.Create(renderFromLight, mediumInterface.outside, parameters,
                                               parameters.ColorSpace(), loc, alloc, shape, alpha);
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: area light type unknown.", name));

            if (area == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create area light.", name));

            parameters.ReportUnused();
            return area;
        }
    }

}  // namespace pbrt
