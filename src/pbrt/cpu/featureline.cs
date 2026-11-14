// (not in pbrt-v4) featureline.cs
//
// Feature lines (silhouettes, the outlines of objects, creases, steps in depth) drawn by the path tracer, after "Physically-
// based feature line rendering" (West, Georgiev, Hachisuka, SIGGRAPH Asia 2021): a line is a light source that the paths
// find. Each segment of a path is tested for a line near it: rays around the segment (a stencil, on the circle whose radius
// is half of the width of the line, a width given in pixels at the camera and carried along the path as a cone) are compared
// with the ray of the path, and where they find another object, a surface turned away or one at another depth, the path ends
// on the line and returns its colour. The lines are therefore seen as the camera would see them drawn in the scene: in
// reflections and through glass, out of focus where the scene is, and with the colours of what they are seen through.
// (Written from the idea of the paper, which could not be read again here: the stencil and the three measures are the
// common ones of ray traced feature lines, Choudhury and Parker 2009, and the thresholds are parameters.)
//
// Integrator "featureline": the parameters of "path" and
//   float linewidth           the width of a line in pixels; 2
//   spectrum linecolor        its colour ("rgb linecolor" [...], the radiance of the line); black
//   integer linesamples       the rays of the stencil around each segment; 8
//   bool objectlines          a line between two objects (or two materials); true
//   float normalthreshold     a line where the normals differ by more than this angle in degrees (0: none); 40
//   float depththreshold      a line where a surface is off the plane of the other by more than this many radii of the
//                             stencil (0: none); 3
//   string linebounces        "specular" (the default): lines are looked for from the camera and after specular and glossy
//                             bounces; "camera": from the camera only; "all": after every bounce

using System;
using System.Collections.Generic;

namespace pbrt
{
    public enum FeatureLineBounces
    {
        Camera,
        Specular,
        All
    }

    public struct FeatureLineSettings
    {
        public Float width;
        public int samples;
        public bool objectLines;
        // The cosine of the angle between normals beyond which there is a line (2: no such line)
        public Float cosNormal;
        // In radii of the stencil (0: no such line)
        public Float depthThreshold;
        public FeatureLineBounces bounces;
    }

