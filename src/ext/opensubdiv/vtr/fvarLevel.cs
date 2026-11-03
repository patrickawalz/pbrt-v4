//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/fvarLevel.h and vtr/fvarLevel.cpp
//
//  See the notes at the top of level.cs regarding bit-field members (TagBit), const/non-const accessor
//  overloads (single method returning the mutable form) and pointer arguments (arrays).
using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;

using IndexArray = OpenSubdiv.Vtr.Array<int>;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using LocalIndexArray = OpenSubdiv.Vtr.Array<ushort>;
using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;
using IndexVector = OpenSubdiv.Vtr.StdVector<int>;

namespace OpenSubdiv.Vtr.Internal
{
    //
    //  FVarLevel:
    //      Simple container of face-varying topology, associated with a particular
    //  level.  It is typically constructed and initialized similarly to levels -- the
    //  base level in a Factory and subsequent levels by refinement.
    //
    public class FVarLevel
    {
        //
        //  Tags for edges and values:
        //
        public struct ETag
        {
            public byte _bits;

            public void clear()
            {
                _bits = 0;
            }

            private TagBit getBit(int shift)
            {
                return new TagBit((uint)(_bits >> shift));
            }

            private void setBit(int shift, TagBit value)
            {
                _bits = (byte)((_bits & ~(1 << shift)) | (value.Value << shift));
            }

            public TagBit _mismatch { get { return getBit(0); } set { setBit(0, value); } }  // local FVar topology does not match
            public TagBit _disctsV0 { get { return getBit(1); } set { setBit(1, value); } }  // discontinuous at vertex 0
            public TagBit _disctsV1 { get { return getBit(2); } set { setBit(2, value); } }  // discontinuous at vertex 1
            public TagBit _linear   { get { return getBit(3); } set { setBit(3, value); } }  // linear boundary constraints

            public Level.ETag combineWithLevelETag(Level.ETag levelTag)
            {
                if (this._mismatch)
                {
                    levelTag._boundary = true;
                    levelTag._infSharp = true;
                }
                return levelTag;
            }
        }

        public struct ValueTag
        {
            public byte _bits;

            public void clear()
            {
                _bits = 0;
            }

            public bool isMismatch() { return _mismatch; }
            public bool isCrease() { return _crease; }
            public bool isCorner() { return !_crease; }
            public bool isSemiSharp() { return _semiSharp; }
            public bool isInfSharp() { return !_semiSharp && !_crease; }
            public bool isDepSharp() { return _depSharp; }
            public bool hasCreaseEnds() { return _crease || _semiSharp; }

            public bool hasInfSharpEdges() { return _infSharpEdges; }
            public bool hasInfIrregularity() { return _infIrregular; }

            private TagBit getBit(int shift)
            {
                return new TagBit((uint)(_bits >> shift));
            }

            private void setBit(int shift, TagBit value)
            {
                _bits = (byte)((_bits & ~(1 << shift)) | (value.Value << shift));
            }

            public TagBit _mismatch    { get { return getBit(0); } set { setBit(0, value); } }  // local FVar topology does not match
            public TagBit _xordinary   { get { return getBit(1); } set { setBit(1, value); } }  // local FVar topology is extra-ordinary
            public TagBit _nonManifold { get { return getBit(2); } set { setBit(2, value); } }  // local FVar topology is non-manifold
            public TagBit _crease      { get { return getBit(3); } set { setBit(3, value); } }  // value is a crease, otherwise a corner
            public TagBit _semiSharp   { get { return getBit(4); } set { setBit(4, value); } }  // value is a corner decaying to crease
            public TagBit _depSharp    { get { return getBit(5); } set { setBit(5, value); } }  // value is a corner by dependency on another

            public TagBit _infSharpEdges { get { return getBit(6); } set { setBit(6, value); } }  // value is a corner by inf-sharp features
            public TagBit _infIrregular  { get { return getBit(7); } set { setBit(7, value); } }  // value span includes inf-sharp irregularity

            public Level.VTag combineWithLevelVTag(Level.VTag levelTag)
            {
                if (this._mismatch)
                {
                    if (this.isCorner())
                    {
                        levelTag._rule = (Level.VTag.VTagSize)Crease.Rule.RULE_CORNER;
                    }
                    else
                    {
                        levelTag._rule = (Level.VTag.VTagSize)Crease.Rule.RULE_CREASE;
                    }
                    if (this.isCrease() || this.isSemiSharp())
                    {
                        levelTag._infSharp = false;
                        levelTag._infSharpCrease = true;
                        levelTag._corner = false;
                    }
                    else
                    {
                        levelTag._infSharp = true;
                        levelTag._infSharpCrease = false;
                        levelTag._corner = !this._infIrregular && !this._infSharpEdges;
                    }
                    levelTag._infSharpEdges = true;
                    levelTag._infIrregular = this._infIrregular;

                    levelTag._boundary = true;
                    levelTag._xordinary = this._xordinary;

                    levelTag._nonManifold |= this._nonManifold;
                }
                return levelTag;
            }

            public ValueTag(byte bits)
            {
                _bits = bits;
            }

            public byte getBits()
            {
                return _bits;
            }
        }

        //  typedef Vtr::ConstArray<ValueTag> ConstValueTagArray;  typedef Vtr::Array<ValueTag> ValueTagArray;
        //  (use ConstArray<FVarLevel.ValueTag> / Array<FVarLevel.ValueTag> directly)

        public struct CreaseEndPair
        {
            public ushort _startFace;
            public ushort _endFace;
        }

        //  typedef Vtr::ConstArray<CreaseEndPair> ConstCreaseEndPairArray;
        //  typedef Vtr::Array<CreaseEndPair> CreaseEndPairArray;

        //  typedef LocalIndex Sibling;  (ushort)
        //  typedef ConstLocalIndexArray ConstSiblingArray;  typedef LocalIndexArray SiblingArray;

        //
        //  Information about the "span" for a face-varying value -- the set of faces
        //  that share face-varying continuous edges around their common vertex.
        //
        //  This is intended for transient internal use only when analyzing the base
        //  level topology.  Information gathered for a single span is translated into
        //  topology tags for the value (ValueTag) which classify the value and persist
        //  in the FVarLevel for later refinement and analysis.  The ValueSpan exists
        //  solely to derive the ValueTag and is not intended (or capable) of capturing
        //  the full topological extent of many spans.
        //
        public struct ValueSpan
        {
            public ushort _size;
            public ushort _start;
            public ushort _disctsEdgeCount;
            public ushort _semiSharpEdgeCount;
            public ushort _infSharpEdgeCount;
        }

        //
        //  Members (public here in place of C++ friend access):
        //
        public Level _level;

