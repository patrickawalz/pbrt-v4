// (not in pbrt-v4) Polarised light-system types: the quantity that a path carries and the operator that a vertex applies to it.
// After PolarisedUPBP's Light / Importance / Attenuation trio (Path/PolarisedLightSystems.hxx). A Stokes vector carries the
// radiance of a path as four components per wavelength, a Mueller matrix is what a scattering event or a change of reference
// frame does to it. With a scalar in place of both, a path is what pbrt-v4 already computes. These types are not used by the
// integrators yet: they are the replacement for the SampledSpectrum products in the throughput of a path.

using System;

namespace pbrt
{
    // Stokes vector (I, Q, U, V) per wavelength: the radiance (S0) and the state of polarisation of the light that is carried.
    public struct StokesLight
    {
        public SampledSpectrum S0, S1, S2, S3;

        public StokesLight(SampledSpectrum s0, SampledSpectrum s1, SampledSpectrum s2, SampledSpectrum s3)
        {
            S0 = s0; S1 = s1; S2 = s2; S3 = s3;
        }

        // Unpolarised light of a given radiance
        public static StokesLight Unpolarised(SampledSpectrum L)
        {
            return new StokesLight(L, new SampledSpectrum(0.0f), new SampledSpectrum(0.0f), new SampledSpectrum(0.0f));
        }

        // The scalar radiance that a film records
        public SampledSpectrum Radiance() { return S0; }

        public Float DegreeOfPolarisation(int wavelength)
        {
            if (S0[wavelength] <= 0)
                return 0;
            return std.sqrt(S1[wavelength] * S1[wavelength] + S2[wavelength] * S2[wavelength] + S3[wavelength] * S3[wavelength]) /
                   S0[wavelength];
        }

        public static StokesLight operator +(StokesLight a, StokesLight b)
        {
            return new StokesLight(a.S0 + b.S0, a.S1 + b.S1, a.S2 + b.S2, a.S3 + b.S3);
        }

        public static StokesLight operator *(StokesLight a, SampledSpectrum s)
        {
            return new StokesLight(a.S0 * s, a.S1 * s, a.S2 * s, a.S3 * s);
        }

        public static StokesLight operator *(StokesLight a, Float s)
        {
            return new StokesLight(a.S0 * s, a.S1 * s, a.S2 * s, a.S3 * s);
        }

        public static StokesLight operator /(StokesLight a, Float s)
        {
            return new StokesLight(a.S0 / s, a.S1 / s, a.S2 / s, a.S3 / s);
        }

        public SampledSpectrum this[int component]
        {
            get { return component == 0 ? S0 : component == 1 ? S1 : component == 2 ? S2 : S3; }
            set
            {
                if (component == 0) S0 = value;
                else if (component == 1) S1 = value;
                else if (component == 2) S2 = value;
                else S3 = value;
            }
        }
    }

    // A 4x4 Mueller matrix per wavelength. It is the attenuation of a path: a scattering event, a reflection or a rotation of
    // the reference frame. The product of the matrices of a path (in the order of travel, applied from the left) is what a
    // scalar throughput is without polarisation.
    public struct MuellerAttenuation
    {
        // Row-major, m[4 * row + column]
        SampledSpectrum m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12, m13, m14, m15;

        public SampledSpectrum this[int row, int column]
        {
            get { return Get(4 * row + column); }
            set { Set(4 * row + column, value); }
        }

        SampledSpectrum Get(int i)
        {
            switch (i)
            {
            case 0: return m0; case 1: return m1; case 2: return m2; case 3: return m3;
            case 4: return m4; case 5: return m5; case 6: return m6; case 7: return m7;
            case 8: return m8; case 9: return m9; case 10: return m10; case 11: return m11;
            case 12: return m12; case 13: return m13; case 14: return m14; default: return m15;
            }
        }

