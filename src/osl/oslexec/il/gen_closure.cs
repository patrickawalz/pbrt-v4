// OSL port: llvm_gen.cpp (part 6) -- closures and point clouds.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        private static void il_gen_keyword_fill(BackendIL rop, Opcode op, ClosureRegistry.ClosureEntry clentry, ustring clname, ILValue mem_void_ptr,
                                                int argsoffset)
        {
            int Nattrs = (op.nargs() - argsoffset) / 2;
            for (int attr_i = 0; attr_i < Nattrs; ++attr_i)
            {
                int argno = attr_i * 2 + argsoffset;
                Symbol Key = rop.opargsym(op, argno);
                Symbol Value = rop.opargsym(op, argno + 1);
                ustring key = Key.get_string();
                TypeDesc ValueType = Value.typespec().simpletype();
                bool legal = false;
                for (int t = 0; t < clentry.nkeyword; ++t)
                {
                    ClosureParam p = clentry.@params[clentry.nformal + t];
                    if (TypeDesc.equivalent(p.type, ValueType) && key.@string() == p.key)
                    {
                        ILValue dst = rop.ll.offset_ptr(mem_void_ptr, p.offset);
                        ILValue src = rop.il_void_ptr(Value);
                        rop.ll.op_memcpy(dst, src, (int)p.type.size(), 4);
                        legal = true;
                        break;
                    }
                }
                if (!legal)
                {
                    rop.shadingcontext().warningfmt("Unsupported closure keyword arg \"{}\" for {} ({}:{})", key, clname, op.sourcefile(), op.sourceline());
                }
            }
        }

        public static bool il_gen_closure(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            int weighted = rop.opargsym(op, 1).typespec().is_string() ? 0 : 1;
            Symbol weight = weighted != 0 ? rop.opargsym(op, 1) : null;
            Symbol Id = rop.opargsym(op, 1 + weighted);
            ustring closure_name = Id.get_string();
            ClosureRegistry.ClosureEntry clentry = rop.shadingsys().find_closure(closure_name);
            if (clentry == null)
            {
                rop.shadingcontext().errorfmt("Closure '{}' is not supported by the current renderer, called from {}:{} in shader \"{}\", layer {} \"{}\", group \"{}\"",
                                              closure_name, op.sourcefile(), op.sourceline(), rop.inst().shadername(), rop.layer(),
                                              rop.inst().layername(), rop.group().name());
                return false;
            }
            ILValue sg_ptr = rop.sg_void_ptr();
            ILValue id_int = rop.ll.constant(clentry.id);
            ILValue size_int = rop.ll.constant(clentry.struct_size);
            ILValue return_ptr = weighted != 0
                                     ? rop.ll.call_function("osl_allocate_weighted_closure_component", sg_ptr, id_int, size_int, rop.il_void_ptr(weight))
                                     : rop.ll.call_function("osl_allocate_closure_component", sg_ptr, id_int, size_int);
            ILValue comp_void_ptr = return_ptr;
            ILLabel notnull_block = rop.ll.new_basic_block("non_null_closure");
            ILLabel next_block = rop.ll.new_basic_block("");
            ILValue cond = rop.ll.op_ne(return_ptr, rop.ll.void_ptr_null());
            rop.ll.op_branch(cond, notnull_block, next_block);
            ILValue comp_ptr = rop.ll.ptr_cast(comp_void_ptr);
            ILValue mem_void_ptr = rop.ll.offset_ptr(comp_ptr, sizeof(ClosureComponent));
            if (clentry.prepare != null)
            {
                rop.ll.call_function("osl_closure_prepare", sg_ptr, id_int, mem_void_ptr);
            }
            else
            {
                rop.ll.op_memset(mem_void_ptr, 0, clentry.struct_size, 4);
            }
            for (int carg = 0; carg < clentry.nformal; ++carg)
            {
                ClosureParam p = clentry.@params[carg];
                if (p.key != null)
                {
                    break;
                }
                Symbol sym = rop.opargsym(op, carg + 2 + weighted);
                TypeDesc t = sym.typespec().simpletype();
                if (!sym.typespec().is_closure_array() && !sym.typespec().is_structure() && TypeDesc.equivalent(t, p.type))
                {
                    ILValue dst = rop.ll.offset_ptr(mem_void_ptr, p.offset);
                    ILValue src = rop.il_void_ptr(sym);
                    rop.ll.op_memcpy(dst, src, (int)p.type.size(), 4);
                }
                else
                {
                    rop.shadingcontext().errorfmt("Incompatible formal argument {} to '{}' closure ({} {}, expected {}). Prototypes don't match renderer registry ({}:{}).",
                                                  carg + 1, closure_name, sym.typespec(), sym.unmangled(), p.type, op.sourcefile(), op.sourceline());
                }
            }
            if (clentry.setup != null)
            {
                rop.ll.call_function("osl_closure_setup", sg_ptr, id_int, mem_void_ptr);
            }
            il_gen_keyword_fill(rop, op, clentry, closure_name, mem_void_ptr, 2 + weighted + clentry.nformal);
            rop.ll.op_branch(next_block);
            rop.il_store_value(return_ptr, Result, 0, null, 0);
            return true;
        }

        public static bool il_gen_pointcloud_search(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Center = rop.opargsym(op, 2);
            Symbol Radius = rop.opargsym(op, 3);
            Symbol Max_points = rop.opargsym(op, 4);
            List<Symbol> clear_derivs_of = new List<Symbol>();
            int attr_arg_offset = 5;
            Symbol Sort = null;
            if (op.nargs() > 5 && rop.opargsym(op, 5).typespec().is_int())
            {
                Sort = rop.opargsym(op, 5);
                ++attr_arg_offset;
            }
            int nattrs = (op.nargs() - attr_arg_offset) / 2;
            int nalloc = Math.Max(1, nattrs);
            ILValue names = rop.ll.op_alloca(8 * nalloc, 16);
            ILValue types = rop.ll.op_alloca(8 * nalloc, 16);
            ILValue values = rop.ll.op_alloca(IntPtr.Size * nalloc, 16);
            List<ILValue> args = new List<ILValue>();
            args.Add(rop.sg_void_ptr());
            args.Add(rop.il_load_value(Filename));
            args.Add(rop.il_void_ptr(Center));
            args.Add(rop.il_load_value(Radius));
            const int maxPointsArgumentIndex = 4;
            ILValue maxPointsVal = rop.il_load_value(Max_points);
            args.Add(maxPointsVal);
            args.Add(Sort != null ? rop.il_load_value(Sort) : rop.ll.constant(0));
            const int indicesArgumentIndex = 6;
            args.Add(null);
            args.Add(rop.ll.constant_ptr(0));
            args.Add(rop.ll.constant(0));
            args.Add(null);
            int capacity = 0x7FFFFFFF;
            int extra_attrs = 0;
            for (int i = 0; i < nattrs; ++i)
            {
                Symbol Name = rop.opargsym(op, attr_arg_offset + i * 2);
                Symbol Value = rop.opargsym(op, attr_arg_offset + i * 2 + 1);
                TypeDesc simpletype = Value.typespec().simpletype();
                if (Name.is_constant() && Name.get_string() == u_index && simpletype.elementtype() == TypeDescs.TypeInt)
                {
                    args[indicesArgumentIndex] = rop.il_void_ptr(Value);
                }
                else if (Name.is_constant() && Name.get_string() == u_distance && simpletype.elementtype() == TypeDescs.TypeFloat)
                {
                    args[7] = rop.il_void_ptr(Value);
                    if (Value.has_derivs())
                    {
                        if (Center.has_derivs())
                        {
                            args[8] = rop.ll.constant((int)simpletype.numelements());
                        }
                        else
                        {
                            clear_derivs_of.Add(Value);
                        }
                    }
                }
                else if (!rop.use_optix())
                {
                    ILValue[] write_args = new ILValue[]
                    {
                        rop.ll.void_ptr(names), rop.ll.void_ptr(types), rop.ll.void_ptr(values), rop.ll.constant(extra_attrs), rop.il_load_value(Name),
                        rop.ll.constant(simpletype), rop.il_void_ptr(Value)
                    };
                    rop.ll.call_function("osl_pointcloud_write_helper", write_args);
                    if (Value.has_derivs())
                    {
                        clear_derivs_of.Add(Value);
                    }
                    extra_attrs++;
                }
                capacity = Math.Min((int)simpletype.numelements(), capacity);
            }
            args[9] = rop.ll.constant(extra_attrs);
            args.Add(rop.ll.void_ptr(names));
            args.Add(rop.ll.void_ptr(types));
            args.Add(rop.ll.void_ptr(values));
            if (args[indicesArgumentIndex] == null)
            {
                ILValue indices = rop.ll.op_alloca(4 * Math.Max(1, capacity == 0x7FFFFFFF ? 1 : capacity), 16);
                args[indicesArgumentIndex] = rop.ll.void_ptr(indices);
            }
            if (Max_points.is_constant())
            {
                int const_max_points = Max_points.get_int();
                if (capacity < const_max_points)
                {
                    rop.shadingcontext().warningfmt("Arrays too small for pointcloud lookup at ({}:{}) ({}:{})", op.sourcefile(), op.sourceline());
                    args[maxPointsArgumentIndex] = rop.ll.constant(capacity);
                }
            }
            else
            {
                ILValue capacity_val = rop.ll.constant(capacity);
                ILValue cond = rop.ll.op_le(capacity_val, maxPointsVal);
                ILValue clampedMaxPoints = rop.ll.op_select(cond, capacity_val, maxPointsVal);
                args[maxPointsArgumentIndex] = clampedMaxPoints;
            }
            ILValue count = rop.ll.call_function("osl_pointcloud_search", args.ToArray());
            for (int i = 0; i < clear_derivs_of.Count; ++i)
            {
                rop.il_zero_derivs(clear_derivs_of[i], count);
            }
            rop.il_store_value(count, Result);
            return true;
        }

        public static bool il_gen_pointcloud_get(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Indices = rop.opargsym(op, 2);
            Symbol Count = rop.opargsym(op, 3);
            Symbol Attr_name = rop.opargsym(op, 4);
            Symbol Data = rop.opargsym(op, 5);
            ILValue count = rop.il_load_value(Count);
            int element_count = Math.Min(Data.typespec().arraylength(), Indices.typespec().arraylength());
            ILValue elem_count_val = rop.ll.constant(element_count);
            ILValue cond = rop.ll.op_le(elem_count_val, count);
            ILValue clampedCount = rop.ll.op_select(cond, elem_count_val, count);
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Filename), rop.il_void_ptr(Indices), clampedCount, rop.il_load_value(Attr_name),
                rop.ll.constant(Data.typespec().simpletype()), rop.il_void_ptr(Data)
            };
            ILValue found = rop.ll.call_function("osl_pointcloud_get", args);
            rop.il_store_value(found, Result);
            if (Data.has_derivs())
            {
                rop.il_zero_derivs(Data, clampedCount);
            }
            return true;
        }

        public static bool il_gen_pointcloud_write(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Pos = rop.opargsym(op, 2);
            int nattrs = (op.nargs() - 3) / 2;
            int nalloc = Math.Max(1, nattrs);
            ILValue names = rop.ll.op_alloca(8 * nalloc, 16);
            ILValue types = rop.ll.op_alloca(8 * nalloc, 16);
            ILValue values = rop.ll.op_alloca(IntPtr.Size * nalloc, 16);
            for (int i = 0; i < nattrs; ++i)
            {
                Symbol namesym = rop.opargsym(op, 3 + 2 * i);
                Symbol valsym = rop.opargsym(op, 3 + 2 * i + 1);
                ILValue[] wargs = new ILValue[]
                {
                    rop.ll.void_ptr(names), rop.ll.void_ptr(types), rop.ll.void_ptr(values), rop.ll.constant(i), rop.il_load_value(namesym),
                    rop.ll.constant(valsym.typespec().simpletype()), rop.il_void_ptr(valsym)
                };
                rop.ll.call_function("osl_pointcloud_write_helper", wargs);
            }
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(Filename), rop.il_void_ptr(Pos), rop.ll.constant(nattrs), rop.ll.void_ptr(names),
                rop.ll.void_ptr(types), rop.ll.void_ptr(values)
            };
            ILValue ret = rop.ll.call_function("osl_pointcloud_write", args);
            rop.il_store_value(ret, Result);
            return true;
        }
    }
}
