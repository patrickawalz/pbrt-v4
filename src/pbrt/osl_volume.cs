// (not in pbrt-v4) Open Shading Language integration: volume shaders and the "osl" medium.
//
//   MakeNamedMedium "fog" "string type" "osl" "string shader" "cloud" "float density" 2 "point3 p0" [-1 -1 -1] "point3 p1" [1 1 1]
//
// A shader group (the usage of the group is "volume") is executed at the points of a ray inside the medium. Its closure result is turned into
// the coefficients that pbrt's volumetric integrators work with:
//
//   anisotropic_vdf (albedo, extinction, g)           sigma_t = weight * extinction, sigma_s = albedo * sigma_t (at most sigma_t), HG(g)
//   medium_vdf (albedo, depth, color, g, ior, prio)   sigma_t = -ln(color) * weight / depth (testrender's definition), sigma_s = albedo * sigma_t
//   henyey_greenstein (g), isotropic ()               sigma_s = weight, HG(g) (g = 0)
//   absorption ()                                     sigma_a = weight
//   emission (), uniform_edf (emittance)              Le = weight; the medium emits sigma_a * Le, as the media of pbrt do
//
// The sum of several closures adds the coefficients up; the phase function of a sum of scattering closures is the mixture of their Henyey-Greenstein
// functions, weighted by the (channel average of the) scattering coefficient of each. transparent() and the surface closures are ignored.
//
// A shader is an opaque function of the position, so the majorant that delta and ratio tracking need is found as follows:
//   * When the optimized group reads no varying global (P, I, N, u, v, time, ...) and no userdata, its closures are constants: the medium is evaluated
//     once and behaves exactly as pbrt's "homogeneous" medium (HomogeneousMajorantIterator, no shader execution while rendering).
//   * Otherwise a grid of majorants over the bounds of the medium is built when the medium is first used: each cell holds the largest extinction found at
//     jittered samples of the shader inside the cell and at its corners, times a safety margin. THIS BOUND IS EMPIRICAL: a feature narrower than the
//     sample spacing can exceed it. Every shader evaluation in a ray is compared with the majorant of its cell and the violations are counted
//     (OSLMedium.Violations, a warning at the first one, the statistic "Media/OSL majorant violations"). A user supplied "float majorant" replaces the
//     grid by one value that is trusted for the whole medium (and is still checked).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OIIO;
using OSL;

namespace pbrt
{
    /// What a volume shader gives at a point as RGB values, before the scale of the medium
    public unsafe struct OSLVolumeValues
    {
        public const int MaxLobes = 8;
        public Color3 sigma_a, sigma_s, emission;
        public bool hasEmission;
        public int nLobes;
        public fixed float lobeG[MaxLobes];
        /// The channel average of the scattering coefficient of the lobe
        public fixed float lobeWeight[MaxLobes];
    }

    /// The walk of a closure tree of a volume shader
    public static unsafe class OSLVolumeClosures
    {
        private static Float Average(Color3 c)
        {
            return (c.x + c.y + c.z) * (1f / 3f);
        }

        private static Color3 Clean(Color3 c)
        {
            return new Color3(c.x > 0 ? c.x : 0, c.y > 0 ? c.y : 0, c.z > 0 ? c.z : 0);
        }

        private static void AddLobe(ref OSLVolumeValues v, Float g, Float weight)
        {
            if (!(weight > 0))
            {
                return;
            }
            g = Clamp(g, -0.999f, 0.999f);
            int closest = -1;
            Float closestDistance = Float.MaxValue;
            for (int i = 0; i < v.nLobes; ++i)
            {
                Float d = std.abs(v.lobeG[i] - g);
                if (d < closestDistance)
                {
                    closestDistance = d;
                    closest = i;
                }
            }
            if (closest >= 0 && (closestDistance < 1e-6f || v.nLobes == OSLVolumeValues.MaxLobes))
            {
                // the same function, or no room left: the weights of lobes that are merged add up (the g of a merged pair is the weighted mean)
                Float total = v.lobeWeight[closest] + weight;
                v.lobeG[closest] = (v.lobeG[closest] * v.lobeWeight[closest] + g * weight) / total;
                v.lobeWeight[closest] = total;
                return;
            }
            v.lobeG[v.nLobes] = g;
            v.lobeWeight[v.nLobes] = weight;
            ++v.nLobes;
        }

