// (not in pbrt-v4) sdfphases.cs
//
// Multiphase signed distance fields (Galin, Hubert-Brierre, Schott, Cani, Peytavie and Guerin, "The PhaseTree: Multiphase Signed
// Distance Fields", 2026). A multiphase field gives one signed distance for each of its phases (its materials): phase i is the
// solid where its value is negative, at most one value is negative at a point, so the phases share the solid out between them
// without overlap, and each value is itself a bound of the distance to its phase. The operators keep these properties:
//
//   phase(a, m)                        the field a as the single phase m (the "set" of the paper)
//   under(A, B)                        the union in which the phases of A keep their volume and B fills what is left of its own
//   over(A, B)                         the same with B on top: under(B, A)
//   contact(a, b, ma, mb [, alpha])    the fields a and b as the phases ma and mb pressed together: where they overlap the
//                                      border between them is alpha a = (1 - alpha) b (alpha 0.5 by default; 1 is under, 0 over)
//   contactsmooth(a, b, ma, mb, r [, alpha])   the same with the two blended where they meet outside (the smooth union of the
//                                      authors' code, which is below the union by r at most)
//   layer(A, m, t)                     a coat of the thickness t around all of A as the new phase m
//   intersect(A, s)  subtract(A, s)    A cut by the ordinary field s (also A & s and A - s): every phase is cut
//   change(A, s, ma, mb)               the part of phase ma inside the field s becomes phase mb
//   remove(A, m)                       A without its phase m
//   shrink(A, r)                       every phase of A moved in by r, which opens a gap of 2 r between neighbours
//   checker(ma, mb [, size])           a field without end: cubes of the given size that alternate between two phases
//   merge(A [, m, ...])                back to an ordinary field: the union of the listed phases (all of them without a list);
//                                      one phase alone, merge(A, m), is that phase as a solid of its own
//
// A multiphase field is also an ordinary field, the union of its phases, so it can be the "string sdf" of a shape and an operand
// of every other function; the phase at a point is the label of the point (the label of the smallest value), which the "sdflabel"
// texture turns into a material.
//
// The nodes of this file are what the functions build with SdfParseContext.legacyPhases ("bool legacyphases"); without it they
// build the bundles of ordinary fields of sdfphasetree.cs, which is where the rest of the functions are listed. With the nodes
// of this file, moving or turning a multiphase field makes an ordinary field of it (place the parts first and give them their
// phase afterward), and phases are numbered from 0 to SdfPhaseNode.MaxPhases - 1.
//
// One solid inside another with a material each (a liquid in a glass), with the shapes and materials of pbrt as they are:
//   - one Shape "sdf" for each phase, "merge(A, m)", except that the inner one is moved in by a hair, "offset(merge(A, 1), -0.0002)"
//     for a solid of the size of 1: two shapes with the very same wall are hit at the same distance in either order, and the
//     tracer then steps over one of them. The hair is seen as a thin line where the free surface meets the wall: at 0.001 that
//     line stood out of the noise against a reference made by hand, at 0.0002 it did not;
//   - the outer shape takes a "mix" of its material against the outside and its material against the inner solid (a dielectric
//     of the ratio of the two indices), with Texture "sdftouch" of the inner phase as the amount;
//   - the inner shape takes a "mix" of its material against the outside and an "interface" material, with "sdftouch" of the outer
//     phase (and a tolerance above the hair) as the amount, so that only its free surface is seen.
// The value of a phase is also 0 on any surface of its operand that lies in the border of a phase above it, although the phase
// has no volume there: keep the operands from sharing a face (a liquid whose own bottom is the bottom of the glass).
//
// From the paper's code (MIT license) only the formulas of the operators are used; its checker with random phases and its wavy
// slabs are not here.

using System;
using System.Linq.Expressions;

namespace pbrt
{

    // SdfPhaseNode Definition
    public abstract class SdfPhaseNode : SdfNode
    {
        public const int MaxPhases = 8;

        // The values of the phases at p, MaxPhases of them; a phase that the field does not have is at plus infinity
        public abstract void Phases(SdfVector p, Span<double> values);

