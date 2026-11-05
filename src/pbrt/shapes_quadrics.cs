// (not in pbrt-v4) shapes_quadrics.cs
//
// The RenderMan standard quadrics, ray traced directly (no dicing): the sphere, disk and cylinder of shapes.cs plus the cone, paraboloid,
// hyperboloid and torus defined here. pbrt-v3 had the cone, paraboloid and hyperboloid; the torus is new. All of them follow the
// conventions of the pbrt-v4 sphere: an object-space ray with interval error bounds, a conservative t range, a refined hit point and
// pError, and (u, v) as in RenderMan (u = phi / phimax, v along the profile).
//
// Sampling is by area in object space (uniform for the shapes whose area element is constant in the parameterization, otherwise through
// the inverse CDF of the profile), converted to solid angle with the exact Jacobian of the transform when it scales (a non-uniform scale
// makes the density of an object-space uniform sample vary over the render-space surface).

using System;

namespace pbrt
{
    // The area scale of the linear part of renderFromObject
    public unsafe struct QuadricScale
    {
        public bool isUniform;  // the linear part is a similarity (a rotation, a translation and a uniform scale)
        public Float scale;     // its scale factor
        public Float jacobian;  // scale^2: the factor by which a similarity multiplies areas
        Float det;
        Float i00, i01, i02, i10, i11, i12, i20, i21, i22;

        public QuadricScale(Transform t)
        {
            SquareMatrix4 m = t.GetMatrix();
            SquareMatrix4 mi = t.GetInverseMatrix();
            double g00 = (double)m[0][0] * m[0][0] + (double)m[1][0] * m[1][0] + (double)m[2][0] * m[2][0];
            double g11 = (double)m[0][1] * m[0][1] + (double)m[1][1] * m[1][1] + (double)m[2][1] * m[2][1];
            double g22 = (double)m[0][2] * m[0][2] + (double)m[1][2] * m[1][2] + (double)m[2][2] * m[2][2];
            double g01 = (double)m[0][0] * m[0][1] + (double)m[1][0] * m[1][1] + (double)m[2][0] * m[2][1];
            double g02 = (double)m[0][0] * m[0][2] + (double)m[1][0] * m[1][2] + (double)m[2][0] * m[2][2];
            double g12 = (double)m[0][1] * m[0][2] + (double)m[1][1] * m[1][2] + (double)m[2][1] * m[2][2];
            const double tolerance = 1e-4;
            isUniform = g00 > 0 && Math.Abs(g11 - g00) <= tolerance * g00 && Math.Abs(g22 - g00) <= tolerance * g00 &&
                        Math.Abs(g01) <= tolerance * g00 && Math.Abs(g02) <= tolerance * g00 && Math.Abs(g12) <= tolerance * g00;
            scale = (Float)Math.Sqrt(g00);
            jacobian = (Float)g00;
            double determinant = (double)m[0][0] * ((double)m[1][1] * m[2][2] - (double)m[1][2] * m[2][1]) -
                                 (double)m[0][1] * ((double)m[1][0] * m[2][2] - (double)m[1][2] * m[2][0]) +
                                 (double)m[0][2] * ((double)m[1][0] * m[2][1] - (double)m[1][1] * m[2][0]);
            det = (Float)Math.Abs(determinant);
            i00 = mi[0][0];
            i01 = mi[0][1];
            i02 = mi[0][2];
            i10 = mi[1][0];
            i11 = mi[1][1];
            i12 = mi[1][2];
            i20 = mi[2][0];
            i21 = mi[2][1];
            i22 = mi[2][2];
        }

        // The factor by which the transform multiplies the area of a surface element with the object-space normal n
        public Float Jacobian(Vector3f n)
        {
            if (isUniform)
            {
                return jacobian;
            }
            Float length = std.sqrt(n.x * n.x + n.y * n.y + n.z * n.z);
            if (length == 0)
            {
                return det;
            }
            Float x = n.x / length, y = n.y / length, z = n.z / length;
            Float wx = i00 * x + i10 * y + i20 * z;
            Float wy = i01 * x + i11 * y + i21 * z;
            Float wz = i02 * x + i12 * y + i22 * z;
            return det * std.sqrt(wx * wx + wy * wy + wz * wz);
        }
    }

    public static unsafe class QuadricUtil
    {
        // The angle of (x, y) in [0, 2 pi)
        public static Float PhiOf(Float x, Float y)
        {
            Float phi = std.atan2(y, x);
            if (phi < 0)
            {
                phi += 2 * Pi;
            }
            return phi;
        }

        // The sweep angle in radians for a parameter in degrees: 360 and above is exactly 2 pi, so that no sliver of a full surface is lost
        public static Float SweepRadians(Float degrees)
        {
            degrees = Clamp(degrees, 0, 360);
            return degrees >= 360 ? 2 * Pi : Radians(degrees);
        }

        // The extent in x and y of the annular sector rMin <= r <= rMax, 0 <= phi <= phiMax
        public static void SectorExtent(Float rMin, Float rMax, Float phiMax, out Float xMin, out Float xMax, out Float yMin,
                                        out Float yMax)
        {
            if (phiMax >= 2 * Pi)
            {
                xMin = yMin = -rMax;
                xMax = yMax = rMax;
                return;
            }
            xMin = yMin = Float.MaxValue;
            xMax = yMax = -Float.MaxValue;
            for (int k = 0; k < 5; ++k)
            {
                Float angle = k == 0 ? (Float)0 : (k == 1 ? phiMax : (k - 1) * (Pi / 2));
                if (angle > phiMax)
                {
                    continue;
                }
                Float c = std.cos(angle), s = std.sin(angle);
                for (int j = 0; j < 2; ++j)
                {
                    Float r = j == 0 ? rMin : rMax;
                    xMin = std.min(xMin, r * c);
                    xMax = std.max(xMax, r * c);
                    yMin = std.min(yMin, r * s);
                    yMax = std.max(yMax, r * s);
                }
            }
            // Round-off in the trigonometry must not leave a hit outside the box
            Float pad = 4e-6f * rMax;
            xMin -= pad;
            xMax += pad;
            yMin -= pad;
            yMax += pad;
        }

        // Converts a point sampled by area into a sample by solid angle from the reference point of ctx
        public static pstd.optional<ShapeSample> ToSolidAngle(ShapeSampleContext ctx, pstd.optional<ShapeSample> ss)
        {
            if (!ss)
            {
                return default;
            }
            ss.Ref().intr.time = ctx.time;
            Vector3f wi = ss.value().intr.p() - ctx.p();
            if (LengthSquared(wi) == 0)
            {
                return default;
            }
            wi = Normalize(wi);

            // Convert area sampling PDF in ss to solid angle measure
            ss.Ref().pdf /= AbsDot(ss.value().intr.n, -wi) / DistanceSquared(ctx.p(), ss.value().intr.p());
            if (IsInf(ss.value().pdf) || IsNaN(ss.value().pdf))
            {
                return default;
            }
            return ss;
        }

        // The Weingarten equations: dn/du and dn/dv from the first and second derivatives of the parameterization
        public static void NormalDerivatives(Vector3f dpdu, Vector3f dpdv, Vector3f d2Pduu, Vector3f d2Pduv, Vector3f d2Pdvv,
                                             out Normal3f dndu, out Normal3f dndv)
        {
            Float E = Dot(dpdu, dpdu), F = Dot(dpdu, dpdv), G = Dot(dpdv, dpdv);
            Vector3f n = Normalize(Cross(dpdu, dpdv));
            Float e = Dot(n, d2Pduu), f = Dot(n, d2Pduv), g = Dot(n, d2Pdvv);
            Float EGF2 = DifferenceOfProducts(E, G, F, F);
            Float invEGF2 = (EGF2 == 0) ? (Float)0 : 1 / EGF2;
            dndu = (Normal3f)((f * F - e * G) * invEGF2 * dpdu + (e * F - f * E) * invEGF2 * dpdv);
            dndv = (Normal3f)((g * F - f * G) * invEGF2 * dpdu + (f * F - g * E) * invEGF2 * dpdv);
        }

