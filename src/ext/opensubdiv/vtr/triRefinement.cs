//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;

using IndexArray = OpenSubdiv.Vtr.Array<int>;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using LocalIndexArray = OpenSubdiv.Vtr.Array<ushort>;
using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;
using IndexVector = OpenSubdiv.Vtr.StdVector<int>;

namespace OpenSubdiv.Vtr
{
    //
    //  TriRefinement:
    //      A TriRefinement is a subclass of Refinement that splits all faces into tris.
    //  It provides the configuration of parent-to-child components and the population of
    //  all required topological relations in order to complete a valid Refinement.
    //
    public class TriRefinement : Refinement
    {
        //
        //  Simple constructor, destructor and basic initializers:
        //
        public TriRefinement(Level parentArg, Level childArg, Sdc.Options optionsArg) :
            base(parentArg, childArg, optionsArg)
        {
            _splitType = Sdc.Split.SPLIT_TO_TRIS;
            _regFaceSize = 3;
        }

        //
        //  Methods to construct the parent-to-child mapping
        //
        public override void allocateParentChildIndices()
        {
            //
            //  Initialize the vectors of indices mapping parent components to those child components
            //  that will originate from each.
            //
            //
            //  Beware these child-counts when Loop subdivision supports N-sided faces in the cage
            //      - there will 2*(N-2) additional face-child-faces for each N-sided face
            //      - there will 2*(N-2)+1 additional face-child-edges for each N-sided face
            //      - there will 1 face-child-vertex for each N-sided face
            //  Can consider these reasonable estimates and grow as needed later -- but be clear
            //  about it if so.
            //
            int faceChildFaceCount = _parent.getNumFaces() * 4;
            int faceChildEdgeCount = (int)_parent._faceEdgeIndices.Count;
            int edgeChildEdgeCount = (int)_parent._edgeVertIndices.Count;

            int faceChildVertCount = 0;
            int edgeChildVertCount = _parent.getNumEdges();
            int vertChildVertCount = _parent.getNumVertices();

            //
            //  First initialize the count/offset vectors for the child-faces and child-edges of
            //  parent faces.  For now we can use the parent's face-vert counts for the child-edges
            //  of faces, but we must use a local vector for the child-faces.
            //
            //  This will be more necessary (and need adjustment) when N-sided faces are supported.
            //
            _localFaceChildFaceCountsAndOffsets.Resize(_parent.getNumFaces() * 2, 4);
            for (int i = 0; i < _parent.getNumFaces(); ++i)
            {
                _localFaceChildFaceCountsAndOffsets[i * 2 + 1] = 4 * i;
            }

            _faceChildFaceCountsAndOffsets = new IndexArray(_localFaceChildFaceCountsAndOffsets.Data, 0,
                                                            (int)_localFaceChildFaceCountsAndOffsets.Count);
            _faceChildEdgeCountsAndOffsets = _parent.shareFaceVertCountsAndOffsets();

            //
            //  Given we will be ignoring initial values with uniform refinement and assigning all
            //  directly, initializing here is a waste...
            //
            int initValue = 0;

            _faceChildFaceIndices.Resize(faceChildFaceCount, initValue);
            _faceChildEdgeIndices.Resize(faceChildEdgeCount, initValue);
            _edgeChildEdgeIndices.Resize(edgeChildEdgeCount, initValue);

            _faceChildVertIndex.Resize(faceChildVertCount, initValue);
            _edgeChildVertIndex.Resize(edgeChildVertCount, initValue);
            _vertChildVertIndex.Resize(vertChildVertCount, initValue);
        }

        //
        //  Methods to populate the face-vertex relation of the child Level:
        //      - child faces only originate from parent faces
        //
        public override void populateFaceVertexRelation()
        {
            //  Both face-vertex and face-edge share the face-vertex counts/offsets within a
            //  Level, so be sure not to re-initialize it if already done:
            //
            if (_child._faceVertCountsAndOffsets.Count == 0)
            {
                populateFaceVertexCountsAndOffsets();
            }
            _child._faceVertIndices.Resize(_child.getNumFaces() * 3);

            populateFaceVerticesFromParentFaces();
        }

        public void populateFaceVertexCountsAndOffsets()
        {
            _child._faceVertCountsAndOffsets.Resize(_child.getNumFaces() * 2, 3);

            for (int i = 0; i < _child.getNumFaces(); ++i)
            {
                _child._faceVertCountsAndOffsets[i * 2 + 1] = i * 3;
            }
        }

