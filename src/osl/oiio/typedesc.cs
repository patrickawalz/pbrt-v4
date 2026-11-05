// OSL port: the parts of OpenImageIO that OSL builds on: typedesc.h (TypeDesc), ustring.h (ustring)
//
// OIIO is a separate library in the C++; here it is the smallest port that the compiler and the runtime need, kept in its own namespace.

using System;
using System.Text;

namespace OIIO
{
    public enum BASETYPE : byte
    {
        UNKNOWN = 0, NONE, UCHAR, CHAR, USHORT, SHORT, UINT, INT, ULONGLONG, LONGLONG, HALF, FLOAT, DOUBLE, STRING, PTR, USTRINGHASH,
        LASTBASE
    }

    public enum AGGREGATE : byte
    {
        SCALAR = 1, VEC2 = 2, VEC3 = 3, VEC4 = 4, MATRIX33 = 9, MATRIX44 = 16
    }

    public enum VECSEMANTICS : byte
    {
        NOSEMANTICS = 0, COLOR, POINT, VECTOR, NORMAL, TIMECODE, KEYCODE, RATIONAL, BOX
    }

    public struct TypeDesc : IEquatable<TypeDesc>
    {
        public byte basetype;
        public byte aggregate;
        public byte vecsemantics;
        public byte reserved;
        public int arraylen;

        // The BASETYPE constants are exposed as ints, as the C++ enumerators convert implicitly: TypeDesc.FLOAT, TypeDesc.VEC3 ...
        public const int UNKNOWN = (int)BASETYPE.UNKNOWN, NONE = (int)BASETYPE.NONE, UCHAR = (int)BASETYPE.UCHAR, UINT8 = UCHAR,
                         CHAR = (int)BASETYPE.CHAR, INT8 = CHAR, USHORT = (int)BASETYPE.USHORT, UINT16 = USHORT, SHORT = (int)BASETYPE.SHORT,
                         INT16 = SHORT, UINT = (int)BASETYPE.UINT, UINT32 = UINT, INT = (int)BASETYPE.INT, INT32 = INT,
                         ULONGLONG = (int)BASETYPE.ULONGLONG, UINT64 = ULONGLONG, LONGLONG = (int)BASETYPE.LONGLONG, INT64 = LONGLONG,
                         HALF = (int)BASETYPE.HALF, FLOAT = (int)BASETYPE.FLOAT, DOUBLE = (int)BASETYPE.DOUBLE,
                         STRING = (int)BASETYPE.STRING, PTR = (int)BASETYPE.PTR, USTRINGHASH = (int)BASETYPE.USTRINGHASH;
        public const int SCALAR = (int)AGGREGATE.SCALAR, VEC2 = (int)AGGREGATE.VEC2, VEC3 = (int)AGGREGATE.VEC3, VEC4 = (int)AGGREGATE.VEC4,
                         MATRIX33 = (int)AGGREGATE.MATRIX33, MATRIX44 = (int)AGGREGATE.MATRIX44;
        public const int NOSEMANTICS = 0, COLOR = 1, POINT = 2, VECTOR = 3, NORMAL = 4;

        // OIIO's default TypeDesc is an UNKNOWN scalar
        public TypeDesc()
        {
            basetype = (byte)UNKNOWN;
            aggregate = (byte)SCALAR;
            vecsemantics = (byte)NOSEMANTICS;
            reserved = 0;
            arraylen = 0;
        }

        public TypeDesc(int btype)
        {
            basetype = (byte)btype;
            aggregate = (byte)SCALAR;
            vecsemantics = (byte)NOSEMANTICS;
            reserved = 0;
            arraylen = 0;
        }

        public TypeDesc(int btype, int arraylength)
        {
            basetype = (byte)btype;
            aggregate = (byte)SCALAR;
            vecsemantics = (byte)NOSEMANTICS;
            reserved = 0;
            arraylen = arraylength;
        }

        public TypeDesc(int btype, int agg, int semantics, int arraylength = 0)
        {
            basetype = (byte)btype;
            aggregate = (byte)agg;
            vecsemantics = (byte)semantics;
            reserved = 0;
            arraylen = arraylength;
        }

        public bool is_array() { return arraylen != 0; }
        public bool is_unsized_array() { return arraylen < 0; }
        public bool is_sized_array() { return arraylen > 0; }
        public int numelements() { return (arraylen >= 1 ? arraylen : 1); }
        public int basevalues() { return numelements() * aggregate; }

        public int basesize()
        {
            switch ((BASETYPE)basetype)
            {
                case BASETYPE.UNKNOWN: return 0;
                case BASETYPE.NONE: return 0;
                case BASETYPE.UCHAR: case BASETYPE.CHAR: return 1;
                case BASETYPE.USHORT: case BASETYPE.SHORT: case BASETYPE.HALF: return 2;
                case BASETYPE.UINT: case BASETYPE.INT: case BASETYPE.FLOAT: return 4;
                case BASETYPE.ULONGLONG: case BASETYPE.LONGLONG: case BASETYPE.DOUBLE: return 8;
                case BASETYPE.STRING: case BASETYPE.PTR: case BASETYPE.USTRINGHASH: return 8;
                default: return 0;
            }
        }

