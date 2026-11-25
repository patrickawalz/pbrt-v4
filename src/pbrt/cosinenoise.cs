// (not in pbrt-v4) cosinenoise.cs
//
// Cosine noise (Balint and Mantiuk, "Closed Form Transmittance in Heterogeneous Media Using Cosine Noise", CESCG 2019): a noise
// that is a constant plus a sum of plane cosine waves,
//
//     f(x) = offset + sum_k w_k cos(S_k . x + phase_k).
//
// Along a ray each wave is a cosine of the distance, so the integral of f between two points of a ray is a closed form. That
// gives the optical depth of a medium whose density is f without stepping, whatever the frequency of the noise.
//
// Three things are built on it here:
//
//     CosineNoise              the function, its integral along a ray, and its exact bounds over a box
//     CosineNoiseTexture       Texture "name" "float" "cosinenoise": the noise as a pattern, beside "fbm" and "wrinkled". A "texture"
//                              medium of it is a volume at once, with tight majorants from the bounds over a box
//     CosineNoiseMedium        MakeNamedMedium "name" "string type" "cosinenoise": a volume of the noise, like the "cloud" medium. Its
//                              transmittance is the closed form (section 4 of the paper), and where the field is never negative the
//                              distances to its collisions are sampled by inverting the closed form (section 5): no majorant, no
//                              null collision, no stepping
//
// The waves. The paper takes 24 quasi-random directions whose lengths vary by a fifth, and stacks octaves ("waves" is the number
// of an octave, 24 unless given). Here the directions of
// an octave are the points of a spherical Fibonacci lattice, their lengths 0.8 to 1.2 by a low discrepancy sequence, and each
// octave is the one before it turned by a fixed rotation and scaled by "lacunarity", with "gain" times its weight. Every wave has
// its own phase, so that the waves do not all peak at the origin. Nothing is tabulated.
//
// The density. The sum of the waves has the mean zero and the standard deviation s = sqrt(sum_k w_k^2 / 2), and with many waves
// it is close to a normal variable: it stays within about three s although its bound is the sum of the weights A, some eleven s
// for four octaves. The density of the texture and of the medium is the field in units of s, clamped at zero,
//
//     density(x) = max(0, offset + (sum of the waves)(x) / s),
//
// so "offset" is in standard deviations: at 0 half of space is empty, at 1 about a sixth, at 3 next to none. The density is at
// most offset + A / s (CosineNoise.MaxDensity), a bound that holds and that the noise does not come near.
//
//     "offset" >= A / s   the field is never negative. Nothing is clamped and the closed form is the optical depth exactly: the
//                         transmittance is one evaluation of it and the distance to a collision is the root of it
//                         ("string sampling" "closedform", which is what such a medium does unless told "majorant"). A / s is
//                         sqrt(2 "waves") for one octave and grows with the octaves; fewer waves leave more contrast at the
//                         smallest offset that keeps the field positive.
//     below that          the field goes below zero and the density has empty regions (the look of the paper's clouds). The
//                         paper clamps the samples and the integrals there and accepts the artefacts, which is biased. Here the closed form is kept as a
//                         control: the density is the field plus what the clamp adds, r = max(0, -field), and
//                         exp(-integral of r) is estimated by ratio tracking with the majorant of r alone. The product is an
//                         unbiased estimate of the transmittance. A medium with a box does this cell by cell of a grid: the
//                         closed form alone where the field stays positive, nothing where it stays negative, and where it has
//                         both signs the closed form with its residual or ratio tracking of the density, whichever has the
//                         smaller bound in the cell. The distances to the collisions are sampled by tracking against the
//                         bounds of the density over the cells of the same grid.

using System;

namespace pbrt
{
    using static pbrt;

