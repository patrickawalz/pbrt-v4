// (not in pbrt-v4) Polarisation.hxx and PolarisationFrame.hxx of PolarisedUPBP (Petr Vevoda, Martin Sik, Tomas Davidovic,
// Iliyan Georgiev, Jaroslav Krivanek; modified by Michal Mojzik; MIT license), ported to C#. Intensity is a SampledSpectrum.

using System;

namespace pbrt
{
    // Coordinate system tracking for polarisation: the frame that a Stokes vector or a Mueller matrix is expressed in. The z
    // axis is the direction of propagation. Unlike Frame it can be left unset, and the directions that SetFromZX() gets are
    // expected to be unit length.
    public struct PolarisationFrame
    {
        public const Float EpsilonRay = 1e-3f;
        public const Float EpsilonCollinear = 2.4e-3f;

        public Vector3f x, y, z;

        public PolarisationFrame(Vector3f x, Vector3f y, Vector3f z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public void SetFromZ(Vector3f propagationDirection)
        {
            SetFromZX(propagationDirection, new Vector3f(0, 0, 1));
        }

        public void SetFromZX(Vector3f propagationDirection, Vector3f reference)
        {
            z = propagationDirection;
            Vector3f temporaryX = reference;
            if (std.abs(std.abs(pbrt.Dot(propagationDirection, temporaryX)) - 1.0f) < EpsilonRay)
            {
                temporaryX = std.abs(propagationDirection.x) > 0.99f ? new Vector3f(0, 1, 0) : new Vector3f(1, 0, 0);
            }

            y = pbrt.Normalize(pbrt.Cross(propagationDirection, temporaryX));
            x = pbrt.Cross(y, propagationDirection);
        }

        public Vector3f ToWorld(Vector3f a)
        {
            return x * a.x + y * a.y + z * a.z;
        }

        public Vector3f ToLocal(Vector3f a)
        {
            return new Vector3f(pbrt.Dot(a, x), pbrt.Dot(a, y), pbrt.Dot(a, z));
        }

        public Vector3f Binormal()
        {
            return x;
        }

        public Vector3f Tangent()
        {
            return y;
        }

        public Vector3f Normal()
        {
            return z;
        }

        // Whether the propagation directions of the two frames are collinear
        public static bool Collinear(PolarisationFrame a, PolarisationFrame b)
        {
            return std.abs(pbrt.Dot(a.z, b.z) - 1.0f) < EpsilonCollinear;
        }
    }

    public struct StokesVector
    {
        public SampledSpectrum s0, s1, s2, s3;

        public StokesVector(SampledSpectrum value)
        {
            s0 = value;
            s1 = new SampledSpectrum(0.0f);
            s2 = new SampledSpectrum(0.0f);
            s3 = new SampledSpectrum(0.0f);
        }

        public StokesVector(SampledSpectrum s0, SampledSpectrum s1, SampledSpectrum s2, SampledSpectrum s3)
        {
            this.s0 = s0;
            this.s1 = s1;
            this.s2 = s2;
            this.s3 = s3;
        }

        public bool IsPolarised()
        {
            Float maximum = std.max(s1.MaxComponentValue(), std.max(s2.MaxComponentValue(), s3.MaxComponentValue()));
            Float minimum = std.min(s1.MinComponentValue(), std.min(s2.MinComponentValue(), s3.MinComponentValue()));
            return maximum >= 1e-12f || minimum <= -1e-12f;
        }

        public SampledSpectrum this[int i]
        {
            get
            {
                switch (i)
                {
                case 0: return s0;
                case 1: return s1;
                case 2: return s2;
                default: return s3;
                }
            }
            set
            {
                switch (i)
                {
                case 0: s0 = value; break;
                case 1: s1 = value; break;
                case 2: s2 = value; break;
                default: s3 = value; break;
                }
            }
        }

        public void operator *=(SampledSpectrum b)
        {
            for (int i = 0; i < 4; ++i)
            {
                this[i] = this[i] * b;
            }
        }

        public void operator /=(SampledSpectrum b)
        {
            for (int i = 0; i < 4; ++i)
            {
                this[i] = this[i] / b;
            }
        }

        public void operator +=(StokesVector b)
        {
            for (int i = 0; i < 4; ++i)
            {
                this[i] = this[i] + b[i];
            }
        }

        public void operator -=(StokesVector b)
        {
            for (int i = 0; i < 4; ++i)
            {
                this[i] = this[i] - b[i];
            }
        }

        public static StokesVector operator *(StokesVector a, SampledSpectrum b)
        {
            a *= b;
            return a;
        }

        public static StokesVector operator /(StokesVector a, SampledSpectrum b)
        {
            a /= b;
            return a;
        }

        public static StokesVector operator +(StokesVector a, StokesVector b)
        {
            a += b;
            return a;
        }

        public static StokesVector operator -(StokesVector a, StokesVector b)
        {
            a -= b;
            return a;
        }

        public void RotateFrame(PolarisationFrame newFrame, PolarisationFrame currentFrame)
        {
            GetRotationCosSin(newFrame, currentFrame, out Float cos2, out Float sin2);
            SampledSpectrum oldS1 = s1;
            s1 = s1 * cos2 + s2 * sin2;
            s2 = oldS1 * -sin2 + s2 * cos2;
        }

        public static void GetRotationCosSin(PolarisationFrame newFrame, PolarisationFrame currentFrame, out Float cos2,
                                             out Float sin2)
        {
            DCHECK(PolarisationFrame.Collinear(newFrame, currentFrame));
            Vector3f x = newFrame.Binormal();
            Float cos = pbrt.Dot(x, currentFrame.Binormal());
            Float sin = pbrt.Dot(x, currentFrame.Tangent());
            cos2 = cos * cos - sin * sin;
            sin2 = 2 * sin * cos;
        }
    }