        // The extent of cos and sin over the arc phiStart .. phiStart + sweep (radians, sweep <= 2 pi)
        public static void ArcExtremes(double phiStart, double sweep, out double cosMin, out double cosMax, out double sinMin,
                                       out double sinMax)
        {
            double a = phiStart, b = phiStart + sweep;
            cosMin = Math.Min(Math.Cos(a), Math.Cos(b));
            cosMax = Math.Max(Math.Cos(a), Math.Cos(b));
            sinMin = Math.Min(Math.Sin(a), Math.Sin(b));
            sinMax = Math.Max(Math.Sin(a), Math.Sin(b));
            if (ArcContains(phiStart, sweep, 0))
            {
                cosMax = 1;
            }
            if (ArcContains(phiStart, sweep, Math.PI))
            {
                cosMin = -1;
            }
            if (ArcContains(phiStart, sweep, Math.PI / 2))
            {
                sinMax = 1;
            }
            if (ArcContains(phiStart, sweep, 3 * Math.PI / 2))
            {
                sinMin = -1;
            }
        }

        public static bool ArcContains(double phiStart, double sweep, double angle)
        {
            double twoPi = 2 * Math.PI;
            double relative = angle - phiStart;
            relative -= twoPi * Math.Floor(relative / twoPi);
            return relative <= sweep;
        }
    }

    // The quartic solver: all real roots of c4 x^4 + c3 x^3 + c2 x^2 + c1 x + c0 = 0 in ascending order. The real axis is split at the
    // critical points (the roots of the cubic derivative) into intervals on which the quartic is monotone; every interval whose end
    // values differ in sign holds exactly one root, which a safeguarded Newton iteration brackets and converges to. A critical point at
    // which the quartic vanishes is a root of even multiplicity and is reported twice.
    public static unsafe class RobustQuartic
    {
        public static int Solve(double c4, double c3, double c2, double c1, double c0, double* roots)
        {
            double scale = Math.Max(Math.Max(Math.Abs(c4), Math.Abs(c3)), Math.Max(Math.Max(Math.Abs(c2), Math.Abs(c1)), Math.Abs(c0)));
            if (!(scale > 0) || double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return 0;
            }
            if (Math.Abs(c4) < 1e-12 * scale)
            {
                return RobustCubic.Solve(c3, c2, c1, c0, roots);
            }
            double a3 = c3 / c4, a2 = c2 / c4, a1 = c1 / c4, a0 = c0 / c4;

            // The critical points: the real roots of the derivative 4 x^3 + 3 a3 x^2 + 2 a2 x + a1
            double* critical = stackalloc double[3];
            int criticalCount = CriticalPoints(a3, a2, a1, critical);

            // Every real root lies within the Cauchy bound
            double bound = 1 + Math.Max(Math.Max(Math.Abs(a3), Math.Abs(a2)), Math.Max(Math.Abs(a1), Math.Abs(a0)));
            double* knots = stackalloc double[5];
            double* values = stackalloc double[5];
            bool* vanishes = stackalloc bool[5];
            int knotCount = 0;
            knots[knotCount++] = -bound;
            for (int i = 0; i < criticalCount; ++i)
            {
                knots[knotCount++] = critical[i];
            }
            knots[knotCount++] = bound;
            for (int i = 0; i < knotCount; ++i)
            {
                double x = knots[i];
                values[i] = Evaluate(a3, a2, a1, a0, x);
                // A critical point at which the quartic is zero to within its rounding error is a multiple root
                double magnitude = Math.Abs(x * x * x * x) + Math.Abs(a3 * x * x * x) + Math.Abs(a2 * x * x) + Math.Abs(a1 * x) + Math.Abs(a0);
                vanishes[i] = i > 0 && i < knotCount - 1 && Math.Abs(values[i]) <= 1e-13 * magnitude;
            }

            int count = 0;
            for (int i = 0; i < knotCount; ++i)
            {
                if (vanishes[i])
                {
                    roots[count++] = knots[i];
                    roots[count++] = knots[i];
                    continue;
                }
                if (i + 1 < knotCount && !vanishes[i + 1] && (values[i] < 0) != (values[i + 1] < 0) && values[i] != 0 && values[i + 1] != 0)
                {
                    roots[count++] = FindRoot(a3, a2, a1, a0, knots[i], knots[i + 1], values[i]);
                }
                else if (values[i] == 0 && i > 0 && i < knotCount - 1)
                {
                    roots[count++] = knots[i];
                }
            }
            return count;
        }

        // The real roots, ascending, of x^3 + b x^2 + c x + d (the derivative of the monic quartic divided by 4, with b = 3 a3 / 4,
        // c = a2 / 2, d = a1 / 4) by Cardano's formula for the depressed cubic, polished with Newton steps on the derivative
        private static int CriticalPoints(double a3, double a2, double a1, double* roots)
        {
            double b = 0.75 * a3, c = 0.5 * a2, d = 0.25 * a1;
            double p = c - b * b / 3, q = 2 * b * b * b / 27 - b * c / 3 + d;
            double shift = -b / 3;
            int count;
            double discriminant = 0.25 * q * q + p * p * p / 27;
            if (discriminant > 0)
            {
                // One real root; use the larger cube root so that the two terms do not cancel
                double sqrt = Math.Sqrt(discriminant);
                double u = Math.Cbrt(-0.5 * q - (q >= 0 ? sqrt : -sqrt));
                double y = u != 0 ? u - p / (3 * u) : 0;
                roots[0] = y + shift;
                count = 1;
            }
            else
            {
                // Three real roots (two or three coincide when the discriminant vanishes)
                if (p >= 0)
                {
                    roots[0] = shift;
                    return 1;
                }
                double m = 2 * Math.Sqrt(-p / 3);
                double argument = Math.Max(-1, Math.Min(1, 3 * q / (p * m)));
                double angle = Math.Acos(argument) / 3;
                roots[0] = m * Math.Cos(angle - 2 * Math.PI / 3) + shift;
                roots[1] = m * Math.Cos(angle) + shift;
                roots[2] = m * Math.Cos(angle + 2 * Math.PI / 3) + shift;
                count = 3;
            }
            for (int i = 0; i < count; ++i)
            {
                // Newton steps on the cubic, kept only while they reduce its value
                double x = roots[i];
                double value = ((x + b) * x + c) * x + d;
                for (int iteration = 0; iteration < 3 && value != 0; ++iteration)
                {
                    double slope = (3 * x + 2 * b) * x + c;
                    if (slope == 0)
                    {
                        break;
                    }
                    double next = x - value / slope;
                    double nextValue = ((next + b) * next + c) * next + d;
                    if (!(Math.Abs(nextValue) < Math.Abs(value)))
                    {
                        break;
                    }
                    x = next;
                    value = nextValue;
                }
                roots[i] = x;
            }
            // Ascending order
            for (int i = 1; i < count; ++i)
            {
                double key = roots[i];
                int j = i - 1;
                while (j >= 0 && roots[j] > key)
                {
                    roots[j + 1] = roots[j];
                    --j;
                }
                roots[j + 1] = key;
            }
            return count;
        }