        public void populateFaceVerticesFromParentFaces()
        {
            for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
            {
                ConstIndexArray pFaceVerts = _parent.getFaceVertices(pFace),
                                pFaceEdges = _parent.getFaceEdges(pFace),
                                pFaceChildren = getFaceChildFaces(pFace);

                System.Diagnostics.Debug.Assert(pFaceVerts.size() == 3);
                System.Diagnostics.Debug.Assert(pFaceChildren.size() == 4);

                int[] cVertsOfPEdges = new int[3];
                cVertsOfPEdges[0] = _edgeChildVertIndex[pFaceEdges[0]];
                cVertsOfPEdges[1] = _edgeChildVertIndex[pFaceEdges[1]];
                cVertsOfPEdges[2] = _edgeChildVertIndex[pFaceEdges[2]];

                //
                //  For the child face at vertex I (where I is 0..2), the child vertex
                //  of vertex I becomes the I'th vertex of its child face.  This matches
                //  the pattern for quads of irregular faces for Catmark.
                //
                //  The orientation for the 4th "interior" face is unclear -- it begins
                //  with the child vertex of the 2nd edge of the triangle.  According
                //  to the notes with the Hbr implementation "the ordering of vertices
                //  here is done to preserve parametric space as best we can."
                //
                if (Types.IndexIsValid(pFaceChildren[0]))
                {
                    IndexArray cFaceVerts = _child.getFaceVertices(pFaceChildren[0]);

                    cFaceVerts[0] = _vertChildVertIndex[pFaceVerts[0]];
                    cFaceVerts[1] = cVertsOfPEdges[0];
                    cFaceVerts[2] = cVertsOfPEdges[2];
                }
                if (Types.IndexIsValid(pFaceChildren[1]))
                {
                    IndexArray cFaceVerts = _child.getFaceVertices(pFaceChildren[1]);

                    cFaceVerts[0] = cVertsOfPEdges[0];
                    cFaceVerts[1] = _vertChildVertIndex[pFaceVerts[1]];
                    cFaceVerts[2] = cVertsOfPEdges[1];
                }
                if (Types.IndexIsValid(pFaceChildren[2]))
                {
                    IndexArray cFaceVerts = _child.getFaceVertices(pFaceChildren[2]);

                    cFaceVerts[0] = cVertsOfPEdges[2];
                    cFaceVerts[1] = cVertsOfPEdges[1];
                    cFaceVerts[2] = _vertChildVertIndex[pFaceVerts[2]];
                }
                if (Types.IndexIsValid(pFaceChildren[3]))
                {
                    IndexArray cFaceVerts = _child.getFaceVertices(pFaceChildren[3]);

                    cFaceVerts[0] = cVertsOfPEdges[1];
                    cFaceVerts[1] = cVertsOfPEdges[2];
                    cFaceVerts[2] = cVertsOfPEdges[0];
                }
            }
        }

        //
        //  Methods to populate the face-vertex relation of the child Level:
        //      - child faces only originate from parent faces
        //
        public override void populateFaceEdgeRelation()
        {
            //  Both face-vertex and face-edge share the face-vertex counts/offsets, so be sure
            //  not to re-initialize it if already done:
            //
            if (_child._faceVertCountsAndOffsets.Count == 0)
            {
                populateFaceVertexCountsAndOffsets();
            }
            _child._faceEdgeIndices.Resize(_child.getNumFaces() * 3);

            populateFaceEdgesFromParentFaces();
        }

        public void populateFaceEdgesFromParentFaces()
        {
            for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
            {
                ConstIndexArray pFaceVerts = _parent.getFaceVertices(pFace),
                                pFaceEdges = _parent.getFaceEdges(pFace),
                                pFaceChildFaces = getFaceChildFaces(pFace),
                                pFaceChildEdges = getFaceChildEdges(pFace);

                System.Diagnostics.Debug.Assert(pFaceChildFaces.size() == 4);
                System.Diagnostics.Debug.Assert(pFaceChildEdges.size() == 3);

                int[,] pEdgeChildEdges = new int[3, 2];
                for (int i = 0; i < 3; ++i)
                {
                    int pEdge = pFaceEdges[i];
                    ConstIndexArray cEdges = getEdgeChildEdges(pEdge);

                    ConstIndexArray pEdgeVerts = _parent.getEdgeVertices(pEdge);

                    //  Be careful to consider degenerate edge when orienting here:
                    bool edgeReversedWrtFace = (pEdgeVerts[0] != pEdgeVerts[1]) &&
                                               (pFaceVerts[i] != pEdgeVerts[0]);

                    pEdgeChildEdges[i, 0] = cEdges[edgeReversedWrtFace ? 1 : 0];
                    pEdgeChildEdges[i, 1] = cEdges[!edgeReversedWrtFace ? 1 : 0];
                }

                if (Types.IndexIsValid(pFaceChildFaces[0]))
                {
                    IndexArray cFaceEdges = _child.getFaceEdges(pFaceChildFaces[0]);

                    cFaceEdges[0] = pEdgeChildEdges[0, 0];
                    cFaceEdges[1] = pFaceChildEdges[0];
                    cFaceEdges[2] = pEdgeChildEdges[2, 1];
                }
                if (Types.IndexIsValid(pFaceChildFaces[1]))
                {
                    IndexArray cFaceEdges = _child.getFaceEdges(pFaceChildFaces[1]);

                    cFaceEdges[0] = pEdgeChildEdges[0, 1];
                    cFaceEdges[1] = pEdgeChildEdges[1, 0];
                    cFaceEdges[2] = pFaceChildEdges[1];
                }
                if (Types.IndexIsValid(pFaceChildFaces[2]))
                {
                    IndexArray cFaceEdges = _child.getFaceEdges(pFaceChildFaces[2]);

                    cFaceEdges[0] = pFaceChildEdges[2];
                    cFaceEdges[1] = pEdgeChildEdges[1, 1];
                    cFaceEdges[2] = pEdgeChildEdges[2, 0];
                }
                if (Types.IndexIsValid(pFaceChildFaces[3]))
                {
                    IndexArray cFaceEdges = _child.getFaceEdges(pFaceChildFaces[3]);

                    cFaceEdges[0] = pFaceChildEdges[2];
                    cFaceEdges[1] = pFaceChildEdges[0];
                    cFaceEdges[2] = pFaceChildEdges[1];
                }
            }
        }