    public struct MuellerMatrix
    {
        public SampledSpectrum m00, m01, m02, m03;
        public SampledSpectrum m10, m11, m12, m13;
        public SampledSpectrum m20, m21, m22, m23;
        public SampledSpectrum m30, m31, m32, m33;

        public MuellerMatrix(SampledSpectrum m00, SampledSpectrum m01, SampledSpectrum m02, SampledSpectrum m03,
                             SampledSpectrum m10, SampledSpectrum m11, SampledSpectrum m12, SampledSpectrum m13,
                             SampledSpectrum m20, SampledSpectrum m21, SampledSpectrum m22, SampledSpectrum m23,
                             SampledSpectrum m30, SampledSpectrum m31, SampledSpectrum m32, SampledSpectrum m33)
        {
            this.m00 = m00; this.m01 = m01; this.m02 = m02; this.m03 = m03;
            this.m10 = m10; this.m11 = m11; this.m12 = m12; this.m13 = m13;
            this.m20 = m20; this.m21 = m21; this.m22 = m22; this.m23 = m23;
            this.m30 = m30; this.m31 = m31; this.m32 = m32; this.m33 = m33;
        }

        // The diagonal matrix of a value: what a scalar does to every component
        public static MuellerMatrix Diagonal(SampledSpectrum value)
        {
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            return new MuellerMatrix(value, zero, zero, zero,
                                     zero, value, zero, zero,
                                     zero, zero, value, zero,
                                     zero, zero, zero, value);
        }

        public MuellerMatrix(SampledSpectrum m00)
        {
            this = default;
            this.m00 = m00;
        }

        public SampledSpectrum Get(int row, int column)
        {
            switch (row * 4 + column)
            {
            case 0: return m00;
            case 1: return m01;
            case 2: return m02;
            case 3: return m03;
            case 4: return m10;
            case 5: return m11;
            case 6: return m12;
            case 7: return m13;
            case 8: return m20;
            case 9: return m21;
            case 10: return m22;
            case 11: return m23;
            case 12: return m30;
            case 13: return m31;
            case 14: return m32;
            default: return m33;
            }
        }

        public void Set(int row, int column, SampledSpectrum value)
        {
            switch (row * 4 + column)
            {
            case 0: m00 = value; break;
            case 1: m01 = value; break;
            case 2: m02 = value; break;
            case 3: m03 = value; break;
            case 4: m10 = value; break;
            case 5: m11 = value; break;
            case 6: m12 = value; break;
            case 7: m13 = value; break;
            case 8: m20 = value; break;
            case 9: m21 = value; break;
            case 10: m22 = value; break;
            case 11: m23 = value; break;
            case 12: m30 = value; break;
            case 13: m31 = value; break;
            case 14: m32 = value; break;
            default: m33 = value; break;
            }
        }

        public void operator *=(SampledSpectrum b)
        {
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    Set(row, column, Get(row, column) * b);
                }
            }
        }

        public void operator /=(SampledSpectrum b)
        {
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    Set(row, column, Get(row, column) / b);
                }
            }
        }

        public void operator +=(MuellerMatrix b)
        {
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    Set(row, column, Get(row, column) + b.Get(row, column));
                }
            }
        }

        public void operator -=(MuellerMatrix b)
        {
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    Set(row, column, Get(row, column) - b.Get(row, column));
                }
            }
        }

        public void operator *=(MuellerMatrix b)
        {
            this = this * b;
        }

        public static MuellerMatrix operator *(MuellerMatrix a, SampledSpectrum b)
        {
            a *= b;
            return a;
        }

        public static MuellerMatrix operator +(MuellerMatrix a, MuellerMatrix b)
        {
            a += b;
            return a;
        }

        public static StokesVector operator *(MuellerMatrix a, StokesVector b)
        {
            StokesVector result = default;
            for (int row = 0; row < 4; ++row)
            {
                SampledSpectrum sum = a.Get(row, 0) * b[0];
                for (int column = 1; column < 4; ++column)
                {
                    sum += a.Get(row, column) * b[column];
                }

                result[row] = sum;
            }

            return result;
        }

        public static MuellerMatrix operator *(MuellerMatrix a, MuellerMatrix b)
        {
            MuellerMatrix result = default;
            for (int row = 0; row < 4; ++row)
            {
                for (int column = 0; column < 4; ++column)
                {
                    SampledSpectrum sum = a.Get(row, 0) * b.Get(0, column);
                    for (int k = 1; k < 4; ++k)
                    {
                        sum += a.Get(row, k) * b.Get(k, column);
                    }

                    result.Set(row, column, sum);
                }
            }

            return result;
        }

        // The matrix that expresses a Stokes vector of enterFrame in exitFrame
        public static MuellerMatrix RotateFrame(PolarisationFrame exitFrame, PolarisationFrame enterFrame)
        {
            StokesVector.GetRotationCosSin(exitFrame, enterFrame, out Float cos2, out Float sin2);
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum one = new SampledSpectrum(1.0f);
            SampledSpectrum c = new SampledSpectrum(cos2);
            SampledSpectrum s = new SampledSpectrum(sin2);
            return new MuellerMatrix(one, zero, zero, zero,
                                     zero, c, s, zero,
                                     zero, -s, c, zero,
                                     zero, zero, zero, one);
        }
    }
}