    public static unsafe class FeatureLines
    {
        // Whether there is a line by the segment of the ray, which ends at `si` (or leaves the scene). `radius` is the radius of
        // the cone of the line where the ray starts and `spread` how much it grows for each unit of distance; `rotation` in
        // [0,1) turns the stencil. From the camera the rays of the stencil start on the circle of the cone, as the rays of the
        // neighbouring pixels do (an orthographic camera has no other difference between them); after a bounce they start
        // at the point of the bounce, where a start beside it could be under the surface.
        public static bool Detect(Primitive aggregate, Ray ray, pstd.optional<ShapeIntersection> si, FeatureLineSettings settings, Float radius, Float spread,
                                  Float rotation, bool fromCamera)
        {
            Float length = pbrt.Length(ray.d);
            if (length == 0 || settings.samples <= 0)
            {
                return false;
            }
            Vector3f d = ray.d / length;
            Vector3f u, v;
            pbrt.CoordinateSystem(d, &u, &v);
            bool hit = (bool)si;
            Float distance = hit ? si.value().tHit * length : 0;
            Float r = radius + spread * distance;
            Point3f pHit = hit ? si.value().intr.p() : default;
            Normal3f nHit = hit ? si.value().intr.n : default;
            for (int k = 0; k < settings.samples; ++k)
            {
                Float phi = 2 * pbrt.Pi * (rotation + (Float)k / settings.samples);
                Vector3f offset = MathF.Cos(phi) * u + MathF.Sin(phi) * v;
                // The ray of the stencil: to the point of the circle around the hit, or, for a ray that leaves the scene, in
                // the direction where the cone is as wide far away
                Point3f origin = fromCamera ? ray.o + radius * offset : ray.o;
                Vector3f direction = hit ? (pHit + r * offset) - origin : d + spread * offset;
                if (pbrt.LengthSquared(direction) == 0)
                {
                    continue;
                }
                direction = pbrt.Normalize(direction);
                pstd.optional<ShapeIntersection> other = aggregate.Intersect(new Ray(origin, direction, ray.time), pbrt.Infinity);
                if (!other)
                {
                    if (hit)
                    {
                        return true;
                    }
                    continue;
                }
                if (!hit)
                {
                    return true;
                }
                SurfaceInteraction a = si.value().intr, b = other.value().intr;
                if (settings.objectLines && (a.objectId != b.objectId || !ReferenceEquals(a.material, b.material)))
                {
                    return true;
                }
                if (pbrt.Dot(nHit, b.n) < settings.cosNormal)
                {
                    return true;
                }
                if (settings.depthThreshold > 0)
                {
                    Vector3f between = b.p() - pHit;
                    Float off = std.max(pbrt.AbsDot(between, nHit), pbrt.AbsDot(between, b.n));
                    if (off > settings.depthThreshold * r)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // FeatureLineIntegrator Definition: PathIntegrator with the test for a line on each segment
    public sealed unsafe class FeatureLineIntegrator : RayIntegrator
    {
        public FeatureLineIntegrator(int maxDepth, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights, string lightSampleStrategy,
                                     bool regularize, FeatureLineSettings settings, Spectrum lineColor)
            : base(camera, sampler, aggregate, lights)
        {
            this.maxDepth = maxDepth;
            this.lightSampler = LightSampler.Create(lightSampleStrategy, lights.ToArray(), new Allocator());
            this.regularize = regularize;
            this.settings = settings;
            this.lineColor = lineColor;
        }

        public override SampledSpectrum Li(RayDifferential ray, ref SampledWavelengths lambda, Sampler sampler,
                                           ScratchBuffer scratchBuffer, VisibleSurface visibleSurf)
        {
            SampledSpectrum L = new SampledSpectrum(0.0f), beta = new SampledSpectrum(1.0f);
            int depth = 0;

            Float p_b = 0, etaScale = 1;
            bool specularBounce = false, anyNonSpecularBounces = false;
            int oslRayType = 0;
            LightSampleContext prevIntrCtx = default;

            // The cone of a line: half of its width, as the radius at the camera and its growth with the distance, from the
            // rays of the neighbouring pixels
            Float radius = 0, spread = 0;
            bool lookForLines = ray.hasDifferentials;

            while (true)
            {
                pstd.optional<ShapeIntersection> si = Intersect(new Ray(ray));

                // The cone on this segment. Where the ray has the rays of the neighbouring pixels with it (from the camera,
                // and after a specular reflection or refraction, which bends them with the curvature of the surface), the
                // cone is theirs: a line keeps its width on the screen in a curved mirror or through a lens. After another
                // bounce it goes on as it was.
                if (lookForLines && ray.hasDifferentials)
                {
                    Vector3f d = Normalize(ray.d);
                    Float pixelAngle = (Length(Normalize(ray.rxDirection) - d) + Length(Normalize(ray.ryDirection) - d)) / 2;
                    Float pixelOffset = (Distance(ray.rxOrigin, ray.o) + Distance(ray.ryOrigin, ray.o)) / 2;
                    // (the differentials are those of a pixel times Wireframe.differentialScale)
                    spread = settings.width / 2 * pixelAngle / Wireframe.differentialScale;
                    radius = settings.width / 2 * pixelOffset / Wireframe.differentialScale;
                }

                // A line by this segment: the path ends on it
                if (lookForLines && (spread > 0 || radius > 0))
                {
                    if (FeatureLines.Detect(aggregate, new Ray(ray), si, settings, radius, spread, sampler.Get1D(), depth == 0))
                    {
                        // (a colour of 1 is a line as bright as a white surface under a light of 1: the film weighs a
                        // spectrum by the luminous efficiency, whose integral is CIE_Y_integral)
                        L += beta * lineColor.Sample(lambda) / CIE_Y_integral;
                        break;
                    }
                    if ((bool)si)
                    {
                        radius += spread * si.value().tHit * Length(ray.d);
                    }
                }

                if (!si)
                {
                    foreach (Light light in infiniteLights)
                    {
                        SampledSpectrum Le0 = light.Le(new Ray(ray), lambda);
                        if (depth == 0 || specularBounce)
                            L += beta * Le0;
                        else
                        {
                            Float p_l = lightSampler.PMF(prevIntrCtx, light) * light.PDF_Li(prevIntrCtx, ray.d, true);
                            Float w_b = PowerHeuristic(1, p_b, 1, p_l);
                            L += beta * w_b * Le0;
                        }
                    }
                    break;
                }
                si.value().intr.rayType = oslRayType;
                SampledSpectrum Le = si.value().intr.Le(-ray.d, lambda);
                if ((bool)Le)
                {
                    if (depth == 0 || specularBounce || si.value().intr.areaLight == null)
                        L += beta * Le;
                    else
                    {
                        Light areaLight = si.value().intr.areaLight;
                        Float p_l = lightSampler.PMF(prevIntrCtx, areaLight) * areaLight.PDF_Li(prevIntrCtx, ray.d, true);
                        Float w_l = PowerHeuristic(1, p_b, 1, p_l);
                        L += beta * w_l * Le;
                    }
                }

                SurfaceInteraction isect = si.value().intr;
                BSDF bsdf = isect.GetBSDF(ray, ref lambda, camera, scratchBuffer, sampler);
                if (!bsdf)
                {
                    specularBounce = true;
                    isect.SkipIntersection(ref ray, si.value().tHit);
                    continue;
                }

                if (regularize && anyNonSpecularBounces)
                {
                    bsdf.Regularize();
                }

                if (depth++ == maxDepth)
                    break;

                if (IsNonSpecular(bsdf.Flags()))
                {
                    L += beta * SampleLd(isect, bsdf, ref lambda, sampler);
                }

                Vector3f wo = -ray.d;
                Float u = sampler.Get1D();
                pstd.optional<BSDFSample> bs = bsdf.Sample_f(wo, u, sampler.Get2D());
                if (!bs)
                    break;
                beta *= bs.value().f * AbsDot(bs.value().wi, isect.shading.n) / bs.value().pdf;
                p_b = bs.value().pdfIsProportional ? bsdf.PDF(wo, bs.value().wi) : bs.value().pdf;
                specularBounce = bs.value().IsSpecular();
                oslRayType = OSLRayType.FromFlags(bs.value().flags);
                anyNonSpecularBounces |= !bs.value().IsSpecular();
                if (bs.value().IsTransmission())
                    etaScale *= Sqr(bs.value().eta);
                prevIntrCtx = new LightSampleContext(si.value().intr);

                // Lines are looked for on the next segment after a specular or glossy bounce, after any bounce, or not at all
                if (settings.bounces == FeatureLineBounces.Camera)
                {
                    lookForLines = false;
                }
                else if (settings.bounces == FeatureLineBounces.Specular && !(bs.value().IsSpecular() || bs.value().IsGlossy()))
                {
                    lookForLines = false;
                }

                ray = isect.SpawnRay(ray, bsdf, bs.value().wi, bs.value().flags, bs.value().eta);

                SampledSpectrum rrBeta = beta * etaScale;
                if (rrBeta.MaxComponentValue() < 1 && depth > 1)
                {
                    Float q = std.max<Float>(0, 1 - rrBeta.MaxComponentValue());
                    if (sampler.Get1D() < q)
                        break;
                    beta /= 1 - q;
                }
            }
            return L;
        }

        // (as PathIntegrator::SampleLd)
        SampledSpectrum SampleLd(SurfaceInteraction intr, BSDF bsdf, ref SampledWavelengths lambda, Sampler sampler)
        {
            LightSampleContext ctx = new LightSampleContext(intr);
            BxDFFlags flags = bsdf.Flags();
            if (IsReflective(flags) && !IsTransmissive(flags))
                ctx.pi = intr.OffsetRayOrigin(intr.wo);
            else if (IsTransmissive(flags) && !IsReflective(flags))
                ctx.pi = intr.OffsetRayOrigin(-intr.wo);

            Float u = sampler.Get1D();
            pstd.optional<SampledLight> sampledLight = lightSampler.Sample(ctx, u);
            Point2f uLight = sampler.Get2D();
            if (!sampledLight)
                return new SampledSpectrum(0.0f);

            Light light = sampledLight.value().light;
            pstd.optional<LightLiSample> ls = light.SampleLi(ctx, uLight, lambda, true);
            if (!ls || !ls.value().L || ls.value().pdf == 0)
                return new SampledSpectrum(0.0f);

            Vector3f wo = intr.wo, wi = ls.value().wi;
            SampledSpectrum f = bsdf.f(wo, wi) * AbsDot(wi, intr.shading.n);
            if (!f || !Unoccluded(intr, ls.value().pLight))
                return new SampledSpectrum(0.0f);

            Float p_l = sampledLight.value().p * ls.value().pdf;
            if (IsDeltaLight(light.Type()))
                return ls.value().L * f / p_l;
            Float p_b = bsdf.PDF(wo, wi);
            Float w_l = PowerHeuristic(1, p_l, 1, p_b);
            return w_l * ls.value().L * f / p_l;
        }

        public override string ToString()
        {
            return StringPrintf("[ FeatureLineIntegrator maxDepth: %d lineWidth: %f samples: %d ]", maxDepth, settings.width, settings.samples);
        }

        public static FeatureLineIntegrator Create(ParameterDictionary parameters, Camera camera, Sampler sampler, Primitive aggregate, List<Light> lights,
                                                   FileLoc loc)
        {
            int maxDepth = parameters.GetOneInt("maxdepth", 5);
            string lightStrategy = parameters.GetOneString("lightsampler", "bvh");
            bool regularize = parameters.GetOneBool("regularize", false);
            FeatureLineSettings settings = new FeatureLineSettings();
            settings.width = parameters.GetOneFloat("linewidth", 2f);
            settings.samples = parameters.GetOneInt("linesamples", 8);
            settings.objectLines = parameters.GetOneBool("objectlines", true);
            Float angle = parameters.GetOneFloat("normalthreshold", 40f);
            settings.cosNormal = angle > 0 ? MathF.Cos(Radians(angle)) : -2;
            settings.depthThreshold = parameters.GetOneFloat("depththreshold", 3f);
            string bounces = parameters.GetOneString("linebounces", "specular");
            if (bounces == "camera")
            {
                settings.bounces = FeatureLineBounces.Camera;
            }
            else if (bounces == "specular")
            {
                settings.bounces = FeatureLineBounces.Specular;
            }
            else if (bounces == "all")
            {
                settings.bounces = FeatureLineBounces.All;
            }
            else
            {
                ErrorExit(loc, StringPrintf("featureline: unknown linebounces \"%s\" (\"camera\", \"specular\", \"all\").", bounces));
            }
            Spectrum lineColor = parameters.GetOneSpectrum("linecolor", new ConstantSpectrum(0f), SpectrumType.Albedo, new Allocator());
            return new FeatureLineIntegrator(maxDepth, camera, sampler, aggregate, lights, lightStrategy, regularize, settings, lineColor);
        }

        int maxDepth;
        LightSampler lightSampler;
        bool regularize;
        FeatureLineSettings settings;
        Spectrum lineColor;
    }
}
