// A C# implementation of the subset of the RPly API (Diego Nehab, MIT license) that pbrt-v4
// uses for reading and writing PLY files: ASCII and binary (little/big endian) files with
// scalar and list properties, read through per-property callbacks.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace pbrt
{
    public enum e_ply_storage_mode { PLY_BIG_ENDIAN, PLY_LITTLE_ENDIAN, PLY_ASCII, PLY_DEFAULT }

    public enum e_ply_type
    {
        PLY_INT8, PLY_UINT8, PLY_INT16, PLY_UINT16, PLY_INT32, PLY_UIN32, PLY_FLOAT32, PLY_FLOAT64,
        PLY_CHAR, PLY_UCHAR, PLY_SHORT, PLY_USHORT, PLY_INT, PLY_UINT, PLY_FLOAT, PLY_DOUBLE,
        PLY_LIST
    }

    public class p_ply_property
    {
        public string name;
        public e_ply_type type, length_type, value_type;
        public Func<p_ply_argument, int> read_cb;
        public object pdata;
        public long idata;
    }

    public class p_ply_element
    {
        public string name;
        public long ninstances;
        public List<p_ply_property> property = new List<p_ply_property>();
    }

    public class p_ply_argument
    {
        public p_ply_element element;
        public long instance_index;
        public p_ply_property property;
        public long length, value_index;
        public double value;
        public object pdata;
        public long idata;
    }

    public class p_ply
    {
        public e_ply_storage_mode storage_mode;
        public List<p_ply_element> element = new List<p_ply_element>();
        public List<string> comment = new List<string>();
        public List<string> obj_info = new List<string>();
        public byte[] data;       // reading: the whole file
        public int pos;           // reading: current position
        public Action<p_ply, string> error_cb;

        // writing
        public Stream fp;
        public int welement, wproperty;
        public long winstance_index, wvalue_index, wlength;
        public bool headerWritten;
    }

    public static class rply
    {
        public const e_ply_storage_mode PLY_DEFAULT = e_ply_storage_mode.PLY_DEFAULT;
        public const e_ply_storage_mode PLY_ASCII = e_ply_storage_mode.PLY_ASCII;
        public const e_ply_storage_mode PLY_LITTLE_ENDIAN = e_ply_storage_mode.PLY_LITTLE_ENDIAN;
        public const e_ply_storage_mode PLY_BIG_ENDIAN = e_ply_storage_mode.PLY_BIG_ENDIAN;

        private static void ply_error(p_ply ply, string message)
        {
            if (ply.error_cb != null) ply.error_cb(ply, message);
        }

        private static readonly string[] ply_type_list = {
            "int8",    "uint8",  "int16", "uint16", "int32",  "uint32", "float32",
            "float64", "char",   "uchar", "short",  "ushort", "int",    "uint",
            "float",   "double", "list" }; /* order matches e_ply_type enum */

        private static e_ply_type TypeFromName(string s)
        {
            for (int i = 0; i < ply_type_list.Length; i++)
                if (ply_type_list[i] == s) return (e_ply_type)i;
            return (e_ply_type)(-1);
        }

        private static string TypeName(e_ply_type t)
        {
            return ply_type_list[(int)t];
        }

        private static int TypeSize(e_ply_type t)
        {
            switch (t)
            {
                case e_ply_type.PLY_INT8: case e_ply_type.PLY_UINT8: case e_ply_type.PLY_CHAR: case e_ply_type.PLY_UCHAR: return 1;
                case e_ply_type.PLY_INT16: case e_ply_type.PLY_UINT16: case e_ply_type.PLY_SHORT: case e_ply_type.PLY_USHORT: return 2;
                case e_ply_type.PLY_INT32: case e_ply_type.PLY_UIN32: case e_ply_type.PLY_INT: case e_ply_type.PLY_UINT:
                case e_ply_type.PLY_FLOAT32: case e_ply_type.PLY_FLOAT: return 4;
                default: return 8;
            }
        }

        ///////////////////////////////////////////////////////////////////////
        // Reading

        public static p_ply ply_open(string name, Action<p_ply, string> error_cb, long idata, object pdata)
        {
            p_ply ply = new p_ply();
            ply.error_cb = error_cb;
            try { ply.data = File.ReadAllBytes(name); }
            catch (Exception) { ply_error(ply, "Unable to open file"); return null; }
            return ply;
        }

        private static string ReadLine(p_ply ply)
        {
            int s = ply.pos;
            while (ply.pos < ply.data.Length && ply.data[ply.pos] != '\n') ++ply.pos;
            string line = Encoding.ASCII.GetString(ply.data, s, ply.pos - s).TrimEnd('\r');
            if (ply.pos < ply.data.Length) ++ply.pos;
            return line;
        }

        public static int ply_read_header(p_ply ply)
        {
            ply.pos = 0;
            if (ReadLine(ply) != "ply") { ply_error(ply, "Invalid file format"); return 0; }
            string fmt = ReadLine(ply);
            string[] f = fmt.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (f.Length < 2 || f[0] != "format") { ply_error(ply, "Invalid file format"); return 0; }
            switch (f[1])
            {
                case "ascii": ply.storage_mode = e_ply_storage_mode.PLY_ASCII; break;
                case "binary_little_endian": ply.storage_mode = e_ply_storage_mode.PLY_LITTLE_ENDIAN; break;
                case "binary_big_endian": ply.storage_mode = e_ply_storage_mode.PLY_BIG_ENDIAN; break;
                default: ply_error(ply, "Invalid file format"); return 0;
            }
            while (true)
            {
                if (ply.pos >= ply.data.Length) { ply_error(ply, "Unexpected end of header"); return 0; }
                string line = ReadLine(ply);
                string[] w = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length == 0) continue;
                if (w[0] == "end_header") break;
                if (w[0] == "comment") { ply.comment.Add(line.Length > 8 ? line.Substring(8) : ""); continue; }
                if (w[0] == "obj_info") { ply.obj_info.Add(line.Length > 9 ? line.Substring(9) : ""); continue; }
                if (w[0] == "element" && w.Length >= 3)
                {
                    p_ply_element e = new p_ply_element();
                    e.name = w[1];
                    e.ninstances = long.Parse(w[2], CultureInfo.InvariantCulture);
                    ply.element.Add(e);
                    continue;
                }
                if (w[0] == "property" && ply.element.Count > 0)
                {
                    p_ply_property p = new p_ply_property();
                    if (w.Length >= 5 && w[1] == "list")
                    {
                        p.type = e_ply_type.PLY_LIST;
                        p.length_type = TypeFromName(w[2]);
                        p.value_type = TypeFromName(w[3]);
                        p.name = w[4];
                        if ((int)p.length_type < 0 || (int)p.value_type < 0) { ply_error(ply, "Invalid property type"); return 0; }
                    }
                    else if (w.Length >= 3)
                    {
                        p.type = TypeFromName(w[1]);
                        p.name = w[2];
                        if ((int)p.type < 0) { ply_error(ply, "Invalid property type"); return 0; }
                    }
                    else { ply_error(ply, "Invalid property"); return 0; }
                    ply.element[ply.element.Count - 1].property.Add(p);
                    continue;
                }
                ply_error(ply, "Unexpected token in header: " + w[0]);
                return 0;
            }
            return 1;
        }

        public static p_ply_element ply_get_next_element(p_ply ply, p_ply_element last)
        {
            if (last == null) return ply.element.Count > 0 ? ply.element[0] : null;
            int i = ply.element.IndexOf(last);
            return (i >= 0 && i + 1 < ply.element.Count) ? ply.element[i + 1] : null;
        }

        public static int ply_get_element_info(p_ply_element element, out string name, out long ninstances)
        {
            name = element.name;
            ninstances = element.ninstances;
            return 1;
        }

        public static long ply_set_read_cb(p_ply ply, string element_name, string property_name,
                                           Func<p_ply_argument, int> read_cb, object pdata, long idata)
        {
            foreach (p_ply_element e in ply.element)
            {
                if (e.name != element_name) continue;
                foreach (p_ply_property p in e.property)
                {
                    if (p.name != property_name) continue;
                    p.read_cb = read_cb;
                    p.pdata = pdata;
                    p.idata = idata;
                    return e.ninstances;
                }
            }
            return 0;
        }

        public static int ply_get_argument_user_data(p_ply_argument argument, out object pdata, out long idata)
        {
            pdata = argument.pdata;
            idata = argument.idata;
            return 1;
        }

        public static int ply_get_argument_element(p_ply_argument argument, out p_ply_element element, out long instance_index)
        {
            element = argument.element;
            instance_index = argument.instance_index;
            return 1;
        }

        public static int ply_get_argument_property(p_ply_argument argument, out p_ply_property property,
                                                    out long length, out long value_index)
        {
            property = argument.property;
            length = argument.length;
            value_index = argument.value_index;
            return 1;
        }

        public static double ply_get_argument_value(p_ply_argument argument)
        {
            return argument.value;
        }

        private static string NextToken(p_ply ply)
        {
            while (ply.pos < ply.data.Length && (ply.data[ply.pos] == ' ' || ply.data[ply.pos] == '\n' ||
                                                 ply.data[ply.pos] == '\r' || ply.data[ply.pos] == '\t'))
                ++ply.pos;
            if (ply.pos >= ply.data.Length) return null;
            int s = ply.pos;
            while (ply.pos < ply.data.Length && !(ply.data[ply.pos] == ' ' || ply.data[ply.pos] == '\n' ||
                                                  ply.data[ply.pos] == '\r' || ply.data[ply.pos] == '\t'))
                ++ply.pos;
            return Encoding.ASCII.GetString(ply.data, s, ply.pos - s);
        }

        private static bool ReadValue(p_ply ply, e_ply_type t, out double v)
        {
            v = 0;
            if (ply.storage_mode == e_ply_storage_mode.PLY_ASCII)
            {
                string tok = NextToken(ply);
                if (tok == null) return false;
                return double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            }
            int sz = TypeSize(t);
            if (ply.pos + sz > ply.data.Length) return false;
            byte[] b = new byte[sz];
            Array.Copy(ply.data, ply.pos, b, 0, sz);
            ply.pos += sz;
            bool fileLittle = ply.storage_mode == e_ply_storage_mode.PLY_LITTLE_ENDIAN;
            if (fileLittle != BitConverter.IsLittleEndian) Array.Reverse(b);
            switch (t)
            {
                case e_ply_type.PLY_INT8: case e_ply_type.PLY_CHAR: v = (sbyte)b[0]; break;
                case e_ply_type.PLY_UINT8: case e_ply_type.PLY_UCHAR: v = b[0]; break;
                case e_ply_type.PLY_INT16: case e_ply_type.PLY_SHORT: v = BitConverter.ToInt16(b, 0); break;
                case e_ply_type.PLY_UINT16: case e_ply_type.PLY_USHORT: v = BitConverter.ToUInt16(b, 0); break;
                case e_ply_type.PLY_INT32: case e_ply_type.PLY_INT: v = BitConverter.ToInt32(b, 0); break;
                case e_ply_type.PLY_UIN32: case e_ply_type.PLY_UINT: v = BitConverter.ToUInt32(b, 0); break;
                case e_ply_type.PLY_FLOAT32: case e_ply_type.PLY_FLOAT: v = BitConverter.ToSingle(b, 0); break;
                default: v = BitConverter.ToDouble(b, 0); break;
            }
            return true;
        }

        public static int ply_read(p_ply ply)
        {
            foreach (p_ply_element e in ply.element)
            {
                for (long inst = 0; inst < e.ninstances; ++inst)
                {
                    foreach (p_ply_property p in e.property)
                    {
                        p_ply_argument arg = new p_ply_argument();
                        arg.element = e; arg.instance_index = inst; arg.property = p;
                        arg.pdata = p.pdata; arg.idata = p.idata;
                        if (p.type == e_ply_type.PLY_LIST)
                        {
                            double len;
                            if (!ReadValue(ply, p.length_type, out len)) { ply_error(ply, "Error reading '" + p.name + "' of '" + e.name + "' number " + inst); return 0; }
                            arg.length = (long)len;
                            arg.value_index = -1;
                            arg.value = len;
                            if (p.read_cb != null && p.read_cb(arg) == 0) { ply_error(ply, "Aborted by user"); return 0; }
                            for (long j = 0; j < (long)len; ++j)
                            {
                                double v;
                                if (!ReadValue(ply, p.value_type, out v)) { ply_error(ply, "Error reading '" + p.name + "' of '" + e.name + "' number " + inst); return 0; }
                                arg.value_index = j;
                                arg.value = v;
                                if (p.read_cb != null && p.read_cb(arg) == 0) { ply_error(ply, "Aborted by user"); return 0; }
                            }
                        }
                        else
                        {
                            double v;
                            if (!ReadValue(ply, p.type, out v)) { ply_error(ply, "Error reading '" + p.name + "' of '" + e.name + "' number " + inst); return 0; }
                            arg.length = 1;
                            arg.value_index = 0;
                            arg.value = v;
                            if (p.read_cb != null && p.read_cb(arg) == 0) { ply_error(ply, "Aborted by user"); return 0; }
                        }
                    }
                }
            }
            return 1;
        }

        ///////////////////////////////////////////////////////////////////////
        // Writing

        public static p_ply ply_create(string name, e_ply_storage_mode storage_mode, Action<p_ply, string> error_cb,
                                       long idata, object pdata)
        {
            p_ply ply = new p_ply();
            ply.error_cb = error_cb;
            ply.storage_mode = storage_mode == e_ply_storage_mode.PLY_DEFAULT
                                   ? (BitConverter.IsLittleEndian ? e_ply_storage_mode.PLY_LITTLE_ENDIAN : e_ply_storage_mode.PLY_BIG_ENDIAN)
                                   : storage_mode;
            try { ply.fp = new FileStream(name, FileMode.Create, FileAccess.Write); }
            catch (Exception) { ply_error(ply, "Unable to create file"); return null; }
            return ply;
        }

        public static int ply_add_element(p_ply ply, string name, long ninstances)
        {
            p_ply_element e = new p_ply_element();
            e.name = name;
            e.ninstances = ninstances;
            ply.element.Add(e);
            return 1;
        }

        public static int ply_add_scalar_property(p_ply ply, string name, e_ply_type type)
        {
            if (ply.element.Count == 0) return 0;
            p_ply_property p = new p_ply_property();
            p.name = name; p.type = type;
            ply.element[ply.element.Count - 1].property.Add(p);
            return 1;
        }

        public static int ply_add_list_property(p_ply ply, string name, e_ply_type length_type, e_ply_type value_type)
        {
            if (ply.element.Count == 0) return 0;
            p_ply_property p = new p_ply_property();
            p.name = name; p.type = e_ply_type.PLY_LIST; p.length_type = length_type; p.value_type = value_type;
            ply.element[ply.element.Count - 1].property.Add(p);
            return 1;
        }

        private static void WriteString(p_ply ply, string s)
        {
            byte[] b = Encoding.ASCII.GetBytes(s);
            ply.fp.Write(b, 0, b.Length);
        }

        public static int ply_write_header(p_ply ply)
        {
            WriteString(ply, "ply\n");
            switch (ply.storage_mode)
            {
                case e_ply_storage_mode.PLY_ASCII: WriteString(ply, "format ascii 1.0\n"); break;
                case e_ply_storage_mode.PLY_LITTLE_ENDIAN: WriteString(ply, "format binary_little_endian 1.0\n"); break;
                default: WriteString(ply, "format binary_big_endian 1.0\n"); break;
            }
            foreach (string c in ply.comment) WriteString(ply, "comment " + c + "\n");
            foreach (string c in ply.obj_info) WriteString(ply, "obj_info " + c + "\n");
            foreach (p_ply_element e in ply.element)
            {
                WriteString(ply, "element " + e.name + " " + e.ninstances + "\n");
                foreach (p_ply_property p in e.property)
                {
                    if (p.type == e_ply_type.PLY_LIST)
                        WriteString(ply, "property list " + TypeName(p.length_type) + " " + TypeName(p.value_type) + " " + p.name + "\n");
                    else
                        WriteString(ply, "property " + TypeName(p.type) + " " + p.name + "\n");
                }
            }
            WriteString(ply, "end_header\n");
            ply.welement = 0; ply.wproperty = 0; ply.winstance_index = 0; ply.wvalue_index = -1; ply.wlength = 0;
            ply.headerWritten = true;
            return 1;
        }

        private static void WriteValue(p_ply ply, e_ply_type t, double v)
        {
            if (ply.storage_mode == e_ply_storage_mode.PLY_ASCII)
            {
                bool isFloat = t == e_ply_type.PLY_FLOAT32 || t == e_ply_type.PLY_FLOAT || t == e_ply_type.PLY_FLOAT64 || t == e_ply_type.PLY_DOUBLE;
                WriteString(ply, (isFloat ? ((float)v).ToString("R", CultureInfo.InvariantCulture) : ((long)v).ToString(CultureInfo.InvariantCulture)) + " ");
                return;
            }
            byte[] b;
            switch (t)
            {
                case e_ply_type.PLY_INT8: case e_ply_type.PLY_CHAR: b = new byte[] { unchecked((byte)(sbyte)v) }; break;
                case e_ply_type.PLY_UINT8: case e_ply_type.PLY_UCHAR: b = new byte[] { (byte)v }; break;
                case e_ply_type.PLY_INT16: case e_ply_type.PLY_SHORT: b = BitConverter.GetBytes((short)v); break;
                case e_ply_type.PLY_UINT16: case e_ply_type.PLY_USHORT: b = BitConverter.GetBytes((ushort)v); break;
                case e_ply_type.PLY_INT32: case e_ply_type.PLY_INT: b = BitConverter.GetBytes((int)v); break;
                case e_ply_type.PLY_UIN32: case e_ply_type.PLY_UINT: b = BitConverter.GetBytes((uint)v); break;
                case e_ply_type.PLY_FLOAT32: case e_ply_type.PLY_FLOAT: b = BitConverter.GetBytes((float)v); break;
                default: b = BitConverter.GetBytes(v); break;
            }
            bool fileLittle = ply.storage_mode == e_ply_storage_mode.PLY_LITTLE_ENDIAN;
            if (fileLittle != BitConverter.IsLittleEndian) Array.Reverse(b);
            ply.fp.Write(b, 0, b.Length);
        }

        public static int ply_write(p_ply ply, double value)
        {
            if (ply.welement >= ply.element.Count) return 0;
            p_ply_element e = ply.element[ply.welement];
            p_ply_property p = e.property[ply.wproperty];
            bool endOfProperty;
            if (p.type == e_ply_type.PLY_LIST)
            {
                if (ply.wvalue_index < 0)
                {
                    // the length
                    WriteValue(ply, p.length_type, value);
                    ply.wlength = (long)value;
                    ply.wvalue_index = 0;
                    endOfProperty = ply.wlength == 0;
                }
                else
                {
                    WriteValue(ply, p.value_type, value);
                    ++ply.wvalue_index;
                    endOfProperty = ply.wvalue_index >= ply.wlength;
                }
            }
            else
            {
                WriteValue(ply, p.type, value);
                endOfProperty = true;
            }
            if (endOfProperty)
            {
                ply.wvalue_index = -1;
                if (++ply.wproperty >= e.property.Count)
                {
                    ply.wproperty = 0;
                    if (ply.storage_mode == e_ply_storage_mode.PLY_ASCII) WriteString(ply, "\n");
                    if (++ply.winstance_index >= e.ninstances)
                    {
                        ply.winstance_index = 0;
                        ++ply.welement;
                    }
                }
            }
            return 1;
        }

        public static int ply_close(p_ply ply)
        {
            if (ply.fp != null)
            {
                ply.fp.Flush();
                ply.fp.Dispose();
                ply.fp = null;
            }
            ply.data = null;
            return 1;
        }
    }
}
