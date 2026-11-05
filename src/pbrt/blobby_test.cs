// (not in pbrt-v4) blobby_test.cs
//
// Tests of the blobby primitive (shapes_blobby.cs) against the closed forms of a sphere, of a capsule and of the union of two spheres, and against a
// brute-force march of the field along random rays for programs that have no closed form.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class BlobbyTests
    {
        private const double Threshold = 0.421;

        // The radius (in units of the radius of the blob) of the surface of the profile
        private static double SurfaceRadius()
        {
            double lo = 0, hi = 1;
            for (int i = 0; i < 200; ++i)
            {
                double mid = 0.5 * (lo + hi);
                if (BlobbyField.Profile(mid) > Threshold)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            return Math.Sqrt(0.5 * (lo + hi));
        }

        private static float[] Matrix(double sx, double sy, double sz, double tx, double ty, double tz)
        {
            return new float[] { (float)sx, 0, 0, 0, 0, (float)sy, 0, 0, 0, 0, (float)sz, 0, (float)tx, (float)ty, (float)tz, 1 };
        }

        private static float[] Join(params float[][] parts)
        {
            List<float> all = new List<float>();
            foreach (float[] part in parts)
            {
                all.AddRange(part);
            }
            return all.ToArray();
        }

        private static BlobbyField Field(int nleaf, int[] code, float[] floats)
        {
            string error;
            BlobbyField field = BlobbyField.Parse(nleaf, code, floats, Threshold, out error);
            ASSERT_TRUE(field != null, error);
            return field;
        }

        private static BlobbyShape Shape_(BlobbyField field, Transform t, bool reverse)
        {
            return new BlobbyShape(field, t, Inverse(t), reverse, 0);
        }

        private static Ray RandomRay(Random rng, double cx, double cy, double cz, double far, double aim)
        {
            double z = 1 - 2 * rng.NextDouble(), r = Math.Sqrt(Math.Max(0, 1 - z * z)), phi = 2 * Math.PI * rng.NextDouble();
            double ox = cx + far * r * Math.Cos(phi), oy = cy + far * r * Math.Sin(phi), oz = cz + far * z;
            double z2 = 1 - 2 * rng.NextDouble(), r2 = Math.Sqrt(Math.Max(0, 1 - z2 * z2)), phi2 = 2 * Math.PI * rng.NextDouble();
            double rho = aim * Math.Pow(rng.NextDouble(), 1.0 / 3);
            double tx = cx + rho * r2 * Math.Cos(phi2), ty = cy + rho * r2 * Math.Sin(phi2), tz = cz + rho * z2;
            Vector3f d = pbrt.Normalize(new Vector3f((Float)(tx - ox), (Float)(ty - oy), (Float)(tz - oz)));
            return new Ray(new Point3f((Float)ox, (Float)oy, (Float)oz), d);
        }

        // The first sign change of f - threshold along the ray by marching with a small step and bisection (the reference)
        private static bool March(Func<double, double, double, double> f, Ray ray, double tEnd, double step, out double tHit)
        {
            double ox = ray.o.x, oy = ray.o.y, oz = ray.o.z, dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
            double previous = f(ox, oy, oz) - Threshold, tp = 0;
            for (double t = step; t <= tEnd; t += step)
            {
                double value = f(ox + t * dx, oy + t * dy, oz + t * dz) - Threshold;
                if ((previous > 0) != (value > 0))
                {
                    double a = tp, b = t;
                    for (int i = 0; i < 60; ++i)
                    {
                        double m = 0.5 * (a + b);
                        double fm = f(ox + m * dx, oy + m * dy, oz + m * dz) - Threshold;
                        if ((fm > 0) == (previous > 0))
                        {
                            a = m;
                        }
                        else
                        {
                            b = m;
                        }
                    }
                    tHit = 0.5 * (a + b);
                    return true;
                }
                previous = value;
                tp = t;
            }
            tHit = 0;
            return false;
        }

        [TEST("Blobby", "SingleEllipsoidIsASphere")]
        public static void SingleEllipsoidIsASphere()
        {
            double r0 = SurfaceRadius();
            double scale = (float)1.5, cx = (float)0.3, cy = (float)-0.2, cz = (float)0.5;
            double radius = scale * r0;
            BlobbyField field = Field(1, new int[] { 1001, 0 }, Matrix(scale, scale, scale, cx, cy, cz));
            BlobbyShape shape = Shape_(field, new Transform(), false);
            Random rng = new Random(11);
            int hits = 0;
            for (int i = 0; i < 3000; ++i)
            {
                Ray ray = RandomRay(rng, cx, cy, cz, 5, 1.2 * radius);
                // the sphere
                double ox = ray.o.x - cx, oy = ray.o.y - cy, oz = ray.o.z - cz;
                double b = ox * ray.d.x + oy * ray.d.y + oz * ray.d.z, c = ox * ox + oy * oy + oz * oz - radius * radius;
                double disc = b * b - c;
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                bool expected = disc > 1e-9;
                if (disc < -1e-9 || disc > 1e-9)
                {
                    EXPECT_EQ(expected, si.has_value(), "hit or miss, ray " + i);
                    EXPECT_EQ(expected, shape.IntersectP(ray), "IntersectP, ray " + i);
                }
                if (expected && si.has_value())
                {
                    ++hits;
                    double t = -b - Math.Sqrt(disc);
                    EXPECT_NEAR(t, si.value().tHit, 2e-5 * Math.Max(1, t), "t, ray " + i);
                    Point3f p = si.value().intr.p();
                    Vector3f n = new Vector3f(si.value().intr.n.x, si.value().intr.n.y, si.value().intr.n.z);
                    Vector3f outward = pbrt.Normalize(new Vector3f((Float)(p.x - cx), (Float)(p.y - cy), (Float)(p.z - cz)));
                    EXPECT_NEAR(1.0, pbrt.Dot(n, outward), 2e-4, "normal, ray " + i);
                }
            }
            EXPECT_GT(hits, 1000);
            // the area, by Crofton lines
            EXPECT_NEAR(4 * Math.PI * radius * radius, shape.Area(), 0.012 * 4 * Math.PI * radius * radius, "area");
        }

        [TEST("Blobby", "SingleEllipsoidSampling")]
        public static void SingleEllipsoidSampling()
        {
            double r0 = SurfaceRadius();
            double scale = (float)1.5, cx = (float)0.3, cy = (float)-0.2, cz = (float)0.5;
            double radius = scale * r0;
            BlobbyField field = Field(1, new int[] { 1001, 0 }, Matrix(scale, scale, scale, cx, cy, cz));
            BlobbyShape shape = Shape_(field, new Transform(), false);
            double area = shape.Area();
            Random rng = new Random(5);
            int count = 20000;
            double[] sum = new double[3];
            double octantMax = 0;
            int[] octants = new int[8];
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                Point3f p = ss.value().intr.p();
                double dx = p.x - cx, dy = p.y - cy, dz = p.z - cz;
                EXPECT_NEAR(radius, Math.Sqrt(dx * dx + dy * dy + dz * dz), 2e-4, "on the surface");
                EXPECT_NEAR(1.0 / area, ss.value().pdf, 1e-6);
                sum[0] += dx / radius;
                sum[1] += dy / radius;
                sum[2] += dz / radius;
                ++octants[(dx > 0 ? 1 : 0) + (dy > 0 ? 2 : 0) + (dz > 0 ? 4 : 0)];
            }
            double se = Math.Sqrt(1.0 / 3 / count);
            for (int axis = 0; axis < 3; ++axis)
            {
                EXPECT_LT(Math.Abs(sum[axis] / count), 4 * se, "mean of axis " + axis);
            }
            double expected = count / 8.0, sd = Math.Sqrt(count / 8.0 * 7 / 8);
            for (int o = 0; o < 8; ++o)
            {
                octantMax = Math.Max(octantMax, Math.Abs(octants[o] - expected) / sd);
            }
            EXPECT_LT(octantMax, 4.5, "octants");
        }

        [TEST("Blobby", "SolidAngleDensityOfASphere")]
        public static void SolidAngleDensityOfASphere()
        {
            double r0 = SurfaceRadius();
            double scale = (float)1.5;
            double radius = scale * r0;
            BlobbyField field = Field(1, new int[] { 1001, 0 }, Matrix(scale, scale, scale, 0, 0, 0));
            BlobbyShape shape = Shape_(field, new Transform(), false);
            double area = shape.Area();
            Point3f ref_ = new Point3f(2.5f, 1.0f, -3.0f);
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(ref_), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            Random rng = new Random(3);
            int checkedDirections = 0, checkedSamples = 0;
            for (int i = 0; i < 2000; ++i)
            {
                // the direction to a random point in the disk of the sphere seen from the reference
                Vector3f toCenter = pbrt.Normalize(new Vector3f(-ref_.x, -ref_.y, -ref_.z));
                double distance = Math.Sqrt((double)ref_.x * ref_.x + (double)ref_.y * ref_.y + (double)ref_.z * ref_.z);
                double spread = 1.3 * Math.Asin(radius / distance);
                double a = spread * Math.Sqrt(rng.NextDouble()), phi = 2 * Math.PI * rng.NextDouble();
                Vector3f helper = Math.Abs(toCenter.x) < 0.9 ? new Vector3f(1, 0, 0) : new Vector3f(0, 1, 0);
                Vector3f e1 = pbrt.Normalize(pbrt.Cross(toCenter, helper)), e2 = pbrt.Cross(toCenter, e1);
                Vector3f wi = pbrt.Normalize(toCenter * (Float)Math.Cos(a) + (e1 * (Float)Math.Cos(phi) + e2 * (Float)Math.Sin(phi)) * (Float)Math.Sin(a));
                // the sphere
                double ox = ref_.x, oy = ref_.y, oz = ref_.z;
                double b = ox * wi.x + oy * wi.y + oz * wi.z, c = ox * ox + oy * oy + oz * oz - radius * radius;
                double disc = b * b - c;
                double pdf = shape.PDF(ctx, wi);
                if (disc < 1e-6)
                {
                    if (disc < -1e-6)
                    {
                        EXPECT_EQ(0.0, pdf, "no hit, direction " + i);
                    }
                    continue;
                }
                double t = -b - Math.Sqrt(disc);
                double px = ox + t * wi.x, py = oy + t * wi.y, pz = oz + t * wi.z;
                double cosine = Math.Abs((px * wi.x + py * wi.y + pz * wi.z) / radius);
                if (cosine < 0.02)
                {
                    continue;
                }
                double expected = t * t / (area * cosine);
                EXPECT_NEAR(1.0, pdf / expected, 2e-3, "pdf of direction " + i);
                ++checkedDirections;
            }
            EXPECT_GT(checkedDirections, 1000);
            // samples: the pdf of a sample at a point that is the first hit equals PDF(ctx, wi)
            for (int i = 0; i < 500; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                if (!ss.has_value())
                {
                    continue;
                }
                Vector3f wi = pbrt.Normalize(ss.value().intr.p() - ref_);
                double dot = pbrt.Dot(new Vector3f(ss.value().intr.n.x, ss.value().intr.n.y, ss.value().intr.n.z), wi);
                if (dot < -0.05)
                {
                    EXPECT_NEAR(1.0, shape.PDF(ctx, wi) / ss.value().pdf, 5e-3, "sample " + i);
                    ++checkedSamples;
                }
            }
            EXPECT_GT(checkedSamples, 100);
        }

        [TEST("Blobby", "OperationsOnValues")]
        public static void OperationsOnValues()
        {
            // two ellipsoids and a constant: the value at a point is written out by hand
            float[] floats = Join(Matrix(1, 1, 1, 0, 0, 0), Matrix(1, 1, 1, 0.5, 0, 0), new float[] { 0.25f });
            int[] leaves = { 1001, 0, 1001, 16, 1000, 32 };
            double x = 0.3, y = 0.1, z = -0.2;
            double a = BlobbyField.Profile(x * x + y * y + z * z), b = BlobbyField.Profile((x - 0.5) * (x - 0.5) + y * y + z * z), c = 0.25;
            int[][] programs =
            {
                new int[] { 0, 3, 0, 1, 2 }, new int[] { 1, 3, 0, 1, 2 }, new int[] { 2, 2, 0, 1 }, new int[] { 3, 2, 0, 1 }, new int[] { 4, 2, 0, 1 },
                new int[] { 5, 2, 0, 2 }, new int[] { 6, 1, 1 }, new int[] { 7, 1, 0 }
            };
            double[] expected = { a + b + c, a * b * c, Math.Max(a, b), Math.Min(a, b), a - b, a / c, -b, a };
            for (int i = 0; i < programs.Length; ++i)
            {
                List<int> code = new List<int>(leaves);
                code.AddRange(programs[i]);
                BlobbyField field = Field(3, code.ToArray(), floats);
                EXPECT_NEAR(expected[i], field.Value(x, y, z), 1e-6, "operation " + programs[i][0]);
            }
            // a chain: (a + b) * c - a, the operations numbered from nleaf
            BlobbyField chain = Field(3, new int[] { 1001, 0, 1001, 16, 1000, 32, 0, 2, 0, 1, 1, 2, 3, 2, 4, 2, 4, 0 }, floats);
            EXPECT_NEAR((a + b) * c - a, chain.Value(x, y, z), 1e-6, "chain");
            // 2 / 0 is 0
            BlobbyField zero = Field(3, new int[] { 1001, 0, 1001, 16, 1000, 32, 4, 2, 2, 2, 5, 2, 0, 3 }, floats);
            EXPECT_NEAR(0.0, zero.Value(x, y, z), 1e-12, "division by zero");
        }

        [TEST("Blobby", "InvalidPrograms")]
        public static void InvalidPrograms()
        {
            float[] floats = Matrix(1, 1, 1, 0, 0, 0);
            string error;
            EXPECT_TRUE(BlobbyField.Parse(2, new int[] { 1001, 0 }, floats, Threshold, out error) == null && error != null, "too few leaves");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1001, 1 }, floats, Threshold, out error) == null, "matrix out of the floats");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1001, 0, 0, 2, 0, 5 }, floats, Threshold, out error) == null, "operand that does not exist");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1001, 0, 4, 1, 0 }, floats, Threshold, out error) == null, "subtract with one operand");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1005, 0 }, floats, Threshold, out error) == null, "unknown leaf");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1001, 0, 9, 1, 0 }, floats, Threshold, out error) == null, "unknown operation");
            EXPECT_TRUE(BlobbyField.Parse(1, new int[] { 1001, 0 }, new float[16], Threshold, out error) == null, "singular matrix");
        }

        // Rays against the brute-force march of the field of a program
        private static void CompareWithMarch(string label, BlobbyField field, Transform placement, double cx, double cy, double cz, double aim, int rays, uint seed)
        {
            BlobbyShape shape = Shape_(field, placement, false);
            Transform inverse = Inverse(placement);
            Random rng = new Random((int)seed);
            int marchHits = 0, ours = 0, mismatches = 0;
            for (int i = 0; i < rays; ++i)
            {
                Ray ray = RandomRay(rng, cx, cy, cz, 6, aim);
                Point3f o = inverse.Apply(ray.o);
                Vector3f d = inverse.Apply(ray.d);
                Ray local = new Ray(o, d);
                double tHit;
                // the march covers the ray from the origin until it has left the bound for sure
                bool marched = March(field.Value, local, 14 * Math.Max(1, pbrt.Length(d)), 2e-4 / Math.Max(1, pbrt.Length(d)), out tHit);
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                if (si.has_value())
                {
                    ++ours;
                }
                if (marched)
                {
                    ++marchHits;
                    if (!si.has_value())
                    {
                        ++mismatches;
                        continue;
                    }
                    EXPECT_NEAR(tHit, si.value().tHit, 3e-5 * Math.Max(1, tHit), label + " t, ray " + i);
                    EXPECT_TRUE(shape.IntersectP(ray), label + " IntersectP, ray " + i);
                }
                else if (si.has_value())
                {
                    // the march has a step: a thin sliver can slip between its samples; a hit that it misses must be a grazing one
                    Point3f p = inverse.Apply(si.value().intr.p());
                    Normal3f n = si.value().intr.n;
                    if (Math.Abs(pbrt.Dot(d, new Vector3f(n.x, n.y, n.z))) > 0.05)
                    {
                        ++mismatches;
                    }
                }
            }
            EXPECT_GT(marchHits, rays / 10, label + " rays that hit");
            EXPECT_EQ(0, mismatches, label + " rays with a different hit or miss than the march (" + ours + " hits of ours, " + marchHits + " of the march)");
        }

        [TEST("Blobby", "SumOfBlobsAgainstMarch")]
        public static void SumOfBlobsAgainstMarch()
        {
            // five blobs that overlap and blend (the sum is the metaball of the classics)
            List<float> floats = new List<float>();
            List<int> code = new List<int>();
            double[][] blobs =
            {
                new double[] { 1.0, 1.0, 1.0, 0, 0, 0 }, new double[] { 0.8, 0.8, 0.8, 0.9, 0.2, 0 }, new double[] { 0.7, 0.7, 0.7, -0.6, 0.7, 0.3 },
                new double[] { 0.9, 0.6, 0.6, 0.1, -0.9, 0.2 }, new double[] { 0.6, 0.6, 0.6, 0.4, 0.3, -0.9 }
            };
            for (int i = 0; i < blobs.Length; ++i)
            {
                code.Add(1001);
                code.Add(16 * i);
                floats.AddRange(Matrix(blobs[i][0], blobs[i][1], blobs[i][2], blobs[i][3], blobs[i][4], blobs[i][5]));
            }
            code.Add(0);
            code.Add(blobs.Length);
            for (int i = 0; i < blobs.Length; ++i)
            {
                code.Add(i);
            }
            BlobbyField field = Field(blobs.Length, code.ToArray(), floats.ToArray());
            CompareWithMarch("sum", field, new Transform(), 0.2, 0.1, 0, 1.2, 1500, 21);
            Transform placement = Translate(new Vector3f(1, -2, 3)) * Rotate(40, new Vector3f(1, 1, 0)) * Scale(1.3f, 0.8f, 1.1f);
            Point3f c = placement.Apply(new Point3f(0.2f, 0.1f, 0));
            CompareWithMarch("sum placed", field, placement, c.x, c.y, c.z, 1.5, 1200, 22);
        }

        [TEST("Blobby", "OperationsAgainstMarch")]
        public static void OperationsAgainstMarch()
        {
            // (A max B) - (C min D) / (E + 0.5), negated and multiplied: every operation is on the path to the field
            List<float> floats = new List<float>();
            double[][] blobs =
            {
                new double[] { 1.0, 1.0, 1.0, 0, 0, 0 }, new double[] { 0.9, 0.9, 0.9, 0.8, 0, 0.2 }, new double[] { 1.1, 0.8, 0.8, 0.1, 0.3, 0 },
                new double[] { 0.8, 1.0, 0.8, 0.3, -0.2, 0.3 }, new double[] { 0.7, 0.7, 0.7, 0, 0.5, 0.3 }
            };
            List<int> code = new List<int>();
            for (int i = 0; i < blobs.Length; ++i)
            {
                code.Add(1001);
                code.Add(16 * i);
                floats.AddRange(Matrix(blobs[i][0], blobs[i][1], blobs[i][2], blobs[i][3], blobs[i][4], blobs[i][5]));
            }
            code.Add(1000);
            code.Add(16 * blobs.Length);
            floats.Add(0.5f);
            // 5: constant is leaf 5; values: 0..5 leaves, 6 = max(A,B), 7 = min(C,D), 8 = E + const, 9 = 7 / 8, 10 = 6 - 9, 11 = negate(10), 12 = 11 * A + ...
            code.AddRange(new int[] { 2, 2, 0, 1 });
            code.AddRange(new int[] { 3, 2, 2, 3 });
            code.AddRange(new int[] { 0, 2, 4, 5 });
            code.AddRange(new int[] { 5, 2, 7, 8 });
            code.AddRange(new int[] { 4, 2, 6, 9 });
            code.AddRange(new int[] { 6, 1, 10 });
            code.AddRange(new int[] { 0, 2, 11, 6 });
            code.AddRange(new int[] { 7, 1, 12 });
            BlobbyField field = Field(6, code.ToArray(), floats.ToArray());
            CompareWithMarch("operations", field, new Transform(), 0.3, 0.1, 0.1, 1.3, 1500, 31);
        }

        [TEST("Blobby", "CapsuleAgainstClosedForm")]
        public static void CapsuleAgainstClosedForm()
        {
            // a segment from (0, 0, -1) to (0, 0, 1) of radius 1: the surface is the capsule of radius r0
            double r0 = SurfaceRadius();
            float[] floats = Join(new float[] { 0, 0, -1, 0, 0, 1, 1 }, Matrix(1, 1, 1, 0, 0, 0));
            BlobbyField field = Field(1, new int[] { 1002, 0 }, floats);
            BlobbyShape shape = Shape_(field, new Transform(), false);
            Random rng = new Random(8);
            int hits = 0;
            for (int i = 0; i < 4000; ++i)
            {
                Ray ray = RandomRay(rng, 0, 0, 0, 5, 1.8);
                double tHit;
                Func<double, double, double, double> f = (x, y, z) =>
                {
                    double zc = Math.Max(-1, Math.Min(1, z));
                    double d2 = x * x + y * y + (z - zc) * (z - zc);
                    return d2 < r0 * r0 ? 1 : 0;
                };
                bool marched = March(f, ray, 12, 1e-3, out tHit);
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                if (marched)
                {
                    ++hits;
                    ASSERT_TRUE(si.has_value(), "ray " + i);
                    // the step function has no root to bisect on: refine the capsule by its distance function
                    double a = Math.Max(0, tHit - 2e-3), bb = tHit + 2e-3;
                    for (int k = 0; k < 60; ++k)
                    {
                        double m = 0.5 * (a + bb);
                        double x = ray.o.x + m * ray.d.x, y = ray.o.y + m * ray.d.y, z = ray.o.z + m * ray.d.z;
                        double zc = Math.Max(-1, Math.Min(1, z));
                        bool inside = x * x + y * y + (z - zc) * (z - zc) < r0 * r0;
                        if (inside)
                        {
                            bb = m;
                        }
                        else
                        {
                            a = m;
                        }
                    }
                    EXPECT_NEAR(0.5 * (a + bb), si.value().tHit, 3e-5, "t, ray " + i);
                }
            }
            EXPECT_GT(hits, 800);
            double expectedArea = 2 * Math.PI * r0 * 2 + 4 * Math.PI * r0 * r0;
            EXPECT_NEAR(expectedArea, shape.Area(), 0.012 * expectedArea, "capsule area");
        }

        [TEST("Blobby", "UnionOfTwoSpheresSampling")]
        public static void UnionOfTwoSpheresSampling()
        {
            // the maximum of two blobs is the union of two spheres of radius 1 with centers 1 apart: the area is 6 pi and the share of the area is known per slab
            double r0 = SurfaceRadius();
            double scale = (float)(1.0 / r0);
            float[] floats = Join(Matrix(scale, scale, scale, 0, 0, 0), Matrix(scale, scale, scale, 1, 0, 0));
            BlobbyField field = Field(2, new int[] { 1001, 0, 1001, 16, 2, 2, 0, 1 }, floats);
            BlobbyShape shape = Shape_(field, new Transform(), false);
            EXPECT_NEAR(6 * Math.PI, shape.Area(), 0.012 * 6 * Math.PI, "area of the union");
            Random rng = new Random(77);
            int count = 30000;
            int[] bins = new int[4];
            for (int i = 0; i < count; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                double x = ss.value().intr.p().x;
                ++bins[x < -0.5 ? 0 : x < 0.5 ? 1 : x < 1.5 ? 2 : 3];
            }
            double[] share = { 1.0 / 6, 1.0 / 3, 1.0 / 3, 1.0 / 6 };
            for (int k = 0; k < 4; ++k)
            {
                double se = Math.Sqrt(share[k] * (1 - share[k]) / count);
                EXPECT_LT(Math.Abs(bins[k] / (double)count - share[k]) / se, 4.5, "share of slab " + k + ": " + bins[k] / (double)count + " vs " + share[k]);
            }
            // no sample lies inside either sphere (they are on the union)
            for (int i = 0; i < 2000; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                Point3f p = ss.value().intr.p();
                double da = Math.Sqrt((double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z);
                double db = Math.Sqrt((p.x - 1.0) * (p.x - 1.0) + (double)p.y * p.y + (double)p.z * p.z);
                EXPECT_GT(Math.Min(da, db), 1 - 5e-4);
                EXPECT_LT(Math.Min(da, db), 1 + 5e-4);
            }
        }

        [TEST("Blobby", "OrientationUnderMirror")]
        public static void OrientationUnderMirror()
        {
            double r0 = SurfaceRadius();
            BlobbyField field = Field(1, new int[] { 1001, 0 }, Matrix(1, 1, 1, 0, 0, 0));
            Transform mirror = Translate(new Vector3f(2, 0, 0)) * Scale(-1, 1, 1);
            foreach (bool reverse in new bool[] { false, true })
            {
                BlobbyShape shape = Shape_(field, mirror, reverse);
                Ray ray = new Ray(new Point3f(2, 0, 5), new Vector3f(0, 0, -1));
                pstd.optional<ShapeIntersection> si = shape.Intersect(ray);
                ASSERT_TRUE(si.has_value());
                EXPECT_NEAR(5 - r0, si.value().tHit, 1e-4);
                double nz = si.value().intr.n.z;
                EXPECT_TRUE(reverse ? nz < -0.99 : nz > 0.99, "intersection normal, reverse " + reverse);
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f(0.3f, 0.6f));
                ASSERT_TRUE(ss.has_value());
                Point3f p = ss.value().intr.p();
                double outwardDot = ss.value().intr.n.x * (p.x - 2.0) + ss.value().intr.n.y * (double)p.y + ss.value().intr.n.z * (double)p.z;
                EXPECT_TRUE(reverse ? outwardDot < 0 : outwardDot > 0, "sample normal, reverse " + reverse);
            }
        }

        [TEST("Blobby", "ParserNames")]
        public static void ParserNames()
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            ParsedParameter nleaf = new ParsedParameter(null);
            nleaf.type = "integer";
            nleaf.name = "nleaf";
            nleaf.ints.push_back(1);
            list.Add(nleaf);
            ParsedParameter code = new ParsedParameter(null);
            code.type = "integer";
            code.name = "code";
            code.ints.push_back(1001);
            code.ints.push_back(0);
            list.Add(code);
            ParsedParameter flt = new ParsedParameter(null);
            flt.type = "float";
            flt.name = "flt";
            foreach (float v in Matrix(1, 1, 1, 0, 0, 0))
            {
                flt.floats.push_back(v);
            }
            list.Add(flt);
            pstd.vector<Shape> made = Shape.Create("blobby", new Transform(), new Transform(), false, new ParameterDictionary(list, RGBColorSpace.sRGB),
                                                  new Dictionary<string, FloatTexture>(), null, new Allocator());
            ASSERT_EQ(1, (int)made.size());
            EXPECT_TRUE(made[0].IntersectP(new Ray(new Point3f(0, 0, 5), new Vector3f(0, 0, -1))));
            EXPECT_TRUE(!made[0].IntersectP(new Ray(new Point3f(0, 3, 5), new Vector3f(0, 0, -1))));
        }
    }
}