        // A scattering coefficient with a Henyey-Greenstein lobe
        private static void AddScattering(ref OSLVolumeValues v, Color3 sigma_s, Float g)
        {
            sigma_s = Clean(sigma_s);
            v.sigma_s = v.sigma_s + sigma_s;
            AddLobe(ref v, g, Average(sigma_s));
        }

        // A coefficient of extinction that scatters with the albedo (at most all of it)
        private static void AddExtinction(ref OSLVolumeValues v, Color3 sigma_t, Color3 albedo, Float g)
        {
            sigma_t = Clean(sigma_t);
            Color3 scattering = new Color3(std.min(Clamp(albedo.x, 0f, 1f) * sigma_t.x, sigma_t.x), std.min(Clamp(albedo.y, 0f, 1f) * sigma_t.y, sigma_t.y),
                                           std.min(Clamp(albedo.z, 0f, 1f) * sigma_t.z, sigma_t.z));
            v.sigma_a = v.sigma_a + (sigma_t - scattering);
            AddScattering(ref v, scattering, g);
        }

        public static void Accumulate(ClosureColor* c, Color3 w, ref OSLVolumeValues v)
        {
            if (c == null)
            {
                return;
            }
            if (c->id == ClosureColor.MUL)
            {
                ClosureMul* m = (ClosureMul*)c;
                Accumulate(m->closure, w * m->weight, ref v);
                return;
            }
            if (c->id == ClosureColor.ADD)
            {
                ClosureAdd* a = (ClosureAdd*)c;
                Accumulate(a->closureA, w, ref v);
                Accumulate(a->closureB, w, ref v);
                return;
            }
            ClosureComponent* comp = (ClosureComponent*)c;
            void* data = ClosureComponent.data(comp);
            Color3 weight = w * new Color3(comp->w);
            switch (comp->id)
            {
                case OSLClosureId.Emission:
                {
                    v.emission = v.emission + Clean(weight);
                    v.hasEmission = true;
                    break;
                }
                case OSLClosureId.MxUniformEdf:
                {
                    v.emission = v.emission + Clean(weight * OSLClosureData.ReadColor3(data, OSLClosures.MxUniformEdfEmittance));
                    v.hasEmission = true;
                    break;
                }
                case OSLClosureId.HenyeyGreenstein:
                {
                    AddScattering(ref v, weight, OSLClosureData.ReadFloat(data, 0));
                    break;
                }
                case OSLClosureId.Isotropic:
                {
                    AddScattering(ref v, weight, 0);
                    break;
                }
                case OSLClosureId.Absorption:
                {
                    v.sigma_a = v.sigma_a + Clean(weight);
                    break;
                }
                case OSLClosureId.MxAnisotropicVdf:
                {
                    Color3 albedo = OSLClosureData.ReadColor3(data, 0);
                    Color3 extinction = OSLClosureData.ReadColor3(data, 12);
                    Float anisotropy = OSLClosureData.ReadFloat(data, 24);
                    AddExtinction(ref v, weight * extinction, albedo, anisotropy);
                    break;
                }
                case OSLClosureId.MxMediumVdf:
                {
                    Color3 albedo = OSLClosureData.ReadColor3(data, 0);
                    Float depth = OSLClosureData.ReadFloat(data, 12);
                    Color3 color = OSLClosureData.ReadColor3(data, 16);
                    Float anisotropy = OSLClosureData.ReadFloat(data, 28);
                    // both black: a vacuum that only carries the index of refraction of a dielectric
                    bool vacuum = albedo.x <= 0 && albedo.y <= 0 && albedo.z <= 0 && color.x <= 0 && color.y <= 0 && color.z <= 0;
                    if (!vacuum)
                    {
                        const Float epsilon = 1e-10f;
                        Float invDepth = 1 / std.max(depth, 1e-8f);
                        Color3 sigma_t = new Color3(-MathF.Log(std.max(color.x, epsilon)), -MathF.Log(std.max(color.y, epsilon)),
                                                    -MathF.Log(std.max(color.z, epsilon))) * weight * invDepth;
                        AddExtinction(ref v, sigma_t, albedo, anisotropy);
                    }
                    break;
                }
                default:
                    break;
            }
        }
    }

