// OSL port: testshade/testshade.cpp -- the command line tool that builds a shader group from the arguments, shades a grid of points with it and prints
// or saves the requested outputs. Not ported: OptiX, batched execution, OSLQuery based features beyond listing parameters, image file output (no
// image library here), the multithreaded shading loop (the points are shaded in order on one thread, so printed output is deterministic).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using OIIO;
using OSL;
using OSL.pvt;

namespace OSL.TestShade
{
    public sealed class ParamEntry
    {
        public string name;
        public TypeDesc type;
        public object val;
        public ParamHints hint;
    }

    /// The journal::Report2ErrorHandler of testshade, which opens files for fprintf
    public sealed class TestshadeReporter : OSL.journal.Report2ErrorHandler
    {
        public TestshadeReporter(ErrorHandler eh, OSL.journal.TrackRecentlyReported tracker) : base(eh, tracker)
        {
        }

        public override void report_file_print(int thread_index, int shade_index, string filename, string message)
        {
            File.AppendAllText(filename, message);
        }
    }

    public static unsafe class Program
    {
        private static ShadingSystem shadingsys;
        private static List<string> shadernames = new List<string>();
        private static List<string> outputfiles = new List<string>();
        private static List<string> outputvars = new List<string>();
        private static List<ustring> outputvarnames = new List<ustring>();
        private static List<TypeDesc> outputvartypes = new List<TypeDesc>();
        private static string dataformatname = "";
        private static List<string> entrylayers = new List<string>();
        private static List<string> entryoutputs = new List<string>();
        private static List<int> entrylayer_index = new List<int>();
        private static List<ShaderSymbol> entrylayer_symbols = new List<ShaderSymbol>();
        private static bool debug1 = false;
        private static bool debug2 = false;
        private static bool llvm_debug = false;
        private static bool verbose = false;
        private static bool runstats = false;
        private static bool batched = false;
        private static bool vary_Pdxdy = false;
        private static bool vary_udxdy = false;
        private static bool vary_vdxdy = false;
        private static bool warmup = false;
        private static bool profile = false;
        // (not in OpenShadingLanguage) --ilassembly <dir>: compile each group to a persisted assembly in <dir>, load it back and shade with that
        private static string ilassembly_dir = "";
        // (not in OpenShadingLanguage) --ilreport <file>: write the list of shadeops the IL backend could not map to a managed implementation
        private static string ilreport_file = "";
        private static bool O0 = false, O1 = false, O2 = false;
        private static int llvm_opt = 1;
        private static bool pixelcenters = false;
        private static bool debugnan = false;
        private static bool debug_uninit = false;
        private static bool use_group_outputs = false;
        private static bool do_oslquery = false;
        private static bool print_groupdata = false;
        private static bool print_group_stats = false;
        private static bool inbuffer = false;
        private static bool use_shade_image = false;
        private static bool userdata_isconnected = false;
        private static bool print_outputs = false;
        private static bool output_placement = false;  // this port always reads the outputs back with get_symbol (testshade --no-output-placement)
        private static int xres = 1, yres = 1;
        private static int num_threads = 0;
        private static string groupname = "";
        private static string groupspec = "";
        private static string layername = "";
        private static List<string> connections = new List<string>();
        private static List<ParamEntry> parameters = new List<ParamEntry>();
        private static List<ParamEntry> reparams = new List<ParamEntry>();
        private static string reparam_layer = "";
        private static int iters = 1;
        private static string raytype_name = "camera";
        private static int raytype_bit = 0;
        private static bool raytype_opt = false;
        private static string extraoptions = "";
        private static string texoptions = "";
        private static string colorspace = "";
        private static Matrix44* Mshad;  // "shader" space to "common" space matrix
        private static Matrix44* Mobj;   // "object" space to "common" space matrix
        private static ShaderGroup shadergroup;
        private static string archivegroup = "";
        private static int exprcount = 0;
        private static float uscale = 1, vscale = 1;
        private static float uoffset = 0, voffset = 0;
        private static List<string> shader_setup_args = new List<string>();
        private static SimpleRenderer rend;

        // ----------------------------------------------------------------------------------------------------------------------------------------
        // Command line parsing (the part of OIIO::ArgParse that testshade uses)

        private delegate void ArgAction(string[] argv);

        private sealed class ArgOption
        {
            public string name;
            public int nargs;
            public ArgAction action;
        }

        private static string[] sub(List<string> args, int start, int count)
        {
            string[] r = new string[count];
            for (int i = 0; i < count; ++i)
            {
                r[i] = args[start + i];
            }
            return r;
        }

        private static ArgOption opt(string name, int nargs, ArgAction action)
        {
            ArgOption o = new ArgOption();
            o.name = name;
            o.nargs = nargs;
            o.action = action;
            return o;
        }

        /// Parses 'args' (args[0] is the program) with the options; arguments that are not options go to 'filename_action'. Returns false on a bad option.
        private static bool parse_args(List<string> args, List<ArgOption> options, ArgAction filename_action)
        {
            Dictionary<string, ArgOption> table = new Dictionary<string, ArgOption>();
            foreach (ArgOption o in options)
            {
                table[o.name] = o;
            }
            bool ok = true;
            for (int i = 1; i < args.Count; ++i)
            {
                string a = args[i];
                if (a.Length > 1 && a[0] == '-')
                {
                    string stripped = a.StartsWith("--") ? a.Substring(2) : a.Substring(1);
                    int colon = stripped.IndexOf(':');
                    string key = colon >= 0 ? stripped.Substring(0, colon) : stripped;
                    ArgOption o;
                    if (!table.TryGetValue(key, out o))
                    {
                        Console.Error.WriteLine("ERROR: Invalid option \"" + a + "\"");
                        ok = false;
                        continue;
                    }
                    if (i + o.nargs >= args.Count + (o.nargs == 0 ? 1 : 0) && o.nargs > 0 && i + o.nargs > args.Count - 1)
                    {
                        Console.Error.WriteLine("ERROR: Missing parameter(s) for option \"" + a + "\"");
                        ok = false;
                        break;
                    }
                    string[] argv = new string[o.nargs + 1];
                    argv[0] = a;
                    for (int k = 0; k < o.nargs; ++k)
                    {
                        argv[k + 1] = args[i + 1 + k];
                    }
                    i += o.nargs;
                    o.action(argv);
                }
                else
                {
                    filename_action(new string[] { a });
                }
            }
            return ok;
        }

        private static ArgAction flag(Action<bool> set)
        {
            return delegate (string[] argv) { set(true); };
        }

        private static int to_int(string s)
        {
            int v;
            if (int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return 0;
        }

        private static float to_float(string s)
        {
            float v;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return v;
            }
            return 0.0f;
        }

        private static void stash_shader_arg(string[] argv)
        {
            foreach (string a in argv)
            {
                shader_setup_args.Add(a);
            }
        }

        private static void stash_userdata(string[] argv)
        {
            List<ParamEntry> list = new List<ParamEntry>();
            add_param(list, argv[0], argv[1], argv[2]);
            foreach (ParamEntry pe in list)
            {
                UserDataItem item = new UserDataItem();
                item.name = pe.name;
                item.type = pe.type;
                item.data = param_bytes(pe);
                rend.userdata.Add(item);
            }
        }