    // CosineNoise Definition
    public sealed unsafe class CosineNoise
    {
        // CosineNoise Public Methods
        public CosineNoise(Float frequency, int octaves, int waves, Float lacunarity, Float gain, Float offset)
        {
            this.octaves = octaves;
            count = waves * octaves;
            sx = new double[count];
            sy = new double[count];
            sz = new double[count];
            phase = new double[count];
            weight = new double[count];

            // The rotation from one octave to the next: by the golden angle about a fixed axis
            double axisLength = Math.Sqrt(14.0);
            double ax = 1.0 / axisLength, ay = 2.0 / axisLength, az = 3.0 / axisLength;
            double angle = Math.PI * (3.0 - Math.Sqrt(5.0));

            amplitude = 0;
            double squares = 0;
            double octaveScale = frequency, octaveWeight = 1, octaveAngle = 0;
            for (int o = 0; o < octaves; ++o)
            {
                double cosAngle = Math.Cos(octaveAngle), sinAngle = Math.Sin(octaveAngle);
                for (int i = 0; i < waves; ++i)
                {
                    int k = o * waves + i;
                    // A point of the spherical Fibonacci lattice
                    double z = 1.0 - (2.0 * i + 1.0) / waves;
                    double radius = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                    double longitude = 2.0 * Math.PI * Fraction(i * 0.6180339887498949);
                    double x = radius * Math.Cos(longitude), y = radius * Math.Sin(longitude);
                    // Its length, 0.8 to 1.2
                    double length = 0.8 + 0.4 * Fraction((i + 1) * 0.7548776662466927);
                    // Turned about the axis by the angle of the octave (Rodrigues)
                    double dot = ax * x + ay * y + az * z;
                    double cx = ay * z - az * y, cy = az * x - ax * z, cz = ax * y - ay * x;
                    double rx = x * cosAngle + cx * sinAngle + ax * dot * (1.0 - cosAngle);
                    double ry = y * cosAngle + cy * sinAngle + ay * dot * (1.0 - cosAngle);
                    double rz = z * cosAngle + cz * sinAngle + az * dot * (1.0 - cosAngle);
                    sx[k] = rx * length * octaveScale;
                    sy[k] = ry * length * octaveScale;
                    sz[k] = rz * length * octaveScale;
                    phase[k] = 2.0 * Math.PI * Fraction((k + 1) * 0.5698402909980532);
                    weight[k] = octaveWeight;
                    amplitude += octaveWeight;
                    squares += octaveWeight * octaveWeight;
                }
                octaveScale *= lacunarity;
                octaveWeight *= gain;
                octaveAngle += angle;
            }
            norm = Math.Sqrt(0.5 * squares);
            this.offset = offset * norm;
        }

        // The sum of the weights of the waves: the field lies within this of its offset
        public double Amplitude()
        {
            return amplitude;
        }

        // What the field is divided by to give the density: the standard deviation of the sum of the waves
        public double Norm()
        {
            return norm;
        }

        // Whether the field is never negative, so that the density is the field and nothing is clamped
        public bool NeverNegative()
        {
            return offset >= amplitude;
        }

        // A bound of the density: the offset and every wave at its crest
        public double MaxDensity()
        {
            return Math.Max(0.0, offset + amplitude) / norm;
        }

        // The largest value of what the clamp adds to the field, max(0, -field), in units of the density
        public double ResidualBound()
        {
            return Math.Max(0.0, amplitude - offset) / norm;
        }

        // offset + sum_k w_k cos(S_k . p + phase_k)
        public double Field(double px, double py, double pz)
        {
            double sum = offset;
            for (int k = 0; k < count; ++k)
            {
                sum += weight[k] * Math.Cos(sx[k] * px + sy[k] * py + sz[k] * pz + phase[k]);
            }
            return sum;
        }

        // The density: the field in standard deviations of the sum of the waves, clamped at zero
        public Float Density(Point3f p)
        {
            return (Float)(Math.Max(0.0, Field(p.x, p.y, p.z)) / norm);
        }

        // The integral of the field over t in [t0, t1] along o + t d, in closed form. A wave is cos(a + b t) there with
        // a = S . o + phase and b = S . d, and its integral is (sin(a + b t1) - sin(a + b t0)) / b. That is written as
        // (t1 - t0) cos(a + b (t0 + t1) / 2) sinc(b (t1 - t0) / 2), which has no division by b and holds for a ray that
        // runs along the crests of a wave (b = 0).
        public double FieldIntegral(double ox, double oy, double oz, double dx, double dy, double dz, double t0, double t1)
        {
            double dt = t1 - t0, tMid = 0.5 * (t0 + t1);
            double sum = offset * dt;
            for (int k = 0; k < count; ++k)
            {
                double a = sx[k] * ox + sy[k] * oy + sz[k] * oz + phase[k];
                double b = sx[k] * dx + sy[k] * dy + sz[k] * dz;
                double half = 0.5 * b * dt;
                double sinc = Math.Abs(half) < 1e-4 ? 1.0 - half * half / 6.0 : Math.Sin(half) / half;
                sum += weight[k] * dt * Math.Cos(a + b * tMid) * sinc;
            }
            return sum;
        }

