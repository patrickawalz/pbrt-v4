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
        }

        // Compiles a child at the point (x, y, z); the result is a variable that holds the value of the child
        public Expression Compile(SdfNode node, Expression x, Expression y, Expression z)
        {
            Expression native = node.Compile(this, x, y, z);
            if (native != null)
            {
                ++nativeNodes;
                return Define(native);
            }
            // No compiled form: call the node's own Evaluate
            Expression point = Expression.New(vectorConstructor, x, y, z);
            return Define(Expression.Call(Expression.Constant(node, typeof(SdfNode)), evaluateMethod, point));
        }

        // Stores a value in a new variable (so that a value that is used twice is computed once)
        public Expression Define(Expression value)
        {
            ParameterExpression variable = Expression.Variable(typeof(double));
            variables.Add(variable);
            statements.Add(Expression.Assign(variable, value));
            return variable;
        }

        public Expression Constant(double value)
        {
            return Expression.Constant(value, typeof(double));
        }

        public Expression Add(Expression a, Expression b)
        {
            return Expression.Add(a, b);
        }

        public Expression Subtract(Expression a, Expression b)
        {
            return Expression.Subtract(a, b);
        }

        public Expression Multiply(Expression a, Expression b)
        {
            return Expression.Multiply(a, b);
        }

        public Expression Divide(Expression a, Expression b)
        {
            return Expression.Divide(a, b);
        }

        public Expression Negate(Expression a)
        {
            return Expression.Negate(a);
        }

        public Expression Sqrt(Expression a)
        {
            return Expression.Call(sqrtMethod, a);
        }

        public Expression Abs(Expression a)
        {
            return Expression.Call(absMethod, a);
        }

        public Expression Min(Expression a, Expression b)
        {
            return Expression.Call(minMethod, a, b);
        }

        public Expression Max(Expression a, Expression b)
        {
            return Expression.Call(maxMethod, a, b);
        }

        public Expression Sin(Expression a)
        {
            return Expression.Call(sinMethod, a);
        }

        public Expression Cos(Expression a)
        {
            return Expression.Call(cosMethod, a);
        }

        public Expression Round(Expression a)
        {
            return Expression.Call(roundMethod, a);
        }

        // min(max(value, lower), upper)
        public Expression Clamp(Expression value, double lower, double upper)
        {
            return Min(Max(value, Constant(lower)), Constant(upper));
        }

        // The smooth minimum of SdfBlend.SmoothMin for a width k > 0
        public Expression SmoothMin(Expression a, Expression b, double k, SdfBlendKind kind)
        {
            if (kind == SdfBlendKind.Polynomial)
            {
                Expression h = Define(Divide(Max(Subtract(Constant(k), Abs(Subtract(a, b))), Constant(0)), Constant(k)));
                return Subtract(Min(a, b), Multiply(Multiply(Multiply(h, h), Constant(k)), Constant(0.25)));
            }
            if (kind == SdfBlendKind.Cubic)
            {
                Expression h = Define(Divide(Max(Subtract(Constant(k), Abs(Subtract(a, b))), Constant(0)), Constant(k)));
                return Subtract(Min(a, b), Multiply(Multiply(Multiply(Multiply(h, h), h), Constant(k)), Constant(1.0 / 6.0)));
            }
            return Expression.Call(smoothMinMethod, a, b, Constant(k), Expression.Constant(kind));
        }

        public Expression SmoothMax(Expression a, Expression b, double k, SdfBlendKind kind)
        {
            return Negate(SmoothMin(Negate(a), Negate(b), k, kind));
        }

        // SdfCompileContext Public Members
        public int nativeNodes;
        public readonly List<ParameterExpression> variables;
        public readonly List<Expression> statements;

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
        public static SdfNode Compile(SdfNode source)
        {
            ParameterExpression x = Expression.Parameter(typeof(double), "x");
            ParameterExpression y = Expression.Parameter(typeof(double), "y");
            ParameterExpression z = Expression.Parameter(typeof(double), "z");
            SdfCompileContext context = new SdfCompileContext();
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
        private readonly double lipschitz;
        private readonly bool rigorous, localBounds;
    }

}  // namespace pbrt