        private static double Evaluate(double a3, double a2, double a1, double a0, double x)
        {
            return (((x + a3) * x + a2) * x + a1) * x + a0;
        }

        private static double Derivative(double a3, double a2, double a1, double x)
        {
            return ((4 * x + 3 * a3) * x + 2 * a2) * x + a1;
        }

        // The root of the monic quartic in (lo, hi), where it is monotone and has the value flo at lo
        private static double FindRoot(double a3, double a2, double a1, double a0, double lo, double hi, double flo)
        {
            bool lowIsNegative = flo < 0;
            double x = 0.5 * (lo + hi);
            for (int iteration = 0; iteration < 200; ++iteration)
            {
                double f = Evaluate(a3, a2, a1, a0, x);
                if (f == 0)
                {
                    return x;
                }
                if ((f < 0) == lowIsNegative)
                {
                    lo = x;
                }
                else
                {
                    hi = x;
                }
                double derivative = Derivative(a3, a2, a1, x);
                double next = derivative != 0 ? x - f / derivative : 0.5 * (lo + hi);
                if (!(next > lo && next < hi))
                {
                    next = 0.5 * (lo + hi);
                }
                if (Math.Abs(next - x) <= 4e-16 * Math.Max(1.0, Math.Abs(x)))
                {
                    return next;
                }
                x = next;
            }
            return x;
        }
    }

    // Shared machinery of the quadrics: the transform, sampling by area with the Jacobian of the transform, and the conversion to solid
    // angle. A subclass supplies the ray intersection, the interaction at a hit and the sampling in object space.
    public abstract unsafe class QuadricBase : Shape
    {
        protected Transform renderFromObject, objectFromRender;
        protected bool reverseOrientation, transformSwapsHandedness;
        protected QuadricScale xform;
        Float objectAreaCache = -1, renderAreaCache = -1;

        protected QuadricBase(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation)
        {
            this.renderFromObject = renderFromObject;
            this.objectFromRender = objectFromRender;
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            this.xform = new QuadricScale(renderFromObject);
        }

        // The area of the surface in object space
        protected abstract Float ComputeObjectArea();

        // A point sampled uniformly by object-space area (u -> point is a measure-preserving map), the object-space normal there (in
        // the direction of cross(dpdu, dpdv)) and its (u, v)
        protected abstract void SampleObject(Point2f u, out Point3f pObj, out Vector3f nObj, out Point2f uv);

        // The object-space normal at a point of the surface, in the direction of cross(dpdu, dpdv)
        protected abstract Vector3f ObjectNormal(Point3f pObj);

        protected abstract Bounds3f ObjectBounds();

        public abstract pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax);

        // (not in pbrt-v4) The point and the unit normal (along cross(dpdu, dpdv)) at the parameters (u, v) in [0, 1]^2, in object space, for the
        // diced displacement (dicing.cs)
        protected abstract void EvaluateDiced(double u, double v, out SdfVector p, out SdfVector n);

        public IDicedSurface CreateDicedSurface()
        {
            return new AnalyticDicedSurface(EvaluateDiced);
        }

