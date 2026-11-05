// (not in OpenShadingLanguage) osl_test.cs: a test host that runs only the OSL tests (the *_test.cs files under src/osl) without building the renderer
//   dotnet run -c Release --project src/osl/osl_test -- [--gtest_filter=Fmt.*] [--gtest_list_tests]
using System;
using System.Reflection;
using testing;

public static class OslTestProgram
{
    public static int Main(string[] argv)
    {
        string filter = null;
        bool list = false;
        foreach (string a in argv)
        {
            if (a.StartsWith("--gtest_filter="))
            {
                filter = a.Substring("--gtest_filter=".Length);
            }
            else if (a == "--gtest_list_tests")
            {
                list = true;
            }
        }
        return Gtest.RunAllTests(Assembly.GetExecutingAssembly(), filter, list);
    }
}

// gtest.cs refers to pbrt.FatalException for death tests; the OSL tests have none
namespace pbrt
{
    public class FatalException : Exception
    {
        public FatalException(string message) : base(message)
        {
        }
    }
}
