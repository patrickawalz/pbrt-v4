// OSL port: src/oslinfo/oslinfo.cpp
//
// oslinfo -- list parameters of a compiled OSL shader. Based somewhat on code released by NVIDIA as part of Gelato (gsoinfo.cpp), BSD-3-Clause.
// The option parsing replaces OIIO::ArgParse (-v, --runstats, -p SEARCHPATH, --param NAME and the file names); the numbers are printed the way
// std::ostream does by default (%g with six significant digits).

using System;
using System.Collections.Generic;
using System.Text;
using OIIO;
using OSL;

namespace OSL.OslInfo
{
    public static class OslInfoProgram
    {
        private static string searchpath = "";
        private static bool verbose = false;
        private static bool runstats = false;
        private static string oneparam = "";
        private static List<string> filenames = new List<string>();
        private static StringBuilder output = new StringBuilder();

        private static string num(float f)
        {
            return Fmt.format_g(f, 6, false, false);
        }

        // Print the default values for a parameter built out of strings.
        private static void print_default_string_vals(OSLQuery.Parameter p, bool verbose)
        {
            int ne;
            if (p.varlenarray || p.type.arraylen < 0)
            {
                ne = p.sdefault.Count;
            }
            else
            {
                ne = p.type.numelements();
            }
            if (verbose)
            {
                for (int a = 0; a < ne; ++a)
                {
                    output.Append("\t\tDefault value: \"").Append(Strutil.escape_chars(p.sdefault[a].@string())).Append("\"\n");
                }
            }
            else
            {
                for (int a = 0; a < ne; ++a)
                {
                    output.Append("\"").Append(Strutil.escape_chars(p.sdefault[a].@string())).Append("\" ");
                }
                output.Append("\n");
            }
        }

        // Print the default values for a parameter built out of integers (including arrays thereof).
        private static void print_default_int_vals(OSLQuery.Parameter p, bool verbose)
        {
            int nf = p.type.aggregate;
            int ne;
            if (p.varlenarray || p.type.arraylen < 0)
            {
                ne = p.idefault.Count / nf;
            }
            else
            {
                ne = p.type.numelements();
            }
            if (verbose)
            {
                output.Append("\t\tDefault value:");
            }
            if (p.type.arraylen != 0 || nf > 1)
            {
                output.Append(" [");
            }
            for (int a = 0; a < ne; ++a)
            {
                for (int f = 0; f < nf; ++f)
                {
                    output.Append(' ').Append(p.idefault[a * nf + f]);
                }
            }
            if (p.type.arraylen != 0 || nf > 1)
            {
                output.Append(" ]");
            }
            output.Append('\n');
        }

        // Print the default values for a parameter built out of floats (including color, point, etc., or arrays thereof).
        private static void print_default_float_vals(OSLQuery.Parameter p, bool verbose)
        {
            int nf = p.type.aggregate;
            int ne;
            if (p.varlenarray || p.type.arraylen < 0)
            {
                ne = p.fdefault.Count / nf;
            }
            else
            {
                ne = p.type.numelements();
            }
            if (verbose)
            {
                output.Append("\t\tDefault value:");
            }
            if (p.type.arraylen != 0 || nf > 1)
            {
                output.Append(" [");
            }
            for (int a = 0; a < ne; ++a)
            {
                if (verbose && p.spacename.Count > a && !p.spacename[a].empty())
                {
                    output.Append(" \"").Append(p.spacename[a].@string()).Append("\"");
                }
                for (int f = 0; f < nf; ++f)
                {
                    output.Append(' ').Append(num(p.fdefault[a * nf + f]));
                }
            }
            if (p.type.arraylen != 0 || nf > 1)
            {
                output.Append(" ]");
            }
            output.Append('\n');
        }

        // Print all the metadata for a parameter.
        private static void print_metadata(OSLQuery.Parameter m)
        {
            string typestring = m.type.c_str();
            output.Append("\t\tmetadata: ").Append(typestring).Append(' ').Append(m.name.@string()).Append(" =");
            for (int d = 0; d < m.idefault.Count; ++d)
            {
                output.Append(" ").Append(m.idefault[d]);
            }
            for (int d = 0; d < m.fdefault.Count; ++d)
            {
                output.Append(" ").Append(num(m.fdefault[d]));
            }
            for (int d = 0; d < m.sdefault.Count; ++d)
            {
                output.Append(" \"").Append(Strutil.escape_chars(m.sdefault[d].@string())).Append("\"");
            }
            output.Append('\n');
        }