        public Options _options;

        public bool _isLinear;
        public bool _hasLinearBoundaries;
        public bool _hasDependentSharpness;
        public int _valueCount;

        public IndexVector _faceVertValues = new IndexVector();

        public StdVector<ETag> _edgeTags = new StdVector<ETag>();

        public StdVector<ushort> _vertSiblingCounts = new StdVector<ushort>();
        public IndexVector _vertSiblingOffsets = new IndexVector();
        public StdVector<ushort> _vertFaceSiblings = new StdVector<ushort>();

        public IndexVector _vertValueIndices = new IndexVector();
        public StdVector<ValueTag> _vertValueTags = new StdVector<ValueTag>();
        public StdVector<CreaseEndPair> _vertValueCreaseEnds = new StdVector<CreaseEndPair>();

        //
        //  Simple (for now) constructor:
        //
        public FVarLevel(Level level)
        {
            _level = level;
            _isLinear = false;
            _hasLinearBoundaries = false;
            _hasDependentSharpness = false;
            _valueCount = 0;
        }

        public Level getLevel() { return _level; }

        public int getNumValues() { return _valueCount; }
        public int getNumFaceValuesTotal() { return _faceVertValues.Count; }

        public bool isLinear() { return _isLinear; }
        public bool hasLinearBoundaries() { return _hasLinearBoundaries; }
        public bool hasSmoothBoundaries() { return !_hasLinearBoundaries; }
        public bool hasCreaseEnds() { return hasSmoothBoundaries(); }

        public Options getOptions() { return _options; }

        //  Face-varying values for the face-vertices of a face:
        public IndexArray getFaceValues(int fIndex)
        {
            int vCount = _level.getNumFaceVertices(fIndex);
            int vOffset = _level.getOffsetOfFaceVertices(fIndex);
            return new IndexArray(_faceVertValues.Data, vOffset, vCount);
        }

        public ETag getEdgeTag(int eIndex) { return _edgeTags[eIndex]; }
        public bool edgeTopologyMatches(int eIndex) { return !getEdgeTag(eIndex)._mismatch; }

        //  Number of values associated with a vertex and offset to the first (the vertex-values
        //  of a vertex are stored contiguously, the "siblings" of the vertex):
        public int getNumVertexValues(int v) { return _vertSiblingCounts[v]; }
        public int getVertexValueOffset(int v, int i = 0) { return _vertSiblingOffsets[v] + i; }

        public int getVertexValue(int v, int i = 0) { return _vertValueIndices[getVertexValueOffset(v, i)]; }

        public int findVertexValueIndex(int vertexIndex, int valueIndex)
        {
            if (_level.getDepth() > 0)
            {
                return valueIndex;
            }

            int vvIndex = getVertexValueOffset(vertexIndex);
            while (_vertValueIndices[vvIndex] != valueIndex)
            {
                ++vvIndex;
            }
            return vvIndex;
        }

        public IndexArray getVertexValues(int vIndex)
        {
            int vCount = getNumVertexValues(vIndex);
            int vOffset = getVertexValueOffset(vIndex);
            return new IndexArray(_vertValueIndices.Data, vOffset, vCount);
        }

        public Array<ValueTag> getVertexValueTags(int vIndex)
        {
            int vCount = getNumVertexValues(vIndex);
            int vOffset = getVertexValueOffset(vIndex);
            return new Array<ValueTag>(_vertValueTags.Data, vOffset, vCount);
        }

        public Array<CreaseEndPair> getVertexValueCreaseEnds(int vIndex)
        {
            int vCount = getNumVertexValues(vIndex);
            int vOffset = getVertexValueOffset(vIndex);
            return new Array<CreaseEndPair>(_vertValueCreaseEnds.Data, vOffset, vCount);
        }

        public LocalIndexArray getVertexFaceSiblings(int vIndex)
        {
            int vCount = _level.getNumVertexFaces(vIndex);
            int vOffset = _level.getOffsetOfVertexFaces(vIndex);
            return new LocalIndexArray(_vertFaceSiblings.Data, vOffset, vCount);
        }

        public ValueTag getValueTag(int valueIndex) { return _vertValueTags[valueIndex]; }
        public bool valueTopologyMatches(int valueIndex) { return !getValueTag(valueIndex)._mismatch; }

        public CreaseEndPair getValueCreaseEndPair(int valueIndex) { return _vertValueCreaseEnds[valueIndex]; }

        //
        //  Initialization and sizing methods to allocate space:
        //
        public void setOptions(Options options)
        {
            _options = options;
        }

        public void resizeComponents()
        {
            //  Per-face members:
            _faceVertValues.Resize(_level.getNumFaceVerticesTotal());

            //  Per-edge members:
            ETag edgeTagMatch = new ETag();
            edgeTagMatch.clear();
            _edgeTags.Resize(_level.getNumEdges(), edgeTagMatch);

            //  Per-vertex members:
            _vertSiblingCounts.Resize(_level.getNumVertices());
            _vertSiblingOffsets.Resize(_level.getNumVertices());

            _vertFaceSiblings.Resize(_level.getNumVertexFacesTotal(), 0);
        }

        public void resizeVertexValues(int vertexValueCount)
        {
            _vertValueIndices.Resize(vertexValueCount);

            ValueTag valueTagMatch = new ValueTag();
            valueTagMatch.clear();
            _vertValueTags.Resize(vertexValueCount, valueTagMatch);

            if (hasCreaseEnds())
            {
                _vertValueCreaseEnds.Resize(vertexValueCount);
            }
        }

        public void resizeValues(int valueCount)
        {
            _valueCount = valueCount;
        }

