// (not in pbrt-v4) furmaterial.cs
//
// Material "fur": a fiber of a cuticle, a cortex and a medulla (FurBxDF), for the strand shapes and the curves.
//
//   "spectrum sigma_a"       the absorption of the cortex for a length of one radius of the fiber (as "sigma_a" of "hair");
//                            without it "float eumelanin" and "float pheomelanin" (the pigments of "hair"), or else brown
//   "integer shells"         (1) rings of the cortex. With more than one the texture of "sigma_a" (or of the pigments) is read
//                            once for each ring, with the second texture coordinate the depth of the ring: 0 at the axis of
//                            the fiber and 1 at its surface, in place of the coordinate across the strand; a texture of that
//                            coordinate is the color of the fiber by its depth
//   "float medulla"          (0) the radius of the medulla, of the radius of the fiber (0: none, the fiber of "hair")
//   "float sigma_ms"         (2) the scattering of the medulla for a length of one radius
//   "spectrum sigma_ma"      (0) the absorption of the medulla
//   "float g"                (0.4) the mean cosine of the scattering of the medulla
//   "float eta" (1.55)  "float beta_m" (0.3)  "float beta_n" (0.3)  "float alpha" (2)    as those of "hair"
//   "string cuticle"         "smooth" (the default: a smooth surface, spread by beta_m and beta_n as "hair" is), "microfacet"
//                            (a rough dielectric surface of "float roughness" (0.2), one reflection at the facets),
//                            "heitz" or "facetforge" (the same with the light between the facets, by the walk of Heitz et
//                            al. 2016 or by FacetForge: no light is lost at the surface); with "walk"
//   "float aspect"           (1) the width of the cross-section across its long axis over the width along it: an ellipse below
//                            1; with "walk"
//   "vector3 axis"           (0 0 1) the long axis of the cross-section is the part of this direction across the fiber, turned
//   "float twist"            (0) by this many turns for each unit of the first texture coordinate (along the strand)
//   "float diameter"         (0) of the fiber in micrometers. Above 0 the light that the fiber bends around itself is added
//                            (FiberDiffractionBxDF: a narrow lobe straight on, of another width for each wavelength), and the
//                            fiber of rays has half the light: the strands of the scene are to be twice as wide for it
//   "string diffraction"     "exact" (the default: the lobe of a slit, with its bright and dark lines) or "smooth" (the lines
//                            beyond the middle of the lobe left out for their mean: less colored speckle; not in the paper)
//   "float beta_d"           (0.02) the width of the diffraction lobe along the fiber, on the scale of beta_m (the paper has
//                            "the longitudinal function of pbrt" without a width). The light that a straight fiber bends
//                            around itself stays on the cone of its direction: it does not meet the cuticle, so it is not
//                            spread by beta_m, only by how far the fiber is from straight; the default is about 1 degree.
//                            (0.17 is as wide as the TT lobe at beta_m 0.3, 0.3 as the R lobe, 0.51 as the TRT lobe.)
//   "string field"           "far" (the default: the lobe at every point of the strand, which is right for a strand thinner
//                            than a pixel) or "multiscale" (after the near and far fields of Yan et al. 2017, here for the
//                            diffraction; not in the papers): by the part of the width of the strand that the pixel covers
//                            (all of it: "far"), and for the rest the near field, where the fiber is the inner half of the
//                            strand, without the lobe, and the light of the outer half goes straight on. The choice
//                            between the two is made by the point (a hash of it), in that proportion. For the strand
//                            shapes (rocap, rocylinder, ribbon3); "far" on other shapes
//   "string lobeintegral"    "exact" (the default: the lobe divided by its integral) or "paper" (as the paper writes it)
//   "string model"           "walk" (the default: the random walk of FurBxDF) or "table" (the five lobes of the paper with
//                            tables of what the medulla scatters, FurTableBxDF: one ring of the cortex)
//   "integer nsamples"       (1) walks for each value of the fiber
//   "integer maxdepth"       (64) steps of a walk

using System;

