// OSL port: the equivalent of LLVM_Util (llvm_util.cpp / OSL/llvm_util.h) for the .NET IL backend.
//
// LLVM_Util wraps an llvm::IRBuilder and hands out llvm::Value*s; ILUtil wraps an instruction recorder and hands out ILValues. An ILValue is an SSA-like
// handle: a constant, a function argument, an IL local (every computed value lives in its own local, so the evaluation stack is empty between
// operations, which is what lets labels/branches work anywhere), a stack-frame address (the alloca's) or the address of a constant data blob.
//
// The code of a function is first recorded (ILLabel.code of each block of ILFunction.blocks) and then replayed into a System.Reflection.Emit.ILGenerator by an ILTarget:
//      DynamicILTarget     DynamicMethod, for the JIT use; data blobs live in unmanaged memory whose address is embedded in the code
//      PersistedILTarget   PersistedAssemblyBuilder, for the .dll; data blobs are initialized static data (RVA fields), strings are re-registered by
//                          an "osl_group_strings" function that the loader calls (backend_persist.cs)
// Recording first has two advantages: the alloca frame size is only known when the function is complete (all allocas are carved out of one localloc
// at function entry, like LLVM places allocas in the entry block), and the code can be dumped as text for the llvm_debug options.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using OIIO;

namespace OSL.pvt
{
    /// The machine-level types of values the generated code handles (the llvm types that llvm_gen uses: bool/i8, i32, i64, float, double, pointers)
    public enum ILType
    {
        Void,
        I1,
        I4,
        I8,
        F4,
        F8,
        Ptr
    }

    public enum ILKind
    {
        Const,
        Local,
        Arg,
        Frame,
        Data
    }

    public sealed class ILLabel
    {
        public string name;
        public int id;
        /// The instructions of this block (like an llvm::BasicBlock, a label owns its code and the blocks are laid out in creation order)
        public List<ILInstr> code = new List<ILInstr>();

        public override string ToString()
        {
            return "L" + id + (string.IsNullOrEmpty(name) ? "" : "_" + name);
        }
    }

    /// A blob of constant data (the llvm global constants of llvm_create_constant)
    public sealed class ILDataRef
    {
        public string name;
        public byte[] bytes;
        /// Dynamic target: the unmanaged copy
        public IntPtr native;
        /// Persisted target: the static data field
        public FieldInfo field;
    }

    public sealed class ILValue
    {
        public ILType type;
        public ILKind kind;
        public long ival;
        public double dval;
        /// Local index, argument index or frame offset
        public int index;
        public ILDataRef data;
        public string name;

        public bool is_constant() { return kind == ILKind.Const; }

        public override string ToString()
        {
            switch (kind)
            {
                case ILKind.Const:
                    return type == ILType.F4 || type == ILType.F8 ? type + " " + dval : type + " " + ival;
                case ILKind.Local:
                    return "%l" + index + ":" + type;
                case ILKind.Arg:
                    return "%arg" + index;
                case ILKind.Frame:
                    return "frame+" + index;
                default:
                    return "data(" + data.name + ")+" + ival;
            }
        }
    }

    public sealed class ILInstr
    {
        public OpCode op;
        public object operand;

        public ILInstr(OpCode op, object operand)
        {
            this.op = op;
            this.operand = operand;
        }
    }

    /// A function under construction (the llvm::Function)
    public sealed class ILFunction
    {
        public string name;
        public ILType ret;
        public ILType[] parms;
        public List<ILLabel> blocks = new List<ILLabel>();
        /// Types of the locals; local 0 is the frame pointer
        public List<ILType> locals = new List<ILType>();
        public int frame_size;
        public int label_count;
        public MethodInfo method;
        public bool completed;
    }

    /// Where finished functions go: a DynamicMethod or a MethodBuilder of a persisted assembly
    public abstract class ILTarget
    {
        /// Called when a function is started (the persisted target defines the method, so that calls can be recorded before the body exists)
        public virtual void declare(ILFunction f)
        {
        }

        /// Called when the function is complete: 'replay' emits its code into the given generator
        public abstract void emit(ILFunction f, Action<ILGenerator> replay);

        public abstract void create_data(ILDataRef d);

        public virtual bool is_persisted()
        {
            return false;
        }

        /// Keep a managed object alive for the life of the code and return an opaque pointer-sized token for it (0 if the target cannot embed objects)
        public virtual long pin_object(object o)
        {
            return 0;
        }
    }

    public sealed unsafe class DynamicILTarget : ILTarget, IDisposable
    {
        private List<IntPtr> m_blobs = new List<IntPtr>();
        private List<GCHandle> m_pins = new List<GCHandle>();
        private bool m_disposed;

        public override long pin_object(object o)
        {
            GCHandle h = GCHandle.Alloc(o, GCHandleType.Normal);
            m_pins.Add(h);
            return (long)GCHandle.ToIntPtr(h);
        }

