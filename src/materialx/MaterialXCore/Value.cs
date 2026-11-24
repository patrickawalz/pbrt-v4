// MaterialX is Copyright Contributors to the MaterialX Project and is licensed
// under Apache-2.0. This file is a C# port of MaterialXCore/Value.h and Value.cpp.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MaterialX
{
    // Value Definition
    public abstract class Value
    {
        // Value Public Types
        public enum FloatFormat
        {
            FloatFormatDefault = 0,
            FloatFormatFixed = 1,
            FloatFormatScientific = 2
        }

        // Value Public Methods
        public static Value createValue<T>(T data)
        {
            return new TypedValue<T>(data);
        }

        public static Value createValueFromStrings(string value, string type, TypeDef typeDef = null)
        {
            Func<string, Value> creator;
            if (_creatorMap.TryGetValue(type, out creator))
            {
                return creator(value);
            }

            if (typeDef != null && typeDef.getMembers().Count != 0)
            {
                // If we're given a TypeDef pointer that has child members, then we can create a new aggregate value.
                return AggregateValue.createAggregateValueFromString(value, type, typeDef);
            }

            return TypedValue<string>.createFromString(value);
        }

        public static bool hasBuiltInCreator(string type)
        {
            return _creatorMap.ContainsKey(type);
        }

        public abstract Value copy();

        public bool isA<T>()
        {
            if (typeof(T) == typeof(AggregateValue))
            {
                return this is AggregateValue;
            }
            return this is TypedValue<T>;
        }

        public T asA<T>()
        {
            if (typeof(T) == typeof(AggregateValue))
            {
                if (!(this is AggregateValue))
                {
                    throw new ExceptionTypeError("Incorrect type specified for value");
                }
                return (T)(object)this;
            }
            TypedValue<T> typedVal = this as TypedValue<T>;
            if (typedVal == null)
            {
                throw new ExceptionTypeError("Incorrect type specified for value");
            }
            return typedVal.getData();
        }

        public abstract string getTypeString();

        public abstract string getValueString();

        public static void setFloatFormat(FloatFormat format)
        {
            _floatFormat = format;
        }

        public static void setFloatPrecision(int precision)
        {
            _floatPrecision = precision + 1;
        }

        public static FloatFormat getFloatFormat()
        {
            return _floatFormat;
        }

        public static int getFloatPrecision()
        {
            // The thread-local default must read as 6 on every thread.
            return _floatPrecision - 1;
        }

        public abstract bool isEqual(Value other);

        // Value Private Members
        [ThreadStatic] private static FloatFormat _floatFormat;
        [ThreadStatic] private static int _floatPrecision_storage;
        [ThreadStatic] private static bool _floatPrecision_set;

        private static int _floatPrecision
        {
            get
            {
                return _floatPrecision_set ? _floatPrecision_storage : 7;
            }
            set
            {
                _floatPrecision_storage = value;
                _floatPrecision_set = true;
            }
        }

        private static readonly Dictionary<string, Func<string, Value>> _creatorMap = createCreatorMap();

        private static Dictionary<string, Func<string, Value>> createCreatorMap()
        {
            Dictionary<string, Func<string, Value>> map = new Dictionary<string, Func<string, Value>>(StringComparer.Ordinal);
            map["integer"] = TypedValue<int>.createFromString;
            map["boolean"] = TypedValue<bool>.createFromString;
            map["float"] = TypedValue<float>.createFromString;
            map["color3"] = TypedValue<Color3>.createFromString;
            map["color4"] = TypedValue<Color4>.createFromString;
            map["vector2"] = TypedValue<Vector2>.createFromString;
            map["vector3"] = TypedValue<Vector3>.createFromString;
            map["vector4"] = TypedValue<Vector4>.createFromString;
            map["matrix33"] = TypedValue<Matrix33>.createFromString;
            map["matrix44"] = TypedValue<Matrix44>.createFromString;
            map["string"] = TypedValue<string>.createFromString;
            map["integerarray"] = TypedValue<List<int>>.createFromString;
            map["booleanarray"] = TypedValue<List<bool>>.createFromString;
            map["floatarray"] = TypedValue<List<float>>.createFromString;
            map["stringarray"] = TypedValue<List<string>>.createFromString;
            return map;
        }
    }

    // TypedValue Definition
    public class TypedValue<T> : Value
    {
        // TypedValue Public Methods
        public TypedValue()
        {
            _data = default(T);
        }

        public TypedValue(T value)
        {
            _data = value;
        }

        public override Value copy()
        {
            return Value.createValue<T>(_data);
        }

        public void setData(T value)
        {
            _data = value;
        }

        public void setData(TypedValue<T> value)
        {
            _data = value._data;
        }

        public T getData()
        {
            return _data;
        }

        public override string getTypeString()
        {
            return TYPE;
        }

        public override string getValueString()
        {
            return Mx.toValueString<T>(_data);
        }

        public override bool isEqual(Value other)
        {
            if (other == null || !other.isA<T>())
            {
                return false;
            }
            return Mx.valueDataEqual<T>(_data, other.asA<T>());
        }

        public static Value createFromString(string value)
        {
            try
            {
                return Value.createValue<T>(Mx.fromValueString<T>(value));
            }
            catch (ExceptionTypeError)
            {
            }
            return null;
        }

        // TypedValue Public Members
        public static readonly string TYPE = Mx.getTypeString<T>();

        // TypedValue Private Members
        private T _data;
    }

    // AggregateValue Definition
    public class AggregateValue : Value
    {
        // AggregateValue Public Methods
        public AggregateValue(string typeName)
        {
            _typeName = typeName;
        }

        public override Value copy()
        {
            AggregateValue result = createAggregateValue(_typeName);
            foreach (Value val in _data)
            {
                result.appendValue(val.copy());
            }
            return result;
        }

        public void appendValue(Value valuePtr)
        {
            _data.Add(valuePtr);
        }

        public List<Value> getMembers()
        {
            return _data;
        }

        public Value getMemberValue(int index)
        {
            return _data[index];
        }

        public override string getTypeString()
        {
            return _typeName;
        }

        public override string getValueString()
        {
            if (_data.Count == 0)
            {
                return Mx.EMPTY_STRING;
            }

            string result = "{";
            string separator = "";
            foreach (Value val in _data)
            {
                result += separator + val.getValueString();
                separator = ";";
            }
            result += "}";

            return result;
        }

        public override bool isEqual(Value other)
        {
            if (!other.isA<AggregateValue>())
            {
                return false;
            }
            AggregateValue otherAggregate = other.asA<AggregateValue>();

            int dataSize = _data.Count;
            int otherDataSize = otherAggregate._data.Count;
            if (dataSize != otherDataSize)
            {
                return false;
            }
            for (int i = 0; i < dataSize; i++)
            {
                if (!_data[i].isEqual(otherAggregate._data[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static AggregateValue createAggregateValue(string typeName)
        {
            return new AggregateValue(typeName);
        }

        public static AggregateValue createAggregateValueFromString(string value, string type, TypeDef typeDef)
        {
            List<string> subValues = Mx.parseStructValueString(value);

            AggregateValue result = AggregateValue.createAggregateValue(type);
            List<Member> members = typeDef.getMembers();

            if (subValues.Count != members.Count)
            {
                throw new Exception("Wrong number of initializers - expect " + members.Count.ToString(CultureInfo.InvariantCulture));
            }

            Document doc = typeDef.getDocument();
            for (int i = 0; i < members.Count; ++i)
            {
                Member member = members[i];

                // This will return nullptr if the type is not a listed typedef.
                TypeDef subTypeDef = doc.getTypeDef(members[i].getType());

                // Calling Value::createValueFromStrings() here allows support for recursively nested structs.
                result.appendValue(Value.createValueFromStrings(subValues[i], member.getType(), subTypeDef));
            }

            return result;
        }

        // AggregateValue Private Members
        private readonly string _typeName;
        private readonly List<Value> _data = new List<Value>();
    }

    // ScopedFloatFormatting Definition
    public class ScopedFloatFormatting : IDisposable
    {
        // ScopedFloatFormatting Public Methods
        public ScopedFloatFormatting(Value.FloatFormat format, int precision = -1)
        {
            _format = Value.getFloatFormat();
            _precision = Value.getFloatPrecision();
            Value.setFloatFormat(format);
            if (precision >= 0)
            {
                Value.setFloatPrecision(precision);
            }
        }

        ~ScopedFloatFormatting()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // ScopedFloatFormatting Protected Methods
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // The formatting state is per thread, so it is only restored from the owning thread.
                Value.setFloatFormat(_format);
                Value.setFloatPrecision(_precision);
            }
            _disposed = true;
        }

        // ScopedFloatFormatting Private Members
        private Value.FloatFormat _format;
        private int _precision;
        private bool _disposed;
    }

    // Value String Functions
    public static partial class Mx
    {
        public static string getTypeString<T>()
        {
            if (typeof(T) == typeof(int) || typeof(T) == typeof(long))
            {
                return "integer";
            }
            if (typeof(T) == typeof(bool))
            {
                return "boolean";
            }
            if (typeof(T) == typeof(float) || typeof(T) == typeof(double))
            {
                return "float";
            }
            if (typeof(T) == typeof(Color3))
            {
                return "color3";
            }
            if (typeof(T) == typeof(Color4))
            {
                return "color4";
            }
            if (typeof(T) == typeof(Vector2))
            {
                return "vector2";
            }
            if (typeof(T) == typeof(Vector3))
            {
                return "vector3";
            }
            if (typeof(T) == typeof(Vector4))
            {
                return "vector4";
            }
            if (typeof(T) == typeof(Matrix33))
            {
                return "matrix33";
            }
            if (typeof(T) == typeof(Matrix44))
            {
                return "matrix44";
            }
            if (typeof(T) == typeof(string))
            {
                return "string";
            }
            if (typeof(T) == typeof(List<int>))
            {
                return "integerarray";
            }
            if (typeof(T) == typeof(List<bool>))
            {
                return "booleanarray";
            }
            if (typeof(T) == typeof(List<float>))
            {
                return "floatarray";
            }
            if (typeof(T) == typeof(List<string>))
            {
                return "stringarray";
            }
            throw new ExceptionTypeError("Unsupported value type: " + typeof(T).Name);
        }

        public static string toValueString<T>(T data)
        {
            object boxed = data;
            switch (boxed)
            {
                case string value:
                    return toValueString(value);
                case bool value:
                    return toValueString(value);
                case int value:
                    return toValueString(value);
                case long value:
                    return toValueString(value);
                case float value:
                    return toValueString(value);
                case double value:
                    return toValueString(value);
                case Color3 value:
                    return toValueString(value);
                case Color4 value:
                    return toValueString(value);
                case Vector2 value:
                    return toValueString(value);
                case Vector3 value:
                    return toValueString(value);
                case Vector4 value:
                    return toValueString(value);
                case Matrix33 value:
                    return toValueString(value);
                case Matrix44 value:
                    return toValueString(value);
                case List<int> value:
                    return toValueString(value);
                case List<bool> value:
                    return toValueString(value);
                case List<float> value:
                    return toValueString(value);
                case List<string> value:
                    return toValueString(value);
                case null:
                    return EMPTY_STRING;
            }
            throw new ExceptionTypeError("Unsupported value type: " + typeof(T).Name);
        }

        public static string toValueString(string data)
        {
            return data;
        }

        public static string toValueString(bool data)
        {
            return data ? VALUE_STRING_TRUE : VALUE_STRING_FALSE;
        }

        public static string toValueString(int data)
        {
            return data.ToString(CultureInfo.InvariantCulture);
        }

        public static string toValueString(long data)
        {
            return data.ToString(CultureInfo.InvariantCulture);
        }

        public static string toValueString(float data)
        {
            return formatFloating((double)data);
        }

        public static string toValueString(double data)
        {
            return formatFloating(data);
        }

        public static string toValueString(Vector2 data)
        {
            return toValueString(data.x) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.y);
        }

        public static string toValueString(Vector3 data)
        {
            return toValueString(data.x) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.y) + ARRAY_PREFERRED_SEPARATOR +
                   toValueString(data.z);
        }

        public static string toValueString(Vector4 data)
        {
            return toValueString(data.x) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.y) + ARRAY_PREFERRED_SEPARATOR +
                   toValueString(data.z) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.w);
        }

        public static string toValueString(Color3 data)
        {
            return toValueString(data.r) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.g) + ARRAY_PREFERRED_SEPARATOR +
                   toValueString(data.b);
        }

        public static string toValueString(Color4 data)
        {
            return toValueString(data.r) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.g) + ARRAY_PREFERRED_SEPARATOR +
                   toValueString(data.b) + ARRAY_PREFERRED_SEPARATOR + toValueString(data.a);
        }

        public static string toValueString(Matrix33 data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    str.Append(toValueString(data[i, j]));
                    if (i + 1 < 3 || j + 1 < 3)
                    {
                        str.Append(ARRAY_PREFERRED_SEPARATOR);
                    }
                }
            }
            return str.ToString();
        }

        public static string toValueString(Matrix44 data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 4; j++)
                {
                    str.Append(toValueString(data[i, j]));
                    if (i + 1 < 4 || j + 1 < 4)
                    {
                        str.Append(ARRAY_PREFERRED_SEPARATOR);
                    }
                }
            }
            return str.ToString();
        }

        public static string toValueString(List<int> data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                str.Append(toValueString(data[i]));
                if (i + 1 < data.Count)
                {
                    str.Append(ARRAY_PREFERRED_SEPARATOR);
                }
            }
            return str.ToString();
        }

        public static string toValueString(List<bool> data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                str.Append(toValueString(data[i]));
                if (i + 1 < data.Count)
                {
                    str.Append(ARRAY_PREFERRED_SEPARATOR);
                }
            }
            return str.ToString();
        }

        public static string toValueString(List<float> data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                str.Append(toValueString(data[i]));
                if (i + 1 < data.Count)
                {
                    str.Append(ARRAY_PREFERRED_SEPARATOR);
                }
            }
            return str.ToString();
        }

        public static string toValueString(List<string> data)
        {
            StringBuilder str = new StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                str.Append(toValueString(data[i]));
                if (i + 1 < data.Count)
                {
                    str.Append(ARRAY_PREFERRED_SEPARATOR);
                }
            }
            return str.ToString();
        }

        public static T fromValueString<T>(string value)
        {
            if (typeof(T) == typeof(string))
            {
                return (T)(object)value;
            }
            if (typeof(T) == typeof(bool))
            {
                return (T)(object)boolFromValueString(value);
            }
            if (typeof(T) == typeof(int))
            {
                return (T)(object)intFromValueString(value);
            }
            if (typeof(T) == typeof(long))
            {
                return (T)(object)longFromValueString(value);
            }
            if (typeof(T) == typeof(float))
            {
                return (T)(object)floatFromValueString(value);
            }
            if (typeof(T) == typeof(double))
            {
                return (T)(object)doubleFromValueString(value);
            }
            if (typeof(T) == typeof(Vector2))
            {
                return (T)(object)new Vector2(vectorFromValueString(value, 2));
            }
            if (typeof(T) == typeof(Vector3))
            {
                return (T)(object)new Vector3(vectorFromValueString(value, 3));
            }
            if (typeof(T) == typeof(Vector4))
            {
                return (T)(object)new Vector4(vectorFromValueString(value, 4));
            }
            if (typeof(T) == typeof(Color3))
            {
                return (T)(object)new Color3(vectorFromValueString(value, 3));
            }
            if (typeof(T) == typeof(Color4))
            {
                return (T)(object)new Color4(vectorFromValueString(value, 4));
            }
            if (typeof(T) == typeof(Matrix33))
            {
                return (T)(object)new Matrix33(matrixFromValueString(value, 3));
            }
            if (typeof(T) == typeof(Matrix44))
            {
                return (T)(object)new Matrix44(matrixFromValueString(value, 4));
            }
            if (typeof(T) == typeof(List<int>))
            {
                List<int> data = new List<int>();
                foreach (string token in splitString(value, ","))
                {
                    data.Add(intFromValueString(trimSpaces(token)));
                }
                return (T)(object)data;
            }
            if (typeof(T) == typeof(List<bool>))
            {
                List<bool> data = new List<bool>();
                foreach (string token in splitString(value, ","))
                {
                    data.Add(boolFromValueString(trimSpaces(token)));
                }
                return (T)(object)data;
            }
            if (typeof(T) == typeof(List<float>))
            {
                List<float> data = new List<float>();
                foreach (string token in splitString(value, ","))
                {
                    data.Add(floatFromValueString(trimSpaces(token)));
                }
                return (T)(object)data;
            }
            if (typeof(T) == typeof(List<string>))
            {
                List<string> data = new List<string>();
                foreach (string token in splitString(value, ","))
                {
                    data.Add(trimSpaces(token));
                }
                return (T)(object)data;
            }
            throw new ExceptionTypeError("Unsupported value type: " + typeof(T).Name);
        }

        // Equality of the data held by a TypedValue (operator== in the C++).
        public static bool valueDataEqual<T>(T a, T b)
        {
            if (a is List<int> intsA && b is List<int> intsB)
            {
                return System.Linq.Enumerable.SequenceEqual(intsA, intsB);
            }
            if (a is List<bool> boolsA && b is List<bool> boolsB)
            {
                return System.Linq.Enumerable.SequenceEqual(boolsA, boolsB);
            }
            if (a is List<float> floatsA && b is List<float> floatsB)
            {
                return System.Linq.Enumerable.SequenceEqual(floatsA, floatsB);
            }
            if (a is List<string> stringsA && b is List<string> stringsB)
            {
                return System.Linq.Enumerable.SequenceEqual(stringsA, stringsB);
            }
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        public static List<string> parseStructValueString(string value)
        {
            const char SEPARATOR = ';';
            const char OPEN_BRACE = '{';
            const char CLOSE_BRACE = '}';

            if (value.Length == 0)
            {
                return new List<string>();
            }

            // Validate the string is correctly formatted - must be at least 2 characters long and start and end with braces
            if (value.Length < 2 || (value[0] != OPEN_BRACE || value[value.Length - 1] != CLOSE_BRACE))
            {
                return new List<string>();
            }

            List<string> split = new List<string>();

            // Strip off the surrounding braces
            string substring = value.Substring(1, value.Length - 2);

            // Sequentially examine each character to parse the list initializer.
            StringBuilder part = new StringBuilder();
            int braceDepth = 0;
            foreach (char c in substring)
            {
                if (c == OPEN_BRACE)
                {
                    // We've already trimmed the starting brace, so any additional braces indicate members that are themselves list initializers.
                    // We will just return this as a string of the list initializer.
                    braceDepth += 1;
                }
                if (braceDepth > 0 && c == CLOSE_BRACE)
                {
                    braceDepth -= 1;
                }

                if (braceDepth == 0 && c == SEPARATOR)
                {
                    // When we hit a separator we store the currently accumulated part, and clear to start collecting the next.
                    split.Add(part.ToString());
                    part.Clear();
                }
                else
                {
                    part.Append(c);
                }
            }

            if (part.Length != 0)
            {
                split.Add(part.ToString());
            }

            return split;
        }

        // Value String Private Functions
        private static bool boolFromValueString(string value)
        {
            if (value == VALUE_STRING_TRUE)
            {
                return true;
            }
            if (value == VALUE_STRING_FALSE)
            {
                return false;
            }
            throw new ExceptionTypeError("Type mismatch in boolean fromValueString: " + value);
        }

        private static float[] vectorFromValueString(string value, int numElements)
        {
            List<string> tokens = splitString(value, ARRAY_VALID_SEPARATORS);
            if (tokens.Count != numElements)
            {
                throw new ExceptionTypeError("Type mismatch in vector fromValueString: " + value);
            }
            float[] data = new float[numElements];
            for (int i = 0; i < numElements; i++)
            {
                data[i] = floatFromValueString(tokens[i]);
            }
            return data;
        }

        private static float[] matrixFromValueString(string value, int numRows)
        {
            List<string> tokens = splitString(value, ARRAY_VALID_SEPARATORS);
            if (tokens.Count != numRows * numRows)
            {
                throw new ExceptionTypeError("Type mismatch in matrix fromValueString: " + value);
            }
            float[] data = new float[numRows * numRows];
            for (int i = 0; i < numRows; i++)
            {
                for (int j = 0; j < numRows; j++)
                {
                    data[i * numRows + j] = floatFromValueString(tokens[i * numRows + j]);
                }
            }
            return data;
        }

        // Extraction of an integer as std::istream does it in the classic locale:
        // leading white space is skipped and the longest valid prefix is used.
        private static bool extractInteger(string value, out long result)
        {
            result = 0;
            int pos = 0;
            while (pos < value.Length && isStreamSpace(value[pos]))
            {
                pos++;
            }
            bool negative = false;
            if (pos < value.Length && (value[pos] == '+' || value[pos] == '-'))
            {
                negative = value[pos] == '-';
                pos++;
            }
            int digits = 0;
            decimal accumulated = 0;
            while (pos < value.Length && isdigit(value[pos]))
            {
                if (accumulated < 1e20m)
                {
                    accumulated = accumulated * 10 + (value[pos] - '0');
                }
                digits++;
                pos++;
            }
            if (digits == 0)
            {
                return false;
            }
            if (negative)
            {
                accumulated = -accumulated;
            }
            if (accumulated > long.MaxValue || accumulated < long.MinValue)
            {
                return false;
            }
            result = (long)accumulated;
            return true;
        }

        private static bool isStreamSpace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\v' || c == '\f' || c == '\r';
        }

        private static int intFromValueString(string value)
        {
            long result;
            if (!extractInteger(value, out result) || result > int.MaxValue || result < int.MinValue)
            {
                throw new ExceptionTypeError("Type mismatch in generic fromValueString: " + value);
            }
            return (int)result;
        }

        private static long longFromValueString(string value)
        {
            long result;
            if (!extractInteger(value, out result))
            {
                throw new ExceptionTypeError("Type mismatch in generic fromValueString: " + value);
            }
            return result;
        }

        // Extraction of a floating-point number as std::istream does it in the
        // classic locale: the characters that can belong to a number are gathered
        // and the gathered text must then be a complete number.
        private static bool extractFloating(string value, out double result)
        {
            result = 0.0;
            int pos = 0;
            while (pos < value.Length && isStreamSpace(value[pos]))
            {
                pos++;
            }
            int start = pos;
            if (pos < value.Length && (value[pos] == '+' || value[pos] == '-'))
            {
                pos++;
            }
            int digits = 0;
            bool seenPoint = false;
            bool seenExponent = false;
            while (pos < value.Length)
            {
                char c = value[pos];
                if (isdigit(c))
                {
                    digits++;
                    pos++;
                }
                else if (c == '.' && !seenPoint && !seenExponent)
                {
                    seenPoint = true;
                    pos++;
                }
                else if ((c == 'e' || c == 'E') && !seenExponent && digits > 0)
                {
                    seenExponent = true;
                    pos++;
                    if (pos < value.Length && (value[pos] == '+' || value[pos] == '-'))
                    {
                        pos++;
                    }
                }
                else
                {
                    break;
                }
            }
            if (digits == 0)
            {
                return false;
            }
            string text = value.Substring(start, pos - start);
            if (text[0] == '+')
            {
                text = text.Substring(1);
            }
            return double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
                                   CultureInfo.InvariantCulture, out result);
        }

        private static float floatFromValueString(string value)
        {
            double result;
            if (!extractFloating(value, out result))
            {
                throw new ExceptionTypeError("Type mismatch in generic fromValueString: " + value);
            }
            float single = float.Parse(result.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            if (float.IsInfinity(single))
            {
                throw new ExceptionTypeError("Type mismatch in generic fromValueString: " + value);
            }
            return single;
        }

        private static double doubleFromValueString(string value)
        {
            double result;
            if (!extractFloating(value, out result) || double.IsInfinity(result))
            {
                throw new ExceptionTypeError("Type mismatch in generic fromValueString: " + value);
            }
            return result;
        }

        // A floating-point number written as std::ostream writes it in the classic
        // locale with the current float format and precision.
        private static string formatFloating(double v)
        {
            Value.FloatFormat fmt = Value.getFloatFormat();
            int prec = Value.getFloatPrecision();
            if (double.IsNaN(v))
            {
                return BitConverter.DoubleToInt64Bits(v) < 0 ? "-nan" : "nan";
            }
            if (double.IsInfinity(v))
            {
                return v < 0 ? "-inf" : "inf";
            }
            if (fmt == Value.FloatFormat.FloatFormatFixed)
            {
                return formatFixed(v, prec);
            }
            if (fmt == Value.FloatFormat.FloatFormatScientific)
            {
                return formatScientific(v, prec);
            }
            return formatGeneral(v, prec);
        }

        private static string formatFixed(double v, int prec)
        {
            string result = Math.Abs(v).ToString("F" + prec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return (v < 0 || (v == 0.0 && 1.0 / v < 0) ? "-" : "") + result;
        }

        private static string twoDigitExponent(int x)
        {
            int magnitude = Math.Abs(x);
            return "e" + (x < 0 ? "-" : "+") + (magnitude < 10 ? "0" : "") + magnitude.ToString(CultureInfo.InvariantCulture);
        }

        private static string formatScientific(double v, int prec)
        {
            string es = Math.Abs(v).ToString("e" + prec.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int epos = es.IndexOf('e');
            int x = int.Parse(es.Substring(epos + 1), CultureInfo.InvariantCulture);
            return (v < 0 || (v == 0.0 && 1.0 / v < 0) ? "-" : "") + es.Substring(0, epos) + twoDigitExponent(x);
        }

        private static string formatGeneral(double v, int prec)
        {
            if (prec == 0)
            {
                prec = 1;
            }
            if (v == 0.0)
            {
                return 1.0 / v < 0 ? "-0" : "0";
            }
            string es = v.ToString("e" + (prec - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            int epos = es.IndexOf('e');
            int x = int.Parse(es.Substring(epos + 1), CultureInfo.InvariantCulture);
            string result;
            if (x < -4 || x >= prec)
            {
                string mant = es.Substring(0, epos);
                if (mant.Contains("."))
                {
                    mant = mant.TrimEnd('0').TrimEnd('.');
                }
                result = mant + twoDigitExponent(x);
            }
            else
            {
                result = v.ToString("F" + Math.Max(0, prec - 1 - x).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                if (result.Contains("."))
                {
                    result = result.TrimEnd('0').TrimEnd('.');
                }
            }
            return result;
        }
    }
}
