// OSL port: liboslexec/constfold.cpp (third part): the folders of the point cloud, noise, function call, assignment and query ops, and the registration
// of all the folders in OpFolderTable. See constfold.cs for the conventions.

using System;
using System.Collections.Generic;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL.pvt
{
    public static unsafe partial class ConstFold
    {
        public static unsafe int constfold_pointcloud_search(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            System.Diagnostics.Debug.Assert(op.nargs() >= 5);
            int result_sym = rop.oparg(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Center = rop.opargsym(op, 2);
            Symbol Radius = rop.opargsym(op, 3);
            Symbol Max_points = rop.opargsym(op, 4);
            System.Diagnostics.Debug.Assert(Filename.typespec().is_string() && Center.typespec().is_triple() && Radius.typespec().is_float()
                                            && Max_points.typespec().is_int());

            // Can't constant fold unless all the required input args are constant
            if (!(Filename.is_constant() && Center.is_constant() && Radius.is_constant() && Max_points.is_constant()))
            {
                return 0;
            }

            // Handle the optional 'sort' flag, and don't bother constant folding if sorted results may be required.
            int attr_arg_offset = 5;  // where the opt attrs begin
            if (op.nargs() > 5 && rop.opargsym(op, 5).typespec().is_int())
            {
                // Sorting requested
                Symbol Sort = rop.opargsym(op, 5);
                if (!Sort.is_constant() || Sort.get_int() != 0)
                {
                    return 0;  // forget it if sorted data might be requested
                }
                ++attr_arg_offset;
            }
            int nattrs = (op.nargs() - attr_arg_offset) / 2;

            // First pass through the optional arguments: gather the query names, types, and destinations. If any of the query names are not known
            // constants, we can't optimize this call so just return.
            List<ustring> names = new List<ustring>();
            List<int> value_args = new List<int>();
            List<TypeDesc> value_types = new List<TypeDesc>();
            for (int i = 0, num_queries = 0; i < nattrs; ++i)
            {
                Symbol Name = rop.opargsym(op, attr_arg_offset + i * 2);
                Symbol Value = rop.opargsym(op, attr_arg_offset + i * 2 + 1);
                System.Diagnostics.Debug.Assert(Name.typespec().is_string());
                if (!Name.is_constant())
                {
                    return 0;  // unknown optional argument, punt
                }
                if (++num_queries > RuntimeOptimizer.max_new_consts_per_fold)
                {
                    return 0;
                }
                names.Add(Name.get_string());
                value_args.Add(rop.oparg(op, attr_arg_offset + i * 2 + 1));
                value_types.Add(Value.typespec().simpletype());
            }

            // We're doing a fixed query, so instead of running at every shade, perform the search now.
            const int maxconst = 256;  // Max number of points to consider a constant
            int[] indices = new int[maxconst + 1];  // Make room for one more!
            float[] distances = new float[maxconst + 1];
            int maxpoints = Math.Min(maxconst + 1, Max_points.get_int());
            ustring filename = Filename.get_string();
            int count = 0;
            if (!filename.empty())
            {
                fixed (int* pindices = indices)
                fixed (float* pdistances = distances)
                {
                    count = rop.renderer().pointcloud_search(rop.shaderglobals(), filename.hash(), Center.get_vec3(), Radius.get_float(), maxpoints,
                                                             false, pindices, pdistances, 0);
                }
                rop.shadingsys().pointcloud_stats(1, 0, count);
            }

            // If it returns few enough results (256 points or less), just fold those results into constant arrays. If more than that, let the query
            // happen at runtime to avoid tying up a bunch of memory.
            if (count > maxconst)
            {
                return 0;
            }

            // If the query returned no matching points, just turn the whole pointcloud_search call into an assignment of 0 to the 'result'.
            if (count < 1)
            {
                rop.turn_into_assign_zero(op, "Folded constant pointcloud_search lookup");
                return 1;
            }

            // From here on out, we are able to fold the query (it returned results, but not too many). Start by removing the original
            // pointcloud_search call itself from the shader code.
            rop.turn_into_nop(op, "Folded constant pointcloud_search lookup");

            // Now, for each optional individual query, do a pointcloud_get NOW to retrieve it, create a constant array for the shader to hold those
            // results, and add to the shader an array copy to move it from the constant into the place the shader wanted the query results to go.
            // (This assignment can be further optimized later on as well, depending on how it's used.) If any of the individual queries fail now,
            // we will return a failed result in the end.
            for (int i = 0; i < nattrs; ++i)
            {
                // We had stashed names, data types, and destinations earlier. Retrieve them now to build a query.
                if (names[i].empty())
                {
                    continue;
                }
                TypeDesc const_valtype = value_types[i];
                Array const_data;
                if (names[i].@string() == "index")
                {
                    // "index" is a special case -- it's retrieving the hit point indices, not data on those hit points.
                    //
                    const_data = indices;
                }
                else
                {
                    // Named queries.
                    byte[] tmp = new byte[Math.Max((int)const_valtype.size(), 8)];  // temporary data
                    bool ok;
                    fixed (byte* ptmp = tmp)
                    fixed (int* pindices = indices)
                    {
                        ok = rop.renderer().pointcloud_get(rop.shaderglobals(), filename.hash(), pindices, count, names[i].hash(), const_valtype, ptmp) != 0;
                        const_data = ok ? array_from_bytes(const_valtype, ptmp) : null;
                    }
                    rop.shadingsys().pointcloud_stats(0, 1, 0);
                    if (!ok)
                    {
                        count = 0;  // Make it look like an error in the end
                        break;
                    }
                }
                // Now make a constant array for those results we just retrieved...
                int const_array_sym = rop.add_constant(new TypeSpec(const_valtype), const_data);
                // ... and add an instruction to copy the constant into the original destination for the query.
                int[] args_to_add = new int[] { value_args[i], const_array_sym };
                rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                                RuntimeOptimizer.InsertRelation.GroupWithNext);
            }

            // Query results all copied. The only thing left to do is to assign status (query result count) to the original "result".
            int[] final_args = new int[] { result_sym, rop.add_constant(count) };
            rop.insert_code(opnum, u_assign, final_args, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                            RuntimeOptimizer.InsertRelation.GroupWithNext);

            return 1;
        }

        public static unsafe int constfold_pointcloud_get(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            // Symbol& Result     = *rop.opargsym (op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Indices = rop.opargsym(op, 2);
            Symbol Count = rop.opargsym(op, 3);
            Symbol Attr_name = rop.opargsym(op, 4);
            Symbol Data = rop.opargsym(op, 5);
            if (!(Filename.is_constant() && Indices.is_constant() && Count.is_constant() && Attr_name.is_constant()))
            {
                return 0;
            }

            // All inputs are constants -- we can just turn this into an array assignment.

            ustring filename = Filename.get_string();
            int count = Count.get_int();
            if (filename.empty() || count < 1)
            {
                rop.turn_into_assign_zero(op, "Folded constant pointcloud_get");
                return 1;
            }

            if (count >= 1024)  // Too many, don't bother folding
            {
                return 0;
            }

            TypeDesc valtype = Data.typespec().simpletype();
            byte[] data = new byte[Math.Max((int)valtype.size(), 8)];
            int ok;
            Array constdata;
            fixed (byte* pdata = data)
            fixed (int* pindices = (int[])Indices.data())
            {
                ok = rop.renderer().pointcloud_get(rop.shaderglobals(), filename.hash(), pindices, count, Attr_name.get_string().hash(), valtype, pdata);
                // Renderer services treat strings as ustringhash, so we need to convert it back to ustring before continuing
                constdata = array_from_bytes(valtype, pdata);
            }
            rop.shadingsys().pointcloud_stats(0, 1, 0);

            rop.turn_into_assign(op, rop.add_constant(ok), "Folded constant pointcloud_get");

            // Now make a constant array for those results we just retrieved...
            int const_array_sym = rop.add_constant(new TypeSpec(valtype), constdata);
            // ... and add an instruction to copy the constant into the original destination for the query.
            int[] args_to_add = new int[] { rop.oparg(op, 5) /* Data symbol*/, const_array_sym };
            rop.insert_code(opnum, u_assign, args_to_add, RuntimeOptimizer.RecomputeRWRangesOption.RecomputeRWRanges,
                            RuntimeOptimizer.InsertRelation.GroupWithNext);
            return 1;
        }

        public static unsafe int constfold_noise(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];

            // Decode some info about which noise function we're dealing with
            //    bool periodic = (op.opname() == Strings::pnoise);
            int arg = 0;  // Next arg to read
            Symbol Result = rop.opargsym(op, arg++);
            int outdim = Result.typespec().is_triple() ? 3 : 1;
            Symbol Name = rop.opargsym(op, arg++);
            ustring name = new ustring("");
            if (Name.typespec().is_string())
            {
                if (Name.is_constant())
                {
                    name = Name.get_string();
                }
            }
            else
            {
                // Not a string, must be the old-style noise/pnoise
                --arg;  // forget that arg
                Name = null;
                name = op.opname();
            }

            // Noise with name that is not a constant at osl-compile-time was marked as taking the derivs of its coordinate arguments. If at this
            // point we can determine that the name is known and not "gabor", when we can turn its derivative taking off.
            if (op.argtakesderivs_all() != 0 && name.length() != 0 && name.@string() != "gabor")
            {
                op.argtakesderivs_all(0);
            }

            // Gabor noise is the only one that takes optional arguments, so optimize them away for other noise types.
            if (name.length() != 0 && name.@string() != "gabor")
            {
                for (int a = arg; a < op.nargs(); ++a)
                {
                    // Advance until we hit a string argument, which will be the first optional token/value pair. Then just turn all arguments from
                    // that point on into empty strings, which will later be skipped, and in the mean time will eliminate the dependencies on whatever
                    // values were previously passed.
                    if (rop.opargsym(op, a).typespec().is_string())
                    {
                        for (; a < op.nargs(); a += 2)
                        {
                            System.Diagnostics.Debug.Assert(a + 1 < op.nargs());
                            int cind = rop.add_constant(new ustring(""));
                            rop.inst().args()[op.firstarg() + a] = cind;
                            rop.inst().args()[op.firstarg() + a + 1] = cind;
                        }
                    }
                }
            }

            // Early out: for now, we only fold cell noise
            if (name != u_cellnoise && name != u_cell)
            {
                return 0;
            }

            // Take an early out if any args are not constant (other than the result)
            for (int i = 1; i < op.nargs(); ++i)
            {
                if (!rop.opargsym(op, i).is_constant())
                {
                    return 0;
                }
            }

            // Extract the constant input coordinates
            float[] input = new float[4];
            int indim = 0;
            for (; arg < op.nargs() && indim < 4; ++arg)
            {
                Symbol inp = rop.opargsym(op, arg);
                if (inp.typespec().is_float())
                {
                    input[indim++] = inp.get_float(0);
                }
                else if (inp.typespec().is_triple())
                {
                    input[indim++] = inp.get_float(0);
                    input[indim++] = inp.get_float(1);
                    input[indim++] = inp.get_float(2);
                }
                else
                {
                    return 0;  // optional args starting, we don't fold them yet
                }
            }

            if (name == u_cellnoise || name == u_cell)
            {
                // CellNoise of oslnoise.h: the hashed noise at 1, 2, 3 (a point) or 4 (a point and a time) coordinates
                if (outdim == 1)
                {
                    float n;
                    if (indim == 1)
                    {
                        n = OslNoise.int_hash_noise(false, input[0]);
                    }
                    else if (indim == 2)
                    {
                        n = OslNoise.int_hash_noise(false, input[0], input[1]);
                    }
                    else if (indim == 3)
                    {
                        n = OslNoise.int_hash_noise(false, input[0], input[1], input[2]);
                    }
                    else
                    {
                        n = OslNoise.int_hash_noise(false, input[0], input[1], input[2], input[3]);
                    }
                    int cind = rop.add_constant(n);
                    rop.turn_into_assign(op, cind, "const fold cellnoise");
                    return 1;
                }
                else
                {
                    System.Diagnostics.Debug.Assert(outdim == 3);
                    float[] n = new float[3];
                    fixed (float* pn = n)
                    {
                        if (indim == 1)
                        {
                            OslNoise.int_hash_noisev(false, pn, input[0]);
                        }
                        else if (indim == 2)
                        {
                            OslNoise.int_hash_noisev(false, pn, input[0], input[1]);
                        }
                        else if (indim == 3)
                        {
                            OslNoise.int_hash_noisev(false, pn, input[0], input[1], input[2]);
                        }
                        else
                        {
                            OslNoise.int_hash_noisev(false, pn, input[0], input[1], input[2], input[3]);
                        }
                    }
                    int cind = rop.add_constant(TypePoint, n);
                    rop.turn_into_assign(op, cind, "const fold cellnoise");
                    return 1;
                }
            }

            return 0;
        }

        public static int constfold_functioncall(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            // Make a "functioncall" block disappear if the only non-nop statements inside it is 'return'.
            bool has_return = false;
            bool has_anything_else = false;
            for (int i = opnum + 1, e = op.jump(0); i < e; ++i)
            {
                Opcode inner = rop.inst().ops()[i];
                if (inner.opname() == u_return)
                {
                    has_return = true;
                }
                else if (inner.opname() != u_nop)
                {
                    has_anything_else = true;
                }
            }
            int changed = 0;
            if (!has_anything_else)
            {
                // Possibly due to optimizations, there's nothing in the function body but the return. So just eliminate the whole block of ops.
                for (int i = opnum, e = op.jump(0); i < e; ++i)
                {
                    if (rop.inst().ops()[i].opname() != u_nop)
                    {
                        rop.turn_into_nop(rop.inst().ops()[i], "empty function");
                        ++changed;
                    }
                }
            }
            else if (!has_return)
            {
                // The function is just a straight-up execution, no return statement, so kill the "function" op.
                if (rop.keep_no_return_function_calls())
                {
                    rop.turn_into_functioncall_nr(op, "'functioncall' transmuted to 'no return' version");
                }
                else
                {
                    rop.turn_into_nop(op, "'function' not necessary");
                }
                ++changed;
            }

            return changed;
        }

        public static int constfold_useparam(RuntimeOptimizer rop, int opnum)
        {
            // Just eliminate useparam (from shaders compiled with old oslc)
            Opcode op = rop.inst().ops()[opnum];
            rop.turn_into_nop(op);
            return 1;
        }

        public static int constfold_assign(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol B = argsym(rop, op, 1);
            int Aalias = rop.block_alias(rop.inst().arg(op.firstarg() + 0));
            Symbol AA = rop.inst().symbol(Aalias);
            // N.B. symbol() returns NULL if alias is < 0

            if (B.is_constant() && AA != null && AA.is_constant())
            {
                // Try to turn A=C into nop if A already is C
                if (AA.typespec().is_int() && B.typespec().is_int())
                {
                    if (AA.get_int() == B.get_int())
                    {
                        rop.turn_into_nop(op, "reassignment of current value");
                        return 1;
                    }
                }
                else if (AA.typespec().is_float() && B.typespec().is_float())
                {
                    if (AA.get_float() == B.get_float())
                    {
                        rop.turn_into_nop(op, "reassignment of current value");
                        return 1;
                    }
                }
                else if (AA.typespec().is_float() && B.typespec().is_int())
                {
                    if (AA.get_float() == B.get_int())
                    {
                        rop.turn_into_nop(op, "reassignment of current value");
                        return 1;
                    }
                }
                else if (AA.typespec().is_triple() && B.typespec().is_triple())
                {
                    if (AA.get_vec3() == B.get_vec3())
                    {
                        rop.turn_into_nop(op, "reassignment of current value");
                        return 1;
                    }
                }
                else if (AA.typespec().is_triple() && B.typespec().is_float())
                {
                    float b = B.get_float();
                    if (AA.get_vec3() == new Vec3(b, b, b))
                    {
                        rop.turn_into_nop(op, "reassignment of current value");
                        return 1;
                    }
                }
            }
            return 0;
        }

        public static int constfold_warning(RuntimeOptimizer rop, int opnum)
        {
            if (rop.shadingsys().max_warnings_per_thread() == 0)
            {
                Opcode op = rop.inst().ops()[opnum];
                rop.turn_into_nop(op, "warnings disabled by max_warnings_per_thread == 0");
                return 1;
            }
            return 0;
        }

        public static int constfold_deriv(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            if (A.is_constant())
            {
                rop.turn_into_assign_zero(op, "deriv of constant => 0");
                return 1;
            }
            return 0;
        }

        public static int constfold_isconstant(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol A = argsym(rop, op, 1);
            // If at this point we know it's a constant, it's certainly a constant, so we can constant fold it. Note that if it's not known to be a
            // constant at this point, that doesn't mean we won't detect it to be constant after further optimization, so we never fold this to 0.
            if (A.is_constant())
            {
                rop.turn_into_assign_one(op, "isconstant => 1");
                return 1;
            }
            return 0;
        }

        public static int constfold_raytype(RuntimeOptimizer rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Name = rop.opargsym(op, 1);
            System.Diagnostics.Debug.Assert(Name.typespec().is_string());
            if (!Name.is_constant())
            {
                return 0;  // Can't optimize non-constant raytype name
            }

            int bit = rop.shadingsys().raytype_bit(Name.get_string());
            if ((bit & rop.raytypes_on()) != 0)
            {
                rop.turn_into_assign_one(op, "raytype => 1");
                return 1;
            }
            if ((bit & rop.raytypes_off()) != 0)
            {
                rop.turn_into_assign_zero(op, "raytype => 0");
                return 1;
            }
            return 0;  // indeterminate until execution time
        }

        /// Enter the folders in OpFolderTable under the folder names of the op table (the C++ names them directly in setup_op_descriptors); called
        /// once, from the static constructor of RuntimeOptimizer.
        public static void register_folders()
        {
            Dictionary<string, OpFolder> f = OpFolderTable.folders;
            f["aassign"] = constfold_aassign;
            f["abs"] = constfold_abs;
            f["acos"] = constfold_acos;
            f["add"] = constfold_add;
            f["and"] = constfold_and;
            f["aref"] = constfold_aref;
            f["arraylength"] = constfold_arraylength;
            f["asin"] = constfold_asin;
            f["bitand"] = constfold_bitand;
            f["bitor"] = constfold_bitor;
            f["cbrt"] = constfold_cbrt;
            f["ceil"] = constfold_ceil;
            f["clamp"] = constfold_clamp;
            f["compassign"] = constfold_compassign;
            f["compl"] = constfold_compl;
            f["compref"] = constfold_compref;
            f["concat"] = constfold_concat;
            f["cos"] = constfold_cos;
            f["degrees"] = constfold_degrees;
            f["deriv"] = constfold_deriv;
            f["div"] = constfold_div;
            f["dot"] = constfold_dot;
            f["endswith"] = constfold_endswith;
            f["eq"] = constfold_eq;
            f["erf"] = constfold_erf;
            f["erfc"] = constfold_erfc;
            f["exp"] = constfold_exp;
            f["exp2"] = constfold_exp2;
            f["expm1"] = constfold_expm1;
            f["floor"] = constfold_floor;
            f["format"] = constfold_format;
            f["functioncall"] = constfold_functioncall;
            f["ge"] = constfold_ge;
            f["getattribute"] = constfold_getattribute;
            f["getchar"] = constfold_getchar;
            f["getmatrix"] = constfold_getmatrix;
            f["getmessage"] = constfold_getmessage;
            f["gettextureinfo"] = constfold_gettextureinfo;
            f["gt"] = constfold_gt;
            f["hash"] = constfold_hash;
            f["if"] = constfold_if;
            f["inversesqrt"] = constfold_inversesqrt;
            f["isconstant"] = constfold_isconstant;
            f["le"] = constfold_le;
            f["log"] = constfold_log;
            f["log10"] = constfold_log10;
            f["log2"] = constfold_log2;
            f["logb"] = constfold_logb;
            f["lt"] = constfold_lt;
            f["matrix"] = constfold_matrix;
            f["max"] = constfold_max;
            f["min"] = constfold_min;
            f["mix"] = constfold_mix;
            f["mod"] = constfold_mod;
            f["mul"] = constfold_mul;
            f["mxcompassign"] = constfold_mxcompassign;
            f["neg"] = constfold_neg;
            f["neq"] = constfold_neq;
            f["noise"] = constfold_noise;
            f["normalize"] = constfold_normalize;
            f["or"] = constfold_or;
            f["pointcloud_get"] = constfold_pointcloud_get;
            f["pointcloud_search"] = constfold_pointcloud_search;
            f["pow"] = constfold_pow;
            f["radians"] = constfold_radians;
            f["raytype"] = constfold_raytype;
            f["regex_search"] = constfold_regex_search;
            f["select"] = constfold_select;
            f["setmessage"] = constfold_setmessage;
            f["sin"] = constfold_sin;
            f["sincos"] = constfold_sincos;
            f["split"] = constfold_split;
            f["sqrt"] = constfold_sqrt;
            f["startswith"] = constfold_startswith;
            f["stof"] = constfold_stof;
            f["stoi"] = constfold_stoi;
            f["strlen"] = constfold_strlen;
            f["sub"] = constfold_sub;
            f["substr"] = constfold_substr;
            f["texture"] = constfold_texture;
            f["transform"] = constfold_transform;
            f["transformc"] = constfold_transformc;
            f["triple"] = constfold_triple;
            f["useparam"] = constfold_useparam;
            f["warning"] = constfold_warning;
            f["xor"] = constfold_xor;
        }
    }
}
