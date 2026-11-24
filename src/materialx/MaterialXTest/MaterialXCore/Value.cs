//
// Copyright Contributors to the MaterialX Project
// SPDX-License-Identifier: Apache-2.0
//

using System;
using System.Collections.Generic;
using System.Text;
using MaterialX;
using testing;
using static MaterialX.Mx;
using static MaterialXTest.Catch;

public static class CoreValueTests
{
    // The value-initialized instance of a type: T v0{}
    private static T valueInitialized<T>()
    {
        if (typeof(T) == typeof(string))
        {
            return (T)(object)EMPTY_STRING;
        }
        if (typeof(T).IsClass)
        {
            return Activator.CreateInstance<T>();
        }
        return default(T);
    }

    public static void testTypedValue<T>(T v1, T v2)
    {
        T v0 = valueInitialized<T>();

        // Constructor and assignment
        Value value0 = Value.createValue(v0);
        Value value1 = Value.createValue(v1);
        Value value2 = Value.createValue(v2);
        REQUIRE(value0.isA<T>());
        REQUIRE(value1.isA<T>());
        REQUIRE(value2.isA<T>());
        REQUIRE(valueDataEqual(value0.asA<T>(), v0));
        REQUIRE(valueDataEqual(value1.asA<T>(), v1));
        REQUIRE(valueDataEqual(value2.asA<T>(), v2));

        // Equality and inequality
        REQUIRE(valueDataEqual(value0.copy().asA<T>(), value0.asA<T>()));
        REQUIRE(valueDataEqual(value1.copy().asA<T>(), value1.asA<T>()));
        REQUIRE(valueDataEqual(value2.copy().asA<T>(), value2.asA<T>()));
        REQUIRE(!valueDataEqual(value1.asA<T>(), value2.asA<T>()));

        // Serialization and deserialization
        Value newValue0 = TypedValue<T>.createFromString(value0.getValueString());
        Value newValue1 = TypedValue<T>.createFromString(value1.getValueString());
        Value newValue2 = TypedValue<T>.createFromString(value2.getValueString());
        REQUIRE(valueDataEqual(newValue0.asA<T>(), v0));
        REQUIRE(valueDataEqual(newValue1.asA<T>(), v1));
        REQUIRE(valueDataEqual(newValue2.asA<T>(), v2));
    }

    [TEST("Core", "ValueStrings")]
    public static void Core_ValueStrings()
    {
        // Convert from data values to value strings.
        REQUIRE(toValueString(1) == "1");
        REQUIRE(toValueString(1.0f) == "1");
        REQUIRE(toValueString(true) == "true");
        REQUIRE(toValueString(false) == "false");
        REQUIRE(toValueString(new Color3(1.0f)) == "1, 1, 1");
        REQUIRE(toValueString("text") == "text");

        // Convert from floats to value strings with custom formatting.
        {
            using ScopedFloatFormatting fmt = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatFixed, 3);
            REQUIRE(toValueString(0.1234f) == "0.123");
            REQUIRE(toValueString(new Color3(1.0f)) == "1.000, 1.000, 1.000");
        }
        {
            using ScopedFloatFormatting fmt = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatScientific, 2);
            REQUIRE(toValueString(0.1234f) == "1.23e-01");
        }
        {
            using ScopedFloatFormatting fmt = new ScopedFloatFormatting(Value.FloatFormat.FloatFormatDefault, 2);
            REQUIRE(toValueString(0.1234f) == "0.12");
        }

        // Convert from value strings to data values.
        REQUIRE(fromValueString<int>("1") == 1);
        REQUIRE(fromValueString<float>("1") == 1.0f);
        REQUIRE(fromValueString<bool>("true") == true);
        REQUIRE(fromValueString<bool>("false") == false);
        REQUIRE(fromValueString<Color3>("1, 1, 1") == new Color3(1.0f));
        REQUIRE(fromValueString<string>("text") == "text");

        // Verify that invalid conversions throw exceptions.
        REQUIRE_THROWS_AS<ExceptionTypeError>(() => { fromValueString<int>("text"); });
        REQUIRE_THROWS_AS<ExceptionTypeError>(() => { fromValueString<float>("text"); });
        REQUIRE_THROWS_AS<ExceptionTypeError>(() => { fromValueString<bool>("1"); });
        REQUIRE_THROWS_AS<ExceptionTypeError>(() => { fromValueString<Color3>("1"); });

        // Parse value strings using structure syntax features.
        REQUIRE(vectorEqual(parseStructValueString("{{1;2;3};4}"), new List<string> { "{1;2;3}","4" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;2;3;4}"), new List<string> { "1","2","3","4" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;{2;3};4}"), new List<string> { "1","{2;3}","4" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;{2;3;4}}"), new List<string> { "1","{2;3;4}" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;{2;{3;4}}}"), new List<string> { "1","{2;{3;4}}" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;2;{3};4}"), new List<string> { "1","2","{3}","4" }));
        REQUIRE(vectorEqual(parseStructValueString("{1;2;{3};4}"), new List<string> { "1","2","{3}","4" }));
    }

    [TEST("Core", "TypedValues")]
    public static void Core_TypedValues()
    {
        // Base types
        testTypedValue<int>(1, 2);
        testTypedValue<bool>(false, true);
        testTypedValue<float>(1.0f, 2.0f);
        testTypedValue(new Color3(0.1f, 0.2f, 0.3f),
                       new Color3(0.5f, 0.6f, 0.7f));
        testTypedValue(new Color4(0.1f, 0.2f, 0.3f, 0.4f),
                       new Color4(0.5f, 0.6f, 0.7f, 0.8f));
        testTypedValue(new Vector2(1.0f, 2.0f),
                       new Vector2(1.5f, 2.5f));
        testTypedValue(new Vector3(1.0f, 2.0f, 3.0f),
                       new Vector3(1.5f, 2.5f, 3.5f));
        testTypedValue(new Vector4(1.0f, 2.0f, 3.0f, 4.0f),
                       new Vector4(1.5f, 2.5f, 3.5f, 4.5f));
        testTypedValue(new Matrix33(0.0f),
                       new Matrix33(1.0f));
        testTypedValue(new Matrix44(0.0f),
                       new Matrix44(1.0f));
        testTypedValue("first_value",
                       "second_value");

        // Array types
        testTypedValue(new List<int> { 1, 2, 3 },
                       new List<int> { 4, 5, 6 });
        testTypedValue(new List<bool> { false, false, false },
                       new List<bool> { true, true, true });
        testTypedValue(new List<float> { 1.0f, 2.0f, 3.0f },
                       new List<float> { 4.0f, 5.0f, 6.0f });
        testTypedValue(new List<string> { "Item A", "Item B", "Item C" },
                       new List<string> { "Item D", "Item E", "Item F" });

        // Alias types
        testTypedValue<long>(1L, 2L);
        testTypedValue<double>(1.0, 2.0);

        // Construct a string value from a string literal
        Value value = Value.createValue("text");
        REQUIRE(value.isA<string>());
        REQUIRE(value.asA<string>() == "text");
    }
}
