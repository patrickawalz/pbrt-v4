// OSL port: include/osl_pvt.h and liboslexec/typespec.cpp, liboslcomp/symtab.cpp (Symbol, StructSpec), liboslexec/oslexec.cpp (shadertypename)
//
// The compiler's and the runtime's shared description of types and symbols. Where the C++ stores a symbol's constant as a raw pointer the
// C# keeps the managed array (int[], float[] or ustring[]) in 'data'; the bit fields are plain bools and ints.

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using static OIIO.TypeDescs;

namespace OSL
{
    public enum SymArena
    {
        Unknown = 0,    // Unknown/uninitialized value
        Absolute,       // Absolute address
        Heap,           // Belongs to context heap
        Outputs,        // Belongs to output arena
        UserData,       // UserData arena
        Interactive     // Interactively edited variables
    }

    namespace pvt
    {

        /// Kinds of shaders
        public enum ShaderType
        {
            Unknown = 0,
            Generic,
            Surface,
            Displacement,
            Volume,
            Light,
            Last
        }

        /// Kinds of symbols
        public enum SymType
        {
            SymTypeParam,
            SymTypeOutputParam,
            SymTypeLocal,
            SymTypeTemp,
            SymTypeGlobal,
            SymTypeConst,
            SymTypeFunction,
            SymTypeType,
            SymTypeLast
        }

        public static class PvtFunctions
        {
            public static string shadertypename(ShaderType s)
            {
                switch (s)
                {
                    case ShaderType.Generic: return "shader";
                    case ShaderType.Surface: return "surface";
                    case ShaderType.Displacement: return "displacement";
                    case ShaderType.Volume: return "volume";
                    case ShaderType.Light: return "light";
                    default: return "unknown";
                }
            }

            public static ShaderType shadertype_from_name(string name)
            {
                if (name == "shader" || name == "generic")
                {
                    return ShaderType.Generic;
                }
                if (name == "surface")
                {
                    return ShaderType.Surface;
                }
                if (name == "displacement")
                {
                    return ShaderType.Displacement;
                }
                if (name == "volume")
                {
                    return ShaderType.Volume;
                }
                if (name == "light")
                {
                    return ShaderType.Light;
                }
                return ShaderType.Unknown;
            }

            public static bool equivalent(StructSpec a, StructSpec b)
            {
                if (a.numfields() != b.numfields())
                {
                    return false;
                }
                for (int i = 0; i < a.numfields(); ++i)
                {
                    if (!equivalent(a.field(i).type, b.field(i).type))
                    {
                        return false;
                    }
                }
                return true;
            }

            public static bool equivalent(TypeSpec a, TypeSpec b)
            {
                // The two complex types are equivalent if they are actually identical
                if (a == b)
                {
                    return true;
                }
                // or if they are structs, and the structs are equivalent
                if (a.is_structure() || b.is_structure())
                {
                    return a.is_structure() && b.is_structure() && a.structspec().name() == b.structspec().name()
                           && equivalent(a.structspec(), b.structspec());
                }
                // or if the underlying simple types are equivalent
                return ((a.is_vectriple_based() && b.is_vectriple_based()) || TypeDesc.equivalent(a.simpletype(), b.simpletype()))
                       // ... and either both or neither are closures
                       && a.is_closure() == b.is_closure()
                       // ... and, if arrays, they are the same length, or both unsized, or one is unsized and the other isn't
                       && (a.simpletype().arraylen == b.simpletype().arraylen || a.is_unsized_array() != b.is_unsized_array());
            }

            // Relaxed rules just look to see that the types are isomorphic to each other (ie: same number of base values)
            public static bool relaxed_equivalent(TypeSpec a, TypeSpec b)
            {
                TypeDesc paramtype = a.simpletype();
                TypeDesc valuetype = b.simpletype();

                return valuetype.basetype == paramtype.basetype && !valuetype.is_unsized_array()
                       && ((!paramtype.is_unsized_array() && valuetype.basevalues() == paramtype.basevalues())
                           || (paramtype.is_unsized_array() && valuetype.basevalues() % paramtype.aggregate == 0)
                           || (paramtype.is_vec3() && valuetype == TypeFloat));
            }

            /// Is type src assignable to dst?  It is if they are equivalent(), or if dst is a float or float-aggregate and src is a float or int.
            public static bool assignable(TypeSpec dst, TypeSpec src)
            {
                if (dst.is_closure() || src.is_closure())
                {
                    return dst.is_closure() && src.is_closure();
                }
                return equivalent(dst, src) || (dst.is_float_based() && !dst.is_array() && (src.is_float() || src.is_int()));
            }
        }

        /// Light-weight way to describe types for the compiler -- simple types, closures, or the ID of a structure.
        public struct TypeSpec : IEquatable<TypeSpec>
        {
            private TypeDesc m_simple;  ///< Data if it's a simple type
            private short m_structure;  ///< 0 is not a structure, >=1 for structure id
            private bool m_closure;     ///< Is it a closure? (m_simple also used)

            private static readonly List<StructSpec> m_structs = new List<StructSpec>();

            /// Default ctr of TypeSpec (unknown type)
            public TypeSpec()
            {
                m_simple = new TypeDesc(TypeDesc.UNKNOWN);
                m_structure = 0;
                m_closure = false;
            }

            /// Construct a TypeSpec that represents an ordinary simple type (including arrays of simple types).
            public TypeSpec(TypeDesc simple)
            {
                m_simple = simple;
                m_structure = 0;
                m_closure = false;
            }

            /// Construct a TypeSpec representing a closure (pass closure=true) of a simple type.
            public TypeSpec(TypeDesc simple, bool closure)
            {
                m_simple = closure ? new TypeDesc(TypeDesc.PTR) : simple;
                m_structure = 0;
                m_closure = closure;
            }

