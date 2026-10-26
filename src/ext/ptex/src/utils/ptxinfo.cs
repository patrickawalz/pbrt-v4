// ptxinfo.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

using System;
using System.IO;
using System.Text;
using Ptex;

public static unsafe class PtxInfo
{
    static TextWriter cout;

    // C++ ostream default formatting of a floating point value (%g, 6 significant digits)
    static string G(double v)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
        return v.ToString("G6", System.Globalization.CultureInfo.InvariantCulture).Replace('E', 'e');
    }

    static void DumpFaceInfo(in FaceInfo f)
    {
        Res res = f.res;
        cout.Write("  res: " + (int)res.ulog2 + ' ' + (int)res.vlog2
                   + " (" + res.u() + " x " + res.v() + ")"
                   + "  adjface: "
                   + f.adjface(0) + ' '
                   + f.adjface(1) + ' '
                   + f.adjface(2) + ' '
                   + f.adjface(3)
                   + "  adjedge: "
                   + (int)f.adjedge(0) + ' '
                   + (int)f.adjedge(1) + ' '
                   + (int)f.adjedge(2) + ' '
                   + (int)f.adjedge(3)
                   + "  flags:");

        // output flag names
        if (f.flags == 0) cout.Write(" (none)");
        else
        {
            if (f.isSubface()) cout.Write(" subface");
            if (f.isConstant()) cout.Write(" constant");
            if (f.isNeighborhoodConstant()) cout.Write(" nbconstant");
            if (f.hasEdits()) cout.Write(" hasedits");
        }
        cout.Write("\n");
    }

    static void DumpTiling(PtexFaceData dh)
    {
        cout.Write("  tiling: ");
        if (dh.isTiled())
        {
            Res res = dh.tileRes();
            cout.Write("ntiles = " + dh.res().ntiles(res)
                       + ", res = "
                       + (int)res.ulog2 + ' ' + (int)res.vlog2
                       + " (" + res.u() + " x " + res.v() + ")\n");
        }
        else if (dh.isConstant())
        {
            cout.Write("  (constant)\n");
        }
        else
        {
            cout.Write("  (untiled)\n");
        }
    }

    static void DumpData(PtexTexture r, int faceid, bool dumpall)
    {
        int levels = 1;
        if (dumpall)
        {
            PtexReader R = (PtexReader)r;
            if (R != null) levels = R.header().nlevels;
        }

        FaceInfo f = r.getFaceInfo(faceid);
        int nchan = r.numChannels();
        float* pixel = (float*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)(sizeof(float) * nchan));
        Res res = f.res;
        while (levels != 0 && res.ulog2 >= 1 && res.vlog2 >= 1)
        {
            int ures = res.u(), vres = res.v();
            cout.Write("  data (" + ures + " x " + vres + ")");
            if (f.isConstant()) { ures = vres = 1; }
            bool isconst = (ures == 1 && vres == 1);
            if (isconst) cout.Write(", const: ");
            else cout.Write(":");
            for (int vi = 0; vi < vres; vi++)
            {
                for (int ui = 0; ui < ures; ui++)
                {
                    if (!isconst) cout.Write("\n    (" + ui + ", " + vi + "): ");
                    r.getPixel(faceid, ui, vi, pixel, 0, nchan, res);
                    for (int c = 0; c < nchan; c++)
                    {
                        cout.Write(" " + ((double)pixel[c]).ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
                    }
                }
            }
            cout.Write("\n");
            res.ulog2--;
            res.vlog2--;
            levels--;
        }
        System.Runtime.InteropServices.NativeMemory.Free(pixel);
    }

    static void DumpMetaData(PtexMetaData meta)
    {
        cout.Write("meta:\n");
        for (int i = 0; i < meta.numKeys(); i++)
        {
            string key;
            MetaDataType type;
            meta.getKey(i, out key, out type);
            cout.Write("  " + key + " type=" + PtexGlobals.MetaDataTypeName(type));
            switch (type)
            {
                case MetaDataType.mdt_string:
                {
                    string val = null;
                    meta.getValue(key, out val);
                    cout.Write("  \"" + val + "\"");
                }
                break;
                case MetaDataType.mdt_int8:
                {
                    sbyte* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int j = 0; j < count; j++)
                    {
                        if (j % 10 == 0 && (j != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + (char)(byte)val[j]);  // int8_t prints as a character
                    }
                }
                break;
                case MetaDataType.mdt_int16:
                {
                    short* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int j = 0; j < count; j++)
                    {
                        if (j % 10 == 0 && (j != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + val[j]);
                    }
                }
                break;
                case MetaDataType.mdt_int32:
                {
                    int* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int j = 0; j < count; j++)
                    {
                        if (j % 10 == 0 && (j != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + val[j]);
                    }
                }
                break;
                case MetaDataType.mdt_float:
                {
                    float* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int j = 0; j < count; j++)
                    {
                        if (j % 10 == 0 && (j != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + G(val[j]));
                    }
                }
                break;
                case MetaDataType.mdt_double:
                {
                    double* val; int count;
                    meta.getValue(key, out val, out count);
                    for (int j = 0; j < count; j++)
                    {
                        if (j % 10 == 0 && (j != 0 || count > 10)) cout.Write("\n  ");
                        cout.Write("  " + G(val[j]));
                    }
                }
                break;
            }
            cout.Write("\n");
        }
    }

    static void DumpInternal(PtexTexture tx)
    {
        PtexReader r = (PtexReader)tx;
        Header h = r.header();
        ExtHeader eh = r.extheader();
        cout.Write("Header:\n"
                   + "  magic: ");
        if (h.magic == PtexIO.Magic)
            cout.Write("'Ptex'\n");
        else
            cout.Write(h.magic + "\n");

        cout.Write("  version: " + h.version + '.' + h.minorversion + "\n"
                   + "  meshtype: " + h.meshtype + "\n"
                   + "  datatype: " + h.datatype + "\n"
                   + "  alphachan: " + h.alphachan + "\n"
                   + "  nchannels: " + h.nchannels + "\n"
                   + "  nlevels: " + h.nlevels + "\n"
                   + "  nfaces: " + h.nfaces + "\n"
                   + "  extheadersize: " + h.extheadersize + "\n"
                   + "  faceinfosize: " + h.faceinfosize + "\n"
                   + "  constdatasize: " + h.constdatasize + "\n"
                   + "  levelinfosize: " + h.levelinfosize + "\n"
                   + "  leveldatasize: " + h.leveldatasize + "\n"
                   + "  metadatazipsize: " + h.metadatazipsize + "\n"
                   + "  metadatamemsize: " + h.metadatamemsize + "\n"
                   + "  ubordermode: " + eh.ubordermode + "\n"
                   + "  vbordermode: " + eh.vbordermode + "\n"
                   + "  lmdheaderzipsize: " + eh.lmdheaderzipsize + "\n"
                   + "  lmdheadermemsize: " + eh.lmdheadermemsize + "\n"
                   + "  lmddatasize: " + eh.lmddatasize + "\n"
                   + "  editdatasize: " + eh.editdatasize + "\n"
                   + "  editdatapos: " + eh.editdatapos + "\n");

        cout.Write("Level info:\n");
        for (int i = 0; i < h.nlevels; i++)
        {
            LevelInfo l = r.levelinfo(i);
            cout.Write("  Level " + i + "\n"
                       + "    leveldatasize: " + l.leveldatasize + "\n"
                       + "    levelheadersize: " + l.levelheadersize + "\n"
                       + "    nfaces: " + l.nfaces + "\n");
        }
    }

    static int CheckAdjacency(PtexTexture tx)
    {
        int result = 0;
        bool noinfo = true;
        for (int fid = 0; fid < tx.numFaces(); fid++)
        {
            FaceInfo finfo = tx.getFaceInfo(fid);
            for (int e = 0; e < 4; ++e)
            {
                if (finfo.adjface(e) >= 0)
                {
                    noinfo = false;
                    FaceInfo adjf = tx.getFaceInfo(finfo.adjface(e));
                    int oppfid = adjf.adjface((int)finfo.adjedge(e));

                    // trivial match
                    if (oppfid == fid)
                        continue;

                    // subface case
                    if (finfo.isSubface() && !adjf.isSubface())
                    {
                        // neighbor face might be pointing to "other" subface
                        if (oppfid == finfo.adjface((e + 1) % 4))
                            continue;
                    }

                    Console.Error.Write("face " + fid + " edge " + e
                                        + " has incorrect adjacency\n");
                    ++result;
                }
            }
        }
        if (noinfo)
        {
            Console.Error.Write("\"" + tx.path() + "\" does not appear to have"
                                + "any adjacency information.\n");
            ++result;
        }
        if (result == 0)
        {
            cout.Write("Adjacency information appears consistent.\n");
        }
        return result;
    }

    static void usage()
    {
        Console.Error.Write("Usage: ptxinfo [options] file\n"
                            + "  -v Show ptex software version\n"
                            + "  -m Dump meta data\n"
                            + "  -f Dump face info\n"
                            + "  -d Dump data\n"
                            + "  -D Dump data for all mipmap levels\n"
                            + "  -t Dump tiling info\n"
                            + "  -i Dump internal info\n"
                            + "  -c Check validity of adjacency data\n");
        cout.Flush();
        Environment.Exit(1);
    }

    public static int Main(string[] args)
    {
        cout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 65536);
        cout.NewLine = "\n";
        int rc = Run(args);
        cout.Flush();
        return rc;
    }

    static int Run(string[] args)
    {
        bool showver = false;
        bool dumpmeta = false;
        bool dumpfaceinfo = false;
        bool dumpdata = false;
        bool dumpalldata = false;
        bool dumpinternal = false;
        bool dumptiling = false;
        bool checkadjacency = false;
        string fname = null;

        for (int ai = 0; ai < args.Length; ai++)
        {
            string arg = args[ai];
            if (arg.Length > 0 && arg[0] == '-')
            {
                int cp = 1;
                if (cp >= arg.Length) usage();  // handle bare '-'
                while (cp < arg.Length)
                {
                    switch (arg[cp++])
                    {
                        case 'v': showver = true; break;
                        case 'm': dumpmeta = true; break;
                        case 'd': dumpdata = true; break;
                        case 'D': dumpdata = true; dumpalldata = true; break;
                        case 'f': dumpfaceinfo = true; break;
                        case 't': dumptiling = true; break;
                        case 'i': dumpinternal = true; break;
                        case 'c': checkadjacency = true; break;
                        default: usage(); break;
                    }
                }
            }
            else if (fname != null) usage();
            else fname = arg;
        }

        if (showver)
        {
            cout.Write("Ptex v" + PtexVersion.PtexLibraryMajorVersion + "." + PtexVersion.PtexLibraryMinorVersion + "\n");
        }

        if (fname == null)
        {
            if (!showver) usage();
            return 0;
        }

        string error = "";
        PtexTexture r = PtexTexture.open(fname, ref error);
        if (r == null)
        {
            cout.Flush();
            Console.Error.Write(error + "\n");
            return 1;
        }

        try
        {
            if (checkadjacency)
            {
                return CheckAdjacency(r);
            }

            cout.Write("meshType: " + PtexGlobals.MeshTypeName(r.meshType()) + "\n");
            cout.Write("dataType: " + PtexGlobals.DataTypeName(r.dataType()) + "\n");
            cout.Write("numChannels: " + r.numChannels() + "\n");
            cout.Write("alphaChannel: ");
            if (r.alphaChannel() == -1) cout.Write("(none)\n");
            else cout.Write(r.alphaChannel() + "\n");
            cout.Write("uBorderMode: " + PtexGlobals.BorderModeName(r.uBorderMode()) + "\n");
            cout.Write("vBorderMode: " + PtexGlobals.BorderModeName(r.vBorderMode()) + "\n");
            cout.Write("edgeFilterMode: " + PtexGlobals.EdgeFilterModeName(r.edgeFilterMode()) + "\n");
            cout.Write("numFaces: " + r.numFaces() + "\n");
            cout.Write("hasEdits: " + (r.hasEdits() ? "yes" : "no") + "\n");
            cout.Write("hasMipMaps: " + (r.hasMipMaps() ? "yes" : "no") + "\n");

            using (PtexPtr<PtexMetaData> meta = new PtexPtr<PtexMetaData>(r.getMetaData()))
            {
                if (meta.get() != null)
                {
                    cout.Write("numMetaKeys: " + meta.get().numKeys() + "\n");
                    if (dumpmeta && meta.get().numKeys() != 0) DumpMetaData(meta.get());
                }
            }

            if (dumpfaceinfo || dumpdata || dumptiling)
            {
                ulong texels = 0;
                for (int i = 0; i < r.numFaces(); i++)
                {
                    cout.Write("face " + i + ":");
                    FaceInfo f = r.getFaceInfo(i);
                    DumpFaceInfo(f);
                    texels += (ulong)f.res.size();
                    if (dumptiling)
                    {
                        using (PtexPtr<PtexFaceData> dh = new PtexPtr<PtexFaceData>(r.getData(i, f.res)))
                            DumpTiling(dh.get());
                    }
                    if (dumpdata) DumpData(r, i, dumpalldata);
                }
                cout.Write("texels: " + texels + "\n");
            }
            if (dumpinternal) DumpInternal(r);
        }
        finally
        {
            r.release();
        }
        return 0;
    }
}