        //
        //  Initialize the component tags once all face-values have been assigned...
        //
        //  Constructing the mapping between vertices and their face-varying values involves:
        //
        //      - iteration through all vertices to mark edge discontinuities and classify
        //      - allocation of vectors mapping vertices to their multiple (sibling) values
        //      - iteration through all vertices and their distinct values to tag topologically
        //
        //  Once values have been identified for each vertex and tagged, refinement propagates
        //  the tags to child values using more simplified logic (child values inherit the
        //  topology of their parent) and no further analysis is required.
        //
        public void completeTopologyFromFaceValues(int regularBoundaryValence)
        {
            //
            //  Assign some members and local variables based on the interpolation options (the
            //  members support queries that are expected later):
            //
            //  Given the growing number of options and behaviors to support, this is likely going
            //  to get another pass.  It may be worth identifying the behavior for each "feature",
            //  i.e. determine smooth or sharp for corners, creases and darts, but the fact that
            //  the rule for one value may be dependent on that of another complicates this.
            //
            Options.VtxBoundaryInterpolation geomOptions = _options.GetVtxBoundaryInterpolation();
            Options.FVarLinearInterpolation fvarOptions = _options.GetFVarLinearInterpolation();

            _isLinear = (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_ALL);

            _hasLinearBoundaries = (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_ALL) ||
                                   (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_BOUNDARIES);

            _hasDependentSharpness = (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_CORNERS_PLUS1) ||
                                     (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_CORNERS_PLUS2);

            bool geomCornersAreSmooth = (geomOptions != Options.VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_AND_CORNER);
            bool fvarCornersAreSharp = (fvarOptions != Options.FVarLinearInterpolation.FVAR_LINEAR_NONE);

            bool makeSmoothCornersSharp = geomCornersAreSmooth && fvarCornersAreSharp;

            bool sharpenBothIfOneCorner = (fvarOptions == Options.FVarLinearInterpolation.FVAR_LINEAR_CORNERS_PLUS2);

            bool sharpenDarts = sharpenBothIfOneCorner || _hasLinearBoundaries;


            //
            //  It's awkward and potentially inefficient to try and accomplish everything in one
            //  pass over the vertices...
            //
            //  Make a first pass through the vertices to identify discts edges and to determine
            //  the number of values-per-vertex for subsequent allocation.  The presence of a
            //  discts edge warrants marking vertices at BOTH ends as having mismatched topology
            //  wrt the vertices (part of why full topological analysis is deferred).
            //
            //  So this first pass will allocate/initialize the overall structure of the topology.
            //  Given N vertices and M (as yet unknown) sibling values, the first pass achieves
            //  the following:
            //
            //      - assigns a local vector indicating which of the N vertices "match"
            //          - requires a single value but must also have no discts incident edges
            //      - determines the number of values associated with each of the N vertices
            //      - assigns an offset to the first value for each of the N vertices
            //      - initializes the vert-face "siblings" for all N vertices
            //  and
            //      - tags any incident edges as discts
            //
            //  The second pass initializes remaining members based on the total number of siblings
            //  M after allocating appropriate vectors dependent on M.
            //
            bool[] vertexMismatch = new bool[_level.getNumVertices()];

            _vertFaceSiblings.Resize(_level.getNumVertexFacesTotal(), 0);

            int maxValence = _level.getMaxValence();

            int[] indexBuffer = new int[maxValence];
            int[] valueBuffer = new int[maxValence];
            ushort[] siblingBuffer = new ushort[maxValence];
            ValueSpan[] spanBuffer = new ValueSpan[maxValence];

            int[] uniqueValues = valueBuffer;
            ushort[] vValueSiblings = siblingBuffer;

            int totalValueCount = 0;
            for (int vIndex = 0; vIndex < _level.getNumVertices(); ++vIndex)
            {
                //
                //  Retrieve the FVar values from each incident face and store locally for
                //  use -- we will identify the index of its corresponding "sibling" as we
                //  inspect them more closely later:
                //
                ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
                ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);

                int[] vValues = indexBuffer;

                for (int i = 0; i < vFaces.size(); ++i)
                {
                    vValues[i] = _faceVertValues[_level.getOffsetOfFaceVertices(vFaces[i]) + vInFace[i]];
                }

                //
                //  Inspect the incident edges of the vertex and tag those whose FVar values are
                //  discts between the two (or more) faces sharing that edge.  When manifold, we
                //  know an edge is discts when two successive fvar-values differ -- so we will
                //  make use of the local buffer of values.  Unfortunately we can't infer anything
                //  about the edges for a non-manifold vertex, so that case will be more complex.
                //
                ConstIndexArray vEdges = _level.getVertexEdges(vIndex);
                ConstLocalIndexArray vInEdge = _level.getVertexEdgeLocalIndices(vIndex);

                bool vIsManifold = !_level.getVertexTag(vIndex)._nonManifold;
                bool vIsBoundary = _level.getVertexTag(vIndex)._boundary;

                if (vIsManifold)
                {
                    //
                    //  We want to use face indices here as we are accessing the fvar-values per
                    //  face.  The indexing range here maps to the interior edges for boundary
                    //  and interior verts:
                    //
                    for (int i = vIsBoundary ? 1 : 0; i < vFaces.size(); ++i)
                    {
                        int vFaceNext = i;
                        int vFacePrev = (i != 0) ? (i - 1) : (vFaces.size() - 1);

                        if (vValues[vFaceNext] != vValues[vFacePrev])
                        {
                            int eIndex = vEdges[i];

                            //  Tag both end vertices as not matching topology:
                            ConstIndexArray eVerts = _level.getEdgeVertices(eIndex);
                            vertexMismatch[eVerts[0]] = true;
                            vertexMismatch[eVerts[1]] = true;

                            //  Tag the corresponding edge as discts:
                            ref ETag eTag = ref _edgeTags[eIndex];

                            eTag._disctsV0 = (eVerts[0] == vIndex);
                            eTag._disctsV1 = (eVerts[1] == vIndex);
                            eTag._mismatch = true;
                            eTag._linear = _hasLinearBoundaries;
                        }
                    }
                }
                else if (vFaces.size() > 0)
                {
                    //
                    //  Unfortunately for non-manifold cases we can't make as much use of the
                    //  retrieved face-values as there is no correlation between the incident
                    //  edge and face lists.  So inspect each edge for continuity between its
                    //  faces in general -- which is awkward (and what we were hoping to avoid
                    //  by doing the overall vertex traversal to begin with):
                    //
                    for (int i = 0; i < vEdges.size(); ++i)
                    {
                        int eIndex = vEdges[i];

                        ConstIndexArray eFaces = _level.getEdgeFaces(eIndex);
                        if (eFaces.size() < 2)
                        {
                            continue;
                        }

                        ConstLocalIndexArray eInFace = _level.getEdgeFaceLocalIndices(eIndex);
                        ConstIndexArray eVerts = _level.getEdgeVertices(eIndex);

                        int vertInEdge = vInEdge[i];
                        bool markEdgeDiscts = false;
                        int valueIndexInFace0 = 0;
                        for (int j = 0; !markEdgeDiscts && (j < eFaces.size()); ++j)
                        {
                            int fIndex = eFaces[j];
                            ConstIndexArray fVerts = _level.getFaceVertices(fIndex);
                            ConstIndexArray fValues = getFaceValues(fIndex);

                            int edgeInFace = eInFace[j];
                            int edgeReversed = (eVerts[0] != fVerts[edgeInFace]) ? 1 : 0;
                            int vertInFace = edgeInFace + ((vertInEdge != edgeReversed) ? 1 : 0);
                            if (vertInFace == fVerts.size())
                            {
                                vertInFace = 0;
                            }

                            if (j == 0)
                            {
                                valueIndexInFace0 = fValues[vertInFace];
                            }
                            else
                            {
                                markEdgeDiscts = (fValues[vertInFace] != valueIndexInFace0);
                            }
                        }
                        if (markEdgeDiscts)
                        {
                            //  Tag both end vertices as not matching topology:
                            vertexMismatch[eVerts[0]] = true;
                            vertexMismatch[eVerts[1]] = true;

                            //  Tag the corresponding edge as discts:
                            ref ETag eTag = ref _edgeTags[eIndex];

                            eTag._disctsV0 = (eVerts[0] == vIndex);
                            eTag._disctsV1 = (eVerts[1] == vIndex);
                            eTag._mismatch = true;
                            eTag._linear = _hasLinearBoundaries;
                        }
                    }
                }

                //
                //  While we've tagged the vertex as having mismatched FVar topology in the presence of
                //  any discts edges, we also need to account for different treatment of vertices along
                //  geometric boundaries if the FVar interpolation rules affect them.  So inspect all
                //  boundary vertices that have not already been tagged.
                //
                if (vIsBoundary && !vertexMismatch[vIndex])
                {
                    if (_hasLinearBoundaries && (vFaces.size() > 0))
                    {
                        vertexMismatch[vIndex] = true;

                        if (vIsManifold)
                        {
                            _edgeTags[vEdges[0]]._linear = true;
                            _edgeTags[vEdges[vEdges.size() - 1]]._linear = true;
                        }
                        else
                        {
                            for (int i = 0; i < vEdges.size(); ++i)
                            {
                                if (_level.getEdgeTag(vEdges[i])._boundary)
                                {
                                    _edgeTags[vEdges[i]]._linear = true;
                                }
                            }
                        }
                    }
                    else if (vFaces.size() == 1)
                    {
                        if (makeSmoothCornersSharp)
                        {
                            vertexMismatch[vIndex] = true;
                        }
                    }
                }

                //
                //  Inspect the set of fvar-values around the vertex to identify the number of
                //  unique values.  While doing so, associate a "sibling index" (over the range
                //  of unique values) with each value around the vertex (this latter need makes
                //  it harder to make simple use of std::sort() and uniq() on the set of values)
                //
                int uniqueValueCount = 1;

                uniqueValues[0] = vValues[0];
                vValueSiblings[0] = 0;

                for (int i = 1; i < vFaces.size(); ++i)
                {
                    if (vValues[i] == vValues[i - 1])
                    {
                        vValueSiblings[i] = vValueSiblings[i - 1];
                    }
                    else
                    {
                        //  Add the "new" value if not already present -- unless found, the
                        //  sibling index will be for the next/new unique value:
                        vValueSiblings[i] = (ushort)uniqueValueCount;

                        if (uniqueValueCount == 1)
                        {
                            uniqueValues[uniqueValueCount++] = vValues[i];
                        }
                        else if ((uniqueValueCount == 2) && (uniqueValues[0] != vValues[i]))
                        {
                            uniqueValues[uniqueValueCount++] = vValues[i];
                        }
                        else
                        {
                            //  std::find(uniqueBegin, uniqueEnd, vValues[i]):
                            int uniqueFound = 0;
                            while ((uniqueFound < uniqueValueCount) && (uniqueValues[uniqueFound] != vValues[i]))
                            {
                                ++uniqueFound;
                            }
                            if (uniqueFound == uniqueValueCount)
                            {
                                uniqueValues[uniqueValueCount++] = vValues[i];
                            }
                            else
                            {
                                vValueSiblings[i] = (ushort)uniqueFound;
                            }
                        }
                    }
                }

                //
                //  Some non-manifold cases can have multiple fvar-values but without any discts
                //  edges that would previously have identified mismatch (e.g. two faces meeting
                //  at a common vertex), so deal with that case now that we've counted values:
                //
                if (!vIsManifold && !vertexMismatch[vIndex])
                {
                    vertexMismatch[vIndex] = (uniqueValueCount > 1);
                }

                //
                //  Update the value count and offset for this vertex and cumulative totals:
                //
                _vertSiblingCounts[vIndex] = (ushort)uniqueValueCount;
                _vertSiblingOffsets[vIndex] = totalValueCount;

                totalValueCount += uniqueValueCount;

                //  Update the vert-face siblings from the local array above:
                if (uniqueValueCount > 1)
                {
                    LocalIndexArray vFaceSiblings = getVertexFaceSiblings(vIndex);
                    for (int i = 0; i < vFaces.size(); ++i)
                    {
                        vFaceSiblings[i] = vValueSiblings[i];
                    }
                }
            }

