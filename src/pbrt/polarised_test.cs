// (not in pbrt-v4) polarised_test.cs

using System;
using testing;

namespace pbrt
{
    public static unsafe class PolarisedTests
    {
        // PolarisedUPBP's FresnelReal() (Utils2.hxx), the reference of FrDielectricPolarised()
        static void UPBPFresnelReal(Float aCosInc, Float mIOR, out Float oFs, out Float oFp, out Float oDs, out Float oDp)
        {
            if (mIOR < 1.0f && std.acos(aCosInc) >= std.asin(mIOR))
            {
                oFs = 1.0f;
                oFp = 1.0f;
            }
            else
            {
                Float cos2 = aCosInc * aCosInc;
                Float sin2 = 1 - cos2;
                Float n2 = mIOR * mIOR;
                Float sqrt_n_sin = std.sqrt(n2 - sin2);
                oFs = pbrt.Sqr(-pbrt.Sqr(sqrt_n_sin - aCosInc) / (n2 - 1));
                oFp = pbrt.Sqr((n2 * aCosInc - sqrt_n_sin) / (n2 * aCosInc + sqrt_n_sin));
            }

            oDs = -pbrt.Pi / 2;
            oDp = std.atan(mIOR) < std.acos(aCosInc) ? pbrt.Pi / 2 : -pbrt.Pi / 2;
        }

        // PolarisedUPBP's FresnelComplex()
        static void UPBPFresnelComplex(Float cosTheta, pstd.complex ior, out Float oFs, out Float oFp, out Float oDs, out Float oDp)
        {
            Float sinThetaSqr = 1 - cosTheta * cosTheta;
            Float iorSqrMinusExtinctionSqr = ior.re * ior.re - ior.im * ior.im;
            Float iorSqrPlusExtinctionSqr = ior.re * ior.re + ior.im * ior.im;
            Float term1 = iorSqrMinusExtinctionSqr - sinThetaSqr;
            Float term2 = std.sqrt(term1 * term1 + 4 * ior.re * ior.re * ior.im * ior.im);
            Float a = std.sqrt(0.5f * (term2 + iorSqrMinusExtinctionSqr - sinThetaSqr));
            Float b = std.sqrt(0.5f * (term2 - iorSqrMinusExtinctionSqr + sinThetaSqr));
            oFs = (term2 - 2 * a * cosTheta + cosTheta * cosTheta) / (term2 + 2 * a * cosTheta + cosTheta * cosTheta);
            Float t = sinThetaSqr * sinThetaSqr / (cosTheta * cosTheta);
            oFp = (term2 - 2 * a * sinThetaSqr / cosTheta + t) / (term2 + 2 * a * sinThetaSqr / cosTheta + t) * oFs;
            oDs = std.atan2(2 * b * cosTheta, cosTheta * cosTheta - term2);
            oDp = std.atan2(2 * cosTheta * (iorSqrMinusExtinctionSqr * b - 2 * ior.re * ior.im * a),
                            iorSqrPlusExtinctionSqr * iorSqrPlusExtinctionSqr * cosTheta * cosTheta - term2);
        }

        static Float WrappedDifference(Float a, Float b)
        {
            Float d = a - b;
            while (d > pbrt.Pi) d -= 2 * pbrt.Pi;
            while (d < -pbrt.Pi) d += 2 * pbrt.Pi;
            return std.abs(d);
        }

        static SampledSpectrum S(Float v)
        {
            return new SampledSpectrum(v);
        }

