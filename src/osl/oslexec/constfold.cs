// OSL port: liboslexec/constfold.cpp (first part): the constant folders of the arithmetic, comparison, logic, array, component and simple string ops.
//
// Every folder is a static method constfold_<name>(RuntimeOptimizer rop, int opnum) that returns the number of changes it made (the OpFolder delegate of
// pvt.cs); register_folders() (constfold_register.cs) enters them in OpFolderTable under the folder names of the op table.
//
// The C++ passes the constant it creates as a pointer to its value; here the value is a managed array (see RuntimeOptimizer.add_constant). Symbols are
// objects, so the comparisons of symbol addresses (&A == &B) are reference comparisons.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL.pvt
{
    public static partial class ConstFold
    {
        // names of ops we'll be using frequently
        private static readonly ustring u_aassign = new ustring("aassign");
        private static readonly ustring u_add = new ustring("add");
        private static readonly ustring u_assign = new ustring("assign");
        private static readonly ustring u_cbrt = new ustring("cbrt");
        private static readonly ustring u_cell = new ustring("cell");
        private static readonly ustring u_cellnoise = new ustring("cellnoise");
        private static readonly ustring u_compassign = new ustring("compassign");
        private static readonly ustring u_eq = new ustring("eq");
        private static readonly ustring u_error = new ustring("error");
        private static readonly ustring u_fmt_range_check = new ustring("Index [%d] out of range %s[0..%d]: %s:%d (group %s, layer %d %s, shader %s)");
        private static readonly ustring u_fmterror = new ustring("%s");
        private static readonly ustring u_if = new ustring("if");
        private static readonly ustring u_inversesqrt = new ustring("inversesqrt");
        private static readonly ustring u_mul = new ustring("mul");
        private static readonly ustring u_mxcompassign = new ustring("mxcompassign");
        private static readonly ustring u_nop = new ustring("nop");
        private static readonly ustring u_return = new ustring("return");
        private static readonly ustring u_sqrt = new ustring("sqrt");
        private static readonly ustring u_sub = new ustring("sub");

        // The i-th argument symbol of an op (rop.inst()->argsymbol(op.firstarg() + i))
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Symbol argsym(RuntimeOptimizer rop, Opcode op, int i)
        {
            return rop.inst().argsymbol(op.firstarg() + i);
        }

        // add_constant of a Vec3 value held in the given (triple or float) type
        private static int add_constant(RuntimeOptimizer rop, TypeSpec type, Vec3 v)
        {
            return rop.add_constant(type, new float[] { v.x, v.y, v.z });
        }

        // The constants hold the same type and the same bits (the memcmp of the C++)
        private static bool bits_equal(Symbol A, Symbol B)
        {
            return RuntimeOptimizer.data_equal(A.data(), 0, B.data(), 0, A.typespec().simpletype().basevalues());
        }

        public static bool equal_consts(Symbol A, Symbol B)
        {
            return ReferenceEquals(A, B) || (PvtFunctions.equivalent(A.typespec(), B.typespec()) && bits_equal(A, B));
        }

        public static bool unequal_consts(Symbol A, Symbol B)
        {
            return PvtFunctions.equivalent(A.typespec(), B.typespec()) && !bits_equal(A, B);
        }

        public static int constfold_none(RuntimeOptimizer rop, int opnum)
        {
            return 0;
        }

        public static int constfold_add(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (RuntimeOptimizer.is_zero(A))
            {
                // R = 0 + B  =>   R = B
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 2), "0 + A => A");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(B))
            {
                // R = A + 0   =>   R = A
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "A + 0 => A");
                return 1;
            }
            if (A.is_constant() && B.is_constant())
            {
                if (A.typespec().is_int() && B.typespec().is_int())
                {
                    int cind = rop.add_constant(unchecked(A.get_int() + B.get_int()));
                    rop.turn_into_assign(op, cind, "const + const");
                    return 1;
                }
                else if (A.typespec().is_float() && B.typespec().is_float())
                {
                    int cind = rop.add_constant(A.get_float() + B.get_float());
                    rop.turn_into_assign(op, cind, "const + const");
                    return 1;
                }
                else if (A.typespec().is_triple_or_float() && B.typespec().is_triple_or_float())
                {
                    Symbol R = argsym(rop, op, 0);
                    Vec3 result = A.coerce_vec3() + B.coerce_vec3();
                    int cind = add_constant(rop, R.typespec(), result);
                    rop.turn_into_assign(op, cind, "const + const");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_sub(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (RuntimeOptimizer.is_zero(B))
            {
                // R = A - 0   =>   R = A
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "A - 0 => A");
                return 1;
            }
            // R = A - B, if both are constants, =>  R = C
            if (A.is_constant() && B.is_constant())
            {
                if (A.typespec().is_int() && B.typespec().is_int())
                {
                    int cind = rop.add_constant(unchecked(A.get_int() - B.get_int()));
                    rop.turn_into_assign(op, cind, "const - const");
                    return 1;
                }
                else if (A.typespec().is_float() && B.typespec().is_float())
                {
                    int cind = rop.add_constant(A.get_float() - B.get_float());
                    rop.turn_into_assign(op, cind, "const - const");
                    return 1;
                }
                else if (A.typespec().is_triple_or_float() && B.typespec().is_triple_or_float())
                {
                    Symbol R = argsym(rop, op, 0);
                    Vec3 result = A.coerce_vec3() - B.coerce_vec3();
                    int cind = add_constant(rop, R.typespec(), result);
                    rop.turn_into_assign(op, cind, "const - const");
                    return 1;
                }
            }
            // R = A - A  =>  R = 0    even if not constant!
            if (ReferenceEquals(A, B))
            {
                rop.turn_into_assign_zero(op, "A - A => 0");
            }
            return 0;
        }

        public static int constfold_mul(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (RuntimeOptimizer.is_one(A))
            {
                // R = 1 * B  =>   R = B
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 2), "1 * A => A");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(A))
            {
                // R = 0 * B  =>   R = 0
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "0 * A => 0");
                return 1;
            }
            if (RuntimeOptimizer.is_one(B))
            {
                // R = A * 1   =>   R = A
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "A * 1 => A");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(B))
            {
                // R = A * 0   =>   R = 0
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 2), "A * 0 => 0");
                return 1;
            }
            if (A.is_constant() && B.is_constant())
            {
                if (A.typespec().is_int() && B.typespec().is_int())
                {
                    int cind = rop.add_constant(unchecked(A.get_int() * B.get_int()));
                    rop.turn_into_assign(op, cind, "const * const");
                    return 1;
                }
                else if (A.typespec().is_float() && B.typespec().is_float())
                {
                    int cind = rop.add_constant(A.get_float() * B.get_float());
                    rop.turn_into_assign(op, cind, "const * const");
                    return 1;
                }
                else if (A.typespec().is_triple_or_float() && B.typespec().is_triple_or_float())
                {
                    Symbol R = argsym(rop, op, 0);
                    Vec3 result = A.coerce_vec3() * B.coerce_vec3();
                    int cind = add_constant(rop, R.typespec(), result);
                    rop.turn_into_assign(op, cind, "const * const");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_div(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (RuntimeOptimizer.is_one(B))
            {
                // R = A / 1   =>   R = A
                rop.turn_into_assign(op, rop.inst().arg(op.firstarg() + 1), "A / 1 => A");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(B) && (B.typespec().is_float() || B.typespec().is_triple() || B.typespec().is_int()))
            {
                // R = A / 0   =>   R = 0      because of OSL div by zero rule
                rop.turn_into_assign_zero(op, "A / 0 => 0 (by OSL division rules)");
                return 1;
            }
            if (A.is_constant() && B.is_constant())
            {
                int cind = -1;
                if (A.typespec().is_int() && B.typespec().is_int())
                {
                    int a = A.get_int();
                    int b = B.get_int();
                    // (int.MinValue / -1 overflows; the C++ is undefined there)
                    cind = rop.add_constant((a == int.MinValue && b == -1) ? a : a / b);
                }
                else if (A.typespec().is_int_or_float() && B.typespec().is_int_or_float())
                {
                    cind = rop.add_constant(A.coerce_float() / B.coerce_float());
                }
                else if (A.typespec().is_triple_or_float() && B.typespec().is_triple_or_float())
                {
                    Symbol R = argsym(rop, op, 0);
                    Vec3 result = A.coerce_vec3() / B.coerce_vec3();
                    cind = add_constant(rop, R.typespec(), result);
                }
                if (cind >= 0)
                {
                    rop.turn_into_assign(op, cind, "const / const");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_mod(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);

            if (RuntimeOptimizer.is_zero(A))
            {
                // R = 0 % B  =>   R = 0
                rop.turn_into_assign_zero(op, "0 % A => 0");
                return 1;
            }
            if (RuntimeOptimizer.is_zero(B))
            {
                // R = A % 0   =>   R = 0
                rop.turn_into_assign_zero(op, "A % 0 => 0");
                return 1;
            }
            if (A.is_constant() && B.is_constant() && A.typespec().is_int() && B.typespec().is_int())
            {
                int a = A.get_int();
                int b = B.get_int();
                int cind = rop.add_constant(b != 0 ? (b == -1 ? 0 : a % b) : 0);
                rop.turn_into_assign(op, cind, "const % const");
                return 1;
            }
            return 0;
        }

        public static int constfold_dot(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);

            // Dot with (0,0,0) -> 0
            if (RuntimeOptimizer.is_zero(A) || RuntimeOptimizer.is_zero(B))
            {
                rop.turn_into_assign_zero(op, "dot(a,(0,0,0)) => 0");
                return 1;
            }

            // dot(const,const) -> const
            if (A.is_constant() && B.is_constant())
            {
                System.Diagnostics.Debug.Assert(A.typespec().is_triple() && B.typespec().is_triple());
                float result = A.get_vec3().dot(B.get_vec3());
                int cind = rop.add_constant(result);
                rop.turn_into_assign(op, cind, "dot(const,const)");
                return 1;
            }

            return 0;
        }

        public static int constfold_neg(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            if (A.is_constant())
            {
                if (A.typespec().is_int())
                {
                    int cind = rop.add_constant(A.typespec(), new int[] { unchecked(-A.get_int()) });
                    rop.turn_into_assign(op, cind, "-const");
                    return 1;
                }
                else if (A.typespec().is_float())
                {
                    int cind = rop.add_constant(A.typespec(), new float[] { -A.get_float() });
                    rop.turn_into_assign(op, cind, "-const");
                    return 1;
                }
                else if (A.typespec().is_triple())
                {
                    Vec3 result = -A.get_vec3();
                    int cind = add_constant(rop, A.typespec(), result);
                    rop.turn_into_assign(op, cind, "-const");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_abs(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            if (A.is_constant())
            {
                if (A.typespec().is_int())
                {
                    int v = A.get_int();
                    int cind = rop.add_constant(A.typespec(), new int[] { v < 0 ? unchecked(-v) : v });
                    rop.turn_into_assign(op, cind, "abs(const)");
                    return 1;
                }
                else if (A.typespec().is_float())
                {
                    int cind = rop.add_constant(A.typespec(), new float[] { Math.Abs(A.get_float()) });
                    rop.turn_into_assign(op, cind, "abs(const)");
                    return 1;
                }
                else if (A.typespec().is_triple())
                {
                    Vec3 result = A.get_vec3();
                    result.x = Math.Abs(result.x);
                    result.y = Math.Abs(result.y);
                    result.z = Math.Abs(result.z);
                    int cind = add_constant(rop, A.typespec(), result);
                    rop.turn_into_assign(op, cind, "abs(const)");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_eq(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (A.is_constant() && B.is_constant())
            {
                bool val = false;
                if (PvtFunctions.equivalent(A.typespec(), B.typespec()))
                {
                    val = equal_consts(A, B);
                }
                else if (A.typespec().is_float() && B.typespec().is_int())
                {
                    val = (A.get_float() == B.get_int());
                }
                else if (A.typespec().is_int() && B.typespec().is_float())
                {
                    val = (A.get_int() == B.get_float());
                }
                else
                {
                    return 0;  // unhandled cases
                }
                // Turn the 'eq R A B' into 'assign R X' where X is 0 or 1.
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const == const");
                return 1;
            }
            return 0;
        }

        public static int constfold_neq(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (A.is_constant() && B.is_constant())
            {
                bool val = false;
                if (PvtFunctions.equivalent(A.typespec(), B.typespec()))
                {
                    val = !equal_consts(A, B);
                }
                else if (A.typespec().is_float() && B.typespec().is_int())
                {
                    val = (A.get_float() != B.get_int());
                }
                else if (A.typespec().is_int() && B.typespec().is_float())
                {
                    val = (A.get_int() != B.get_float());
                }
                else
                {
                    return 0;  // unhandled case
                }
                // Turn the 'neq R A B' into 'assign R X' where X is 0 or 1.
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const != const");
                return 1;
            }
            return 0;
        }

        public static int constfold_lt(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            TypeSpec ta = A.typespec();
            TypeSpec tb = B.typespec();
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'leq R A B' into 'assign R X' where X is 0 or 1.
                bool val = false;
                if (ta.is_float() && tb.is_float())
                {
                    val = (A.get_float() < B.get_float());
                }
                else if (ta.is_float() && tb.is_int())
                {
                    val = (A.get_float() < B.get_int());
                }
                else if (ta.is_int() && tb.is_float())
                {
                    val = (A.get_int() < B.get_float());
                }
                else if (ta.is_int() && tb.is_int())
                {
                    val = (A.get_int() < B.get_int());
                }
                else
                {
                    return 0;  // unhandled case
                }
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const < const");
                return 1;
            }
            return 0;
        }

        public static int constfold_le(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            TypeSpec ta = A.typespec();
            TypeSpec tb = B.typespec();
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'leq R A B' into 'assign R X' where X is 0 or 1.
                bool val = false;
                if (ta.is_float() && tb.is_float())
                {
                    val = (A.get_float() <= B.get_float());
                }
                else if (ta.is_float() && tb.is_int())
                {
                    val = (A.get_float() <= B.get_int());
                }
                else if (ta.is_int() && tb.is_float())
                {
                    val = (A.get_int() <= B.get_float());
                }
                else if (ta.is_int() && tb.is_int())
                {
                    val = (A.get_int() <= B.get_int());
                }
                else
                {
                    return 0;  // unhandled case
                }
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const <= const");
                return 1;
            }
            return 0;
        }

        public static int constfold_gt(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            TypeSpec ta = A.typespec();
            TypeSpec tb = B.typespec();
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'gt R A B' into 'assign R X' where X is 0 or 1.
                bool val = false;
                if (ta.is_float() && tb.is_float())
                {
                    val = (A.get_float() > B.get_float());
                }
                else if (ta.is_float() && tb.is_int())
                {
                    val = (A.get_float() > B.get_int());
                }
                else if (ta.is_int() && tb.is_float())
                {
                    val = (A.get_int() > B.get_float());
                }
                else if (ta.is_int() && tb.is_int())
                {
                    val = (A.get_int() > B.get_int());
                }
                else
                {
                    return 0;  // unhandled case
                }
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const > const");
                return 1;
            }
            return 0;
        }

        public static int constfold_ge(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            TypeSpec ta = A.typespec();
            TypeSpec tb = B.typespec();
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'leq R A B' into 'assign R X' where X is 0 or 1.
                bool val = false;
                if (ta.is_float() && tb.is_float())
                {
                    val = (A.get_float() >= B.get_float());
                }
                else if (ta.is_float() && tb.is_int())
                {
                    val = (A.get_float() >= B.get_int());
                }
                else if (ta.is_int() && tb.is_float())
                {
                    val = (A.get_int() >= B.get_float());
                }
                else if (ta.is_int() && tb.is_int())
                {
                    val = (A.get_int() >= B.get_int());
                }
                else
                {
                    return 0;  // unhandled case
                }
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const >= const");
                return 1;
            }
            return 0;
        }

        public static int constfold_or(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (A.is_constant() && B.is_constant())
            {
                System.Diagnostics.Debug.Assert(A.typespec().is_int() && B.typespec().is_int());
                bool val = A.get_int() != 0 || B.get_int() != 0;
                // Turn the 'or R A B' into 'assign R X' where X is 0 or 1.
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const || const");
                return 1;
            }
            return 0;
        }

        public static int constfold_and(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol B = argsym(rop, op, 2);
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'and R A B' into 'assign R X' where X is 0 or 1.
                System.Diagnostics.Debug.Assert(A.typespec().is_int() && B.typespec().is_int());
                bool val = A.get_int() != 0 && B.get_int() != 0;
                int cind = rop.add_constant(val ? 1 : 0);
                rop.turn_into_assign(op, cind, "const && const");
                return 1;
            }
            return 0;
        }

        public static int constfold_bitand(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.op(opnum);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'bitand R A B' into 'assign R X'.
                System.Diagnostics.Debug.Assert(A.typespec().is_int() && B.typespec().is_int());
                int cind = rop.add_constant(A.get_int() & B.get_int());
                rop.turn_into_assign(op, cind, "const & const");
                return 1;
            }
            return 0;
        }

        public static int constfold_bitor(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.op(opnum);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'bitor R A B' into 'assign R X'.
                System.Diagnostics.Debug.Assert(A.typespec().is_int() && B.typespec().is_int());
                int cind = rop.add_constant(A.get_int() | B.get_int());
                rop.turn_into_assign(op, cind, "const | const");
                return 1;
            }
            return 0;
        }

        public static int constfold_xor(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.op(opnum);
            Symbol A = rop.opargsym(op, 1);
            Symbol B = rop.opargsym(op, 2);
            if (A.is_constant() && B.is_constant())
            {
                // Turn the 'xor R A B' into 'assign R X'.
                System.Diagnostics.Debug.Assert(A.typespec().is_int() && B.typespec().is_int());
                int cind = rop.add_constant(A.get_int() ^ B.get_int());
                rop.turn_into_assign(op, cind, "const ^ const");
                return 1;
            }
            return 0;
        }

        public static int constfold_compl(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.op(opnum);
            Symbol A = rop.opargsym(op, 1);
            if (A.is_constant())
            {
                // Turn the 'compl R A' into 'assign R X'.
                System.Diagnostics.Debug.Assert(A.typespec().is_int());
                int cind = rop.add_constant(~A.get_int());
                rop.turn_into_assign(op, cind, "~const");
                return 1;
            }
            return 0;
        }

        public static int constfold_if(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol C = argsym(rop, op, 0);
            if (C.is_constant())
            {
                int result = -1;  // -1 == we don't know
                if (C.typespec().is_int())
                {
                    result = (C.get_int() != 0) ? 1 : 0;
                }
                else if (C.typespec().is_float())
                {
                    result = (C.get_float() != 0.0f) ? 1 : 0;
                }
                else if (C.typespec().is_triple())
                {
                    result = (C.get_vec3() != new Vec3(0, 0, 0)) ? 1 : 0;
                }
                else if (C.typespec().is_string())
                {
                    ustring s = C.get_string();
                    result = (s.length() != 0) ? 1 : 0;
                }
                int changed = 0;
                if (result > 0)
                {
                    changed += rop.turn_into_nop(op.jump(0), op.jump(1), "elide 'else'");
                    changed += rop.turn_into_nop(op, "elide 'else'");
                }
                else if (result == 0)
                {
                    changed += rop.turn_into_nop(opnum, op.jump(0), "elide 'if'");
                }
                return changed;
            }

            // Eliminate 'if' that contains no statements to execute
            int jump = op.farthest_jump();
            bool only_nops = true;
            for (int i = opnum + 1; i < jump && only_nops; ++i)
            {
                only_nops &= (rop.inst().ops()[i].opname() == u_nop);
            }
            if (only_nops)
            {
                rop.turn_into_nop(op, "'if' with no body");
                return 1;
            }

            return 0;
        }

        // Is an array known to have all elements having the same value?
        private static bool array_all_elements_equal(Symbol s)
        {
            TypeDesc t = s.typespec().simpletype();
            int size = t.elementsize() / sizeof_basetype(t);  // base values in one element
            int n = t.numelements();
            object data = s.data();
            for (int i = 1; i < n; ++i)
            {
                if (!RuntimeOptimizer.data_equal(data, 0, data, i * size, size))
                {
                    return false;
                }
            }
            return true;
        }

        // The size in bytes of one base value of a type
        private static int sizeof_basetype(TypeDesc t)
        {
            return (int)t.basesize();
        }

        public static int constfold_aref(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = argsym(rop, op, 0);
            Symbol A = argsym(rop, op, 1);
            Symbol Index = argsym(rop, op, 2);
            System.Diagnostics.Debug.Assert(A.typespec().is_array() && Index.typespec().is_int());

            // Try to turn R=A[I] into R=C if A and I are const.
            if (A.is_constant() && Index.is_constant())
            {
                TypeSpec elemtype = A.typespec().elementtype();
                System.Diagnostics.Debug.Assert(PvtFunctions.equivalent(elemtype, R.typespec()));
                int length = A.typespec().arraylength();
                int orig_index = Index.get_int();
                int index = Math.Clamp(orig_index, 0, length - 1);
                System.Diagnostics.Debug.Assert(index >= 0 && index < length);
                int cind = rop.add_constant(elemtype, A.data(), new TypeDesc(), index * elemtype.simpletype().basevalues());
                rop.turn_into_assign(op, cind, "aref const fold: const_array[const]");
                if (rop.inst().master().range_checking() && index != orig_index)
                {
                    // the original index was out of range, and the user cares about reporting errors
                    int[] args_to_add = new int[]
                    {
                        rop.add_constant(u_fmt_range_check),
                        rop.add_constant(orig_index),
                        rop.add_constant(new ustring(A.unmangled())),
                        rop.add_constant(length - 1),
                        rop.add_constant(op.sourcefile()),
                        rop.add_constant(op.sourceline()),
                        rop.add_constant(rop.group().name()),
                        rop.add_constant(rop.layer()),
                        rop.add_constant(rop.inst().layername()),
                        rop.add_constant(new ustring(rop.inst().shadername()))
                    };
                    rop.insert_code(opnum, u_error, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                    RuntimeOptimizer.InsertRelation.GroupWithNext);
                    Opcode newop = rop.inst().ops()[opnum];
                    newop.argreadonly(0);
                }
                return 1;
            }
            // Even if the index isn't constant, we still know the answer if all the array elements are equal!
            if (A.is_constant() && array_all_elements_equal(A))
            {
                TypeSpec elemtype = A.typespec().elementtype();
                System.Diagnostics.Debug.Assert(PvtFunctions.equivalent(elemtype, R.typespec()));
                int cind = rop.add_constant(elemtype, A.data(), new TypeDesc(), 0);
                rop.turn_into_assign(op, cind, "aref of elements-equal array");
                return 1;
            }
            return 0;
        }

        public static int constfold_arraylength(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = argsym(rop, op, 0);
            Symbol A = argsym(rop, op, 1);
            System.Diagnostics.Debug.Assert(R.typespec().is_int() && A.typespec().is_array());

            // Try to turn R=arraylength(A) into R=C if the array length is known
            int len = A.typespec().is_unsized_array() ? A.initializers() : A.typespec().arraylength();
            if (len > 0)
            {
                int cind = rop.add_constant(new TypeSpec(new TypeDesc(TypeDesc.INT)), new int[] { len });
                rop.turn_into_assign(op, cind, "const fold arraylength");
                return 1;
            }
            return 0;
        }

        public static int constfold_aassign(RuntimeOptimizer rop, int opnum)
        {
            // Array assignment
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = argsym(rop, op, 0);
            Symbol I = argsym(rop, op, 1);
            Symbol C = argsym(rop, op, 2);
            if (!I.is_constant() || !C.is_constant())
            {
                return 0;  // not much we can do if not assigning constants
            }
            System.Diagnostics.Debug.Assert(R.typespec().is_array() && I.typespec().is_int());

            TypeSpec elemtype = R.typespec().elementtype();
            if (elemtype.is_closure())
            {
                return 0;  // don't worry about closures
            }
            TypeDesc elemsimpletype = elemtype.simpletype();

            // Look for patterns where all array elements are assigned in succession within the same block, in which case we can turn the result
            // into a constant!
            int len = R.typespec().arraylength();
            if (len <= 0)
            {
                return 0;  // don't handle arrays of unknown length
            }
            int elemvals = elemsimpletype.basevalues();  // number of base values in an element
            int[] index_assigned = new int[len];
            for (int i = 0; i < len; ++i)
            {
                index_assigned[i] = -1;
            }
            // constant storage, of the base type of the array
            Array fill = elemsimpletype.basetype == TypeDesc.FLOAT ? (Array)new float[elemvals * len]
                         : elemsimpletype.basetype == TypeDesc.INT ? (Array)new int[elemvals * len]
                                                                   : (Array)new ustring[elemvals * len];
            int num_assigned = 0;
            int opindex = opnum;
            int highestop = opindex;
            for (; ; )
            {
                Opcode opi = rop.inst().ops()[opindex];
                if (opi.opname() != u_aassign)
                {
                    break;  // not a successive aassign op
                }
                Symbol Ri = argsym(rop, opi, 0);
                if (!ReferenceEquals(Ri, R))
                {
                    break;  // not a compassign to the same variable
                }
                Symbol Ii = argsym(rop, opi, 1);
                Symbol Ci = argsym(rop, opi, 2);
                if (!Ii.is_constant() || !Ci.is_constant())
                {
                    break;  // not assigning constants
                }
                int indexval = Ii.get_int();
                if (indexval < 0 || indexval >= len)
                {
                    break;  // out of range index; let runtime deal with it
                }
                if (PvtFunctions.equivalent(elemtype, Ci.typespec()))
                {
                    // equivalent types
                    Array.Copy((Array)Ci.data(), 0, fill, indexval * elemvals, elemvals);
                }
                else if (elemtype.is_float() && Ci.typespec().is_int())
                {
                    // special case of float[i] = int
                    ((float[])fill)[indexval] = Ci.coerce_float();
                }
                else
                {
                    break;  // a case we don't handle
                }
                if (index_assigned[indexval] < 0)
                {
                    ++num_assigned;
                }
                index_assigned[indexval] = opindex;
                highestop = opindex;
                opindex = rop.next_block_instruction(opindex);
                if (opindex == 0)
                {
                    break;
                }
            }
            if (num_assigned == len)
            {
                // woo-hoo! we had a succession of constant aassign ops to the same variable, filling in all indices. Turn the whole shebang into a
                // single assignment.
                int cind = rop.add_constant(R.typespec(), fill);
                rop.turn_into_assign(op, cind, "replaced element-by-element assignment");
                rop.turn_into_nop(opnum + 1, highestop + 1, "replaced element-by-element assignment");
                return highestop + 1 - opnum;
            }

            return 0;
        }

        public static int constfold_compassign(RuntimeOptimizer rop, int opnum)
        {
            // Component assignment
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = argsym(rop, op, 0);
            Symbol I = argsym(rop, op, 1);
            Symbol C = argsym(rop, op, 2);
            if (!I.is_constant() || !C.is_constant())
            {
                return 0;  // not much we can do if not assigning constants
            }
            System.Diagnostics.Debug.Assert(R.typespec().is_triple() && I.typespec().is_int() && (C.typespec().is_float() || C.typespec().is_int()));

            // We are obviously not assigning to a constant, but it could be that at this point in our current block, the value of A is known, and
            // that will show up as a block alias.
            int Aalias = rop.block_alias(rop.inst().arg(op.firstarg() + 0));
            Symbol AA = rop.inst().symbol(Aalias);
            // N.B. symbol returns NULL if Aalias is < 0

            // Try to simplify A[I]=C if we already know the old value of A as a constant. We can turn it into A[I] = N, where N is the old A but with
            // the Ith component set to C. If it turns out that the old A[I] == C, and thus the assignment doesn't change A's value, we can eliminate
            // the assignment entirely.
            if (AA != null && AA.is_constant())
            {
                System.Diagnostics.Debug.Assert(AA.typespec().is_triple());
                int index = I.get_int();
                if (index < 0 || index >= 3)
                {
                    // We are indexing a const triple out of range. But this isn't necessarily a reportable error, because it may be a code path
                    // that will never be taken. Punt -- don't optimize this op, leave it to the execute-time range check to catch, if indeed it is a
                    // problem.
                    return 0;
                }
                float[] aa = (float[])AA.data();
                float c = C.coerce_float();
                if (aa[index] == c)
                {
                    // If the component assignment doesn't change that component, just omit the op entirely.
                    rop.turn_into_nop(op, "useless compassign");
                    return 1;
                }
                // If the previous value of the triple was a constant, and we're assigning a new constant to one component (and the index is also a
                // constant), just turn it into an assignment of a new constant triple.
                float[] newval = new float[] { aa[0], aa[1], aa[2] };
                newval[index] = c;
                int cind = rop.add_constant(AA.typespec(), newval);
                rop.turn_into_assign(op, cind, "fold compassign");
                return 1;
            }

            // Look for patterns where all three components are assigned in succession within the same block, in which case we can turn the result
            // into a constant!
            int[] index_assigned = new int[] { -1, -1, -1 };
            float[] filled_values = new float[3];
            int num_assigned = 0;
            int opindex = opnum;
            int highestop = opindex;
            for (; ; )
            {
                Opcode opi = rop.inst().ops()[opindex];
                if (opi.opname() != u_compassign)
                {
                    break;  // not a successive compassign op
                }
                Symbol Ri = argsym(rop, opi, 0);
                if (!ReferenceEquals(Ri, R))
                {
                    break;  // not a compassign to the same variable
                }
                Symbol Ii = argsym(rop, opi, 1);
                Symbol Ci = argsym(rop, opi, 2);
                if (!Ii.is_constant() || !Ci.is_constant())
                {
                    break;  // not assigning constants
                }
                int indexval = Ii.get_int();
                if (indexval < 0 || indexval >= 3)
                {
                    break;  // out of range index; let runtime deal with it
                }
                filled_values[indexval] = Ci.coerce_float();
                if (index_assigned[indexval] < 0)
                {
                    ++num_assigned;
                }
                index_assigned[indexval] = opindex;
                highestop = opindex;
                opindex = rop.next_block_instruction(opindex);
                if (opindex == 0)
                {
                    break;
                }
            }
            if (num_assigned == 3)
            {
                // woo-hoo! we had a succession of constant compassign ops to the same variable, filling in all indices. Turn the whole shebang into
                // a single assignment.
                int cind = rop.add_constant(R.typespec(), filled_values);
                rop.turn_into_assign(op, cind, "replaced element-by-element assignment");
                rop.turn_into_nop(opnum + 1, highestop + 1, "replaced element-by-element assignment");
                return highestop + 1 - opnum;
            }

            return 0;
        }

        public static int constfold_mxcompassign(RuntimeOptimizer rop, int opnum)
        {
            // Matrix component assignment
            Opcode op = rop.inst().ops()[opnum];
            Symbol R = argsym(rop, op, 0);
            Symbol J = argsym(rop, op, 1);
            Symbol I = argsym(rop, op, 2);
            Symbol C = argsym(rop, op, 3);
            if (!J.is_constant() || !I.is_constant() || !C.is_constant())
            {
                return 0;  // not much we can do if not assigning constants
            }
            System.Diagnostics.Debug.Assert(R.typespec().is_matrix() && J.typespec().is_int() && I.typespec().is_int()
                                            && (C.typespec().is_float() || C.typespec().is_int()));

            // We are obviously not assigning to a constant, but it could be that at this point in our current block, the value of A is known, and
            // that will show up as a block alias.
            int Aalias = rop.block_alias(rop.inst().arg(op.firstarg() + 0));
            Symbol AA = rop.inst().symbol(Aalias);
            // N.B. symbol returns NULL if Aalias is < 0

            // Try to simplify A[J,I]=C if we already know the old value of A as a constant. We can turn it into A[J,I] = N, where N is the old A but
            // with the designated component set to C. If it turns out that the old A[J,I] == C, and thus the assignment doesn't change A's value, we
            // can eliminate the assignment entirely.
            if (AA != null && AA.is_constant())
            {
                System.Diagnostics.Debug.Assert(AA.typespec().is_matrix());
                int jndex = J.get_int();
                int index = I.get_int();
                if (index < 0 || index >= 3 || jndex < 0 || jndex >= 3)
                {
                    // We are indexing a const matrix out of range. But this isn't necessarily a reportable error, because it may be a code path
                    // that will never be taken. Punt -- don't optimize this op, leave it to the execute-time range check to catch, if indeed it is
                    // a problem.
                    return 0;
                }
                float[] aa = (float[])AA.data();
                float c = C.coerce_float();
                if (aa[jndex * 4 + index] == c)
                {
                    // If the component assignment doesn't change that component, just omit the op entirely.
                    rop.turn_into_nop(op, "useless mxcompassign");
                    return 1;
                }
                // If the previous value of the matrix was a constant, and we're assigning a new constant to one component (and the index is also a
                // constant), just turn it into an assignment of a new constant triple.
                float[] newval = (float[])aa.Clone();
                newval[jndex * 4 + index] = c;
                int cind = rop.add_constant(AA.typespec(), newval);
                rop.turn_into_assign(op, cind, "fold mxcompassign");
                return 1;
            }

            // Look for patterns where all 16 components are assigned in succession within the same block, in which case we can turn the result into
            // a constant!
            int[] index_assigned = new int[16];
            for (int i = 0; i < 16; ++i)
            {
                index_assigned[i] = -1;
            }
            float[] filled_values = new float[16];
            int num_assigned = 0;
            int opindex = opnum;
            int highestop = opindex;
            for (; ; )
            {
                Opcode opi = rop.inst().ops()[opindex];
                if (opi.opname() != u_mxcompassign)
                {
                    break;  // not a successive mxcompassign op
                }
                Symbol Ri = argsym(rop, opi, 0);
                if (!ReferenceEquals(Ri, R))
                {
                    break;  // not a mxcompassign to the same variable
                }
                Symbol Ji = argsym(rop, opi, 1);
                Symbol Ii = argsym(rop, opi, 2);
                Symbol Ci = argsym(rop, opi, 3);
                if (!Ji.is_constant() || !Ii.is_constant() || !Ci.is_constant())
                {
                    break;  // not assigning constants
                }
                int jndexval = Ji.get_int();
                int indexval = Ii.get_int();
                if (jndexval < 0 || jndexval >= 4 || indexval < 0 || indexval >= 4)
                {
                    break;  // out of range index; let runtime deal with it
                }
                float c = Ci.coerce_float();
                filled_values[jndexval * 4 + indexval] = c;
                if (index_assigned[jndexval * 4 + indexval] < 0)
                {
                    ++num_assigned;
                }
                index_assigned[jndexval * 4 + indexval] = opindex;
                highestop = opindex;
                opindex = rop.next_block_instruction(opindex);
                if (opindex == 0)
                {
                    break;
                }
            }
            if (num_assigned == 16)
            {
                // woo-hoo! we had a succession of constant mxcompassign ops to the same variable, filling in all indices. Turn the whole shebang
                // into a single assignment.
                int cind = rop.add_constant(R.typespec(), filled_values);
                rop.turn_into_assign(op, cind, "replaced element-by-element assignment");
                rop.turn_into_nop(opnum + 1, highestop + 1, "replaced element-by-element assignment");
                return highestop + 1 - opnum;
            }

            return 0;
        }

        public static int constfold_compref(RuntimeOptimizer rop, int opnum)
        {
            // Component reference
            // Try to turn R=A[I] into R=C if A and I are const.
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            Symbol Index = argsym(rop, op, 2);
            if (A.is_constant() && Index.is_constant())
            {
                System.Diagnostics.Debug.Assert(A.typespec().is_triple() && Index.typespec().is_int());
                int index = Index.get_int();
                if (index < 0 || index >= 3)
                {
                    // We are indexing a const triple out of range. But this isn't necessarily a reportable error, because it may be a code path
                    // that will never be taken. Punt -- don't optimize this op, leave it to the execute-time range check to catch, if indeed it is a
                    // problem.
                    return 0;
                }
                int cind = rop.add_constant(A.get_float(index));
                rop.turn_into_assign(op, cind, "const_triple[const]");
                return 1;
            }
            return 0;
        }

        public static int constfold_strlen(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=strlen(s) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            if (S.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string());
                int result = OslRuntime.osl_strlen_is(S.get_string().hash());
                int cind = rop.add_constant(result);
                rop.turn_into_assign(op, cind, "const fold strlen");
                return 1;
            }
            return 0;
        }

        public static unsafe int constfold_hash(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=hash(s) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            Symbol T = op.nargs() > 2 ? argsym(rop, op, 2) : null;
            if (S.is_constant() && (T == null || T.is_constant()))
            {
                int cind = -1;
                if (S.typespec().is_string())
                {
                    cind = rop.add_constant(unchecked((int)S.get_string().hash()));
                }
                else if (op.nargs() == 2 && S.typespec().is_int())
                {
                    cind = rop.add_constant(OslNoise.inthashi(S.get_int()));
                }
                else if (op.nargs() == 2 && S.typespec().is_float())
                {
                    cind = rop.add_constant(OslNoise.inthashf(S.get_float()));
                }
                else if (op.nargs() == 3 && S.typespec().is_float() && T.typespec().is_float())
                {
                    cind = rop.add_constant(OslNoise.inthashf(S.get_float(), T.get_float()));
                }
                else if (op.nargs() == 2 && S.typespec().is_triple())
                {
                    fixed (float* p = (float[])S.dataptr())
                    {
                        cind = rop.add_constant(OslNoise.inthashf(p));
                    }
                }
                else if (op.nargs() == 3 && S.typespec().is_triple() && T.typespec().is_float())
                {
                    fixed (float* p = (float[])S.dataptr())
                    {
                        cind = rop.add_constant(OslNoise.inthashf(p, T.get_float()));
                    }
                }
                if (cind >= 0)
                {
                    rop.turn_into_assign(op, cind, "const fold hash");
                    return 1;
                }
            }
            return 0;
        }

        public static int constfold_getchar(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=getchar(s,i) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            Symbol I = argsym(rop, op, 2);
            if (S.is_constant() && I.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string() && I.typespec().is_int());
                int result = OslRuntime.osl_getchar_isi(S.get_string().hash(), I.get_int());
                int cind = rop.add_constant(result);
                rop.turn_into_assign(op, cind, "const fold getchar");
                return 1;
            }
            return 0;
        }

        public static int constfold_endswith(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=endswith(s,e) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            Symbol E = argsym(rop, op, 2);
            if (S.is_constant() && E.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string() && E.typespec().is_string());
                ustring s = S.get_string();
                ustring e = E.get_string();
                int cind = rop.add_constant(OslRuntime.osl_endswith_iss(s.hash(), e.hash()) != 0 ? 1 : 0);
                rop.turn_into_assign(op, cind, "const fold endswith");
                return 1;
            }
            return 0;
        }

        public static int constfold_startswith(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=startswith(s,e) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            Symbol E = argsym(rop, op, 2);
            if (S.is_constant() && E.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string() && E.typespec().is_string());
                ustring s = S.get_string();
                ustring e = E.get_string();
                int cind = rop.add_constant(OslRuntime.osl_startswith_iss(s.hash(), e.hash()) != 0 ? 1 : 0);
                rop.turn_into_assign(op, cind, "const fold startswith");
                return 1;
            }
            return 0;
        }

        public static int constfold_stoi(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=stoi(s) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            if (S.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string());
                ustring s = S.get_string();
                int cind = rop.add_constant(OslRuntime.osl_stoi_is(s.hash()));
                rop.turn_into_assign(op, cind, "const fold stoi");
                return 1;
            }
            return 0;
        }

        public static int constfold_stof(RuntimeOptimizer rop, int opnum)
        {
            // Try to turn R=stof(s) into R=C
            Opcode op = rop.inst().ops()[opnum];
            Symbol S = argsym(rop, op, 1);
            if (S.is_constant())
            {
                System.Diagnostics.Debug.Assert(S.typespec().is_string());
                ustring s = S.get_string();
                int cind = rop.add_constant(OslRuntime.osl_stof_fs(s.hash()));
                rop.turn_into_assign(op, cind, "const fold stof");
                return 1;
            }
            return 0;
        }
    }
}
