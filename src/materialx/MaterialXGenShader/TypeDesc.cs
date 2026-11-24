// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/TypeDesc.h and
// TypeDesc.cpp.

using System;
using System.Collections.Generic;
using static MaterialX.Mx;

namespace MaterialX
{
    // TypeDesc Definition
    // A type descriptor for MaterialX data types.
    //
    // All types need to have a type descriptor registered in order for shader generators
    // to know about the type. It can be used for type comparisons as well as getting more
    // information about the type. Type descriptors for all standard library data types are
    // defined by default and can be accessed from the Type static class, e.g:
    //
    //     Type.FLOAT
    //     Type.COLOR3
    //
    // A value type, compared by the hash of its name, as in the C++.
    public readonly struct TypeDesc : IEquatable<TypeDesc>, IComparable<TypeDesc>
    {
        // TypeDesc Public Types
        public const byte BASETYPE_NONE = 0;
        public const byte BASETYPE_BOOLEAN = 1;
        public const byte BASETYPE_INTEGER = 2;
        public const byte BASETYPE_FLOAT = 3;
        public const byte BASETYPE_STRING = 4;
        public const byte BASETYPE_STRUCT = 5;
        public const byte BASETYPE_LAST = 6;

        public const byte SEMANTIC_NONE = 0;
        public const byte SEMANTIC_COLOR = 1;
        public const byte SEMANTIC_VECTOR = 2;
        public const byte SEMANTIC_MATRIX = 3;
        public const byte SEMANTIC_FILENAME = 4;
        public const byte SEMANTIC_CLOSURE = 5;
        public const byte SEMANTIC_SHADER = 6;
        public const byte SEMANTIC_MATERIAL = 7;
        public const byte SEMANTIC_ENUM = 8;
        public const byte SEMANTIC_LAST = 9;

        // Data block holding large data needed by the type.
        public class DataBlock
        {
            public DataBlock(string name, List<StructMemberDesc> members = null)
            {
                _name = name;
                _members = members;
            }

            public string getName()
            {
                return _name;
            }

            public List<StructMemberDesc> getStructMembers()
            {
                return _members;
            }

            private readonly string _name;
            private readonly List<StructMemberDesc> _members;
        }

        // TypeDesc Public Methods

        // Constructor.
        public TypeDesc(string name, byte basetype, byte semantic, ushort size, DataBlock data)
        {
            // Note: We only store the hash to keep the class size minimal.
            _idOffset = unchecked(constexpr_hash(name) - NONE_ID);
            _basetype = basetype;
            _semantic = semantic;
            _size = size;
            _data = data;
        }

        // Return the unique id assigned to this type.
        // The id is a hash of the given type name.
        public uint typeId()
        {
            return unchecked(_idOffset + NONE_ID);
        }

        // Return the name of the type.
        public string getName()
        {
            return _data != null ? _data.getName() : NONE_TYPE_NAME;
        }

        // Return the basetype for the type.
        public byte getBaseType()
        {
            return _basetype;
        }

        // Return the semantic for the type.
        public byte getSemantic()
        {
            return _semantic;
        }

        // Return the number of elements the type is composed of.
        // Will return 1 for scalar types and a size greater than 1 for aggregate type.
        // For array types 0 is returned since the number of elements is undefined
        // until an array is instantiated.
        public int getSize()
        {
            return _size;
        }

        // Return true if the type is a scalar type.
        public bool isScalar()
        {
            return _size == 1;
        }

        // Return true if the type is an aggregate type.
        public bool isAggregate()
        {
            return _size > 1;
        }

        // Return true if the type is an array type.
        public bool isArray()
        {
            return _size == 0;
        }

        // Return true if the type is an aggregate of 2 floats.
        public bool isFloat2()
        {
            return _size == 2 && (_semantic == SEMANTIC_COLOR || _semantic == SEMANTIC_VECTOR);
        }

        // Return true if the type is an aggregate of 3 floats.
        public bool isFloat3()
        {
            return _size == 3 && (_semantic == SEMANTIC_COLOR || _semantic == SEMANTIC_VECTOR);
        }

        // Return true if the type is an aggregate of 4 floats.
        public bool isFloat4()
        {
            return _size == 4 && (_semantic == SEMANTIC_COLOR || _semantic == SEMANTIC_VECTOR);
        }

        // Return true if the type represents a closure.
        public bool isClosure()
        {
            return (_semantic == SEMANTIC_CLOSURE || _semantic == SEMANTIC_SHADER || _semantic == SEMANTIC_MATERIAL);
        }

        // Return true if the type represents a struct.
        public bool isStruct()
        {
            return _basetype == BASETYPE_STRUCT;
        }

        // Return a pointer to a struct member description vector if an entry for this type exists.
        public List<StructMemberDesc> getStructMembers()
        {
            return _data != null ? _data.getStructMembers() : null;
        }

        // Equality operator
        public static bool operator ==(TypeDesc lhs, TypeDesc rhs)
        {
            return lhs._idOffset == rhs._idOffset;
        }

        // Inequality operator
        public static bool operator !=(TypeDesc lhs, TypeDesc rhs)
        {
            return lhs._idOffset != rhs._idOffset;
        }

        // Less-than operator
        public static bool operator <(TypeDesc lhs, TypeDesc rhs)
        {
            return lhs.typeId() < rhs.typeId();
        }

        public static bool operator >(TypeDesc lhs, TypeDesc rhs)
        {
            return lhs.typeId() > rhs.typeId();
        }

        public bool Equals(TypeDesc other)
        {
            return this == other;
        }

        public override bool Equals(object obj)
        {
            return obj is TypeDesc other && this == other;
        }

        public int CompareTo(TypeDesc other)
        {
            return typeId().CompareTo(other.typeId());
        }

        // Hash operator
        public override int GetHashCode()
        {
            return (int)typeId();
        }

        // Create a Value from a string for a given typeDesc
        public Value createValueFromStrings(string value)
        {
            if (!isStruct())
            {
                return Value.createValueFromStrings(value, getName());
            }

            // Value::createValueFromStrings() can only create a valid Value for a struct if it is passed
            // the optional TypeDef argument, otherwise it just returns a "string" typed Value.
            // So if this is a struct type we need to create a new AggregateValue.

            List<string> subValues = parseStructValueString(value);

            List<StructMemberDesc> structMembers = getStructMembers();

            if (structMembers == null || (subValues.Count != structMembers.Count))
            {
                throw new ExceptionShaderGenError("Wrong number of initializers for struct type " + getName());
            }

            AggregateValue result = AggregateValue.createAggregateValue(getName());
            for (int i = 0; i < structMembers.Count; ++i)
            {
                result.appendValue(structMembers[i].getType().createValueFromStrings(subValues[i]));
            }

            return result;
        }

        // TypeDesc Public Members
        public const string NONE_TYPE_NAME = "none";

        // TypeDesc Private Methods

        // Simple hash function, used to hash the type name at compile time in the C++.
        private static uint constexpr_hash(string str)
        {
            uint h = 2166136261;
            unchecked
            {
                foreach (char c in System.Text.Encoding.UTF8.GetBytes(str))
                {
                    h = (h * 16777619) ^ c;
                }
            }
            return h;
        }

        // TypeDesc Private Members

        // The id of the "none" type. The id is stored relative to it so that
        // the default value of the struct is the "none" type, as the default
        // constructor makes it in the C++.
        private static readonly uint NONE_ID = constexpr_hash("none");

        private readonly uint _idOffset;
        private readonly byte _basetype;
        private readonly byte _semantic;
        private readonly ushort _size;
        private readonly DataBlock _data;
    }

