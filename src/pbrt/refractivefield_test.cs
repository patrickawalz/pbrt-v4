// (not in pbrt-v4) Tests of the refractive index field and its light paths (refractivefield.cs).

using System;
using testing;

namespace pbrt
{
    using static pbrt;

    public static unsafe class RefractiveFieldTests
    {
        // The path from a point in a direction to where it leaves the field: the sum of sigma, the number of pieces, the
        // largest | |v|^2 - n^2 | met at the ends of the pieces, and the ray at the end
        static RefractiveRay Trace(TetrahedralIndexField field, Point3f p, Vector3f d, out double sigma, out int pieces, out double worstEnergy)
        {
            RefractiveRay ray = new RefractiveRay();
            sigma = 0;
            pieces = 0;
            worstEnergy = 0;
            EXPECT_TRUE(ray.Begin(field, p, d));
            RefractivePiece piece;
            while (ray.Next(out piece))
            {
                sigma += piece.sigma;
                pieces++;
                if (ray.Inside())
                {
                    double n = field.Index(new Point3f((Float)ray.x, (Float)ray.y, (Float)ray.z));
                    worstEnergy = Math.Max(worstEnergy, Math.Abs(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz - n * n));
                }
            }
            return ray;
        }

        // n^2 linear over the whole box: the field holds it exactly, and the path is one parabola in closed form,
        // x0 + v0 sigma + 1/4 g sigma^2 with g the gradient of n^2, whatever the lattice.
        // The bending of a ray from the ground through an atmosphere whose index falls with the height above a sphere,
        // n = 1 + 2.77e-4 exp(-h / 8.4 km), on a lattice that stores nothing (cells of 5 km by 100 m): against the
        // integral of the bending of a ray in a medium of spherical symmetry (n r sin z stays what it is),
        //     the integral over r of -(dn/dr) / n  k / sqrt(n^2 r^2 - k^2),    k = n0 r0 sin z0,
        // for a ray that leaves along the horizon and for one that leaves 5 degrees above it. (Units: km.)
        [TEST("RefractiveField", "AstronomicalRefraction")]
        public static void AstronomicalRefraction()
        {
            const double Radius = 6371, Scale = 8.4, Excess = 2.77e-4;
            Func<double, double, double, double> index = (double x, double y, double z) =>
            {
                double h = Math.Sqrt(x * x + (y + Radius) * (y + Radius) + z * z) - Radius;
                return 1 + Excess * Math.Exp(-h / Scale);
            };
            Bounds3f box = new Bounds3f(new Point3f(-20, -40, -10), new Point3f(1500, 160, 10));
            TetrahedralIndexField field = new TetrahedralIndexField(box, 304, 2000, 1, index, true);
            double[] elevations = { 0, 5 };
            for (int e = 0; e < elevations.Length; ++e)
            {
                double elevation = elevations[e] * Math.PI / 180, height = 0.002;
                RefractiveRay ray = new RefractiveRay();
                Vector3f d = new Vector3f((Float)Math.Cos(elevation), (Float)Math.Sin(elevation), 0);
                EXPECT_TRUE(ray.Begin(field, new Point3f(0, (Float)height, 0), d));
                RefractivePiece piece;
                int pieces = 0;
                while (ray.Next(out piece))
                {
                    ++pieces;
                }
                double n = Math.Sqrt(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz);
                double bending = Math.Atan2(d.y, d.x) - Math.Atan2(ray.vy / n, ray.vx / n);
                // The integral, with r = r0 + u^2 (the integrand is not bounded at the start of a ray along the horizon)
                double r0 = Radius + height, n0 = 1 + Excess * Math.Exp(-height / Scale), k = n0 * r0 * Math.Cos(Math.Atan2(d.y, d.x));
                double expected = 0, top = Math.Sqrt(150.0);
                const int Steps = 400000;
                for (int i = 0; i < Steps; ++i)
                {
                    double u = (i + 0.5) * top / Steps, r = r0 + u * u;
                    double excess = Excess * Math.Exp(-(r - Radius) / Scale), nr = 1 + excess, slope = -excess / Scale;
                    double under = nr * nr * r * r - k * k;
                    if (under > 0)
                    {
                        expected += -slope / nr * k / Math.Sqrt(under) * 2 * u * top / Steps;
                    }
                }
                Console.WriteLine("MEASURE refraction at {0} degrees: {1:F3} arc minutes, the integral {2:F3}; {3} pieces, the ray leaves at height {4:F1} km", elevations[e],
                                  bending * 180 * 60 / Math.PI, expected * 180 * 60 / Math.PI, pieces, Math.Sqrt(ray.x * ray.x + (ray.y + Radius) * (ray.y + Radius)) - Radius);
                EXPECT_LT(Math.Abs(bending - expected), 0.005 * expected);
            }
        }

