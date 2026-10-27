// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/loopsubdiv.h, util/loopsubdiv.cpp
//
// SDVertex / SDFace / SDEdge are reference types, so their "pointers" are references. Pointers
// are only compared for identity or ordered (SDEdge's min/max of two vertex addresses), so each
// SDVertex carries a unique id that provides the same strict ordering.

using System;
using System.Collections.Generic;

namespace pbrt
{

    // LoopSubdiv Local Structures
    internal unsafe class SDVertex
    {
        private static long nextId = 0;

        // SDVertex Constructor
        public SDVertex() { p = new Point3f(0, 0, 0); id = nextId++; }
        public SDVertex(Point3f p) { this.p = p; id = nextId++; }

        // SDVertex Methods
        public int valence()
        {
            SDFace f = startFace;
            if (!boundary)
            {
                // Compute valence of interior vertex
                int nf = 1;
                while ((f = f.nextFace(this)) != startFace)
                    ++nf;
                return nf;
            }
            else
            {
                // Compute valence of boundary vertex
                int nf = 1;
                while ((f = f.nextFace(this)) != null)
                    ++nf;
                f = startFace;
                while ((f = f.prevFace(this)) != null)
                    ++nf;
                return nf + 1;
            }
        }

        public void oneRing(Point3f[] p)
        {
            int pi = 0;
            if (!boundary)
            {
                // Get one-ring vertices for interior vertex
                SDFace face = startFace;
                do
                {
                    p[pi++] = face.nextVert(this).p;
                    face = face.nextFace(this);
                } while (face != startFace);
            }
            else
            {
                // Get one-ring vertices for boundary vertex
                SDFace face = startFace, f2;
                while ((f2 = face.nextFace(this)) != null)
                    face = f2;
                p[pi++] = face.nextVert(this).p;
                do
                {
                    p[pi++] = face.prevVert(this).p;
                    face = face.prevFace(this);
                } while (face != null);
            }
        }

        public Point3f p;
        public SDFace startFace = null;
        public SDVertex child = null;
        public bool regular = false, boundary = false;
        public readonly long id;
    }

    internal unsafe class SDFace
    {
        // LoopSubdiv Macros
        public static int NEXT(int i) { return (i + 1) % 3; }
        public static int PREV(int i) { return (i + 2) % 3; }

        // SDFace Constructor
        public SDFace()
        {
            for (int i = 0; i < 3; ++i)
            {
                v[i] = null;
                f[i] = null;
            }
            for (int i = 0; i < 4; ++i)
                children[i] = null;
        }

        // SDFace Methods
        public int vnum(SDVertex vert)
        {
            for (int i = 0; i < 3; ++i)
                if (v[i] == vert)
                    return i;
            pbrt.LOG_FATAL("Basic logic error in SDFace::vnum()");
            return -1;
        }
        public SDFace nextFace(SDVertex vert) { return f[vnum(vert)]; }
        public SDFace prevFace(SDVertex vert) { return f[PREV(vnum(vert))]; }
        public SDVertex nextVert(SDVertex vert) { return v[NEXT(vnum(vert))]; }
        public SDVertex prevVert(SDVertex vert) { return v[PREV(vnum(vert))]; }
        public SDVertex otherVert(SDVertex v0, SDVertex v1)
        {
            for (int i = 0; i < 3; ++i)
                if (v[i] != v0 && v[i] != v1)
                    return v[i];
            pbrt.LOG_FATAL("Basic logic error in SDFace::otherVert()");
            return null;
        }
        public SDVertex[] v = new SDVertex[3];
        public SDFace[] f = new SDFace[3];
        public SDFace[] children = new SDFace[4];
    }

    internal unsafe class SDEdge
    {
        // SDEdge Constructor
        public SDEdge(SDVertex v0 = null, SDVertex v1 = null)
        {
            // std::min / std::max of the two pointers
            long i0 = v0 == null ? -1 : v0.id, i1 = v1 == null ? -1 : v1.id;
            v[0] = i0 <= i1 ? v0 : v1;
            v[1] = i0 <= i1 ? v1 : v0;
            f[0] = f[1] = null;
            f0edgeNum = -1;
        }