            /// Construct a TypeSpec describing a struct or array of structs, by supplying the struct name, structure id, and array length.
            /// If structid == 0, search the existing table for a (globally) matching name and use that struct if it exists, otherwise add an
            /// entry to the struct table.
            public TypeSpec(string name, int structid, int arraylen = 0)
            {
                m_simple = new TypeDesc(TypeDesc.UNKNOWN, arraylen);
                m_structure = (short)structid;
                m_closure = false;
                if (m_structure == 0)
                {
                    m_structure = (short)structure_id(name, true);
                }
            }

            public static implicit operator TypeSpec(TypeDesc simple) { return new TypeSpec(simple); }

            /// Express the type as a string
            public string @string()
            {
                StringBuilder str = new StringBuilder();
                if (is_closure() || is_closure_array())
                {
                    str.Append("closure color");
                    if (is_unsized_array())
                    {
                        str.Append("[]");
                    }
                    else if (arraylength() > 0)
                    {
                        str.Append("[" + arraylength() + "]");
                    }
                }
                else if (structure() > 0)
                {
                    StructSpec ss = structspec();
                    if (ss != null)
                    {
                        str.Append("struct " + structspec().name());
                    }
                    else
                    {
                        str.Append("struct " + structure());
                    }
                    if (is_unsized_array())
                    {
                        str.Append("[]");
                    }
                    else if (arraylength() > 0)
                    {
                        str.Append("[" + arraylength() + "]");
                    }
                }
                else
                {
                    str.Append(simpletype().c_str());
                }
                return str.ToString();
            }

            public string c_str() { return @string(); }

            /// Return the c_str giving a human-readable name of a type, fully accounting for exotic types like structs, etc.
            public string type_c_str()
            {
                if (is_structure())
                {
                    return "struct " + structspec().name();
                }
                return c_str();
            }

            public override string ToString() { return @string(); }

            /// Assignment of a simple TypeDesc to a full TypeSpec.
            public void Assign(TypeDesc simple)
            {
                m_simple = simple;
                m_structure = 0;
                m_closure = false;
            }

            public static bool operator ==(TypeSpec a, TypeSpec b)
            {
                return a.m_simple == b.m_simple && a.m_structure == b.m_structure && a.m_closure == b.m_closure;
            }
            public static bool operator !=(TypeSpec a, TypeSpec b) { return !(a == b); }
            public bool Equals(TypeSpec o) { return this == o; }
            public override bool Equals(object o) { return o is TypeSpec t && this == t; }
            public override int GetHashCode() { return HashCode.Combine(m_simple, m_structure, m_closure); }

            /// Return just the simple type underlying this TypeSpec -- only works reliable if it's not a struct, a struct will return an UNKNOWN type.
            public TypeDesc simpletype() { return m_simple; }

            /// Is the type unknown/uninitialized?
            public bool is_unknown() { return m_simple == TypeUnknown && m_structure == 0 && !m_closure; }

            public bool is_closure() { return m_closure && !is_array(); }
            public bool is_closure_array() { return m_closure && is_array(); }
            public bool is_closure_based() { return m_closure; }
            public bool is_structure() { return m_structure > 0 && !is_array(); }
            public bool is_structure_array() { return m_structure > 0 && is_array(); }
            public bool is_structure_based() { return m_structure > 0; }

            /// Return the structure ID of this typespec, or 0 if it's not a struct.
            public int structure() { return m_structure; }

            /// Return the structspec for this structure.
            public StructSpec structspec() { return structspec(m_structure); }

            /// Find a structure record by id number.
            public static StructSpec structspec(int id) { return id != 0 ? m_structs[id] : null; }

            /// Find a structure index by name, or return 0 if not found. If 'add' is true, add the struct if not already found.
            public static int structure_id(string name, bool add = false)
            {
                ustring n = new ustring(name);
                lock (m_structs)
                {
                    for (int i = m_structs.Count - 1; i > 0; --i)
                    {
                        if (m_structs[i] != null && m_structs[i].name() == n)
                        {
                            return i;
                        }
                    }
                    if (add)
                    {
                        if (m_structs.Count >= 0x8000)
                        {
                            throw new InvalidOperationException("more struct id's than fit in a short!");
                        }
                        return new_struct(new StructSpec(n, 0));
                    }
                }
                return 0;  // Not found, not added
            }

            /// Make room for one new structure and return its index.
            public static int new_struct(StructSpec n)
            {
                lock (m_structs)
                {
                    if (m_structs.Count == 0)
                    {
                        m_structs.Add(null);  // Allocate an empty one
                    }
                    m_structs.Add(n);
                    return m_structs.Count - 1;
                }
            }

            /// Return a reference to the structure list.
            public static List<StructSpec> struct_list() { return m_structs; }

            public bool is_array() { return m_simple.arraylen != 0; }
            public bool is_unsized_array() { return m_simple.arraylen < 0; }
            public bool is_sized_array() { return m_simple.arraylen > 0; }

            /// Returns the length of the array, or 0 if not an array.
            public int arraylength()
            {
                System.Diagnostics.Debug.Assert(m_simple.arraylen >= 0, "Called arraylength() on TypeSpec of array with unspecified length");
                return m_simple.arraylen;
            }

            /// Number of elements
            public int numelements()
            {
                System.Diagnostics.Debug.Assert(m_simple.arraylen >= 0, "Called numelements() on TypeSpec of array with unspecified length");
                return Math.Max(1, m_simple.arraylen);
            }

            /// Alter this typespec to make it into an array of the given length (including 0 -> make it not be an array).
            public void make_array(int len) { m_simple.arraylen = len; }