        private static byte[] param_bytes(ParamEntry pe)
        {
            if (pe.val is float[] fa)
            {
                byte[] r = new byte[fa.Length * 4];
                Buffer.BlockCopy(fa, 0, r, 0, r.Length);
                return r;
            }
            if (pe.val is int[] ia)
            {
                byte[] r = new byte[ia.Length * 4];
                Buffer.BlockCopy(ia, 0, r, 0, r.Length);
                return r;
            }
            if (pe.val is string[] sa)
            {
                byte[] r = new byte[sa.Length * 8];
                for (int i = 0; i < sa.Length; ++i)
                {
                    BitConverter.GetBytes(new ustring(sa[i]).hash()).CopyTo(r, i * 8);
                }
                return r;
            }
            return new byte[0];
        }

        private static void getargs(string[] argv)
        {
            // Some arguments set up the shader instances, but other args and housekeeping are needed first, so the shader setup args are stored
            // until they can be processed in full.
            shader_setup_args.Clear();
            shader_setup_args.Add("testshade");

            List<string> args = new List<string>();
            args.Add("testshade");
            args.AddRange(argv);

            List<ArgOption> o = new List<ArgOption>();
            o.Add(opt("v", 0, delegate (string[] a) { verbose = true; }));
            o.Add(opt("buildinfo", 0, delegate (string[] a)
            {
                Console.Out.WriteLine("OSL " + OslVersion.OSL_LIBRARY_VERSION_STRING + " | C#/.NET IL backend");
                Environment.Exit(0);
            }));
            o.Add(opt("t", 1, delegate (string[] a) { num_threads = to_int(a[1]); }));
            o.Add(opt("optix", 0, delegate (string[] a) { }));
            o.Add(opt("debug", 0, delegate (string[] a) { debug1 = true; }));
            o.Add(opt("debug2", 0, delegate (string[] a) { debug2 = true; }));
            o.Add(opt("llvm_debug", 0, delegate (string[] a) { llvm_debug = true; }));
            o.Add(opt("runstats", 0, delegate (string[] a) { runstats = true; }));
            o.Add(opt("stats", 0, delegate (string[] a) { runstats = true; }));
            o.Add(opt("batched", 0, delegate (string[] a) { batched = false; }));
            o.Add(opt("vary_pdxdy", 0, delegate (string[] a) { vary_Pdxdy = true; }));
            o.Add(opt("vary_udxdy", 0, delegate (string[] a) { vary_udxdy = true; }));
            o.Add(opt("vary_vdxdy", 0, delegate (string[] a) { vary_vdxdy = true; }));
            o.Add(opt("profile", 0, delegate (string[] a) { profile = true; }));
            o.Add(opt("saveptx", 0, delegate (string[] a) { }));
            o.Add(opt("warmup", 0, delegate (string[] a) { warmup = true; }));
            o.Add(opt("res", 2, delegate (string[] a) { xres = to_int(a[1]); yres = to_int(a[2]); }));
            o.Add(opt("g", 2, delegate (string[] a) { xres = to_int(a[1]); yres = to_int(a[2]); }));
            o.Add(opt("options", 1, delegate (string[] a) { extraoptions = a[1]; }));
            o.Add(opt("texoptions", 1, delegate (string[] a) { texoptions = a[1]; }));
            o.Add(opt("colorspace", 1, delegate (string[] a) { colorspace = a[1]; }));
            o.Add(opt("o", 2, delegate (string[] a) { outputvars.Add(a[1]); outputfiles.Add(a[2]); }));
            o.Add(opt("d", 1, delegate (string[] a) { dataformatname = a[1]; }));
            o.Add(opt("od", 1, delegate (string[] a) { dataformatname = a[1]; }));
            o.Add(opt("print", 0, delegate (string[] a) { print_outputs = true; }));
            o.Add(opt("groupname", 1, delegate (string[] a) { groupname = a[1]; }));
            o.Add(opt("layer", 1, stash_shader_arg));
            o.Add(opt("param", 2, stash_shader_arg));
            o.Add(opt("shader", 2, stash_shader_arg));
            o.Add(opt("connect", 4, stash_shader_arg));
            o.Add(opt("reparam", 3, stash_shader_arg));
            o.Add(opt("group", 1, stash_shader_arg));
            o.Add(opt("archivegroup", 1, delegate (string[] a) { archivegroup = a[1]; }));
            o.Add(opt("raytype", 1, delegate (string[] a) { raytype_name = a[1]; }));
            o.Add(opt("raytype_opt", 0, delegate (string[] a) { raytype_opt = true; }));
            o.Add(opt("iters", 1, delegate (string[] a) { iters = to_int(a[1]); }));
            o.Add(opt("ilreport", 1, delegate (string[] a) { ilreport_file = a[1]; }));
            o.Add(opt("ilassembly", 1, delegate (string[] a) { ilassembly_dir = a[1]; }));
            o.Add(opt("O0", 0, delegate (string[] a) { O0 = true; }));
            o.Add(opt("O1", 0, delegate (string[] a) { O1 = true; }));
            o.Add(opt("O2", 0, delegate (string[] a) { O2 = true; }));
            o.Add(opt("llvm_opt", 1, delegate (string[] a) { llvm_opt = to_int(a[1]); }));
            foreach (string name in new string[] { "optix_no_inline", "optix_no_inline_layer_funcs", "optix_no_merge_layer_funcs", "optix_no_inline_rend_lib",
                                                   "optix_no_rend_lib_bitcode", "optix_register_inline_funcs" })
            {
                o.Add(opt(name, 0, delegate (string[] a) { }));
            }
            foreach (string name in new string[] { "optix_no_inline_thresh", "optix_force_inline_thresh" })
            {
                o.Add(opt(name, 1, delegate (string[] a) { }));
            }
            o.Add(opt("entry", 1, delegate (string[] a) { entrylayers.Add(a[1]); }));
            o.Add(opt("entryoutput", 1, delegate (string[] a) { entryoutputs.Add(a[1]); }));
            o.Add(opt("center", 0, delegate (string[] a) { pixelcenters = true; }));
            o.Add(opt("debugnan", 0, delegate (string[] a) { debugnan = true; }));
            o.Add(opt("debuguninit", 0, delegate (string[] a) { debug_uninit = true; }));
            o.Add(opt("groupoutputs", 0, delegate (string[] a) { use_group_outputs = true; }));
            o.Add(opt("oslquery", 0, delegate (string[] a) { do_oslquery = true; }));
            o.Add(opt("print-groupdata", 0, delegate (string[] a) { print_groupdata = true; }));
            o.Add(opt("print-group-stats", 0, delegate (string[] a) { print_group_stats = true; }));
            o.Add(opt("inbuffer", 0, delegate (string[] a) { inbuffer = true; }));
            o.Add(opt("no-output-placement", 0, delegate (string[] a) { output_placement = false; }));
            o.Add(opt("shadeimage", 0, delegate (string[] a) { use_shade_image = true; }));
            o.Add(opt("noshadeimage", 0, delegate (string[] a) { use_shade_image = false; }));
            o.Add(opt("expr", 1, stash_shader_arg));
            o.Add(opt("offsetuv", 2, delegate (string[] a) { }));  // as in the C++, which does not store these
            o.Add(opt("offsetst", 2, delegate (string[] a) { uoffset = to_float(a[1]); voffset = to_float(a[2]); }));
            o.Add(opt("scaleuv", 2, delegate (string[] a) { uscale = to_float(a[1]); vscale = to_float(a[2]); }));
            o.Add(opt("scalest", 2, delegate (string[] a) { uscale = to_float(a[1]); vscale = to_float(a[2]); }));
            o.Add(opt("userdata", 2, stash_userdata));
            o.Add(opt("userdata_isconnected", 0, delegate (string[] a) { userdata_isconnected = true; }));
            o.Add(opt("locale", 1, delegate (string[] a) { }));
            o.Add(opt("use_rs_bitcode", 0, delegate (string[] a) { }));
            o.Add(opt("jbufferMB", 1, delegate (string[] a) { }));
            o.Add(opt("help", 0, delegate (string[] a)
            {
                Console.Out.WriteLine("testshade -- Test Open Shading Language");
                Environment.Exit(0);
            }));
            parse_args(args, o, stash_shader_arg);
        }

