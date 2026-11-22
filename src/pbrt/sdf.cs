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
using System.Runtime.CompilerServices;

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

        // An upper bound of how fast the value changes along the straight segment from a to b, per unit of length: never above
        // Lipschitz(), and below it for a field whose gradient is long only across the direction of the segment (the bound by
        // direction of Ban and Valasek, "Generalized Lipschitz tracing of implicit surfaces", 2025, here in closed form for the
        // nodes that have one instead of from their table). A ray may step by the value over this bound.
        public virtual double RayLipschitz(SdfVector a, SdfVector b)
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

        // Whether the field is the exact distance to the surface at every point outside the solid, and likewise inside it. A union
        // by the minimum keeps the exact distance outside but not inside, an intersection or a subtraction by the maximum the
        // other way round (Quilez, "Interior distance"); operators that offset the surface or read a depth below it (offset, shell,
        // inset, a density from the distance) are only right where the side they use is exact.
        public virtual bool IsExactOutside()
        {
            return false;
        }

        public virtual bool IsExactInside()
        {
            return false;
        }

        // Adds to w the share of each labelled operand (see SdfLabel) in the material at p; weight is the share of this node. A node
        // without labels below it gives all of its weight to label 0.
        public virtual void Weights(SdfVector p, SdfWeights w, double weight)
        {
            w.Add(0, weight);
        }

        // The number of nodes of the field, as a measure of what a value of it costs (1 for a node that does not say)
        public virtual int NodeCount()
        {
            return 1;
        }

        // The field with proxies (SdfProxy) around the parts of at least `threshold` nodes that have a box, placed only where
        // nothing above the part reads more than on which side of its surface a point is (unions, intersections, subtractions,
        // transforms): what the placement rule of Hubert-Brierre et al. ("Accelerating signed distance functions", 2025) allows
        // for a proxy that is not continuous. A node that is not one of those returns itself.
        // Below a blend the values of a part matter up to the width of the blend away from its surface. `blend` is the sum of the
        // widths of the smooth unions above the part: a proxy that keeps the field out to that far from the box of the part (and
        // gives the distance to the box beyond) leaves the solid as it is, since a blend that lets go of the farther operand at
        // its width never reads the part where the proxy changed it. That needs a box that the value grows away from and is
        // not in the paper. (The paper's own 1-Lipschitz proxy for below a blend was tried and taken out: it divides the values
        // of the part by 3 to 4, which widens every blend of the part and shortens the steps of a ray near it.)
        public virtual SdfNode WithProxies(int threshold, double blend)
        {
            return this;
        }

        // A field with the values of this one inside the ball of the given center and radius, without the parts that cannot
        // matter there (an operand of a union that is farther than the other everywhere in the ball), and the range that the
        // value stays in over the ball. A node that cannot be cut down returns itself with its value at the center give or take
        // what its Lipschitz bound allows over the radius. For the fields of the cells of Shape "sdf" ("bool prune"), after
        // Keeter, "Massively parallel rendering of complex closed-form implicit surfaces" (2020).
        public virtual SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            double value = Evaluate(center);
            double reach = (HasLocalBounds() ? Math.Min(Lipschitz(center, radius), Lipschitz()) : Lipschitz()) * radius;
            lower = value - reach;
            upper = value + reach;
            return this;
        }

        // A text that two fields share exactly when they are built alike from the same nodes, to tell equal pruned fields
        public virtual void Signature(System.Text.StringBuilder key)
        {
            key.Append('#').Append(SdfIdentity.Of(this));
        }

        // A box that holds the solid, if the node knows one (false for a field without end such as a plane, and for the fields whose
        // extent is not known in closed form): what lets Shape "sdf" do without "pmin" and "pmax". grows tells whether the field
        // is at least the distance to that box at every point outside it, which is what an operator that moves the surface outward by a
        // known amount (an offset, a shell, a smooth union, a displacement) needs in order to bound its own solid; it holds for
        // every field that is exact outside.
        public virtual bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.None(out lower, out upper, out grows);
        }

        // The gradient in closed form for the nodes that have one (after Quilez, "3D distance and gradient functions"), chained
        // through the operators; a node without one answers with the central difference of its own subtree, for which h is the
        // offset. This is the gradient of the field as it is: of length 1 only where the field is an exact distance.
        public virtual SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return Gradient(p, h);
        }

        public SdfVector Gradient(SdfVector p, double h, bool analytic)
        {
            return analytic ? AnalyticGradient(p, h) : Gradient(p, h);
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

    // SdfProxy Definition: a field behind the box of its solid (the proxy node of Hubert-Brierre, Guerin, Peytavie and Galin,
    // "Accelerating signed distance functions", 2025). Away from the box by more than a margin the value is the distance to the
    // box (times the Lipschitz bound of the field, which the tracer divides by), and the field itself is not asked. That is a
    // bound of the distance to the same solid, but it jumps where the two meet, so a proxy belongs only below nodes that look
    // at the sign of their operands (see SdfNode.WithProxies), never below an offset, a shell or a blend.
    public sealed class SdfProxy : SdfNode
    {
        public SdfProxy(SdfNode child, SdfVector lower, SdfVector upper, double keep)
        {
            this.child = child;
            this.lower = lower;
            this.upper = upper;
            this.keep = keep;
            lipschitz = Math.Max(child.Lipschitz(), 1e-30);
            margin = 1e-3 * (upper - lower).Length() + 1.000001 * keep;
        }

        // The part with proxies inside it, and one around it if it is costly enough and has a box
        public static SdfNode Wrap(SdfNode part, int threshold, double blend)
        {
            if (part is SdfProxy)
            {
                return part;
            }
            if (blend > 0)
            {
                // Below a blend only the outermost part gets a proxy: the ones inside it would keep the field out to the widths
                // of all the blends between, which is most of the box of the outer one
                if (part.NodeCount() >= threshold && part.Bounds(out SdfVector partLower, out SdfVector partUpper, out bool partGrows))
                {
                    if (partGrows && part.Lipschitz() >= 1)
                    {
                        return new SdfProxy(part, partLower, partUpper, blend);
                    }
                }
                return part.WithProxies(threshold, blend);
            }
            SdfNode inner = part.WithProxies(threshold, blend);
            if (part.NodeCount() >= threshold && inner.Bounds(out SdfVector lower, out SdfVector upper, out bool grows))
            {
                return new SdfProxy(inner, lower, upper, 0);
            }
            return inner;
        }

        // For an operand of a blend: a proxy only if the box of the operand, with what the proxy keeps around it, is at most
        // half of that of the blend (a long chain of blends is nearly as large as the chain without its last part)
        public static SdfNode WrapOperand(SdfNode blendNode, SdfNode part, int threshold, double blend)
        {
            if (blend > 0 && blendNode.Bounds(out SdfVector outerLower, out SdfVector outerUpper, out bool outerGrows) &&
                part.Bounds(out SdfVector lower, out SdfVector upper, out bool grows))
            {
                double keep = blend;
                SdfVector outer = outerUpper - outerLower, inner = upper - lower;
                if ((inner.x + 2 * keep) * (inner.y + 2 * keep) * (inner.z + 2 * keep) > 0.5 * (outer.x + 2 * keep) * (outer.y + 2 * keep) * (outer.z + 2 * keep))
                {
                    return part.WithProxies(threshold, blend);
                }
            }
            return Wrap(part, threshold, blend);
        }

        public override int NodeCount()
        {
            return 1 + child.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            return this;
        }

        // The value of the proxy is at least the distance to its box whatever the field inside
        public override bool Bounds(out SdfVector lowerBound, out SdfVector upperBound, out bool grows)
        {
            lowerBound = lower;
            upperBound = upper;
            grows = lipschitz >= 1;
            return true;
        }

        public override SdfNode Prune(SdfVector center, double radius, out double least, out double most)
        {
            double distance = SdfExtent.Distance(center, lower, upper);
            if (distance - radius > margin)
            {
                // The whole ball is on the side of the box distance: the box alone stands for the proxy there
                least = lipschitz * (distance - radius);
                most = lipschitz * (distance + radius);
                if (outside == null)
                {
                    outside = new SdfBoxBound(lower, upper, lipschitz);
                }
                return outside;
            }
            SdfNode pruned = child.Prune(center, radius, out least, out most);
            if (distance + radius > margin)
            {
                // Part of the ball is on the side of the box distance
                least = Math.Min(least, lipschitz * Math.Max(distance - radius, 0));
                most = Math.Max(most, lipschitz * (distance + radius));
                return pruned == child ? this : new SdfProxy(pruned, lower, upper, keep);
            }
            // The whole ball is on the side of the field: the proxy is not needed there
            return pruned;
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append("P(");
            child.Signature(key);
            key.Append(')');
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return SdfExtent.Distance(p, lower, upper) > margin ? Gradient(p, h) : child.AnalyticGradient(p, h);
        }

        public override double Evaluate(SdfVector p)
        {
            double distance = SdfExtent.Distance(p, lower, upper);
            return distance > margin ? distance * lipschitz : child.Evaluate(p);
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
            return context.Proxy(child, lower, upper, margin, lipschitz, x, y, z);
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p, w, weight);
        }

        private readonly SdfNode child;
        private readonly SdfVector lower, upper;
        private readonly double lipschitz, margin, keep;
        private SdfNode outside;
    }

    // SdfBoxBound Definition: the distance to a box times a factor: what a proxy is away from its box
    public sealed class SdfBoxBound : SdfNode
    {
        public SdfBoxBound(SdfVector lower, SdfVector upper, double factor)
        {
            this.lower = lower;
            this.upper = upper;
            this.factor = factor;
        }

        public override double Evaluate(SdfVector p)
        {
            return SdfExtent.Distance(p, lower, upper) * factor;
        }

        public override double Lipschitz()
        {
            return factor;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Multiply(context.Sqrt(context.BoxDistance(lower, upper, x, y, z)), context.Constant(factor));
        }

        private readonly SdfVector lower, upper;
        private readonly double factor;
    }

    // SdfLocalBounds Definition: whether the twists and bends that are made from now on give a Lipschitz bound that depends on
    // the place (Shape "sdf" sets it from "bool warpbounds" while it parses). It takes 33 to 40% of the steps off a ray, but each
    // step then asks for the bound, and for a field that is cheap to evaluate the render came out slower.
    public static class SdfLocalBounds
    {
        [ThreadStatic]
        public static bool warps;
    }

    // SdfIdentity Definition: a number for each node
    public static class SdfIdentity
    {
        public static long Of(SdfNode node)
        {
            lock (numbers)
            {
                if (!numbers.TryGetValue(node, out object number))
                {
                    number = (long)(++count);
                    numbers.Add(node, number);
                }
                return (long)number;
            }
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SdfNode, object> numbers =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SdfNode, object>();
        private static long count;
    }

    // ISdfUnion Definition: a union or a smooth union of two fields, for the compiler to see a chain of them. The width is that
    // of the blend (0 for the minimum; negative for a node that is not a union that can be cut short).
    public interface ISdfUnion
    {
        SdfNode UnionFirst();
        SdfNode UnionSecond();
        double UnionWidth();
        SdfBlendKind UnionKind();
    }

    // SdfExtent Definition: the arithmetic of the boxes of SdfNode.Bounds
    public static class SdfExtent
    {
        public static bool None(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(0, 0, 0);
            upper = new SdfVector(0, 0, 0);
            grows = false;
            return false;
        }

        // The box of the given half sizes about the origin, for an exact primitive
        public static bool Symmetric(double hx, double hy, double hz, out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(-hx, -hy, -hz);
            upper = new SdfVector(hx, hy, hz);
            grows = true;
            return true;
        }

        public static void Expand(ref SdfVector lower, ref SdfVector upper, double amount)
        {
            lower = new SdfVector(lower.x - amount, lower.y - amount, lower.z - amount);
            upper = new SdfVector(upper.x + amount, upper.y + amount, upper.z + amount);
        }

        // The box of both of two fields
        public static bool Merge(SdfNode a, SdfNode b, out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!a.Bounds(out SdfVector la, out SdfVector ua, out bool ga) || !b.Bounds(out SdfVector lb, out SdfVector ub, out bool gb))
            {
                return None(out lower, out upper, out grows);
            }
            lower = new SdfVector(Math.Min(la.x, lb.x), Math.Min(la.y, lb.y), Math.Min(la.z, lb.z));
            upper = new SdfVector(Math.Max(ua.x, ub.x), Math.Max(ua.y, ub.y), Math.Max(ua.z, ub.z));
            grows = ga && gb;
            return true;
        }

        // The box of what two fields share: the overlap of their boxes, or the box of the one that has one
        public static bool Overlap(SdfNode a, SdfNode b, out SdfVector lower, out SdfVector upper, out bool grows)
        {
            bool hasA = a.Bounds(out SdfVector la, out SdfVector ua, out bool ga), hasB = b.Bounds(out SdfVector lb, out SdfVector ub, out bool gb);
            grows = false;
            if (!hasA && !hasB)
            {
                return None(out lower, out upper, out grows);
            }
            if (!hasB)
            {
                lower = la;
                upper = ua;
                return true;
            }
            if (!hasA)
            {
                lower = lb;
                upper = ub;
                return true;
            }
            lower = new SdfVector(Math.Max(la.x, lb.x), Math.Max(la.y, lb.y), Math.Max(la.z, lb.z));
            // Boxes that do not meet leave nothing: a box without size at the corner
            upper = new SdfVector(Math.Max(Math.Min(ua.x, ub.x), lower.x), Math.Max(Math.Min(ua.y, ub.y), lower.y),
                                  Math.Max(Math.Min(ua.z, ub.z), lower.z));
            return true;
        }

        // The largest distance of the box from an axis, over its corners: (x, y) picks the two coordinates across the axis
        public static double Reach(double lowerX, double upperX, double lowerY, double upperY)
        {
            double x = Math.Max(Math.Abs(lowerX), Math.Abs(upperX)), y = Math.Max(Math.Abs(lowerY), Math.Abs(upperY));
            return Math.Sqrt(x * x + y * y);
        }

        // The distance of p to the box (0 inside it)
        public static double Distance(SdfVector p, SdfVector lower, SdfVector upper)
        {
            double dx = Math.Max(Math.Max(lower.x - p.x, p.x - upper.x), 0), dy = Math.Max(Math.Max(lower.y - p.y, p.y - upper.y), 0);
            double dz = Math.Max(Math.Max(lower.z - p.z, p.z - upper.z), 0);
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }

    // SdfExactNode Definition: the base class of the primitives that are exact distances on both sides of their surface
    public abstract class SdfExactNode : SdfNode
    {
        public override bool IsExactOutside()
        {
            return true;
        }

        public override bool IsExactInside()
        {
            return true;
        }
    }

    // Primitives (after Inigo Quilez's distance functions; all but the ellipsoid and the fractals are exact)

    public sealed class SdfSphere : SdfExactNode
    {
        public SdfSphere(double radius)
        {
            this.radius = radius;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return p.Normalized();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(radius, radius, radius, out lower, out upper, out grows);
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

    public sealed class SdfBox : SdfExactNode
    {
        public SdfBox(double hx, double hy, double hz, double rounding)
        {
            halfExtent = new SdfVector(hx - rounding, hy - rounding, hz - rounding);
            this.rounding = rounding;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double wx = Math.Abs(p.x) - halfExtent.x, wy = Math.Abs(p.y) - halfExtent.y, wz = Math.Abs(p.z) - halfExtent.z;
            double largest = Math.Max(wx, Math.Max(wy, wz));
            double gx, gy, gz;
            if (largest > 0)
            {
                double qx = Math.Max(wx, 0), qy = Math.Max(wy, 0), qz = Math.Max(wz, 0);
                double length = Math.Sqrt(qx * qx + qy * qy + qz * qz);
                gx = qx / length;
                gy = qy / length;
                gz = qz / length;
            }
            else
            {
                gx = wx == largest ? 1 : 0;
                gy = wx != largest && wy == largest ? 1 : 0;
                gz = wx != largest && wy != largest ? 1 : 0;
            }
            return new SdfVector(p.x < 0 ? -gx : gx, p.y < 0 ? -gy : gy, p.z < 0 ? -gz : gz);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(halfExtent.x + rounding, halfExtent.y + rounding, halfExtent.z + rounding, out lower, out upper, out grows);
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

    public sealed class SdfTorus : SdfExactNode
    {
        public SdfTorus(double majorRadius, double minorRadius)
        {
            this.majorRadius = majorRadius;
            this.minorRadius = minorRadius;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double ring = Math.Sqrt(p.x * p.x + p.z * p.z), offset = ring - majorRadius;
            return new SdfVector(p.x * offset, p.y * ring, p.z * offset).Normalized();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(majorRadius + minorRadius, minorRadius, majorRadius + minorRadius, out lower, out upper, out grows);
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
    public sealed class SdfCylinder : SdfExactNode
    {
        public SdfCylinder(double radius, double halfHeight)
        {
            this.radius = radius;
            this.halfHeight = halfHeight;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double length = Math.Sqrt(p.x * p.x + p.z * p.z);
            double ex = length - radius, ey = Math.Abs(p.y) - halfHeight;
            double ux = length > 0 ? p.x / length : 1, uz = length > 0 ? p.z / length : 0, vy = p.y < 0 ? -1 : 1;
            if (Math.Max(ex, ey) <= 0)
            {
                return ex > ey ? new SdfVector(ux, 0, uz) : new SdfVector(0, vy, 0);
            }
            double hx = Math.Max(ex, 0), hy = Math.Max(ey, 0), f = Math.Sqrt(hx * hx + hy * hy);
            return new SdfVector(hx * ux / f, hy * vy / f, hx * uz / f);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(radius, halfHeight, radius, out lower, out upper, out grows);
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

    public sealed class SdfCapsule : SdfExactNode
    {
        public SdfCapsule(SdfVector a, SdfVector b, double radius)
        {
            this.a = a;
            this.b = b;
            this.radius = radius;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            SdfVector pa = p - a, ba = b - a;
            double denominator = SdfVector.Dot(ba, ba);
            double t = denominator > 0 ? Math.Min(Math.Max(SdfVector.Dot(pa, ba) / denominator, 0), 1) : 0;
            return (pa - ba * t).Normalized();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(Math.Min(a.x, b.x) - radius, Math.Min(a.y, b.y) - radius, Math.Min(a.z, b.z) - radius);
            upper = new SdfVector(Math.Max(a.x, b.x) + radius, Math.Max(a.y, b.y) + radius, Math.Max(a.z, b.z) + radius);
            grows = true;
            return true;
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
    public sealed class SdfCone : SdfExactNode
    {
        public SdfCone(double radius, double height)
        {
            this.radius = radius;
            this.height = height;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            lower = new SdfVector(-radius, 0, -radius);
            upper = new SdfVector(radius, height, radius);
            grows = true;
            return true;
        }

        // (called from the compiled form of a field: put in line there, see SdfCompileContext.Compile)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    public sealed class SdfPlane : SdfExactNode
    {
        public SdfPlane(SdfVector normal, double offset)
        {
            this.normal = normal.Normalized();
            this.offset = offset;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return normal;
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

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            // f = k0 (k0 - 1) / k1 with k0 = |p / r| and k1 = |p / r^2|
            SdfVector b = new SdfVector(p.x / (radii.x * radii.x), p.y / (radii.y * radii.y), p.z / (radii.z * radii.z));
            SdfVector c = new SdfVector(b.x / (radii.x * radii.x), b.y / (radii.y * radii.y), b.z / (radii.z * radii.z));
            double k0 = new SdfVector(p.x / radii.x, p.y / radii.y, p.z / radii.z).Length(), k1 = b.Length();
            if (k1 == 0)
            {
                return new SdfVector(0, 0, 0);
            }
            return b * ((2 * k0 - 1) / (k0 * k1)) - c * ((k0 * k0 - k0) / (k1 * k1 * k1));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The estimate is not known to be at least the distance to the box
            SdfExtent.Symmetric(radii.x, radii.y, radii.z, out lower, out upper, out grows);
            grows = false;
            return true;
        }

        // (called from the compiled form of a field: put in line there, see SdfCompileContext.Compile)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
    public sealed class SdfOctahedron : SdfExactNode
    {
        public SdfOctahedron(double size)
        {
            this.size = size;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double x = Math.Abs(p.x), y = Math.Abs(p.y), z = Math.Abs(p.z);
            double sx = p.x < 0 ? -1 : 1, sy = p.y < 0 ? -1 : 1, sz = p.z < 0 ? -1 : 1;
            double m = x + y + z - size;
            double qx, qy, qz;
            int order;
            if (3 * x < m)
            {
                qx = x; qy = y; qz = z; order = 0;
            }
            else if (3 * y < m)
            {
                qx = y; qy = z; qz = x; order = 1;
            }
            else if (3 * z < m)
            {
                qx = z; qy = x; qz = y; order = 2;
            }
            else
            {
                return new SdfVector(0.57735026919 * sx, 0.57735026919 * sy, 0.57735026919 * sz);
            }
            double free = 0.5 * (qz - qy + size), k = Math.Min(Math.Max(free, 0), size);
            double vx = qx, vy = qy - size + k, vz = qz - k, length = Math.Sqrt(vx * vx + vy * vy + vz * vz);
            double gx = vx / length, gy = vy / length, gz = vz / length;
            if (free > 0 && free < size)
            {
                // k moves with the point: the closest point slides along an edge
                gy = 0.5 * (vy + vz) / length;
                gz = gy;
            }
            if (order == 0)
            {
                return new SdfVector(sx * gx, sy * gy, sz * gz);
            }
            if (order == 1)
            {
                return new SdfVector(sx * gz, sy * gx, sz * gy);
            }
            return new SdfVector(sx * gy, sy * gz, sz * gx);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Symmetric(size, size, size, out lower, out upper, out grows);
        }

        // (called from the compiled form of a field: put in line there, see SdfCompileContext.Compile)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The sponge is carved out of the box, and its estimate is not below that of the box
            return box.Bounds(out lower, out upper, out grows);
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

        // The derivative of SmoothMin with respect to a (the one with respect to b is 1 minus it)
        public static double SmoothMinWeight(double a, double b, double k, SdfBlendKind kind)
        {
            if (k <= 0)
            {
                return a <= b ? 1 : 0;
            }
            if (kind == SdfBlendKind.Polynomial)
            {
                double h = Math.Max(k - Math.Abs(a - b), 0) / k;
                return a <= b ? 1 - 0.5 * h : 0.5 * h;
            }
            if (kind == SdfBlendKind.Cubic)
            {
                double h = Math.Max(k - Math.Abs(a - b), 0) / k;
                return a <= b ? 1 - 0.5 * h * h : 0.5 * h * h;
            }
            // exp(-a / k) / (exp(-a / k) + exp(-b / k))
            return 1 / (1 + Math.Exp((a - b) / k));
        }

        public static double SmoothMax(double a, double b, double k, SdfBlendKind kind)
        {
            return -SmoothMin(-a, -b, k, kind);
        }
    }

    public sealed class SdfUnion : SdfNode, ISdfUnion
    {
        public SdfUnion(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override bool IsExactOutside()
        {
            return a.IsExactOutside() && b.IsExactOutside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return a.Evaluate(p) <= b.Evaluate(p) ? a.AnalyticGradient(p, h) : b.AnalyticGradient(p, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Merge(a, b, out lower, out upper, out grows);
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            // All the operands of the unions below this one, in their order. Taken two by two, an operand is only left out if
            // the union of those before it is nearer: in a long union whose operands get nearer one after the other every one
            // of them is the nearest so far and stays. Against the smallest of the upper ends of all of them, only the
            // operands that can be the nearest somewhere in the ball stay.
            List<SdfNode> operands = new List<SdfNode>();
            Stack<SdfNode> pending = new Stack<SdfNode>();
            pending.Push(this);
            while (pending.Count > 0)
            {
                SdfNode node = pending.Pop();
                if (node is SdfUnion union)
                {
                    pending.Push(union.b);
                    pending.Push(union.a);
                }
                else
                {
                    operands.Add(node);
                }
            }
            SdfNode[] pruned = new SdfNode[operands.Count];
            double[] least = new double[operands.Count];
            lower = double.PositiveInfinity;
            upper = double.PositiveInfinity;
            bool same = true;
            for (int i = 0; i < pruned.Length; ++i)
            {
                pruned[i] = operands[i].Prune(center, radius, out least[i], out double most);
                lower = Math.Min(lower, least[i]);
                upper = Math.Min(upper, most);
                same &= pruned[i] == operands[i];
            }
            SdfNode kept = null;
            int nearest = 0;
            for (int i = 0; i < pruned.Length; ++i)
            {
                if (least[i] < least[nearest])
                {
                    nearest = i;
                }
            }
            for (int i = 0; i < pruned.Length; ++i)
            {
                if (least[i] < upper || i == nearest)
                {
                    kept = kept == null ? pruned[i] : new SdfUnion(kept, pruned[i]);
                }
                else
                {
                    same = false;
                }
            }
            return same ? this : kept;
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append("U(");
            a.Signature(key);
            key.Append(',');
            b.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            SdfNode pa = SdfProxy.Wrap(a, threshold, blend), pb = SdfProxy.Wrap(b, threshold, blend);
            return pa == a && pb == b ? this : new SdfUnion(pa, pb);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return Math.Max(a.RayLipschitz(from, to), b.RayLipschitz(from, to));
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
            Expression culled = context.cull ? context.UnionChain(this, x, y, z) : null;
            if (culled != null)
            {
                return culled;
            }
            return context.Min(context.Compile(a, x, y, z), context.Compile(b, x, y, z));
        }

        public SdfNode UnionFirst()
        {
            return a;
        }

        public SdfNode UnionSecond()
        {
            return b;
        }

        public double UnionWidth()
        {
            return 0;
        }

        public SdfBlendKind UnionKind()
        {
            return SdfBlendKind.Polynomial;
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            if (a.Evaluate(p) <= b.Evaluate(p)) a.Weights(p, w, weight); else b.Weights(p, w, weight);
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

        public override bool IsExactInside()
        {
            return a.IsExactInside() && b.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return a.Evaluate(p) >= b.Evaluate(p) ? a.AnalyticGradient(p, h) : b.AnalyticGradient(p, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Overlap(a, b, out lower, out upper, out grows);
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            SdfNode pa = a.Prune(center, radius, out double la, out double ua), pb = b.Prune(center, radius, out double lb, out double ub);
            lower = Math.Max(la, lb);
            upper = Math.Max(ua, ub);
            if (la >= ub)
            {
                return pa;
            }
            if (lb >= ua)
            {
                return pb;
            }
            return pa == a && pb == b ? this : new SdfIntersection(pa, pb);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append("I(");
            a.Signature(key);
            key.Append(',');
            b.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            SdfNode pa = SdfProxy.Wrap(a, threshold, blend), pb = SdfProxy.Wrap(b, threshold, blend);
            return pa == a && pb == b ? this : new SdfIntersection(pa, pb);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return Math.Max(a.RayLipschitz(from, to), b.RayLipschitz(from, to));
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            if (a.Evaluate(p) >= b.Evaluate(p)) a.Weights(p, w, weight); else b.Weights(p, w, weight);
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

        public override bool IsExactInside()
        {
            return a.IsExactInside() && b.IsExactOutside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return a.Evaluate(p) >= -b.Evaluate(p) ? a.AnalyticGradient(p, h) : -b.AnalyticGradient(p, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return a.Bounds(out lower, out upper, out grows);
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            SdfNode pa = a.Prune(center, radius, out double la, out double ua), pb = b.Prune(center, radius, out double lb, out double ub);
            // max(a, -b)
            lower = Math.Max(la, -ub);
            upper = Math.Max(ua, -lb);
            if (la >= -lb)
            {
                return pa;
            }
            return pa == a && pb == b ? this : new SdfSubtraction(pa, pb);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append("D(");
            a.Signature(key);
            key.Append(',');
            b.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            // Below a blend the part taken away is read inside its surface, where a proxy has nothing to give
            SdfNode pa = SdfProxy.Wrap(a, threshold, blend), pb = blend > 0 ? b : SdfProxy.Wrap(b, threshold, blend);
            return pa == a && pb == b ? this : new SdfSubtraction(pa, pb);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return Math.Max(a.RayLipschitz(from, to), b.RayLipschitz(from, to));
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            // The cut surface belongs to the subtracted shape
            if (a.Evaluate(p) >= -b.Evaluate(p)) a.Weights(p, w, weight); else b.Weights(p, w, weight);
        }

        private readonly SdfNode a, b;
    }

    // What is in one of a and b but not in both (Quilez, "sdf xor"). Every point of either surface is on the surface of the
    // result, so the distance is the smaller of |a| and |b|: exact on both sides when a and b are, unlike the other three.
    public sealed class SdfXor : SdfNode
    {
        public SdfXor(SdfNode a, SdfNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override bool IsExactOutside()
        {
            return a.IsExactOutside() && a.IsExactInside() && b.IsExactOutside() && b.IsExactInside();
        }

        public override bool IsExactInside()
        {
            return IsExactOutside();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Merge(a, b, out lower, out upper, out grows);
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            if (Math.Min(da, db) >= -Math.Max(da, db))
            {
                return da <= db ? a.AnalyticGradient(p, h) : b.AnalyticGradient(p, h);
            }
            return da >= db ? -a.AnalyticGradient(p, h) : -b.AnalyticGradient(p, h);
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            if (blend > 0)
            {
                return this;
            }
            SdfNode pa = SdfProxy.Wrap(a, threshold, blend), pb = SdfProxy.Wrap(b, threshold, blend);
            return pa == a && pb == b ? this : new SdfXor(pa, pb);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return Math.Max(a.RayLipschitz(from, to), b.RayLipschitz(from, to));
        }

        public override double Evaluate(SdfVector p)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            return Math.Max(Math.Min(da, db), -Math.Max(da, db));
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
            Expression da = context.Define(context.Compile(a, x, y, z));
            Expression db = context.Define(context.Compile(b, x, y, z));
            return context.Max(context.Min(da, db), context.Negate(context.Max(da, db)));
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            if (Math.Abs(a.Evaluate(p)) <= Math.Abs(b.Evaluate(p))) a.Weights(p, w, weight); else b.Weights(p, w, weight);
        }

        private readonly SdfNode a, b;
    }

    // 0 = smooth union, 1 = smooth intersection, 2 = smooth subtraction (a minus b)
    public sealed class SdfSmoothCombine : SdfNode, ISdfUnion
    {
        public SdfSmoothCombine(SdfNode a, SdfNode b, double width, SdfBlendKind kind, int operation)
        {
            this.a = a;
            this.b = b;
            this.width = width;
            this.kind = kind;
            this.operation = operation;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            SdfVector ga = a.AnalyticGradient(p, h), gb = b.AnalyticGradient(p, h);
            if (operation == 0)
            {
                double weight = SdfBlend.SmoothMinWeight(da, db, width, kind);
                return ga * weight + gb * (1 - weight);
            }
            if (operation == 1)
            {
                double weight = SdfBlend.SmoothMinWeight(-da, -db, width, kind);
                return ga * weight + gb * (1 - weight);
            }
            double share = SdfBlend.SmoothMinWeight(-da, db, width, kind);
            return ga * share - gb * (1 - share);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (operation == 1)
            {
                // A smooth maximum is not below the maximum
                return SdfExtent.Overlap(a, b, out lower, out upper, out grows);
            }
            if (operation == 2)
            {
                return a.Bounds(out lower, out upper, out grows);
            }
            // A smooth minimum is below the minimum by at most a share of the width
            if (!SdfExtent.Merge(a, b, out lower, out upper, out grows) || !grows)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            double below = kind == SdfBlendKind.Polynomial ? 0.25 * width : (kind == SdfBlendKind.Cubic ? width / 6 : width * Math.Log(2));
            SdfExtent.Expand(ref lower, ref upper, Math.Max(below, 0));
            return true;
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            SdfNode pa = a.Prune(center, radius, out double la, out double ua), pb = b.Prune(center, radius, out double lb, out double ub);
            // A polynomial blend leaves the nearer field as it is once the two are a width apart; the exponential one never does
            bool sharp = width <= 0 || kind != SdfBlendKind.Exponential;
            double reach = Math.Max(width, 0);
            double dip = width <= 0 ? 0 : (kind == SdfBlendKind.Polynomial ? 0.25 * width : (kind == SdfBlendKind.Cubic ? width / 6 : width * Math.Log(2)));
            if (operation == 0)
            {
                lower = Math.Min(la, lb) - dip;
                upper = Math.Min(ua, ub);
                if (sharp && la >= ub + reach)
                {
                    lower = lb;
                    return pb;
                }
                if (sharp && lb >= ua + reach)
                {
                    lower = la;
                    return pa;
                }
            }
            else if (operation == 1)
            {
                lower = Math.Max(la, lb);
                upper = Math.Max(ua, ub) + dip;
                if (sharp && la >= ub + reach)
                {
                    upper = ua;
                    return pa;
                }
                if (sharp && lb >= ua + reach)
                {
                    upper = ub;
                    return pb;
                }
            }
            else
            {
                // The smooth maximum of a and -b
                lower = Math.Max(la, -ub);
                upper = Math.Max(ua, -lb) + dip;
                if (sharp && la >= -lb + reach)
                {
                    upper = ua;
                    return pa;
                }
            }
            return pa == a && pb == b ? this : new SdfSmoothCombine(pa, pb, width, kind, operation);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append('S').Append(operation).Append('|').Append(width.ToString("R")).Append('|').Append((int)kind).Append('(');
            a.Signature(key);
            key.Append(',');
            b.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        // A smooth union reads its operands up to its width away from their surfaces (see SdfNode.WithProxies); a smooth
        // intersection or subtraction is never below its first operand, so it reads no more than a plain one does. The
        // exponential blend never lets go of an operand: no proxies below it.
        public override SdfNode WithProxies(int threshold, double blend)
        {
            double reach = Math.Max(width, 0);
            if (reach > 0 && kind == SdfBlendKind.Exponential)
            {
                return this;
            }
            double below = operation == 0 ? blend + reach : blend;
            SdfNode pa = SdfProxy.WrapOperand(this, a, threshold, below);
            SdfNode pb = operation == 2 && (below > 0 || reach > 0) ? b : SdfProxy.WrapOperand(this, b, threshold, below);
            return pa == a && pb == b ? this : new SdfSmoothCombine(pa, pb, width, kind, operation);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return Math.Max(a.RayLipschitz(from, to), b.RayLipschitz(from, to));
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

        // A union for the chains of SdfCompileContext.UnionChain: only the smooth union, and only the blends that leave the
        // nearer field as it is once the two are a width apart
        public SdfNode UnionFirst()
        {
            return a;
        }

        public SdfNode UnionSecond()
        {
            return b;
        }

        public double UnionWidth()
        {
            return operation == 0 && (width <= 0 || kind != SdfBlendKind.Exponential) ? Math.Max(width, 0) : -1;
        }

        public SdfBlendKind UnionKind()
        {
            return kind;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression culled = context.cull && UnionWidth() >= 0 ? context.UnionChain(this, x, y, z) : null;
            if (culled != null)
            {
                return culled;
            }
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            if (operation == 2) db = -db;
            // Across the blend width the weight moves smoothly from one operand to the other
            double t = width <= 0 ? (operation == 1 ? (da >= db ? 1 : 0) : (da <= db ? 1 : 0))
                                  : SdfWeights.SmoothStep(operation == 1 ? 0.5 + 0.5 * (da - db) / width : 0.5 + 0.5 * (db - da) / width);
            if (t > 0) a.Weights(p, w, weight * t);
            if (t < 1) b.Weights(p, w, weight * (1 - t));
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

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // Between a and b, so not below the smaller
            if (t < 0 || t > 1)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            return SdfExtent.Merge(a, b, out lower, out upper, out grows);
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

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression t = context.Define(context.Clamp(context.Compile(weight, x, y, z), 0, 1));
            Expression da = context.Compile(a, x, y, z), db = context.Compile(b, x, y, z);
            return context.Add(context.Multiply(context.Subtract(context.Constant(1), t), da), context.Multiply(t, db));
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

        // Beyond the offset surface the distance stays exact on the side that the surface moved toward
        public override bool IsExactOutside()
        {
            return amount >= 0 && child.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return amount <= 0 && child.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return child.AnalyticGradient(p, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            if (amount <= 0)
            {
                return true;
            }
            if (!grows)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            SdfExtent.Expand(ref lower, ref upper, amount);
            return true;
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            SdfNode pruned = child.Prune(center, radius, out lower, out upper);
            lower -= amount;
            upper -= amount;
            return pruned == child ? this : new SdfOffset(pruned, amount);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append('O').Append(amount.ToString("R")).Append('(');
            child.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + child.NodeCount();
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return child.RayLipschitz(from, to);
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p, w, weight);
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

        // The outside of the skin is the outside of the child and its hollow
        public override bool IsExactOutside()
        {
            return child.IsExactOutside() && child.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            SdfVector gradient = child.AnalyticGradient(p, h);
            return child.Evaluate(p) < 0 ? -gradient : gradient;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows) || !grows)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            SdfExtent.Expand(ref lower, ref upper, 0.5 * Math.Abs(thickness));
            return true;
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(p, w, weight);
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
            // A turn by a quarter has entries that are 0 and 1 but for the last place of the cosine: made exact, which also
            // lets the compiled form leave their products out
            for (int i = 0; i < 9; ++i)
            {
                if (Math.Abs(m[i]) < 1e-15)
                {
                    m[i] = 0;
                }
                else if (Math.Abs(Math.Abs(m[i]) - 1) < 1e-15)
                {
                    m[i] = Math.Sign(m[i]);
                }
            }
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
            // A rotation and a translation keep distances: M^T M is the identity
            double deviation = 0;
            for (int i = 0; i < 3; ++i)
            {
                for (int j = 0; j < 3; ++j)
                {
                    double entry = m[i] * m[j] + m[3 + i] * m[3 + j] + m[6 + i] * m[6 + j];
                    deviation = Math.Max(deviation, Math.Abs(entry - (i == j ? 1 : 0)));
                }
            }
            rigid = deviation < 1e-9;
        }

        private SdfVector Linear(SdfVector p)
        {
            return new SdfVector(m[0] * p.x + m[1] * p.y + m[2] * p.z, m[3] * p.x + m[4] * p.y + m[5] * p.z,
                                 m[6] * p.x + m[7] * p.y + m[8] * p.z);
        }

        public override bool IsExactOutside()
        {
            return rigid && child.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return rigid && child.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            // The gradient of f(M p + t) is M^T grad f
            SdfVector g = child.AnalyticGradient(Linear(p) + translation, h);
            return new SdfVector(m[0] * g.x + m[3] * g.y + m[6] * g.z, m[1] * g.x + m[4] * g.y + m[7] * g.z,
                                 m[2] * g.x + m[5] * g.y + m[8] * g.z);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The solid is the inverse image of that of the child: p = M^-1 (q - t) over the corners q of its box
            if (!child.Bounds(out SdfVector childLower, out SdfVector childUpper, out grows))
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            double c00 = m[4] * m[8] - m[5] * m[7], c01 = m[5] * m[6] - m[3] * m[8], c02 = m[3] * m[7] - m[4] * m[6];
            double determinant = m[0] * c00 + m[1] * c01 + m[2] * c02;
            if (Math.Abs(determinant) < 1e-300)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            double i00 = c00 / determinant, i01 = (m[2] * m[7] - m[1] * m[8]) / determinant, i02 = (m[1] * m[5] - m[2] * m[4]) / determinant;
            double i10 = c01 / determinant, i11 = (m[0] * m[8] - m[2] * m[6]) / determinant, i12 = (m[2] * m[3] - m[0] * m[5]) / determinant;
            double i20 = c02 / determinant, i21 = (m[1] * m[6] - m[0] * m[7]) / determinant, i22 = (m[0] * m[4] - m[1] * m[3]) / determinant;
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
            for (int corner = 0; corner < 8; ++corner)
            {
                double qx = ((corner & 1) != 0 ? childUpper.x : childLower.x) - translation.x;
                double qy = ((corner & 2) != 0 ? childUpper.y : childLower.y) - translation.y;
                double qz = ((corner & 4) != 0 ? childUpper.z : childLower.z) - translation.z;
                double px = i00 * qx + i01 * qy + i02 * qz, py = i10 * qx + i11 * qy + i12 * qz, pz = i20 * qx + i21 * qy + i22 * qz;
                minX = Math.Min(minX, px); minY = Math.Min(minY, py); minZ = Math.Min(minZ, pz);
                maxX = Math.Max(maxX, px); maxY = Math.Max(maxY, py); maxZ = Math.Max(maxZ, pz);
            }
            lower = new SdfVector(minX, minY, minZ);
            upper = new SdfVector(maxX, maxY, maxZ);
            grows = grows && rigid;
            return true;
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            // The ball is inside the ball of the child's space about the image of the center
            SdfNode pruned = child.Prune(Linear(center) + translation, radius * scale, out lower, out upper);
            if (pruned == child)
            {
                return this;
            }
            // The copy keeps the number of the transform, so that equal cut-down fields are seen to be equal
            SdfAffine copy = new SdfAffine(pruned, m, translation);
            copy.transform = transform != 0 ? transform : SdfIdentity.Of(this);
            return copy;
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            // The same transform of another child: the number of this node stands for the transform
            key.Append('A').Append(transform != 0 ? transform : SdfIdentity.Of(this)).Append('(');
            child.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + child.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            // The width of a blend above is a length of this frame: only a rigid transform keeps it for the child
            if (blend > 0 && !rigid)
            {
                return this;
            }
            SdfNode wrapped = SdfProxy.Wrap(child, threshold, blend);
            return wrapped == child ? this : new SdfAffine(wrapped, m, translation);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            // The segment becomes a segment, longer by the stretch of the transform along it
            SdfVector along = to - from;
            double length = along.Length();
            if (length == 0)
            {
                return Lipschitz();
            }
            double stretch = Linear(along).Length() / length;
            return Math.Min(child.RayLipschitz(Linear(from) + translation, Linear(to) + translation) * stretch, Lipschitz());
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
            // Transforms one inside the other are one transform: p -> N (M p + t) + u = (N M) p + (N t + u)
            double[] matrix = m;
            SdfVector offset = translation;
            SdfNode inner = child;
            while (inner is SdfAffine)
            {
                SdfAffine next = (SdfAffine)inner;
                double[] product = new double[9];
                for (int row = 0; row < 3; ++row)
                {
                    for (int column = 0; column < 3; ++column)
                    {
                        product[3 * row + column] = next.m[3 * row] * matrix[column] + next.m[3 * row + 1] * matrix[3 + column] +
                                                    next.m[3 * row + 2] * matrix[6 + column];
                    }
                }
                offset = next.Linear(offset) + next.translation;
                matrix = product;
                inner = next.child;
            }
            Expression nx = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(matrix[0]), x),
                                                                                context.Multiply(context.Constant(matrix[1]), y)),
                                                                    context.Multiply(context.Constant(matrix[2]), z)),
                                                        context.Constant(offset.x)));
            Expression ny = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(matrix[3]), x),
                                                                                context.Multiply(context.Constant(matrix[4]), y)),
                                                                    context.Multiply(context.Constant(matrix[5]), z)),
                                                        context.Constant(offset.y)));
            Expression nz = context.Define(context.Add(context.Add(context.Add(context.Multiply(context.Constant(matrix[6]), x),
                                                                                context.Multiply(context.Constant(matrix[7]), y)),
                                                                    context.Multiply(context.Constant(matrix[8]), z)),
                                                        context.Constant(offset.z)));
            return context.Compile(inner, nx, ny, nz);
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(Linear(p) + translation, w, weight);
        }

        private readonly SdfNode child;
        private readonly double[] m;
        private readonly SdfVector translation;
        private readonly double scale;
        private readonly bool rigid;
        private long transform;
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

        public override bool IsExactOutside()
        {
            return sx == sy && sy == sz && child.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return sx == sy && sy == sz && child.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            SdfVector g = child.AnalyticGradient(new SdfVector(p.x / sx, p.y / sy, p.z / sz), h / smallest);
            return new SdfVector(g.x * smallest / sx, g.y * smallest / sy, g.z * smallest / sz);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            lower = new SdfVector(lower.x * sx, lower.y * sy, lower.z * sz);
            upper = new SdfVector(upper.x * sx, upper.y * sy, upper.z * sz);
            grows = grows && sx == sy && sy == sz;
            return true;
        }

        public override int NodeCount()
        {
            return 1 + child.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            // The width of a blend above is not the same length for the child
            if (blend > 0)
            {
                return this;
            }
            SdfNode wrapped = SdfProxy.Wrap(child, threshold, blend);
            return wrapped == child ? this : new SdfScale(wrapped, sx, sy, sz);
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

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            child.Weights(new SdfVector(p.x / sx, p.y / sy, p.z / sz), w, weight);
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
            this.domainRadius = domainRadius;
            localBounds = SdfLocalBounds.warps;
        }

        // The twist stretches by sqrt(1 + k^2 r^2) at the distance r from its axis: over a ball that is the stretch at the far
        // side of the ball, which near the axis is much less than at the edge of the domain
        public override double Lipschitz(SdfVector center, double radius)
        {
            double reach = Math.Min(Math.Sqrt(center.x * center.x + center.z * center.z) + radius, domainRadius);
            double factor = Math.Sqrt(1 + k * k * reach * reach);
            double c = Math.Cos(k * center.y), s = Math.Sin(k * center.y);
            SdfVector mapped = new SdfVector(c * center.x - s * center.z, center.y, s * center.x + c * center.z);
            return Math.Min(child.Lipschitz(mapped, radius * factor) * factor, lipschitz);
        }

        public override bool HasLocalBounds()
        {
            return localBounds;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double c = Math.Cos(k * p.y), s = Math.Sin(k * p.y);
            double qx = c * p.x - s * p.z, qz = s * p.x + c * p.z;
            SdfVector g = child.AnalyticGradient(new SdfVector(qx, p.y, qz), h);
            return new SdfVector(c * g.x + s * g.z, g.y + k * (qx * g.z - qz * g.x), c * g.z - s * g.x);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // A turn about y keeps the height and the distance from the axis
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            double reach = SdfExtent.Reach(lower.x, upper.x, lower.z, upper.z);
            lower = new SdfVector(-reach, lower.y, -reach);
            upper = new SdfVector(reach, upper.y, reach);
            grows = false;
            return true;
        }

        // A step w along the ray moves the twisted point by the turned step plus, for its part w.y along the axis, k times the
        // distance r from the axis sideways: at most sqrt((|w.xz| + |w.y| |k| r)^2 + w.y^2). Across the axis that is 1 whatever
        // the twist; r is largest at an end of the segment.
        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            SdfVector along = to - from;
            double length = along.Length();
            if (length == 0)
            {
                return lipschitz;
            }
            double reach = Math.Min(Math.Max(Math.Sqrt(from.x * from.x + from.z * from.z), Math.Sqrt(to.x * to.x + to.z * to.z)), domainRadius);
            double across = Math.Sqrt(along.x * along.x + along.z * along.z) / length, axial = Math.Abs(along.y) / length;
            double side = across + axial * Math.Abs(k) * reach;
            return Math.Min(child.Lipschitz() * Math.Sqrt(side * side + axial * axial), lipschitz);
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
        private readonly double k, lipschitz, domainRadius;
        private readonly bool localBounds;
    }

    // Bends the space about z: the x axis curls toward y with k radians per unit of length
    public sealed class SdfBend : SdfNode
    {
        public SdfBend(SdfNode child, double k, double domainRadius)
        {
            this.child = child;
            this.k = k;
            // The bend turns the point about z by the angle k x, so moving along x both carries the point along (by 1) and turns
            // it (by k times its distance r from the axis), and the two can point the same way: a stretch of up to 1 + |k| r.
            // (This was sqrt(1 + k^2 r^2), the stretch of the twist, where the two are at a right angle; for the bend that is
            // too small, and a ray could step past the surface.)
            lipschitz = child.Lipschitz() * (1 + Math.Abs(k) * domainRadius);
            this.domainRadius = domainRadius;
            localBounds = SdfLocalBounds.warps;
        }

        // As for the twist, with the distance from the z axis
        public override double Lipschitz(SdfVector center, double radius)
        {
            double reach = Math.Min(Math.Sqrt(center.x * center.x + center.y * center.y) + radius, domainRadius);
            double factor = 1 + Math.Abs(k) * reach;
            double c = Math.Cos(k * center.x), s = Math.Sin(k * center.x);
            SdfVector mapped = new SdfVector(c * center.x - s * center.y, s * center.x + c * center.y, center.z);
            return Math.Min(child.Lipschitz(mapped, radius * factor) * factor, lipschitz);
        }

        public override bool HasLocalBounds()
        {
            return localBounds;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double c = Math.Cos(k * p.x), s = Math.Sin(k * p.x);
            double qx = c * p.x - s * p.y, qy = s * p.x + c * p.y;
            SdfVector g = child.AnalyticGradient(new SdfVector(qx, qy, p.z), h);
            return new SdfVector(g.x * (c - k * qy) + g.y * (s + k * qx), c * g.y - s * g.x, g.z);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // A turn about z keeps z and the distance from the axis
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            double reach = SdfExtent.Reach(lower.x, upper.x, lower.y, upper.y);
            lower = new SdfVector(-reach, -reach, lower.z);
            upper = new SdfVector(reach, reach, upper.z);
            grows = false;
            return true;
        }

        // As for the twist: the turn is about z by the angle k x, so the part w.x of a step turns the point
        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            SdfVector along = to - from;
            double length = along.Length();
            if (length == 0)
            {
                return lipschitz;
            }
            double reach = Math.Min(Math.Max(Math.Sqrt(from.x * from.x + from.y * from.y), Math.Sqrt(to.x * to.x + to.y * to.y)), domainRadius);
            double plane = Math.Sqrt(along.x * along.x + along.y * along.y) / length, across = Math.Abs(along.z) / length;
            double side = plane + Math.Abs(along.x) / length * Math.Abs(k) * reach;
            return Math.Min(child.Lipschitz() * Math.Sqrt(side * side + across * across), lipschitz);
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
        private readonly double k, lipschitz, domainRadius;
        private readonly bool localBounds;
    }

    // Repeats the field with the given period along each axis (a period of 0 leaves an axis alone); the count limits the
    // number of copies to either side of the origin (0 = unlimited)
    public sealed class SdfRepeat : SdfNode
    {
        public SdfRepeat(SdfNode child, SdfVector period, int count, double jitter, int seed)
        {
            this.child = child;
            this.period = period;
            this.count = count;
            this.jitter = Math.Max(jitter, 0);
            this.seed = seed;
            // The copy of a cell is not always the nearest one: a child that is off the middle of its cell, or that reaches out
            // of it, has a copy in a neighbouring cell that is nearer to some points, and the value of the cell's own copy alone
            // is then too large (a ray steps past the surface) and cuts the copies off at the walls of the cells. With the box
            // of the child the neighbours that can be nearer are found, and a cap by the distance to the boxes of the copies
            // that were not looked at keeps the value a bound (Magniez and Zanni, "Improving spatial domain repetition of
            // implicit surfaces", 2026: their neighbourhood of cells and their safe distance). The field is then that of the
            // union of the whole copies.
            // With a jitter the copy of each cell is moved by its own vector of up to `jitter` along each repeated axis (their
            // equation 5). As they note, that is the same as a larger box for every copy: all that is done with the box below
            // holds for the box grown by the jitter, and the search of the neighbours then finds the nearest copy whatever the
            // jitter is, so their limit on it (which keeps their fixed neighbourhood enough) is not needed here.
            boxed = child.Bounds(out boxLower, out boxUpper, out bool grows);
            if (boxed)
            {
                boxLower = new SdfVector(boxLower.x - (period.x > 0 ? this.jitter : 0), boxLower.y - (period.y > 0 ? this.jitter : 0),
                                         boxLower.z - (period.z > 0 ? this.jitter : 0));
                boxUpper = new SdfVector(boxUpper.x + (period.x > 0 ? this.jitter : 0), boxUpper.y + (period.y > 0 ? this.jitter : 0),
                                         boxUpper.z + (period.z > 0 ? this.jitter : 0));
                // Far enough that the copies beyond do not reach the cell (the cap stays above 0 in it), and at least 1
                reachX = Reach(boxLower.x, boxUpper.x, period.x);
                reachY = Reach(boxLower.y, boxUpper.y, period.y);
                reachZ = Reach(boxLower.z, boxUpper.z, period.z);
            }
        }

        private static int Reach(double lower, double upper, double length)
        {
            if (length <= 0)
            {
                return 0;
            }
            return Math.Max(1, (int)Math.Ceiling(Math.Max(upper, -lower) / length - 0.5 + 1e-9));
        }

        // The jitter of the copy of a cell along an axis (0 for x, 1 for y, 2 for z): the same for a cell on every run, even
        // over [-jitter, jitter], 0 along an axis that is not repeated
        public double Jitter(double ix, double iy, double iz, int axis)
        {
            if (jitter == 0 || (axis == 0 ? period.x : (axis == 1 ? period.y : period.z)) <= 0)
            {
                return 0;
            }
            ulong bits = (ulong)(long)ix * 0x9E3779B97F4A7C15UL ^ (ulong)(long)iy * 0xC2B2AE3D27D4EB4FUL ^ (ulong)(long)iz * 0x165667B19E3779F9UL ^
                         ((ulong)(uint)seed << 32 | (uint)axis);
            return jitter * (2 * ((pbrt.MixBits(bits) >> 11) * (1.0 / 9007199254740992.0)) - 1);
        }

        // The index of the cell of a coordinate (0 for an axis that is not repeated)
        private double Index(double value, double length)
        {
            if (length <= 0)
            {
                return 0;
            }
            double index = Math.Round(value / length);
            if (count > 0)
            {
                index = Math.Min(Math.Max(index, -count), count);
            }
            return index;
        }

        // Whether there is a copy at the cell index (every index without a count)
        private bool Exists(double index, double length)
        {
            return length > 0 ? (count <= 0 || Math.Abs(index) <= count) : index == 0;
        }

        // A lower bound of the distance from q (a point in the frame of the copy of the cell `index`) to the box of any copy
        // that is `steps` cells or more away along the axis
        private double Beyond(double q, double index, double length, double lower, double upper, int steps)
        {
            if (length <= 0)
            {
                return double.PositiveInfinity;
            }
            double nearest = double.PositiveInfinity;
            if (Exists(index + steps, length))
            {
                nearest = Math.Max(steps * length + lower - q, 0);
            }
            if (Exists(index - steps, length))
            {
                nearest = Math.Min(nearest, Math.Max(q - (upper - steps * length), 0));
            }
            return nearest;
        }

        // The value at p: the smallest over the copies that can be the nearest, capped by the distance to the boxes of the
        // others. shifted is p in the frame of the copy that gave the value, and capped tells that the cap did.
        private double Nearest(SdfVector p, out SdfVector shifted, out bool capped)
        {
            double ix = Index(p.x, period.x), iy = Index(p.y, period.y), iz = Index(p.z, period.z);
            SdfVector q = new SdfVector(p.x - period.x * ix, p.y - period.y * iy, p.z - period.z * iz);
            capped = false;
            shifted = jitter == 0 ? q : new SdfVector(q.x - Jitter(ix, iy, iz, 0), q.y - Jitter(ix, iy, iz, 1), q.z - Jitter(ix, iy, iz, 2));
            double best = child.Evaluate(shifted);
            if (!boxed)
            {
                return best;
            }
            // No other copy can be nearer than its box
            double others = Math.Min(Math.Min(Beyond(q.x, ix, period.x, boxLower.x, boxUpper.x, 1), Beyond(q.y, iy, period.y, boxLower.y, boxUpper.y, 1)),
                                     Beyond(q.z, iz, period.z, boxLower.z, boxUpper.z, 1));
            if (best <= others)
            {
                return best;
            }
            for (int jx = -reachX; jx <= reachX; ++jx)
            {
                if (!Exists(ix + jx, period.x))
                {
                    continue;
                }
                for (int jy = -reachY; jy <= reachY; ++jy)
                {
                    if (!Exists(iy + jy, period.y))
                    {
                        continue;
                    }
                    for (int jz = -reachZ; jz <= reachZ; ++jz)
                    {
                        if ((jx == 0 && jy == 0 && jz == 0) || !Exists(iz + jz, period.z))
                        {
                            continue;
                        }
                        SdfVector r = new SdfVector(q.x - jx * period.x, q.y - jy * period.y, q.z - jz * period.z);
                        if (SdfExtent.Distance(r, boxLower, boxUpper) >= best)
                        {
                            continue;
                        }
                        if (jitter > 0)
                        {
                            r = new SdfVector(r.x - Jitter(ix + jx, iy + jy, iz + jz, 0), r.y - Jitter(ix + jx, iy + jy, iz + jz, 1),
                                              r.z - Jitter(ix + jx, iy + jy, iz + jz, 2));
                        }
                        double value = (compiledChild ?? child).Evaluate(r);
                        if (value < best)
                        {
                            best = value;
                            shifted = r;
                        }
                    }
                }
            }
            double cap = Math.Min(Math.Min(Beyond(q.x, ix, period.x, boxLower.x, boxUpper.x, reachX + 1),
                                           Beyond(q.y, iy, period.y, boxLower.y, boxUpper.y, reachY + 1)),
                                  Beyond(q.z, iz, period.z, boxLower.z, boxUpper.z, reachZ + 1));
            if (cap < best)
            {
                capped = true;
                return cap;
            }
            return best;
        }

        // For the compiled form: the value at p when the cell's own copy may not be the nearest
        public double EvaluateNeighbours(double x, double y, double z)
        {
            return Nearest(new SdfVector(x, y, z), out SdfVector shifted, out bool capped);
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            Nearest(p, out SdfVector shifted, out bool capped);
            return capped ? Gradient(p, h) : child.AnalyticGradient(shifted, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The copies are the child moved by up to count periods; without a count there is no end to them
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            grows = false;
            if (count <= 0 && (period.x > 0 || period.y > 0 || period.z > 0))
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            double rx = period.x > 0 ? period.x * count + jitter : 0, ry = period.y > 0 ? period.y * count + jitter : 0;
            double rz = period.z > 0 ? period.z * count + jitter : 0;
            lower = new SdfVector(lower.x - rx, lower.y - ry, lower.z - rz);
            upper = new SdfVector(upper.x + rx, upper.y + ry, upper.z + rz);
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return Nearest(p, out SdfVector shifted, out bool capped);
        }

        public override double Lipschitz()
        {
            // The cap is a distance to a box
            return boxed ? Math.Max(child.Lipschitz(), 1) : child.Lipschitz();
        }

        // A child without a box (one without end) is only folded into its cell, which is a bound only if the copy of a cell is
        // the nearest one everywhere in the cell: not known, so not claimed
        public override bool IsRigorous()
        {
            return boxed && child.IsRigorous();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            // The search of the neighbours, which is a call, asks the child in its own compiled form
            if (compiledChild == null)
            {
                compiledChild = SdfCompiledNode.Compile(child);
            }
            Expression ix = Index(context, x, period.x), iy = Index(context, y, period.y), iz = Index(context, z, period.z);
            Expression nx = Shift(context, x, ix, period.x), ny = Shift(context, y, iy, period.y), nz = Shift(context, z, iz, period.z);
            Expression own;
            if (jitter == 0)
            {
                own = context.Compile(child, nx, ny, nz);
            }
            else
            {
                System.Reflection.MethodInfo moved = typeof(SdfRepeat).GetMethod("Jitter");
                Expression self = Expression.Constant(this);
                own = context.Compile(child, context.Define(context.Subtract(nx, Expression.Call(self, moved, ix, iy, iz, Expression.Constant(0)))),
                                      context.Define(context.Subtract(ny, Expression.Call(self, moved, ix, iy, iz, Expression.Constant(1)))),
                                      context.Define(context.Subtract(nz, Expression.Call(self, moved, ix, iy, iz, Expression.Constant(2)))));
            }
            if (!boxed)
            {
                return own;
            }
            // The cell's own copy where no other can be nearer, the search of the neighbours elsewhere
            Expression others = context.Define(context.Min(context.Min(Beyond(context, nx, ix, period.x, boxLower.x, boxUpper.x),
                                                                       Beyond(context, ny, iy, period.y, boxLower.y, boxUpper.y)),
                                                           Beyond(context, nz, iz, period.z, boxLower.z, boxUpper.z)));
            return Expression.Condition(Expression.LessThanOrEqual(own, others), own,
                                        Expression.Call(Expression.Constant(this), typeof(SdfRepeat).GetMethod("EvaluateNeighbours"), x, y, z));
        }

        private Expression Index(SdfCompileContext context, Expression value, double length)
        {
            if (length <= 0)
            {
                return context.Constant(0);
            }
            Expression index = context.Define(context.Round(context.Divide(value, context.Constant(length))));
            if (count > 0)
            {
                index = context.Define(context.Min(context.Max(index, context.Constant(-count)), context.Constant(count)));
            }
            return index;
        }

        private static Expression Shift(SdfCompileContext context, Expression value, Expression index, double length)
        {
            return length <= 0 ? value : context.Define(context.Subtract(value, context.Multiply(context.Constant(length), index)));
        }

        // Beyond(q, index, length, lower, upper, 1) as an expression
        private Expression Beyond(SdfCompileContext context, Expression q, Expression index, double length, double lower, double upper)
        {
            Expression none = context.Constant(double.PositiveInfinity);
            if (length <= 0)
            {
                return none;
            }
            Expression above = context.Max(context.Subtract(context.Constant(length + lower), q), context.Constant(0));
            Expression below = context.Max(context.Subtract(q, context.Constant(upper - length)), context.Constant(0));
            if (count > 0)
            {
                above = Expression.Condition(Expression.LessThan(index, context.Constant(count)), above, none);
                below = Expression.Condition(Expression.GreaterThan(index, context.Constant(-count)), below, none);
            }
            return context.Min(above, below);
        }

        private readonly SdfNode child;
        private readonly SdfVector period;
        private readonly int count;
        private readonly double jitter;
        private readonly int seed;
        private SdfNode compiledChild;
        private readonly bool boxed;
        private readonly SdfVector boxLower, boxUpper;
        private readonly int reachX, reachY, reachZ;
    }

    // Mirrors the field in the plane perpendicular to an axis (0, 1 or 2), keeping the positive side
    public sealed class SdfMirror : SdfNode
    {
        public SdfMirror(SdfNode child, int axis)
        {
            this.child = child;
            this.axis = axis;
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            SdfVector g = child.AnalyticGradient(new SdfVector(axis == 0 ? Math.Abs(p.x) : p.x, axis == 1 ? Math.Abs(p.y) : p.y,
                                                               axis == 2 ? Math.Abs(p.z) : p.z), h);
            return new SdfVector(axis == 0 && p.x < 0 ? -g.x : g.x, axis == 1 && p.y < 0 ? -g.y : g.y,
                                 axis == 2 && p.z < 0 ? -g.z : g.z);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            grows = false;
            double mx = axis == 0 ? Math.Max(upper.x, 0) : 0, my = axis == 1 ? Math.Max(upper.y, 0) : 0, mz = axis == 2 ? Math.Max(upper.z, 0) : 0;
            lower = new SdfVector(axis == 0 ? -mx : lower.x, axis == 1 ? -my : lower.y, axis == 2 ? -mz : lower.z);
            upper = new SdfVector(axis == 0 ? mx : upper.x, axis == 1 ? my : upper.y, axis == 2 ? mz : upper.z);
            return true;
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

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            double qx = p.x - Math.Min(Math.Max(p.x, -halfLength.x), halfLength.x);
            double qy = p.y - Math.Min(Math.Max(p.y, -halfLength.y), halfLength.y);
            double qz = p.z - Math.Min(Math.Max(p.z, -halfLength.z), halfLength.z);
            SdfVector g = child.AnalyticGradient(new SdfVector(qx, qy, qz), h);
            // A coordinate within the stretched part does not move the point that the child sees
            return new SdfVector(Math.Abs(p.x) < halfLength.x ? 0 : g.x, Math.Abs(p.y) < halfLength.y ? 0 : g.y,
                                 Math.Abs(p.z) < halfLength.z ? 0 : g.z);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            grows = false;
            lower = new SdfVector(lower.x - halfLength.x, lower.y - halfLength.y, lower.z - halfLength.z);
            upper = new SdfVector(upper.x + halfLength.x, upper.y + halfLength.y, upper.z + halfLength.z);
            return true;
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

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The noise of an octave is within [-1, 1]
            if (!child.Bounds(out lower, out upper, out grows) || !grows)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            double total = 0, weight = 1;
            for (int i = 0; i < octaves; ++i)
            {
                total += weight;
                weight *= roughness;
            }
            SdfExtent.Expand(ref lower, ref upper, Math.Abs(amplitude) * total);
            return true;
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(p) + Offset(p.x, p.y, p.z);
        }

        // What the noise adds to the child at a point
        public double Offset(double x, double y, double z)
        {
            double value = 0, weight = 1, scale = frequency;
            for (int i = 0; i < octaves; ++i)
            {
                value += weight * Sample(x * scale, y * scale, z * scale);
                weight *= roughness;
                scale *= 2;
            }
            return amplitude * value;
        }

        // The child is compiled; the noise is a call
        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Add(context.Compile(child, x, y, z),
                               Expression.Call(Expression.Constant(this), typeof(SdfNoiseDisplacement).GetMethod("Offset"), x, y, z));
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
