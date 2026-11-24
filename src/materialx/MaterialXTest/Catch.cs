// (not in MaterialX) Catch.cs: the Catch2 assertions used by MaterialXTest,
// expressed with the gtest port in src/ext/gtest so the test bodies keep the
// form they have upstream.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using testing;

namespace MaterialXTest
{
    public static class Catch
    {
        public static void REQUIRE(bool condition, [CallerArgumentExpression("condition")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Gtest.ASSERT_TRUE(condition, expression, file, line);
        }

        public static void REQUIRE_FALSE(bool condition, [CallerArgumentExpression("condition")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Gtest.ASSERT_FALSE(condition, expression, file, line);
        }

        public static void CHECK(bool condition, [CallerArgumentExpression("condition")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            Gtest.EXPECT_TRUE(condition, expression, file, line);
        }

        public static void REQUIRE_THROWS_AS<T>(Action statement, [CallerArgumentExpression("statement")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) where T : System.Exception
        {
            bool thrown = false;
            try
            {
                statement();
            }
            catch (T)
            {
                thrown = true;
            }
            if (!thrown)
            {
                Gtest.FAIL("Expected: " + expression + " throws " + typeof(T).Name, file, line);
            }
        }

        public static void CHECK_THROWS_AS<T>(Action statement, [CallerArgumentExpression("statement")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0) where T : System.Exception
        {
            bool thrown = false;
            try
            {
                statement();
            }
            catch (T)
            {
                thrown = true;
            }
            if (!thrown)
            {
                Gtest.ADD_FAILURE("Expected: " + expression + " throws " + typeof(T).Name, file, line);
            }
        }

        public static void REQUIRE_NOTHROW(Action statement, [CallerArgumentExpression("statement")] string expression = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
        {
            try
            {
                statement();
            }
            catch (System.Exception e)
            {
                Gtest.FAIL("Expected: " + expression + " does not throw, but it threw " + e.GetType().Name + ": " + e.Message, file, line);
            }
        }

        // TEST_CASE and SECTION run a test body as Catch2 does: the body is run
        // from its start once for every leaf section, so that each section
        // sees the state its enclosing sections set up and nothing else.
        public static void TEST_CASE(Action body)
        {
            _completedSections = new HashSet<string>();
            do
            {
                _moreSections = false;
                _sectionPath = new List<string>();
                _sectionEntered = new List<bool> { false };
                _sectionPending = new List<bool> { false };
                body();
            }
            while (_moreSections);
        }

        public static void SECTION(string name, Action body)
        {
            string path = string.Join("/", _sectionPath) + "/" + name;
            if (_completedSections.Contains(path))
            {
                return;
            }
            int level = _sectionEntered.Count - 1;
            if (_sectionEntered[level])
            {
                // A sibling section has run in this pass; this one waits for the next.
                _moreSections = true;
                _sectionPending[_sectionPending.Count - 1] = true;
                return;
            }
            _sectionEntered[level] = true;
            _sectionPath.Add(name);
            _sectionEntered.Add(false);
            _sectionPending.Add(false);
            try
            {
                body();
            }
            finally
            {
                bool pending = _sectionPending[_sectionPending.Count - 1];
                _sectionPending.RemoveAt(_sectionPending.Count - 1);
                _sectionEntered.RemoveAt(_sectionEntered.Count - 1);
                _sectionPath.RemoveAt(_sectionPath.Count - 1);
                if (!pending)
                {
                    _completedSections.Add(path);
                }
                else
                {
                    _sectionPending[_sectionPending.Count - 1] = true;
                }
            }
        }

        [ThreadStatic] private static HashSet<string> _completedSections;
        [ThreadStatic] private static List<string> _sectionPath;
        [ThreadStatic] private static List<bool> _sectionEntered;
        [ThreadStatic] private static List<bool> _sectionPending;
        [ThreadStatic] private static bool _moreSections;

        // The equivalent of comparing two vectors with operator==.
        public static bool vectorEqual<T>(List<T> a, List<T> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }
            for (int i = 0; i < a.Count; i++)
            {
                if (!EqualityComparer<T>.Default.Equals(a[i], b[i]))
                {
                    return false;
                }
            }
            return true;
        }

        public static void INFO(string message)
        {
            Console.Out.Write(message + "\n");
        }

        public static void WARN(string message)
        {
            Console.Out.Write("warning: " + message + "\n");
        }
    }
}