        // The compiled form: the value of each phase as an expression, null for a phase that the field does not have. Which
        // phases a field has is known when it is compiled, so only those are computed.
        public abstract Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z);

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression smallest = Smallest(context, CompilePhases(context, x, y, z), -1);
            return smallest ?? context.Constant(double.PositiveInfinity);
        }

        // The smallest of the phases but for one (-1 for all of them); null if there is none
        public static Expression Smallest(SdfCompileContext context, Expression[] values, int but)
        {
            Expression smallest = null;
            for (int i = 0; i < MaxPhases; ++i)
            {
                if (i != but && values[i] != null)
                {
                    smallest = smallest == null ? values[i] : context.Min(smallest, values[i]);
                }
            }
            return smallest;
        }

        // The union of the phases
        public override double Evaluate(SdfVector p)
        {
            Span<double> values = stackalloc double[MaxPhases];
            Phases(p, values);
            double smallest = values[0];
            for (int i = 1; i < MaxPhases; ++i)
            {
                smallest = Math.Min(smallest, values[i]);
            }
            return smallest;
        }

        // The phase at p: that of the smallest value
        public int PhaseAt(SdfVector p)
        {
            Span<double> values = stackalloc double[MaxPhases];
            Phases(p, values);
            int phase = 0;
            for (int i = 1; i < MaxPhases; ++i)
            {
                if (values[i] < values[phase])
                {
                    phase = i;
                }
            }
            return phase;
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            w.Add(PhaseAt(p), weight);
        }

        protected static void Clear(Span<double> values)
        {
            for (int i = 0; i < MaxPhases; ++i)
            {
                values[i] = double.PositiveInfinity;
            }
        }
    }

    // SdfPhaseSet Definition: an ordinary field as one phase
    public sealed class SdfPhaseSet : SdfPhaseNode
    {
        public SdfPhaseSet(SdfNode child, int phase)
        {
            this.child = child;
            this.phase = phase;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = new Expression[MaxPhases];
            values[phase] = context.Compile(child, x, y, z);
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            Clear(values);
            values[phase] = child.Evaluate(p);
        }

        public override double Evaluate(SdfVector p)
        {
            return child.Evaluate(p);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return child.Bounds(out lower, out upper, out grows);
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfNode child;
        private readonly int phase;
    }

    // SdfPhaseUnder Definition: the union of two multiphase fields in which a keeps its volume. A phase of the result is the
    // union of that phase in a and in b, less the other phases of a: max(min(a_k, b_k), -min of the other a_j).
    public sealed class SdfPhaseUnder : SdfPhaseNode
    {
        public SdfPhaseUnder(SdfPhaseNode a, SdfPhaseNode b)
        {
            this.a = a;
            this.b = b;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] va = a.CompilePhases(context, x, y, z), vb = b.CompilePhases(context, x, y, z);
            Expression[] values = new Expression[MaxPhases];
            for (int i = 0; i < MaxPhases; ++i)
            {
                if (va[i] == null && vb[i] == null)
                {
                    continue;
                }
                Expression same = va[i] == null ? vb[i] : (vb[i] == null ? va[i] : context.Min(va[i], vb[i]));
                Expression others = Smallest(context, va, i);
                values[i] = context.Define(others == null ? same : context.Max(same, context.Negate(others)));
            }
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            Span<double> va = stackalloc double[MaxPhases];
            a.Phases(p, va);
            b.Phases(p, values);
            // The smallest value of a and the smallest of its others give the union of the other phases for every phase
            int least = 0;
            for (int i = 1; i < MaxPhases; ++i)
            {
                if (va[i] < va[least])
                {
                    least = i;
                }
            }
            double second = double.PositiveInfinity;
            for (int i = 0; i < MaxPhases; ++i)
            {
                if (i != least)
                {
                    second = Math.Min(second, va[i]);
                }
            }
            for (int i = 0; i < MaxPhases; ++i)
            {
                values[i] = Math.Max(Math.Min(va[i], values[i]), -(i == least ? second : va[least]));
            }
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return SdfExtent.Merge(a, b, out lower, out upper, out grows);
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        private readonly SdfPhaseNode a, b;
    }

    // SdfPhaseContact Definition: two ordinary fields as two phases in contact. s = alpha a - (1 - alpha) b is negative on the
    // side of a; the phases are max(a, s) and max(b, -s), or with a blending radius max(u, s) and max(u, -s) for the smooth
    // union u of a and b.
    public sealed class SdfPhaseContact : SdfPhaseNode
    {
        public SdfPhaseContact(SdfNode a, SdfNode b, int phaseA, int phaseB, double alpha, double radius)
        {
            this.a = a;
            this.b = b;
            this.phaseA = phaseA;
            this.phaseB = phaseB;
            this.alpha = alpha;
            this.radius = radius;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression da = context.Compile(a, x, y, z), db = context.Compile(b, x, y, z);
            Expression separation = context.Define(context.Subtract(context.Multiply(context.Constant(alpha), da),
                                                                    context.Multiply(context.Constant(1 - alpha), db)));
            Expression[] values = new Expression[MaxPhases];
            if (radius > 0)
            {
                Expression union = context.Define(context.SmoothMin(da, db, 6 * radius, SdfBlendKind.Cubic));
                values[phaseA] = context.Define(context.Max(union, separation));
                values[phaseB] = context.Define(context.Max(union, context.Negate(separation)));
            }
            else
            {
                values[phaseA] = context.Define(context.Max(da, separation));
                values[phaseB] = context.Define(context.Max(db, context.Negate(separation)));
            }
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            double da = a.Evaluate(p), db = b.Evaluate(p);
            double separation = alpha * da - (1 - alpha) * db;
            Clear(values);
            if (radius > 0)
            {
                // The smooth union of the authors' code: the cubic blend over six times the radius, which dips by the radius at most
                double union = SdfBlend.SmoothMin(da, db, 6 * radius, SdfBlendKind.Cubic);
                values[phaseA] = Math.Max(union, separation);
                values[phaseB] = Math.Max(union, -separation);
            }
            else
            {
                values[phaseA] = Math.Max(da, separation);
                values[phaseB] = Math.Max(db, -separation);
            }
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!SdfExtent.Merge(a, b, out lower, out upper, out grows))
            {
                return false;
            }
            if (radius > 0)
            {
                // The blend is below the smaller field by at most the radius
                if (!grows)
                {
                    return SdfExtent.None(out lower, out upper, out grows);
                }
                SdfExtent.Expand(ref lower, ref upper, radius);
            }
            return true;
        }

        public override double Lipschitz()
        {
            return Math.Max(a.Lipschitz(), b.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        private readonly SdfNode a, b;
        private readonly int phaseA, phaseB;
        private readonly double alpha, radius;
    }

    // SdfPhaseLayer Definition: a coat of a thickness around a multiphase field as a new phase. With f the union of the
    // phases, the coat is the union grown by the thickness less the union itself: max(f - thickness, -f).
    public sealed class SdfPhaseLayer : SdfPhaseNode
    {
        public SdfPhaseLayer(SdfPhaseNode child, int phase, double thickness)
        {
            this.child = child;
            this.phase = phase;
            this.thickness = thickness;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = child.CompilePhases(context, x, y, z);
            Expression union = Smallest(context, values, phase);
            if (union == null)
            {
                return values;
            }
            union = context.Define(union);
            Expression coat = context.Max(context.Subtract(union, context.Constant(thickness)), context.Negate(union));
            values[phase] = context.Define(values[phase] == null ? coat : context.Min(values[phase], coat));
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            child.Phases(p, values);
            double union = double.PositiveInfinity;
            for (int i = 0; i < MaxPhases; ++i)
            {
                if (i != phase)
                {
                    union = Math.Min(union, values[i]);
                }
            }
            // A phase of the child with the number of the coat joins it
            values[phase] = Math.Min(values[phase], Math.Max(union - thickness, -union));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows) || !grows)
            {
                return SdfExtent.None(out lower, out upper, out grows);
            }
            SdfExtent.Expand(ref lower, ref upper, Math.Max(thickness, 0));
            return true;
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfPhaseNode child;
        private readonly int phase;
        private readonly double thickness;
    }

    // SdfPhaseCut Definition: a multiphase field cut by an ordinary one: every phase is intersected with it, or with its
    // outside for a subtraction
    public sealed class SdfPhaseCut : SdfPhaseNode
    {
        public SdfPhaseCut(SdfPhaseNode child, SdfNode cutter, bool subtract)
        {
            this.child = child;
            this.cutter = cutter;
            this.subtract = subtract;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = child.CompilePhases(context, x, y, z);
            Expression cut = context.Compile(cutter, x, y, z);
            if (subtract)
            {
                cut = context.Define(context.Negate(cut));
            }
            for (int i = 0; i < MaxPhases; ++i)
            {
                if (values[i] != null)
                {
                    values[i] = context.Define(context.Max(values[i], cut));
                }
            }
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            child.Phases(p, values);
            double cut = subtract ? -cutter.Evaluate(p) : cutter.Evaluate(p);
            for (int i = 0; i < MaxPhases; ++i)
            {
                values[i] = Math.Max(values[i], cut);
            }
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (subtract)
            {
                return child.Bounds(out lower, out upper, out grows);
            }
            return SdfExtent.Overlap(child, cutter, out lower, out upper, out grows);
        }

        public override double Lipschitz()
        {
            return Math.Max(child.Lipschitz(), cutter.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous() && cutter.IsRigorous();
        }

        private readonly SdfPhaseNode child;
        private readonly SdfNode cutter;
        private readonly bool subtract;
    }

    // SdfPhaseChange Definition: inside an ordinary field the phase `from` becomes the phase `to`: from loses the region,
    // max(from, -s), and to gains what from had in it, min(to, max(from, s))
    public sealed class SdfPhaseChange : SdfPhaseNode
    {
        public SdfPhaseChange(SdfPhaseNode child, SdfNode region, int from, int to)
        {
            this.child = child;
            this.region = region;
            this.from = from;
            this.to = to;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = child.CompilePhases(context, x, y, z);
            Expression before = values[from];
            if (before == null)
            {
                return values;
            }
            Expression inside = context.Compile(region, x, y, z);
            values[from] = context.Define(context.Max(before, context.Negate(inside)));
            Expression moved = context.Max(before, inside);
            values[to] = context.Define(values[to] == null ? moved : context.Min(values[to], moved));
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            child.Phases(p, values);
            double inside = region.Evaluate(p), before = values[from];
            if (double.IsPositiveInfinity(before))
            {
                return;
            }
            values[from] = Math.Max(before, -inside);
            values[to] = Math.Min(values[to], Math.Max(before, inside));
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return child.Bounds(out lower, out upper, out grows);
        }

        public override double Lipschitz()
        {
            return Math.Max(child.Lipschitz(), region.Lipschitz());
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous() && region.IsRigorous();
        }

        private readonly SdfPhaseNode child;
        private readonly SdfNode region;
        private readonly int from, to;
    }

    // SdfPhaseAdjust Definition: a multiphase field without one of its phases (phase >= 0), and with every phase moved in by a
    // distance
    public sealed class SdfPhaseAdjust : SdfPhaseNode
    {
        public SdfPhaseAdjust(SdfPhaseNode child, int removed, double inward)
        {
            this.child = child;
            this.removed = removed;
            this.inward = inward;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = child.CompilePhases(context, x, y, z);
            if (removed >= 0)
            {
                values[removed] = null;
            }
            for (int i = 0; i < MaxPhases && inward != 0; ++i)
            {
                if (values[i] != null)
                {
                    values[i] = context.Define(context.Add(values[i], context.Constant(inward)));
                }
            }
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            child.Phases(p, values);
            if (removed >= 0)
            {
                values[removed] = double.PositiveInfinity;
            }
            for (int i = 0; i < MaxPhases; ++i)
            {
                values[i] += inward;
            }
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            if (!child.Bounds(out lower, out upper, out grows))
            {
                return false;
            }
            if (inward < 0)
            {
                // Moved outward
                if (!grows)
                {
                    return SdfExtent.None(out lower, out upper, out grows);
                }
                SdfExtent.Expand(ref lower, ref upper, -inward);
            }
            return true;
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfPhaseNode child;
        private readonly int removed;
        private readonly double inward;
    }

    // SdfPhaseChecker Definition: cubes of a size that alternate between two phases, without end. The value is the distance to
    // the closest face of the cube of the point, negative for the phase of the cube.
    public sealed class SdfPhaseChecker : SdfPhaseNode
    {
        public SdfPhaseChecker(int phaseA, int phaseB, double size)
        {
            this.phaseA = phaseA;
            this.phaseB = phaseB;
            this.size = size;
        }

        public override Expression[] CompilePhases(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression scale = context.Constant(1 / size);
            Expression sx = context.Define(context.Multiply(x, scale)), sy = context.Define(context.Multiply(y, scale));
            Expression sz = context.Define(context.Multiply(z, scale));
            Expression fx = context.Define(context.Floor(sx)), fy = context.Define(context.Floor(sy)), fz = context.Define(context.Floor(sz));
            Expression qx = context.Define(context.Subtract(sx, fx)), qy = context.Define(context.Subtract(sy, fy));
            Expression qz = context.Define(context.Subtract(sz, fz));
            Expression one = context.Constant(1);
            Expression nearest = context.Min(context.Min(context.Min(qx, context.Subtract(one, qx)), context.Min(qy, context.Subtract(one, qy))),
                                             context.Min(qz, context.Subtract(one, qz)));
            Expression distance = context.Define(context.Multiply(nearest, context.Constant(size)));
            // 0 for an even cube, 1 for an odd one: the sum of the three whole numbers less twice the floor of its half
            Expression sum = context.Define(context.Add(context.Add(fx, fy), fz));
            Expression odd = context.Subtract(sum, context.Multiply(context.Constant(2), context.Floor(context.Multiply(sum, context.Constant(0.5)))));
            // -distance in an even cube, +distance in an odd one
            Expression first = context.Define(context.Multiply(context.Subtract(context.Multiply(context.Constant(2), odd), one), distance));
            Expression[] values = new Expression[MaxPhases];
            values[phaseA] = first;
            values[phaseB] = context.Define(context.Negate(first));
            return values;
        }

        public override void Phases(SdfVector p, Span<double> values)
        {
            double x = p.x / size, y = p.y / size, z = p.z / size;
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            double qx = x - fx, qy = y - fy, qz = z - fz;
            double distance = Math.Min(Math.Min(Math.Min(qx, 1 - qx), Math.Min(qy, 1 - qy)), Math.Min(qz, 1 - qz)) * size;
            bool even = (((long)fx + (long)fy + (long)fz) & 1) == 0;
            Clear(values);
            values[phaseA] = even ? -distance : distance;
            values[phaseB] = even ? distance : -distance;
        }

        private readonly int phaseA, phaseB;
        private readonly double size;
    }

    // SdfPhaseMerge Definition: the union of some of the phases of a multiphase field as an ordinary field; one phase alone is
    // that phase as a solid of its own, which the paper shows to be a bound of the distance to it
    public sealed class SdfPhaseMerge : SdfNode
    {
        public SdfPhaseMerge(SdfPhaseNode child, bool[] chosen)
        {
            this.child = child;
            this.chosen = chosen;
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression[] values = child.CompilePhases(context, x, y, z);
            Expression smallest = null;
            for (int i = 0; i < SdfPhaseNode.MaxPhases; ++i)
            {
                if (chosen[i] && values[i] != null)
                {
                    smallest = smallest == null ? values[i] : context.Min(smallest, values[i]);
                }
            }
            return smallest ?? context.Constant(double.PositiveInfinity);
        }

        public override double Evaluate(SdfVector p)
        {
            Span<double> values = stackalloc double[SdfPhaseNode.MaxPhases];
            child.Phases(p, values);
            double smallest = double.PositiveInfinity;
            for (int i = 0; i < SdfPhaseNode.MaxPhases; ++i)
            {
                if (chosen[i])
                {
                    smallest = Math.Min(smallest, values[i]);
                }
            }
            return smallest;
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            // The box of all of the phases holds some of them; their field is not below that of all of them
            return child.Bounds(out lower, out upper, out grows);
        }

        public override double Lipschitz()
        {
            return child.Lipschitz();
        }

        public override bool IsRigorous()
        {
            return child.IsRigorous();
        }

        private readonly SdfPhaseNode child;
        private readonly bool[] chosen;
    }

    // Texture "sdftouch": 1 where the point is on the surface of a field (within a tolerance), 0 elsewhere. With one phase of a
    // multiphase field as the shape and a neighbouring phase as the field of this texture, it tells the part of the surface that
    // the two phases share from the part that faces the outside, as the amount of a mix material: what a solid inside another
    // (a liquid in a glass) needs to have the right material on either part.
    public sealed class SdfTouchTexture : FloatTexture
    {
        public SdfTouchTexture(Transform renderFromTexture, SdfNode field, double tolerance)
        {
            this.renderFromTexture = renderFromTexture;
            this.field = field;
            this.tolerance = tolerance;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = renderFromTexture.ApplyInverse(ctx.p);
            return Math.Abs(field.Evaluate(new SdfVector(p.x, p.y, p.z))) <= tolerance ? 1 : 0;
        }

        public override Float MinValue()
        {
            return 0;
        }

        public override Float MaxValue()
        {
            return 1;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SdfTouchTexture tolerance: %f ]", tolerance);
        }

        public static SdfTouchTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                             Allocator alloc)
        {
            string expression = parameters.GetOneString("sdf", "");
            if (expression.Length == 0)
            {
                pbrt.ErrorExit(loc, "sdftouch texture: the \"string sdf\" expression is required.");
            }
            SdfParseContext context = new SdfParseContext();
            context.legacyPhases = parameters.GetOneBool("legacyphases", false);
            context.domainRadius = parameters.GetOneFloat("domainradius", 2);
            context.meshLoader = SdfMeshLoader.Load;
            context.vdbLoader = SdfVdbLoader.Load;
            return new SdfTouchTexture(renderFromTexture, SdfParser.Parse(expression, context), parameters.GetOneFloat("tolerance", 1e-3f));
        }

        private readonly Transform renderFromTexture;
        private readonly SdfNode field;
        private readonly double tolerance;
    }
}