    // StructMemberDesc Definition
    // Type descriptor for member of a compound type.
    public class StructMemberDesc
    {
        // StructMemberDesc Public Methods
        public StructMemberDesc(TypeDesc type, string name, string defaultValueStr)
        {
            _type = type;
            _name = name;
            _defaultValueStr = defaultValueStr;
        }

        public TypeDesc getType()
        {
            return _type;
        }

        public string getName()
        {
            return _name;
        }

        public string getDefaultValueStr()
        {
            return _defaultValueStr;
        }

        // StructMemberDesc Private Members
        private readonly TypeDesc _type;
        private readonly string _name;
        private readonly string _defaultValueStr;
    }

    // TypeSystem Definition
    public class TypeSystem
    {
        // TypeSystem Public Methods

        // Create a new type system.
        public static TypeSystem create()
        {
            return new TypeSystem();
        }

        // Register an existing type decription.
        public void registerType(TypeDesc type)
        {
            if (_typesByName.ContainsKey(type.getName()))
            {
                // Remove the old type so we can replace it.
                int i = 0;
                foreach (TypeDesc t in _types)
                {
                    if (t == type)
                    {
                        _types[i] = type;
                        _typesByName[type.getName()] = type;
                        break;
                    }
                    ++i;
                }
            }
            else
            {
                _types.Add(type);
                _typesByName[type.getName()] = type;
            }
        }

        // Create and register a new type description.
        public void registerType(string name, byte basetype, byte semantic, ushort size, List<StructMemberDesc> members = null)
        {
            // Allocate a data block and use to initialize a new type description.
            TypeDesc.DataBlock data = new TypeDesc.DataBlock(name, members);
            _dataBlocks.Add(data);
            registerType(new TypeDesc(name, basetype, semantic, size, data));
        }

        // Return a type description by name.
        // If no type is found Type.NONE is returned.
        public TypeDesc getType(string name)
        {
            TypeDesc found;
            return _typesByName.TryGetValue(name, out found) ? found : Type.NONE;
        }

        // Return all registered type descriptions.
        public List<TypeDesc> getTypes()
        {
            return _types;
        }

