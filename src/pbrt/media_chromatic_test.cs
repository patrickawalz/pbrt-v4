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
    }
}
