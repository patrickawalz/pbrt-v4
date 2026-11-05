// (not in pbrt-v4) Tests of the NURBS primitive (shapes_nurbs.cs): the Bezier decomposition against the Cox-de Boor evaluation, exact rational surfaces
// (a cylinder and a sphere with poles) against their equations, ray intersection against dense point sets, trimming against the analytic region, the
// area and the sampling of trimmed surfaces.

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class NurbsTests
    {
        // ---- the reference: the basis functions of Piegl and Tiller
        private static int FindSpan(int order, double[] knots, int n, double u)
        {
            int p = order - 1;
            if (u >= knots[n])
            {
                int k = n - 1;
                while (knots[k] >= knots[n])
                {
                    --k;
                }
                return k;
            }
            int low = p, high = n;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (u >= knots[mid])
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }
            return low;
        }

        private static double[] Basis(int order, double[] knots, int n, double u)
        {
            int p = order - 1;
            int span = FindSpan(order, knots, n, u);
            double[] N = new double[p + 1], left = new double[p + 1], right = new double[p + 1];
            N[0] = 1;
            for (int j = 1; j <= p; ++j)
            {
                left[j] = u - knots[span + 1 - j];
                right[j] = knots[span + j] - u;
                double saved = 0;
                for (int r = 0; r < j; ++r)
                {
                    double temp = N[r] / (right[r + 1] + left[j - r]);
                    N[r] = saved + right[r + 1] * temp;
                    saved = left[j - r] * temp;
                }
                N[j] = saved;
            }
            double[] result = new double[n];
            for (int i = 0; i <= p; ++i)
            {
                result[span - p + i] = N[i];
            }
            return result;
        }

        private sealed class Surface
        {
            public int nu, nv, uOrder, vOrder;
            public double[] uKnots, vKnots;
            public double[] homogeneous;  // ((i * nv) + j) * 4
            public double uMin, uMax, vMin, vMax;

            public double[] Point(double u, double v)
            {
                double[] bu = Basis(uOrder, uKnots, nu, u), bv = Basis(vOrder, vKnots, nv, v);
                double[] sum = new double[4];
                for (int i = 0; i < nu; ++i)
                {
                    if (bu[i] == 0)
                    {
                        continue;
                    }
                    for (int j = 0; j < nv; ++j)
                    {
                        double b = bu[i] * bv[j];
                        for (int c = 0; c < 4; ++c)
                        {
                            sum[c] += b * homogeneous[(i * nv + j) * 4 + c];
                        }
                    }
                }
                return new double[] { sum[0] / sum[3], sum[1] / sum[3], sum[2] / sum[3] };
            }

            public NurbsSurfaceData Build()
            {
                NurbsSurfaceData data = new NurbsSurfaceData();
                string error = data.Build(nu, uOrder, uKnots, nv, vOrder, vKnots, homogeneous, uMin, uMax, vMin, vMax);
                ASSERT_TRUE(error == null, error);
                return data;
            }
        }

        private static double[] RandomKnots(Random rng, int order, int n)
        {
            // clamped half of the time, free the other half; interior knots of multiplicity up to order - 1
            int count = n + order;
            double[] knots = new double[count];
            double value = 0;
            int i = 0;
            bool clamped = rng.Next(2) == 0;
            if (clamped)
            {
                for (; i < order; ++i)
                {
                    knots[i] = 0;
                }
            }
            while (i < count - (clamped ? order : 0))
            {
                value += 0.2 + rng.NextDouble();
                int repeat = Math.Min(1 + rng.Next(order - 1), count - (clamped ? order : 0) - i);
                for (int r = 0; r < repeat; ++r)
                {
                    knots[i++] = value;
                }
            }
            if (clamped)
            {
                value += 0.2 + rng.NextDouble();
                for (; i < count; ++i)
                {
                    knots[i] = value;
                }
            }
            return knots;
        }

        private static Surface RandomSurface(Random rng, bool rational, double bend)
        {
            Surface s = new Surface();
            s.uOrder = 2 + rng.Next(4);
            s.vOrder = 2 + rng.Next(4);
            s.nu = s.uOrder + rng.Next(4);
            s.nv = s.vOrder + rng.Next(4);
            s.uKnots = RandomKnots(rng, s.uOrder, s.nu);
            s.vKnots = RandomKnots(rng, s.vOrder, s.nv);
            while (!(s.uKnots[s.nu] > s.uKnots[s.uOrder - 1]))
            {
                s.uKnots = RandomKnots(rng, s.uOrder, s.nu);
            }
            while (!(s.vKnots[s.nv] > s.vKnots[s.vOrder - 1]))
            {
                s.vKnots = RandomKnots(rng, s.vOrder, s.nv);
            }
            s.homogeneous = new double[4 * s.nu * s.nv];
            for (int i = 0; i < s.nu; ++i)
            {
                for (int j = 0; j < s.nv; ++j)
                {
                    double w = rational ? 0.4 + 2 * rng.NextDouble() : 1;
                    double x = 2.0 * i / s.nu + bend * (rng.NextDouble() - 0.5);
                    double y = 2.0 * j / s.nv + bend * (rng.NextDouble() - 0.5);
                    double z = bend * (rng.NextDouble() - 0.5);
                    s.homogeneous[(i * s.nv + j) * 4] = x * w;
                    s.homogeneous[(i * s.nv + j) * 4 + 1] = y * w;
                    s.homogeneous[(i * s.nv + j) * 4 + 2] = z * w;
                    s.homogeneous[(i * s.nv + j) * 4 + 3] = w;
                }
            }
            double uLow = s.uKnots[s.uOrder - 1], uHigh = s.uKnots[s.nu], vLow = s.vKnots[s.vOrder - 1], vHigh = s.vKnots[s.nv];
            // a sub-range half of the time
            bool sub = rng.Next(2) == 0;
            s.uMin = sub ? uLow + 0.15 * (uHigh - uLow) : uLow;
            s.uMax = sub ? uHigh - 0.1 * (uHigh - uLow) : uHigh;
            s.vMin = sub ? vLow + 0.05 * (vHigh - vLow) : vLow;
            s.vMax = sub ? vHigh - 0.2 * (vHigh - vLow) : vHigh;
            return s;
        }

        [TEST("Nurbs", "DecompositionMatchesCoxDeBoor")]
        public static void DecompositionMatchesCoxDeBoor()
        {
            Random rng = new Random(17);
            double worst = 0, worstDerivative = 0;
            for (int trial = 0; trial < 80; ++trial)
            {
                Surface s = RandomSurface(rng, trial % 2 == 0, 1.0);
                NurbsSurfaceData data = s.Build();
                double[] p = new double[3], pu = new double[3], pv = new double[3];
                for (int k = 0; k < 30; ++k)
                {
                    double u = s.uMin + (s.uMax - s.uMin) * rng.NextDouble(), v = s.vMin + (s.vMax - s.vMin) * rng.NextDouble();
                    data.Evaluate(u, v, p, pu, pv);
                    double[] reference = s.Point(u, v);
                    for (int c = 0; c < 3; ++c)
                    {
                        worst = Math.Max(worst, Math.Abs(p[c] - reference[c]));
                    }
                    // derivatives against central differences of the reference (staying inside the range)
                    double h = 1e-6 * (s.uMax - s.uMin);
                    if (u - h > s.uMin && u + h < s.uMax && v - h > s.vMin && v + h < s.vMax)
                    {
                        double[] a = s.Point(u + h, v), b = s.Point(u - h, v), c2 = s.Point(u, v + h), d = s.Point(u, v - h);
                        for (int c = 0; c < 3; ++c)
                        {
                            // (skip the knots where the derivative is discontinuous: a lower continuity spans the two sides)
                            double fu = (a[c] - b[c]) / (2 * h), fv = (c2[c] - d[c]) / (2 * h);
                            worstDerivative = Math.Max(worstDerivative, Math.Min(Math.Abs(pu[c] - fu), 1e9) / (1 + Math.Abs(fu)) * 1e-3);
                            worstDerivative = Math.Max(worstDerivative, Math.Min(Math.Abs(pv[c] - fv), 1e9) / (1 + Math.Abs(fv)) * 1e-3);
                        }
                    }
                }
            }
            EXPECT_LT(worst, 1e-9, "Bezier cells against Cox-de Boor");
            EXPECT_LT(worstDerivative, 1e-6, "derivatives against differences (1e-3 scaled)");
        }

        private static ParsedParameter Param(string type, string name, double[] values, int[] ints)
        {
            ParsedParameter p = new ParsedParameter(null);
            p.type = type;
            p.name = name;
            if (values != null)
            {
                foreach (double v in values)
                {
                    p.floats.push_back((Float)v);
                }
            }
            if (ints != null)
            {
                foreach (int v in ints)
                {
                    p.ints.push_back(v);
                }
            }
            return p;
        }

        // The "nupatch" shape of a surface, with trim curves given by the caller
        private static Shape CreateShape(Surface s, Transform t, bool reverse, List<ParsedParameter> trim)
        {
            List<ParsedParameter> list = new List<ParsedParameter>();
            // RenderMan lists the points with u varying fastest: Pw[(j * nu + i) * 4]
            double[] pw = new double[4 * s.nu * s.nv];
            for (int j = 0; j < s.nv; ++j)
            {
                for (int i = 0; i < s.nu; ++i)
                {
                    for (int c = 0; c < 4; ++c)
                    {
                        pw[(j * s.nu + i) * 4 + c] = s.homogeneous[(i * s.nv + j) * 4 + c];
                    }
                }
            }
            list.Add(Param("float", "Pw", pw, null));
            list.Add(Param("integer", "nu", null, new int[] { s.nu }));
            list.Add(Param("integer", "nv", null, new int[] { s.nv }));
            list.Add(Param("integer", "uorder", null, new int[] { s.uOrder }));
            list.Add(Param("integer", "vorder", null, new int[] { s.vOrder }));
            list.Add(Param("float", "uknots", s.uKnots, null));
            list.Add(Param("float", "vknots", s.vKnots, null));
            list.Add(Param("float", "umin", new double[] { s.uMin }, null));
            list.Add(Param("float", "umax", new double[] { s.uMax }, null));
            list.Add(Param("float", "vmin", new double[] { s.vMin }, null));
            list.Add(Param("float", "vmax", new double[] { s.vMax }, null));
            if (trim != null)
            {
                list.AddRange(trim);
            }
            ParameterDictionary dict = new ParameterDictionary(list, RGBColorSpace.sRGB);
            pstd.vector<Shape> shapes = Shape.Create("nupatch", t, Inverse(t), reverse, dict, new Dictionary<string, FloatTexture>(), null, new Allocator());
            ASSERT_EQ(1, (int)shapes.size());
            return shapes[0];
        }

        private static double[] Direction(Random rng)
        {
            double[] d = { rng.NextDouble() - 0.5, rng.NextDouble() - 0.5, rng.NextDouble() - 0.5 };
            double l = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
            return new double[] { d[0] / l, d[1] / l, d[2] / l };
        }

        [TEST("Nurbs", "RaysAgainstGenericSurfaces")]
        public static void RaysAgainstGenericSurfaces()
        {
            Random rng = new Random(29);
            Transform rigid = Translate(new Vector3f(0.3f, -0.2f, 0.5f)) * Rotate(33, Normalize(new Vector3f(1, 0.5f, -1)));
            int totalHits = 0, totalMisses = 0;
            for (int trial = 0; trial < 24; ++trial)
            {
                Surface s = RandomSurface(rng, trial % 2 == 0, 0.8);
                Transform t = trial % 3 == 0 ? rigid : new Transform();
                Shape shape = CreateShape(s, t, false, null);
                // dense points of the surface
                int resolution = 120;
                List<double[]> dense = new List<double[]>();
                double[] low = { 1e30, 1e30, 1e30 }, high = { -1e30, -1e30, -1e30 };
                for (int i = 0; i <= resolution; ++i)
                {
                    for (int j = 0; j <= resolution; ++j)
                    {
                        double[] q = s.Point(s.uMin + (s.uMax - s.uMin) * i / resolution, s.vMin + (s.vMax - s.vMin) * j / resolution);
                        Point3f r = t.Apply(new Point3f((Float)q[0], (Float)q[1], (Float)q[2]));
                        double[] w = { r.x, r.y, r.z };
                        dense.Add(w);
                        for (int c = 0; c < 3; ++c)
                        {
                            low[c] = Math.Min(low[c], w[c]);
                            high[c] = Math.Max(high[c], w[c]);
                        }
                    }
                }
                double size = Math.Sqrt(Math.Pow(high[0] - low[0], 2) + Math.Pow(high[1] - low[1], 2) + Math.Pow(high[2] - low[2], 2));
                double spacing = size / (resolution * 0.4);
                double[] center = { 0.5 * (low[0] + high[0]), 0.5 * (low[1] + high[1]), 0.5 * (low[2] + high[2]) };
                int aimedMisses = 0, violations = 0, hits = 0, misses = 0, offSurface = 0;
                for (int k = 0; k < 300; ++k)
                {
                    // aimed at a point of the surface
                    double u = s.uMin + (s.uMax - s.uMin) * rng.NextDouble(), v = s.vMin + (s.vMax - s.vMin) * rng.NextDouble();
                    double[] q = s.Point(u, v);
                    Point3f target = t.Apply(new Point3f((Float)q[0], (Float)q[1], (Float)q[2]));
                    double[] dir = Direction(rng);
                    Ray aimed = new Ray(new Point3f((Float)(target.x - 2 * size * dir[0]), (Float)(target.y - 2 * size * dir[1]), (Float)(target.z - 2 * size * dir[2])),
                                        new Vector3f((Float)dir[0], (Float)dir[1], (Float)dir[2]));
                    pstd.optional<ShapeIntersection> si = shape.Intersect(aimed);
                    if (!si.has_value())
                    {
                        // a ray that grazes the surface (within 0.02 of its tangent plane) may miss it after the rounding of its origin to float
                        double h = 1e-6 * (s.uMax - s.uMin);
                        double[] a = s.Point(Math.Min(u + h, s.uMax), v), b = s.Point(Math.Max(u - h, s.uMin), v), c = s.Point(u, Math.Min(v + h, s.vMax)), e = s.Point(u, Math.Max(v - h, s.vMin));
                        double[] du = { a[0] - b[0], a[1] - b[1], a[2] - b[2] }, dv = { c[0] - e[0], c[1] - e[1], c[2] - e[2] };
                        Vector3f n = Normalize(t.Apply(new Vector3f((Float)(du[1] * dv[2] - du[2] * dv[1]), (Float)(du[2] * dv[0] - du[0] * dv[2]), (Float)(du[0] * dv[1] - du[1] * dv[0]))));
                        if (Math.Abs(n.x * dir[0] + n.y * dir[1] + n.z * dir[2]) > 0.02)
                        {
                            ++aimedMisses;
                        }
                    }
                    else
                    {
                        // the hit is on the ray and on the surface at its uv
                        Point3f hit = si.value().intr.p();
                        double hu = s.uMin + si.value().intr.uv.x * (s.uMax - s.uMin), hv = s.vMin + si.value().intr.uv.y * (s.vMax - s.vMin);
                        double[] at = s.Point(hu, hv);
                        Point3f expected = t.Apply(new Point3f((Float)at[0], (Float)at[1], (Float)at[2]));
                        if (Distance(hit, expected) > 2e-4 * size)
                        {
                            ++offSurface;
                        }
                    }
                    // a random ray
                    double[] origin = { center[0] + 1.5 * size * (rng.NextDouble() - 0.5), center[1] + 1.5 * size * (rng.NextDouble() - 0.5), center[2] + 1.5 * size * (rng.NextDouble() - 0.5) };
                    double[] aim = { center[0] + 0.7 * size * (rng.NextDouble() - 0.5), center[1] + 0.7 * size * (rng.NextDouble() - 0.5), center[2] + 0.7 * size * (rng.NextDouble() - 0.5) };
                    double[] d = { aim[0] - origin[0], aim[1] - origin[1], aim[2] - origin[2] };
                    double dl = Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]);
                    for (int c = 0; c < 3; ++c)
                    {
                        d[c] /= dl;
                    }
                    double nearest = double.PositiveInfinity;
                    foreach (double[] w in dense)
                    {
                        double tt = (w[0] - origin[0]) * d[0] + (w[1] - origin[1]) * d[1] + (w[2] - origin[2]) * d[2];
                        double dist = tt < 0 ? Math.Sqrt(Math.Pow(w[0] - origin[0], 2) + Math.Pow(w[1] - origin[1], 2) + Math.Pow(w[2] - origin[2], 2))
                                             : Math.Sqrt(Math.Pow(w[0] - origin[0] - tt * d[0], 2) + Math.Pow(w[1] - origin[1] - tt * d[1], 2) + Math.Pow(w[2] - origin[2] - tt * d[2], 2));
                        nearest = Math.Min(nearest, dist);
                    }
                    Ray ray = new Ray(new Point3f((Float)origin[0], (Float)origin[1], (Float)origin[2]), new Vector3f((Float)d[0], (Float)d[1], (Float)d[2]));
                    bool hit2 = shape.IntersectP(ray);
                    if (hit2)
                    {
                        ++hits;
                    }
                    else
                    {
                        ++misses;
                    }
                    if (nearest > spacing && hit2)
                    {
                        ++violations;
                    }
                    if (nearest < 2e-4 * size && !hit2)
                    {
                        ++violations;
                    }
                }
                EXPECT_EQ(0, aimedMisses, "trial " + trial + " rays aimed at the surface that miss");
                EXPECT_EQ(0, offSurface, "trial " + trial + " hits that are not the surface at their uv");
                EXPECT_EQ(0, violations, "trial " + trial + " rays contradicting the dense points");
                totalHits += hits;
                totalMisses += misses;
            }
            EXPECT_GT(totalHits, 200, "random rays that hit");
            EXPECT_GT(totalMisses, 200, "random rays that miss");
        }

        // ---- exact rational surfaces: a cylinder and a sphere
        private static void Circle(out double[] x, out double[] y, out double[] w, out double[] knots)
        {
            double r = Math.Sqrt(0.5);
            x = new double[] { 1, 1, 0, -1, -1, -1, 0, 1, 1 };
            y = new double[] { 0, 1, 1, 1, 0, -1, -1, -1, 0 };
            w = new double[] { 1, r, 1, r, 1, r, 1, r, 1 };
            knots = new double[] { 0, 0, 0, 0.25, 0.25, 0.5, 0.5, 0.75, 0.75, 1, 1, 1 };
        }

        private static Surface Cylinder(double radius, double height)
        {
            Circle(out double[] cx, out double[] cy, out double[] cw, out double[] ck);
            Surface s = new Surface();
            s.nu = 9;
            s.nv = 2;
            s.uOrder = 3;
            s.vOrder = 2;
            s.uKnots = ck;
            s.vKnots = new double[] { 0, 0, 1, 1 };
            s.homogeneous = new double[4 * 18];
            for (int i = 0; i < 9; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    s.homogeneous[(i * 2 + j) * 4] = radius * cx[i] * cw[i];
                    s.homogeneous[(i * 2 + j) * 4 + 1] = radius * cy[i] * cw[i];
                    s.homogeneous[(i * 2 + j) * 4 + 2] = (j == 0 ? -0.5 * height : 0.5 * height) * cw[i];
                    s.homogeneous[(i * 2 + j) * 4 + 3] = cw[i];
                }
            }
            s.uMin = 0;
            s.uMax = 1;
            s.vMin = 0;
            s.vMax = 1;
            return s;
        }

        private static Surface Sphere(double radius)
        {
            Circle(out double[] cx, out double[] cy, out double[] cw, out double[] ck);
            double r = Math.Sqrt(0.5);
            double[] rr = { 0, 1, 1, 1, 0 }, zz = { -1, -1, 0, 1, 1 }, ww = { 1, r, 1, r, 1 };
            Surface s = new Surface();
            s.nu = 9;
            s.nv = 5;
            s.uOrder = 3;
            s.vOrder = 3;
            s.uKnots = ck;
            s.vKnots = new double[] { 0, 0, 0, 0.5, 0.5, 1, 1, 1 };
            s.homogeneous = new double[4 * 45];
            for (int i = 0; i < 9; ++i)
            {
                for (int j = 0; j < 5; ++j)
                {
                    double w = cw[i] * ww[j];
                    s.homogeneous[(i * 5 + j) * 4] = radius * cx[i] * rr[j] * w;
                    s.homogeneous[(i * 5 + j) * 4 + 1] = radius * cy[i] * rr[j] * w;
                    s.homogeneous[(i * 5 + j) * 4 + 2] = radius * zz[j] * w;
                    s.homogeneous[(i * 5 + j) * 4 + 3] = w;
                }
            }
            s.uMin = 0;
            s.uMax = 1;
            s.vMin = 0;
            s.vMax = 1;
            return s;
        }

        [TEST("Nurbs", "CylinderAndSphereAreExact")]
        public static void CylinderAndSphereAreExact()
        {
            Random rng = new Random(4);
            Transform nonUniform = Translate(new Vector3f(0.3f, 0.2f, -0.4f)) * Rotate(23, Normalize(new Vector3f(0.5f, 0.1f, 1)));
            foreach (bool sphere in new bool[] { false, true })
            {
                double radius = 1.7, height = 2.5;
                Surface s = sphere ? Sphere(radius) : Cylinder(radius, height);
                Shape shape = CreateShape(s, nonUniform, false, null);
                Transform inverse = Inverse(nonUniform);
                double expectedArea = sphere ? 4 * Math.PI * radius * radius : 2 * Math.PI * radius * height;
                EXPECT_NEAR(1.0, shape.Area() / expectedArea, 2e-4, (sphere ? "sphere" : "cylinder") + " area");
                int wrong = 0, hits = 0, expectedHits = 0, radial = 0;
                for (int k = 0; k < 3000; ++k)
                {
                    Point3f origin = nonUniform.Apply(new Point3f((Float)(6 * (rng.NextDouble() - 0.5)), (Float)(6 * (rng.NextDouble() - 0.5)), (Float)(6 * (rng.NextDouble() - 0.5))));
                    Vector3f direction = Normalize(nonUniform.Apply(new Vector3f((Float)(rng.NextDouble() - 0.5), (Float)(rng.NextDouble() - 0.5), (Float)(rng.NextDouble() - 0.5))));
                    // the analytic first hit in object space
                    Point3f o = inverse.Apply(origin);
                    Vector3f d = inverse.Apply(direction);
                    double a = d.x * d.x + d.y * d.y + (sphere ? d.z * d.z : 0), b = 2 * (o.x * d.x + o.y * d.y + (sphere ? o.z * d.z : 0));
                    double c = o.x * o.x + o.y * o.y + (sphere ? o.z * o.z : 0) - radius * radius;
                    double disc = b * b - 4 * a * c;
                    double best = double.PositiveInfinity;
                    if (disc >= 0 && a > 0)
                    {
                        double sq = Math.Sqrt(disc);
                        foreach (double tt in new double[] { (-b - sq) / (2 * a), (-b + sq) / (2 * a) })
                        {
                            double z = o.z + tt * d.z;
                            if (tt > 1e-6 && (sphere || Math.Abs(z) <= 0.5 * height) && tt < best)
                            {
                                best = tt;
                            }
                        }
                    }
                    // skip the rays that graze, or end at the rim of the cylinder
                    if (disc >= 0 && disc < 1e-3 * b * b)
                    {
                        continue;
                    }
                    if (!sphere && !double.IsPositiveInfinity(best) && Math.Abs(Math.Abs(o.z + best * d.z) - 0.5 * height) < 1e-3)
                    {
                        continue;
                    }
                    pstd.optional<ShapeIntersection> si = shape.Intersect(new Ray(origin, direction));
                    bool expected = !double.IsPositiveInfinity(best);
                    if (expected)
                    {
                        ++expectedHits;
                    }
                    if (si.has_value() != expected)
                    {
                        ++wrong;
                        continue;
                    }
                    if (expected)
                    {
                        ++hits;
                        Point3f q = inverse.Apply(si.value().intr.p());
                        double rho = sphere ? Math.Sqrt((double)q.x * q.x + (double)q.y * q.y + (double)q.z * q.z) : Math.Sqrt((double)q.x * q.x + (double)q.y * q.y);
                        if (Math.Abs(rho - radius) > 2e-5 || Math.Abs(si.value().tHit - best) > 2e-4)
                        {
                            ++radial;
                        }
                        // the normal is radial (up to the sign)
                        Normal3f nn = si.value().intr.n;
                        Vector3f nObject = Normalize(inverse.Apply(new Vector3f(nn.x, nn.y, nn.z)));
                        double nr = Math.Abs(nObject.x * q.x + nObject.y * q.y + (sphere ? nObject.z * q.z : 0)) / radius;
                        if (nr < 0.98 && !(Math.Abs(q.z) > 0.999 * radius && sphere))
                        {
                            ++radial;
                        }
                    }
                }
                string label = sphere ? "sphere" : "cylinder";
                EXPECT_EQ(0, wrong, label + " hit or miss against the equation (" + expectedHits + " expected hits)");
                EXPECT_EQ(0, radial, label + " hit points off the surface or normals not radial");
                EXPECT_GT(hits, 300, label + " hits");
            }
        }

        // ---- trimming
        private static Surface Flat()
        {
            Surface s = new Surface();
            s.nu = 2;
            s.nv = 2;
            s.uOrder = 2;
            s.vOrder = 2;
            s.uKnots = new double[] { 0, 0, 1, 1 };
            s.vKnots = new double[] { 0, 0, 1, 1 };
            s.homogeneous = new double[16];
            for (int i = 0; i < 2; ++i)
            {
                for (int j = 0; j < 2; ++j)
                {
                    s.homogeneous[(i * 2 + j) * 4] = 3.0 * i;
                    s.homogeneous[(i * 2 + j) * 4 + 1] = 2.0 * j;
                    s.homogeneous[(i * 2 + j) * 4 + 2] = 0;
                    s.homogeneous[(i * 2 + j) * 4 + 3] = 1;
                }
            }
            s.uMin = 0;
            s.uMax = 1;
            s.vMin = 0;
            s.vMax = 1;
            return s;
        }

        // Trim curves: a list of loops, each a list of polylines closed by straight segments, or circles
        private sealed class TrimBuilder
        {
            public List<int> perLoop = new List<int>(), orders = new List<int>(), counts = new List<int>();
            public List<double> knots = new List<double>(), mins = new List<double>(), maxs = new List<double>(), us = new List<double>(), vs = new List<double>(), ws = new List<double>();

            public void Polygon(double[][] corners)
            {
                perLoop.Add(corners.Length);
                for (int i = 0; i < corners.Length; ++i)
                {
                    double[] a = corners[i], b = corners[(i + 1) % corners.Length];
                    orders.Add(2);
                    counts.Add(2);
                    knots.AddRange(new double[] { 0, 0, 1, 1 });
                    mins.Add(0);
                    maxs.Add(1);
                    us.AddRange(new double[] { a[0], b[0] });
                    vs.AddRange(new double[] { a[1], b[1] });
                    ws.AddRange(new double[] { 1, 1 });
                }
            }

            public void Circle(double cx, double cy, double radius, bool counterclockwise)
            {
                NurbsTests.Circle(out double[] x, out double[] y, out double[] w, out double[] k);
                perLoop.Add(1);
                orders.Add(3);
                counts.Add(9);
                knots.AddRange(k);
                mins.Add(0);
                maxs.Add(1);
                for (int i = 0; i < 9; ++i)
                {
                    int idx = counterclockwise ? i : 8 - i;
                    us.Add((cx + radius * x[idx]) * w[idx]);
                    vs.Add((cy + radius * y[idx]) * w[idx]);
                    ws.Add(w[idx]);
                }
            }

            public List<ParsedParameter> Parameters()
            {
                List<ParsedParameter> list = new List<ParsedParameter>();
                list.Add(Param("integer", "trimncurves", null, perLoop.ToArray()));
                list.Add(Param("integer", "trimorder", null, orders.ToArray()));
                list.Add(Param("integer", "trimn", null, counts.ToArray()));
                list.Add(Param("float", "trimknots", knots.ToArray(), null));
                list.Add(Param("float", "trimmin", mins.ToArray(), null));
                list.Add(Param("float", "trimmax", maxs.ToArray(), null));
                list.Add(Param("float", "trimu", us.ToArray(), null));
                list.Add(Param("float", "trimv", vs.ToArray(), null));
                list.Add(Param("float", "trimw", ws.ToArray(), null));
                return list;
            }
        }

        // A square with a circular hole, and a second island: the analytic visible region in (u, v)
        private static bool VisibleRegion(double u, double v)
        {
            bool inSquare = u > 0.1 && u < 0.9 && v > 0.1 && v < 0.9;
            bool inHole = (u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5) < 0.04;
            return inSquare && !inHole;
        }

        [TEST("Nurbs", "TrimmedSurfaceHitsAndArea")]
        public static void TrimmedSurfaceHitsAndArea()
        {
            Random rng = new Random(6);
            TrimBuilder trim = new TrimBuilder();
            trim.Polygon(new double[][] { new double[] { 0.1, 0.1 }, new double[] { 0.9, 0.1 }, new double[] { 0.9, 0.9 }, new double[] { 0.1, 0.9 } });
            trim.Circle(0.5, 0.5, 0.2, false);
            Surface s = Flat();
            Transform t = Translate(new Vector3f(0.3f, -0.2f, 0.5f)) * Rotate(33, Normalize(new Vector3f(1, 0.5f, -1)));
            Shape shape = CreateShape(s, t, false, trim.Parameters());
            double expectedArea = 6 * (0.64 - Math.PI * 0.04);
            EXPECT_NEAR(1.0, shape.Area() / expectedArea, 2e-4, "trimmed area");
            int wrong = 0, hits = 0, misses = 0;
            for (int k = 0; k < 4000; ++k)
            {
                double u = rng.NextDouble(), v = rng.NextDouble();
                double r = Math.Min(Math.Min(Math.Abs(u - 0.1), Math.Abs(u - 0.9)), Math.Min(Math.Abs(v - 0.1), Math.Abs(v - 0.9)));
                double hole = Math.Abs(Math.Sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)) - 0.2);
                if (Math.Min(r, hole) < 1e-3)
                {
                    continue;
                }
                Point3f target = t.Apply(new Point3f((Float)(3 * u), (Float)(2 * v), 0));
                Vector3f d = Normalize(t.Apply(new Vector3f((Float)(0.3 * (rng.NextDouble() - 0.5)), (Float)(0.3 * (rng.NextDouble() - 0.5)), 1)));
                pstd.optional<ShapeIntersection> si = shape.Intersect(new Ray(target - 2 * d, d));
                bool expected = VisibleRegion(u, v);
                if (si.has_value() != expected)
                {
                    ++wrong;
                }
                else if (expected)
                {
                    ++hits;
                    if (Distance(si.value().intr.p(), target) > 1e-4f || Math.Abs(si.value().intr.uv.x - u) > 1e-4 || Math.Abs(si.value().intr.uv.y - v) > 1e-4)
                    {
                        ++wrong;
                    }
                }
                else
                {
                    ++misses;
                }
            }
            EXPECT_EQ(0, wrong, "trimmed hits against the region (" + hits + " hits, " + misses + " misses)");
            EXPECT_GT(hits, 1000, "hits");
            EXPECT_GT(misses, 500, "misses");

            // a clockwise loop alone shows nothing; two counterclockwise loops show both islands; a hole with the wrong orientation does not cut
            TrimBuilder clockwise = new TrimBuilder();
            clockwise.Polygon(new double[][] { new double[] { 0.1, 0.1 }, new double[] { 0.1, 0.9 }, new double[] { 0.9, 0.9 }, new double[] { 0.9, 0.1 } });
            Shape none = CreateShape(s, new Transform(), false, clockwise.Parameters());
            EXPECT_TRUE(!none.IntersectP(new Ray(new Point3f(1.5f, 1.0f, 1), new Vector3f(0, 0, -1))), "inside a clockwise loop");
            TrimBuilder islands = new TrimBuilder();
            islands.Polygon(new double[][] { new double[] { 0.1, 0.1 }, new double[] { 0.4, 0.1 }, new double[] { 0.4, 0.9 }, new double[] { 0.1, 0.9 } });
            islands.Circle(0.75, 0.5, 0.15, true);
            Shape two = CreateShape(s, new Transform(), false, islands.Parameters());
            EXPECT_TRUE(two.IntersectP(new Ray(new Point3f(0.75f, 1.0f, 1), new Vector3f(0, 0, -1))), "first island");
            EXPECT_TRUE(two.IntersectP(new Ray(new Point3f(2.25f, 1.0f, 1), new Vector3f(0, 0, -1))), "second island");
            EXPECT_TRUE(!two.IntersectP(new Ray(new Point3f(1.5f, 1.0f, 1), new Vector3f(0, 0, -1))), "between the islands");
            EXPECT_NEAR(1.0, two.Area() / (6 * (0.3 * 0.8 + Math.PI * 0.0225)), 2e-4, "area of the islands");
        }

        [TEST("Nurbs", "TrimmedSampling")]
        public static void TrimmedSampling()
        {
            Random rng = new Random(15);
            TrimBuilder trim = new TrimBuilder();
            trim.Polygon(new double[][] { new double[] { 0.1, 0.1 }, new double[] { 0.9, 0.1 }, new double[] { 0.9, 0.9 }, new double[] { 0.1, 0.9 } });
            trim.Circle(0.5, 0.5, 0.2, false);
            Surface s = Flat();
            Shape shape = CreateShape(s, new Transform(), false, trim.Parameters());
            // E[f / pdf] against the integral of f = x + y^2 + 1 over the region (a midpoint rule in the parameters, with the area element 6)
            int grid = 3000;
            double integral = 0;
            for (int i = 0; i < grid; ++i)
            {
                for (int j = 0; j < grid; ++j)
                {
                    double u = (i + 0.5) / grid, v = (j + 0.5) / grid;
                    if (VisibleRegion(u, v))
                    {
                        integral += (3 * u + 4 * v * v + 1) * 6.0 / ((double)grid * grid);
                    }
                }
            }
            int n = 400000, outside = 0;
            double sum = 0, sumSquares = 0, inverseSum = 0, inverseSquares = 0;
            for (int i = 0; i < n; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                ASSERT_TRUE(ss.has_value());
                Point3f p = ss.value().intr.p();
                if (!VisibleRegion(p.x / 3, p.y / 2))
                {
                    ++outside;
                }
                double f = (p.x + p.y * p.y + 1) / ss.value().pdf;
                sum += f;
                sumSquares += f * f;
                double inverse = 1.0 / ss.value().pdf;
                inverseSum += inverse;
                inverseSquares += inverse * inverse;
            }
            EXPECT_EQ(0, outside, "samples in the trimmed away region");
            double mean = sum / n, error = Math.Sqrt(Math.Max(0, sumSquares / n - mean * mean) / n);
            EXPECT_LT(Math.Abs(mean - integral) / (error + 1e-3 * Math.Abs(integral) / 4), 4.0, "E[f / pdf] " + mean + " +- " + error + " against " + integral);
            double areaMean = inverseSum / n, areaError = Math.Sqrt(Math.Max(0, inverseSquares / n - areaMean * areaMean) / n);
            double expectedArea = 6 * (0.64 - Math.PI * 0.04);
            EXPECT_LT(Math.Abs(areaMean - expectedArea) / (areaError + 1e-4 * expectedArea / 4), 4.0, "E[1 / pdf] " + areaMean + " +- " + areaError + " against the area " + expectedArea);
            // the pdf of Sample(ctx) against PDF(ctx, wi), and its integral over the sphere of directions against the visible fraction
            ShapeSampleContext ctx = new ShapeSampleContext(new Point3fi(new Point3f(1.2f, 0.8f, 2.5f)), new Normal3f(0, 0, 0), new Normal3f(0, 0, 0), 0);
            int inconsistent = 0, compared = 0;
            for (int i = 0; i < 4000; ++i)
            {
                pstd.optional<ShapeSample> ss = shape.Sample(ctx, new Point2f((Float)rng.NextDouble(), (Float)rng.NextDouble()));
                if (!ss.has_value())
                {
                    continue;
                }
                Vector3f wi = Normalize(ss.value().intr.p() - ctx.p());
                ++compared;
                if (Math.Abs(shape.PDF(ctx, wi) / ss.value().pdf - 1) > 1e-3)
                {
                    ++inconsistent;
                }
            }
            EXPECT_EQ(0, inconsistent, "Sample(ctx).pdf against PDF(ctx, wi) (of " + compared + ")");
            double pdfSum = 0, pdfSquares = 0;
            int nDirections = 400000;
            int hits = 0;
            for (int i = 0; i < nDirections; ++i)
            {
                double z = -rng.NextDouble(), phi = 2 * Math.PI * rng.NextDouble();
                double r = Math.Sqrt(Math.Max(0, 1 - z * z));
                Vector3f w = new Vector3f((Float)(r * Math.Cos(phi)), (Float)(r * Math.Sin(phi)), (Float)z);
                double pdf = shape.PDF(ctx, w);
                pdfSum += pdf;
                pdfSquares += pdf * pdf;
                if (pdf > 0)
                {
                    ++hits;
                }
            }
            // the directions below the point: half of the sphere, with the density 1 / (2 pi): the integral of the pdf over it is the sum over the
            // directions of pdf * 2 pi / n, and it must be 1 (the surface is flat and all of it is first hit)
            double integralPdf = pdfSum / nDirections * 2 * Math.PI, integralError = Math.Sqrt((pdfSquares / nDirections - Math.Pow(pdfSum / nDirections, 2)) / nDirections) * 2 * Math.PI;
            EXPECT_LT(Math.Abs(integralPdf - 1) / integralError, 4.0, "integral of PDF(ctx, wi) " + integralPdf + " +- " + integralError);
        }

        [TEST("Nurbs", "ParserAndErrors")]
        public static void ParserAndErrors()
        {
            Surface s = Flat();
            Shape shape = CreateShape(s, new Transform(), false, null);
            EXPECT_NEAR(6.0, shape.Area(), 1e-4);
            pstd.optional<ShapeIntersection> si = shape.Intersect(new Ray(new Point3f(0.75f, 0.5f, 1), new Vector3f(0, 0, -1)));
            ASSERT_TRUE(si.has_value());
            EXPECT_NEAR(1.0, si.value().tHit, 1e-5);
            EXPECT_NEAR(0.25, si.value().intr.uv.x, 1e-5);
            EXPECT_NEAR(0.25, si.value().intr.uv.y, 1e-5);
        }
    }
}  // namespace pbrt