        private static void process_shader_setup_args()
        {
            List<ArgOption> o = new List<ArgOption>();
            o.Add(opt("layer", 1, delegate (string[] a) { layername = a[1]; }));
            o.Add(opt("param", 2, action_param));
            o.Add(opt("shader", 2, action_shaderdecl));
            o.Add(opt("connect", 4, delegate (string[] a)
            {
                connections.Add(a[1]);
                connections.Add(a[2]);
                connections.Add(a[3]);
                connections.Add(a[4]);
            }));
            o.Add(opt("reparam", 3, action_reparam));
            o.Add(opt("group", 1, action_groupspec));
            o.Add(opt("expr", 1, specify_expr));
            bool ok = parse_args(shader_setup_args, o, add_shader);
            if (!ok || (shadernames.Count == 0 && groupspec.Length == 0))
            {
                Console.Error.WriteLine("ERROR: No shader or group was specified.");
                Console.Error.WriteLine("Try `testshade --help` for an explanation of all arguments");
                Environment.Exit(1);
            }
        }

        // ----------------------------------------------------------------------------------------------------------------------------------------
        // Shader group setup

        private static void inject_params()
        {
            foreach (ParamEntry pv in parameters)
            {
                shadingsys.Parameter(shadergroup, pv.name, pv.type, pv.val, pv.hint);
            }
        }

        private static void set_shadingsys_options()
        {
            shadingsys.attribute("llvm_debugging_symbols", 1);
            shadingsys.attribute("llvm_profiling_events", 1);
            shadingsys.attribute("llvm_debug", llvm_debug ? 2 : 0);
            shadingsys.attribute("debug", debug2 ? 2 : (debug1 ? 1 : 0));
            if (debug1 || debug2)
            {
                shadingsys.attribute("compile_report", 1);
            }
            int optlevel = 2;  // default
            if (O0)
            {
                optlevel = 0;
            }
            if (O1)
            {
                optlevel = 1;
            }
            if (O2)
            {
                optlevel = 2;
            }
            string opt_env = Environment.GetEnvironmentVariable("TESTSHADE_OPT");
            if (opt_env != null)
            {
                optlevel = to_int(opt_env);
            }
            shadingsys.attribute("optimize", optlevel);
            string llvm_opt_env = Environment.GetEnvironmentVariable("TESTSHADE_LLVM_OPT");
            if (llvm_opt_env != null)
            {
                llvm_opt = to_int(llvm_opt_env);
            }
            shadingsys.attribute("llvm_optimize", llvm_opt);
            shadingsys.attribute("profile", profile ? 1 : 0);
            shadingsys.attribute("debug_nan", debugnan ? 1 : 0);
            shadingsys.attribute("debug_uninit", debug_uninit ? 1 : 0);
            shadingsys.attribute("userdata_isconnected", userdata_isconnected ? 1 : 0);
            if (colorspace.Length != 0)
            {
                shadingsys.attribute("colorspace", colorspace);
            }
            shadingsys.attribute("llvm_jit_fma", 0);
            // Not batched: no need for the uniform/varying analysis
            shadingsys.attribute("opt_batched_analysis", 0);

            // Allow user provided extraoptions to override the values set above
            if (extraoptions.Length != 0)
            {
                shadingsys.attribute("options", extraoptions);
            }
            if (texoptions.Length != 0)
            {
                shadingsys.texturesys().attribute("options", texoptions);
            }
        }

        private static void compile_buffer(string sourcecode, string shadername)
        {
            string osobuffer;
            ErrorHandler eh = rend.errhandler();
            OSLCompiler compiler = new OSLCompiler(eh);
            if (!compiler.compile_buffer(sourcecode, out osobuffer, new List<string>(), "/home/claude/OpenShadingLanguage/src/shaders/stdosl.h",
                                         shadername + ".osl"))
            {
                Console.Error.WriteLine("Could not compile \"" + shadername + "\"");
                Environment.Exit(1);
            }
            if (!shadingsys.LoadMemoryCompiledShader(shadername, osobuffer))
            {
                Console.Error.WriteLine("Could not load compiled buffer from \"" + shadername + "\"");
                Environment.Exit(1);
            }
        }

        private static void shader_from_buffers(string shadername)
        {
            string oslfilename = shadername;
            if (!oslfilename.EndsWith(".osl"))
            {
                oslfilename += ".osl";
            }
            if (!File.Exists(oslfilename))
            {
                Console.Error.WriteLine("Could not open \"" + oslfilename + "\"");
                Environment.Exit(1);
            }
            compile_buffer(File.ReadAllText(oslfilename), shadername);
        }

        private static void add_shader(string[] argv)
        {
            string shadername = argv[0];
            set_shadingsys_options();
            if (inbuffer)
            {
                shader_from_buffers(shadername);
            }
            inject_params();
            shadernames.Add(shadername);
            shadingsys.Shader(shadergroup, "surface", shadername, layername);
            layername = "";
            parameters.Clear();
        }

        private static void action_shaderdecl(string[] argv)
        {
            // `--shader shadername layername` is exactly equivalent to `--layer layername` followed by naming the shader.
            layername = argv[2];
            add_shader(new string[] { argv[1] });
        }

        private static void specify_expr(string[] argv)
        {
            string shadername = "expr_" + exprcount++.ToString(CultureInfo.InvariantCulture);
            string sourcecode = "shader " + shadername + " (\n"
                                + "    float s = u [[ int interpolated=1 ]],\n"
                                + "    float t = v [[ int interpolated=1 ]],\n"
                                + "    output color result = 0,\n"
                                + "    output float alpha = 1,\n"
                                + "  )\n"
                                + "{\n"
                                + "    " + argv[1] + "\n"
                                + "    ;\n"
                                + "}\n";
            if (verbose)
            {
                Console.Out.Write("Expression-based shader text is:\n---\n" + sourcecode + "---\n");
            }
            set_shadingsys_options();
            compile_buffer(sourcecode, shadername);
            inject_params();
            shadernames.Add(shadername);
            shadingsys.Shader(shadergroup, "surface", shadername, layername);
            layername = "";
            parameters.Clear();
        }

