// OSL port: llvm_gen.cpp (part 3) -- messages, attributes, texture info, dictionaries.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.pvt
{
    public static unsafe partial class ILGen
    {
        public static bool il_gen_getattribute(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int nargs = op.nargs();
            bool array_lookup = rop.opargsym(op, nargs - 2).typespec().is_int();
            bool object_lookup = rop.opargsym(op, 2).typespec().is_string() && nargs >= 4;
            int object_slot = object_lookup ? 1 : 0;
            int attrib_slot = object_slot + 1;
            int index_slot = array_lookup ? nargs - 2 : 0;
            Symbol Result = rop.opargsym(op, 0);
            Symbol ObjectName = rop.opargsym(op, object_slot);
            Symbol Attribute = rop.opargsym(op, attrib_slot);
            Symbol Index = rop.opargsym(op, index_slot);
            Symbol Destination = rop.opargsym(op, nargs - 1);
            TypeDesc dest_type = Destination.typespec().simpletype();
            ILValue obj_name_arg = object_lookup ? rop.il_load_value(ObjectName) : rop.il_const_hash(new ustring(""));
            ILValue attr_name_arg = rop.il_load_value(Attribute);
            // The renderer attribute getter specs (build_attribute_getter) are a GPU feature; this backend always takes the osl_get_attribute path
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.ll.constant(Destination.has_derivs() ? 1 : 0), obj_name_arg, attr_name_arg, rop.ll.constant(array_lookup ? 1 : 0),
                rop.il_load_value(Index), rop.ll.constant(dest_type), rop.il_void_ptr(Destination)
            };
            ILValue r = rop.ll.call_function("osl_get_attribute", args);
            rop.il_store_value(r, Result);
            return true;
        }

        public static bool il_gen_gettextureinfo(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            bool use_coords = (op.nargs() == 6);
            Symbol Result = rop.opargsym(op, 0);
            Symbol Filename = rop.opargsym(op, 1);
            Symbol Dataname = rop.opargsym(op, use_coords ? 4 : 2);
            Symbol Data = rop.opargsym(op, use_coords ? 5 : 3);
            Symbol S = use_coords ? rop.opargsym(op, 2) : null;
            Symbol T = use_coords ? rop.opargsym(op, 3) : null;
            long texture_handle = 0;
            if (Filename.is_constant() && rop.shadingsys().opt_texture_handle())
            {
                texture_handle = rop.il_texture_handle(Filename.get_string());
            }
            List<ILValue> args = new List<ILValue>();
            args.Add(rop.sg_void_ptr());
            args.Add(rop.il_load_value(Filename));
            args.Add(rop.ll.constant_ptr(texture_handle));
            if (use_coords)
            {
                args.Add(rop.il_load_value(S));
                args.Add(rop.il_load_value(T));
            }
            args.Add(rop.il_load_value(Dataname));
            args.Add(rop.ll.constant((int)Data.typespec().simpletype().basetype));
            args.Add(rop.ll.constant((int)Data.typespec().simpletype().arraylen));
            args.Add(rop.ll.constant((int)Data.typespec().simpletype().aggregate));
            args.Add(rop.il_void_ptr(Data));
            args.Add(rop.ll.void_ptr_null());
            ILValue r = rop.ll.call_function(use_coords ? "osl_get_textureinfo_st" : "osl_get_textureinfo", args.ToArray());
            rop.il_store_value(r, Result);
            if (Data.has_derivs())
            {
                rop.il_zero_derivs(Data);
            }
            rop.generated_texture_call(texture_handle != 0);
            return true;
        }

        public static bool il_gen_getmessage(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            int has_source = (op.nargs() == 4) ? 1 : 0;
            Symbol Result = rop.opargsym(op, 0);
            Symbol Source = rop.opargsym(op, 1);
            Symbol Name = rop.opargsym(op, 1 + has_source);
            Symbol Data = rop.opargsym(op, 2 + has_source);
            if (has_source != 0 && Source.is_constant() && Source.get_string().@string() == "trace")
            {
                ILValue[] targs = new ILValue[5];
                targs[0] = rop.sg_void_ptr();
                targs[1] = rop.il_load_value(Name);
                if (Data.typespec().is_closure_based())
                {
                    targs[2] = rop.ll.constant(new TypeDesc(TypeDesc.UNKNOWN, Data.typespec().arraylength()));
                    targs[3] = rop.il_void_ptr(Data);
                }
                else
                {
                    targs[2] = rop.ll.constant(Data.typespec().simpletype());
                    targs[3] = rop.il_void_ptr(Data);
                }
                targs[4] = rop.ll.constant(Data.has_derivs() ? 1 : 0);
                ILValue tr = rop.ll.call_function("osl_trace_get", targs);
                rop.il_store_value(tr, Result);
                return true;
            }
            ILValue[] args = new ILValue[9];
            args[0] = rop.sg_void_ptr();
            args[1] = has_source != 0 ? rop.il_load_value(Source) : rop.ll.constant64(new ustring("").hash());
            args[2] = rop.il_load_value(Name);
            if (Data.typespec().is_closure_based())
            {
                args[3] = rop.ll.constant(new TypeDesc(TypeDesc.UNKNOWN, Data.typespec().arraylength()));
                args[4] = rop.il_void_ptr(Data);
            }
            else
            {
                args[3] = rop.ll.constant(Data.typespec().simpletype());
                args[4] = rop.il_void_ptr(Data);
            }
            args[5] = rop.ll.constant(Data.has_derivs() ? 1 : 0);
            args[6] = rop.ll.constant(rop.inst().id());
            args[7] = rop.il_const_hash(op.sourcefile());
            args[8] = rop.ll.constant(op.sourceline());
            ILValue r = rop.ll.call_function("osl_getmessage", args);
            rop.il_store_value(r, Result);
            return true;
        }

        public static bool il_gen_setmessage(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Name = rop.opargsym(op, 0);
            Symbol Data = rop.opargsym(op, 1);
            ILValue[] args = new ILValue[7];
            args[0] = rop.sg_void_ptr();
            args[1] = rop.il_load_value(Name);
            if (Data.typespec().is_closure_based())
            {
                args[2] = rop.ll.constant(new TypeDesc(TypeDesc.UNKNOWN, Data.typespec().arraylength()));
                args[3] = rop.il_void_ptr(Data);
            }
            else
            {
                args[2] = rop.ll.constant(Data.typespec().simpletype());
                args[3] = rop.il_void_ptr(Data);
            }
            args[4] = rop.ll.constant(rop.inst().id());
            args[5] = rop.il_const_hash(op.sourcefile());
            args[6] = rop.ll.constant(op.sourceline());
            rop.ll.call_function("osl_setmessage", args);
            return true;
        }

        public static bool il_gen_dict_find(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol Source = rop.opargsym(op, 1);
            Symbol Query = rop.opargsym(op, 2);
            bool sourceint = Source.typespec().is_int();
            ILValue[] args = new ILValue[] { rop.sg_void_ptr(), rop.il_load_value(Source), rop.il_load_value(Query) };
            string func = sourceint ? "osl_dict_find_iis" : "osl_dict_find_iss";
            ILValue ret = rop.ll.call_function(func, args);
            rop.il_store_value(ret, Result);
            return true;
        }

        public static bool il_gen_dict_next(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol NodeID = rop.opargsym(op, 1);
            ILValue ret = rop.ll.call_function("osl_dict_next", rop.sg_void_ptr(), rop.il_load_value(NodeID));
            rop.il_store_value(ret, Result);
            return true;
        }

        public static bool il_gen_dict_value(BackendIL rop, int opnum)
        {
            Opcode op = rop.inst().ops()[opnum];
            Symbol Result = rop.opargsym(op, 0);
            Symbol NodeID = rop.opargsym(op, 1);
            Symbol Name = rop.opargsym(op, 2);
            Symbol Value = rop.opargsym(op, 3);
            ILValue[] args = new ILValue[]
            {
                rop.sg_void_ptr(), rop.il_load_value(NodeID), rop.il_load_value(Name), rop.ll.constant(Value.typespec().simpletype()), rop.il_void_ptr(Value)
            };
            ILValue ret = rop.ll.call_function("osl_dict_value", args);
            rop.il_store_value(ret, Result);
            return true;
        }
    }
}
