// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/mesh.h, util/mesh.cpp
//
// (std::vector<T> parameters/members are pstd.vector<T>; the "const T *" buffers owned by the
// meshes are T[] arrays obtained from the BufferCaches.)

using System;
using System.Collections.Generic;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        private static void PlyErrorCallback(p_ply ply, string message)
        {
            Error($"PLY writing error: {message}");
        }

        public static bool WritePLY(string filename, pstd.span<int> triIndices, pstd.span<int> quadIndices,
                                    pstd.span<Point3f> p, pstd.span<Normal3f> n, pstd.span<Point2f> uv,
                                    pstd.span<int> faceIndices)
        {
            p_ply plyFile = rply.ply_create(filename, rply.PLY_DEFAULT, PlyErrorCallback, 0, null);
            if (plyFile == null)
                return false;

            int nVertices = (int)p.size();
            rply.ply_add_element(plyFile, "vertex", nVertices);
            rply.ply_add_scalar_property(plyFile, "x", e_ply_type.PLY_FLOAT);
            rply.ply_add_scalar_property(plyFile, "y", e_ply_type.PLY_FLOAT);
            rply.ply_add_scalar_property(plyFile, "z", e_ply_type.PLY_FLOAT);
            if (n.size() != 0)
            {
                rply.ply_add_scalar_property(plyFile, "nx", e_ply_type.PLY_FLOAT);
                rply.ply_add_scalar_property(plyFile, "ny", e_ply_type.PLY_FLOAT);
                rply.ply_add_scalar_property(plyFile, "nz", e_ply_type.PLY_FLOAT);
            }
            if (uv.size() != 0)
            {
                rply.ply_add_scalar_property(plyFile, "u", e_ply_type.PLY_FLOAT);
                rply.ply_add_scalar_property(plyFile, "v", e_ply_type.PLY_FLOAT);
            }

            int nTriangles = (int)(triIndices.size() / 3), nQuads = (int)(quadIndices.size() / 4);
            rply.ply_add_element(plyFile, "face", nTriangles + nQuads);
            rply.ply_add_list_property(plyFile, "vertex_indices", e_ply_type.PLY_UINT8, e_ply_type.PLY_INT);
            if (faceIndices.size() != 0)
                rply.ply_add_scalar_property(plyFile, "face_indices", e_ply_type.PLY_INT);

            rply.ply_write_header(plyFile);

            for (int i = 0; i < nVertices; ++i)
            {
                rply.ply_write(plyFile, p[i].x);
                rply.ply_write(plyFile, p[i].y);
                rply.ply_write(plyFile, p[i].z);
                if (n.size() != 0)
                {
                    rply.ply_write(plyFile, n[i].x);
                    rply.ply_write(plyFile, n[i].y);
                    rply.ply_write(plyFile, n[i].z);
                }
                if (uv.size() != 0)
                {
                    rply.ply_write(plyFile, uv[i].x);
                    rply.ply_write(plyFile, uv[i].y);
                }
            }

            for (int i = 0; i < nTriangles; ++i)
            {
                rply.ply_write(plyFile, 3);
                rply.ply_write(plyFile, triIndices[3 * i]);
                rply.ply_write(plyFile, triIndices[3 * i + 1]);
                rply.ply_write(plyFile, triIndices[3 * i + 2]);
                if (faceIndices.size() != 0)
                    rply.ply_write(plyFile, faceIndices[i]);
            }
            for (int i = 0; i < nQuads; ++i)
            {
                rply.ply_write(plyFile, 4);
                rply.ply_write(plyFile, quadIndices[4 * i]);
                rply.ply_write(plyFile, quadIndices[4 * i + 1]);
                rply.ply_write(plyFile, quadIndices[4 * i + 2]);
                rply.ply_write(plyFile, quadIndices[4 * i + 3]);
                if (faceIndices.size() != 0)
                    rply.ply_write(plyFile, faceIndices[i]);
            }

            rply.ply_close(plyFile);
            return true;
        }
    }

    // TriangleMesh Definition
    public unsafe class TriangleMesh
    {
        // STAT_RATIO("Geometry/Triangles per mesh", nTris, nTriMeshes);
        [ThreadStatic] private static long nTris, nTriMeshes;
        private static StatRegisterer STATS_REGnTris = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Geometry/Triangles per mesh", nTris, nTriMeshes);
            nTris = 0;
            nTriMeshes = 0;
        });
        // STAT_MEMORY_COUNTER("Memory/Triangles", triangleBytes);
        [ThreadStatic] private static long triangleBytes;
        private static StatRegisterer STATS_REGtriangleBytes = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Triangles", triangleBytes);
            triangleBytes = 0;
        });

        // TriangleMesh Public Methods
        public TriangleMesh(Transform renderFromObject, bool reverseOrientation, pstd.vector<int> indices,
                            pstd.vector<Point3f> p, pstd.vector<Vector3f> s, pstd.vector<Normal3f> n,
                            pstd.vector<Point2f> uv, pstd.vector<int> faceIndices, Allocator alloc)
        {
            nTriangles = (int)(indices.size() / 3);
            nVertices = (int)p.size();
            CHECK_EQ((indices.size() % 3), (ulong)0);
            ++nTriMeshes;
            nTris += nTriangles;
            triangleBytes += 64;  // sizeof(*this)
            // Initialize mesh _vertexIndices_
            vertexIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(indices.data(), 0, (int)indices.size()), alloc);

            // Transform mesh vertices to rendering space and initialize mesh _p_
            Point3f[] pts = p.ToArray();
            for (int i = 0; i < pts.Length; ++i)
                pts[i] = renderFromObject.Apply(pts[i]);
            this.p = pbrt.point3BufferCache.LookupOrAdd(new pstd.span<Point3f>(pts), alloc);

            // Remainder of _TriangleMesh_ constructor
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();

            if (!uv.empty())
            {
                CHECK_EQ((ulong)nVertices, uv.size());
                this.uv = pbrt.point2BufferCache.LookupOrAdd(new pstd.span<Point2f>(uv.data(), 0, (int)uv.size()), alloc);
            }
            if (!n.empty())
            {
                CHECK_EQ((ulong)nVertices, n.size());
                Normal3f[] nns = n.ToArray();
                for (int i = 0; i < nns.Length; ++i)
                {
                    nns[i] = renderFromObject.Apply(nns[i]);
                    if (reverseOrientation)
                        nns[i] = -nns[i];
                }
                this.n = pbrt.normal3BufferCache.LookupOrAdd(new pstd.span<Normal3f>(nns), alloc);
            }
            if (!s.empty())
            {
                CHECK_EQ((ulong)nVertices, s.size());
                Vector3f[] sss = s.ToArray();
                for (int i = 0; i < sss.Length; ++i)
                    sss[i] = renderFromObject.Apply(sss[i]);
                this.s = pbrt.vector3BufferCache.LookupOrAdd(new pstd.span<Vector3f>(sss), alloc);
            }

            if (!faceIndices.empty())
            {
                CHECK_EQ((ulong)nTriangles, faceIndices.size());
                this.faceIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(faceIndices.data(), 0, (int)faceIndices.size()), alloc);
            }

            // Make sure that we don't have too much stuff to be using integers to
            // index into things.
            CHECK_LE(p.size(), (ulong)int.MaxValue);
            // We could be clever and check indices.size() / 3 if we were careful
            // to promote to a 64-bit int before multiplying by 3 when we look up
            // in the indices array...
            CHECK_LE(indices.size(), (ulong)int.MaxValue);
        }

        private static string Span<T>(T[] a, int n)
        {
            if (a == null) return "(nullptr)";
            List<string> parts = new List<string>();
            for (int i = 0; i < n; ++i) parts.Add(a[i].ToString());
            return "[ " + string.Join(", ", parts) + " ]";
        }

        public override string ToString()
        {
            return pbrt.StringPrintf(
                "[ TriangleMesh reverseOrientation: %s transformSwapsHandedness: %s " +
                "nTriangles: %d nVertices: %d vertexIndices: %s p: %s n: %s " +
                "s: %s uv: %s faceIndices: %s ]",
                reverseOrientation, transformSwapsHandedness, nTriangles, nVertices,
                Span(vertexIndices, 3 * nTriangles), Span(p, nVertices), Span(n, nVertices),
                Span(s, nVertices), Span(uv, nVertices), Span(faceIndices, nTriangles));
        }

        public bool WritePLY(string filename)
        {
            if (s != null)
                Warning($"{filename}: PLY mesh will be missing tangent vectors \"S\".");

            return pbrt.WritePLY(
                filename, new pstd.span<int>(vertexIndices, 0, 3 * nTriangles),
                new pstd.span<int>(), new pstd.span<Point3f>(p, 0, nVertices),
                new pstd.span<Normal3f>(n, 0, n != null ? nVertices : 0),
                new pstd.span<Point2f>(uv, 0, uv != null ? nVertices : 0),
                new pstd.span<int>(faceIndices, 0, faceIndices != null ? nTriangles : 0));
        }

        // TriangleMesh Public Members
        public int nTriangles, nVertices;
        public int[] vertexIndices = null;
        public Point3f[] p = null;
        public Normal3f[] n = null;
        public Vector3f[] s = null;
        public Point2f[] uv = null;
        public int[] faceIndices = null;
        public bool reverseOrientation, transformSwapsHandedness;
    }

    // BilinearPatchMesh Definition
    public unsafe class BilinearPatchMesh
    {
        // STAT_RATIO("Geometry/Bilinear patches per mesh", nBlps, nBilinearMeshes);
        [ThreadStatic] private static long nBlps, nBilinearMeshes;
        private static StatRegisterer STATS_REGnBlps = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportRatio("Geometry/Bilinear patches per mesh", nBlps, nBilinearMeshes);
            nBlps = 0;
            nBilinearMeshes = 0;
        });
        // STAT_MEMORY_COUNTER("Memory/Bilinear patches", blpBytes);
        [ThreadStatic] private static long blpBytes;
        private static StatRegisterer STATS_REGblpBytes = new StatRegisterer((StatsAccumulator accum) =>
        {
            accum.ReportMemoryCounter("Memory/Bilinear patches", blpBytes);
            blpBytes = 0;
        });

        // BilinearPatchMesh Public Methods
        public BilinearPatchMesh(Transform renderFromObject, bool reverseOrientation, pstd.vector<int> indices,
                                 pstd.vector<Point3f> P, pstd.vector<Normal3f> N, pstd.vector<Point2f> UV,
                                 pstd.vector<int> fIndices, PiecewiseConstant2D imageDist, Allocator alloc)
        {
            this.reverseOrientation = reverseOrientation;
            this.transformSwapsHandedness = renderFromObject.SwapsHandedness();
            nPatches = (int)(indices.size() / 4);
            nVertices = (int)P.size();
            imageDistribution = imageDist;
            CHECK_EQ((indices.size() % 4), (ulong)0);
            ++nBilinearMeshes;
            nBlps += nPatches;

            // Make sure that we don't have too much stuff to be using integers to
            // index into things.
            CHECK_LE(P.size(), (ulong)int.MaxValue);
            CHECK_LE(indices.size(), (ulong)int.MaxValue);

            vertexIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(indices.data(), 0, (int)indices.size()), alloc);

            blpBytes += 64;  // sizeof(*this)

            // Transform mesh vertices to rendering space
            Point3f[] pts = P.ToArray();
            for (int i = 0; i < pts.Length; ++i)
                pts[i] = renderFromObject.Apply(pts[i]);
            p = pbrt.point3BufferCache.LookupOrAdd(new pstd.span<Point3f>(pts), alloc);

            // Copy _UV_ and _N_ vertex data, if present
            if (!UV.empty())
            {
                CHECK_EQ((ulong)nVertices, UV.size());
                uv = pbrt.point2BufferCache.LookupOrAdd(new pstd.span<Point2f>(UV.data(), 0, (int)UV.size()), alloc);
            }
            if (!N.empty())
            {
                CHECK_EQ((ulong)nVertices, N.size());
                Normal3f[] nns = N.ToArray();
                for (int i = 0; i < nns.Length; ++i)
                {
                    nns[i] = renderFromObject.Apply(nns[i]);
                    if (reverseOrientation)
                        nns[i] = -nns[i];
                }
                n = pbrt.normal3BufferCache.LookupOrAdd(new pstd.span<Normal3f>(nns), alloc);
            }

            if (!fIndices.empty())
            {
                CHECK_EQ((ulong)nPatches, fIndices.size());
                faceIndices = pbrt.intBufferCache.LookupOrAdd(new pstd.span<int>(fIndices.data(), 0, (int)fIndices.size()), alloc);
            }
        }

        private static string Span<T>(T[] a, int n)
        {
            if (a == null) return "(nullptr)";
            List<string> parts = new List<string>();
            for (int i = 0; i < n; ++i) parts.Add(a[i].ToString());
            return "[ " + string.Join(", ", parts) + " ]";
        }

        public override string ToString()
        {
            return pbrt.StringPrintf(
                "[ BilinearMatchMesh reverseOrientation: %s transformSwapsHandedness: " +
                "%s " +
                "nPatches: %d nVertices: %d vertexIndices: %s p: %s n: %s " +
                "uv: %s faceIndices: %s ]",
                reverseOrientation, transformSwapsHandedness, nPatches, nVertices,
                Span(vertexIndices, 4 * nPatches), Span(p, nVertices), Span(n, nVertices),
                Span(uv, nVertices), Span(faceIndices, nPatches));
        }

        // BilinearPatchMesh Public Members
        public bool reverseOrientation, transformSwapsHandedness;
        public int nPatches, nVertices;
        public int[] vertexIndices = null;
        public Point3f[] p = null;
        public Normal3f[] n = null;
        public Point2f[] uv = null;
        public int[] faceIndices = null;
        public PiecewiseConstant2D imageDistribution;
    }

    // Disp&& for TriQuadMesh::Displace: displace(p, n, uv, nVertices)
    public delegate void MeshDisplaceFunc(Point3f[] p, Normal3f[] n, Point2f[] uv, int nVertices);

    public unsafe class TriQuadMesh
    {

        public TriQuadMesh Displace(Func<Point3f, Point3f, Float> dist, Float maxDist, MeshDisplaceFunc displace,
                                    FileLoc loc = null)
        {
            if (uv.empty())
                ErrorExit(loc, "Vertex uvs are currently required by Displace(). Sorry.\n");

            // Prepare the output mesh
            TriQuadMesh outputMesh = Clone();
            outputMesh.ConvertToOnlyTriangles();
            if (outputMesh.n.empty())
                outputMesh.ComputeNormals();
            pstd.vector<int> oldTriIndices = new pstd.vector<int>(outputMesh.triIndices.ToArray());
            outputMesh.triIndices.clear();

            // Refine
            HashMap<(int, int), int> edgeSplit = new HashMap<(int, int), int>();
            for (int i = 0; i < (int)oldTriIndices.size() / 3; ++i)
                outputMesh.Refine(dist, maxDist, oldTriIndices[3 * i], oldTriIndices[3 * i + 1], oldTriIndices[3 * i + 2],
                                  edgeSplit);

            // Displace
            displace(outputMesh.p.data(), outputMesh.n.data(), outputMesh.uv.data(), (int)outputMesh.p.size());

            outputMesh.ComputeNormals();

            return outputMesh;
        }

        // TriQuadMesh Private Methods
        private void Refine(Func<Point3f, Point3f, Float> distance, Float maxDist, int v0, int v1, int v2,
                            HashMap<(int, int), int> edgeSplit)
        {
            Point3f p0 = p[v0], p1 = p[v1], p2 = p[v2];
            Float d01 = distance(p0, p1), d12 = distance(p1, p2), d20 = distance(p2, p0);

            if (d01 < maxDist && d12 < maxDist && d20 < maxDist)
            {
                triIndices.push_back(v0);
                triIndices.push_back(v1);
                triIndices.push_back(v2);
                return;
            }

            // order so that the first two vertices have the longest edge
            int[] v;
            if (d01 > d12)
            {
                if (d01 > d20)
                    v = new int[] { v0, v1, v2 };
                else
                    v = new int[] { v2, v0, v1 };
            }
            else
            {
                if (d12 > d20)
                    v = new int[] { v1, v2, v0 };
                else
                    v = new int[] { v2, v0, v1 };
            }

            // has the edge been spilt before?
            (int first, int second) edge = (v[0], v[1]);
            if (v[0] > v[1])
            {
                int t = edge.first; edge.first = edge.second; edge.second = t;
            }

            int vmid;
            if (edgeSplit.HasKey(edge))
            {
                vmid = edgeSplit[edge];
            }
            else
            {
                vmid = (int)p.size();
                edgeSplit.Insert(edge, vmid);
                p.push_back((p[v[0]] + p[v[1]]) / 2);
                if (!n.empty())
                {
                    Normal3f nn = n[v[0]] + n[v[1]];
                    if (LengthSquared(nn) > 0)
                        nn = Normalize(nn);
                    n.push_back(nn);
                }
                if (!uv.empty())
                    uv.push_back((uv[v[0]] + uv[v[1]]) / 2);
            }

            Refine(distance, maxDist, v[0], vmid, v[2], edgeSplit);
            Refine(distance, maxDist, vmid, v[1], v[2], edgeSplit);
        }
        // TriQuadMesh Public Methods
        public static TriQuadMesh ReadPLY(string filename)
        {
            TriQuadMesh mesh = new TriQuadMesh();

            p_ply ply = rply.ply_open(filename, rply_message_callback, 0, null);
            if (ply == null)
                ErrorExit($"Couldn't open PLY file \"{filename}\"");

            if (rply.ply_read_header(ply) == 0)
                ErrorExit($"Unable to read the header of PLY file \"{filename}\"");

            p_ply_element element = null;
            size_t vertexCount = 0, faceCount = 0;

            /* Inspect the structure of the PLY file */
            while ((element = rply.ply_get_next_element(ply, element)) != null)
            {
                string name;
                long nInstances;

                rply.ply_get_element_info(element, out name, out nInstances);
                if (name == "vertex")
                    vertexCount = (size_t)nInstances;
                else if (name == "face")
                    faceCount = (size_t)nInstances;
            }

            if (vertexCount == 0 || faceCount == 0)
                ErrorExit($"{filename}: PLY file is invalid! No face/vertex elements found!");

            mesh.p.resize(vertexCount);
            if (rply.ply_set_read_cb(ply, "vertex", "x", rply_vertex_callback, mesh.p, 0x30) == 0 ||
                rply.ply_set_read_cb(ply, "vertex", "y", rply_vertex_callback, mesh.p, 0x31) == 0 ||
                rply.ply_set_read_cb(ply, "vertex", "z", rply_vertex_callback, mesh.p, 0x32) == 0)
            {
                ErrorExit($"{filename}: Vertex coordinate property not found!");
            }

            mesh.n.resize(vertexCount);
            if (rply.ply_set_read_cb(ply, "vertex", "nx", rply_vertex_callback, mesh.n, 0x30) == 0 ||
                rply.ply_set_read_cb(ply, "vertex", "ny", rply_vertex_callback, mesh.n, 0x31) == 0 ||
                rply.ply_set_read_cb(ply, "vertex", "nz", rply_vertex_callback, mesh.n, 0x32) == 0)
                mesh.n.resize(0);

            /* There seem to be lots of different conventions regarding UV coordinate
             * names */
            mesh.uv.resize(vertexCount);
            if (((rply.ply_set_read_cb(ply, "vertex", "u", rply_vertex_callback, mesh.uv, 0x20) != 0) &&
                 (rply.ply_set_read_cb(ply, "vertex", "v", rply_vertex_callback, mesh.uv, 0x21) != 0)) ||
                ((rply.ply_set_read_cb(ply, "vertex", "s", rply_vertex_callback, mesh.uv, 0x20) != 0) &&
                 (rply.ply_set_read_cb(ply, "vertex", "t", rply_vertex_callback, mesh.uv, 0x21) != 0)) ||
                ((rply.ply_set_read_cb(ply, "vertex", "texture_u", rply_vertex_callback, mesh.uv, 0x20) != 0) &&
                 (rply.ply_set_read_cb(ply, "vertex", "texture_v", rply_vertex_callback, mesh.uv, 0x21) != 0)) ||
                ((rply.ply_set_read_cb(ply, "vertex", "texture_s", rply_vertex_callback, mesh.uv, 0x20) != 0) &&
                 (rply.ply_set_read_cb(ply, "vertex", "texture_t", rply_vertex_callback, mesh.uv, 0x21) != 0)))
                ;
            else
                mesh.uv.resize(0);

            FaceCallbackContext context = new FaceCallbackContext();
            context.triIndices.reserve(faceCount * 3);
            context.quadIndices.reserve(faceCount * 4);
            if (rply.ply_set_read_cb(ply, "face", "vertex_indices", rply_face_callback, context, 0) == 0)
                ErrorExit($"{filename}: vertex indices not found in PLY file");

            if (rply.ply_set_read_cb(ply, "face", "face_indices", rply_faceindex_callback, mesh.faceIndices, 0) != 0)
                mesh.faceIndices.reserve(faceCount);

            if (rply.ply_read(ply) == 0)
                ErrorExit($"{filename}: unable to read the contents of PLY file");

            mesh.triIndices = context.triIndices;
            mesh.quadIndices = context.quadIndices;

            rply.ply_close(ply);

            for (int i = 0; i < (int)mesh.triIndices.size(); ++i)
            {
                int idx = mesh.triIndices[i];
                if (idx < 0 || idx >= (int)mesh.p.size())
                    ErrorExit($"plymesh: Vertex index {idx} is out of bounds! Valid range is [0..{(int)mesh.p.size()})");
            }
            for (int i = 0; i < (int)mesh.quadIndices.size(); ++i)
            {
                int idx = mesh.quadIndices[i];
                if (idx < 0 || idx >= (int)mesh.p.size())
                    ErrorExit($"plymesh: Vertex index {idx} is out of bounds! Valid range is [0..{(int)mesh.p.size()})");
            }

            return mesh;
        }

        private static string Vec<T>(pstd.vector<T> v)
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < (int)v.size(); ++i) parts.Add(v[i].ToString());
            return "[ " + string.Join(", ", parts) + " ]";
        }

        public void ConvertToOnlyTriangles()
        {
            if (quadIndices.empty())
                return;

            triIndices.reserve(triIndices.size() + 3 * quadIndices.size() / 2);

            for (size_t i = 0; i < quadIndices.size(); i += 4)
            {
                triIndices.push_back(quadIndices[i]);  // 0, 1, 2 of original
                triIndices.push_back(quadIndices[i + 1]);
                triIndices.push_back(quadIndices[i + 3]);

                triIndices.push_back(quadIndices[i]);  // 0, 2, 3 of original
                triIndices.push_back(quadIndices[i + 3]);
                triIndices.push_back(quadIndices[i + 2]);
            }

            quadIndices.clear();
        }

        public TriQuadMesh Clone()
        {
            TriQuadMesh m = new TriQuadMesh();
            m.p = new pstd.vector<Point3f>(p.ToArray());
            m.n = new pstd.vector<Normal3f>(n.ToArray());
            m.uv = new pstd.vector<Point2f>(uv.ToArray());
            m.faceIndices = new pstd.vector<int>(faceIndices.ToArray());
            m.triIndices = new pstd.vector<int>(triIndices.ToArray());
            m.quadIndices = new pstd.vector<int>(quadIndices.ToArray());
            return m;
        }

        public void ComputeNormals()
        {
            n.resize(p.size());
            for (size_t i = 0; i < n.size(); ++i)
                n[i] = new Normal3f(0, 0, 0);

            for (size_t i = 0; i < triIndices.size(); i += 3)
            {
                int* v = stackalloc int[3];
                v[0] = triIndices[i]; v[1] = triIndices[i + 1]; v[2] = triIndices[i + 2];
                Vector3f v10 = p[v[1]] - p[v[0]];
                Vector3f v21 = p[v[2]] - p[v[1]];

                Vector3f cr = Cross(v10, v21); Normal3f vn = new Normal3f(cr.x, cr.y, cr.z);
                if (LengthSquared(vn) > 0)
                {
                    vn = Normalize(vn);
                    n[v[0]] += vn;
                    n[v[1]] += vn;
                    n[v[2]] += vn;
                }
            }
            CHECK_EQ((ulong)0, quadIndices.size());  // TODO: handle this...

            for (size_t i = 0; i < n.size(); ++i)
                if (LengthSquared(n[i]) > 0)
                    n[i] = Normalize(n[i]);
        }

        public pstd.vector<Point3f> p = new pstd.vector<Point3f>();
        public pstd.vector<Normal3f> n = new pstd.vector<Normal3f>();
        public pstd.vector<Point2f> uv = new pstd.vector<Point2f>();
        public pstd.vector<int> faceIndices = new pstd.vector<int>();
        public pstd.vector<int> triIndices = new pstd.vector<int>(), quadIndices = new pstd.vector<int>();

        public override string ToString()
        {
            return "[ TriQuadMesh p: " + Vec(p) + " n: " + Vec(n) + " uv: " + Vec(uv) + " faceIndices: " + Vec(faceIndices) +
                   " triIndices: " + Vec(triIndices) + " quadIndices: " + Vec(quadIndices) + " ]";
        }

        private class FaceCallbackContext
        {
            public int[] face = new int[4];
            public pstd.vector<int> triIndices = new pstd.vector<int>(), quadIndices = new pstd.vector<int>();
        }

        private static void rply_message_callback(p_ply ply, string message)
        {
            Warning($"rply: {message}");
        }

        /* Callback to handle vertex data from RPly */
        private static int rply_vertex_callback(p_ply_argument argument)
        {
            object buffer;
            long index, flags;

            rply.ply_get_argument_user_data(argument, out buffer, out flags);
            p_ply_element el;
            rply.ply_get_argument_element(argument, out el, out index);

            int stride = (int)((flags & 0x0F0) >> 4);
            int offset = (int)(flags & 0x00F);
            float value = (float)rply.ply_get_argument_value(argument);

            if (stride == 3)
            {
                // Point3f or Normal3f
                pstd.vector<Point3f> pv = buffer as pstd.vector<Point3f>;
                if (pv != null)
                {
                    Point3f t = pv[index]; t[offset] = value; pv[index] = t;
                }
                else
                {
                    pstd.vector<Normal3f> nv = (pstd.vector<Normal3f>)buffer;
                    Normal3f t = nv[index]; t[offset] = value; nv[index] = t;
                }
            }
            else
            {
                pstd.vector<Point2f> uvv = (pstd.vector<Point2f>)buffer;
                Point2f t = uvv[index]; t[offset] = value; uvv[index] = t;
            }

            return 1;
        }

        /* Callback to handle face data from RPly */
        private static int rply_face_callback(p_ply_argument argument)
        {
            object ctx;
            long flags;
            rply.ply_get_argument_user_data(argument, out ctx, out flags);
            FaceCallbackContext context = (FaceCallbackContext)ctx;

            long length, value_index;
            p_ply_property prop;
            rply.ply_get_argument_property(argument, out prop, out length, out value_index);

            if (length != 3 && length != 4)
            {
                Warning($"plymesh: Ignoring face with {(int)length} vertices (only triangles and quads " +
                        "are supported!)");
                return 1;
            }
            else if (value_index < 0)
            {
                return 1;
            }

            if (value_index >= 0)
                context.face[value_index] = (int)rply.ply_get_argument_value(argument);

            if (value_index == length - 1)
            {
                if (length == 3)
                    for (int i = 0; i < 3; ++i)
                        context.triIndices.push_back(context.face[i]);
                else
                {
                    CHECK_EQ(length, 4);

                    // Note: modify order since we're specifying it as a blp...
                    context.quadIndices.push_back(context.face[0]);
                    context.quadIndices.push_back(context.face[1]);
                    context.quadIndices.push_back(context.face[3]);
                    context.quadIndices.push_back(context.face[2]);
                }
            }

            return 1;
        }

        private static int rply_faceindex_callback(p_ply_argument argument)
        {
            object fi;
            long flags;
            rply.ply_get_argument_user_data(argument, out fi, out flags);
            pstd.vector<int> faceIndices = (pstd.vector<int>)fi;

            faceIndices.push_back((int)rply.ply_get_argument_value(argument));

            return 1;
        }
    }

}