        [TEST("RefractiveField", "ConstantGradient")]
        public static void ConstantGradient()
        {
            Bounds3f box = new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
            Vector3f g = new Vector3f(0.3f, -0.5f, 0.2f);
            Func<Point3f, double> index = (Point3f p) => (Float)Math.Sqrt(2.25 + g.x * p.x + g.y * p.y + g.z * p.z);
            int[] cells = { 1, 7, 24 };
            RNG rng = new RNG(11);
            foreach (int n in cells)
            {
                TetrahedralIndexField field = new TetrahedralIndexField(box, n, n + 1, n + 2, index);
                double worst = 0, worstEnergy = 0;
                for (int trial = 0; trial < 200; ++trial)
                {
                    Point3f p = new Point3f(-0.9f + 1.8f * rng.Uniform<Float>(), -0.9f + 1.8f * rng.Uniform<Float>(), -0.9f + 1.8f * rng.Uniform<Float>());
                    Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                    double sigma, energy;
                    int pieces;
                    RefractiveRay ray = Trace(field, p, d, out sigma, out pieces, out energy);
                    // (the index at the start as the field holds it: the square root of what the lattice stores)
                    double n0 = field.Index(p);
                    Vector3f unit = Normalize(d);
                    double ex = p.x + n0 * unit.x * sigma + 0.25 * g.x * sigma * sigma;
                    double ey = p.y + n0 * unit.y * sigma + 0.25 * g.y * sigma * sigma;
                    double ez = p.z + n0 * unit.z * sigma + 0.25 * g.z * sigma * sigma;
                    worst = Math.Max(worst, Math.Sqrt((ray.x - ex) * (ray.x - ex) + (ray.y - ey) * (ray.y - ey) + (ray.z - ez) * (ray.z - ez)));
                    worstEnergy = Math.Max(worstEnergy, energy);
                }
                Console.WriteLine("MEASURE constant gradient, {0} cells: end of the path against the parabola {1:E2}, |v|^2 - n^2 {2:E2}", n, worst, worstEnergy);
                // (the lattice stores n^2 from an index in single precision: the gradient is that of the stored values)
                EXPECT_LT(worst, 2e-6);
                EXPECT_LT(worstEnergy, 2e-6);
            }
        }