    /// How the majorant of an OSL medium is found (the parameters of the scene file)
    public sealed class OSLMajorantOptions
    {
        /// Cells per axis of the majorant grid ("integer majorantres")
        public Point3i resolution = new Point3i(16, 16, 16);
        /// Jittered shader samples per axis in each cell ("integer majorantsamples"); the 8 corners of the cell are always sampled
        public int samplesPerAxis = 3;
        /// The largest extinction found in a cell is multiplied by this ("float majorantmargin")
        public Float margin = 1.25f;
        /// If positive: an extinction that bounds the medium everywhere, used instead of the grid ("float majorant" or "float sigma_t_max")
        public Float userMajorant = 0;
        /// Compare every evaluation with the majorant of its cell ("bool checkmajorant")
        public bool check = true;
        /// Run the optimized group's analysis and use the exact homogeneous behaviour when the closures do not depend on the position ("bool detecthomogeneous")
        public bool detectHomogeneous = true;
    }

    // OSLMedium Definition
    public sealed unsafe class OSLMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // The direction and time of the ray that is being tracked (set by SampleRay(), read by SamplePoint(): the Medium interface has
        // no direction in SamplePoint())
        [ThreadStatic] private static Vector3f currentDirection;
        [ThreadStatic] private static Float currentTime;

        private static long totalViolations = 0;

        /// The number of shader evaluations during rendering that exceeded the majorant, in all OSL media
        public static long TotalViolations
        {
            get { return Interlocked.Read(ref totalViolations); }
        }

        /// The number of shader evaluations inside the bounds, over all OSL media, for the statistic of the violations
        private static long totalChecked = 0;

        private static StatRegisterer STATS_REGviolations = new StatRegisterer((StatsAccumulator accum) =>
        {
            long n = Interlocked.Exchange(ref totalViolations, 0);
            long c = Interlocked.Exchange(ref totalChecked, 0);
            if (n > 0)
            {
                accum.ReportRatio("Media/OSL majorant violations", n, c);
            }
        });

        private OSLShaderGroup group;
        private OSLGroupSpec spec;
        private Transform renderFromMedium;
        private Bounds3f bounds;
        private bool boundsGiven;
        private Float sigmaScale, LeScale;
        private OSLMajorantOptions options;
        private bool emissive;
        private Float rayleigh;

        private readonly object prepareMutex = new object();
        private volatile bool prepared = false;

        // The homogeneous case
        private bool constantValues = false;
        private DenselySampledSpectrum sigma_a_spec, sigma_s_spec, Le_spec;
        private PhaseFunction constantPhase;

        // The heterogeneous case
        private MajorantGrid majorantGrid;
        private long violations = 0;
        private Float worstRatio = 0;
        private bool warned = false;

        private readonly ConcurrentDictionary<int, HGPhaseFunction> hgCache = new ConcurrentDictionary<int, HGPhaseFunction>();
        private const int HgCacheLimit = 4096;

        public OSLMedium(OSLGroupSpec spec, OSLShaderGroup group, Transform renderFromMedium, Bounds3f bounds, bool boundsGiven, Float sigmaScale,
                         Float LeScale, OSLMajorantOptions options, Float rayleigh)
        {
            this.spec = spec;
            this.group = group;
            this.renderFromMedium = renderFromMedium;
            this.bounds = bounds;
            this.boundsGiven = boundsGiven;
            this.sigmaScale = sigmaScale;
            this.LeScale = LeScale;
            this.options = options;
            this.rayleigh = rayleigh;
            emissive = spec.mayEmit && LeScale > 0;
        }

        public OSLShaderGroup Group() { return group; }

        public Bounds3f MediumBounds() { return bounds; }

        /// Whether the scene file gave the bounds (a homogeneous medium without bounds fills the space like pbrt's "homogeneous")
        public bool HasBounds() { return boundsGiven; }

        public override bool IsEmissive() { return emissive; }

        /// Whether the closures of the shader do not depend on the position: the medium is homogeneous (decided when the medium is first used)
        public bool IsHomogeneous()
        {
            Prepare();
            return constantValues;
        }

        /// The number of evaluations of this medium that exceeded the majorant
        public long Violations() { return Interlocked.Read(ref violations); }

        /// The largest ratio of the extinction of an evaluation to the majorant of its cell, over the violations
        public Float WorstViolationRatio() { return worstRatio; }