        // TypeSystem Protected Methods
        protected TypeSystem()
        {
            // Register all the standard types.
            registerType(Type.BOOLEAN);
            registerType(Type.INTEGER);
            registerType(Type.INTEGERARRAY);
            registerType(Type.FLOAT);
            registerType(Type.FLOATARRAY);
            registerType(Type.VECTOR2);
            registerType(Type.VECTOR3);
            registerType(Type.VECTOR4);
            registerType(Type.COLOR3);
            registerType(Type.COLOR4);
            registerType(Type.MATRIX33);
            registerType(Type.MATRIX44);
            registerType(Type.STRING);
            registerType(Type.FILENAME);
            registerType(Type.BSDF);
            registerType(Type.EDF);
            registerType(Type.VDF);
            registerType(Type.SURFACESHADER);
            registerType(Type.VOLUMESHADER);
            registerType(Type.DISPLACEMENTSHADER);
            registerType(Type.LIGHTSHADER);
            registerType(Type.MATERIAL);
        }

        // TypeSystem Private Members
        private List<TypeDesc> _types = new List<TypeDesc>();
        private Dictionary<string, TypeDesc> _typesByName = new Dictionary<string, TypeDesc>(StringComparer.Ordinal);
        private List<TypeDesc.DataBlock> _dataBlocks = new List<TypeDesc.DataBlock>();
    }

    // Type Definition
    // Type descriptors for all standard types (the namespace Type in the C++).
    public static class Type
    {
        // Type.NONE matches the TypeDesc default constructor
        public static readonly TypeDesc NONE = new TypeDesc();
        public static readonly TypeDesc BOOLEAN = new TypeDesc("boolean", TypeDesc.BASETYPE_BOOLEAN, TypeDesc.SEMANTIC_NONE, 1, new TypeDesc.DataBlock("boolean"));
        public static readonly TypeDesc INTEGER = new TypeDesc("integer", TypeDesc.BASETYPE_INTEGER, TypeDesc.SEMANTIC_NONE, 1, new TypeDesc.DataBlock("integer"));
        public static readonly TypeDesc FLOAT = new TypeDesc("float", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_NONE, 1, new TypeDesc.DataBlock("float"));
        public static readonly TypeDesc INTEGERARRAY = new TypeDesc("integerarray", TypeDesc.BASETYPE_INTEGER, TypeDesc.SEMANTIC_NONE, 0, new TypeDesc.DataBlock("integerarray"));
        public static readonly TypeDesc FLOATARRAY = new TypeDesc("floatarray", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_NONE, 0, new TypeDesc.DataBlock("floatarray"));
        public static readonly TypeDesc VECTOR2 = new TypeDesc("vector2", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_VECTOR, 2, new TypeDesc.DataBlock("vector2"));
        public static readonly TypeDesc VECTOR3 = new TypeDesc("vector3", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_VECTOR, 3, new TypeDesc.DataBlock("vector3"));
        public static readonly TypeDesc VECTOR4 = new TypeDesc("vector4", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_VECTOR, 4, new TypeDesc.DataBlock("vector4"));
        public static readonly TypeDesc COLOR3 = new TypeDesc("color3", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_COLOR, 3, new TypeDesc.DataBlock("color3"));
        public static readonly TypeDesc COLOR4 = new TypeDesc("color4", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_COLOR, 4, new TypeDesc.DataBlock("color4"));
        public static readonly TypeDesc MATRIX33 = new TypeDesc("matrix33", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_MATRIX, 9, new TypeDesc.DataBlock("matrix33"));
        public static readonly TypeDesc MATRIX44 = new TypeDesc("matrix44", TypeDesc.BASETYPE_FLOAT, TypeDesc.SEMANTIC_MATRIX, 16, new TypeDesc.DataBlock("matrix44"));
        public static readonly TypeDesc STRING = new TypeDesc("string", TypeDesc.BASETYPE_STRING, TypeDesc.SEMANTIC_NONE, 1, new TypeDesc.DataBlock("string"));
        public static readonly TypeDesc FILENAME = new TypeDesc("filename", TypeDesc.BASETYPE_STRING, TypeDesc.SEMANTIC_FILENAME, 1, new TypeDesc.DataBlock("filename"));
        public static readonly TypeDesc BSDF = new TypeDesc("BSDF", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_CLOSURE, 1, new TypeDesc.DataBlock("BSDF"));
        public static readonly TypeDesc EDF = new TypeDesc("EDF", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_CLOSURE, 1, new TypeDesc.DataBlock("EDF"));
        public static readonly TypeDesc VDF = new TypeDesc("VDF", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_CLOSURE, 1, new TypeDesc.DataBlock("VDF"));
        public static readonly TypeDesc SURFACESHADER = new TypeDesc("surfaceshader", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_SHADER, 1, new TypeDesc.DataBlock("surfaceshader"));
        public static readonly TypeDesc VOLUMESHADER = new TypeDesc("volumeshader", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_SHADER, 1, new TypeDesc.DataBlock("volumeshader"));
        public static readonly TypeDesc DISPLACEMENTSHADER = new TypeDesc("displacementshader", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_SHADER, 1, new TypeDesc.DataBlock("displacementshader"));
        public static readonly TypeDesc LIGHTSHADER = new TypeDesc("lightshader", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_SHADER, 1, new TypeDesc.DataBlock("lightshader"));
        public static readonly TypeDesc MATERIAL = new TypeDesc("material", TypeDesc.BASETYPE_NONE, TypeDesc.SEMANTIC_MATERIAL, 1, new TypeDesc.DataBlock("material"));
    }
}