        // The distance t in (tStart, tEnd) at which the integral of the field from tStart reaches the target, for a field that
        // is never negative (the integral then grows with t and has one such distance): the paper's distance sampling, with
        // Newton's method kept inside a bracket in place of its bisection (the derivative of the integral is the field). False
        // if the integral to tEnd, returned in toEnd, is short of the target.
        public bool InvertFieldIntegral(double ox, double oy, double oz, double dx, double dy, double dz, double tStart, double tEnd, double target,
                                        out double t, out double toEnd)
        {
            // Bracket the distance, starting from where a field at its mean would reach the target
            double lo = tStart, hi = Math.Min(tEnd, tStart + target / offset);
            double valueHi = FieldIntegral(ox, oy, oz, dx, dy, dz, tStart, hi);
            while (valueHi < target)
            {
                if (hi == tEnd)
                {
                    t = tEnd;
                    toEnd = valueHi;
                    return false;
                }
                lo = hi;
                hi = Math.Min(tEnd, tStart + 2.0 * (hi - tStart));
                valueHi = FieldIntegral(ox, oy, oz, dx, dy, dz, tStart, hi);
            }
            toEnd = 0;
            t = 0.5 * (lo + hi);
            for (int iteration = 0; iteration < 64; ++iteration)
            {
                double value = FieldIntegral(ox, oy, oz, dx, dy, dz, tStart, t) - target;
                if (Math.Abs(value) <= 1e-9 * target || hi - lo <= 1e-13 * Math.Max(1.0, Math.Abs(hi)))
                {
                    break;
                }
                if (value < 0)
                {
                    lo = t;
                }
                else
                {
                    hi = t;
                }
                double slope = Field(ox + t * dx, oy + t * dy, oz + t * dz);
                double next = slope > 0 ? t - value / slope : lo;
                t = next > lo && next < hi ? next : 0.5 * (lo + hi);
            }
            return true;
        }

        // The smallest and the largest value of the field over a box. Over a box the argument of a wave covers an interval, and
        // the bounds of a cosine over an interval are exact; the bounds of the sum are the sums of them (they hold, and are tight
        // where the box is small against the wavelengths).
        public void FieldBounds(Bounds3f box, out double lo, out double hi)
        {
            double cx = 0.5 * ((double)box.pMin.x + box.pMax.x), cy = 0.5 * ((double)box.pMin.y + box.pMax.y), cz = 0.5 * ((double)box.pMin.z + box.pMax.z);
            double hx = 0.5 * ((double)box.pMax.x - box.pMin.x), hy = 0.5 * ((double)box.pMax.y - box.pMin.y), hz = 0.5 * ((double)box.pMax.z - box.pMin.z);
            lo = offset;
            hi = offset;
            for (int k = 0; k < count; ++k)
            {
                double centre = sx[k] * cx + sy[k] * cy + sz[k] * cz + phase[k];
                double half = Math.Abs(sx[k]) * hx + Math.Abs(sy[k]) * hy + Math.Abs(sz[k]) * hz;
                double waveLo, waveHi;
                CosineBounds(centre - half, centre + half, out waveLo, out waveHi);
                lo += weight[k] * waveLo;
                hi += weight[k] * waveHi;
            }
        }

        // CosineNoise Private Methods
        private static double Fraction(double v)
        {
            return v - Math.Floor(v);
        }

        // The bounds of the cosine over [a, b]
        private static void CosineBounds(double a, double b, out double lo, out double hi)
        {
            const double twoPi = 2.0 * Math.PI;
            if (b - a >= twoPi)
            {
                lo = -1;
                hi = 1;
                return;
            }
            double ca = Math.Cos(a), cb = Math.Cos(b);
            lo = Math.Min(ca, cb);
            hi = Math.Max(ca, cb);
            // A crest (a multiple of 2 pi) or a trough (an odd multiple of pi) inside the interval
            if (Math.Floor(b / twoPi) > Math.Floor(a / twoPi) || a == Math.Floor(a / twoPi) * twoPi)
            {
                hi = 1;
            }
            if (Math.Floor((b - Math.PI) / twoPi) > Math.Floor((a - Math.PI) / twoPi))
            {
                lo = -1;
            }
        }

