//
//   Copyright 2015 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;

namespace OpenSubdiv.Far
{
    /// \brief Object used to compute and query ptex face indices.
    ///
    public class PtexIndices
    {
        /// \brief Constructor
        public PtexIndices(TopologyRefiner refiner)
        {
            initializePtexIndices(refiner);
        }

        //  The destructor has no equivalent in C#.

        /// \brief Returns the number of ptex faces in the mesh
        public int GetNumFaces()
        {
            return _ptexIndices[_ptexIndices.Count - 1];
        }

        /// \brief Returns the ptex face index given a coarse face 'f' or -1
        public int GetFaceId(int f)
        {
            System.Diagnostics.Debug.Assert(f < (int)_ptexIndices.Count);
            return _ptexIndices[f];
        }

        /// \brief Returns ptex face adjacency information for a given coarse face
        ///
        /// @param refiner    The TopologyRefiner containing the topology
        ///
        /// @param face       Coarse face index
        ///
        /// @param quadrant   Quadrant index if 'face' is not a quad (the local
        ///                   index of the sub-face)
        ///
        /// @param adjFaces   ptex face indices of the 4 faces adjacent to the quadrant
        ///
        /// @param adjEdges   ptex edge indices of the 4 faces adjacent to the quadrant
        ///
        public void GetAdjacency(
            TopologyRefiner refiner,
            int face, int quadrant,
            int[] adjFaces, int[] adjEdges)
        {
            int regFaceSize =
                SchemeTypeTraits.GetRegularFaceSize(refiner.GetSchemeType());

            Level level = refiner.getLevel(0);

            ConstIndexArray fedges = level.getFaceEdges(face);

            if (fedges.size() == regFaceSize)
            {
                // Regular ptex quad face
                for (int i = 0; i < regFaceSize; ++i)
                {
                    int edge = fedges[i];
                    int adjface = getAdjacentFace(level, edge, face);
                    if (adjface == -1)
                    {
                        adjFaces[i] = -1;  // boundary or non-manifold
                        adjEdges[i] = 0;
                    }
                    else
                    {
                        ConstIndexArray aedges = level.getFaceEdges(adjface);
                        if (aedges.size() == regFaceSize)
                        {
                            adjFaces[i] = _ptexIndices[adjface];
                            adjEdges[i] = aedges.FindIndex(edge);
                            System.Diagnostics.Debug.Assert(adjEdges[i] != -1);
                        }
                        else
                        {
                            // neighbor is a sub-face
                            adjFaces[i] = _ptexIndices[adjface] +
                                          (aedges.FindIndex(edge) + 1) % aedges.size();
                            adjEdges[i] = 3;
                        }
                        System.Diagnostics.Debug.Assert(adjFaces[i] != -1);
                    }
                }
                if (regFaceSize == 3)
                {
                    adjFaces[3] = -1;
                    adjEdges[3] = 0;
                }
            }
            else if (regFaceSize == 4)
            {
                //  Ptex sub-face 'quadrant' (non-quad)
                //
                // Ptex adjacency pattern for non-quads:
                //
                //             v2
                /*             o
                //            / \
                //           /   \
                //          /0   3\
                //         /       \
                //        o_ 1   2 _o
                //       /  -_   _-  \
                //      /  2  -o-  1  \
                //     /3      |      0\
                //    /       1|2       \
                //   /    0    |    3    \
                //  o----------o----------o
                // v0                     v1
                */
                System.Diagnostics.Debug.Assert(quadrant >= 0 && quadrant < fedges.size());

                int nextQuadrant = (quadrant + 1) % fedges.size(),
                    prevQuadrant = (quadrant + fedges.size() - 1) % fedges.size();

                {   // resolve neighbors within the sub-face (edges 1 & 2)
                    adjFaces[1] = _ptexIndices[face] + nextQuadrant;
                    adjEdges[1] = 2;

                    adjFaces[2] = _ptexIndices[face] + prevQuadrant;
                    adjEdges[2] = 1;
                }

                {   // resolve neighbor outside the sub-face (edge 0)
                    int edge0 = fedges[quadrant];
                    int adjface0 = getAdjacentFace(level, edge0, face);
                    if (adjface0 == -1)
                    {
                        adjFaces[0] = -1;  // boundary or non-manifold
                        adjEdges[0] = 0;
                    }
                    else
                    {
                        ConstIndexArray afedges = level.getFaceEdges(adjface0);
                        if (afedges.size() == 4)
                        {
                            adjFaces[0] = _ptexIndices[adjface0];
                            adjEdges[0] = afedges.FindIndexIn4Tuple(edge0);
                        }
                        else
                        {
                            int subedge = (afedges.FindIndex(edge0) + 1) % afedges.size();
                            adjFaces[0] = _ptexIndices[adjface0] + subedge;
                            adjEdges[0] = 3;
                        }
                        System.Diagnostics.Debug.Assert(adjFaces[0] != -1);
                    }

                    // resolve neighbor outside the sub-face (edge 3)
                    int edge3 = fedges[prevQuadrant];
                    int adjface3 = getAdjacentFace(level, edge3, face);
                    if (adjface3 == -1)
                    {
                        adjFaces[3] = -1;  // boundary or non-manifold
                        adjEdges[3] = 0;
                    }
                    else
                    {
                        ConstIndexArray afedges = level.getFaceEdges(adjface3);
                        if (afedges.size() == 4)
                        {
                            adjFaces[3] = _ptexIndices[adjface3];
                            adjEdges[3] = afedges.FindIndexIn4Tuple(edge3);
                        }
                        else
                        {
                            int subedge = afedges.FindIndex(edge3);
                            adjFaces[3] = _ptexIndices[adjface3] + subedge;
                            adjEdges[3] = 0;
                        }
                        System.Diagnostics.Debug.Assert(adjFaces[3] != -1);
                    }
                }
            }
            else
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in PtexIndices::GetAdjacency() -- " +
                    "irregular faces only supported for quad schemes.");
            }
        }

        private void initializePtexIndices(TopologyRefiner refiner)
        {
            int regFaceSize = SchemeTypeTraits.GetRegularFaceSize(
                    refiner.GetSchemeType());

            Level coarseLevel = refiner.getLevel(0);

            int nfaces = coarseLevel.getNumFaces();
            //  _ptexIndices.resize(nfaces+1)
            _ptexIndices = new List<int>(new int[nfaces + 1]);
            int ptexID = 0;
            for (int i = 0; i < nfaces; ++i)
            {
                _ptexIndices[i] = ptexID;
                ConstIndexArray fverts = coarseLevel.getFaceVertices(i);
                ptexID += fverts.size() == regFaceSize ? 1 : fverts.size();
            }
            // last entry contains the number of ptex texture faces
            _ptexIndices[nfaces] = ptexID;
        }

        // Returns the face adjacent to 'face' along edge 'edge'
        private static int getAdjacentFace(Level level, int edge, int face)
        {
            ConstIndexArray adjFaces = level.getEdgeFaces(edge);
            if (adjFaces.size() != 2)
            {
                return -1;
            }
            return (adjFaces[0] == face) ? adjFaces[1] : adjFaces[0];
        }

        private List<int> _ptexIndices;
    }
}