            //
            //  Now that we know the total number of additional sibling values (M values in addition
            //  to the N vertex values) allocate space to accommodate all N + M vertex values.
            //
            //  Then make the second pass through the vertices to identify the values associated with
            //  each and to inspect and tag local face-varying topology for those that don't match:
            //
            resizeVertexValues(totalValueCount);

            for (int vIndex = 0; vIndex < _level.getNumVertices(); ++vIndex)
            {
                ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
                ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);

                //
                //  First step is to assign the values associated with the faces by retrieving them
                //  from the faces.  If the face-varying topology around this vertex matches the vertex
                //  topology, there is little more to do as other members were bulk-initialized to
                //  match, so we can continue immediately:
                //
                IndexArray vValues = getVertexValues(vIndex);

                if (vFaces.size() > 0)
                {
                    vValues[0] = _faceVertValues[_level.getOffsetOfFaceVertices(vFaces[0]) + vInFace[0]];
                }
                else
                {
                    vValues[0] = 0;
                }
                if (!vertexMismatch[vIndex])
                {
                    continue;
                }
                if (vValues.size() > 1)
                {
                    ConstLocalIndexArray vFaceSiblings = getVertexFaceSiblings(vIndex);

                    for (int i = 1, nextSibling = 1; i < vFaces.size(); ++i)
                    {
                        if (vFaceSiblings[i] == nextSibling)
                        {
                            vValues[nextSibling++] = _faceVertValues[_level.getOffsetOfFaceVertices(vFaces[i]) + vInFace[i]];
                        }
                    }
                }

