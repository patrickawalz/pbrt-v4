// (not in OpenShadingLanguage) optinterp_test.cs: a small reference interpreter over the ops of the instances of an optimized (or unoptimized) group, used
// by optimizer_test.cs to check that the optimizer keeps the meaning of a shader group (the same output parameters are computed).
//
// It runs the layers in order, one value array per symbol (int[], float[] or ustring[]; derivatives are not tracked), implements the control flow of the
// ops (if, for, while, dowhile, break, continue, functioncall, return, exit) and the arithmetic, comparison, component, array, triple constructor and a
// few math ops that the test shaders use. An op it does not know throws NotSupportedException.

using System;
using System.Collections.Generic;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public sealed class OptInterp
        {
            private enum Flow
            {
                Normal,
                Break,
                Continue,
                Return,
                Exit
            }

            private readonly ShaderGroup m_group;
            private readonly Dictionary<Symbol, Array>[] m_store;
            private readonly Dictionary<string, Array> m_globals = new Dictionary<string, Array>();
            public readonly List<string> printed = new List<string>();

            public OptInterp(ShaderGroup group)
            {
                m_group = group;
                m_store = new Dictionary<Symbol, Array>[group.nlayers()];
            }

            /// Run all the layers that are not unused, in order
            public void Run()
            {
                for (int layer = 0; layer < m_group.nlayers(); ++layer)
                {
                    ShaderInstance inst = m_group[layer];
                    if (inst.unused())
                    {
                        continue;
                    }
                    RunLayer(layer, inst);
                }
            }

            /// The value of an output parameter (by name) of a layer, as floats (ints are converted); null if the layer has no such symbol
            public float[] Output(int layer, string name)
            {
                if (m_store[layer] == null)
                {
                    return null;
                }
                foreach (KeyValuePair<Symbol, Array> kv in m_store[layer])
                {
                    if (kv.Key.name().@string() == name && (kv.Key.symtype() == SymType.SymTypeOutputParam || kv.Key.symtype() == SymType.SymTypeParam))
                    {
                        return AsFloats(kv.Value);
                    }
                }
                return null;
            }

            public string OutputString(int layer, string name)
            {
                foreach (KeyValuePair<Symbol, Array> kv in m_store[layer])
                {
                    if (kv.Key.name().@string() == name && kv.Value is ustring[])
                    {
                        return ((ustring[])kv.Value)[0].@string();
                    }
                }
                return null;
            }

            private static Array Alloc(Symbol s)
            {
                TypeDesc t = s.typespec().simpletype();
                int n = Math.Max(t.basevalues(), 1);
                if (s.typespec().is_unsized_array() && s.data() != null)
                {
                    n = ((Array)s.data()).Length;
                }
                if (t.basetype == TypeDesc.FLOAT)
                {
                    return new float[n];
                }
                if (t.basetype == TypeDesc.INT)
                {
                    return new int[n];
                }
                if (t.basetype == TypeDesc.STRING)
                {
                    ustring[] u = new ustring[n];
                    for (int i = 0; i < n; ++i)
                    {
                        u[i] = new ustring("");
                    }
                    return u;
                }
                throw new NotSupportedException("type " + s.typespec().@string());
            }

            private static float[] AsFloats(Array a)
            {
                if (a is float[])
                {
                    return (float[])a;
                }
                int[] ia = (int[])a;
                float[] f = new float[ia.Length];
                for (int i = 0; i < f.Length; ++i)
                {
                    f[i] = ia[i];
                }
                return f;
            }

            private Array Storage(int layer, Symbol s)
            {
                Array a;
                if (!m_store[layer].TryGetValue(s, out a))
                {
                    a = Alloc(s);
                    if (s.symtype() == SymType.SymTypeGlobal)
                    {
                        string name = s.name().@string();
                        Array g;
                        if (!m_globals.TryGetValue(name, out g))
                        {
                            g = a;
                            if (g is float[])
                            {
                                float[] f = (float[])g;
                                if (name == "P" || name == "Ps")
                                {
                                    f[0] = 0.1f;
                                    f[1] = 0.2f;
                                    f[2] = 0.3f;
                                }
                                else if (name == "u")
                                {
                                    f[0] = 0.25f;
                                }
                                else if (name == "v")
                                {
                                    f[0] = 0.75f;
                                }
                                else if (name == "I")
                                {
                                    f[2] = -1;
                                }
                                else if (name == "N" || name == "Ng")
                                {
                                    f[2] = 1;
                                }
                                else if (name == "time")
                                {
                                    f[0] = 0.5f;
                                }
                            }
                            m_globals[name] = g;
                        }
                        a = g;
                    }
                    else if (s.symtype() == SymType.SymTypeConst || s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                    {
                        object d = s.data();
                        if (d != null)
                        {
                            Array.Copy((Array)d, a, Math.Min(((Array)d).Length, a.Length));
                        }
                    }
                    m_store[layer][s] = a;
                }
                return a;
            }

            private void RunLayer(int layer, ShaderInstance inst)
            {
                m_store[layer] = new Dictionary<Symbol, Array>();
                foreach (Symbol s in inst.symbols())
                {
                    Storage(layer, s);
                }

                // Values coming from upstream layers
                foreach (Connection c in inst.connections())
                {
                    ShaderInstance up = m_group[c.srclayer];
                    if (m_store[c.srclayer] == null)
                    {
                        continue;  // the upstream layer didn't run
                    }
                    Symbol srcsym = up.symbol(c.src.param);
                    Symbol dstsym = inst.symbol(c.dst.param);
                    Array srcv = Storage(c.srclayer, srcsym);
                    Array dstv = Storage(layer, dstsym);
                    CopyConnection(c, srcsym, srcv, dstsym, dstv);
                }

                // The init ops of the parameters that were not connected
                foreach (Symbol s in inst.param_range())
                {
                    if (s.has_init_ops() && s.valuesource() == Symbol.ValueSource.DefaultVal)
                    {
                        RunRange(layer, inst, s.initbegin(), s.initend());
                    }
                }

                RunRange(layer, inst, inst.maincodebegin(), inst.maincodeend());
            }

            private static int ElementValues(TypeSpec t)
            {
                if (t.is_array())
                {
                    return t.elementtype().simpletype().basevalues();
                }
                return t.simpletype().basevalues();
            }

            private static void CopyConnection(Connection c, Symbol srcsym, Array srcv, Symbol dstsym, Array dstv)
            {
                if (c.is_complete())
                {
                    AssignValues(dstsym.typespec(), dstv, srcsym.typespec(), srcv);
                    return;
                }
                int srcoff = 0, srclen = ElementValues(srcsym.typespec());
                if (c.src.arrayindex >= 0)
                {
                    srcoff = c.src.arrayindex * srclen;
                }
                if (c.src.channel >= 0)
                {
                    srcoff += c.src.channel;
                    srclen = 1;
                }
                int dstoff = 0, dstlen = ElementValues(dstsym.typespec());
                if (c.dst.arrayindex >= 0)
                {
                    dstoff = c.dst.arrayindex * dstlen;
                }
                if (c.dst.channel >= 0)
                {
                    dstoff += c.dst.channel;
                    dstlen = 1;
                }
                for (int i = 0; i < dstlen; ++i)
                {
                    int si = srclen == 1 ? srcoff : srcoff + i;
                    SetValue(dstv, dstoff + i, srcv, si);
                }
            }

            // dst[di] = src[si] with the int/float conversions
            private static void SetValue(Array dst, int di, Array src, int si)
            {
                if (dst is float[])
                {
                    ((float[])dst)[di] = src is float[] ? ((float[])src)[si] : ((int[])src)[si];
                }
                else if (dst is int[])
                {
                    ((int[])dst)[di] = src is int[] ? ((int[])src)[si] : (int)((float[])src)[si];
                }
                else
                {
                    ((ustring[])dst)[di] = ((ustring[])src)[si];
                }
            }

            // R = A with the conversions of the assign op
            private static void AssignValues(TypeSpec rt, Array r, TypeSpec at, Array a)
            {
                if (r is ustring[])
                {
                    Array.Copy(a, r, Math.Min(a.Length, r.Length));
                    return;
                }
                if (a.Length == r.Length || at.is_array())
                {
                    int n = Math.Min(a.Length, r.Length);
                    for (int i = 0; i < n; ++i)
                    {
                        SetValue(r, i, a, i);
                    }
                    return;
                }
                if (r is int[])
                {
                    SetValue(r, 0, a, 0);
                    return;
                }
                // scalar to triple or matrix: replicate (a matrix from a scalar is the scalar matrix)
                if (rt.is_matrix())
                {
                    float f = AsFloats(a)[0];
                    float[] m = (float[])r;
                    for (int i = 0; i < 16; ++i)
                    {
                        m[i] = (i % 5 == 0) ? f : 0;
                    }
                    return;
                }
                for (int i = 0; i < r.Length; ++i)
                {
                    SetValue(r, i, a, 0);
                }
            }

            private Flow RunRange(int layer, ShaderInstance inst, int begin, int end)
            {
                int pc = begin;
                while (pc < end)
                {
                    Opcode op = inst.ops()[pc];
                    string name = op.opname().@string();
                    switch (name)
                    {
                        case "if":
                        {
                            Symbol cs = inst.argsymbol(op.firstarg());
                            bool t = NonZero(Storage(layer, cs));
                            Flow f = t ? RunRange(layer, inst, pc + 1, op.jump(0)) : RunRange(layer, inst, op.jump(0), op.jump(1));
                            if (f != Flow.Normal)
                            {
                                return f;
                            }
                            pc = op.jump(1);
                            continue;
                        }
                        case "for":
                        case "while":
                        case "dowhile":
                        {
                            Symbol cs = inst.argsymbol(op.firstarg());
                            Flow f = RunRange(layer, inst, pc + 1, op.jump(0));
                            if (f != Flow.Normal)
                            {
                                return f;
                            }
                            bool first = name == "dowhile";
                            int guard = 0;
                            while (true)
                            {
                                if (!first)
                                {
                                    f = RunRange(layer, inst, op.jump(0), op.jump(1));
                                    if (f != Flow.Normal)
                                    {
                                        return f;
                                    }
                                    if (!NonZero(Storage(layer, cs)))
                                    {
                                        break;
                                    }
                                }
                                first = false;
                                f = RunRange(layer, inst, op.jump(1), op.jump(2));
                                if (f == Flow.Break)
                                {
                                    break;
                                }
                                if (f == Flow.Return || f == Flow.Exit)
                                {
                                    return f;
                                }
                                f = RunRange(layer, inst, op.jump(2), op.jump(3));
                                if (f == Flow.Break)
                                {
                                    break;
                                }
                                if (f == Flow.Return || f == Flow.Exit)
                                {
                                    return f;
                                }
                                if (++guard > 100000)
                                {
                                    throw new InvalidOperationException("runaway loop");
                                }
                            }
                            pc = op.jump(3);
                            continue;
                        }
                        case "functioncall":
                        case "functioncall_nr":
                        {
                            Flow f = RunRange(layer, inst, pc + 1, op.jump(0));
                            if (f == Flow.Exit)
                            {
                                return f;
                            }
                            pc = op.jump(0);
                            continue;
                        }
                        case "break":
                            return Flow.Break;
                        case "continue":
                            return Flow.Continue;
                        case "return":
                            return Flow.Return;
                        case "exit":
                            return Flow.Exit;
                        default:
                            Exec(layer, inst, op, name);
                            break;
                    }
                    ++pc;
                }
                return Flow.Normal;
            }

            private static bool NonZero(Array a)
            {
                if (a is int[])
                {
                    return ((int[])a)[0] != 0;
                }
                if (a is float[])
                {
                    return ((float[])a)[0] != 0.0f;
                }
                return ((ustring[])a)[0].length() != 0;
            }

            // Component i of a float-based or int operand, with a scalar broadcast over the components
            private static float Comp(Array a, int i)
            {
                if (a is int[])
                {
                    return ((int[])a)[Math.Min(i, ((int[])a).Length - 1)];
                }
                float[] f = (float[])a;
                return f[f.Length == 1 ? 0 : i];
            }

            private void Exec(int layer, ShaderInstance inst, Opcode op, string name)
            {
                Symbol[] s = new Symbol[op.nargs()];
                Array[] v = new Array[op.nargs()];
                for (int i = 0; i < op.nargs(); ++i)
                {
                    s[i] = inst.argsymbol(op.firstarg() + i);
                    v[i] = Storage(layer, s[i]);
                }
                switch (name)
                {
                    case "nop":
                    case "end":
                    case "useparam":
                        return;
                    case "printf":
                    case "error":
                    case "warning":
                    case "fprintf":
                        return;
                    case "assign":
                        AssignValues(s[0].typespec(), v[0], s[1].typespec(), v[1]);
                        return;
                    case "add":
                    case "sub":
                    case "mul":
                    case "div":
                    case "mod":
                        Binary(name, s, v);
                        return;
                    case "neg":
                        if (v[0] is int[])
                        {
                            ((int[])v[0])[0] = unchecked(-((int[])v[1])[0]);
                        }
                        else
                        {
                            for (int i = 0; i < v[0].Length; ++i)
                            {
                                ((float[])v[0])[i] = -Comp(v[1], i);
                            }
                        }
                        return;
                    case "abs":
                        if (v[0] is int[])
                        {
                            ((int[])v[0])[0] = Math.Abs(((int[])v[1])[0]);
                        }
                        else
                        {
                            Unary(v, MathF.Abs);
                        }
                        return;
                    case "floor":
                        Unary(v, MathF.Floor);
                        return;
                    case "ceil":
                        Unary(v, MathF.Ceiling);
                        return;
                    case "sqrt":
                        Unary(v, delegate (float x) { return x >= 0 ? MathF.Sqrt(x) : 0.0f; });
                        return;
                    case "sin":
                        Unary(v, MathF.Sin);
                        return;
                    case "cos":
                        Unary(v, MathF.Cos);
                        return;
                    case "min":
                    case "max":
                        if (v[0] is int[])
                        {
                            int a = ((int[])v[1])[0], b = ((int[])v[2])[0];
                            ((int[])v[0])[0] = name == "min" ? Math.Min(a, b) : Math.Max(a, b);
                        }
                        else
                        {
                            for (int i = 0; i < v[0].Length; ++i)
                            {
                                float a = Comp(v[1], i), b = Comp(v[2], i);
                                ((float[])v[0])[i] = name == "min" ? (b < a ? b : a) : (a < b ? b : a);
                            }
                        }
                        return;
                    case "clamp":
                        for (int i = 0; i < v[0].Length; ++i)
                        {
                            float x = Comp(v[1], i), lo = Comp(v[2], i), hi = Comp(v[3], i);
                            ((float[])v[0])[i] = x < lo ? lo : (x > hi ? hi : x);
                        }
                        return;
                    case "mix":
                        for (int i = 0; i < v[0].Length; ++i)
                        {
                            float a = Comp(v[1], i), b = Comp(v[2], i), x = Comp(v[3], i);
                            ((float[])v[0])[i] = (1.0f - x) * a + x * b;
                        }
                        return;
                    case "dot":
                    {
                        float d = 0;
                        for (int i = 0; i < 3; ++i)
                        {
                            d += Comp(v[1], i) * Comp(v[2], i);
                        }
                        ((float[])v[0])[0] = d;
                        return;
                    }
                    case "eq":
                    case "neq":
                    case "lt":
                    case "le":
                    case "gt":
                    case "ge":
                        Compare(name, v);
                        return;
                    case "and":
                        ((int[])v[0])[0] = (((int[])v[1])[0] != 0 && ((int[])v[2])[0] != 0) ? 1 : 0;
                        return;
                    case "or":
                        ((int[])v[0])[0] = (((int[])v[1])[0] != 0 || ((int[])v[2])[0] != 0) ? 1 : 0;
                        return;
                    case "not":
                        ((int[])v[0])[0] = NonZero(v[1]) ? 0 : 1;
                        return;
                    case "bitand":
                        ((int[])v[0])[0] = ((int[])v[1])[0] & ((int[])v[2])[0];
                        return;
                    case "bitor":
                        ((int[])v[0])[0] = ((int[])v[1])[0] | ((int[])v[2])[0];
                        return;
                    case "xor":
                        ((int[])v[0])[0] = ((int[])v[1])[0] ^ ((int[])v[2])[0];
                        return;
                    case "compl":
                        ((int[])v[0])[0] = ~((int[])v[1])[0];
                        return;
                    case "color":
                    case "point":
                    case "vector":
                    case "normal":
                    {
                        int first = (op.nargs() > 1 && s[1].typespec().is_string()) ? 2 : 1;
                        float[] r = (float[])v[0];
                        if (op.nargs() - first == 1)
                        {
                            for (int i = 0; i < 3; ++i)
                            {
                                r[i] = Comp(v[first], 0);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < 3; ++i)
                            {
                                r[i] = Comp(v[first + i], 0);
                            }
                        }
                        return;
                    }
                    case "compref":
                        ((float[])v[0])[0] = ((float[])v[1])[((int[])v[2])[0]];
                        return;
                    case "compassign":
                        ((float[])v[0])[((int[])v[1])[0]] = Comp(v[2], 0);
                        return;
                    case "aref":
                    {
                        int n = ElementValues(s[1].typespec());
                        int idx = ((int[])v[2])[0];
                        for (int i = 0; i < n; ++i)
                        {
                            SetValue(v[0], i, v[1], idx * n + i);
                        }
                        return;
                    }
                    case "aassign":
                    {
                        int n = ElementValues(s[0].typespec());
                        int idx = ((int[])v[1])[0];
                        for (int i = 0; i < n; ++i)
                        {
                            SetValue(v[0], idx * n + i, v[2], v[2].Length == 1 ? 0 : i);
                        }
                        return;
                    }
                    case "arraylength":
                        ((int[])v[0])[0] = s[1].typespec().arraylength();
                        return;
                    case "strlen":
                        ((int[])v[0])[0] = System.Text.Encoding.UTF8.GetByteCount(((ustring[])v[1])[0].@string());
                        return;
                    default:
                        throw new NotSupportedException("op " + name);
                }
            }

            private static void Unary(Array[] v, Func<float, float> f)
            {
                for (int i = 0; i < v[0].Length; ++i)
                {
                    ((float[])v[0])[i] = f(Comp(v[1], i));
                }
            }

            private static void Binary(string name, Symbol[] s, Array[] v)
            {
                if (v[0] is int[])
                {
                    int a = ((int[])v[1])[0], b = ((int[])v[2])[0];
                    int r;
                    switch (name)
                    {
                        case "add":
                            r = unchecked(a + b);
                            break;
                        case "sub":
                            r = unchecked(a - b);
                            break;
                        case "mul":
                            r = unchecked(a * b);
                            break;
                        case "div":
                            r = b == 0 ? 0 : (b == -1 ? unchecked(-a) : a / b);
                            break;
                        default:
                            r = b == 0 ? 0 : (b == -1 ? 0 : a % b);
                            break;
                    }
                    ((int[])v[0])[0] = r;
                    return;
                }
                float[] rv = (float[])v[0];
                for (int i = 0; i < rv.Length; ++i)
                {
                    float a = Comp(v[1], i), b = Comp(v[2], i);
                    switch (name)
                    {
                        case "add":
                            rv[i] = a + b;
                            break;
                        case "sub":
                            rv[i] = a - b;
                            break;
                        case "mul":
                            rv[i] = a * b;
                            break;
                        case "div":
                            rv[i] = b != 0.0f ? a / b : 0.0f;
                            break;
                        default:
                            rv[i] = b != 0.0f ? a - b * MathF.Truncate(a / b) : 0.0f;
                            break;
                    }
                }
            }

            private static void Compare(string name, Array[] v)
            {
                bool result;
                if (v[1] is ustring[])
                {
                    bool eq = ((ustring[])v[1])[0] == ((ustring[])v[2])[0];
                    result = name == "eq" ? eq : !eq;
                }
                else
                {
                    int n = Math.Max(v[1].Length, v[2].Length);
                    if (name == "eq" || name == "neq")
                    {
                        bool eq = true;
                        for (int i = 0; i < n; ++i)
                        {
                            eq &= Comp(v[1], i) == Comp(v[2], i);
                        }
                        result = name == "eq" ? eq : !eq;
                    }
                    else
                    {
                        float a = Comp(v[1], 0), b = Comp(v[2], 0);
                        result = name == "lt" ? a < b : name == "le" ? a <= b : name == "gt" ? a > b : a >= b;
                    }
                }
                ((int[])v[0])[0] = result ? 1 : 0;
            }
        }
    }
}
