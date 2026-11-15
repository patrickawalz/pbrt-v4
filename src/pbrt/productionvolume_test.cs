// (not in pbrt-v4) productionvolume_test.cs
//
// The volume integrators of productionvolume.cs in a slab (the surfaces z = 0 and z = -1, a light at infinity), where the
// answers are known:
//   - Beer's law: the transmittance of the absorption alone, by wavelength;
//   - single scattering: the radiance scattered once towards the eye, against its closed form, for coefficients that change
//     with the wavelength (the mean over the wavelengths of a path, whichever of them samples the distance);
//   - multiple scattering: the light that comes back out of the slab after one scattering, against Chandrasekhar's closed
//     form, and after any number, against the walk of the layered BxDF (combine_test.cs).
// The heterogeneous integrators, the overlaps and the nesting are checked by renders against volpath (docs/open-items.md).

using System;
using testing;

namespace pbrt
{
    public static unsafe class ProductionVolumeTests
    {
        sealed class Slab : VolumeServices
        {
            public Vector3f toLight = new Vector3f(0, 0, 1);
            public SampledSpectrum sigma_t;

            public Slab(ulong seed) { rng = new RNG(seed); }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                distance = ray.d.z > 0 ? -ray.o.z / ray.d.z : (ray.d.z < 0 ? (-1 - ray.o.z) / ray.d.z : Float.PositiveInfinity);
                P = ray.Apply(distance);
                return !Float.IsInfinity(distance);
            }

