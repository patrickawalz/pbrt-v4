// (not in pbrt-v4) shadow_aware_test.cs
//
// Tests of the planetary shadow-aware distance sampling (shadow_aware_distance.cs): the shadow interval of a ray against the visibility of
// the sun from points of the ray (a sphere intersection of the planet, written again here), the density of the sampled distances
// against the collisions of the majorant that the walk itself draws, and the densities of the wavelengths.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ShadowAwareTests
    {
        const double PlanetRadius = 6360000.0;

        static AtmosphereMedium MakeAtmosphere()
        {
            ParsedParameter profile = new ParsedParameter(new FileLoc());
            profile.type = "string";
            profile.name = "aerosolprofile";
            profile.AddString("continental_clean");
            List<ParsedParameter> list = new List<ParsedParameter> { profile };
            Medium m = Medium.Create("atmosphere", new ParameterDictionary(list, RGBColorSpace.sRGB), new Transform(), new FileLoc(), new Allocator());
            return (AtmosphereMedium)m;
        }

        // Whether the ray from p toward the light (a unit vector) hits the sphere of the planet, in double precision: the planet shadows p
        static bool Shadowed(double px, double py, double pz, Vector3f toLight)
        {
            double cx = 0, cy = -PlanetRadius, cz = 0;
            double ox = px - cx, oy = py - cy, oz = pz - cz;
            double b = ox * toLight.x + oy * toLight.y + oz * toLight.z;
            double c = ox * ox + oy * oy + oz * oz - PlanetRadius * PlanetRadius;
            double discriminant = b * b - c;
            // The sphere is hit in front of p if its centre is ahead of the ray and the closest approach is inside
            return discriminant > 0 && -b > 0 && c > 0;
        }

        // The shadow interval of a ray in the cylinder is the set of the points that the planet shadows, as a ray from each point to the sun sees
        [TEST("ShadowAware", "ShadowIntervalMatchesVisibilityOfTheSun")]
        public static void ShadowAware_ShadowInterval()
        {
            RNG rng = new RNG(3);
            int rays = 0, withShadow = 0, mismatches = 0, points = 0;
            for (int trial = 0; trial < 4000; ++trial)
            {
                // The origin and the direction of a ray of the sky, and a direction toward a sun below or above the horizon
                Float height = trial % 2 == 0 ? 1.7f : 100000f * rng.Uniform<Float>();
                Point3f o = new Point3f(3000f * (rng.Uniform<Float>() - 0.5f), height, 3000f * (rng.Uniform<Float>() - 0.5f));
                Vector3f d = Normalize(SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>())));
                Float elevation = (-12f + 20f * rng.Uniform<Float>()) * Pi / 180;
                Vector3f toLight = new Vector3f(0, MathF.Sin(elevation), MathF.Cos(elevation));
                double tMax = 400000.0 * rng.Uniform<Float>() + 1000.0;
                ++rays;
                bool any = ShadowAwareDistance.ShadowInterval(0, -PlanetRadius, 0, PlanetRadius, o, d, tMax, toLight, out double s0, out double s1);
                if (any)
                {
                    ++withShadow;
                }
                // With no interval the points of the ray are all lit or all shadowed (the strategy does not exist for either)
                int shadowedPoints = 0, litPoints = 0;
                for (int k = 0; k < 64; ++k)
                {
                    double t = tMax * (k + 0.5) / 64;
                    double px = o.x + t * d.x, py = o.y + t * d.y, pz = o.z + t * d.z;
                    // Points inside the planet are neither lit nor part of the atmosphere
                    double altitude = Math.Sqrt(px * px + (py + PlanetRadius) * (py + PlanetRadius) + pz * pz) - PlanetRadius;
                    if (altitude < 0)
                    {
                        continue;
                    }
                    bool shadowed = Shadowed(px, py, pz, toLight);
                    if (shadowed)
                    {
                        ++shadowedPoints;
                    }
                    else
                    {
                        ++litPoints;
                    }
                    if (!any)
                    {
                        continue;
                    }
                    bool inInterval = t >= s0 && t <= s1;
                    ++points;
                    // Points within a few metres of the boundary are not decided in float arithmetic of the test
                    if (shadowed != inInterval)
                    {
                        double edge = Math.Min(Math.Abs(t - s0), Math.Abs(t - s1));
                        if (edge > 30.0)
                        {
                            ++mismatches;
                        }
                    }
                }
                if (!any && shadowedPoints > 2 && litPoints > 2)
                {
                    ++mismatches;
                }
            }
            EXPECT_GT(withShadow, rays / 10);
            EXPECT_LT(withShadow, rays);
            EXPECT_GT(points, 30000);
            EXPECT_EQ(mismatches, 0);
        }

        // The density of the sampled distances: it integrates to one over the lit pieces for every wavelength, has no mass in the shadow, and
        // the samples follow it
        [TEST("ShadowAware", "DistancesFollowTheDensityOfTheLitPieces")]
        public static void ShadowAware_Distances()
        {
            AtmosphereMedium atmosphere = MakeAtmosphere();
            RNG rng = new RNG(5);
            ShadowAwareDistance distance = new ShadowAwareDistance();
            int configurations = 0;
            double worstIntegral = 0, worstSpread = 0;
            for (int trial = 0; trial < 30; ++trial)
            {
                Point3f o = new Point3f(0, 1.7f + 30000f * (trial % 3 == 2 ? rng.Uniform<Float>() : 0f), 0);
                Float elevation = (2f + 40f * rng.Uniform<Float>()) * Pi / 180;
                Float azimuth = 2 * Pi * rng.Uniform<Float>();
                Vector3f d = new Vector3f(MathF.Cos(elevation) * MathF.Sin(azimuth), MathF.Sin(elevation), MathF.Cos(elevation) * MathF.Cos(azimuth));
                Float sunElevation = (-8f + 4f * rng.Uniform<Float>()) * Pi / 180;
                Vector3f toLight = new Vector3f(0, MathF.Sin(sunElevation), MathF.Cos(sunElevation));
                SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                if (!distance.Build(atmosphere, o, d, Infinity, toLight, lambda))
                {
                    continue;
                }
                ++configurations;
                // The extent of the lit pieces, and the integral of each wavelength's density over it by the midpoint rule on a fine grid
                const int n = 120000;
                double[] integral = new double[NSpectrumSamples];
                double tEnd = 4000000.0;
                for (int k = 0; k < n; ++k)
                {
                    double t = tEnd * (k + 0.5) / n;
                    SampledSpectrum q = distance.Density((Float)t);
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        integral[c] += q[c] * tEnd / n;
                    }
                }
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    worstIntegral = Math.Max(worstIntegral, Math.Abs(integral[c] - 1));
                }
                // The sampled distances against the cumulative of the density (a Kolmogorov distance)
                const int samples = 4000;
                List<double> ts = new List<double>();
                for (int k = 0; k < samples; ++k)
                {
                    if (distance.Sample(rng.Uniform<Float>(), out Float t, out SampledSpectrum q))
                    {
                        ts.Add(t);
                        // The density at the sample is the density of that distance
                        EXPECT_NEAR(q[0] / distance.Density(t)[0], 1.0, 1e-3);
                        // Nothing is sampled in the shadow: the shadow interval has no density
                        EXPECT_GT(q[0], 0);
                    }
                }
                ts.Sort();
                double[] cdf = new double[n + 1];
                for (int k = 0; k < n; ++k)
                {
                    cdf[k + 1] = cdf[k] + distance.Density((Float)(tEnd * (k + 0.5) / n))[0] * tEnd / n;
                }
                double kolmogorov = 0;
                for (int k = 0; k < ts.Count; ++k)
                {
                    int cell = Math.Min((int)(ts[k] / tEnd * n), n - 1);
                    double f = cdf[cell] + (ts[k] / tEnd * n - cell) * (cdf[cell + 1] - cdf[cell]);
                    kolmogorov = Math.Max(kolmogorov, Math.Max(Math.Abs(f - (double)k / ts.Count), Math.Abs(f - (double)(k + 1) / ts.Count)));
                }
                worstSpread = Math.Max(worstSpread, kolmogorov);
            }
            Console.WriteLine("shadow aware distances: " + configurations + " rays, worst |integral - 1| " + worstIntegral + ", worst Kolmogorov distance " + worstSpread);
            EXPECT_GT(configurations, 8);
            EXPECT_LT(worstIntegral, 0.02);
            // 4000 samples: the 99.9 % point of the Kolmogorov distance is 1.95 / sqrt(n) = 0.031
            EXPECT_LT(worstSpread, 0.035);
        }

        // A ray that is lit all along, or shadowed all along, has no strategy
        [TEST("ShadowAware", "NoStrategyWhereTheRayIsLitOrShadowedAllAlong")]
        public static void ShadowAware_NoStrategy()
        {
            AtmosphereMedium atmosphere = MakeAtmosphere();
            ShadowAwareDistance distance = new ShadowAwareDistance();
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.3f);
            // The sun high in the sky: nothing is in the shadow
            Vector3f noon = new Vector3f(0, MathF.Sin(1.2f), MathF.Cos(1.2f));
            EXPECT_FALSE(distance.Build(atmosphere, new Point3f(0, 1.7f, 0), Normalize(new Vector3f(0, 0.5f, 1)), Infinity, noon, lambda));
            // A ray that starts in the shadow of the planet and stays in it (the sun is far below the horizon)
            Vector3f midnight = new Vector3f(0, -0.9f, 0.43f);
            EXPECT_FALSE(distance.Build(atmosphere, new Point3f(0, 1.7f, 0), Normalize(new Vector3f(0, 0.2f, 1)), 100000f, midnight, lambda));
        }
    }

}  // namespace pbrt