namespace pbrt
{
    // FurMaterial Definition
    public sealed unsafe class FurMaterial : Material
    {
        // FurMaterial Public Methods
        public FurMaterial(SpectrumTexture sigma_a, FloatTexture eumelanin, FloatTexture pheomelanin, int shells, FloatTexture medulla, FloatTexture sigma_ms,
                           SpectrumTexture sigma_ma, FloatTexture g, FloatTexture eta, FloatTexture beta_m, FloatTexture beta_n, FloatTexture alpha, int samples, int maxDepth, bool table, Float diameter, Float roughness, Float aspect, Vector3f axis, Float twist, int cuticle, bool smoothLobe, Float lobeWidth, bool paperIntegral, bool multiscale)
        {
            this.multiscale = multiscale;
            this.lobeWidth = lobeWidth;
            this.paperIntegral = paperIntegral;
            this.smoothLobe = smoothLobe;
            this.cuticle = cuticle;
            this.roughness = roughness;
            this.aspect = aspect;
            this.axis = axis;
            this.twist = twist;
            this.table = table;
            this.diameter = diameter;
            this.sigma_a = sigma_a;
            this.eumelanin = eumelanin;
            this.pheomelanin = pheomelanin;
            this.shells = shells;
            this.medulla = medulla;
            this.sigma_ms = sigma_ms;
            this.sigma_ma = sigma_ma;
            this.g = g;
            this.eta = eta;
            this.beta_m = beta_m;
            this.beta_n = beta_n;
            this.alpha = alpha;
            this.samples = samples;
            this.maxDepth = maxDepth;
        }

        public static string Name()
        {
            return "FurMaterial";
        }

        public override bool CanEvaluateTextures<TextureEvaluator>(TextureEvaluator texEval)
        {
            return texEval.CanEvaluate(new FloatTexture[] { eumelanin, pheomelanin, medulla, sigma_ms, g, eta, beta_m, beta_n, alpha },
                                       new SpectrumTexture[] { sigma_a, sigma_ma });
        }

        public override BSDF GetBSDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            Float bm = std.max<Float>(1e-2f, std.min<Float>(1.0f, texEval.Apply(beta_m, ctx)));
            Float bn = std.max<Float>(1e-2f, std.min<Float>(1.0f, texEval.Apply(beta_n, ctx)));
            Float core = std.max<Float>(0, std.min<Float>(0.98f, texEval.Apply(medulla, ctx)));
            // The offset across the strand, as that of "hair"
            Float h = -1 + 2 * ctx.uv[1];
            // The field of the light that the fiber bends around itself: far (the lobe, on a strand twice as wide), or near
            // where the pixel is smaller than the strand, by the part of the width of the strand that the pixel covers
            bool near = false;
            if (multiscale && diameter > 0 && new RNG(pbrt.Hash(ctx.p)).Uniform<Float>() >= Covered(ctx))
            {
                // Near: the fiber is the inner half of the strand, and the light of the outer half passes it by
                if (Math.Abs(h) > 0.5f)
                {
                    return new BSDF(ctx.n, ctx.ns, ctx.dpdus, buf.Alloc(new FiberPassBxDF()));
                }
                near = true;
                h *= 2;
            }
            // The rings from the axis out: the medulla, and the rings of the cortex
            int first = core > 0 ? 1 : 0;
            Float[] bounds = new Float[first + shells], scattering = new Float[first + shells];
            SampledSpectrum[] absorption = new SampledSpectrum[first + shells];
            if (core > 0)
            {
                bounds[0] = core;
                scattering[0] = std.max<Float>(0, texEval.Apply(sigma_ms, ctx));
                absorption[0] = pbrt.ClampZero(texEval.Apply(sigma_ma, ctx, lambda));
            }
            for (int ring = 0; ring < shells; ++ring)
            {
                bounds[first + ring] = core + (1 - core) * (ring + 1) / shells;
                MaterialEvalContext at = ctx;
                if (shells > 1)
                {
                    // (the depth of the middle of the ring, where the coordinate across the strand was)
                    at.uv = new Point2f(ctx.uv[0], core + (1 - core) * (ring + 0.5f) / shells);
                }
                if (sigma_a != null)
                {
                    absorption[first + ring] = pbrt.ClampZero(texEval.Apply(sigma_a, at, lambda));
                }
                else
                {
                    absorption[first + ring] = HairBxDF.SigmaAFromConcentration(std.max<Float>(0, eumelanin != null ? texEval.Apply(eumelanin, at) : 0),
                                                                                std.max<Float>(0, pheomelanin != null ? texEval.Apply(pheomelanin, at) : 0)).Sample(lambda);
                }
            }
            bounds[first + shells - 1] = 1;
            if (table)
            {
                FurTableBxDF lobes = new FurTableBxDF(h, texEval.Apply(eta, ctx), absorption[first + shells / 2], core, core > 0 ? scattering[0] : 0,
                                                      core > 0 ? absorption[0] : new SampledSpectrum(0f),
                                                      std.max<Float>(-0.99f, std.min<Float>(0.99f, texEval.Apply(g, ctx))), bm, bn, texEval.Apply(alpha, ctx));
                return new BSDF(ctx.n, ctx.ns, ctx.dpdus, near ? buf.Alloc(lobes) : Diffracting(lobes, bm, false, lambda, buf));
            }
            FurBxDF bxdf = new FurBxDF(h, texEval.Apply(eta, ctx), bounds, absorption, scattering, std.max<Float>(-0.99f, std.min<Float>(0.99f, texEval.Apply(g, ctx))), bm, bn,
                                       texEval.Apply(alpha, ctx), samples, maxDepth);
            if (roughness > 0)
            {
                if (cuticle == 1)
                {
                    bxdf.UseMicrofacets(roughness);
                }
                else if (cuticle == 2)
                {
                    bxdf.UseMicrofacetsOfHeitz(roughness);
                }
                else
                {
                    bxdf.UseMicrofacetsOfFacetForge(roughness);
                }
            }
            if (aspect < 1)
            {
                // The long axis in the frame of the BSDF (x along the strand, z the shading normal): the part of `axis` across the
                // strand, and the twist along it
                Vector3f along = pbrt.Normalize(ctx.dpdus), normal = new Vector3f(ctx.ns.x, ctx.ns.y, ctx.ns.z);
                Vector3f side = pbrt.Cross(normal, along);
                bxdf.Aspect = aspect;
                bxdf.Orientation = (Float)(Math.Atan2(pbrt.Dot(axis, normal), pbrt.Dot(axis, side)) + 2 * Math.PI * twist * ctx.uv[0]);
            }
            return new BSDF(ctx.n, ctx.ns, ctx.dpdus, near ? buf.Alloc(bxdf) : Diffracting(bxdf, bm, true, lambda, buf));
        }

