// (not in pbrt-v4) sdf.cs
//
// Signed distance fields in double precision: the node library behind Shape "sdf" and the float texture "sdf" (the counterpart of
// OctaneRender's Vectron). A node is a function f(p) that is negative inside the solid, positive outside and zero on the surface.
// Every node also reports a Lipschitz bound, an upper bound of |grad f|, which is what makes sphere tracing and the culling of empty
// space safe for fields that are not exact distances (a smooth blend, a twist, a displacement, a fractal estimate): a step of
// |f| / Lipschitz can never cross the surface.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;

namespace pbrt
{

    // SdfVector Definition
    public readonly struct SdfVector
    {
        public SdfVector(double x, double y, double z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static SdfVector operator +(SdfVector a, SdfVector b)
        {
            return new SdfVector(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static SdfVector operator -(SdfVector a, SdfVector b)
        {
            return new SdfVector(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public static SdfVector operator -(SdfVector a)
        {
            return new SdfVector(-a.x, -a.y, -a.z);
        }

        public static SdfVector operator *(double s, SdfVector a)
        {
            return new SdfVector(s * a.x, s * a.y, s * a.z);
        }

        public static SdfVector operator *(SdfVector a, double s)
        {
            return new SdfVector(s * a.x, s * a.y, s * a.z);
        }

        public static SdfVector operator /(SdfVector a, double s)
        {
            return new SdfVector(a.x / s, a.y / s, a.z / s);
        }

        public static double Dot(SdfVector a, SdfVector b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public static SdfVector Cross(SdfVector a, SdfVector b)
        {
            return new SdfVector(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public double Length()
        {
            return Math.Sqrt(x * x + y * y + z * z);
        }

        public double LengthSquared()
        {
            return x * x + y * y + z * z;
        }

        public SdfVector Normalized()
        {
            double length = Length();
            return length > 0 ? new SdfVector(x / length, y / length, z / length) : new SdfVector(0, 0, 1);
        }

        public double this[int axis]
        {
            get
            {
                return axis == 0 ? x : (axis == 1 ? y : z);
            }
        }

        public readonly double x, y, z;
    }

    // SdfNode Definition
    public abstract class SdfNode
    {
        // The signed distance (or the bounded estimate of it) at p
        public abstract double Evaluate(SdfVector p);

        // The expression of the field at the point (x, y, z) for the compiled evaluator (sdfcompile.cs); the children are compiled with
        // context.Compile. null (the default) means that the node has no compiled form and is called through its Evaluate.
        public virtual Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return null;
        }

        // An upper bound of the length of the gradient, 1 for an exact distance
        public virtual double Lipschitz()
        {
            return 1;
        }

        // An upper bound of the length of the gradient within the ball of the given radius about center, never above Lipschitz(): the
        // bound that segment tracing (Galin et al. 2020) uses to take longer steps where the field is tame (a blend whose weight does
        // not change near the point, say). The default is the global bound.
        public virtual double Lipschitz(SdfVector center, double radius)
        {
            return Lipschitz();
        }

        // Whether Lipschitz(center, radius) can be smaller than Lipschitz() for this field
        public virtual bool HasLocalBounds()
        {
            return false;
        }

        // Whether Lipschitz() is a proven bound of the gradient (false for fractal distance estimates, whose bound only holds near
        // the surface); the bounds that a density texture derives from the field are only used for rigorous fields
        public virtual bool IsRigorous()
        {
            return true;
        }

        // The gradient by a tetrahedral central difference; h is the offset of the four samples
        public SdfVector Gradient(SdfVector p, double h)
        {
            SdfVector k0 = new SdfVector(1, -1, -1), k1 = new SdfVector(-1, -1, 1), k2 = new SdfVector(-1, 1, -1),
                      k3 = new SdfVector(1, 1, 1);
            SdfVector sum = k0 * Evaluate(p + h * k0) + k1 * Evaluate(p + h * k1) + k2 * Evaluate(p + h * k2) +
                            k3 * Evaluate(p + h * k3);
            // For f(p) = g . p the four samples add up to 4 h g
            return sum / (4 * h);
        }
    }

    // Primitives (after Inigo Quilez's distance functions; all but the ellipsoid and the fractals are exact)

    public sealed class SdfSphere : SdfNode
    {
        public SdfSphere(double radius)
        {
            this.radius = radius;
        }

        public override double Evaluate(SdfVector p)
        {
            return p.Length() - radius;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression squared = context.Add(context.Add(context.Multiply(x, x), context.Multiply(y, y)), context.Multiply(z, z));
            return context.Subtract(context.Sqrt(squared), context.Constant(radius));
        }

        private readonly double radius;
    }

    public sealed class SdfBox : SdfNode
    {
        public SdfBox(double hx, double hy, double hz, double rounding)
        {
            halfExtent = new SdfVector(hx - rounding, hy - rounding, hz - rounding);
            this.rounding = rounding;
        }

        public override double Evaluate(SdfVector p)
        {
            double qx = Math.Abs(p.x) - halfExtent.x, qy = Math.Abs(p.y) - halfExtent.y, qz = Math.Abs(p.z) - halfExtent.z;
            double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0), oz = Math.Max(qz, 0);
            return Math.Sqrt(ox * ox + oy * oy + oz * oz) + Math.Min(Math.Max(qx, Math.Max(qy, qz)), 0) - rounding;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression qx = context.Define(context.Subtract(context.Abs(x), context.Constant(halfExtent.x)));
            Expression qy = context.Define(context.Subtract(context.Abs(y), context.Constant(halfExtent.y)));
            Expression qz = context.Define(context.Subtract(context.Abs(z), context.Constant(halfExtent.z)));
            Expression ox = context.Define(context.Max(qx, context.Constant(0)));
            Expression oy = context.Define(context.Max(qy, context.Constant(0)));
            Expression oz = context.Define(context.Max(qz, context.Constant(0)));
            Expression outside = context.Sqrt(context.Add(context.Add(context.Multiply(ox, ox), context.Multiply(oy, oy)), context.Multiply(oz, oz)));
            Expression inside = context.Min(context.Max(qx, context.Max(qy, qz)), context.Constant(0));
            return context.Subtract(context.Add(outside, inside), context.Constant(rounding));
        }

        private readonly SdfVector halfExtent;
        private readonly double rounding;
    }

    public sealed class SdfTorus : SdfNode
    {
        public SdfTorus(double majorRadius, double minorRadius)
        {
            this.majorRadius = majorRadius;
            this.minorRadius = minorRadius;
        }

        public override double Evaluate(SdfVector p)
        {
            double qx = Math.Sqrt(p.x * p.x + p.z * p.z) - majorRadius;
            return Math.Sqrt(qx * qx + p.y * p.y) - minorRadius;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression ring = context.Sqrt(context.Add(context.Multiply(x, x), context.Multiply(z, z)));
            Expression qx = context.Define(context.Subtract(ring, context.Constant(majorRadius)));
            return context.Subtract(context.Sqrt(context.Add(context.Multiply(qx, qx), context.Multiply(y, y))), context.Constant(minorRadius));
        }

        private readonly double majorRadius, minorRadius;
    }

    // A capped cylinder along y
    public sealed class SdfCylinder : SdfNode
    {
        public SdfCylinder(double radius, double halfHeight)
        {
            this.radius = radius;
            this.halfHeight = halfHeight;
        }

        public override double Evaluate(SdfVector p)
        {
            double dx = Math.Sqrt(p.x * p.x + p.z * p.z) - radius, dy = Math.Abs(p.y) - halfHeight;
            double ox = Math.Max(dx, 0), oy = Math.Max(dy, 0);
            return Math.Min(Math.Max(dx, dy), 0) + Math.Sqrt(ox * ox + oy * oy);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression ring = context.Sqrt(context.Add(context.Multiply(x, x), context.Multiply(z, z)));
            Expression dx = context.Define(context.Subtract(ring, context.Constant(radius)));
            Expression dy = context.Define(context.Subtract(context.Abs(y), context.Constant(halfHeight)));
            Expression ox = context.Define(context.Max(dx, context.Constant(0)));
            Expression oy = context.Define(context.Max(dy, context.Constant(0)));
            Expression inside = context.Min(context.Max(dx, dy), context.Constant(0));
            return context.Add(inside, context.Sqrt(context.Add(context.Multiply(ox, ox), context.Multiply(oy, oy))));
        }

        private readonly double radius, halfHeight;
    }

    public sealed class SdfCapsule : SdfNode
    {
        public SdfCapsule(SdfVector a, SdfVector b, double radius)
        {
            this.a = a;
            this.b = b;
            this.radius = radius;
        }

        public override double Evaluate(SdfVector p)
        {
            SdfVector pa = p - a, ba = b - a;
            double denominator = SdfVector.Dot(ba, ba);
            double h = denominator > 0 ? Math.Min(Math.Max(SdfVector.Dot(pa, ba) / denominator, 0), 1) : 0;
            return (pa - ba * h).Length() - radius;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            SdfVector ba = b - a;
            double denominator = SdfVector.Dot(ba, ba);
            Expression pax = context.Define(context.Subtract(x, context.Constant(a.x)));
            Expression pay = context.Define(context.Subtract(y, context.Constant(a.y)));
            Expression paz = context.Define(context.Subtract(z, context.Constant(a.z)));
            Expression h = context.Constant(0);
            if (denominator > 0)
            {
                Expression projection = context.Add(context.Add(context.Multiply(pax, context.Constant(ba.x)),
                                                                context.Multiply(pay, context.Constant(ba.y))),
                                                    context.Multiply(paz, context.Constant(ba.z)));
                h = context.Define(context.Clamp(context.Divide(projection, context.Constant(denominator)), 0, 1));
            }
            Expression dx = context.Define(context.Subtract(pax, context.Multiply(context.Constant(ba.x), h)));
            Expression dy = context.Define(context.Subtract(pay, context.Multiply(context.Constant(ba.y), h)));
            Expression dz = context.Define(context.Subtract(paz, context.Multiply(context.Constant(ba.z), h)));
            Expression length = context.Sqrt(context.Add(context.Add(context.Multiply(dx, dx), context.Multiply(dy, dy)), context.Multiply(dz, dz)));
            return context.Subtract(length, context.Constant(radius));
        }

        private readonly SdfVector a, b;
        private readonly double radius;
    }

    // A finite cone with its apex at (0, height, 0) and its base of the given radius on y = 0
    public sealed class SdfCone : SdfNode
    {
        public SdfCone(double radius, double height)
        {
            this.radius = radius;
            this.height = height;
        }

        public override double Evaluate(SdfVector p)
        {
            double wx = Math.Sqrt(p.x * p.x + p.z * p.z), wy = p.y - height;
            double qx = radius, qy = -height;
            double t = Math.Min(Math.Max((wx * qx + wy * qy) / (qx * qx + qy * qy), 0), 1);
            double ax = wx - qx * t, ay = wy - qy * t;
            double s = Math.Min(Math.Max(wx / qx, 0), 1);
            double bx = wx - qx * s, by = wy - qy;
            double d = Math.Min(ax * ax + ay * ay, bx * bx + by * by);
            // k = sign(qy) = -1 because the base is below the apex
            double sign = Math.Max(-(wx * qy - wy * qx), -(wy - qy));
            return Math.Sqrt(d) * (sign > 0 ? 1 : -1);
        }

        private readonly double radius, height;
    }

    public sealed class SdfPlane : SdfNode
    {
        public SdfPlane(SdfVector normal, double offset)
        {
            this.normal = normal.Normalized();
            this.offset = offset;
        }

        public override double Evaluate(SdfVector p)
        {
            return SdfVector.Dot(p, normal) - offset;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression dot = context.Add(context.Add(context.Multiply(x, context.Constant(normal.x)), context.Multiply(y, context.Constant(normal.y))),
                                         context.Multiply(z, context.Constant(normal.z)));
            return context.Subtract(dot, context.Constant(offset));
        }

        private readonly SdfVector normal;
        private readonly double offset;
    }

    // An ellipsoid by Quilez's first-order estimate (not an exact distance, so the Lipschitz bound is the axis ratio)
    public sealed class SdfEllipsoid : SdfNode
    {
        public SdfEllipsoid(double rx, double ry, double rz)
        {
            radii = new SdfVector(rx, ry, rz);
            lipschitz = Math.Max(rx, Math.Max(ry, rz)) / Math.Min(rx, Math.Min(ry, rz));
        }

        public override double Evaluate(SdfVector p)
        {
            SdfVector scaled = new SdfVector(p.x / radii.x, p.y / radii.y, p.z / radii.z);
            double k0 = scaled.Length();
            SdfVector doubleScaled = new SdfVector(scaled.x / radii.x, scaled.y / radii.y, scaled.z / radii.z);
            double k1 = doubleScaled.Length();
            return k1 > 0 ? k0 * (k0 - 1) / k1 : -Math.Min(radii.x, Math.Min(radii.y, radii.z));
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        private readonly SdfVector radii;
        private readonly double lipschitz;
    }

    // A regular octahedron with the given distance from the center to a vertex (exact)
    public sealed class SdfOctahedron : SdfNode
    {
        public SdfOctahedron(double size)
        {
            this.size = size;
        }

        public override double Evaluate(SdfVector p)
        {
            double x = Math.Abs(p.x), y = Math.Abs(p.y), z = Math.Abs(p.z);
            double m = x + y + z - size;
            double qx, qy, qz;
            if (3 * x < m)
            {
                qx = x; qy = y; qz = z;
            }
            else if (3 * y < m)
            {
                qx = y; qy = z; qz = x;
            }
            else if (3 * z < m)
            {
                qx = z; qy = x; qz = y;
            }
            else
            {
                return m * 0.57735026919;
            }
            double k = Math.Min(Math.Max(0.5 * (qz - qy + size), 0), size);
            return new SdfVector(qx, qy - size + k, qz - k).Length();
        }

        private readonly double size;
    }

    // The Mandelbulb (the distance estimate 0.5 r log r / dr; the safety factor below 1 shortens the sphere tracing steps)
    public sealed class SdfMandelbulb : SdfNode
    {
        public SdfMandelbulb(double power, int iterations, double bailout, double safety)
        {
            this.power = power;
            this.iterations = iterations;
            this.bailout = bailout;
            this.safety = safety;
        }

        public override double Evaluate(SdfVector p)
        {
            SdfVector z = p;
            double dr = 1, r = 0;
            for (int i = 0; i < iterations; ++i)
            {
                r = z.Length();
                if (r > bailout)
                {
                    break;
                }
                double theta = Math.Acos(Math.Min(Math.Max(z.z / r, -1), 1)) * power;
                double phi = Math.Atan2(z.y, z.x) * power;
                dr = Math.Pow(r, power - 1) * power * dr + 1;
                double zr = Math.Pow(r, power);
                z = zr * new SdfVector(Math.Sin(theta) * Math.Cos(phi), Math.Sin(theta) * Math.Sin(phi), Math.Cos(theta)) + p;
            }
            return 0.5 * Math.Log(Math.Max(r, 1e-300)) * r / dr;
        }

        public override double Lipschitz()
        {
            return 1 / safety;
        }

        public override bool IsRigorous()
        {
            return false;
        }

        private readonly double power, bailout, safety;
        private readonly int iterations;
    }

    // The Menger sponge in [-1, 1]^3 (a lower bound of the distance: the box minus the three crosses at each level)
    public sealed class SdfMengerSponge : SdfNode
    {
        public SdfMengerSponge(int iterations)
        {
            this.iterations = iterations;
            box = new SdfBox(1, 1, 1, 0);
        }

        public override double Evaluate(SdfVector p)
        {
            double d = box.Evaluate(p);
            double s = 1;
            for (int i = 0; i < iterations; ++i)
            {
                double ax = Mod(p.x * s, 2) - 1, ay = Mod(p.y * s, 2) - 1, az = Mod(p.z * s, 2) - 1;
                s *= 3;
                double rx = Math.Abs(1 - 3 * Math.Abs(ax)), ry = Math.Abs(1 - 3 * Math.Abs(ay)), rz = Math.Abs(1 - 3 * Math.Abs(az));
                double da = Math.Max(rx, ry), db = Math.Max(ry, rz), dc = Math.Max(rz, rx);
                double c = (Math.Min(da, Math.Min(db, dc)) - 1) / s;
                d = Math.Max(d, c);
            }
            return d;
        }

        private static double Mod(double x, double y)
        {
            return x - y * Math.Floor(x / y);
        }

        private readonly int iterations;
        private readonly SdfBox box;
    }

    // The gyroid sheet sin x cos y + sin y cos z + sin z cos x = 0 with the given period and wall thickness (the field is
    // divided by its largest gradient so that it is a lower bound of the distance)
    public sealed class SdfGyroid : SdfNode
    {
        public SdfGyroid(double period, double thickness)
        {
            frequency = 2 * Math.PI / period;
            this.thickness = thickness;
        }

        public override double Evaluate(SdfVector p)
        {
            double x = p.x * frequency, y = p.y * frequency, z = p.z * frequency;
            double g = Math.Sin(x) * Math.Cos(y) + Math.Sin(y) * Math.Cos(z) + Math.Sin(z) * Math.Cos(x);
            return Math.Abs(g) / (frequency * 2.449489742783178) - thickness;
        }

        private readonly double frequency, thickness;
    }

    // Operators

    public enum SdfBlendKind
    {
        Polynomial,
        Cubic,
        Exponential
    }

    public static class SdfBlend
    {
        // A smooth minimum of a and b with a blend width k (which becomes the plain minimum for k = 0)
        public static double SmoothMin(double a, double b, double k, SdfBlendKind kind)
        {
            if (k <= 0)
            {
                return Math.Min(a, b);
            }
            if (kind == SdfBlendKind.Polynomial)
            {
                double h = Math.Max(k - Math.Abs(a - b), 0) / k;
                return Math.Min(a, b) - h * h * k * 0.25;
            }
            if (kind == SdfBlendKind.Cubic)
            {
                double h = Math.Max(k - Math.Abs(a - b), 0) / k;
                return Math.Min(a, b) - h * h * h * k * (1.0 / 6.0);
            }
            double res = Math.Exp(-a / k) + Math.Exp(-b / k);
            return -k * Math.Log(res);
        }

        public static double SmoothMax(double a, double b, double k, SdfBlendKind kind)
        {
            return -SmoothMin(-a, -b, k, kind);
        }
    }

    public sealed class SdfUnion : SdfNode
    {
        public SdfUnion(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Min(a.Evaluate(p), b.Evaluate(p));
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return Math.Max(a.Lipschitz(center, radius), b.Lipschitz(center, radius));
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Min(context.Compile(a, x, y, z), context.Compile(b, x, y, z));
        }

        private readonly SdfNode a, b;
    }

    public sealed class SdfIntersection : SdfNode
    {
        public SdfIntersection(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Max(a.Evaluate(p), b.Evaluate(p));
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return Math.Max(a.Lipschitz(center, radius), b.Lipschitz(center, radius));
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Max(context.Compile(a, x, y, z), context.Compile(b, x, y, z));
        }

        private readonly SdfNode a, b;
    }

    // a with b removed
    public sealed class SdfSubtraction : SdfNode
    {
        public SdfSubtraction(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Max(a.Evaluate(p), -b.Evaluate(p));
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return Math.Max(a.Lipschitz(center, radius), b.Lipschitz(center, radius));
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression da = context.Compile(a, x, y, z);
            Expression db = context.Compile(b, x, y, z);
            return context.Max(da, context.Negate(db));
        }

        private readonly SdfNode a, b;
    }

    // 0 = smooth union, 1 = smooth intersection, 2 = smooth subtraction (a minus b)
    public sealed class SdfSmoothCombine : SdfNode
    {
        public SdfSmoothCombine(SdfNode a, SdfNode b, double width, SdfBlendKind kind, int operation)
        {
            this.a = a;
            this.b = b;
            this.width = width;
            this.kind = kind;
            this.operation = operation;
        }

        public override double Evaluate(SdfVector p)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            if (operation == 0)
            {
                return SdfBlend.SmoothMin(da, db, width, kind);
            }
            if (operation == 1)
            {
                return SdfBlend.SmoothMax(da, db, width, kind);
            }
            return SdfBlend.SmoothMax(da, -db, width, kind);
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return Math.Max(a.Lipschitz(center, radius), b.Lipschitz(center, radius));
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression da = context.Compile(a, x, y, z);
            Expression db = context.Compile(b, x, y, z);
            if (width <= 0)
            {
                // The plain minimum or maximum, as SdfBlend does for a width of 0
                if (operation == 0)
                {
                    return context.Min(da, db);
                }
                if (operation == 1)
                {
                    return context.Max(da, db);
                }
                return context.Max(da, context.Negate(db));
            }
            if (operation == 0)
            {
                return context.SmoothMin(da, db, width, kind);
            }
            if (operation == 1)
            {
                return context.SmoothMax(da, db, width, kind);
            }
            return context.SmoothMax(da, context.Negate(db), width, kind);
        }

        private readonly SdfNode a, b;
        private readonly double width;
        private readonly SdfBlendKind kind;
        private readonly int operation;
    }

    // Octane's mixing of two fields: (1 - t) a + t b, with t constant
    public sealed class SdfMix : SdfNode
    {
        public SdfMix(SdfNode a, SdfNode b, double t)
        {
            this.a = a;
            this.b = b;
            this.t = t;
        }

        public override double Evaluate(SdfVector p)
        {
            return (1 - t) * a.Evaluate(p) + t * b.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return (1 - t) * a.Lipschitz() + t * b.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return (1 - t) * a.Lipschitz(center, radius) + t * b.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression da = context.Compile(a, x, y, z);
            Expression db = context.Compile(b, x, y, z);
            return context.Add(context.Multiply(context.Constant(1 - t), da), context.Multiply(context.Constant(t), db));
        }

        private readonly SdfNode a, b;
        private readonly double t;
    }

    // Octane's mixing of two fields with a weight that varies in space: (1 - t(p)) a + t(p) b, where t is the value of another field
    // clamped to [0, 1] (a ramp or a noise below, or any field). The gradient of the blend is (1 - t) grad a + t grad b + (b - a) grad t,
    // so the Lipschitz bound is that of the larger of a and b plus the bound of t times the largest difference |a - b| in the domain,
    // which is at most |a(0) - b(0)| + (La + Lb) times the radius of the domain (or the maximum that is given).
    public sealed class SdfMixField : SdfNode
    {
        public SdfMixField(SdfNode a, SdfNode b, SdfNode weight, double domainRadius, double maxDifference)
        {
            this.a = a;
            this.b = b;
            this.weight = weight;
            SdfVector origin = new SdfVector(0, 0, 0);
            double difference = maxDifference > 0 ? maxDifference
                                                  : Math.Abs(a.Evaluate(origin) - b.Evaluate(origin)) +
                                                    (a.Lipschitz() + b.Lipschitz()) * domainRadius;
            lipschitz = Math.Max(a.Lipschitz(), b.Lipschitz()) + weight.Lipschitz() * difference;
        }

        public override double Evaluate(SdfVector p)
        {
            double t = Math.Min(Math.Max(weight.Evaluate(p), 0), 1);
            return (1 - t) * a.Evaluate(p) + t * b.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            double boundA = a.Lipschitz(center, radius), boundB = b.Lipschitz(center, radius);
            double boundWeight = weight.Lipschitz(center, radius);
            // The weight over the ball lies within reach of its value at the center
            double w = weight.Evaluate(center), reach = boundWeight * radius;
            if (w + reach <= 0)
            {
                return boundA;
            }
            if (w - reach >= 1)
            {
                return boundB;
            }
            double valueA = a.Evaluate(center), valueB = b.Evaluate(center);
            double difference = Math.Abs(valueA - valueB) + (boundA + boundB) * radius;
            return Math.Max(boundA, boundB) + boundWeight * difference;
        }

        public override bool HasLocalBounds()
        {
            return true;
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous() && weight.IsRigorous();
        }

        private readonly SdfNode a, b, weight;
        private readonly double lipschitz;
    }

    // A scalar field for a weight: the smooth step of a field between two values, 0 where the field is below low and 1 where it is above
    // high (low and high may be given in either order, which reverses the ramp)
    public sealed class SdfRamp : SdfNode
    {
        public SdfRamp(SdfNode child, double low, double high)
        {
            this.child = child;
            this.low = low;
            this.high = high;
        }

        public override double Evaluate(SdfVector p)
        {
            double width = high - low;
            if (width == 0)
            {
                return child.Evaluate(p) < low ? 0 : 1;
            }
            double t = Math.Min(Math.Max((child.Evaluate(p) - low) / width, 0), 1);
            return t * t * (3 - 2 * t);
        }

        // The smooth step has a slope of at most 3 / 2
        public override double Lipschitz()
        {
            return high == low ? 0 : 1.5 * child.Lipschitz() / Math.Abs(high - low);
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            double childBound = child.Lipschitz(center, radius);
            if (high == low)
            {
                return 0;
            }
            // Where the child stays on one side of both ends of the ramp in the ball the weight is constant
            double value = child.Evaluate(center), reach = childBound * radius;
            double lowEnd = Math.Min(low, high), highEnd = Math.Max(low, high);
            if (value + reach < lowEnd || value - reach > highEnd)
            {
                return 0;
            }
            return 1.5 * childBound / Math.Abs(high - low);
        }

        public override bool HasLocalBounds()
        {
            return true;
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfNode child;
        private readonly double low, high;
    }

    // A scalar field for a weight: fractal noise remapped to [0, 1] (0.5 + 0.5 times the normalized sum of the octaves, clamped)
    public sealed class SdfNoiseWeight : SdfNode
    {
        public SdfNoiseWeight(double frequency, int octaves, double roughness)
        {
            this.frequency = frequency;
            this.octaves = Math.Max(1, octaves);
            this.roughness = roughness;
            double sum = 0, weight = 1, rate = 1, weightSum = 0;
            for (int i = 0; i < this.octaves; ++i)
            {
                sum += weight * rate;
                weightSum += weight;
                weight *= roughness;
                rate *= 2;
            }
            normalization = weightSum;
            lipschitz = 0.5 * frequency * SdfNoiseDisplacement.NoiseGradientBound * sum / weightSum;
        }

        public override double Evaluate(SdfVector p)
        {
            double value = 0, weight = 1, scale = frequency;
            for (int i = 0; i < octaves; ++i)
            {
                value += weight * pbrt.Noise(p.x * scale, p.y * scale, p.z * scale);
                weight *= roughness;
                scale *= 2;
            }
            return Math.Min(Math.Max(0.5 + 0.5 * value / normalization, 0), 1);
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        private readonly double frequency, roughness, normalization, lipschitz;
        private readonly int octaves;
    }

    // Offsets the surface outward (a negative amount shrinks it)
    public sealed class SdfOffset : SdfNode
    {
        public SdfOffset(SdfNode child, double amount)
        {
            this.child = child;
            this.amount = amount;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(p) - amount;
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return child.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return child.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Subtract(context.Compile(child, x, y, z), context.Constant(amount));
        }

        private readonly SdfNode child;
        private readonly double amount;
    }

    // The shell of the given thickness around the surface (a hollow skin)
    public sealed class SdfShell : SdfNode
    {
        public SdfShell(SdfNode child, double thickness)
        {
            this.child = child;
            this.thickness = thickness;
        }

        public override double Evaluate(SdfVector p)
        {
            return Math.Abs(child.Evaluate(p)) - 0.5 * thickness;
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return child.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return child.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Subtract(context.Abs(context.Compile(child, x, y, z)), context.Constant(0.5 * thickness));
        }

        private readonly SdfNode child;
        private readonly double thickness;
    }

    // A rigid or affine change of the point: f(M p + t) with the 3x3 matrix M (row major) and the translation t
    public sealed class SdfAffine : SdfNode
    {
        public SdfAffine(SdfNode child, double[] matrix, SdfVector translation)
        {
            this.child = child;
            m = (double[])matrix.Clone();
            this.translation = translation;
            // The operator norm of the matrix, by power iteration on M^T M
            SdfVector v = new SdfVector(1, 0.7, 0.3).Normalized();
            double norm = 0;
            for (int i = 0; i < 64; ++i)
            {
                SdfVector mv = Linear(v);
                SdfVector mtmv = new SdfVector(m[0] * mv.x + m[3] * mv.y + m[6] * mv.z, m[1] * mv.x + m[4] * mv.y + m[7] * mv.z,
                                               m[2] * mv.x + m[5] * mv.y + m[8] * mv.z);
                norm = mtmv.Length();
                if (norm == 0)
                {
                    break;
                }
                v = mtmv / norm;
            }
            scale = Math.Sqrt(norm) * 1.0000001;
        }

        private SdfVector Linear(SdfVector p)
        {
            return new SdfVector(m[0] * p.x + m[1] * p.y + m[2] * p.z, m[3] * p.x + m[4] * p.y + m[5] * p.z,
                                 m[6] * p.x + m[7] * p.y + m[8] * p.z);
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(Linear(p) + translation);
        }

        public override double Lipschitz()
        {
            return child.Lipschitz() * scale;
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            // The ball maps to a ball of scale times the radius, about the mapped center
            SdfVector mapped = Linear(center) + translation;
            return child.Lipschitz(mapped, scale * radius) * scale;
        }

        public override bool HasLocalBounds()
        {
            return child.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression nx = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(m[0]), x),
                                                                                context.Multiply(context.Constant(m[1]), y)),
                                                                    context.Multiply(context.Constant(m[2]), z)),
                                                        context.Constant(translation.x)));
            Expression ny = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(m[3]), x),
                                                                                context.Multiply(context.Constant(m[4]), y)),
                                                                    context.Multiply(context.Constant(m[5]), z)),
                                                        context.Constant(translation.y)));
            Expression nz = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(m[6]), x),
                                                                                context.Multiply(context.Constant(m[7]), y)),
                                                                    context.Multiply(context.Constant(m[8]), z)),
                                                        context.Constant(translation.z)));
            return context.Compile(child, nx, ny, nz);
        }

        private readonly SdfNode child;
        private readonly double[] m;
        private readonly SdfVector translation;
        private readonly double scale;
    }

    // Scales the field about the origin; a uniform scale keeps it an exact distance, a non-uniform one is scaled by the
    // smallest factor so that the estimate stays a lower bound
    public sealed class SdfScale : SdfNode
    {
        public SdfScale(SdfNode child, double sx, double sy, double sz)
        {
            this.child = child;
            this.sx = sx;
            this.sy = sy;
            this.sz = sz;
            smallest = Math.Min(sx, Math.Min(sy, sz));
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(new SdfVector(p.x / sx, p.y / sy, p.z / sz)) * smallest;
        }

        public override double Lipschitz()
        {
            // d/dp_i of f(p / s) * smallest is at most L * smallest / s_i, and smallest <= s_i
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression nx = context.Define(context.Divide(x, context.Constant(sx)));
            Expression ny = context.Define(context.Divide(y, context.Constant(sy)));
            Expression nz = context.Define(context.Divide(z, context.Constant(sz)));
            return context.Multiply(context.Compile(child, nx, ny, nz), context.Constant(smallest));
        }

        private readonly SdfNode child;
        private readonly double sx, sy, sz, smallest;
    }

    // Twists the space about y by k radians per unit of height
    public sealed class SdfTwist : SdfNode
    {
        public SdfTwist(SdfNode child, double k, double domainRadius)
        {
            this.child = child;
            this.k = k;
            lipschitz = child.Lipschitz() * Math.Sqrt(1 + k * k * domainRadius * domainRadius);
        }

        public override double Evaluate(SdfVector p)
        {
            double c = Math.Cos(k * p.y), s = Math.Sin(k * p.y);
            return child.Evaluate(new SdfVector(c * p.x - s * p.z, p.y, s * p.x + c * p.z));
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression angle = context.Define(context.Multiply(context.Constant(k), y));
            Expression c = context.Define(context.Cos(angle));
            Expression s = context.Define(context.Sin(angle));
            Expression nx = context.Define(context.Subtract(context.Multiply(c, x), context.Multiply(s, z)));
            Expression nz = context.Define(context.Add(context.Multiply(s, x), context.Multiply(c, z)));
            return context.Compile(child, nx, y, nz);
        }

        private readonly SdfNode child;
        private readonly double k, lipschitz;
    }

    // Bends the space about z: the x axis curls toward y with k radians per unit of length
    public sealed class SdfBend : SdfNode
    {
        public SdfBend(SdfNode child, double k, double domainRadius)
        {
            this.child = child;
            this.k = k;
            lipschitz = child.Lipschitz() * Math.Sqrt(1 + k * k * domainRadius * domainRadius);
        }

        public override double Evaluate(SdfVector p)
        {
            double c = Math.Cos(k * p.x), s = Math.Sin(k * p.x);
            return child.Evaluate(new SdfVector(c * p.x - s * p.y, s * p.x + c * p.y, p.z));
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression angle = context.Define(context.Multiply(context.Constant(k), x));
            Expression c = context.Define(context.Cos(angle));
            Expression s = context.Define(context.Sin(angle));
            Expression nx = context.Define(context.Subtract(context.Multiply(c, x), context.Multiply(s, y)));
            Expression ny = context.Define(context.Add(context.Multiply(s, x), context.Multiply(c, y)));
            return context.Compile(child, nx, ny, z);
        }

        private readonly SdfNode child;
        private readonly double k, lipschitz;
    }

    // Repeats the field with the given period along each axis (a period of 0 leaves an axis alone); the count limits the
    // number of copies to either side of the origin (0 = unlimited)
    public sealed class SdfRepeat : SdfNode
    {
        public SdfRepeat(SdfNode child, SdfVector period, int count)
        {
            this.child = child;
            this.period = period;
            this.count = count;
        }

        private double Fold(double value, double length)
        {
            if (length <= 0)
            {
                return value;
            }
            double index = Math.Round(value / length);
            if (count > 0)
            {
                index = Math.Min(Math.Max(index, -count), count);
            }
            return value - length * index;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(new SdfVector(Fold(p.x, period.x), Fold(p.y, period.y), Fold(p.z, period.z)));
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression nx = Fold(context, x, period.x);
            Expression ny = Fold(context, y, period.y);
            Expression nz = Fold(context, z, period.z);
            return context.Compile(child, nx, ny, nz);
        }

        private Expression Fold(SdfCompileContext context, Expression value, double length)
        {
            if (length <= 0)
            {
                return value;
            }
            Expression index = context.Define(context.Round(context.Divide(value, context.Constant(length))));
            if (count > 0)
            {
                index = context.Define(context.Min(context.Max(index, context.Constant(-count)), context.Constant(count)));
            }
            return context.Define(context.Subtract(value, context.Multiply(context.Constant(length), index)));
        }

        private readonly SdfNode child;
        private readonly SdfVector period;
        private readonly int count;
    }

    // Mirrors the field in the plane perpendicular to an axis (0, 1 or 2), keeping the positive side
    public sealed class SdfMirror : SdfNode
    {
        public SdfMirror(SdfNode child, int axis)
        {
            this.child = child;
            this.axis = axis;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(new SdfVector(axis == 0 ? Math.Abs(p.x) : p.x, axis == 1 ? Math.Abs(p.y) : p.y,
                                                axis == 2 ? Math.Abs(p.z) : p.z));
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression nx = axis == 0 ? context.Define(context.Abs(x)) : x;
            Expression ny = axis == 1 ? context.Define(context.Abs(y)) : y;
            Expression nz = axis == 2 ? context.Define(context.Abs(z)) : z;
            return context.Compile(child, nx, ny, nz);
        }

        private readonly SdfNode child;
        private readonly int axis;
    }

    // Stretches the field along the axes without distorting the ends: the central part of the given half lengths is a copy
    public sealed class SdfElongate : SdfNode
    {
        public SdfElongate(SdfNode child, SdfVector halfLength)
        {
            this.child = child;
            this.halfLength = halfLength;
        }

        public override double Evaluate(SdfVector p)
        {
            double qx = p.x - Math.Min(Math.Max(p.x, -halfLength.x), halfLength.x);
            double qy = p.y - Math.Min(Math.Max(p.y, -halfLength.y), halfLength.y);
            double qz = p.z - Math.Min(Math.Max(p.z, -halfLength.z), halfLength.z);
            return child.Evaluate(new SdfVector(qx, qy, qz));
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression qx = context.Define(context.Subtract(x, context.Clamp(x, -halfLength.x, halfLength.x)));
            Expression qy = context.Define(context.Subtract(y, context.Clamp(y, -halfLength.y, halfLength.y)));
            Expression qz = context.Define(context.Subtract(z, context.Clamp(z, -halfLength.z, halfLength.z)));
            return context.Compile(child, qx, qy, qz);
        }

        private readonly SdfNode child;
        private readonly SdfVector halfLength;
    }

    // Displaces the surface by amplitude * fBm(frequency * p), the Perlin noise of pbrt with the given number of octaves
    public sealed class SdfNoiseDisplacement : SdfNode
    {
        // The largest gradient of the noise, measured once over a large sample of points (with a margin)
        public static readonly double NoiseGradientBound = MeasureNoiseGradient();

        private static double MeasureNoiseGradient()
        {
            double largest = 0;
            RNG rng = new RNG(11);
            const double h = 1e-3;
            for (int i = 0; i < 200000; ++i)
            {
                double x = 100 * rng.Uniform<Float>(), y = 100 * rng.Uniform<Float>(), z = 100 * rng.Uniform<Float>();
                double gx = (Sample(x + h, y, z) - Sample(x - h, y, z)) / (2 * h);
                double gy = (Sample(x, y + h, z) - Sample(x, y - h, z)) / (2 * h);
                double gz = (Sample(x, y, z + h) - Sample(x, y, z - h)) / (2 * h);
                largest = Math.Max(largest, Math.Sqrt(gx * gx + gy * gy + gz * gz));
            }
            return largest * 1.25;
        }

        private static double Sample(double x, double y, double z)
        {
            return pbrt.Noise(x, y, z);
        }

        public SdfNoiseDisplacement(SdfNode child, double amplitude, double frequency, int octaves, double roughness)
        {
            this.child = child;
            this.amplitude = amplitude;
            this.frequency = frequency;
            this.octaves = octaves;
            this.roughness = roughness;
            // Each octave doubles the frequency and scales the amplitude by the roughness
            double sum = 0, weight = 1, rate = 1;
            for (int i = 0; i < octaves; ++i)
            {
                sum += weight * rate;
                weight *= roughness;
                rate *= 2;
            }
            lipschitz = child.Lipschitz() + Math.Abs(amplitude) * frequency * NoiseGradientBound * sum;
        }

        public override double Evaluate(SdfVector p)
        {
            double value = 0, weight = 1, scale = frequency;
            for (int i = 0; i < octaves; ++i)
            {
                value += weight * Sample(p.x * scale, p.y * scale, p.z * scale);
                weight *= roughness;
                scale *= 2;
            }
            return child.Evaluate(p) + amplitude * value;
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfNode child;
        private readonly double amplitude, frequency, roughness, lipschitz;
        private readonly int octaves;
    }

}  // namespace pbrt
