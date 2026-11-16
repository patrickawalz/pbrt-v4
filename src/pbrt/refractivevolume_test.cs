// (not in pbrt-v4) Tests of the volume of a refractive medium (refractivemedium.cs).

using System;
using testing;

namespace pbrt
{
    using static pbrt;

    public static unsafe class RefractiveVolumeTests
    {
        // The services of the renderer in a scene whose only surface is a sphere about the origin, seen from inside
        sealed class Shell : VolumeServices
        {
            public Float radius;
            private RNG own;

            public Shell(ulong seed, Float radius)
            {
                rng = new RNG(seed);
                own = new RNG(seed + 1000);
                this.radius = radius;
            }

            public override bool GetNearestHit(Ray ray, out Point3f P, out Float distance, out VolumeGeometry g)
            {
                g = new VolumeGeometry();
                g.isSurface = true;
                double ox = ray.o.x, oy = ray.o.y, oz = ray.o.z, dx = ray.d.x, dy = ray.d.y, dz = ray.d.z;
                double b = ox * dx + oy * dy + oz * dz, c = ox * ox + oy * oy + oz * oz - (double)radius * radius;
                distance = (Float)(-b + Math.Sqrt(Math.Max(b * b - c, 0)));
                P = ray.Apply(distance);
                return true;
            }

            public override Float GenerateRandomNumber() { return own.Uniform<Float>(); }

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

        // A grid of densities over the box from -1 to 1: smooth, never nothing
        static GridMedium Smoke(Float emission)
        {
            const int n = 16;
            Float[] density = new Float[n * n * n], scale = new Float[n * n * n];
            for (int z = 0; z < n; ++z)
            {
                for (int y = 0; y < n; ++y)
                {
                    for (int x = 0; x < n; ++x)
                    {
                        Float fx = (x + 0.5f) / n - 0.5f, fy = (y + 0.5f) / n - 0.5f, fz = (z + 0.5f) / n - 0.5f;
                        density[x + n * (y + n * z)] = 0.4f + 0.6f * std.exp(-6 * (fx * fx + fy * fy + fz * fz)) * (1 + 0.3f * std.sin(17 * fx + 11 * fy * fz));
                        scale[x + n * (y + n * z)] = emission > 0 ? 0.5f + fx : 0;
                    }
                }
            }
            Allocator alloc = new Allocator();
            return new GridMedium(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), new Transform(), new ConstantSpectrum(0.3f), new ConstantSpectrum(0.9f), 1,
                                  0.3f, new SampledGrid<Float>(density, n, n, n, alloc), default, 1, 0, new ConstantSpectrum(emission),
                                  new SampledGrid<Float>(scale, n, n, n, alloc), alloc);
        }

        // An index with a strong bump in the middle of the same box
        static TetrahedralIndexField Bump(Float amplitude)
        {
            return new TetrahedralIndexField(new Bounds3f(new Point3f(-1, -1, -1), new Point3f(1, 1, 1)), 24, 24, 24, (Point3f p) =>
            {
                double r2 = ((double)p.x * p.x + (double)p.y * p.y + (double)p.z * p.z) / 0.64;
                double inside = Math.Max(0, 1 - r2);
                return (Float)(1 + amplitude * inside * inside * inside);
            });
        }

        // Along the path from a point in a direction to the shell, by the field's own pieces cut finely: the optical depth,
        // the integral of T mu_a Le (n_start / n)^2 over the arc length, the index at the two ends, and how much the
        // direction turns
        static double scatteredByQuadrature;

