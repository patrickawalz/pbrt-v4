// OSL port: liboslexec/constfold.cpp (second part): the folders of the string, math, triple/matrix and space transform ops.
// See constfold.cs for the conventions.

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL.pvt
{
    public static unsafe partial class ConstFold
    {
        public static unsafe int constfold_split(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            // Symbol &R (*rop.inst()->argsymbol(op.firstarg()+0));
            Symbol Str = rop.opargsym(op, 1);
            Symbol Results = rop.opargsym(op, 2);
            Symbol Sep = rop.opargsym(op, 3);
            Symbol Maxsplit = rop.opargsym(op, 4);
            if (Str.is_constant() && (Sep == null || Sep.is_constant()) && (Maxsplit == null || Maxsplit.is_constant()))
            {
                // The split string, separator string, and maxsplit are all constants. Compute the results with Strutil::split.
                int resultslen = Results.typespec().arraylength();
                int maxsplit = Maxsplit != null ? Maxsplit.get_int() : resultslen;
                maxsplit = Math.Min(maxsplit, resultslen);
                ustring sep = Sep != null ? Sep.get_string() : new ustring("");
                ulong[] splits = new ulong[Math.Max(resultslen, 1)];
                int n;
                fixed (ulong* psplits = splits)
                {
                    // (osl_split is the runtime's Strutil::split of the same arguments: it clamps maxsplit to [0, resultslen] and returns
                    // min(maxsplit, number of pieces))
                    n = OslRuntime.osl_split(Str.get_string().hash(), psplits, sep.hash(), maxsplit, resultslen);
                }
                // Temporarily stash the index of the symbol holding results
                int resultsarg = rop.inst().args()[op.firstarg() + 2];
                // Turn the 'split' into a straight assignment of the return value...
                rop.turn_into_assign(op, rop.add_constant(n));
                // Create a constant array holding the split results
                ustring[] usplits = new ustring[resultslen];
                for (int i = 0; i < resultslen; ++i)
                {
                    usplits[i] = i < n ? OslStrings.from_hash(splits[i]) : new ustring("");
                }
                int cind = rop.add_constant(new TypeSpec(new TypeDesc(TypeDesc.STRING, resultslen)), usplits);
                // And insert an instruction copying our constant array to the user's results array.
                int[] args = new int[] { resultsarg, cind };
                rop.insert_code(opnum, u_assign, args, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                RuntimeOptimizer.InsertRelation.GroupWithNext);
                return 1;
            }

            return 0;
        }

        public static int constfold_concat(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=concat(s,...) into R=C
            Opcode op = rop.inst().ops()[opnum];
            ustring result = new ustring("");
            for (int i = 1; i < op.nargs(); ++i)
            {
                Symbol S = argsym(rop, op, i);
                if (!S.is_constant())
                {
                    return 0;  // something non-constant
                }
                ustring old = result;
                ustring s = S.get_string();
                result = new ustring(old.@string() + s.@string());
            }
            // If we made it this far, all args were constants, and the concatenation is in result.
            int cind = rop.add_constant(result);
            rop.turn_into_assign(op, cind, "const fold concat");
            return 1;
        }

        public static int constfold_format(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=format(fmt,...) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol Format = rop.opargsym(op, 1);
            if (!Format.is_constant())
            {
                return 0;
            }
            ustring fmt = Format.get_string();

            // split fmt into the prefix (the starting part of the string that we haven't yet processed) and the suffix (the ending part that we've
            // fully processed).
            string prefix = fmt.@string();
            string suffix = "";
            int args_expanded = 0;

            // While there is still a constant argument at the end of the arg list, peel it off and use it to rewrite the format string.
            for (int argnum = op.nargs() - 1; argnum >= 2; --argnum)
            {
                Symbol Arg = rop.opargsym(op, argnum);
                if (!Arg.is_constant())
                {
                    break;  // no more constants
                }

                // find the last format specification
                int pos = prefix.Length - 1;  // (std::string::npos: search from the end)
                while (true)
                {
                    pos = pos >= 0 ? prefix.LastIndexOf('%', pos) : -1;  // find at or before pos
                    if (pos < 0)
                    {
                        // Fewer '%' tokens than arguments? Must be malformed. Punt.
                        return 0;
                    }
                    if (pos == 0 || prefix[pos - 1] != '%')
                    {
                        // we found the format specifier
                        break;
                    }
                    // False alarm! Beware of %% which is a literal % rather than a format specifier. Back up and try again.
                    if (pos >= 2)
                    {
                        pos -= 2;  // back up
                    }
                    else
                    {
                        // This can only happen if the %% is at the start of the format string, but it shouldn't be since there are still args to
                        // process. Punt.
                        return 0;
                    }
                }
                System.Diagnostics.Debug.Assert(pos < prefix.Length && prefix[pos] == '%');

                // cleave off the last format specification into mid
                string mid = prefix.Substring(pos);
                string formatted;
                TypeSpec argtype = Arg.typespec();
                if (argtype.is_int())
                {
                    formatted = Strutil.sprintf(mid, Arg.get_int());
                }
                else if (argtype.is_float())
                {
                    formatted = Strutil.sprintf(mid, Arg.get_float());
                }
                else if (argtype.is_triple())
                {
                    string format = Fmt.format("{} {} {}", mid, mid, mid);
                    Vec3 v = Arg.get_vec3();
                    formatted = Strutil.sprintf(format, v.x, v.y, v.z);
                }
                else if (argtype.is_matrix())
                {
                    string format = Fmt.format("{} {} {} {} {} {} {} {} {} {} {} {} {} {} {} {}", mid, mid, mid, mid, mid, mid, mid, mid, mid, mid, mid,
                                               mid, mid, mid, mid, mid);
                    float[] m = (float[])Arg.data();
                    formatted = Strutil.sprintf(format, m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14],
                                                m[15]);
                }
                else if (argtype.is_string())
                {
                    formatted = Strutil.sprintf(mid, Arg.get_string().@string());
                }
                else
                {
                    break;  // something else we don't handle -- we're done
                }

                // We were able to format, so rejigger the strings.
                prefix = prefix.Remove(pos);
                suffix = formatted + suffix;
                args_expanded += 1;
            }

            // Rewrite the op
            if (args_expanded == op.nargs() - 2)
            {
                // Special case -- completely expanded, replace with a string assignment
                int cind = rop.add_constant(new ustring(prefix + suffix));
                rop.turn_into_assign(op, cind, "fully constant fold format()");
                return 1;
            }
            else if (args_expanded != 0)
            {
                // Partially expanded -- rewrite the instruction. It's actually easier to turn this instruction into a nop and insert a new one. Grab
                // the previous arguments, drop the ones we folded, and replace the format string with our new one.
                List<int> newargs = rop.inst().args().GetRange(op.firstarg(), op.nargs() - args_expanded);
                newargs[1] = rop.add_constant(new ustring(prefix + suffix));
                ustring opname = op.opname();
                rop.turn_into_nop(op, "partial constant fold format()");
                rop.insert_code(opnum, opname, newargs, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges);
                return 1;
            }

            return 0;
        }

        public static int constfold_substr(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=substr(s,start,len) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = rop.opargsym(op, 1);
            Symbol Start = rop.opargsym(op, 2);
            Symbol Len = rop.opargsym(op, 3);
            if (S.is_constant() && Start.is_constant() && Len.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string() && Start.typespec().is_int() && Len.typespec().is_int());
                ustring s = S.get_string();
                int start = Start.get_int();
                int len = Len.get_int();
                // (the runtime's substr computes exactly this: b = start, plus the length if negative, clamped to [0, slen]; the length is clamped
                // to [0, slen]; ustring(s, b, n) is the substring)
                ustring r = OslStrings.from_hash(OslRuntime.osl_substr_ssii(s.hash(), start, len));
                int cind = rop.add_constant(r);
                rop.turn_into_assign(op, cind, "const fold substr");
                return 1;
            }
            return 0;
        }

        public static int constfold_regex_search(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=regex_search(subj,reg) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol Subj = argsym(rop, op, 1);
            Symbol Reg = argsym(rop, op, 2);
            if (op.nargs() == 3  // only the 2-arg version without search results
                && Subj.is_constant() && Reg.is_constant())
            {
                System.Diagnostics.Debug.Assert(Subj.typespec().is_string() && Reg.typespec().is_string());
                ustring s = Subj.get_string();
                ustring r = Reg.get_string();
                Regex reg = new Regex(r.@string(), RegexOptions.ECMAScript);
                int result = reg.IsMatch(s.@string()) ? 1 : 0;
                int cind = rop.add_constant(result);
                rop.turn_into_assign(op, cind, "const fold regex_search");
                return 1;
            }
            return 0;
        }

        private static float clamp(float x, float minv, float maxv)
        {
            if (x < minv)
            {
                return minv;
            }
            else if (x > maxv)
            {
                return maxv;
            }
            else
            {
                return x;
            }
        }

        public static int constfold_clamp(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=clamp(x,min,max) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            Symbol Min = argsym(rop, op, 2);
            Symbol Max = argsym(rop, op, 3);
            if (X.is_constant() && Min.is_constant() && Max.is_constant() && PvtFunctions.equivalent(X.typespec(), Min.typespec())
                && PvtFunctions.equivalent(X.typespec(), Max.typespec()) && (X.typespec().is_float() || X.typespec().is_triple()))
            {
                float[] x = (float[])X.data();
                float[] min = (float[])Min.data();
                float[] max = (float[])Max.data();
                float[] result = new float[3];
                result[0] = clamp(x[0], min[0], max[0]);
                if (X.typespec().is_triple())
                {
                    result[1] = clamp(x[1], min[1], max[1]);
                    result[2] = clamp(x[2], min[2], max[2]);
                }
                int cind = rop.add_constant(X.typespec(), result);
                rop.turn_into_assign(op, cind, "const fold clamp");
                return 1;
            }
            return 0;
        }

        public static int constfold_mix(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=mix(a,b,x) into
            //   R = c             if a,b,x are all are constant
            //   R = a             if x is constant and x == 0
            //   R = b             if x is constant and x == 1
            //   R = a             if a and b are the same (even if not constant)
            //
            Opcode op = rop.inst().ops()[opnum];
            int Rind = rop.oparg(op, 0);
            int Aind = rop.oparg(op, 1);
            int Bind = rop.oparg(op, 2);
            int Xind = rop.oparg(op, 3);
            Symbol R = rop.inst().symbol(Rind);
            Symbol A = rop.inst().symbol(Aind);
            Symbol B = rop.inst().symbol(Bind);
            Symbol X = rop.inst().symbol(Xind);
            // Everything better be a float or triple
            if (!((A.typespec().is_float() || A.typespec().is_triple()) && (B.typespec().is_float() || B.typespec().is_triple())
                  && (X.typespec().is_float() || X.typespec().is_triple())))
            {
                return 0;
            }
            if (X.is_constant() && A.is_constant() && B.is_constant())
            {
                // All three constants
                float[] result = new float[3];
                float[] a = (float[])A.data();
                float[] b = (float[])B.data();
                float[] x = (float[])X.data();
                int atriple = A.typespec().is_triple() ? 1 : 0;
                int btriple = B.typespec().is_triple() ? 1 : 0;
                int xtriple = X.typespec().is_triple() ? 1 : 0;
                bool rtriple = R.typespec().is_triple();
                int ncomps = rtriple ? 3 : 1;
                for (int i = 0; i < ncomps; ++i)
                {
                    float xval = x[xtriple * i];
                    result[i] = (1.0f - xval) * a[atriple * i] + xval * b[btriple * i];
                }
                int cind = rop.add_constant(R.typespec(), result);
                rop.turn_into_assign(op, cind, "const fold mix");
                return 1;
            }

            // Two special cases... X is 0, X is 1
            if (RuntimeOptimizer.is_zero(X))
            {
                // mix(A,B,0) == A
                rop.turn_into_assign(op, Aind, "mix(a,b,0) => a");
                return 1;
            }
            if (RuntimeOptimizer.is_one(X))
            {
                // mix(A,B,1) == B
                rop.turn_into_assign(op, Bind, "mix(a,b,1) => b");
                return 1;
            }

            if (RuntimeOptimizer.is_zero(A) && (!B.connected() || !rop.opt_mix() || rop.optimization_pass() > 2))
            {
                // mix(0,b,x) == b*x, but only do this if b is not connected. Because if b is connected, it may pull on something expensive.
                rop.turn_into_new_op(op, u_mul, Rind, Bind, Xind, "mix(0,b,x) => b*x");
                return 1;
            }

            // mix (a, a, x) is a, regardless of x and even if none are constants
            if (Aind == Bind)
            {
                rop.turn_into_assign(op, Aind, "const fold: mix(a,a,x) -> a");
            }

            // Special sauce: mix(a,b,x) is implemented as a*(1-x)+b*x. But consider cases where x is not constant (thus not foldable), but
            // nonetheless turns out to be 0 or 1 much of the time. If a and b are short local computations, it's not so bad, but if they are shader
            // parameters connected to other layers, this affair may needlessly evaluate other layers for no purpose other than to multiply their
            // results by zero. So we try to ameliorate that case with some extra tests here. N.B. we delay doing this until a few optimization
            // passes in, to give enough time to optimize away the inputs in other ways before introducing the 'if'.
            if (rop.opt_mix() && rop.optimization_pass() > 1 && !X.is_constant() && (A.connected() || B.connected()))
            {
                // A or B are connected, and thus presumed expensive, so turn into:
                //    if (X == 0)  // But eliminate this clause if B not connected
                //        R = A;
                //    else if (X == 1)  // But eliminate this clause if A not connected
                //        R = B;
                //    else
                //        R = A*(1-X) + B*X;
                int if0op = -1;  // Op where we have the 'if' for testing x==0
                int if1op = -1;  // Op where we have the 'if' for testing x==1
                if (B.connected())
                {
                    // Add the test and conditional for X==0, in which case we can just R=A and not have to access B
                    int cond = rop.add_temp(TypeInt);
                    int fzero = rop.add_constant(0.0f);
                    rop.insert_code(opnum++, u_eq, RuntimeOptimizer.InsertRelation.GroupWithNext, cond, Xind, fzero);
                    if0op = opnum;
                    rop.insert_code(opnum++, u_if, RuntimeOptimizer.InsertRelation.GroupWithNext, cond);
                    rop.op(if0op).argreadonly(0);
                    rop.symbol(cond).mark_rw(if0op, true, false);
                    // Add the true (R=A) clause
                    rop.insert_code(opnum++, u_assign, RuntimeOptimizer.InsertRelation.GroupWithNext, Rind, Aind);
                }
                int if0op_false = opnum;  // Where we jump if the 'if x==0' is false
                if (A.connected())
                {
                    // Add the test and conditional for X==1, in which case we can just R=B and not have to access A
                    int cond = rop.add_temp(TypeInt);
                    int fone = rop.add_constant(1.0f);
                    rop.insert_code(opnum++, u_eq, RuntimeOptimizer.InsertRelation.GroupWithNext, cond, Xind, fone);
                    if1op = opnum;
                    rop.insert_code(opnum++, u_if, RuntimeOptimizer.InsertRelation.GroupWithNext, cond);
                    rop.op(if1op).argreadonly(0);
                    rop.symbol(cond).mark_rw(if1op, true, false);
                    // Add the true (R=B) clause
                    rop.insert_code(opnum++, u_assign, RuntimeOptimizer.InsertRelation.GroupWithNext, Rind, Bind);
                }
                int if1op_false = opnum;  // Where we jump if the 'if x==1' is false
                // Add the (R=A*(1-X)+B*X) clause -- always need that
                int one_minus_x = rop.add_temp(X.typespec());
                int temp1 = rop.add_temp(A.typespec());
                int temp2 = rop.add_temp(B.typespec());
                int fone2 = rop.add_constant(1.0f);
                rop.insert_code(opnum++, u_sub, RuntimeOptimizer.InsertRelation.GroupWithNext, one_minus_x, fone2, Xind);
                rop.insert_code(opnum++, u_mul, RuntimeOptimizer.InsertRelation.GroupWithNext, temp1, Aind, one_minus_x);
                rop.insert_code(opnum++, u_mul, RuntimeOptimizer.InsertRelation.GroupWithNext, temp2, Bind, Xind);
                rop.insert_code(opnum++, u_add, RuntimeOptimizer.InsertRelation.GroupWithNext, Rind, temp1, temp2);
                // Now go back and patch the 'if' ops with the right jump addresses
                if (if0op >= 0)
                {
                    rop.op(if0op).set_jump(if0op_false, opnum);
                }
                if (if1op >= 0)
                {
                    rop.op(if1op).set_jump(if1op_false, opnum);
                }
                // The next op is the original mix, make it nop
                rop.turn_into_nop(rop.op(opnum), "smart 'mix'");
                return 1;
            }

            return 0;
        }

        public static int constfold_select(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=select(a,b,cond) into (per component):
            //   R[c] = a          if cond is constant and zero
            //   R[c] = b          if cond is constant and nonzero
            //   R = a             if a == b (even if nothing is constant)
            //
            Opcode op = rop.inst().ops()[opnum];
            // int Rind = rop.oparg(op,0);
            int Aind = rop.oparg(op, 1);
            int Bind = rop.oparg(op, 2);
            int Cind = rop.oparg(op, 3);
            Symbol C = rop.inst().symbol(Cind);

            if (C.is_constant() && RuntimeOptimizer.is_zero(C))
            {
                rop.turn_into_assign(op, Aind, "select(A,B,0) => A");
                return 1;
            }
            if (C.is_constant() && RuntimeOptimizer.is_nonzero(C))
            {
                rop.turn_into_assign(op, Bind, "select(A,B,non-0) => B");
                return 1;
            }
            if (Aind == Bind)
            {
                rop.turn_into_assign(op, Aind, "select(c,a,a) -> a");
                return 1;
            }
            return 0;
        }

        public static int constfold_min(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=min(x,y) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            Symbol Y = argsym(rop, op, 2);
            if (X.is_constant() && Y.is_constant() && PvtFunctions.equivalent(X.typespec(), Y.typespec()))
            {
                if (X.typespec().is_float() || X.typespec().is_triple())
                {
                    float[] x = (float[])X.data();
                    float[] y = (float[])Y.data();
                    float[] result = new float[3];
                    result[0] = (y[0] < x[0]) ? y[0] : x[0];  // std::min
                    if (X.typespec().is_triple())
                    {
                        result[1] = (y[1] < x[1]) ? y[1] : x[1];
                        result[2] = (y[2] < x[2]) ? y[2] : x[2];
                    }
                    int cind = rop.add_constant(X.typespec(), result);
                    rop.turn_into_assign(op, cind, "const fold min");
                    return 1;
                }
                if (X.typespec().is_int())
                {
                    int[] x = (int[])X.data();
                    int[] y = (int[])Y.data();
                    int result = Math.Min(x[0], y[0]);
                    int cind = rop.add_constant(result);
                    rop.turn_into_assign(op, cind, "const fold min");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_max(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=max(x,y) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            Symbol Y = argsym(rop, op, 2);
            if (X.is_constant() && Y.is_constant() && PvtFunctions.equivalent(X.typespec(), Y.typespec()))
            {
                if (X.typespec().is_float() || X.typespec().is_triple())
                {
                    float[] x = (float[])X.data();
                    float[] y = (float[])Y.data();
                    float[] result = new float[3];
                    result[0] = (x[0] < y[0]) ? y[0] : x[0];  // std::max
                    if (X.typespec().is_triple())
                    {
                        result[1] = (x[1] < y[1]) ? y[1] : x[1];
                        result[2] = (x[2] < y[2]) ? y[2] : x[2];
                    }
                    int cind = rop.add_constant(X.typespec(), result);
                    rop.turn_into_assign(op, cind, "const fold max");
                    return 1;
                }
                if (X.typespec().is_int())
                {
                    int[] x = (int[])X.data();
                    int[] y = (int[])Y.data();
                    int result = Math.Max(x[0], y[0]);
                    int cind = rop.add_constant(result);
                    rop.turn_into_assign(op, cind, "const fold max");
                    return 1;
                }
            }
            return 0;
        }

        // The AUTO_DECLFOLDER_FLOAT_OR_TRIPLE macro: a constant folder for a simple function of one argument that can be float or triple and returns
        // the same type as its argument.
        private static int fold_float_or_triple(RuntimeOptimizer rop, int opnum, Func<float, float> impl, string name)
        {
            // Try to turn R=f(x) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            if (X.is_constant() && (X.typespec().is_float() || X.typespec().is_triple()))
            {
                float[] x = (float[])X.data();
                float[] result = new float[3];
                result[0] = impl(x[0]);
                if (X.typespec().is_triple())
                {
                    result[1] = impl(x[1]);
                    result[2] = impl(x[2]);
                }
                int cind = rop.add_constant(X.typespec(), result);
                rop.turn_into_assign(op, cind, "const fold " + name);
                return 1;
            }
            return 0;
        }

        // OIIO::degrees and OIIO::radians (the constants are converted from double to float as the C++ template does)
        private static float degrees(float rad)
        {
            return rad * (float)(180.0 / Math.PI);
        }

        private static float radians(float deg)
        {
            return deg * (float)(Math.PI / 180.0);
        }

        // Built with OSL_FAST_MATH, so cos, sin, acos, asin, exp, exp2, expm1, log, log10, log2 and cbrt use the OIIO fast_ functions
        public static int constfold_sqrt(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.safe_sqrt, "sqrt"); }
        public static int constfold_inversesqrt(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.safe_inversesqrt, "inversesqrt"); }
        public static int constfold_degrees(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, degrees, "degrees"); }
        public static int constfold_radians(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, radians, "radians"); }
        public static int constfold_floor(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, MathF.Floor, "floor"); }
        public static int constfold_ceil(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, MathF.Ceiling, "ceil"); }
        public static int constfold_erf(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_erf, "erf"); }
        public static int constfold_erfc(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_erfc, "erfc"); }
        public static int constfold_logb(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_logb, "logb"); }
        public static int constfold_cos(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_cos, "cos"); }
        public static int constfold_sin(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_sin, "sin"); }
        public static int constfold_acos(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_acos, "acos"); }
        public static int constfold_asin(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_asin, "asin"); }
        public static int constfold_exp(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_exp, "exp"); }
        public static int constfold_exp2(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_exp2, "exp2"); }
        public static int constfold_expm1(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_expm1, "expm1"); }
        public static int constfold_log(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_log, "log"); }
        public static int constfold_log10(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_log10, "log10"); }
        public static int constfold_log2(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_log2, "log2"); }
        public static int constfold_cbrt(RuntimeOptimizer rop, int opnum) { return fold_float_or_triple(rop, opnum, fmath.fast_cbrt, "cbrt"); }

        public static int constfold_pow(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            Symbol Y = argsym(rop, op, 2);

            if (RuntimeOptimizer.is_zero(Y))
            {
                // x^0 == 1
                rop.turn_into_assign_one(op, "pow(x,0) => 1");
                return 1;
            }
            if (RuntimeOptimizer.is_one(Y))
            {
                // x^1 == x
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "pow(x,1) => x");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(X))
            {
                // 0^y == 0
                rop.turn_into_assign_zero(op, "pow(0,x) => 0");
                return 1;
            }
            if (X.is_constant() && Y.is_constant())
            {
                // if x and y are both constant, pre-compute x^y
                float[] x = (float[])X.data();
                float[] y = (float[])Y.data();
                int nxcomps = X.typespec().is_triple() ? 3 : 1;
                int nycomps = Y.typespec().is_triple() ? 3 : 1;
                float[] result = new float[3];
                for (int i = 0; i < nxcomps; ++i)
                {
                    int j = Math.Min(i, nycomps - 1);
                    result[i] = fmath.fast_safe_pow(x[i], y[j]);
                }
                int cind = rop.add_constant(X.typespec(), result);
                rop.turn_into_assign(op, cind, "const fold pow");
                return 1;
            }

            // A few special cases of constant y:
            if (Y.is_constant() && Y.typespec().is_float())
            {
                int resultarg = rop.inst().args()[op.firstarg() + 0];
                int xarg = rop.inst().args()[op.firstarg() + 1];
                float yval = Y.get_float();
                if (yval == 2.0f)
                {
                    rop.turn_into_new_op(op, u_mul, resultarg, xarg, xarg, "pow(x,2) => x*x");
                    return 1;
                }
                if (yval == 0.5f)
                {
                    rop.turn_into_new_op(op, u_sqrt, resultarg, xarg, -1, "pow(x,0.5) => sqrt(x)");
                    return 1;
                }
                if (yval == -0.5f)
                {
                    rop.turn_into_new_op(op, u_inversesqrt, resultarg, xarg, -1, "pow(x,-0.5) => inversesqrt(x)");
                    return 1;
                }
                if (yval == 1.0f / 3.0f)
                {
                    rop.turn_into_new_op(op, u_cbrt, resultarg, xarg, -1, "pow(x,1.0/3.0) => cbrt(x)");
                    return 1;
                }
            }

            return 0;
        }

        public static unsafe int constfold_sincos(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn sincos(const_angle,s,c) into s=sin_a, c = cos_a
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 0);
            if (A.is_constant())
            {
                int sinarg = rop.inst().args()[op.firstarg() + 1];
                int cosarg = rop.inst().args()[op.firstarg() + 2];
                float angle = A.get_float();
                float s, c;
                fmath.fast_sincos(angle, &s, &c);
                // Turn this op into the sin assignment
                rop.turn_into_new_op(op, u_assign, sinarg, rop.add_constant(s), -1, "const fold sincos");
                // And insert a new op for the cos assignment
                int[] args_to_add = new int[] { cosarg, rop.add_constant(c) };
                rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                RuntimeOptimizer.InsertRelation.GroupWithNext);
                return 1;
            }
            return 0;
        }

        public static int constfold_normalize(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=normalize(x) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol X = argsym(rop, op, 1);
            System.Diagnostics.Debug.Assert(X.typespec().is_triple());
            if (X.is_constant())
            {
                Vec3 result = X.get_vec3();
                // Imath::Vec3<float>::normalize()
                float l = OslRuntime.Vec3Length(result);
                if (l != 0.0f)
                {
                    result.x /= l;
                    result.y /= l;
                    result.z /= l;
                }
                int cind = add_constant(rop, X.typespec(), result);
                rop.turn_into_assign(op, cind, "const fold normalize");
                return 1;
            }
            return 0;
        }

        public static int constfold_triple(RuntimeOptimizer rop, int opnum)
        {
            // Turn R=triple(a,b,c) into R=C if the components are all constants
            Opcode op = rop.inst().ops()[opnum];
            System.Diagnostics.Debug.Assert(op.nargs() == 4 || op.nargs() == 5);
            bool using_space = (op.nargs() == 5);
            int space_offset = using_space ? 1 : 0;
            Symbol R = argsym(rop, op, 0);
            Symbol A = argsym(rop, op, 1 + space_offset);
            Symbol B = argsym(rop, op, 2 + space_offset);
            Symbol C = argsym(rop, op, 3 + space_offset);
            if (using_space)
            {
                // If we're using a space name and it's equivalent to "common", just pretend it doesn't exist.
                Symbol Space = argsym(rop, op, 1);
                if (Space.is_constant() && (Space.get_string() == Strings.common || Space.get_string() == rop.shadingsys().commonspace_synonym()))
                {
                    using_space = false;
                }
            }
            if (A.is_constant() && A.typespec().is_float() && B.is_constant() && C.is_constant() && !using_space)
            {
                System.Diagnostics.Debug.Assert(A.typespec().is_float() && B.typespec().is_float() && C.typespec().is_float());
                float[] result = new float[3];
                result[0] = A.get_float();
                result[1] = B.get_float();
                result[2] = C.get_float();
                int cind = rop.add_constant(R.typespec(), result);
                rop.turn_into_assign(op, cind, "triple(const,const,const) => triple constant");
                return 1;
            }
            return 0;
        }

        public static unsafe int constfold_matrix(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int nargs = op.nargs();
            int using_space = rop.opargsym(op, 1).typespec().is_string() ? 1 : 0;
            if (using_space != 0 && nargs > 2 && rop.opargsym(op, 2).typespec().is_string())
            {
                using_space = 2;
            }
            int nfloats = nargs - 1 - using_space;
            System.Diagnostics.Debug.Assert(nfloats == 1 || nfloats == 16 || (nfloats == 0 && using_space == 2));
            if (nargs == 3 && using_space == 2)
            {
                // Try to simplify R=matrix(from,to) in cases of an identify transform: if From and To are the same variable (even if not a
                // constant), or if their values are the same, or if one is "common" and the other is the designated common space synonym.
                Symbol From = argsym(rop, op, 1);
                Symbol To = argsym(rop, op, 2);
                ustring from = From.is_constant() ? From.get_string() : new ustring("$unknown1$");
                ustring to = To.is_constant() ? To.get_string() : new ustring("$unknown2$");
                ustring commonsyn = rop.inst().shadingsys().commonspace_synonym();
                if (ReferenceEquals(From, To) || from == to || ((from == Strings.common && to == commonsyn) || (from == commonsyn && to == Strings.common)))
                {
                    rop.turn_into_assign(op, rop.add_constant(Matrix44.identity), "matrix(spaceA,spaceA) => identity matrix");
                    return 1;
                }
                // Try to simplify R=matrix(from,to) in cases of an constant (but different) names -- do the matrix retrieval now, if not
                // time-varying matrices.
                if (!(From.is_constant() && To.is_constant()))
                {
                    return 0;
                }
                // Shader and object spaces will vary from execution to execution, so we can't optimize those away.
                if (from == Strings.shader || from == Strings.@object || to == Strings.shader || to == Strings.@object)
                {
                    return 0;
                }
                // But whatever spaces are left *may* be optimizable if they are not time-varying.
                RendererServices rs = rop.shadingsys().renderer();
                Matrix44 Mfrom, Mto;
                bool ok = true;
                if (from == Strings.common || from == commonsyn)
                {
                    Mfrom = Matrix44.identity;
                }
                else
                {
                    ok &= rs.get_matrix(rop.shaderglobals(), out Mfrom, from.hash());
                }
                if (to == Strings.common || to == commonsyn)
                {
                    Mto = Matrix44.identity;
                }
                else
                {
                    ok &= rs.get_inverse_matrix(rop.shaderglobals(), out Mto, to.hash());
                }
                if (ok)
                {
                    // The from-to matrix is known and not time-varying, so just turn it into a constant rather than calling getmatrix at execution
                    // time.
                    Matrix44 Mresult = Mfrom * Mto;
                    int cind = rop.add_constant(Mresult);
                    rop.turn_into_assign(op, cind, "const fold matrix");
                    return 1;
                }
            }
            if (using_space == 1 && nfloats == 1)
            {
                // Turn matrix("common",1) info identity matrix.
                Symbol From = argsym(rop, op, 1);
                Symbol Val = argsym(rop, op, 2);
                if (From.is_constant() && Val.is_constant() && Val.get_float() == 1.0f)
                {
                    ustring from = From.get_string();
                    if (from == Strings.common || from == rop.inst().shadingsys().commonspace_synonym())
                    {
                        rop.turn_into_assign(op, rop.add_constant(Matrix44.identity), "matrix(\"common\",1) => identity matrix");
                    }
                }
            }
            if (nfloats == 16 && using_space == 0)
            {
                // Try to turn matrix(...16 float consts...) into just a const matrix assign.
                bool all_const = true;
                float[] M = new float[16];
                for (int i = 0; i < 16; ++i)
                {
                    Symbol Val = argsym(rop, op, 1 + i);
                    if (Val.is_constant())
                    {
                        M[i] = Val.get_float();
                    }
                    else
                    {
                        all_const = false;
                        break;
                    }
                }
                if (all_const)
                {
                    rop.turn_into_assign(op, rop.add_constant(TypeMatrix, M), "const fold matrix");
                    return 1;
                }
            }
            if (nfloats == 1 && using_space == 0)
            {
                // Try to turn matrix(const float) into just a const matrix assign.
                Symbol Val = argsym(rop, op, 1);
                if (Val.is_constant())
                {
                    float val = Val.get_float();
                    Matrix44 M = new Matrix44(val, 0, 0, 0, 0, val, 0, 0, 0, 0, val, 0, 0, 0, 0, val);
                    rop.turn_into_assign(op, rop.add_constant(M), "const fold matrix");
                    return 1;
                }
            }
            return 0;
        }

        public static unsafe int constfold_getmatrix(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=getmatrix(from,to,M) into R=1,M=const if it's an identity transform or if the result is a non-time-varying matrix.
            Opcode op = rop.inst().ops()[opnum];
            Symbol From = argsym(rop, op, 1);
            Symbol To = argsym(rop, op, 2);
            if (!(From.is_constant() && To.is_constant()))
            {
                return 0;
            }
            // OK, From and To are constant strings.
            ustring from = From.get_string();
            ustring to = To.get_string();
            ustring commonsyn = rop.inst().shadingsys().commonspace_synonym();

            // Shader and object spaces will vary from execution to execution, so we can't optimize those away.
            if (from == Strings.shader || from == Strings.@object || to == Strings.shader || to == Strings.@object)
            {
                return 0;
            }

            // But whatever spaces are left *may* be optimizable if they are not time-varying.
            RendererServices rs = rop.shadingsys().renderer();
            Matrix44 Mfrom, Mto;
            bool ok = true;
            if (from == Strings.common || from == commonsyn || from == to)
            {
                Mfrom = Matrix44.identity;
            }
            else
            {
                ok &= rs.get_matrix(rop.shaderglobals(), out Mfrom, from.hash());
            }
            if (to == Strings.common || to == commonsyn || from == to)
            {
                Mto = Matrix44.identity;
            }
            else
            {
                ok &= rs.get_inverse_matrix(rop.shaderglobals(), out Mto, to.hash());
            }
            if (ok)
            {
                // The from-to matrix is known and not time-varying, so just turn it into a constant rather than calling getmatrix at execution time.
                int resultarg = rop.inst().args()[op.firstarg() + 0];
                int dataarg = rop.inst().args()[op.firstarg() + 3];
                // Make data the first argument
                rop.inst().args()[op.firstarg() + 0] = dataarg;
                // Now turn it into an assignment
                Matrix44 Mresult = Mfrom * Mto;
                int cind = rop.add_constant(Mresult);
                rop.turn_into_assign(op, cind, "getmatrix of known matrix");

                // Now insert a new instruction that assigns 1 to the original return result of getmatrix.
                int[] args_to_add = new int[] { resultarg, rop.add_constant(1) };
                rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                RuntimeOptimizer.InsertRelation.GroupWithNext);
                return 1;
            }
            return 0;
        }

        public static int constfold_transform(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn identity transforms into assignments
            Opcode op = rop.inst().ops()[opnum];
            Symbol M = argsym(rop, op, 1);
            if (op.nargs() == 3 && M.typespec().is_matrix() && RuntimeOptimizer.is_one(M))
            {
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 2), "transform by identity");
                return 1;
            }
            if (op.nargs() == 4)
            {
                Symbol T = argsym(rop, op, 2);
                if (M.is_constant() && T.is_constant())
                {
                    System.Diagnostics.Debug.Assert(M.typespec().is_string() && T.typespec().is_string());
                    ustring from = M.get_string();
                    ustring to = T.get_string();
                    ustring syn = rop.shadingsys().commonspace_synonym();
                    if (from == syn)
                    {
                        from = Strings.common;
                    }
                    if (to == syn)
                    {
                        to = Strings.common;
                    }
                    if (from == to)
                    {
                        rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 3), "transform by identity");
                        return 1;
                    }
                }
            }
            return 0;
        }

        public static int constfold_transformc(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            // Symbol &Result = *rop.opargsym (op, 0);
            Symbol From = rop.opargsym(op, 1);
            Symbol To = rop.opargsym(op, 2);
            Symbol C = rop.opargsym(op, 3);

            if (From.is_constant() && To.is_constant())
            {
                ustring from = From.get_string();
                ustring to = To.get_string();
                if (from == Strings.RGB)
                {
                    from = Strings.rgb;
                }
                if (to == Strings.RGB)
                {
                    to = Strings.rgb;
                }
                if (from == to)
                {
                    rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 3), "transformc by identity");
                    return 1;
                }
                if (C.is_constant())
                {
                    Color3 Cin = new Color3(C.get_float(0), C.get_float(1), C.get_float(2));
                    Color3 result = rop.shadingsys().colorsystem().transformc(from.hash(), to.hash(), Cin);
                    rop.turn_into_assign(op, rop.add_constantc(result), "transformc => constant");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_setmessage(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Name = argsym(rop, op, 0);

            // Record that the inst set a message
            if (Name.is_constant())
            {
                System.Diagnostics.Debug.Assert(Name.typespec().is_string());
                rop.register_message(Name.get_string());
            }
            else
            {
                rop.register_unknown_message();
            }

            return 0;
        }

        public static int constfold_getmessage(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int has_source = (op.nargs() == 4) ? 1 : 0;
            if (has_source != 0)
            {
                return 0;  // Don't optimize away sourced getmessage
            }
            Symbol Name = argsym(rop, op, 1 + has_source);
            if (Name.is_constant())
            {
                System.Diagnostics.Debug.Assert(Name.typespec().is_string());
                if (!rop.message_possibly_set(Name.get_string()))
                {
                    // If the messages could not have been sent, get rid of the getmessage op, leave the destination value alone, and assign 0 to
                    // the returned status of getmessage.
                    rop.turn_into_assign_zero(op, "impossible getmessage");
                    return 1;
                }
            }
            return 0;
        }

        // The array of the base type of t (int[], float[] or ustring[]) that holds the value of a type that was written to a byte buffer by a renderer
        // (strings are the ustringhash of the renderer interface, converted back to ustrings)
        private static unsafe Array array_from_bytes(TypeDesc t, byte* buf)
        {
            int n = t.basevalues();
            if (t.basetype == TypeDesc.FLOAT)
            {
                float[] f = new float[n];
                for (int i = 0; i < n; ++i)
                {
                    f[i] = ((float*)buf)[i];
                }
                return f;
            }
            else if (t.basetype == TypeDesc.INT)
            {
                int[] v = new int[n];
                for (int i = 0; i < n; ++i)
                {
                    v[i] = ((int*)buf)[i];
                }
                return v;
            }
            else if (t.basetype == TypeDesc.STRING)
            {
                ustring[] s = new ustring[n];
                for (int i = 0; i < n; ++i)
                {
                    s[i] = OslStrings.from_hash(((ulong*)buf)[i]);
                }
                return s;
            }
            throw new InvalidOperationException("unsupported type for a folded constant");
        }

        public static unsafe int constfold_getattribute(RuntimeOptimizer rop, int opnum)
        {
            if (!rop.shadingsys().fold_getattribute())
            {
                return 0;
            }

            // getattribute() has eight "flavors":
            //   * getattribute (attribute_name, value)
            //   * getattribute (attribute_name, value[])
            //   * getattribute (attribute_name, index, value)
            //   * getattribute (attribute_name, index, value[])
            //   * getattribute (object, attribute_name, value)
            //   * getattribute (object, attribute_name, value[])
            //   * getattribute (object, attribute_name, index, value)
            //   * getattribute (object, attribute_name, index, value[])
            Opcode op = rop.inst().ops()[opnum];
            int nargs = op.nargs();
            System.Diagnostics.Debug.Assert(nargs >= 3 && nargs <= 5);
            bool array_lookup = rop.opargsym(op, nargs - 2).typespec().is_int();
            bool object_lookup = rop.opargsym(op, 2).typespec().is_string() && nargs >= 4;
            int object_slot = object_lookup ? 1 : 0;
            int attrib_slot = object_slot + 1;
            int index_slot = nargs - 2;
            int dest_slot = nargs - 1;

            //    Symbol& Result      = *rop.opargsym (op, 0);
            Symbol ObjectName = rop.opargsym(op, object_slot);  // only valid if object_slot is true
            Symbol Attribute = rop.opargsym(op, attrib_slot);
            Symbol Index = rop.opargsym(op, index_slot);  // only valid if array_lookup is true
            Symbol Destination = rop.opargsym(op, dest_slot);

            if (!Attribute.is_constant() || (object_lookup && !ObjectName.is_constant()) || (array_lookup && !Index.is_constant()))
            {
                return 0;  // Non-constant things prevent a fold
            }
            if (Destination.typespec().is_array())
            {
                return 0;  // Punt on arrays for now
            }

            ustring attr_name = Attribute.get_string();
            const int maxbufsize = 1024;
            byte* buf = stackalloc byte[maxbufsize];
            TypeDesc attr_type = Destination.typespec().simpletype();
            if (attr_type.size() > maxbufsize)
            {
                return 0;  // Don't constant fold humongous things
            }

            bool found = false;
            Array attr_data = null;

            // Check global things first
            if (attr_name.@string() == "osl:version" && attr_type == TypeInt)
            {
                attr_data = new int[] { OSLCompilerImpl.OSL_LIBRARY_VERSION_CODE };
                found = true;
            }
            else if (attr_name.@string() == "shader:shadername" && attr_type == TypeString)
            {
                attr_data = new ustring[] { new ustring(rop.inst().shadername()) };
                found = true;
            }
            else if (attr_name.@string() == "shader:layername" && attr_type == TypeString)
            {
                attr_data = new ustring[] { rop.inst().layername() };
                found = true;
            }
            else if (attr_name.@string() == "shader:groupname" && attr_type == TypeString)
            {
                attr_data = new ustring[] { rop.group().name() };
                found = true;
            }

            if (!found)
            {
                // If the object name is not supplied, it implies that we are supposed to search the shaded object first, then if that fails, the
                // scene-wide namespace. We can't do that yet, have to wait until shade time.
                ustring obj_name = new ustring("");
                if (object_lookup)
                {
                    obj_name = ObjectName.get_string();
                }
                if (obj_name.empty())
                {
                    return 0;
                }

                found = array_lookup ? rop.renderer().get_array_attribute(null, false, obj_name.hash(), attr_type, attr_name.hash(), Index.get_int(), buf)
                                     : rop.renderer().get_attribute(null, false, obj_name.hash(), attr_type, attr_name.hash(), buf);
                if (found)
                {
                    // (the renderer interface hands back strings as ustringhash, which the folded constant needs as a ustring)
                    attr_data = array_from_bytes(attr_type, buf);
                }
            }

            if (found)
            {
                // Now we turn the existing getattribute op into this for success:
                //       assign result 1
                //       assign data [retrieved values]
                // but if it fails, don't change anything, because we want it to issue errors at runtime.

                // Make the data destination be the first argument
                int oldresultarg = rop.inst().args()[op.firstarg() + 0];
                int dataarg = rop.inst().args()[op.firstarg() + dest_slot];
                rop.inst().args()[op.firstarg() + 0] = dataarg;
                // Now turn it into an assignment
                int cind = rop.add_constant(new TypeSpec(attr_type), attr_data);
                rop.turn_into_assign(op, cind, "const fold getattribute");
                // Now insert a new instruction that assigns 1 to the original return result of getattribute.
                int[] args_to_add = new int[] { oldresultarg, rop.add_constant(1) };
                rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                RuntimeOptimizer.InsertRelation.GroupWithNext);
                return 1;
            }
            else
            {
                return 0;
            }
        }

        public static unsafe int constfold_gettextureinfo(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];

            // FIXME: For now, punt on constant folding the variety of gettextureinfo that is passed texture coordinates. Eventually, if we can
            // determine that the filename is constant and is known to not be UDIM, we can fall back to the other case.
            bool use_coords = (op.nargs() == 6);
            if (use_coords)
            {
                return 0;
            }

            Symbol Result = argsym(rop, op, 0);
            Symbol Filename = argsym(rop, op, 1);
            Symbol Dataname = argsym(rop, op, use_coords ? 4 : 2);
            Symbol Data = argsym(rop, op, use_coords ? 5 : 3);
            System.Diagnostics.Debug.Assert(Result.typespec().is_int() && Filename.typespec().is_string() && Dataname.typespec().is_string());

            if (Filename.is_constant() && Dataname.is_constant())
            {
                ustring filename = Filename.get_string();
                ustring dataname = Dataname.get_string();
                TypeDesc t = Data.typespec().simpletype();
                byte[] mydata = new byte[Math.Max((int)t.size(), 8)];
                // FIXME(ptex) -- exclude folding of ptex, since these things can vary per face.
                ulong em = 0;
                bool result;
                fixed (byte* pdata = mydata)
                {
                    result = rop.renderer().get_texture_info(filename.hash(), null, rop.shadingcontext().texture_thread_info(), rop.shaderglobals(),
                                                             0 /* TODO: subimage? */, dataname.hash(), t, pdata, &em);
                }
                // Results from RendererServices for TypeDesc::STRING will actaully be a ustringhash as that is what we expect during execution. We
                // will need to convert it back to a regular ustring before continuing since this is the compile/optimization phase.
                ustring errormessage = OslStrings.from_hash(em);
                // Now we turn
                //       gettextureinfo result filename dataname data
                // into this for success:
                //       assign data [retrieved values]
                //       assign result 1
                // into this for failure:
                //       error "%s" errormessage
                //       assign result 0
                if (result)
                {
                    int oldresultarg = rop.inst().args()[op.firstarg() + 0];
                    int dataarg = rop.inst().args()[op.firstarg() + 3];
                    // Make data the first argument
                    rop.inst().args()[op.firstarg() + 0] = dataarg;
                    // Now turn it into an assignment
                    Array constdata;
                    fixed (byte* pdata = mydata)
                    {
                        constdata = array_from_bytes(Data.typespec().simpletype(), pdata);
                    }
                    int cind = rop.add_constant(Data.typespec(), constdata);
                    rop.turn_into_assign(op, cind, "const fold gettextureinfo");

                    // Now insert a new instruction that assigns 1 to the original return result of gettextureinfo.
                    int[] args_to_add = new int[] { oldresultarg, rop.add_constant(1) };
                    rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                    RuntimeOptimizer.InsertRelation.GroupWithNext);
                    return 1;
                }
                else
                {
                    // Constant fold to 0
                    rop.turn_into_assign_zero(op, "const fold gettextureinfo");
                    if (errormessage.size() != 0)
                    {
                        // display the error message if control flow ever reaches here
                        int[] args_to_add = new int[] { rop.add_constant(u_fmterror), rop.add_constant(errormessage) };
                        rop.insert_code(opnum, u_error, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                        RuntimeOptimizer.InsertRelation.GroupWithNext);
                        Opcode newop = rop.inst().ops()[opnum];
                        newop.argreadonly(0);
                        newop.argreadonly(1);
                    }
                    return 1;
                }
            }
            return 0;
        }

        // The CHECK(field, ctype, osltype) macro of constfold_texture, for a field that is a float (isfloat) or an int: if the optional argument is
        // given the default value, set elide, otherwise remember that the field was set.
        private static void texture_check(Symbol Value, TypeDesc valuetype, bool osl_is_float, float optvalue, ref bool field_set, ref bool elide)
        {
            object data = Value.data();
            if (osl_is_float)
            {
                if (valuetype == new TypeDesc(TypeDesc.FLOAT) && ((float[])data)[0] == optvalue)
                {
                    elide = true;
                }
                else if (valuetype == new TypeDesc(TypeDesc.INT) && ((int[])data)[0] == (int)optvalue)
                {
                    elide = true;
                }
                else
                {
                    field_set = true;
                }
            }
            else
            {
                if (valuetype == new TypeDesc(TypeDesc.INT) && ((int[])data)[0] == (int)optvalue)
                {
                    elide = true;
                }
                else
                {
                    field_set = true;
                }
            }
        }

        // The tail of CHECK_str(field, float, FLOAT): the unprefixed name (width, blur) sets all of s, t and r
        private static void texture_check_all(Symbol Value, TypeDesc valuetype, float opts, float optt, float optr, ref bool sset, ref bool tset,
                                              ref bool rset, ref bool elide)
        {
            object data = Value.data();
            if (valuetype == new TypeDesc(TypeDesc.FLOAT))
            {
                float v = ((float[])data)[0];
                if (v == opts && v == optt && v == optr)
                {
                    elide = true;
                }
                else
                {
                    sset = true;
                    tset = true;
                    rset = true;
                }
            }
            else if (valuetype == new TypeDesc(TypeDesc.INT))
            {
                int v = ((int[])data)[0];
                if (v == (int)opts && v == (int)optt && v == (int)optr)
                {
                    elide = true;
                }
                else
                {
                    sset = true;
                    tset = true;
                    rset = true;
                }
            }
        }

        // The interpolation mode names of the "interp" optional argument (tex_interp_to_code of oslexec_pvt.h)
        private static int tex_interp_to_code(ustring modename)
        {
            int mode = -1;
            if (modename == Strings.smartcubic)
            {
                mode = TextureOpt.InterpSmartBicubic;
            }
            else if (modename == Strings.linear)
            {
                mode = TextureOpt.InterpBilinear;
            }
            else if (modename == Strings.cubic)
            {
                mode = TextureOpt.InterpBicubic;
            }
            else if (modename == Strings.closest)
            {
                mode = TextureOpt.InterpClosest;
            }
            return mode;
        }

        // texture -- we can eliminate a lot of superfluous setting of optional parameters to their default values.
        public static int constfold_texture(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            // Symbol &Result = *rop.opargsym (op, 0);
            // Symbol &Filename = *rop.opargsym (op, 1);
            // Symbol &S = *rop.opargsym (op, 2);
            // Symbol &T = *rop.opargsym (op, 3);

            int first_optional_arg = 4;
            if (op.nargs() > 4 && rop.opargsym(op, 4).typespec().is_float())
            {
                // user_derivs = true;
                first_optional_arg = 8;
                System.Diagnostics.Debug.Assert(rop.opargsym(op, 5).typespec().is_float());
                System.Diagnostics.Debug.Assert(rop.opargsym(op, 6).typespec().is_float());
                System.Diagnostics.Debug.Assert(rop.opargsym(op, 7).typespec().is_float());
            }

            TextureOpt opt = TextureOpt.make_default();  // So we can check the defaults
            bool swidth_set = false, twidth_set = false, rwidth_set = false;
            bool sblur_set = false, tblur_set = false, rblur_set = false;
            bool swrap_set = false, twrap_set = false, rwrap_set = false;
            bool firstchannel_set = false, fill_set = false, interp_set = false;
            bool any_elided = false;
            for (int i = first_optional_arg; i < op.nargs() - 1; i += 2)
            {
                Symbol Name = rop.opargsym(op, i);
                Symbol Value = rop.opargsym(op, i + 1);
                System.Diagnostics.Debug.Assert(Name.typespec().is_string());
                if (Name.is_constant() && Value.is_constant())
                {
                    ustring name = Name.get_string();
                    bool elide = false;
                    TypeDesc valuetype = Value.typespec().simpletype();

                    // (the chain below is the CHECK_str(width), CHECK_str(blur), CHECK(firstchannel), CHECK(fill), wrap and interp tests of the C++)
                    if (name == Strings.swidth && !swidth_set)
                    {
                        texture_check(Value, valuetype, true, opt.swidth, ref swidth_set, ref elide);
                    }
                    else if (name == Strings.twidth && !twidth_set)
                    {
                        texture_check(Value, valuetype, true, opt.twidth, ref twidth_set, ref elide);
                    }
                    else if (name == Strings.rwidth && !rwidth_set)
                    {
                        texture_check(Value, valuetype, true, opt.rwidth, ref rwidth_set, ref elide);
                    }
                    else if (name == Strings.width && !swidth_set && !twidth_set && !rwidth_set)
                    {
                        texture_check_all(Value, valuetype, opt.swidth, opt.twidth, opt.rwidth, ref swidth_set, ref twidth_set, ref rwidth_set, ref elide);
                    }
                    else if (name == Strings.sblur && !sblur_set)
                    {
                        texture_check(Value, valuetype, true, opt.sblur, ref sblur_set, ref elide);
                    }
                    else if (name == Strings.tblur && !tblur_set)
                    {
                        texture_check(Value, valuetype, true, opt.tblur, ref tblur_set, ref elide);
                    }
                    else if (name == Strings.rblur && !rblur_set)
                    {
                        texture_check(Value, valuetype, true, opt.rblur, ref rblur_set, ref elide);
                    }
                    else if (name == Strings.blur && !sblur_set && !tblur_set && !rblur_set)
                    {
                        texture_check_all(Value, valuetype, opt.sblur, opt.tblur, opt.rblur, ref sblur_set, ref tblur_set, ref rblur_set, ref elide);
                    }
                    else if (name == Strings.firstchannel && !firstchannel_set)
                    {
                        texture_check(Value, valuetype, false, opt.firstchannel, ref firstchannel_set, ref elide);
                    }
                    else if (name == Strings.fill && !fill_set)
                    {
                        texture_check(Value, valuetype, true, opt.fill, ref fill_set, ref elide);
                    }
                    else if ((name == Strings.wrap || name == Strings.swrap || name == Strings.twrap || name == Strings.rwrap)
                             && valuetype == new TypeDesc(TypeDesc.STRING))
                    {
                        // Special trick is needed for wrap modes because the input is a string but the field we're setting is an int enum. N.B. the
                        // C++ then runs the int-typed CHECKs against the type of the string argument, which never matches: the wrap modes are never
                        // elided, and the s, t, r wrap flags are set. (For the unprefixed 'wrap' nothing happens.)
                        if (name == Strings.swrap && !swrap_set)
                        {
                            swrap_set = true;
                        }
                        else if (name == Strings.twrap && !twrap_set)
                        {
                            twrap_set = true;
                        }
                        else if (name == Strings.rwrap && !rwrap_set)
                        {
                            rwrap_set = true;
                        }
                    }
                    // Cases that don't fit the pattern
                    else if (name == Strings.interp && !interp_set)
                    {
                        if (valuetype == new TypeDesc(TypeDesc.STRING) && tex_interp_to_code(((ustring[])Value.data())[0]) == opt.interpmode)
                        {
                            elide = true;
                        }
                        else
                        {
                            interp_set = true;
                        }
                    }

                    if (elide)
                    {
                        // Just turn the param name into empty string and it will be skipped.
                        int cind = rop.add_constant(new ustring(""));
                        rop.inst().args()[op.firstarg() + i] = cind;
                        rop.inst().args()[op.firstarg() + i + 1] = cind;
                        any_elided = true;
                    }
                }
            }
            return any_elided ? 1 : 0;
        }
    }
}