        //
        //  Methods to populate the edge-vertex relation of the child Level:
        //      - child edges originate from parent faces and edges
        //
        public override void populateEdgeVertexRelation()
        {
            _child._edgeVertIndices.Resize(_child.getNumEdges() * 2);

            populateEdgeVerticesFromParentFaces();
            populateEdgeVerticesFromParentEdges();
        }

        public void populateEdgeVerticesFromParentFaces()
        {
            for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
            {
                ConstIndexArray pFaceEdges = _parent.getFaceEdges(pFace),
                                pFaceChildEdges = getFaceChildEdges(pFace);

                System.Diagnostics.Debug.Assert(pFaceEdges.size() == 3);
                System.Diagnostics.Debug.Assert(pFaceChildEdges.size() == 3);

                int[] pEdgeChildVerts = new int[3];
                pEdgeChildVerts[0] = _edgeChildVertIndex[pFaceEdges[0]];
                pEdgeChildVerts[1] = _edgeChildVertIndex[pFaceEdges[1]];
                pEdgeChildVerts[2] = _edgeChildVertIndex[pFaceEdges[2]];

                if (Types.IndexIsValid(pFaceChildEdges[0]))
                {
                    IndexArray cEdgeVerts = _child.getEdgeVertices(pFaceChildEdges[0]);

                    cEdgeVerts[0] = pEdgeChildVerts[0];
                    cEdgeVerts[1] = pEdgeChildVerts[2];
                }
                if (Types.IndexIsValid(pFaceChildEdges[1]))
                {
                    IndexArray cEdgeVerts = _child.getEdgeVertices(pFaceChildEdges[1]);

                    cEdgeVerts[0] = pEdgeChildVerts[1];
                    cEdgeVerts[1] = pEdgeChildVerts[0];
                }
                if (Types.IndexIsValid(pFaceChildEdges[2]))
                {
                    IndexArray cEdgeVerts = _child.getEdgeVertices(pFaceChildEdges[2]);

                    cEdgeVerts[0] = pEdgeChildVerts[2];
                    cEdgeVerts[1] = pEdgeChildVerts[1];
                }
            }
        }

        public void populateEdgeVerticesFromParentEdges()
        {
            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                ConstIndexArray pEdgeVerts = _parent.getEdgeVertices(pEdge),
                                pEdgeChildEdges = getEdgeChildEdges(pEdge);

                if (Types.IndexIsValid(pEdgeChildEdges[0]))
                {
                    IndexArray cEdgeVerts = _child.getEdgeVertices(pEdgeChildEdges[0]);

                    cEdgeVerts[0] = _edgeChildVertIndex[pEdge];
                    cEdgeVerts[1] = _vertChildVertIndex[pEdgeVerts[0]];
                }
                if (Types.IndexIsValid(pEdgeChildEdges[1]))
                {
                    IndexArray cEdgeVerts = _child.getEdgeVertices(pEdgeChildEdges[1]);

                    cEdgeVerts[0] = _edgeChildVertIndex[pEdge];
                    cEdgeVerts[1] = _vertChildVertIndex[pEdgeVerts[1]];
                }
            }
        }

        //
        //  Methods to populate the edge-face relation of the child Level:
        //      - child edges originate from parent faces and edges
        //      - sparse refinement poses challenges with allocation here
        //          - we need to update the counts/offsets as we populate
        //
        public override void populateEdgeFaceRelation()
        {
            //
            //  This is essentially the same as the quad-split version except for the
            //  sizing estimates:
            //      - every child-edge within a face will have 2 incident faces
            //      - every child-edge from a edge may have N incident faces
            //          - use the parents edge-face count for this
            //
            int childEdgeFaceIndexSizeEstimate = (int)_faceChildEdgeIndices.Count * 2 +
                                                 (int)_parent._edgeFaceIndices.Count * 2;

            _child._edgeFaceCountsAndOffsets.Resize(_child.getNumEdges() * 2);
            _child._edgeFaceIndices.Resize(childEdgeFaceIndexSizeEstimate);
            _child._edgeFaceLocalIndices.Resize(childEdgeFaceIndexSizeEstimate);

            // Update _maxEdgeFaces from the parent level before calling the
            // populateEdgeFacesFromParent methods below, as these may further
            // update _maxEdgeFaces.
            _child._maxEdgeFaces = _parent._maxEdgeFaces;

            populateEdgeFacesFromParentFaces();
            populateEdgeFacesFromParentEdges();

            //  Revise the over-allocated estimate based on what is used (as indicated in the
            //  count/offset for the last vertex) and trim the index vector accordingly:
            childEdgeFaceIndexSizeEstimate = _child.getNumEdgeFaces(_child.getNumEdges() - 1) +
                                             _child.getOffsetOfEdgeFaces(_child.getNumEdges() - 1);
            _child._edgeFaceIndices.Resize(childEdgeFaceIndexSizeEstimate);
            _child._edgeFaceLocalIndices.Resize(childEdgeFaceIndexSizeEstimate);
        }

