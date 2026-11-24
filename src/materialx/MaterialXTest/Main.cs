// (not in MaterialX) Main.cs: the test host for the MaterialX port
//   dotnet run -c Release --project src/materialx/MaterialXTest -- [--gtest_filter=Core.*] [--gtest_list_tests]

using System;
using System.Reflection;
using testing;

public static class MaterialXTestProgram
{
    public static int Main(string[] argv)
    {
        string filter = null;
        bool list = false;
        foreach (string argument in argv)
        {
            if (argument.StartsWith("--gtest_filter="))
            {
                filter = argument.Substring("--gtest_filter=".Length);
            }
            else if (argument == "--gtest_list_tests")
            {
                list = true;
            }
        }
        return Gtest.RunAllTests(Assembly.GetExecutingAssembly(), filter, list);
    }
}

// gtest.cs refers to pbrt.FatalException for death tests; these tests have none
namespace pbrt
{
    public class FatalException : Exception
    {
        public FatalException(string message) : base(message)
        {
        }
    }
}
