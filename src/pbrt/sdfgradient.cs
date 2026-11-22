// (not in pbrt-v4) sdfgradient.cs
//
// The value of a field and its gradient from one compiled function. The compiled form of a field (sdfcompile.cs) is a list of
// statements over a few operations; this goes through that list once and writes, after each value, its three partial
// derivatives by the rules of differentiation (forward mode). A normal then costs one call that is two to three times a value,
// where the central difference costs four values, and it is the gradient of the field itself, not a difference over an offset.
// What has no rule here is differentiated by differences: a node that is called (a mesh, a fractal) by its own Gradient, any other
// call by a central difference in each of its numbers. A compiled form with a loop (the hierarchy of a culled union) is not
// taken: the gradient is then asked of the nodes.
//
// This is what "bool analyticgradient" uses for a compiled field with blends. Timed on a ring of 200 blended parts with the
// fields of the cells (96 samples a pixel): 353 s with the gradients of the nodes, 118 s with this function, and 100 s with the
// central difference of the compiled value, which stays the default.

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace pbrt
{

    // The value at (x, y, z), with the gradient in (gx, gy, gz)
    public delegate double SdfGradientFunction(double x, double y, double z, out double gx, out double gy, out double gz);

    // SdfGradientCompiler Definition
    public sealed class SdfGradientCompiler
    {
        // The function for a field, or null for a field without a compiled form or with one that is not taken. step is the
        // offset of the differences for what is called.
        public static SdfGradientFunction Compile(SdfNode source, double step)
        {
            ParameterExpression x = Expression.Parameter(typeof(double), "x");
            ParameterExpression y = Expression.Parameter(typeof(double), "y");
            ParameterExpression z = Expression.Parameter(typeof(double), "z");
            SdfCompileContext primal = new SdfCompileContext();
            Expression result = primal.Compile(source, x, y, z);
            // Without a blend the gradients of the nodes are the faster way: a union asks only the nearest operand for its
            // gradient, where this function works out the derivatives of all of them. With blends the nodes ask every operand
            // for its value and its gradient at every level of a chain, which this function does once.
            if (primal.nativeNodes < 2 || primal.blends == 0)
            {
                return null;
            }
            SdfGradientCompiler compiler = new SdfGradientCompiler(primal, step);
            compiler.partials[x] = new Expression[] { compiler.output.Constant(1), compiler.output.Constant(0), compiler.output.Constant(0) };
            compiler.partials[y] = new Expression[] { compiler.output.Constant(0), compiler.output.Constant(1), compiler.output.Constant(0) };
            compiler.partials[z] = new Expression[] { compiler.output.Constant(0), compiler.output.Constant(0), compiler.output.Constant(1) };
            ParameterExpression gx = Expression.Parameter(typeof(double).MakeByRefType(), "gx");
            ParameterExpression gy = Expression.Parameter(typeof(double).MakeByRefType(), "gy");
            ParameterExpression gz = Expression.Parameter(typeof(double).MakeByRefType(), "gz");
            try
            {
                foreach (Expression statement in primal.statements)
                {
                    compiler.Statement(statement);
                }
                Value value = compiler.Differentiate(result);
                List<Expression> body = new List<Expression>(compiler.output.statements);
                body.Add(Expression.Assign(gx, value.d[0]));
                body.Add(Expression.Assign(gy, value.d[1]));
                body.Add(Expression.Assign(gz, value.d[2]));
                body.Add(value.primal);
                List<ParameterExpression> variables = new List<ParameterExpression>(primal.variables);
                variables.AddRange(compiler.output.variables);
                return Expression.Lambda<SdfGradientFunction>(Expression.Block(typeof(double), variables, body), x, y, z, gx, gy, gz).Compile();
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }

        // The gradient of a node that is called, for the compiled function
        public static SdfVector NodeGradient(SdfNode node, double x, double y, double z, double step)
        {
            return node.Gradient(new SdfVector(x, y, z), step, true);
        }

        // SdfGradientCompiler Private Methods
        private SdfGradientCompiler(SdfCompileContext primal, double step)
        {
            this.primal = primal;
            this.step = step;
            output = new SdfCompileContext();
            partials = new Dictionary<ParameterExpression, Expression[]>();
        }

        // A value that can be written again at no cost (a variable or a constant) and its three partial derivatives (the same)
        private struct Value
        {
            public Expression primal;
            public Expression[] d;
        }

        private Value Constant(Expression primalValue)
        {
            Value value = new Value();
            value.primal = primalValue;
            value.d = new Expression[] { output.Constant(0), output.Constant(0), output.Constant(0) };
            return value;
        }

        private Value Stored(Expression primalValue, Expression dx, Expression dy, Expression dz)
        {
            Value value = new Value();
            value.primal = output.Define(primalValue);
            value.d = new Expression[] { output.Define(dx), output.Define(dy), output.Define(dz) };
            return value;
        }

        // A statement of the compiled form, with the derivatives of what it sets
        private void Statement(Expression statement)
        {
            if (statement is BinaryExpression && statement.NodeType == ExpressionType.Assign && ((BinaryExpression)statement).Left.Type == typeof(double))
            {
                BinaryExpression assign = (BinaryExpression)statement;
                ParameterExpression variable = (ParameterExpression)assign.Left;
                Value value = Differentiate(assign.Right);
                output.statements.Add(Expression.Assign(variable, value.primal));
                if (primal.IsChanging(variable))
                {
                    output.MarkChanging(variable);
                    Expression[] derivatives = Partials(variable);
                    for (int i = 0; i < 3; ++i)
                    {
                        output.statements.Add(Expression.Assign(derivatives[i], value.d[i]));
                    }
                }
                else
                {
                    partials[variable] = value.d;
                }
                return;
            }
            if (statement is ConditionalExpression && statement.Type == typeof(void))
            {
                ConditionalExpression choice = (ConditionalExpression)statement;
                Expression first = output.Scoped(() => Statement(choice.IfTrue)), second = output.Scoped(() => Statement(choice.IfFalse));
                output.statements.Add(Expression.IfThenElse(choice.Test, first, second));
                return;
            }
            if (statement is BlockExpression && ((BlockExpression)statement).Variables.Count == 0)
            {
                foreach (Expression inner in ((BlockExpression)statement).Expressions)
                {
                    Statement(inner);
                }
                return;
            }
            if (statement is DefaultExpression)
            {
                return;
            }
            throw new NotSupportedException();
        }

        // The variables of the derivatives of a variable that is set more than once
        private Expression[] Partials(ParameterExpression variable)
        {
            if (!partials.TryGetValue(variable, out Expression[] derivatives))
            {
                derivatives = new Expression[] { output.Changing(typeof(double)), output.Changing(typeof(double)), output.Changing(typeof(double)) };
                partials.Add(variable, derivatives);
            }
            return derivatives;
        }

        private Value Differentiate(Expression e)
        {
            if (e is ConstantExpression)
            {
                return Constant(e);
            }
            if (e is ParameterExpression)
            {
                ParameterExpression variable = (ParameterExpression)e;
                if (primal.IsChanging(variable))
                {
                    Value changing = new Value();
                    changing.primal = e;
                    changing.d = Partials(variable);
                    return changing;
                }
                if (!partials.TryGetValue(variable, out Expression[] known))
                {
                    return Constant(e);
                }
                Value value = new Value();
                value.primal = e;
                value.d = known;
                return value;
            }
            if (e is UnaryExpression && e.NodeType == ExpressionType.Negate)
            {
                Value a = Differentiate(((UnaryExpression)e).Operand);
                return Stored(output.Negate(a.primal), output.Negate(a.d[0]), output.Negate(a.d[1]), output.Negate(a.d[2]));
            }
            if (e is BinaryExpression)
            {
                Value a = Differentiate(((BinaryExpression)e).Left), b = Differentiate(((BinaryExpression)e).Right);
                if (e.NodeType == ExpressionType.Add)
                {
                    return Stored(output.Add(a.primal, b.primal), output.Add(a.d[0], b.d[0]), output.Add(a.d[1], b.d[1]), output.Add(a.d[2], b.d[2]));
                }
                if (e.NodeType == ExpressionType.Subtract)
                {
                    return Stored(output.Subtract(a.primal, b.primal), output.Subtract(a.d[0], b.d[0]), output.Subtract(a.d[1], b.d[1]),
                                  output.Subtract(a.d[2], b.d[2]));
                }
                if (e.NodeType == ExpressionType.Multiply)
                {
                    return Stored(output.Multiply(a.primal, b.primal),
                                  output.Add(output.Multiply(a.d[0], b.primal), output.Multiply(a.primal, b.d[0])),
                                  output.Add(output.Multiply(a.d[1], b.primal), output.Multiply(a.primal, b.d[1])),
                                  output.Add(output.Multiply(a.d[2], b.primal), output.Multiply(a.primal, b.d[2])));
                }
                if (e.NodeType == ExpressionType.Divide)
                {
                    // (a / b)' = (a' - (a / b) b') / b
                    Expression quotient = output.Define(output.Divide(a.primal, b.primal));
                    Expression inverse = output.Define(output.Divide(output.Constant(1), b.primal));
                    return Stored(quotient, output.Multiply(output.Subtract(a.d[0], output.Multiply(quotient, b.d[0])), inverse),
                                  output.Multiply(output.Subtract(a.d[1], output.Multiply(quotient, b.d[1])), inverse),
                                  output.Multiply(output.Subtract(a.d[2], output.Multiply(quotient, b.d[2])), inverse));
                }
                throw new NotSupportedException();
            }
            if (e is ConditionalExpression)
            {
                // One of two values by a test: the value and the derivatives of the one that the test picks
                ConditionalExpression choice = (ConditionalExpression)e;
                ParameterExpression picked = output.Changing(typeof(double));
                Expression[] derivatives = { output.Changing(typeof(double)), output.Changing(typeof(double)), output.Changing(typeof(double)) };
                Expression first = output.Scoped(() => Assign(picked, derivatives, Differentiate(choice.IfTrue)));
                Expression second = output.Scoped(() => Assign(picked, derivatives, Differentiate(choice.IfFalse)));
                output.statements.Add(Expression.IfThenElse(choice.Test, first, second));
                Value value = new Value();
                value.primal = picked;
                value.d = derivatives;
                return value;
            }
            if (e is MethodCallExpression)
            {
                return Call((MethodCallExpression)e);
            }
            throw new NotSupportedException();
        }

        private void Assign(ParameterExpression variable, Expression[] derivatives, Value value)
        {
            output.statements.Add(Expression.Assign(variable, value.primal));
            for (int i = 0; i < 3; ++i)
            {
                output.statements.Add(Expression.Assign(derivatives[i], value.d[i]));
            }
        }

        // The derivatives of a value times a factor
        private Value Chain(Expression primalValue, Value a, Expression factor)
        {
            Expression scale = output.Define(factor);
            return Stored(primalValue, output.Multiply(a.d[0], scale), output.Multiply(a.d[1], scale), output.Multiply(a.d[2], scale));
        }

        private Value Call(MethodCallExpression call)
        {
            MethodInfo method = call.Method;
            if (method.DeclaringType == typeof(Math) && call.Arguments.Count == 1)
            {
                Value a = Differentiate(call.Arguments[0]);
                if (method.Name == "Sqrt")
                {
                    // 1 / (2 sqrt), kept finite at 0, where the derivatives of what is under the root are 0 too (the length of
                    // the part of a point that is outside of a box, for a point inside it)
                    Expression root = output.Define(output.Sqrt(a.primal));
                    return Chain(root, a, output.Divide(output.Constant(0.5), output.Max(root, output.Constant(1e-150))));
                }
                if (method.Name == "Abs")
                {
                    return Chain(output.Abs(a.primal), a, Expression.Call(copySignMethod, output.Constant(1), a.primal));
                }
                if (method.Name == "Sin")
                {
                    return Chain(output.Sin(a.primal), a, output.Cos(a.primal));
                }
                if (method.Name == "Cos")
                {
                    return Chain(output.Cos(a.primal), a, output.Negate(output.Sin(a.primal)));
                }
                if (method.Name == "Floor")
                {
                    return Constant(output.Define(output.Floor(a.primal)));
                }
                if (method.Name == "Round")
                {
                    return Constant(output.Define(output.Round(a.primal)));
                }
                throw new NotSupportedException();
            }
            if (method.DeclaringType == typeof(Math) && (method.Name == "Min" || method.Name == "Max"))
            {
                Value a = Differentiate(call.Arguments[0]), b = Differentiate(call.Arguments[1]);
                Value value = new Value();
                value.primal = output.Define(method.Name == "Min" ? output.Min(a.primal, b.primal) : output.Max(a.primal, b.primal));
                value.d = new Expression[3];
                List<Expression> first = new List<Expression>(), second = new List<Expression>();
                for (int i = 0; i < 3; ++i)
                {
                    if (a.d[i] == b.d[i])
                    {
                        value.d[i] = a.d[i];
                        continue;
                    }
                    ParameterExpression derivative = output.Changing(typeof(double));
                    value.d[i] = derivative;
                    first.Add(Expression.Assign(derivative, a.d[i]));
                    second.Add(Expression.Assign(derivative, b.d[i]));
                }
                if (first.Count > 0)
                {
                    Expression test = method.Name == "Min" ? Expression.LessThan(a.primal, b.primal) : Expression.GreaterThan(a.primal, b.primal);
                    output.statements.Add(Expression.IfThenElse(test, Expression.Block(first), Expression.Block(second)));
                }
                return value;
            }
            // Any other call: its numbers are the arguments that are numbers and the three numbers of an argument that is a
            // point. The value is the call with those; the derivatives are by a difference in each number that has any.
            List<Value> numbers = new List<Value>();
            List<Expression> arguments = new List<Expression>();
            List<int> first3 = new List<int>();
            foreach (Expression argument in call.Arguments)
            {
                if (argument.Type == typeof(double))
                {
                    numbers.Add(Differentiate(argument));
                    arguments.Add(null);
                    first3.Add(-1);
                }
                else if (argument is NewExpression && argument.Type == typeof(SdfVector) && ((NewExpression)argument).Arguments.Count == 3)
                {
                    first3.Add(numbers.Count);
                    foreach (Expression component in ((NewExpression)argument).Arguments)
                    {
                        numbers.Add(Differentiate(component));
                    }
                    arguments.Add(argument);
                }
                else if (argument is ConstantExpression)
                {
                    arguments.Add(argument);
                    first3.Add(-1);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            if (call.Object != null && !(call.Object is ConstantExpression))
            {
                throw new NotSupportedException();
            }
            Func<int, double, Expression> shifted = (int which, double by) =>
            {
                List<Expression> actual = new List<Expression>();
                int number = 0;
                for (int i = 0; i < call.Arguments.Count; ++i)
                {
                    if (call.Arguments[i].Type == typeof(double))
                    {
                        actual.Add(number == which ? output.Add(numbers[number].primal, output.Constant(by)) : numbers[number].primal);
                        ++number;
                    }
                    else if (first3[i] >= 0)
                    {
                        Expression[] components = new Expression[3];
                        for (int k = 0; k < 3; ++k)
                        {
                            components[k] = number == which ? output.Add(numbers[number].primal, output.Constant(by)) : numbers[number].primal;
                            ++number;
                        }
                        actual.Add(Expression.New(((NewExpression)call.Arguments[i]).Constructor, components));
                    }
                    else
                    {
                        actual.Add(arguments[i]);
                    }
                }
                return call.Object == null ? Expression.Call(method, actual) : Expression.Call(call.Object, method, actual);
            };
            Value result = new Value();
            result.primal = output.Define(shifted(-1, 0));
            Expression[] sums = { output.Constant(0), output.Constant(0), output.Constant(0) };
            bool node = call.Object != null && ((ConstantExpression)call.Object).Value is SdfNode && method.Name == "Evaluate" && numbers.Count == 3 &&
                        first3.Count == 1 && first3[0] == 0;
            Expression gradient = null;
            for (int s = 0; s < numbers.Count; ++s)
            {
                bool any = false;
                for (int i = 0; i < 3; ++i)
                {
                    any |= !IsZero(numbers[s].d[i]);
                }
                if (!any)
                {
                    continue;
                }
                Expression slope;
                if (node)
                {
                    // The gradient of the node, asked once for its three numbers
                    if (gradient == null)
                    {
                        ParameterExpression stored = Expression.Variable(typeof(SdfVector));
                        output.variables.Add(stored);
                        output.statements.Add(Expression.Assign(stored, Expression.Call(nodeGradientMethod, Expression.Constant(((ConstantExpression)call.Object).Value, typeof(SdfNode)),
                                                                                         numbers[0].primal, numbers[1].primal, numbers[2].primal, Expression.Constant(step))));
                        gradient = stored;
                    }
                    slope = output.Define(Expression.Field(gradient, s == 0 ? "x" : (s == 1 ? "y" : "z")));
                }
                else
                {
                    slope = output.Define(output.Multiply(output.Subtract(shifted(s, step), shifted(s, -step)), output.Constant(0.5 / step)));
                }
                for (int i = 0; i < 3; ++i)
                {
                    sums[i] = output.Add(sums[i], output.Multiply(slope, numbers[s].d[i]));
                }
            }
            result.d = new Expression[] { output.Define(sums[0]), output.Define(sums[1]), output.Define(sums[2]) };
            return result;
        }

        private static bool IsZero(Expression e)
        {
            return e is ConstantExpression && ((ConstantExpression)e).Value is double && (double)((ConstantExpression)e).Value == 0;
        }

        // SdfGradientCompiler Private Members
        private readonly SdfCompileContext primal, output;
        private readonly double step;
        private readonly Dictionary<ParameterExpression, Expression[]> partials;
        private static readonly MethodInfo copySignMethod = typeof(Math).GetMethod("CopySign", new Type[] { typeof(double), typeof(double) });
        private static readonly MethodInfo nodeGradientMethod = typeof(SdfGradientCompiler).GetMethod("NodeGradient");
    }
}