            /// For an array, return the TypeSpec of an individual element of the array.  For a non-array, just return the type.
            public TypeSpec elementtype()
            {
                TypeSpec t = this;
                t.make_array(0);
                return t;
            }

            public int aggregate() { return m_simple.aggregate; }

            public bool is_int() { return m_simple == TypeInt && !is_closure(); }
            public bool is_float() { return m_simple == TypeFloat && !is_closure(); }
            public bool is_color() { return m_simple == TypeColor && !is_closure(); }
            public bool is_point() { return m_simple == TypePoint && !is_closure(); }
            public bool is_vector() { return m_simple == TypeVector && !is_closure(); }
            public bool is_normal() { return m_simple == TypeNormal && !is_closure(); }
            public bool is_string() { return m_simple == TypeString && !is_closure(); }
            public bool is_string_based() { return m_simple.basetype == TypeDesc.STRING; }
            public bool is_int_based() { return m_simple.basetype == TypeDesc.INT; }
            public bool is_float_based() { return m_simple.basetype == TypeDesc.FLOAT && !m_closure; }
            public bool is_void() { return m_simple.basetype == TypeDesc.NONE && m_simple.aggregate == TypeDesc.SCALAR && m_simple.arraylen == 0; }

            public bool is_triple()
            {
                return !is_closure() && m_simple.aggregate == TypeDesc.VEC3 && m_simple.basetype == TypeDesc.FLOAT && !m_simple.is_array();
            }

            public bool is_triple_based()
            {
                return !is_closure() && m_simple.aggregate == TypeDesc.VEC3 && m_simple.basetype == TypeDesc.FLOAT;
            }

            public bool is_triple_or_float()
            {
                return !is_closure() && (m_simple.aggregate == TypeDesc.VEC3 || m_simple.aggregate == TypeDesc.SCALAR)
                       && m_simple.basetype == TypeDesc.FLOAT && !m_simple.is_array();
            }

            public bool is_numeric()
            {
                return !is_closure() && !is_array() && (m_simple.basetype == TypeDesc.FLOAT || m_simple.basetype == TypeDesc.INT);
            }

            public bool is_scalarnum() { return is_numeric() && m_simple.aggregate == TypeDesc.SCALAR; }
            public bool is_int_or_float() { return is_scalarnum(); }

            public bool is_vectriple()
            {
                return !is_closure() && (m_simple == TypePoint || m_simple == TypeVector || m_simple == TypeNormal);
            }

            public bool is_vectriple_based()
            {
                TypeDesc elem = m_simple.elementtype();
                return elem == TypePoint || elem == TypeVector || elem == TypeNormal;
            }

            public bool is_matrix() { return m_simple == TypeMatrix && !is_closure(); }
            public bool is_color_closure() { return is_closure(); }

            /// Given a pointer to a type code string that we use for argument checking ("p", "v", etc.) return the TypeSpec of the first type
            /// described by the string (UNKNOWN if it couldn't be recognized). 'advance' receives the number of characters taken.
            public static TypeSpec type_from_code(string code, int start, out int advance)
            {
                TypeSpec t = new TypeSpec();
                t.m_simple = TypeUnknown;
                int i = start;
                switch (code[i])
                {
                    case 'i': t = TypeInt; break;
                    case 'f': t = TypeFloat; break;
                    case 'c': t = TypeColor; break;
                    case 'p': t = TypePoint; break;
                    case 'v': t = TypeVector; break;
                    case 'n': t = TypeNormal; break;
                    case 'm': t = TypeMatrix; break;
                    case 's': t = TypeString; break;
                    case 'h': t = TypeUInt64; break;  // ustringhash_pod
                    case 'x': t = new TypeDesc(TypeDesc.NONE); break;
                    case 'X': t = new TypeDesc(TypeDesc.PTR); break;
                    case 'L': t = new TypeDesc(TypeDesc.LONGLONG); break;
                    case 'C':  // color closure
                        t = new TypeSpec(TypeColor, true);
                        break;
                    case 'S':  // structure
                        {
                            // Following the 'S' is the numeric structure ID
                            int j = i + 1, id = 0;
                            while (j < code.Length && char.IsAsciiDigit(code[j]))
                            {
                                id = id * 10 + (code[j] - '0');
                                ++j;
                            }
                            t = new TypeSpec("struct", id);
                            // Skip to the last digit
                            while (i + 1 < code.Length && char.IsAsciiDigit(code[i + 1]))
                            {
                                ++i;
                            }
                        }
                        break;
                    case '?': break;  // anything will match, so keep 'UNKNOWN'
                    case '*': break;
                    case '.': break;
                    default:
                        advance = 1;
                        return new TypeSpec();
                }
                ++i;

                if (i < code.Length && code[i] == '[')
                {
                    ++i;
                    t.make_array(-1);  // signal arrayness, unknown length
                    if (i < code.Length && (char.IsAsciiDigit(code[i]) || code[i] == ']'))
                    {
                        if (char.IsAsciiDigit(code[i]))
                        {
                            int n = 0;
                            for (int j = i; j < code.Length && char.IsAsciiDigit(code[j]); ++j)
                            {
                                n = n * 10 + (code[j] - '0');
                            }
                            t.make_array(n);
                        }
                        while (i < code.Length && char.IsAsciiDigit(code[i]))
                        {
                            ++i;
                        }
                        if (i < code.Length && code[i] == ']')
                        {
                            ++i;
                        }
                    }
                }

                advance = i - start;
                return t;
            }

            public static TypeSpec type_from_code(string code)
            {
                int advance;
                return type_from_code(code, 0, out advance);
            }

