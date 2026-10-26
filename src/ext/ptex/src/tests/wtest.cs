// wtest.cpp
using System;
using System.IO;
using System.Runtime.InteropServices;
using Ptex;

public static unsafe class WTest
{
    static void writeMeta(PtexWriter w,
                          string sval, double[] dvals, int ndvals, short[] ivals, int nivals,
                          string xval)
    {
        if (sval != null) w.writeMeta("sval", sval);
        if (dvals != null) fixed (double* p = dvals) w.writeMeta("dvals", p, ndvals);
        if (ivals != null) fixed (short* p = ivals) w.writeMeta("ivals", p, nivals);
        if (xval != null) w.writeMeta("xval", xval);
    }

    static bool checkMeta(string path,
                          string sval, double[] dvals, int ndvals, short[] ivals, int nivals,
                          string xval)
    {
        string error = "";
        PtexTexture tx = PtexTexture.open(path, ref error);
        if (tx == null)
        {
            Console.Error.WriteLine(error);
            return false;
        }
        using (PtexPtr<PtexTexture> txp = new PtexPtr<PtexTexture>(tx))
        using (PtexPtr<PtexMetaData> metap = new PtexPtr<PtexMetaData>(tx.getMetaData()))
        {
            PtexMetaData meta = metap.get();

            string f_sval;
            meta.getValue("sval", out f_sval);

            double* f_dvals;
            int f_ndvals;
            meta.getValue("dvals", out f_dvals, out f_ndvals);

            short* f_ivals;
            int f_nivals;
            meta.getValue("ivals", out f_ivals, out f_nivals);

            string f_xval;
            meta.getValue("xval", out f_xval);

            bool ok = ((sval == null || sval == f_sval) &&
                       (ndvals == 0 || (ndvals == f_ndvals &&
                                        cmp(dvals, f_dvals, ndvals))) &&
                       (nivals == 0 || (nivals == f_nivals &&
                                        cmp(ivals, f_ivals, nivals))) &&
                       (xval == null || xval == f_xval));
            if (!ok)
            {
                Console.Error.WriteLine("Meta data readback failed");
                return false;
            }
        }
        return true;
    }

    static bool cmp(double[] a, double* b, int n)
    {
        for (int i = 0; i < n; i++) if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i])) return false;
        return true;
    }

    static bool cmp(short[] a, short* b, int n)
    {
        for (int i = 0; i < n; i++) if (a[i] != b[i]) return false;
        return true;
    }

    public static int Main(string[] args)
    {
        Res[] res = { new Res((sbyte)8, (sbyte)7),
                      new Res((ushort)0x0201),
                      new Res((sbyte)3, (sbyte)1),
                      new Res((ushort)0x0405),
                      new Res((sbyte)9, (sbyte)8),
                      new Res((ushort)0x0402),
                      new Res((sbyte)6, (sbyte)2),
                      new Res((ushort)0x0407),
                      new Res((sbyte)2, (sbyte)1) };
        int[][] adjedges = { new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 },
                             new int[] { 2, 3, 0, 1 } };
        int[][] adjfaces = { new int[] { 3, 1, -1, -1 },
                             new int[] { 4, 2, -1, 0 },
                             new int[] { 5, -1, -1, 1 },
                             new int[] { 6, 4, 0, -1 },
                             new int[] { 7, 5, 1, 3 },
                             new int[] { 8, -1, 2, 4 },
                             new int[] { -1, 7, 3, -1 },
                             new int[] { -1, 8, 4, 6 },
                             new int[] { -1, -1, 5, 7 } };

        int nfaces = res.Length;
        DataType dt = DataType.dt_uint16;
        float ptexOne = PtexGlobals.OneValue(dt);
        int alpha = -1;
        int nchan = 3;

        string error = "";
        PtexWriter w =
            PtexWriter.open("test.ptx", MeshType.mt_quad, dt, nchan, alpha, nfaces, ref error);
        if (w == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        int size = 0;
        for (int i = 0; i < nfaces; i++)
            size = Math.Max(size, res[i].size());
        size *= PtexGlobals.DataSize(dt) * nchan;

        byte* buff = (byte*)NativeMemory.Alloc((nuint)size);
        for (int i = 0; i < nfaces; i++)
        {
            new Span<byte>(buff, size).Clear();
            ushort* fbuff = (ushort*)buff;
            int ures = res[i].u(), vres = res[i].v();
            for (int v = 0; v < vres; v++)
            {
                for (int u = 0; u < ures; u++)
                {
                    float c = (u ^ v) & 1;
                    fbuff[(v * ures + u) * nchan] = (ushort)(u / (float)(ures - 1) * ptexOne);
                    fbuff[(v * ures + u) * nchan + 1] = (ushort)(v / (float)(vres - 1) * ptexOne);
                    fbuff[(v * ures + u) * nchan + 2] = (ushort)(c * ptexOne);
                }
            }

            w.writeFace(i, new FaceInfo(res[i], adjfaces[i], adjedges[i]), buff);
        }
        NativeMemory.Free(buff);

        string sval = "a str val";
        int ndvals = 3;
        double[] dvals = { 1.1, 2.2, 3.3 };
        int nivals = 4;
        short[] ivals = { 2, 4, 6, 8 };
        string xval = null;

        writeMeta(w, sval, dvals, ndvals, ivals, nivals, xval);
        if (!w.close(ref error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        w.release();
        if (!checkMeta("test.ptx", sval, dvals, ndvals, ivals, nivals, xval))
            return 1;

        // add some incremental edits
        w = PtexWriter.edit("test.ptx", true, MeshType.mt_quad, dt, nchan, alpha, nfaces, ref error);
        sval = "a string value";
        dvals[2] = 0;
        writeMeta(w, sval, dvals, ndvals, null, 0, null);

        if (!w.close(ref error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        w.release();
        if (!checkMeta("test.ptx", sval, dvals, ndvals, ivals, nivals, xval))
            return 1;

        // add some non-incremental edits, including some large meta data
        ndvals = 500;
        dvals = new double[ndvals];
        for (int i = 0; i < ndvals; i++) dvals[i] = i;

        w = PtexWriter.edit("test.ptx", false, MeshType.mt_quad, dt, nchan, alpha, nfaces, ref error);
        xval = "another string value";
        writeMeta(w, null, dvals, ndvals, null, 0, xval);
        if (!w.close(ref error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        w.release();
        if (!checkMeta("test.ptx", sval, dvals, ndvals, ivals, nivals, xval))
            return 1;

        return 0;
    }
}