        // SDEdge key (replaces operator< of std::set / std::map)
        public (long, long) Key()
        {
            return (v[0] == null ? -1 : v[0].id, v[1] == null ? -1 : v[1].id);
        }

        public SDVertex[] v = new SDVertex[2];
        public SDFace[] f = new SDFace[2];
        public int f0edgeNum;
    }

    public static unsafe partial class pbrt
    {
        // LoopSubdiv Inline Functions
        private static Float beta(int valence)
        {
            if (valence == 3)
                return 3f / 16f;
            else
                return 3f / (8f * valence);
        }

        private static Float loopGamma(int valence)
        {
            return 1f / (valence + 3f / (8f * beta(valence)));
        }

        // LoopSubdiv Function Definitions
        public static TriangleMesh LoopSubdivide(Transform* renderFromObject, bool reverseOrientation, int nLevels,
                                                 pstd.span<int> vertexIndices, pstd.span<Point3f> p, Allocator alloc)
        {
            List<SDVertex> vertices = new List<SDVertex>();
            List<SDFace> faces = new List<SDFace>();
            // Allocate _LoopSubdiv_ vertices and faces
            SDVertex[] verts = new SDVertex[p.size()];
            for (int i = 0; i < (int)p.size(); ++i)
            {
                verts[i] = new SDVertex(p[i]);
                vertices.Add(verts[i]);
            }
            size_t nFaces = vertexIndices.size() / 3;
            SDFace[] fs = new SDFace[nFaces];
            for (int i = 0; i < (int)nFaces; ++i)
            {
                fs[i] = new SDFace();
                faces.Add(fs[i]);
            }

            // Set face to vertex pointers
            int vp = 0;
            for (size_t i = 0; i < nFaces; ++i, vp += 3)
            {
                SDFace f = faces[(int)i];
                for (int j = 0; j < 3; ++j)
                {
                    SDVertex v = vertices[vertexIndices[vp + j]];
                    f.v[j] = v;
                    v.startFace = f;
                }
            }

            // Set neighbor pointers in _faces_
            Dictionary<(long, long), SDEdge> edges = new Dictionary<(long, long), SDEdge>();
            for (int i = 0; i < (int)nFaces; ++i)
            {
                SDFace f = faces[i];
                for (int edgeNum = 0; edgeNum < 3; ++edgeNum)
                {
                    // Update neighbor pointer for _edgeNum_
                    int v0 = edgeNum, v1 = SDFace.NEXT(edgeNum);
                    SDEdge e = new SDEdge(f.v[v0], f.v[v1]);
                    SDEdge found;
                    if (!edges.TryGetValue(e.Key(), out found))
                    {
                        // Handle new edge
                        e.f[0] = f;
                        e.f0edgeNum = edgeNum;
                        edges[e.Key()] = e;
                    }
                    else
                    {
                        // Handle previously seen edge
                        e = found;
                        e.f[0].f[e.f0edgeNum] = f;
                        f.f[edgeNum] = e.f[0];
                        edges.Remove(e.Key());
                    }
                }
            }

            // Finish vertex initialization
            for (size_t i = 0; i < p.size(); ++i)
            {
                SDVertex v = vertices[(int)i];
                SDFace f = v.startFace;
                do
                {
                    f = f.nextFace(v);
                } while ((f != null) && f != v.startFace);
                v.boundary = (f == null);
                if (!v.boundary && v.valence() == 6)
                    v.regular = true;
                else if (v.boundary && v.valence() == 4)
                    v.regular = true;
                else
                    v.regular = false;
            }

            // Refine _LoopSubdiv_ into triangles
            List<SDFace> f_ = new List<SDFace>(faces);
            List<SDVertex> v_ = new List<SDVertex>(vertices);

            for (int i = 0; i < nLevels; ++i)
            {
                // Update _f_ and _v_ for next level of subdivision
                List<SDFace> newFaces = new List<SDFace>();
                List<SDVertex> newVertices = new List<SDVertex>();

                // Allocate next level of children in mesh tree
                foreach (SDVertex vertex in v_)
                {
                    vertex.child = new SDVertex();
                    vertex.child.regular = vertex.regular;
                    vertex.child.boundary = vertex.boundary;
                    newVertices.Add(vertex.child);
                }
                foreach (SDFace face in f_)
                {
                    for (int k = 0; k < 4; ++k)
                    {
                        face.children[k] = new SDFace();
                        newFaces.Add(face.children[k]);
                    }
                }

                // Update vertex positions and create new edge vertices

                // Update vertex positions for even vertices
                foreach (SDVertex vertex in v_)
                {
                    if (!vertex.boundary)
                    {
                        // Apply one-ring rule for even vertex
                        if (vertex.regular)
                            vertex.child.p = weightOneRing(vertex, 1f / 16f);
                        else
                            vertex.child.p = weightOneRing(vertex, beta(vertex.valence()));
                    }
                    else
                    {
                        // Apply boundary rule for even vertex
                        vertex.child.p = weightBoundary(vertex, 1f / 8f);
                    }
                }

                // Compute new odd edge vertices
                Dictionary<(long, long), SDVertex> edgeVerts = new Dictionary<(long, long), SDVertex>();
                foreach (SDFace face in f_)
                {
                    for (int k = 0; k < 3; ++k)
                    {
                        // Compute odd vertex on _k_th edge
                        SDEdge edge = new SDEdge(face.v[k], face.v[SDFace.NEXT(k)]);
                        SDVertex vert;
                        edgeVerts.TryGetValue(edge.Key(), out vert);
                        if (vert == null)
                        {
                            // Create and initialize new odd vertex
                            vert = new SDVertex();
                            newVertices.Add(vert);
                            vert.regular = true;
                            vert.boundary = (face.f[k] == null);
                            vert.startFace = face.children[3];

                            // Apply edge rules to compute new vertex position
                            if (vert.boundary)
                            {
                                vert.p = 0.5f * edge.v[0].p;
                                vert.p += 0.5f * edge.v[1].p;
                            }
                            else
                            {
                                vert.p = 3f / 8f * edge.v[0].p;
                                vert.p += 3f / 8f * edge.v[1].p;
                                vert.p += 1f / 8f * face.otherVert(edge.v[0], edge.v[1]).p;
                                vert.p += 1f / 8f * face.f[k].otherVert(edge.v[0], edge.v[1]).p;
                            }
                            edgeVerts[edge.Key()] = vert;
                        }
                    }
                }

                // Update new mesh topology

                // Update even vertex face pointers
                foreach (SDVertex vertex in v_)
                {
                    int vertNum = vertex.startFace.vnum(vertex);
                    vertex.child.startFace = vertex.startFace.children[vertNum];
                }

                // Update face neighbor pointers
                foreach (SDFace face in f_)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        // Update children _f_ pointers for siblings
                        face.children[3].f[j] = face.children[SDFace.NEXT(j)];
                        face.children[j].f[SDFace.NEXT(j)] = face.children[3];

                        // Update children _f_ pointers for neighbor children
                        SDFace f2 = face.f[j];
                        face.children[j].f[j] = f2 != null ? f2.children[f2.vnum(face.v[j])] : null;
                        f2 = face.f[SDFace.PREV(j)];
                        face.children[j].f[SDFace.PREV(j)] = f2 != null ? f2.children[f2.vnum(face.v[j])] : null;
                    }
                }