        // n^2 = 4 - |x|^2: the ray equation is that of a harmonic oscillator, x(sigma) = x0 cos(sigma) + v0 sin(sigma). The
        // field is not linear, so the lattice's field differs from it: in value by the square of the size of the cells, but
        // in gradient (which is constant in a tetrahedron) by the size itself, with either sign. Along most paths those
        // errors cancel from cell to cell and the path's distance from the closed form falls with the square of the size
        // (the median); along a path that runs nearly with the lattice they do not, and the worst falls with the size.
        [TEST("RefractiveField", "HarmonicConvergence")]
        public static void HarmonicConvergence()
        {
            Bounds3f box = new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1));
            Func<Point3f, double> index = (Point3f p) => (Float)Math.Sqrt(4 - (double)p.x * p.x - (double)p.y * p.y - (double)p.z * p.z);
            int[] cells = { 8, 16, 32, 64 };
            double[] errors = new double[cells.Length], medians = new double[cells.Length];
            const double target = 0.25;
            const int Trials = 400;
            for (int c = 0; c < cells.Length; ++c)
            {
                TetrahedralIndexField field = new TetrahedralIndexField(box, cells[c], cells[c], cells[c], index);
                RNG rng = new RNG(5);
                double[] each = new double[Trials];
                for (int trial = 0; trial < Trials; ++trial)
                {
                    Point3f p = new Point3f(-0.3f + 0.6f * rng.Uniform<Float>(), -0.3f + 0.6f * rng.Uniform<Float>(), -0.3f + 0.6f * rng.Uniform<Float>());
                    Vector3f d = Normalize(new Vector3f(rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f, rng.Uniform<Float>() - 0.5f));
                    double n0 = Math.Sqrt(4 - (double)p.x * p.x - (double)p.y * p.y - (double)p.z * p.z);
                    RefractiveRay ray = new RefractiveRay();
                    EXPECT_TRUE(ray.Begin(field, p, d));
                    double sigma = 0;
                    RefractivePiece piece;
                    bool reached = false;
                    while (ray.Next(out piece))
                    {
                        if (sigma + piece.sigma >= target)
                        {
                            double s = target - sigma;
                            double px = piece.x + s * (piece.vx + 0.5 * s * piece.ax), py = piece.y + s * (piece.vy + 0.5 * s * piece.ay);
                            double pz = piece.z + s * (piece.vz + 0.5 * s * piece.az);
                            double ex = p.x * Math.Cos(target) + n0 * d.x * Math.Sin(target), ey = p.y * Math.Cos(target) + n0 * d.y * Math.Sin(target);
                            double ez = p.z * Math.Cos(target) + n0 * d.z * Math.Sin(target);
                            each[trial] = Math.Sqrt((px - ex) * (px - ex) + (py - ey) * (py - ey) + (pz - ez) * (pz - ez));
                            errors[c] = Math.Max(errors[c], each[trial]);
                            reached = true;
                            break;
                        }
                        sigma += piece.sigma;
                    }
                    EXPECT_TRUE(reached);
                }
                Array.Sort(each);
                medians[c] = each[Trials / 2];
                Console.WriteLine("MEASURE harmonic field, {0} cells: path against x0 cos + v0 sin at sigma 0.25: worst {1:E3}, median {2:E3}", cells[c], errors[c],
                                  medians[c]);
            }
            for (int c = 1; c < cells.Length; ++c)
            {
                // (the worst: a half for each halving of the cells; the median: a quarter; both with room)
                EXPECT_LT(errors[c], errors[c - 1] / 1.8);
                EXPECT_LT(medians[c], medians[c - 1] / 3);
            }
        }

        // A path traced to where it leaves the field, turned round and traced back, returns to its start with its
        // direction reversed; and |v| stays the index along it. A smooth field with a bump in the middle.
        [TEST("RefractiveField", "Reversible")]
        public static void Reversible()
        {
            Bounds3f box = new Bounds3f(new Point3f(-2, -1, -1), new Point3f(2, 1, 1));
            Func<Point3f, double> index = (Point3f p) =>
            {
                double r2 = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
                return (Float)(1 + 0.4 * Math.Exp(-3 * r2) + 0.05 * Math.Sin(3 * p.x) * Math.Cos(2 * p.y));
            };
            TetrahedralIndexField field = new TetrahedralIndexField(box, 40, 20, 20, index);
            RNG rng = new RNG(23);
            double worstBack = 0, worstDirection = 0, worstEnergy = 0;
            long pieces = 0;
            for (int trial = 0; trial < 300; ++trial)
            {
                Point3f p = new Point3f(-1.5f + 3 * rng.Uniform<Float>(), -0.8f + 1.6f * rng.Uniform<Float>(), -0.8f + 1.6f * rng.Uniform<Float>());
                Vector3f d = SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                double sigma, energy;
                int count;
                RefractiveRay start = new RefractiveRay();
                EXPECT_TRUE(start.Begin(field, p, d));
                double startLength = Math.Sqrt(start.vx * start.vx + start.vy * start.vy + start.vz * start.vz);
                RefractiveRay ray = Trace(field, p, d, out sigma, out count, out energy);
                pieces += count;
                worstEnergy = Math.Max(worstEnergy, energy);
                // Back along the same path for the same sigma
                ray.Reverse();
                EXPECT_TRUE(ray.Inside());
                double left = sigma;
                RefractivePiece piece;
                double bx = ray.x, by = ray.y, bz = ray.z, bvx = ray.vx, bvy = ray.vy, bvz = ray.vz;
                while (left > 0 && ray.Next(out piece))
                {
                    if (piece.sigma >= left)
                    {
                        bx = piece.x + left * (piece.vx + 0.5 * left * piece.ax);
                        by = piece.y + left * (piece.vy + 0.5 * left * piece.ay);
                        bz = piece.z + left * (piece.vz + 0.5 * left * piece.az);
                        bvx = piece.vx + left * piece.ax;
                        bvy = piece.vy + left * piece.ay;
                        bvz = piece.vz + left * piece.az;
                        left = 0;
                        break;
                    }
                    left -= piece.sigma;
                    bx = ray.x;
                    by = ray.y;
                    bz = ray.z;
                    bvx = ray.vx;
                    bvy = ray.vy;
                    bvz = ray.vz;
                }
                // (what is left of sigma at the very start of the path is rounding)
                EXPECT_LT(left, 1e-9);
                worstBack = Math.Max(worstBack, Math.Sqrt((bx - p.x) * (bx - p.x) + (by - p.y) * (by - p.y) + (bz - p.z) * (bz - p.z)));
                double length = Math.Sqrt(bvx * bvx + bvy * bvy + bvz * bvz);
                // (against the direction that the path started with, as the ray holds it)
                double ux = start.vx / startLength, uy = start.vy / startLength, uz = start.vz / startLength;
                worstDirection = Math.Max(worstDirection, Math.Sqrt((bvx / length + ux) * (bvx / length + ux) + (bvy / length + uy) * (bvy / length + uy) +
                                                                    (bvz / length + uz) * (bvz / length + uz)));
            }
            Console.WriteLine("MEASURE there and back, {0} pieces: start missed by {1:E2}, direction by {2:E2}, |v|^2 - n^2 {3:E2}", pieces, worstBack, worstDirection,
                              worstEnergy);
            EXPECT_LT(worstBack, 1e-8);
            EXPECT_LT(worstDirection, 1e-8);
            EXPECT_LT(worstEnergy, 1e-6);
        }

        // A field with a gradient only in a ball in the middle: the paths are the same whether the cells without a gradient
        // are crossed in one piece or tetrahedron by tetrahedron; a path that misses the ball takes far fewer pieces.
        [TEST("RefractiveField", "UniformCells")]
        public static void UniformCells()
        {
            Bounds3f box = new Bounds3f(new Point3f(-2, -2, -2), new Point3f(2, 2, 2));
            Func<Point3f, double> index = (Point3f p) =>
            {
                double r2 = (double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z;
                double inside = Math.Max(0, 1 - r2 / 0.36);
                return (Float)(1 + 0.3 * inside * inside * inside);
            };
            TetrahedralIndexField field = new TetrahedralIndexField(box, 48, 48, 48, index);
            RNG rng = new RNG(31);
            double worst = 0;
            long with = 0, without = 0, withMissing = 0, withoutMissing = 0;
            int bent = 0;
            for (int trial = 0; trial < 400; ++trial)
            {
                Point3f p = new Point3f(-1.9f, -1.5f + 3 * rng.Uniform<Float>(), -1.5f + 3 * rng.Uniform<Float>());
                Point3f aim = new Point3f(0, 3 * (rng.Uniform<Float>() - 0.5f), 3 * (rng.Uniform<Float>() - 0.5f));
                Vector3f d = Normalize(aim - p);
                double sigmaA, sigmaB, energy;
                int piecesA, piecesB;
                field.skipUniform = true;
                RefractiveRay a = Trace(field, p, d, out sigmaA, out piecesA, out energy);
                field.skipUniform = false;
                RefractiveRay b = Trace(field, p, d, out sigmaB, out piecesB, out energy);
                with += piecesA;
                without += piecesB;
                worst = Math.Max(worst, Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z)));
                worst = Math.Max(worst, Math.Sqrt((a.vx - b.vx) * (a.vx - b.vx) + (a.vy - b.vy) * (a.vy - b.vy) + (a.vz - b.vz) * (a.vz - b.vz)));
                worst = Math.Max(worst, Math.Abs(sigmaA - sigmaB));
                Vector3f unit = Normalize(d);
                double length = Math.Sqrt(a.vx * a.vx + a.vy * a.vy + a.vz * a.vz);
                if (a.vx / length * unit.x + a.vy / length * unit.y + a.vz / length * unit.z < 1 - 1e-9)
                {
                    bent++;
                }
                else
                {
                    withMissing += piecesA;
                    withoutMissing += piecesB;
                }
            }
            field.skipUniform = true;
            Console.WriteLine("MEASURE uniform cells: pieces {0} against {1} by tetrahedron ({2} against {3} for the paths that miss the ball); ends differ by {4:E2}; " +
                              "{5} of 400 paths bent", with, without, withMissing, withoutMissing, worst, bent);
            EXPECT_LT(worst, 1e-9);
            EXPECT_LT(withMissing * 8, withoutMissing);
            EXPECT_GT(bent, 40);
            EXPECT_LT(bent, 360);
        }

        // The index from a grid of samples on a lattice that is finer than the grid: the field's difference from the grid's
        // own interpolation falls with the square of the size of the cells; the worst difference of the paths from those of
        // a much finer lattice falls at least with the size (HarmonicConvergence says why not with its square).
        [TEST("RefractiveField", "Refinement")]
        public static void Refinement()
        {
            const int N = 8;
            Float[] values = new Float[N * N * N];
            RNG fill = new RNG(3);
            for (int k = 0; k < N; ++k)
            {
                for (int j = 0; j < N; ++j)
                {
                    for (int i = 0; i < N; ++i)
                    {
                        double u = (i + 0.5) / N - 0.5, v = (j + 0.5) / N - 0.5, w = (k + 0.5) / N - 0.5;
                        values[i + N * (j + N * k)] = (Float)(1 + 0.25 * Math.Exp(-8 * (u * u + v * v + w * w)) + 0.02 * fill.Uniform<Float>());
                    }
                }
            }
            Bounds3f box = new Bounds3f(new Point3f(0, 0, 0), new Point3f(1, 1, 1));
            int[] refine = { 2, 4, 8 };
            TetrahedralIndexField reference = TetrahedralIndexField.FromGrid(box, values, N, N, N, 32);
            double[] fieldError = new double[refine.Length], pathError = new double[refine.Length];
            for (int c = 0; c < refine.Length; ++c)
            {
                TetrahedralIndexField field = TetrahedralIndexField.FromGrid(box, values, N, N, N, refine[c]);
                RNG rng = new RNG(41);
                for (int trial = 0; trial < 4000; ++trial)
                {
                    Point3f p = new Point3f(rng.Uniform<Float>(), rng.Uniform<Float>(), rng.Uniform<Float>());
                    fieldError[c] = Math.Max(fieldError[c], Math.Abs(field.Index(p) - (Float)TetrahedralIndexField.Interpolate(values, N, N, N, p)));
                }
                for (int trial = 0; trial < 200; ++trial)
                {
                    Point3f p = new Point3f(0.001f, 0.2f + 0.6f * rng.Uniform<Float>(), 0.2f + 0.6f * rng.Uniform<Float>());
                    Vector3f d = Normalize(new Vector3f(1, 0.4f * (rng.Uniform<Float>() - 0.5f), 0.4f * (rng.Uniform<Float>() - 0.5f)));
                    double sigma, energy;
                    int pieces;
                    RefractiveRay a = Trace(field, p, d, out sigma, out pieces, out energy);
                    RefractiveRay b = Trace(reference, p, d, out sigma, out pieces, out energy);
                    double la = Math.Sqrt(a.vx * a.vx + a.vy * a.vy + a.vz * a.vz), lb = Math.Sqrt(b.vx * b.vx + b.vy * b.vy + b.vz * b.vz);
                    pathError[c] = Math.Max(pathError[c], Math.Sqrt((a.vx / la - b.vx / lb) * (a.vx / la - b.vx / lb) + (a.vy / la - b.vy / lb) * (a.vy / la - b.vy / lb) +
                                                                    (a.vz / la - b.vz / lb) * (a.vz / la - b.vz / lb)));
                }
                Console.WriteLine("MEASURE refine {0}: index against the grid's interpolation {1:E3}; direction on leaving against refine 32: {2:E3}", refine[c],
                                  fieldError[c], pathError[c]);
            }
            for (int c = 1; c < refine.Length; ++c)
            {
                EXPECT_LT(fieldError[c], fieldError[c - 1] / 3);
                EXPECT_LT(pathError[c], pathError[c - 1] / 1.8);
            }
        }
    }
}