        private static void oslinfo(string name)
        {
            Timer t = new Timer(runstats ? Timer.StartNowVal.StartNow : Timer.StartNowVal.DontStartNow);
            OSLQuery g = new OSLQuery();
            g.open(name, searchpath);
            string e = g.geterror();
            if (e.Length != 0)
            {
                output.Append("ERROR opening shader \"").Append(name).Append("\" (").Append(e).Append(")\n");
                return;
            }
            if (runstats)
            {
                // display timings in an easy to sort form
                output.Append(num((float)t.stop())).Append(" sec for ").Append(name).Append("\n");
                return;
            }

            if (oneparam.Length == 0)
            {
                output.Append(g.shadertype().@string()).Append(" \"").Append(g.shadername().@string()).Append("\"\n");
                if (verbose)
                {
                    for (int m = 0; m < g.metadata().Count; ++m)
                    {
                        print_metadata(g.metadata()[m]);
                    }
                }
            }

            for (int i = 0; i < g.nparams(); ++i)
            {
                OSLQuery.Parameter p = g.getparam(i);
                if (p == null)
                {
                    break;
                }
                if (oneparam.Length != 0 && oneparam != p.name.@string())
                {
                    continue;
                }
                string typestring;
                if (p.isstruct)
                {
                    typestring = "struct " + p.structname.@string();
                }
                else
                {
                    typestring = p.type.c_str();
                }
                if (verbose)
                {
                    output.Append("    \"").Append(p.name.@string()).Append("\" \"").Append(p.isoutput ? "output " : "").Append(typestring).Append("\"\n");
                }
                else
                {
                    output.Append(p.isoutput ? "output " : "").Append(typestring).Append(' ').Append(p.name.@string()).Append(' ');
                }
                if (p.isstruct)
                {
                    if (verbose)
                    {
                        output.Append("\t\t");
                    }
                    output.Append("fields: {");
                    for (int f = 0; f < p.fields.Count; ++f)
                    {
                        if (f != 0)
                        {
                            output.Append(", ");
                        }
                        string fieldname = p.name.@string() + '.' + p.fields[f].@string();
                        OSLQuery.Parameter field = g.getparam(fieldname);
                        if (field != null)
                        {
                            output.Append(field.type.c_str()).Append(' ').Append(p.fields[f].@string());
                        }
                        else
                        {
                            output.Append("UNKNOWN");
                        }
                    }
                    output.Append("}\n");
                }
                else if (!p.validdefault)
                {
                    if (verbose)
                    {
                        output.Append("\t\tUnknown default value\n");
                    }
                    else
                    {
                        output.Append("nodefault\n");
                    }
                }
                else if (p.type.basetype == TypeDesc.STRING)
                {
                    print_default_string_vals(p, verbose);
                }
                else if (p.type.basetype == TypeDesc.INT)
                {
                    print_default_int_vals(p, verbose);
                }
                else
                {
                    print_default_float_vals(p, verbose);
                }
                if (verbose)
                {
                    for (int k = 0; k < p.metadata.Count; ++k)
                    {
                        print_metadata(p.metadata[k]);
                    }
                }
            }
        }

        private static void print_help()
        {
            output.Append("oslinfo -- list parameters of a compiled OSL shader\n");
            output.Append("Usage:  oslinfo [options] file0 [file1 ...]\n");
            output.Append("Options:\n");
            output.Append("    -v                          Verbose output\n");
            output.Append("    --runstats                  Benchmark shader loading time for queries\n");
            output.Append("    -p SEARCHPATH               Set searchpath for shaders\n");
            output.Append("    --param NAME                Output information about just this parameter\n");
        }

        public static int Main(string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                if (a == "-v")
                {
                    verbose = true;
                }
                else if (a == "--runstats")
                {
                    runstats = true;
                }
                else if (a == "-p" && i + 1 < args.Length)
                {
                    searchpath = args[++i];
                }
                else if (a == "--param" && i + 1 < args.Length)
                {
                    oneparam = args[++i];
                }
                else
                {
                    filenames.Add(a);
                }
            }
            if (filenames.Count == 0)
            {
                print_help();
            }
            foreach (string filename in filenames)
            {
                oslinfo(filename);
            }
            Console.Out.Write(output.ToString());
            Console.Out.Flush();
            return 0;
        }
    }
}
