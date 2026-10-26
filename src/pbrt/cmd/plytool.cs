// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cmd/plytool.cpp

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
#else
global using Float = System.Single;
#endif

using System;
using System.Collections.Generic;
using pbrt;
using static pbrt.Globals;

public static unsafe class PlyToolMain
{
    static void help()
    {
        Console.Write(@"plytool provides a number of operations on PLY meshes.

usage: plytool <command> [options]

where <command> is:

cat: Print a text representation of the mesh.

displace: Apply displacement mapping to a mesh.

info: Print general information about the mesh.

split: Split the mesh into multiple PLY files.

""plytool help <command>"" provides detailed information about <command>.
");
    }

    static void usage(string msg = null)
    {
        if (msg != null)
        {
            Console.Error.Write("plytool: ");
            Console.Error.Write(msg);
            Console.Error.Write("\n\n");
        }

        help();

        Environment.Exit(1);
    }

    static int help(List<string> args)
    {
        if (args.Count == 0)
        {
            help();
            return 0;
        }
        foreach (string cmd in args)
        {
            if (cmd == "cat")
            {
                Console.Write("usage: plytool cat <filename>\n");
            }
            else if (cmd == "info")
            {
                Console.Write("usage: plytool info <filename...>\n");
            }
            else if (cmd == "displace")
            {
                Console.Write(@"usage: plytool displace [options] <filename>

options:
  --scale <s>       Scale to apply to displacement value in image.
                    (Default: 1)
  --uvscale <s>     Scale to apply to uv texture coordinates in image.
                    (Default: 1)
  --edge-length <s> Maximum length of an edge in the undisplaced mesh.
                    (Default: 1)
  --image <name>    Filename for image used to define displacements.
  --outfile <name>  Filename name for emitted PLY file.
");
            }
            else if (cmd == "split")
            {
                Console.Write(@"usage: plytool split [options] <filename>

options:
  --maxfaces <n>    Maximum number of faces in an output PLY file.
                    (Default: 1000000)
  --outbase <name>  Base name for emitted PLY files.  Consecutive numbers
                    and a "".ply"" suffix will be appended to <name>.
                    (Default: based on <source.ply>.)
");
            }
            else
            {
                usage(StringPrintf("%s: command unknown", cmd));
                return 1;
            }
        }

        return 0;
    }

    static int cat(List<string> args)
    {
        string filename = "";
        foreach (string a in args)
        {
            if (filename.Length == 0)
                filename = a;
            else
                usage(StringPrintf("%s: unexpected argument", a));
        }

        if (filename.Length == 0)
            usage("must specify PLY filename.");

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);

        for (int i = 0; i < (int)mesh.triIndices.size(); i += 3)
            Printf("Triangle: %d %d %d\n", mesh.triIndices[i], mesh.triIndices[i + 1],
                   mesh.triIndices[i + 2]);
        for (int i = 0; i < (int)mesh.quadIndices.size(); i += 4)
            Printf("Quad: %d %d %d %d\n", mesh.quadIndices[i], mesh.quadIndices[i + 1],
                   mesh.quadIndices[i + 2], mesh.quadIndices[i + 3]);
        for (int i = 0; i < (int)mesh.p.size(); ++i)
            Printf("Vertex position %d: %s\n", i, mesh.p[i]);
        for (int i = 0; i < (int)mesh.n.size(); ++i)
            Printf("Vertex normal %d: %s\n", i, mesh.n[i]);
        for (int i = 0; i < (int)mesh.uv.size(); ++i)
            Printf("Vertex uv %d: %s\n", i, mesh.uv[i]);

        return 0;
    }

    static int info(List<string> args)
    {
        foreach (string filename in args)
        {
            TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);

            Printf("%s:\n", filename);
            Printf("\tTriangles: %d\n", mesh.triIndices.size() / 3);
            Printf("\tQuads: %d\n", mesh.quadIndices.size() / 4);
            Printf("\tVertex positions: %d\n", mesh.p.size());
            Printf("\tVertex normals: %d\n", mesh.n.size());
            Printf("\tVertex uvs: %d\n", mesh.uv.size());
            Printf("\tFace indices: %d\n", mesh.faceIndices.size());

            bool[] vertexUsed = new bool[mesh.p.size()];
            for (int i = 0; i < (int)mesh.triIndices.size(); ++i)
            {
                int v = mesh.triIndices[i];
                CHECK(v >= 0 && v < (int)mesh.p.size());
                vertexUsed[v] = true;
            }
            for (int i = 0; i < (int)mesh.quadIndices.size(); ++i)
            {
                int v = mesh.quadIndices[i];
                CHECK(v >= 0 && v < (int)mesh.p.size());
                vertexUsed[v] = true;
            }
            for (int i = 0; i < vertexUsed.Length; ++i)
                if (!vertexUsed[i])
                    Printf("Notice: vertex %d is not used.\n", i);

            Bounds3f bounds = new Bounds3f();
            for (int i = 0; i < (int)mesh.p.size(); ++i)
                bounds = Union(bounds, mesh.p[i]);
            Printf("\tBounding box: %s\n", bounds);
        }

        return 0;
    }

    static pstd.span<T> Sp<T>(pstd.vector<T> v) { return new pstd.span<T>(v.data(), 0, (int)v.size()); }

    static int displace(List<string> args)
    {
        Float scale = 1, uvScale = 1, edgeLength = 1;
        string filename = "", imageFilename = "", outFilename = "";
        Action<string> onError = (string err) =>
        {
            usage(err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, end, "uvscale", ref uvScale, onError) ||
                ParseArg(args, ref iter, end, "image", ref imageFilename, onError) ||
                ParseArg(args, ref iter, end, "outfile", ref outFilename, onError) ||
                ParseArg(args, ref iter, end, "edge-length", ref edgeLength, onError))
                ;  // yaay
            else if (filename.Length == 0)
                filename = args[iter];
            else
                usage(StringPrintf("unexpected argument \"%s\"", args[iter]));
        }

        if (filename.Length == 0)
            usage("must specify source PLY filename.");
        if (outFilename.Length == 0)
            usage("must specify output PLY filename.");
        if (imageFilename.Length == 0)
            usage("must specify image displacement map.");

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(filename);
        ImageAndMetadata immeta = Image.Read(imageFilename);

        TriQuadMesh outputMesh = mesh.Displace(
            (Point3f v0, Point3f v1) => { return Distance(v0, v1); }, edgeLength,
            (Point3f[] p, Normal3f[] n, Point2f[] uv, int nVertices) =>
            {
                ParallelFor(0, nVertices, (long i) =>
                {
                    Point2f uvp = new Point2f(uvScale * uv[i][0], 1 - uvScale * uv[i][1]);
                    Float d = immeta.image.Bilerp(uvp, WrapMode.Repeat).Average();
                    p[i] += (Vector3f)((d * scale) * n[i]);
                });
            });

        if (!WritePLY(outFilename, Sp(outputMesh.triIndices), new pstd.span<int>(), Sp(outputMesh.p),
                      Sp(outputMesh.n), Sp(outputMesh.uv), new pstd.span<int>()))
            return 1;

        return 0;
    }