        // The part of the width of the strand that the pixel covers at the point, at most 1 (1 where it is not known: the far
        // field). The footprint is that of the camera for the point, whatever the ray that came to it (as glint.cs has it: one
        // function of the point), on the plane that faces the camera, measured across the strand.
        private Float Covered(MaterialEvalContext ctx)
        {
            SurfaceInteraction si = ctx.si;
            if (si == null || si.differentialsCamera == null || !(si.strandWidth > 0))
            {
                return 1;
            }
            Vector3f toCamera = si.differentialsCamera.GetCameraTransform().RenderFromCamera(new Point3f(0, 0, 0), si.time) - si.p();
            if (pbrt.LengthSquared(toCamera) == 0)
            {
                return 1;
            }
            toCamera = pbrt.Normalize(toCamera);
            Vector3f dpdx = default, dpdy = default;
            si.differentialsCamera.Approximate_dp_dxy(si.p(), new Normal3f(toCamera.x, toCamera.y, toCamera.z), si.time, si.differentialsSamples, &dpdx, &dpdy);
            // (the differentials are made smaller for the samples of the pixel: the pixel itself is wanted)
            Float scale = pbrt.GetOptions().disablePixelJitter ? 1 : std.max<Float>(0.125f, 1 / std.sqrt((Float)si.differentialsSamples));
            Vector3f across = pbrt.Cross(toCamera, pbrt.Normalize(ctx.dpdus));
            if (pbrt.LengthSquared(across) == 0)
            {
                return 1;
            }
            across = pbrt.Normalize(across);
            Float pixel = (Math.Abs(pbrt.Dot(dpdx, across)) + Math.Abs(pbrt.Dot(dpdy, across))) / scale;
            if (Float.IsNaN(pixel))
            {
                return 1;
            }
            return Math.Min(1, pixel / si.strandWidth);
        }