                //  XXXX (barfowl) -- this pre-emptive sharpening of values will need to be
                //  revisited soon.  This intentionally avoids the overhead of identifying the
                //  local topology of the values along its boundaries -- necessary for smooth
                //  boundary values but not for sharp as far as refining and limiting the
                //  values is concerned.  But ultimately we need more information than just
                //  the sharp tag when it comes to identifying and gathering FVar patches.
                //
                //  Currently values for non-manifold vertices are sharpened, and that may
                //  also need to be revisited.
                //
                //  Until then...
                //
                //  If all values for this vertex are to be designated as sharp, the value tags
                //  have already been initialized for this by default, so we can continue.  On
                //  further inspection there may be other cases where all are determined to be
                //  sharp, but use what information we can now to avoid that inspection:
                //
                //  Regarding sharpness of the vertex itself, its vertex tags reflect the inf-
                //  or semi-sharp nature of the vertex and edges around it, so be careful not
                //  to assume too much from say, the presence of an incident inf-sharp edge.
                //  We can make clear decisions based on the sharpness of the vertex itself.
                //
                Array<ValueTag> vValueTags = getVertexValueTags(vIndex);

                Level.VTag vTag = _level.getVertexTag(vIndex);

                bool allCornersAreSharp = _hasLinearBoundaries || vTag._infSharp || vTag._nonManifold ||
                                          (_hasDependentSharpness && (vValues.size() > 2)) ||
                                          (sharpenDarts && (vValues.size() == 1) && !vTag._boundary);

                //
                //  Values may be a mix of sharp corners and smooth boundaries -- start by
                //  gathering information about the "span" of faces for each value.
                //
                //  Note that the term "span" presumes sequential and continuous, but the
                //  result for a span may include multiple disconnected regions sharing the
                //  common value -- think of a familiar non-manifold "bowtie" vertex in FVar
                //  space.  Such spans are locally non-manifold but are marked as "disjoint"
                //  to avoid overloading "non-manifold" here.
                //
                ValueSpan[] vValueSpans = spanBuffer;
                System.Array.Clear(vValueSpans, 0, vValues.size());

                gatherValueSpans(vIndex, vValueSpans);

                //
                //  Spans are identified as sharp or smooth based on their own local topology,
                //  but the sharpness of one span may be dependent on the sharpness of another
                //  if certain linear-interpolation options were specified.  Mark both as
                //  infinitely sharp where possible (rather than semi-sharp) to avoid
                //  re-assessing this dependency as sharpness is reduced during refinement.
                //
                bool hasDependentValuesToSharpen = false;
                if (!allCornersAreSharp)
                {
                    if (_hasDependentSharpness && (vValues.size() == 2))
                    {
                        //  Detect interior inf-sharp or discts edges:
                        allCornersAreSharp = (vValueSpans[0]._infSharpEdgeCount != 0) || (vValueSpans[1]._infSharpEdgeCount != 0) ||
                                             (vValueSpans[0]._disctsEdgeCount != 0) || (vValueSpans[1]._disctsEdgeCount != 0);

                        //  Detect a sharp corner, making both sharp:
                        if (sharpenBothIfOneCorner)
                        {
                            allCornersAreSharp |= (vValueSpans[0]._size == 1) || (vValueSpans[1]._size == 1);
                        }

                        //  If only one semi-sharp, need to mark the other as dependent on it:
                        hasDependentValuesToSharpen = (vValueSpans[0]._semiSharpEdgeCount > 0) !=
                                                      (vValueSpans[1]._semiSharpEdgeCount > 0);
                    }
                }

