// (not in pbrt-v4) Regression tests for media whose extinction depends strongly on wavelength, run through the
// whole VolPathIntegrator so that the spectral MIS weights (r_u, r_l) are exercised together with null collisions.
//
// The camera sits at the center of an interface sphere that is filled with the medium, and the sphere is
// surrounded by a large sphere that emits a uniform spectrum inward. Every camera ray therefore travels the same distance through the medium
// and the right answer is known in closed form:
//
//   absorber      sigma_s = 0:             L_pixel(lambda) = L(lambda) exp(-sigma_a(lambda) R). It is computed by
//                                          rendering the same scene without a medium under a light whose spectrum
//                                          is the attenuated spectrum.
//   furnace       sigma_a = 0:             energy is conserved, so the pixel equals L whatever sigma_s(lambda) is.
//   equilibrium   Le = L, any sigma_a/s:   emission balances absorption, so the pixel equals L.
//
// The reference image is rendered with the same sample count and seed, so both images see the same sampled
// wavelengths and only the estimator under test differs.
//
// The texture-medium cases use a density texture whose bound is deliberately three times too large, so that two
// thirds of the collisions are null collisions, and a second texture that really varies along the ray.

using System;
using System.Collections.Generic;
using System.IO;
using testing;

namespace pbrt
{
    public static unsafe class ChromaticMediaTests
    {
        private const Float sphereRadius = 1f, emitterRadius = 5f;

        // Density 1 everywhere, with a bound that is valid but loose
        private sealed class LooseConstantTexture : FloatTexture
        {
            public LooseConstantTexture(Float value, Float bound) { this.value = value; this.bound = bound; }
            public override string ToString() { return "[ LooseConstantTexture ]"; }
            public override Float MinValue() { return value; }
            public override Float MaxValue() { return bound; }
            public override Float Evaluate(TextureEvalContext ctx) { return value; }
            private Float value, bound;
        }

        // sigma(lambda) = lo .. hi over 450 .. 650 nm, rising or falling, sampled every 5 nm
        private static Spectrum MakeRamp(Float lo, Float hi, bool rising)
        {
            List<Float> lambdas = new List<Float>();
            List<Float> values = new List<Float>();
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 5)
            {
                Float x = Clamp((lambda - 450) / 200, 0, 1);
                Float s = x * x * (3 - 2 * x);
                lambdas.Add(lambda);
                values.Add(lo + (hi - lo) * (rising ? s : 1 - s));
            }
            return new PiecewiseLinearSpectrum(lambdas, values);
        }