        public void populateEdgeFacesFromParentFaces()
        {
            for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
            {
                ConstIndexArray pFaceChildFaces = getFaceChildFaces(pFace),
                                pFaceChildEdges = getFaceChildEdges(pFace);

                System.Diagnostics.Debug.Assert(pFaceChildFaces.size() == 4);
                System.Diagnostics.Debug.Assert(pFaceChildEdges.size() == 3);

                //  Every child-edge of a face potentially shares the middle child face:
                int cFaceMiddle = pFaceChildFaces[3];
                bool isFaceMiddleValid = Types.IndexIsValid(cFaceMiddle);

                for (int j = 0; j < pFaceChildEdges.size(); ++j)
                {
                    int cEdge = pFaceChildEdges[j];
                    if (Types.IndexIsValid(cEdge))
                    {
                        //  Reserve enough edge-faces, populate and trim as needed:
                        _child.resizeEdgeFaces(cEdge, 2);

                        IndexArray cEdgeFaces = _child.getEdgeFaces(cEdge);
                        LocalIndexArray cEdgeInFace = _child.getEdgeFaceLocalIndices(cEdge);

                        int cEdgeFaceCount = 0;
                        if (Types.IndexIsValid(pFaceChildFaces[j]))
                        {
                            cEdgeFaces[cEdgeFaceCount] = pFaceChildFaces[j];
                            cEdgeInFace[cEdgeFaceCount] = (ushort)((j + 1) % 3);
                            cEdgeFaceCount++;
                        }
                        if (isFaceMiddleValid)
                        {
                            cEdgeFaces[cEdgeFaceCount] = cFaceMiddle;
                            cEdgeInFace[cEdgeFaceCount] = (ushort)((j + 1) % 3);
                            cEdgeFaceCount++;
                        }
                        _child.trimEdgeFaces(cEdge, cEdgeFaceCount);
                    }
                }
            }
        }

