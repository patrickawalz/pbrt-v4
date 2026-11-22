// (not in pbrt-v4) sdfphasetree.cs
//
// Multiphase fields as bundles of ordinary fields. The operators of sdfphases.cs (Galin et al., "The PhaseTree: Multiphase
// Signed Distance Fields", 2026) each compute all the phases of their result in a node of their own, so nothing that the
// ordinary nodes can do (the fields of the cells, proxies, gradients in closed form, culled unions, the bounds along a ray,
// the transforms and the warps) reaches below them. Here a multiphase field is instead:
//
//   - one ordinary field for the solid of all its phases, in the cheapest form that has that solid: what a shape made of the
//     whole field marches, with everything an ordinary field has;
//   - one ordinary field for each phase, built from the nodes of sdf.cs by the formulas of the paper's operators, so that
//     merge(A, m), one phase as a solid of its own, is an ordinary field too. The fields of a bundle share their parts, and
//     the compiled evaluator computes a shared part once (SdfCompileContext.Compile).
//
// A bundle is in one of two states. Its fields are either the phases themselves (each negative in its own phase only), or,
// after an operator that changes the solid without saying whose the new part is (an offset, a displacement, a blend with
// an ordinary field or with another bundle), fields of which the smallest tells the phase at a point of the solid: the
// material that such an operator adds goes to the phase that is nearest. Phase(m) gives the phase itself in either state:
// in the second one the solid cut along the borders where two fields are equal, (F_m - min of the others) / 2 < 0.
//
// What this adds to the list of sdfphases.cs (the functions are the same, and SdfParseContext.legacyPhases brings the old
// nodes back):
//   - any number of phases;
//   - translate, rotate, rotateaxis, scale, twist, bend, repeat, mirror, elongate (and translate_t, rotate_t) of a multiphase
//     field keep its phases; so do offset, shell, displace, inset, texdisplace and displace_t, with the nearest phase for what
//     they add;
//   - union(A, B) is under(A, B); intersect(A, B) and subtract(A, B) cut the phases of A by the solid of B; xor(A, B) keeps the
//     phases of each outside the other; union, xor and smoothunion with an ordinary field keep the phases of the multiphase
//     one, and the ordinary field takes the nearest phase; smoothunion(A, B, ...) is under(A, B) with the blend as the solid;
//     smoothintersect and smoothsubtract cut the solid with the blend, and what is left keeps its phases; all with every blend
//     the ordinary functions take;
//   - contactsmooth takes the name of a blend as a seventh argument (the radius is then the width of that blend).
// subtract(s, A) and smoothsubtract(s, A, ...) of an ordinary field s have no phase left and stay ordinary, as does a
// multiphase field under any other function (it is then its solid, with its phases as labels).