    static int split(List<string> args)
    {
        string inPLY = "", outPLYBase = "";
        int maxFaces = 1000000;
        Action<string> onError = (string err) =>
        {
            usage(err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "maxfaces", ref maxFaces, onError) ||
                ParseArg(args, ref iter, end, "outbase", ref outPLYBase, onError))
                ;  // yaay
            else if (inPLY.Length == 0)
                inPLY = args[iter];
            else
                usage(StringPrintf("unexpected argument \"%s\"", args[iter]));
        }

        if (inPLY.Length == 0)
            usage("must specify source PLY filename.");

        if (outPLYBase.Length == 0)
            outPLYBase = RemoveExtension(inPLY);

        TriQuadMesh mesh = TriQuadMesh.ReadPLY(inPLY);

        if (mesh.quadIndices.size() > 0)
        {
            Console.Error.Write(StringPrintf(
                "%s: sorry, mesh has quad faces. plytool currently only supports triangle meshes.\n", inPLY));
            return 1;
        }
        if (mesh.faceIndices.size() > 0)
        {
            Console.Error.Write(StringPrintf(
                "%s: sorry, mesh has faceIndices, which are not currently supported by plytool.\n", inPLY));
            return 1;
        }

        int nFaces = (int)(mesh.triIndices.size() / 3);
        if (nFaces <= maxFaces)
        {
            Console.Error.Write(StringPrintf("%s: mesh has %d faces and so has not been split up.\n",
                                             inPLY, nFaces));
            return 0;
        }

        int nMeshes = (nFaces + maxFaces - 1) / maxFaces;
        Console.Error.Write(StringPrintf("%s: mesh has %d faces and will be split into %d meshes.\n",
                                         inPLY, nFaces, nMeshes));

        int nFacesPerMesh = nFaces / nMeshes;  // more or less...
        for (int i = 0; i < nMeshes; ++i)
        {
            int firstFaceIndex = i * nFacesPerMesh;
            int lastFaceIndex = (i + 1) * nFacesPerMesh;
            if (i == nMeshes - 1)
                lastFaceIndex = nFaces;

            SortedDictionary<int, int> vertexIndexRemap = new SortedDictionary<int, int>();
            pstd.vector<int> indices = new pstd.vector<int>();
            pstd.vector<Point3f> p = new pstd.vector<Point3f>();
            pstd.vector<Normal3f> n = new pstd.vector<Normal3f>();
            pstd.vector<Point2f> uv = new pstd.vector<Point2f>();
            for (int vertexIndex = 3 * firstFaceIndex; vertexIndex < 3 * lastFaceIndex; ++vertexIndex)
            {
                int index = mesh.triIndices[vertexIndex];
                int found;
                if (vertexIndexRemap.TryGetValue(index, out found))
                    indices.push_back(found);
                else
                {
                    int newIndex = vertexIndexRemap.Count;
                    vertexIndexRemap[index] = newIndex;
                    indices.push_back(newIndex);

                    p.push_back(mesh.p[index]);
                    if (!mesh.n.empty())
                        n.push_back(mesh.n[index]);
                    // TODO: there's no "s" in TriQuadMesh???
                    if (!mesh.uv.empty())
                        uv.push_back(mesh.uv[index]);
                }
            }

            TriangleMesh triMesh = new TriangleMesh(new Transform(), false /* reverse orientation */, indices, p,
                                                    new pstd.vector<Vector3f>(), n, uv, new pstd.vector<int>(),
                                                    null);
            if (!triMesh.WritePLY(StringPrintf("%s-%03d.ply", outPLYBase, i)))
                return 1;
        }

        return 0;
    }

    public static int Main(string[] argv)
    {
        InitPBRT(new PBRTOptions());

        if (argv.Length < 1)
        {
            help();
            return 0;
        }

        List<string> args = GetCommandLineArguments(argv);

        string cmd = args[0];
        args.RemoveAt(0);

        int ret = 0;
        if (cmd == "help")
            ret = help(args);
        else if (cmd == "cat")
            ret = cat(args);
        else if (cmd == "displace")
            ret = displace(args);
        else if (cmd == "info")
            ret = info(args);
        else if (cmd == "split")
            ret = split(args);
        else
            usage(StringPrintf("%s: command unknown", cmd));

        CleanupPBRT();

        return ret;
    }
}
