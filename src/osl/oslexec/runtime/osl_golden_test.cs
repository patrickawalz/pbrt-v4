// (not in OpenShadingLanguage) osl_golden_test.cs
//
// Runs rows of golden data against the OslRuntime entry points, which it finds by name through reflection the way the IL backend binds them. The rows were
// produced by calling the real C++ extern "C" functions (built from the OSL sources) on the same inputs.

using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using OIIO;

namespace OSL
{
    namespace pvt
    {
        public static unsafe class GoldenRuntime
        {
            private const int BufferWords = 128;

            private static uint ParseHex(string s)
            {
                return uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            private static string Hex(float f)
            {
                return BitConverter.SingleToUInt32Bits(f).ToString("x8");
            }

            // The fill used by "b:N" buffers; the C++ side uses the same generator
            private static void FillFromSeed(float* b, uint seed)
            {
                uint st = seed * 2654435761u + 12345u;
                for (int i = 0; i < BufferWords; ++i)
                {
                    st = st * 1664525u + 1013904223u;
                    b[i] = (st >> 8) / 16777216.0f * 4.0f - 2.0f;
                }
            }

            private static string Call(string row, out string prefix)
            {
                int bar1 = row.IndexOf('|');
                int bar2 = row.IndexOf('|', bar1 + 1);
                string name = row.Substring(0, bar1);
                string argText = row.Substring(bar1 + 1, bar2 - bar1 - 1);
                prefix = row.Substring(0, bar2 + 1);
                string[] tokens = argText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                MethodInfo method = typeof(OslRuntime).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                if (method == null)
                {
                    return " missing";
                }
                object[] args = new object[tokens.Length];
                IntPtr[] buffers = new IntPtr[tokens.Length];
                float[][] initial = new float[tokens.Length][];
                try
                {
                    for (int k = 0; k < tokens.Length; ++k)
                    {
                        string tk = tokens[k];
                        if (tk.StartsWith("f:"))
                        {
                            args[k] = BitConverter.UInt32BitsToSingle(ParseHex(tk.Substring(2)));
                        }
                        else if (tk.StartsWith("i:"))
                        {
                            args[k] = (int)ParseHex(tk.Substring(2));
                        }
                        else if (tk.StartsWith("s:"))
                        {
                            args[k] = new ustring(tk.Substring(2)).hash();
                        }
                        else if (tk == "n")
                        {
                            args[k] = Pointer.Box((void*)0, typeof(void*));
                        }
                        else
                        {
                            buffers[k] = Marshal.AllocHGlobal(BufferWords * sizeof(float));
                            float* b = (float*)buffers[k];
                            if (tk.StartsWith("b:"))
                            {
                                FillFromSeed(b, uint.Parse(tk.Substring(2), CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                string[] words = tk.Substring(2).Split(',');
                                for (int i = 0; i < BufferWords; ++i)
                                {
                                    b[i] = i < words.Length ? BitConverter.UInt32BitsToSingle(ParseHex(words[i])) : 0.0f;
                                }
                            }
                            initial[k] = new float[BufferWords];
                            for (int i = 0; i < BufferWords; ++i)
                            {
                                initial[k][i] = b[i];
                            }
                            args[k] = Pointer.Box((void*)buffers[k], typeof(void*));
                        }
                    }

                    object result = method.Invoke(null, args);
                    StringBuilder sb = new StringBuilder();
                    if (result is float)
                    {
                        sb.Append(" r:").Append(Hex((float)result));
                    }
                    else if (result is int)
                    {
                        sb.Append(" r:").Append(((uint)(int)result).ToString("x8"));
                    }
                    for (int k = 0; k < tokens.Length; ++k)
                    {
                        if (buffers[k] == IntPtr.Zero)
                        {
                            continue;
                        }
                        float* b = (float*)buffers[k];
                        bool changed = false;
                        for (int i = 0; i < BufferWords; ++i)
                        {
                            if (BitConverter.SingleToUInt32Bits(b[i]) != BitConverter.SingleToUInt32Bits(initial[k][i]))
                            {
                                changed = true;
                            }
                        }
                        if (changed)
                        {
                            sb.Append(" k").Append(k).Append(':');
                            for (int i = 0; i < 9; ++i)
                            {
                                sb.Append(i != 0 ? "," : "").Append(Hex(b[i]));
                            }
                        }
                    }
                    return sb.ToString();
                }
                finally
                {
                    for (int k = 0; k < buffers.Length; ++k)
                    {
                        if (buffers[k] != IntPtr.Zero)
                        {
                            Marshal.FreeHGlobal(buffers[k]);
                        }
                    }
                }
            }

            // Returns a description of every row that does not reproduce, empty when all do
            public static string Check(string[] rows)
            {
                StringBuilder failures = new StringBuilder();
                int count = 0;
                for (int r = 0; r < rows.Length; ++r)
                {
                    string prefix;
                    string got = prefix_row(rows[r], out prefix);
                    if (got != rows[r])
                    {
                        if (count < 10)
                        {
                            failures.Append("\n  expected ").Append(rows[r]).Append("\n  got      ").Append(got);
                        }
                        ++count;
                    }
                }
                if (count != 0)
                {
                    return count + " of " + rows.Length + " rows differ:" + failures.ToString();
                }
                return "";
            }

            private static string prefix_row(string row, out string prefix)
            {
                string tail = Call(row, out prefix);
                return prefix + tail;
            }
        }
    }
}
