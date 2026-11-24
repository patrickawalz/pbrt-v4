// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenShader/Syntax.h and Syntax.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // Syntax Definition
    // Base class for syntax objects used by shader generators
    // to emit code with correct syntax for each language.
    public abstract class Syntax
    {
        // Syntax Public Types
        // Punctuation types
        public enum Punctuation
        {
            PARENTHESES,
            CURLY_BRACKETS,
            SQUARE_BRACKETS,
            DOUBLE_SQUARE_BRACKETS
        }

        // Syntax Public Methods
        // Register syntax handling for a data type.
        // Required to be set for all supported data types.
        public void registerTypeSyntax(TypeDesc type, TypeSyntax syntax)
        {
            int index;
            if (_typeSyntaxIndexByType.TryGetValue(type, out index))
            {
                _typeSyntaxes[index] = syntax;
            }
            else
            {
                _typeSyntaxes.Add(syntax);
                _typeSyntaxIndexByType[type] = _typeSyntaxes.Count - 1;
            }

            // Make this type a restricted name
            registerReservedWords(new StringSet(new string[] { syntax.getName() }));
        }

        // Register names that are reserved words not to be used by a code generator when naming
        // variables and functions. Keywords, types, built-in functions etc. should be
        // added to this set. Multiple calls will add to the internal set of names.
        public void registerReservedWords(StringSet names)
        {
            _reservedWords.UnionWith(names);
        }

        // Register a set string replacements for disallowed tokens
        // for a code generator when naming variables and functions.
        // Multiple calls will add to the internal set of tokens.
        public void registerInvalidTokens(StringMap tokens)
        {
            // As std::unordered_map::insert: tokens already registered are kept.
            foreach (KeyValuePair<string, string> token in tokens)
            {
                if (!_invalidTokens.ContainsKey(token.Key))
                {
                    _invalidTokens[token.Key] = token.Value;
                }
            }
        }

        // Returns a set of names that are reserved words for this language syntax.
        public StringSet getReservedWords()
        {
            return _reservedWords;
        }

        // Returns a mapping from disallowed tokens to replacement strings for this language syntax.
        public StringMap getInvalidTokens()
        {
            return _invalidTokens;
        }

        // Returns the type syntax object for a named type.
        // Throws an exception if a type syntax is not defined for the given type.
        public TypeSyntax getTypeSyntax(TypeDesc type)
        {
            int index;
            if (!_typeSyntaxIndexByType.TryGetValue(type, out index))
            {
                throw new ExceptionShaderGenError("No syntax is defined for the given type '" + type.getName() + "'.");
            }
            return _typeSyntaxes[index];
        }

        // Returns an array of all registered type syntax objects
        public List<TypeSyntax> getTypeSyntaxes()
        {
            return _typeSyntaxes;
        }

        // Return a type description for the given type name.
        public TypeDesc getType(string name)
        {
            return _typeSystem.getType(name);
        }

        // Returns the name syntax of the given type
        public string getTypeName(TypeDesc type)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            return syntax.getName();
        }

        // Returns the type name in an output context
        public virtual string getOutputTypeName(TypeDesc type)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            string outputModifier = getOutputQualifier();
            return outputModifier.Length != 0 ? outputModifier + " " + syntax.getName() : syntax.getName();
        }

        // Returns a type alias for the given data type.
        // If not used returns an empty string.
        public string getTypeAlias(TypeDesc type)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            return syntax.getTypeAlias();
        }

        // Returns a custom type definition if needed for the given data type.
        // If not used returns an empty string.
        public string getTypeDefinition(TypeDesc type)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            return syntax.getTypeDefinition();
        }

        // Returns the default value string for the given type
        public string getDefaultValue(TypeDesc type, bool uniform = false)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            return syntax.getDefaultValue(uniform);
        }

        // Returns the value string for a given type and value object
        public virtual string getValue(TypeDesc type, Value value, bool uniform = false)
        {
            TypeSyntax syntax = getTypeSyntax(type);
            return syntax.getValue(value, uniform);
        }

        // Returns the value string for a given shader port object
        public virtual string getValue(ShaderPort port, bool uniform = false)
        {
            TypeSyntax syntax = getTypeSyntax(port.getType());
            return syntax.getValue(port, uniform);
        }

        // Returns a type qualifier to be used when declaring types for input variables.
        // Default implementation returns empty string and derived syntax classes should
        // override this method.
        public virtual string getInputQualifier()
        {
            return EMPTY_STRING;
        }

        // Returns a type qualifier to be used when declaring types for output variables.
        // Default implementation returns empty string and derived syntax classes should
        // override this method.
        public virtual string getOutputQualifier()
        {
            return EMPTY_STRING;
        }

        // Get the qualifier used when declaring constant variables.
        // Derived classes must define this method.
        public abstract string getConstantQualifier();

        // Get the qualifier used when declaring uniform variables.
        // Default implementation returns empty string and derived syntax classes should
        // override this method.
        public virtual string getUniformQualifier()
        {
            return EMPTY_STRING;
        }

        // Return the characters used for a newline.
        public virtual string getNewline()
        {
            return NEWLINE;
        }

        // Return the characters used for a single indentation level.
        public virtual string getIndentation()
        {
            return INDENTATION;
        }

        // Return the characters used to begin/end a string definition.
        public virtual string getStringQuote()
        {
            return STRING_QUOTE;
        }

        // Return the string pattern used for a file include statement.
        public virtual string getIncludeStatement()
        {
            return INCLUDE_STATEMENT;
        }

        // Return the characters used for single line comment.
        public virtual string getSingleLineComment()
        {
            return SINGLE_LINE_COMMENT;
        }

        // Return the characters used to begin a multi line comments block.
        public virtual string getBeginMultiLineComment()
        {
            return BEGIN_MULTI_LINE_COMMENT;
        }

        // Return the characters used to end a multi line comments block.
        public virtual string getEndMultiLineComment()
        {
            return END_MULTI_LINE_COMMENT;
        }

        // Return the file extension used for source code files in this language.
        public abstract string getSourceFileExtension();

        // Return the array suffix to use for declaring an array type.
        public virtual string getArrayTypeSuffix(TypeDesc type, Value value)
        {
            return EMPTY_STRING;
        }

        // Return the array suffix to use for declaring an array variable.
        public virtual string getArrayVariableSuffix(TypeDesc type, Value value)
        {
            if (type.isArray())
            {
                if (value.isA<List<float>>())
                {
                    int size = value.asA<List<float>>().Count;
                    return "[" + Mx.to_string(size) + "]";
                }
                else if (value.isA<List<int>>())
                {
                    int size = value.asA<List<int>>().Count;
                    return "[" + Mx.to_string(size) + "]";
                }
            }
            return EMPTY_STRING;
        }

        // Modify the given name string to remove any invalid characters or tokens.
        public virtual void makeValidName(ref string name)
        {
            // The C++ works on bytes, so every byte of a multi-byte character is replaced.
            StringBuilder valid = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (c < 0x80)
                {
                    valid.Append(isInvalidChar(c) ? '_' : c);
                }
                else
                {
                    valid.Append('_', c < 0x800 || char.IsSurrogate(c) ? 2 : 3);
                }
            }
            name = valid.ToString();
            name = replaceSubstrings(name, _invalidTokens);
            if (_reservedWords.Contains(name))
            {
                // We append "1" here because thats the prior behavior from makeIdentifier() below when
                // the reservedWords were added to the identifiers list.
                name = name+"1";
            }
        }

        // Make sure the given name is a unique identifier,
        // updating it if needed to make it unique.
        public virtual void makeIdentifier(ref string name, Dictionary<string, int> identifiers)
        {
            makeValidName(ref name);

            int found;
            if (identifiers.TryGetValue(name, out found))
            {
                // Name is not unique so append the counter and keep
                // incrementing until a unique name is found.
                string name2 = EMPTY_STRING;
                do
                {
                    name2 = name + Mx.to_string(found++);
                } while (identifiers.ContainsKey(name2));
                identifiers[name] = found;

                name = name2;
            }

            // Save it among the known identifiers.
            identifiers[name] = 1;
        }

        // Create a unique identifier for the given variable name and type.
        // The method is used for naming variables (inputs and outputs) in generated code.
        // Derived classes can override this method to have a custom naming strategy.
        // Default implementation adds a number suffix, or increases an existing number suffix,
        // on the name string if there is a name collision.
        public virtual string getVariableName(string name, TypeDesc type, Dictionary<string, int> identifiers)
        {
            // Default implementation just makes an identifier, but derived
            // classes can override this for custom variable naming.
            string variable = name;
            makeIdentifier(ref variable, identifiers);
            return variable;
        }

        // Given an input specification attempt to remap this to an enumeration which is accepted by
        // the shader generator. The enumeration may be converted to a different type than the input.
        // @param value The value string to remap.
        // @param type The type of the value to remap,
        // @param enumNames Type enumeration names
        // @param result Enumeration type and value (returned).
        // @return Return true if the remapping was successful.
        public virtual bool remapEnumeration(string value, TypeDesc type, string enumNames, ref (TypeDesc, Value) result)
        {
            return false;
        }

        // Create and return a type syntax for a struct type.
        public virtual StructTypeSyntax createStructSyntax(string structTypeName, string defaultValue, string uniformDefaultValue, string typeAlias, string typeDefinition)
        {
            return new StructTypeSyntax(
                this,
                structTypeName,
                defaultValue,
                uniformDefaultValue,
                typeAlias,
                typeDefinition);
        }

        // Syntax Public Members
        // Constants with commonly used strings.
        public const string NEWLINE = "\n";

        public const string SEMICOLON = ";";

        public const string COMMA = ",";

        // Syntax Protected Methods
        // Protected constructor
        protected Syntax(TypeSystem typeSystem)
        {
            _typeSystem = typeSystem;
        }

        // Syntax Protected Members
        protected TypeSystem _typeSystem;

        protected List<TypeSyntax> _typeSyntaxes = new List<TypeSyntax>();

        protected Dictionary<TypeDesc, int> _typeSyntaxIndexByType = new Dictionary<TypeDesc, int>();

        protected StringSet _reservedWords = new StringSet();

        protected StringMap _invalidTokens = new StringMap();

        protected const string INDENTATION = "    ";

        protected const string STRING_QUOTE = "\"";

        protected const string INCLUDE_STATEMENT = "#include";

        protected const string SINGLE_LINE_COMMENT = "// ";

        protected const string BEGIN_MULTI_LINE_COMMENT = "/* ";

        protected const string END_MULTI_LINE_COMMENT = " */";

        protected static readonly Dictionary<char, int> CHANNELS_MAPPING = new Dictionary<char, int>();
    }

    // TypeSyntax Definition
    // Base class for syntax handling of types.
    public abstract class TypeSyntax
    {
        // TypeSyntax Public Methods
        // Returns the type name.
        public string getName()
        {
            return _name;
        }

        // Returns a type alias if needed to define the type in the target language.
        public string getTypeAlias()
        {
            return _typeAlias;
        }

        // Returns a type definition if needed to define the type in the target language.
        public string getTypeDefinition()
        {
            return _typeDefinition;
        }

        // Returns the default value for this type.
        public string getDefaultValue(bool uniform)
        {
            return uniform ? _uniformDefaultValue : _defaultValue;
        }

        // Returns the syntax for accessing type members if the type
        // can be swizzled.
        public List<string> getMembers()
        {
            return _members;
        }

        // Returns a value formatted according to this type syntax.
        // The value is constructed from the given shader port object.
        public virtual string getValue(ShaderPort port, bool uniform)
        {
            if (port == null || port.getValue() == null)
            {
                return getDefaultValue(uniform);
            }
            return getValue(port.getValue(), uniform);
        }

        // Returns a value formatted according to this type syntax.
        // The value is constructed from the given value object.
        public abstract string getValue(Value value, bool uniform);

        // TypeSyntax Protected Methods
        // Protected constructor
        protected TypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias, string typeDefinition, List<string> members)
        {
            _parent = parent;
            _name = name;
            _defaultValue = defaultValue;
            _uniformDefaultValue = uniformDefaultValue;
            _typeAlias = typeAlias;
            _typeDefinition = typeDefinition;
            _members = members != null ? members : EMPTY_MEMBERS;
        }

        // TypeSyntax Protected Members
        // parent syntax class
        protected Syntax _parent;

        // type name
        protected string _name = "";

        // default value syntax
        protected string _defaultValue = "";

        // default value syntax when assigned to uniforms
        protected string _uniformDefaultValue = "";

        // type alias if needed in source code
        protected string _typeAlias = "";

        // custom type definition if needed in source code
        protected string _typeDefinition = "";

        // syntax for member access
        protected List<string> _members = new List<string>();

        protected static readonly List<string> EMPTY_MEMBERS = new List<string>();
    }

    // ScalarTypeSyntax Definition
    // Specialization of TypeSyntax for scalar types.
    public class ScalarTypeSyntax : TypeSyntax
    {
        // ScalarTypeSyntax Public Methods
        public ScalarTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, EMPTY_MEMBERS)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            return value.getValueString();
        }
    }

    // StringTypeSyntax Definition
    // Specialization of TypeSyntax for string types.
    public class StringTypeSyntax : ScalarTypeSyntax
    {
        // StringTypeSyntax Public Methods
        public StringTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            return "\"" + value.getValueString() + "\"";
        }
    }

    // AggregateTypeSyntax Definition
    // Specialization of TypeSyntax for aggregate types.
    public class AggregateTypeSyntax : TypeSyntax
    {
        // AggregateTypeSyntax Public Methods
        public AggregateTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            string valueString = value.getValueString();
            return valueString.Length == 0 ? valueString : getName() + "(" + valueString + ")";
        }
    }

    // StructTypeSyntax Definition
    // Specialization of TypeSyntax for struct types.
    public class StructTypeSyntax : TypeSyntax
    {
        // StructTypeSyntax Public Methods
        public StructTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            // Throws ExceptionTypeError if the value is not an aggregate, e.g. when a
            // TypeDef shadows a built-in type name and the port carries a TypedValue<T>.
            AggregateValue aggValue = value.asA<AggregateValue>();

            string result = "{";

            string separator = "";
            foreach (Value memberValue in aggValue.getMembers())
            {
                result += separator;
                separator = ";";

                string memberTypeName = memberValue.getTypeString();
                TypeDesc memberTypeDesc = _parent.getType(memberTypeName);

                // Recursively use the syntax to generate the output, so we can support nested structs.
                string valueStr = _parent.getValue(memberTypeDesc, memberValue, true);

                result += valueStr;
            }

            result += "}";

            return result;
        }
    }

    // Syntax Functions
    public static partial class Mx
    {
        internal static bool isInvalidChar(char c)
        {
            return !isalnum(c) && c != '_';
        }
    }
}
