// nanovdb2pbrt.cpp
// pbrt is Copyright(c) 1998-2023 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
#else
global using Float = System.Single;
#endif
global using int32_t = System.Int32;
global using uint32_t = System.UInt32;
global using uint64_t = System.UInt64;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using pbrt;
using static pbrt.Globals;

public static unsafe class Nanovdb2pbrtMain
{
    static nanovdb.GridHandle<NanoVDBBuffer> readGrid(string filename, string gridName, Allocator alloc)
    {
        NanoVDBBuffer buf = new NanoVDBBuffer(alloc);
        nanovdb.GridHandle<NanoVDBBuffer> grid = new nanovdb.GridHandle<NanoVDBBuffer>();
        try
        {
            grid = nanovdb.io.IO.readGrid<NanoVDBBuffer>(filename, gridName, 0 /* not verbose */, buf);
        }
        catch (Exception e) when (!(e is FatalException))
        {
            ErrorExit(StringPrintf("nanovdb: %s: %s", filename, e.Message));
        }

        if (grid)
        {
            if (!grid.gridMetaData().isFogVolume() && !grid.gridMetaData().isUnknown())
                ErrorExit(StringPrintf("%s: \"%s\" isn't a FogVolume grid?", filename, gridName));

            LOG_VERBOSE("%s: found %d \"%s\" voxels", filename, grid.gridMetaData().activeVoxelCount(), gridName);
        }

        return grid;
    }

    static void usage(string msg = "")
    {
        if (msg.Length != 0)
            Console.Error.Write("nanovdb2pbrt: " + msg + "\n\n");

        Console.Error.Write(@"usage: nanovdb2pbrt [<options>] <filename.nvdb>

Options:
  --grid <name>        Name of grid to extract. Default: ""density""
");
        Console.Error.Flush();
        Environment.Exit(msg.Length == 0 ? 0 : 1);
    }

    // %f with C semantics (fixed, six digits)
    static string CF(double v)
    {
        if (double.IsNaN(v)) return BitConverter.DoubleToInt64Bits(v) < 0 ? "-nan" : "nan";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }

    public static int Main(string[] argv)
    {
        try
        {
            return MainImpl(argv);
        }
        catch (FatalException)
        {
            return 1;
        }
    }

    static int MainImpl(string[] argv)
    {
        nanovdb.io.IO.USE_ZIP = true;  // #define NANOVDB_USE_ZIP 1

        List<string> args = GetCommandLineArguments(argv);

        Action<string> onError = (string err) =>
        {
            usage(err);
            Environment.Exit(1);
        };

        string filename = "";
        string grid = "density";
        int downsample = 0;
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (args[iter].Length == 0 || args[iter][0] != '-')
            {
                if (filename.Length == 0)
                    filename = args[iter];
                else
                {
                    usage();
                    return 1;
                }
            }
            else if (ParseArg(args, ref iter, end, "downsample", ref downsample, onError) ||
                     ParseArg(args, ref iter, end, "grid", ref grid, onError))
            {
                // success
            }
            else
            {
                usage();
                return 1;
            }
        }

        if (filename.Length == 0)
            usage("must specify a nanovdb filename");

        Allocator alloc = null;
        nanovdb.GridHandle<NanoVDBBuffer> nanoGrid = readGrid(filename, grid, alloc);
        if (!nanoGrid)
            ErrorExit(StringPrintf("%s: didn't find \"%s\" grid.", filename, grid));
        nanovdb.NanoGrid<float> floatGrid = nanoGrid.grid<float>();
        if (floatGrid.IsNull)  // (the C++ dereferences a null grid here)
            ErrorExit(StringPrintf("%s: \"%s\" isn't a float grid.", filename, grid));

        nanovdb.BBox<double> bbox = floatGrid.worldBBox();
        Bounds3f bounds = new Bounds3f(new Point3f((Float)bbox.min()[0], (Float)bbox.min()[1], (Float)bbox.min()[2]),
                                       new Point3f((Float)bbox.max()[0], (Float)bbox.max()[1], (Float)bbox.max()[2]));

        List<Float> values = new List<Float>();

        int x0 = floatGrid.indexBBox().min()[0], x1 = floatGrid.indexBBox().max()[0] + 1;
        int y0 = floatGrid.indexBBox().min()[1], y1 = floatGrid.indexBBox().max()[1] + 1;
        int z0 = floatGrid.indexBBox().min()[2], z1 = floatGrid.indexBBox().max()[2] + 1;

        for (int z = z0; z <= z1; ++z)
            for (int y = y0; y <= y1; ++y)
                for (int x = x0; x <= x1; ++x)
                {
                    values.Add(floatGrid.tree().getValue(new nanovdb.Coord(x, y, z)));
                }

        StreamWriter o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false), 1 << 20);
        o.NewLine = "\n";
        o.Write("\"integer nx\" " + (1 + x1 - x0) + " \"integer ny\" " + (1 + y1 - y0) + "  \"integer nz\" " + (1 + z1 - z0) + "\n");
        o.Write("\t\"point3 p0\" [ " + CF(bounds.pMin.x) + " " + CF(bounds.pMin.y) + " " + CF(bounds.pMin.z) +
                 " ] \"point3 p1\" [ " + CF(bounds.pMax.x) + " " + CF(bounds.pMax.y) + " " + CF(bounds.pMax.z) + " ]\n");
        o.Write("\t\"float " + grid + "\" [\n");
        for (int i = 0; i < values.Count; ++i)
        {
            Float d = values[i];
            if (d == 0) o.Write("0 ");
            else o.Write(CF(d) + " ");
            if ((i % 20) == 19) o.Write("\n");
        }
        o.Write("]\n");
        o.Flush();
        GC.KeepAlive(nanoGrid);  // (the C++ handle is destroyed at scope exit; the grid is raw memory owned by it)
        return 0;
    }
}
