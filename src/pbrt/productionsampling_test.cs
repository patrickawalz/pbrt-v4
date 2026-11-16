// (not in pbrt-v4) productionsampling_test.cs
//
// The sampling of section 4 of Fong et al., "Production Volume Rendering", in the volumes of productionvolume.cs:
//   - the equiangular density: it integrates to one, and its samples have it (light off the ray, on its line, ray without end);
//   - a point light in a fog: the three single scattering volumes (homogeneous, marched, tracked), each with the density of
//     the transmittance, the equiangular density and both, against the quadrature of the integral; the equiangular
//     density has the smaller deviation; several points lit along one interval;
//   - a light at infinity (no point to take the density about): tracking with the uniform density (the course's listing 18,
//     corrected), against the closed form of the slab;
//   - the light at the point of the scattering sampled from the lights and from the phase function, against the
//     quadrature, under a sky and a forward phase function;
//   - Volume.Emission (section 3.8) against its closed form, and the contrast control of section 3.7;
//   - the unbiased ray-marching transmittance estimator of Kettunen et al. 2021 ("transmittance" "raymarching") against the
//     quadrature of the optical depth through a grid that varies, next to ratio tracking, and in the tracked volume's
//     equiangular point;
//   - Kutz et al. 2017 side by side with what was there: delta tracking with the wavelength that samples for the others,
//     decomposition tracking ("tracker" "decomposition"), spectral tracking ("spectral" "tracking"), on the same walks
//     through a coloured grid: what leaves unscattered against exp(-tau), what leaves at all, the deviation, the lookups.

using System;
using testing;

namespace pbrt
{
    public static unsafe class ProductionSamplingTests
    {
        // A point light of unit intensity in a medium whose extinction is known; the ray ends at a wall at a distance
        // (the random numbers that a volume asks of the services are not those of the generator that it steps with: a volume
        // copies that generator before it asks, and would get its first number twice)
        abstract class Known : VolumeServices
        {
            public SampledSpectrum sigma_t;
            protected RNG own;

            public override Float GenerateRandomNumber() { return own.Uniform<Float>(); }
        }

        sealed class Fog : Known
        {
            public Point3f light;
            public Float distance;
            public bool givesPoint = true;

            public Fog(ulong seed)
            {
                rng = new RNG(seed);
                own = new RNG(seed + 1000);
            }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                distance = this.distance;
                P = ray.Apply(Float.IsInfinity(distance) ? 0 : distance);
                return !Float.IsInfinity(distance);
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                Vector3f to = light - ctx.P;
                Float rho = Length(to);
                wi = to / rho;
                L = new SampledSpectrum(1 / (rho * rho));
                beamR = new SampledSpectrum(1f);
                beamTransmittance = Exp(-sigma_t * rho);
                return true;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                Vector3f to = light - ctx.P;
                Float rho = Length(to);
                wi = to / rho;
                L = new SampledSpectrum(1 / (rho * rho));
                return true;
            }

            public override bool ChooseLight(VolumeContext ctx, out VolumeLight chosen)
            {
                chosen = new VolumeLight();
                chosen.probability = 1;
                chosen.hasPoint = givesPoint;
                chosen.fixedPoint = givesPoint;
                chosen.point = light;
                return true;
            }

            // (the fog is everywhere and nothing is in it)
            public override bool Connect(VolumeContext ctx, MediumInteraction from, Point3f to, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR,
                                         out VolumePlace place)
            {
                place = new VolumePlace();
                place.medium = ctx.medium;
                beamR = new SampledSpectrum(1f);
                beamTransmittance = Exp(-sigma_t * Distance(from.p(), to));
                return true;
            }

            public override bool EvaluateLight(VolumeContext ctx, VolumeLight chosen, VolumePlace place, MediumInteraction point, out Vector3f wi, out SampledSpectrum L,
                                               out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                Vector3f to = light - point.p();
                Float rho = Length(to);
                wi = to / rho;
                L = new SampledSpectrum(1 / (rho * rho));
                beamR = new SampledSpectrum(1f);
                beamTransmittance = Exp(-sigma_t * rho);
                return true;
            }
        }

        // A slab (z from -1 to 0) under a light at infinity or under a sky of unit radiance over the upper hemisphere
        sealed class Slab : Known
        {
            public Vector3f toLight = new Vector3f(0, 0, 1);
            public bool sky;