        static void Quadrature(TetrahedralIndexField field, Medium inner, SampledWavelengths lambda, Point3f o, Vector3f d, Float shell, out double depth,
                               out double emitted, out double indexAtStart, out double indexAtEnd, out double turned)
        {
            scatteredByQuadrature = 0;
            RefractiveRay ray = new RefractiveRay();
            EXPECT_TRUE(ray.Begin(field, o, d));
            indexAtStart = Math.Sqrt(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz);
            depth = 0;
            emitted = 0;
            RefractivePiece piece;
            const int Cuts = 400;
            while (ray.Next(out piece))
            {
                for (int c = 0; c < Cuts; ++c)
                {
                    double s = piece.sigma * (c + 0.5) / Cuts, ds = piece.sigma / Cuts;
                    double vx = piece.vx + s * piece.ax, vy = piece.vy + s * piece.ay, vz = piece.vz + s * piece.az;
                    double n = Math.Sqrt(vx * vx + vy * vy + vz * vz);
                    MediumProperties mp = inner.SamplePoint(piece.Point(s), lambda);
                    double extinction = mp.sigma_a[0] + mp.sigma_s[0];
                    emitted += Math.Exp(-depth - 0.5 * extinction * n * ds) * mp.sigma_a[0] * mp.Le[0] * (indexAtStart / n) * (indexAtStart / n) * n * ds;
                    // (what is scattered along the way, as it is seen from the start)
                    scatteredByQuadrature += Math.Exp(-depth - 0.5 * extinction * n * ds) * mp.sigma_s[0] * (indexAtStart / n) * (indexAtStart / n) * n * ds;
                    depth += extinction * n * ds;
                }
            }
            indexAtEnd = Math.Sqrt(ray.vx * ray.vx + ray.vy * ray.vy + ray.vz * ray.vz);
            Vector3f unit = Normalize(d);
            turned = Math.Acos(Math.Min(1, (ray.vx * unit.x + ray.vy * unit.y + ray.vz * unit.z) / indexAtEnd));
            // (beyond the box the smoke is nothing: the rest of the way to the shell adds nothing)
        }

        // The transmittance along bent paths, by each estimator through the curve's straight ray, against the optical depth
        // along the curve by quadrature; with the factor (n_start / n_end)^2 for a path that starts inside the bump.
        [TEST("RefractiveVolume", "Transmittance")]
        public static void Transmittance()
        {
            GridMedium smoke = Smoke(0);
            TetrahedralIndexField field = Bump(0.35f);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            string[] estimators = { "ratio", "residuallocal", "tracklength", "nextflight", "pseriescmf" };
            Point3f[] origins = { new Point3f(-0.95f, 0.3f, 0.1f), new Point3f(-0.95f, -0.45f, 0.35f), new Point3f(0.1f, 0.2f, -0.1f), new Point3f(0.3f, -0.95f, 0.5f) };
            Vector3f[] directions = { new Vector3f(1, -0.1f, 0.05f), new Vector3f(1, 0.3f, -0.2f), new Vector3f(0.4f, 1, 0.3f), new Vector3f(-0.2f, 1, -0.4f) };
            foreach (string name in estimators)
            {
                smoke.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Transmittance(name));
                RefractiveMedium medium = new RefractiveMedium(smoke, field, false);
                medium.volumeIntegrator = smoke.volumeIntegrator;
                for (int r = 0; r < origins.Length; ++r)
                {
                    double depth, emitted, n0, n1, turned;
                    Quadrature(field, smoke, lambda, origins[r], directions[r], 4, out depth, out emitted, out n0, out n1, out turned);
                    double exact = Math.Exp(-depth) * (n0 / n1) * (n0 / n1);
                    Shell shell = new Shell((ulong)(100 + r), 4);
                    double sum = 0, squares = 0;
                    const int Count = 40000;
                    bool everBent = false;
                    for (int s = 0; s < Count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.medium = medium;
                        bool bent, hit;
                        Point3f P;
                        VolumeGeometry g;
                        Ray arrival;
                        SampledSpectrum T = Volume.Create(ctx, medium).Transmit(shell, new Ray(origins[r], Normalize(directions[r])), 10, out bent, out hit, out P, out g,
                                                                                out arrival);
                        everBent |= bent;
                        // (ratio tracking and track length sample with one wavelength for the others, and their estimate is that
                        // wavelength's; the others estimate every wavelength, over the densities of the path)
                        double value = name == "ratio" || name == "tracklength" ? T[0] : (T / ctx.r.Average()).Average();
                        sum += value;
                        squares += value * value;
                    }
                    double mean = sum / Count, error = Math.Sqrt(Math.Max(0, squares / Count - mean * mean) / Count);
                    Console.WriteLine("MEASURE transmittance, {0,-14} path {1} (turned {2:F3} rad, n {3:F3} to {4:F3}): {5:F5} +- {6:F5} against {7:F5}", name, r, turned,
                                      n0, n1, mean, error, exact);
                    EXPECT_TRUE(everBent);
                    EXPECT_LT(Math.Abs(mean - exact), 4 * error + 2e-4);
                }
            }
        }