        // Parse str for `len` floats, separated by commas.
        private static bool parse_float_list(string str, float[] f, int len)
        {
            string_view sv = new string_view(str);
            bool ok = true;
            for (int i = 0; i < len && ok; ++i)
            {
                float v;
                ok &= Strutil.parse_float(ref sv, out v);
                f[i] = v;
                if (ok && i < len - 1)
                {
                    ok &= Strutil.parse_char(ref sv, ',');
                }
            }
            return ok;
        }

        private static bool string_is_int(string s)
        {
            int v;
            return s.Length != 0 && int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v);
        }

        private static bool string_is_float(string s)
        {
            float v;
            return s.Length != 0 && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static bool equivalent(TypeDesc a, TypeDesc b)
        {
            return a.basetype == b.basetype && a.aggregate == b.aggregate && a.arraylen == b.arraylen;
        }

        // Utility: Add {paramname, stringval} to the given parameter list.
        private static void add_param(List<ParamEntry> list, string command, string paramname, string stringval)
        {
            TypeDesc type = new TypeDesc();  // UNKNOWN
            ParamHints hint = ParamHints.none;
            float[] f = new float[16];

            // Dissect optional modifiers from a command that might look like "--param:type=float:interactive=1"
            int colonpos = command.IndexOf(':');
            if (colonpos >= 0)
            {
                string rest = command.Substring(colonpos + 1);
                foreach (string o in rest.Split(':'))
                {
                    // Each option should look like "foo=bar", split at the '='
                    string[] parts = o.Split('=');
                    if (parts.Length == 2)
                    {
                        if (parts[0] == "type")
                        {
                            type = TypeDescParse.fromstring(parts[1]);
                        }
                        else if (parts[0] == "lockgeom")
                        {
                            hint = ParamHintsOps.set(hint, ParamHints.interpolated, to_int(parts[1]) == 0);
                        }
                        else if (parts[0] == "interpolated")
                        {
                            hint = ParamHintsOps.set(hint, ParamHints.interpolated, to_int(parts[1]) != 0);
                        }
                        else if (parts[0] == "interactive")
                        {
                            hint = ParamHintsOps.set(hint, ParamHints.interactive, to_int(parts[1]) != 0);
                        }
                    }
                }
            }

            bool unknown = type.basetype == TypeDesc.UNKNOWN;

            // If it is or might be a matrix, look for 16 comma-separated floats
            if ((unknown || type == TypeDescs.TypeMatrix) && parse_float_list(stringval, f, 16))
            {
                list.Add(make_param(paramname, TypeDescs.TypeMatrix, (float[])f.Clone(), hint));
                return;
            }
            // If it is or might be a vector type, look for 3 comma-separated floats
            if ((unknown || equivalent(type, TypeDescs.TypeVector)) && parse_float_list(stringval, f, 3))
            {
                if (unknown)
                {
                    type = TypeDescs.TypeVector;
                }
                list.Add(make_param(paramname, type, new float[] { f[0], f[1], f[2] }, hint));
                return;
            }
            // If it is or might be an int, look for an int that takes up the whole string.
            if ((unknown || type == TypeDescs.TypeInt) && string_is_int(stringval))
            {
                list.Add(make_param(paramname, TypeDescs.TypeInt, new int[] { to_int(stringval) }, hint));
                return;
            }
            // If it is or might be an float, look for a float that takes up the whole string.
            if ((unknown || type == TypeDescs.TypeFloat) && string_is_float(stringval))
            {
                list.Add(make_param(paramname, TypeDescs.TypeFloat, new float[] { to_float(stringval) }, hint));
                return;
            }

            // Catch-all for float types and arrays
            if (type.basetype == TypeDesc.FLOAT)
            {
                int n = type.aggregate * (int)type.numelements();
                float[] vals = new float[n];
                string_view sv = new string_view(stringval);
                for (int i = 0; i < n; ++i)
                {
                    float v;
                    Strutil.parse_float(ref sv, out v);
                    vals[i] = v;
                    Strutil.parse_char(ref sv, ',');
                }
                list.Add(make_param(paramname, type, vals, hint));
                return;
            }

            // Catch-all for int types and arrays
            if (type.basetype == TypeDesc.INT)
            {
                int n = type.aggregate * (int)type.numelements();
                int[] vals = new int[n];
                string_view sv = new string_view(stringval);
                for (int i = 0; i < n; ++i)
                {
                    int v;
                    Strutil.parse_int(ref sv, out v);
                    vals[i] = v;
                    Strutil.parse_char(ref sv, ',');
                }
                list.Add(make_param(paramname, type, vals, hint));
                return;
            }

            // String arrays are slightly tricky
            if (type.basetype == TypeDesc.STRING && type.arraylen != 0)
            {
                string[] elements = stringval.Split(new char[] { ',' }, type.arraylen);
                string[] result = new string[type.arraylen];
                for (int i = 0; i < result.Length; ++i)
                {
                    result[i] = i < elements.Length ? elements[i] : "";
                }
                list.Add(make_param(paramname, type, result, hint));
                return;
            }

            // All remaining cases -- it's a string
            list.Add(make_param(paramname, TypeDescs.TypeString, new string[] { stringval }, hint));
        }

        private static ParamEntry make_param(string name, TypeDesc type, object val, ParamHints hint)
        {
            ParamEntry pe = new ParamEntry();
            pe.name = name;
            pe.type = type;
            pe.val = val;
            pe.hint = hint;
            return pe;
        }

        private static void action_param(string[] argv)
        {
            string command = argv[0];
            bool use_reparam = command.StartsWith("--reparam", StringComparison.OrdinalIgnoreCase) || command.StartsWith("-reparam", StringComparison.OrdinalIgnoreCase);
            add_param(use_reparam ? reparams : parameters, command, argv[1], argv[2]);
        }

        // reparam -- just set reparam_layer and then let action_param do all the hard work.
        private static void action_reparam(string[] argv)
        {
            reparam_layer = argv[1];
            action_param(new string[] { argv[0], argv[2], argv[3] });
        }

        private static void action_groupspec(string[] argv)
        {
            shadingsys.ShaderGroupEnd(shadergroup);
            string spec = argv[1];
            if (File.Exists(spec))
            {
                // If it names a file, use the contents of the file as the group specification.
                spec = File.ReadAllText(spec);
            }
            groupspec = spec;
            set_shadingsys_options();
            if (verbose)
            {
                Console.Out.Write("Processing group specification:\n---\n" + spec + "\n---\n");
            }
            shadergroup = shadingsys.ShaderGroupBegin(groupname, "surface", spec);
        }

        // ----------------------------------------------------------------------------------------------------------------------------------------
        // Matrices and shader globals

        private static Matrix44 translate(Matrix44 M, Vec3 t)
        {
            Matrix44 T = Matrix44.identity;
            T.m30 = t.x;
            T.m31 = t.y;
            T.m32 = t.z;
            return T * M;
        }

        private static Matrix44 rotate_z(Matrix44 M, float angle)
        {
            float c = MathF.Cos(angle);
            float s = MathF.Sin(angle);
            Matrix44 R = Matrix44.identity;
            R.m00 = c;
            R.m01 = s;
            R.m10 = -s;
            R.m11 = c;
            return R * M;
        }

        private static Matrix44 scale(Matrix44 M, Vec3 s)
        {
            Matrix44 S = Matrix44.identity;
            S.m00 = s.x;
            S.m11 = s.y;
            S.m22 = s.z;
            return S * M;
        }

        // Here we set up transformations. These are just examples, set up so that our unit tests can transform among spaces in ways that we will
        // recognize as correct. The "shader" and "object" spaces are required by OSL and the ShaderGlobals will need to have references to them. For good
        // measure, we also set up a "myspace" space, registering it with the RendererServices.
        private static void setup_transformations(SimpleRenderer r)
        {
            Matrix44 M = Matrix44.identity;
            r.camera_params(M, new ustring("perspective").hash(), 90.0f, 0.1f, 1000.0f, xres, yres);

            // Make a "shader" space that is translated one unit in x and rotated 45deg about the z axis.
            Matrix44 shad = Matrix44.identity;
            shad = translate(shad, new Vec3(1.0f, 0.0f, 0.0f));
            shad = rotate_z(shad, (float)(Math.PI / 4.0));
            *Mshad = shad;

            // Make an object space that is translated one unit in y and rotated 90deg about the z axis.
            Matrix44 obj = Matrix44.identity;
            obj = translate(obj, new Vec3(0.0f, 1.0f, 0.0f));
            obj = rotate_z(obj, (float)(Math.PI / 2.0));
            *Mobj = obj;

            Matrix44 Mmyspace = Matrix44.identity;
            Mmyspace = scale(Mmyspace, new Vec3(1.0f, 2.0f, 1.0f));
            r.name_transform("myspace", Mmyspace);
        }

        // Set up the ShaderGlobals fields for pixel (x,y).
        private static void setup_shaderglobals(ref ShaderGlobals sg, int x, int y)
        {
            // Just zero the whole thing out to start
            sg = new ShaderGlobals();

            // Set "shader" space to be Mshad. In a real renderer, this may be different for each shader group.
            sg.shader2common = Mshad;

            // Set "object" space to be Mobj. In a real renderer, this may be different for each object.
            sg.object2common = Mobj;

            // Just make it look like all shades are the result of 'raytype' rays.
            sg.raytype = raytype_bit;

            // Set up u,v to vary across the "patch", and also their derivatives. Note that since u & x, and v & y are aligned, we only need to set values
            // for dudx and dvdy, we can use the zeroing above to have set dvdx and dudy to 0.
            if (pixelcenters)
            {
                // Our patch is like an "image" with shading samples at the centers of each pixel.
                sg.u = uscale * (float)(x + 0.5f) / xres + uoffset;
                sg.v = vscale * (float)(y + 0.5f) / yres + voffset;
                if (vary_udxdy)
                {
                    sg.dudx = 1.0f - sg.u;
                    sg.dudy = sg.u;
                }
                else
                {
                    sg.dudx = uscale / xres;
                }
                if (vary_vdxdy)
                {
                    sg.dvdx = 1.0f - sg.v;
                    sg.dvdy = sg.v;
                }
                else
                {
                    sg.dvdy = vscale / yres;
                }
            }
            else
            {
                // Our patch is like a Reyes grid of points, with the border samples being exactly on u,v == 0 or 1.
                sg.u = uscale * ((xres == 1) ? 0.5f : (float)x / (xres - 1)) + uoffset;
                sg.v = vscale * ((yres == 1) ? 0.5f : (float)y / (yres - 1)) + voffset;
                if (vary_udxdy)
                {
                    sg.dudx = 1.0f - sg.u;
                    sg.dudy = sg.u;
                }
                else
                {
                    sg.dudx = uscale / Math.Max(1, xres - 1);
                }
                if (vary_vdxdy)
                {
                    sg.dvdx = 1.0f - sg.v;
                    sg.dvdy = sg.v;
                }
                else
                {
                    sg.dvdy = vscale / Math.Max(1, yres - 1);
                }
            }

            // Assume that position P is simply (u,v,1), that makes the patch lie on [0,1] at z=1.
            sg.P = new Vec3(sg.u, sg.v, 1.0f);
            // Derivatives with respect to x,y
            if (vary_Pdxdy)
            {
                sg.dPdx = new Vec3(1.0f - sg.u, 1.0f - sg.v, (float)(sg.u * 0.5));
                sg.dPdy = new Vec3(1.0f - sg.v, 1.0f - sg.u, (float)(sg.v * 0.5));
            }
            else
            {
                sg.dPdx = new Vec3(uscale / Math.Max(1, xres - 1), 0.0f, 0.0f);
                sg.dPdy = new Vec3(0.0f, vscale / Math.Max(1, yres - 1), 0.0f);
            }
            sg.dPdz = new Vec3(0.0f, 0.0f, 0.0f);  // just use 0 for volume tangent
            // Tangents of P with respect to surface u,v
            sg.dPdu = new Vec3(1.0f, 0.0f, 0.0f);
            sg.dPdv = new Vec3(0.0f, 1.0f, 0.0f);
            // That also implies that our normal points to (0,0,1)
            sg.N = new Vec3(0, 0, 1);
            sg.Ng = new Vec3(0, 0, 1);

            // Set the surface area of the patch to 1 (which it is). This is only used for light shaders that call the surfacearea() function.
            sg.surfacearea = 1;
        }

        // ----------------------------------------------------------------------------------------------------------------------------------------
        // Outputs

        /// The output parameters of a layer and their types, in declaration order (what OSLQuery gives)
        private static List<KeyValuePair<string, TypeDesc>> layer_params(ShaderGroup group, int lay, out List<bool> isoutput)
        {
            List<KeyValuePair<string, TypeDesc>> result = new List<KeyValuePair<string, TypeDesc>>();
            isoutput = new List<bool>();
            ShaderMaster master = group[lay].master();
            foreach (Symbol s in master.m_symbols)
            {
                if (s.symtype() == SymType.SymTypeParam || s.symtype() == SymType.SymTypeOutputParam)
                {
                    result.Add(new KeyValuePair<string, TypeDesc>(s.name().@string(), s.typespec().simpletype()));
                    isoutput.Add(s.symtype() == SymType.SymTypeOutputParam);
                }
            }
            return result;
        }

        private static string[] get_string_array(ShaderGroup group, string name, int count)
        {
            object val;
            string[] result = new string[count];
            if (shadingsys.getattribute(group, name, new TypeDesc(TypeDesc.PTR), out val))
            {
                if (val is string[] sa)
                {
                    return sa;
                }
                if (val is System.Collections.IEnumerable items)
                {
                    int k = 0;
                    foreach (object item in items)
                    {
                        if (k < count)
                        {
                            result[k++] = item.ToString();
                        }
                    }
                }
            }
            return result;
        }

        private static void setup_output_images(SimpleRenderer r, ShaderGroup group)
        {
            // If the command line didn't specify any outputs, default to Cout.
            if (outputvars.Count == 0)
            {
                outputvars.Add("Cout");
                outputfiles.Add("null");
            }

            // Declare entry layers, if specified
            if (entrylayers.Count != 0)
            {
                Console.Out.Write("Entry layers:");
                string[] layers = new string[entrylayers.Count];
                for (int i = 0; i < entrylayers.Count; ++i)
                {
                    ustring ln = new ustring(entrylayers[i]);
                    int layid = shadingsys.find_layer(group, ln);
                    layers[i] = entrylayers[i];
                    entrylayer_index.Add(layid);
                    Console.Out.Write(" " + entrylayers[i] + "(" + layid.ToString(CultureInfo.InvariantCulture) + ")");
                }
                Console.Out.Write("\n");
                shadingsys.attribute(group, "entry_layers", new TypeDesc(TypeDesc.STRING, entrylayers.Count), layers);
            }

            // Get info about the number of layers in the shader group
            int num_layers = group.nlayers();
            string[] layernames = new string[num_layers];
            for (int i = 0; i < num_layers; ++i)
            {
                layernames[i] = group[i].layername().@string();
            }

            // For each output file specified on the command line, figure out if it's really an output of some layer (and its type), and tell the
            // renderer that it's an output.
            for (int i = 0; i < outputfiles.Count; ++i)
            {
                string layer = "";
                string var = outputvars[i];
                int dot = outputvars[i].IndexOf('.');
                if (dot >= 0)
                {
                    layer = outputvars[i].Substring(0, dot);
                    var = outputvars[i].Substring(dot + 1);
                }
                TypeDesc vartype = new TypeDesc();
                bool found = false;
                // Go back to front so if the name we were given doesn't designate a layer, we preferentially find it at the end.
                for (int lay = num_layers - 1; lay >= 0 && !found; --lay)
                {
                    if (layer == layernames[lay] || layer.Length == 0)
                    {
                        List<bool> isoutput;
                        List<KeyValuePair<string, TypeDesc>> ps = layer_params(group, lay, out isoutput);
                        for (int p = 0; p < ps.Count; ++p)
                        {
                            if (isoutput[p] && ps[p].Key == var)
                            {
                                vartype = ps[p].Value;
                                found = true;
                                break;
                            }
                        }
                    }
                }
                if (found)
                {
                    outputvarnames.Add(new ustring(var));
                    outputvartypes.Add(vartype);
                    if (outputfiles[i] != "null")
                    {
                        Console.Out.WriteLine("Output " + outputvars[i] + " to " + outputfiles[i]);
                    }
                    TypeDesc tbase = new TypeDesc(vartype.basetype);
                    int nchans = (int)vartype.basevalues();
                    r.add_output(outputvars[i], outputfiles[i], tbase, nchans);
                }
            }

            if (outputvars.Count != 0)
            {
                // Tell the shading system which outputs we want
                List<string> aovnames = new List<string>();
                for (int i = 0; i < outputvars.Count; ++i)
                {
                    string varname = outputvars[i];
                    int dot = varname.IndexOf('.');
                    if (dot >= 0)
                    {
                        // If the name contains a dot, it's intended to be layer.symbol
                        varname = varname.Substring(dot + 1);
                    }
                    aovnames.Add(outputvars[i]);
                }
                TypeDesc aovtype = new TypeDesc(TypeDesc.STRING, aovnames.Count);
                if (use_group_outputs)
                {
                    shadingsys.attribute(group, "renderer_outputs", aovtype, aovnames.ToArray());
                }
                else
                {
                    shadingsys.attribute("renderer_outputs", aovtype, aovnames.ToArray());
                }
            }

            if (ilassembly_dir.Length != 0 && !raytype_opt)
            {
                Directory.CreateDirectory(ilassembly_dir);
                PerThreadInfo pthread = shadingsys.create_thread_info();
                ShadingContext pctx = shadingsys.get_context(pthread);
                string pfile = Path.Combine(ilassembly_dir, "group" + System.Diagnostics.Process.GetCurrentProcess().Id + ".dll");
                bool pok = BackendIL.save_group_assembly(group, pctx, pfile) && BackendIL.load_group_assembly(group, pctx, pfile);
                shadingsys.release_context(pctx);
                shadingsys.destroy_thread_info(pthread);
                if (!pok)
                {
                    Console.Error.WriteLine("ilassembly: could not save and load the group");
                }
            }

            if (entryoutputs.Count != 0)
            {
                // Because we can only call find_symbol or get_symbol on something that has been set up to shade (or executed), we call execute() but tell
                // it not to actually run the shader.
                PerThreadInfo thread_info = shadingsys.create_thread_info();
                ShadingContext ctx = shadingsys.get_context(thread_info);
                raytype_bit = shadingsys.raytype_bit(new ustring(raytype_name));
                ShaderGlobals sg = new ShaderGlobals();
                setup_shaderglobals(ref sg, 0, 0);
                if (raytype_opt)
                {
                    shadingsys.set_raytypes(group, raytype_bit, ~raytype_bit);
                    shadingsys.optimize_group(group, ctx);
                }
                shadingsys.execute(ctx, group, ref sg, false);
                Console.Out.Write("Entry outputs:");
                for (int i = 0; i < entryoutputs.Count; ++i)
                {
                    ustring name = new ustring(entryoutputs[i]);
                    ShaderSymbol sym = shadingsys.find_symbol(group, name);
                    if (sym == null)
                    {
                        Console.Out.Write("\nEntry output " + entryoutputs[i] + " not found. Abording.\n");
                        Environment.Exit(1);
                    }
                    entrylayer_symbols.Add(sym);
                    Console.Out.Write(" " + entryoutputs[i]);
                }
                Console.Out.Write("\n");
                shadingsys.release_context(ctx);
                shadingsys.destroy_thread_info(thread_info);
            }
        }

        private static string fmt_g(float v)
        {
            return Fmt.format("{:g}", v);
        }

        // For pixel (x,y) that was just shaded by the given shading context, save each of the requested outputs to the corresponding output buffer.
        private static void save_outputs(SimpleRenderer r, ShadingContext ctx, int x, int y)
        {
            if (print_outputs)
            {
                Console.Out.Write("Pixel (" + x.ToString(CultureInfo.InvariantCulture) + ", " + y.ToString(CultureInfo.InvariantCulture) + "):\n");
            }
            // For each output requested on the command line...
            for (int i = 0, e = r.noutputs(); i < e; ++i)
            {
                // Skip if we couldn't open the image or didn't match a known output
                OutputBuffer outputimg = r.outputbuf(i);
                if (outputimg == null)
                {
                    continue;
                }

                // Ask for a pointer to the symbol's data, as computed by this shader.
                ShaderSymbol sym = shadingsys.find_symbol(ctx.group(), r.outputname(i));
                if (sym == null)
                {
                    continue;  // Skip if symbol isn't found
                }
                TypeDesc t = shadingsys.symbol_typedesc(sym);
                object managed;
                void* data = shadingsys.symbol_address(ctx, sym, out managed);
                int nchans = outputimg.nchannels;
                GCHandle pin = default(GCHandle);
                if (data == null && managed != null)
                {
                    pin = GCHandle.Alloc(managed, GCHandleType.Pinned);
                    data = (void*)pin.AddrOfPinnedObject();
                }
                if (data == null)
                {
                    continue;
                }
                try
                {
                    if (t.basetype == TypeDesc.FLOAT)
                    {
                        // If the variable we are outputting is float-based, set it directly in the output buffer.
                        outputimg.setpixel(x, y, (float*)data);
                        if (print_outputs)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            sb.Append("  ").Append(outputvarnames[i].@string()).Append(" :");
                            for (int c = 0; c < nchans; ++c)
                            {
                                sb.Append(' ').Append(fmt_g(((float*)data)[c]));
                            }
                            sb.Append('\n');
                            Console.Out.Write(sb.ToString());
                        }
                    }
                    else if (t.basetype == TypeDesc.INT)
                    {
                        // We are outputting an integer variable, so we need to convert it to floating point.
                        float* pixel = stackalloc float[nchans];
                        for (int c = 0; c < nchans; ++c)
                        {
                            pixel[c] = (float)((int*)data)[c];
                        }
                        outputimg.setpixel(x, y, pixel);
                        if (print_outputs)
                        {
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            sb.Append("  ").Append(outputvarnames[i].@string()).Append(" :");
                            for (int c = 0; c < nchans; ++c)
                            {
                                sb.Append(' ').Append(((int*)data)[c].ToString(CultureInfo.InvariantCulture));
                            }
                            sb.Append('\n');
                            Console.Out.Write(sb.ToString());
                        }
                    }
                    // N.B. Drop any outputs that aren't float- or int-based
                }
                finally
                {
                    if (pin.IsAllocated)
                    {
                        pin.Free();
                    }
                }
            }
        }