        void Set(int i, SampledSpectrum v)
        {
            switch (i)
            {
            case 0: m0 = v; break; case 1: m1 = v; break; case 2: m2 = v; break; case 3: m3 = v; break;
            case 4: m4 = v; break; case 5: m5 = v; break; case 6: m6 = v; break; case 7: m7 = v; break;
            case 8: m8 = v; break; case 9: m9 = v; break; case 10: m10 = v; break; case 11: m11 = v; break;
            case 12: m12 = v; break; case 13: m13 = v; break; case 14: m14 = v; break; default: m15 = v; break;
            }
        }

        public static MuellerAttenuation Zero()
        {
            MuellerAttenuation r = default;
            for (int i = 0; i < 16; ++i)
                r.Set(i, new SampledSpectrum(0.0f));
            return r;
        }

        public static MuellerAttenuation Identity()
        {
            MuellerAttenuation r = Zero();
            for (int i = 0; i < 4; ++i)
                r[i, i] = new SampledSpectrum(1.0f);
            return r;
        }

        // What a scalar attenuation does to unpolarised and polarised light alike
        public static MuellerAttenuation Scalar(SampledSpectrum a)
        {
            MuellerAttenuation r = Zero();
            for (int i = 0; i < 4; ++i)
                r[i, i] = a;
            return r;
        }

        // Rotation of the reference frame of the Stokes vector by an angle (radians) about the direction of travel
        public static MuellerAttenuation Rotator(Float angle)
        {
            Float c = std.cos(2 * angle), s = std.sin(2 * angle);
            MuellerAttenuation r = Identity();
            r[1, 1] = new SampledSpectrum(c); r[1, 2] = new SampledSpectrum(s);
            r[2, 1] = new SampledSpectrum(-s); r[2, 2] = new SampledSpectrum(c);
            return r;
        }

        // Ideal linear polariser with the transmission axis at an angle from the reference axis
        public static MuellerAttenuation LinearPolariser(Float angle)
        {
            Float c = std.cos(2 * angle), s = std.sin(2 * angle);
            MuellerAttenuation r = Zero();
            Float[,] v = { { 1, c, s, 0 }, { c, c * c, c * s, 0 }, { s, c * s, s * s, 0 }, { 0, 0, 0, 0 } };
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    r[i, j] = new SampledSpectrum(0.5f * v[i, j]);
            return r;
        }

        // Rayleigh scattering by a scattering angle with cosine cosTheta (normalised so that the unpolarised phase function
        // integrates to one), for light in the scattering plane frame
        public static MuellerAttenuation Rayleigh(Float cosTheta)
        {
            Float c2 = cosTheta * cosTheta;
            Float k = 3 / (16 * Pi);
            MuellerAttenuation r = Zero();
            Float d = 1 + c2, o = c2 - 1;
            Float[,] v = { { d, o, 0, 0 }, { o, d, 0, 0 }, { 0, 0, 2 * cosTheta, 0 }, { 0, 0, 0, 2 * cosTheta } };
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                    r[i, j] = new SampledSpectrum(k * v[i, j]);
            return r;
        }

        public static StokesLight operator *(MuellerAttenuation m, StokesLight s)
        {
            StokesLight r = default;
            for (int i = 0; i < 4; ++i)
                r[i] = m[i, 0] * s.S0 + m[i, 1] * s.S1 + m[i, 2] * s.S2 + m[i, 3] * s.S3;
            return r;
        }

        // (a * b) applies b first
        public static MuellerAttenuation operator *(MuellerAttenuation a, MuellerAttenuation b)
        {
            MuellerAttenuation r = Zero();
            for (int i = 0; i < 4; ++i)
                for (int j = 0; j < 4; ++j)
                {
                    SampledSpectrum sum = new SampledSpectrum(0.0f);
                    for (int k = 0; k < 4; ++k)
                        sum += a[i, k] * b[k, j];
                    r[i, j] = sum;
                }
            return r;
        }

        public static MuellerAttenuation operator *(MuellerAttenuation a, SampledSpectrum s)
        {
            MuellerAttenuation r = Zero();
            for (int i = 0; i < 16; ++i)
                r.Set(i, a.Get(i) * s);
            return r;
        }

        // The scalar attenuation of unpolarised light by this matrix: the first element, which is what a path without
        // polarisation carries
        public SampledSpectrum UnpolarisedGain() { return m0; }
    }
}