            /// Return the argument checking code ("p", "v", etc.) corresponding to the type.
            public string code_from_type()
            {
                string outs;
                TypeDesc elem = elementtype().simpletype();
                if (is_structure() || is_structure_array())
                {
                    outs = "S" + structure();
                }
                else if (is_closure() || is_closure_array())
                {
                    outs = "C";
                }
                else
                {
                    if (elem == TypeInt) outs = "i";
                    else if (elem == TypeFloat) outs = "f";
                    else if (elem == TypeColor) outs = "c";
                    else if (elem == TypePoint) outs = "p";
                    else if (elem == TypeVector) outs = "v";
                    else if (elem == TypeNormal) outs = "n";
                    else if (elem == TypeMatrix) outs = "m";
                    else if (elem == TypeString) outs = "s";
                    else if (elem.basetype == TypeDesc.NONE) outs = "x";
                    else outs = "x";  // only in error circumstances: the code for 'void'
                }

                if (is_array())
                {
                    outs += is_unsized_array() ? "[]" : "[" + arraylength() + "]";
                }
                return outs;
            }

            /// Return the type code used to build mangled osl_* runtime function names. All triple subtypes (color, point, vector, normal)
            /// collapse to "v"; with derivs, float and triple types are prefixed with "d".
            public string runtime_typecode(bool derivs = false)
            {
                string code = code_from_type();
                if (code == "c" || code == "p" || code == "n")
                {
                    code = "v";
                }
                if (derivs && (code == "f" || code == "v"))
                {
                    code = "d" + code;
                }
                return code;
            }

            /// Take a type code string (possibly containing many types) and turn it into a human-readable string.
            public static string typelist_from_code(string code)
            {
                StringBuilder ret = new StringBuilder();
                int p = 0;
                while (p < code.Length)
                {
                    int advance = 1;
                    if (ret.Length > 0)
                    {
                        ret.Append(", ");
                    }
                    if (code[p] == '.')
                    {
                        ret.Append("...");
                    }
                    else if (code[p] == 'T')
                    {
                        ret.Append("...");
                    }
                    else if (code[p] == '?')
                    {
                        ret.Append("<any>");
                    }
                    else
                    {
                        TypeSpec t = type_from_code(code, p, out advance);
                        ret.Append(t.type_c_str());
                    }
                    p += advance;
                    if (p < code.Length && code[p] == '[')
                    {
                        ret.Append("[]");
                        ++p;
                        while (p < code.Length && char.IsAsciiDigit(code[p]))
                        {
                            ++p;
                        }
                        if (p < code.Length && code[p] == ']')
                        {
                            ++p;
                        }
                    }
                }
                return ret.ToString();
            }

            /// Take a type code string (possibly containing many types) and turn it into a TypeSpec list.
            public static void typespecs_from_codes(string code, List<TypeSpec> types)
            {
                types.Clear();
                int p = 0;
                while (code != null && p < code.Length)
                {
                    int advance;
                    types.Add(type_from_code(code, p, out advance));
                    p += advance;
                }
            }
        }

        /// Describe the layout of an OSL 'struct'. Basically it's just a list of all the individual fields' names and types.
        public class StructSpec
        {
            /// Description of a single structure field -- just a type and name.
            public struct FieldSpec
            {
                public TypeSpec type;
                public ustring name;

                public FieldSpec(TypeSpec t, ustring n)
                {
                    type = t;
                    name = n;
                }
            }

            private ustring m_name;                   ///< Structure name (unmangled)
            private int m_scope;                      ///< Structure's scope id
            private List<FieldSpec> m_fields = new List<FieldSpec>();  ///< List of fields of the struct

            /// Construct a new struct with the given name, in the given scope.
            public StructSpec(ustring name, int scope)
            {
                m_name = name;
                m_scope = scope;
            }

            /// Append a new field (with type and name) to this struct.
            public void add_field(TypeSpec type, ustring name) { m_fields.Add(new FieldSpec(type, name)); }

            /// The name of this struct (may not be unique across all scopes).
            public ustring name() { return m_name; }

            /// The unique mangled name (with scope embedded) of this struct.
            public string mangled() { return scope() != 0 ? "___" + scope() + "_" + m_name : m_name.@string(); }

            /// The scope number where this struct was defined.
            public int scope() { return m_scope; }

            /// Number of fields in the struct.
            public int numfields() { return m_fields.Count; }

            /// Return an individual FieldSpec for one field of the struct, indexed numerically (starting with 0).
            public FieldSpec field(int i) { return m_fields[i]; }