        private static void shade_region(SimpleRenderer r, ShaderGroup group, int xbegin, int xend, int ybegin, int yend, bool save)
        {
            PerThreadInfo thread_info = shadingsys.create_thread_info();
            ShadingContext ctx = shadingsys.get_context(thread_info);
            ShaderGlobals shaderglobals = new ShaderGlobals();
            raytype_bit = shadingsys.raytype_bit(new ustring(raytype_name));

            for (int y = ybegin; y < yend; ++y)
            {
                int shadeindex = y * xres + xbegin;
                for (int x = xbegin; x < xend; ++x, ++shadeindex)
                {
                    setup_shaderglobals(ref shaderglobals, x, y);
                    int thread_index = 0;

                    // Actually run the shader for this point
                    if (entrylayer_index.Count == 0)
                    {
                        // Sole entry point for whole group, default behavior
                        shadingsys.execute(ctx, group, thread_index, shadeindex, ref shaderglobals, null, null);
                    }
                    else
                    {
                        // Explicit list of entries to call in order
                        shadingsys.execute_init(ctx, group, thread_index, shadeindex, ref shaderglobals, null, null);
                        if (entrylayer_symbols.Count != 0)
                        {
                            for (int i = 0, e = entrylayer_symbols.Count; i < e; ++i)
                            {
                                shadingsys.execute_layer(ctx, thread_index, shadeindex, ref shaderglobals, null, null,
                                                         entrylayer_symbols[i].symbol.layer());
                            }
                        }
                        else
                        {
                            for (int i = 0, e = entrylayer_index.Count; i < e; ++i)
                            {
                                shadingsys.execute_layer(ctx, thread_index, shadeindex, ref shaderglobals, null, null, entrylayer_index[i]);
                            }
                        }
                        shadingsys.execute_cleanup(ctx);
                    }

                    // Save all the designated outputs. But only do so if we are on the last iteration requested.
                    if (save && (print_outputs || !output_placement))
                    {
                        save_outputs(r, ctx, x, y);
                    }
                }
            }

            shadingsys.release_context(ctx);
            shadingsys.destroy_thread_info(thread_info);
        }

