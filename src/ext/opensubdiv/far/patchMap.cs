//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Vtr;

using ConstPatchParamArray = OpenSubdiv.Vtr.ConstArray<OpenSubdiv.Far.PatchParam>;

namespace OpenSubdiv.Far
{
    /// \brief An quadtree-based map connecting coarse faces to their sub-patches.
    ///
    /// Quadtree-based map connecting coarse faces to their sub-patches.
    ///
    /// PatchTable::PatchHandle can be obtained from the (u,v) coordinates of
    /// a coarse face.
    ///
    public class PatchMap
    {
        public PatchMap(PatchTable patchTable)
        {
            _minPatchFace = -1;
            _maxPatchFace = -1;
            _maxDepth = 0;

            _patchesAreTriangular =
                patchTable.GetVaryingPatchDescriptor().GetNumControlVertices() == 3;

            if (patchTable.GetNumPatchesTotal() > 0)
            {
                initializeHandles(patchTable);
                initializeQuadtree(patchTable);
            }
        }

        /// \brief Returns a handle to the sub-patch of the face at the given (u,v).
        /// Note that the (u,v) coordinates are expressed in the parametric space of
        /// the face; null is returned if the face is not supported by the map.
        public PatchTable.PatchHandle FindPatch(int faceid, double u, double v)
        {
            //
            //  Reject patch faces not supported by this map, or those corresponding
            //  to holes or otherwise unassigned (the root node for a patch will
            //  have all or no quadrants set):
            //
            if ((faceid < _minPatchFace) || (faceid > _maxPatchFace))
            {
                return null;
            }

            QuadNode node = _quadtree[faceid - _minPatchFace];

            if (!node.children[0].isSet)
            {
                return null;
            }

            //
            //  Search the tree for the sub-patch containing the given (u,v)
            //
            System.Diagnostics.Debug.Assert((u >= 0.0) && (u <= 1.0) && (v >= 0.0) && (v <= 1.0));

            double median = 0.5;
            bool triRotated = false;

            for (int depth = 0; depth <= _maxDepth; ++depth, median *= 0.5)
            {
                int quadrant = _patchesAreTriangular
                             ? transformUVToTriQuadrant(median, ref u, ref v, ref triRotated)
                             : transformUVToQuadQuadrant(median, ref u, ref v);

                //  holes should have been rejected at the root node of the face
                System.Diagnostics.Debug.Assert(node.children[quadrant].isSet);

                if (node.children[quadrant].isLeaf)
                {
                    return _handles[(int)node.children[quadrant].index];
                }
                else
                {
                    node = _quadtree[(int)node.children[quadrant].index];
                }
            }
            System.Diagnostics.Debug.Assert(false);
            return null;
        }

        private void initializeHandles(PatchTable patchTable)
        {
            //  Initialize the map with the min and max patch face index range
            _minPatchFace = (int)patchTable.GetPatchParamTable()[0].GetFaceId();
            _maxPatchFace = _minPatchFace;

            int numArrays = (int)patchTable.GetNumPatchArrays();
            int numPatches = (int)patchTable.GetNumPatchesTotal();

            _handles = new PatchTable.PatchHandle[numPatches];
            for (int i = 0; i < numPatches; ++i)
            {
                _handles[i] = new PatchTable.PatchHandle();
            }

            for (int pArray = 0, handleIndex = 0; pArray < numArrays; ++pArray)
            {
                ConstPatchParamArray patchParams = patchTable.GetPatchParams(pArray);

                int patchSize = patchTable.GetPatchArrayDescriptor(pArray).GetNumControlVertices();

                for (int j = 0; j < patchTable.GetNumPatches(pArray); ++j, ++handleIndex)
                {
                    PatchTable.PatchHandle h = _handles[handleIndex];

                    h.arrayIndex = pArray;
                    h.patchIndex = handleIndex;
                    h.vertIndex = j * patchSize;

                    int patchFaceId = patchParams[j].GetFaceId();

                    _minPatchFace = Math.Min(_minPatchFace, patchFaceId);
                    _maxPatchFace = Math.Max(_maxPatchFace, patchFaceId);
                }
            }
        }

        private void initializeQuadtree(PatchTable patchTable)
        {
            //
            //  Create and initialize the quadtree for this map:
            //
            int nPatchFaces = (_maxPatchFace - _minPatchFace) + 1;
            int nHandles = (int)_handles.Length;

            _quadtree = new List<QuadNode>(nPatchFaces + nHandles);
            for (int i = 0; i < nPatchFaces; ++i)
            {
                _quadtree.Add(new QuadNode());
            }

            //
            //  Populate the quadtree from the patch handles
            //
            StdVector<PatchParam> patchParams = patchTable.GetPatchParamTable();

            for (int handle = 0; handle < nHandles; ++handle)
            {
                PatchParam param = patchParams[handle];

                int depth = param.GetDepth();
                int rootDepth = param.NonQuadRoot() ? 1 : 0;

                _maxDepth = Math.Max(_maxDepth, depth);

                QuadNode node = _quadtree[param.GetFaceId() - _minPatchFace];

                if (depth == rootDepth)
                {
                    //  Special case of a root node -- no tree traversal needed
                    assignRootNode(node, handle);
                    continue;
                }

                //
                //  Use the (u,v) values of the patch to identify the path from
                //  the root to the leaf:
                //
                if (!_patchesAreTriangular)
                {
                    int u = param.GetU();
                    int v = param.GetV();

                    for (int j = rootDepth + 1; j <= depth; ++j)
                    {
                        int uBit = (u >> (depth - j)) & 1;
                        int vBit = (v >> (depth - j)) & 1;

                        int quadrant = (vBit << 1) | uBit;
                        node = assignLeafOrChildNode(node, (j == depth), quadrant, handle);
                    }
                }
                else
                {
                    //  Use the (u,v) of the center of the triangle to traverse:
                    double u = 0.25;
                    double v = 0.25;

                    param.UnnormalizeTriangle(ref u, ref v);

                    double median = 0.5;
                    bool triRotated = false;

                    for (int j = rootDepth + 1; j <= depth; ++j, median *= 0.5)
                    {
                        int quadrant = transformUVToTriQuadrant(median, ref u, ref v, ref triRotated);
                        node = assignLeafOrChildNode(node, (j == depth), quadrant, handle);
                    }
                }
            }

            //  Reduce the capacity of the vector to free unused memory (no-op here):
            _quadtree.TrimExcess();
        }