        // CosineNoise Private Members
        private readonly int octaves, count;
        private readonly double[] sx, sy, sz, phase, weight;
        private readonly double amplitude, offset, norm;
    }

    // CosineNoiseTexture Definition
    //
    //     Texture "haze" "float" "cosinenoise" "float frequency" 4 "integer octaves" 4 "float offset" 0.3
    //     MakeNamedMedium "fog" "string type" "texture" "string density" "haze" "point3 p0" [...] "point3 p1" [...]
    //
    // "frequency" 1, "octaves" 4, "lacunarity" 2, "gain" 0.5, "offset" 1 (in standard deviations of the noise), "density" 1 (what the
    // value is multiplied by).
    public sealed unsafe class CosineNoiseTexture : FloatTexture
    {
        public CosineNoiseTexture(TextureMapping3D mapping, CosineNoise noise, Float density)
        {
            this.mapping = mapping;
            this.noise = noise;
            this.density = density;
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return (Float)(density * noise.MaxDensity());
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            return density * noise.Density(p);
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            // The box of the points of the texture that the region maps to
            Bounds3f box = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                Point3f corner = mapping.Map(ctx).p;
                box = (i == 0) ? new Bounds3f(corner) : pbrt.Union(box, corner);
            }
            double fieldLo, fieldHi;
            noise.FieldBounds(box, out fieldLo, out fieldHi);
            // (the arithmetic of the bounds and that of the value round differently)
            lo = (Float)Math.Max(0.0, density * Math.Max(0.0, fieldLo) / noise.Norm() - 1e-5);
            hi = (Float)Math.Min(density * noise.MaxDensity(), density * Math.Max(0.0, fieldHi) / noise.Norm() + 1e-5);
        }

