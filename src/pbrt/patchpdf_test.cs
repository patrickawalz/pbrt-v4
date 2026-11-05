// (not in pbrt-v4) patchpdf_test.cs
//
// BezierClipPatchShape: Sample(u) draws from a density that is not uniform in the area, so PDF(Interaction) must be the area density of the sampling at the point,
// not 1 / Area. Checked on a strongly curved bicubic patch of known area: E[1 / pdf] over the samples is the area, the pdf integrates to one over the patch, the
// share of the samples in each of the 4 x 4 bins of the parameters equals the integral of PDF(Interaction) over the bin, and PDF(Interaction) at a sampled point
// equals the pdf that Sample returned.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class PatchPdfTests
    {
        // A net with x = 3 u and y = 3 v exactly, and a tall bump in z, so that the Jacobian varies a lot
        private static readonly double[,] Heights =
        {
            { 0.0, 0.2, 3.0, 0.0 },
            { 0.3, 6.0, -2.0, 0.4 },
            { 0.1, -3.0, 8.0, 0.2 },
            { 0.0, 0.5, 0.1, 0.0 }
        };

        private static double[] Position(double u, double v)
        {
            double[] bu = Bernstein(u), bv = Bernstein(v);
            double z = 0;
            for (int i = 0; i < 4; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    z += bu[i] * bv[j] * Heights[i, j];
                }
            }
            return new double[] { 3 * u, 3 * v, z };
        }

        private static double[] Bernstein(double t)
        {
            double m = 1 - t;
            return new double[] { m * m * m, 3 * m * m * t, 3 * m * t * t, t * t * t };
        }

        private static double Jacobian(double u, double v)
        {
            double h = 1e-6;
            double[] a = Position(u + h, v), b = Position(u - h, v), c = Position(u, v + h), d = Position(u, v - h);
            double[] pu = { (a[0] - b[0]) / (2 * h), (a[1] - b[1]) / (2 * h), (a[2] - b[2]) / (2 * h) };
            double[] pv = { (c[0] - d[0]) / (2 * h), (c[1] - d[1]) / (2 * h), (c[2] - d[2]) / (2 * h) };
            double x = pu[1] * pv[2] - pu[2] * pv[1], y = pu[2] * pv[0] - pu[0] * pv[2], z = pu[0] * pv[1] - pu[1] * pv[0];
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static readonly double[] GaussNodes = { -0.9324695142031521, -0.6612093864662645, -0.2386191860831969, 0.2386191860831969, 0.6612093864662645, 0.9324695142031521 };
        private static readonly double[] GaussWeights = { 0.1713244923791704, 0.3607615730481386, 0.4679139345726910, 0.4679139345726910, 0.3607615730481386, 0.1713244923791704 };

        // The integral of f over [u0, u1] x [v0, v1] with a 6 x 6 Gauss rule in each of n x n subcells
        private static double Integrate(Func<double, double, double> f, double u0, double u1, double v0, double v1, int n)
        {
            double sum = 0;
            for (int a = 0; a < n; ++a)
            {
                for (int b = 0; b < n; ++b)
                {
                    double ua = u0 + (u1 - u0) * a / n, ub = u0 + (u1 - u0) * (a + 1) / n, va = v0 + (v1 - v0) * b / n, vb = v0 + (v1 - v0) * (b + 1) / n;
                    for (int i = 0; i < 6; ++i)
                    {
                        for (int j = 0; j < 6; ++j)
                        {
                            sum += GaussWeights[i] * GaussWeights[j] * 0.25 * (ub - ua) * (vb - va) *
                                   f(0.5 * (ua + ub) + 0.5 * (ub - ua) * GaussNodes[i], 0.5 * (va + vb) + 0.5 * (vb - va) * GaussNodes[j]);
                        }
                    }
                }
            }
            return sum;
        }

        private static Shape MakePatch()
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter type = new ParsedParameter(null);
            type.type = "string";
            type.name = "type";
            type.strings.push_back("bicubic");
            list.Add(type);
            ParsedParameter p = new ParsedParameter(null);
            p.type = "point3";
            p.name = "P";
            for (int v = 0; v < 4; ++v)
            {
                for (int u = 0; u < 4; ++u)
                {
                    p.floats.push_back((float)(u));
                    p.floats.push_back((float)(v));
                    p.floats.push_back((float)Heights[u, v]);
                }
            }
            list.Add(p);
            Transform id = new Transform();
            pstd.vector<Shape> shapes = Shape.Create("patch", id, id, false, new ParameterDictionary(list, RGBColorSpace.sRGB), new Dictionary<string, FloatTexture>(), null,
                                                     new Allocator());
            ASSERT_EQ(1, (int)shapes.size());
            return shapes[0];
        }

        [TEST("PatchPdf", "AreaDensityOfSampling")]
        public static void AreaDensityOfSampling()
        {
            Shape shape = MakePatch();
            double area = Integrate((u, v) => Jacobian(u, v), 0, 1, 0, 1, 8);
            EXPECT_NEAR(area, shape.Area(), 2e-3 * area, "area");
            Random rng = new Random(9);
            int count = 200000;
            double sum = 0, sum2 = 0;
            int[] bins = new int[16];
            int mismatches = 0;
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                double inverse = 1.0 / ss.value().pdf;
                sum += inverse;
                sum2 += inverse * inverse;
                Point3f q = ss.value().intr.p();
                int bx = Math.Min(3, (int)(q.x / 3 * 4)), by = Math.Min(3, (int)(q.y / 3 * 4));
                ++bins[by * 4 + bx];
                if (i < 5000)
                {
                    double pdf = shape.PDF(ss.value().intr);
                    if (Math.Abs(pdf / ss.value().pdf - 1) > 2e-3)
                    {
                        ++mismatches;
                    }
                }
            }
            double mean = sum / count, se = Math.Sqrt(Math.Max(0, sum2 / count - mean * mean) / count);
            EXPECT_LT(Math.Abs(mean - area) / se, 4.0, "E[1 / pdf] " + mean + " +- " + se + " vs the area " + area);
            // sampled pdf against PDF(Interaction): only points within 1e-3 of the border of a cell can differ
            EXPECT_LT(mismatches, 25, "PDF(Interaction) differs from the pdf of the sample");
            // the bins
            double total = 0;
            for (int by = 0; by < 4; ++by)
            {
                for (int bx = 0; bx < 4; ++bx)
                {
                    double probability = Integrate((u, v) =>
                    {
                        double[] q = Position(u, v);
                        Interaction intr = new Interaction(new Point3fi(new Point3f((Float)q[0], (Float)q[1], (Float)q[2])), new Normal3f(0, 0, 1), new Point2f((Float)u, (Float)v));
                        return shape.PDF(intr) * Jacobian(u, v);
                    }, bx / 4.0, (bx + 1) / 4.0, by / 4.0, (by + 1) / 4.0, 4);
                    total += probability;
                    double observed = bins[by * 4 + bx] / (double)count, sigma = Math.Sqrt(probability * (1 - probability) / count);
                    EXPECT_LT(Math.Abs(observed - probability) / sigma, 4.5, "bin " + bx + "," + by + ": " + observed + " vs " + probability);
                }
            }
            EXPECT_NEAR(1.0, total, 5e-3, "the pdf integrates to one over the patch");
            // away from the patch the density is zero
            Interaction away = new Interaction(new Point3fi(new Point3f(1.5f, 1.5f, 50)), new Normal3f(0, 0, 1), new Point2f(0.5f, 0.5f));
            EXPECT_EQ(0.0, shape.PDF(away), "a point that is not on the patch");
        }
    }
}
