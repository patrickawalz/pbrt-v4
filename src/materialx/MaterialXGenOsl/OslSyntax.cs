// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXGenOsl/OslSyntax.h and OslSyntax.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using static MaterialX.Mx;

namespace MaterialX
{
    // OslSyntax Definition
    // Syntax class for OSL (Open Shading Language)
    public class OslSyntax : Syntax
    {
        // OslSyntax Public Methods
        public OslSyntax(TypeSystem typeSystem) : base(typeSystem)
        {
            // Add in all reserved words and keywords in OSL
            registerReservedWords(new StringSet(new string[]
                { // OSL types and keywords
                  "and", "break", "closure", "color", "continue", "do", "else", "emit", "float", "for", "if", "illuminance",
                  "illuminate", "int", "matrix", "normal", "not", "or", "output", "point", "public", "return", "string",
                  "struct", "vector", "void", "while",
                  "bool", "case", "catch", "char", "class", "const", "delete", "default", "double", "enum", "extern",
                  "false", "friend", "goto", "inline", "long", "new", "operator", "private", "protected", "short",
                  "signed", "sizeof", "static", "switch", "template", "this", "throw", "true", "try", "typedef", "uniform",
                  "union", "unsigned", "varying", "virtual", "volatile",
                  // OSL standard library functions names
                  "degrees", "radians", "cos", "sin", "tan", "acos", "asin", "atan", "atan2", "cosh", "sinh", "tanh",
                  "pow", "log", "log2", "log10", "logb", "sqrt", "inversesqrt", "cbrt", "hypot", "abs", "fabs", "sign",
                  "floor", "ceil", "round", "trunc", "fmod", "mod", "min", "max", "clamp", "mix", "select", "isnan",
                  "isinf", "isfinite", "erf", "erfc", "cross", "dot", "length", "distance", "normalize", "faceforward",
                  "reflect", "fresnel", "transform", "transformu", "rotate", "luminance", "blackbody", "wavelength_color",
                  "transformc", "determinant", "transpose", "step", "smoothstep", "linearstep", "smooth_linearstep", "aastep",
                  "hash", "strlen", "getchar", "startswith", "endswith", "substr", "stof", "stoi", "concat", "textureresource",
                  "backfacing", "raytype", "iscameraray", "isdiffuseray", "isglossyray", "isshadowray", "getmatrix",
                  "emission", "background", "diffuse", "oren_nayer", "translucent", "phong", "ward", "microfacet",
                  "reflection", "transparent", "debug", "holdout", "subsurface", "sheen",
                  "oren_nayar_diffuse_bsdf", "burley_diffuse_bsdf", "dielectric_bsdf", "conductor_bsdf", "generalized_schlick_bsdf",
                  "translucent_bsdf", "transparent_bsdf", "subsurface_bssrdf", "sheen_bsdf", "uniform_edf", "anisotropic_vdf",
                  "medium_vdf", "layer", "artistic_ior" }));

            //
            // Register type syntax handlers for each data type.
            //

            registerTypeSyntax(
                Type.FLOAT,
                new ScalarTypeSyntax(
                    this,
                    "float",
                    "0.0",
                    "0.0"));

            registerTypeSyntax(
                Type.FLOATARRAY,
                new OslFloatArrayTypeSyntax(
                    this,
                    "float"));

            registerTypeSyntax(
                Type.INTEGER,
                new ScalarTypeSyntax(
                    this,
                    "int",
                    "0",
                    "0"));

            registerTypeSyntax(
                Type.INTEGERARRAY,
                new OslIntegerArrayTypeSyntax(
                    this,
                    "int"));

            registerTypeSyntax(
                Type.BOOLEAN,
                new OslBooleanTypeSyntax(this));

            registerTypeSyntax(
                // Note: the color type in OSL is a built in type and
                // should not use the custom OslVecTypeSyntax.
                Type.COLOR3,
                new AggregateTypeSyntax(
                    this,
                    "color",
                    "color(0.0)",
                    "color(0.0)",
                    EMPTY_STRING,
                    EMPTY_STRING,
                    VECTOR_MEMBERS));

            registerTypeSyntax(
                Type.COLOR4,
                new OslColor4TypeSyntax(this));

            registerTypeSyntax(
                Type.VECTOR2,
                new OslVecTypeSyntax(
                    this,
                    "vector2",
                    "vector2(0.0, 0.0)",
                    "{0.0, 0.0}",
                    EMPTY_STRING,
                    EMPTY_STRING,
                    VECTOR2_MEMBERS));

            registerTypeSyntax(
                // Note: the vector type in OSL is a built in type and
                // should not use the custom OslVecTypeSyntax.
                Type.VECTOR3,
                new AggregateTypeSyntax(
                    this,
                    "vector",
                    "vector(0.0)",
                    "vector(0.0)",
                    EMPTY_STRING,
                    EMPTY_STRING,
                    VECTOR_MEMBERS));

            registerTypeSyntax(
                Type.VECTOR4,
                new OslVecTypeSyntax(
                    this,
                    "vector4",
                    "vector4(0.0, 0.0, 0.0, 0.0)",
                    "{0.0, 0.0, 0.0, 0.0}",
                    EMPTY_STRING,
                    EMPTY_STRING,
                    VECTOR4_MEMBERS));

            registerTypeSyntax(
                Type.MATRIX33,
                new OSLMatrix3TypeSyntax(
                    this,
                    "matrix",
                    "matrix(1.0)",
                    "matrix(1.0)"));

            registerTypeSyntax(
                Type.MATRIX44,
                new AggregateTypeSyntax(
                    this,
                    "matrix",
                    "matrix(1.0)",
                    "matrix(1.0)"));

            registerTypeSyntax(
                Type.STRING,
                new StringTypeSyntax(
                    this,
                    "string",
                    "\"\"",
                    "\"\""));

            registerTypeSyntax(
                Type.FILENAME,
                new OSLFilenameTypeSyntax(
                    this,
                    "textureresource ",
                    "textureresource (\"\", \"\")",
                    "{\"\", \"\"}",
                    EMPTY_STRING,
                    "struct textureresource { string filename; string colorspace; };"));

            registerTypeSyntax(
                Type.BSDF,
                new ScalarTypeSyntax(
                    this,
                    "BSDF",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define BSDF closure color"));

            registerTypeSyntax(
                Type.EDF,
                new ScalarTypeSyntax(
                    this,
                    "EDF",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define EDF closure color"));

            registerTypeSyntax(
                Type.VDF,
                new ScalarTypeSyntax(
                    this,
                    "VDF",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define VDF closure color"));

            registerTypeSyntax(
                Type.SURFACESHADER,
                new AggregateTypeSyntax(
                    this,
                    "surfaceshader",
                    "surfaceshader(null_closure(), null_closure(), 1.0)",
                    "{ 0, 0, 1.0 }",
                    "closure color",
                    "struct surfaceshader { closure color bsdf; closure color edf; float opacity; };"));

            registerTypeSyntax(
                Type.VOLUMESHADER,
                new ScalarTypeSyntax(
                    this,
                    "volumeshader",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define volumeshader closure color"));

            registerTypeSyntax(
                Type.DISPLACEMENTSHADER,
                new ScalarTypeSyntax(
                    this,
                    "displacementshader",
                    "vector(0.0)",
                    "vector(0.0)",
                    "vector",
                    "#define displacementshader vector"));

            registerTypeSyntax(
                Type.LIGHTSHADER,
                new ScalarTypeSyntax(
                    this,
                    "lightshader",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define lightshader closure color"));

            registerTypeSyntax(
                Type.MATERIAL,
                new ScalarTypeSyntax(
                    this,
                    "MATERIAL",
                    "null_closure()",
                    "0",
                    "closure color",
                    "#define MATERIAL closure color"));
        }

        public static Syntax create(TypeSystem typeSystem)
        {
            return new OslSyntax(typeSystem);
        }

        public override StructTypeSyntax createStructSyntax(string structTypeName, string defaultValue, string uniformDefaultValue, string typeAlias, string typeDefinition)
        {
            return new OslStructTypeSyntax(
                this,
                structTypeName,
                defaultValue,
                uniformDefaultValue,
                typeAlias,
                typeDefinition);
        }

        public override string getOutputQualifier()
        {
            return OUTPUT_QUALIFIER;
        }

        public override string getConstantQualifier()
        {
            return EMPTY_STRING;
        }

        public override string getSourceFileExtension()
        {
            return SOURCE_FILE_EXTENSION;
        }

        // OslSyntax Public Members
        public const string OUTPUT_QUALIFIER = "output";

        public const string SOURCE_FILE_EXTENSION = ".osl";

        public static readonly List<string> VECTOR_MEMBERS = new List<string> { "[0]", "[1]", "[2]" };

        public static readonly List<string> VECTOR2_MEMBERS = new List<string> { ".x", ".y" };

        public static readonly List<string> VECTOR4_MEMBERS = new List<string> { ".x", ".y", ".z", ".w" };

        public static readonly List<string> COLOR4_MEMBERS = new List<string> { ".rgb[0]", ".rgb[1]", ".rgb[2]", ".a" };
    }

    // OslBooleanTypeSyntax Definition
    file class OslBooleanTypeSyntax : ScalarTypeSyntax
    {
        // OslBooleanTypeSyntax Public Methods
        public OslBooleanTypeSyntax(Syntax parent) : base(parent, "int", "0", "0", EMPTY_STRING, "#define true 1\n#define false 0")
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            return value.asA<bool>() ? "1" : "0";
        }
    }

    // OslArrayTypeSyntax Definition
    file abstract class OslArrayTypeSyntax : ScalarTypeSyntax
    {
        // OslArrayTypeSyntax Public Methods
        public OslArrayTypeSyntax(Syntax parent, string name) : base(parent, name, EMPTY_STRING, EMPTY_STRING, EMPTY_STRING)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            if (!isEmpty(value))
            {
                return "{" + value.getValueString() + "}";
            }
            // OSL disallows arrays without initialization when specified as input uniform
            else if (uniform)
            {
                throw new ExceptionShaderGenError("Uniform array cannot initialize to a empty value.");
            }
            return EMPTY_STRING;
        }

        // OslArrayTypeSyntax Protected Methods
        protected abstract bool isEmpty(Value value);
    }

    // OslFloatArrayTypeSyntax Definition
    file class OslFloatArrayTypeSyntax : OslArrayTypeSyntax
    {
        // OslFloatArrayTypeSyntax Public Methods
        public OslFloatArrayTypeSyntax(Syntax parent, string name) : base(parent, name)
        {
        }

        // OslFloatArrayTypeSyntax Protected Methods
        protected override bool isEmpty(Value value)
        {
            List<float> valueArray = value.asA<List<float>>();
            return valueArray.Count == 0;
        }
    }

    // OslIntegerArrayTypeSyntax Definition
    file class OslIntegerArrayTypeSyntax : OslArrayTypeSyntax
    {
        // OslIntegerArrayTypeSyntax Public Methods
        public OslIntegerArrayTypeSyntax(Syntax parent, string name) : base(parent, name)
        {
        }

        // OslIntegerArrayTypeSyntax Protected Methods
        protected override bool isEmpty(Value value)
        {
            List<int> valueArray = value.asA<List<int>>();
            return valueArray.Count == 0;
        }
    }

    // OslVecTypeSyntax Definition
    // In OSL vector2, vector4, and color4 are custom struct types and require a different
    // value syntax for uniforms. So override the aggregate type syntax to support this.
    file class OslVecTypeSyntax : AggregateTypeSyntax
    {
        // OslVecTypeSyntax Public Methods
        public OslVecTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            if (uniform)
            {
                return "{" + value.getValueString() + "}";
            }
            else
            {
                return getName() + "(" + value.getValueString() + ")";
            }
        }
    }

    // OslColor4TypeSyntax Definition
    // For the color4 type we need even more specialization since it's a struct of a struct:
    //
    // struct color4 {
    //    color rgb;
    //    float a;
    // }
    //
    file class OslColor4TypeSyntax : OslVecTypeSyntax
    {
        // OslColor4TypeSyntax Public Methods
        public OslColor4TypeSyntax(Syntax parent) : base(parent, "color4", "color4(color(0.0), 0.0)", "{color(0.0), 0.0}", EMPTY_STRING, EMPTY_STRING, OslSyntax.COLOR4_MEMBERS)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            // The float format and precision of the stream in the C++ are the
            // current ones of Value, which toValueString uses.
            Color4 c = value.asA<Color4>();

            if (uniform)
            {
                return "{color(" + toValueString(c[0]) + ", " + toValueString(c[1]) + ", " + toValueString(c[2]) + "), " + toValueString(c[3]) + "}";
            }
            else
            {
                return "color4(color(" + toValueString(c[0]) + ", " + toValueString(c[1]) + ", " + toValueString(c[2]) + "), " + toValueString(c[3]) + ")";
            }
        }
    }

    // OSLMatrix3TypeSyntax Definition
    file class OSLMatrix3TypeSyntax : AggregateTypeSyntax
    {
        // OSLMatrix3TypeSyntax Public Methods
        public OSLMatrix3TypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            List<string> values = splitString(value.getValueString(), ",");
            if (values.Count == 0)
            {
                throw new ExceptionShaderGenError("No values given to construct a value");
            }

            // Write the value using a stream to maintain any float formatting set
            // using Value::setFloatFormat() and Value::setFloatPrecision()
            StringBuilder ss = new StringBuilder();
            ss.Append(getName() + "(");
            for (int i = 0; i < values.Count; i++)
            {
                ss.Append(values[i] + ", ");
                if ((i + 1) % 3 == 0)
                {
                    ss.Append("0.000" + ", ");
                }
            }
            const string ROW_4 = "0.000, 0.000, 0.000, 1.000";
            ss.Append(ROW_4 + ")");

            return ss.ToString();
        }
    }

    // OSLFilenameTypeSyntax Definition
    file class OSLFilenameTypeSyntax : AggregateTypeSyntax
    {
        // OSLFilenameTypeSyntax Public Methods
        public OSLFilenameTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(ShaderPort port, bool uniform)
        {
            if (port == null)
            {
                return EMPTY_STRING;
            }

            string prefix = uniform ? "{" : getName() + "(";
            string suffix = uniform ? "}" : ")";
            string filename = port.getValue() != null ? port.getValue().getValueString() : EMPTY_STRING;
            return prefix + "\"" + filename + "\", \"" + port.getColorSpace() + "\"" + suffix;
        }

        public override string getValue(Value value, bool uniform)
        {
            string prefix = uniform ? "{" : getName() + "(";
            string suffix = uniform ? "}" : ")";
            return prefix + "\"" + value.getValueString() + "\", \"\"" + suffix;
        }
    }

    // OslStructTypeSyntax Definition
    // Specialization of TypeSyntax for aggregate types.
    file class OslStructTypeSyntax : StructTypeSyntax
    {
        // OslStructTypeSyntax Public Methods
        public OslStructTypeSyntax(Syntax parent, string name, string defaultValue, string uniformDefaultValue, string typeAlias = EMPTY_STRING, string typeDefinition = EMPTY_STRING, List<string> members = null) : base(parent, name, defaultValue, uniformDefaultValue, typeAlias, typeDefinition, members)
        {
        }

        public override string getValue(Value value, bool uniform)
        {
            // Throws ExceptionTypeError if the value is not an aggregate, e.g. when a
            // TypeDef shadows a built-in type name and the port carries a TypedValue<T>.
            AggregateValue aggValue = value.asA<AggregateValue>();

            string result = aggValue.getTypeString() + "(";

            string separator = "";
            foreach (Value memberValue in aggValue.getMembers())
            {
                result += separator;
                separator = ",";

                string memberTypeName = memberValue.getTypeString();
                TypeDesc memberTypeDesc = _parent.getType(memberTypeName);

                // Recursively use the syntax to generate the output, so we can supported nested structs.
                result += _parent.getValue(memberTypeDesc, memberValue, true);
            }

            result += ")";

            return result;
        }
    }
}
