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
                                       out double[] mean, out double[] stderr)
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
            VolPathIntegrator integrator = new VolPathIntegrator(maxDepth, camera, sampler, bvh, lights);
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

        private static void Compare(string label, double[] mean, double[] stderr, double[] reference)
        {
            for (int c = 0; c < 3; ++c)
            {
                Console.WriteLine($"PROBE chromatic {label} channel {c}: {mean[c]:F5} reference {reference[c]:F5} stderr {stderr[c]:F5}");
                EXPECT_LT(Math.Abs(mean[c] - reference[c]), 4 * stderr[c] + 0.01 * Math.Abs(reference[c]));
            }
        }

        private static TextureMedium MakeTextureMedium(Spectrum sigma_a, Spectrum sigma_s, string densityName, Float densityOffset,
                                                       Float densityScale, string emissionName, Float emissionScale,
                                                       NamedTextures textures)
        {
            TextureMedium medium = new TextureMedium(new Bounds3f(new Point3f(-2, -2, -2), new Point3f(2, 2, 2)), new Transform(),
                                                     sigma_a, sigma_s, 0f, null, densityOffset, densityScale, 8, false, 0.01f, 0,
                                                     false, false, false, false, false, false, 4f, .5f, 0f, false, 4, 2, .7f, false,
                                                     4, 0f, true, densityName, "", "", "", emissionName, emissionScale, false, 0f,
                                                     true, null);
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
    }
}