        [TEST("Polarised", "FresnelAverage")]
        public static void Polarised_FresnelAverage()
        {
            // The mean of the reflectances is what FrDielectric() and FrComplex() return
            RNG rng = new RNG(5);
            for (int i = 0; i < 1000; ++i)
            {
                Float cosTheta = 2 * rng.Uniform<Float>() - 1;
                Float eta = 1.05f + 2.0f * rng.Uniform<Float>();
                pbrt.FrDielectricPolarised(cosTheta, eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                EXPECT_LT(std.abs(0.5f * (Fs + Fp) - pbrt.FrDielectric(cosTheta, eta)), (Float)2e-5);

                Float k = 4 * rng.Uniform<Float>();
                Float cosPositive = rng.Uniform<Float>();
                pbrt.FrComplexPolarised(cosPositive, new pstd.complex(eta, k), out Fs, out Fp, out Ds, out Dp);
                EXPECT_LT(std.abs(0.5f * (Fs + Fp) - pbrt.FrComplex(cosPositive, new pstd.complex(eta, k))), (Float)2e-5);
            }
        }

        [TEST("Polarised", "FresnelMatchesUPBP")]
        public static void Polarised_FresnelMatchesUPBP()
        {
            RNG rng = new RNG(7);
            for (int i = 0; i < 1000; ++i)
            {
                // External reflection: the reflectances and the relative retardance agree
                Float cosTheta = 0.02f + 0.98f * rng.Uniform<Float>();
                Float eta = 1.05f + 2.0f * rng.Uniform<Float>();
                pbrt.FrDielectricPolarised(cosTheta, eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                UPBPFresnelReal(cosTheta, eta, out Float rFs, out Float rFp, out Float rDs, out Float rDp);
                EXPECT_LT(std.abs(Fs - rFs), (Float)1e-4);
                EXPECT_LT(std.abs(Fp - rFp), (Float)1e-4);
                // Only the relative retardance enters the matrix, and it is not defined where a reflectance is zero
                if (std.min(Fs, Fp) > 1e-3f)
                {
                    EXPECT_LT(WrappedDifference(Ds - Dp, rDs - rDp), (Float)1e-3);
                }

                // Conductor: the reflectances agree
                Float k = 0.1f + 4 * rng.Uniform<Float>();
                pbrt.FrComplexPolarised(cosTheta, new pstd.complex(eta, k), out Fs, out Fp, out Ds, out Dp);
                UPBPFresnelComplex(cosTheta, new pstd.complex(eta, k), out rFs, out rFp, out rDs, out rDp);
                EXPECT_LT(std.abs(Fs - rFs), (Float)2e-4);
                EXPECT_LT(std.abs(Fp - rFp), (Float)2e-4);
            }
        }

        [TEST("Polarised", "FresnelBrewster")]
        public static void Polarised_FresnelBrewster()
        {
            // At the Brewster angle tan(theta) = eta the parallel component is not reflected: unpolarised light becomes
            // fully polarised perpendicular to the plane of incidence
            Float eta = 1.5f;
            Float theta = std.atan(eta);
            pbrt.FrDielectricPolarised(std.cos(theta), eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
            EXPECT_LT(Fp, (Float)1e-6);
            Vector3f incoming = pbrt.Normalize(new Vector3f(std.sin(theta), 0, -std.cos(theta)));
            Vector3f outgoing = new Vector3f(incoming.x, 0, -incoming.z);
            PolarisedLightSystem.Attenuation reflection =
                PolarisedLightSystem.Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp).Scattered(-incoming, outgoing, new Vector3f(0, 0, 1), false);
            StokesVector reflected = (reflection * new PolarisedLightSystem.Light(S(1))).ToStokesVector();
            EXPECT_LT(std.abs(std.abs(reflected.s1[0]) - reflected.s0[0]), (Float)1e-5);
            EXPECT_GT(reflected.s0[0], (Float)0.05);
        }

        [TEST("Polarised", "Malus")]
        public static void Polarised_Malus()
        {
            // Horizontally polarised light of radiance 1 through a polariser at an angle: I cos^2(angle)
            Vector3f direction = new Vector3f(0, 0, 1);
            for (int i = 0; i <= 8; ++i)
            {
                Float angle = i * pbrt.Pi / 16;
                PolarisedLightSystem.Light horizontal = new PolarisedLightSystem.Light(new StokesVector(S(1), S(1), S(0), S(0)));
                horizontal = horizontal.InDirection(direction, new Vector3f(1, 0, 0));
                PolarisedLightSystem.Attenuation polariser = PolarisedLightSystem.Attenuation.LinearPolariser(angle);
                polariser = polariser.InDirection(direction, false);
                StokesVector o = (polariser * horizontal).ToStokesVector();
                EXPECT_LT(std.abs(o.s0[0] - std.cos(angle) * std.cos(angle)), (Float)1e-5);
            }
        }

        [TEST("Polarised", "RotateFrame")]
        public static void Polarised_RotateFrame()
        {
            // A change of frame keeps the radiance and the degree of polarisation; two compose by adding the angles
            Vector3f z = new Vector3f(0, 0, 1);
            PolarisationFrame a = default, b = default, c = default;
            a.SetFromZX(z, new Vector3f(1, 0, 0));
            b.SetFromZX(z, pbrt.Normalize(new Vector3f(1, 1, 0)));
            c.SetFromZX(z, new Vector3f(0, 1, 0));
            StokesVector s = new StokesVector(S(1), S(0.3f), S(0.4f), S(0.2f));
            StokesVector viaB = s;
            viaB.RotateFrame(b, a);
            viaB.RotateFrame(c, b);
            StokesVector direct = s;
            direct.RotateFrame(c, a);
            for (int i = 0; i < 4; ++i)
            {
                EXPECT_LT(std.abs(viaB[i][0] - direct[i][0]), (Float)1e-5);
            }

            EXPECT_EQ(s.s0[0], direct.s0[0]);
            EXPECT_EQ(s.s3[0], direct.s3[0]);
            // The matrix of the change is what RotateFrame() does to the vector
            StokesVector byMatrix = MuellerMatrix.RotateFrame(c, a) * s;
            for (int i = 0; i < 4; ++i)
            {
                EXPECT_LT(std.abs(byMatrix[i][0] - direct[i][0]), (Float)1e-5);
            }
        }

        static PolarisedLightSystem.Attenuation MakeEvent(int kind, Vector3f incoming, Vector3f outgoing, RNG rng)
        {
            Vector3f normal = pbrt.Normalize(outgoing - incoming);
            if (pbrt.LengthSquared(outgoing - incoming) < 1e-4f)
            {
                normal = new Vector3f(0, 0, 1);
            }

            if (kind == 0)
            {
                return PolarisedLightSystem.Attenuation.Lambert(S(0.2f + 0.7f * rng.Uniform<Float>()));
            }
            else if (kind == 1)
            {
                return new PolarisedLightSystem.Attenuation(S(0.2f + 0.7f * rng.Uniform<Float>()));
            }
            else if (kind == 2)
            {
                pbrt.FrDielectricPolarised(0.1f + 0.9f * rng.Uniform<Float>(), 1.5f, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                return PolarisedLightSystem.Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp).Scattered(-incoming, outgoing, normal, false);
            }
            else if (kind == 3)
            {
                return PolarisedLightSystem.Attenuation.Rayleigh(S(0.5f), pbrt.Dot(incoming, outgoing)).Scattered(-incoming, outgoing, false);
            }

            return PolarisedLightSystem.Attenuation.LinearPolariser(rng.Uniform<Float>()).Scattered(-incoming, outgoing, normal, false);
        }

        [TEST("Polarised", "ChainPairs")]
        public static void Polarised_ChainPairs()
        {
            RNG rng = new RNG(3);
            for (int first = 0; first < 5; ++first)
            {
                for (int second = 0; second < 5; ++second)
                {
                    Vector3f d0 = pbrt.Normalize(new Vector3f(0.3f, 0.5f, 0.8f)), d1 = pbrt.Normalize(new Vector3f(-0.4f, 0.2f, 0.9f)),
                             d2 = pbrt.Normalize(new Vector3f(0.7f, -0.6f, 0.1f));
                    PolarisedLightSystem.Attenuation e0 = MakeEvent(first, d0, d1, rng), e1 = MakeEvent(second, d1, d2, rng);
                    StokesVector start = new StokesVector(S(1), S(0.3f), S(-0.2f), S(0.1f));
                    PolarisedLightSystem.Light light = new PolarisedLightSystem.Light(start).InDirection(d0, new Vector3f(0, 0, 1));
                    StokesVector a = (e1 * (e0 * light)).ToStokesVector();
                    PolarisedLightSystem.Attenuation product = e1;
                    product *= e0;
                    StokesVector b = (product * light).ToStokesVector();
                    for (int i = 0; i < 4; ++i)
                    {
                        EXPECT_LT(std.abs(a[i][0] - b[i][0]), (Float)1e-4, first.ToString() + " then " + second.ToString() + " component " + i.ToString());
                    }
                }
            }
        }

        [TEST("Polarised", "ChainTriples")]
        public static void Polarised_ChainTriples()
        {
            RNG rng = new RNG(4);
            Vector3f d0 = pbrt.Normalize(new Vector3f(0.3f, 0.5f, 0.8f)), d1 = pbrt.Normalize(new Vector3f(-0.4f, 0.2f, 0.9f)),
                     d2 = pbrt.Normalize(new Vector3f(0.7f, -0.6f, 0.1f)), d3 = pbrt.Normalize(new Vector3f(-0.2f, -0.7f, -0.5f));
            for (int k0 = 0; k0 < 5; ++k0)
            {
                for (int k1 = 0; k1 < 5; ++k1)
                {
                    for (int k2 = 0; k2 < 5; ++k2)
                    {
                        PolarisedLightSystem.Attenuation e0 = MakeEvent(k0, d0, d1, rng), e1 = MakeEvent(k1, d1, d2, rng), e2 = MakeEvent(k2, d2, d3, rng);
                        PolarisedLightSystem.Light light = new PolarisedLightSystem.Light(new StokesVector(S(1), S(0.3f), S(-0.2f), S(0.1f))).InDirection(d0, new Vector3f(0, 0, 1));
                        StokesVector a = (e2 * (e1 * (e0 * light))).ToStokesVector();
                        PolarisedLightSystem.Attenuation product = e2;
                        product *= e1;
                        product *= e0;
                        StokesVector b = (product * light).ToStokesVector();
                        for (int i = 0; i < 4; ++i)
                        {
                            EXPECT_LT(std.abs(a[i][0] - b[i][0]), (Float)1e-4);
                        }
                    }
                }
            }
        }

        [TEST("Polarised", "ChainEqualsSequence")]
        public static void Polarised_ChainEqualsSequence()
        {
            // The product of the attenuations of a path, in the optimised representation (scalars, depolarisers and matrices
            // each with the entries they need), is the same as applying the events one after the other
            RNG rng = new RNG(13);
            for (int trial = 0; trial < 300; ++trial)
            {
                int count = 2 + (int)(rng.Uniform<Float>() * 4);
                Vector3f[] directions = new Vector3f[count + 1];
                for (int i = 0; i <= count; ++i)
                {
                    directions[i] = pbrt.SampleUniformSphere(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                }

                // Event i takes the light from directions[i] to directions[i + 1]; the first one applied is the last
                PolarisedLightSystem.Attenuation[] events = new PolarisedLightSystem.Attenuation[count];
                for (int i = 0; i < count; ++i)
                {
                    Vector3f incoming = directions[i], outgoing = directions[i + 1];
                    Vector3f normal = pbrt.Normalize(outgoing - incoming);
                    if (pbrt.LengthSquared(outgoing - incoming) < 1e-4f)
                    {
                        normal = new Vector3f(0, 0, 1);
                    }

                    int kind = (int)(rng.Uniform<Float>() * 5);
                    PolarisedLightSystem.Attenuation e;
                    if (kind == 0)
                    {
                        e = PolarisedLightSystem.Attenuation.Lambert(S(0.2f + 0.7f * rng.Uniform<Float>()));
                    }
                    else if (kind == 1)
                    {
                        e = new PolarisedLightSystem.Attenuation(S(0.2f + 0.7f * rng.Uniform<Float>()));
                    }
                    else if (kind == 2)
                    {
                        pbrt.FrDielectricPolarised(0.1f + 0.9f * rng.Uniform<Float>(), 1.5f, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                        e = PolarisedLightSystem.Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp).Scattered(-incoming, outgoing, normal, false);
                    }
                    else if (kind == 3)
                    {
                        e = PolarisedLightSystem.Attenuation.Rayleigh(S(0.5f), pbrt.Dot(incoming, outgoing)).Scattered(-incoming, outgoing, false);
                    }
                    else
                    {
                        e = PolarisedLightSystem.Attenuation.LinearPolariser(rng.Uniform<Float>()).Scattered(-incoming, outgoing, normal, false);
                    }

                    events[i] = e;
                }

                StokesVector start = new StokesVector(S(1), S(0.3f), S(-0.2f), S(0.1f));
                PolarisedLightSystem.Light sequential = new PolarisedLightSystem.Light(start).InDirection(directions[0], new Vector3f(0, 0, 1));
                for (int i = 0; i < count; ++i)
                {
                    sequential = events[i] * sequential;
                }

                // events[count - 1] is applied last, so it is the leftmost factor
                PolarisedLightSystem.Attenuation product = events[count - 1];
                for (int i = count - 2; i >= 0; --i)
                {
                    product *= events[i];
                }

                PolarisedLightSystem.Light chained = new PolarisedLightSystem.Light(start).InDirection(directions[0], new Vector3f(0, 0, 1));
                chained = product * chained;

                StokesVector a = sequential.ToStokesVector(), b = chained.ToStokesVector();
                for (int i = 0; i < 4; ++i)
                {
                    EXPECT_LT(std.abs(a[i][0] - b[i][0]), (Float)1e-4 * (1 + std.abs(a[0][0])));
                }
            }
        }

        [TEST("Polarised", "ImportanceMeasures")]
        public static void Polarised_ImportanceMeasures()
        {
            // The sensor of unit importance measures the radiance of unpolarised light, whatever the direction it looks in
            Vector3f forward = pbrt.Normalize(new Vector3f(0.3f, -0.2f, 1));
            PolarisedLightSystem.Importance importance = PolarisedLightSystem.Importance.Camera(S(1), forward, new Vector3f(0, 1, 0));
            PolarisedLightSystem.Light light = new PolarisedLightSystem.Light(new StokesVector(S(2), S(0), S(0), S(0)));
            light = light.InDirection(-forward, new Vector3f(0, 1, 0));
            EXPECT_LT(std.abs((importance * light).s0[0] - 2), (Float)1e-5);
            // Zero light and zero attenuation stay zero
            EXPECT_EQ(0.0f, (PolarisedLightSystem.Attenuation.Zero() * light).GetIntensity()[0]);
            EXPECT_EQ(0.0f, (PolarisedLightSystem.Attenuation.Identity() * PolarisedLightSystem.Light.Zero()).GetIntensity()[0]);
        }
    }
}
