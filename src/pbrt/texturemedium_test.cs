// (not in pbrt-v4) texturemedium_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class TextureMediumTests
    {
        // A smooth texture without an analytic gradient, and the same with one
        private class WaveTexture : FloatTexture
        {
            public override string ToString() { return "[ WaveTexture ]"; }
            public override Float MinValue() { return -.5f; }
            public override Float MaxValue() { return .5f; }
            public override Float Evaluate(TextureEvalContext ctx) { return .5f * (Float)Math.Sin(4 * ctx.p.x - 2 * ctx.p.z); }
        }

        private class WaveTextureWithGradient : WaveTexture
        {
            public override Vector3f Gradient(TextureEvalContext ctx)
            {
                Float c = .5f * (Float)Math.Cos(4 * ctx.p.x - 2 * ctx.p.z);
                return new Vector3f(4 * c, 0, -2 * c);
            }
        }

        static TextureMedium MakeMedium(FloatTexture texture, int majorantRes, int hierarchyDepth = 0)
        {
            Bounds3f box = new Bounds3f(new Point3f(-1, 0, -1), new Point3f(1, 2, 1));
            return new TextureMedium(box, new Transform(), new ConstantSpectrum(0.5f), new ConstantSpectrum(1.5f), 1.0f, 0.3f,
                                     texture, 0.6f, 1.0f, majorantRes, 4, hierarchyDepth, 0.5f, null, 1.0f, null);
        }

        [TEST("TextureMedium", "MajorantsCoverDensity")]
        public static void TextureMedium_MajorantsCoverDensity()
        {
            TextureMapping3D map = new PointTransformMapping(new Transform());
            FloatTexture[] textures = new FloatTexture[] { new FBmTexture(map, 6, .5f), new WaveTexture(),
                                                           new WrinkledTexture(map, 4, .6f) };
            RNG rng = new RNG(5);
            foreach (FloatTexture texture in textures)
            {
                int res = 8;
                TextureMedium medium = MakeMedium(texture, res);
                for (int i = 0; i < 5000; ++i)
                {
                    Point3f u = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    Point3f p = new Point3f(-1 + 2 * u.x, 2 * u.y, -1 + 2 * u.z);
                    int x = std.min((int)(u.x * res), res - 1), y = std.min((int)(u.y * res), res - 1),
                        z = std.min((int)(u.z * res), res - 1);
                    Bounds3f voxel = new Bounds3f(new Point3f((Float)x / res, (Float)y / res, (Float)z / res),
                                                  new Point3f((Float)(x + 1) / res, (Float)(y + 1) / res, (Float)(z + 1) / res));
                    EXPECT_LE(medium.Density(p), medium.MaxDensity(voxel) + 1e-5f);
                }
            }
        }

        [TEST("TextureMedium", "RatioTrackingMatchesQuadrature")]
        public static void TextureMedium_RatioTrackingMatchesQuadrature()
        {
            RatioTrackingMatchesQuadrature(0);
        }

        [TEST("TextureMedium", "HierarchicalRatioTrackingMatchesQuadrature")]
        public static void TextureMedium_HierarchicalRatioTrackingMatchesQuadrature()
        {
            RatioTrackingMatchesQuadrature(2);
        }

        [TEST("TextureMedium", "HierarchicalMajorantsCoverDensity")]
        public static void TextureMedium_HierarchicalMajorantsCoverDensity()
        {
            TextureMapping3D map = new PointTransformMapping(new Transform());
            TextureMedium medium = MakeMedium(new FBmTexture(map, 6, .5f), 4, 2);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            RNG rng = new RNG(9);
            int segments = 0;
            for (int i = 0; i < 300; ++i)
            {
                Point3f o = new Point3f(-2, 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, -.3f + .6f * rng.Uniform<Float>(), -.3f + .6f * rng.Uniform<Float>()));
                Ray ray = new Ray(o, d);
                ScratchBuffer buf = new ScratchBuffer();
                RayMajorantIterator iterator = medium.SampleRay(ray, 5, lambda, buf);
                for (pstd.optional<RayMajorantSegment> seg = iterator.Next(); seg; seg = iterator.Next())
                {
                    ++segments;
                    for (int k = 0; k < 8; ++k)
                    {
                        Float t = seg.value().tMin + (k + .5f) / 8 * (seg.value().tMax - seg.value().tMin);
                        MediumProperties mp = medium.SamplePoint(ray.Apply(t), lambda);
                        EXPECT_LE(mp.sigma_a[0] + mp.sigma_s[0], seg.value().sigma_maj[0] * (1 + 1e-4f) + 1e-5f);
                    }
                }
            }
            EXPECT_GT(segments, 300);
        }

        private static Vector3f Normalize(Vector3f v) { return pbrt.Normalize(v); }

        private static void RatioTrackingMatchesQuadrature(int hierarchyDepth)
        {
            TextureMapping3D map = new PointTransformMapping(new Transform());
            TextureMedium medium = MakeMedium(new FBmTexture(map, 5, .5f), 8, hierarchyDepth);
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            Ray ray = new Ray(new Point3f(-2, 1.1f, 0.15f), new Vector3f(1, 0.05f, 0.02f));
            Float tMax = 4;

            // Quadrature of the optical depth
            int n = 40000;
            double depth = 0;
            for (int i = 0; i < n; ++i)
            {
                MediumProperties mp = medium.SamplePoint(ray.Apply((i + .5f) / n * tMax), lambda);
                depth += mp.sigma_a[0] + mp.sigma_s[0];
            }
            double reference = Math.Exp(-depth * tMax / n * Length(ray.d));

            RNG rng = new RNG(11);
            int samples = 40000;
            double sum = 0, sum2 = 0;
            for (int i = 0; i < samples; ++i)
            {
                Float T_ray = 1;
                pbrt.SampleT_maj(ray, medium, tMax / 1, rng.Uniform<Float>(), ref rng, lambda,
                                 (Point3f p, MediumProperties mp, SampledSpectrum sigma_maj, SampledSpectrum T_maj) =>
                                 {
                                     T_ray *= std.max<Float>(0, sigma_maj[0] - mp.sigma_a[0] - mp.sigma_s[0]) / sigma_maj[0];
                                     return T_ray > 0;
                                 });
                sum += T_ray;
                sum2 += T_ray * T_ray;
            }
            double mean = sum / samples, stderr = Math.Sqrt(Math.Max(sum2 / samples - mean * mean, 0) / samples);
            EXPECT_LT(Math.Abs(mean - reference), 4 * stderr + 1e-3);
            EXPECT_LT(reference, 0.95);  // the ray goes through enough medium to matter
        }

        private static double Length(Vector3f v) { return Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z); }

        [TEST("TextureMedium", "GradientOfDensity")]
        public static void TextureMedium_GradientOfDensity()
        {
            TextureMedium numeric = MakeMedium(new WaveTexture(), 4);
            TextureMedium analytic = MakeMedium(new WaveTextureWithGradient(), 4);
            RNG rng = new RNG(2);
            for (int i = 0; i < 200; ++i)
            {
                Point3f p = new Point3f(-1 + 2 * rng.Uniform<Float>(), 2 * rng.Uniform<Float>(), -1 + 2 * rng.Uniform<Float>());
                Vector3f a = analytic.DensityGradient(p), d = numeric.DensityGradient(p);
                EXPECT_LT(std.abs(a.x - d.x), 2e-2f);
                EXPECT_LT(std.abs(a.y - d.y), 2e-2f);
                EXPECT_LT(std.abs(a.z - d.z), 2e-2f);
            }
        }
    }
}