        // The fiber of rays, with the light that it bends around itself where it has a diameter
        private BxDF Diffracting(BxDF rays, Float beta_m, bool estimated, SampledWavelengths lambda, ScratchBuffer buf)
        {
            if (!(diameter > 0))
            {
                return buf.Alloc(rays);
            }
            SampledSpectrum sizes = new SampledSpectrum(0f);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                // (micrometers over nanometers)
                sizes[i] = lambda[i] > 0 ? 1000 * diameter / lambda[i] : 0;
            }
            // (along the fiber as the light that goes through it: the variance of the TT lobe of HairBxDF)
            Float variance = pbrt.Sqr(0.726f * lobeWidth + 0.812f * pbrt.Sqr(lobeWidth) + 3.7f * pbrt.Pow20(lobeWidth));
            return buf.Alloc(new FiberDiffractionBxDF(rays, sizes, variance, estimated, smoothLobe, paperIntegral));
        }

        public override FloatTexture GetDisplacement()
        {
            return null;
        }

        public override bool HasSubsurfaceScattering()
        {
            return false;
        }

        public override Image GetNormalMap()
        {
            return null;
        }

        public override BSSRDF GetBSSRDF<TextureEvaluator>(TextureEvaluator texEval, MaterialEvalContext ctx, ref SampledWavelengths lambda, ScratchBuffer buf)
        {
            return null;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FurMaterial sigma_a: %s shells: %d medulla: %s sigma_ms: %s sigma_ma: %s ]", sigma_a, shells, medulla, sigma_ms, sigma_ma);
        }

        public static FurMaterial Create(TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            SpectrumTexture sigma_a = parameters.GetSpectrumTextureOrNull("sigma_a", SpectrumType.Unbounded, alloc);
            FloatTexture eumelanin = parameters.GetFloatTextureOrNull("eumelanin", alloc);
            FloatTexture pheomelanin = parameters.GetFloatTextureOrNull("pheomelanin", alloc);
            if (sigma_a == null && eumelanin == null && pheomelanin == null)
            {
                // (the brown of "hair")
                sigma_a = new SpectrumConstantTexture(HairBxDF.SigmaAFromConcentration(1.3f, 0f));
            }
            SpectrumTexture sigma_ma = parameters.GetSpectrumTextureOrNull("sigma_ma", SpectrumType.Unbounded, alloc);
            if (sigma_ma == null)
            {
                sigma_ma = new SpectrumConstantTexture(new ConstantSpectrum(0));
            }
            string model = parameters.GetOneString("model", "walk");
            if (model != "walk" && model != "table")
            {
                pbrt.ErrorExit(loc, "fur: \"model\" is \"walk\" or \"table\".");
            }
            string diffraction = parameters.GetOneString("diffraction", "exact");
            if (diffraction != "exact" && diffraction != "smooth")
            {
                pbrt.ErrorExit(loc, "fur: \"diffraction\" is \"exact\" or \"smooth\".");
            }
            string lobeIntegral = parameters.GetOneString("lobeintegral", "exact");
            if (lobeIntegral != "exact" && lobeIntegral != "paper")
            {
                pbrt.ErrorExit(loc, "fur: \"lobeintegral\" is \"exact\" or \"paper\".");
            }
            string field = parameters.GetOneString("field", "far");
            if (field != "far" && field != "multiscale")
            {
                pbrt.ErrorExit(loc, "fur: \"field\" is \"far\" or \"multiscale\".");
            }
            string cuticle = parameters.GetOneString("cuticle", "smooth");
            if (cuticle != "smooth" && cuticle != "microfacet" && cuticle != "heitz" && cuticle != "facetforge")
            {
                pbrt.ErrorExit(loc, "fur: \"cuticle\" is \"smooth\", \"microfacet\", \"heitz\" or \"facetforge\".");
            }
            return new FurMaterial(sigma_a, eumelanin, pheomelanin, Math.Max(1, parameters.GetOneInt("shells", 1)), parameters.GetFloatTexture("medulla", 0f, alloc),
                                   parameters.GetFloatTexture("sigma_ms", 2f, alloc), sigma_ma, parameters.GetFloatTexture("g", 0.4f, alloc),
                                   parameters.GetFloatTexture("eta", 1.55f, alloc), parameters.GetFloatTexture("beta_m", 0.3f, alloc),
                                   parameters.GetFloatTexture("beta_n", 0.3f, alloc), parameters.GetFloatTexture("alpha", 2f, alloc),
                                   Math.Max(1, parameters.GetOneInt("nsamples", 1)), Math.Max(4, parameters.GetOneInt("maxdepth", 64)), model == "table", Math.Max(0, parameters.GetOneFloat("diameter", 0f)), cuticle != "smooth" ? Math.Max(1e-3f, parameters.GetOneFloat("roughness", 0.2f)) : 0,
                                   Math.Max(0.05f, Math.Min(1, parameters.GetOneFloat("aspect", 1f))), parameters.GetOneVector3f("axis", new Vector3f(0, 0, 1)),
                                   parameters.GetOneFloat("twist", 0f), cuticle == "microfacet" ? 1 : cuticle == "heitz" ? 2 : cuticle == "facetforge" ? 3 : 0, diffraction == "smooth",
                                   Math.Max(1e-2f, Math.Min(1, parameters.GetOneFloat("beta_d", 0.02f))), lobeIntegral == "paper", field == "multiscale");
        }

        // FurMaterial Private Members
        private readonly SpectrumTexture sigma_a, sigma_ma;
        private readonly FloatTexture eumelanin, pheomelanin, medulla, sigma_ms, g, eta, beta_m, beta_n, alpha;
        private readonly int shells, samples, maxDepth, cuticle;
        private readonly bool table, smoothLobe, paperIntegral, multiscale;
        private readonly Float lobeWidth;
        private readonly Float diameter, roughness, aspect, twist;
        private readonly Vector3f axis;
    }
}