            /// Look up the named field, return its index, or -1 if not found.
            public int lookup_field(ustring name)
            {
                for (int i = 0, e = numfields(); i < e; ++i)
                {
                    if (field(i).name == name)
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        /// The compiler (or runtime) record of a single symbol (identifier) and all relevant information about it.
        public class Symbol
        {
            /// Where did the symbol's value come from?
            public enum ValueSource { DefaultVal, InstanceVal, GeomVal, ConnectedVal }

            // Special offset meaning that the offset is unknown/uninitialized.
            public const int unknown_offset = -1;

            protected object m_data = null;  ///< The data of a constant or default: int[], float[] or ustring[]
            protected ustring m_name;        ///< Symbol name (unmangled)
            protected TypeSpec m_typespec;   ///< Data type of the symbol
            protected int m_size;            ///< Size of data (in bytes, without derivs)
            protected SymType m_symtype;     ///< Kind of symbol (param, local, etc.)
            protected bool m_has_derivs;
            protected bool m_const_initializer;
            protected bool m_connected_down;
            protected bool m_initialized;
            protected bool m_interpolated;
            protected bool m_interactive;
            protected bool m_noninteractive;
            protected bool m_allowconnect;
            protected bool m_renderer_output;
            protected bool m_readonly;
            protected bool m_is_uniform;
            protected bool m_forced_llvm_bool;  // kept for fidelity; the IL backend reads it the same way
            protected SymArena m_arena;
            protected ValueSource m_valuesource;
            protected short m_fieldid;
            protected short m_layer;
            protected int m_scope;
            protected int m_dataoffset;
            protected int m_wide_dataoffset;
            protected int m_initializers;
            protected ASTNode m_node = null;
            protected Symbol m_alias = null;
            protected int m_initbegin, m_initend;
            protected int m_firstread, m_lastread;
            protected int m_firstwrite, m_lastwrite;

            public Symbol(ustring name, TypeSpec datatype, SymType symtype, ASTNode declaration_node = null)
            {
                m_name = name;
                m_typespec = datatype;
                m_size = datatype.is_unsized_array() ? 0 : (int)datatype.simpletype().size();
                m_symtype = symtype;
                m_allowconnect = true;
                m_is_uniform = true;
                m_arena = SymArena.Unknown;
                m_valuesource = ValueSource.DefaultVal;
                m_fieldid = -1;
                m_layer = -1;
                m_scope = 0;
                m_dataoffset = unknown_offset;
                m_wide_dataoffset = unknown_offset;
                m_initializers = 0;
                m_node = declaration_node;
                m_initbegin = 0;
                m_initend = 0;
                m_firstread = int.MaxValue;
                m_lastread = -1;
                m_firstwrite = int.MaxValue;
                m_lastwrite = -1;
            }

            /// Copy another symbol's state into this one (the C++ operator=, a memcpy)
            public void Assign(Symbol a)
            {
                if (this != a)
                {
                    m_data = a.m_data; m_name = a.m_name; m_typespec = a.m_typespec; m_size = a.m_size; m_symtype = a.m_symtype;
                    m_has_derivs = a.m_has_derivs; m_const_initializer = a.m_const_initializer; m_connected_down = a.m_connected_down;
                    m_initialized = a.m_initialized; m_interpolated = a.m_interpolated; m_interactive = a.m_interactive;
                    m_noninteractive = a.m_noninteractive; m_allowconnect = a.m_allowconnect; m_renderer_output = a.m_renderer_output;
                    m_readonly = a.m_readonly; m_is_uniform = a.m_is_uniform; m_forced_llvm_bool = a.m_forced_llvm_bool;
                    m_arena = a.m_arena; m_valuesource = a.m_valuesource; m_fieldid = a.m_fieldid; m_layer = a.m_layer;
                    m_scope = a.m_scope; m_dataoffset = a.m_dataoffset; m_wide_dataoffset = a.m_wide_dataoffset;
                    m_initializers = a.m_initializers; m_node = a.m_node; m_alias = a.m_alias; m_initbegin = a.m_initbegin;
                    m_initend = a.m_initend; m_firstread = a.m_firstread; m_lastread = a.m_lastread; m_firstwrite = a.m_firstwrite;
                    m_lastwrite = a.m_lastwrite;
                }
            }

            /// The symbol's (unmangled) name, guaranteed unique only within the symbol's declaration scope.
            public ustring name() { return m_name; }

            /// The symbol's name, mangled to incorporate the scope so it will be a globally unique name.
            public string mangled() { return scope() != 0 ? "___" + scope() + "_" + m_name : m_name.@string(); }

            /// Return an unmangled version of the symbol name: removes a "___<scope>_" prefix.
            public string unmangled()
            {
                string result = m_name.@string();
                if (result.StartsWith("___", StringComparison.Ordinal))
                {
                    int i = 3;
                    while (i < result.Length && char.IsAsciiDigit(result[i]))
                    {
                        ++i;
                    }
                    if (i < result.Length && result[i] == '_')
                    {
                        ++i;
                    }
                    result = result.Substring(i);
                }
                return result;
            }

            /// The runtime type code string for this symbol's type, suitable for building mangled osl_* function names.
            public string arg_typecode(bool derivs = false) { return typespec().runtime_typecode(derivs); }

            private static readonly HashSet<string> cpp_reserved = new HashSet<string>
            {
                "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char", "char8_t",
                "char16_t", "char32_t", "class", "compl", "concept", "const", "consteval", "constexpr", "constinit", "const_cast", "continue",
                "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double", "dynamic_cast", "else", "enum",
                "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if", "inline", "int", "long", "mutable",
                "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or", "or_eq", "private", "protected", "public",
                "register", "reinterpret_cast", "requires", "return", "short", "signed", "sizeof", "static", "static_assert", "static_cast",
                "struct", "switch", "template", "this", "thread_local", "throw", "true", "try", "typedef", "typeid", "typename", "union",
                "unsigned", "using", "virtual", "void", "volatile", "wchar_t", "while", "xor", "xor_eq"
            };

            /// The mangled name of the symbol, but only using symbols that are safe to use in C++ code.
            public string cpp_safe_name()
            {
                string result;
                if (m_name.size() > 0 && m_name[0] == '$')
                {
                    result = "___" + m_name.@string().Substring(1);
                }
                else
                {
                    result = m_name.@string();
                }
                result = result.Replace(".", "__");
                if (cpp_reserved.Contains(result))
                {
                    result += "_osl";
                }
                return result;
            }

            public TypeSpec typespec() { return m_typespec; }
            public SymType symtype() { return m_symtype; }
            public void symtype(SymType newsymtype) { m_symtype = newsymtype; }
            public int scope() { return m_scope; }
            public void scope(int s) { m_scope = s; }
            public ASTNode node() { return m_node; }
            public bool is_function() { return m_symtype == SymType.SymTypeFunction; }
            public bool is_structure() { return m_symtype == SymType.SymTypeType; }

            /// Return the symbol that this really refers to, tracing aliases back until it finds a symbol that isn't an alias.
            public Symbol dealias()
            {
                Symbol s = this;
                while (s.m_alias != null)
                {
                    s = s.m_alias;
                }
                return s;
            }

            /// Establish that this symbol is really an alias for another symbol.
            public void alias(Symbol other)
            {
                System.Diagnostics.Debug.Assert(other != this);  // circular alias would be bad
                m_alias = other;
            }

            public static string symtype_shortname(SymType s)
            {
                string[] names = { "param", "oparam", "local", "temp", "global", "const", "func", "typename" };
                return names[(int)s];
            }

            public string symtype_shortname() { return symtype_shortname(symtype()); }

            /// The symbol's data (the managed array of a constant or default value), or null.
            public object data() { return m_data; }
            public object dataptr() { return m_data; }

            public void set_dataptr(SymArena arena, object ptr)
            {
                m_arena = arena;
                m_data = ptr;
            }

            public void set_dataptr(SymArena arena, object ptr, int offset)
            {
                m_arena = arena;
                m_data = ptr;
                m_dataoffset = offset;
            }

            public void dataoffset(int d) { m_dataoffset = d; }
            public int dataoffset() { return m_dataoffset; }
            public void wide_dataoffset(int d) { m_wide_dataoffset = d; }
            public int wide_dataoffset() { return m_wide_dataoffset; }
            public SymArena arena() { return m_arena; }
            public void initializers(int d) { m_initializers = d; }
            public int initializers() { return m_initializers; }
            public bool has_derivs() { return m_has_derivs; }
            public void has_derivs(bool new_derivs) { m_has_derivs = new_derivs; }
            public int size() { return m_size; }
            public void size(long newsize) { m_size = (int)newsize; }

            /// Return the size for each point, including derivs.
            public int derivsize() { return m_has_derivs ? 3 * m_size : m_size; }

            public bool connected() { return valuesource() == ValueSource.ConnectedVal; }
            public bool connected_down() { return m_connected_down; }
            public void connected_down(bool c) { m_connected_down = c; }

            public ValueSource valuesource() { return m_valuesource; }
            public void valuesource(ValueSource v) { m_valuesource = v; }
            public string valuesourcename() { return valuesourcename(valuesource()); }

            public static string valuesourcename(ValueSource v)
            {
                switch (v)
                {
                    case ValueSource.DefaultVal: return "default";
                    case ValueSource.InstanceVal: return "instance";
                    case ValueSource.GeomVal: return "geom";
                    case ValueSource.ConnectedVal: return "connected";
                }
                return null;
            }

            public int fieldid() { return m_fieldid; }
            public void fieldid(int id) { m_fieldid = (short)id; }
            public int layer() { return m_layer; }
            public void layer(int id) { m_layer = (short)id; }
            public int initbegin() { return m_initbegin; }
            public void initbegin(int i) { m_initbegin = i; }
            public int initend() { return m_initend; }
            public void initend(int i) { m_initend = i; }

            public void set_initrange(int b = 0, int e = 0)
            {
                m_initbegin = b;
                m_initend = e;
            }

            public bool has_init_ops() { return m_initbegin != m_initend; }

            /// Clear read/write usage info.
            public void clear_rw()
            {
                m_firstread = m_firstwrite = int.MaxValue;
                m_lastread = m_lastwrite = -1;
            }

            /// Mark whether the symbol was read and/or written on the given op.
            public void mark_rw(int op, bool read, bool write)
            {
                if (read)
                {
                    m_firstread = Math.Min(m_firstread, op);
                    m_lastread = Math.Max(m_lastread, op);
                }
                if (write)
                {
                    m_firstwrite = Math.Min(m_firstwrite, op);
                    m_lastwrite = Math.Max(m_lastwrite, op);
                }
            }

            public void union_rw(int fr, int lr, int fw, int lw)
            {
                m_firstread = Math.Min(m_firstread, fr);
                m_lastread = Math.Max(m_lastread, lr);
                m_firstwrite = Math.Min(m_firstwrite, fw);
                m_lastwrite = Math.Max(m_lastwrite, lw);
            }

            // Mark the symbol as always being read (and, if write==true, also that it's always written).
            public void mark_always_used(bool write = false)
            {
                m_firstread = 0;
                m_lastread = int.MaxValue;
                if (write)
                {
                    m_firstwrite = 0;
                    m_lastwrite = int.MaxValue;
                }
            }

            public int firstread() { return m_firstread; }
            public int lastread() { return m_lastread; }
            public int firstwrite() { return m_firstwrite; }
            public int lastwrite() { return m_lastwrite; }
            public int firstuse() { return Math.Min(firstread(), firstwrite()); }
            public int lastuse() { return Math.Max(lastread(), lastwrite()); }
            public bool everread() { return lastread() >= 0; }
            public bool everwritten() { return lastwrite() >= 0; }
            public bool everused() { return everread() || everwritten(); }
            public bool everused_in_group() { return everused() || connected_down() || renderer_output(); }

            public void set_read(int first, int last)
            {
                m_firstread = first;
                m_lastread = last;
            }

            public void set_write(int first, int last)
            {
                m_firstwrite = first;
                m_lastwrite = last;
            }

            public bool initialized() { return m_initialized; }
            public void initialized(bool init) { m_initialized = init; }

            public bool lockgeom() { return !m_interpolated && !m_interactive; }
            public bool interpolated() { return m_interpolated; }
            public void interpolated(bool val) { m_interpolated = val; }
            public bool interactive() { return m_interactive; }
            public void interactive(bool val) { m_interactive = val; }
            public bool noninteractive() { return m_noninteractive; }
            public void noninteractive(bool val) { m_noninteractive = val; }
            public bool allowconnect() { return m_allowconnect; }
            public void allowconnect(bool val) { m_allowconnect = val; }
            public bool const_initializer() { return m_const_initializer; }
            public void const_initializer(bool val) { m_const_initializer = val; }

            public int arraylen() { return m_typespec.arraylength(); }

            public void arraylen(int len)
            {
                m_typespec.make_array(len);
                m_size = (int)m_typespec.simpletype().size();
            }

            public bool renderer_output() { return m_renderer_output; }
            public void renderer_output(bool v) { m_renderer_output = v; }
            public bool is_uniform() { return m_is_uniform; }
            public bool is_varying() { return !m_is_uniform; }
            public void make_varying() { m_is_uniform = false; }
            public bool forced_llvm_bool() { return m_forced_llvm_bool; }
            public void forced_llvm_bool(bool v) { m_forced_llvm_bool = v; }
            public bool readonly_() { return m_readonly; }
            public void readonly_(bool v) { m_readonly = v; }
            public bool is_constant() { return symtype() == SymType.SymTypeConst; }
            public bool is_temp() { return symtype() == SymType.SymTypeTemp; }

            // Retrieve the const float value (must be a const float!)
            public float get_float(int index = 0) { return ((float[])m_data)[index]; }

            // Retrieve a const float value (coerce from int if necessary)
            public float coerce_float(int index = 0)
            {
                return typespec().is_int_based() ? (float)get_int(index) : get_float(index);
            }

            // Retrieve the const int value (must be a const int!)
            public int get_int(int index = 0) { return ((int[])m_data)[index]; }

            // Retrieve the const string value (must be a const string!)
            public ustring get_string(int index = 0) { return ((ustring[])m_data)[index]; }

            // Retrieve the const vec3 value (must be a const triple!): the data array holds three floats per triple
            public Vec3 get_vec3(int index = 0)
            {
                float[] f = (float[])m_data;
                return new Vec3(f[3 * index], f[3 * index + 1], f[3 * index + 2]);
            }

            // Retrieve the const vec3 value (coerce from float if necessary)
            public Vec3 coerce_vec3()
            {
                if (typespec().is_triple())
                {
                    return get_vec3(0);
                }
                float f = coerce_float();
                return new Vec3(f, f, f);
            }

            /// Print the constant values; strings are shown in the "escaped" C notation.
            public string print_vals(int maxvals = 100000000)
            {
                StringBuilder o = new StringBuilder();
                if (data() == null)
                {
                    return "";
                }
                TypeDesc t = typespec().simpletype();
                int n = Math.Min(t.aggregate * t.numelements(), maxvals);
                if (t.basetype == TypeDesc.FLOAT)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        o.Append(j != 0 ? " " : "").Append(get_float(j).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
                else if (t.basetype == TypeDesc.INT)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        o.Append(j != 0 ? " " : "").Append(get_int(j));
                    }
                }
                else if (t.basetype == TypeDesc.STRING)
                {
                    for (int j = 0; j < n; ++j)
                    {
                        o.Append(j != 0 ? " " : "").Append('"').Append(Strutil.escape_chars(get_string(j).@string())).Append('"');
                    }
                }
                if (t.aggregate * t.numelements() > maxvals)
                {
                    o.Append("...");
                }
                return o.ToString();
            }

            public string print(int maxvals = 100000000)
            {
                StringBuilder o = new StringBuilder();
                o.Append(symtype_shortname(symtype())).Append(' ').Append(typespec().@string()).Append(' ').Append(name().@string());
                if (everused())
                {
                    o.Append(" (used " + firstuse() + ' ' + lastuse() + " read " + firstread() + ' ' + lastread() + " write " + firstwrite()
                             + ' ' + lastwrite());
                }
                else
                {
                    o.Append(" (unused");
                }
                o.Append(has_derivs() ? " derivs" : "").Append(')');
                if (symtype() == SymType.SymTypeParam || symtype() == SymType.SymTypeOutputParam)
                {
                    if (has_init_ops())
                    {
                        o.Append(" init [" + initbegin() + ',' + initend() + ")");
                    }
                    if (connected())
                    {
                        o.Append(" connected");
                    }
                    if (connected_down())
                    {
                        o.Append(" down-connected");
                    }
                    if (!connected() && !connected_down())
                    {
                        o.Append(" unconnected");
                    }
                    if (renderer_output())
                    {
                        o.Append(" renderer-output");
                    }
                    if (symtype() == SymType.SymTypeParam && !lockgeom())
                    {
                        o.Append(" lockgeom=0");
                    }
                    if (symtype() == SymType.SymTypeParam && interactive())
                    {
                        o.Append(" interactive=1");
                    }
                    if (symtype() == SymType.SymTypeParam && noninteractive())
                    {
                        o.Append(" interactive=0");
                    }
                }
                o.Append('\n');
                if (symtype() == SymType.SymTypeConst)
                {
                    o.Append("\tconst: ").Append(print_vals(maxvals)).Append('\n');
                }
                else if (symtype() == SymType.SymTypeParam || symtype() == SymType.SymTypeOutputParam)
                {
                    if (valuesource() == ValueSource.DefaultVal && !has_init_ops())
                    {
                        o.Append("\tdefault: ").Append(print_vals(maxvals)).Append('\n');
                    }
                    else if (valuesource() == ValueSource.InstanceVal)
                    {
                        o.Append("\tvalue: ").Append(print_vals(maxvals)).Append('\n');
                    }
                }
                return o.ToString();
            }
        }

        /// Intermediate Representation opcode
        public class Opcode
        {
            /// Maximum jump targets an op can have.
            public const int max_jumps = 4;

            private ustring m_op;                   ///< Name of opcode
            private int m_firstarg;                 ///< Index of first argument
            private int m_nargs;                    ///< Total number of arguments
            private ustring m_method;               ///< Which param or method this code is for
            private int[] m_jump = new int[max_jumps];  ///< Jump addresses (-1 means none)
            private ustring m_sourcefile;           ///< Source filename for this op
            private int m_sourceline;               ///< Line of source code for this op
            private uint m_argread;                 ///< Bit field - which args are read
            private uint m_argwrite;                ///< Bit field - which args are written
            private uint m_argtakesderivs;          ///< Bit field - which args take derivs
            private bool m_requires_masking;
            private bool m_analysis_flag;

            public Opcode(ustring op, ustring method, int firstarg = 0, int nargs = 0)
            {
                m_firstarg = firstarg;
                m_method = method;
                m_sourceline = 0;
                reset(op, nargs);  // does most of the heavy lifting
            }

            public Opcode(Opcode o)
            {
                m_op = o.m_op; m_firstarg = o.m_firstarg; m_nargs = o.m_nargs; m_method = o.m_method;
                Array.Copy(o.m_jump, m_jump, max_jumps);
                m_sourcefile = o.m_sourcefile; m_sourceline = o.m_sourceline; m_argread = o.m_argread; m_argwrite = o.m_argwrite;
                m_argtakesderivs = o.m_argtakesderivs; m_requires_masking = o.m_requires_masking; m_analysis_flag = o.m_analysis_flag;
            }

            public void reset(ustring opname, int nargs)
            {
                m_op = opname;
                m_nargs = nargs;
                set_jump();
                m_argread = ~1u;  // Default - all args are read except the first
                m_argwrite = 1;   // Default - first arg only is written by the op
                m_argtakesderivs = 0;
                m_requires_masking = false;
                m_analysis_flag = false;
            }

            public ustring opname() { return m_op; }
            public int firstarg() { return m_firstarg; }
            public int nargs() { return m_nargs; }
            public ustring method() { return m_method; }
            public void method(ustring method) { m_method = method; }

            public void source(ustring sourcefile, int sourceline)
            {
                m_sourcefile = sourcefile;
                m_sourceline = sourceline;
            }

            public ustring sourcefile() { return m_sourcefile; }
            public int sourceline() { return m_sourceline; }

            public void set_args(int firstarg, int nargs)
            {
                m_firstarg = firstarg;
                m_nargs = nargs;
            }

            /// Set the jump addresses (-1 means no jump)
            public void set_jump(int jump0 = -1, int jump1 = -1, int jump2 = -1, int jump3 = -1)
            {
                m_jump[0] = jump0;
                m_jump[1] = jump1;
                m_jump[2] = jump2;
                m_jump[3] = jump3;
            }

            public void add_jump(int target)
            {
                for (int i = 0; i < max_jumps; ++i)
                {
                    if (m_jump[i] < 0)
                    {
                        m_jump[i] = target;
                        return;
                    }
                }
            }

            /// Return the i'th jump target address (-1 for none).
            public int jump(int i) { return m_jump[i]; }
            public void jump(int i, int value) { m_jump[i] = value; }

            /// What's the farthest address that we jump to?
            public int farthest_jump()
            {
                int f = jump(0);
                for (int i = 1; i < max_jumps; ++i)
                {
                    f = Math.Max(f, jump(i));
                }
                return f;
            }

            public bool argread(int arg) { return arg < 32 ? (m_argread & (1u << arg)) != 0 : true; }
            public bool argwrite(int arg) { return arg < 32 ? (m_argwrite & (1u << arg)) != 0 : false; }

            public void argread(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argread |= (1u << arg);
                    }
                    else
                    {
                        m_argread &= ~(1u << arg);
                    }
                }
            }