using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace pbrt
{

    // SdfPhaseSeparation Definition: alpha a - (1 - alpha) b, negative where a is the smaller of the two for alpha = 1/2: the
    // border between two phases in contact, and the border between a phase and the nearest of the others
    public sealed class SdfPhaseSeparation : SdfNode
    {
        public SdfPhaseSeparation(SdfNode a, SdfNode b, double alpha)
        {
            this.a = a;
            this.b = b;
            this.alpha = alpha;
        }

        public override double Evaluate(SdfVector p)
        {
            return alpha * a.Evaluate(p) - (1 - alpha) * b.Evaluate(p);
        }

        public override double Lipschitz()
        {
            return alpha * a.Lipschitz() + (1 - alpha) * b.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return alpha * a.Lipschitz(center, radius) + (1 - alpha) * b.Lipschitz(center, radius);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return alpha * a.RayLipschitz(from, to) + (1 - alpha) * b.RayLipschitz(from, to);
        }

        public override bool HasLocalBounds()
        {
            return a.HasLocalBounds() || b.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return a.IsRigorous() && b.IsRigorous();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return a.AnalyticGradient(p, h) * alpha - b.AnalyticGradient(p, h) * (1 - alpha);
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            SdfNode pa = a.Prune(center, radius, out double la, out double ua), pb = b.Prune(center, radius, out double lb, out double ub);
            lower = alpha * la - (1 - alpha) * ub;
            upper = alpha * ua - (1 - alpha) * lb;
            return pa == a && pb == b ? this : new SdfPhaseSeparation(pa, pb, alpha);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            key.Append("D|").Append(alpha.ToString("R")).Append('(');
            a.Signature(key);
            key.Append(',');
            b.Signature(key);
            key.Append(')');
        }

        public override int NodeCount()
        {
            return 1 + a.NodeCount() + b.NodeCount();
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            Expression da = context.Compile(a, x, y, z), db = context.Compile(b, x, y, z);
            return context.Subtract(context.Multiply(context.Constant(alpha), da), context.Multiply(context.Constant(1 - alpha), db));
        }

        private readonly SdfNode a, b;
        private readonly double alpha;
    }

    // SdfPhaseTree Definition: a multiphase field as the field of its solid and a field for each phase. As an ordinary field
    // it is its solid, and the label of a point is its phase.
    public sealed class SdfPhaseTree : SdfNode
    {
        // SdfPhaseTree Public Methods
        public SdfPhaseTree(SdfNode solid, SdfNode[] fields, bool partitioned)
        {
            this.solid = solid;
            this.fields = fields;
            this.partitioned = partitioned;
            phases = new SdfNode[fields.Length];
        }

        // One more than the largest number of a phase
        public int Count()
        {
            return fields.Length;
        }

        public SdfNode Solid()
        {
            return solid;
        }

        // The field of a phase as the bundle keeps it (null for a phase that the field does not have)
        public SdfNode Field(int phase)
        {
            return phase < fields.Length ? fields[phase] : null;
        }

        // Whether the fields only tell the phase by which of them is the smallest
        public bool Partitioned()
        {
            return partitioned;
        }

        // The phase as a field of its own: negative in the phase and nowhere else, and a bound of the distance to it
        public SdfNode Phase(int phase)
        {
            if (phase >= fields.Length || fields[phase] == null)
            {
                return null;
            }
            if (!partitioned)
            {
                return fields[phase];
            }
            lock (phases)
            {
                if (phases[phase] == null)
                {
                    SdfNode others = null;
                    for (int i = 0; i < fields.Length; ++i)
                    {
                        if (i != phase && fields[i] != null)
                        {
                            others = others == null ? fields[i] : new SdfUnion(others, fields[i]);
                        }
                    }
                    phases[phase] = others == null ? solid : new SdfIntersection(solid, new SdfPhaseSeparation(fields[phase], others, 0.5));
                }
                return phases[phase];
            }
        }

        // The phase at p: that of the smallest field
        public int PhaseAt(SdfVector p)
        {
            int phase = 0;
            double smallest = double.PositiveInfinity;
            for (int i = 0; i < fields.Length; ++i)
            {
                if (fields[i] != null)
                {
                    double value = fields[i].Evaluate(p);
                    if (value < smallest)
                    {
                        smallest = value;
                        phase = i;
                    }
                }
            }
            return phase;
        }

        public override void Weights(SdfVector p, SdfWeights w, double weight)
        {
            w.Add(PhaseAt(p), weight);
        }

        public override double Evaluate(SdfVector p)
        {
            return solid.Evaluate(p);
        }

        public override Expression Compile(SdfCompileContext context, Expression x, Expression y, Expression z)
        {
            return context.Compile(solid, x, y, z);
        }

        public override double Lipschitz()
        {
            return solid.Lipschitz();
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return solid.Lipschitz(center, radius);
        }

        public override double RayLipschitz(SdfVector from, SdfVector to)
        {
            return solid.RayLipschitz(from, to);
        }

        public override bool HasLocalBounds()
        {
            return solid.HasLocalBounds();
        }

        public override bool IsRigorous()
        {
            return solid.IsRigorous();
        }

        public override bool IsExactOutside()
        {
            return solid.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return solid.IsExactInside();
        }

        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            return solid.AnalyticGradient(p, h);
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return solid.Bounds(out lower, out upper, out grows);
        }

        // The field of a cell is that of the solid: the phases are asked of the whole field
        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            return solid.Prune(center, radius, out lower, out upper);
        }

        public override void Signature(System.Text.StringBuilder key)
        {
            solid.Signature(key);
        }

        public override int NodeCount()
        {
            return solid.NodeCount();
        }

        public override SdfNode WithProxies(int threshold, double blend)
        {
            SdfNode proxied = solid.WithProxies(threshold, blend);
            return proxied == solid ? this : new SdfPhaseTree(proxied, fields, partitioned);
        }

        // SdfPhaseTree Private Members
        private readonly SdfNode solid;
        private readonly SdfNode[] fields;
        private readonly bool partitioned;
        private readonly SdfNode[] phases;
    }

    // SdfPhaseOps Definition: the functions of the parser that take or make a multiphase field. `build` is the parser's own
    // maker of ordinary nodes (name, arguments, without warnings), which is how an ordinary function is applied to the solid
    // and to each phase with all the arguments and checks it has; `fail` reports an error and does not return.
    public static class SdfPhaseOps
    {
        // The node for the function, or null if the function is not one for multiphase fields with these arguments
        public static SdfNode Call(string name, List<object> a, Func<string, List<object>, bool, SdfNode> build, Action<string> fail)
        {
            bool first = a.Count > 0 && a[0] is SdfPhaseTree, second = a.Count > 1 && a[1] is SdfPhaseTree;
            switch (name)
            {
                case "phase":
                    {
                        Count(a, name, 2, 2, fail);
                        int phase = Index(a, 1, name, fail);
                        SdfNode child = Shape(a, 0, name, fail);
                        SdfNode[] fields = new SdfNode[phase + 1];
                        fields[phase] = child;
                        return new SdfPhaseTree(child, fields, false);
                    }
                case "under":
                    Count(a, name, 2, 2, fail);
                    return Under(Tree(a, 0, name, fail), Tree(a, 1, name, fail));
                case "over":
                    Count(a, name, 2, 2, fail);
                    return Under(Tree(a, 1, name, fail), Tree(a, 0, name, fail));
                case "contact":
                case "contactsmooth":
                    return Contact(name, a, build, fail);
                case "layer":
                    Count(a, name, 3, 3, fail);
                    return Layer(Tree(a, 0, name, fail), Index(a, 1, name, fail), Number(a, 2, name, fail));
                case "change":
                    Count(a, name, 4, 4, fail);
                    if (Index(a, 2, name, fail) == Index(a, 3, name, fail))
                    {
                        fail("change: two different phases");
                    }
                    return Change(Tree(a, 0, name, fail), Shape(a, 1, name, fail), Index(a, 2, name, fail), Index(a, 3, name, fail));
                case "remove":
                    Count(a, name, 2, 2, fail);
                    return Adjust(Tree(a, 0, name, fail), Index(a, 1, name, fail), 0, fail);
                case "shrink":
                    Count(a, name, 2, 2, fail);
                    return Adjust(Tree(a, 0, name, fail), -1, Number(a, 1, name, fail), fail);
                case "checker":
                    {
                        Count(a, name, 2, 3, fail);
                        int phaseA = Index(a, 0, name, fail), phaseB = Index(a, 1, name, fail);
                        double size = a.Count > 2 ? Number(a, 2, name, fail) : 1;
                        if (phaseA == phaseB || size <= 0)
                        {
                            fail("checker: two different phases and a size above 0");
                        }
                        // The cubes of sdfphases.cs, one field for the even ones and one for the odd ones
                        SdfPhaseChecker cubes = new SdfPhaseChecker(0, 1, size);
                        bool[] even = new bool[SdfPhaseNode.MaxPhases], odd = new bool[SdfPhaseNode.MaxPhases];
                        even[0] = true;
                        odd[1] = true;
                        SdfNode[] fields = new SdfNode[Math.Max(phaseA, phaseB) + 1];
                        fields[phaseA] = new SdfPhaseMerge(cubes, even);
                        fields[phaseB] = new SdfPhaseMerge(cubes, odd);
                        return new SdfPhaseTree(new SdfUnion(fields[phaseA], fields[phaseB]), fields, false);
                    }
                case "merge":
                    {
                        if (a.Count < 1)
                        {
                            fail("merge takes a multiphase field and the phases to keep");
                        }
                        SdfPhaseTree tree = Tree(a, 0, name, fail);
                        if (a.Count == 1)
                        {
                            return tree.Solid();
                        }
                        bool[] chosen = new bool[tree.Count()];
                        SdfNode merged = null;
                        for (int i = 1; i < a.Count; ++i)
                        {
                            int phase = Index(a, i, name, fail);
                            if (tree.Phase(phase) != null && !chosen[phase])
                            {
                                chosen[phase] = true;
                                merged = merged == null ? tree.Phase(phase) : new SdfUnion(merged, tree.Phase(phase));
                            }
                        }
                        if (merged == null)
                        {
                            fail("merge: the field has none of these phases");
                        }
                        return merged;
                    }
                case "union":
                    if (first && second)
                    {
                        Count(a, name, 2, 2, fail);
                        return Under((SdfPhaseTree)a[0], (SdfPhaseTree)a[1]);
                    }
                    return first || second ? Join(name, a, build) : null;
                case "xor":
                    if (first && second)
                    {
                        Count(a, name, 2, 2, fail);
                        SdfPhaseTree left = (SdfPhaseTree)a[0], right = (SdfPhaseTree)a[1];
                        return Under(Cut(left, right.Solid(), "subtract", a, build), Cut(right, left.Solid(), "subtract", a, build));
                    }
                    return first || second ? Join(name, a, build) : null;
                case "smoothunion":
                    if (first && second)
                    {
                        // The phases of under(A, B) tell whose a point is; the blend is the solid
                        SdfPhaseTree under = Under((SdfPhaseTree)a[0], (SdfPhaseTree)a[1]);
                        List<object> arguments = new List<object>(a);
                        arguments[0] = ((SdfPhaseTree)a[0]).Solid();
                        arguments[1] = ((SdfPhaseTree)a[1]).Solid();
                        SdfNode[] fields = new SdfNode[under.Count()];
                        for (int i = 0; i < fields.Length; ++i)
                        {
                            fields[i] = under.Field(i);
                        }
                        return new SdfPhaseTree(build(name, arguments, false), fields, true);
                    }
                    return first || second ? Join(name, a, build) : null;
                case "intersect":
                case "smoothintersect":
                    if (first)
                    {
                        return Cut((SdfPhaseTree)a[0], second ? ((SdfPhaseTree)a[1]).Solid() : Shape(a, 1, name, fail), name, a, build);
                    }
                    return second ? Cut((SdfPhaseTree)a[1], Shape(a, 0, name, fail), name, a, build) : null;
                case "subtract":
                case "smoothsubtract":
                    // An ordinary field less a multiphase one has no phases
                    return first ? Cut((SdfPhaseTree)a[0], second ? ((SdfPhaseTree)a[1]).Solid() : Shape(a, 1, name, fail), name, a, build) : null;
                case "translate":
                case "rotate":
                case "rotateaxis":
                case "scale":
                case "twist":
                case "bend":
                case "repeat":
                case "mirror":
                case "elongate":
                case "translate_t":
                case "rotate_t":
                    return first ? Remap((SdfPhaseTree)a[0], name, a, build) : null;
                case "offset":
                case "shell":
                case "displace":
                case "inset":
                case "texdisplace":
                case "displace_t":
                    {
                        if (!first)
                        {
                            return null;
                        }
                        SdfPhaseTree tree = (SdfPhaseTree)a[0];
                        return new SdfPhaseTree(Apply(name, a, tree.Solid(), false, build), Fields(tree), true);
                    }
                default:
                    return null;
            }
        }

        // The union in which the phases of a keep their volume: a phase is that phase of a and what b has of it outside of a
        private static SdfPhaseTree Under(SdfPhaseTree a, SdfPhaseTree b)
        {
            SdfNode[] fields = new SdfNode[Math.Max(a.Count(), b.Count())];
            for (int i = 0; i < fields.Length; ++i)
            {
                SdfNode kept = a.Phase(i), filled = b.Phase(i) == null ? null : new SdfSubtraction(b.Phase(i), a.Solid());
                fields[i] = kept == null ? filled : (filled == null ? kept : new SdfUnion(kept, filled));
            }
            return new SdfPhaseTree(new SdfUnion(a.Solid(), b.Solid()), fields, false);
        }

        // Two ordinary fields as two phases pressed together: each one, or their blend, on its side of alpha a = (1 - alpha) b
        private static SdfPhaseTree Contact(string name, List<object> a, Func<string, List<object>, bool, SdfNode> build, Action<string> fail)
        {
            bool smooth = name == "contactsmooth";
            Count(a, name, smooth ? 5 : 4, smooth ? 7 : 5, fail);
            SdfNode first = Shape(a, 0, name, fail), second = Shape(a, 1, name, fail);
            int phaseA = Index(a, 2, name, fail), phaseB = Index(a, 3, name, fail);
            double radius = smooth ? Number(a, 4, name, fail) : 0;
            double alpha = a.Count > (smooth ? 5 : 4) ? Number(a, smooth ? 5 : 4, name, fail) : 0.5;
            if (alpha < 0 || alpha > 1 || phaseA == phaseB || (smooth && radius <= 0))
            {
                fail(name + ": two different phases" + (smooth ? ", a radius above 0" : "") + " and an alpha from 0 to 1");
            }
            SdfNode blend = null;
            if (smooth && a.Count > 6)
            {
                blend = build("smoothunion", new List<object> { first, second, radius, a[6] }, false);
            }
            else if (smooth)
            {
                // The smooth union of the authors' code: the cubic blend over six times the radius, which dips by the radius at most
                blend = new SdfSmoothCombine(first, second, 6 * radius, SdfBlendKind.Cubic, 0);
            }
            SdfNode[] fields = new SdfNode[Math.Max(phaseA, phaseB) + 1];
            fields[phaseA] = new SdfIntersection(blend ?? first, new SdfPhaseSeparation(first, second, alpha));
            fields[phaseB] = new SdfIntersection(blend ?? second, new SdfPhaseSeparation(second, first, 1 - alpha));
            return new SdfPhaseTree(blend ?? new SdfUnion(first, second), fields, false);
        }

        // A coat of a thickness around the other phases as the phase `phase`: those phases grown by the thickness, less themselves
        private static SdfPhaseTree Layer(SdfPhaseTree tree, int phase, double thickness)
        {
            SdfNode[] fields = new SdfNode[Math.Max(tree.Count(), phase + 1)];
            SdfNode others = null;
            for (int i = 0; i < tree.Count(); ++i)
            {
                fields[i] = tree.Phase(i);
                if (i != phase && fields[i] != null)
                {
                    others = others == null ? fields[i] : new SdfUnion(others, fields[i]);
                }
            }
            if (others == null)
            {
                return tree;
            }
            if (fields[phase] == null)
            {
                // The others are the whole solid
                others = tree.Solid();
            }
            SdfNode grown = new SdfOffset(others, thickness);
            SdfNode coat = new SdfSubtraction(grown, others);
            SdfNode solid = fields[phase] == null ? grown : new SdfUnion(grown, fields[phase]);
            fields[phase] = fields[phase] == null ? coat : new SdfUnion(fields[phase], coat);
            return new SdfPhaseTree(solid, fields, false);
        }

        // The part of the phase `from` inside an ordinary field becomes the phase `to`
        private static SdfPhaseTree Change(SdfPhaseTree tree, SdfNode region, int from, int to)
        {
            SdfNode before = tree.Phase(from);
            if (before == null)
            {
                return tree;
            }
            SdfNode[] fields = new SdfNode[Math.Max(tree.Count(), to + 1)];
            for (int i = 0; i < tree.Count(); ++i)
            {
                fields[i] = tree.Phase(i);
            }
            SdfNode moved = new SdfIntersection(before, region);
            fields[from] = new SdfSubtraction(before, region);
            fields[to] = fields[to] == null ? moved : new SdfUnion(fields[to], moved);
            return new SdfPhaseTree(tree.Solid(), fields, false);
        }

        // The field without one of its phases (removed >= 0), with every phase moved in by a distance
        private static SdfPhaseTree Adjust(SdfPhaseTree tree, int removed, double inward, Action<string> fail)
        {
            SdfNode[] fields = new SdfNode[tree.Count()];
            SdfNode solid = null;
            for (int i = 0; i < fields.Length; ++i)
            {
                if (i == removed || tree.Phase(i) == null)
                {
                    continue;
                }
                fields[i] = inward == 0 ? tree.Phase(i) : new SdfOffset(tree.Phase(i), -inward);
                solid = solid == null ? fields[i] : new SdfUnion(solid, fields[i]);
            }
            if (solid == null)
            {
                fail("remove: no phase is left");
            }
            return new SdfPhaseTree(solid, fields, false);
        }

        // intersect, subtract, smoothintersect or smoothsubtract of every phase with an ordinary field (the arguments after
        // the two fields are those of the function)
        private static SdfPhaseTree Cut(SdfPhaseTree tree, SdfNode cutter, string name, List<object> a, Func<string, List<object>, bool, SdfNode> build)
        {
            List<object> arguments = new List<object>(a);
            arguments[1] = cutter;
            arguments[0] = tree.Solid();
            SdfNode solid = build(name, arguments, false);
            if (tree.Partitioned() || name.StartsWith("smooth"))
            {
                // The same fields tell the phase in what is left of the solid. A smooth cut reads how deep a point is, and
                // the field of the solid and the fields of its phases do not agree on that: cut phase by phase, the rounded
                // edge of each phase would stop short of the rounded edge of the solid.
                return new SdfPhaseTree(solid, Fields(tree), true);
            }
            SdfNode[] fields = new SdfNode[tree.Count()];
            for (int i = 0; i < fields.Length; ++i)
            {
                if (tree.Field(i) != null)
                {
                    arguments = new List<object>(a);
                    arguments[1] = cutter;
                    arguments[0] = tree.Field(i);
                    fields[i] = build(name, arguments, true);
                }
            }
            return new SdfPhaseTree(solid, fields, false);
        }

        // union, xor or smoothunion of a multiphase field and an ordinary one: the ordinary function for the solid, and the
        // fields of the multiphase one to tell the phase, the nearest one in what the ordinary field adds
        private static SdfPhaseTree Join(string name, List<object> a, Func<string, List<object>, bool, SdfNode> build)
        {
            int index = a[0] is SdfPhaseTree ? 0 : 1;
            SdfPhaseTree tree = (SdfPhaseTree)a[index];
            List<object> arguments = new List<object>(a);
            arguments[index] = tree.Solid();
            return new SdfPhaseTree(build(name, arguments, false), Fields(tree), true);
        }

        // A function that moves, turns, scales, warps or repeats: applied to the solid and to each field alike
        private static SdfPhaseTree Remap(SdfPhaseTree tree, string name, List<object> a, Func<string, List<object>, bool, SdfNode> build)
        {
            SdfNode[] fields = new SdfNode[tree.Count()];
            for (int i = 0; i < fields.Length; ++i)
            {
                if (tree.Field(i) != null)
                {
                    fields[i] = Apply(name, a, tree.Field(i), true, build);
                }
            }
            return new SdfPhaseTree(Apply(name, a, tree.Solid(), false, build), fields, tree.Partitioned());
        }

        // The function with an ordinary field in the place of its first argument
        private static SdfNode Apply(string name, List<object> a, SdfNode child, bool quiet, Func<string, List<object>, bool, SdfNode> build)
        {
            List<object> arguments = new List<object>(a);
            arguments[0] = child;
            return build(name, arguments, quiet);
        }

        private static SdfNode[] Fields(SdfPhaseTree tree)
        {
            SdfNode[] fields = new SdfNode[tree.Count()];
            for (int i = 0; i < fields.Length; ++i)
            {
                fields[i] = tree.Field(i);
            }
            return fields;
        }

        private static void Count(List<object> a, string name, int least, int most, Action<string> fail)
        {
            if (a.Count < least || a.Count > most)
            {
                fail(name + " takes " + (least == most ? least.ToString() : least + " to " + most) + " arguments");
            }
        }

        private static double Number(List<object> a, int index, string name, Action<string> fail)
        {
            if (index >= a.Count || !(a[index] is double))
            {
                fail(name + ": argument " + (index + 1) + " must be a number");
            }
            return (double)a[index];
        }

        private static int Index(List<object> a, int index, string name, Action<string> fail)
        {
            double value = Number(a, index, name, fail);
            if (value != Math.Floor(value) || value < 0 || value > 4095)
            {
                fail(name + ": a phase is a whole number from 0 up");
            }
            return (int)value;
        }

        private static SdfNode Shape(List<object> a, int index, string name, Action<string> fail)
        {
            if (index >= a.Count || !(a[index] is SdfNode))
            {
                fail(name + ": argument " + (index + 1) + " must be a shape");
            }
            return (SdfNode)a[index];
        }

        private static SdfPhaseTree Tree(List<object> a, int index, string name, Action<string> fail)
        {
            if (index >= a.Count || !(a[index] is SdfPhaseTree))
            {
                fail(name + ": argument " + (index + 1) + " must be a multiphase field (phase, under, over, contact, layer, ...)");
            }
            return (SdfPhaseTree)a[index];
        }
    }
}