        public void populateEdgeFacesFromParentEdges()
        {
            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                ConstIndexArray pEdgeChildEdges = getEdgeChildEdges(pEdge);
                if (!Types.IndexIsValid(pEdgeChildEdges[0]) && !Types.IndexIsValid(pEdgeChildEdges[1]))
                {
                    continue;
                }

                ConstIndexArray pEdgeFaces = _parent.getEdgeFaces(pEdge);
                ConstLocalIndexArray pEdgeInFace = _parent.getEdgeFaceLocalIndices(pEdge);
                ConstIndexArray pEdgeVerts = _parent.getEdgeVertices(pEdge);

                for (int j = 0; j < 2; ++j)
                {
                    int cEdge = pEdgeChildEdges[j];
                    if (!Types.IndexIsValid(cEdge))
                    {
                        continue;
                    }

                    //
                    //  Reserve enough edge-faces, populate and trim as needed:
                    //
                    _child.resizeEdgeFaces(cEdge, pEdgeFaces.size());

                    IndexArray cEdgeFaces = _child.getEdgeFaces(cEdge);
                    LocalIndexArray cEdgeInFace = _child.getEdgeFaceLocalIndices(cEdge);

                    //
                    //  Each parent face may contribute an incident child face:
                    //
                    //      For each incident face and local-index, we immediately know
                    //  the two child faces that are associated with the two child edges.
                    //  We just need to identify how to pair them based on edge direction.
                    //
                    //      Note also here, that we could identify the pairs of child faces
                    //  once for the parent before dealing with each child edge (we do the
                    //  "find edge in face search" twice here as a result).  We will
                    //  generally have 2 or 1 incident face to the parent edge so we
                    //  can put the child-pairs on the stack.
                    //
                    //      Here's a more promising alternative -- instead of iterating
                    //  through the child edges to "pull" data from the parent, iterate
                    //  through the parent edges' faces and apply valid child faces to
                    //  the appropriate child edge.  We should be able to use end-verts
                    //  of the parent edge to get the corresponding child face for each,
                    //  but we can't avoid a vert-in-face search and a subsequent parity
                    //  test of the end-vert.
                    //
                    int cEdgeFaceCount = 0;

                    for (int i = 0; i < pEdgeFaces.size(); ++i)
                    {
                        int pFace = pEdgeFaces[i];
                        int edgeInFace = pEdgeInFace[i];

                        ConstIndexArray pFaceVerts = _parent.getFaceVertices(pFace),
                                        pFaceChildren = getFaceChildFaces(pFace);

                        //  Inspect either this child of the face or the next -- be careful
                        //  to consider degenerate edge when orienting here:
                        int childOfEdge = (pEdgeVerts[0] == pEdgeVerts[1]) ? j :
                                          (pFaceVerts[edgeInFace] != pEdgeVerts[j] ? 1 : 0);

                        int childInFace = edgeInFace + childOfEdge;
                        if (childInFace == pFaceVerts.size())
                        {
                            childInFace = 0;
                        }

                        if (Types.IndexIsValid(pFaceChildren[childInFace]))
                        {
                            cEdgeFaces[cEdgeFaceCount] = pFaceChildren[childInFace];
                            cEdgeInFace[cEdgeFaceCount] = (ushort)edgeInFace;
                            cEdgeFaceCount++;
                        }
                    }
                    _child.trimEdgeFaces(cEdge, cEdgeFaceCount);
                }
            }
        }

        //
        //  Methods to populate the vertex-face relation of the child Level:
        //      - child vertices originate from parent faces, edges and vertices
        //      - sparse refinement poses challenges with allocation here:
        //          - we need to update the counts/offsets as we populate
        //          - note this imposes ordering constraints and inhibits concurrency
        //
        public override void populateVertexFaceRelation()
        {
            //
            //  Unlike quad-splitting, we don't have to consider vertices originating from
            //  faces.  We also have to consider 3 faces for every incident face for vertices
            //  originating from edges.
            //
            int childVertFaceIndexSizeEstimate = (int)_parent._edgeFaceIndices.Count * 3
                                               + (int)_parent._vertFaceIndices.Count;

            _child._vertFaceCountsAndOffsets.Resize(_child.getNumVertices() * 2);
            _child._vertFaceIndices.Resize(childVertFaceIndexSizeEstimate);
            _child._vertFaceLocalIndices.Resize(childVertFaceIndexSizeEstimate);

            //  Remember -- no vertices-from-faces to consider here (until N-gon support)
            if (getFirstChildVertexFromVertices() == 0)
            {
                populateVertexFacesFromParentVertices();
                populateVertexFacesFromParentEdges();
            }
            else
            {
                populateVertexFacesFromParentEdges();
                populateVertexFacesFromParentVertices();
            }

            //  Revise the over-allocated estimate based on what is used (as indicated in the
            //  count/offset for the last vertex) and trim the index vectors accordingly:
            childVertFaceIndexSizeEstimate = _child.getNumVertexFaces(_child.getNumVertices() - 1) +
                                             _child.getOffsetOfVertexFaces(_child.getNumVertices() - 1);
            _child._vertFaceIndices.Resize(childVertFaceIndexSizeEstimate);
            _child._vertFaceLocalIndices.Resize(childVertFaceIndexSizeEstimate);
        }

        public void populateVertexFacesFromParentEdges()
        {
            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                int cVert = _edgeChildVertIndex[pEdge];
                if (!Types.IndexIsValid(cVert))
                {
                    continue;
                }

                ConstIndexArray pEdgeFaces = _parent.getEdgeFaces(pEdge);
                ConstLocalIndexArray pEdgeInFace = _parent.getEdgeFaceLocalIndices(pEdge);

                //
                //  Reserve enough vert-faces, populate and trim to the actual size:
                //
                _child.resizeVertexFaces(cVert, 2 * pEdgeFaces.size());

                IndexArray cVertFaces = _child.getVertexFaces(cVert);
                LocalIndexArray cVertInFace = _child.getVertexFaceLocalIndices(cVert);

                int cVertFaceCount = 0;
                for (int i = 0; i < pEdgeFaces.size(); ++i)
                {
                    int pFace = pEdgeFaces[i];
                    int edgeInFace = pEdgeInFace[i];

                    //
                    //  Identify the corresponding three child faces for this parent face and
                    //  their orientation wrt the child vertex to which they are incident --
                    //  since we have the desired ordering of child faces from the parent face,
                    //  we don't care about the orientation of the parent edge.
                    //
                    ushort leadingFace = (ushort)((edgeInFace + 1) % 3);
                    ushort middleFace = (ushort)3;
                    ushort trailingFace = (ushort)edgeInFace;

                    ushort leadingLocalIndex = (ushort)edgeInFace;
                    ushort middleLocalIndex = (ushort)((edgeInFace + 2) % 3);
                    ushort trailingLocalIndex = (ushort)((edgeInFace + 1) % 3);

                    //
                    //  Now simply assign those of the three child faces that are valid:
                    //
                    ConstIndexArray pFaceChildFaces = getFaceChildFaces(pFace);
                    System.Diagnostics.Debug.Assert(pFaceChildFaces.size() == 4);

                    int cFace = pFaceChildFaces[leadingFace];
                    if (Types.IndexIsValid(cFace))
                    {
                        cVertFaces[cVertFaceCount] = cFace;
                        cVertInFace[cVertFaceCount] = leadingLocalIndex;
                        cVertFaceCount++;
                    }

                    cFace = pFaceChildFaces[middleFace];
                    if (Types.IndexIsValid(cFace))
                    {
                        cVertFaces[cVertFaceCount] = cFace;
                        cVertInFace[cVertFaceCount] = middleLocalIndex;
                        cVertFaceCount++;
                    }

                    cFace = pFaceChildFaces[trailingFace];
                    if (Types.IndexIsValid(cFace))
                    {
                        cVertFaces[cVertFaceCount] = cFace;
                        cVertInFace[cVertFaceCount] = trailingLocalIndex;
                        cVertFaceCount++;
                    }
                }
                _child.trimVertexFaces(cVert, cVertFaceCount);
            }
        }

        public void populateVertexFacesFromParentVertices()
        {
            for (int pVert = 0; pVert < _parent.getNumVertices(); ++pVert)
            {
                int cVert = _vertChildVertIndex[pVert];
                if (!Types.IndexIsValid(cVert))
                {
                    continue;
                }

                //
                //  Inspect the parent vert's faces:
                //
                ConstIndexArray pVertFaces = _parent.getVertexFaces(pVert);
                ConstLocalIndexArray pVertInFace = _parent.getVertexFaceLocalIndices(pVert);

                //
                //  Reserve enough vert-faces, populate and trim to the actual size:
                //
                _child.resizeVertexFaces(cVert, pVertFaces.size());

                IndexArray cVertFaces = _child.getVertexFaces(cVert);
                LocalIndexArray cVertInFace = _child.getVertexFaceLocalIndices(cVert);

                int cVertFaceCount = 0;
                for (int i = 0; i < pVertFaces.size(); ++i)
                {
                    int pFace = pVertFaces[i];
                    ushort pFaceChild = pVertInFace[i];

                    int cFace = getFaceChildFaces(pFace)[pFaceChild];
                    if (Types.IndexIsValid(cFace))
                    {
                        cVertFaces[cVertFaceCount] = cFace;
                        cVertInFace[cVertFaceCount] = pFaceChild;
                        cVertFaceCount++;
                    }
                }
                _child.trimVertexFaces(cVert, cVertFaceCount);
            }
        }

        //
        //  Methods to populate the vertex-edge relation of the child Level:
        //      - child vertices originate from parent faces, edges and vertices
        //      - sparse refinement poses challenges with allocation here:
        //          - we need to update the counts/offsets as we populate
        //          - note this imposes ordering constraints and inhibits concurrency
        //
        public override void populateVertexEdgeRelation()
        {
            //
            //  Notes on allocating/initializing the vertex-edge counts/offsets vector:
            //
            //  Be aware of scheme-specific decisions here, e.g.:
            //      - no verts from parent faces for Loop
            //      - more interior edges and faces for verts from parent edges for Loop
            //      - no guaranteed "neighborhood" around Bilinear verts from verts
            //
            //  If uniform subdivision, vert-edge count will be:
            //      - 2 + 2*N faces incident parent edge for verts from parent edges
            //      - same as parent vert for verts from parent verts
            //  If sparse subdivision, vert-edge count will be:
            //      - non-trivial function of child faces in parent face
            //          - 1 child face will always result in 2 child edges
            //          * 2 child faces can mean 3 or 4 child edges
            //          - 3 child faces will always result in 4 child edges
            //      - 1 or 2 + N faces incident parent edge for verts from parent edges
            //          - where the 1 or 2 is number of child edges of parent edge
            //          - any end vertex will require all N child faces (catmark)
            //      - same as parent vert for verts from parent verts (catmark)
            //
            int childVertEdgeIndexSizeEstimate = (int)_parent._edgeFaceIndices.Count * 2 + _parent.getNumEdges() * 2
                                               + (int)_parent._vertEdgeIndices.Count;

            _child._vertEdgeCountsAndOffsets.Resize(_child.getNumVertices() * 2);
            _child._vertEdgeIndices.Resize(childVertEdgeIndexSizeEstimate);
            _child._vertEdgeLocalIndices.Resize(childVertEdgeIndexSizeEstimate);

            if (getFirstChildVertexFromVertices() == 0)
            {
                populateVertexEdgesFromParentVertices();
                populateVertexEdgesFromParentEdges();
            }
            else
            {
                populateVertexEdgesFromParentEdges();
                populateVertexEdgesFromParentVertices();
            }

            //  Revise the over-allocated estimate based on what is used (as indicated in the
            //  count/offset for the last vertex) and trim the index vectors accordingly:
            childVertEdgeIndexSizeEstimate = _child.getNumVertexEdges(_child.getNumVertices() - 1) +
                                             _child.getOffsetOfVertexEdges(_child.getNumVertices() - 1);
            _child._vertEdgeIndices.Resize(childVertEdgeIndexSizeEstimate);
            _child._vertEdgeLocalIndices.Resize(childVertEdgeIndexSizeEstimate);
        }

        public void populateVertexEdgesFromParentEdges()
        {
            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                int cVert = _edgeChildVertIndex[pEdge];
                if (!Types.IndexIsValid(cVert))
                {
                    continue;
                }

                //
                //  First inspect the parent edge -- its parent faces then its child edges:
                //
                ConstIndexArray pEdgeFaces = _parent.getEdgeFaces(pEdge);
                ConstLocalIndexArray pEdgeInFace = _parent.getEdgeFaceLocalIndices(pEdge);

                ConstIndexArray pEdgeVerts = _parent.getEdgeVertices(pEdge),
                                pEdgeChildEdges = getEdgeChildEdges(pEdge);

                //
                //  Reserve enough vert-edges, populate and trim to the actual size:
                //
                _child.resizeVertexEdges(cVert, pEdgeFaces.size() + 2);

                IndexArray cVertEdges = _child.getVertexEdges(cVert);
                LocalIndexArray cVertInEdge = _child.getVertexEdgeLocalIndices(cVert);

                //
                //  We need to order the incident edges around the vertex appropriately:
                //      - one child edge of the parent edge ("leading" in face 0)
                //      - two child edges interior to face 0
                //      - one other child edge of the parent edge ("trailing" in face 0)
                //      - child edges of all remaining faces
                //  Be careful to place the leading/trailing child edges of the parent edge
                //  correctly -- edges are not directed their orientation may vary.  The
                //  interior child edges are appropriately oriented wrt their parent face.
                //
                //  Also need to consider no faces at all, in which case we just want the
                //  child edges of the parent edge.
                //
                int cVertEdgeCount = 0;

                //  We only care about edge reversal in the first iteration -- in which
                //  the child edges of the parent edges are assigned.  Other iterations
                //  only assign the child edges from the incident parent face:
                bool pEdgeReversed = false;
                int cEdgeOfEdge0 = Types.INDEX_INVALID,
                    cEdgeOfEdge1 = Types.INDEX_INVALID;

                for (int i = 0; i < pEdgeFaces.size(); ++i)
                {
                    int pFace = pEdgeFaces[i];
                    int edgeInFace = pEdgeInFace[i];

                    ConstIndexArray pFaceChildEdges = getFaceChildEdges(pFace);

                    //  Test the orientation of a non-degenerate edge in the first face:
                    if (i == 0)
                    {
                        if (pEdgeVerts[0] != pEdgeVerts[1])
                        {
                            pEdgeReversed = (_parent.getFaceVertices(pFace)[edgeInFace] != pEdgeVerts[0]);
                        }
                        cEdgeOfEdge0 = pEdgeChildEdges[!pEdgeReversed ? 1 : 0];
                        cEdgeOfEdge1 = pEdgeChildEdges[pEdgeReversed ? 1 : 0];
                    }

                    //
                    //  Identify the two interior and incident child edges within the face --
                    //  bracketed by the child edges of the parent edge when dealing with the
                    //  first face:
                    //
                    int cEdgeOfFace0 = pFaceChildEdges[(edgeInFace + 1) % 3];
                    int cEdgeOfFace1 = pFaceChildEdges[edgeInFace];

                    if ((i == 0) && Types.IndexIsValid(cEdgeOfEdge0))
                    {
                        cVertEdges[cVertEdgeCount] = cEdgeOfEdge0;
                        cVertInEdge[cVertEdgeCount] = 0;
                        cVertEdgeCount++;
                    }
                    if (Types.IndexIsValid(cEdgeOfFace0))
                    {
                        cVertEdges[cVertEdgeCount] = cEdgeOfFace0;
                        cVertInEdge[cVertEdgeCount] = 1;
                        cVertEdgeCount++;
                    }
                    if (Types.IndexIsValid(cEdgeOfFace1))
                    {
                        cVertEdges[cVertEdgeCount] = cEdgeOfFace1;
                        cVertInEdge[cVertEdgeCount] = 0;
                        cVertEdgeCount++;
                    }
                    if ((i == 0) && Types.IndexIsValid(cEdgeOfEdge1))
                    {
                        cVertEdges[cVertEdgeCount] = cEdgeOfEdge1;
                        cVertInEdge[cVertEdgeCount] = 0;
                        cVertEdgeCount++;
                    }
                }
                _child.trimVertexEdges(cVert, cVertEdgeCount);
            }
        }

        public void populateVertexEdgesFromParentVertices()
        {
            for (int pVert = 0; pVert < _parent.getNumVertices(); ++pVert)
            {
                int cVert = _vertChildVertIndex[pVert];
                if (!Types.IndexIsValid(cVert))
                {
                    continue;
                }

                //
                //  Inspect the parent vert's edges first:
                //
                ConstIndexArray pVertEdges = _parent.getVertexEdges(pVert);
                ConstLocalIndexArray pVertInEdge = _parent.getVertexEdgeLocalIndices(pVert);

                //
                //  Reserve enough vert-edges, populate and trim to the actual size:
                //
                _child.resizeVertexEdges(cVert, pVertEdges.size());

                IndexArray cVertEdges = _child.getVertexEdges(cVert);
                LocalIndexArray cVertInEdge = _child.getVertexEdgeLocalIndices(cVert);

                int cVertEdgeCount = 0;
                for (int i = 0; i < pVertEdges.size(); ++i)
                {
                    int cEdge = getEdgeChildEdges(pVertEdges[i])[pVertInEdge[i]];
                    if (Types.IndexIsValid(cEdge))
                    {
                        cVertEdges[cVertEdgeCount] = cEdge;
                        cVertInEdge[cVertEdgeCount] = 1;
                        cVertEdgeCount++;
                    }
                }
                _child.trimVertexEdges(cVert, cVertEdgeCount);
            }
        }

        //
        //  Methods to populate child-component indices for sparse selection:
        //
        //  Need to find a better place for these anon helper methods now that they are required
        //  both in the base class and the two subclasses for quad- and tri-splitting...
        //
        private const int IndexSparseMaskNeighboring = (1 << 0);
        private const int IndexSparseMaskSelected = (1 << 1);

        private static void markSparseIndexNeighbor(ref int index)
        {
            index = IndexSparseMaskNeighboring;
        }

        private static void markSparseIndexSelected(ref int index)
        {
            index = IndexSparseMaskSelected;
        }

        public override void markSparseFaceChildren()
        {
            System.Diagnostics.Debug.Assert(_parentFaceTag.Count > 0);

            //
            //  For each parent face:
            //      All boundary edges will be adequately marked as a result of the pass over the
            //  edges above and boundary vertices marked by selection.  So all that remains is to
            //  identify the child faces and interior child edges for a face requiring neighboring
            //  child faces.
            //      For each corner vertex selected, we need to mark the corresponding child face,
            //  the two interior child edges and shared child vertex in the middle.
            //
            for (int pFace = 0; pFace < parent().getNumFaces(); ++pFace)
            {
                //
                //  Mark all descending child components of a selected face.  Otherwise inspect
                //  its incident vertices to see if anything neighboring has been selected --
                //  requiring partial refinement of this face.
                //
                //  Remember that a selected face cannot be transitional, and that only a
                //  transitional face will be partially refined.
                //
                IndexArray fChildFaces = getFaceChildFaces(pFace);
                IndexArray fChildEdges = getFaceChildEdges(pFace);

                System.Diagnostics.Debug.Assert(fChildFaces.size() == 4);
                System.Diagnostics.Debug.Assert(fChildEdges.size() == 3);

                ConstIndexArray fVerts = parent().getFaceVertices(pFace);

                ref SparseTag pFaceTag = ref _parentFaceTag[pFace];

                if (pFaceTag._selected)
                {
                    markSparseIndexSelected(ref fChildFaces[0]);
                    markSparseIndexSelected(ref fChildFaces[1]);
                    markSparseIndexSelected(ref fChildFaces[2]);
                    markSparseIndexSelected(ref fChildFaces[3]);

                    markSparseIndexSelected(ref fChildEdges[0]);
                    markSparseIndexSelected(ref fChildEdges[1]);
                    markSparseIndexSelected(ref fChildEdges[2]);

                    pFaceTag._transitional = 0;
                }
                else
                {
                    int marked = (_parentVertexTag[fVerts[0]]._selected ? 1 : 0)
                               + (_parentVertexTag[fVerts[1]]._selected ? 1 : 0)
                               + (_parentVertexTag[fVerts[2]]._selected ? 1 : 0);

                    if (marked != 0)
                    {
                        //
                        //  If marked, see if we have any transitional edges, in which case we
                        //  need to include the middle face:
                        //
                        ConstIndexArray fEdges = parent().getFaceEdges(pFace);

                        pFaceTag._transitional = (byte)
                               ((_parentEdgeTag[fEdges[0]]._transitional << 0) |
                                (_parentEdgeTag[fEdges[1]]._transitional << 1) |
                                (_parentEdgeTag[fEdges[2]]._transitional << 2));

                        //  Now mark the child faces and their associated edges:
                        //
                        if (pFaceTag._transitional != 0)
                        {
                            markSparseIndexNeighbor(ref fChildFaces[3]);

                            markSparseIndexNeighbor(ref fChildEdges[0]);
                            markSparseIndexNeighbor(ref fChildEdges[1]);
                            markSparseIndexNeighbor(ref fChildEdges[2]);
                        }
                        if (_parentVertexTag[fVerts[0]]._selected)
                        {
                            markSparseIndexNeighbor(ref fChildFaces[0]);
                            markSparseIndexNeighbor(ref fChildEdges[0]);
                        }
                        if (_parentVertexTag[fVerts[1]]._selected)
                        {
                            markSparseIndexNeighbor(ref fChildFaces[1]);
                            markSparseIndexNeighbor(ref fChildEdges[1]);
                        }
                        if (_parentVertexTag[fVerts[2]]._selected)
                        {
                            markSparseIndexNeighbor(ref fChildFaces[2]);
                            markSparseIndexNeighbor(ref fChildEdges[2]);
                        }
                    }
                }
            }
        }

        //
        //  Unlike the quad-split, which can share some vectors with the parent Level since
        //  child components correspond to face-vertices, the tri-split must define its
        //  own local vectors to identify the children for each parent component -- to
        //  be referenced within the base class for more immediate/inline access:
        //
        private IndexVector _localFaceChildFaceCountsAndOffsets = new IndexVector();
    }
}
