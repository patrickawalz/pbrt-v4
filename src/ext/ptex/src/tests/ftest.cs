// ftest.cpp
using System;
using System.IO;
using System.Text;
using Ptex;

public static unsafe class FTest
{
    public static int Main(string[] args)
    {
        TextWriter cout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 65536);
        cout.NewLine = "\n";
        int maxmem = args.Length >= 1 ? int.Parse(args[0]) : 1024 * 1024;
        PtexCache c = PtexCache.create(0, (ulong)maxmem, false, null, null);

        string error = "";
        PtexTexture r = c.get("test.ptx", ref error);

        if (r == null)
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        PtexFilter.Options opts = new PtexFilter.Options(PtexFilter.FilterType.f_bicubic, false, 1.0f);
        PtexFilter f = PtexFilter.getFilter(r, opts);
        float* result = stackalloc float[4];
        int faceid = 0;
        float u = 0, v = 0, uw = .125f, vw = .125f;
        for (v = 0; v <= 1; v += .125f)
        {
            for (u = 0; u <= 1; u += .125f)
            {
                f.eval(result, 0, 1, faceid, u, v, uw, 0, 0, vw);
                cout.Write(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0,8:F6} {1,8:F6} -> {2,8:F6}\n", u, v, result[0]));
            }
        }

        f.release();
        r.release();
        c.release();
        cout.Flush();
        return 0;
    }
}