        /// The majorant grid (null for the homogeneous case); the values are the extinction coefficient bounds (including the scale)
        public MajorantGrid Majorants()
        {
            Prepare();
            return majorantGrid;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Evaluating the shader
        // ---------------------------------------------------------------------------------------------------------------------------------

        /// Runs the group at a point of render space; the values are those of the closures of the shader (unscaled)
        public void Evaluate(Point3f pRender, Vector3f directionRender, Float time, out OSLVolumeValues values)
        {
            values = default(OSLVolumeValues);
            OSLThreadState ts = OSLThreadState.Current();
            ShaderGlobals sg = new ShaderGlobals();
            Point3f pw = OSLScene.WorldFromRender(pRender);
            sg.P = new Vec3(pw.x, pw.y, pw.z);
            Vec3 iw = new Vec3(0, 0, 0);
            if (LengthSquared(directionRender) > 0)
            {
                Vector3f dw = Normalize(OSLScene.WorldFromRender(directionRender));
                iw = new Vec3(dw.x, dw.y, dw.z);
            }
            sg.I = iw;
            // there is no surface: the normals face the viewer
            Vec3 facing = new Vec3(-iw.x, -iw.y, -iw.z);
            sg.N = facing;
            sg.Ng = facing;
            sg.time = time;
            sg.dtime = OSLScene.shutterClose - OSLScene.shutterOpen;
            sg.raytype = OSLRayType.Camera;
            sg.backfacing = 0;
            sg.Ci = null;
            ts.matrices[0] = Matrix44.identity;
            ts.matrices[1] = group.ShaderToCommon();
            sg.object2common = &ts.matrices[0];
            sg.shader2common = &ts.matrices[1];
            OSLShadeState state = ts.shade;
            state.group = group;
            state.si = null;
            state.tex = default(TextureEvalContext);
            state.objectInfo = null;
            state.rayType = OSLRayType.Camera;
            state.traceHit = false;
            sg.renderstate = (void*)GCHandle.ToIntPtr(ts.stateHandle);
            if (OSLShading.Execute(group, ts, ref sg))
            {
                OSLVolumeClosures.Accumulate(sg.Ci, new Color3(1, 1, 1), ref values);
            }
        }

        // The spectrum of a coefficient: an unbounded spectrum of the color (the same for equal channels), or its luminance in the gray mode
        private static SampledSpectrum Coefficient(Color3 c, SampledWavelengths lambda)
        {
            if (OSLSystem.spectralMode == OSLSpectralMode.Gray)
            {
                return new SampledSpectrum(0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z);
            }
            if (c.x == c.y && c.y == c.z)
            {
                return new SampledSpectrum(c.x);
            }
            return new RGBUnboundedSpectrum(OSLSystem.ColorSpace(), new RGB(c.x, c.y, c.z)).Sample(lambda);
        }

        // The largest value of the spectrum of a coefficient over the wavelengths
        private static Float CoefficientBound(Color3 c)
        {
            if (OSLSystem.spectralMode == OSLSpectralMode.Gray)
            {
                return 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;
            }
            if (c.x == c.y && c.y == c.z)
            {
                return c.x;
            }
            return new RGBUnboundedSpectrum(OSLSystem.ColorSpace(), new RGB(c.x, c.y, c.z)).MaxValue();
        }

        private Float ExtinctionBound(ref OSLVolumeValues v)
        {
            return sigmaScale * (CoefficientBound(v.sigma_a) + CoefficientBound(v.sigma_s));
        }

        private HGPhaseFunction HG(Float g)
        {
            int key = BitConverter.SingleToInt32Bits(g);
            HGPhaseFunction hg;
            if (hgCache.TryGetValue(key, out hg))
            {
                return hg;
            }
            hg = new HGPhaseFunction(g, rayleigh);
            if (hgCache.Count < HgCacheLimit)
            {
                hgCache.TryAdd(key, hg);
            }
            return hg;
        }

        // The phase function of the lobes: the one function of a single lobe is shared (no allocation), several lobes are a mixture
        private PhaseFunction Phase(ref OSLVolumeValues v)
        {
            if (v.nLobes <= 1)
            {
                return HG(v.nLobes == 1 ? v.lobeG[0] : 0f);
            }
            Float total = 0;
            for (int i = 0; i < v.nLobes; ++i)
            {
                total += v.lobeWeight[i];
            }
            PhaseFunction[] phases = new PhaseFunction[v.nLobes];
            Float[] weights = new Float[v.nLobes];
            for (int i = 0; i < v.nLobes; ++i)
            {
                phases[i] = HG(v.lobeG[i]);
                weights[i] = v.lobeWeight[i] / total;
            }
            return new MixturePhaseFunction(phases, weights);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Preparing the medium: homogeneous detection and the majorant grid
        // ---------------------------------------------------------------------------------------------------------------------------------

        private const int VaryingGlobals = (int)(SGBits.P | SGBits.I | SGBits.N | SGBits.Ng | SGBits.u | SGBits.v | SGBits.dPdu | SGBits.dPdv |
                                                  SGBits.time | SGBits.dtime | SGBits.dPdtime | SGBits.Ps);

        private bool DependsOnPosition()
        {
            ShadingSystem ss = OSLSystem.ShadingSystem;
            object v;
            if (!ss.getattribute(group.group, "globals_read", TypeDescs.TypeInt, out v))
            {
                return true;
            }
            if (((int)v & VaryingGlobals) != 0)
            {
                return true;
            }
            if (ss.getattribute(group.group, "num_userdata", TypeDescs.TypeInt, out v) && (int)v > 0)
            {
                return true;
            }
            if (ss.getattribute(group.group, "unknown_attributes_needed", TypeDescs.TypeInt, out v) && (int)v > 0)
            {
                return true;
            }
            return false;
        }

        /// Analyzes the shader and builds the majorants. Called by the first use of the medium (the scene must be complete by then: the render space
        /// is needed to run the shader), and by the renderer before rendering so that the work is not done by a render thread.
        public void Prepare()
        {
            if (prepared)
            {
                return;
            }
            lock (prepareMutex)
            {
                if (prepared)
                {
                    return;
                }
                bool varying = !options.detectHomogeneous || DependsOnPosition();
                if (!varying)
                {
                    PrepareHomogeneous();
                }
                else
                {
                    PrepareGrid();
                }
                prepared = true;
            }
        }

        private void PrepareHomogeneous()
        {
            OSLVolumeValues v;
            Point3f center = renderFromMedium.Apply(new Point3f(0.5f, 0.5f, 0.5f));
            Evaluate(center, new Vector3f(0, 0, 0), 0, out v);
            RGBColorSpace cs = OSLSystem.ColorSpace();
            sigma_a_spec = new DenselySampledSpectrum(CoefficientSpectrum(v.sigma_a, cs), new Allocator());
            sigma_s_spec = new DenselySampledSpectrum(CoefficientSpectrum(v.sigma_s, cs), new Allocator());
            sigma_a_spec.Scale(sigmaScale);
            sigma_s_spec.Scale(sigmaScale);
            Le_spec = new DenselySampledSpectrum(EmissionSpectrum(v, out Float norm), new Allocator());
            Le_spec.Scale(LeScale * norm);
            constantPhase = Phase(ref v);
            constantValues = true;
            if (boundsGiven)
            {
                // a homogeneous medium that fills a box: one cell with the exact extinction as the majorant
                majorantGrid = new MajorantGrid(bounds, new Point3i(1, 1, 1), new Allocator());
                majorantGrid.Set(0, 0, 0, 1);
            }
        }

        // The spectrum of a coefficient for a homogeneous medium: the spectrum that pbrt's homogeneous medium makes of an "rgb" value
        private static Spectrum CoefficientSpectrum(Color3 c, RGBColorSpace cs)
        {
            if (OSLSystem.spectralMode == OSLSpectralMode.Gray)
            {
                return new ConstantSpectrum(0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z);
            }
            return new RGBUnboundedSpectrum(cs, new RGB(c.x, c.y, c.z));
        }

        // The spectrum of the emission and the factor that normalizes it photometrically, as the "Le" of pbrt's media
        private static Spectrum EmissionSpectrum(OSLVolumeValues v, out Float norm)
        {
            RGBColorSpace cs = OSLSystem.ColorSpace();
            RGB rgb = new RGB(v.emission.x, v.emission.y, v.emission.z);
            if (!v.hasEmission || (rgb.r == 0 && rgb.g == 0 && rgb.b == 0))
            {
                norm = 1;
                return new ConstantSpectrum(0f);
            }
            if (OSLSystem.spectralMode == OSLSpectralMode.Gray)
            {
                Spectrum flat = new ConstantSpectrum(0.2126f * rgb.r + 0.7152f * rgb.g + 0.0722f * rgb.b);
                norm = 1 / pbrt.SpectrumToPhotometric(new ConstantSpectrum(1f));
                return flat;
            }
            if (OSLSystem.spectralMode == OSLSpectralMode.Unbounded || !OSLSystem.emissionIlluminant)
            {
                norm = 1 / pbrt.SpectrumToPhotometric(new ConstantSpectrum(1f));
                return new RGBUnboundedSpectrum(cs, rgb);
            }
            Spectrum illuminant = new RGBIlluminantSpectrum(cs, rgb);
            norm = 1 / pbrt.SpectrumToPhotometric(illuminant);
            return illuminant;
        }

        private static ulong Mix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }

        private static Float Unit(ulong h)
        {
            return (Float)((h >> 40) * (1.0 / 16777216.0));
        }

        private void PrepareGrid()
        {
            if (options.userMajorant > 0)
            {
                majorantGrid = new MajorantGrid(bounds, new Point3i(1, 1, 1), new Allocator());
                majorantGrid.Set(0, 0, 0, options.userMajorant);
                return;
            }
            Point3i res = options.resolution;
            MajorantGrid grid = new MajorantGrid(bounds, res, new Allocator());
            int k = Math.Max(1, options.samplesPerAxis);
            Float margin = Math.Max(1f, options.margin);
            // The extinction bound at the lattice nodes (the corners of the cells), shared by the 8 cells around a node
            Float[] nodeBound = new Float[(res.x + 1) * (res.y + 1) * (res.z + 1)];
            Parallel.For(0, res.z + 1, (int z) =>
            {
                for (int y = 0; y <= res.y; ++y)
                {
                    for (int x = 0; x <= res.x; ++x)
                    {
                        Point3f pm = bounds.Lerp(new Point3f((Float)x / res.x, (Float)y / res.y, (Float)z / res.z));
                        OSLVolumeValues v;
                        Evaluate(renderFromMedium.Apply(pm), new Vector3f(0, 0, 0), 0, out v);
                        nodeBound[x + (res.x + 1) * (y + (res.y + 1) * z)] = ExtinctionBound(ref v);
                    }
                }
            });
            Parallel.For(0, res.z, (int z) =>
            {
                for (int y = 0; y < res.y; ++y)
                {
                    for (int x = 0; x < res.x; ++x)
                    {
                        Float maxBound = 0;
                        for (int dz = 0; dz < 2; ++dz)
                        {
                            for (int dy = 0; dy < 2; ++dy)
                            {
                                for (int dx = 0; dx < 2; ++dx)
                                {
                                    maxBound = std.max(maxBound, nodeBound[(x + dx) + (res.x + 1) * ((y + dy) + (res.y + 1) * (z + dz))]);
                                }
                            }
                        }
                        ulong cell = (ulong)(x + res.x * (y + res.y * z));
                        int n = 0;
                        for (int sz = 0; sz < k; ++sz)
                        {
                            for (int sy = 0; sy < k; ++sy)
                            {
                                for (int sx = 0; sx < k; ++sx)
                                {
                                    ulong h = Mix(cell * 0x2545F4914F6CDD1DUL + (ulong)(n++) * 0x9E3779B1UL + 12345);
                                    Float ux = (sx + Unit(h)) / k;
                                    Float uy = (sy + Unit(Mix(h))) / k;
                                    Float uz = (sz + Unit(Mix(Mix(h)))) / k;
                                    Point3f pm = bounds.Lerp(new Point3f((x + ux) / res.x, (y + uy) / res.y, (z + uz) / res.z));
                                    OSLVolumeValues v;
                                    Evaluate(renderFromMedium.Apply(pm), new Vector3f(0, 0, 0), 0, out v);
                                    maxBound = std.max(maxBound, ExtinctionBound(ref v));
                                }
                            }
                        }
                        grid.Set(x, y, z, maxBound * margin);
                    }
                }
            });
            majorantGrid = grid;
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // The Medium interface
        // ---------------------------------------------------------------------------------------------------------------------------------

        private MediumProperties Empty()
        {
            return new MediumProperties { sigma_a = new SampledSpectrum(0f), sigma_s = new SampledSpectrum(0f), phase = HG(0f), Le = new SampledSpectrum(0f) };
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            if (!prepared)
            {
                Prepare();
            }
            if (constantValues)
            {
                if (boundsGiven && !pbrt.Inside(renderFromMedium.ApplyInverse(p), bounds))
                {
                    return Empty();
                }
                return new MediumProperties { sigma_a = sigma_a_spec.Sample(lambda), sigma_s = sigma_s_spec.Sample(lambda), phase = constantPhase,
                                              Le = Le_spec.Sample(lambda) };
            }
            Point3f pm = renderFromMedium.ApplyInverse(p);
            if (!pbrt.Inside(pm, bounds))
            {
                return Empty();
            }
            OSLVolumeValues v;
            Evaluate(p, currentDirection, currentTime, out v);
            MediumProperties mp = new MediumProperties();
            mp.sigma_a = sigmaScale * Coefficient(v.sigma_a, lambda);
            mp.sigma_s = sigmaScale * Coefficient(v.sigma_s, lambda);
            mp.phase = Phase(ref v);
            mp.Le = new SampledSpectrum(0f);
            if (v.hasEmission && emissive)
            {
                mp.Le = LeScale * OSLSystem.ToEmissionSpectrum(v.emission.x, v.emission.y, v.emission.z, lambda);
            }
            if (options.check)
            {
                CheckMajorant(pm, mp, lambda);
            }
            return mp;
        }

        // Compares the extinction of an evaluation with the majorant that the tracking of the ray used for its cell
        private void CheckMajorant(Point3f pm, MediumProperties mp, SampledWavelengths lambda)
        {
            Interlocked.Increment(ref totalChecked);
            MajorantGrid grid = majorantGrid;
            Vector3f o = grid.bounds.Offset(pm);
            int x = std.min(std.max((int)(o.x * grid.res.x), 0), grid.res.x - 1);
            int y = std.min(std.max((int)(o.y * grid.res.y), 0), grid.res.y - 1);
            int z = std.min(std.max((int)(o.z * grid.res.z), 0), grid.res.z - 1);
            Float majorant = grid.Lookup(x, y, z);
            SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
            Float worst = 0;
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                worst = std.max(worst, sigma_t[i]);
            }
            if (worst <= majorant * (1 + 1e-4f))
            {
                return;
            }
            Interlocked.Increment(ref violations);
            Interlocked.Increment(ref totalViolations);
            Float ratio = majorant > 0 ? worst / majorant : Float.PositiveInfinity;
            lock (prepareMutex)
            {
                worstRatio = std.max(worstRatio, ratio);
                if (!warned)
                {
                    warned = true;
                    pbrt.Warning(pbrt.StringPrintf("OSL medium: the extinction %f at medium-space point %s exceeds the majorant %f of its cell. The bound is empirical; " +
                                                   "raise \"majorantmargin\", \"majorantsamples\" or \"majorantres\", or give \"float majorant\". Further violations are counted " +
                                                   "(statistic \"Media/OSL majorant violations\").", worst, pm, majorant));
                }
            }
        }

        public RayMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            if (!prepared)
            {
                Prepare();
            }
            currentDirection = ray.d;
            currentTime = ray.time;
            if (constantValues && !boundsGiven)
            {
                return new HomogeneousMajorantIterator(0, raytMax * Length(ray.d), sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            }
            // Transform ray to medium's space and compute bounds overlap
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
            {
                return new DDAMajorantIterator();
            }
            SampledSpectrum sigma_t = constantValues ? sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda) : new SampledSpectrum(1f);
            return new DDAMajorantIterator(ray, tMin, tMax, majorantGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return SampleRay(ray, tMax, lambda);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ OSLMedium group: %s bounds: %s renderFromMedium: %s ]", group.name, bounds, renderFromMedium);
        }

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Scene file
        // ---------------------------------------------------------------------------------------------------------------------------------