        public static CosineNoiseTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            int octaves = parameters.GetOneInt("octaves", 4);
            Float offset = parameters.GetOneFloat("offset", 1);
            if (octaves < 1 || octaves > 16)
            {
                ErrorExit(loc, "\"cosinenoise\": \"octaves\" is 1 to 16.");
            }
            int waves = parameters.GetOneInt("waves", 24);
            if (waves < 1 || waves > 64)
            {
                ErrorExit(loc, "\"cosinenoise\": \"waves\" is 1 to 64.");
            }
            CosineNoise noise = new CosineNoise(parameters.GetOneFloat("frequency", 1), octaves, waves, parameters.GetOneFloat("lacunarity", 2),
                                                parameters.GetOneFloat("gain", 0.5f), offset);
            return new CosineNoiseTexture(map, noise, parameters.GetOneFloat("density", 1));
        }

        public override string ToString()
        {
            return StringPrintf("[ CosineNoiseTexture mapping: %s density: %f ]", mapping, density);
        }

        private readonly TextureMapping3D mapping;
        private readonly CosineNoise noise;
        private readonly Float density;
    }

    // CosineNoiseMedium Definition
    //
    //     MakeNamedMedium "fog" "string type" "cosinenoise" "spectrum sigma_a" ... "spectrum sigma_s" ... "float density" 2
    //         "float frequency" 4 "integer octaves" 4 "float offset" 0.3 "integer waves" 24
    //         "point3 p0" [...] "point3 p1" [...]
    //
    // "string transmittance" is "closedform" unless given (the other estimators of the volume integrator remain), and
    // "string sampling" is "closedform" for a field that is never negative and "majorant" otherwise.
    //
    // The parameters of the noise are those of the texture. With "p0" and "p1" the medium fills that box of its own space, as the
    // "cloud" medium does. Without them it has no extent of its own and fills the shape it is the interior of: the closed form
    // then runs between the two points where a ray crosses the surface, which is how the paper shapes its clouds.
    //
    // The grids. A field that is never negative needs none: its transmittance and its collision distances are closed forms. One
    // that goes below zero is clamped, and what is left to tracking needs bounds. The bound over all of space (every wave at its
    // crest) is several times what the noise reaches, so a medium with a box keeps two grids over it ("majorantres" cells along
    // each axis, 64 by default), filled from the exact bounds of the field over each cell: the largest density, for the tracker
    // that samples the collisions, and how the transmittance through the cell is taken (see the constructor). A medium without a
    // box has only the bounds over all of space: it is correct and slow, so give the box where the shape allows it.
    public sealed unsafe class CosineNoiseMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        // CosineNoiseMedium Public Methods
        public CosineNoiseMedium(bool bounded, Bounds3f bounds, Transform renderFromMedium, Spectrum sigma_a, Spectrum sigma_s, Float g, Float density,
                                 CosineNoise noise, bool closedFormSampling, int majorantResolution, Allocator alloc)
        {
            this.closedFormSampling = closedFormSampling;
            this.bounded = bounded;
            this.bounds = bounds;
            this.renderFromMedium = renderFromMedium;
            sigma_a_spec = new DenselySampledSpectrum(sigma_a, alloc);
            sigma_s_spec = new DenselySampledSpectrum(sigma_s, alloc);
            phase = new HGPhaseFunction(g, HGPhaseFunction.creationRayleigh);
            this.density = density;
            this.noise = noise;
            if (bounded)
            {
                // The largest density and the largest residual of each cell, from the bounds of the field over it
                Point3i res = new Point3i(majorantResolution, majorantResolution, majorantResolution);
                densityGrid = new MajorantGrid(bounds, res, alloc);
                controlGrid = new MajorantGrid(bounds, res, alloc);
                MajorantGrid densities = densityGrid, controls = controlGrid;
                ParallelFor(0, res.z, (long z) =>
                {
                    for (int y = 0; y < res.y; ++y)
                    {
                        for (int x = 0; x < res.x; ++x)
                        {
                            Bounds3f unit = densities.VoxelBounds(x, y, (int)z);
                            Bounds3f cell = new Bounds3f(bounds.Lerp(unit.pMin), bounds.Lerp(unit.pMax));
                            double fieldLo, fieldHi;
                            noise.FieldBounds(cell, out fieldLo, out fieldHi);
                            densities.Set(x, y, (int)z, (Float)(Math.Max(0.0, fieldHi) / noise.Norm() * (1 + 1e-5)));
                            // How the transmittance through the cell is taken ("closedform"):
                            //     >= 0        the closed form of the field, and ratio tracking of what the clamp adds against this
                            //                 bound of it (zero where the field stays positive: the closed form alone)
                            //     < 0         ratio tracking of the density against minus this, its bound in the cell
                            //     -infinity   the field stays negative: the cell is empty
                            // A cell where the field has both signs takes whichever of the two has the smaller bound: the closed
                            // form where the field is mostly positive, tracking where it is mostly below zero and the density
                            // mostly nothing. (Measured, "offset" 0, the spread of one estimate: 0.007 this way, 0.10 with the
                            // closed form everywhere, 0.10 by ratio tracking alone.)
                            double residualBound = Math.Max(0.0, -fieldLo) / noise.Norm() * (1 + 1e-5);
                            double densityBound = Math.Max(0.0, fieldHi) / noise.Norm() * (1 + 1e-5);
                            controls.Set(x, y, (int)z, densityBound == 0 ? Float.NegativeInfinity
                                                                         : residualBound <= densityBound ? (Float)residualBound
                                                                                                                         : (Float)(-densityBound));
                        }
                    }
                });
            }
        }

        public override string ToString()
        {
            return StringPrintf("[ CosineNoiseMedium bounds: %s renderFromMedium: %s phase: %s sigma_a_spec: %s sigma_s_spec: %s density: %f ]",
                                bounds, renderFromMedium, phase, sigma_a_spec, sigma_s_spec, density);
        }

        public override bool IsEmissive()
        {
            return false;
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            Float d = density * noise.Density(renderFromMedium.ApplyInverse(p));
            return new MediumProperties { sigma_a = d * sigma_a_spec.Sample(lambda), sigma_s = d * sigma_s_spec.Sample(lambda), phase = phase,
                                          Le = new SampledSpectrum(0f) };
        }

        // Whether the distances to the collisions are sampled from the closed form, without a majorant
        public bool ClosedFormSampling()
        {
            return closedFormSampling;
        }

        // SampleT_maj() of a field that is never negative: the paper's distance sampling. The optical depth to the next collision
        // of the wavelength that tracks is drawn, -ln(1 - u) / sigma_t, and the distance at which the closed form reaches it is
        // found. The callback is given the extinction of the medium at that point as the majorant and the transmittance to it,
        // so every collision is a real one: there is no majorant and no null collision, and the weights of the callers are those
        // of a homogeneous medium, T sigma / (T[0] sigma_t[0]).
        public SampledSpectrum SampleT_maj(Ray ray, Float tMax, Float u, ref RNG rng, SampledWavelengths lambda,
                                           Func<Point3f, MediumProperties, SampledSpectrum, SampledSpectrum, bool> callback)
        {
            // Normalize ray direction and update _tMax_ accordingly
            tMax *= Length(ray.d);
            ray.d = Normalize(ray.d);

            Float t1 = tMax;
            Ray r = renderFromMedium.ApplyInverse(ray, &t1);
            double tStart = 0, tEnd = t1;
            if (bounded)
            {
                Float tMin, tLast;
                if (!bounds.IntersectP(r.o, r.d, t1, &tMin, &tLast))
                {
                    return new SampledSpectrum(1f);
                }
                tStart = tMin;
                tEnd = tLast;
            }
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            if (!(sigma_t[0] > 0) || !(tEnd > tStart))
            {
                // Nothing for the wavelength that tracks to collide with
                SampledSpectrum T_end = new SampledSpectrum(1f);
                if (tEnd > tStart)
                {
                    double depth = IsInf(t1) && !bounded ? Double.PositiveInfinity
                                                         : noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, tStart, tEnd) / noise.Norm();
                    for (int c = 0; c < NSpectrumSamples; ++c)
                    {
                        T_end[c] = sigma_t[c] > 0 ? (Float)Math.Exp(-sigma_t[c] * depth) : 1f;
                    }
                }
                return T_end;
            }
            while (true)
            {
                // The optical depth to the collision, in units of the density, and the distance that has it
                double target = -Math.Log(1.0 - u) / sigma_t[0];
                double t, toEnd;
                bool collided = noise.InvertFieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, tStart, tEnd, target * noise.Norm(), out t, out toEnd);
                double depth = collided ? target : toEnd / noise.Norm();
                SampledSpectrum T_maj = new SampledSpectrum(1f);
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    T_maj[c] = (Float)Math.Exp(-sigma_t[c] * depth);
                }
                if (!collided)
                {
                    return T_maj;
                }
                u = rng.Uniform<Float>();
                Point3f p = ray.Apply((Float)t);
                MediumProperties mp = SamplePoint(p, lambda, ray.time);
                SampledSpectrum sigma_maj = mp.sigma_a + mp.sigma_s;
                tStart = t;
                if (!(sigma_maj[0] > 0))
                {
                    // (a point where the field touches zero: of probability zero)
                    continue;
                }
                if (!callback(p, mp, sigma_maj, T_maj))
                {
                    return new SampledSpectrum(1f);
                }
            }
        }

        public RayMajorantIterator SampleRay(Ray ray, Float raytMax, SampledWavelengths lambda)
        {
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            if (!bounded)
            {
                // One majorant: the bound of the density over all of space
                return new HomogeneousMajorantIterator(0, raytMax, (Float)noise.MaxDensity() * sigma_t);
            }
            ray = renderFromMedium.ApplyInverse(ray, &raytMax);
            Float tMin, tMax;
            if (!bounds.IntersectP(ray.o, ray.d, raytMax, &tMin, &tMax))
            {
                return new DDAMajorantIterator();
            }
            return new DDAMajorantIterator(ray, tMin, tMax, densityGrid, sigma_t);
        }

        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            return SampleRay(ray, tMax, lambda);
        }

        // The transmittance of the segment of the ray up to tMax (in units of the length of ray.d), for "closedform".
        //
        // The optical depth of the field is the closed form. Where the field is never negative that is the whole of it and the
        // result is exact. Otherwise the density is the field plus r = max(0, -field), and exp(-integral of r) is estimated by
        // ratio tracking against the majorant of r; the collisions are sampled with the largest extinction over the wavelengths,
        // so the weight of every wavelength stays between 0 and 1 and no path probability depends on the estimate.
        public override bool ClosedFormTransmittance(Ray ray, Float tMax, ref RNG rng, SampledWavelengths lambda, out SampledSpectrum T)
        {
            T = new SampledSpectrum(1f);
            double lengthPerT = Length(ray.d);
            Float t0 = 0, t1 = tMax;
            Ray r = renderFromMedium.ApplyInverse(ray, &t1);
            if (bounded)
            {
                Float tMin, tEnd;
                if (!bounds.IntersectP(r.o, r.d, t1, &tMin, &tEnd))
                {
                    return true;
                }
                t0 = tMin;
                t1 = tEnd;
            }
            if (IsInf(t1) || !(t1 > t0))
            {
                // (an endless segment has no finite optical depth to write down: the tracker handles it)
                return !IsInf(t1);
            }
            SampledSpectrum sigma_t = density * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
            double depth = 0;

            // The closed form of the field, and ratio tracking of what it leaves
            double* weights = stackalloc double[NSpectrumSamples];
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                weights[c] = 1;
            }
            double largest = sigma_t.MaxComponentValue();
            if (noise.NeverNegative())
            {
                // Nothing is clamped: the closed form is the transmittance
                depth = noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, t0, t1) / noise.Norm() * lengthPerT;
            }
            else if (bounded)
            {
                // Cell by cell, as the control grid says; the closed form is taken once over each run of cells that use it
                DDAMajorantIterator cells = new DDAMajorantIterator(r, t0, t1, controlGrid, new SampledSpectrum(1f));
                bool any = true, inRun = false;
                double runStart = 0, runEnd = 0;
                while (any)
                {
                    pstd.optional<RayMajorantSegment> segment = cells.Next();
                    if (!segment)
                    {
                        break;
                    }
                    RayMajorantSegment sg = segment.value();
                    Float control = sg.sigma_maj[0];
                    if (control >= 0)
                    {
                        // The closed form, and what the clamp adds against its bound in the cell
                        if (inRun && sg.tMin == runEnd)
                        {
                            runEnd = sg.tMax;
                        }
                        else
                        {
                            if (inRun)
                            {
                                depth += noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, runStart, runEnd) / noise.Norm() * lengthPerT;
                            }
                            inRun = true;
                            runStart = sg.tMin;
                            runEnd = sg.tMax;
                        }
                        if (control > 0)
                        {
                            any = Track(r, sg.tMin, sg.tMax, true, largest * control, lengthPerT, sigma_t, ref rng, weights);
                        }
                    }
                    else if (!IsInf(control))
                    {
                        // Ratio tracking of the density against its bound in the cell
                        any = Track(r, sg.tMin, sg.tMax, false, largest * -control, lengthPerT, sigma_t, ref rng, weights);
                    }
                }
                if (inRun)
                {
                    depth += noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, runStart, runEnd) / noise.Norm() * lengthPerT;
                }
            }
            else
            {
                depth = noise.FieldIntegral(r.o.x, r.o.y, r.o.z, r.d.x, r.d.y, r.d.z, t0, t1) / noise.Norm() * lengthPerT;
                Track(r, t0, t1, true, largest * noise.ResidualBound(), lengthPerT, sigma_t, ref rng, weights);
            }
            for (int c = 0; c < NSpectrumSamples; ++c)
            {
                T[c] = (Float)(Math.Exp(-sigma_t[c] * depth) * weights[c]);
            }
            return true;
        }

        // Ratio tracking over [t0, t1] against a majorant there, of what the clamp adds to the field (max(0, -field)) or of the
        // density itself (max(0, field)); false once every weight is zero
        private bool Track(Ray r, double t0, double t1, bool clampedOff, double majorant, double lengthPerT, SampledSpectrum sigma_t, ref RNG rng,
                           double* weights)
        {
            double t = t0;
            while (true)
            {
                t -= Math.Log(1.0 - rng.Uniform<Float>()) / (majorant * lengthPerT);
                if (!(t < t1))
                {
                    return true;
                }
                double field = noise.Field(r.o.x + t * r.d.x, r.o.y + t * r.d.y, r.o.z + t * r.d.z) / noise.Norm();
                double value = clampedOff ? -field : field;
                if (!(value > 0))
                {
                    continue;
                }
                bool any = false;
                for (int c = 0; c < NSpectrumSamples; ++c)
                {
                    weights[c] *= 1.0 - sigma_t[c] * value / majorant;
                    any = any || weights[c] != 0;
                }
                if (!any)
                {
                    return false;
                }
            }
        }

        public override bool AggregateRegion(out Bounds3f mediumBounds, out Transform renderFromMedium, out MajorantGrid grid)
        {
            mediumBounds = bounds;
            renderFromMedium = this.renderFromMedium;
            grid = densityGrid;
            return bounded;
        }

        public override SampledSpectrum AggregateExtinction(SampledWavelengths lambda)
        {
            return (Float)(density * noise.MaxDensity()) * (sigma_a_spec.Sample(lambda) + sigma_s_spec.Sample(lambda));
        }

        public static CosineNoiseMedium Create(ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            Float g = parameters.GetOneFloat("g", 0f);
            Spectrum sigma_a = parameters.GetOneSpectrum("sigma_a", null, SpectrumType.Unbounded, alloc);
            if (sigma_a == null)
            {
                sigma_a = new ConstantSpectrum(1f);
            }
            Spectrum sigma_s = parameters.GetOneSpectrum("sigma_s", null, SpectrumType.Unbounded, alloc);
            if (sigma_s == null)
            {
                sigma_s = new ConstantSpectrum(1f);
            }
            int octaves = parameters.GetOneInt("octaves", 4);
            Float offset = parameters.GetOneFloat("offset", 1);
            if (octaves < 1 || octaves > 16)
            {
                ErrorExit(loc, "\"cosinenoise\": \"octaves\" is 1 to 16.");
            }
            int waves = parameters.GetOneInt("waves", 24);
            if (waves < 1 || waves > 64)
            {
                ErrorExit(loc, "\"cosinenoise\": \"waves\" is 1 to 64.");
            }
            CosineNoise noise = new CosineNoise(parameters.GetOneFloat("frequency", 1), octaves, waves, parameters.GetOneFloat("lacunarity", 2),
                                                parameters.GetOneFloat("gain", 0.5f), offset);
            // A box of the medium's own space, where one is given
            Point3f far = new Point3f(Infinity, Infinity, Infinity);
            Point3f p0 = parameters.GetOnePoint3f("p0", far);
            Point3f p1 = parameters.GetOnePoint3f("p1", far);
            bool bounded = !IsInf(p0.x) || !IsInf(p1.x);
            if (bounded && (IsInf(p0.x) || IsInf(p1.x)))
            {
                ErrorExit(loc, "\"cosinenoise\": \"p0\" and \"p1\" are given together.");
            }
            int majorantResolution = parameters.GetOneInt("majorantres", 64);
            if (majorantResolution < 1 || majorantResolution > 512)
            {
                ErrorExit(loc, "\"cosinenoise\": \"majorantres\" is 1 to 512.");
            }
            // The distances to the collisions: from the closed form where the field is never negative, by tracking otherwise
            string sampling = parameters.GetOneString("sampling", noise.NeverNegative() ? "closedform" : "majorant");
            if (sampling != "closedform" && sampling != "majorant")
            {
                ErrorExit(loc, "\"cosinenoise\": \"sampling\" is \"closedform\" or \"majorant\".");
            }
            if (sampling == "closedform" && !noise.NeverNegative())
            {
                ErrorExit(loc, StringPrintf("\"cosinenoise\": \"sampling\" \"closedform\" is for a field that is never negative: with these waves that is "
                                            + "\"offset\" %f or more.", noise.Amplitude() / noise.Norm()));
            }
            return new CosineNoiseMedium(bounded, bounded ? new Bounds3f(p0, p1) : new Bounds3f(), renderFromMedium, sigma_a, sigma_s, g,
                                         parameters.GetOneFloat("density", 1), noise, sampling == "closedform", majorantResolution, alloc);
        }

        // CosineNoiseMedium Private Members
        private readonly bool bounded, closedFormSampling;
        private readonly Bounds3f bounds;
        private readonly Transform renderFromMedium;
        private readonly HGPhaseFunction phase;
        private readonly DenselySampledSpectrum sigma_a_spec, sigma_s_spec;
        private readonly Float density;
        private readonly CosineNoise noise;
        private readonly MajorantGrid densityGrid, controlGrid;
    }
}