        public int elementsize() { return aggregate * basesize(); }
        public long size() { return (long)numelements() * elementsize(); }

        public TypeDesc elementtype()
        {
            TypeDesc t = this;
            t.arraylen = 0;
            return t;
        }

        public bool is_vec3() { return aggregate == VEC3 && basetype == FLOAT && arraylen == 0; }

        public static bool operator ==(TypeDesc a, TypeDesc b)
        {
            return a.basetype == b.basetype && a.aggregate == b.aggregate && a.vecsemantics == b.vecsemantics && a.arraylen == b.arraylen;
        }
        public static bool operator !=(TypeDesc a, TypeDesc b) { return !(a == b); }
        public bool Equals(TypeDesc o) { return this == o; }
        public override bool Equals(object o) { return o is TypeDesc t && this == t; }
        public override int GetHashCode() { return HashCode.Combine(basetype, aggregate, vecsemantics, arraylen); }

        // The same underlying type; vector semantics (color vs point ...) are not compared, and an unsized array matches a sized one, as in OIIO
        public static bool equivalent(TypeDesc a, TypeDesc b)
        {
            return a.basetype == b.basetype && a.aggregate == b.aggregate
                   && (a.arraylen == b.arraylen || (a.is_unsized_array() && b.is_sized_array()) || (a.is_sized_array() && b.is_unsized_array()));
        }

        // The OIIO spelling of a type: "float", "color", "float[3]", "matrix", "string"...
        public string c_str()
        {
            string name;
            if (aggregate == SCALAR)
            {
                name = BaseName();
            }
            else if (aggregate == VEC3 && basetype == FLOAT && vecsemantics != NOSEMANTICS)
            {
                switch (vecsemantics)
                {
                    case COLOR: name = "color"; break;
                    case POINT: name = "point"; break;
                    case VECTOR: name = "vector"; break;
                    case NORMAL: name = "normal"; break;
                    default: name = "vec3"; break;
                }
            }
            else if (aggregate == MATRIX44 && basetype == FLOAT)
            {
                name = "matrix";
            }
            else
            {
                name = BaseName() + (aggregate == VEC2 ? "2" : aggregate == VEC3 ? "3" : aggregate == VEC4 ? "4" : aggregate == MATRIX33 ? "3x3" : "4x4");
                if (aggregate == VEC3 && basetype == FLOAT)
                {
                    name = "vec3";
                }
            }
            if (arraylen > 0)
            {
                return name + "[" + arraylen + "]";
            }
            if (arraylen < 0)
            {
                return name + "[]";
            }
            return name;
        }

        public override string ToString() { return c_str(); }

        private string BaseName()
        {
            switch ((BASETYPE)basetype)
            {
                case BASETYPE.UNKNOWN: return "unknown";
                case BASETYPE.NONE: return "void";
                case BASETYPE.UCHAR: return "uint8";
                case BASETYPE.CHAR: return "int8";
                case BASETYPE.USHORT: return "uint16";
                case BASETYPE.SHORT: return "int16";
                case BASETYPE.UINT: return "uint";
                case BASETYPE.INT: return "int";
                case BASETYPE.ULONGLONG: return "uint64";
                case BASETYPE.LONGLONG: return "int64";
                case BASETYPE.HALF: return "half";
                case BASETYPE.FLOAT: return "float";
                case BASETYPE.DOUBLE: return "double";
                case BASETYPE.STRING: return "string";
                case BASETYPE.PTR: return "pointer";
                case BASETYPE.USTRINGHASH: return "ustringhash";
                default: return "unknown";
            }
        }
    }

    // OIIO's named type constants (TypeInt, TypeFloat, TypeColor ...)
    public static class TypeDescs
    {
        public static readonly TypeDesc TypeUnknown = new TypeDesc(TypeDesc.UNKNOWN);
        public static readonly TypeDesc TypeInt = new TypeDesc(TypeDesc.INT);
        public static readonly TypeDesc TypeFloat = new TypeDesc(TypeDesc.FLOAT);
        public static readonly TypeDesc TypeString = new TypeDesc(TypeDesc.STRING);
        public static readonly TypeDesc TypeColor = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC3, TypeDesc.COLOR);
        public static readonly TypeDesc TypePoint = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC3, TypeDesc.POINT);
        public static readonly TypeDesc TypeVector = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC3, TypeDesc.VECTOR);
        public static readonly TypeDesc TypeNormal = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC3, TypeDesc.NORMAL);
        public static readonly TypeDesc TypeMatrix = new TypeDesc(TypeDesc.FLOAT, TypeDesc.MATRIX44, TypeDesc.NOSEMANTICS);
        public static readonly TypeDesc TypeMatrix44 = TypeMatrix;
        public static readonly TypeDesc TypeUInt64 = new TypeDesc(TypeDesc.UINT64);
        public static readonly TypeDesc TypeVector2 = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC2, TypeDesc.NOSEMANTICS);
        public static readonly TypeDesc TypeFloat4 = new TypeDesc(TypeDesc.FLOAT, TypeDesc.VEC4, TypeDesc.NOSEMANTICS);
    }
}