        // The axis that is carried along a curved path for the polarisation of the light (CurvedRayMedium.Carry): one turn
        // for each piece, against the same in a hundred small turns for each piece; it stays across the path, of length one.
        [TEST("RefractiveVolume", "CarriedAxis")]
        public static void CarriedAxis()
        {
            TetrahedralIndexField field = Bump(0.35f);
            RNG rng = new RNG(61);
            double worst = 0, worstAcross = 0, mostTurned = 0;
            for (int trial = 0; trial < 200; ++trial)
            {
                Point3f o = new Point3f(-0.95f, -0.6f + 1.2f * rng.Uniform<Float>(), -0.6f + 1.2f * rng.Uniform<Float>());
                Vector3f d = Normalize(new Vector3f(1, 0.6f * (rng.Uniform<Float>() - 0.5f), 0.6f * (rng.Uniform<Float>() - 0.5f)));
                RefractiveRay ray = new RefractiveRay();
                EXPECT_TRUE(ray.Begin(field, o, d));
                CurvedPath path = new CurvedPath();
                RefractivePiece piece;
                while (ray.Next(out piece))
                {
                    path.pieces.Add(piece);
                }
                path.indexAtStart = 1;
                CurvedRayMedium curve = new CurvedRayMedium(null, path, 0, VolumeIntegrator.Default);
                Vector3f d0, e0, d1, e1;
                curve.Carry(double.PositiveInfinity, out d0, out e0, out d1, out e1);
                // The same by small turns
                double ex = e0.x, ey = e0.y, ez = e0.z;
                foreach (RefractivePiece p in path.pieces)
                {
                    const int Cuts = 100;
                    for (int c = 0; c < Cuts; ++c)
                    {
                        double s0 = p.sigma * c / Cuts, s1 = p.sigma * (c + 1) / Cuts;
                        double ax = p.vx + s0 * p.ax, ay = p.vy + s0 * p.ay, az = p.vz + s0 * p.az, la = Math.Sqrt(ax * ax + ay * ay + az * az);
                        double bx = p.vx + s1 * p.ax, by = p.vy + s1 * p.ay, bz = p.vz + s1 * p.az, lb = Math.Sqrt(bx * bx + by * by + bz * bz);
                        ax /= la;
                        ay /= la;
                        az /= la;
                        bx /= lb;
                        by /= lb;
                        bz /= lb;
                        // (a vector across a is carried to b by taking out what it has along b)
                        double along = ex * bx + ey * by + ez * bz;
                        ex -= along * bx;
                        ey -= along * by;
                        ez -= along * bz;
                        double length = Math.Sqrt(ex * ex + ey * ey + ez * ez);
                        ex /= length;
                        ey /= length;
                        ez /= length;
                    }
                }
                worst = Math.Max(worst, Math.Sqrt((ex - e1.x) * (ex - e1.x) + (ey - e1.y) * (ey - e1.y) + (ez - e1.z) * (ez - e1.z)));
                worstAcross = Math.Max(worstAcross, Math.Abs(Dot(e1, d1)));
                mostTurned = Math.Max(mostTurned, Math.Acos(Math.Min(1, Dot(d0, d1))));
                EXPECT_LT(Math.Abs(Length(e1) - 1), 1e-5);
            }
            Console.WriteLine("MEASURE carried axis: differs from small turns by {0:E2}; along the path {1:E2}; paths turned by up to {2:F3} rad", worst, worstAcross,
                              mostTurned);
            EXPECT_LT(worst, 2e-4);
            EXPECT_LT(worstAcross, 1e-5);
            EXPECT_GT(mostTurned, 0.2);
        }