                // Update face vertex pointers
                foreach (SDFace face in f_)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        // Update child vertex pointer to new even vertex
                        face.children[j].v[j] = face.v[j].child;

                        // Update child vertex pointer to new odd vertex
                        SDVertex vert = edgeVerts[new SDEdge(face.v[j], face.v[SDFace.NEXT(j)]).Key()];
                        face.children[j].v[SDFace.NEXT(j)] = vert;
                        face.children[SDFace.NEXT(j)].v[j] = vert;
                        face.children[3].v[j] = vert;
                    }
                }

                // Prepare for next level of subdivision
                f_ = newFaces;
                v_ = newVertices;
            }

            // Push vertices to limit surface
            Point3f[] pLimit = new Point3f[v_.Count];
            for (int i = 0; i < v_.Count; ++i)
            {
                if (v_[i].boundary)
                    pLimit[i] = weightBoundary(v_[i], 1f / 5f);
                else
                    pLimit[i] = weightOneRing(v_[i], loopGamma(v_[i].valence()));
            }
            for (int i = 0; i < v_.Count; ++i)
                v_[i].p = pLimit[i];

            // Compute vertex tangents on limit surface
            List<Normal3f> Ns = new List<Normal3f>(v_.Count);
            Point3f[] pRing = new Point3f[16];
            foreach (SDVertex vertex in v_)
            {
                Vector3f S = new Vector3f(0, 0, 0), T = new Vector3f(0, 0, 0);
                int valence = vertex.valence();
                if (valence > pRing.Length)
                    Array.Resize(ref pRing, valence);
                vertex.oneRing(pRing);
                if (!vertex.boundary)
                {
                    // Compute tangents of interior face
                    for (int j = 0; j < valence; ++j)
                    {
                        S += std.cos(2 * Pi * j / valence) * (Vector3f)pRing[j];
                        T += std.sin(2 * Pi * j / valence) * (Vector3f)pRing[j];
                    }
                }
                else
                {
                    // Compute tangents of boundary face
                    S = pRing[valence - 1] - pRing[0];
                    if (valence == 2)
                        T = (Vector3f)(pRing[0] + pRing[1] - 2 * vertex.p);
                    else if (valence == 3)
                        T = pRing[1] - vertex.p;
                    else if (valence == 4)  // regular
                        T = (Vector3f)(-1 * pRing[0] + 2 * pRing[1] + 2 * pRing[2] + -1 * pRing[3] + -2 * vertex.p);
                    else
                    {
                        Float theta = Pi / (float)(valence - 1);
                        T = (Vector3f)(std.sin(theta) * (pRing[0] + pRing[valence - 1]));
                        for (int k = 1; k < valence - 1; ++k)
                        {
                            Float wt = (2 * std.cos(theta) - 2) * std.sin((k) * theta);
                            T += (Vector3f)(wt * pRing[k]);
                        }
                        T = -T;
                    }
                }
                Vector3f cr = Cross(S, T);
                Ns.Add(new Normal3f(cr.x, cr.y, cr.z));
            }

            // Create triangle mesh from subdivision mesh
            {
                size_t ntris = (size_t)f_.Count;
                int[] vertsOut = new int[3 * ntris];
                int vpo = 0;
                size_t totVerts = (size_t)v_.Count;
                Dictionary<SDVertex, int> usedVerts = new Dictionary<SDVertex, int>();
                for (int i = 0; i < (int)totVerts; ++i)
                    usedVerts[v_[i]] = i;
                for (int i = 0; i < (int)ntris; ++i)
                {
                    for (int j = 0; j < 3; ++j)
                    {
                        vertsOut[vpo] = usedVerts[f_[i].v[j]];
                        ++vpo;
                    }
                }
                return new TriangleMesh(*renderFromObject, reverseOrientation, new pstd.vector<int>(vertsOut),
                                        new pstd.vector<Point3f>(pLimit), new pstd.vector<Vector3f>(),
                                        new pstd.vector<Normal3f>(Ns), new pstd.vector<Point2f>(),
                                        new pstd.vector<int>(), alloc);
            }
        }

        private static Point3f weightOneRing(SDVertex vert, Float beta)
        {
            // Put _vert_ one-ring in _pRing_
            int valence = vert.valence();
            Point3f[] pRing = new Point3f[valence];  // InlinedVector<Point3f, 16>

            vert.oneRing(pRing);
            Point3f p = (1 - valence * beta) * vert.p;
            for (int i = 0; i < valence; ++i)
                p += beta * pRing[i];
            return p;
        }

        private static Point3f weightBoundary(SDVertex vert, Float beta)
        {
            // Put _vert_ one-ring in _pRing_
            int valence = vert.valence();
            Point3f[] pRing = new Point3f[valence];  // InlinedVector<Point3f, 16>

            vert.oneRing(pRing);
            Point3f p = (1 - 2 * beta) * vert.p;
            p += beta * pRing[0];
            p += beta * pRing[valence - 1];
            return p;
        }
    }

}  // namespace pbrt