            public Slab(ulong seed)
            {
                rng = new RNG(seed);
                own = new RNG(seed + 1000);
            }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                distance = ray.d.z > 0 ? -ray.o.z / ray.d.z : (ray.d.z < 0 ? (-1 - ray.o.z) / ray.d.z : Float.PositiveInfinity);
                P = ray.Apply(distance);
                return !Float.IsInfinity(distance);
            }

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

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L, out Float lightPdf,
                                                     out bool delta, out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                if (!sky)
                {
                    lightPdf = 0;
                    delta = true;
                    return GenerateLightSample(ctx, point, null, out wi, out L, out beamTransmittance, out beamR);
                }
                // the sky, by a direction uniform over the sphere
                wi = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                lightPdf = UniformSpherePDF();
                delta = false;
                L = new SampledSpectrum(1 / lightPdf);
                beamR = new SampledSpectrum(1f);
                beamTransmittance = new SampledSpectrum(0f);
                if (!(wi.z > 0))
                {
                    return false;
                }
                beamTransmittance = Exp(-sigma_t * (-ctx.P.z / wi.z));
                return true;
            }

            public override bool EvaluateLightSample(VolumeContext ctx, MediumInteraction point, Vector3f wi, Float scatterPdf, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR, out SampledSpectrum emitted)
            {
                L = new SampledSpectrum(0f);
                beamR = new SampledSpectrum(1f);
                emitted = new SampledSpectrum(0f);
                beamTransmittance = new SampledSpectrum(0f);
                if (!sky || !(wi.z > 0))
                {
                    return false;
                }
                L = new SampledSpectrum(PowerHeuristic(1, scatterPdf, 1, UniformSpherePDF()));
                beamTransmittance = Exp(-sigma_t * (-ctx.P.z / wi.z));
                return true;
            }
        }

        static VolumeIntegrator Settings(VolumeScattering kind, VolumeDistanceSampling sampling)
        {
            VolumeIntegrator integrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Default.transmittance);
            integrator.scattering = kind;
            integrator.distanceSampling = sampling;
            return integrator;
        }

        static HomogeneousMedium Homogeneous(RGB absorption, RGB scattering, Float g, Float emission)
        {
            return new HomogeneousMedium(new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), 1,
                                         new ConstantSpectrum(emission), 1, g, new Allocator());
        }

        // The same coefficients as a grid of one density: a medium that is tracked. The box is far larger than what the tests
        // look at (a grid falls to nothing over the half cell at its faces), and the rays end where the services say.
        static GridMedium Grid(RGB absorption, RGB scattering, Float emission)
        {
            return Grid(absorption, scattering, emission, 0);
        }

        static GridMedium Grid(RGB absorption, RGB scattering, Float emission, Float g)
        {
            Allocator alloc = new Allocator();
            Float[] density = new Float[512], scale = new Float[512];
            for (int i = 0; i < 512; ++i)
            {
                density[i] = 1;
                scale[i] = emission > 0 ? 1 : 0;
            }
            return new GridMedium(new Bounds3f(new Point3f(-40, -40, -40), new Point3f(40, 40, 40)), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption),
                                  new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), 1, g, new SampledGrid<Float>(density, 8, 8, 8, alloc), default, 1, 0,
                                  new ConstantSpectrum(emission), new SampledGrid<Float>(scale, 8, 8, 8, alloc), alloc);
        }

        // The wavelengths of a path in their four orders: each samples in its turn, as over the paths of a render (what one
        // wavelength samples is weighed against what the others would have: the estimate is that of the four together)
        static SampledWavelengths[] Orders(Float u)
        {
            SampledWavelengths[] orders = new SampledWavelengths[NSpectrumSamples];
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                Float shifted = u + (Float)i / NSpectrumSamples;
                orders[i] = SampledWavelengths.SampleVisible(shifted - std.floor(shifted));
            }
            return orders;
        }

        static void Measure(Medium medium, Known rs, SampledWavelengths[] orders, Ray ray, int count, out double mean, out double error, out double deviation)
        {
            double sum = 0, squares = 0;
            for (int s = 0; s < count; ++s)
            {
                VolumeContext ctx = new VolumeContext();
                ctx.lambda = orders[s % orders.Length];
                MediumProperties here = medium.SamplePoint(ray.o, ctx.lambda);
                rs.sigma_t = here.sigma_a + here.sigma_s;
                ctx.P = ray.o;
                ctx.medium = medium;
                SampledSpectrum L, T, w;
                Point3f P;
                Ray wo;
                VolumeGeometry g;
                Volume.Create(ctx, medium).Integrate(rs, ray, out L, out T, out w, out P, out wo, out g);
                double value = L.Average();
                sum += value;
                squares += value * value;
            }
            mean = sum / count;
            deviation = Math.Sqrt(Math.Max(0, squares / count - mean * mean));
            error = deviation / Math.Sqrt(count);
        }

        [TEST("ProductionSampling", "EquiangularDensity")]
        public static void EquiangularDensityIntegrates()
        {
            RNG rng = new RNG(5);
            Ray ray = new Ray(new Point3f(0.2f, -0.1f, 0.3f), Normalize(new Vector3f(1, 0.4f, -0.2f)), 0);
            // (pivot, tMin, tMax): off the ray, far off, behind the start, on the line beyond the end, on the line behind, no end
            Point3f beyond = ray.Apply(9), behind = ray.Apply(-2);
            Point3f[] pivots = { new Point3f(2, 1, 0), new Point3f(1, 30, 4), new Point3f(-3, 0.5f, 0.2f), beyond, behind, new Point3f(2, 1, 0), new Point3f(1.5f, 0.501f, 0.04f) };
            Float[] tMins = { 0, 0, 0, 1, 0.5f, 0, 0 };
            Float[] tMaxs = { 6, 6, 6, 7, 6, Float.PositiveInfinity, 6 };
            for (int c = 0; c < pivots.Length; ++c)
            {
                EquiangularDensity density = EquiangularDensity.Create(ray, tMins[c], tMaxs[c], pivots[c]);
                EXPECT_TRUE(density.valid);
                // the integral of the density, and the mean of f / pdf over its samples for f = exp(-t) against the integral of f
                double upper = Float.IsInfinity(tMaxs[c]) ? 4000 : tMaxs[c], integral = 0;
                int steps = 4000000;
                for (int i = 0; i < steps; ++i)
                {
                    double t = tMins[c] + (i + 0.5) * (upper - tMins[c]) / steps;
                    integral += density.PDF((Float)t) * (upper - tMins[c]) / steps;
                }
                double estimate = 0, squares = 0, inside = 0;
                int count = 400000;
                for (int s = 0; s < count; ++s)
                {
                    Float t = density.Sample(rng.Uniform<Float>());
                    if (t >= tMins[c] && t <= tMaxs[c])
                    {
                        inside += 1;
                    }
                    Float pdf = density.PDF(t);
                    double value = pdf > 0 ? Math.Exp(-t) / pdf : 0;
                    estimate += value;
                    squares += value * value;
                }
                estimate /= count;
                double error = Math.Sqrt(Math.Max(0, squares / count - estimate * estimate) / count);
                double exact = Math.Exp(-tMins[c]) - (Float.IsInfinity(tMaxs[c]) ? 0 : Math.Exp(-tMaxs[c]));
                Console.WriteLine("MEASURE equiangular density, case {0}: integral {1:F5}, samples inside {2:F5}, int exp(-t) {3:F5} +- {4:F5} (exact {5:F5})", c,
                                  integral, inside / count, estimate, error, exact);
                EXPECT_LT(Math.Abs(integral - 1), Float.IsInfinity(tMaxs[c]) ? 2e-3 : 2e-4);
                EXPECT_GT(inside / count, 0.9999);
                EXPECT_LT(Math.Abs(estimate - exact), 4 * error + 1e-5);
            }
            // a light inside the segment, on the ray: no density
            EXPECT_FALSE(EquiangularDensity.Create(ray, 0, 6, ray.Apply(3)).valid);
        }

        [TEST("ProductionSampling", "PointLightInFog")]
        public static void PointLightInFog()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
            SampledWavelengths[] template = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            Fog rs = new Fog(21);
            rs.light = new Point3f(2, 0.3f, 0);
            rs.distance = 6;
            HomogeneousMedium reference = Homogeneous(absorption, scattering, 0, 0);
            MediumProperties mp = reference.SamplePoint(ray.o, template[0]);
            rs.sigma_t = mp.sigma_a + mp.sigma_s;
            SampledSpectrum sigma_t = rs.sigma_t;
            // The integral over the ray of sigma_s exp(-sigma t) / (4 pi) exp(-sigma rho) / rho^2, the mean over the wavelengths
            double exact = 0;
            int steps = 2000000;
            for (int i = 0; i < steps; ++i)
            {
                double t = (i + 0.5) * rs.distance / steps, rho = Math.Sqrt(0.09 + (t - 2) * (t - 2));
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    exact += mp.sigma_s[c] * Math.Exp(-sigma_t[c] * (t + rho)) / (4 * Math.PI * rho * rho) * rs.distance / steps / NSpectrumSamples;
                }
            }
            string[] kinds = { "homogeneous", "marched", "tracked" };
            VolumeDistanceSampling[] modes = { VolumeDistanceSampling.Density, VolumeDistanceSampling.Equiangular, VolumeDistanceSampling.MIS };
            for (int kind = 0; kind < 3; ++kind)
            {
                double[] deviations = new double[3];
                for (int mode = 0; mode < 3; ++mode)
                {
                    Medium medium;
                    if (kind == 2)
                    {
                        medium = Grid(absorption, scattering, 0);
                    }
                    else
                    {
                        medium = Homogeneous(absorption, scattering, 0, 0);
                    }
                    medium.volumeIntegrator = Settings(kind == 1 ? VolumeScattering.SinglePDF : VolumeScattering.Single, modes[mode]);
                    medium.volumeIntegrator.stepSize = 0.02f;
                    double mean, error;
                    Measure(medium, rs, template, ray, kind == 1 ? 30000 : 300000, out mean, out error, out deviations[mode]);
                    Console.WriteLine("MEASURE point light in fog, {0}, {1}: {2:F6} +- {3:F6} (quadrature {4:F6}), deviation of one sample {5:F4}", kinds[kind], modes[mode],
                                      mean, error, exact, deviations[mode]);
                    // (the march lights the middle of a step for its density and is exact for the transmittance here)
                    EXPECT_LT(Math.Abs(mean - exact), 4 * error + 1e-5);
                }
                if (kind == 0)
                {
                    EXPECT_LT(deviations[1], 0.5 * deviations[0]);
                    EXPECT_LT(deviations[2], 0.5 * deviations[0]);
                }
                else if (kind == 2)
                {
                    // (the transmittance of the equiangular point is itself an estimate here, and with a majorant as tight
                    // as this grid's, ratio tracking gives nothing or everything: the gain is what is left of it)
                    EXPECT_LT(deviations[1], deviations[0]);
                    EXPECT_LT(deviations[2], deviations[0]);
                }
            }

            // Several points lit along one interval (section 4.1): the same mean, the deviation of the mean of as many
            foreach (int kind in new int[] { 0, 2 })
            {
                Medium medium = kind == 2 ? Grid(absorption, scattering, 0) : Homogeneous(absorption, scattering, 0, 0);
                medium.volumeIntegrator = Settings(VolumeScattering.Single, VolumeDistanceSampling.MIS);
                double one, oneError, oneDeviation, many, manyError, manyDeviation;
                Measure(medium, rs, template, ray, 100000, out one, out oneError, out oneDeviation);
                medium.volumeIntegrator.lightSamples = 8;
                Measure(medium, rs, template, ray, 100000, out many, out manyError, out manyDeviation);
                Console.WriteLine("MEASURE point light in fog, {0}, 8 points lit: {1:F6} +- {2:F6} (quadrature {3:F6}), deviation {4:F4} against {5:F4} with one", kinds[kind],
                                  many, manyError, exact, manyDeviation, oneDeviation);
                EXPECT_LT(Math.Abs(many - exact), 4 * manyError + 1e-5);
                EXPECT_LT(manyDeviation, 0.5 * oneDeviation);
            }

            // A ray that does not end, in the homogeneous volume
            {
                rs.distance = Float.PositiveInfinity;
                double unbounded = 0;
                for (int i = 0; i < 4 * steps; ++i)
                {
                    double t = (i + 0.5) * 200.0 / (4 * steps), rho = Math.Sqrt(0.09 + (t - 2) * (t - 2));
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        unbounded += mp.sigma_s[c] * Math.Exp(-sigma_t[c] * (t + rho)) / (4 * Math.PI * rho * rho) * 200.0 / (4 * steps) / NSpectrumSamples;
                    }
                }
                for (int mode = 0; mode < 3; ++mode)
                {
                    HomogeneousMedium medium = Homogeneous(absorption, scattering, 0, 0);
                    medium.volumeIntegrator = Settings(VolumeScattering.Single, modes[mode]);
                    double mean, error, deviation;
                    Measure(medium, rs, template, ray, 300000, out mean, out error, out deviation);
                    Console.WriteLine("MEASURE point light in fog, ray without end, {0}: {1:F6} +- {2:F6} (quadrature {3:F6}), deviation {4:F4}", modes[mode], mean, error,
                                      unbounded, deviation);
                    EXPECT_LT(Math.Abs(mean - unbounded), 4 * error + 1e-5);
                }
            }
        }

        // No point to take the equiangular density about: the tracked volume weighs its collision against a point uniform over
        // the segment (the course's listing 18). The slab of productionvolume_test.cs and its closed form.
        [TEST("ProductionSampling", "UniformAndTracking")]
        public static void UniformAndTracking()
        {
            RGB absorption = new RGB(0.2f, 0.5f, 1.5f), scattering = new RGB(0.5f, 1.5f, 3f);
            SampledWavelengths[] template = Orders(0.61f);
            Slab rs = new Slab(31);
            rs.toLight = Normalize(new Vector3f(0.3f, 0.2f, 0.7f));
            Float mu = 0.6f, mu0 = rs.toLight.z;
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(SafeSqrt(1 - mu * mu), 0, -mu), 0);
            foreach (Float scale in new Float[] { 1, 0.05f })
            {
                RGB a = new RGB(absorption.r * scale, absorption.g * scale, absorption.b * scale), sc = new RGB(scattering.r * scale, scattering.g * scale, scattering.b * scale);
                double[] deviations = new double[2];
                for (int mode = 0; mode < 2; ++mode)
                {
                    GridMedium medium = Grid(a, sc, 0);
                    medium.volumeIntegrator = Settings(VolumeScattering.Single, mode == 0 ? VolumeDistanceSampling.Density : VolumeDistanceSampling.MIS);
                    MediumProperties mp = medium.SamplePoint(new Point3f(0, 0, -0.5f), template[0]);
                    SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
                    double exact = 0;
                    for (int i = 0; i < NSpectrumSamples; ++i)
                    {
                        double st = sigma_t[i], k = st * (1 + mu / mu0);
                        exact += mp.sigma_s[i] / (4 * Math.PI) * (1 - Math.Exp(-k / mu)) / k / NSpectrumSamples;
                    }
                    double mean, error;
                    Measure(medium, rs, template, ray, 400000, out mean, out error, out deviations[mode]);
                    Console.WriteLine("MEASURE slab under a light at infinity, tracked, extinction x {0}, {1}: {2:F6} +- {3:F6} (closed form {4:F6}), deviation {5:F5}", scale,
                                      mode == 0 ? "tracking alone" : "tracking and uniform", mean, error, exact, deviations[mode]);
                    EXPECT_LT(Math.Abs(mean - exact), 4 * error + 1e-6);
                }
                if (scale < 1)
                {
                    // (a thin volume: tracking alone seldom collides)
                    EXPECT_LT(deviations[1], 0.5 * deviations[0]);
                }
            }
        }

        // The light at the point of the scattering, from the lights alone and from the lights and the phase function
        [TEST("ProductionSampling", "LightAndPhase")]
        public static void LightAndPhase()
        {
            RGB absorption = new RGB(0.2f, 0.3f, 0.5f), scattering = new RGB(0.8f, 1.2f, 2f);
            Float gHG = 0.85f;
            SampledWavelengths[] template = Orders(0.23f);
            Slab rs = new Slab(41);
            rs.sky = true;
            // (looking up through the slab from its lower face: the forward lobe of the phase function is towards the sky)
            Float mu = 0.8f;
            Ray ray = new Ray(new Point3f(0, 0, -1), new Vector3f(SafeSqrt(1 - mu * mu), 0, mu), 0);
            HomogeneousMedium reference = Homogeneous(absorption, scattering, gHG, 0);
            MediumProperties mp = reference.SamplePoint(ray.o, template[0]);
            SampledSpectrum sigma_t = mp.sigma_a + mp.sigma_s;
            // int_0^d sigma_s exp(-sigma t) int_{upper hemisphere} p(wo, wi) exp(-sigma depth / cos) dwi dt
            double exact = 0;
            int nt = 300, nc = 300, np = 300;
            double length = 1 / mu;
            Vector3f wo = -ray.d;
            for (int ic = 0; ic < nc; ++ic)
            {
                double cos = (ic + 0.5) / nc, sin = Math.Sqrt(1 - cos * cos);
                for (int ip = 0; ip < np; ++ip)
                {
                    double phi = 2 * Math.PI * (ip + 0.5) / np;
                    Vector3f wi = new Vector3f((Float)(sin * Math.Cos(phi)), (Float)(sin * Math.Sin(phi)), (Float)cos);
                    double p = mp.phase.p(wo, wi) * 2 * Math.PI / (nc * np);
                    for (int it = 0; it < nt; ++it)
                    {
                        double t = (it + 0.5) * length / nt, depth = 1 - t * mu;
                        for (int c = 0; c < NSpectrumSamples; ++c)
                        {
                            exact += mp.sigma_s[c] * Math.Exp(-sigma_t[c] * (t + depth / cos)) * p * length / nt / NSpectrumSamples;
                        }
                    }
                }
            }
            double[] deviations = new double[2];
            for (int mode = 0; mode < 2; ++mode)
            {
                HomogeneousMedium medium = Homogeneous(absorption, scattering, gHG, 0);
                medium.volumeIntegrator = Settings(VolumeScattering.Single, VolumeDistanceSampling.Density);
                medium.volumeIntegrator.phaseSampling = mode == 1;
                double mean, error;
                Measure(medium, rs, template, ray, 400000, out mean, out error, out deviations[mode]);
                Console.WriteLine("MEASURE slab under a sky, phase function of g {0}, {1}: {2:F5} +- {3:F5} (quadrature {4:F5}), deviation {5:F4}", gHG,
                                  mode == 0 ? "lights sampled" : "lights and phase function sampled", mean, error, exact, deviations[mode]);
                EXPECT_LT(Math.Abs(mean - exact), 4 * error + 2e-4 * exact);
            }
            EXPECT_LT(deviations[1], deviations[0]);
        }

        // A ball of density that falls off from its centre, with some variation, in the box from -1 to 1
        static GridMedium Ball(RGB absorption, RGB scattering, Float scale)
        {
            int n = 32;
            Float[] density = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n - 0.5f, fz = (z + 0.5f) / n - 0.5f;
                        Float radius = std.sqrt(fx * fx + fy * fy + fz * fz);
                        density[x + n * (y + n * z)] = std.max<Float>(0, 1 - 2.2f * radius) * (0.75f + 0.25f * std.sin(0.3f + 19 * fx + 13 * fy * fz));
                    }
                }
            }
            Allocator alloc = new Allocator();
            return new GridMedium(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption),
                                  new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), scale, 0, new SampledGrid<Float>(density, n, n, n, alloc), default, 1, 0,
                                  new ConstantSpectrum(0f), new SampledGrid<Float>(new Float[] { 0 }, 1, 1, 1, alloc), alloc);
        }

        // Kettunen et al. 2021 as the estimator of the transmittance between two points of a volume: every wavelength against
        // exp(-tau), tau by quadrature; ratio tracking beside it (its first wavelength, the one that tracks)
        [TEST("ProductionSampling", "RayMarchingTransmittance")]
        public static void RayMarchingTransmittance()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            Fog rs = new Fog(61);
            Point3f[] from = { new Point3f(-1.4f, 0.1f, 0.05f), new Point3f(-0.9f, -0.8f, -0.7f), new Point3f(0.05f, 0.02f, -0.03f) };
            Point3f[] to = { new Point3f(1.4f, -0.2f, 0.1f), new Point3f(0.8f, 0.9f, 0.6f), new Point3f(0.7f, 0.5f, 0.3f) };
            foreach (Float scale in new Float[] { 2, 12, 60 })
            {
                for (int c = 0; c < from.Length; ++c)
                {
                    GridMedium marched = Ball(new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), scale);
                    marched.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.RayMarching());
                    marched.volumeIntegrator.scattering = VolumeScattering.Single;
                    GridMedium tracked = Ball(new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), scale);
                    tracked.volumeIntegrator = Settings(VolumeScattering.Single, VolumeDistanceSampling.Density);
                    Float d = Distance(from[c], to[c]);
                    Vector3f direction = (to[c] - from[c]) / d;
                    double[] tau = new double[NSpectrumSamples];
                    double tauMajorant = 0;
                    int steps = 200000;
                    for (int i = 0; i < steps; ++i)
                    {
                        MediumProperties mp = marched.SamplePoint(from[c] + (Float)((i + 0.5) * d / steps) * direction, lambda);
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            tau[k] += ((double)mp.sigma_a[k] + mp.sigma_s[k]) * d / steps;
                        }
                    }
                    RayMajorantIterator iter = marched.SampleRay(new Ray(from[c], direction, 0), d, lambda, new ScratchBuffer());
                    while (true)
                    {
                        pstd.optional<RayMajorantSegment> seg = iter.Next();
                        if (!seg)
                        {
                            break;
                        }
                        tauMajorant += (double)seg.value().sigma_maj.MaxComponentValue() * (seg.value().tMax - seg.value().tMin);
                    }
                    int count = 40000;
                    double[] mean = new double[NSpectrumSamples], squares = new double[NSpectrumSamples];
                    double ratioMean = 0, ratioSquares = 0, lowest = 1, highest = 0;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.P = from[c];
                        ctx.medium = marched;
                        SampledSpectrum T = Volume.Create(ctx, marched).Transmittance(rs, from[c], to[c]);
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            mean[k] += T[k];
                            squares[k] += (double)T[k] * T[k];
                            lowest = Math.Min(lowest, T[k]);
                            highest = Math.Max(highest, T[k]);
                            // (the densities of the path are left alone)
                            EXPECT_EQ(1, ctx.r[k]);
                        }
                        ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.P = from[c];
                        ctx.medium = tracked;
                        double ratio = Volume.Create(ctx, tracked).Transmittance(rs, from[c], to[c])[0];
                        ratioMean += ratio;
                        ratioSquares += ratio * ratio;
                    }
                    ratioMean /= count;
                    double ratioDeviation = Math.Sqrt(Math.Max(0, ratioSquares / count - ratioMean * ratioMean));
                    int M = RayMarchingEstimator.TupleSize(tauMajorant);
                    double deviation0 = 0;
                    for (int k = 0; k < NSpectrumSamples; ++k)
                    {
                        mean[k] /= count;
                        double deviation = Math.Sqrt(Math.Max(0, squares[k] / count - mean[k] * mean[k])), exact = Math.Exp(-tau[k]);
                        if (k == 0)
                        {
                            deviation0 = deviation;
                        }
                        EXPECT_LT(Math.Abs(mean[k] - exact), 4 * deviation / Math.Sqrt(count) + 1e-6);
                    }
                    Console.WriteLine("MEASURE ray-marching transmittance, extinction x {0}, ray {1}: tau {2:F3} (majorant {3:F2}), exp(-tau) {4:F6}; marched {5:F6}, deviation {6:F6}, " +
                                      "{7} lookups a comb, {8:F1} lookups expected, range [{9:F4}, {10:F4}]; ratio tracking {11:F6}, deviation {12:F6}, about {13:F1} lookups",
                                      scale, c, tau[0], tauMajorant, Math.Exp(-tau[0]), mean[0], deviation0, M, 1.31945 * M + (M >= 6 ? 2 : 0), lowest, highest, ratioMean,
                                      ratioDeviation, tauMajorant);
                    EXPECT_LT(Math.Abs(ratioMean - Math.Exp(-tau[0])), 4 * ratioDeviation / Math.Sqrt(count) + 1e-6);
                    if (Math.Exp(-tau[0]) > 1e-3)
                    {
                        EXPECT_LT(deviation0, 0.1 * ratioDeviation);
                    }
                }
            }

            // The equiangular point of the tracked volume takes its transmittance from this estimator: the point light in the
            // fog of PointLightInFog (a grid of one density, where an estimate of the optical depth is exact)
            {
                RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
                SampledWavelengths[] orders = Orders(0.37f);
                Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
                Fog fog = new Fog(71);
                fog.light = new Point3f(2, 0.3f, 0);
                fog.distance = 6;
                foreach (bool marching in new bool[] { false, true })
                {
                    GridMedium medium = Grid(absorption, scattering, 0);
                    medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, marching ? VolumeIntegrator.RayMarching() : VolumeIntegrator.Default.transmittance);
                    medium.volumeIntegrator.scattering = VolumeScattering.Single;
                    medium.volumeIntegrator.distanceSampling = VolumeDistanceSampling.Equiangular;
                    double mean, error, deviation;
                    Measure(medium, fog, orders, ray, 300000, out mean, out error, out deviation);
                    Console.WriteLine("MEASURE point light in fog, tracked, equiangular, transmittance by {0}: {1:F6} +- {2:F6} (quadrature 0.078532), deviation of one sample {3:F4}",
                                      marching ? "ray marching" : "ratio tracking", mean, error, deviation);
                    EXPECT_LT(Math.Abs(mean - 0.078532), 4 * error + 2e-5);
                    if (marching)
                    {
                        EXPECT_LT(deviation, 0.03);
                    }
                }
            }
        }

        // A sphere around the origin that the rays end at, seen from inside
        sealed class Shell : Known
        {
            public Float radius;

            public Shell(ulong seed)
            {
                rng = new RNG(seed);
                own = new RNG(seed + 1000);
            }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                Vector3f o = (Vector3f)ray.o;
                Float b = Dot(o, ray.d), c = Dot(o, o) - radius * radius;
                distance = -b + SafeSqrt(b * b - c);
                P = ray.Apply(distance);
                return true;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, PhaseFunction phase, out Vector3f wi, out SampledSpectrum L,
                                                     out SampledSpectrum beamTransmittance, out SampledSpectrum beamR)
            {
                wi = new Vector3f(0, 0, 1);
                L = new SampledSpectrum(0f);
                beamTransmittance = new SampledSpectrum(1f);
                beamR = new SampledSpectrum(1f);
                return false;
            }

            public override bool GenerateLightSample(VolumeContext ctx, MediumInteraction point, out Vector3f wi, out SampledSpectrum L)
            {
                wi = new Vector3f(0, 0, 1);
                L = new SampledSpectrum(0f);
                return false;
            }
        }

        // A grid in the box from -1 to 1: a floor of density everywhere with a denser ball on it (`floor` 0: the ball of
        // RayMarchingTransmittance alone, thin towards its rim)
        static GridMedium Smoke(RGB absorption, RGB scattering, Float scale, Float floor)
        {
            return Smoke(absorption, scattering, scale, floor, new Vector3f(0, 0, 0));
        }

        static GridMedium Smoke(RGB absorption, RGB scattering, Float scale, Float floor, Vector3f offset)
        {
            int n = 32;
            Float[] density = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n - 0.5f, fz = (z + 0.5f) / n - 0.5f;
                        Float radius = std.sqrt(fx * fx + fy * fy + fz * fz);
                        density[x + n * (y + n * z)] = floor + (1 - floor) * std.max<Float>(0, 1 - 2.2f * radius) * (0.75f + 0.25f * std.sin(0.3f + 19 * fx + 13 * fy * fz));
                    }
                }
            }
            Allocator alloc = new Allocator();
            return new GridMedium(new Bounds3f(new Point3f(-1, -1, -1) + offset, new Point3f(1, 1, 1) + offset), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption),
                                  new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), scale, 0, new SampledGrid<Float>(density, n, n, n, alloc), default, 1, 0,
                                  new ConstantSpectrum(0f), new SampledGrid<Float>(new Float[] { 0 }, 1, 1, 1, alloc), alloc);
        }

        // A smooth density for a texture medium: 0.6 + 0.5 sin(...), never below 0.1
        private class WaveTexture : FloatTexture
        {
            public override string ToString() { return "[ WaveTexture ]"; }
            public override Float MinValue() { return -.5f; }
            public override Float MaxValue() { return .5f; }
            public override Float Evaluate(TextureEvalContext ctx)
            {
                ++evaluations;
                return .5f * (Float)Math.Sin(4 * ctx.p.x - 2 * ctx.p.z + 3 * ctx.p.y);
            }

            // The number of evaluations, for the comparisons (the tests run on one thread)
            public static long evaluations;
        }

        // The smoke with a core that emits: the emission is where the radius is below `core`, strongest at the centre
        static GridMedium Fire(RGB absorption, RGB scattering, Float scale, Float floor, Float core, Vector3f offset)
        {
            return Fire(absorption, scattering, scale, floor, core, offset, 0);
        }

        // (`aside`: the emission is about a point that far from the centre along x, in the units of the box, where the smoke is thin)
        static GridMedium Fire(RGB absorption, RGB scattering, Float scale, Float floor, Float core, Vector3f offset, Float aside)
        {
            int n = 32;
            Float[] density = new Float[n * n * n], emission = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n - 0.5f, fz = (z + 0.5f) / n - 0.5f;
                        Float radius = std.sqrt(fx * fx + fy * fy + fz * fz);
                        density[x + n * (y + n * z)] = floor + (1 - floor) * std.max<Float>(0, 1 - 2.2f * radius) * (0.75f + 0.25f * std.sin(0.3f + 19 * fx + 13 * fy * fz));
                        Float fromCore = std.sqrt((fx - aside) * (fx - aside) + fy * fy + fz * fz);
                        emission[x + n * (y + n * z)] = std.max<Float>(0, 1 - fromCore / core);
                    }
                }
            }
            Allocator alloc = new Allocator();
            return new GridMedium(new Bounds3f(new Point3f(-1, -1, -1) + offset, new Point3f(1, 1, 1) + offset), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, absorption),
                                  new RGBUnboundedSpectrum(RGBColorSpace.sRGB, scattering), scale, 0, new SampledGrid<Float>(density, n, n, n, alloc), default, 1, 0,
                                  new ConstantSpectrum(3f), new SampledGrid<Float>(emission, n, n, n, alloc), alloc);
        }

        static void MeasureEmission(Medium medium, Known rs, SampledWavelengths[] orders, Point3f P0, Point3f P1, int count, out double mean, out double error,
                                    out double deviation, out double lookups)
        {
            double sum = 0, squares = 0;
            long before = GridMedium.lookups;
            for (int s = 0; s < count; ++s)
            {
                VolumeContext ctx = new VolumeContext();
                ctx.lambda = orders[s % orders.Length];
                ctx.P = P0;
                ctx.medium = medium;
                double value = Volume.Create(ctx, medium).Emission(rs, P0, P1).Average();
                sum += value;
                squares += value * value;
            }
            mean = sum / count;
            deviation = Math.Sqrt(Math.Max(0, squares / count - mean * mean));
            error = deviation / Math.Sqrt(count);
            lookups = (double)(GridMedium.lookups - before) / count;
        }

        // The emission between two points tracked about the mean extinction of the cells ("emission" "residual") against
        // ratio tracking, and both with the emission-aware majorant ("emissionmajorant"): the same value, by closed form in a
        // medium of one density, and against each other through smoke with a core that emits
        [TEST("ProductionSampling", "EmissionTracking")]
        public static void EmissionTracking()
        {
            RGB absorption = new RGB(0.3f, 0.6f, 1.2f), scattering = new RGB(0.5f, 1f, 2f);
            SampledWavelengths[] orders = Orders(0.44f);
            Fog rs = new Fog(77);
            {
                Point3f P0 = new Point3f(0, 0, 0), P1 = new Point3f(1.5f, 0.2f, -0.1f);
                Float d = Distance(P0, P1);
                Medium medium = Grid(absorption, scattering, 2);
                medium.volumeIntegrator = Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                medium.volumeIntegrator.emissionResidual = true;
                double exact = 0;
                foreach (SampledWavelengths lambda in orders)
                {
                    MediumProperties mp = medium.SamplePoint(P0, lambda);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        double st = mp.sigma_a[c] + mp.sigma_s[c];
                        exact += mp.sigma_a[c] * mp.Le[c] * (1 - Math.Exp(-st * d)) / st / NSpectrumSamples / orders.Length;
                    }
                }
                double mean, error, deviation, lookups;
                MeasureEmission(medium, rs, orders, P0, P1, 200000, out mean, out error, out deviation, out lookups);
                Console.WriteLine("MEASURE emission between two points, grid of one density, residual: {0:F5} +- {1:F5} (closed form {2:F5}), deviation {3:F4}", mean, error,
                                  exact, deviation);
                EXPECT_LT(Math.Abs(mean - exact), 4 * error + 2e-4 * exact);
            }
            // A thin medium whose emission is where it is thinnest: the collisions of the majorant of the density pass it by
            {
                double[] means = new double[3], errors = new double[3], deviations = new double[3], looks = new double[3];
                Float[] amounts = { 0, 1, 4 };
                for (int mode = 0; mode < 3; ++mode)
                {
                    GridMedium medium = Fire(new RGB(0.6f, 0.8f, 1f), new RGB(1f, 1f, 1f), 0.5f, 0.05f, 0.1f, new Vector3f(0, 0, 0), 0.35f);
                    medium.volumeIntegrator = Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                    if (amounts[mode] > 0)
                    {
                        medium.RaiseMajorantForEmission(amounts[mode], null);
                    }
                    MeasureEmission(medium, rs, orders, new Point3f(-1.4f, 0.02f, 0.03f), new Point3f(1.4f, 0.01f, -0.02f), 400000, out means[mode], out errors[mode],
                                    out deviations[mode], out looks[mode]);
                    Console.WriteLine("MEASURE emission of a hot place in thin smoke, emission majorant {0}: {1:F6} +- {2:F6}, deviation {3:F5}, lookups {4:F2}, " +
                                      "deviation^2 x lookups {5:F7}", amounts[mode], means[mode], errors[mode], deviations[mode], looks[mode],
                                      deviations[mode] * deviations[mode] * looks[mode]);
                }
                EXPECT_GT(means[0], 1e-5);
                for (int mode = 1; mode < 3; ++mode)
                {
                    EXPECT_LT(Math.Abs(means[mode] - means[0]), 4 * Math.Sqrt(errors[mode] * errors[mode] + errors[0] * errors[0]));
                    EXPECT_LT(deviations[mode], deviations[0]);
                }
            }
            // Through the smoke: thin (the emission is missed), dense (little gets through)
            Float[] scales = { 0.5f, 4, 16 };
            Point3f from = new Point3f(-1.4f, 0.1f, 0.05f), to = new Point3f(1.4f, -0.05f, 0.1f);
            foreach (Float scale in scales)
            {
                double[] means = new double[4], errors = new double[4], deviations = new double[4], looks = new double[4];
                string[] names = { "ratio", "residual", "ratio, emission majorant 1", "residual, emission majorant 1" };
                for (int mode = 0; mode < 4; ++mode)
                {
                    GridMedium medium = Fire(new RGB(0.6f, 0.8f, 1f), new RGB(1f, 1f, 1f), scale, 0.3f, 0.2f, new Vector3f(0, 0, 0));
                    medium.volumeIntegrator = Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                    medium.volumeIntegrator.emissionResidual = (mode & 1) != 0;
                    if (mode >= 2)
                    {
                        medium.RaiseMajorantForEmission(1, null);
                    }
                    MeasureEmission(medium, rs, orders, from, to, 400000, out means[mode], out errors[mode], out deviations[mode], out looks[mode]);
                    Console.WriteLine("MEASURE emission through fire in smoke, scale {0}, {1}: {2:F5} +- {3:F5}, deviation {4:F4}, lookups {5:F2}, deviation^2 x lookups {6:F4}",
                                      scale, names[mode], means[mode], errors[mode], deviations[mode], looks[mode], deviations[mode] * deviations[mode] * looks[mode]);
                }
                EXPECT_GT(means[0], 1e-4);
                for (int mode = 1; mode < 4; ++mode)
                {
                    EXPECT_LT(Math.Abs(means[mode] - means[0]), 4 * Math.Sqrt(errors[mode] * errors[mode] + errors[0] * errors[0]));
                }
            }
        }

        // The light of the emission of a medium (media_emission_light.cs) on the media that have one now: the density of its
        // directions integrates to one, and the emission that reaches a point from every direction is the same by directions
        // drawn towards the emission as by directions drawn uniformly
        [TEST("ProductionSampling", "EmissionLight")]
        public static void EmissionLightOnMedia()
        {
            SampledWavelengths[] orders = Orders(0.31f);
            Fog rs = new Fog(91);
            string[] names = { "grid medium", "aggregate of two grids" };
            Point3f[] origins = { new Point3f(2.5f, 0.4f, 0.3f), new Point3f(0.3f, 0.1f, -0.2f) };
            for (int shape = 0; shape < 2; ++shape)
            {
                Medium medium;
                if (shape == 0)
                {
                    medium = Fire(new RGB(0.6f, 0.8f, 1f), new RGB(1f, 1f, 1f), 2, 0.3f, 0.2f, new Vector3f(0, 0, 0));
                }
                else
                {
                    medium = new AggregateMedium(new Medium[] { Fire(new RGB(0.6f, 0.8f, 1f), new RGB(1f, 1f, 1f), 2, 0.3f, 0.2f, new Vector3f(0, 0, 0)),
                                                                Fire(new RGB(0.3f, 0.1f, 0.05f), new RGB(2f, 1f, 0.5f), 3, 0.4f, 0.12f, new Vector3f(0.5f, 0.3f, -0.2f)) }, 2, 6, null);
                }
                medium.volumeIntegrator = Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                VolumeEmissionLight light = new VolumeEmissionLight(medium, 16);
                EXPECT_TRUE(light.HasEmission());
                foreach (Point3f origin in origins)
                {
                    RNG rng = new RNG(1234);
                    int count = 200000;
                    double integral = 0, integralSquares = 0, uniformSum = 0, uniformSquares = 0, lightSum = 0, lightSquares = 0;
                    for (int s = 0; s < count; ++s)
                    {
                        Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = orders[s % orders.Length];
                        ctx.P = origin;
                        ctx.medium = medium;
                        Volume volume = Volume.Create(ctx, medium);
                        double value = volume.Emission(rs, origin, origin + 8 * w).Average();
                        uniformSum += value;
                        uniformSquares += value * value;
                        Vector3f wl;
                        Float pdf;
                        value = 0;
                        if (light.SampleDirection(origin, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), rng.Uniform<Float>(), 0, out wl, out pdf))
                        {
                            value = volume.Emission(rs, origin, origin + 8 * wl).Average() * UniformSpherePDF() / pdf;
                        }
                        lightSum += value;
                        lightSquares += value * value;
                    }
                    // (the density alone, over many more directions)
                    int directions = 4000000;
                    RNG other = new RNG(977);
                    integral = 0;
                    integralSquares = 0;
                    for (int s = 0; s < directions; ++s)
                    {
                        Vector3f w = SampleUniformSphere(new Point2f(other.Uniform<Float>(), other.Uniform<Float>()));
                        double ratio = light.PdfDirection(origin, w, 0) / UniformSpherePDF();
                        integral += ratio;
                        integralSquares += ratio * ratio;
                    }
                    integral /= directions;
                    double integralError = Math.Sqrt(Math.Max(0, integralSquares / directions - integral * integral) / directions);
                    double uniformMean = uniformSum / count, lightMean = lightSum / count;
                    double uniformDeviation = Math.Sqrt(Math.Max(0, uniformSquares / count - uniformMean * uniformMean));
                    double lightDeviation = Math.Sqrt(Math.Max(0, lightSquares / count - lightMean * lightMean));
                    Console.WriteLine("MEASURE emission light, {0}, from {1}: density of directions integrates to {2:F4} +- {9:F4}; emission by uniform directions {3:F5} +- {4:F5} " +
                                      "(deviation {5:F4}), by directions towards the emission {6:F5} +- {7:F5} (deviation {8:F4})", names[shape], origin, integral, uniformMean,
                                      uniformDeviation / Math.Sqrt(count), uniformDeviation, lightMean, lightDeviation / Math.Sqrt(count), lightDeviation, integralError);
                    EXPECT_LT(Math.Abs(integral - 1), 4 * integralError + 1e-3);
                    EXPECT_GT(uniformMean, 1e-5);
                    EXPECT_LT(Math.Abs(lightMean - uniformMean), 4 * Math.Sqrt(uniformDeviation * uniformDeviation + lightDeviation * lightDeviation) / Math.Sqrt(count));
                    EXPECT_LT(lightDeviation, uniformDeviation);
                }
            }
        }

        // A cloud for the grids: lattice noise of four octaves, cut off below a level and towards the faces of the box, so that
        // it has empty space, thin edges and a dense, uneven core
        static Float LatticeValue(int x, int y, int z)
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xffffff) / (Float)0x1000000;
        }

        static Float LatticeNoise(Float x, Float y, Float z)
        {
            int ix = (int)std.floor(x), iy = (int)std.floor(y), iz = (int)std.floor(z);
            Float fx = x - ix, fy = y - iy, fz = z - iz;
            fx = fx * fx * (3 - 2 * fx);
            fy = fy * fy * (3 - 2 * fy);
            fz = fz * fz * (3 - 2 * fz);
            Float sum = 0;
            for (int c = 0; c < 8; ++c)
            {
                int ox = c & 1, oy = (c >> 1) & 1, oz = c >> 2;
                sum += (ox == 1 ? fx : 1 - fx) * (oy == 1 ? fy : 1 - fy) * (oz == 1 ? fz : 1 - fz) * LatticeValue(ix + ox, iy + oy, iz + oz);
            }
            return sum;
        }

        static Float[] CloudDensity(int n)
        {
            return CloudDensity(n, 4);
        }

        static Float[] CloudDensity(int n, int octaves)
        {
            Float[] density = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n, fz = (z + 0.5f) / n;
                        Float noise = 0, weight = 0.5f, frequency = 3;
                        for (int octave = 0; octave < octaves; ++octave)
                        {
                            noise += weight * LatticeNoise(fx * frequency + 7.3f * octave, fy * frequency + 1.9f * octave, fz * frequency + 4.1f * octave);
                            weight *= 0.5f;
                            frequency *= 2;
                        }
                        Float radius = std.sqrt((fx - 0.5f) * (fx - 0.5f) + (fy - 0.5f) * (fy - 0.5f) + (fz - 0.5f) * (fz - 0.5f));
                        // (the octaves that are left out would have summed to about half of their weight)
                        noise += weight;
                        density[x + n * (y + n * z)] = std.min<Float>(1, 4 * std.max<Float>(0, noise - 0.38f - 1.1f * std.max<Float>(0, radius - 0.22f)));
                    }
                }
            }
            return density;
        }

        static GridMedium CloudGrid(Float[] density, int n, Float scale)
        {
            Allocator alloc = new Allocator();
            return new GridMedium(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), new Transform(), new ConstantSpectrum(0.05f), new ConstantSpectrum(1f), scale, 0,
                                  new SampledGrid<Float>((Float[])density.Clone(), n, n, n, alloc), default, 1, 0, new ConstantSpectrum(0f),
                                  new SampledGrid<Float>(new Float[] { 0 }, 1, 1, 1, alloc), alloc);
        }

        // Pictures of the two structures on one slice of a cloud, for the eye (written only where GRID_SLICES names a
        // folder): the majorant that a ray is given at each point of the slice, with a line wherever rays along x or along y
        // pass from one segment to the next
        [TEST("AdaptiveGrids", "Slices")]
        public static void GridSlices()
        {
            string folder = Environment.GetEnvironmentVariable("GRID_SLICES");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }
            int n = 64, size = 768;
            Float[] density = CloudDensity(n, 3);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            string[] names = { "grid", "adaptive" };
            for (int which = 0; which < 2; ++which)
            {
                GridMedium medium = CloudGrid(density, n, 1);
                GridMedium grid = medium;
                if (which == 1)
                {
                    grid.RefineMajorants(0.5f, 3, 2);
                    grid.adaptiveWalk = true;
                }
                Float[] value = new Float[size * size];
                bool[] edge = new bool[size * size];
                for (int axis = 0; axis < 2; ++axis)
                {
                    for (int line = 0; line < size; ++line)
                    {
                        Float across = -1 + 2 * (line + 0.5f) / size;
                        Point3f o = axis == 0 ? new Point3f(-1.5f, across, 0.013f) : new Point3f(across, -1.5f, 0.013f);
                        Vector3f d = axis == 0 ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
                        RayMajorantIterator iter = medium.SampleRay(new Ray(o, d), 3, lambda, new ScratchBuffer());
                        while (true)
                        {
                            pstd.optional<RayMajorantSegment> seg = iter.Next();
                            if (!seg)
                            {
                                break;
                            }
                            RayMajorantSegment sg = seg.value();
                            int first = (int)std.floor((sg.tMin - 0.5f) / 2 * size), last = (int)std.floor((sg.tMax - 0.5f) / 2 * size);
                            for (int i = std.max(first, 0); i <= std.min(last, size - 1); ++i)
                            {
                                int pixel = axis == 0 ? i + size * (size - 1 - line) : line + size * (size - 1 - i);
                                value[pixel] = sg.sigma_maj[0];
                                if (i == first && sg.tMax > sg.tMin)
                                {
                                    edge[pixel] = true;
                                }
                            }
                        }
                    }
                }
                using (System.IO.FileStream file = System.IO.File.Create(System.IO.Path.Combine(folder, names[which] + ".ppm")))
                {
                    byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n" + size + " " + size + "\n255\n");
                    file.Write(header, 0, header.Length);
                    byte[] pixels = new byte[3 * size * size];
                    for (int i = 0; i < size * size; ++i)
                    {
                        Float shade = std.sqrt(std.min<Float>(1, value[i] / 1.05f));
                        // (the majorant from dark blue to white; the borders of the cells in orange)
                        pixels[3 * i] = (byte)(edge[i] ? 235 : 20 + 235 * shade);
                        pixels[3 * i + 1] = (byte)(edge[i] ? 120 : 30 + 225 * shade);
                        pixels[3 * i + 2] = (byte)(edge[i] ? 20 : 60 + 195 * shade);
                    }
                    file.Write(pixels, 0, pixels.Length);
                }
            }
        }

        // The grid medium and the same with adaptive majorants ("adaptivegrid") beside each other on one cloud, with the
        // estimators of the transmittance. Per ray the transmittance is known by marching the data finely: both estimate it
        // without bias.
        [TEST("AdaptiveGrids", "Transmittance")]
        public static void GridsTransmittance()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            // (a cloud whose detail is at the size of the cells of the data, and a smooth one on a finer grid than it needs)
            for (int which = 0; which < 2; ++which)
            {
                int n = which < 1 ? 64 : 128;
                Float[] density = CloudDensity(n, which < 1 ? 4 : 2);
                Float scale = which == 0 ? 24 : 12;
                // The rays, and the transmittance of each through the data
                int rays = 200, perRay = 100;
                RNG rng = new RNG(17);
                Point3f[] from = new Point3f[rays], to = new Point3f[rays];
                double[] exact = new double[rays];
                GridMedium data = CloudGrid(density, n, scale);
                for (int r = 0; r < rays; ++r)
                {
                    Vector3f w = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    Point3f target = new Point3f(-0.6f + 1.2f * rng.Uniform<Float>(), -0.6f + 1.2f * rng.Uniform<Float>(), -0.6f + 1.2f * rng.Uniform<Float>());
                    from[r] = target - 2.5f * w;
                    to[r] = target + 2.5f * w;
                    int steps = 20000;
                    double tau = 0;
                    for (int i = 0; i < steps; ++i)
                    {
                        MediumProperties mp = data.SamplePoint(from[r] + (Float)((i + 0.5) / steps) * (to[r] - from[r]), lambda);
                        tau += ((double)mp.sigma_a[0] + mp.sigma_s[0]) * 5.0 / steps;
                    }
                    exact[r] = Math.Exp(-tau);
                }
                string[] kinds = { "grid", "grid", "adaptive", "adaptive", "adaptive" };
                string[] estimators = { "ratio", "residuallocal", "ratio", "residuallocal", "residualtracking" };
                for (int e = 0; e < kinds.Length; ++e)
                {
                    System.Diagnostics.Stopwatch build = System.Diagnostics.Stopwatch.StartNew();
                    Medium medium;
                    string built;
                    if (kinds[e] == "grid")
                    {
                        medium = CloudGrid(density, n, scale);
                        built = "16^3 majorants";
                    }
                    else
                    {
                        GridMedium grid = CloudGrid(density, n, scale);
                        grid.RefineMajorants(0.5f, 3, 2);
                        grid.adaptiveWalk = true;
                        medium = grid;
                        built = grid.adaptiveVoxels + " voxels, " + grid.adaptiveLevels + " levels";
                    }
                    double buildSeconds = build.Elapsed.TotalSeconds;
                    medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance(estimators[e]));
                    medium.volumeIntegrator.scattering = VolumeScattering.Single;
                    Fog rs = new Fog(303);
                    double variance = 0, bias = 0, sumEstimates = 0, sumExact = 0;
                    // (the first calls compile the code: not timed)
                    for (int s = 0; s < 3000; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.P = from[s % rays];
                        ctx.medium = medium;
                        Volume.Create(ctx, medium).Transmittance(rs, from[s % rays], to[s % rays]);
                    }
                    long before = GridMedium.lookups;
                    System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                    for (int r = 0; r < rays; ++r)
                    {
                        double sum = 0, squares = 0;
                        for (int s = 0; s < perRay; ++s)
                        {
                            VolumeContext ctx = new VolumeContext();
                            ctx.lambda = lambda;
                            ctx.P = from[r];
                            ctx.medium = medium;
                            double T = Volume.Create(ctx, medium).Transmittance(rs, from[r], to[r])[0];
                            sum += T;
                            squares += T * T;
                        }
                        double mean = sum / perRay;
                        variance += Math.Max(0, squares / perRay - mean * mean) / rays;
                        bias += (mean - exact[r]) * (mean - exact[r]) / rays;
                        sumEstimates += mean;
                        sumExact += exact[r];
                    }
                    double nanoseconds = watch.Elapsed.TotalMilliseconds * 1e6 / (rays * perRay);
                    double lookups = (double)(GridMedium.lookups - before) / (rays * perRay);
                    double deviation = Math.Sqrt(variance), error = Math.Sqrt(bias);
                    Console.WriteLine("MEASURE grids, cloud {0}^3 x {1}, {2,-19} {3,-16} ({4}; built in {5:F2} s): mean {6:F5} (data {7:F5}), deviation {8:F5}, " +
                                      "rms of the mean of a ray from the data {9:F5}, lookups {10:F2}, {11:F0} ns, deviation^2 x ns {12:F3}", n, scale, kinds[e], estimators[e],
                                      built, buildSeconds, sumEstimates / rays, sumExact / rays, deviation, error, lookups, nanoseconds, variance * nanoseconds);
                    // (without bias: the mean over the rays within its error)
                    EXPECT_LT(Math.Abs(sumEstimates - sumExact) / rays, 4 * deviation / Math.Sqrt((double)rays * perRay) + 2e-4);
                }
            }
        }

        // The settings of Kutz et al. 2017 that are compared
        static readonly string[] TrackerNames = { "delta, one wavelength samples", "decomposition, one wavelength samples", "spectral (average, reduced termination)",
                                                  "spectral (maximum)", "spectral with absorption events", "spectral with decomposition" };

        static void Configure(Medium medium, int mode)
        {
            medium.volumeIntegrator = new VolumeIntegrator(mode == 1 || mode == 5 ? VolumeIntegrator.Decomposition() : VolumeIntegrator.Default.tracker,
                                                           VolumeIntegrator.Default.transmittance);
            medium.volumeIntegrator.spectralTracking = mode >= 2;
            medium.volumeIntegrator.spectralMaximum = mode == 3;
            medium.volumeIntegrator.spectralAbsorption = mode == 4;
        }

        // Walks from a point into the medium, followed until they leave the shell: what leaves at all, what leaves unscattered
        static void Walks(Medium medium, Shell rs, SampledWavelengths[] orders, Point3f start, Vector3f direction, int count, out double every, out double everyError,
                          out double deviation, out double unscattered, out double unscatteredError, out double collisions, out double largest)
        {
            double sum = 0, squares = 0, unscatteredSquares = 0;
            unscattered = 0;
            collisions = 0;
            largest = 0;
            for (int s = 0; s < count; ++s)
            {
                VolumeContext ctx = new VolumeContext();
                ctx.lambda = orders[s % orders.Length];
                ctx.medium = medium;
                ctx.P = start;
                Vector3f d = direction;
                SampledSpectrum beta = new SampledSpectrum(1f);
                double value = 0;
                int scattered = 0;
                for (int step = 0; step < 100000; ++step)
                {
                    SampledSpectrum L, T, w;
                    Point3f P;
                    Ray wo;
                    VolumeGeometry g;
                    ctx.throughput = beta;
                    bool hit = Volume.Create(ctx, medium).Integrate(rs, new Ray(ctx.P, d, 0), out L, out T, out w, out P, out wo, out g);
                    beta *= w * T;
                    if (!hit || !(bool)beta)
                    {
                        break;
                    }
                    if (g.isSurface)
                    {
                        value = (beta / ctx.r.Average()).Average();
                        break;
                    }
                    ++scattered;
                    PhaseFunctionSample ps = g.phase.Sample_p(-d, new Point2f(rs.rng.Uniform<Float>(), rs.rng.Uniform<Float>())).value();
                    beta *= ps.p / ps.pdf;
                    d = ps.wi;
                    ctx.P = P;
                }
                sum += value;
                squares += value * value;
                largest = Math.Max(largest, value);
                collisions += scattered;
                if (scattered == 0)
                {
                    unscattered += value;
                    unscatteredSquares += value * value;
                }
            }
            every = sum / count;
            deviation = Math.Sqrt(Math.Max(0, squares / count - every * every));
            everyError = deviation / Math.Sqrt(count);
            unscattered /= count;
            unscatteredError = Math.Sqrt(Math.Max(0, unscatteredSquares / count - unscattered * unscattered) / count);
            collisions /= count;
        }

        // exp(-tau) along the first ray, the mean over the wavelengths, by quadrature
        static double Unscattered(Medium medium, Shell rs, SampledWavelengths lambda, Point3f start, Vector3f direction)
        {
            Point3f exit;
            Float length;
            VolumeGeometry unused;
            rs.GetNearestHit(new Ray(start, direction, 0), out exit, out length, out unused);
            double[] tau = new double[NSpectrumSamples];
            int steps = 100000;
            for (int i = 0; i < steps; ++i)
            {
                MediumProperties mp = medium.SamplePoint(start + (Float)((i + 0.5) * length / steps) * direction, lambda);
                for (int k = 0; k < NSpectrumSamples; ++k)
                {
                    tau[k] += ((double)mp.sigma_a[k] + mp.sigma_s[k]) * length / steps;
                }
            }
            double result = 0;
            for (int k = 0; k < NSpectrumSamples; ++k)
            {
                result += Math.Exp(-tau[k]) / NSpectrumSamples;
            }
            return result;
        }

        // The modes given on one medium made by `make`: every mode's unscattered part against exp(-tau), and what leaves
        // against the first mode's
        static void Compare(string what, Func<Medium> make, int[] modes, int count, out double[] lookups)
        {
            Compare(what, make, modes, -1, count, out lookups);
        }

        // (`reported`: a mode that is measured and not held to the others. The probabilities by the maximum do not follow the
        // throughput: in a strongly coloured medium a walk that leaves has a probability of the order of 2^-collisions and
        // a weight to match, and no number of walks that a test can afford gives its mean.)
        static void Compare(string what, Func<Medium> make, int[] modes, int reported, int count, out double[] lookups)
        {
            SampledWavelengths[] orders = Orders(0.29f);
            Point3f start = new Point3f(-0.85f, 0.1f, 0.05f);
            Vector3f direction = Normalize(new Vector3f(1, -0.1f, 0.05f));
            Shell rs = new Shell(81);
            rs.radius = 0.9f;
            double exact = Unscattered(make(), rs, orders[0], start, direction);
            lookups = new double[modes.Length];
            double first = 0, firstError = 0;
            for (int i = 0; i < modes.Length; ++i)
            {
                Medium medium = make();
                Configure(medium, modes[i]);
                rs = new Shell(81);
                rs.radius = 0.9f;
                long before = GridMedium.lookups + WaveTexture.evaluations;
                double every, everyError, deviation, unscattered, unscatteredError, collisions, largest;
                Walks(medium, rs, orders, start, direction, count, out every, out everyError, out deviation, out unscattered, out unscatteredError, out collisions,
                      out largest);
                lookups[i] = (double)(GridMedium.lookups + WaveTexture.evaluations - before) / count;
                Console.WriteLine("MEASURE {0}, {1}: leaves unscattered {2:F5} +- {3:F5} (exp(-tau) {4:F5}); leaves {5:F5} +- {6:F5}, deviation {7:F4}, largest {8:F2}; " +
                                  "{9:F2} scatterings and {10:F2} lookups of the density a walk", what, TrackerNames[modes[i]], unscattered, unscatteredError, exact, every, everyError,
                                  deviation, largest, collisions, lookups[i]);
                if (modes[i] == reported)
                {
                    continue;
                }
                EXPECT_LT(Math.Abs(unscattered - exact), 4 * unscatteredError + 1e-5);
                if (i == 0)
                {
                    first = every;
                    firstError = everyError;
                }
                else
                {
                    EXPECT_LT(Math.Abs(every - first), 4 * Math.Sqrt(everyError * everyError + firstError * firstError));
                }
            }
        }

        // Kutz et al. 2017 beside what was there, in a grid medium
        [TEST("ProductionSampling", "TrackersSideBySide")]
        public static void TrackersSideBySide()
        {
            // (floor of density, scale of the coefficients, absorption, scattering): grey and dense, coloured and dense, coloured and
            // thin at the rim, faintly coloured
            Float[] floors = { 0.5f, 0.5f, 0, 0.5f };
            Float[] scales = { 6, 6, 12, 6 };
            RGB[] absorptions = { new RGB(0.1f, 0.1f, 0.1f), new RGB(0.05f, 0.2f, 0.6f), new RGB(0.05f, 0.2f, 0.6f), new RGB(0.1f, 0.11f, 0.12f) };
            RGB[] scatterings = { new RGB(1f, 1f, 1f), new RGB(0.4f, 1f, 2.5f), new RGB(0.4f, 1f, 2.5f), new RGB(0.9f, 1f, 1.1f) };
            for (int c = 0; c < floors.Length; ++c)
            {
                int index = c;
                double[] lookups;
                Compare("smoke " + c, () => Smoke(absorptions[index], scatterings[index], scales[index], floors[index]), new int[] { 0, 1, 2, 3, 4, 5 },
                        c == 1 || c == 2 ? 3 : -1, 200000, out lookups);
                if (floors[c] > 0)
                {
                    // (a floor of density: collisions that need no lookup)
                    EXPECT_LT(lookups[1], 0.9 * lookups[0]);
                    EXPECT_LT(lookups[5], 0.9 * lookups[2]);
                }
            }
        }

        // The same in an aggregate of two grids that overlap, and in a texture medium
        [TEST("ProductionSampling", "TrackersInAggregateAndTexture")]
        public static void TrackersInAggregateAndTexture()
        {
            double[] lookups;
            Compare("aggregate of two grids",
                    () => new AggregateMedium(new Medium[] { Smoke(new RGB(0.05f, 0.2f, 0.6f), new RGB(0.4f, 1f, 2.5f), 4, 0.5f),
                                                             Smoke(new RGB(0.3f, 0.1f, 0.05f), new RGB(2f, 1f, 0.5f), 3, 0.4f, new Vector3f(0.3f, 0.2f, -0.1f)) }, 2, 6, null),
                    new int[] { 0, 1, 2, 5 }, 200000, out lookups);
            EXPECT_LT(lookups[1], 0.9 * lookups[0]);
            EXPECT_LT(lookups[3], 0.9 * lookups[2]);
            Compare("texture medium",
                    () => new TextureMedium(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), new Transform(), new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.05f, 0.2f, 0.6f)),
                                            new RGBUnboundedSpectrum(RGBColorSpace.sRGB, new RGB(0.4f, 1f, 2.5f)), 4, 0, new WaveTexture(), 0.6f, 1.0f, 8, 4, 0, 0.5f, null, 1.0f, null),
                    new int[] { 0, 1, 2, 5 }, 200000, out lookups);
            // (a procedural density that is never below 0.1 of at most 1.1: collisions of the control component are not evaluated)
            EXPECT_LT(lookups[1], 0.95 * lookups[0]);
            EXPECT_LT(lookups[3], 0.95 * lookups[2]);
        }

        // Spectral tracking to the point of a single scattering, with and without the control component, alone and weighed
        // against the equiangular point: the point light in the fog of PointLightInFog
        [TEST("ProductionSampling", "SpectralSingleScattering")]
        public static void SpectralSingleScattering()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
            SampledWavelengths[] orders = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            foreach (int mode in new int[] { 2, 4, 5 })
            {
                foreach (VolumeDistanceSampling sampling in new VolumeDistanceSampling[] { VolumeDistanceSampling.Density, VolumeDistanceSampling.MIS })
                {
                    Fog fog = new Fog(91);
                    fog.light = new Point3f(2, 0.3f, 0);
                    fog.distance = 6;
                    GridMedium medium = Grid(absorption, scattering, 0);
                    Configure(medium, mode);
                    medium.volumeIntegrator.scattering = VolumeScattering.Single;
                    medium.volumeIntegrator.distanceSampling = sampling;
                    long before = GridMedium.lookups;
                    double mean, error, deviation;
                    Measure(medium, fog, orders, ray, 300000, out mean, out error, out deviation);
                    Console.WriteLine("MEASURE point light in fog, single scattering, {0}, {1}: {2:F6} +- {3:F6} (quadrature 0.078532), deviation {4:F4}, {5:F2} grid lookups",
                                      TrackerNames[mode], sampling, mean, error, deviation, (double)(GridMedium.lookups - before) / 300000);
                    EXPECT_LT(Math.Abs(mean - 0.078532), 4 * error + 2e-5);
                }
            }
        }

        // The estimators of the transmittance beside one another, through the grid of RayMarchingTransmittance at three
        // densities and in a grid of one density (a majorant with nothing fictitious in it): every wavelength against
        // exp(-tau), the deviation, the lookups. Ratio tracking and the track-length estimator are read at the wavelength that
        // tracks; the others estimate every wavelength by itself.
        [TEST("ProductionSampling", "TransmittanceEstimators")]
        public static void TransmittanceEstimators()
        {
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.52f);
            string[] names = { "ratio", "tracklength", "nextflight", "nextflightratio", "raymarching", "unidirectional", "bidirectional", "residuallocal", "residualtracking",
                               "pseriescumulative", "pseriescmf" };
            Point3f from = new Point3f(-0.9f, -0.8f, -0.7f), to = new Point3f(0.8f, 0.9f, 0.6f);
            Float d = Distance(from, to);
            Vector3f direction = (to - from) / d;
            // (scale of the ball; a negative one: the grid of one density with that extinction, less fictitious matter than any)
            foreach (Float scale in new Float[] { 2, 6, 20, -1 })
            {
                double[] deviations = new double[names.Length], costs = new double[names.Length];
                for (int e = 0; e < names.Length; ++e)
                {
                    GridMedium medium = scale > 0 ? Ball(new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), scale) : Grid(new RGB(0.2f, 0.4f, 1f), new RGB(1f, 2f, 3f), 0);
                    medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance(names[e]));
                    medium.volumeIntegrator.scattering = VolumeScattering.Single;
                    double[] tau = new double[NSpectrumSamples];
                    int steps = 100000;
                    for (int i = 0; i < steps; ++i)
                    {
                        MediumProperties mp = medium.SamplePoint(from + (Float)((i + 0.5) * d / steps) * direction, lambda);
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            tau[k] += ((double)mp.sigma_a[k] + mp.sigma_s[k]) * d / steps;
                        }
                    }
                    bool alone = e >= 2;
                    Fog rs = new Fog(101);
                    int count = 100000;
                    double[] mean = new double[NSpectrumSamples], squares = new double[NSpectrumSamples];
                    double lowest = 1, highest = 0;
                    long before = GridMedium.lookups;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.P = from;
                        ctx.medium = medium;
                        SampledSpectrum T = Volume.Create(ctx, medium).Transmittance(rs, from, to);
                        for (int k = 0; k < NSpectrumSamples; ++k)
                        {
                            mean[k] += T[k];
                            squares[k] += (double)T[k] * T[k];
                            if (alone)
                            {
                                lowest = Math.Min(lowest, T[k]);
                                highest = Math.Max(highest, T[k]);
                                EXPECT_EQ(1, ctx.r[k]);
                            }
                        }
                    }
                    costs[e] = (double)(GridMedium.lookups - before) / count;
                    for (int k = 0; k < (alone ? NSpectrumSamples : 1); ++k)
                    {
                        mean[k] /= count;
                        double deviation = Math.Sqrt(Math.Max(0, squares[k] / count - mean[k] * mean[k]));
                        if (k == 0)
                        {
                            deviations[e] = deviation;
                        }
                        EXPECT_LT(Math.Abs(mean[k] - Math.Exp(-tau[k])), 4 * deviation / Math.Sqrt(count) + 1e-6);
                    }
                    if (names[e].StartsWith("nextflight") || names[e].EndsWith("directional") || names[e] == "residualtracking")
                    {
                        // (sums or products of terms that are not negative; the others have terms of either sign)
                        EXPECT_GE(lowest, 0.0);
                    }
                    Console.WriteLine("MEASURE transmittance, {0}, {1}: tau {2:F3}, exp(-tau) {3:F6}, estimate {4:F6}, deviation {5:F6}, {6:F2} lookups, " +
                                      "deviation^2 x lookups {7:E2}, largest {8:F2}", scale > 0 ? "ball x " + scale : "one density", names[e], tau[0], Math.Exp(-tau[0]), mean[0],
                                      deviations[e], costs[e], deviations[e] * deviations[e] * costs[e], alone ? highest : 1);
                    if (alone)
                    {
                        Console.WriteLine("MEASURE transmittance, {0}, {1}: range [{2:F3}, {3:F3}]", scale > 0 ? "ball x " + scale : "one density", names[e], lowest, highest);
                    }
                }
                if (scale < 0)
                {
                    // (one density: the control of the voxel is the medium, and nothing is left to track)
                    EXPECT_LT(deviations[7], 1e-6);
                    EXPECT_LT(deviations[8], 1e-6);
                }
                // The next flights against what they are built on (where anything gets through at all), and the walk that ends
                // early against the one that does not
                if (scale < 20)
                {
                    EXPECT_LT(deviations[2], deviations[1]);
                    EXPECT_LT(deviations[3], deviations[0]);
                }
                EXPECT_LT(costs[2], costs[3] + 0.05);
            }
        }

        // The light scattered twice (Volume.DoubleScattering) from a point light in a fog that is everywhere: as a path would
        // sample it and by the joint importance sampling of Georgiev et al. 2013, in the three single scattering volumes. The
        // two are independent estimates of one integral; the density in proportion to the inverse distance integrates to one.
        [TEST("ProductionSampling", "DoubleScattering")]
        public static void DoubleScattering()
        {
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            {
                InverseDistanceDensity density = InverseDistanceDensity.Create(ray, 0.5f, 6, new Point3f(2, 0.3f, 0.1f));
                EXPECT_TRUE(density.valid);
                double integral = 0, inside = 0;
                int steps = 2000000;
                for (int i = 0; i < steps; ++i)
                {
                    integral += density.PDF((Float)(0.5 + (i + 0.5) * 5.5 / steps)) * 5.5 / steps;
                }
                RNG rng = new RNG(3);
                double estimate = 0;
                for (int s = 0; s < 400000; ++s)
                {
                    Float t = density.Sample(rng.Uniform<Float>());
                    inside += t >= 0.5f && t <= 6 ? 1 : 0;
                    estimate += Math.Exp(-t) / density.PDF(t) / 400000;
                }
                Console.WriteLine("MEASURE inverse distance density: integral {0:F5}, samples inside {1:F5}, int exp(-t) {2:F5} (exact {3:F5})", integral, inside / 400000,
                                  estimate, Math.Exp(-0.5) - Math.Exp(-6.0));
                EXPECT_LT(Math.Abs(integral - 1), 2e-4);
                EXPECT_LT(Math.Abs(estimate - (Math.Exp(-0.5) - Math.Exp(-6.0))), 5e-3);
                EXPECT_FALSE(InverseDistanceDensity.Create(ray, 0, 6, ray.Apply(3)).valid);
            }
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
            SampledWavelengths[] orders = Orders(0.37f);
            string[] kinds = { "homogeneous", "marched", "tracked" };
            VolumeDoubleScattering[] modes = { VolumeDoubleScattering.None, VolumeDoubleScattering.Plain, VolumeDoubleScattering.Joint, VolumeDoubleScattering.MIS };
            foreach (Float distance in new Float[] { 6, 0.6f })
            {
                // (the light beside the ray; with the short ray, beyond its end)
                for (int kind = 0; kind < 3; ++kind)
                {
                    double[] means = new double[4], errors = new double[4], deviations = new double[4];
                    for (int mode = 0; mode < 4; ++mode)
                    {
                        Fog fog = new Fog(111);
                        fog.light = new Point3f(2, 0.3f, 0);
                        fog.distance = distance;
                        Medium medium = kind == 2 ? Grid(absorption, scattering, 0) : Homogeneous(absorption, scattering, 0, 0);
                        medium.volumeIntegrator = Settings(kind == 1 ? VolumeScattering.SinglePDF : VolumeScattering.Single, VolumeDistanceSampling.Equiangular);
                        medium.volumeIntegrator.stepSize = 0.02f;
                        medium.volumeIntegrator.doubleScattering = modes[mode];
                        if (kind == 2)
                        {
                            // (so that the transmittances do not hide what the sampling of the two points does)
                            medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance("raymarching"));
                            medium.volumeIntegrator.scattering = VolumeScattering.Single;
                            medium.volumeIntegrator.distanceSampling = VolumeDistanceSampling.Equiangular;
                            medium.volumeIntegrator.doubleScattering = modes[mode];
                        }
                        Measure(medium, fog, orders, ray, kind == 1 ? 20000 : (mode == 1 ? 1600000 : 400000), out means[mode], out errors[mode], out deviations[mode]);
                    }
                    double plain = means[1] - means[0], joint = means[2] - means[0];
                    double plainError = Math.Sqrt(errors[1] * errors[1] + errors[0] * errors[0]), jointError = Math.Sqrt(errors[2] * errors[2] + errors[0] * errors[0]);
                    Console.WriteLine("MEASURE double scattering, ray of {0}, {1}: once {2:F6} +- {3:F6}; twice as a path samples it {4:F6} +- {5:F6} (deviation of once and twice " +
                                      "{6:F4}), jointly {7:F6} +- {8:F6} (deviation {9:F4})", distance, kinds[kind], means[0], errors[0], plain, plainError, deviations[1], joint,
                                      jointError, deviations[2]);
                    double both = means[3] - means[0], bothError = Math.Sqrt(errors[3] * errors[3] + errors[0] * errors[0]);
                    Console.WriteLine("MEASURE double scattering, ray of {0}, {1}: by one of the two, weighed {2:F6} +- {3:F6} (deviation {4:F4})", distance, kinds[kind], both,
                                      bothError, deviations[3]);
                    EXPECT_GT(joint, 10 * jointError);
                    EXPECT_LT(Math.Abs(joint - plain), 4 * Math.Sqrt(plainError * plainError + jointError * jointError));
                    EXPECT_LT(Math.Abs(both - joint), 4 * Math.Sqrt(bothError * bothError + jointError * jointError));
                    EXPECT_LT(deviations[2], deviations[1]);
                    EXPECT_LT(deviations[3], deviations[1]);
                }
            }
        }

        // The cloud medium as a texture (cloudmediumtexture.cs): the same density as CloudMedium at every point; the bounds
        // that it gives over a box hold for the densities in the box; and what a texture medium of it then has that the cloud
        // medium has not: a majorant below the cloud medium's, and a control for the residual estimators.
        [TEST("ProductionSampling", "CloudMediumTexture")]
        public static void CloudAsTexture()
        {
            RNG rng = new RNG(121);
            // (the box that the cloud is made for: its density falls with the height from 0 to 1)
            Bounds3f box = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.41f);
            // (density, wispiness, frequency, edge falloff)
            Float[][] settings = { new Float[] { 4, 1, 3, 0 }, new Float[] { 2, 0, 2, 0 }, new Float[] { 1, 1, 5, 0 }, new Float[] { 4, 1, 3, 0.2f } };
            for (int c = 0; c < settings.Length; ++c)
            {
                Float density = settings[c][0], wispiness = settings[c][1], frequency = settings[c][2], falloff = settings[c][3];
                CloudMediumTexture texture = new CloudMediumTexture(new PointTransformMapping(new Transform()), density, wispiness, frequency, falloff, false);
                CloudMedium cloud = new CloudMedium(box, new Transform(), new ConstantSpectrum(0.5f), new ConstantSpectrum(1.5f), 0, density, wispiness, frequency, new Allocator(),
                                                    null, 0, 1, falloff, false);
                TextureMedium medium = new TextureMedium(box, new Transform(), new ConstantSpectrum(0.5f), new ConstantSpectrum(1.5f), 1, 0, texture, 0, 1, 16, 4, 0, 0.5f, null, 1,
                                                         null);
                // The same density
                double worst = 0;
                for (int i = 0; i < 20000; ++i)
                {
                    Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    worst = Math.Max(worst, Math.Abs(cloud.SamplePoint(p, lambda).sigma_s[0] - medium.SamplePoint(p, lambda).sigma_s[0]));
                }
                EXPECT_EQ(0, worst);
                // The bounds over boxes of several sizes
                int outside = 0, boxes = 0, positive = 0;
                double width = 0;
                foreach (Float size in new Float[] { 0.01f, 0.0625f, 0.25f })
                {
                    for (int b = 0; b < 400; ++b)
                    {
                        Point3f corner = new Point3f(Lerp(rng.Uniform<Float>(), 0, 1 - size), Lerp(rng.Uniform<Float>(), 0, 1 - size), Lerp(rng.Uniform<Float>(), 0, 1 - size));
                        Bounds3f region = new Bounds3f(corner, corner + new Vector3f(size, size, size));
                        texture.RegionBounds(region, out Float lo, out Float hi);
                        ++boxes;
                        width += hi - lo;
                        positive += lo > 0 ? 1 : 0;
                        for (int i = 0; i < 200; ++i)
                        {
                            TextureEvalContext ctx = new TextureEvalContext();
                            ctx.p = region.Lerp(new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>()));
                            Float value = texture.Evaluate(ctx);
                            if (value < lo || value > hi)
                            {
                                ++outside;
                            }
                        }
                    }
                }
                // The majorant along rays through the box: the texture medium's against the cloud medium's
                double depthCloud = 0, depthTexture = 0, depthControl = 0, residualMean = 0, residualMiddle = 0;
                for (int i = 0; i < 2000; ++i)
                {
                    Point3f o = new Point3f(-0.5f, Lerp(rng.Uniform<Float>(), 0.05f, 0.95f), Lerp(rng.Uniform<Float>(), 0.05f, 0.95f));
                    Ray ray = new Ray(o, Normalize(new Vector3f(1, Lerp(rng.Uniform<Float>(), -0.1f, 0.1f), Lerp(rng.Uniform<Float>(), -0.1f, 0.1f))), 0);
                    foreach (Medium each in new Medium[] { cloud, medium })
                    {
                        RayMajorantIterator iter = each.SampleRay(ray, 5, lambda, new ScratchBuffer());
                        while (true)
                        {
                            pstd.optional<RayMajorantSegment> seg = iter.Next();
                            if (!seg)
                            {
                                break;
                            }
                            double depth = (double)seg.value().sigma_maj[0] * (seg.value().tMax - seg.value().tMin);
                            if (each == cloud)
                            {
                                depthCloud += depth;
                            }
                            else
                            {
                                depthTexture += depth;
                                depthControl += depth * seg.value().averageFraction;
                                // What residual ratio tracking has left to track: the majorant of the residual about the
                                // mean of the voxel, and about the middle of its bounds
                                Float average = seg.value().averageFraction, least = seg.value().controlFraction;
                                residualMean += depth * std.max(1 - average, average - least);
                                residualMiddle += depth * 0.5f * (1 - least);
                            }
                        }
                    }
                }
                Console.WriteLine("MEASURE cloud as a texture, density {0} wispiness {1} frequency {2} falloff {3}: largest difference of the density {4}; {5} of {6} values " +
                                  "outside their bounds; mean width of the bounds {7:F3}; boxes with a smallest density above zero {8:F3}; majorant depth along rays: cloud " +
                                  "medium {9:F2}, texture medium {10:F2}, of which the control {11:F2}", density, wispiness, frequency, falloff, worst, outside, boxes * 200,
                                  width / boxes, (double)positive / boxes, depthCloud / 2000, depthTexture / 2000, depthControl / 2000);
                Console.WriteLine("MEASURE cloud as a texture, case {0}: majorant depth of the residual about the mean of a voxel {1:F3}, about the middle of its bounds {2:F3}", c,
                                  residualMean / 2000, residualMiddle / 2000);
                EXPECT_EQ(0, outside);
                EXPECT_LT(depthTexture, depthCloud);
                // Residual ratio tracking through the cloud with each of the two controls: the same transmittance; the deviation
                // and what is left to track
                {
                    Fog rs = new Fog(131);
                    Point3f from = new Point3f(0.02f, 0.62f, 0.4f), to = new Point3f(0.98f, 0.7f, 0.55f);
                    double[] means = new double[2], deviations = new double[2], depths = new double[2];
                    for (int control = 0; control < 2; ++control)
                    {
                        TextureMedium tracked = new TextureMedium(box, new Transform(), new ConstantSpectrum(0.5f), new ConstantSpectrum(1.5f), 4, 0, texture, 0, 1, 16, 4, 0, 0.5f,
                                                                  null, 1, null);
                        if (control == 1)
                        {
                            tracked.UseMiddleControl();
                        }
                        tracked.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance("residuallocal"));
                        RayMajorantIterator iter = tracked.SampleRay(new Ray(from, Normalize(to - from), 0), Distance(from, to), lambda, new ScratchBuffer());
                        while (true)
                        {
                            pstd.optional<RayMajorantSegment> seg = iter.Next();
                            if (!seg)
                            {
                                break;
                            }
                            depths[control] += (double)seg.value().sigma_maj[0] * (seg.value().tMax - seg.value().tMin) *
                                               std.max(1 - seg.value().averageFraction, seg.value().averageFraction - seg.value().controlFraction);
                        }
                        double sum = 0, squares = 0;
                        int count = 100000;
                        for (int s = 0; s < count; ++s)
                        {
                            VolumeContext ctx = new VolumeContext();
                            ctx.lambda = lambda;
                            ctx.P = from;
                            ctx.medium = tracked;
                            double value = Volume.Create(ctx, tracked).Transmittance(rs, from, to)[0];
                            sum += value;
                            squares += value * value;
                        }
                        means[control] = sum / count;
                        deviations[control] = Math.Sqrt(Math.Max(0, squares / count - means[control] * means[control]));
                    }
                    Console.WriteLine("MEASURE cloud as a texture, case {0}, residual ratio tracking: control the mean of the voxel: {1:F5}, deviation {2:F5}, {3:F2} lookups, " +
                                      "deviation^2 x lookups {4:E2}; the middle of its bounds: {5:F5}, deviation {6:F5}, {7:F2} lookups, {8:E2}", c, means[0], deviations[0],
                                      depths[0], deviations[0] * deviations[0] * depths[0], means[1], deviations[1], depths[1], deviations[1] * deviations[1] * depths[1]);
                    EXPECT_LT(Math.Abs(means[0] - means[1]), 4 * Math.Sqrt(deviations[0] * deviations[0] + deviations[1] * deviations[1]) / Math.Sqrt(100000.0) + 1e-6);
                }
            }
        }

        // Equiangular sampling with every estimator of the transmittance, alone and weighed against the tracked point: the
        // point light in the fog of PointLightInFog, in the tracked volume, where the transmittance to the equiangular point is
        // the medium's estimator's
        [TEST("ProductionSampling", "EquiangularWithEstimators")]
        public static void EquiangularWithEstimators()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
            SampledWavelengths[] orders = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            string[] names = { "ratio", "tracklength", "residual", "residuallocal", "residualtracking", "nextflight", "nextflightratio", "unidirectional", "bidirectional",
                               "pseriescumulative", "pseriescmf", "raymarching" };
            foreach (string name in names)
            {
                foreach (VolumeDistanceSampling sampling in new VolumeDistanceSampling[] { VolumeDistanceSampling.Equiangular, VolumeDistanceSampling.MIS })
                {
                    Fog fog = new Fog(141);
                    fog.light = new Point3f(2, 0.3f, 0);
                    fog.distance = 6;
                    GridMedium medium = Grid(absorption, scattering, 0);
                    medium.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance(name));
                    medium.volumeIntegrator.scattering = VolumeScattering.Single;
                    medium.volumeIntegrator.distanceSampling = sampling;
                    long before = GridMedium.lookups;
                    double mean, error, deviation;
                    Measure(medium, fog, orders, ray, 200000, out mean, out error, out deviation);
                    Console.WriteLine("MEASURE point light in fog, tracked, {0}, transmittance {1}: {2:F6} +- {3:F6} (quadrature 0.078532), deviation {4:F4}, {5:F2} lookups",
                                      sampling, name, mean, error, deviation, (double)(GridMedium.lookups - before) / 200000);
                    EXPECT_LT(Math.Abs(mean - 0.078532), 4 * error + 2e-5);
                }
            }
        }

        // Equiangular sampling beside the next-event estimation of a path (Miller et al. 2019; Volume.EquiangularShare), in the
        // multiple scattering volumes: the light of the point light that is scattered once on the ray, as the sum of what the
        // volume gathers at its equiangular point and of the next-event estimation at the collision that it returns, with the
        // weight that it returns for it. Against the quadrature of PointLightInFog.
        [TEST("ProductionSampling", "EquiangularBesideNextEvent")]
        public static void EquiangularBesideNextEvent()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.2f, 0.3f, 0.5f);
            SampledWavelengths[] orders = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            string[] kinds = { "homogeneous", "tracked", "tracked with the decomposition tracker" };
            VolumeDistanceSampling[] modes = { VolumeDistanceSampling.Density, VolumeDistanceSampling.Equiangular, VolumeDistanceSampling.MIS };
            for (int kind = 0; kind < 3; ++kind)
            {
                double[] deviations = new double[3];
                for (int mode = 0; mode < 3; ++mode)
                {
                    Fog fog = new Fog(151);
                    fog.light = new Point3f(2, 0.3f, 0);
                    fog.distance = 6;
                    Medium medium = kind == 0 ? Homogeneous(absorption, scattering, 0, 0) : Grid(absorption, scattering, 0);
                    medium.volumeIntegrator = new VolumeIntegrator(kind == 2 ? VolumeIntegrator.Decomposition() : VolumeIntegrator.Default.tracker,
                                                                   VolumeIntegrator.Default.transmittance);
                    medium.volumeIntegrator.distanceSampling = modes[mode];
                    double sum = 0, squares = 0;
                    int count = 400000;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = orders[s % orders.Length];
                        MediumProperties here = medium.SamplePoint(ray.o, ctx.lambda);
                        fog.sigma_t = here.sigma_a + here.sigma_s;
                        ctx.P = ray.o;
                        ctx.medium = medium;
                        SampledSpectrum L, T, w;
                        Point3f P;
                        Ray wo;
                        VolumeGeometry g;
                        bool hit = Volume.Create(ctx, medium).Integrate(fog, ray, out L, out T, out w, out P, out wo, out g);
                        double value = L.Average();
                        if (hit && !g.isSurface)
                        {
                            // The next-event estimation at the collision, as the path tracer makes it
                            ctx.P = P;
                            Vector3f toLight;
                            SampledSpectrum arriving, beam, beamR;
                            fog.GenerateLightSample(ctx, g.scatter, g.phase, out toLight, out arriving, out beam, out beamR);
                            Float direct = g.direct != null ? g.direct.For(true, true, fog.light) : 1;
                            value += direct * (w * T * g.phase.p(-ray.d, toLight) * arriving * beam / ctx.r.Average()).Average();
                        }
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / count;
                    deviations[mode] = Math.Sqrt(Math.Max(0, squares / count - mean * mean));
                    double error = deviations[mode] / Math.Sqrt(count);
                    Console.WriteLine("MEASURE scattered once in a multiple scattering volume, {0}, {1}: {2:F6} +- {3:F6} (quadrature 0.078532), deviation {4:F4}", kinds[kind],
                                      modes[mode], mean, error, deviations[mode]);
                    EXPECT_LT(Math.Abs(mean - 0.078532), 4 * error + 2e-5);
                }
                EXPECT_LT(deviations[2], 0.6 * deviations[0]);
            }
        }

        // Joint importance sampling of two scatterings beside the path in a multiple scattering volume (Volume.JointShare):
        // the light that reaches the start of a ray after one or two scatterings, gathered as the path tracer does it over
        // two rays (what the volumes gather themselves, and the next-event estimation at each collision with the weight that
        // the volume gives), is the same with and without it, alone and with the equiangular point of either kind; and with
        // a light in the medium it varies less.
        [TEST("ProductionSampling", "JointBesideThePath")]
        public static void JointBesideThePath()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.4f, 0.6f, 1f);
            SampledWavelengths[] orders = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            string[] kinds = { "homogeneous", "tracked", "tracked with the decomposition tracker" };
            VolumeDistanceSampling[] distances = { VolumeDistanceSampling.Density, VolumeDistanceSampling.MIS, VolumeDistanceSampling.Equiangular };
            for (int kind = 0; kind < 3; ++kind)
            {
                double reference = 0, referenceError = 0, plain = 0;
                for (int mode = 0; mode < 6; ++mode)
                {
                    bool jointly = mode >= 3;
                    Fog fog = new Fog(171);
                    fog.light = new Point3f(0.7f, 0.2f, 0.05f);
                    fog.distance = 2;
                    Medium medium = kind == 0 ? Homogeneous(absorption, scattering, 0.3f, 0) : Grid(absorption, scattering, 0);
                    medium.volumeIntegrator = kind == 2 ? new VolumeIntegrator(VolumeIntegrator.Decomposition(), VolumeIntegrator.Default.transmittance)
                                                        : Settings(VolumeScattering.Multiple, distances[mode % 3]);
                    medium.volumeIntegrator.scattering = VolumeScattering.Multiple;
                    medium.volumeIntegrator.distanceSampling = distances[mode % 3];
                    medium.volumeIntegrator.doubleScattering = jointly ? VolumeDoubleScattering.Joint : VolumeDoubleScattering.None;
                    RNG choose = new RNG(77);
                    double sum = 0, squares = 0;
                    int count = mode == 0 ? 2400000 : 600000;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = orders[s % orders.Length];
                        MediumProperties here = medium.SamplePoint(ray.o, ctx.lambda);
                        fog.sigma_t = here.sigma_a + here.sigma_s;
                        ctx.P = ray.o;
                        ctx.medium = medium;
                        ctx.mayScatter = true;
                        ctx.mayScatterTwice = true;
                        SampledSpectrum L, T, w;
                        Point3f P;
                        Ray wo;
                        VolumeGeometry g;
                        bool hit = Volume.Create(ctx, medium).Integrate(fog, ray, out L, out T, out w, out P, out wo, out g);
                        double value = L.Average();
                        if (hit && !g.isSurface)
                        {
                            // The first collision: its light, and the ray that the path takes from it
                            ctx.P = P;
                            Vector3f toLight;
                            SampledSpectrum arriving, beam, beamR;
                            fog.GenerateLightSample(ctx, g.scatter, g.phase, out toLight, out arriving, out beam, out beamR);
                            Float direct = g.direct != null ? g.direct.For(true, true, fog.light) : 1;
                            SampledSpectrum beta = w * T;
                            value += direct * (beta * g.phase.p(-ray.d, toLight) * arriving * beam / ctx.r.Average()).Average();
                            pstd.optional<PhaseFunctionSample> ps = g.phase.Sample_p(-ray.d, new Point2f(choose.Uniform<Float>(), choose.Uniform<Float>()));
                            if (ps && ps.value().pdf > 0)
                            {
                                beta *= ps.value().p / ps.value().pdf;
                                ctx.joint = g.joint;
                                if (ctx.joint != null)
                                {
                                    ctx.joint.phasePdf = ps.value().pdf;
                                }
                                // (two scatterings and no more: the second ray draws nothing jointly)
                                ctx.mayScatterTwice = false;
                                Ray second = new Ray(P, ps.value().wi, 0);
                                SampledSpectrum L2, T2, w2;
                                Point3f P2;
                                Ray wo2;
                                VolumeGeometry g2;
                                bool hit2 = Volume.Create(ctx, medium).Integrate(fog, second, out L2, out T2, out w2, out P2, out wo2, out g2);
                                value += (beta * L2).Average();
                                if (hit2 && !g2.isSurface)
                                {
                                    ctx.P = P2;
                                    fog.GenerateLightSample(ctx, g2.scatter, g2.phase, out toLight, out arriving, out beam, out beamR);
                                    Float direct2 = g2.direct != null ? g2.direct.For(true, true, fog.light) : 1;
                                    value += direct2 * (beta * w2 * T2 * g2.phase.p(-second.d, toLight) * arriving * beam / ctx.r.Average()).Average();
                                }
                            }
                        }
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / count;
                    double deviation = Math.Sqrt(Math.Max(0, squares / count - mean * mean)), error = deviation / Math.Sqrt(count);
                    Console.WriteLine("MEASURE scattered once or twice in a multiple scattering volume, {0}, distances {1}, {2}: {3:F6} +- {4:F6}, deviation {5:F4}", kinds[kind],
                                      distances[mode % 3], jointly ? "two drawn jointly" : "the path alone", mean, error, deviation);
                    if (mode == 0)
                    {
                        reference = mean;
                        referenceError = error;
                        plain = deviation;
                    }
                    else
                    {
                        EXPECT_LT(Math.Abs(mean - reference), 4 * Math.Sqrt(error * error + referenceError * referenceError));
                    }
                    if (mode == 3)
                    {
                        EXPECT_LT(deviation, plain);
                    }
                }
            }
        }

        // Once-more scattered next-event estimation (Volume.OnceMoreShare, Volume.OnceMoreShare2) beside the path: the light
        // that reaches the start of a ray after one or two scatterings, gathered over two rays as the path tracer does it,
        // is the same without it, with either implementation, and with each beside the equiangular point and the two points
        // drawn jointly; in a medium that scatters forwards it varies less with it.
        [TEST("ProductionSampling", "OnceMoreBesideThePath")]
        public static void OnceMoreBesideThePath()
        {
            RGB absorption = new RGB(0.05f, 0.1f, 0.2f), scattering = new RGB(0.4f, 0.6f, 1f);
            SampledWavelengths[] orders = Orders(0.37f);
            Ray ray = new Ray(new Point3f(0, 0, 0), new Vector3f(1, 0, 0), 0);
            string[] kinds = { "homogeneous, g 0.9", "tracked, g 0.9", "tracked, g 0.3, decomposition tracker" };
            VolumeOnceMore[] ways = { VolumeOnceMore.None, VolumeOnceMore.OnceMore, VolumeOnceMore.OnceMore2 };
            for (int kind = 0; kind < 3; ++kind)
            {
                double reference = 0, referenceError = 0, plain = 0;
                double[] alone = new double[3];
                for (int mode = 0; mode < 12; ++mode)
                {
                    VolumeOnceMore way = ways[mode % 3];
                    bool equiangular = (mode / 3) % 2 == 1, jointly = mode >= 6;
                    Fog fog = new Fog(191);
                    fog.light = new Point3f(1.2f, 0.12f, 0.05f);
                    fog.distance = 3;
                    Float g = kind == 2 ? 0.3f : 0.9f;
                    Medium medium = kind == 0 ? Homogeneous(absorption, scattering, g, 0) : Grid(absorption, scattering, 0, g);
                    medium.volumeIntegrator = kind == 2 ? new VolumeIntegrator(VolumeIntegrator.Decomposition(), VolumeIntegrator.Default.transmittance)
                                                        : Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                    medium.volumeIntegrator.scattering = VolumeScattering.Multiple;
                    medium.volumeIntegrator.distanceSampling = equiangular ? VolumeDistanceSampling.MIS : VolumeDistanceSampling.Density;
                    medium.volumeIntegrator.doubleScattering = jointly ? VolumeDoubleScattering.Joint : VolumeDoubleScattering.None;
                    medium.volumeIntegrator.onceMore = way;
                    bool pathWeights = kind != 2;
                    RNG choose = new RNG(77);
                    double sum = 0, squares = 0;
                    int count = mode == 0 ? 2400000 : 500000;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = orders[s % orders.Length];
                        MediumProperties here = medium.SamplePoint(ray.o, ctx.lambda);
                        fog.sigma_t = here.sigma_a + here.sigma_s;
                        ctx.P = ray.o;
                        ctx.medium = medium;
                        ctx.mayScatter = true;
                        ctx.mayScatterTwice = true;
                        SampledSpectrum L, T, w;
                        Point3f P;
                        Ray wo;
                        VolumeGeometry gm;
                        bool hit = Volume.Create(ctx, medium).Integrate(fog, ray, out L, out T, out w, out P, out wo, out gm);
                        double value = L.Average();
                        if (hit && !gm.isSurface)
                        {
                            ctx.P = P;
                            Vector3f toLight;
                            SampledSpectrum arriving, beam, beamR;
                            fog.GenerateLightSample(ctx, gm.scatter, gm.phase, out toLight, out arriving, out beam, out beamR);
                            Float direct = gm.direct != null ? gm.direct.For(true, true, fog.light) : 1;
                            SampledSpectrum beta = w * T;
                            value += direct * (beta * gm.phase.p(-ray.d, toLight) * arriving * beam / ctx.r.Average()).Average();
                            if (way != VolumeOnceMore.None)
                            {
                                Volume volume = Volume.Create(ctx, medium);
                                Volume.OnceMoreStart start = new Volume.OnceMoreStart();
                                start.point = gm.scatter;
                                start.phase = gm.phase;
                                value += (beta * (way == VolumeOnceMore.OnceMore ? volume.OnceMoreShare(fog, start, gm.phase, gm.joint, pathWeights)
                                                                                 : volume.OnceMoreShare2(fog, start, gm.phase, gm.joint, pathWeights))).Average();
                            }
                            pstd.optional<PhaseFunctionSample> ps = gm.phase.Sample_p(-ray.d, new Point2f(choose.Uniform<Float>(), choose.Uniform<Float>()));
                            if (ps && ps.value().pdf > 0)
                            {
                                beta *= ps.value().p / ps.value().pdf;
                                ctx.joint = gm.joint;
                                if (ctx.joint != null)
                                {
                                    ctx.joint.phasePdf = ps.value().pdf;
                                }
                                ctx.mayScatterTwice = false;
                                Ray second = new Ray(P, ps.value().wi, 0);
                                SampledSpectrum L2, T2, w2;
                                Point3f P2;
                                Ray wo2;
                                VolumeGeometry g2;
                                bool hit2 = Volume.Create(ctx, medium).Integrate(fog, second, out L2, out T2, out w2, out P2, out wo2, out g2);
                                value += (beta * L2).Average();
                                if (hit2 && !g2.isSurface)
                                {
                                    ctx.P = P2;
                                    fog.GenerateLightSample(ctx, g2.scatter, g2.phase, out toLight, out arriving, out beam, out beamR);
                                    Float direct2 = g2.direct != null ? g2.direct.For(true, true, fog.light) : 1;
                                    value += direct2 * (beta * w2 * T2 * g2.phase.p(-second.d, toLight) * arriving * beam / ctx.r.Average()).Average();
                                }
                            }
                        }
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / count;
                    double deviation = Math.Sqrt(Math.Max(0, squares / count - mean * mean)), error = deviation / Math.Sqrt(count);
                    Console.WriteLine("MEASURE scattered once or twice, {0}: {1,-9} {2,-12} {3,-8}: {4:F6} +- {5:F6}, deviation {6:F4}", kinds[kind], way,
                                      equiangular ? "equiangular" : "", jointly ? "jointly" : "", mean, error, deviation);
                    if (mode == 0)
                    {
                        reference = mean;
                        referenceError = error;
                        plain = deviation;
                    }
                    else
                    {
                        EXPECT_LT(Math.Abs(mean - reference), 4 * Math.Sqrt(error * error + referenceError * referenceError));
                    }
                    if (mode < 3)
                    {
                        alone[mode] = deviation;
                    }
                }
                if (kind < 2)
                {
                    // (forward scattering: what the technique is for)
                    EXPECT_LT(alone[1], plain);
                    EXPECT_LT(alone[2], plain);
                }
            }
        }

        // Volume.Emission against int exp(-sigma t) sigma_a Le dt, and the transmittance of the shadow rays of a multiple
        // scattering volume with the contrast control: exp(-(sigma_a + contrast^depth sigma_s) d)
        [TEST("ProductionSampling", "EmissionAndContrast")]
        public static void EmissionAndContrast()
        {
            RGB absorption = new RGB(0.3f, 0.6f, 1.2f), scattering = new RGB(0.5f, 1f, 2f);
            SampledWavelengths[] orders = Orders(0.44f);
            SampledWavelengths lambda = orders[0];
            Point3f P0 = new Point3f(0, 0, 0), P1 = new Point3f(1.5f, 0.2f, -0.1f);
            Float d = Distance(P0, P1);
            Fog rs = new Fog(51);
            VolumeScattering[] kinds = { VolumeScattering.Absorption, VolumeScattering.Single, VolumeScattering.SinglePDF, VolumeScattering.Multiple };
            for (int shape = 0; shape < 2; ++shape)
            {
                foreach (VolumeScattering kind in kinds)
                {
                    Medium medium = shape == 0 ? Homogeneous(absorption, scattering, 0, 2) : Grid(absorption, scattering, 2);
                    medium.volumeIntegrator = Settings(kind, VolumeDistanceSampling.Density);
                    medium.volumeIntegrator.stepSize = 0.01f;
                    MediumProperties mp = medium.SamplePoint(P0, lambda);
                    double exact = 0;
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        double st = kind == VolumeScattering.Absorption ? mp.sigma_a[c] : mp.sigma_a[c] + mp.sigma_s[c];
                        exact += mp.sigma_a[c] * mp.Le[c] * (1 - Math.Exp(-st * d)) / st / NSpectrumSamples;
                    }
                    double sum = 0, squares = 0;
                    int count = shape == 0 || kind == VolumeScattering.SinglePDF ? 200 : 200000;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = orders[s % orders.Length];
                        ctx.P = P0;
                        ctx.medium = medium;
                        double value = Volume.Create(ctx, medium).Emission(rs, P0, P1).Average();
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / count, error = Math.Sqrt(Math.Max(0, squares / count - mean * mean) / count);
                    Console.WriteLine("MEASURE emission between two points, {0}, {1}: {2:F5} +- {3:F5} (closed form {4:F5})", shape == 0 ? "homogeneous" : "grid", kind, mean,
                                      error, exact);
                    EXPECT_GT(exact, 0.1);
                    EXPECT_LT(Math.Abs(mean - exact), 4 * error + 2e-4 * exact);
                }
            }
            for (int shape = 0; shape < 2; ++shape)
            {
                for (int depth = 0; depth < 3; ++depth)
                {
                    Medium medium = shape == 0 ? Homogeneous(absorption, scattering, 0, 0) : Grid(absorption, scattering, 0);
                    medium.volumeIntegrator = Settings(VolumeScattering.Multiple, VolumeDistanceSampling.Density);
                    medium.volumeIntegrator.contrast = 0.5f;
                    MediumProperties mp = medium.SamplePoint(P0, lambda);
                    double scale = Math.Pow(0.5, depth), exact = Math.Exp(-(mp.sigma_a[0] + scale * mp.sigma_s[0]) * d);
                    double sum = 0, squares = 0;
                    int count = shape == 0 ? 10 : 200000;
                    for (int s = 0; s < count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.P = P0;
                        ctx.medium = medium;
                        ctx.depth = depth;
                        // (the first wavelength tracks: its estimate stands alone)
                        double value = Volume.Create(ctx, medium).Transmittance(rs, P0, P1)[0];
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / count, error = Math.Sqrt(Math.Max(0, squares / count - mean * mean) / count);
                    Console.WriteLine("MEASURE contrast control 0.5, {0}, depth {1}: transmittance {2:F5} +- {3:F5} (closed form {4:F5})", shape == 0 ? "homogeneous" : "grid",
                                      depth, mean, error, exact);
                    EXPECT_LT(Math.Abs(mean - exact), 4 * error + 1e-5);
                }
            }
        }
    }
}