        private static void test_group_attributes(ShaderGroup group)
        {
            object val;
            int nt = 0;
            if (shadingsys.getattribute(group, "num_textures_needed", TypeDescs.TypeInt, out val))
            {
                nt = Convert.ToInt32(val);
                Console.Out.Write("Need " + nt.ToString(CultureInfo.InvariantCulture) + " textures:\n");
                string[] tex = get_string_array(group, "textures_needed", nt);
                for (int i = 0; i < nt; ++i)
                {
                    Console.Out.Write("    " + tex[i] + "\n");
                }
                int unk = 0;
                if (shadingsys.getattribute(group, "unknown_textures_needed", TypeDescs.TypeInt, out val))
                {
                    unk = Convert.ToInt32(val);
                }
                if (unk != 0)
                {
                    Console.Out.Write("    and unknown textures\n");
                }
            }
            int nclosures = 0;
            if (shadingsys.getattribute(group, "num_closures_needed", TypeDescs.TypeInt, out val))
            {
                nclosures = Convert.ToInt32(val);
                Console.Out.Write("Need " + nclosures.ToString(CultureInfo.InvariantCulture) + " closures:\n");
                string[] closures = get_string_array(group, "closures_needed", nclosures);
                for (int i = 0; i < nclosures; ++i)
                {
                    Console.Out.Write("    " + closures[i] + "\n");
                }
                int unk = 0;
                if (shadingsys.getattribute(group, "unknown_closures_needed", TypeDescs.TypeInt, out val))
                {
                    unk = Convert.ToInt32(val);
                }
                if (unk != 0)
                {
                    Console.Out.Write("    and unknown closures\n");
                }
            }
            int nglobals = 0;
            if (shadingsys.getattribute(group, "num_globals_needed", TypeDescs.TypeInt, out val))
            {
                nglobals = Convert.ToInt32(val);
                Console.Out.Write("Need " + nglobals.ToString(CultureInfo.InvariantCulture) + " globals: ");
                string[] globals = get_string_array(group, "globals_needed", nglobals);
                for (int i = 0; i < nglobals; ++i)
                {
                    Console.Out.Write(" " + globals[i]);
                }
                Console.Out.Write("\n");
            }

            int globals_read = 0;
            int globals_write = 0;
            if (shadingsys.getattribute(group, "globals_read", TypeDescs.TypeInt, out val))
            {
                globals_read = Convert.ToInt32(val);
            }
            if (shadingsys.getattribute(group, "globals_write", TypeDescs.TypeInt, out val))
            {
                globals_write = Convert.ToInt32(val);
            }
            Console.Out.Write("Globals read: (" + globals_read.ToString(CultureInfo.InvariantCulture) + ") ");
            for (int i = 1; i < (int)SGBits.last; i <<= 1)
            {
                if ((globals_read & i) != 0)
                {
                    Console.Out.Write(" " + ShadingSystem.globals_name((SGBits)i).@string());
                }
            }
            Console.Out.Write("\nGlobals written: (" + globals_write.ToString(CultureInfo.InvariantCulture) + ") ");
            for (int i = 1; i < (int)SGBits.last; i <<= 1)
            {
                if ((globals_write & i) != 0)
                {
                    Console.Out.Write(" " + ShadingSystem.globals_name((SGBits)i).@string());
                }
            }
            Console.Out.Write("\n");

            int raytype_queries = 0;
            if (shadingsys.getattribute(group, "raytype_queries", TypeDescs.TypeInt, out val))
            {
                raytype_queries = Convert.ToInt32(val);
            }
            Console.Out.Write("raytype() query mask: " + raytype_queries.ToString(CultureInfo.InvariantCulture) + "\n");
        }