        // exp(-sigma(lambda) distance), sampled every nanometer
        private static Spectrum MakeAttenuation(Spectrum sigma, Float distance)
        {
            List<Float> lambdas = new List<Float>();
            List<Float> values = new List<Float>();
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 1)
            {
                lambdas.Add(lambda);
                values.Add((Float)Math.Exp(-sigma.Evaluate(lambda) * distance));
            }
            return new PiecewiseLinearSpectrum(lambdas, values);
        }

        private static void RenderMean(Medium medium, Spectrum lightSpectrum, Float lightScale, bool areaEmitter, int maxDepth, int spp, string filename,
                                       out double[] mean, out double[] stderr, bool bidirectional = false, bool lightWavelengths = false, bool spectralMIS = true)
        {
            Transform id = new Transform();
            Point2i resolution = new Point2i(16, 16);
            AnimatedTransform identityA = new AnimatedTransform(id, 0, id, 1);
            Filter filter = new BoxFilter(new Vector2f(0.5f, 0.5f));
            FilmBaseParameters fp = new FilmBaseParameters(resolution, new Bounds2i(new Point2i(0, 0), resolution), filter, 1.0f,
                                                           PixelSensor.CreateDefault(), filename);
            RGBFilm rgbFilm = new RGBFilm(fp, RGBColorSpace.sRGB);
            CameraBaseParameters cbp = new CameraBaseParameters(new CameraTransform(identityA), rgbFilm, medium,
                                                                new ParameterDictionary(), new FileLoc());
            Camera camera = new PerspectiveCamera(cbp, 45, new Bounds2f(new Point2f(-1, -1), new Point2f(1, 1)), 0.0f, 10.0f);

            Shape sphere = new Sphere(id, id, false, sphereRadius, -sphereRadius, sphereRadius, 360);
            List<Primitive> prims = new List<Primitive>();
            prims.Add(new GeometricPrimitive(sphere, null, null, new MediumInterface(medium, null)));

            // Either a uniform infinite light, which only the phase function samples (its PDF_Li is zero when the
            // PDF is allowed to be incomplete), or a large black sphere that emits the spectrum toward the inside.
            // The area light is also sampled by next event estimation, so the light MIS weights (r_l) of the
            // medium events take part in the result.
            List<Light> lights = new List<Light>();
            if (areaEmitter)
            {
                Shape emitter = new Sphere(id, id, true, emitterRadius, -emitterRadius, emitterRadius, 360);
                Light areaLight = new DiffuseAreaLight(id, new MediumInterface(), lightSpectrum, lightScale, emitter, null,
                                                       new Image(), null, false);
                lights.Add(areaLight);
                Material black = new DiffuseMaterial(new SpectrumConstantTexture(new ConstantSpectrum(0)), null, null);
                prims.Add(new GeometricPrimitive(emitter, black, areaLight, new MediumInterface()));
            }
            else
            {
                lights.Add(new UniformInfiniteLight(id, lightSpectrum, lightScale));
            }
            Primitive bvh = new BVHAggregate(prims);

            Sampler sampler = new IndependentSampler(spp, 3);
            Integrator integrator = bidirectional ? (Integrator)new BDPTIntegrator(camera, sampler, bvh, lights, maxDepth, false, false, false, spectralMIS)
                                                  : new VolPathIntegrator(maxDepth, camera, sampler, bvh, lights);
            if (lightWavelengths)
                rgbFilm.SetWavelengthDistribution(WavelengthDistribution.FromLights(lights));
            integrator.Render();

            ImageAndMetadata im = Image.Read(filename);
            Image image = im.image;
            mean = new double[3];
            stderr = new double[3];
            int n = resolution.x * resolution.y;
            for (int c = 0; c < 3; ++c)
            {
                double sum = 0, sum2 = 0;
                for (int t = 0; t < resolution.y; ++t)
                    for (int s = 0; s < resolution.x; ++s)
                    {
                        double v = image.GetChannel(new Point2i(s, t), c);
                        sum += v;
                        sum2 += v * v;
                    }
                mean[c] = sum / n;
                stderr[c] = Math.Sqrt(Math.Max(sum2 / n - mean[c] * mean[c], 0) / n);
            }
            try { File.Delete(filename); } catch (Exception) { }
        }

        private static void Compare(string label, double[] mean, double[] stderr, double[] reference, double relativeSlack = 0.01)
        {
            for (int c = 0; c < 3; ++c)
            {
                Console.WriteLine($"PROBE chromatic {label} channel {c}: {mean[c]:F5} reference {reference[c]:F5} stderr {stderr[c]:F5}");
                EXPECT_LT(Math.Abs(mean[c] - reference[c]), 4 * stderr[c] + relativeSlack * Math.Abs(reference[c]));
            }
        }

        private static TextureMedium MakeTextureMedium(Spectrum sigma_a, Spectrum sigma_s, string densityName, Float densityOffset,
                                                       Float densityScale, string emissionName, Float emissionScale,
                                                       NamedTextures textures, string sigmaAName = "", string sigmaSName = "",
                                                       bool emissionPerLength = false, Float emissionMajorantScale = 0f,
                                                       bool signedNull = false, Float initialMajorant = 0.01f)
        {
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-2, -2, -2), new Point3f(2, 2, 2)), new Transform(),
                                                     sigma_a, sigma_s, 0f, null, densityOffset, densityScale, 8, signedNull, initialMajorant, 0,
                                                     false, false, false, signedNull, false, false, 4f, .5f, 0f, false, 4, 2, .7f, false,
                                                     4, 0f, true, densityName, sigmaAName, sigmaSName, "", emissionName, emissionScale,
                                                     emissionPerLength, emissionMajorantScale, true, null);
            medium.ResolveTextures(textures, new FileLoc());
            return medium;
        }

        private static NamedTextures MakeTextures()
        {
            NamedTextures textures = new NamedTextures();
            textures.floatTextures["loose"] = new LooseConstantTexture(1f, 3f);
            // The offset keeps the density positive over the sphere; the texture varies along every ray
            textures.floatTextures["varying"] = new FBmTexture(new PointTransformMapping(Scale(1.3f, 1.3f, 1.3f)), 4, .5f);
            return textures;
        }

        private static string Name(string stem, bool areaEmitter)
        {
            return "chromatic_" + stem + (areaEmitter ? "_area.exr" : "_infinite.exr");
        }

        private static string EmitterLabel(bool areaEmitter)
        {
            return areaEmitter ? " (area light)" : " (infinite light)";
        }

        // The pixel equals the unattenuated light times exp(-sigma_a R) for every wavelength
        [TEST("ChromaticMedia", "AbsorberMatchesSpectralTransmittance")]
        public static void ChromaticMedia_AbsorberMatchesSpectralTransmittance()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum sigma_a = MakeRamp(0.3f, 3.8f, true);
            Spectrum zero = new ConstantSpectrum(0);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, MakeAttenuation(sigma_a, sphereRadius), scale, area, 8, 256, Name("ref_a", area), out refMean,
                           out refErr);

                RenderMean(new HomogeneousMedium(sigma_a, zero, 1f, new ConstantSpectrum(0), 1f, 0f, null), L, scale, area, 8, 256,
                           Name("hom_a", area), out mean, out err);
                Compare("absorber homogeneous" + EmitterLabel(area), mean, err, refMean);

                RenderMean(MakeTextureMedium(sigma_a, zero, "loose", 0f, 1f, "", 1f, MakeTextures()), L, scale, area, 8, 256,
                           Name("tex_a", area), out mean, out err);
                Compare("absorber texture medium, bound 3x too large" + EmitterLabel(area), mean, err, refMean);
            }
        }

        // Pure scattering conserves energy for any wavelength dependence of sigma_s
        [TEST("ChromaticMedia", "ScatteringFurnace")]
        public static void ChromaticMedia_ScatteringFurnace()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum sigma_s = MakeRamp(0.3f, 3.8f, false);
            Spectrum zero = new ConstantSpectrum(0);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, L, scale, area, 8, 256, Name("ref_f", area), out refMean, out refErr);

                RenderMean(new HomogeneousMedium(zero, sigma_s, 1f, new ConstantSpectrum(0), 1f, 0.3f, null), L, scale, area, 200,
                           256, Name("hom_f", area), out mean, out err);
                Compare("furnace homogeneous" + EmitterLabel(area), mean, err, refMean);

                RenderMean(MakeTextureMedium(zero, sigma_s, "loose", 0f, 1f, "", 1f, MakeTextures()), L, scale, area, 200, 256,
                           Name("tex_f", area), out mean, out err);
                Compare("furnace texture medium, bound 3x too large" + EmitterLabel(area), mean, err, refMean);

                RenderMean(MakeTextureMedium(zero, sigma_s, "varying", 0.8f, 2f, "", 1f, MakeTextures()), L, scale, area, 200, 256,
                           Name("tex_fv", area), out mean, out err);
                Compare("furnace texture medium, varying density" + EmitterLabel(area), mean, err, refMean);
            }
        }

        // A medium that emits what it absorbs is invisible, with chromatic absorption and scattering together
        [TEST("ChromaticMedia", "EmissionEquilibrium")]
        public static void ChromaticMedia_EmissionEquilibrium()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum sigma_a = MakeRamp(0.3f, 3.0f, true);
            Spectrum sigma_s = MakeRamp(0.3f, 3.0f, false);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, L, scale, area, 8, 256, Name("ref_e", area), out refMean, out refErr);

                RenderMean(new HomogeneousMedium(sigma_a, sigma_s, 1f, L, scale, 0f, null), L, scale, area, 200, 256,
                           Name("hom_e", area), out mean, out err);
                Compare("equilibrium homogeneous" + EmitterLabel(area), mean, err, refMean);

                NamedTextures textures = MakeTextures();
                textures.illuminantSpectrumTextures["emit"] = new SpectrumConstantTexture(L);
                RenderMean(MakeTextureMedium(sigma_a, sigma_s, "loose", 0f, 1f, "emit", scale, textures), L, scale, area, 200, 256,
                           Name("tex_e", area), out mean, out err);
                Compare("equilibrium texture medium, bound 3x too large" + EmitterLabel(area), mean, err, refMean);

                textures = MakeTextures();
                textures.illuminantSpectrumTextures["emit"] = new SpectrumConstantTexture(L);
                RenderMean(MakeTextureMedium(sigma_a, sigma_s, "varying", 0.8f, 2f, "emit", scale, textures), L, scale, area, 200,
                           256, Name("tex_ev", area), out mean, out err);
                Compare("equilibrium texture medium, varying density" + EmitterLabel(area), mean, err, refMean);
            }
        }

        // 1 + 0.8 sin(3x + 1) cos(2z), a density that varies along every ray, with bounds 0.2 and 1.8
        private sealed class WavyDensityTexture : FloatTexture
        {
            public static Float Density(Point3f p)
            {
                return 1f + .8f * (Float)(Math.Sin(3 * p.x + 1) * Math.Cos(2 * p.z));
            }
            public override string ToString() { return "[ WavyDensityTexture ]"; }
            public override Float MinValue() { return .2f; }
            public override Float MaxValue() { return 1.8f; }
            public override Float Evaluate(TextureEvalContext ctx) { return Density(ctx.p); }
        }

        // What a medium with the wavy density and the base absorption sigma_a(lambda) scaled by "ramp" absorbs, per
        // length: emitting it makes the medium invisible against a light of radiance scale
        private sealed class AbsorbedEmissionTexture : SpectrumTexture
        {
            public AbsorbedEmissionTexture(Spectrum sigma_a) { this.sigma_a = sigma_a; }
            public override string ToString() { return "[ AbsorbedEmissionTexture ]"; }
            public override Float MaxValue() { return 1.8f * sigma_a.MaxValue(); }
            public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
            {
                return WavyDensityTexture.Density(ctx.p) * sigma_a.Sample(lambda);
            }
            private Spectrum sigma_a;
        }

        // Progressive null-tracking, every pass starting from the majorant learned by the previous ones. The initial
        // majorant is far below the density, so the first passes rely on the signed null coefficients; boundsFloor 0.5 keeps
        // those weights bounded by starting every voxel at half of its analytic bound. Without a floor the estimate is still
        // unbiased, but its variance is too large for a test to resolve.
        private static TextureMedium MakeProgressiveMedium(Spectrum sigma_a, Spectrum sigma_s, string densityName, string sigmaAName,
                                                           string sigmaSName, string emissionName, Float emissionScale,
                                                           bool emissionPerLength, Float emissionMajorantScale, Float boundsFloor,
                                                           NamedTextures textures)
        {
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-2, -2, -2), new Point3f(2, 2, 2)), new Transform(),
                                                     sigma_a, sigma_s, 0f, null, 0f, 1f, 8, true, 0.1f, 0, false, false, false,
                                                     false, false, false, 4f, .5f, boundsFloor, false, 4, 2, .7f, false, 4, 0f, true,
                                                     densityName, sigmaAName, sigmaSName, "", emissionName, emissionScale,
                                                     emissionPerLength, emissionMajorantScale, true, null);
            medium.ResolveTextures(textures, new FileLoc());
            return medium;
        }

        // Textured absorption under progressive tracking, with the signed null coefficients that the texture forces
        [TEST("ChromaticMedia", "ProgressiveTexturedAbsorber")]
        public static void ChromaticMedia_ProgressiveTexturedAbsorber()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum ramp = MakeRamp(0.3f, 3.8f, true);
            Spectrum zero = new ConstantSpectrum(0);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, MakeAttenuation(ramp, sphereRadius), scale, area, 8, 256, Name("ref_pa", area), out refMean,
                           out refErr);

                // sigma_a is 1 times the texture, which is the ramp: the textures scale the base coefficients
                foreach (Float boundsFloor in new Float[] { 0.5f })
                {
                    TextureMedium.ClearProgressive();
                    NamedTextures textures = MakeTextures();
                    textures.unboundedSpectrumTextures["ramp"] = new SpectrumConstantTexture(ramp);
                    RenderMean(MakeProgressiveMedium(new ConstantSpectrum(1), zero, "loose", "ramp", "", "", 1f, false, 0f, boundsFloor,
                                                     textures), L, scale, area, 8, 256, Name("pa", area), out mean, out err);
                    Compare("progressive textured absorber, boundsfloor " + boundsFloor + EmitterLabel(area), mean, err, refMean);
                }
            }
            TextureMedium.ClearProgressive();
        }

        // Textured scattering and absorption with a density that varies along the ray, in the furnace and in equilibrium
        // with volume emission
        [TEST("ChromaticMedia", "ProgressiveTexturedFurnaceAndEquilibrium")]
        public static void ChromaticMedia_ProgressiveTexturedFurnaceAndEquilibrium()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum rampUp = MakeRamp(0.3f, 3.0f, true), rampDown = MakeRamp(0.3f, 3.0f, false);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, L, scale, area, 8, 256, Name("ref_pf", area), out refMean, out refErr);

                // Furnace: only scattering, scaled by a texture that changes with wavelength
                TextureMedium.ClearProgressive();
                NamedTextures textures = MakeTextures();
                textures.floatTextures["wavy"] = new WavyDensityTexture();
                textures.unboundedSpectrumTextures["down"] = new SpectrumConstantTexture(rampDown);
                RenderMean(MakeProgressiveMedium(zero, new ConstantSpectrum(1), "wavy", "", "down", "", 1f, false, 0f, 0.9f, textures), L,
                           scale, area, 200, 256, Name("pf", area), out mean, out err);
                Compare("progressive textured furnace" + EmitterLabel(area), mean, err, refMean);

                // Equilibrium: textured absorption and scattering, volume emission sigma_a Le with Le = L
                TextureMedium.ClearProgressive();
                textures = MakeTextures();
                textures.floatTextures["wavy"] = new WavyDensityTexture();
                textures.unboundedSpectrumTextures["up"] = new SpectrumConstantTexture(rampUp);
                textures.unboundedSpectrumTextures["down"] = new SpectrumConstantTexture(rampDown);
                textures.illuminantSpectrumTextures["emit"] = new SpectrumConstantTexture(L);
                RenderMean(MakeProgressiveMedium(new ConstantSpectrum(1), new ConstantSpectrum(1), "wavy", "up", "down", "emit", scale,
                                                 false, 0f, 0.5f, textures), L, scale, area, 200, 256, Name("pe", area), out mean, out err);
                Compare("progressive textured equilibrium" + EmitterLabel(area), mean, err, refMean);
            }
            TextureMedium.ClearProgressive();
        }

        // Per-length emission that sets the majorant, under progressive tracking: the medium emits exactly what its
        // textured absorption removes, so it is invisible
        [TEST("ChromaticMedia", "ProgressivePerLengthEmissionEquilibrium")]
        public static void ChromaticMedia_ProgressivePerLengthEmissionEquilibrium()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum rampUp = MakeRamp(0.3f, 3.0f, true), rampDown = MakeRamp(0.3f, 3.0f, false);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, L, scale, area, 8, 256, Name("ref_pl", area), out refMean, out refErr);

                foreach (Float boundsFloor in new Float[] { 0.5f })
                {
                    TextureMedium.ClearProgressive();
                    NamedTextures textures = MakeTextures();
                    textures.floatTextures["wavy"] = new WavyDensityTexture();
                    textures.unboundedSpectrumTextures["up"] = new SpectrumConstantTexture(rampUp);
                    textures.unboundedSpectrumTextures["down"] = new SpectrumConstantTexture(rampDown);
                    // sigma_a = wavy density * 1 * ramp, which is the density times the base absorption times the texture
                    textures.illuminantSpectrumTextures["emit"] = new AbsorbedEmissionTexture(rampUp);
                    RenderMean(MakeProgressiveMedium(new ConstantSpectrum(1), new ConstantSpectrum(1), "wavy", "up", "down", "emit",
                                                     scale, true, 4f, boundsFloor, textures), L, scale, area, 200, 256,
                               Name("pl", area), out mean, out err);
                    Compare("progressive per-length emission equilibrium, boundsfloor " + boundsFloor + EmitterLabel(area), mean, err,
                            refMean);
                }
            }
            TextureMedium.ClearProgressive();
        }

        // sigma_s = ramp(wavy density): a color ramp driven by a density that varies along every ray scales the scattering
        // coefficient; a furnace stays a furnace for it, with the analytic majorants and with progressive tracking
        [TEST("ChromaticMedia", "RampDrivenScatteringFurnace")]
        public static void ChromaticMedia_RampDrivenScatteringFurnace()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum zero = new ConstantSpectrum(0);
            Float[] positions = { .2f, 1f, 1.8f };
            RGB[] colors = { new RGB(.3f, 2.5f, .8f), new RGB(1.5f, .6f, 2.2f), new RGB(2.8f, 1.2f, .4f) };

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, L, scale, area, 8, 256, Name("ref_rf", area), out refMean, out refErr);

                NamedTextures textures = MakeTextures();
                textures.floatTextures["wavy"] = new WavyDensityTexture();
                textures.unboundedSpectrumTextures["tint"] = new SpectrumRampTexture(new WavyDensityTexture(), positions, colors, false,
                                                                                    SpectrumType.Unbounded, RGBColorSpace.sRGB);
                RenderMean(MakeTextureMedium(zero, new ConstantSpectrum(1), "loose", 0f, 1f, "", 1f, textures, "", "tint"), L, scale,
                           area, 200, 256, Name("rf", area), out mean, out err);
                Compare("ramp-driven furnace, analytic bounds" + EmitterLabel(area), mean, err, refMean);

                TextureMedium.ClearProgressive();
                RenderMean(MakeProgressiveMedium(zero, new ConstantSpectrum(1), "loose", "", "tint", "", 1f, false, 0f, 0.9f, textures),
                           L, scale, area, 200, 256, Name("rfp", area), out mean, out err);
                Compare("ramp-driven furnace, progressive" + EmitterLabel(area), mean, err, refMean);
            }
            TextureMedium.ClearProgressive();
        }

        // A blackbody emitter in a chromatic absorber, with nothing but its own emission to see: the pixel is
        // E(lambda) (1 - exp(-sigma_a(lambda) R)) / sigma_a(lambda), which a medium-free scene reproduces with a light of that
        // spectrum
        [TEST("ChromaticMedia", "BlackbodyEmissionMatchesAnalytic")]
        public static void ChromaticMedia_BlackbodyEmissionMatchesAnalytic()
        {
            Spectrum sigma_a = MakeRamp(0.4f, 3.5f, false);
            Spectrum zero = new ConstantSpectrum(0);
            Float emissionScale = 0.8f;
            Spectrum emitted = new BlackbodySpectrum(2600f);
            List<Float> lambdas = new List<Float>(), values = new List<Float>();
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 1)
            {
                Float a = sigma_a.Evaluate(lambda);
                lambdas.Add(lambda);
                values.Add(emitted.Evaluate(lambda) * (1 - (Float)Math.Exp(-a * sphereRadius)) / a);
            }
            Spectrum expected = new PiecewiseLinearSpectrum(lambdas, values);

            foreach (bool area in new bool[] { false, true })
            {
                double[] refMean, refErr, mean, err;
                RenderMean(null, expected, emissionScale, area, 8, 256, Name("ref_bb", area), out refMean, out refErr);

                foreach (bool progressive in new bool[] { false, true })
                {
                    TextureMedium.ClearProgressive();
                    NamedTextures textures = MakeTextures();
                    textures.illuminantSpectrumTextures["fire"] = new SpectrumBlackbodyTexture(new FloatConstantTexture(2600f), 1f, 0f,
                                                                                               false, 1000f);
                    TextureMedium medium;
                    if (progressive)
                        medium = MakeProgressiveMedium(sigma_a, zero, "loose", "", "", "fire", emissionScale, true, 4f, 0.5f, textures);
                    else
                        medium = MakeTextureMedium(sigma_a, zero, "loose", 0f, 1f, "fire", emissionScale, textures, "", "", true, 4f);
                    // The light is switched off: the medium is the only emitter
                    RenderMean(medium, new ConstantSpectrum(0), 0f, area, 8, 256, Name(progressive ? "bbp" : "bb", area), out mean,
                               out err);
                    Compare("blackbody emission " + (progressive ? "progressive" : "analytic bounds") + EmitterLabel(area), mean, err,
                            refMean);
                }
            }
            TextureMedium.ClearProgressive();
        }

        // A glowing blob: emission 2 exp(-|p - c|^2 / 0.04), with the exact bound over a box
        private sealed class BlobEmissionTexture : SpectrumTexture
        {
            public BlobEmissionTexture(Point3f center) { this.center = center; }
            public override string ToString() { return "[ BlobEmissionTexture ]"; }
            public override Float MaxValue() { return 2f; }
            public override Float RegionMax(Bounds3f region)
            {
                Float d2 = 0;
                d2 += Axis(region.pMin.x, region.pMax.x, center.x);
                d2 += Axis(region.pMin.y, region.pMax.y, center.y);
                d2 += Axis(region.pMin.z, region.pMax.z, center.z);
                return 2f * (Float)Math.Exp(-d2 / .04f);
            }
            private static Float Axis(Float lo, Float hi, Float c)
            {
                Float d = c < lo ? lo - c : (c > hi ? c - hi : 0);
                return d * d;
            }
            public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
            {
                Float d2 = pbrt.DistanceSquared(ctx.p, center);
                return new SampledSpectrum(2f * (Float)Math.Exp(-d2 / .04f));
            }
            private Point3f center;
        }

        // Sampling the emission as a light changes the variance and not the mean. The medium is invisible for the
        // equilibrium of absorption and emission, which here needs both strategies: emission is seen from scattering
        // vertices and from the camera.
        [TEST("ChromaticMedia", "EmissionSamplingEquilibrium")]
        public static void ChromaticMedia_EmissionSamplingEquilibrium()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum rampUp = MakeRamp(0.3f, 3.0f, true), rampDown = MakeRamp(0.3f, 3.0f, false);
            try
            {
                foreach (bool area in new bool[] { false, true })
                {
                    double[] refMean, refErr, mean, err;
                    RenderMean(null, L, scale, area, 8, 256, Name("ref_nee", area), out refMean, out refErr);

                    foreach (bool perLength in new bool[] { false, true })
                    {
                        foreach (bool progressive in new bool[] { false, true })
                        {
                            TextureMedium.ClearProgressive();
                            TextureMedium.ClearEmissionLights();
                            NamedTextures textures = MakeTextures();
                            textures.floatTextures["wavy"] = new WavyDensityTexture();
                            textures.unboundedSpectrumTextures["up"] = new SpectrumConstantTexture(rampUp);
                            textures.unboundedSpectrumTextures["down"] = new SpectrumConstantTexture(rampDown);
                            textures.illuminantSpectrumTextures["emit"] = perLength ? (SpectrumTexture)new AbsorbedEmissionTexture(rampUp)
                                                                                    : new SpectrumConstantTexture(L);
                            TextureMedium medium;
                            if (progressive)
                                medium = MakeProgressiveMedium(new ConstantSpectrum(1), new ConstantSpectrum(1), "wavy", "up", "down",
                                                               "emit", scale, perLength, perLength ? 4f : 0f, 0.5f, textures);
                            else
                                medium = MakeTextureMedium(new ConstantSpectrum(1), new ConstantSpectrum(1), "wavy", 0f, 1f, "emit", scale,
                                                           textures, "up", "down", perLength, perLength ? 4f : 0f);
                            medium.EnableEmissionSampling(8);
                            RenderMean(medium, L, scale, area, 200, 256, Name("nee", area), out mean, out err);
                            Compare("emission sampling equilibrium, " + (perLength ? "per-length" : "volume") + ", " +
                                    (progressive ? "progressive" : "analytic bounds") + EmitterLabel(area), mean, err, refMean);
                        }
                    }
                }
            }
            finally
            {
                TextureMedium.ClearProgressive();
                TextureMedium.ClearEmissionLights();
            }
        }

        // A scattering medium with a glowing blob in the dark: the image with the emission also sampled as a light has the
        // same mean as the image that only gathers it at collisions
        [TEST("ChromaticMedia", "EmissionSamplingMatchesGathering")]
        public static void ChromaticMedia_EmissionSamplingMatchesGathering()
        {
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = MakeRamp(0.5f, 3.5f, false);
            try
            {
                foreach (bool perLength in new bool[] { false, true })
                {
                    double[] gatherMean = null, gatherErr = null;
                    foreach (bool sampled in new bool[] { false, true })
                    {
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                                 "", "", perLength, perLength ? 4f : 0f);
                        if (sampled)
                            medium.EnableEmissionSampling(8);
                        double[] mean, err;
                        RenderMean(medium, zero, 0f, true, 200, 512, Name("blob", sampled), out mean, out err);
                        if (!sampled)
                        {
                            gatherMean = mean;
                            gatherErr = err;
                            continue;
                        }
                        for (int c = 0; c < 3; ++c)
                        {
                            double combined = Math.Sqrt(err[c] * err[c] + gatherErr[c] * gatherErr[c]);
                            Console.WriteLine($"PROBE emission sampling {(perLength ? "per-length" : "volume")} channel {c}: sampled {mean[c]:F5} " +
                                              $"(stderr {err[c]:F5}) gathered {gatherMean[c]:F5} (stderr {gatherErr[c]:F5})");
                            EXPECT_LT(Math.Abs(mean[c] - gatherMean[c]), 4 * combined + 0.01 * Math.Abs(gatherMean[c]));
                        }
                    }
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // Bidirectional path tracing finds the emission of a volume with its light subpaths (which start inside the medium
        // and have to stay out of the parts of its bounds that the sphere doesn't enclose), with next event estimation, and
        // with the camera subpaths that pass through it. The medium is invisible when it emits what it absorbs, so every
        // strategy and all of their weights have to add up to the light.
        [TEST("ChromaticMedia", "BidirectionalEmissionEquilibrium")]
        public static void ChromaticMedia_BidirectionalEmissionEquilibrium()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            try
            {
                foreach (bool area in new bool[] { false, true })
                {
                    double[] refMean, refErr, mean, err;
                    RenderMean(null, L, scale, area, 10, 768, Name("ref_bdpt", area), out refMean, out refErr, true);

                    foreach (bool perLength in new bool[] { false, true })
                    {
                        TextureMedium.ClearProgressive();
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.illuminantSpectrumTextures["emit"] = perLength ? (SpectrumTexture)new AbsorbedEmissionTexture(new ConstantSpectrum(1))
                                                                                : new SpectrumConstantTexture(L);
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(1), new ConstantSpectrum(.5f), "wavy", 0f, 1f, "emit",
                                                                 scale, textures, "", "", perLength, perLength ? 4f : 0f);
                        medium.EnableEmissionSampling(8);
                        RenderMean(medium, L, scale, area, 10, 768, Name("bdpt", area), out mean, out err, true);
                        // The image is uniform, so the spread over the pixels is the noise of the render: a loss of a
                        // strategy shows as a few tenths of a percent
                        Compare("bidirectional emission equilibrium, " + (perLength ? "per-length" : "volume") + EmitterLabel(area),
                                mean, err, refMean, 0.004);
                    }
                }
            }
            finally
            {
                TextureMedium.ClearProgressive();
                TextureMedium.ClearEmissionLights();
            }
        }

        // A scattering medium with a glowing blob in the dark. The bidirectional integrator has one more bounce than the
        // volumetric path tracer for the same depth, because its depth counts the vertices between the camera and the
        // emission and the path tracer's counts the scattering before the gathering. The scattering is gray: the
        // bidirectional integrator, like the one of pbrt-v4, samples distances with the first wavelength only, which
        // converges very slowly when the coefficients depend on the wavelength.
        [TEST("ChromaticMedia", "BidirectionalMatchesPathTracing")]
        public static void ChromaticMedia_BidirectionalMatchesPathTracing()
        {
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = new ConstantSpectrum(2);
            try
            {
                foreach (bool perLength in new bool[] { false, true })
                {
                    double[] pathMean = null, pathErr = null;
                    foreach (bool bidirectional in new bool[] { false, true })
                    {
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                                 "", "", perLength, perLength ? 4f : 0f);
                        medium.EnableEmissionSampling(8);
                        double[] mean, err;
                        RenderMean(medium, zero, 0f, true, bidirectional ? 6 : 7, 1024, Name("blob_bdpt", bidirectional),
                                   out mean, out err, bidirectional);
                        if (!bidirectional)
                        {
                            pathMean = mean;
                            pathErr = err;
                            continue;
                        }
                        for (int c = 0; c < 3; ++c)
                        {
                            double combined = Math.Sqrt(err[c] * err[c] + pathErr[c] * pathErr[c]);
                            Console.WriteLine($"PROBE bidirectional {(perLength ? "per-length" : "volume")} channel {c}: bidirectional {mean[c]:F5} " +
                                              $"(stderr {err[c]:F5}) path tracing {pathMean[c]:F5} (stderr {pathErr[c]:F5})");
                            EXPECT_LT(Math.Abs(mean[c] - pathMean[c]), 4 * combined + 0.01 * Math.Abs(pathMean[c]));
                        }
                    }
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // With a scattering coefficient that depends on the wavelength the bidirectional integrator must agree with the volumetric
        // path tracer, and the spectral MIS weights must beat the ones that follow the first wavelength only.
        [TEST("ChromaticMedia", "BidirectionalSpectralMIS")]
        public static void ChromaticMedia_BidirectionalSpectralMIS()
        {
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = MakeRamp(0.8f, 3.5f, false);
            try
            {
                double[] pathMean = null, pathErr = null;
                double[] errors = new double[3];
                for (int variant = 0; variant < 3; ++variant)
                {
                    TextureMedium.ClearEmissionLights();
                    NamedTextures textures = MakeTextures();
                    textures.floatTextures["wavy"] = new WavyDensityTexture();
                    textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                    TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                             "", "", true, 4f);
                    medium.EnableEmissionSampling(8);
                    double[] mean, err;
                    // 0: volumetric path tracing, 1: bidirectional with spectral MIS, 2: bidirectional without it
                    RenderMean(medium, zero, 0f, true, variant == 0 ? 7 : 6, 1024, Name("blob_spec" + variant, true), out mean, out err,
                               variant != 0, false, variant == 1);
                    if (variant == 0)
                    {
                        pathMean = mean;
                        pathErr = err;
                        continue;
                    }
                    for (int c = 0; c < 3; ++c)
                    {
                        double combined = Math.Sqrt(err[c] * err[c] + pathErr[c] * pathErr[c]);
                        Console.WriteLine($"PROBE bidirectional {(variant == 1 ? "spectral MIS" : "first wavelength only")} channel {c}: " +
                                          $"{mean[c]:F5} (stderr {err[c]:F5}) path tracing {pathMean[c]:F5} (stderr {pathErr[c]:F5})");
                        if (variant == 1)
                            EXPECT_LT(Math.Abs(mean[c] - pathMean[c]), 4 * combined + 0.01 * Math.Abs(pathMean[c]));
                        errors[variant] += err[c];
                    }
                }
                Console.WriteLine($"PROBE bidirectional stderr summed over channels: spectral MIS {errors[1]:F5}, first wavelength only {errors[2]:F5}");
                EXPECT_LT(errors[1], errors[2]);
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // Progressive tracking without clamping gives null coefficients that are negative where the density exceeds the majorant.
        // The bidirectional integrator must handle them like the volumetric path tracer does: both must agree with the image of
        // the same medium with an ordinary majorant.
        [TEST("ChromaticMedia", "BidirectionalSignedNull")]
        public static void ChromaticMedia_BidirectionalSignedNull()
        {
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = new ConstantSpectrum(2);
            try
            {
                double[] referenceMean = null, referenceErr = null;
                for (int variant = 0; variant < 3; ++variant)
                {
                    TextureMedium.ClearEmissionLights();
                    TextureMedium.ClearProgressive();
                    NamedTextures textures = MakeTextures();
                    textures.floatTextures["wavy"] = new WavyDensityTexture();
                    textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                    // 0: ordinary majorant, volumetric path tracing; 1 and 2: signed nulls with a majorant far below the density
                    TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                             "", "", false, 0f, variant != 0, 0.4f);
                    medium.EnableEmissionSampling(8);
                    double[] mean, err;
                    RenderMean(medium, zero, 0f, true, variant == 2 ? 6 : 7, 2048, Name("blob_signed" + variant, true), out mean, out err,
                               variant == 2);
                    if (variant == 0)
                    {
                        referenceMean = mean;
                        referenceErr = err;
                        continue;
                    }
                    for (int c = 0; c < 3; ++c)
                    {
                        double combined = Math.Sqrt(err[c] * err[c] + referenceErr[c] * referenceErr[c]);
                        Console.WriteLine($"PROBE signed null {(variant == 1 ? "volumetric path tracing" : "bidirectional")} channel {c}: " +
                                          $"{mean[c]:F5} (stderr {err[c]:F5}) ordinary majorant {referenceMean[c]:F5} (stderr {referenceErr[c]:F5})");
                        EXPECT_LT(Math.Abs(mean[c] - referenceMean[c]), 4 * combined + 0.01 * Math.Abs(referenceMean[c]));
                    }
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
                TextureMedium.ClearProgressive();
            }
        }

        // The density of the directions that forward next event estimation picks integrates to one over the sphere, from
        // inside the bounds of the medium and from outside, and the sampled directions follow it
        [TEST("ChromaticMedia", "FneeDirectionDensity")]
        public static void ChromaticMedia_FneeDirectionDensity()
        {
            try
            {
                TextureMedium.ClearEmissionLights();
                NamedTextures textures = MakeTextures();
                textures.floatTextures["wavy"] = new WavyDensityTexture();
                textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), new ConstantSpectrum(1), "wavy", 0f, 1f, "blob", 1f,
                                                         textures);
                medium.EnableEmissionSampling(8, true);
                VolumeEmissionLight light = medium.EmissionLight();
                foreach (Point3f x in new Point3f[] { new Point3f(0, 0, 0), new Point3f(-1.2f, .7f, .3f), new Point3f(.3f, .1f, .2f) })
                {
                    // The integral over the sphere by stratified sampling
                    int n = 400;
                    double integral = 0;
                    for (int i = 0; i < n; ++i)
                    {
                        for (int j = 0; j < n; ++j)
                        {
                            Vector3f w = SampleUniformSphere(new Point2f((i + .5f) / n, (j + .5f) / n));
                            integral += light.PdfDirection(x, w);
                        }
                    }
                    integral *= 4 * Math.PI / (n * n);
                    Console.WriteLine($"PROBE fnee direction density from {x}: integral {integral:F5}");
                    EXPECT_LT(Math.Abs(integral - 1), 0.02);

                    // 1 / pdf averages to the solid angle of the directions that can be picked, which is the whole sphere
                    // inside the bounds. A histogram of the sampled octants checks the density against the picks.
                    int samples = 200000;
                    double[] octant = new double[8];
                    double[] expected = new double[8];
                    for (int i = 0; i < samples; ++i)
                    {
                        Point2f u = new Point2f(RadicalInverse(0, (uint64_t)i), RadicalInverse(1, (uint64_t)i));
                        if (light.SampleDirection(x, u, RadicalInverse(2, (uint64_t)i), out Vector3f w, out Float pdf))
                            octant[(w.x > 0 ? 1 : 0) + (w.y > 0 ? 2 : 0) + (w.z > 0 ? 4 : 0)] += 1.0 / samples;
                    }
                    for (int i = 0; i < n; ++i)
                    {
                        for (int j = 0; j < n; ++j)
                        {
                            Vector3f w = SampleUniformSphere(new Point2f((i + .5f) / n, (j + .5f) / n));
                            expected[(w.x > 0 ? 1 : 0) + (w.y > 0 ? 2 : 0) + (w.z > 0 ? 4 : 0)] +=
                                light.PdfDirection(x, w) * 4 * Math.PI / (n * n);
                        }
                    }
                    for (int o = 0; o < 8; ++o)
                    {
                        Console.WriteLine($"PROBE fnee octant {o}: picked {octant[o]:F4} density {expected[o]:F4}");
                        EXPECT_LT(Math.Abs(octant[o] - expected[o]), 0.01);
                    }
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // Forward next event estimation only changes the variance: the invisible emitting medium, and the glowing blob in the
        // dark compared with the image that gathers the emission at collisions alone
        [TEST("ChromaticMedia", "FneeEquilibriumAndGathering")]
        public static void ChromaticMedia_FneeEquilibriumAndGathering()
        {
            Spectrum L = new ConstantSpectrum(1);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = MakeRamp(0.5f, 3.5f, false);
            try
            {
                foreach (bool area in new bool[] { false, true })
                {
                    double[] refMean, refErr, mean, err;
                    RenderMean(null, L, scale, area, 8, 256, Name("ref_fnee", area), out refMean, out refErr);
                    foreach (bool perLength in new bool[] { false, true })
                    {
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.unboundedSpectrumTextures["up"] = new SpectrumConstantTexture(MakeRamp(0.3f, 3.0f, true));
                        textures.unboundedSpectrumTextures["down"] = new SpectrumConstantTexture(MakeRamp(0.3f, 3.0f, false));
                        textures.illuminantSpectrumTextures["emit"] = perLength
                            ? (SpectrumTexture)new AbsorbedEmissionTexture(MakeRamp(0.3f, 3.0f, true)) : new SpectrumConstantTexture(L);
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(1), new ConstantSpectrum(1), "wavy", 0f, 1f, "emit", scale,
                                                                 textures, "up", "down", perLength, perLength ? 4f : 0f);
                        medium.EnableEmissionSampling(8, true);
                        RenderMean(medium, L, scale, area, 200, 256, Name("fnee", area), out mean, out err);
                        Compare("line sampling equilibrium, " + (perLength ? "per-length" : "volume") + EmitterLabel(area), mean, err, refMean);
                    }
                }

                foreach (bool perLength in new bool[] { false, true })
                {
                    double[] gatherMean = null, gatherErr = null;
                    foreach (bool sampled in new bool[] { false, true })
                    {
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                                 "", "", perLength, perLength ? 4f : 0f);
                        if (sampled)
                            medium.EnableEmissionSampling(8, true);
                        double[] mean, err;
                        RenderMean(medium, zero, 0f, true, 200, 512, Name("blob_fnee", sampled), out mean, out err);
                        if (!sampled)
                        {
                            gatherMean = mean;
                            gatherErr = err;
                            continue;
                        }
                        for (int c = 0; c < 3; ++c)
                        {
                            double combined = Math.Sqrt(err[c] * err[c] + gatherErr[c] * gatherErr[c]);
                            Console.WriteLine($"PROBE line sampling {(perLength ? "per-length" : "volume")} channel {c}: sampled {mean[c]:F5} " +
                                              $"(stderr {err[c]:F5}) gathered {gatherMean[c]:F5} (stderr {gatherErr[c]:F5})");
                            EXPECT_LT(Math.Abs(mean[c] - gatherMean[c]), 4 * combined + 0.01 * Math.Abs(gatherMean[c]));
                        }
                    }
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // The bidirectional integrator with forward next event estimation of the emissive volume must agree with the volumetric
        // path tracer that gathers the emission, and with the bidirectional integrator that samples points of the volume.
        [TEST("ChromaticMedia", "BidirectionalFnee")]
        public static void ChromaticMedia_BidirectionalFnee()
        {
            Spectrum zero = new ConstantSpectrum(0);
            Spectrum sigma_s = MakeRamp(0.8f, 3.5f, false);
            try
            {
                foreach (bool perLength in new bool[] { false, true })
                {
                    double[] pathMean = null, pathErr = null;
                    double[] errors = new double[3];
                    for (int variant = 0; variant < 3; ++variant)
                    {
                        TextureMedium.ClearEmissionLights();
                        NamedTextures textures = MakeTextures();
                        textures.floatTextures["wavy"] = new WavyDensityTexture();
                        textures.illuminantSpectrumTextures["blob"] = new BlobEmissionTexture(new Point3f(.35f, .1f, .25f));
                        TextureMedium medium = MakeTextureMedium(new ConstantSpectrum(.2f), sigma_s, "wavy", 0f, 1f, "blob", 1f, textures,
                                                                 "", "", perLength, perLength ? 4f : 0f);
                        // 0: volumetric path tracing gathering the emission, 1: bidirectional with points of the volume,
                        // 2: bidirectional with forward next event estimation
                        if (variant != 0)
                            medium.EnableEmissionSampling(8, variant == 2);
                        double[] mean, err;
                        RenderMean(medium, zero, 0f, true, variant == 0 ? 7 : 6, 1024, Name("blob_bfnee" + variant, perLength), out mean, out err,
                                   variant != 0);
                        if (variant == 0)
                        {
                            pathMean = mean;
                            pathErr = err;
                            continue;
                        }
                        for (int c = 0; c < 3; ++c)
                        {
                            double combined = Math.Sqrt(err[c] * err[c] + pathErr[c] * pathErr[c]);
                            Console.WriteLine($"PROBE bidirectional {(perLength ? "per-length" : "volume")} {(variant == 1 ? "points" : "line sampling")} " +
                                              $"channel {c}: {mean[c]:F5} (stderr {err[c]:F5}) path tracing {pathMean[c]:F5} (stderr {pathErr[c]:F5})");
                            EXPECT_LT(Math.Abs(mean[c] - pathMean[c]), 4 * combined + 0.01 * Math.Abs(pathMean[c]));
                            errors[variant] += err[c];
                        }
                    }
                    Console.WriteLine($"PROBE bidirectional {(perLength ? "per-length" : "volume")} stderr summed: points {errors[1]:F5}, line sampling {errors[2]:F5}");
                }
            }
            finally
            {
                TextureMedium.ClearEmissionLights();
            }
        }

        // A spiky light spectrum rendered with wavelengths that follow it must give the same image as the sensor-only density
        // (here through an emitting and absorbing medium, so that the spectral MIS weights take part), with less noise.
        [TEST("ChromaticMedia", "LightWavelengthSampling")]
        public static void ChromaticMedia_LightWavelengthSampling()
        {
            List<Float> lambdas = new List<Float>();
            List<Float> values = new List<Float>();
            for (Float lambda = Lambda_min; lambda <= Lambda_max; lambda += 1)
            {
                lambdas.Add(lambda);
                values.Add((Float)(Math.Exp(-0.5 * Math.Pow((lambda - 620) / 10, 2)) + 0.3 * Math.Exp(-0.5 * Math.Pow((lambda - 450) / 12, 2)) + 0.002));
            }
            Spectrum L = new PiecewiseLinearSpectrum(lambdas, values);
            Float scale = 1 / SpectrumToPhotometric(L);
            Spectrum sigma_a = MakeRamp(0.3f, 3.0f, true);
            Spectrum sigma_s = MakeRamp(0.3f, 3.0f, false);

            double[] refMean, refErr, mean, err, meanLights, errLights;
            RenderMean(null, L, scale, true, 8, 1024, Name("ref_w", true), out refMean, out refErr);
            RenderMean(new HomogeneousMedium(sigma_a, sigma_s, 1f, L, scale, 0f, null), L, scale, true, 200, 256,
                       Name("vis_w", true), out mean, out err);
            RenderMean(new HomogeneousMedium(sigma_a, sigma_s, 1f, L, scale, 0f, null), L, scale, true, 200, 256,
                       Name("lights_w", true), out meanLights, out errLights, false, true);
            Compare("equilibrium, sensor-only wavelengths", mean, err, refMean);
            Compare("equilibrium, wavelengths that follow the light", meanLights, errLights, refMean);
            for (int c = 0; c < 3; ++c)
                Console.WriteLine($"PROBE wavelength sampling channel {c}: stderr sensor-only {err[c]:F5}, following the light {errLights[c]:F5}");
            EXPECT_LT(errLights[0] + errLights[1] + errLights[2], err[0] + err[1] + err[2]);
        }
    }
}
