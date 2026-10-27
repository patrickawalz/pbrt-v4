// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// textures.h, textures.cpp
//
// NOT PORTED: The GPU texture classes
// (GPUFloatImageTexture, GPUSpectrumImageTexture, GPUFloatPtexTexture, GPUSpectrumPtexTexture) are
// not ported (GPU rendering is out of scope).

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // static std::mutex ptexMutex; static Ptex::PtexCache *cache;
        internal static readonly object ptexMutex = new object();
        internal static Ptex.PtexCache ptexCache;

        // STAT_COUNTER("Texture/Ptex lookups", nLookups);
        [ThreadStatic] internal static long nLookups;
        private static StatRegisterer STATS_REGnLookups = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Texture/Ptex lookups", nLookups);
            nLookups = 0;
        });
        // STAT_COUNTER("Texture/Ptex files accessed", nFilesAccessed);
        [ThreadStatic] internal static long nFilesAccessed;
        private static StatRegisterer STATS_REGnFilesAccessed = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Texture/Ptex files accessed", nFilesAccessed);
            nFilesAccessed = 0;
        });
        // STAT_COUNTER("Texture/Ptex block reads", nBlockReads);
        [ThreadStatic] internal static long nBlockReads;
        private static StatRegisterer STATS_REGnBlockReads = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Texture/Ptex block reads", nBlockReads);
            nBlockReads = 0;
        });
        // STAT_MEMORY_COUNTER("Memory/Ptex peak memory used", peakMemoryUsed);
        [ThreadStatic] internal static long peakMemoryUsed;
        private static StatRegisterer STATS_REGpeakMemoryUsed = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Ptex peak memory used", peakMemoryUsed);
            peakMemoryUsed = 0;
        });
        // STAT_MEMORY_COUNTER("Memory/GPU Ptex memory used", gpuPtexMemoryUsed);
        // (GPU only; nothing in this port adds to it)
        [ThreadStatic] internal static long gpuPtexMemoryUsed;
        private static StatRegisterer STATS_REGgpuPtexMemoryUsed = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/GPU Ptex memory used", gpuPtexMemoryUsed);
            gpuPtexMemoryUsed = 0;
        });
        // STAT_RATIO("Texture/Ptex file cache hits", ptexCacheHits, ptexCacheLookups);
        [ThreadStatic] internal static long ptexCacheHits, ptexCacheLookups;
        private static StatRegisterer STATS_REGptexCacheHits = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Texture/Ptex file cache hits", ptexCacheHits, ptexCacheLookups);
            ptexCacheHits = 0;
            ptexCacheLookups = 0;
        });

        // struct : public PtexErrorHandler { void reportError(const char *error) override { Error("%s", error); } } errorHandler;
        private sealed class PtexErrorHandlerImpl : Ptex.PtexErrorHandler
        {
            public override void reportError(string error) { pbrt.Error(error); }
        }
        internal static readonly Ptex.PtexErrorHandler ptexErrorHandler = new PtexErrorHandlerImpl();

        // STAT_COUNTER("Scene/Textures", nTextures);
        [ThreadStatic] internal static long nTextures;
        private static StatRegisterer STATS_REGnTextures = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportCounter("Scene/Textures", nTextures);
            nTextures = 0;
        });

        internal static MIPMap GetImageTextureMIPMap(ImageTextureBase b)
        {
            return b.GetMIPMap();
        }

        // CheckerboardTexture Function Definitions
        public static Float Checkerboard(TextureEvalContext ctx, TextureMapping2D map2D, TextureMapping3D map3D)
        {
            // Define 1D checkerboard filtered integral functions
            Float d(Float x)
            {
                Float y = x / 2 - std.floor(x / 2) - 0.5f;
                return x / 2 + y * (1 - 2 * std.abs(y));
            }

            Float bf(Float x, Float r)
            {
                if (std.floor(x - r) == std.floor(x + r))
                    return 1 - 2 * ((int)std.floor(x) & 1);
                return (d(x + r) - 2 * d(x) + d(x - r)) / Sqr(r);
            }

            if (map2D != null)
            {
                // Return weights for 2D checkerboard texture
                CHECK(map3D == null);
                TexCoord2D c = map2D.Map(ctx);
                Float ds = std.max(std.abs(c.dsdx), std.abs(c.dsdy));
                Float dt = std.max(std.abs(c.dtdx), std.abs(c.dtdy));
                // Integrate product of 2D checkerboard function and triangle filter
                ds *= 1.5f;
                dt *= 1.5f;
                return 0.5f - bf(c.st[0], ds) * bf(c.st[1], dt) / 2;
            }
            else
            {
                // Return weights for 3D checkerboard texture
                CHECK(map3D != null);
                TexCoord3D c = map3D.Map(ctx);
                Float dx = 1.5f * std.max(std.abs(c.dpdx.x), std.abs(c.dpdy.x));
                Float dy = 1.5f * std.max(std.abs(c.dpdx.y), std.abs(c.dpdy.y));
                Float dz = 1.5f * std.max(std.abs(c.dpdx.z), std.abs(c.dpdy.z));
                return 0.5f - 0.5f * bf(c.p.x, dx) * bf(c.p.y, dy) * bf(c.p.z, dz);
            }
        }

        // InsidePolkaDot Function Definition
        public static bool InsidePolkaDot(Point2f st)
        {
            // Compute cell indices (_sCell_,_tCell_ for dots
            int sCell = (int)std.floor(st[0] + .5f), tCell = (int)std.floor(st[1] + .5f);

            if (Noise(sCell + .5f, tCell + .5f) > 0)
            {
                // Determine dot position and test if _st_ is inside it
                Float radius = .35f;
                Float maxShift = 0.5f - radius;
                Float sCenter = sCell + maxShift * Noise(sCell + 1.5f, tCell + 2.8f);
                Float tCenter = tCell + maxShift * Noise(sCell + 4.5f, tCell + 9.8f);
                Vector2f dst = st - new Point2f(sCenter, tCenter);
                if (LengthSquared(dst) < Sqr(radius))
                    return true;
            }
            return false;
        }
    }


    // TextureEvalContext Definition
    public struct TextureEvalContext
    {
        // TextureEvalContext Public Methods
        public TextureEvalContext(Interaction intr)
        {
            p = intr.p();
            uv = intr.uv;
            dpdx = default; dpdy = default; n = default;
            dudx = 0; dudy = 0; dvdx = 0; dvdy = 0;
            faceIndex = 0;
        }
        public TextureEvalContext(SurfaceInteraction si)
        {
            p = si.p();
            dpdx = si.dpdx;
            dpdy = si.dpdy;
            n = si.n;
            uv = si.uv;
            dudx = si.dudx;
            dudy = si.dudy;
            dvdx = si.dvdx;
            dvdy = si.dvdy;
            faceIndex = si.faceIndex;
        }
        public TextureEvalContext(Point3f p, Vector3f dpdx, Vector3f dpdy, Normal3f n, Point2f uv, Float dudx, Float dudy,
                                  Float dvdx, Float dvdy, int faceIndex)
        {
            this.p = p;
            this.dpdx = dpdx;
            this.dpdy = dpdy;
            this.n = n;
            this.uv = uv;
            this.dudx = dudx;
            this.dudy = dudy;
            this.dvdx = dvdx;
            this.dvdy = dvdy;
            this.faceIndex = faceIndex;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ TextureEvalContext p: %s dpdx: %s dpdy: %s n: %s uv: %s dudx: %f dudy: %f " +
                                        "dvdx: %f dvdy: %f faceIndex: %d ]", p, dpdx, dpdy, n, uv, dudx, dudy, dvdx, dvdy, faceIndex);
        }

        public Point3f p;
        public Vector3f dpdx, dpdy;
        public Normal3f n;
        public Point2f uv;
        public Float dudx, dudy, dvdx, dvdy;
        public int faceIndex;
    }

    // TexCoord2D Definition
    public struct TexCoord2D
    {
        public Point2f st;
        public Float dsdx, dsdy, dtdx, dtdy;
        public override string ToString()
        {
            return pbrt.StringPrintf("[ TexCoord2D st: %s dsdx: %f dsdy: %f dtdx: %f dtdy: %f ]", st, dsdx, dsdy, dtdx, dtdy);
        }
    }

    // TexCoord3D Definition
    public struct TexCoord3D
    {
        public Point3f p;
        public Vector3f dpdx, dpdy;
        public override string ToString()
        {
            return pbrt.StringPrintf("[ TexCoord3D p: %s dpdx: %s dpdy: %s ]", p, dpdx, dpdy);
        }
    }

    // TextureMapping2D Definition
    // (TaggedPointer over UVMapping, SphericalMapping, CylindricalMapping, PlanarMapping)
    public abstract unsafe partial class TextureMapping2D
    {
        // TextureMapping2D Interface
        public abstract TexCoord2D Map(TextureEvalContext ctx);
        public abstract override string ToString();

        public static TextureMapping2D Create(ParameterDictionary parameters, Transform renderFromTexture, FileLoc loc,
                                              Allocator alloc)
        {
            string type = parameters.GetOneString("mapping", "uv");
            if (type == "uv")
            {
                Float su = parameters.GetOneFloat("uscale", 1f);
                Float sv = parameters.GetOneFloat("vscale", 1f);
                Float du = parameters.GetOneFloat("udelta", 0f);
                Float dv = parameters.GetOneFloat("vdelta", 0f);
                return new UVMapping(su, sv, du, dv);
            }
            else if (type == "spherical")
                return new SphericalMapping(pbrt.Inverse(renderFromTexture));
            else if (type == "cylindrical")
                return new CylindricalMapping(pbrt.Inverse(renderFromTexture));
            else if (type == "planar")
                return new PlanarMapping(pbrt.Inverse(renderFromTexture),
                                         parameters.GetOneVector3f("v1", new Vector3f(1, 0, 0)),
                                         parameters.GetOneVector3f("v2", new Vector3f(0, 1, 0)),
                                         parameters.GetOneFloat("udelta", 0f), parameters.GetOneFloat("vdelta", 0f));
            else
            {
                pbrt.Error(loc, pbrt.StringPrintf("2D texture mapping \"%s\" unknown", type));
                return new UVMapping();
            }
        }
    }

    // UVMapping Definition
    public sealed unsafe class UVMapping : TextureMapping2D
    {
        // UVMapping Public Methods
        public UVMapping(Float su = 1, Float sv = 1, Float du = 0, Float dv = 0)
        {
            this.su = su; this.sv = sv; this.du = du; this.dv = dv;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ UVMapping su: %f sv: %f du: %f dv: %f ]", su, sv, du, dv);
        }

        public override TexCoord2D Map(TextureEvalContext ctx)
        {
            // Compute texture differentials for 2D $(u,v)$ mapping
            Float dsdx = su * ctx.dudx, dsdy = su * ctx.dudy;
            Float dtdx = sv * ctx.dvdx, dtdy = sv * ctx.dvdy;

            Point2f st = new Point2f(su * ctx.uv[0] + du, sv * ctx.uv[1] + dv);
            return new TexCoord2D { st = st, dsdx = dsdx, dsdy = dsdy, dtdx = dtdx, dtdy = dtdy };
        }

        private Float su, sv, du, dv;
    }

    // SphericalMapping Definition
    public sealed unsafe class SphericalMapping : TextureMapping2D
    {
        // SphericalMapping Public Methods
        public SphericalMapping(Transform textureFromRender) { this.textureFromRender = textureFromRender; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SphericalMapping textureFromRender: %s ]", textureFromRender);
        }

        public override TexCoord2D Map(TextureEvalContext ctx)
        {
            Point3f pt = textureFromRender.Apply(ctx.p);
            // Compute $\partial\,s/\partial\,\pt{}$ and $\partial\,t/\partial\,\pt{}$ for
            // spherical mapping
            Float x2y2 = pbrt.Sqr(pt.x) + pbrt.Sqr(pt.y);
            Float sqrtx2y2 = std.sqrt(x2y2);
            Vector3f dsdp = new Vector3f(-pt.y, pt.x, 0) / (2 * pbrt.Pi * x2y2);
            Vector3f dtdp = 1 / (pbrt.Pi * (x2y2 + pbrt.Sqr(pt.z))) *
                            new Vector3f(pt.x * pt.z / sqrtx2y2, pt.y * pt.z / sqrtx2y2, -sqrtx2y2);

            // Compute texture coordinate differentials for spherical mapping
            Vector3f dpdx = textureFromRender.Apply(ctx.dpdx);
            Vector3f dpdy = textureFromRender.Apply(ctx.dpdy);
            Float dsdx = pbrt.Dot(dsdp, dpdx), dsdy = pbrt.Dot(dsdp, dpdy);
            Float dtdx = pbrt.Dot(dtdp, dpdx), dtdy = pbrt.Dot(dtdp, dpdy);

            // Return $(s,t)$ texture coordinates and differentials based on spherical mapping
            Vector3f vec = pbrt.Normalize(pt - new Point3f(0, 0, 0));
            Point2f st = new Point2f(pbrt.SphericalTheta(vec) * pbrt.InvPi, pbrt.SphericalPhi(vec) * pbrt.Inv2Pi);
            return new TexCoord2D { st = st, dsdx = dsdx, dsdy = dsdy, dtdx = dtdx, dtdy = dtdy };
        }

        // SphericalMapping Private Members
        private Transform textureFromRender;
    }

    // CylindricalMapping Definition
    public sealed unsafe class CylindricalMapping : TextureMapping2D
    {
        // CylindricalMapping Public Methods
        public CylindricalMapping(Transform textureFromRender) { this.textureFromRender = textureFromRender; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ CylindricalMapping textureFromRender: %s ]", textureFromRender);
        }

        public override TexCoord2D Map(TextureEvalContext ctx)
        {
            Point3f pt = textureFromRender.Apply(ctx.p);
            // Compute texture coordinate differentials for cylinder $(u,v)$ mapping
            Float x2y2 = pbrt.Sqr(pt.x) + pbrt.Sqr(pt.y);
            Vector3f dsdp = new Vector3f(-pt.y, pt.x, 0) / (2 * pbrt.Pi * x2y2),
                     dtdp = new Vector3f(0, 0, 1);
            Vector3f dpdx = textureFromRender.Apply(ctx.dpdx), dpdy = textureFromRender.Apply(ctx.dpdy);
            Float dsdx = pbrt.Dot(dsdp, dpdx), dsdy = pbrt.Dot(dsdp, dpdy);
            Float dtdx = pbrt.Dot(dtdp, dpdx), dtdy = pbrt.Dot(dtdp, dpdy);

            Point2f st = new Point2f((pbrt.Pi + std.atan2(pt.y, pt.x)) * pbrt.Inv2Pi, pt.z);
            return new TexCoord2D { st = st, dsdx = dsdx, dsdy = dsdy, dtdx = dtdx, dtdy = dtdy };
        }

        // CylindricalMapping Private Members
        private Transform textureFromRender;
    }

    // PlanarMapping Definition
    public sealed unsafe class PlanarMapping : TextureMapping2D
    {
        // PlanarMapping Public Methods
        public PlanarMapping(Transform textureFromRender, Vector3f vs, Vector3f vt, Float ds, Float dt)
        {
            this.textureFromRender = textureFromRender;
            this.vs = vs; this.vt = vt; this.ds = ds; this.dt = dt;
        }

        public override TexCoord2D Map(TextureEvalContext ctx)
        {
            Vector3f vec = (Vector3f)textureFromRender.Apply(ctx.p);
            // Initialize partial derivatives of planar mapping $(s,t)$ coordinates
            Vector3f dpdx = textureFromRender.Apply(ctx.dpdx);
            Vector3f dpdy = textureFromRender.Apply(ctx.dpdy);
            Float dsdx = pbrt.Dot(vs, dpdx), dsdy = pbrt.Dot(vs, dpdy);
            Float dtdx = pbrt.Dot(vt, dpdx), dtdy = pbrt.Dot(vt, dpdy);

            Point2f st = new Point2f(ds + pbrt.Dot(vec, vs), dt + pbrt.Dot(vec, vt));
            return new TexCoord2D { st = st, dsdx = dsdx, dsdy = dsdy, dtdx = dtdx, dtdy = dtdy };
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PlanarMapping vs: %s vt: %s ds: %f dt: %f]", vs, vt, ds, dt);
        }

        // PlanarMapping Private Members
        private Transform textureFromRender;
        private Vector3f vs, vt;
        private Float ds, dt;
    }

    // TextureMapping3D Definition
    // (TaggedPointer over PointTransformMapping)
    public abstract unsafe partial class TextureMapping3D
    {
        // TextureMapping3D Interface
        public abstract TexCoord3D Map(TextureEvalContext ctx);
        public abstract override string ToString();

        public static TextureMapping3D Create(ParameterDictionary parameters, Transform renderFromTexture, FileLoc loc,
                                              Allocator alloc)
        {
            return new PointTransformMapping(pbrt.Inverse(renderFromTexture));
        }
    }

    // PointTransformMapping Definition
    public sealed unsafe class PointTransformMapping : TextureMapping3D
    {
        // PointTransformMapping Public Methods
        public PointTransformMapping(Transform textureFromRender) { this.textureFromRender = textureFromRender; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PointTransformMapping textureFromRender: %s ]", textureFromRender);
        }

        public override TexCoord3D Map(TextureEvalContext ctx)
        {
            return new TexCoord3D { p = textureFromRender.Apply(ctx.p), dpdx = textureFromRender.Apply(ctx.dpdx),
                                    dpdy = textureFromRender.Apply(ctx.dpdy) };
        }

        private Transform textureFromRender;
    }

    // FloatConstantTexture Definition
    public sealed unsafe class FloatConstantTexture : FloatTexture
    {
        public FloatConstantTexture(Float value) { this.value = value; }
        public override Float Evaluate(TextureEvalContext ctx) { return value; }
        // FloatConstantTexture Public Methods
        public static FloatConstantTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                  FileLoc loc, Allocator alloc)
        {
            return new FloatConstantTexture(parameters.GetOneFloat("value", 1f));
        }

        public override string ToString() { return pbrt.StringPrintf("[ FloatConstantTexture value: %f ]", value); }

        private Float value;
    }

    // SpectrumConstantTexture Definition
    public sealed unsafe class SpectrumConstantTexture : SpectrumTexture
    {
        // SpectrumConstantTexture Public Methods
        public SpectrumConstantTexture(Spectrum value) { this.value = value; }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            return value.Sample(lambda);
        }

        public static SpectrumConstantTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                     SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            Spectrum one = new ConstantSpectrum(1f);
            Spectrum c = parameters.GetOneSpectrum("value", one, spectrumType, alloc);
            return new SpectrumConstantTexture(c);
        }
        public override string ToString() { return pbrt.StringPrintf("[ SpectrumConstantTexture value: %s ]", value); }

        private Spectrum value;
    }

    // FloatBilerpTexture Definition
    public sealed unsafe class FloatBilerpTexture : FloatTexture
    {
        public FloatBilerpTexture(TextureMapping2D mapping, Float v00, Float v01, Float v10, Float v11)
        {
            this.mapping = mapping; this.v00 = v00; this.v01 = v01; this.v10 = v10; this.v11 = v11;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord2D c = mapping.Map(ctx);
            return (1 - c.st[0]) * (1 - c.st[1]) * v00 + c.st[0] * (1 - c.st[1]) * v10 +
                   (1 - c.st[0]) * c.st[1] * v01 + c.st[0] * c.st[1] * v11;
        }

        public static FloatBilerpTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

            return new FloatBilerpTexture(map, parameters.GetOneFloat("v00", 0f), parameters.GetOneFloat("v01", 1f),
                                          parameters.GetOneFloat("v10", 0f), parameters.GetOneFloat("v11", 1f));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatBilerpTexture mapping: %s v00: %f v01: %f v10: %f v11: %f ]", mapping,
                                        v00, v01, v10, v11);
        }

        // BilerpTexture Private Data
        private TextureMapping2D mapping;
        private Float v00, v01, v10, v11;
    }

    // SpectrumBilerpTexture Definition
    public sealed unsafe class SpectrumBilerpTexture : SpectrumTexture
    {
        public SpectrumBilerpTexture(TextureMapping2D mapping, Spectrum v00, Spectrum v01, Spectrum v10, Spectrum v11)
        {
            this.mapping = mapping; this.v00 = v00; this.v01 = v01; this.v10 = v10; this.v11 = v11;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            TexCoord2D c = mapping.Map(ctx);
            return pbrt.Bilerp(new Float[] { c.st[0], c.st[1] },
                                  new SampledSpectrum[] { v00.Sample(lambda), v10.Sample(lambda), v01.Sample(lambda),
                                                          v11.Sample(lambda) });
        }

        public static SpectrumBilerpTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                   SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

            Spectrum zero = new ConstantSpectrum(0f);
            Spectrum one = new ConstantSpectrum(1f);

            return new SpectrumBilerpTexture(map, parameters.GetOneSpectrum("v00", zero, spectrumType, alloc),
                                             parameters.GetOneSpectrum("v01", one, spectrumType, alloc),
                                             parameters.GetOneSpectrum("v10", zero, spectrumType, alloc),
                                             parameters.GetOneSpectrum("v11", one, spectrumType, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumBilerpTexture mapping: %s v00: %s v01: %s v10: %s v11: %s ]", mapping,
                                        v00, v01, v10, v11);
        }

        // BilerpTexture Private Data
        private TextureMapping2D mapping;
        private Spectrum v00, v01, v10, v11;
    }

    public sealed unsafe class FloatCheckerboardTexture : FloatTexture
    {
        public FloatCheckerboardTexture(TextureMapping2D map2D, TextureMapping3D map3D, FloatTexture tex1, FloatTexture tex2)
        {
            this.map2D = map2D; this.map3D = map3D; tex = new FloatTexture[] { tex1, tex2 };
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float w = pbrt.Checkerboard(ctx, map2D, map3D);
            Float t0 = 0, t1 = 0;
            if (w != 1)
                t0 = tex[0].Evaluate(ctx);
            if (w != 0)
                t1 = tex[1].Evaluate(ctx);
            return (1 - w) * t0 + w * t1;
        }

        public static FloatCheckerboardTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                      FileLoc loc, Allocator alloc)
        {
            int dim = parameters.GetOneInt("dimension", 2);
            if (dim != 2 && dim != 3)
            {
                pbrt.Error(loc, pbrt.StringPrintf("%d dimensional checkerboard texture not supported", dim));
                return null;
            }
            FloatTexture tex1 = parameters.GetFloatTexture("tex1", 1f, alloc);
            FloatTexture tex2 = parameters.GetFloatTexture("tex2", 0f, alloc);
            if (dim == 2)
            {
                // Initialize 2D texture mapping _map_ from _tp_
                TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

                return new FloatCheckerboardTexture(map, null, tex1, tex2);
            }
            else
            {
                // Initialize 3D texture mapping _map_ from _tp_
                TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
                return new FloatCheckerboardTexture(null, map, tex1, tex2);
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatCheckerboardTexture map2D: %s map3D: %s tex[0]: %s tex[1]: %s ",
                                        map2D != null ? map2D.ToString() : "(nullptr)",
                                        map3D != null ? map3D.ToString() : "(nullptr)", tex[0], tex[1]);
        }

        private TextureMapping2D map2D;
        private TextureMapping3D map3D;
        private FloatTexture[] tex;
    }

    // SpectrumCheckerboardTexture Definition
    public sealed unsafe class SpectrumCheckerboardTexture : SpectrumTexture
    {
        // SpectrumCheckerboardTexture Public Methods
        public SpectrumCheckerboardTexture(TextureMapping2D map2D, TextureMapping3D map3D, SpectrumTexture tex1,
                                           SpectrumTexture tex2)
        {
            this.map2D = map2D; this.map3D = map3D; tex = new SpectrumTexture[] { tex1, tex2 };
        }

        public static SpectrumCheckerboardTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                         SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            int dim = parameters.GetOneInt("dimension", 2);
            if (dim != 2 && dim != 3)
            {
                pbrt.Error(loc, pbrt.StringPrintf("%d dimensional checkerboard texture not supported", dim));
                return null;
            }

            Spectrum zero = new ConstantSpectrum(0f);
            Spectrum one = new ConstantSpectrum(1f);

            SpectrumTexture tex1 = parameters.GetSpectrumTexture("tex1", one, spectrumType, alloc);
            SpectrumTexture tex2 = parameters.GetSpectrumTexture("tex2", zero, spectrumType, alloc);
            if (dim == 2)
            {
                // Initialize 2D texture mapping _map_ from _tp_
                TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

                return new SpectrumCheckerboardTexture(map, null, tex1, tex2);
            }
            else
            {
                // Initialize 3D texture mapping _map_ from _tp_
                TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
                return new SpectrumCheckerboardTexture(null, map, tex1, tex2);
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumCheckerboardTexture map2D: %s map3D: %s tex[0]: %s tex[1]: %s ",
                                        map2D != null ? map2D.ToString() : "(nullptr)",
                                        map3D != null ? map3D.ToString() : "(nullptr)", tex[0], tex[1]);
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float w = pbrt.Checkerboard(ctx, map2D, map3D);
            SampledSpectrum t0 = new SampledSpectrum(), t1 = new SampledSpectrum();
            if (w != 1)
                t0 = tex[0].Evaluate(ctx, lambda);
            if (w != 0)
                t1 = tex[1].Evaluate(ctx, lambda);
            return (1 - w) * t0 + w * t1;
        }

        // SpectrumCheckerboardTexture Private Members
        private TextureMapping2D map2D;
        private TextureMapping3D map3D;
        private SpectrumTexture[] tex;
    }

    public sealed unsafe class FloatDotsTexture : FloatTexture
    {
        public FloatDotsTexture(TextureMapping2D mapping, FloatTexture outsideDot, FloatTexture insideDot)
        {
            this.mapping = mapping; this.outsideDot = outsideDot; this.insideDot = insideDot;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord2D c = mapping.Map(ctx);
            return pbrt.InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx) : outsideDot.Evaluate(ctx);
        }

        public static FloatDotsTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                              FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

            return new FloatDotsTexture(map, parameters.GetFloatTexture("inside", 1f, alloc),
                                        parameters.GetFloatTexture("outside", 0f, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatDotsTexture mapping: %s insideDot: %s outsideDot: %s ]", mapping, insideDot,
                                        outsideDot);
        }

        // DotsTexture Private Data
        private TextureMapping2D mapping;
        private FloatTexture outsideDot, insideDot;
    }

    // SpectrumDotsTexture Definition
    public sealed unsafe class SpectrumDotsTexture : SpectrumTexture
    {
        // SpectrumDotsTexture Public Methods
        public SpectrumDotsTexture(TextureMapping2D mapping, SpectrumTexture outsideDot, SpectrumTexture insideDot)
        {
            this.mapping = mapping; this.outsideDot = outsideDot; this.insideDot = insideDot;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            TexCoord2D c = mapping.Map(ctx);
            return pbrt.InsidePolkaDot(c.st) ? insideDot.Evaluate(ctx, lambda) : outsideDot.Evaluate(ctx, lambda);
        }

        public static SpectrumDotsTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                 SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);
            Spectrum zero = new ConstantSpectrum(0f);
            Spectrum one = new ConstantSpectrum(1f);

            return new SpectrumDotsTexture(map, parameters.GetSpectrumTexture("inside", one, spectrumType, alloc),
                                           parameters.GetSpectrumTexture("outside", zero, spectrumType, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumDotsTexture mapping: %s insideDot: %s outsideDot: %s ]", mapping,
                                        insideDot, outsideDot);
        }

        // SpectrumDotsTexture Private Members
        private TextureMapping2D mapping;
        private SpectrumTexture outsideDot, insideDot;
    }

    // FBmTexture Definition
    public sealed unsafe class FBmTexture : FloatTexture
    {
        // FBmTexture Public Methods
        public FBmTexture(TextureMapping3D mapping, int octaves, Float omega)
        {
            this.mapping = mapping; this.omega = omega; this.octaves = octaves;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord3D c = mapping.Map(ctx);
            return pbrt.FBm(c.p, c.dpdx, c.dpdy, omega, octaves);
        }

        public static FBmTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                        Allocator alloc)
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new FBmTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FBmTexture mapping: %s omega: %f octaves: %d ]", mapping, omega, octaves);
        }

        private TextureMapping3D mapping;
        private Float omega;
        private int octaves;
    }

    // TexInfo Definition
    public sealed class TexInfo : IEquatable<TexInfo>
    {
        // TexInfo Public Methods
        public TexInfo(string f, MIPMapFilterOptions filterOptions, WrapMode wm, ColorEncoding encoding)
        {
            filename = f; this.filterOptions = filterOptions; wrapMode = wm; this.encoding = encoding;
        }

        // (std::map ordering is replaced by hashing; equivalence is the same as !(a<b) && !(b<a).
        // ColorEncoding is a pointer in pbrt, so it is compared by reference.)
        public bool Equals(TexInfo t)
        {
            return t != null && filename == t.filename && filterOptions.Equals(t.filterOptions) &&
                   ReferenceEquals(encoding, t.encoding) && wrapMode == t.wrapMode;
        }
        public override bool Equals(object o) { return Equals(o as TexInfo); }
        public override int GetHashCode()
        {
            return HashCode.Combine(filename, filterOptions, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(encoding), wrapMode);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ TexInfo filename: %s filterOptions: %s wrapMode: %s encoding: %s ]", filename,
                                        filterOptions, wrapMode, encoding);
        }

        public string filename;
        public MIPMapFilterOptions filterOptions;
        public WrapMode wrapMode;
        public ColorEncoding encoding;
    }

    // ImageTextureBase Definition
    public unsafe class ImageTextureBase
    {
        // ImageTextureBase Public Methods
        public ImageTextureBase(TextureMapping2D mapping, string filename, MIPMapFilterOptions filterOptions,
                                WrapMode wrapMode, Float scale, bool invert, ColorEncoding encoding, Allocator alloc)
        {
            this.mapping = mapping; this.filename = filename; this.scale = scale; this.invert = invert;
            // Get _MIPMap_ from texture cache if present
            TexInfo texInfo = new TexInfo(filename, filterOptions, wrapMode, encoding);
            lock (textureCacheMutex)
            {
                if (textureCache.TryGetValue(texInfo, out MIPMap cached))
                {
                    mipmap = cached;
                    return;
                }
            }

            // Create _MIPMap_ for _filename_ and add to texture cache
            mipmap = MIPMap.CreateFromFile(filename, filterOptions, wrapMode, encoding, alloc);
            lock (textureCacheMutex)
            {
                // This is actually ok, but if it hits, it means we've wastefully
                // loaded this texture. (Note that in that case, should just return
                // the one that's already in there and not replace it.)
                pbrt.CHECK(!textureCache.ContainsKey(texInfo));
                textureCache[texInfo] = mipmap;
            }
        }

        public MIPMap GetMIPMap() { return mipmap; }

        public static void ClearCache() { lock (textureCacheMutex) { textureCache.Clear(); } }

        public void MultiplyScale(Float s) { scale *= s; }

        // (the C++ copy constructor, used by FloatScaledTexture::Create)
        public ImageTextureBase CloneBase() { return (ImageTextureBase)MemberwiseClone(); }

        // ImageTextureBase Protected Members
        protected TextureMapping2D mapping;
        protected string filename;
        protected Float scale;
        protected bool invert;
        protected MIPMap mipmap;

        // ImageTextureBase Private Members
        private static readonly object textureCacheMutex = new object();
        private static readonly Dictionary<TexInfo, MIPMap> textureCache = new Dictionary<TexInfo, MIPMap>();
    }

    // FloatImageTexture Definition
    public sealed unsafe class FloatImageTexture : FloatTexture
    {
        // (C++ inherits from ImageTextureBase; FloatTexture is already the base class here, so the
        // ImageTextureBase part is held by composition.)
        private ImageTextureBase b;
        private TextureMapping2D mapping { get { return b_mapping; } }
        private TextureMapping2D b_mapping;
        private Float scale;
        private bool invert;
        private MIPMap mipmap;
        private string filename;

        public FloatImageTexture(TextureMapping2D m, string filename, MIPMapFilterOptions filterOptions, WrapMode wm,
                                 Float scale, bool invert, ColorEncoding encoding, Allocator alloc)
        {
            b = new ImageTextureBase(m, filename, filterOptions, wm, scale, invert, encoding, alloc);
            b_mapping = m; this.filename = filename; this.scale = scale; this.invert = invert;
            mipmap = pbrt.GetImageTextureMIPMap(b);
        }

        public void MultiplyScale(Float s) { scale *= s; }
        public FloatImageTexture Clone() { return (FloatImageTexture)MemberwiseClone(); }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord2D c = mapping.Map(ctx);
            // Texture coordinates are (0,0) in the lower left corner, but
            // image coordinates are (0,0) in the upper left.
            c.st.y = 1 - c.st.y;
            Float v = scale * mipmap.Filter<Float>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
            return invert ? std.max<Float>(0, 1 - v) : v;
        }

        public static FloatImageTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                               FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

            // Initialize _ImageTexture_ parameters
            Float maxAniso = parameters.GetOneFloat("maxanisotropy", 8f);
            string filter = parameters.GetOneString("filter", "bilinear");
            MIPMapFilterOptions filterOptions = new MIPMapFilterOptions();
            filterOptions.maxAnisotropy = maxAniso;
            pstd.optional<FilterFunction> ff = pbrt.ParseFilter(filter);
            if (ff)
                filterOptions.filter = ff.value();
            else
                pbrt.Error(loc, pbrt.StringPrintf("%s: filter function unknown", filter));

            string wrapString = parameters.GetOneString("wrap", "repeat");
            pstd.optional<WrapMode> wrapMode = pbrt.ParseWrapMode(wrapString);
            if (!wrapMode)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: wrap mode unknown", wrapString));
            Float scale = parameters.GetOneFloat("scale", 1f);
            bool invert = parameters.GetOneBool("invert", false);
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));

            string defaultEncoding = pbrt.HasExtension(filename, "png") ? "sRGB" : "linear";
            string encodingString = parameters.GetOneString("encoding", defaultEncoding);
            ColorEncoding encoding = ColorEncoding.Get(encodingString, alloc);

            return new FloatImageTexture(map, filename, filterOptions, wrapMode.value(), scale, invert, encoding, alloc);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatImageTexture filename: %s mapping: %s scale: %f invert: %s mipmap: %s ]",
                                        filename, mapping, scale, invert, mipmap);
        }
    }

    // SpectrumImageTexture Definition
    public sealed unsafe class SpectrumImageTexture : SpectrumTexture
    {
        private ImageTextureBase b;
        private TextureMapping2D mapping;
        private Float scale;
        private bool invert;
        private MIPMap mipmap;
        private string filename;
        private SpectrumType spectrumType;

        // SpectrumImageTexture Public Methods
        public SpectrumImageTexture(TextureMapping2D mapping, string filename, MIPMapFilterOptions filterOptions,
                                    WrapMode wrapMode, Float scale, bool invert, ColorEncoding encoding,
                                    SpectrumType spectrumType, Allocator alloc)
        {
            b = new ImageTextureBase(mapping, filename, filterOptions, wrapMode, scale, invert, encoding, alloc);
            this.mapping = mapping; this.filename = filename; this.scale = scale; this.invert = invert;
            mipmap = pbrt.GetImageTextureMIPMap(b);
            this.spectrumType = spectrumType;
        }

        public void MultiplyScale(Float s) { scale *= s; }
        public SpectrumImageTexture Clone() { return (SpectrumImageTexture)MemberwiseClone(); }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            // Apply texture mapping and flip $t$ coordinate for image texture lookup
            TexCoord2D c = mapping.Map(ctx);
            c.st.y = 1 - c.st.y;

            // Lookup filtered RGB value in _MIPMap_
            RGB rgb = scale * mipmap.Filter<RGB>(c.st, new Vector2f(c.dsdx, c.dtdx), new Vector2f(c.dsdy, c.dtdy));
            rgb = pbrt.ClampZero(invert ? (new RGB(1, 1, 1) - rgb) : rgb);

            // Return _SampledSpectrum_ for RGB image texture value
            RGBColorSpace cs = mipmap.GetRGBColorSpace();
            if (cs != null)
            {
                if (spectrumType == SpectrumType.Unbounded)
                    return new RGBUnboundedSpectrum(cs, rgb).Sample(lambda);
                else if (spectrumType == SpectrumType.Albedo)
                    return new RGBAlbedoSpectrum(cs, pbrt.Clamp(rgb, 0, 1)).Sample(lambda);
                else
                    return new RGBIlluminantSpectrum(cs, rgb).Sample(lambda);
            }
            // otherwise it better be a one-channel texture
            pbrt.DCHECK(rgb[0] == rgb[1] && rgb[1] == rgb[2]);
            return new SampledSpectrum(rgb[0]);
        }

        public static SpectrumImageTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                  SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            // Initialize 2D texture mapping _map_ from _tp_
            TextureMapping2D map = TextureMapping2D.Create(parameters, renderFromTexture, loc, alloc);

            // Initialize _ImageTexture_ parameters
            Float maxAniso = parameters.GetOneFloat("maxanisotropy", 8f);
            string filter = parameters.GetOneString("filter", "bilinear");
            MIPMapFilterOptions filterOptions = new MIPMapFilterOptions();
            filterOptions.maxAnisotropy = maxAniso;
            pstd.optional<FilterFunction> ff = pbrt.ParseFilter(filter);
            if (ff)
                filterOptions.filter = ff.value();
            else
                pbrt.Error(loc, pbrt.StringPrintf("%s: filter function unknown", filter));

            string wrapString = parameters.GetOneString("wrap", "repeat");
            pstd.optional<WrapMode> wrapMode = pbrt.ParseWrapMode(wrapString);
            if (!wrapMode)
                pbrt.ErrorExit(pbrt.StringPrintf("%s: wrap mode unknown", wrapString));
            Float scale = parameters.GetOneFloat("scale", 1f);
            bool invert = parameters.GetOneBool("invert", false);
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));

            string defaultEncoding = pbrt.HasExtension(filename, "png") ? "sRGB" : "linear";
            string encodingString = parameters.GetOneString("encoding", defaultEncoding);
            ColorEncoding encoding = ColorEncoding.Get(encodingString, alloc);

            return new SpectrumImageTexture(map, filename, filterOptions, wrapMode.value(), scale, invert, encoding,
                                            spectrumType, alloc);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumImageTexture filename: %s mapping: %s scale: %f invert: %s mipmap: %s ]",
                                        filename, mapping, scale, invert, mipmap);
        }
    }

    // MarbleTexture Definition
    public sealed unsafe class MarbleTexture : SpectrumTexture
    {
        // MarbleTexture Public Methods
        public MarbleTexture(TextureMapping3D mapping, int octaves, Float omega, Float scale, Float variation)
        {
            this.mapping = mapping; this.octaves = octaves; this.omega = omega; this.scale = scale; this.variation = variation;
        }

        private static readonly RGB[] colors = {
            new RGB(.58f, .58f, .6f), new RGB(.58f, .58f, .6f), new RGB(.58f, .58f, .6f),
            new RGB(.5f, .5f, .5f),   new RGB(.6f, .59f, .58f), new RGB(.58f, .58f, .6f),
            new RGB(.58f, .58f, .6f), new RGB(.2f, .2f, .33f),  new RGB(.58f, .58f, .6f),
        };

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            TexCoord3D c = mapping.Map(ctx);
            c.p *= scale;
            Float marble = c.p.y + variation * pbrt.FBm(c.p, scale * c.dpdx, scale * c.dpdy, omega, octaves);
            Float t = .5f + .5f * std.sin(marble);
            // Evaluate marble spline at $t$ to compute color _rgb_
            int nSeg = colors.Length - 3;
            int first = std.min<int>((int)std.floor(t * nSeg), nSeg - 1);
            t = t * nSeg - first;
            // (EvaluateCubicBezier() is applied per channel; it is a componentwise blossom.)
            Float[] ch = new Float[4];
            RGB rgb = new RGB();
            for (int k = 0; k < 3; ++k)
            {
                for (int i = 0; i < 4; ++i)
                    ch[i] = colors[first + i][k];
                rgb[k] = 1.5f * pbrt.EvaluateCubicBezier(new pstd.span<Float>(ch), t);
            }

            return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MarbleTexture mapping: %s octaves: %d omega: %f scale: %f variation: %f ]",
                                        mapping, octaves, omega, scale, variation);
        }

        public static MarbleTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                           Allocator alloc)
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new MarbleTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f),
                                     parameters.GetOneFloat("scale", 1f), parameters.GetOneFloat("variation", .2f));
        }

        // MarbleTexture Private Members
        private TextureMapping3D mapping;
        private int octaves;
        private Float omega, scale, variation;
    }

    // FloatMixTexture Definition
    public sealed unsafe class FloatMixTexture : FloatTexture
    {
        // FloatMixTexture Public Methods
        public FloatMixTexture(FloatTexture tex1, FloatTexture tex2, FloatTexture amount)
        {
            this.tex1 = tex1; this.tex2 = tex2; this.amount = amount;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float amt = amount.Evaluate(ctx);
            Float t1 = 0, t2 = 0;
            if (amt != 1)
                t1 = tex1.Evaluate(ctx);
            if (amt != 0)
                t2 = tex2.Evaluate(ctx);
            return (1 - amt) * t1 + amt * t2;
        }

        public static FloatMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            return new FloatMixTexture(parameters.GetFloatTexture("tex1", 0f, alloc), parameters.GetFloatTexture("tex2", 1f, alloc),
                                       parameters.GetFloatTexture("amount", 0.5f, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatMixTexture tex1: %s tex2: %s amount: %s ]", tex1, tex2, amount);
        }

        private FloatTexture tex1, tex2;
        private FloatTexture amount;
    }

    // FloatDirectionMixTexture Definition
    public sealed unsafe class FloatDirectionMixTexture : FloatTexture
    {
        // FloatDirectionMixTexture Public Methods
        public FloatDirectionMixTexture(FloatTexture tex1, FloatTexture tex2, Vector3f dir)
        {
            this.tex1 = tex1; this.tex2 = tex2; this.dir = dir;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float amt = pbrt.AbsDot(ctx.n, dir);
            Float t1 = 0, t2 = 0;
            if (amt != 0)
                t1 = tex1.Evaluate(ctx);
            if (amt != 1)
                t2 = tex2.Evaluate(ctx);
            return amt * t1 + (1 - amt) * t2;
        }

        public static FloatDirectionMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                      FileLoc loc, Allocator alloc)
        {
            Vector3f dir = parameters.GetOneVector3f("dir", new Vector3f(0, 1, 0));
            dir = pbrt.Normalize(renderFromTexture.Apply(dir));
            return new FloatDirectionMixTexture(parameters.GetFloatTexture("tex1", 0f, alloc),
                                                parameters.GetFloatTexture("tex2", 1f, alloc), dir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatDirectionMixTexture tex1: %s tex2: %s dir: %s ]", tex1, tex2, dir);
        }

        // FloatDirectionMixTexture Private Members
        private FloatTexture tex1, tex2;
        private Vector3f dir;
    }

    // SpectrumMixTexture Definition
    public sealed unsafe class SpectrumMixTexture : SpectrumTexture
    {
        public SpectrumMixTexture(SpectrumTexture tex1, SpectrumTexture tex2, FloatTexture amount)
        {
            this.tex1 = tex1; this.tex2 = tex2; this.amount = amount;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float amt = amount.Evaluate(ctx);
            SampledSpectrum t1 = new SampledSpectrum(), t2 = new SampledSpectrum();
            if (amt != 1)
                t1 = tex1.Evaluate(ctx, lambda);
            if (amt != 0)
                t2 = tex2.Evaluate(ctx, lambda);
            return (1 - amt) * t1 + amt * t2;
        }

        public static SpectrumMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            Spectrum zero = new ConstantSpectrum(0f);
            Spectrum one = new ConstantSpectrum(1f);
            return new SpectrumMixTexture(parameters.GetSpectrumTexture("tex1", zero, spectrumType, alloc),
                                          parameters.GetSpectrumTexture("tex2", one, spectrumType, alloc),
                                          parameters.GetFloatTexture("amount", 0.5f, alloc));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumMixTexture tex1: %s tex2: %s amount: %s ]", tex1, tex2, amount);
        }

        private SpectrumTexture tex1, tex2;
        private FloatTexture amount;
    }

    // SpectrumDirectionMixTexture Definition
    public sealed unsafe class SpectrumDirectionMixTexture : SpectrumTexture
    {
        // SpectrumDirectionMixTexture Public Methods
        public SpectrumDirectionMixTexture(SpectrumTexture tex1, SpectrumTexture tex2, Vector3f dir)
        {
            this.tex1 = tex1; this.tex2 = tex2; this.dir = dir;
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float amt = pbrt.AbsDot(ctx.n, dir);
            SampledSpectrum t1 = new SampledSpectrum(), t2 = new SampledSpectrum();
            if (amt != 0)
                t1 = tex1.Evaluate(ctx, lambda);
            if (amt != 1)
                t2 = tex2.Evaluate(ctx, lambda);
            return amt * t1 + (1 - amt) * t2;
        }

        public static SpectrumDirectionMixTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                         SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            Spectrum zero = new ConstantSpectrum(0f);
            Spectrum one = new ConstantSpectrum(1f);
            Vector3f dir = parameters.GetOneVector3f("dir", new Vector3f(0, 1, 0));
            dir = pbrt.Normalize(renderFromTexture.Apply(dir));
            return new SpectrumDirectionMixTexture(parameters.GetSpectrumTexture("tex1", zero, spectrumType, alloc),
                                                   parameters.GetSpectrumTexture("tex2", one, spectrumType, alloc), dir);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumDirectionMixTexture tex1: %s tex2: %s dir: %s ]", tex1, tex2, dir);
        }

        // SpectrumDirectionMixTexture Private Members
        private SpectrumTexture tex1, tex2;
        private Vector3f dir;
    }

    // PtexTexture Declarations
    public unsafe class PtexTextureBase
    {
        public PtexTextureBase(string filename, ColorEncoding encoding, Float scale)
        {
            this.filename = filename; this.encoding = encoding; this.scale = scale;
            lock (pbrt.ptexMutex)
            {
                if (pbrt.ptexCache == null)
                {
                    int maxFiles = 100;
                    ulong maxMem = 1ul << 32;  // 4GB
                    bool premultiply = true;

                    pbrt.ptexCache = Ptex.PtexCache.create(maxFiles, maxMem, premultiply, null,
                                                              pbrt.ptexErrorHandler);
                    // TODO? cache->setSearchPath(...);
                }
            }

            // Issue an error if the texture doesn't exist or has an unsupported
            // number of channels.
            valid = false;
            string error = "";
            Ptex.PtexTexture texture = pbrt.ptexCache.get(filename, ref error);
            if (texture == null)
                pbrt.Error(error);
            else
            {
                if (texture.numChannels() != 1 && texture.numChannels() != 3)
                    pbrt.Error(pbrt.StringPrintf("%s: only one and three channel ptex textures are supported", filename));
                else
                    valid = true;
                texture.release();
            }
        }

        public static void ReportStats()
        {
            if (pbrt.ptexCache == null)
                return;

            Ptex.PtexCache.Stats stats;
            pbrt.ptexCache.getStats(out stats);

            pbrt.nFilesAccessed += (long)stats.filesAccessed;
            pbrt.nBlockReads += (long)stats.blockReads;
            pbrt.peakMemoryUsed = std.max(pbrt.peakMemoryUsed, (long)stats.peakMemUsed);
        }

        public int SampleTexture(TextureEvalContext ctx, float* result)
        {
            if (!valid)
            {
                result[0] = 0f;
                return 1;
            }

            ++pbrt.nLookups;
            string error = "";
            Ptex.PtexTexture texture = pbrt.ptexCache.get(filename, ref error);
            pbrt.CHECK(texture != null);
            // TODO: make the filter an option?
            Ptex.PtexFilter.Options opts = new Ptex.PtexFilter.Options(Ptex.PtexFilter.FilterType.f_bspline);
            Ptex.PtexFilter filter = Ptex.PtexFilter.getFilter(texture, opts);
            int nc = texture.numChannels();

            int firstChan = 0;
            filter.eval(result, firstChan, nc, ctx.faceIndex, (float)ctx.uv[0], (float)ctx.uv[1], (float)ctx.dudx,
                        (float)ctx.dvdx, (float)ctx.dudy, (float)ctx.dvdy);
            filter.release();
            texture.release();

            if (encoding != ColorEncoding.Linear)
            {
                // It feels a little dirty to convert to 8-bits to run through the
                // encoding, but it's probably fine.
                byte[] result8 = new byte[3];
                for (int i = 0; i < nc; ++i)
                    result8[i] = (byte)pbrt.Clamp(result[i] * 255f + 0.5f, 0, 255);

                // Handle Float == double.
                Float[] fResult = new Float[3];
                encoding.ToLinear(new pstd.span<byte>(result8, 0, nc), new pstd.span<Float>(fResult, 0, nc));
                for (int c = 0; c < nc; ++c)
                    result[c] = (float)fResult[c];
            }

            for (int c = 0; c < nc; ++c)
                result[c] *= (float)scale;

            return nc;
        }

        protected string BaseToString()
        {
            return pbrt.StringPrintf("valid: %s filename: %s encoding: %s scale: %f", valid, filename, encoding, scale);
        }
        internal string BaseToStringInternal() { return BaseToString(); }

        private bool valid;
        private string filename;
        private ColorEncoding encoding;
        private Float scale;
    }

    public sealed unsafe class FloatPtexTexture : FloatTexture
    {
        private PtexTextureBase b;
        public FloatPtexTexture(string filename, ColorEncoding encoding, Float scale)
        {
            b = new PtexTextureBase(filename, encoding, scale);
        }
        public override Float Evaluate(TextureEvalContext ctx)
        {
            float* result = stackalloc float[3];
            int nc = b.SampleTexture(ctx, result);
            if (nc == 1)
                return result[0];
            pbrt.DCHECK_EQ(3, nc);
            return (result[0] + result[1] + result[2]) / 3;
        }
        public static FloatPtexTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                              Allocator alloc)
        {
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            string encodingString = parameters.GetOneString("encoding", "gamma 2.2");
            ColorEncoding encoding = ColorEncoding.Get(encodingString, alloc);
            Float scale = parameters.GetOneFloat("scale", 1f);
            return new FloatPtexTexture(filename, encoding, scale);
        }
        public override string ToString() { return pbrt.StringPrintf("[ FloatPtexTexture %s ]", BaseToStringPublic()); }
        private string BaseToStringPublic() { return b.BaseToStringInternal(); }
    }

    public sealed unsafe class SpectrumPtexTexture : SpectrumTexture
    {
        private PtexTextureBase b;
        private SpectrumType spectrumType;
        public SpectrumPtexTexture(string filename, ColorEncoding encoding, Float scale, SpectrumType spectrumType)
        {
            b = new PtexTextureBase(filename, encoding, scale);
            this.spectrumType = spectrumType;
        }
        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            float* result = stackalloc float[3];
            int nc = b.SampleTexture(ctx, result);
            if (nc == 1)
                return new SampledSpectrum(result[0]);
            pbrt.DCHECK_EQ(3, nc);
            RGB rgb = new RGB(result[0], result[1], result[2]);
            if (spectrumType == SpectrumType.Unbounded)
                return new RGBUnboundedSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
            else if (spectrumType == SpectrumType.Albedo)
                return new RGBAlbedoSpectrum(RGBColorSpace.sRGB, pbrt.Clamp(rgb, 0, 1)).Sample(lambda);
            else
                return new RGBIlluminantSpectrum(RGBColorSpace.sRGB, rgb).Sample(lambda);
        }
        public static SpectrumPtexTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                 SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            string encodingString = parameters.GetOneString("encoding", "gamma 2.2");
            ColorEncoding encoding = ColorEncoding.Get(encodingString, alloc);
            Float scale = parameters.GetOneFloat("scale", 1f);
            return new SpectrumPtexTexture(filename, encoding, scale, spectrumType);
        }
        public override string ToString() { return pbrt.StringPrintf("[ SpectrumPtexTexture %s ]", b.BaseToStringInternal()); }
    }

    // FloatScaledTexture Definition
    public sealed unsafe class FloatScaledTexture : FloatTexture
    {
        // FloatScaledTexture Public Methods
        public FloatScaledTexture(FloatTexture tex, FloatTexture scale) { this.tex = tex; this.scale = scale; }

        public static FloatTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                          Allocator alloc)
        {
            FloatTexture tex = parameters.GetFloatTexture("tex", 1f, alloc);
            FloatTexture scale = parameters.GetFloatTexture("scale", 1f, alloc);

            for (int i = 0; i < 2; ++i)
            {
                // Check whether _scale_ is a constant that can be folded in
                if (scale is FloatConstantTexture cscale)
                {
                    Float cs = cscale.Evaluate(new TextureEvalContext());
                    if (cs == 1)
                    {
                        // Just return _tex_ if the scale is 1
                        return tex;
                    }
                    else if (tex is FloatImageTexture image)
                    {
                        FloatImageTexture imageCopy = image.Clone();
                        imageCopy.MultiplyScale(cs);
                        return imageCopy;
                    }
                }
                FloatTexture tmp = tex; tex = scale; scale = tmp;
            }
            {
                FloatTexture tmp = tex; tex = scale; scale = tmp;
            }
            return new FloatScaledTexture(tex, scale);
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Float sc = scale.Evaluate(ctx);
            if (sc == 0)
                return 0;
            return tex.Evaluate(ctx) * sc;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatScaledTexture tex: %s scale: %s ]", tex, scale);
        }

        private FloatTexture tex, scale;
    }

    // SpectrumScaledTexture Definition
    public sealed unsafe class SpectrumScaledTexture : SpectrumTexture
    {
        // SpectrumScaledTexture Public Methods
        public SpectrumScaledTexture(SpectrumTexture tex, FloatTexture scale) { this.tex = tex; this.scale = scale; }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            Float sc = scale.Evaluate(ctx);
            if (sc == 0)
                return new SampledSpectrum(0f);
            return tex.Evaluate(ctx, lambda) * sc;
        }

        public static SpectrumTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                             SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            Spectrum one = new ConstantSpectrum(1f);
            SpectrumTexture tex = parameters.GetSpectrumTexture("tex", one, spectrumType, alloc);
            FloatTexture scale = parameters.GetFloatTexture("scale", 1f, alloc);

            if (scale is FloatConstantTexture cscale)
            {
                Float cs = cscale.Evaluate(new TextureEvalContext());
                if (cs == 1)
                {
                    return tex;
                }
                else if (tex is SpectrumImageTexture image)
                {
                    SpectrumImageTexture imageCopy = image.Clone();
                    imageCopy.MultiplyScale(cs);
                    return imageCopy;
                }
            }

            return new SpectrumScaledTexture(tex, scale);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SpectrumScaledTexture tex: %s scale: %s ]", tex, scale);
        }

        private SpectrumTexture tex;
        private FloatTexture scale;
    }

    // WindyTexture Definition
    public sealed unsafe class WindyTexture : FloatTexture
    {
        // WindyTexture Public Methods
        public WindyTexture(TextureMapping3D mapping) { this.mapping = mapping; }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord3D c = mapping.Map(ctx);
            Float windStrength = pbrt.FBm(.1f * c.p, .1f * c.dpdx, .1f * c.dpdy, .5f, 3);
            Float waveHeight = pbrt.FBm(c.p, c.dpdx, c.dpdy, .5f, 6);
            return std.abs(windStrength) * waveHeight;
        }

        public static WindyTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                          Allocator alloc)
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new WindyTexture(map);
        }

        public override string ToString() { return pbrt.StringPrintf("[ WindyTexture mapping: %s ]", mapping); }

        private TextureMapping3D mapping;
    }

    // WrinkledTexture Definition
    public sealed unsafe class WrinkledTexture : FloatTexture
    {
        // WrinkledTexture Public Methods
        public WrinkledTexture(TextureMapping3D mapping, int octaves, Float omega)
        {
            this.mapping = mapping; this.octaves = octaves; this.omega = omega;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            TexCoord3D c = mapping.Map(ctx);
            return pbrt.Turbulence(c.p, c.dpdx, c.dpdy, omega, octaves);
        }

        public static WrinkledTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            // Initialize 3D texture mapping _map_ from _tp_
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new WrinkledTexture(map, parameters.GetOneInt("octaves", 8), parameters.GetOneFloat("roughness", .5f));
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ WrinkledTexture mapping: %s octaves: %d omega: %f ]", mapping, octaves, omega);
        }

        // WrinkledTexture Private Data
        private TextureMapping3D mapping;
        private int octaves;
        private Float omega;
    }

    // FloatTexture / SpectrumTexture factories (textures.cpp)
    public abstract unsafe partial class FloatTexture
    {
        public static FloatTexture Create(string name, Transform renderFromTexture, TextureParameterDictionary parameters,
                                          FileLoc loc, Allocator alloc, bool gpu)
        {
            FloatTexture tex = null;
            if (name == "constant")
                tex = FloatConstantTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "scale")
                tex = FloatScaledTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "mix")
                tex = FloatMixTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "directionmix")
                tex = FloatDirectionMixTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "bilerp")
                tex = FloatBilerpTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "imagemap")
            {
                if (gpu)
                    pbrt.ErrorExit(loc, "GPU image textures are not supported by this port.");
                else
                    tex = FloatImageTexture.Create(renderFromTexture, parameters, loc, alloc);
            }
            else if (name == "checkerboard")
                tex = FloatCheckerboardTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "dots")
                tex = FloatDotsTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "fbm")
                tex = FBmTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "wrinkled")
                tex = WrinkledTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "windy")
                tex = WindyTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "ptex")
            {
                if (gpu)
                    pbrt.ErrorExit(loc, "GPU ptex textures are not supported by this port.");
                else
                    tex = FloatPtexTexture.Create(renderFromTexture, parameters, loc, alloc);
            }
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: float texture type unknown.", name));

            if (tex == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create texture.", name));

            ++pbrt.nTextures;

            parameters.ReportUnused();
            return tex;
        }
    }

    public abstract unsafe partial class SpectrumTexture
    {
        public static SpectrumTexture Create(string name, Transform renderFromTexture, TextureParameterDictionary parameters,
                                             SpectrumType spectrumType, FileLoc loc, Allocator alloc, bool gpu)
        {
            SpectrumTexture tex = null;
            if (name == "constant")
                tex = SpectrumConstantTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "scale")
                tex = SpectrumScaledTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "mix")
                tex = SpectrumMixTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "directionmix")
                tex = SpectrumDirectionMixTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "bilerp")
                tex = SpectrumBilerpTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "imagemap")
            {
                if (gpu)
                    pbrt.ErrorExit(loc, "GPU image textures are not supported by this port.");
                else
                    tex = SpectrumImageTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            }
            else if (name == "checkerboard")
                tex = SpectrumCheckerboardTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "dots")
                tex = SpectrumDotsTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            else if (name == "marble")
                tex = MarbleTexture.Create(renderFromTexture, parameters, loc, alloc);
            else if (name == "ptex")
            {
                if (gpu)
                    pbrt.ErrorExit(loc, "GPU ptex textures are not supported by this port.");
                else
                    tex = SpectrumPtexTexture.Create(renderFromTexture, parameters, spectrumType, loc, alloc);
            }
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: spectrum texture type unknown.", name));

            if (tex == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create texture.", name));

            ++pbrt.nTextures;

            parameters.ReportUnused();

            return tex;
        }
    }

    // ITextureEvaluator: the interface that C++ TextureEvaluator template arguments satisfy
    public interface ITextureEvaluator
    {
        bool CanEvaluate(FloatTexture[] ftex, SpectrumTexture[] stex);
        Float Apply(FloatTexture tex, TextureEvalContext ctx);
        SampledSpectrum Apply(SpectrumTexture tex, TextureEvalContext ctx, SampledWavelengths lambda);
    }

    // UniversalTextureEvaluator Definition
    public struct UniversalTextureEvaluator : ITextureEvaluator
    {
        // UniversalTextureEvaluator Public Methods
        public bool CanEvaluate(FloatTexture[] ftex, SpectrumTexture[] stex) { return true; }

        // UniversalTextureEvaluator Method Definitions
        public Float Apply(FloatTexture tex, TextureEvalContext ctx) { return tex.Evaluate(ctx); }

        public SampledSpectrum Apply(SpectrumTexture tex, TextureEvalContext ctx, SampledWavelengths lambda)
        {
            return tex.Evaluate(ctx, lambda);
        }
    }

    // BasicTextureEvaluator Definition
    public struct BasicTextureEvaluator : ITextureEvaluator
    {
        // BasicTextureEvaluator Public Methods
        public bool CanEvaluate(FloatTexture[] ftex, SpectrumTexture[] stex)
        {
            // Return _false_ if any _FloatTexture_s cannot be evaluated
            foreach (FloatTexture f in ftex)
                if (f != null && !(f is FloatConstantTexture) && !(f is FloatImageTexture))
                    return false;
            // Return _false_ if any _SpectrumTexture_s cannot be evaluated
            foreach (SpectrumTexture s in stex)
                if (s != null && !(s is SpectrumConstantTexture) && !(s is SpectrumImageTexture))
                    return false;
            return true;
        }

        public Float Apply(FloatTexture tex, TextureEvalContext ctx)
        {
            if (tex is FloatConstantTexture c)
                return c.Evaluate(ctx);
            else if (tex is FloatImageTexture i)
                return i.Evaluate(ctx);
            else
            {
                if (tex != null)
                    pbrt.LOG_FATAL("BasicTextureEvaluator::operator() called with " + tex.ToString());
                return 0f;
            }
        }

        public SampledSpectrum Apply(SpectrumTexture tex, TextureEvalContext ctx, SampledWavelengths lambda)
        {
            if (tex is SpectrumConstantTexture c)
                return c.Evaluate(ctx, lambda);
            else if (tex is SpectrumImageTexture i)
                return i.Evaluate(ctx, lambda);
            else
            {
                if (tex != null)
                    pbrt.LOG_FATAL("BasicTextureEvaluator::operator() called with " + tex.ToString());
                return new SampledSpectrum(0f);
            }
        }
    }

}  // namespace pbrt
