// (not in pbrt-v4) refractiveindex.cs
//
// The refractive index field n(x) of a heterogeneous refractive medium, with the gradient and the Hessian that the curved light
// paths (refractiveray.cs) and the connections between two points need (Pediredla et al., "Path Tracing Estimators for
// Refractive Radiative Transfer", sections 3.1, 4 and 6, appendices A and B).
//
// GridRefractiveIndexField is the field of the paper: a cubic B-spline through the values of a grid, which has continuous
// first and second derivatives and a value, gradient and Hessian in constant time. The spline continues outside the grid as a
// straight extension, so a light path that steps a little past the boundary of the medium meets a field that is still smooth.
// The analytic fields are the references of the tests: the Luneburg lens (the lens of the paper's volumetric caustics) and the
// field with a constant gradient, whose light paths are known in closed form.

using System;

namespace pbrt
{

    // RefractiveIndexField Definition
    public abstract class RefractiveIndexField
    {
        // The index at p and its gradient
        public abstract void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient);

        // The index at p, its gradient and its Hessian
        public abstract void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient,
                                                     out SquareMatrix3 hessian);

        // The matrix with the elements a_i b_j
        public static SquareMatrix3 OuterProduct(Vector3f a, Vector3f b)
        {
            return new SquareMatrix3(a.x * b.x, a.x * b.y, a.x * b.z,
                                     a.y * b.x, a.y * b.y, a.y * b.z,
                                     a.z * b.x, a.z * b.y, a.z * b.z);
        }
    }

    // GridRefractiveIndexField Definition: the values are at the grid points, the first at bounds.pMin and the last at
    // bounds.pMax along each axis, x fastest: values[(z * resolutionY + y) * resolutionX + x]
    public sealed class GridRefractiveIndexField : RefractiveIndexField
    {
        // GridRefractiveIndexField Public Methods
        public GridRefractiveIndexField(Bounds3f bounds, int resolutionX, int resolutionY, int resolutionZ, Float[] values)
        {
            if (resolutionX < 2 || resolutionY < 2 || resolutionZ < 2)
            {
                throw new ArgumentException("GridRefractiveIndexField needs at least two values along each axis.");
            }
            if (values.Length != resolutionX * resolutionY * resolutionZ)
            {
                throw new ArgumentException("GridRefractiveIndexField: the number of values does not match the resolution.");
            }
            this.bounds = bounds;
            this.resolutionX = resolutionX;
            this.resolutionY = resolutionY;
            this.resolutionZ = resolutionZ;
            scaleX = (resolutionX - 1) / (bounds.pMax.x - bounds.pMin.x);
            scaleY = (resolutionY - 1) / (bounds.pMax.y - bounds.pMin.y);
            scaleZ = (resolutionZ - 1) / (bounds.pMax.z - bounds.pMin.z);

            // The coefficients are the values filtered along each axis, so that the spline goes through the values
            coefficients = (Float[])values.Clone();
            int longest = Math.Max(resolutionX, Math.Max(resolutionY, resolutionZ));
            Float[] scratch = new Float[2 * longest];
            for (int z = 0; z < resolutionZ; ++z)
            {
                for (int y = 0; y < resolutionY; ++y)
                {
                    PrefilterLine(coefficients, (z * resolutionY + y) * resolutionX, 1, resolutionX, scratch);
                }
            }
            for (int z = 0; z < resolutionZ; ++z)
            {
                for (int x = 0; x < resolutionX; ++x)
                {
                    PrefilterLine(coefficients, z * resolutionY * resolutionX + x, resolutionX, resolutionY, scratch);
                }
            }
            for (int y = 0; y < resolutionY; ++y)
            {
                for (int x = 0; x < resolutionX; ++x)
                {
                    PrefilterLine(coefficients, y * resolutionX + x, resolutionX * resolutionY, resolutionZ, scratch);
                }
            }
        }

        public override void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient)
        {
            Evaluate(p, false, out value, out gradient, out SquareMatrix3 hessian);
        }

        public override void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient,
                                                     out SquareMatrix3 hessian)
        {
            Evaluate(p, true, out value, out gradient, out hessian);
        }

        // GridRefractiveIndexField Private Methods

        // The coefficients c of the spline satisfy (c[i - 1] + 4 c[i] + c[i + 1]) / 6 = f[i] at the interior points. The end
        // coefficients continue the line through their two neighbours, which makes the first and the last coefficient equal to the
        // value there, and a field that is linear exactly linear. Thomas algorithm, in place.
        static void PrefilterLine(Float[] data, int start, int stride, int count, Float[] scratch)
        {
            if (count < 3)
            {
                return;
            }
            int unknowns = count - 2;
            Float first = data[start];
            Float last = data[start + (count - 1) * stride];
            // scratch[i] holds the modified upper diagonal and scratch[count + i] the modified right hand side
            Float rightHandSide = 6 * data[start + stride] - first;
            if (unknowns == 1)
            {
                rightHandSide -= last;
            }
            scratch[0] = 0.25f;
            scratch[count] = rightHandSide * 0.25f;
            for (int i = 1; i < unknowns; ++i)
            {
                rightHandSide = 6 * data[start + (i + 1) * stride];
                if (i == unknowns - 1)
                {
                    rightHandSide -= last;
                }
                Float denominator = 4 - scratch[i - 1];
                scratch[i] = 1 / denominator;
                scratch[count + i] = (rightHandSide - scratch[count + i - 1]) / denominator;
            }
            Float next = scratch[count + unknowns - 1];
            data[start + unknowns * stride] = next;
            for (int i = unknowns - 2; i >= 0; --i)
            {
                next = scratch[count + i] - scratch[i] * next;
                data[start + (i + 1) * stride] = next;
            }
        }

        // The coefficient at a grid index. Outside the grid the coefficients continue the line through the last two.
        Float Coefficient(int x, int y, int z)
        {
            if (x < 0)
            {
                return (1 - x) * Coefficient(0, y, z) + x * Coefficient(1, y, z);
            }
            if (x >= resolutionX)
            {
                int beyond = x - (resolutionX - 1);
                return (1 + beyond) * Coefficient(resolutionX - 1, y, z) - beyond * Coefficient(resolutionX - 2, y, z);
            }
            if (y < 0)
            {
                return (1 - y) * Coefficient(x, 0, z) + y * Coefficient(x, 1, z);
            }
            if (y >= resolutionY)
            {
                int beyond = y - (resolutionY - 1);
                return (1 + beyond) * Coefficient(x, resolutionY - 1, z) - beyond * Coefficient(x, resolutionY - 2, z);
            }
            if (z < 0)
            {
                return (1 - z) * Coefficient(x, y, 0) + z * Coefficient(x, y, 1);
            }
            if (z >= resolutionZ)
            {
                int beyond = z - (resolutionZ - 1);
                return (1 + beyond) * Coefficient(x, y, resolutionZ - 1) - beyond * Coefficient(x, y, resolutionZ - 2);
            }
            return coefficients[(z * resolutionY + y) * resolutionX + x];
        }

        // The weights of the four coefficients that the cubic B-spline has at the fraction t of a cell: the value at 0..3, the
        // first derivative at 4..7 and the second derivative at 8..11, per unit of the cell
        static void BasisWeights(Float t, Span<Float> weights)
        {
            Float t2 = t * t;
            Float t3 = t2 * t;
            Float oneMinusT = 1 - t;
            weights[0] = oneMinusT * oneMinusT * oneMinusT / 6;
            weights[1] = (3 * t3 - 6 * t2 + 4) / 6;
            weights[2] = (-3 * t3 + 3 * t2 + 3 * t + 1) / 6;
            weights[3] = t3 / 6;
            weights[4] = -oneMinusT * oneMinusT / 2;
            weights[5] = (3 * t2 - 4 * t) / 2;
            weights[6] = (-3 * t2 + 2 * t + 1) / 2;
            weights[7] = t2 / 2;
            weights[8] = oneMinusT;
            weights[9] = 3 * t - 2;
            weights[10] = 1 - 3 * t;
            weights[11] = t;
        }

        void Evaluate(Point3f p, bool needHessian, out Float value, out Vector3f gradient, out SquareMatrix3 hessian)
        {
            Float ux = (p.x - bounds.pMin.x) * scaleX;
            Float uy = (p.y - bounds.pMin.y) * scaleY;
            Float uz = (p.z - bounds.pMin.z) * scaleZ;
            int cellX = (int)Math.Floor(ux);
            int cellY = (int)Math.Floor(uy);
            int cellZ = (int)Math.Floor(uz);
            Span<Float> weightsX = stackalloc Float[12];
            Span<Float> weightsY = stackalloc Float[12];
            Span<Float> weightsZ = stackalloc Float[12];
            BasisWeights(ux - cellX, weightsX);
            BasisWeights(uy - cellY, weightsY);
            BasisWeights(uz - cellZ, weightsZ);

            Float sumValue = 0, sumX = 0, sumY = 0, sumZ = 0;
            Float sumXX = 0, sumYY = 0, sumZZ = 0, sumXY = 0, sumXZ = 0, sumYZ = 0;
            for (int k = 0; k < 4; ++k)
            {
                for (int j = 0; j < 4; ++j)
                {
                    for (int i = 0; i < 4; ++i)
                    {
                        Float c = Coefficient(cellX - 1 + i, cellY - 1 + j, cellZ - 1 + k);
                        Float wx = weightsX[i], wy = weightsY[j], wz = weightsZ[k];
                        Float dx = weightsX[4 + i], dy = weightsY[4 + j], dz = weightsZ[4 + k];
                        sumValue += c * wx * wy * wz;
                        sumX += c * dx * wy * wz;
                        sumY += c * wx * dy * wz;
                        sumZ += c * wx * wy * dz;
                        if (needHessian)
                        {
                            Float ddx = weightsX[8 + i], ddy = weightsY[8 + j], ddz = weightsZ[8 + k];
                            sumXX += c * ddx * wy * wz;
                            sumYY += c * wx * ddy * wz;
                            sumZZ += c * wx * wy * ddz;
                            sumXY += c * dx * dy * wz;
                            sumXZ += c * dx * wy * dz;
                            sumYZ += c * wx * dy * dz;
                        }
                    }
                }
            }
            value = sumValue;
            gradient = new Vector3f(sumX * scaleX, sumY * scaleY, sumZ * scaleZ);
            if (needHessian)
            {
                Float xy = sumXY * scaleX * scaleY;
                Float xz = sumXZ * scaleX * scaleZ;
                Float yz = sumYZ * scaleY * scaleZ;
                hessian = new SquareMatrix3(sumXX * scaleX * scaleX, xy, xz,
                                            xy, sumYY * scaleY * scaleY, yz,
                                            xz, yz, sumZZ * scaleZ * scaleZ);
            }
            else
            {
                hessian = default;
            }
        }

        // GridRefractiveIndexField Private Members
        Bounds3f bounds;
        int resolutionX, resolutionY, resolutionZ;
        Float scaleX, scaleY, scaleZ;
        Float[] coefficients;
    }

    // LuneburgRefractiveIndexField Definition: n(r) = (2 - (r / R)^p)^(1 / p) inside the sphere of radius R and 1 outside (the
    // paper's volumetric caustics of figure 7; p = 2 is the Luneburg lens, which focuses a parallel beam to a point of its
    // surface on the other side)
    public sealed class LuneburgRefractiveIndexField : RefractiveIndexField
    {
        public LuneburgRefractiveIndexField(Point3f center, Float radius, Float exponent)
        {
            this.center = center;
            this.radius = radius;
            this.exponent = exponent;
        }

        public override void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient)
        {
            ValueGradientAndHessian(p, out value, out gradient, out SquareMatrix3 hessian);
        }

        public override void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient,
                                                     out SquareMatrix3 hessian)
        {
            Vector3f offset = p - center;
            Float r = pbrt.Length(offset);
            if (r >= radius)
            {
                value = 1;
                gradient = new Vector3f(0, 0, 0);
                hessian = SquareMatrix3.Zero();
                return;
            }
            // The radial profile n(r) and its first and second derivative. At the center the direction is the zero vector
            Float rClamped = Math.Max(r, 1e-6f * radius);
            Float radiusToExponent = (Float)Math.Pow(radius, exponent);
            Float g = 2 - (Float)Math.Pow(rClamped / radius, exponent);
            value = (Float)Math.Pow(g, 1 / exponent);
            Float q = (Float)Math.Pow(rClamped, exponent - 1) / radiusToExponent;
            Float qDerivative = (exponent - 1) * (Float)Math.Pow(rClamped, exponent - 2) / radiusToExponent;
            Float gPower = value / g;
            Float first = -gPower * q;
            Float second = (1 - exponent) * gPower / g * q * q - gPower * qDerivative;
            Vector3f direction = offset / rClamped;
            gradient = first * direction;
            hessian = (second - first / rClamped) * OuterProduct(direction, direction) + (first / rClamped) * new SquareMatrix3();
        }

        Point3f center;
        Float radius;
        Float exponent;
    }

    // LinearRefractiveIndexField Definition: n(p) = n0 + g . p. A light path in it has the invariant that n sin(theta) is constant
    // along a gradient g that is along z, so that n(x) = C cosh(g x / C + a): a closed form for the tests
    public sealed class LinearRefractiveIndexField : RefractiveIndexField
    {
        public LinearRefractiveIndexField(Float n0, Vector3f slope)
        {
            this.n0 = n0;
            this.slope = slope;
        }

        public override void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient)
        {
            value = n0 + slope.x * p.x + slope.y * p.y + slope.z * p.z;
            gradient = slope;
        }

        public override void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient,
                                                     out SquareMatrix3 hessian)
        {
            ValueAndGradient(p, out value, out gradient);
            hessian = SquareMatrix3.Zero();
        }

        Float n0;
        Vector3f slope;
    }

    // BoundedRefractiveIndexField Definition: the field inside a region and a constant index outside it, the index of the
    // surroundings of a refractive medium. The medium does not model a refraction at its boundary, so the inner field has to
    // be this index on the boundary of the region (a step in n would need Snell's law and the Fresnel equations there).
    public sealed class BoundedRefractiveIndexField : RefractiveIndexField
    {
        public BoundedRefractiveIndexField(RefractiveIndexField inner, Func<Point3f, bool> contains, Float outside)
        {
            this.inner = inner;
            this.contains = contains;
            this.outside = outside;
        }

        public override void ValueAndGradient(Point3f p, out Float value, out Vector3f gradient)
        {
            if (contains(p))
            {
                inner.ValueAndGradient(p, out value, out gradient);
                return;
            }
            value = outside;
            gradient = new Vector3f(0, 0, 0);
        }

        public override void ValueGradientAndHessian(Point3f p, out Float value, out Vector3f gradient,
                                                     out SquareMatrix3 hessian)
        {
            if (contains(p))
            {
                inner.ValueGradientAndHessian(p, out value, out gradient, out hessian);
                return;
            }
            value = outside;
            gradient = new Vector3f(0, 0, 0);
            hessian = SquareMatrix3.Zero();
        }

        RefractiveIndexField inner;
        Func<Point3f, bool> contains;
        Float outside;
    }

}  // namespace pbrt
