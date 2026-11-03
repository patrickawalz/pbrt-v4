//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/componentInterfaces.h
//
//  Simple classes supporting the interfaces required of generic topological
//  types in the Scheme mask queries, e.g. <typename FACE, VERTEX, etc.>.  They implement the
//  OpenSubdiv.Sdc interfaces IFaceNeighborhood / IEdgeNeighborhood / IVertexNeighborhood.
using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;

using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;

namespace OpenSubdiv.Vtr.Internal
{
    //
    //  For <typename FACE>, which provides information in the neighborhood of a face:
    //
    public class FaceInterface : IFaceNeighborhood
    {
        private int _vertCount;

        public FaceInterface()
        {
        }

        public FaceInterface(int vertCount)
        {
            _vertCount = vertCount;
        }

        //  Generic interface expected of <typename FACE>:
        public int GetNumVertices()
        {
            return _vertCount;
        }
    }

    //
    //  For <typename EDGE>, which provides information in the neighborhood of an edge:
    //
    public class EdgeInterface : IEdgeNeighborhood
    {
        private Level _level;

        private int _eIndex;

        public EdgeInterface()
        {
        }

        public EdgeInterface(Level level)
        {
            _level = level;
        }

        public void SetIndex(int edgeIndex)
        {
            _eIndex = edgeIndex;
        }

        //  Generic interface expected of <typename EDGE>:
        public int GetNumFaces()
        {
            return _level.getEdgeFaces(_eIndex).size();
        }

        public float GetSharpness()
        {
            return _level.getEdgeSharpness(_eIndex);
        }

        public void GetChildSharpnesses(Crease crease, float[] s)
        {
            //  Need to use the Refinement here to identify the two child edges:
            s[0] = s[1] = GetSharpness() - 1.0f;
        }

        public void GetNumVerticesPerFace(int[] vertsPerFace)
        {
            ConstIndexArray eFaces = _level.getEdgeFaces(_eIndex);
            for (int i = 0; i < eFaces.size(); ++i)
            {
                vertsPerFace[i] = _level.getFaceVertices(eFaces[i]).size();
            }
        }
    }

    //
    //  For <typename VERTEX>, which provides information in the neighborhood of a vertex:
    //
    public class VertexInterface : IVertexNeighborhood
    {
        private Level _parent;
        private Level _child;

        private int _pIndex;
        private int _cIndex;
        private int _eCount;
        private int _fCount;

        public VertexInterface()
        {
        }

        public VertexInterface(Level parent, Level child)
        {
            _parent = parent;
            _child = child;
        }

        public void SetIndex(int parentIndex, int childIndex)
        {
            _pIndex = parentIndex;
            _cIndex = childIndex;
            _eCount = _parent.getVertexEdges(_pIndex).size();
            _fCount = _parent.getVertexFaces(_pIndex).size();
        }

        //  Generic interface expected of <typename VERT>:
        public int GetNumEdges()
        {
            return _eCount;
        }

        public int GetNumFaces()
        {
            return _fCount;
        }

        public float GetSharpness()
        {
            return _parent.getVertexSharpness(_pIndex);
        }

        public float[] GetSharpnessPerEdge(float[] pSharpness)
        {
            ConstIndexArray pEdges = _parent.getVertexEdges(_pIndex);
            for (int i = 0; i < _eCount; ++i)
            {
                pSharpness[i] = _parent.getEdgeSharpness(pEdges[i]);
            }
            return pSharpness;
        }

        public float GetChildSharpness(Crease crease)
        {
            return _child.getVertexSharpness(_cIndex);
        }

        public float[] GetChildSharpnessPerEdge(Crease crease, float[] cSharpness)
        {
            StackBuffer<float> pSharpness = new StackBuffer<float>(_eCount);
            GetSharpnessPerEdge(pSharpness);
            crease.SubdivideEdgeSharpnessesAroundVertex(_eCount, pSharpness, cSharpness);
            return cSharpness;
        }
    }
}