        // ----------------------------------------------------------------------------------------------------------------------------------------

        private static int test_shade(string[] argv)
        {
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();

            // Get the command line arguments. Those that set up the shader instances are queued up in shader_setup_args for later handling.
            Mshad = (Matrix44*)Marshal.AllocHGlobal(sizeof(Matrix44));
            Mobj = (Matrix44*)Marshal.AllocHGlobal(sizeof(Matrix44));
            *Mshad = Matrix44.identity;
            *Mobj = Matrix44.identity;

            rend = new SimpleRenderer();
            getargs(argv);
            if (debug1 || verbose)
            {
                rend.errhandler().verbose = true;
            }

            TextureSystem texturesys = new SimpleTextureSystem();

            // Create a new shading system. We pass it the RendererServices object that services callbacks from the shading system, the TextureSystem and an
            // error handler.
            shadingsys = new ShadingSystem(rend, texturesys, rend.errhandler());
            rend.init_shadingsys(shadingsys);

            // Register the layout of all closures known to this renderer. Any closure used by the shader which is not registered, or registered with a
            // different number of arguments will lead to a runtime error.
            SimpleRenderer.register_closures(shadingsys);

            // Start the shader group and grab a reference to it.
            shadergroup = shadingsys.ShaderGroupBegin(groupname);

            // Revisit the command line arguments that we stashed to set up the shader itself.
            process_shader_setup_args();
            if (parameters.Count != 0)
            {
                Console.Error.Write("ERROR: Pending parameters without a shader:");
                foreach (ParamEntry pv in parameters)
                {
                    Console.Error.Write(" " + pv.name);
                }
                Console.Error.Write("\n");
                Console.Error.Write("Did you mistakenly put --param after the shader declaration?\n");
                return 1;
            }

            if (shadergroup == null)
            {
                Console.Error.Write("ERROR: Invalid shader group. Exiting testshade.\n");
                return 1;
            }

            // Set shading sys options again, in case late-encountered command line options change their values.
            set_shadingsys_options();

            if (groupname.Length != 0)
            {
                shadingsys.attribute(shadergroup, "groupname", groupname);
            }

            // Now set up the connections
            for (int i = 0; i < connections.Count; i += 4)
            {
                if (i + 3 < connections.Count)
                {
                    Console.Out.Write("Connect " + connections[i] + "." + connections[i + 1] + " to " + connections[i + 2] + "." + connections[i + 3] + "\n");
                    bool ok = shadingsys.ConnectShaders(shadergroup, connections[i], connections[i + 1], connections[i + 2], connections[i + 3]);
                    if (!ok)
                    {
                        return 1;
                    }
                }
            }

            // End the group
            shadingsys.ShaderGroupEnd(shadergroup);

            if (verbose || do_oslquery)
            {
                object val;
                string pickle = "";
                if (shadingsys.getattribute(shadergroup, "pickle", TypeDescs.TypeString, out val))
                {
                    pickle = val is string ps ? ps : val.ToString();
                }
                Console.Out.Write("Shader group:\n---\n" + pickle + "\n---\n");
                Console.Out.Write("\n");
                Console.Out.Write("Shader group \"" + shadergroup.name().@string() + "\" layers are:\n");
                int num_layers = shadergroup.nlayers();
                for (int i = 0; i < num_layers; ++i)
                {
                    string ln = shadergroup[i].layername().@string();
                    Console.Out.Write("    " + (ln.Length != 0 ? ln : "<unnamed>") + "\n");
                    if (do_oslquery)
                    {
                        List<bool> isoutput;
                        List<KeyValuePair<string, TypeDesc>> ps = layer_params(shadergroup, i, out isoutput);
                        for (int p = 0; p < ps.Count; ++p)
                        {
                            Console.Out.Write("\t" + (isoutput[p] ? "output " : "") + ps[p].Value.ToString() + " " + ps[p].Key + "\n");
                        }
                    }
                }
                Console.Out.Write("\n");
            }
            if (archivegroup.Length != 0)
            {
                shadingsys.archive_shadergroup(shadergroup, archivegroup);
            }

            if (outputfiles.Count != 0)
            {
                Console.Out.Write("\n");
            }

            rend.shaders().Add(shadergroup);

            // Set up the named transformations, including shader and object.
            setup_transformations(rend);

            // Set up the image outputs requested on the command line
            setup_output_images(rend, shadergroup);

            if (debug1)
            {
                test_group_attributes(shadergroup);
            }

            rend.prepare_render();
            if (warmup)
            {
                rend.warmup();
            }

            // Allow a settable number of iterations to "render" the whole image, which is useful for time trials
            for (int iter = 0; iter < iters; ++iter)
            {
                bool save = (iter == (iters - 1));  // save on last iteration
                shade_region(rend, shadergroup, 0, xres, 0, yres, save);

                // If any reparam was requested, do it now
                if (reparams.Count != 0 && reparam_layer.Length != 0 && (iter + 1 < iters))
                {
                    foreach (ParamEntry pv in reparams)
                    {
                        shadingsys.ReParameter(shadergroup, reparam_layer, pv.name, pv.type, pv.val);
                    }
                }
            }

            // Report the messages the shaders journaled, leaving out repeats
            int error_repeats = 0;
            shadingsys.getattribute("error_repeats", out error_repeats);
            bool limit_errors = error_repeats == 0;
            bool limit_warnings = error_repeats == 0;
            OSL.journal.TrackRecentlyReported tracker = new OSL.journal.TrackRecentlyReported(limit_errors, 25, limit_warnings, 25);
            TestshadeReporter reporter = new TestshadeReporter(rend.errhandler(), tracker);
            rend.process_journal(reporter);

            // This awkward condition preserves an output oddity from long ago, eliminating the need to update hundreds of ref outputs.
            if (outputfiles.Count == 1 && outputfiles[0] == "null")
            {
                Console.Out.Write("\n");
            }

            // Write the output images to disk
            rend.finalize_pixel_buffer();
            for (int i = 0; i < rend.noutputs(); ++i)
            {
                if (print_outputs || outputfiles[i] == "null")
                {
                    continue;  // don't write an image file
                }
                ImageWriter.write(rend.outputbuf(i), dataformatname);
            }

            // Print some debugging info
            if (debug1 || runstats || profile)
            {
                Console.Out.Write("\n");
                Console.Out.Write(shadingsys.getstats(5) + "\n");
            }

            if (debug1 || print_groupdata)
            {
                object val;
                int groupdata_size = 0;
                if (shadingsys.getattribute(shadergroup, "llvm_groupdata_size", TypeDescs.TypeInt, out val))
                {
                    groupdata_size = Convert.ToInt32(val);
                }
                Console.Out.Write("Groupdata size: " + groupdata_size.ToString(CultureInfo.InvariantCulture) + "\n");
            }

            // Give the renderer a chance to do initial cleanup while everything is still alive
            rend.clear();
            shadergroup = null;
            if (ilreport_file.Length != 0)
            {
                File.WriteAllText(ilreport_file, ILRuntimeTable.report());
            }
            shadingsys.Dispose();
            return 0;
        }

        public static int Main(string[] args)
        {
            try
            {
                return test_shade(args);
            }
            finally
            {
                Console.Out.Flush();
                Console.Error.Flush();
            }
        }
    }
}
