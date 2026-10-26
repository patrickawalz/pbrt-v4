// filters_test.cpp

using System;
using testing;

namespace pbrt
{
    public static unsafe class FiltersTests
    {
        [TEST("Sinc", "ZeroHandling")]
        public static void Sinc_ZeroHandling()
        {
            Float x = 0;
            Float prev = 1;
            for (int i = 0; i < 10000; ++i)
            {
                Float cur = Sinc(x);
                EXPECT_LE(cur, prev);
                x = NextFloatUp(x);
                prev = cur;
            }

            x = -0.0f;
            prev = 1;
            for (int i = 0; i < 10000; ++i)
            {
                Float cur = Sinc(x);
                EXPECT_LE(cur, prev);
                x = NextFloatDown(x);
                prev = cur;
            }
        }

        private static Filter[] makeFilters(Vector2f radius)
        {
            return new Filter[] { new BoxFilter(radius), new GaussianFilter(radius),
                                  new MitchellFilter(radius), new LanczosSincFilter(radius),
                                  new TriangleFilter(radius) };
        }

        [TEST("Filter", "ZeroPastRadius")]
        public static void Filter_ZeroPastRadius()
        {
            foreach (Vector2f r in new Vector2f[] { new Vector2f(1, 1), new Vector2f(1.5f, .25f), new Vector2f(.33f, 5.2f),
                                                    new Vector2f(.1f, .1f), new Vector2f(3, 3) })
            {
                foreach (Filter f in makeFilters(r))
                {
                    EXPECT_EQ(0, f.Evaluate(new Point2f(0, (Float)(r.y + 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f(r.x, (Float)(r.y + 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f(-r.x, (Float)(r.y + 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f(0, (Float)(-r.y - 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f(r.x, (Float)(-r.y - 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f(-r.x, (Float)(-r.y - 1e-3))));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(r.x + 1e-3), 0)));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(r.x + 1e-3), r.y)));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(r.x + 1e-3), -r.y)));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(-r.x - 1e-3), 0)));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(-r.x - 1e-3), r.y)));
                    EXPECT_EQ(0, f.Evaluate(new Point2f((Float)(-r.x - 1e-3), -r.y)));
                }
            }
        }

        private static Float integrateFilter(Filter f)
        {
            Float sum = 0;
            int sqrtSamples = 256;
            int nSamples = sqrtSamples * sqrtSamples;
            Float area = 2 * f.Radius().x * 2 * f.Radius().y;
            foreach (Point2f u in new Stratified2D(sqrtSamples, sqrtSamples))
            {
                Float py = Lerp(u.y, -f.Radius().y, f.Radius().y), px = Lerp(u.x, -f.Radius().x, f.Radius().x);
                Point2f p = new Point2f(px, py);
                sum += f.Evaluate(p);
            }
            return sum / nSamples * area;
        }

        [TEST("Filter", "Integral")]
        public static void Filter_Integral()
        {
            Func<Float, Float, bool> approxEqual = (Float a, Float b) =>
            {
                if (std.max(std.abs(a), std.abs(b)) < 1e-3)
                    return std.abs(a - b) < 1e-5;
                else
                    return 2 * std.abs(a - b) / std.abs(a + b) < 1e-2;
            };

            foreach (Vector2f r in new Vector2f[] { new Vector2f(1, 1), new Vector2f(2.5f, 1), new Vector2f(1, 2.5f), new Vector2f(3.4f, 2.5f) })
                foreach (Filter f in makeFilters(r))
                    EXPECT_TRUE(approxEqual(f.Integral(), integrateFilter(f)), f.ToString());
        }
    }
}