            public void argwrite(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argwrite |= (1u << arg);
                    }
                    else
                    {
                        m_argwrite &= ~(1u << arg);
                    }
                }
            }

            public void argwriteonly(int arg)
            {
                argread(arg, false);
                argwrite(arg, true);
            }

            public void argreadonly(int arg)
            {
                argread(arg, true);
                argwrite(arg, false);
            }

            public bool argtakesderivs(int arg) { return arg < 32 ? (m_argtakesderivs & (1u << arg)) != 0 : false; }

            public void argtakesderivs(int arg, bool val)
            {
                if (arg < 32)
                {
                    if (val)
                    {
                        m_argtakesderivs |= (1u << arg);
                    }
                    else
                    {
                        m_argtakesderivs &= ~(1u << arg);
                    }
                }
            }

            public void set_argbits(uint read, uint wr, uint deriv)
            {
                m_argread = read;
                m_argwrite = wr;
                m_argtakesderivs = deriv;
            }

            public uint argread_bits() { return m_argread; }
            public uint argwrite_bits() { return m_argwrite; }
            public uint argtakesderivs_all() { return m_argtakesderivs; }
            public void argtakesderivs_all(uint newval) { m_argtakesderivs = newval; }

            /// Are two opcodes identical enough to merge their instances? Not a true 'equal'.
            public static bool equivalent(Opcode a, Opcode b)
            {
                if (!(a.m_op == b.m_op && a.m_firstarg == b.m_firstarg && a.m_nargs == b.m_nargs))
                {
                    return false;
                }
                for (int i = 0; i < max_jumps; ++i)
                {
                    if (a.m_jump[i] != b.m_jump[i])
                    {
                        return false;
                    }
                }
                return true;
            }

            /// Runtime optimizer may have case to transmute an op to a different form. Only opname is changed.
            public void transmute_opname(ustring opname) { m_op = opname; }

            public bool requires_masking() { return m_requires_masking; }
            public void requires_masking(bool v) { m_requires_masking = v; }
            public bool analysis_flag() { return m_analysis_flag; }
            public void analysis_flag(bool v) { m_analysis_flag = v; }
        }
    }
}
