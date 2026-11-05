// OSL port: include/OSL/oslclosure.h and include/OSL/genclosure.h
//
// Closures live in memory the shading context hands out (its closure pool), so the structures are unmanaged with the layout of the C++: a ClosureColor
// is its id; a ClosureComponent is the id and a weight, followed in memory by the closure's parameter data; ClosureMul and ClosureAdd point at their
// operands. The IL backend and the renderers reach them through pointers.

using System;
using System.Runtime.InteropServices;
using OIIO;

namespace OSL
{
    /// Names of the standard light path event labels
    public static class Labels
    {
        public static readonly ustring NONE = new ustring("");
        public static readonly ustring CAMERA = new ustring("C");
        public static readonly ustring LIGHT = new ustring("L");
        public static readonly ustring BACKGROUND = new ustring("B");
        public static readonly ustring VOLUME = new ustring("V");
        public static readonly ustring OBJECT = new ustring("O");
        public static readonly ustring TRANSMIT = new ustring("T");
        public static readonly ustring REFLECT = new ustring("R");
        public static readonly ustring DIFFUSE = new ustring("D");
        public static readonly ustring GLOSSY = new ustring("G");
        public static readonly ustring SINGULAR = new ustring("S");
        public static readonly ustring STRAIGHT = new ustring("s");
        public static readonly ustring STOP = new ustring("__stop__");
    }

    /// Base of the closure tree nodes: the id says which kind of node follows
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClosureColor
    {
        public const int COMPONENT_BASE_ID = 0;
        public const int MUL = -1;
        public const int ADD = -2;

        public int id;

        public static ClosureComponent* as_comp(ClosureColor* c) { return (ClosureComponent*)c; }
        public static ClosureMul* as_mul(ClosureColor* c) { return (ClosureMul*)c; }
        public static ClosureAdd* as_add(ClosureColor* c) { return (ClosureAdd*)c; }
    }

    /// A closure primitive: its id (>= 0), its weight, and after it in memory the parameter data
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClosureComponent
    {
        public int id;
        public Vec3 w;

        /// Pointer to the parameter data that follows the component
        public static void* data(ClosureComponent* c) { return (byte*)(c + 1); }
    }

    /// A closure scaled by a color weight
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClosureMul
    {
        public int id;
        public Color3 weight;
        public ClosureColor* closure;
    }

    /// The sum of two closures
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ClosureAdd
    {
        public int id;
        public ClosureColor* closureA;
        public ClosureColor* closureB;
    }

    /// Description of one parameter of a closure primitive: its type, where it lives in the closure's data, an optional keyword, and the size of the field.
    /// The list of ClosureParam of a closure ends with a CLOSURE_FINISH_PARAM entry (an UNKNOWN type, the struct size in 'offset', its alignment in
    /// 'field_size').
    public struct ClosureParam
    {
        public TypeDesc type;
        public int offset;
        public string key;
        public int field_size;

        public ClosureParam(TypeDesc type, int offset, string key, int field_size)
        {
            this.type = type;
            this.offset = offset;
            this.key = key;
            this.field_size = field_size;
        }

        public static ClosureParam CLOSURE_INT_PARAM(int offset) { return new ClosureParam(TypeDescs.TypeInt, offset, null, sizeof(int)); }
        public static ClosureParam CLOSURE_FLOAT_PARAM(int offset) { return new ClosureParam(TypeDescs.TypeFloat, offset, null, sizeof(float)); }
        public static ClosureParam CLOSURE_COLOR_PARAM(int offset) { return new ClosureParam(TypeDescs.TypeColor, offset, null, 3 * sizeof(float)); }
        public static ClosureParam CLOSURE_VECTOR_PARAM(int offset) { return new ClosureParam(TypeDescs.TypeVector, offset, null, 3 * sizeof(float)); }
        public static ClosureParam CLOSURE_STRING_PARAM(int offset) { return new ClosureParam(TypeDescs.TypeString, offset, null, sizeof(ulong)); }
        public static ClosureParam CLOSURE_CLOSURE_PARAM(int offset) { return new ClosureParam(new TypeDesc(TypeDesc.PTR), offset, null, IntPtr.Size); }
        public static ClosureParam CLOSURE_INT_KEYPARAM(int offset, string key) { return new ClosureParam(TypeDescs.TypeInt, offset, key, sizeof(int)); }
        public static ClosureParam CLOSURE_FLOAT_KEYPARAM(int offset, string key) { return new ClosureParam(TypeDescs.TypeFloat, offset, key, sizeof(float)); }
        public static ClosureParam CLOSURE_COLOR_KEYPARAM(int offset, string key) { return new ClosureParam(TypeDescs.TypeColor, offset, key, 3 * sizeof(float)); }
        public static ClosureParam CLOSURE_VECTOR_KEYPARAM(int offset, string key) { return new ClosureParam(TypeDescs.TypeVector, offset, key, 3 * sizeof(float)); }
        public static ClosureParam CLOSURE_STRING_KEYPARAM(int offset, string key) { return new ClosureParam(TypeDescs.TypeString, offset, key, sizeof(ulong)); }
        public static ClosureParam CLOSURE_FINISH_PARAM(int structsize, int alignment) { return new ClosureParam(new TypeDesc(), structsize, null, alignment); }
    }

    /// Callbacks a renderer registers to initialize and finish the data of a closure primitive
    public unsafe delegate void PrepareClosureFunc(RendererServices rs, int id, void* data);
    public unsafe delegate void SetupClosureFunc(RendererServices rs, int id, void* data);
}
