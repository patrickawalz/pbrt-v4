// (not in pbrt-v4) specularpolynomial_test.cs
//
// SpecularPolynomial.Roots(): on random triangles with normals at their corners, a point of the triangle and a point x0 give
// by the law of reflection, or of refraction, a point x2; the roots between x0 and x2 must have the point among them.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class SpecularPolynomialTests
    {
        [TEST("SpecularPolynomial", "FindsTheChain")]
        public static void FindsTheChain()
        {
            RNG rng = new RNG(11);
            foreach (bool reflection in new bool[] { true, false })
            {
                int tried = 0, found = 0, rootsTotal = 0;
                double worst = 0;
                for (int trial = 0; trial < 2000; ++trial)
                {
                    Point3f p0 = new Point3f(0, 0, 0), p1 = new Point3f(1 + 0.3f * rng.Uniform<Float>(), 0.2f * rng.Uniform<Float>(), 0.1f * rng.Uniform<Float>()),
                            p2 = new Point3f(0.3f * rng.Uniform<Float>(), 1 + 0.3f * rng.Uniform<Float>(), 0.1f * rng.Uniform<Float>());
                    Vector3f n0 = Normalize(new Vector3f(0.4f * (rng.Uniform<Float>() - 0.5f), 0.4f * (rng.Uniform<Float>() - 0.5f), 1));
                    Vector3f n1 = Normalize(new Vector3f(0.4f * (rng.Uniform<Float>() - 0.5f), 0.4f * (rng.Uniform<Float>() - 0.5f), 1));
                    Vector3f n2 = Normalize(new Vector3f(0.4f * (rng.Uniform<Float>() - 0.5f), 0.4f * (rng.Uniform<Float>() - 0.5f), 1));
                    Float u = 0.1f + 0.5f * rng.Uniform<Float>(), v = 0.1f + (0.8f - u) * rng.Uniform<Float>();
                    Point3f q = p0 + u * (p1 - p0) + v * (p2 - p0);
                    Vector3f n = Normalize(n0 + u * (n1 - n0) + v * (n2 - n0));
                    Point3f x0 = q + (1 + 2 * rng.Uniform<Float>()) * Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, 0.6f));
                    Vector3f wi = Normalize(x0 - q), wo;
                    Float eta = 1;
                    if (reflection)
                    {
                        wo = Reflect(wi, n);
                    }
                    else
                    {
                        eta = 1.33f;
                        Float etap;
                        if (!Refract(wi, (Normal3f)n, eta, &etap, &wo))
                        {
                            continue;
                        }
                    }
                    Point3f x2 = q + (1 + 2 * rng.Uniform<Float>()) * wo;
                    ++tried;
                    List<Point3f> points = new List<Point3f>();
                    SpecularPolynomial.Roots(x0, x2, p0, p1, p2, n0, n1, n2, reflection, eta, points);
                    rootsTotal += points.Count;
                    double nearest = 1e9;
                    foreach (Point3f point in points)
                    {
                        nearest = Math.Min(nearest, Length(point - q));
                    }
                    if (nearest < 1e-3)
                    {
                        ++found;
                        worst = Math.Max(worst, nearest);
                    }
                }
                Console.WriteLine("MEASURE specular polynomial, {0}: the chain is among the roots in {1} of {2} triangles (off by at most {3:E2}); {4:F2} roots a triangle",
                                  reflection ? "reflection" : "refraction", found, tried, worst, (double)rootsTotal / tried);
                EXPECT_EQ(found, tried);
            }
        }
    }
}