        public override void emit(ILFunction f, Action<ILGenerator> replay)
        {
            Type[] ptypes = new Type[f.parms.Length];
            for (int i = 0; i < ptypes.Length; ++i)
            {
                ptypes[i] = ILUtil.clr_type(f.parms[i]);
            }
            DynamicMethod dm = new DynamicMethod(f.name, ILUtil.clr_type(f.ret), ptypes, typeof(ILUtil).Module, true);
            replay(dm.GetILGenerator());
            f.method = dm;
        }

        public override void create_data(ILDataRef d)
        {
            int n = Math.Max(1, d.bytes.Length);
            IntPtr p = (IntPtr)NativeMemory.AlignedAlloc((nuint)n, 16);
            Marshal.Copy(d.bytes, 0, p, d.bytes.Length);
            d.native = p;
            m_blobs.Add(p);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                m_disposed = true;
                foreach (IntPtr p in m_blobs)
                {
                    NativeMemory.AlignedFree((void*)p);
                }
                m_blobs.Clear();
                foreach (GCHandle h in m_pins)
                {
                    h.Free();
                }
                m_pins.Clear();
            }
        }

        ~DynamicILTarget()
        {
            Dispose(false);
        }
    }

    public sealed class PersistedILTarget : ILTarget
    {
        public readonly TypeBuilder type;
        private Dictionary<ILFunction, MethodBuilder> m_builders = new Dictionary<ILFunction, MethodBuilder>();
        private int m_datacount;

        public PersistedILTarget(TypeBuilder tb)
        {
            type = tb;
        }

        public override bool is_persisted()
        {
            return true;
        }

        public override void declare(ILFunction f)
        {
            Type[] ptypes = new Type[f.parms.Length];
            for (int i = 0; i < ptypes.Length; ++i)
            {
                ptypes[i] = ILUtil.clr_type(f.parms[i]);
            }
            MethodBuilder mb = type.DefineMethod(f.name, MethodAttributes.Public | MethodAttributes.Static, ILUtil.clr_type(f.ret), ptypes);
            m_builders[f] = mb;
            f.method = mb;
        }

        public override void emit(ILFunction f, Action<ILGenerator> replay)
        {
            if (!m_builders.ContainsKey(f))
            {
                declare(f);
            }
            replay(m_builders[f].GetILGenerator());
        }

        public override void create_data(ILDataRef d)
        {
            byte[] bytes = d.bytes.Length == 0 ? new byte[1] : d.bytes;
            d.field = type.DefineInitializedData("__data" + (m_datacount++) + "_" + d.name, bytes, FieldAttributes.Public | FieldAttributes.Static);
        }
    }

    /// Thrown for malformed generated code (type mismatches the C++ would have asserted on)
    public sealed class ILGenException : Exception
    {
        public ILGenException(string msg) : base(msg)
        {
        }
    }

    public sealed unsafe partial class ILUtil
    {
        private ILTarget m_target;
        private ILFunction m_func;
        private ILLabel m_cur;
        private List<ILLabel[]> m_loops = new List<ILLabel[]>();
        private List<ILLabel> m_return_block = new List<ILLabel>();
        /// Every string constant that went into the code, by hash, so a persisted assembly can register them when it is loaded
        public Dictionary<ulong, string> string_constants = new Dictionary<ulong, string>();
        public List<ILFunction> functions = new List<ILFunction>();
        public int debug;

        public ILUtil(ILTarget target)
        {
            m_target = target;
        }

        public ILTarget target() { return m_target; }
        public ILFunction current_function() { return m_func; }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Types
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public static Type clr_type(ILType t)
        {
            switch (t)
            {
                case ILType.Void: return typeof(void);
                case ILType.I1: return typeof(int);
                case ILType.I4: return typeof(int);
                case ILType.I8: return typeof(long);
                case ILType.F4: return typeof(float);
                case ILType.F8: return typeof(double);
                default: return typeof(void*);
            }
        }

        public static ILType il_type(Type t)
        {
            if (t == typeof(void))
            {
                return ILType.Void;
            }
            if (t == typeof(int) || t == typeof(uint) || t == typeof(short) || t == typeof(ushort) || t == typeof(byte) || t == typeof(sbyte))
            {
                return ILType.I4;
            }
            if (t == typeof(bool))
            {
                return ILType.I1;
            }
            if (t == typeof(long) || t == typeof(ulong))
            {
                return ILType.I8;
            }
            if (t == typeof(float))
            {
                return ILType.F4;
            }
            if (t == typeof(double))
            {
                return ILType.F8;
            }
            if (t.IsPointer || t == typeof(IntPtr) || t == typeof(UIntPtr) || t.IsByRef)
            {
                return ILType.Ptr;
            }
            throw new ILGenException("unsupported parameter type " + t);
        }

        private static bool compatible(ILType a, ILType b)
        {
            if (a == b)
            {
                return true;
            }
            if ((a == ILType.I1 || a == ILType.I4) && (b == ILType.I1 || b == ILType.I4))
            {
                return true;
            }
            if ((a == ILType.I8 || a == ILType.Ptr) && (b == ILType.I8 || b == ILType.Ptr))
            {
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Functions and blocks
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public ILFunction make_function(string name, ILType ret, params ILType[] parms)
        {
            ILFunction f = new ILFunction();
            f.name = name;
            f.ret = ret;
            f.parms = parms;
            f.locals.Add(ILType.Ptr);
            ILLabel entry = new ILLabel();
            entry.name = "entry";
            entry.id = f.label_count++;
            f.blocks.Add(entry);
            functions.Add(f);
            m_target.declare(f);
            return f;
        }

        /// Make f the function code is recorded into (LLVM_Util::current_function(f) + new_builder)
        public void begin_function(ILFunction f)
        {
            m_func = f;
            m_cur = f.blocks[0];
            m_loops.Clear();
            m_return_block.Clear();
        }

        public ILValue current_function_arg(int i)
        {
            ILValue v = new ILValue();
            v.kind = ILKind.Arg;
            v.index = i;
            v.type = m_func.parms[i];
            return v;
        }

        /// Finish the function: emit it into the target (LLVM_Util::end_builder + the later JIT)
        public void end_function()
        {
            ILFunction f = m_func;
            f.completed = true;
            m_target.emit(f, delegate(ILGenerator il)
            {
                replay(f, il);
            });
            m_func = null;
        }

        public ILLabel new_basic_block(string name = "")
        {
            ILLabel l = new ILLabel();
            l.name = name;
            l.id = m_func.label_count++;
            m_func.blocks.Add(l);
            return l;
        }

        /// Make 'block' the place where code goes next (marks the label)
        public void set_insert_point(ILLabel block)
        {
            m_cur = block;
        }

        public void op_branch(ILLabel block)
        {
            record(OpCodes.Br, block);
            set_insert_point(block);
        }

        public void op_branch(ILValue cond, ILLabel trueblock, ILLabel falseblock)
        {
            load(cond);
            record(OpCodes.Brtrue, trueblock);
            record(OpCodes.Br, falseblock);
            set_insert_point(trueblock);
        }

        public void op_return(ILValue retval = null)
        {
            if (retval != null)
            {
                load(retval);
            }
            record(OpCodes.Ret, null);
        }

        public void push_loop(ILLabel step, ILLabel after)
        {
            m_loops.Add(new ILLabel[] { step, after });
        }

        public void pop_loop()
        {
            m_loops.RemoveAt(m_loops.Count - 1);
        }

        public ILLabel push_function(ILLabel after = null)
        {
            if (after == null)
            {
                after = new_basic_block("after_function");
            }
            m_return_block.Add(after);
            return after;
        }

        public bool inside_function()
        {
            return m_return_block.Count != 0;
        }

        public void pop_function()
        {
            set_insert_point(m_return_block[m_return_block.Count - 1]);
            m_return_block.RemoveAt(m_return_block.Count - 1);
        }

        public ILLabel return_block()
        {
            return m_return_block[m_return_block.Count - 1];
        }

        public ILLabel loop_step_block() { return m_loops[m_loops.Count - 1][0]; }
        public ILLabel loop_after_block() { return m_loops[m_loops.Count - 1][1]; }

        private void record(OpCode op, object operand)
        {
            m_cur.code.Add(new ILInstr(op, operand));
        }

        private int new_local(ILType t)
        {
            m_func.locals.Add(t);
            return m_func.locals.Count - 1;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Constants
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private static ILValue make_const(ILType t, long i, double d)
        {
            ILValue v = new ILValue();
            v.kind = ILKind.Const;
            v.type = t;
            v.ival = i;
            v.dval = d;
            return v;
        }

        public ILValue constant(int i) { return make_const(ILType.I4, i, 0); }
        public ILValue constant(float f) { return make_const(ILType.F4, 0, f); }
        public ILValue constanti64(long i) { return make_const(ILType.I8, i, 0); }
        public ILValue constant64(ulong i) { return make_const(ILType.I8, (long)i, 0); }
        public ILValue constant_bool(bool b) { return make_const(ILType.I1, b ? 1 : 0, 0); }
        public ILValue constant8(int i) { return make_const(ILType.I1, i & 0xff, 0); }
        public ILValue constant_ptr(long p) { return make_const(ILType.Ptr, p, 0); }
        public ILValue void_ptr_null() { return make_const(ILType.Ptr, 0, 0); }

        /// A string constant is its 64-bit ustring hash (LLVM_Util::UstringRep::hash)
        public ILValue constant(ustring s)
        {
            string_constants[s.hash()] = s.@string();
            return make_const(ILType.I8, (long)s.hash(), 0);
        }

        public ILValue constant(TypeDesc t)
        {
            return make_const(ILType.I8, TypeDescBits.to_long(t), 0);
        }

        /// A blob of constant data and its address (create_global_constant)
        public ILValue create_global_constant(byte[] bytes, string name)
        {
            ILDataRef d = new ILDataRef();
            d.name = name;
            d.bytes = bytes;
            m_target.create_data(d);
            ILValue v = new ILValue();
            v.kind = ILKind.Data;
            v.type = ILType.Ptr;
            v.data = d;
            v.ival = 0;
            return v;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Loading values onto the evaluation stack
        // ------------------------------------------------------------------------------------------------------------------------------------------

        /// Push the value on the evaluation stack
        public void load(ILValue v)
        {
            switch (v.kind)
            {
                case ILKind.Const:
                    switch (v.type)
                    {
                        case ILType.I1:
                        case ILType.I4:
                            record(OpCodes.Ldc_I4, (int)v.ival);
                            break;
                        case ILType.I8:
                            record(OpCodes.Ldc_I8, v.ival);
                            break;
                        case ILType.F4:
                            record(OpCodes.Ldc_R4, (float)v.dval);
                            break;
                        case ILType.F8:
                            record(OpCodes.Ldc_R8, v.dval);
                            break;
                        default:
                            record(OpCodes.Ldc_I8, v.ival);
                            record(OpCodes.Conv_U, null);
                            break;
                    }
                    break;
                case ILKind.Local:
                    record(OpCodes.Ldloc, v.index);
                    break;
                case ILKind.Arg:
                    record(OpCodes.Ldarg, v.index);
                    break;
                case ILKind.Frame:
                    record(OpCodes.Ldloc, 0);
                    if (v.index != 0)
                    {
                        record(OpCodes.Ldc_I4, v.index);
                        record(OpCodes.Add, null);
                    }
                    break;
                default:
                    if (m_target.is_persisted())
                    {
                        record(OpCodes.Ldsflda, v.data);
                    }
                    else
                    {
                        record(OpCodes.Ldc_I8, (long)v.data.native);
                    }
                    record(OpCodes.Conv_U, null);
                    if (v.ival != 0)
                    {
                        record(OpCodes.Ldc_I4, (int)v.ival);
                        record(OpCodes.Add, null);
                    }
                    break;
            }
        }

        /// Pop the stack top into a fresh local
        private ILValue store_temp(ILType t)
        {
            int idx = new_local(t);
            record(OpCodes.Stloc, idx);
            ILValue v = new ILValue();
            v.kind = ILKind.Local;
            v.type = t;
            v.index = idx;
            return v;
        }

        /// A mutable local (for values that are assigned in several blocks, where LLVM would use a phi)
        public ILValue new_variable(ILType t)
        {
            int idx = new_local(t);
            ILValue v = new ILValue();
            v.kind = ILKind.Local;
            v.type = t;
            v.index = idx;
            return v;
        }

        public void assign_variable(ILValue variable, ILValue value)
        {
            load(value);
            record(OpCodes.Stloc, variable.index);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Pointers
        // ------------------------------------------------------------------------------------------------------------------------------------------

        /// Reinterpretations are no-ops in IL: pointers are native ints (these exist to keep the call sites close to llvm_gen)
        public ILValue void_ptr(ILValue val, string llname = "")
        {
            return val;
        }

        public ILValue ptr_cast(ILValue val, string llname = "")
        {
            return val;
        }

        public ILValue offset_ptr(ILValue ptr, int offset)
        {
            if (offset == 0)
            {
                return ptr;
            }
            if (ptr.kind == ILKind.Frame)
            {
                ILValue r = new ILValue();
                r.kind = ILKind.Frame;
                r.type = ILType.Ptr;
                r.index = ptr.index + offset;
                return r;
            }
            if (ptr.kind == ILKind.Data)
            {
                ILValue r = new ILValue();
                r.kind = ILKind.Data;
                r.type = ILType.Ptr;
                r.data = ptr.data;
                r.ival = ptr.ival + offset;
                return r;
            }
            if (ptr.kind == ILKind.Const)
            {
                return constant_ptr(ptr.ival + offset);
            }
            load(ptr);
            record(OpCodes.Ldc_I4, offset);
            record(OpCodes.Add, null);
            return store_temp(ILType.Ptr);
        }

        /// ptr + offset, where the offset is a run-time byte offset (int or long)
        public ILValue offset_ptr(ILValue ptr, ILValue offset)
        {
            if (offset.kind == ILKind.Const)
            {
                return offset_ptr(ptr, (int)offset.ival);
            }
            load(ptr);
            load(offset);
            if (offset.type == ILType.I4 || offset.type == ILType.I1)
            {
                record(OpCodes.Conv_I, null);
            }
            record(OpCodes.Add, null);
            return store_temp(ILType.Ptr);
        }

        /// ptr + index * elemsize (the GEP over an array of elements of 'elemsize' bytes)
        public ILValue GEP(ILValue ptr, ILValue index, int elemsize)
        {
            if (index.kind == ILKind.Const)
            {
                return offset_ptr(ptr, (int)index.ival * elemsize);
            }
            load(ptr);
            load(index);
            if (index.type == ILType.I4 || index.type == ILType.I1)
            {
                record(OpCodes.Conv_I, null);
            }
            if (elemsize != 1)
            {
                record(OpCodes.Ldc_I4, elemsize);
                record(OpCodes.Conv_I, null);
                record(OpCodes.Mul, null);
            }
            record(OpCodes.Add, null);
            return store_temp(ILType.Ptr);
        }

        public ILValue GEP(ILValue ptr, int index, int elemsize)
        {
            return offset_ptr(ptr, index * elemsize);
        }

        /// Reserve 'size' bytes in the frame of the current function (alloca)
        public ILValue op_alloca(int size, int align = 16, string name = "")
        {
            if (align < 4)
            {
                align = 4;
            }
            if (align > 16)
            {
                align = 16;
            }
            int off = (m_func.frame_size + align - 1) / align * align;
            m_func.frame_size = off + Math.Max(size, 1);
            ILValue v = new ILValue();
            v.kind = ILKind.Frame;
            v.type = ILType.Ptr;
            v.index = off;
            v.name = name;
            return v;
        }

        public ILValue op_load(ILType t, ILValue ptr, string llname = "")
        {
            load(ptr);
            switch (t)
            {
                case ILType.I1:
                    record(OpCodes.Ldind_U1, null);
                    break;
                case ILType.I4:
                    record(OpCodes.Ldind_I4, null);
                    break;
                case ILType.I8:
                    record(OpCodes.Ldind_I8, null);
                    break;
                case ILType.F4:
                    record(OpCodes.Ldind_R4, null);
                    break;
                case ILType.F8:
                    record(OpCodes.Ldind_R8, null);
                    break;
                default:
                    record(OpCodes.Ldind_I, null);
                    break;
            }
            return store_temp(t);
        }

        public void op_store(ILValue val, ILValue ptr)
        {
            load(ptr);
            load(val);
            switch (val.type)
            {
                case ILType.I1:
                    record(OpCodes.Stind_I1, null);
                    break;
                case ILType.I4:
                    record(OpCodes.Stind_I4, null);
                    break;
                case ILType.I8:
                    record(OpCodes.Stind_I8, null);
                    break;
                case ILType.F4:
                    record(OpCodes.Stind_R4, null);
                    break;
                case ILType.F8:
                    record(OpCodes.Stind_R8, null);
                    break;
                default:
                    record(OpCodes.Stind_I, null);
                    break;
            }
        }

        /// Store an int as one byte (the int8 flags of the group data)
        public void op_store8(ILValue val, ILValue ptr)
        {
            load(ptr);
            load(val);
            record(OpCodes.Stind_I1, null);
        }

        public void op_memset(ILValue ptr, int val, int len, int align = 1)
        {
            if (len <= 0)
            {
                return;
            }
            load(ptr);
            record(OpCodes.Ldc_I4, val);
            record(OpCodes.Ldc_I4, len);
            record(OpCodes.Initblk, null);
        }

        public void op_memset(ILValue ptr, int val, ILValue len, int align = 1)
        {
            if (len.kind == ILKind.Const)
            {
                op_memset(ptr, val, (int)len.ival, align);
                return;
            }
            load(ptr);
            record(OpCodes.Ldc_I4, val);
            load(len);
            record(OpCodes.Initblk, null);
        }

        public void op_memcpy(ILValue dst, ILValue src, int len, int align = 1)
        {
            if (len <= 0)
            {
                return;
            }
            load(dst);
            load(src);
            record(OpCodes.Ldc_I4, len);
            record(OpCodes.Cpblk, null);
        }

        public void op_memcpy(ILValue dst, ILValue src, ILValue len, int align = 1)
        {
            if (len.kind == ILKind.Const)
            {
                op_memcpy(dst, src, (int)len.ival, align);
                return;
            }
            load(dst);
            load(src);
            load(len);
            record(OpCodes.Cpblk, null);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Arithmetic
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private static bool is_int(ILType t) { return t == ILType.I4 || t == ILType.I1; }

        private ILValue binary(OpCode op, ILValue a, ILValue b, string opname)
        {
            if (!compatible(a.type, b.type))
            {
                throw new ILGenException("Op " + opname + " has bad value type combination " + a.type + " " + b.type + " in " + m_func.name);
            }
            load(a);
            load(b);
            record(op, null);
            return store_temp(a.type == ILType.I1 ? ILType.I4 : a.type);
        }

        public ILValue op_add(ILValue a, ILValue b)
        {
            if (a.kind == ILKind.Const && b.kind == ILKind.Const && is_int(a.type) && is_int(b.type))
            {
                return constant((int)(a.ival + b.ival));
            }
            if (a.type == ILType.Ptr && b.type != ILType.Ptr)
            {
                return offset_ptr(a, b);
            }
            return binary(OpCodes.Add, a, b, "add");
        }

        public ILValue op_sub(ILValue a, ILValue b)
        {
            if (a.kind == ILKind.Const && b.kind == ILKind.Const && is_int(a.type) && is_int(b.type))
            {
                return constant((int)(a.ival - b.ival));
            }
            return binary(OpCodes.Sub, a, b, "sub");
        }

        public ILValue op_mul(ILValue a, ILValue b)
        {
            if (a.kind == ILKind.Const && b.kind == ILKind.Const && is_int(a.type) && is_int(b.type))
            {
                return constant((int)(a.ival * b.ival));
            }
            return binary(OpCodes.Mul, a, b, "mul");
        }

        public ILValue op_div(ILValue a, ILValue b)
        {
            return binary(OpCodes.Div, a, b, "div");
        }

        /// Float remainder is fmod (LLVM frem), int remainder is the signed srem
        public ILValue op_mod(ILValue a, ILValue b)
        {
            return binary(OpCodes.Rem, a, b, "mod");
        }

        public ILValue op_neg(ILValue a)
        {
            load(a);
            record(OpCodes.Neg, null);
            return store_temp(a.type == ILType.I1 ? ILType.I4 : a.type);
        }

        public ILValue op_not(ILValue a)
        {
            if (a.type == ILType.I1)
            {
                load(a);
                record(OpCodes.Ldc_I4_0, null);
                record(OpCodes.Ceq, null);
                return store_temp(ILType.I1);
            }
            load(a);
            record(OpCodes.Not, null);
            return store_temp(a.type);
        }

        public ILValue op_and(ILValue a, ILValue b)
        {
            ILValue r = binary(OpCodes.And, a, b, "and");
            if (a.type == ILType.I1 && b.type == ILType.I1)
            {
                r.type = ILType.I1;
            }
            return r;
        }

        public ILValue op_or(ILValue a, ILValue b)
        {
            ILValue r = binary(OpCodes.Or, a, b, "or");
            if (a.type == ILType.I1 && b.type == ILType.I1)
            {
                r.type = ILType.I1;
            }
            return r;
        }

        public ILValue op_xor(ILValue a, ILValue b)
        {
            ILValue r = binary(OpCodes.Xor, a, b, "xor");
            if (a.type == ILType.I1 && b.type == ILType.I1)
            {
                r.type = ILType.I1;
            }
            return r;
        }

        public ILValue op_shl(ILValue a, ILValue b)
        {
            load(a);
            load(b);
            record(OpCodes.Shl, null);
            return store_temp(a.type);
        }

        /// Signed int: arithmetic shift
        public ILValue op_shr(ILValue a, ILValue b)
        {
            load(a);
            load(b);
            record(OpCodes.Shr, null);
            return store_temp(a.type);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Comparisons (the 'ordered' flags decide what happens on NaN, as LLVM's fcmp predicates do; the result is an I1)
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private ILValue compare(ILValue a, ILValue b, int kind, bool ordered)
        {
            // kind: 0 eq, 1 ne, 2 gt, 3 lt, 4 ge, 5 le
            if (!compatible(a.type, b.type))
            {
                throw new ILGenException("compare has bad value type combination " + a.type + " " + b.type + " in " + m_func.name);
            }
            bool isf = a.type == ILType.F4 || a.type == ILType.F8;
            if (!isf)
            {
                load(a);
                load(b);
                switch (kind)
                {
                    case 0:
                        record(OpCodes.Ceq, null);
                        break;
                    case 1:
                        record(OpCodes.Ceq, null);
                        record(OpCodes.Ldc_I4_0, null);
                        record(OpCodes.Ceq, null);
                        break;
                    case 2:
                        record(OpCodes.Cgt, null);
                        break;
                    case 3:
                        record(OpCodes.Clt, null);
                        break;
                    case 4:
                        record(OpCodes.Clt, null);
                        record(OpCodes.Ldc_I4_0, null);
                        record(OpCodes.Ceq, null);
                        break;
                    default:
                        record(OpCodes.Cgt, null);
                        record(OpCodes.Ldc_I4_0, null);
                        record(OpCodes.Ceq, null);
                        break;
                }
                return store_temp(ILType.I1);
            }
            switch (kind)
            {
                case 0:
                    if (ordered)
                    {
                        load(a);
                        load(b);
                        record(OpCodes.Ceq, null);
                    }
                    else
                    {
                        // UEQ: not (a<b or a>b)
                        load(a);
                        load(b);
                        record(OpCodes.Clt, null);
                        load(a);
                        load(b);
                        record(OpCodes.Cgt, null);
                        record(OpCodes.Or, null);
                        record(OpCodes.Ldc_I4_0, null);
                        record(OpCodes.Ceq, null);
                    }
                    break;
                case 1:
                    if (ordered)
                    {
                        // ONE: a<b or a>b
                        load(a);
                        load(b);
                        record(OpCodes.Clt, null);
                        load(a);
                        load(b);
                        record(OpCodes.Cgt, null);
                        record(OpCodes.Or, null);
                    }
                    else
                    {
                        // UNE: not (a==b)
                        load(a);
                        load(b);
                        record(OpCodes.Ceq, null);
                        record(OpCodes.Ldc_I4_0, null);
                        record(OpCodes.Ceq, null);
                    }
                    break;
                case 2:
                    load(a);
                    load(b);
                    record(ordered ? OpCodes.Cgt : OpCodes.Cgt_Un, null);
                    break;
                case 3:
                    load(a);
                    load(b);
                    record(ordered ? OpCodes.Clt : OpCodes.Clt_Un, null);
                    break;
                case 4:
                    // OGE: not (a<b or unordered) ; UGE: not (a<b)
                    load(a);
                    load(b);
                    record(ordered ? OpCodes.Clt_Un : OpCodes.Clt, null);
                    record(OpCodes.Ldc_I4_0, null);
                    record(OpCodes.Ceq, null);
                    break;
                default:
                    load(a);
                    load(b);
                    record(ordered ? OpCodes.Cgt_Un : OpCodes.Cgt, null);
                    record(OpCodes.Ldc_I4_0, null);
                    record(OpCodes.Ceq, null);
                    break;
            }
            return store_temp(ILType.I1);
        }

        public ILValue op_eq(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 0, ordered); }
        public ILValue op_ne(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 1, ordered); }
        public ILValue op_gt(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 2, ordered); }
        public ILValue op_lt(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 3, ordered); }
        public ILValue op_ge(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 4, ordered); }
        public ILValue op_le(ILValue a, ILValue b, bool ordered = false) { return compare(a, b, 5, ordered); }

        public ILValue op_select(ILValue cond, ILValue a, ILValue b)
        {
            if (!compatible(a.type, b.type))
            {
                throw new ILGenException("select has bad value type combination " + a.type + " " + b.type + " in " + m_func.name);
            }
            ILType t = a.type == ILType.I1 ? ILType.I4 : a.type;
            ILValue result = new_variable(t);
            ILLabel lt = new_basic_block("sel_t");
            ILLabel lend = new_basic_block("sel_end");
            load(cond);
            record(OpCodes.Brtrue, lt);
            load(b);
            record(OpCodes.Stloc, result.index);
            record(OpCodes.Br, lend);
            set_insert_point(lt);
            load(a);
            record(OpCodes.Stloc, result.index);
            set_insert_point(lend);
            return result;
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Conversions
        // ------------------------------------------------------------------------------------------------------------------------------------------

        public ILValue op_float_to_int(ILValue a)
        {
            if (a.type == ILType.I4)
            {
                return a;
            }
            load(a);
            record(OpCodes.Conv_I4, null);
            return store_temp(ILType.I4);
        }

        public ILValue op_float_to_double(ILValue a)
        {
            load(a);
            record(OpCodes.Conv_R8, null);
            return store_temp(ILType.F8);
        }

        public ILValue op_int_to_float(ILValue a)
        {
            if (a.type == ILType.F4)
            {
                return a;
            }
            if (a.kind == ILKind.Const)
            {
                return constant((float)a.ival);
            }
            load(a);
            record(OpCodes.Conv_R4, null);
            return store_temp(ILType.F4);
        }

        public ILValue op_int_to_longlong(ILValue a)
        {
            if (a.kind == ILKind.Const)
            {
                return constanti64(a.ival);
            }
            load(a);
            record(OpCodes.Conv_I8, null);
            return store_temp(ILType.I8);
        }

        public ILValue op_bool_to_int(ILValue a)
        {
            if (a.type == ILType.I1)
            {
                ILValue r = new ILValue();
                r.kind = a.kind;
                r.type = ILType.I4;
                r.ival = a.ival;
                r.index = a.index;
                return r;
            }
            return a;
        }

        public ILValue op_int8_to_int(ILValue a)
        {
            return a;
        }

        public ILValue op_int_to_int8(ILValue a)
        {
            return a;
        }

        /// A pointer as the 64-bit integer (ptr_to_int64_cast)
        public ILValue ptr_to_int64_cast(ILValue a)
        {
            load(a);
            record(OpCodes.Conv_U8, null);
            return store_temp(ILType.I8);
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Calls
        // ------------------------------------------------------------------------------------------------------------------------------------------

        /// Call a function that was generated into the same target (a layer function)
        public ILValue call_function(ILFunction callee, params ILValue[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                load(args[i]);
            }
            record(OpCodes.Call, callee);
            if (callee.ret == ILType.Void)
            {
                return null;
            }
            return store_temp(callee.ret);
        }

        /// Call a managed method (a shadeop)
        public ILValue call_method(MethodInfo mi, string name, params ILValue[] args)
        {
            ParameterInfo[] pars = mi.GetParameters();
            if (pars.Length != args.Length)
            {
                throw new ILGenException("call to " + name + " with " + args.Length + " arguments, but it takes " + pars.Length);
            }
            for (int i = 0; i < args.Length; ++i)
            {
                ILType pt = il_type(pars[i].ParameterType);
                if (!compatible(pt, args[i].type))
                {
                    throw new ILGenException("call to " + name + " argument " + i + " is " + args[i].type + " but the function takes " + pt + " in " + m_func.name);
                }
                load(args[i]);
            }
            record(OpCodes.Call, mi);
            ILType rt = il_type(mi.ReturnType);
            if (rt == ILType.Void)
            {
                return null;
            }
            return store_temp(rt);
        }

        /// Call a shadeop by its name (LLVM_Util::call_function(const char*, args)). Names that have no managed implementation compile to a call that
        /// throws, and are recorded in ILRuntimeTable.missing for the report.
        public ILValue call_function(string name, params ILValue[] args)
        {
            MethodInfo mi = ILRuntimeTable.find(name);
            if (mi != null)
            {
                return call_method(mi, name, args);
            }
            ILRuntimeTable.note_missing(name);
            string sig = BuiltinDecl.signature(name);
            for (int i = 0; i < args.Length; ++i)
            {
                // the arguments are dropped: only their evaluation (already done: values are locals/constants) is irrelevant
            }
            record(OpCodes.Ldstr, name);
            record(OpCodes.Call, typeof(ILRuntimeTable).GetMethod("missing_function"));
            if (sig == null || sig.Length == 0 || sig[0] == 'x')
            {
                return null;
            }
            ILType rt = BuiltinDecl.il_type_of(sig[0]);
            switch (rt)
            {
                case ILType.F4:
                    return constant(0.0f);
                case ILType.I8:
                    return constanti64(0);
                case ILType.Ptr:
                    return void_ptr_null();
                default:
                    return constant(0);
            }
        }

        // ------------------------------------------------------------------------------------------------------------------------------------------
        // Replay into an ILGenerator
        // ------------------------------------------------------------------------------------------------------------------------------------------

        private void replay(ILFunction f, ILGenerator il)
        {
            LocalBuilder[] locals = new LocalBuilder[f.locals.Count];
            for (int i = 0; i < locals.Length; ++i)
            {
                locals[i] = il.DeclareLocal(clr_type(f.locals[i]));
            }
            Dictionary<ILLabel, Label> labels = new Dictionary<ILLabel, Label>();
            Dictionary<ILLabel, bool> marked = new Dictionary<ILLabel, bool>();
            if (f.frame_size > 0)
            {
                il.Emit(OpCodes.Ldc_I4, (f.frame_size + 15) & ~15);
                il.Emit(OpCodes.Conv_U);
                il.Emit(OpCodes.Localloc);
                il.Emit(OpCodes.Stloc, locals[0]);
            }
            foreach (ILLabel block in f.blocks)
            {
                if (!labels.ContainsKey(block))
                {
                    labels[block] = il.DefineLabel();
                }
                il.MarkLabel(labels[block]);
                foreach (ILInstr ins in block.code)
                {
                    replay_instr(f, il, ins, locals, labels);
                }
            }
            // A function that falls off the end returns (LLVM blocks are always terminated; here the last block may be open)
            if (f.ret == ILType.Void)
            {
                il.Emit(OpCodes.Ret);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Ret);
            }
        }

        private void replay_instr(ILFunction f, ILGenerator il, ILInstr ins, LocalBuilder[] locals, Dictionary<ILLabel, Label> labels)
        {
            {
                object o = ins.operand;
                if (o == null)
                {
                    il.Emit(ins.op);
                }
                else if (o is ILLabel)
                {
                    ILLabel l = (ILLabel)o;
                    if (!labels.ContainsKey(l))
                    {
                        labels[l] = il.DefineLabel();
                    }
                    il.Emit(ins.op, labels[l]);
                }
                else if (ins.op == OpCodes.Ldloc || ins.op == OpCodes.Stloc)
                {
                    il.Emit(ins.op, locals[(int)o]);
                }
                else if (ins.op == OpCodes.Ldarg)
                {
                    il.Emit(OpCodes.Ldarg, (short)(int)o);
                }
                else if (o is int)
                {
                    il.Emit(ins.op, (int)o);
                }
                else if (o is long)
                {
                    il.Emit(ins.op, (long)o);
                }
                else if (o is float)
                {
                    il.Emit(ins.op, (float)o);
                }
                else if (o is double)
                {
                    il.Emit(ins.op, (double)o);
                }
                else if (o is string)
                {
                    il.Emit(ins.op, (string)o);
                }
                else if (o is MethodInfo)
                {
                    il.Emit(ins.op, (MethodInfo)o);
                }
                else if (o is ILFunction)
                {
                    ILFunction callee = (ILFunction)o;
                    if (callee.method == null)
                    {
                        throw new ILGenException("function " + f.name + " calls " + callee.name + " which has not been emitted yet");
                    }
                    il.Emit(ins.op, callee.method);
                }
                else if (o is ILDataRef)
                {
                    ILDataRef d = (ILDataRef)o;
                    if (d.field != null)
                    {
                        il.Emit(ins.op, d.field);
                    }
                    else
                    {
                        throw new ILGenException("data blob without field");
                    }
                }
                else
                {
                    throw new ILGenException("bad operand " + o);
                }
            }
        }

        /// Text form of the recorded code (the bitcode_string of the debug options)
        public static string dump(ILFunction f)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("define ").Append(f.ret).Append(' ').Append(f.name).Append('(');
            for (int i = 0; i < f.parms.Length; ++i)
            {
                sb.Append(i == 0 ? "" : ", ").Append(f.parms[i]);
            }
            sb.Append(")  frame=").Append(f.frame_size).Append(" locals=").Append(f.locals.Count).Append('\n');
            foreach (ILLabel block in f.blocks)
            {
                sb.Append(block).Append(":\n");
                foreach (ILInstr ins in block.code)
                {
                sb.Append("    ").Append(ins.op.Name);
                if (ins.operand != null)
                {
                    object o = ins.operand;
                    sb.Append(' ');
                    if (o is MethodInfo)
                    {
                        sb.Append(((MethodInfo)o).Name);
                    }
                    else if (o is ILFunction)
                    {
                        sb.Append(((ILFunction)o).name);
                    }
                    else if (o is ILDataRef)
                    {
                        sb.Append(((ILDataRef)o).name);
                    }
                    else
                    {
                        sb.Append(o);
                    }
                }
                sb.Append('\n');
                }
            }
            return sb.ToString();
        }
    }
}
