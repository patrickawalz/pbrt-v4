// (not in pbrt-v4) OptimizedPolarisedLightSystem of PolarisedUPBP's PolarisedLightSystems.hxx (Petr Vevoda, Martin Sik, Tomas
// Davidovic, Iliyan Georgiev, Jaroslav Krivanek; modified by Michal Mojzik; MIT license), ported to C#.
//
// Light, Importance and Attenuation are the three types that every throughput product of a path goes through: Light is the
// Stokes vector of radiance, Attenuation the Mueller matrix of an event, Importance what the sensor responds to. Each carries
// the reference frames that its Stokes components are expressed in, and a product rotates one into the other. The types only
// hold what their kind of element needs: a Lambertian reflection is a depolariser (one value), a scalar is one value, and
// the remaining entries stay unused.

using System;

namespace pbrt
{
    public enum PolarisationElementType
    {
        Zero,
        Plain,
        DepolarisingEnter,
        DepolarisingExit,
        DepolarisingBoth,
        Standard,
        FrameUnset
    }

    public static class PolarisedLightSystem
    {
        public static bool IsPolarised()
        {
            return true;
        }

        // Radiance: a Stokes vector with the frame it is expressed in
        public struct Light
        {
            internal StokesVector stokes;
            internal PolarisationFrame frame;
            internal PolarisationElementType elementType;

            public Light(SampledSpectrum intensity)
            {
                stokes = new StokesVector(intensity);
                frame = default;
                elementType = PolarisationElementType.Plain;
            }

            public Light(StokesVector stokes)
            {
                this.stokes = stokes;
                frame = default;
                elementType = PolarisationElementType.FrameUnset;
            }

            public static Light Zero()
            {
                Light light = default;
                light.elementType = PolarisationElementType.Zero;
                return light;
            }

            public bool IsPolarised()
            {
                return stokes.IsPolarised();
            }

            public SampledSpectrum GetIntensity()
            {
                return elementType == PolarisationElementType.Zero ? new SampledSpectrum(0.0f) : stokes.s0;
            }

            public void operator *=(SampledSpectrum b)
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                    break;
                case PolarisationElementType.Plain:
                    stokes.s0 *= b;
                    break;
                default:
                    stokes *= b;
                    break;
                }
            }

