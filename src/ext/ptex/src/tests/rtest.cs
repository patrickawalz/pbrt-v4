// rtest.cpp
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Ptex;

public static unsafe class RTest
{
    static TextWriter cout;

    static string F(string fmt, params object[] a) { return string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, a); }

    static string G(double v)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        return v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    static void DumpData(Res res, DataType dt, int nchan, void* data, string prefix)
    {
        float* pixel = (float*)NativeMemory.Alloc((nuint)(sizeof(float) * nchan));
        byte* cpixel = (byte*)NativeMemory.Alloc((nuint)nchan);
        cout.Write(F("{0}data ({1} x {2}):\n", prefix, res.u(), res.v()));
        int ures = res.u(), vres = res.v();
        int pixelSize = PtexGlobals.DataSize(dt) * nchan;

        for (int vi = 0; vi < vres; vi++)
        {
            if (vi == 8 && vres > 16) { vi = vres - 8; cout.Write(prefix + "  ...\n"); }
            cout.Write(prefix + "  ");
            for (int ui = 0; ui < ures; ui++)
            {
                if (ui == 8 && ures > 16) { ui = ures - 8; cout.Write("... "); }
                byte* dpixel = (byte*)data + (vi * ures + ui) * pixelSize;
                PtexGlobals.ConvertToFloat(pixel, dpixel, dt, nchan);
                PtexGlobals.ConvertFromFloat(cpixel, pixel, DataType.dt_uint8, nchan);
                for (int c = 0; c < nchan; c++)
                {
                    cout.Write(cpixel[c].ToString("x2"));
                }
                cout.Write(" ");
            }
            cout.Write("\n");
        }

        NativeMemory.Free(cpixel);
        NativeMemory.Free(pixel);
    }

    static void DumpMetaData(PtexMetaData meta)
    {
        // Sort the meta keys before printing them out because the
        // internal key order may change depending on whether large meta
        // data support was included.  Normally the key order shouldn't
        // matter, but we don't want it to cause the regression test to fail.
        SortedDictionary<string, int> sortedkeys = new SortedDictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < meta.numKeys(); i++)
        {
            string key;
            MetaDataType type;
            meta.getKey(i, out key, out type);
            sortedkeys[key] = i;
        }

        cout.Write("meta:\n");
        foreach (KeyValuePair<string, int> iter in sortedkeys)
        {
            string key;
            MetaDataType type;
            meta.getKey(iter.Value, out key, out type);
            cout.Write("  " + key + " type=" + PtexGlobals.MetaDataTypeName(type));
            switch (type)
            {
                case MetaDataType.mdt_string:
                {
                    string val = null;
                    meta.getValue(key, out val);
                    cout.Write("  " + val);
                }
                break;
                case MetaDataType.mdt_int8:
                {
                    sbyte* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 10 == 0 && (i != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + (char)(byte)val[i]);
                    }
                }
                break;
                case MetaDataType.mdt_int16:
                {
                    short* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 10 == 0 && (i != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + val[i]);
                    }
                }
                break;
                case MetaDataType.mdt_int32:
                {
                    int* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 10 == 0 && (i != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + val[i]);
                    }
                }
                break;
                case MetaDataType.mdt_float:
                {
                    float* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 10 == 0 && (i != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + G(val[i]));
                    }
                }
                break;
                case MetaDataType.mdt_double:
                {
                    double* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int i = 0; i < count; i++)
                    {
                        if (i % 10 == 0 && (i != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + G(val[i]));
                    }
                }
                break;
            }
            cout.Write("\n");
        }
    }

    public static int Main(string[] args)
    {
        cout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 65536);
        cout.NewLine = "\n";
        int rc = Run();
        cout.Flush();
        return rc;
    }

    static int Run()
    {
        string error = "";
        PtexCache c = PtexCache.create(0, 0, false, null, null);
        c.setSearchPath("foo/bar:.");
        PtexTexture r = c.get("test.ptx", ref error);

        if (r == null)
        {
            cout.Flush();
            Console.Error.WriteLine(error);
            return 1;
        }
        cout.Write("meshType: " + PtexGlobals.MeshTypeName(r.meshType()) + "\n");
        cout.Write("dataType: " + PtexGlobals.DataTypeName(r.dataType()) + "\n");
        cout.Write("numChannels: " + r.numChannels() + "\n");
        cout.Write("alphaChannel: ");
        if (r.alphaChannel() == -1) cout.Write("(none)\n");
        else cout.Write(r.alphaChannel() + "\n");
        cout.Write("numFaces: " + r.numFaces() + "\n");

        PtexMetaData meta = r.getMetaData();
        cout.Write("numMetaKeys: " + meta.numKeys() + "\n");
        if (meta.numKeys() != 0) DumpMetaData(meta);
        meta.release();

        int nfaces = r.numFaces();
        for (int i = 0; i < nfaces; i++)
        {
            FaceInfo f = r.getFaceInfo(i);
            cout.Write("face " + i + ":\n"
                       + "  res: " + (int)f.res.ulog2 + ' ' + (int)f.res.vlog2 + "\n"
                       + "  adjface: "
                       + f.adjfaces[0] + ' '
                       + f.adjfaces[1] + ' '
                       + f.adjfaces[2] + ' '
                       + f.adjfaces[3] + "\n"
                       + "  adjedge: "
                       + (int)f.adjedge(0) + ' '
                       + (int)f.adjedge(1) + ' '
                       + (int)f.adjedge(2) + ' '
                       + (int)f.adjedge(3) + "\n"
                       + "  flags: " + (int)f.flags + "\n");

            Res res = f.res;
            void* data = NativeMemory.Alloc((nuint)(PtexGlobals.DataSize(r.dataType()) * r.numChannels() * res.size()));
            while (res.ulog2 > 0 || res.vlog2 > 0)
            {
                r.getData(i, data, 0, res);
                DumpData(res, r.dataType(), r.numChannels(), data, "  ");
                if (res.ulog2 != 0) res.ulog2--;
                if (res.vlog2 != 0) res.vlog2--;
            }
            r.getData(i, data, 0, new Res((sbyte)0, (sbyte)0));
            DumpData(res, r.dataType(), r.numChannels(), data, "  ");
            NativeMemory.Free(data);

            // Read more channels than are available.
            // This should wipe pixel with zeroes and nothing more.
            float* pixel = stackalloc float[3];
            pixel[0] = -1.0f; pixel[1] = -1.0f; pixel[2] = -1.0f;
            r.getPixel(i, 1, 1, pixel, 3, 3);
            if (pixel[0] != 0.0f || pixel[1] != 0.0f || pixel[2] != 0.0f)
            {
                cout.Flush();
                Console.Error.WriteLine("pixel should be zero");
                return 1;
            }
        }

        r.release();
        c.release();
        return 0;
    }
}