        // The parameters of the medium that are not parameters of the shader
        private static readonly string[] mediumParameters = new string[] { "scale", "Lescale", "preset", "p0", "p1", "majorant", "sigma_t_max", "majorantres",
                                                                           "majorantsamples", "majorantmargin", "checkmajorant", "detecthomogeneous",
                                                                           "rayleigh", "tracker", "transmittance" };

        private static bool Has(ParameterDictionary parameters, string name)
        {
            foreach (ParsedParameter p in parameters.GetParameterVector())
            {
                if (p.name == name)
                {
                    return true;
                }
            }
            return false;
        }

        public static OSLMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            OSLScene.Enabled = true;
            Float scale = parameters.GetOneFloat("scale", 1f);
            Float Lescale = parameters.GetOneFloat("Lescale", 1f);
            bool boundsGiven = Has(parameters, "p0") || Has(parameters, "p1");
            Point3f p0 = parameters.GetOnePoint3f("p0", new Point3f(0f, 0f, 0f));
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(1f, 1f, 1f));
            OSLMajorantOptions options = new OSLMajorantOptions();
            List<int> res = parameters.GetIntArray("majorantres");
            if (res.Count == 1)
            {
                options.resolution = new Point3i(res[0], res[0], res[0]);
            }
            else if (res.Count == 3)
            {
                options.resolution = new Point3i(res[0], res[1], res[2]);
            }
            else if (res.Count != 0)
            {
                pbrt.ErrorExit(loc, "\"integer majorantres\" takes one value or three.");
            }
            if (options.resolution.x < 1 || options.resolution.y < 1 || options.resolution.z < 1)
            {
                pbrt.ErrorExit(loc, "\"integer majorantres\" must be positive.");
            }
            options.samplesPerAxis = parameters.GetOneInt("majorantsamples", options.samplesPerAxis);
            options.margin = parameters.GetOneFloat("majorantmargin", options.margin);
            options.userMajorant = parameters.GetOneFloat("majorant", parameters.GetOneFloat("sigma_t_max", 0f));
            options.check = parameters.GetOneBool("checkmajorant", true);
            options.detectHomogeneous = parameters.GetOneBool("detecthomogeneous", true);
            Float rayleigh = parameters.GetOneFloat("rayleigh", 0f);
            parameters.GetOneString("tracker", "majorant");
            parameters.GetOneString("transmittance", "ratio");

            string preset = parameters.GetOneString("preset", "");
            TextureParameterDictionary tparams = new TextureParameterDictionary(parameters, new NamedTextures());
            OSLGroupSpec spec = OSLGroupBuilder.FromParameters(tparams, loc, false, "", alloc, mediumParameters);
            if (preset.Length != 0)
            {
                // The measured coefficients of the preset are the values of the parameters sigma_a and sigma_s of the shader, when it has them and they are not given
                RGB sa, ss;
                if (!pbrt.GetMediumScatteringRGB(preset, out sa, out ss))
                {
                    pbrt.Warning(loc, pbrt.StringPrintf("Material preset \"%s\" not found.", preset));
                }
                else
                {
                    OSLLayerSpec last = spec.layers.Count > 0 ? spec.layers[spec.layers.Count - 1] : null;
                    AddPresetParameter(spec, last, "sigma_a", sa);
                    AddPresetParameter(spec, last, "sigma_s", ss);
                }
            }

            Transform worldFromShader = new Transform();
            OSLShaderGroup group = OSLGroupBuilder.Build(spec, "medium", worldFromShader, null);
            group.renderFromShader = renderFromMedium;
            group.hasRenderFromShader = true;
            return new OSLMedium(spec, group, renderFromMedium, new Bounds3f(p0, p1), boundsGiven, scale, Lescale, options, rayleigh);
        }

        private static void AddPresetParameter(OSLGroupSpec spec, OSLLayerSpec layer, string name, RGB value)
        {
            if (layer == null || spec.groupSpec != null)
            {
                return;
            }
            foreach (OSLParam existing in layer.parameters)
            {
                if (existing.name == name)
                {
                    return;
                }
            }
            OSLQuery query = new OSLQuery();
            if (!query.open(layer.shader, string.Join(":", OSLSystem.SearchPaths())) || query.getparam(name) == null)
            {
                return;
            }
            OSLParam op = new OSLParam();
            op.name = name;
            op.type = TypeDescs.TypeColor;
            op.value = new float[] { value.r, value.g, value.b };
            layer.parameters.Add(op);
        }
    }
}