        // Quadtree node with 4 children, tree is just a vector of nodes
        public class QuadNode
        {
            public QuadNode()
            {
                children = new Child[4];
            }

            public struct Child
            {
                public bool isSet;    // true if the child has been set
                public bool isLeaf;   // true if the child is a QuadNode
                public uint index;    // child index (either QuadNode or Handle)
            }

            // sets all the children to point to the patch of given index
            public void SetChildren(int index)
            {
                for (int i = 0; i < 4; ++i)
                {
                    children[i].isSet = true;
                    children[i].isLeaf = true;
                    children[i].index = (uint)index;
                }
            }

            // sets the child in "quadrant" to point to the node or patch of the given index
            public void SetChild(int quadrant, int index, bool isLeaf)
            {
                System.Diagnostics.Debug.Assert(!children[quadrant].isSet);

                children[quadrant].isSet = true;
                children[quadrant].isLeaf = isLeaf;
                children[quadrant].index = (uint)index;
            }

            public Child[] children;
        }

        // Internal methods supporting quadtree construction and queries
        private void assignRootNode(QuadNode node, int index)
        {
            //  Assign the given index to all children of the node as a leaf
            node.SetChildren(index);
        }

        private QuadNode assignLeafOrChildNode(QuadNode node, bool isLeaf, int quadrant, int index)
        {
            if (isLeaf)
            {
                node.SetChild(quadrant, index, true);
                return node;
            }
            if (node.children[quadrant].isSet)
            {
                return _quadtree[(int)node.children[quadrant].index];
            }
            else
            {
                int newChildNodeIndex = (int)_quadtree.Count;

                _quadtree.Add(new QuadNode());
                node.SetChild(quadrant, newChildNodeIndex, false);
                return _quadtree[newChildNodeIndex];
            }
        }

        //
        //  Given a median value for both U and V, these methods transform a (u,v) pair
        //  into the quadrant that contains them and returns the quadrant index.
        //
        //  Quadrant indexing for tri and quad patches -- consistent with PatchParam's
        //  usage of UV bits:
        //
        //      (0,1) o-----o-----o (1,1)     (0,1) o     (1,0) o-----o-----o (0,0)
        //            |     |     |                 |\           \  1 |\  0 |
        //            |  2  |  3  |                 |  \           \  |  \  |
        //            |     |     |                 | 2  \           \| 3  \|
        //            o-----o-----o                 o-----o           o-----o
        //            |     |     |                 |\  3 |\           \  2 |
        //            |  0  |  1  |                 |  \  |  \           \  |
        //            |     |     |                 | 0  \| 1  \           \|
        //      (0,0) o-----o-----o (1,0)     (0,0) o-----o-----o (1,0)     o (0,1)
        //
        //  The triangular case also takes and returns/affects the rotation of the
        //  quadrant being searched and identified (quadrant 3 imparts a rotation).
        //
        public static int transformUVToQuadQuadrant(double median, ref double u, ref double v)
        {
            int uHalf = (u >= median) ? 1 : 0;
            if (uHalf != 0)
            {
                u -= median;
            }

            int vHalf = (v >= median) ? 1 : 0;
            if (vHalf != 0)
            {
                v -= median;
            }

            return (vHalf << 1) | uHalf;
        }

        public static int transformUVToTriQuadrant(double median, ref double u, ref double v, ref bool rotated)
        {
            if (!rotated)
            {
                if (u >= median)
                {
                    u -= median;
                    return 1;
                }
                if (v >= median)
                {
                    v -= median;
                    return 2;
                }
                if ((u + v) >= median)
                {
                    rotated = true;
                    return 3;
                }
                return 0;
            }
            else
            {
                if (u < median)
                {
                    v -= median;
                    return 1;
                }
                if (v < median)
                {
                    u -= median;
                    return 2;
                }
                u -= median;
                v -= median;

                if ((u + v) < median)
                {
                    rotated = false;
                    return 3;
                }
                return 0;
            }
        }

        private bool _patchesAreTriangular;  // tri and quad assembly and search requirements differ

        private int _minPatchFace;  // minimum patch face index supported by the map
        private int _maxPatchFace;  // maximum patch face index supported by the map
        private int _maxDepth;      // maximum depth of a patch in the tree

        private PatchTable.PatchHandle[] _handles;  // all the patches in the PatchTable
        private List<QuadNode> _quadtree;           // quadtree nodes
    }
}