                //
                //  Inspect each vertex value to determine if it is a smooth boundary (crease) and tag
                //  it accordingly.  If not semi-sharp, be sure to consider those values sharpened by
                //  the topology of other values.
                //
                for (int i = 0; i < vValues.size(); ++i)
                {
                    ref ValueTag valueTag = ref vValueTags[i];

                    valueTag.clear();
                    valueTag._mismatch = true;

                    ValueSpan vSpan = vValueSpans[i];
                    if (vSpan._disctsEdgeCount != 0)
                    {
                        valueTag._nonManifold = true;
                        continue;
                    }
                    System.Diagnostics.Debug.Assert(vSpan._size != 0);

                    bool isInfSharp = allCornersAreSharp || (vSpan._infSharpEdgeCount != 0) ||
                                      ((vSpan._size == 1) && fvarCornersAreSharp);

                    if (vSpan._size == 1)
                    {
                        valueTag._xordinary = !isInfSharp;
                    }
                    else
                    {
                        valueTag._xordinary = (vSpan._size != regularBoundaryValence);
                    }

                    valueTag._infSharpEdges = (vSpan._infSharpEdgeCount > 0);
                    valueTag._infIrregular = (vSpan._infSharpEdgeCount != 0) ? ((vSpan._size - vSpan._infSharpEdgeCount) > 1)
                                           : (isInfSharp ? (vSpan._size > 1) : (bool)valueTag._xordinary);

                    if (!isInfSharp)
                    {
                        //
                        //  Remember that a semi-sharp value (or one dependent on one) needs to be
                        //  treated as a corner (at least three sharp edges or one sharp vertex)
                        //  until the sharpness has decayed, so don't tag them as creases here.
                        //  But do initialize and maintain the ends of the crease until needed.
                        //
                        if ((vSpan._semiSharpEdgeCount != 0) || vTag._semiSharp)
                        {
                            valueTag._semiSharp = true;
                        }
                        else if (hasDependentValuesToSharpen)
                        {
                            valueTag._semiSharp = true;
                            valueTag._depSharp = true;
                        }
                        else
                        {
                            valueTag._crease = true;
                        }

                        if (hasCreaseEnds())
                        {
                            ref CreaseEndPair valueCrease = ref getVertexValueCreaseEnds(vIndex)[i];

                            valueCrease._startFace = vSpan._start;
                            if ((i == 0) && (vSpan._start != 0))
                            {
                                valueCrease._endFace = (ushort)(vSpan._start + vSpan._size - 1 - vFaces.size());
                            }
                            else
                            {
                                valueCrease._endFace = (ushort)(vSpan._start + vSpan._size - 1);
                            }
                        }
                    }
                }
            }
        }

        //
        //  Values tagged as creases have their two "end values" identified relative to the incident
        //  faces of the vertex for compact storage and quick retrieval.  This method identifies the
        //  values for the two ends of such a crease value:
        //
        public void getVertexCreaseEndValues(int vIndex, int vSibling, int[] endValues)
        {
            ConstArray<CreaseEndPair> vValueCreaseEnds = getVertexValueCreaseEnds(vIndex);

            ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);

            ushort vertFace0 = vValueCreaseEnds[vSibling]._startFace;
            ushort vertFace1 = vValueCreaseEnds[vSibling]._endFace;

            ConstIndexArray face0Values = getFaceValues(vFaces[vertFace0]);
            ConstIndexArray face1Values = getFaceValues(vFaces[vertFace1]);

            int endInFace0 = vInFace[vertFace0];
            int endInFace1 = vInFace[vertFace1];

            endInFace0 = (endInFace0 == (face0Values.size() - 1)) ? 0 : (endInFace0 + 1);
            endInFace1 = ((endInFace1 != 0) ? endInFace1 : face1Values.size()) - 1;

            endValues[0] = face0Values[endInFace0];
            endValues[1] = face1Values[endInFace1];
        }

        //
        //  Debugging aids...
        //
        public bool validate()
        {
            //
            //  Verify that member sizes match sizes for the associated level:
            //
            if ((int)_vertSiblingCounts.Count != _level.getNumVertices())
            {
                Console.Write("Error:  vertex count mismatch\n");
                return false;
            }
            if ((int)_edgeTags.Count != _level.getNumEdges())
            {
                Console.Write("Error:  edge count mismatch\n");
                return false;
            }
            if ((int)_faceVertValues.Count != _level.getNumFaceVerticesTotal())
            {
                Console.Write("Error:  face-value/face-vert count mismatch\n");
                return false;
            }
            if (_level.getDepth() > 0)
            {
                if (_valueCount != (int)_vertValueIndices.Count)
                {
                    Console.Write("Error:  value/vertex-value count mismatch\n");
                    return false;
                }
            }

            //
            //  Verify that face-verts and (locally computed) face-vert siblings yield the
            //  expected face-vert values:
            //
            StdVector<ushort> fvSiblingVector = new StdVector<ushort>();
            buildFaceVertexSiblingsFromVertexFaceSiblings(fvSiblingVector);

            for (int fIndex = 0; fIndex < _level.getNumFaces(); ++fIndex)
            {
                ConstIndexArray fVerts = _level.getFaceVertices(fIndex);
                ConstIndexArray fValues = getFaceValues(fIndex);
                int fSiblingsOffset = _level.getOffsetOfFaceVertices(fIndex);

                for (int fvIndex = 0; fvIndex < fVerts.size(); ++fvIndex)
                {
                    int vIndex = fVerts[fvIndex];

                    int fvValue = fValues[fvIndex];
                    ushort fvSibling = fvSiblingVector[fSiblingsOffset + fvIndex];
                    if (fvSibling >= getNumVertexValues(vIndex))
                    {
                        Console.Write("Error:  invalid sibling {0} for face-vert {1}.{2} = {3}\n", fvSibling, fIndex, fvIndex, vIndex);
                        return false;
                    }

                    int testValue = getVertexValue(vIndex, fvSibling);
                    if (testValue != fvValue)
                    {
                        Console.Write("Error:  unexpected value {0} for sibling {1} of face-vert {2}.{3} = {4} (expecting {5})\n",
                                testValue, fvSibling, fIndex, fvIndex, vIndex, fvValue);
                        return false;
                    }
                }
            }

            //
            //  Verify that the vert-face siblings yield the expected value:
            //
            for (int vIndex = 0; vIndex < _level.getNumVertices(); ++vIndex)
            {
                ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
                ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);
                ConstLocalIndexArray vSiblings = getVertexFaceSiblings(vIndex);

                for (int j = 0; j < vFaces.size(); ++j)
                {
                    ushort vSibling = vSiblings[j];
                    if (vSibling >= getNumVertexValues(vIndex))
                    {
                        Console.Write("Error:  invalid sibling {0} at vert-face {1}.{2}\n", vSibling, vIndex, j);
                        return false;
                    }

                    int fIndex = vFaces[j];
                    int fvIndex = vInFace[j];
                    int fvValue = getFaceValues(fIndex)[fvIndex];

                    int vValue = getVertexValue(vIndex, vSibling);
                    if (vValue != fvValue)
                    {
                        Console.Write("Error:  value mismatch between face-vert {0}.{1} and vert-face {2}.{3} ({4} != {5})\n",
                                fIndex, fvIndex, vIndex, j, fvValue, vValue);
                        return false;
                    }
                }
            }
            return true;
        }

        public void print()
        {
            StdVector<ushort> fvSiblingVector = new StdVector<ushort>();
            buildFaceVertexSiblingsFromVertexFaceSiblings(fvSiblingVector);

            Console.Write("Face-varying data channel:\n");
            Console.Write("  Inventory:\n");
            Console.Write("    vertex count       = {0}\n", _level.getNumVertices());
            Console.Write("    source value count = {0}\n", _valueCount);
            Console.Write("    vertex value count = {0}\n", (int)_vertValueIndices.Count);

            Console.Write("  Face values:\n");
            for (int i = 0; i < _level.getNumFaces(); ++i)
            {
                ConstIndexArray fVerts = _level.getFaceVertices(i);
                ConstIndexArray fValues = getFaceValues(i);
                int fSiblingsOffset = _level.getOffsetOfFaceVertices(i);

                Console.Write("    face{0,4}:  ", i);

                Console.Write("verts =");
                for (int j = 0; j < fVerts.size(); ++j)
                {
                    Console.Write("{0,4}", fVerts[j]);
                }
                Console.Write(",  values =");
                for (int j = 0; j < fValues.size(); ++j)
                {
                    Console.Write("{0,4}", fValues[j]);
                }
                Console.Write(",  siblings =");
                for (int j = 0; j < fVerts.size(); ++j)
                {
                    Console.Write("{0,4}", (int)fvSiblingVector[fSiblingsOffset + j]);
                }
                Console.Write("\n");
            }

            Console.Write("  Vertex values:\n");
            for (int i = 0; i < _level.getNumVertices(); ++i)
            {
                int vCount = getNumVertexValues(i);
                int vOffset = getVertexValueOffset(i);

                Console.Write("    vert{0,4}:  vcount = {1,1}, voffset ={2,4}, ", i, vCount, vOffset);

                ConstIndexArray vValues = getVertexValues(i);

                Console.Write("values =");
                for (int j = 0; j < vValues.size(); ++j)
                {
                    Console.Write("{0,4}", vValues[j]);
                }
                if (vCount > 1)
                {
                    ConstArray<ValueTag> vValueTags = getVertexValueTags(i);

                    Console.Write(", crease =");
                    for (int j = 0; j < vValueTags.size(); ++j)
                    {
                        Console.Write("{0,4}", (int)vValueTags[j]._crease);
                    }
                    Console.Write(", semi-sharp =");
                    for (int j = 0; j < vValueTags.size(); ++j)
                    {
                        Console.Write("{0,2}", (int)vValueTags[j]._semiSharp);
                    }
                }
                Console.Write("\n");
            }

            Console.Write("  Edge discontinuities:\n");
            for (int i = 0; i < _level.getNumEdges(); ++i)
            {
                ETag eTag = getEdgeTag(i);
                if (eTag._mismatch)
                {
                    ConstIndexArray eVerts = _level.getEdgeVertices(i);
                    Console.Write("    edge{0,4}:  verts = [{1,4}{2,4}], discts = [{3},{4}]\n", i, eVerts[0], eVerts[1],
                            (int)eTag._disctsV0, (int)eTag._disctsV1);
                }
            }
        }

        public void initializeFaceValuesFromFaceVertices()
        {
            ConstIndexArray srcFaceVerts = _level.getFaceVertices();

            System.Array.Copy(srcFaceVerts.Data, srcFaceVerts.begin(), _faceVertValues.Data, 0, srcFaceVerts.size());
        }

        public void initializeFaceValuesFromVertexFaceSiblings()
        {
            //
            //  Iterate through all face-values first and initialize them with the first value
            //  associated with each face-vertex.  Then make a second sparse pass through the
            //  vertex-faces to offset those with multiple values.  This turns out to be much
            //  more efficient than a single iteration through the vertex-faces since the first
            //  pass is much more memory coherent.
            //
            ConstIndexArray fvIndices = _level.getFaceVertices();
            for (int i = 0; i < fvIndices.size(); ++i)
            {
                _faceVertValues[i] = getVertexValueOffset(fvIndices[i]);
            }

            //
            //  Now use the vert-face-siblings to populate the face-vert-values:
            //
            for (int vIndex = 0; vIndex < _level.getNumVertices(); ++vIndex)
            {
                if (getNumVertexValues(vIndex) > 1)
                {
                    ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
                    ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);
                    ConstLocalIndexArray vSiblings = getVertexFaceSiblings(vIndex);

                    for (int j = 0; j < vFaces.size(); ++j)
                    {
                        if (vSiblings[j] != 0)
                        {
                            int fvOffset = _level.getOffsetOfFaceVertices(vFaces[j]);

                            _faceVertValues[fvOffset + vInFace[j]] += vSiblings[j];
                        }
                    }
                }
            }
        }

        public void buildFaceVertexSiblingsFromVertexFaceSiblings(StdVector<ushort> fvSiblings)
        {
            fvSiblings.Resize(_level.getNumFaceVerticesTotal());
            System.Array.Clear(fvSiblings.Data, 0, _level.getNumFaceVerticesTotal());

            for (int vIndex = 0; vIndex < _level.getNumVertices(); ++vIndex)
            {
                //  We can skip cases of one sibling as we initialized to 0...
                if (getNumVertexValues(vIndex) > 1)
                {
                    ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
                    ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);
                    ConstLocalIndexArray vSiblings = getVertexFaceSiblings(vIndex);

                    for (int j = 0; j < vFaces.size(); ++j)
                    {
                        if (vSiblings[j] > 0)
                        {
                            fvSiblings[_level.getOffsetOfFaceVertices(vFaces[j]) + vInFace[j]] = vSiblings[j];
                        }
                    }
                }
            }
        }

        //
        //  Higher-level topological queries, i.e. values in a neighborhood:
        //    - given an edge, return values corresponding to its vertices within a given face
        //    - given a vertex, return values corresponding to verts at the ends of its edges
        //
        public void getEdgeFaceValues(int eIndex, int fIncToEdge, int[] valuesPerVert)
        {
            ConstIndexArray eVerts = _level.getEdgeVertices(eIndex);

            if ((getNumVertexValues(eVerts[0]) + getNumVertexValues(eVerts[1])) > 2)
            {
                int eFace = _level.getEdgeFaces(eIndex)[fIncToEdge];
                int eInFace = _level.getEdgeFaceLocalIndices(eIndex)[fIncToEdge];

                ConstIndexArray fValues = getFaceValues(eFace);

                valuesPerVert[0] = fValues[eInFace];
                valuesPerVert[1] = fValues[((eInFace + 1) < fValues.size()) ? (eInFace + 1) : 0];

                //  Given the way these two end-values are used (both weights the same) we really
                //  don't need to ensure the value pair matches the vertex pair...
                if (eVerts[0] != _level.getFaceVertices(eFace)[eInFace])
                {
                    int tmp = valuesPerVert[0];
                    valuesPerVert[0] = valuesPerVert[1];
                    valuesPerVert[1] = tmp;
                }
            }
            else
            {
                //  Remember the extra level of indirection at level 0 -- avoid it here:
                if (_level.getDepth() > 0)
                {
                    valuesPerVert[0] = getVertexValueOffset(eVerts[0]);
                    valuesPerVert[1] = getVertexValueOffset(eVerts[1]);
                }
                else
                {
                    valuesPerVert[0] = getVertexValue(eVerts[0]);
                    valuesPerVert[1] = getVertexValue(eVerts[1]);
                }
            }
        }

        public void getVertexEdgeValues(int vIndex, int[] valuesPerEdge)
        {
            ConstIndexArray vEdges = _level.getVertexEdges(vIndex);
            ConstLocalIndexArray vInEdge = _level.getVertexEdgeLocalIndices(vIndex);

            ConstIndexArray vFaces = _level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFace = _level.getVertexFaceLocalIndices(vIndex);

            bool vIsBoundary = _level.getVertexTag(vIndex)._boundary;
            bool vIsManifold = !_level.getVertexTag(vIndex)._nonManifold;

            bool isBaseLevel = (_level.getDepth() == 0);

            for (int i = 0; i < vEdges.size(); ++i)
            {
                int eIndex = vEdges[i];
                ConstIndexArray eVerts = _level.getEdgeVertices(eIndex);

                //  Remember this method is for presumed continuous edges around the vertex:
                System.Diagnostics.Debug.Assert(edgeTopologyMatches(eIndex));

                int vOther = eVerts[(vInEdge[i] == 0) ? 1 : 0];
                if (getNumVertexValues(vOther) == 1)
                {
                    valuesPerEdge[i] = isBaseLevel ? getVertexValue(vOther) : getVertexValueOffset(vOther);
                }
                else if (vIsManifold)
                {
                    if (vIsBoundary && (i == (vEdges.size() - 1)))
                    {
                        ConstIndexArray fValues = getFaceValues(vFaces[i - 1]);

                        int prevInFace = (vInFace[i - 1] != 0) ? (vInFace[i - 1] - 1) : (fValues.size() - 1);
                        valuesPerEdge[i] = fValues[prevInFace];
                    }
                    else
                    {
                        ConstIndexArray fValues = getFaceValues(vFaces[i]);

                        int nextInFace = (vInFace[i] == (fValues.size() - 1)) ? 0 : (vInFace[i] + 1);
                        valuesPerEdge[i] = fValues[nextInFace];
                    }
                }
                else
                {
                    int eFace0 = _level.getEdgeFaces(eIndex)[0];
                    int eInFace0 = _level.getEdgeFaceLocalIndices(eIndex)[0];

                    ConstIndexArray fVerts = _level.getFaceVertices(eFace0);
                    ConstIndexArray fValues = getFaceValues(eFace0);
                    if (vOther == fVerts[eInFace0])
                    {
                        valuesPerEdge[i] = fValues[eInFace0];
                    }
                    else
                    {
                        int valueInFace = (eInFace0 == (fValues.size() - 1)) ? 0 : (eInFace0 + 1);
                        valuesPerEdge[i] = fValues[valueInFace];
                    }
                }
            }
        }

        //
        //  Gather information about the "span" of faces for each value:
        //
        //  The "size" (number of faces in which each value occurs), is most immediately useful
        //  in determining whether a value is a corner or smooth boundary, while other properties
        //  such as the first face and whether or not the span is interrupted by discts, semi-
        //  sharp or infinite edges, are useful to fully qualify smooth boundaries by the caller.
        //
        public void gatherValueSpans(int vIndex, ValueSpan[] vValueSpans)
        {
            ConstIndexArray vEdges = _level.getVertexEdges(vIndex);
            ConstIndexArray vFaces = _level.getVertexFaces(vIndex);

            ConstLocalIndexArray vFaceSiblings = getVertexFaceSiblings(vIndex);

            bool vHasSingleValue = (getNumVertexValues(vIndex) == 1);
            bool vIsBoundary = vEdges.size() > vFaces.size();
            bool vIsNonManifold = _level.getVertexTag(vIndex)._nonManifold;

            if (vIsNonManifold)
            {
                //  This needs more work as spans around a non-manifold vertex may themselves be
                //  manifold.  Just mark all spans with a discts edge for now to trigger them
                //  non-manifold

                ConstIndexArray vValues = getVertexValues(vIndex);
                for (int i = 0; i < vValues.size(); ++i)
                {
                    vValueSpans[i]._size = 0;
                    vValueSpans[i]._disctsEdgeCount = 1;
                }
            }
            else if (vHasSingleValue && !vIsBoundary)
            {
                //  Mark an interior dart disjoint if more than one discts edge:
                vValueSpans[0]._size = 0;
                vValueSpans[0]._start = 0;
                for (int i = 0; i < vEdges.size(); ++i)
                {
                    if (_edgeTags[vEdges[i]]._mismatch)
                    {
                        if (vValueSpans[0]._size > 0)
                        {
                            vValueSpans[0]._disctsEdgeCount = 1;
                            break;
                        }
                        else
                        {
                            vValueSpans[0]._size = (ushort)vFaces.size();
                            vValueSpans[0]._start = (ushort)i;
                        }
                    }
                    else if (_level.getEdgeTag(vEdges[i])._infSharp)
                    {
                        ++vValueSpans[0]._infSharpEdgeCount;
                    }
                    else if (_level.getEdgeTag(vEdges[i])._semiSharp)
                    {
                        ++vValueSpans[0]._semiSharpEdgeCount;
                    }
                }
                vValueSpans[0]._size = (ushort)vFaces.size();
            }
            else
            {
                //  Walk around the vertex and accumulate span info for each value -- be
                //  careful about the span for the first value "wrapping" around:
                vValueSpans[0]._size = 1;
                vValueSpans[0]._start = 0;
                if (!vIsBoundary && (vFaceSiblings[vFaces.size() - 1] == 0))
                {
                    if (_edgeTags[vEdges[0]]._mismatch)
                    {
                        ++vValueSpans[0]._disctsEdgeCount;
                    }
                    else if (_level.getEdgeTag(vEdges[0])._infSharp)
                    {
                        ++vValueSpans[0]._infSharpEdgeCount;
                    }
                    else if (_level.getEdgeTag(vEdges[0])._semiSharp)
                    {
                        ++vValueSpans[0]._semiSharpEdgeCount;
                    }
                }
                for (int i = 1; i < vFaces.size(); ++i)
                {
                    if (vFaceSiblings[i] == vFaceSiblings[i - 1])
                    {
                        if (_edgeTags[vEdges[i]]._mismatch)
                        {
                            ++vValueSpans[vFaceSiblings[i]]._disctsEdgeCount;
                        }
                        else if (_level.getEdgeTag(vEdges[i])._infSharp)
                        {
                            ++vValueSpans[vFaceSiblings[i]]._infSharpEdgeCount;
                        }
                        else if (_level.getEdgeTag(vEdges[i])._semiSharp)
                        {
                            ++vValueSpans[vFaceSiblings[i]]._semiSharpEdgeCount;
                        }
                    }
                    else
                    {
                        //  If we have already set the span for this value, mark disjoint
                        if (vValueSpans[vFaceSiblings[i]]._size > 0)
                        {
                            ++vValueSpans[vFaceSiblings[i]]._disctsEdgeCount;
                        }
                        vValueSpans[vFaceSiblings[i]]._start = (ushort)i;
                    }
                    ++vValueSpans[vFaceSiblings[i]]._size;
                }
                //  If the span for value 0 has wrapped around, decrement the disjoint added
                //  at the interior edge where it started the closing part of the span:
                if ((vFaceSiblings[vFaces.size() - 1] == 0) && !vIsBoundary)
                {
                    --vValueSpans[0]._disctsEdgeCount;
                }
            }
        }

        //
        //  Methods to retrieve and combine value and vertex tags:
        //
        public void getFaceValueTags(int faceIndex, ValueTag[] valueTags)
        {
            ConstIndexArray faceValues = getFaceValues(faceIndex);
            ConstIndexArray faceVerts = _level.getFaceVertices(faceIndex);

            for (int i = 0; i < faceValues.size(); ++i)
            {
                int srcValueIndex = findVertexValueIndex(faceVerts[i], faceValues[i]);
                System.Diagnostics.Debug.Assert(_vertValueIndices[srcValueIndex] == faceValues[i]);

                valueTags[i] = _vertValueTags[srcValueIndex];
            }
        }

        public ValueTag getFaceCompositeValueTag(int faceIndex)
        {
            ConstIndexArray faceValues = getFaceValues(faceIndex);
            ConstIndexArray faceVerts = _level.getFaceVertices(faceIndex);

            byte compInt = 0;
            for (int i = 0; i < faceValues.size(); ++i)
            {
                int srcValueIndex = findVertexValueIndex(faceVerts[i], faceValues[i]);
                System.Diagnostics.Debug.Assert(_vertValueIndices[srcValueIndex] == faceValues[i]);

                ValueTag srcTag = _vertValueTags[srcValueIndex];
                byte srcInt = srcTag.getBits();

                compInt |= srcInt;
            }
            return new ValueTag(compInt);
        }
    }
}