        public abstract SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time);

        protected virtual Vector3f ObjectPError(Point3f pObj)
        {
            return gamma(5) * Abs((Vector3f)pObj);
        }

        protected Float ObjectArea()
        {
            if (objectAreaCache < 0)
            {
                objectAreaCache = ComputeObjectArea();
            }
            return objectAreaCache;
        }

        // The area in render space: the object-space area times the area scale of the transform (averaged over the surface when it
        // is not a similarity)
        public override Float Area()
        {
            if (xform.isUniform)
            {
                return ObjectArea() * xform.jacobian;
            }
            if (renderAreaCache < 0)
            {
                const int n = 64;
                double sum = 0;
                for (int i = 0; i < n; ++i)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        Point3f pObj;
                        Vector3f nObj;
                        Point2f uv;
                        SampleObject(new Point2f((i + 0.5f) / n, (j + 0.5f) / n), out pObj, out nObj, out uv);
                        sum += xform.Jacobian(nObj);
                    }
                }
                renderAreaCache = (Float)(ObjectArea() * sum / (n * n));
            }
            return renderAreaCache;
        }

        public override Bounds3f Bounds()
        {
            return renderFromObject.Apply(ObjectBounds());
        }

        public override DirectionCone NormalBounds()
        {
            return DirectionCone.EntireSphere();
        }

        // The cone of render-space normals around the object-space direction axis, within the angle with cosine cosTheta of it
        protected DirectionCone TransformNormalCone(Vector3f axisObj, Float cosTheta)
        {
            if (!xform.isUniform)
            {
                return DirectionCone.EntireSphere();
            }
            Normal3f n = Normalize(renderFromObject.Apply(new Normal3f(axisObj.x, axisObj.y, axisObj.z)));
            if (reverseOrientation ^ transformSwapsHandedness)
            {
                n = -n;
            }
            return new DirectionCone((Vector3f)n, cosTheta);
        }

        public sealed override pstd.optional<ShapeIntersection> Intersect(Ray ray, Float tMax = Infinity)
        {
            pstd.optional<QuadricIntersection> isect = BasicIntersect(ray, tMax);
            if (!isect)
            {
                return default;
            }
            SurfaceInteraction intr = InteractionFromIntersection(isect.value(), -ray.d, ray.time);
            return new ShapeIntersection(intr, isect.value().tHit);
        }

        public sealed override bool IntersectP(Ray r, Float tMax = Infinity)
        {
            return BasicIntersect(r, tMax);
        }

        // Wraps the object-space differential geometry of a hit into a render-space SurfaceInteraction
        protected SurfaceInteraction MakeInteraction(Point3f pHit, Vector3f pError, Point2f uv, Vector3f wo, Vector3f dpdu,
                                                     Vector3f dpdv, Normal3f dndu, Normal3f dndv, Float time)
        {
            bool flipNormal = reverseOrientation ^ transformSwapsHandedness;
            Vector3f woObject = objectFromRender.Apply(wo);
            return renderFromObject.Apply(new SurfaceInteraction(new Point3fi(pHit, pError), uv, woObject, dpdu, dpdv, dndu, dndv,
                                                                 time, flipNormal));
        }

        public override pstd.optional<ShapeSample> Sample(Point2f u)
        {
            Point3f pObj;
            Vector3f nObj;
            Point2f uv;
            SampleObject(u, out pObj, out nObj, out uv);
            Point3fi pi = renderFromObject.Apply(new Point3fi(pObj, ObjectPError(pObj)));
            Normal3f n = Normalize(renderFromObject.Apply(new Normal3f(nObj.x, nObj.y, nObj.z)));
            if (reverseOrientation ^ transformSwapsHandedness)
            {
                n = -n;
            }
            Float pdf = 1 / (ObjectArea() * xform.Jacobian(nObj));
            return new ShapeSample(new Interaction(pi, n, uv), pdf);
        }

        // The density per unit render-space area of Sample(u) at a point of the surface
        public override Float PDF(Interaction intr)
        {
            Point3f pObj = objectFromRender.Apply(intr.p());
            Float jacobian = xform.isUniform ? xform.jacobian : xform.Jacobian(ObjectNormal(pObj));
            return 1 / (ObjectArea() * jacobian);
        }

        public override pstd.optional<ShapeSample> Sample(ShapeSampleContext ctx, Point2f u)
        {
            return QuadricUtil.ToSolidAngle(ctx, Sample(u));
        }

        public override Float PDF(ShapeSampleContext ctx, Vector3f wi)
        {
            // Intersect sample ray with shape geometry
            Ray ray = ctx.SpawnRay(wi);
            pstd.optional<ShapeIntersection> isect = Intersect(ray);
            if (!isect)
            {
                return 0;
            }

            // Compute PDF in solid angle measure from shape intersection point
            Float pdf = PDF(isect.value().intr) /
                        (AbsDot(isect.value().intr.n, -wi) / DistanceSquared(ctx.p(), isect.value().intr.p()));
            if (IsInf(pdf) || IsNaN(pdf))
            {
                pdf = 0;
            }
            return pdf;
        }
    }

    // A surface of revolution about the z axis whose squared radius is a quadratic in z, rho^2 = A z^2 + B z + C, swept through
    // phi in [0, phimax] from a generator curve: the cone and hyperboloid (a straight generator) and the paraboloid. The generator at
    // height z is the point q(z) (it has an azimuth of its own for a skew hyperboloid); the surface is P(phi, z) = Rz(phi) q(z).
    // (u, v) = (phi / phimax, (z - zStart) / (zEnd - zStart)).
    public abstract unsafe class RevolvedQuadric : QuadricBase
    {
        protected Float zMin, zMax;       // the extent in z, zMin < zMax
        protected Float zStart, zEnd;     // v = 0 and v = 1
        protected Float phiMax;
        protected Float qA, qB, qC;       // rho^2 = qA z^2 + qB z + qC

        protected RevolvedQuadric(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float zStart,
                                  Float zEnd, Float phiMax)
            : base(renderFromObject, objectFromRender, reverseOrientation)
        {
            this.zStart = zStart;
            this.zEnd = zEnd;
            this.zMin = std.min(zStart, zEnd);
            this.zMax = std.max(zStart, zEnd);
            this.phiMax = phiMax;
        }

        protected override void EvaluateDiced(double u, double v, out SdfVector p, out SdfVector n)
        {
            Float z = (Float)(zStart + v * ((double)zEnd - zStart));
            Vector3f q, dq, ddq;
            Profile(z, out q, out dq, out ddq);
            double phi = u * phiMax, c = Math.Cos(phi), s = Math.Sin(phi);
            p = new SdfVector(c * q.x - s * q.y, s * q.x + c * q.y, z);
            Vector3f normal = NormalFromRotation(q, dq, (Float)c, (Float)s);
            n = new SdfVector(normal.x, normal.y, normal.z).Normalized();
        }

        // The generator point q(z) and its first and second derivatives with respect to z
        protected abstract void Profile(Float z, out Vector3f q, out Vector3f dq, out Vector3f ddq);

        // z with the density proportional to rho sqrt(1 + rho'^2) on [zMin, zMax], for u in [0, 1)
        protected abstract Float SampleZ(Float u);

        // The integral of rho sqrt(1 + rho'^2) over [zMin, zMax]
        protected abstract double ProfileArea();

        protected override Float ComputeObjectArea()
        {
            return (Float)(phiMax * ProfileArea());
        }

        // The (unnormalized) normal cross(dpdu, dpdv) where the generator is rotated by (c, s) = (cos phi, sin phi)
        protected Vector3f NormalFromRotation(Vector3f q, Vector3f dq, Float c, Float s)
        {
            Float px = c * q.x - s * q.y, py = s * q.x + c * q.y;
            Float pr = std.sqrt(px * px + py * py);
            Float tx, ty;
            if (pr > 0)
            {
                tx = -py / pr;
                ty = px / pr;
            }
            else
            {
                tx = -s;
                ty = c;
            }
            Float sign = zEnd > zStart ? 1 : -1;
            Float a = sign * (c * dq.x - s * dq.y), b = sign * (s * dq.x + c * dq.y), e = sign * dq.z;
            return new Vector3f(ty * e, -tx * e, tx * b - ty * a);
        }

        protected override Vector3f ObjectNormal(Point3f pObj)
        {
            Vector3f q, dq, ddq;
            Profile(pObj.z, out q, out dq, out ddq);
            Float qr = std.sqrt(q.x * q.x + q.y * q.y), pr = std.sqrt(pObj.x * pObj.x + pObj.y * pObj.y);
            Float c = 1, s = 0;
            if (qr > 0 && pr > 0)
            {
                c = (q.x * pObj.x + q.y * pObj.y) / (qr * pr);
                s = (q.x * pObj.y - q.y * pObj.x) / (qr * pr);
            }
            return NormalFromRotation(q, dq, c, s);
        }

        protected override void SampleObject(Point2f u, out Point3f pObj, out Vector3f nObj, out Point2f uv)
        {
            Float z = SampleZ(u[0]);
            z = Clamp(z, zMin, zMax);
            Float phi = u[1] * phiMax;
            Vector3f q, dq, ddq;
            Profile(z, out q, out dq, out ddq);
            Float c = std.cos(phi), s = std.sin(phi);
            pObj = new Point3f(c * q.x - s * q.y, s * q.x + c * q.y, z);
            nObj = NormalFromRotation(q, dq, c, s);
            uv = new Point2f(u[1], (z - zStart) / (zEnd - zStart));
        }

        protected override Bounds3f ObjectBounds()
        {
            Float rMax = std.max(Radius(zMin), Radius(zMax));
            Float xMin, xMax, yMin, yMax;
            QuadricUtil.SectorExtent(RadiusMin(), rMax, phiMax, out xMin, out xMax, out yMin, out yMax);
            return new Bounds3f(new Point3f(xMin, yMin, zMin), new Point3f(xMax, yMax, zMax));
        }

        // The radius of the surface at height z
        protected Float Radius(Float z)
        {
            Float rho2 = (qA * z + qB) * z + qC;
            return rho2 > 0 ? std.sqrt(rho2) : (Float)0;
        }

        // The smallest radius over [zMin, zMax] (a conservative 0 unless a subclass knows better)
        protected virtual Float RadiusMin()
        {
            return 0;
        }

        public override pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax)
        {
            // Transform Ray origin and direction to object space
            Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
            Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));

            // Compute the quadratic coefficients of x^2 + y^2 - (A z^2 + B z + C) along the ray
            Interval a = Sqr(di.x) + Sqr(di.y) - qA * Sqr(di.z);
            Interval b = 2 * (di.x * oi.x + di.y * oi.y - qA * oi.z * di.z) - qB * di.z;
            Interval c = Sqr(oi.x) + Sqr(oi.y) - qA * Sqr(oi.z) - qB * oi.z - qC;

            Interval t0, t1;
            if (a.LowerBound() <= 0 && a.UpperBound() >= 0)
            {
                // The ray is parallel to a generator of the surface: the equation is linear (one root, the other is at infinity)
                if (b.LowerBound() <= 0 && b.UpperBound() >= 0)
                {
                    return default;
                }
                t0 = t1 = -c / b;
            }
            else
            {
                // Compute quadratic discriminant
                Interval discrim = b * b - 4 * a * c;
                if (discrim.LowerBound() < 0)
                {
                    return default;
                }

                // Compute quadratic t values (the numerically stable form: no subtraction of nearly equal terms)
                Interval rootDiscrim = Sqrt(discrim);
                Interval q;
                if ((Float)b < 0)
                {
                    q = -.5f * (b - rootDiscrim);
                }
                else
                {
                    q = -.5f * (b + rootDiscrim);
                }
                t0 = q / a;
                t1 = c / q;
                // Swap quadratic t values so that t0 is the lesser
                if (t0.LowerBound() > t1.LowerBound())
                {
                    Interval tmp = t0;
                    t0 = t1;
                    t1 = tmp;
                }
            }

            // Check quadric shape t0 and t1 for nearest intersection
            if (t0.UpperBound() > tMax || t1.LowerBound() <= 0)
            {
                return default;
            }
            Interval tShapeHit = t0;
            if (tShapeHit.LowerBound() <= 0)
            {
                tShapeHit = t1;
                if (tShapeHit.UpperBound() > tMax)
                {
                    return default;
                }
            }

            Point3f pHit;
            Float phi;
            if (!TryHit(oi, di, tShapeHit, out pHit, out phi))
            {
                // The nearest root is clipped: the second root of the full surface may be inside the partial one
                if (tShapeHit == t1)
                {
                    return default;
                }
                tShapeHit = t1;
                if (t1.UpperBound() > tMax)
                {
                    return default;
                }
                if (!TryHit(oi, di, tShapeHit, out pHit, out phi))
                {
                    return default;
                }
            }
            return new QuadricIntersection((Float)tShapeHit, pHit, phi);
        }

        // The refined hit point at the ray parameter t and its angle phi relative to the generator; false if it is clipped
        bool TryHit(Point3fi oi, Vector3fi di, Interval t, out Point3f pHit, out Float phi)
        {
            pHit = (Point3f)oi + (Float)t * (Vector3f)di;
            phi = 0;
            if (pHit.z < zMin || pHit.z > zMax)
            {
                return false;
            }

            // Refine the hit point: reproject it onto the surface at its height
            Vector3f q, dq, ddq;
            Profile(pHit.z, out q, out dq, out ddq);
            Float qr = std.sqrt(q.x * q.x + q.y * q.y);
            Float hr = std.sqrt(pHit.x * pHit.x + pHit.y * pHit.y);
            if (hr > 0)
            {
                Float k = qr / hr;
                pHit.x *= k;
                pHit.y *= k;
            }
            else
            {
                pHit.x = q.x;
                pHit.y = q.y;
            }

            phi = std.atan2(q.x * pHit.y - q.y * pHit.x, q.x * pHit.x + q.y * pHit.y);
            if (phi < 0)
            {
                phi += 2 * Pi;
            }
            return phi <= phiMax;
        }

        public override SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time)
        {
            Point3f pHit = isect.pObj;
            Float phi = isect.phi;
            Float dz = zEnd - zStart;
            Float u = phi / phiMax;
            Float v = (pHit.z - zStart) / dz;

            Vector3f q, dq, ddq;
            Profile(pHit.z, out q, out dq, out ddq);
            Float c = std.cos(phi), s = std.sin(phi);
            Vector3f rdq = new Vector3f(c * dq.x - s * dq.y, s * dq.x + c * dq.y, dq.z);
            Vector3f rddq = new Vector3f(c * ddq.x - s * ddq.y, s * ddq.x + c * ddq.y, ddq.z);

            Vector3f dpdu = new Vector3f(-phiMax * pHit.y, phiMax * pHit.x, 0);
            Vector3f dpdv = dz * rdq;
            Vector3f d2Pduu = -phiMax * phiMax * new Vector3f(pHit.x, pHit.y, 0);
            Vector3f d2Pduv = phiMax * dz * new Vector3f(-rdq.y, rdq.x, 0);
            Vector3f d2Pdvv = dz * dz * rddq;
            Normal3f dndu, dndv;
            QuadricUtil.NormalDerivatives(dpdu, dpdv, d2Pduu, d2Pduv, d2Pdvv, out dndu, out dndv);

            Vector3f pError = gamma(5) * Abs((Vector3f)pHit);
            return MakeInteraction(pHit, pError, new Point2f(u, v), wo, dpdu, dpdv, dndu, dndv, time);
        }
    }

    // A surface of revolution whose generator is the straight segment p1 - p2 (the cone and the hyperboloid)
    public abstract unsafe class SegmentRevolvedQuadric : RevolvedQuadric
    {
        protected Point3f p1, p2;

        protected SegmentRevolvedQuadric(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                         Point3f p1, Point3f p2, Float zStart, Float zEnd, Float phiMax)
            : base(renderFromObject, objectFromRender, reverseOrientation, zStart, zEnd, phiMax)
        {
            this.p1 = p1;
            this.p2 = p2;
            // rho^2(z) = |p1.xy + (z - z1) g|^2 with g = (p2.xy - p1.xy) / (z2 - z1)
            double dz = (double)p2.z - p1.z;
            double gx = ((double)p2.x - p1.x) / dz, gy = ((double)p2.y - p1.y) / dz;
            double hx = p1.x - p1.z * gx, hy = p1.y - p1.z * gy;
            qA = (Float)(gx * gx + gy * gy);
            qB = (Float)(2 * (hx * gx + hy * gy));
            qC = (Float)(hx * hx + hy * hy);
        }

        protected override void Profile(Float z, out Vector3f q, out Vector3f dq, out Vector3f ddq)
        {
            Float dz = p2.z - p1.z;
            Float s = (z - p1.z) / dz;
            q = new Vector3f(p1.x + s * (p2.x - p1.x), p1.y + s * (p2.y - p1.y), z);
            dq = new Vector3f((p2.x - p1.x) / dz, (p2.y - p1.y) / dz, 1);
            ddq = new Vector3f(0, 0, 0);
        }

        // S(z)^2 = rho^2 + (rho rho')^2 = Q(z) + (Q'(z) / 2)^2 for the area element rho sqrt(1 + rho'^2) = S
        protected double AreaElement(double z)
        {
            double rho2 = (qA * z + qB) * z + qC;
            double half = qA * z + 0.5 * qB;
            return Math.Sqrt(Math.Max(0, rho2 + half * half));
        }
    }

    // Cone Definition: RenderMan's Cone height radius thetamax; the base circle of the given radius is at z = 0 and the apex at
    // z = height. Extended with zmin and zmax to cut the cone between two heights (v is then measured from zmin to zmax).
    public sealed unsafe class Cone : SegmentRevolvedQuadric
    {
        Float height, radius, k;

        public Cone(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float height, Float radius,
                    Float zMin, Float zMax, Float phiMax)
            : base(renderFromObject, objectFromRender, reverseOrientation, new Point3f(radius, 0, 0), new Point3f(0, 0, height),
                   ClampLow(height, zMin, zMax), ClampHigh(height, zMin, zMax), QuadricUtil.SweepRadians(phiMax))
        {
            this.height = height;
            this.radius = radius;
            this.k = radius / height;
        }

        // The z range lies between the base and the apex
        static Float ClampLow(Float height, Float zMin, Float zMax)
        {
            Float lo = std.min(0, height), hi = std.max(0, height);
            return Clamp(std.min(zMin, zMax), lo, hi);
        }

        static Float ClampHigh(Float height, Float zMin, Float zMax)
        {
            Float lo = std.min(0, height), hi = std.max(0, height);
            return Clamp(std.max(zMin, zMax), lo, hi);
        }

        protected override double ProfileArea()
        {
            double w0 = height - (double)zMin, w1 = height - (double)zMax;
            double kk = Math.Abs((double)radius / height);
            return kk * Math.Sqrt(1 + kk * kk) * 0.5 * Math.Abs(w0 * w0 - w1 * w1);
        }

        // The area up to z grows as (h - zMin)^2 - (h - z)^2, which inverts in closed form
        protected override Float SampleZ(Float u)
        {
            double w0 = height - (double)zMin, w1 = height - (double)zMax;
            double w2 = w0 * w0 + u * (w1 * w1 - w0 * w0);
            double w = Math.Sqrt(Math.Max(0, w2)) * (height > 0 ? 1 : -1);
            return (Float)(height - w);
        }

        protected override Float RadiusMin()
        {
            return std.min(Radius(zMin), Radius(zMax));
        }

        public override DirectionCone NormalBounds()
        {
            // The normals (cos phi, sin phi, k) / sqrt(1 + k^2) lie on a cone about the z axis
            Float cosTheta = std.abs(k) / std.sqrt(1 + k * k) - 1e-5f;
            return TransformNormalCone(new Vector3f(0, 0, k >= 0 ? 1 : -1), cosTheta);
        }

        public override string ToString()
        {
            return StringPrintf("[ Cone renderFromObject: %s objectFromRender: %s reverseOrientation: %s transformSwapsHandedness: %s " +
                                "height: %f radius: %f zMin: %f zMax: %f phiMax: %f ]",
                                renderFromObject, objectFromRender, reverseOrientation, transformSwapsHandedness, height, radius,
                                zMin, zMax, phiMax);
        }

        public static Cone Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                  ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float height = parameters.GetOneFloat("height", 1);
            Float radius = parameters.GetOneFloat("radius", 1);
            Float zmin = parameters.GetOneFloat("zmin", 0);
            Float zmax = parameters.GetOneFloat("zmax", height);
            Float thetamax = parameters.GetOneFloat("thetamax", 360);
            Float phimax = parameters.GetOneFloat("phimax", thetamax);
            if (height == 0 || radius <= 0)
            {
                ErrorExit(loc, "cone: \"height\" must be nonzero and \"radius\" positive.");
            }
            return new Cone(renderFromObject, objectFromRender, reverseOrientation, height, radius, zmin, zmax, phimax);
        }
    }

    // Hyperboloid Definition: RenderMan's Hyperboloid point1 point2 thetamax, the surface swept by the segment p1 - p2 turning about the
    // z axis. (u, v) = (phi / phimax, along the segment from p1 to p2); phi is measured from the segment.
    public sealed unsafe class Hyperboloid : SegmentRevolvedQuadric
    {
        const int TableCells = 512;
        double[] cumulative;   // the integral of the area element from zMin to the lower edge of each cell
        double cellWidth;
        double total;

        public Hyperboloid(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Point3f p1, Point3f p2,
                           Float phiMax)
            : base(renderFromObject, objectFromRender, reverseOrientation, p1, p2, p1.z, p2.z, QuadricUtil.SweepRadians(phiMax))
        {
            // Integrate the area element with 5-point Gauss-Legendre rules, cell by cell
            double[] nodes = { 0.0, -0.5384693101056831, 0.5384693101056831, -0.9061798459386640, 0.9061798459386640 };
            double[] weights = { 0.5688888888888889, 0.4786286704993665, 0.4786286704993665, 0.2369268850561891, 0.2369268850561891 };
            cumulative = new double[TableCells + 1];
            cellWidth = ((double)zMax - zMin) / TableCells;
            for (int i = 0; i < TableCells; ++i)
            {
                double center = zMin + (i + 0.5) * cellWidth;
                double sum = 0;
                for (int j = 0; j < 5; ++j)
                {
                    sum += weights[j] * AreaElement(center + 0.5 * cellWidth * nodes[j]);
                }
                cumulative[i + 1] = cumulative[i] + 0.5 * cellWidth * sum;
            }
            total = cumulative[TableCells];
        }

        protected override double ProfileArea()
        {
            return total;
        }

        // Inverts the cumulative area: find the cell by bisection, then solve the integral of the (nearly linear) area element in it
        protected override Float SampleZ(Float u)
        {
            double target = u * total;
            int lo = 0, hi = TableCells;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (cumulative[mid] <= target)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }
            double z0 = zMin + lo * cellWidth;
            double remaining = target - cumulative[lo];
            double s0 = AreaElement(z0), s1 = AreaElement(z0 + cellWidth);
            double slope = (s1 - s0) / cellWidth;
            double discriminant = Math.Max(0, s0 * s0 + 2 * slope * remaining);
            double denominator = s0 + Math.Sqrt(discriminant);
            double d = denominator > 0 ? 2 * remaining / denominator : cellWidth * (remaining / Math.Max(1e-300, cumulative[lo + 1] - cumulative[lo]));
            d = Math.Min(Math.Max(d, 0), cellWidth);
            return (Float)(z0 + d);
        }

        protected override Bounds3f ObjectBounds()
        {
            // The generator turns as it rises, so the azimuth of the surface is not 0 .. phimax: use the full circle
            Float rMax = std.max(Radius(zMin), Radius(zMax));
            if (qA > 0)
            {
                Float zThroat = -qB / (2 * qA);
                if (zThroat > zMin && zThroat < zMax)
                {
                    rMax = std.max(rMax, Radius(zThroat));
                }
            }
            return new Bounds3f(new Point3f(-rMax, -rMax, zMin), new Point3f(rMax, rMax, zMax));
        }

        public override string ToString()
        {
            return StringPrintf("[ Hyperboloid renderFromObject: %s objectFromRender: %s reverseOrientation: %s " +
                                "transformSwapsHandedness: %s p1: %s p2: %s phiMax: %f ]",
                                renderFromObject, objectFromRender, reverseOrientation, transformSwapsHandedness, p1, p2, phiMax);
        }

        public static Hyperboloid Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                         ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Point3f p1 = parameters.GetOnePoint3f("p1", new Point3f(0, 0, 0));
            Point3f p2 = parameters.GetOnePoint3f("p2", new Point3f(1, 1, 1));
            Float thetamax = parameters.GetOneFloat("thetamax", 360);
            Float phimax = parameters.GetOneFloat("phimax", thetamax);
            if (p1.z == p2.z)
            {
                ErrorExit(loc, "hyperboloid: \"p1\" and \"p2\" must have different z.");
            }
            return new Hyperboloid(renderFromObject, objectFromRender, reverseOrientation, p1, p2, phimax);
        }
    }

    // Paraboloid Definition: RenderMan's Paraboloid rmax zmin zmax thetamax, x^2 + y^2 = rmax^2 z / zmax for zmin <= z <= zmax
    public sealed unsafe class Paraboloid : RevolvedQuadric
    {
        Float radius;
        Float c;   // rho = c sqrt(z), c^2 = radius^2 / zMax

        public Paraboloid(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float radius, Float zMin,
                          Float zMax, Float phiMax)
            : base(renderFromObject, objectFromRender, reverseOrientation, std.max(0, std.min(zMin, zMax)), std.max(std.max(zMin, zMax), 1e-30f),
                   QuadricUtil.SweepRadians(phiMax))
        {
            this.radius = radius;
            this.c = radius / std.sqrt(zMax);
            qA = 0;
            qB = c * c;
            qC = 0;
        }

        protected override void Profile(Float z, out Vector3f q, out Vector3f dq, out Vector3f ddq)
        {
            // Keep clear of the apex, where the slope of the profile is infinite
            Float zz = std.max(z, 1e-7f * zMax);
            Float root = std.sqrt(zz);
            q = new Vector3f(c * root, 0, z);
            dq = new Vector3f(c / (2 * root), 0, 1);
            ddq = new Vector3f(-c / (4 * zz * root), 0, 0);
        }

        // rho sqrt(1 + rho'^2) = c sqrt(z + c^2 / 4), whose integral is (2 c / 3) (z + c^2 / 4)^(3/2)
        protected override double ProfileArea()
        {
            double m = (double)c * c / 4;
            return 2.0 * c / 3 * (Math.Pow(zMax + m, 1.5) - Math.Pow(zMin + m, 1.5));
        }

        protected override Float SampleZ(Float u)
        {
            double m = (double)c * c / 4;
            double w0 = Math.Pow(zMin + m, 1.5), w1 = Math.Pow(zMax + m, 1.5);
            double w = w0 + u * (w1 - w0);
            return (Float)(Math.Pow(w, 2.0 / 3) - m);
        }

        protected override Float RadiusMin()
        {
            return Radius(zMin);
        }

        public override DirectionCone NormalBounds()
        {
            // The normals (cos phi, sin phi, -rho') / sqrt(1 + rho'^2) lie within the angle of the one at zMax of -z
            Float cosTheta = c / std.sqrt(4 * zMax + c * c) - 1e-5f;
            return TransformNormalCone(new Vector3f(0, 0, -1), cosTheta);
        }

        public override string ToString()
        {
            return StringPrintf("[ Paraboloid renderFromObject: %s objectFromRender: %s reverseOrientation: %s " +
                                "transformSwapsHandedness: %s radius: %f zMin: %f zMax: %f phiMax: %f ]",
                                renderFromObject, objectFromRender, reverseOrientation, transformSwapsHandedness, radius, zMin, zMax,
                                phiMax);
        }

        public static Paraboloid Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                        ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float radius = parameters.GetOneFloat("radius", 1);
            Float zmin = parameters.GetOneFloat("zmin", 0);
            Float zmax = parameters.GetOneFloat("zmax", 1);
            Float thetamax = parameters.GetOneFloat("thetamax", 360);
            Float phimax = parameters.GetOneFloat("phimax", thetamax);
            if (std.max(zmin, zmax) <= 0 || radius <= 0)
            {
                ErrorExit(loc, "paraboloid: \"radius\" must be positive and \"zmax\" greater than zero.");
            }
            return new Paraboloid(renderFromObject, objectFromRender, reverseOrientation, radius, zmin, zmax, phimax);
        }
    }

    // Torus Definition: RenderMan's Torus majorrad minorrad phimin phimax thetamax. The tube of radius minorRadius whose center circle
    // has radius majorRadius about the z axis: P = ((R + r cos(phi)) cos(theta), (R + r cos(phi)) sin(theta), r sin(phi)) with the
    // minor angle phi in [phiMin, phiMax] and the sweep angle theta in [0, thetaMax]. (u, v) = (theta / thetaMax, (phi - phiMin) /
    // (phiMax - phiMin)). Rays are intersected by solving the quartic for the ray parameter.
    public sealed unsafe class Torus : QuadricBase
    {
        Float majorRadius, minorRadius;
        Float phiMin, phiSweep;   // the minor angle runs from phiMin to phiMin + phiSweep (radians, phiSweep <= 2 pi)
        Float thetaMax;
        bool fullMinor;

        public Torus(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation, Float majorRadius,
                     Float minorRadius, Float phiMinDegrees, Float phiMaxDegrees, Float thetaMaxDegrees)
            : base(renderFromObject, objectFromRender, reverseOrientation)
        {
            this.majorRadius = majorRadius;
            // The tube may touch the axis (a horn torus) but not cross it
            this.minorRadius = std.min(minorRadius, majorRadius);
            Float lo = std.min(phiMinDegrees, phiMaxDegrees), hi = std.max(phiMinDegrees, phiMaxDegrees);
            hi = std.min(hi, lo + 360);
            this.phiMin = Radians(lo);
            this.fullMinor = hi - lo >= 360;
            this.phiSweep = fullMinor ? 2 * Pi : Radians(hi - lo);
            this.thetaMax = QuadricUtil.SweepRadians(thetaMaxDegrees);
        }

        protected override void EvaluateDiced(double u, double v, out SdfVector p, out SdfVector n)
        {
            double theta = u * thetaMax, phi = phiMin + v * phiSweep;
            double ct = Math.Cos(theta), st = Math.Sin(theta), cp = Math.Cos(phi), sp = Math.Sin(phi);
            double rho = majorRadius + minorRadius * cp;
            p = new SdfVector(rho * ct, rho * st, minorRadius * sp);
            n = new SdfVector(cp * ct, cp * st, sp);
        }

        protected override Float ComputeObjectArea()
        {
            double phiEnd = (double)phiMin + phiSweep;
            return (Float)(thetaMax * minorRadius * (majorRadius * (double)phiSweep + minorRadius * (Math.Sin(phiEnd) - Math.Sin(phiMin))));
        }

        // The minor angle of an object-space point, relative to phiMin, in [0, 2 pi)
        Float MinorAngle(Float rho, Float z)
        {
            Float phi = std.atan2(z, rho - majorRadius) - phiMin;
            Float twoPi = 2 * Pi;
            phi -= twoPi * std.floor(phi / twoPi);
            return phi;
        }

        protected override Vector3f ObjectNormal(Point3f pObj)
        {
            Float rho = std.sqrt(pObj.x * pObj.x + pObj.y * pObj.y);
            Float radial = rho - majorRadius;
            if (rho > 0)
            {
                return new Vector3f(radial * pObj.x / rho, radial * pObj.y / rho, pObj.z);
            }
            return new Vector3f(0, 0, pObj.z);
        }

        protected override void SampleObject(Point2f u, out Point3f pObj, out Vector3f nObj, out Point2f uv)
        {
            double theta = u[1] * thetaMax;
            double sinStart = Math.Sin(phiMin);
            double total = (double)majorRadius * phiSweep + (double)minorRadius * (Math.Sin((double)phiMin + phiSweep) - sinStart);
            double target = u[0] * total;

            // The area density in the minor angle is proportional to R + r cos(phi): invert its integral
            // F(x) = R x + r (sin(phiMin + x) - sin(phiMin)) on x in [0, phiSweep] with a safeguarded Newton iteration
            double lo = 0, hi = phiSweep;
            double x = total > 0 ? phiSweep * target / total : 0;
            for (int iteration = 0; iteration < 40; ++iteration)
            {
                double f = majorRadius * x + minorRadius * (Math.Sin(phiMin + x) - sinStart) - target;
                if (f > 0)
                {
                    hi = x;
                }
                else
                {
                    lo = x;
                }
                double derivative = majorRadius + minorRadius * Math.Cos(phiMin + x);
                double next = derivative > 0 ? x - f / derivative : 0.5 * (lo + hi);
                if (!(next > lo && next < hi))
                {
                    next = 0.5 * (lo + hi);
                }
                if (Math.Abs(next - x) <= 1e-14)
                {
                    x = next;
                    break;
                }
                x = next;
            }

            double phi = phiMin + x;
            double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
            double cosTheta = Math.Cos(theta), sinTheta = Math.Sin(theta);
            double rho = majorRadius + minorRadius * cosPhi;
            pObj = new Point3f((Float)(rho * cosTheta), (Float)(rho * sinTheta), (Float)(minorRadius * sinPhi));
            nObj = new Vector3f((Float)(cosPhi * cosTheta), (Float)(cosPhi * sinTheta), (Float)sinPhi);
            uv = new Point2f(u[1], (Float)(x / phiSweep));
        }

        protected override Bounds3f ObjectBounds()
        {
            double cosMin, cosMax, sinMin, sinMax;
            QuadricUtil.ArcExtremes(phiMin, phiSweep, out cosMin, out cosMax, out sinMin, out sinMax);
            Float rhoMin = (Float)(majorRadius + minorRadius * cosMin), rhoMax = (Float)(majorRadius + minorRadius * cosMax);
            Float xMin, xMax, yMin, yMax;
            QuadricUtil.SectorExtent(std.max(rhoMin, 0), rhoMax, thetaMax, out xMin, out xMax, out yMin, out yMax);
            Float pad = 1e-6f * (majorRadius + minorRadius);
            return new Bounds3f(new Point3f(xMin, yMin, (Float)(minorRadius * sinMin) - pad),
                                new Point3f(xMax, yMax, (Float)(minorRadius * sinMax) + pad));
        }

        public override pstd.optional<QuadricIntersection> BasicIntersect(Ray r, Float tMax)
        {
            // Transform Ray origin and direction to object space
            Point3fi oi = objectFromRender.Apply(new Point3fi(r.o));
            Vector3fi di = objectFromRender.Apply(new Vector3fi(r.d));
            double ox = (Float)oi.x, oy = (Float)oi.y, oz = (Float)oi.z;
            double dx = (Float)di.x, dy = (Float)di.y, dz = (Float)di.z;
            double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!(length > 0))
            {
                return default;
            }

            // Work in units of the major radius along the unit direction e, from the point of the ray closest to the center: with
            // o' there and the parameter s from it, |P|^2 = |o'|^2 + s^2 and the torus equation (|P|^2 + 1 - r^2)^2 = 4 (x^2 + y^2)
            // is the depressed quartic s^4 + c2 s^2 + c1 s + c0 = 0
            double scale = 1 / (double)majorRadius;
            double ex = dx / length, ey = dy / length, ez = dz / length;
            double nx = ox * scale, ny = oy * scale, nz = oz * scale;
            double s0 = -(nx * ex + ny * ey + nz * ez);
            double px = nx + s0 * ex, py = ny + s0 * ey, pz = nz + s0 * ez;
            double distance2 = px * px + py * py + pz * pz;
            double rn = minorRadius * scale;
            double reach = 1 + rn;
            if (distance2 > reach * reach * (1 + 1e-9))
            {
                return default;
            }
            double gamma0 = distance2 + 1 - rn * rn;
            double axy = ex * ex + ey * ey, bxy = px * ex + py * ey, cxy = px * px + py * py;
            double c2 = 2 * gamma0 - 4 * axy;
            double c1 = -8 * bxy;
            double c0 = gamma0 * gamma0 - 4 * cxy;
            double* roots = stackalloc double[4];
            int count = RobustQuartic.Solve(1, 0, c2, c1, c0, roots);

            // A root must lie beyond the uncertainty of the object-space ray origin
            double originError = 0, maxOrigin = Math.Max(Math.Abs(ox), Math.Max(Math.Abs(oy), Math.Abs(oz)));
            originError = Math.Max(originError, 0.5 * oi.x.Width());
            originError = Math.Max(originError, 0.5 * oi.y.Width());
            originError = Math.Max(originError, 0.5 * oi.z.Width());
            double tSafe = (originError + gamma(6) * maxOrigin) / length;

            double twoPi = 2 * Math.PI;
            for (int i = 0; i < count; ++i)
            {
                double t = (roots[i] + s0) / scale / length;
                if (t <= tSafe)
                {
                    continue;
                }
                if (t >= tMax)
                {
                    break;
                }

                // Refine the hit point: reproject it onto the tube
                double hx = ox + t * dx, hy = oy + t * dy, hz = oz + t * dz;
                double rho = Math.Sqrt(hx * hx + hy * hy);
                double radial = rho - majorRadius;
                double tube = Math.Sqrt(radial * radial + hz * hz);
                if (tube == 0)
                {
                    continue;
                }
                double k = minorRadius / tube;
                double rhoRefined = majorRadius + radial * k;
                double zRefined = hz * k;
                double xRefined, yRefined;
                if (rho > 0)
                {
                    xRefined = hx * rhoRefined / rho;
                    yRefined = hy * rhoRefined / rho;
                }
                else
                {
                    xRefined = rhoRefined;
                    yRefined = 0;
                }

                // Test against the clipping parameters
                double theta = Math.Atan2(yRefined, xRefined);
                if (theta < 0)
                {
                    theta += twoPi;
                }
                if (theta > thetaMax)
                {
                    continue;
                }
                if (!fullMinor)
                {
                    double phiRelative = Math.Atan2(hz, radial) - phiMin;
                    phiRelative -= twoPi * Math.Floor(phiRelative / twoPi);
                    if (phiRelative > phiSweep)
                    {
                        continue;
                    }
                }
                return new QuadricIntersection((Float)t, new Point3f((Float)xRefined, (Float)yRefined, (Float)zRefined), (Float)theta);
            }
            return default;
        }

        public override SurfaceInteraction InteractionFromIntersection(QuadricIntersection isect, Vector3f wo, Float time)
        {
            Point3f pHit = isect.pObj;
            Float theta = isect.phi;
            Float rho = std.sqrt(pHit.x * pHit.x + pHit.y * pHit.y);
            Float phiRelative = MinorAngle(rho, pHit.z);
            Float phi = phiRelative + phiMin;
            Float cosPhi = std.cos(phi), sinPhi = std.sin(phi);
            Float cosTheta = rho > 0 ? pHit.x / rho : 1, sinTheta = rho > 0 ? pHit.y / rho : 0;

            Float u = theta / thetaMax;
            Float v = phiRelative / phiSweep;
            Vector3f dpdu = new Vector3f(-thetaMax * pHit.y, thetaMax * pHit.x, 0);
            Vector3f dpdv = phiSweep * minorRadius * new Vector3f(-sinPhi * cosTheta, -sinPhi * sinTheta, cosPhi);
            Vector3f d2Pduu = -thetaMax * thetaMax * new Vector3f(pHit.x, pHit.y, 0);
            Vector3f d2Pduv = thetaMax * phiSweep * minorRadius * sinPhi * new Vector3f(sinTheta, -cosTheta, 0);
            Vector3f d2Pdvv = -phiSweep * phiSweep * minorRadius * new Vector3f(cosPhi * cosTheta, cosPhi * sinTheta, sinPhi);
            Normal3f dndu, dndv;
            QuadricUtil.NormalDerivatives(dpdu, dpdv, d2Pduu, d2Pduv, d2Pdvv, out dndu, out dndv);

            Vector3f pError = gamma(5) * Abs((Vector3f)pHit);
            return MakeInteraction(pHit, pError, new Point2f(u, v), wo, dpdu, dpdv, dndu, dndv, time);
        }

        public override string ToString()
        {
            return StringPrintf("[ Torus renderFromObject: %s objectFromRender: %s reverseOrientation: %s transformSwapsHandedness: %s " +
                                "majorRadius: %f minorRadius: %f phiMin: %f phiSweep: %f thetaMax: %f ]",
                                renderFromObject, objectFromRender, reverseOrientation, transformSwapsHandedness, majorRadius,
                                minorRadius, phiMin, phiSweep, thetaMax);
        }

        public static Torus Create(Transform renderFromObject, Transform objectFromRender, bool reverseOrientation,
                                   ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float minorDefault = parameters.GetOneFloat("minorrad", 0.25f);
            Float majorDefault = parameters.GetOneFloat("majorrad", 1);
            Float major = parameters.GetOneFloat("majorradius", majorDefault);
            Float minor = parameters.GetOneFloat("minorradius", minorDefault);
            Float phimin = parameters.GetOneFloat("phimin", 0);
            Float phimax = parameters.GetOneFloat("phimax", 360);
            Float thetamax = parameters.GetOneFloat("thetamax", 360);
            if (major <= 0 || minor <= 0)
            {
                ErrorExit(loc, "torus: \"majorradius\" and \"minorradius\" must be positive.");
            }
            return new Torus(renderFromObject, objectFromRender, reverseOrientation, major, minor, phimin, phimax, thetamax);
        }
    }
}  // namespace pbrt