            public override Float GenerateRandomNumber() { return rng.Uniform<Float>(); }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = toLight;
                L = new SampledSpectrum(1f);
                beamR = new SampledSpectrum(1f);
                beamTransmittance = Exp(-sigma_t * (-ctx.P.z / toLight.z));
                return true;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                wi = toLight;
                L = new SampledSpectrum(1f);
                return true;
            }
        }

        static HomogeneousMedium Medium(RGB absorption, RGB scattering, VolumeScattering kind)
        {
            HomogeneousMedium medium = new HomogeneousMedium(new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering),
                                                             1, new ConstantSpectrum(0f), 1, 0, new Allocator());
            VolumeIntegrator integrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Default.transmittance);
            integrator.scattering = kind;
            medium.volumeIntegrator = integrator;
            return medium;
        }

        [TEST("ProductionVolume", "BeersLaw")]
        public static void BeersLaw()
        {
            HomogeneousMedium medium = Medium(new RGB(0.3f, 0.8f, 2f), new RGB(1f, 1f, 1f), VolumeScattering.Absorption);
            VolumeContext ctx = new VolumeContext();
            ctx.lambda = SampledWavelengths.SampleVisible(0.3f);
            ctx.P = new Point3f(0, 0, 0);
            Slab rs = new Slab(1);
            Vector3f d = Normalize(new Vector3f(0.6f, 0, -0.8f));
            SampledSpectrum L, T, w;
            Point3f P;
            Ray wo;
            VolumeGeometry g;
            EXPECT_TRUE(Volume.Create(ctx, medium).Integrate(rs, new Ray(ctx.P, d, 0), out L, out T, out w, out P, out wo, out g));
            MediumProperties mp = medium.SamplePoint(ctx.P, ctx.lambda);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_LT(Math.Abs(T[i] - Math.Exp(-mp.sigma_a[i] / 0.8)), 1e-5);
                EXPECT_EQ(1f, w[i]);
            }
            EXPECT_LT(Math.Abs(P.z + 1), 1e-5);
        }

        [TEST("ProductionVolume", "SingleScattering")]
        public static void SingleScattering()
        {
            // coefficients that differ by a factor of 6 between the ends of the spectrum
            HomogeneousMedium medium = Medium(new RGB(0.2f, 0.5f, 1.5f), new RGB(0.5f, 1.5f, 3f), VolumeScattering.Single);
            RNG rng = new RNG(7);
            Slab rs = new Slab(2);
            rs.toLight = Normalize(new Vector3f(0.3f, 0.2f, 0.7f));
            Float mu = 0.6f, mu0 = rs.toLight.z;
            Vector3f d = new Vector3f(SafeSqrt(1 - mu * mu), 0, -mu);
            double estimate = 0, exact = 0, squares = 0, through = 0, throughExact = 0;
            int count = 400000;
            for (int s = 0; s < count; ++s)
            {
                VolumeContext ctx = new VolumeContext();
                ctx.lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                ctx.P = new Point3f(0, 0, 0);
                MediumProperties mp = medium.SamplePoint(ctx.P, ctx.lambda);
                rs.sigma_t = mp.sigma_a + mp.sigma_s;
                SampledSpectrum L, T, w;
                Point3f P;
                Ray wo;
                VolumeGeometry g;
                Volume.Create(ctx, medium).Integrate(rs, new Ray(ctx.P, d, 0), out L, out T, out w, out P, out wo, out g);
                double mean = 0;
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    mean += L[i] / NSpectrumSamples;
                    double st = rs.sigma_t[i], k = st * (1 + mu / mu0);
                    exact += mp.sigma_s[i] / (4 * Math.PI) * (1 - Math.Exp(-k / mu)) / k / NSpectrumSamples;
                    through += T[i] / NSpectrumSamples;
                    throughExact += Math.Exp(-st / mu) / NSpectrumSamples;
                }
                estimate += mean;
                squares += mean * mean;
            }
            estimate /= count;
            exact /= count;
            double error = Math.Sqrt(Math.Max(0, squares / count - estimate * estimate) / count);
            Console.WriteLine("MEASURE production volume, single scattering in a slab with coloured coefficients: {0:F5} +- {1:F5}, closed form {2:F5}; beam {3:F5} / {4:F5}",
                              estimate, error, exact, through / count, throughExact / count);
            EXPECT_LT(Math.Abs(estimate - exact), 4 * error + 1e-5);
            EXPECT_LT(Math.Abs(through - throughExact) / count, 1e-5);
        }

        // The course's listing 13: the point of the scattering from a density built by marching, the transmittance by
        // quadrature. Against the same closed form; the march takes the volume as constant over a step, which is exact here.
        [TEST("ProductionVolume", "SingleScatteringByDensity")]
        public static void SingleScatteringByDensity()
        {
            HomogeneousMedium medium = Medium(new RGB(0.2f, 0.5f, 1.5f), new RGB(0.5f, 1.5f, 3f), VolumeScattering.SinglePDF);
            medium.volumeIntegrator.stepSize = 0.05f;
            RNG rng = new RNG(11);
            Slab rs = new Slab(3);
            rs.toLight = Normalize(new Vector3f(0.3f, 0.2f, 0.7f));
            Float mu = 0.6f, mu0 = rs.toLight.z;
            Vector3f d = new Vector3f(SafeSqrt(1 - mu * mu), 0, -mu);
            double estimate = 0, exact = 0, squares = 0, through = 0, throughExact = 0, shadow = 0, shadowExact = 0;
            int count = 100000;
            for (int s = 0; s < count; ++s)
            {
                VolumeContext ctx = new VolumeContext();
                ctx.lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                ctx.P = new Point3f(0, 0, 0);
                MediumProperties mp = medium.SamplePoint(ctx.P, ctx.lambda);
                rs.sigma_t = mp.sigma_a + mp.sigma_s;
                SampledSpectrum L, T, w;
                Point3f P;
                Ray wo;
                VolumeGeometry g;
                Volume volume = Volume.Create(ctx, medium);
                EXPECT_TRUE(volume is SingleScatterPDFVolume);
                volume.Integrate(rs, new Ray(ctx.P, d, 0), out L, out T, out w, out P, out wo, out g);
                SampledSpectrum between = volume.Transmittance(rs, new Point3f(0.1f, 0.2f, -0.1f), new Point3f(0.4f, -0.3f, -0.8f));
                double length = Distance(new Point3f(0.1f, 0.2f, -0.1f), new Point3f(0.4f, -0.3f, -0.8f));
                double mean = 0;
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    mean += L[i] / NSpectrumSamples;
                    double st = rs.sigma_t[i], k = st * (1 + mu / mu0);
                    exact += mp.sigma_s[i] / (4 * Math.PI) * (1 - Math.Exp(-k / mu)) / k / NSpectrumSamples;
                    through += T[i] / NSpectrumSamples;
                    throughExact += Math.Exp(-st / mu) / NSpectrumSamples;
                    shadow += between[i] / NSpectrumSamples;
                    shadowExact += Math.Exp(-st * length) / NSpectrumSamples;
                }
                estimate += mean;
                squares += mean * mean;
            }
            estimate /= count;
            exact /= count;
            double error = Math.Sqrt(Math.Max(0, squares / count - estimate * estimate) / count);
            Console.WriteLine("MEASURE production volume, single scattering by a marched density: {0:F5} +- {1:F5}, closed form {2:F5}; beam {3:F5} / {4:F5}; " +
                              "between two points {5:F5} / {6:F5}", estimate, error, exact, through / count, throughExact / count, shadow / count, shadowExact / count);
            EXPECT_LT(Math.Abs(estimate - exact), 4 * error + 1e-5);
            EXPECT_LT(Math.Abs(through - throughExact) / count, 1e-5);
            EXPECT_LT(Math.Abs(shadow - shadowExact) / count, 1e-5);
        }

        // The course's listing 2
        [TEST("ProductionVolume", "Empty")]
        public static void Empty()
        {
            HomogeneousMedium medium = Medium(new RGB(0.2f, 0.5f, 1.5f), new RGB(0.5f, 1.5f, 3f), VolumeScattering.None);
            VolumeContext ctx = new VolumeContext();
            ctx.lambda = SampledWavelengths.SampleVisible(0.3f);
            ctx.P = new Point3f(0, 0, 0);
            Slab rs = new Slab(5);
            SampledSpectrum L, T, w;
            Point3f P;
            Ray wo;
            VolumeGeometry g;
            Volume volume = Volume.Create(ctx, medium);
            EXPECT_TRUE(volume is EmptyVolume);
            EXPECT_TRUE(volume.Integrate(rs, new Ray(ctx.P, new Vector3f(0, 0, -1), 0), out L, out T, out w, out P, out wo, out g));
            EXPECT_TRUE(g.isSurface);
            EXPECT_LT(Math.Abs(P.z + 1), 1e-6);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                EXPECT_EQ(1, T[i]);
                EXPECT_EQ(1, w[i]);
                EXPECT_EQ(0, L[i]);
                EXPECT_EQ(1, volume.Transmittance(rs, ctx.P, P)[i]);
            }
        }

        [TEST("ProductionVolume", "MultipleScattering")]
        public static void MultipleScattering()
        {
            HomogeneousMedium medium = Medium(new RGB(0.1f, 0.1f, 0.1f), new RGB(0.9f, 0.9f, 0.9f), VolumeScattering.Multiple);
            Slab rs = new Slab(3);
            foreach (Float mu0 in new Float[] { 0.9f, 0.5f })
            {
                double once = 0, every = 0, squares = 0;
                int count = 300000;
                for (int s = 0; s < count; ++s)
                {
                    VolumeContext ctx = new VolumeContext();
                    ctx.lambda = SampledWavelengths.SampleVisible(rs.rng.Uniform<Float>());
                    ctx.P = new Point3f(0, 0, 0);
                    Vector3f d = new Vector3f(SafeSqrt(1 - mu0 * mu0), 0, -mu0);
                    SampledSpectrum beta = new SampledSpectrum(1f);
                    int scatterings = 0;
                    double value = 0;
                    for (int step = 0; step < 4000; ++step)
                    {
                        SampledSpectrum L, T, w;
                        Point3f P;
                        Ray wo;
                        VolumeGeometry g;
                        bool hit = Volume.Create(ctx, medium).Integrate(rs, new Ray(ctx.P, d, 0), out L, out T, out w, out P, out wo, out g);
                        beta *= w * T;
                        if (!hit || !(bool)beta)
                        {
                            break;
                        }
                        if (g.isSurface)
                        {
                            if (d.z > 0)
                            {
                                value = beta[0] / ctx.r.Average();
                            }
                            break;
                        }
                        ++scatterings;
                        PhaseFunctionSample ps = g.phase.Sample_p(-d, new Point2f(rs.rng.Uniform<Float>(), rs.rng.Uniform<Float>())).value();
                        beta *= ps.p / ps.pdf;
                        d = ps.wi;
                        ctx.P = P;
                    }
                    every += value;
                    squares += value * value;
                    if (scatterings == 1)
                    {
                        once += value;
                    }
                }
                every /= count;
                once /= count;
                double error = Math.Sqrt(Math.Max(0, squares / count - every * every) / count);
                // (the coefficients as colours are 0.1 and 0.9 to the precision of the RGB to spectrum conversion)
                MediumProperties mp = medium.SamplePoint(new Point3f(0, 0, 0), SampledWavelengths.SampleVisible(0.5f));
                double tau = mp.sigma_a[0] + mp.sigma_s[0], albedo = mp.sigma_s[0] / tau, single = 0;
                for (int i = 0; i < 200000; ++i)
                {
                    double m = (i + 0.5) / 200000;
                    single += 0.5 * albedo * m / (mu0 + m) * (1 - Math.Exp(-tau * (1 / mu0 + 1 / m))) / 200000;
                }
                double layered = mu0 == 0.9f ? 0.28695 : 0.39451;
                Console.WriteLine("MEASURE production volume, slab of optical thickness {0:F3} and albedo {1:F3}, cosine {2}: one scattering {3:F5} (Chandrasekhar {4:F5}), " +
                                  "every scattering {5:F5} +- {6:F5} (the layered walk {7:F5})", tau, albedo, mu0, once, single, every, error, layered);
                EXPECT_LT(Math.Abs(once - single), 4 * error + 5e-4);
                EXPECT_LT(Math.Abs(every - layered), 4 * error + 4e-3);
            }
        }
    }
}
