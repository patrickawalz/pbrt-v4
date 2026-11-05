// OSL port: the lookup of the shadeops by name. The C++ backend declares every function of builtindecl.h in the LLVM module and resolves the names to the
// addresses of the compiled osl_* functions (initialize_llvm_helper_function_map); here the names are resolved by reflection over the static methods
// whose name starts with "osl_" (OslRuntime and the other classes of the library), once.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace OSL.pvt
{
    public static class ILRuntimeTable
    {
        private static Dictionary<string, MethodInfo> s_methods;
        private static readonly object s_lock = new object();
        private static SortedSet<string> s_missing = new SortedSet<string>();

        private static void build()
        {
            Dictionary<string, MethodInfo> m = new Dictionary<string, MethodInfo>();
            foreach (Type t in typeof(ILRuntimeTable).Assembly.GetTypes())
            {
                foreach (MethodInfo mi in t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (mi.Name.StartsWith("osl_") && !m.ContainsKey(mi.Name))
                    {
                        m[mi.Name] = mi;
                    }
                }
            }
            s_methods = m;
        }

        public static MethodInfo find(string name)
        {
            lock (s_lock)
            {
                if (s_methods == null)
                {
                    build();
                }
                MethodInfo mi;
                return s_methods.TryGetValue(name, out mi) ? mi : null;
            }
        }

        public static void note_missing(string name)
        {
            lock (s_lock)
            {
                s_missing.Add(name);
            }
        }

        /// The names of the declared shadeops (builtindecl.h) that have no managed implementation, and those that were called by compiled code
        public static string report()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# shadeops declared in builtindecl.h without a managed implementation (osl_* static method)\n");
            foreach (string n in BuiltinDecl.names())
            {
                if (find(n) == null)
                {
                    sb.Append(n).Append(' ').Append(BuiltinDecl.signature(n)).Append('\n');
                }
            }
            sb.Append("# referenced by generated code and missing\n");
            lock (s_lock)
            {
                foreach (string n in s_missing)
                {
                    sb.Append(n).Append('\n');
                }
            }
            return sb.ToString();
        }

        public static string[] missing_called()
        {
            lock (s_lock)
            {
                string[] r = new string[s_missing.Count];
                s_missing.CopyTo(r);
                return r;
            }
        }

        /// The body of the call that replaces a shadeop that has no managed implementation
        public static void missing_function(string name)
        {
            throw new NotImplementedException("shadeop " + name + " is not implemented in the managed runtime");
        }

        /// Called by a persisted shader assembly to intern the strings its code uses as hashes
        public static void register_string(string s)
        {
            OIIO.ustring u = new OIIO.ustring(s);
        }
    }
}
