// (not in pbrt-v4) sdfcompile.cs
//
// A compiled evaluator for signed distance fields (see sdf.cs). The tree of nodes that the parser builds is interpreted by one virtual
// call per node and one SdfVector per edge; here the whole tree is turned into one System.Linq.Expressions lambda of three doubles
// (and so into one block of JIT-compiled machine code with the constants of the nodes folded into it, and no virtual calls between
// nodes). A node that has no compiled form (the fractals, the noise displacement, the cone, the mesh and so on) stays a call of its own
// Evaluate from inside the compiled code, so every tree compiles and the compiled field computes the same values as the interpreted
// one (the operations are the same, in the same order).
//
// Where it is used: Shape "sdf" and Texture "sdf" compile their field unless "bool compile" false is given.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace pbrt
{

    // SdfCompileContext Definition
    public sealed class SdfCompileContext
    {
        // SdfCompileContext Public Methods
        public SdfCompileContext()
        {
            variables = new List<ParameterExpression>();
            statements = new List<Expression>();
            shared = new List<Dictionary<(SdfNode, Expression, Expression, Expression), Expression>>();
            shared.Add(new Dictionary<(SdfNode, Expression, Expression, Expression), Expression>());
            defined = new List<Dictionary<Expression, Expression>>();
            defined.Add(new Dictionary<Expression, Expression>());
            stored = new HashSet<ParameterExpression>();
            changing = new HashSet<ParameterExpression>();
            constants = new Dictionary<long, Expression>();
            built = new Dictionary<(string, Expression, Expression), Expression>();
        }

        // Compiles a child at the point (x, y, z); the result is a variable that holds the value of the child
        // A node that is a part of several others (the fields of the phases of a multiphase field, sdfphasetree.cs) is computed
        // once at a point: its variable is kept for as long as the statements that set it are sure to have run.
        public Expression Compile(SdfNode node, Expression x, Expression y, Expression z)
        {
            (SdfNode, Expression, Expression, Expression) key = (node, x, y, z);
            for (int i = shared.Count - 1; i >= 0; --i)
            {
                if (shared[i].TryGetValue(key, out Expression known))
                {
                    return known;
                }
            }
            Dictionary<(SdfNode, Expression, Expression, Expression), Expression> scope = shared[shared.Count - 1];
            Expression native = node.Compile(this, x, y, z);
            Expression value;
            if (native != null)
            {
                ++nativeNodes;
                value = Define(native);
            }
            else
            {
                // No compiled form: call the node's own Evaluate
                Expression point = Expression.New(vectorConstructor, x, y, z);
                // By the node's own class: a call to a sealed class needs no look-up of the method, and can be put in line
                Type type = node.GetType();
                value = Define(Expression.Call(Expression.Constant(node, type), type.GetMethod("Evaluate", new Type[] { typeof(SdfVector) }), point));
            }
            scope[key] = value;
            return value;
        }

        // Stores a value in a new variable (so that a value that is used twice is computed once). A constant and a variable
        // that is set once are their own store, and a value that was stored before, in statements that are sure to have run,
        // is not stored again: the functions below give one and the same expression for one and the same operation on the
        // same operands, which is how two values are seen to be the same (the transforms of two parts by the same amounts,
        // say).
        public Expression Define(Expression value)
        {
            if (value is ConstantExpression || (value is ParameterExpression && stored.Contains((ParameterExpression)value)))
            {
                return value;
            }
            bool fixedValue = IsFixed(value);
            if (fixedValue)
            {
                for (int i = defined.Count - 1; i >= 0; --i)
                {
                    if (defined[i].TryGetValue(value, out Expression known))
                    {
                        return known;
                    }
                }
            }
            ParameterExpression variable = Expression.Variable(typeof(double));
            variables.Add(variable);
            stored.Add(variable);
            statements.Add(Expression.Assign(variable, value));
            if (fixedValue)
            {
                defined[defined.Count - 1][value] = variable;
            }
            return variable;
        }

        // A variable that is set more than once: a value made from it is not the same value every time
        public ParameterExpression Changing(Type type)
        {
            ParameterExpression variable = Expression.Variable(type);
            variables.Add(variable);
            changing.Add(variable);
            return variable;
        }

        public bool IsChanging(ParameterExpression variable)
        {
            return changing.Contains(variable);
        }

        // For a variable that was not made by Changing
        public void MarkChanging(ParameterExpression variable)
        {
            changing.Add(variable);
        }

        // Whether a value is made only of constants, the point and variables that are set once
        private bool IsFixed(Expression value)
        {
            if (value is ConstantExpression)
            {
                return true;
            }
            if (value is ParameterExpression)
            {
                return !changing.Contains((ParameterExpression)value);
            }
            if (value is BinaryExpression)
            {
                return IsFixed(((BinaryExpression)value).Left) && IsFixed(((BinaryExpression)value).Right);
            }
            if (value is UnaryExpression)
            {
                return IsFixed(((UnaryExpression)value).Operand);
            }
            if (value is ConditionalExpression)
            {
                ConditionalExpression choice = (ConditionalExpression)value;
                return IsFixed(choice.Test) && IsFixed(choice.IfTrue) && IsFixed(choice.IfFalse);
            }
            if (value is MethodCallExpression)
            {
                MethodCallExpression call = (MethodCallExpression)value;
                if (call.Object != null && !IsFixed(call.Object))
                {
                    return false;
                }
                foreach (Expression argument in call.Arguments)
                {
                    if (!IsFixed(argument))
                    {
                        return false;
                    }
                }
                return true;
            }
            return false;
        }

        // One constant for one value
        public Expression Constant(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (!constants.TryGetValue(bits, out Expression constant))
            {
                constant = Expression.Constant(value, typeof(double));
                constants.Add(bits, constant);
            }
            return constant;
        }

        private static bool IsConstant(Expression a, out double value)
        {
            if (a is ConstantExpression && ((ConstantExpression)a).Value is double)
            {
                value = (double)((ConstantExpression)a).Value;
                return true;
            }
            value = 0;
            return false;
        }

        // One expression for one operation on the same operands
        private Expression Built(string operation, Expression a, Expression b, Func<Expression> make)
        {
            (string, Expression, Expression) key = (operation, a, b);
            if (!built.TryGetValue(key, out Expression expression))
            {
                expression = make();
                built.Add(key, expression);
            }
            return expression;
        }

        // The operations below work out what they can when the function is made: an operation on constants is a constant, and
        // adding 0, multiplying by 1 or by 0 and the like leave no operation at all. (The compiler of the function may not do
        // this itself: for it x * 0 is not 0 for every x, and x + 0 not x.) A transform that only moves a part is then three
        // additions and not the nine multiplications and nine additions of a matrix with ones and zeros in it.
        public Expression Add(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(va + vb);
            }
            if (ca && va == 0)
            {
                return b;
            }
            if (cb && vb == 0)
            {
                return a;
            }
            return Built("+", a, b, () => Expression.Add(a, b));
        }

        public Expression Subtract(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(va - vb);
            }
            if (cb && vb == 0)
            {
                return a;
            }
            if (ca && va == 0)
            {
                return Negate(b);
            }
            return Built("-", a, b, () => Expression.Subtract(a, b));
        }

        public Expression Multiply(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(va * vb);
            }
            if ((ca && va == 0) || (cb && vb == 0))
            {
                return Constant(0);
            }
            if (ca && va == 1)
            {
                return b;
            }
            if (cb && vb == 1)
            {
                return a;
            }
            if (ca && va == -1)
            {
                return Negate(b);
            }
            if (cb && vb == -1)
            {
                return Negate(a);
            }
            return Built("*", a, b, () => Expression.Multiply(a, b));
        }

        // A division by a constant is a multiplication by its inverse, which can differ from the division in the last place
        public Expression Divide(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(va / vb);
            }
            if (cb)
            {
                return Multiply(a, Constant(1 / vb));
            }
            return Built("/", a, b, () => Expression.Divide(a, b));
        }

        public Expression Negate(Expression a)
        {
            if (IsConstant(a, out double va))
            {
                return Constant(-va);
            }
            if (a is UnaryExpression && a.NodeType == ExpressionType.Negate)
            {
                return ((UnaryExpression)a).Operand;
            }
            return Built("neg", a, null, () => Expression.Negate(a));
        }

        public Expression Sqrt(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Sqrt(va)) : Built("sqrt", a, null, () => Expression.Call(sqrtMethod, a));
        }

        public Expression Abs(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Abs(va)) : Built("abs", a, null, () => Expression.Call(absMethod, a));
        }

        // (A comparison in the place of Math.Min and Math.Max was tried: it was slower in every field that was timed, by a
        // third for a union of 200 parts and by more for small fields.)
        public Expression Min(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(Math.Min(va, vb));
            }
            if (a == b || (cb && double.IsPositiveInfinity(vb)))
            {
                return a;
            }
            if (ca && double.IsPositiveInfinity(va))
            {
                return b;
            }
            return Built("min", a, b, () => Expression.Call(minMethod, a, b));
        }

        public Expression Max(Expression a, Expression b)
        {
            bool ca = IsConstant(a, out double va), cb = IsConstant(b, out double vb);
            if (ca && cb)
            {
                return Constant(Math.Max(va, vb));
            }
            if (a == b || (cb && double.IsNegativeInfinity(vb)))
            {
                return a;
            }
            if (ca && double.IsNegativeInfinity(va))
            {
                return b;
            }
            return Built("max", a, b, () => Expression.Call(maxMethod, a, b));
        }

        // A value that can be written twice without being worked out twice
        public Expression Simple(Expression a)
        {
            return a is ConstantExpression || a is ParameterExpression ? a : Define(a);
        }

        public Expression Sin(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Sin(va)) : Built("sin", a, null, () => Expression.Call(sinMethod, a));
        }

        public Expression Cos(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Cos(va)) : Built("cos", a, null, () => Expression.Call(cosMethod, a));
        }

        public Expression Floor(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Floor(va)) : Built("floor", a, null, () => Expression.Call(floorMethod, a));
        }

        public Expression Round(Expression a)
        {
            return IsConstant(a, out double va) ? Constant(Math.Round(va)) : Built("round", a, null, () => Expression.Call(roundMethod, a));
        }

        // min(max(value, lower), upper)
        public Expression Clamp(Expression value, double lower, double upper)
        {
            return Min(Max(value, Constant(lower)), Constant(upper));
        }

        // A chain of plain unions union(union(union(p1, p2), p3), ...), compiled as a bounding volume hierarchy over its operands
        // (the distance queries against a hierarchy that Quilez mentions in "binary search for SDFs"). The value of an operand is
        // at least the distance to its box (SdfNode.Bounds with grows), so a subtree whose box is at least as far as the smallest
        // value found so far cannot lower it and is skipped; of two subtrees the one with the nearer box is visited first, which
        // makes the value small early. The minimum does not depend on the order, so the result is that of the plain chain. A
        // smooth union is left alone: its result depends on the order of its operands, and following that order gains nothing
        // (timed: 4% on 36 operands in order along a ring, nothing on 200, and 51% slower on 200 in random order). null for a
        // chain with fewer than eight operands that have a box.
        public Expression UnionChain(SdfNode root, Expression x, Expression y, Expression z)
        {
            List<SdfNode> boxed = new List<SdfNode>(), open = new List<SdfNode>();
            SdfNode node = root;
            while (node is ISdfUnion && ((ISdfUnion)node).UnionWidth() == 0)
            {
                Sort(((ISdfUnion)node).UnionSecond(), boxed, open);
                node = ((ISdfUnion)node).UnionFirst();
            }
            Sort(node, boxed, open);
            if (boxed.Count < 8)
            {
                return null;
            }
            ParameterExpression result = Changing(typeof(double));
            statements.Add(Expression.Assign(result, Constant(double.PositiveInfinity)));
            // The operands without a box are always asked
            foreach (SdfNode operand in open)
            {
                statements.Add(Expression.Assign(result, Min(result, Compile(operand, x, y, z))));
            }
            UnionTree(boxed, result, x, y, z);
            return result;
        }

        private static void Sort(SdfNode operand, List<SdfNode> boxed, List<SdfNode> open)
        {
            if (operand.Bounds(out SdfVector lower, out SdfVector upper, out bool grows) && grows)
            {
                boxed.Add(operand);
            }
            else
            {
                open.Add(operand);
            }
        }

        // The box of some operands
        private static void Box(List<SdfNode> operands, out SdfVector lower, out SdfVector upper)
        {
            double lx = double.PositiveInfinity, ly = double.PositiveInfinity, lz = double.PositiveInfinity;
            double ux = double.NegativeInfinity, uy = double.NegativeInfinity, uz = double.NegativeInfinity;
            foreach (SdfNode operand in operands)
            {
                operand.Bounds(out SdfVector l, out SdfVector u, out bool grows);
                lx = Math.Min(lx, l.x); ly = Math.Min(ly, l.y); lz = Math.Min(lz, l.z);
                ux = Math.Max(ux, u.x); uy = Math.Max(uy, u.y); uz = Math.Max(uz, u.z);
            }
            lower = new SdfVector(lx, ly, lz);
            upper = new SdfVector(ux, uy, uz);
        }

        // The squared distance of the point to a box
        public Expression BoxDistance(SdfVector lower, SdfVector upper, Expression x, Expression y, Expression z)
        {
            Expression dx = Max(Max(Subtract(Constant(lower.x), x), Subtract(x, Constant(upper.x))), Constant(0));
            Expression dy = Max(Max(Subtract(Constant(lower.y), y), Subtract(y, Constant(upper.y))), Constant(0));
            Expression dz = Max(Max(Subtract(Constant(lower.z), z), Subtract(z, Constant(upper.z))), Constant(0));
            return Define(Add(Add(Multiply(dx, dx), Multiply(dy, dy)), Multiply(dz, dz)));
        }

        // Whether a subtree whose box is at the squared distance `squared` can lower the result: the point is in the box, or the
        // box is nearer than the result
        private Expression Needed(Expression squared, Expression result)
        {
            return Expression.OrElse(Expression.LessThanOrEqual(squared, Constant(0)),
                                     Expression.AndAlso(Expression.GreaterThan(result, Constant(0)),
                                                        Expression.LessThan(squared, Multiply(result, result))));
        }

        // A proxy (SdfProxy): the distance to the box beyond the margin, the field only inside it
        public Expression Proxy(SdfNode child, SdfVector lower, SdfVector upper, double margin, double lipschitz, Expression x, Expression y,
                                Expression z)
        {
            Expression squared = BoxDistance(lower, upper, x, y, z);
            ParameterExpression result = Changing(typeof(double));
            Expression inside = Scoped(() => statements.Add(Expression.Assign(result, Compile(child, x, y, z))));
            statements.Add(Expression.IfThenElse(Expression.GreaterThan(squared, Constant(margin * margin)),
                                                 Expression.Assign(result, Multiply(Sqrt(squared), Constant(lipschitz))), inside));
            return result;
        }

        // The statements made by `emit`, as one block
        public Expression Scoped(Action emit)
        {
            List<Expression> outer = statements;
            statements = new List<Expression>();
            // What is computed inside the block may not have run when the code after it does
            shared.Add(new Dictionary<(SdfNode, Expression, Expression, Expression), Expression>());
            defined.Add(new Dictionary<Expression, Expression>());
            emit();
            shared.RemoveAt(shared.Count - 1);
            defined.RemoveAt(defined.Count - 1);
            Expression block = statements.Count > 0 ? Expression.Block(statements) : Expression.Empty();
            statements = outer;
            return block;
        }

        // The operands as a tree: split in two at the middle of their centers along the longest side of the box of the centers,
        // and the two halves visited in two passes, the half with the nearer box in the first
        private void UnionTree(List<SdfNode> operands, ParameterExpression result, Expression x, Expression y, Expression z)
        {
            if (operands.Count == 1)
            {
                statements.Add(Expression.Assign(result, Min(result, Compile(operands[0], x, y, z))));
                return;
            }
            double lx = double.PositiveInfinity, ly = double.PositiveInfinity, lz = double.PositiveInfinity;
            double ux = double.NegativeInfinity, uy = double.NegativeInfinity, uz = double.NegativeInfinity;
            double[] cx = new double[operands.Count], cy = new double[operands.Count], cz = new double[operands.Count];
            for (int i = 0; i < operands.Count; ++i)
            {
                operands[i].Bounds(out SdfVector l, out SdfVector u, out bool grows);
                cx[i] = 0.5 * (l.x + u.x); cy[i] = 0.5 * (l.y + u.y); cz[i] = 0.5 * (l.z + u.z);
                lx = Math.Min(lx, cx[i]); ly = Math.Min(ly, cy[i]); lz = Math.Min(lz, cz[i]);
                ux = Math.Max(ux, cx[i]); uy = Math.Max(uy, cy[i]); uz = Math.Max(uz, cz[i]);
            }
            double[] keys = ux - lx >= uy - ly && ux - lx >= uz - lz ? cx : (uy - ly >= uz - lz ? cy : cz);
            SdfNode[] sorted = operands.ToArray();
            Array.Sort(keys, sorted);
            int half = sorted.Length / 2;
            List<SdfNode> left = new List<SdfNode>(), right = new List<SdfNode>();
            for (int i = 0; i < sorted.Length; ++i)
            {
                (i < half ? left : right).Add(sorted[i]);
            }
            Box(left, out SdfVector leftLower, out SdfVector leftUpper);
            Box(right, out SdfVector rightLower, out SdfVector rightUpper);
            Expression leftSquared = BoxDistance(leftLower, leftUpper, x, y, z);
            Expression rightSquared = BoxDistance(rightLower, rightUpper, x, y, z);
            ParameterExpression leftFirst = Changing(typeof(bool)), pass = Changing(typeof(int));
            statements.Add(Expression.Assign(leftFirst, Expression.LessThanOrEqual(leftSquared, rightSquared)));
            statements.Add(Expression.Assign(pass, Expression.Constant(0)));
            Expression leftBlock = Scoped(() => UnionTree(left, result, x, y, z));
            Expression rightBlock = Scoped(() => UnionTree(right, result, x, y, z));
            LabelTarget done = Expression.Label();
            // In the first pass the left half if it is the nearer one, in the second the other half
            Expression body = Expression.Block(
                Expression.IfThenElse(Expression.Equal(Expression.Equal(pass, Expression.Constant(0)), leftFirst),
                                      Expression.IfThen(Needed(leftSquared, result), leftBlock),
                                      Expression.IfThen(Needed(rightSquared, result), rightBlock)),
                Expression.Assign(pass, Expression.Add(pass, Expression.Constant(1))),
                Expression.IfThen(Expression.GreaterThanOrEqual(pass, Expression.Constant(2)), Expression.Break(done)));
            statements.Add(Expression.Loop(body, done));
        }

        // The smooth minimum of SdfBlend.SmoothMin for a width k > 0
        public Expression SmoothMin(Expression first, Expression second, double k, SdfBlendKind kind)
        {
            ++blends;
            Expression a = Simple(first), b = Simple(second);
            if (kind == SdfBlendKind.Polynomial)
            {
                Expression h = Define(Divide(Max(Subtract(Constant(k), Abs(Subtract(a, b))), Constant(0)), Constant(k)));
                return Subtract(Min(a, b), Multiply(Multiply(h, h), Constant(k * 0.25)));
            }
            if (kind == SdfBlendKind.Cubic)
            {
                Expression h = Define(Divide(Max(Subtract(Constant(k), Abs(Subtract(a, b))), Constant(0)), Constant(k)));
                return Subtract(Min(a, b), Multiply(Multiply(Multiply(h, h), h), Constant(k * (1.0 / 6.0))));
            }
            return Expression.Call(smoothMinMethod, a, b, Constant(k), Expression.Constant(kind));
        }

        public Expression SmoothMax(Expression a, Expression b, double k, SdfBlendKind kind)
        {
            return Negate(SmoothMin(Negate(a), Negate(b), k, kind));
        }

        // SdfCompileContext Public Members
        public int nativeNodes;
        // The number of smooth minima and maxima in the compiled form
        public int blends;
        public readonly List<ParameterExpression> variables;
        public List<Expression> statements;
        private readonly List<Dictionary<(SdfNode, Expression, Expression, Expression), Expression>> shared;
        private readonly List<Dictionary<Expression, Expression>> defined;
        private readonly HashSet<ParameterExpression> stored, changing;
        private readonly Dictionary<long, Expression> constants;
        private readonly Dictionary<(string, Expression, Expression), Expression> built;
        // Whether chains of unions skip the operands that are too far to matter (UnionChain)
        public bool cull;

        // SdfCompileContext Private Members
        private static readonly ConstructorInfo vectorConstructor =
            typeof(SdfVector).GetConstructor(new Type[] { typeof(double), typeof(double), typeof(double) });
        private static readonly MethodInfo evaluateMethod = typeof(SdfNode).GetMethod("Evaluate");
        private static readonly MethodInfo sqrtMethod = typeof(Math).GetMethod("Sqrt", new Type[] { typeof(double) });
        private static readonly MethodInfo absMethod = typeof(Math).GetMethod("Abs", new Type[] { typeof(double) });
        private static readonly MethodInfo minMethod = typeof(Math).GetMethod("Min", new Type[] { typeof(double), typeof(double) });
        private static readonly MethodInfo maxMethod = typeof(Math).GetMethod("Max", new Type[] { typeof(double), typeof(double) });
        private static readonly MethodInfo sinMethod = typeof(Math).GetMethod("Sin", new Type[] { typeof(double) });
        private static readonly MethodInfo cosMethod = typeof(Math).GetMethod("Cos", new Type[] { typeof(double) });
        private static readonly MethodInfo floorMethod = typeof(Math).GetMethod("Floor", new Type[] { typeof(double) });
        private static readonly MethodInfo roundMethod = typeof(Math).GetMethod("Round", new Type[] { typeof(double) });
        private static readonly MethodInfo smoothMinMethod = typeof(SdfBlend).GetMethod("SmoothMin");
    }

    // SdfCompiledNode Definition: a field with the compiled evaluator of another field (which keeps the bounds)
    public sealed class SdfCompiledNode : SdfNode
    {
        // SdfCompiledNode Public Methods
        public SdfCompiledNode(SdfNode source, Func<double, double, double, double> function, int nativeNodes)
        {
            this.source = source;
            this.function = function;
            this.nativeNodes = nativeNodes;
            lipschitz = source.Lipschitz();
            rigorous = source.IsRigorous();
            localBounds = source.HasLocalBounds();
        }

        // Compiles a field; a field with fewer than two nodes that have a compiled form (a single primitive, which the delegate call would only
        // slow down) is returned as it is
        public static SdfNode Compile(SdfNode source, bool cull = false)
        {
            ParameterExpression x = Expression.Parameter(typeof(double), "x");
            ParameterExpression y = Expression.Parameter(typeof(double), "y");
            ParameterExpression z = Expression.Parameter(typeof(double), "z");
            SdfCompileContext context = new SdfCompileContext();
            context.cull = cull;
            Expression result = context.Compile(source, x, y, z);
            if (context.nativeNodes < 2)
            {
                return source;
            }
            List<Expression> body = new List<Expression>(context.statements);
            body.Add(result);
            Expression<Func<double, double, double, double>> lambda =
                Expression.Lambda<Func<double, double, double, double>>(Expression.Block(typeof(double), context.variables, body), x, y, z);
            return new SdfCompiledNode(source, lambda.Compile(), context.nativeNodes);
        }

        public override bool IsExactOutside()
        {
            return source.IsExactOutside();
        }

        public override bool IsExactInside()
        {
            return source.IsExactInside();
        }

        public override bool Bounds(out SdfVector lower, out SdfVector upper, out bool grows)
        {
            return source.Bounds(out lower, out upper, out grows);
        }

        public override SdfNode Prune(SdfVector center, double radius, out double lower, out double upper)
        {
            return source.Prune(center, radius, out lower, out upper);
        }

        public override double RayLipschitz(SdfVector a, SdfVector b)
        {
            return source.RayLipschitz(a, b);
        }

        // The gradient from one compiled function with the value (sdfgradient.cs), made when it is first asked for; the
        // gradients of the nodes if there is none
        public override SdfVector AnalyticGradient(SdfVector p, double h)
        {
            if (!gradientTried)
            {
                lock (gradientLock)
                {
                    if (!gradientTried)
                    {
                        gradient = SdfGradientCompiler.Compile(source, h);
                        gradientTried = true;
                    }
                }
            }
            if (gradient != null)
            {
                gradient(p.x, p.y, p.z, out double gx, out double gy, out double gz);
                return new SdfVector(gx, gy, gz);
            }
            return source.AnalyticGradient(p, h);
        }

        public override double Evaluate(SdfVector p)
        {
            return function(p.x, p.y, p.z);
        }

        public override double Lipschitz()
        {
            return lipschitz;
        }

        public override double Lipschitz(SdfVector center, double radius)
        {
            return source.Lipschitz(center, radius);
        }

        public override bool HasLocalBounds()
        {
            return localBounds;
        }

        public override bool IsRigorous()
        {
            return rigorous;
        }

        // SdfCompiledNode Public Members
        public readonly int nativeNodes;

        // SdfCompiledNode Private Members
        private readonly SdfNode source;
        private readonly Func<double, double, double, double> function;
        private SdfGradientFunction gradient;
        private volatile bool gradientTried;
        private readonly object gradientLock = new object();
        private readonly double lipschitz;
        private readonly bool rigorous, localBounds;
    }

}  // namespace pbrt