            public void operator /=(SampledSpectrum b)
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                    break;
                case PolarisationElementType.Plain:
                    stokes.s0 /= b;
                    break;
                default:
                    stokes /= b;
                    break;
                }
            }

            public void operator *=(Attenuation b)
            {
                this = b * this;
            }

            public void operator +=(Light b)
            {
                Add(b, 1);
            }

            public void operator -=(Light b)
            {
                Add(b, -1);
            }

            public static Light operator *(Light a, SampledSpectrum b)
            {
                a *= b;
                return a;
            }

            public static Light operator /(Light a, SampledSpectrum b)
            {
                a /= b;
                return a;
            }

            public static Light operator +(Light a, Light b)
            {
                a += b;
                return a;
            }

            public static Light operator -(Light a, Light b)
            {
                a -= b;
                return a;
            }

            // Adds b multiplied by mult (1 or -1) to this light
            void Add(Light b, Float mult)
            {
                switch (b.elementType)
                {
                case PolarisationElementType.Zero:
                    return;
                case PolarisationElementType.Plain:
                    if (elementType == PolarisationElementType.Zero)
                    {
                        this = b;
                        this *= new SampledSpectrum(mult);
                    }
                    else
                    {
                        stokes.s0 += mult * b.stokes.s0;
                    }

                    break;
                case PolarisationElementType.Standard:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        this = b;
                        this *= new SampledSpectrum(mult);
                        break;
                    case PolarisationElementType.Plain:
                        stokes.s0 += mult * b.stokes.s0;
                        stokes.s1 = mult * b.stokes.s1;
                        stokes.s2 = mult * b.stokes.s2;
                        stokes.s3 = mult * b.stokes.s3;
                        frame = b.frame;
                        elementType = PolarisationElementType.Standard;
                        break;
                    case PolarisationElementType.Standard:
                        StokesVector bStokes = b.stokes;
                        bStokes.RotateFrame(frame, b.frame);
                        bStokes *= new SampledSpectrum(mult);
                        stokes += bStokes;
                        break;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of a light is not set");
                        break;
                    }

                    break;
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: the frame of a light is not set");
                    break;
                }
            }

            // Light that travels in worldDir: its frame is set from it (a zero or plain light has no frame)
            public Light InDirection(Vector3f worldDir)
            {
                if (elementType == PolarisationElementType.Zero || elementType == PolarisationElementType.Plain)
                {
                    return this;
                }

                frame.SetFromZ(worldDir);
                elementType = PolarisationElementType.Standard;
                return this;
            }

            public Light InDirection(Vector3f worldDir, Vector3f worldUp)
            {
                if (elementType == PolarisationElementType.Zero || elementType == PolarisationElementType.Plain)
                {
                    return this;
                }

                frame.SetFromZX(worldDir, worldUp);
                elementType = PolarisationElementType.Standard;
                return this;
            }

            // The same light expressed in the frame of a direction and an up vector
            public Light RepresentInDirection(Vector3f worldDir, Vector3f worldUp)
            {
                PolarisationFrame newFrame = default;
                newFrame.SetFromZX(worldDir, worldUp);
                return RotateToFrame(newFrame);
            }

            public StokesVector ToStokesVector()
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                    return new StokesVector(new SampledSpectrum(0.0f), new SampledSpectrum(0.0f), new SampledSpectrum(0.0f),
                                            new SampledSpectrum(0.0f));
                case PolarisationElementType.Plain:
                    return new StokesVector(stokes.s0, new SampledSpectrum(0.0f), new SampledSpectrum(0.0f),
                                            new SampledSpectrum(0.0f));
                case PolarisationElementType.Standard:
                    return stokes;
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: the frame of a light is not set");
                    return stokes;
                }
            }

            internal Light ConfigureReferenceFrame(PolarisationFrame newFrame)
            {
                if (elementType == PolarisationElementType.Zero || elementType == PolarisationElementType.Plain)
                {
                    return this;
                }

                frame = newFrame;
                elementType = PolarisationElementType.Standard;
                return this;
            }

            // (PolarisedUPBP returns from the Standard case before the rotation, which leaves it unreachable; a light with a
            // frame is rotated to the new one)
            Light RotateToFrame(PolarisationFrame newFrame)
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                case PolarisationElementType.Plain:
                    return this;
                case PolarisationElementType.Standard:
                    break;
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: the frame of a light is not set");
                    return this;
                }

                stokes.RotateFrame(newFrame, frame);
                frame = newFrame;
                return this;
            }
        }

        // A Mueller matrix with the two frames that it connects
        public struct Attenuation
        {
            // The frame in which the matrix expects the Stokes vector of its argument; its type tells what is in use
            internal MuellerMatrix matrix;
            internal PolarisationFrame enterFrame;
            internal PolarisationFrame exitFrame;
            internal PolarisationElementType elementType;

            Attenuation(SampledSpectrum depolariserIntensity, PolarisationElementType validity)
            {
                matrix = new MuellerMatrix(depolariserIntensity);
                enterFrame = default;
                exitFrame = default;
                elementType = validity;
            }

            public Attenuation(SampledSpectrum intensity)
            {
                matrix = new MuellerMatrix(intensity);
                enterFrame = default;
                exitFrame = default;
                elementType = PolarisationElementType.Plain;
            }

            public Attenuation(MuellerMatrix matrix)
            {
                this.matrix = matrix;
                enterFrame = default;
                exitFrame = default;
                elementType = PolarisationElementType.FrameUnset;
            }

            public static Attenuation Zero()
            {
                Attenuation attenuation = default;
                attenuation.elementType = PolarisationElementType.Zero;
                return attenuation;
            }

            public static Attenuation Identity()
            {
                return new Attenuation(new SampledSpectrum(1.0f));
            }

            public static Attenuation LinearPolariser(Float radianAngle)
            {
                SampledSpectrum zero = new SampledSpectrum(0.0f);
                Float cos = std.cos(2 * radianAngle);
                Float sin = std.sin(2 * radianAngle);
                return new Attenuation(new MuellerMatrix(
                    new SampledSpectrum(0.5f), new SampledSpectrum(cos * 0.5f), new SampledSpectrum(sin * 0.5f), zero,
                    new SampledSpectrum(cos * 0.5f), new SampledSpectrum(cos * cos * 0.5f), new SampledSpectrum(sin * cos * 0.5f), zero,
                    new SampledSpectrum(sin * 0.5f), new SampledSpectrum(sin * cos * 0.5f), new SampledSpectrum(sin * sin * 0.5f), zero,
                    zero, zero, zero, zero));
            }

            // A diffuse reflection scatters into all polarisations the same: it only has its first element
            public static Attenuation Lambert(SampledSpectrum intensity)
            {
                return new Attenuation(intensity, PolarisationElementType.DepolarisingBoth);
            }

            // Fs and Fp are the reflectances of the perpendicular and parallel polarisations, Ds and Dp their retardances
            public static Attenuation Fresnel(SampledSpectrum multipliedBy, Float Fs, Float Fp, Float Ds, Float Dp)
            {
                return Fresnel(multipliedBy, new SampledSpectrum(Fs), new SampledSpectrum(Fp), new SampledSpectrum(Ds),
                               new SampledSpectrum(Dp));
            }

            // The same for terms that differ between the wavelengths, as those of a conductor
            public static Attenuation Fresnel(SampledSpectrum multipliedBy, SampledSpectrum Fs, SampledSpectrum Fp,
                                              SampledSpectrum Ds, SampledSpectrum Dp)
            {
                SampledSpectrum A = (Fs + Fp) * 0.5f * multipliedBy;
                SampledSpectrum B = (Fs - Fp) * 0.5f * multipliedBy;
                SampledSpectrum C = new SampledSpectrum(0.0f), S = new SampledSpectrum(0.0f);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    Float D = Ds[i] - Dp[i];
                    Float sqrtFsFp = std.sqrt(Fs[i] * Fp[i]);
                    C[i] = std.cos(D) * sqrtFsFp * multipliedBy[i];
                    S[i] = std.sin(D) * sqrtFsFp * multipliedBy[i];
                }

                SampledSpectrum zero = new SampledSpectrum(0.0f);
                return new Attenuation(new MuellerMatrix(
                    A, B, zero, zero,
                    B, A, zero, zero,
                    zero, zero, C, S,
                    zero, zero, -S, C));
            }

            public static Attenuation Rayleigh(SampledSpectrum d0, Float cosPhi)
            {
                cosPhi = -cosPhi;
                Float cosPhi2 = cosPhi * cosPhi;
                Float sinPhi2 = 1.0f - cosPhi * cosPhi;

                SampledSpectrum f00 = d0 * (-sinPhi2) / (1.0f + cosPhi2);
                SampledSpectrum f01 = d0 * (2.0f * cosPhi) / (1.0f + cosPhi2);

                SampledSpectrum zero = new SampledSpectrum(0.0f);
                return new Attenuation(new MuellerMatrix(
                    d0, f00, zero, zero,
                    f00, d0, zero, zero,
                    zero, zero, f01, zero,
                    zero, zero, zero, f01));
            }

            public SampledSpectrum GetIntensity()
            {
                return elementType == PolarisationElementType.Zero ? new SampledSpectrum(0.0f) : matrix.m00;
            }

            public MuellerMatrix GetMuellerMatrix()
            {
                return matrix;
            }

            public bool HasNaNs()
            {
                for (int row = 0; row < 4; ++row)
                {
                    for (int column = 0; column < 4; ++column)
                    {
                        if (matrix.Get(row, column).HasNaNs())
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public static Light operator *(Attenuation a, Light b)
            {
                switch (b.elementType)
                {
                case PolarisationElementType.Zero:
                    return Light.Zero();
                case PolarisationElementType.Plain:
                    switch (a.elementType)
                    {
                    case PolarisationElementType.Zero:
                        return Light.Zero();
                    case PolarisationElementType.Plain:
                    case PolarisationElementType.DepolarisingBoth:
                    case PolarisationElementType.DepolarisingExit:
                        return new Light(a.matrix.m00 * b.stokes.s0);
                    case PolarisationElementType.DepolarisingEnter:
                    case PolarisationElementType.Standard:
                        return new Light(new StokesVector(a.matrix.m00 * b.stokes.s0, a.matrix.m10 * b.stokes.s0,
                                                          a.matrix.m20 * b.stokes.s0, a.matrix.m30 * b.stokes.s0))
                            .ConfigureReferenceFrame(a.exitFrame);
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return Light.Zero();
                    }
                case PolarisationElementType.Standard:
                    switch (a.elementType)
                    {
                    case PolarisationElementType.Zero:
                        return Light.Zero();
                    case PolarisationElementType.Plain:
                        return b * a.matrix.m00;
                    case PolarisationElementType.DepolarisingBoth:
                        return new Light(a.matrix.m00 * b.stokes.s0);
                    case PolarisationElementType.DepolarisingExit:
                    {
                        StokesVector stokes = b.stokes;
                        stokes.RotateFrame(a.enterFrame, b.frame);
                        return new Light(a.matrix.m00 * stokes.s0 + a.matrix.m01 * stokes.s1 + a.matrix.m02 * stokes.s2 +
                                         a.matrix.m03 * stokes.s3);
                    }
                    case PolarisationElementType.DepolarisingEnter:
                        return new Light(new StokesVector(a.matrix.m00 * b.stokes.s0, a.matrix.m10 * b.stokes.s0,
                                                          a.matrix.m20 * b.stokes.s0, a.matrix.m30 * b.stokes.s0))
                            .ConfigureReferenceFrame(a.exitFrame);
                    case PolarisationElementType.Standard:
                    {
                        StokesVector stokes = b.stokes;
                        stokes.RotateFrame(a.enterFrame, b.frame);
                        return new Light(a.matrix * stokes).ConfigureReferenceFrame(a.exitFrame);
                    }
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return Light.Zero();
                    }
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: the frame of a light is not set");
                    return Light.Zero();
                }
            }

            public static Attenuation operator *(Attenuation a, Attenuation b)
            {
                a *= b;
                return a;
            }

            public void operator *=(Attenuation b)
            {
                switch (b.elementType)
                {
                case PolarisationElementType.Zero:
                    this = Zero();
                    return;
                case PolarisationElementType.Plain:
                    this *= b.matrix.m00;
                    return;
                case PolarisationElementType.DepolarisingBoth:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        return;
                    case PolarisationElementType.Plain:
                    case PolarisationElementType.DepolarisingExit:
                        matrix.m00 *= b.matrix.m00;
                        elementType = PolarisationElementType.DepolarisingBoth;
                        return;
                    case PolarisationElementType.DepolarisingBoth:
                        matrix.m00 *= b.matrix.m00;
                        return;
                    case PolarisationElementType.DepolarisingEnter:
                    case PolarisationElementType.Standard:
                        matrix.m00 *= b.matrix.m00;
                        matrix.m10 *= b.matrix.m00;
                        matrix.m20 *= b.matrix.m00;
                        matrix.m30 *= b.matrix.m00;
                        elementType = PolarisationElementType.DepolarisingEnter;
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return;
                    }
                case PolarisationElementType.DepolarisingExit:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        return;
                    case PolarisationElementType.Plain:
                    case PolarisationElementType.DepolarisingBoth:
                    {
                        SampledSpectrum value = matrix.m00;
                        this = b;
                        this *= value;
                        return;
                    }
                    case PolarisationElementType.DepolarisingExit:
                        matrix.m03 = matrix.m00 * b.matrix.m03;
                        matrix.m02 = matrix.m00 * b.matrix.m02;
                        matrix.m01 = matrix.m00 * b.matrix.m01;
                        matrix.m00 *= b.matrix.m00;
                        return;
                    case PolarisationElementType.DepolarisingEnter:
                    case PolarisationElementType.Standard:
                        for (int row = 0; row < 4; ++row)
                        {
                            for (int column = 3; column >= 0; --column)
                            {
                                matrix.Set(row, column, matrix.Get(row, 0) * b.matrix.Get(0, column));
                            }
                        }

                        enterFrame = b.enterFrame;
                        elementType = PolarisationElementType.Standard;
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return;
                    }
                case PolarisationElementType.DepolarisingEnter:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        return;
                    case PolarisationElementType.Plain:
                    {
                        SampledSpectrum value = matrix.m00;
                        this = b;
                        this *= value;
                        return;
                    }
                    case PolarisationElementType.DepolarisingBoth:
                    case PolarisationElementType.DepolarisingEnter:
                        this *= b.matrix.m00;
                        return;
                    case PolarisationElementType.DepolarisingExit:
                    {
                        StokesVector.GetRotationCosSin(enterFrame, b.exitFrame, out Float cos2, out Float sin2);
                        matrix.m00 = matrix.m00 * b.matrix.m00 +
                                     matrix.m01 * (cos2 * b.matrix.m10 + sin2 * b.matrix.m20) +
                                     matrix.m02 * (-sin2 * b.matrix.m10 + cos2 * b.matrix.m20) +
                                     matrix.m03 * b.matrix.m30;
                        elementType = PolarisationElementType.DepolarisingBoth;
                        return;
                    }
                    case PolarisationElementType.Standard:
                        matrix *= MuellerMatrix.RotateFrame(enterFrame, b.exitFrame);
                        for (int row = 0; row < 4; ++row)
                        {
                            matrix.Set(row, 0, matrix.Get(row, 0) * b.matrix.Get(0, 0));
                            for (int column = 1; column < 4; ++column)
                            {
                                matrix.Set(row, 0, matrix.Get(row, 0) + matrix.Get(row, column) * b.matrix.Get(0, column));
                            }
                        }

                        elementType = PolarisationElementType.DepolarisingEnter;
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return;
                    }
                case PolarisationElementType.Standard:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        return;
                    case PolarisationElementType.Plain:
                    {
                        SampledSpectrum value = matrix.m00;
                        this = b;
                        this *= value;
                        return;
                    }
                    case PolarisationElementType.DepolarisingBoth:
                        matrix.m01 = matrix.m00 * b.matrix.m01;
                        matrix.m02 = matrix.m00 * b.matrix.m02;
                        matrix.m03 = matrix.m00 * b.matrix.m03;
                        matrix.m00 *= b.matrix.m00;
                        enterFrame = b.enterFrame;
                        elementType = PolarisationElementType.DepolarisingExit;
                        return;
                    case PolarisationElementType.DepolarisingExit:
                    {
                        StokesVector.GetRotationCosSin(enterFrame, b.exitFrame, out Float cos2, out Float sin2);
                        // (PolarisedUPBP stores this rotated row in the second row of the matrix and then multiplies the
                        // unrotated first row; the rotated row is what the product needs)
                        SampledSpectrum rotated0 = matrix.m00;
                        SampledSpectrum rotated1 = cos2 * matrix.m01 - sin2 * matrix.m02;
                        SampledSpectrum rotated2 = sin2 * matrix.m01 + cos2 * matrix.m02;
                        SampledSpectrum rotated3 = matrix.m03;
                        for (int column = 0; column < 4; ++column)
                        {
                            matrix.Set(0, column, rotated0 * b.matrix.Get(0, column) + rotated1 * b.matrix.Get(1, column) +
                                                  rotated2 * b.matrix.Get(2, column) + rotated3 * b.matrix.Get(3, column));
                        }

                        enterFrame = b.enterFrame;
                        elementType = PolarisationElementType.DepolarisingExit;
                        return;
                    }
                    case PolarisationElementType.DepolarisingEnter:
                        for (int row = 0; row < 4; ++row)
                        {
                            for (int column = 3; column >= 0; --column)
                            {
                                matrix.Set(row, column, matrix.Get(row, 0) * b.matrix.Get(0, column));
                            }
                        }

                        enterFrame = b.enterFrame;
                        elementType = PolarisationElementType.Standard;
                        return;
                    case PolarisationElementType.Standard:
                        matrix *= MuellerMatrix.RotateFrame(enterFrame, b.exitFrame) * b.matrix;
                        enterFrame = b.enterFrame;
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                        return;
                    }
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: the frame of an attenuation is not set");
                    return;
                }
            }

            public void operator *=(SampledSpectrum b)
            {
                Scale(b, false);
            }

            public void operator /=(SampledSpectrum b)
            {
                Scale(b, true);
            }

            public static Attenuation operator *(Attenuation a, SampledSpectrum b)
            {
                a *= b;
                return a;
            }

            // Multiplies or divides the entries that the kind of element uses
            void Scale(SampledSpectrum b, bool divide)
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                    return;
                case PolarisationElementType.DepolarisingBoth:
                case PolarisationElementType.Plain:
                    matrix.m00 = divide ? matrix.m00 / b : matrix.m00 * b;
                    return;
                case PolarisationElementType.DepolarisingEnter:
                    matrix.m00 = divide ? matrix.m00 / b : matrix.m00 * b;
                    matrix.m10 = divide ? matrix.m10 / b : matrix.m10 * b;
                    matrix.m20 = divide ? matrix.m20 / b : matrix.m20 * b;
                    matrix.m30 = divide ? matrix.m30 / b : matrix.m30 * b;
                    return;
                case PolarisationElementType.DepolarisingExit:
                    matrix.m00 = divide ? matrix.m00 / b : matrix.m00 * b;
                    matrix.m01 = divide ? matrix.m01 / b : matrix.m01 * b;
                    matrix.m02 = divide ? matrix.m02 / b : matrix.m02 * b;
                    matrix.m03 = divide ? matrix.m03 / b : matrix.m03 * b;
                    return;
                case PolarisationElementType.Standard:
                    if (divide)
                    {
                        matrix /= b;
                    }
                    else
                    {
                        matrix *= b;
                    }

                    return;
                }
            }

            public void operator +=(Attenuation b)
            {
                Add(b, 1);
            }

            public void operator -=(Attenuation b)
            {
                Add(b, -1);
            }

            // Adds b multiplied by mult (1 or -1) to this attenuation
            void Add(Attenuation b, Float mult)
            {
                SampledSpectrum scale = new SampledSpectrum(mult);
                switch (b.elementType)
                {
                case PolarisationElementType.Zero:
                    return;
                case PolarisationElementType.DepolarisingBoth:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        this = b;
                        this *= scale;
                        return;
                    case PolarisationElementType.DepolarisingBoth:
                    case PolarisationElementType.Standard:
                        matrix.m00 += mult * b.matrix.m00;
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: plain attenuations and partial depolarisers cannot be added");
                        return;
                    }
                case PolarisationElementType.Standard:
                    switch (elementType)
                    {
                    case PolarisationElementType.Zero:
                        this = b;
                        this *= scale;
                        return;
                    case PolarisationElementType.DepolarisingBoth:
                    {
                        SampledSpectrum value = matrix.m00;
                        this = b;
                        this *= scale;
                        matrix.m00 += value;
                        return;
                    }
                    case PolarisationElementType.Standard:
                        matrix += MuellerMatrix.RotateFrame(exitFrame, b.exitFrame) * (b.matrix * scale) *
                                  MuellerMatrix.RotateFrame(b.enterFrame, enterFrame);
                        return;
                    default:
                        pbrt.LOG_FATAL("PolarisedLightSystem: plain attenuations and partial depolarisers cannot be added");
                        return;
                    }
                default:
                    pbrt.LOG_FATAL("PolarisedLightSystem: plain attenuations and partial depolarisers cannot be added");
                    return;
                }
            }

            // Both frames point the same way: for the participating media. Only to be called while the frame is not set.
            public Attenuation InDirection(Vector3f worldDirFix, bool fromCamera)
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                case PolarisationElementType.DepolarisingBoth:
                case PolarisationElementType.DepolarisingEnter:
                case PolarisationElementType.DepolarisingExit:
                case PolarisationElementType.Plain:
                    return this;
                }

                enterFrame.SetFromZ(fromCamera ? -worldDirFix : worldDirFix);
                exitFrame = enterFrame;
                elementType = PolarisationElementType.Standard;
                return this;
            }

            // The frames of a scattering at a surface: the light arrives along -worldDirFix and leaves along worldDirGen (the other
            // way round for a path from the camera). As in Mitsuba 3's BSDFs the x axis of the Stokes frame at both ends is the
            // s axis, perpendicular to the plane of the two directions and the normal: cross(normal, propagation direction).
            // That is the axis that the Mueller matrices of Fresnel() are written for.
            public Attenuation Scattered(Vector3f worldDirFix, Vector3f worldDirGen, Vector3f worldSurfaceNormal,
                                         bool fromCamera)
            {
                if (!PrepareForFrames())
                {
                    return this;
                }

                Vector3f directionExit = fromCamera ? worldDirFix : worldDirGen;
                Vector3f directionEnter = fromCamera ? -worldDirGen : -worldDirFix;
                SetFrames(directionExit, StokesAxis(worldSurfaceNormal, directionExit), directionEnter,
                          StokesAxis(worldSurfaceNormal, directionEnter));
                return this;
            }

            // The same for a scattering in a medium: the x axis of the frames is the normal of the plane of the two directions,
            // the axis that the Mueller matrices of scattering are written for
            public Attenuation Scattered(Vector3f worldDirFix, Vector3f worldDirGen, bool fromCamera)
            {
                if (!PrepareForFrames())
                {
                    return this;
                }

                Vector3f normal = pbrt.Cross(worldDirFix, worldDirGen);
                if (pbrt.LengthSquared(normal) < 1e-12f)
                {
                    Vector3f other = std.abs(worldDirFix.x) > 0.99f ? new Vector3f(0, 1, 0) : new Vector3f(1, 0, 0);
                    normal = pbrt.Cross(worldDirFix, pbrt.Normalize(pbrt.Cross(worldDirFix, other)));
                }

                normal = pbrt.Normalize(normal);
                Vector3f directionExit = fromCamera ? worldDirFix : worldDirGen;
                Vector3f directionEnter = fromCamera ? -worldDirGen : -worldDirFix;
                SetFrames(directionExit, normal, directionEnter, normal);
                return this;
            }

            // Both frames along one direction with the given reference axis for x: the sensor looking along forward
            internal Attenuation ForSensor(Vector3f direction, Vector3f reference)
            {
                if (!PrepareForFrames())
                {
                    return this;
                }

                SetFrames(direction, reference, direction, reference);
                return this;
            }

            // Whether the attenuation takes frames (a depolariser does not), after making a scalar a matrix
            bool PrepareForFrames()
            {
                switch (elementType)
                {
                case PolarisationElementType.Zero:
                case PolarisationElementType.DepolarisingBoth:
                case PolarisationElementType.DepolarisingEnter:
                case PolarisationElementType.DepolarisingExit:
                    return false;
                case PolarisationElementType.Plain:
                    matrix = MuellerMatrix.Diagonal(matrix.m00);
                    break;
                }

                return true;
            }

            void SetFrames(Vector3f directionExit, Vector3f referenceExit, Vector3f directionEnter, Vector3f referenceEnter)
            {
                exitFrame.SetFromZX(directionExit, referenceExit);
                enterFrame.SetFromZX(directionEnter, referenceEnter);
                elementType = PolarisationElementType.Standard;
            }

            // The s axis of light that propagates along direction at a surface with the normal; any axis along the normal
            static Vector3f StokesAxis(Vector3f normal, Vector3f direction)
            {
                Vector3f axis = pbrt.Cross(normal, direction);
                return pbrt.LengthSquared(axis) == 0 ? new Vector3f(1, 0, 0) : axis;
            }
        }

        // What the sensor responds to: an attenuation that is applied to the light that arrives at the sensor
        public struct Importance
        {
            internal Attenuation attenuation;

            public Importance(SampledSpectrum intensity)
            {
                attenuation = new Attenuation(intensity);
            }

            public static Importance Zero()
            {
                Importance importance = default;
                importance.attenuation = Attenuation.Zero();
                return importance;
            }

            // The importance for a direction and the orientation of the camera
            public static Importance Camera(SampledSpectrum value, Vector3f forward, Vector3f up)
            {
                Importance importance = new Importance(value);
                importance.attenuation = importance.attenuation.ForSensor(-forward, up);
                return importance;
            }

            public SampledSpectrum GetIntensity()
            {
                return attenuation.GetIntensity();
            }

            public void operator *=(SampledSpectrum b)
            {
                attenuation *= b;
            }

            public void operator *=(Attenuation b)
            {
                attenuation *= b;
            }

            public static Importance operator *(Importance a, Attenuation b)
            {
                a *= b;
                return a;
            }

            // The Stokes vector that the sensor measures of the light
            public static StokesVector operator *(Importance a, Light b)
            {
                return (a.attenuation * b).ToStokesVector();
            }
        }
    }
}