        // The walk of the volume along bent paths: the weight that reaches the shell without a collision is the
        // transmittance, and what the medium emits along the way is the integral along the curve, for the tracker of the
        // medium and for spectral tracking.
        [TEST("RefractiveVolume", "Walk")]
        public static void Walk()
        {
            GridMedium smoke = Smoke(2);
            TetrahedralIndexField field = Bump(0.35f);
            SampledWavelengths lambda = SampledWavelengths.SampleUniform(0.5f);
            Point3f[] origins = { new Point3f(-0.95f, 0.3f, 0.1f), new Point3f(0.1f, 0.2f, -0.1f) };
            Vector3f[] directions = { new Vector3f(1, -0.1f, 0.05f), new Vector3f(0.4f, 1, 0.3f) };
            for (int mode = 0; mode < 2; ++mode)
            {
                smoke.volumeIntegrator = new VolumeIntegrator(VolumeIntegrator.Default.tracker, VolumeIntegrator.Default.transmittance);
                smoke.volumeIntegrator.spectralTracking = mode == 1;
                RefractiveMedium medium = new RefractiveMedium(smoke, field, false);
                medium.volumeIntegrator = smoke.volumeIntegrator;
                for (int r = 0; r < origins.Length; ++r)
                {
                    double depth, emitted, n0, n1, turned;
                    Quadrature(field, smoke, lambda, origins[r], directions[r], 4, out depth, out emitted, out n0, out n1, out turned);
                    double through = Math.Exp(-depth) * (n0 / n1) * (n0 / n1), scatters = scatteredByQuadrature;
                    Shell shell = new Shell((ulong)(300 + r), 4);
                    double sumT = 0, squaresT = 0, sumL = 0, squaresL = 0, sumS = 0, squaresS = 0;
                    const int Count = 200000;
                    int scattered = 0;
                    for (int s = 0; s < Count; ++s)
                    {
                        VolumeContext ctx = new VolumeContext();
                        ctx.lambda = lambda;
                        ctx.medium = medium;
                        ctx.mayScatter = true;
                        SampledSpectrum L, T, w;
                        Point3f P;
                        Ray wo;
                        VolumeGeometry g;
                        bool hit = Volume.Create(ctx, medium).Integrate(shell, new Ray(origins[r], Normalize(directions[r])), out L, out T, out w, out P, out wo, out g);
                        double reached = hit && g.isSurface ? (w * T)[0] / ctx.r[0] : 0;
                        if (hit && !g.isSurface)
                        {
                            scattered++;
                            double collided = (w * T)[0] / ctx.r[0];
                            sumS += collided;
                            squaresS += collided * collided;
                            // (a collision is on the curve, with the direction of the path there: inside the box)
                            EXPECT_LT(std.abs(P.x), 1.001f);
                            EXPECT_LT(std.abs(Length(g.scatter.wo) - 1), 1e-4f);
                        }
                        sumT += reached;
                        squaresT += reached * reached;
                        sumL += L[0];
                        squaresL += L[0] * L[0];
                    }
                    double meanT = sumT / Count, errorT = Math.Sqrt(Math.Max(0, squaresT / Count - meanT * meanT) / Count);
                    double meanL = sumL / Count, errorL = Math.Sqrt(Math.Max(0, squaresL / Count - meanL * meanL) / Count);
                    Console.WriteLine("MEASURE walk, {0,-8} path {1}: reaches the shell {2:F5} +- {3:F5} against {4:F5}; emitted {5:F5} +- {6:F5} against {7:F5}; " +
                                      "{8} collisions", mode == 1 ? "spectral" : "tracker", r, meanT, errorT, through, meanL, errorL, emitted, scattered);
                    EXPECT_LT(Math.Abs(meanT - through), 4 * errorT + 2e-4);
                    EXPECT_LT(Math.Abs(meanL - emitted), 4 * errorL + 2e-4);
                    double meanS = sumS / Count, errorS = Math.Sqrt(Math.Max(0, squaresS / Count - meanS * meanS) / Count);
                    Console.WriteLine("MEASURE walk, {0,-8} path {1}: weight of the collisions {2:F5} +- {3:F5} against {4:F5}", mode == 1 ? "spectral" : "tracker", r, meanS,
                                      errorS, scatters);
                    EXPECT_LT(Math.Abs(meanS - scatters), 4 * errorS + 2e-4);
                    EXPECT_GT(scattered, Count / 20);
                }
            }
        }
    }
}
