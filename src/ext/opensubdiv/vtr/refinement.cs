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

//
//  Declaration for the main refinement class (Refinement) and its pre-requisites:
//
namespace OpenSubdiv.Vtr
{
    //
    //  Refinement:
    //      A refinement is a mapping between two levels -- relating the components in the original
    //  (parent) level to the one refined (child).  The refinement may be complete (uniform) or sparse
    //  (adaptive or otherwise selective), so not all components in the parent level will spawn
    //  components in the child level.
    //
    //  Refinement is an abstract class and expects subclasses corresponding to the different types
    //  of topological splits that the supported subdivision schemes collectively require, i.e. those
    //  listed in Sdc::SplitType.  Note the virtual requirements expected of the subclasses in the list
    //  of protected methods -- they differ mainly in the topology that is created in the child Level
    //  and not the propagation of tags through refinement, subdivision of sharpness values or the
    //  treatment of face-varying data.  The primary subclasses are QuadRefinement and TriRefinement.
    //
    //  At a high level, all that is necessary in terms of interface is to construct, initialize
    //  (linking the two levels), optionally select components for sparse refinement (via use of the
    //  SparseSelector) and call the refine() method.  This usage is expected of Far::TopologyRefiner.
    //
    //  Since we really want this class to be restricted from public access eventually, all methods
    //  begin with lower case (as is the convention for protected methods) and the list of friends
    //  will be maintained more strictly.
    //
    public abstract class Refinement
    {
        //
        //  Options associated with the actual refinement operation, which may end up
        //  quite involved if we want to allow for the refinement of data that is not
        //  of interest to be suppressed.  For now we have:
        //
        //      "sparse": the alternative to uniform refinement, which requires that
        //          components be previously selected/marked to be included.
        //
        //      "minimal topology": this is one that may get broken down into a finer
        //          set of options.  It suppresses "full topology" in the child level
        //          and only generates what is minimally necessary for interpolation --
        //          which requires at least the face-vertices for faces, but also the
        //          vertex-faces for any face-varying channels present.  So it will
        //          generate one or two of the six possible topological relations.
        //
        //  These are strictly controlled right now, e.g. for sparse refinement, we
        //  currently enforce full topology at the finest level to allow for subsequent
        //  patch construction.
        //
        //  (The C++ 1-bit bit-fields are bools here; default(Options) matches Options().)
        //
        public struct Options
        {
            public bool _sparse;
            public bool _faceVertsFirst;
            public bool _minimalTopology;

            //  Still under consideration:
            //unsigned int _childToParentMap : 1;
        }

        //
        //  Tags have now been added per-component in Level, but there is additional need to tag
        //  components within Refinement -- we can't tag the parent level components for any
        //  refinement (in order to keep it const) and tags associated with children that are
        //  specific to the child-to-parent mapping may not be warranted in the child level.
        //
        //  Parent tags are only required for sparse refinement.  The main property to tag is
        //  whether a component was selected, and so a single SparseTag is used for all three
        //  component types.  Tagging if a component is "transitional" is also useful.  This may
        //  only be necessary for edges but is currently packed into a mask per-edge for faces,
        //  which could be deferred, in which case "transitional" could be a single bit.
        //
        //  Child tags are part of the child-to-parent mapping, which consists of the parent
        //  component index for each child component, plus a tag for the child indicating more
        //  about its relationship to its parent, e.g. is it completely defined, what the parent
        //  component type is, what is the index of the child within its parent, etc.
        //
        //  The C++ bit-fields are emulated with masked properties on a packed byte (assignment
        //  truncates to the field width exactly as a bit-field does).  The 1-bit flags that are
        //  only ever used as booleans (_selected, _incomplete) are exposed as bool.
        //
        public struct SparseTag
        {
            private byte _bits;

            //  component specifically selected for refinement (1 bit)
            public bool _selected
            {
                get
                {
                    return (_bits & 0x01) != 0;
                }
                set
                {
                    _bits = (byte)((_bits & ~0x01) | (value ? 0x01 : 0x00));
                }
            }

            //  adjacent to a refined component (4-bits for face)
            public byte _transitional
            {
                get
                {
                    return (byte)((_bits >> 1) & 0x0F);
                }
                set
                {
                    _bits = (byte)((_bits & ~0x1E) | ((value & 0x0F) << 1));
                }
            }
        }

        public struct ChildTag
        {
            private byte _bits;

            //  incomplete neighborhood to represent limit of parent (1 bit)
            public bool _incomplete
            {
                get
                {
                    return (_bits & 0x01) != 0;
                }
                set
                {
                    _bits = (byte)((_bits & ~0x01) | (value ? 0x01 : 0x00));
                }
            }

            //  type of parent component:  vertex, edge or face (2 bits)
            public byte _parentType
            {
                get
                {
                    return (byte)((_bits >> 1) & 0x03);
                }
                set
                {
                    _bits = (byte)((_bits & ~0x06) | ((value & 0x03) << 1));
                }
            }

            //  index of child wrt parent:  0-3, or iterative if N > 4 (2 bits)
            public byte _indexInParent
            {
                get
                {
                    return (byte)((_bits >> 3) & 0x03);
                }
                set
                {
                    _bits = (byte)((_bits & ~0x18) | ((value & 0x03) << 3));
                }
            }
        }

        //
        //  Methods (and types) involved in subdividing the topology -- though not
        //  fully exploited, any subset of the 6 relations can be generated:
        //
        public struct Relations
        {
            public bool _faceVertices;
            public bool _faceEdges;
            public bool _edgeVertices;
            public bool _edgeFaces;
            public bool _vertexFaces;
            public bool _vertexEdges;

            public void setAll(bool enable)
            {
                _faceVertices = enable;
                _faceEdges = enable;
                _edgeVertices = enable;
                _edgeFaces = enable;
                _vertexFaces = enable;
                _vertexEdges = enable;
            }
        }

        public Refinement(Level parentArg, Level childArg, Sdc.Options options)
        {
            _parent = parentArg;
            _child = childArg;
            _options = options;
            _regFaceSize = -1;
            _uniform = false;
            _faceVertsFirst = false;
            _childFaceFromFaceCount = 0;
            _childEdgeFromFaceCount = 0;
            _childEdgeFromEdgeCount = 0;
            _childVertFromFaceCount = 0;
            _childVertFromEdgeCount = 0;
            _childVertFromVertCount = 0;
            _firstChildFaceFromFace = 0;
            _firstChildEdgeFromFace = 0;
            _firstChildEdgeFromEdge = 0;
            _firstChildVertFromFace = 0;
            _firstChildVertFromEdge = 0;
            _firstChildVertFromVert = 0;

            System.Diagnostics.Debug.Assert((childArg.getDepth() == 0) && (childArg.getNumVertices() == 0));
            childArg._depth = 1 + parentArg.getDepth();
        }

        //  C++ virtual destructor: deletes the FVarRefinement instances, which hold no
        //  unmanaged resources, so there is nothing to release in C#.

        //  (const and non-const overloads of parent()/child() collapse to one in C#)
        public Level parent()
        {
            return _parent;
        }

        public Level child()
        {
            return _child;
        }

        public Sdc.Split getSplitType()
        {
            return _splitType;
        }

        public int getRegularFaceSize()
        {
            return _regFaceSize;
        }

        public Sdc.Options getOptions()
        {
            return _options;
        }

        //  Face-varying:
        public int getNumFVarChannels()
        {
            return (int)_fvarChannels.Count;
        }

        public FVarRefinement getFVarRefinement(int c)
        {
            return _fvarChannels[c];
        }

        //
        //  The main refinement method -- provides a high-level overview of refinement:
        //
        //  The refinement process is as follows:
        //      - determine a mapping from parent components to their potential child components
        //          - for sparse refinement this mapping will be partial
        //      - determine the reverse mapping from chosen child components back to their parents
        //          - previously this was optional -- not strictly necessary and comes at added cost
        //          - does simplify iteration of child components when refinement is sparse
        //      - propagate/initialize component Tags from parents to their children
        //          - knowing these Tags for a child component simplifies dealing with it later
        //      - subdivide the topology, i.e. populate all topology relations for the child Level
        //          - any subset of the 6 relations in a Level can be created
        //          - using the minimum required in the last Level is very advantageous
        //      - subdivide the sharpness values in the child Level
        //      - subdivide face-varying channels in the child Level
        //
        public void refine(Options refineOptions = default(Options))
        {
            //  This will become redundant when/if assigned on construction:
            System.Diagnostics.Debug.Assert((_parent != null) && (_child != null));

            _uniform = !refineOptions._sparse;
            _faceVertsFirst = refineOptions._faceVertsFirst;

            //  We may soon have an option here to suppress refinement of FVar channels...
            bool refineOptions_ignoreFVarChannels = false;

            bool optionallyRefineFVar = (_parent.getNumFVarChannels() > 0) && !refineOptions_ignoreFVarChannels;

            //
            //  Initialize the parent-to-child and reverse child-to-parent mappings and propagate
            //  component tags to the new child components:
            //
            populateParentToChildMapping();

            initializeChildComponentCounts();

            populateChildToParentMapping();

            propagateComponentTags();

            //
            //  Subdivide the topology -- populating only those of the 6 relations specified
            //  (though we do require the vertex-face relation for refining FVar channels):
            //
            Relations relationsToPopulate = new Relations();
            if (refineOptions._minimalTopology)
            {
                relationsToPopulate.setAll(false);
                relationsToPopulate._faceVertices = true;
            }
            else
            {
                relationsToPopulate.setAll(true);
            }
            if (optionallyRefineFVar)
            {
                relationsToPopulate._vertexFaces = true;
            }

            subdivideTopology(relationsToPopulate);

            //
            //  Subdivide the sharpness values and face-varying channels:
            //    - note there is some dependency of the vertex tag/Rule for semi-sharp vertices
            //
            subdivideSharpnessValues();

            if (optionallyRefineFVar)
            {
                subdivideFVarChannels();
            }

            //  Various debugging support:
            //
            //printf("Vertex refinement to level %d completed...\n", _child->getDepth());
            //_child->print();
            //printf("  validating refinement to level %d...\n", _child->getDepth());
            //_child->validateTopology();
            //assert(_child->validateTopology());
        }

        public bool hasFaceVerticesFirst()
        {
            return _faceVertsFirst;
        }

        //
        //  Access to members -- some testing classes (involving vertex interpolation)
        //  currently make use of these:
        //
        public int getNumChildFacesFromFaces()
        {
            return _childFaceFromFaceCount;
        }

        public int getNumChildEdgesFromFaces()
        {
            return _childEdgeFromFaceCount;
        }

        public int getNumChildEdgesFromEdges()
        {
            return _childEdgeFromEdgeCount;
        }

        public int getNumChildVerticesFromFaces()
        {
            return _childVertFromFaceCount;
        }

        public int getNumChildVerticesFromEdges()
        {
            return _childVertFromEdgeCount;
        }

        public int getNumChildVerticesFromVertices()
        {
            return _childVertFromVertCount;
        }

        public int getFirstChildFaceFromFaces()
        {
            return _firstChildFaceFromFace;
        }

        public int getFirstChildEdgeFromFaces()
        {
            return _firstChildEdgeFromFace;
        }

        public int getFirstChildEdgeFromEdges()
        {
            return _firstChildEdgeFromEdge;
        }

        public int getFirstChildVertexFromFaces()
        {
            return _firstChildVertFromFace;
        }

        public int getFirstChildVertexFromEdges()
        {
            return _firstChildVertFromEdge;
        }

        public int getFirstChildVertexFromVertices()
        {
            return _firstChildVertFromVert;
        }

        public int getFaceChildVertex(int f)
        {
            return _faceChildVertIndex[f];
        }

        public int getEdgeChildVertex(int e)
        {
            return _edgeChildVertIndex[e];
        }

        public int getVertexChildVertex(int v)
        {
            return _vertChildVertIndex[v];
        }

        //  (const and non-const overloads collapse to one returning the mutable IndexArray,
        //   which converts implicitly to ConstIndexArray)
        public IndexArray getFaceChildFaces(int parentFace)
        {
            return new IndexArray(_faceChildFaceIndices.Data, _faceChildFaceCountsAndOffsets[2 * parentFace + 1],
                                  _faceChildFaceCountsAndOffsets[2 * parentFace]);
        }

        public IndexArray getFaceChildEdges(int parentFace)
        {
            return new IndexArray(_faceChildEdgeIndices.Data, _faceChildEdgeCountsAndOffsets[2 * parentFace + 1],
                                  _faceChildEdgeCountsAndOffsets[2 * parentFace]);
        }

        public IndexArray getEdgeChildEdges(int parentEdge)
        {
            return new IndexArray(_edgeChildEdgeIndices.Data, parentEdge * 2, 2);
        }

        //  Child-to-parent relationships
        public bool isChildVertexComplete(int v)
        {
            return !_childVertexTag[v]._incomplete;
        }

        public int getChildFaceParentFace(int f)
        {
            return _childFaceParentIndex[f];
        }

        public int getChildFaceInParentFace(int f)
        {
            return _childFaceTag[f]._indexInParent;
        }

        public int getChildEdgeParentIndex(int e)
        {
            return _childEdgeParentIndex[e];
        }

        public int getChildVertexParentIndex(int v)
        {
            return _childVertexParentIndex[v];
        }

        //  Methods to access and modify tags (const and non-const overloads collapse to one
        //  returning a ref):
        public ref SparseTag getParentFaceSparseTag(int f)
        {
            return ref _parentFaceTag[f];
        }

        public ref SparseTag getParentEdgeSparseTag(int e)
        {
            return ref _parentEdgeTag[e];
        }

        public ref SparseTag getParentVertexSparseTag(int v)
        {
            return ref _parentVertexTag[v];
        }

        public ref ChildTag getChildFaceTag(int f)
        {
            return ref _childFaceTag[f];
        }

        public ref ChildTag getChildEdgeTag(int e)
        {
            return ref _childEdgeTag[e];
        }

        public ref ChildTag getChildVertexTag(int v)
        {
            return ref _childVertexTag[v];
        }

        //
        //  Methods involved in constructing the parent-to-child mapping -- when the
        //  refinement is sparse, additional methods are needed to identify the selection:
        //
        public void populateParentToChildMapping()
        {
            allocateParentChildIndices();

            //
            //  If sparse refinement, mark indices of any components in addition to those selected
            //  so that we have the full neighborhood for selected components:
            //
            if (!_uniform)
            {
                //  Make sure the selection was non-empty -- currently unsupported...
                if (_parentVertexTag.Count == 0)
                {
                    System.Diagnostics.Debug.Assert(false, "Unsupported empty sparse refinement detected in Refinement");
                }
                markSparseChildComponentIndices();
            }

            populateParentChildIndices();
        }

        private static bool isSparseIndexMarked(int index)
        {
            return index != 0;
        }

        private static int sequenceSparseIndexVector(IndexVector indexVector, int baseValue = 0)
        {
            int validCount = 0;
            for (int i = 0; i < (int)indexVector.Count; ++i)
            {
                indexVector[i] = isSparseIndexMarked(indexVector[i])
                               ? (baseValue + validCount++) : Types.INDEX_INVALID;
            }
            return validCount;
        }

        private static int sequenceFullIndexVector(IndexVector indexVector, int baseValue = 0)
        {
            int indexCount = (int)indexVector.Count;
            for (int i = 0; i < indexCount; ++i)
            {
                indexVector[i] = baseValue++;
            }
            return indexCount;
        }

        public void populateParentChildIndices()
        {
            //
            //  Two vertex orderings are currently supported -- ordering vertices refined
            //  from vertices first, or those refined from faces first.  It's possible this
            //  may be extended to more possibilities.  Once the ordering is defined here,
            //  other than analogous initialization in FVarRefinement, the treatment of
            //  vertices in blocks based on origin should make the rest of the code
            //  invariant to ordering changes.
            //
            //  These two blocks now differ only in the utility function that assigns the
            //  sequential values to the index vectors -- so parameterization/simplification
            //  is now possible...
            //
            if (_uniform)
            {
                //  child faces:
                _firstChildFaceFromFace = 0;
                _childFaceFromFaceCount = sequenceFullIndexVector(_faceChildFaceIndices, _firstChildFaceFromFace);

                //  child edges:
                _firstChildEdgeFromFace = 0;
                _childEdgeFromFaceCount = sequenceFullIndexVector(_faceChildEdgeIndices, _firstChildEdgeFromFace);

                _firstChildEdgeFromEdge = _childEdgeFromFaceCount;
                _childEdgeFromEdgeCount = sequenceFullIndexVector(_edgeChildEdgeIndices, _firstChildEdgeFromEdge);

                //  child vertices:
                if (_faceVertsFirst)
                {
                    _firstChildVertFromFace = 0;
                    _childVertFromFaceCount = sequenceFullIndexVector(_faceChildVertIndex, _firstChildVertFromFace);

                    _firstChildVertFromEdge = _firstChildVertFromFace + _childVertFromFaceCount;
                    _childVertFromEdgeCount = sequenceFullIndexVector(_edgeChildVertIndex, _firstChildVertFromEdge);

                    _firstChildVertFromVert = _firstChildVertFromEdge + _childVertFromEdgeCount;
                    _childVertFromVertCount = sequenceFullIndexVector(_vertChildVertIndex, _firstChildVertFromVert);
                }
                else
                {
                    _firstChildVertFromVert = 0;
                    _childVertFromVertCount = sequenceFullIndexVector(_vertChildVertIndex, _firstChildVertFromVert);

                    _firstChildVertFromFace = _firstChildVertFromVert + _childVertFromVertCount;
                    _childVertFromFaceCount = sequenceFullIndexVector(_faceChildVertIndex, _firstChildVertFromFace);

                    _firstChildVertFromEdge = _firstChildVertFromFace + _childVertFromFaceCount;
                    _childVertFromEdgeCount = sequenceFullIndexVector(_edgeChildVertIndex, _firstChildVertFromEdge);
                }
            }
            else
            {
                //  child faces:
                _firstChildFaceFromFace = 0;
                _childFaceFromFaceCount = sequenceSparseIndexVector(_faceChildFaceIndices, _firstChildFaceFromFace);

                //  child edges:
                _firstChildEdgeFromFace = 0;
                _childEdgeFromFaceCount = sequenceSparseIndexVector(_faceChildEdgeIndices, _firstChildEdgeFromFace);

                _firstChildEdgeFromEdge = _childEdgeFromFaceCount;
                _childEdgeFromEdgeCount = sequenceSparseIndexVector(_edgeChildEdgeIndices, _firstChildEdgeFromEdge);

                //  child vertices:
                if (_faceVertsFirst)
                {
                    _firstChildVertFromFace = 0;
                    _childVertFromFaceCount = sequenceSparseIndexVector(_faceChildVertIndex, _firstChildVertFromFace);

                    _firstChildVertFromEdge = _firstChildVertFromFace + _childVertFromFaceCount;
                    _childVertFromEdgeCount = sequenceSparseIndexVector(_edgeChildVertIndex, _firstChildVertFromEdge);

                    _firstChildVertFromVert = _firstChildVertFromEdge + _childVertFromEdgeCount;
                    _childVertFromVertCount = sequenceSparseIndexVector(_vertChildVertIndex, _firstChildVertFromVert);
                }
                else
                {
                    _firstChildVertFromVert = 0;
                    _childVertFromVertCount = sequenceSparseIndexVector(_vertChildVertIndex, _firstChildVertFromVert);

                    _firstChildVertFromFace = _firstChildVertFromVert + _childVertFromVertCount;
                    _childVertFromFaceCount = sequenceSparseIndexVector(_faceChildVertIndex, _firstChildVertFromFace);

                    _firstChildVertFromEdge = _firstChildVertFromFace + _childVertFromFaceCount;
                    _childVertFromEdgeCount = sequenceSparseIndexVector(_edgeChildVertIndex, _firstChildVertFromEdge);
                }
            }
        }

        public void printParentToChildMapping()
        {
            Console.Write("Parent-to-child component mapping:\n");
            for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
            {
                Console.Write("  Face " + pFace + ":\n");
                Console.Write("    Child vert:  " + _faceChildVertIndex[pFace] + "\n");

                Console.Write("    Child faces: ");
                ConstIndexArray childFaces = getFaceChildFaces(pFace);
                for (int i = 0; i < childFaces.size(); ++i)
                {
                    Console.Write(" " + childFaces[i]);
                }
                Console.Write("\n");

                Console.Write("    Child edges: ");
                ConstIndexArray childEdges = getFaceChildEdges(pFace);
                for (int i = 0; i < childEdges.size(); ++i)
                {
                    Console.Write(" " + childEdges[i]);
                }
                Console.Write("\n");
            }
            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                Console.Write("  Edge " + pEdge + ":\n");
                Console.Write("    Child vert:  " + _edgeChildVertIndex[pEdge] + "\n");

                ConstIndexArray childEdges = getEdgeChildEdges(pEdge);
                Console.Write("    Child edges: " + childEdges[0] + " " + childEdges[1] + "\n");
            }
            for (int pVert = 0; pVert < _parent.getNumVertices(); ++pVert)
            {
                Console.Write("  Vert " + pVert + ":\n");
                Console.Write("    Child vert:  " + _vertChildVertIndex[pVert] + "\n");
            }
        }

        public abstract void allocateParentChildIndices();

        //  Supporting method for sparse refinement:
        public void initializeSparseSelectionTags()
        {
            _parentFaceTag.Resize(_parent.getNumFaces());
            _parentEdgeTag.Resize(_parent.getNumEdges());
            _parentVertexTag.Resize(_parent.getNumVertices());
        }

        public abstract void markSparseFaceChildren();

        public void initializeChildComponentCounts()
        {
            //
            //  Assign the child's component counts/inventory based on the child components identified:
            //
            _child._faceCount = _childFaceFromFaceCount;
            _child._edgeCount = _childEdgeFromFaceCount + _childEdgeFromEdgeCount;
            _child._vertCount = _childVertFromFaceCount + _childVertFromEdgeCount + _childVertFromVertCount;
        }

        //
        //  Methods to construct the child-to-parent mapping:
        //
        public void populateChildToParentMapping()
        {
            ChildTag[,] initialChildTags = new ChildTag[2, 4];
            for (int i = 0; i < 2; ++i)
            {
                for (int j = 0; j < 4; ++j)
                {
                    ref ChildTag tag = ref initialChildTags[i, j];

                    tag._incomplete = (i != 0);
                    tag._parentType = 0;
                    tag._indexInParent = (byte)j;
                }
            }

            populateFaceParentVectors(initialChildTags);
            populateEdgeParentVectors(initialChildTags);
            populateVertexParentVectors(initialChildTags);
        }

        public void populateFaceParentVectors(ChildTag[,] initialChildTags)
        {
            _childFaceTag.Resize(_child.getNumFaces());
            _childFaceParentIndex.Resize(_child.getNumFaces());

            populateFaceParentFromParentFaces(initialChildTags);
        }

        public void populateFaceParentFromParentFaces(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                int cFace = getFirstChildFaceFromFaces();
                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
                {
                    ConstIndexArray cFaces = getFaceChildFaces(pFace);
                    if (cFaces.size() == 4)
                    {
                        _childFaceTag[cFace + 0] = initialChildTags[0, 0];
                        _childFaceTag[cFace + 1] = initialChildTags[0, 1];
                        _childFaceTag[cFace + 2] = initialChildTags[0, 2];
                        _childFaceTag[cFace + 3] = initialChildTags[0, 3];

                        _childFaceParentIndex[cFace + 0] = pFace;
                        _childFaceParentIndex[cFace + 1] = pFace;
                        _childFaceParentIndex[cFace + 2] = pFace;
                        _childFaceParentIndex[cFace + 3] = pFace;

                        cFace += 4;
                    }
                    else
                    {
                        bool childTooLarge = (cFaces.size() > 4);
                        for (int i = 0; i < cFaces.size(); ++i, ++cFace)
                        {
                            _childFaceTag[cFace] = initialChildTags[0, childTooLarge ? 0 : i];
                            _childFaceParentIndex[cFace] = pFace;
                        }
                    }
                }
            }
            else
            {
                //  Child faces of faces:
                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
                {
                    bool incomplete = !_parentFaceTag[pFace]._selected;

                    IndexArray cFaces = getFaceChildFaces(pFace);
                    if (!incomplete && (cFaces.size() == 4))
                    {
                        _childFaceTag[cFaces[0]] = initialChildTags[0, 0];
                        _childFaceTag[cFaces[1]] = initialChildTags[0, 1];
                        _childFaceTag[cFaces[2]] = initialChildTags[0, 2];
                        _childFaceTag[cFaces[3]] = initialChildTags[0, 3];

                        _childFaceParentIndex[cFaces[0]] = pFace;
                        _childFaceParentIndex[cFaces[1]] = pFace;
                        _childFaceParentIndex[cFaces[2]] = pFace;
                        _childFaceParentIndex[cFaces[3]] = pFace;
                    }
                    else
                    {
                        bool childTooLarge = (cFaces.size() > 4);
                        for (int i = 0; i < cFaces.size(); ++i)
                        {
                            if (Types.IndexIsValid(cFaces[i]))
                            {
                                _childFaceTag[cFaces[i]] = initialChildTags[incomplete ? 1 : 0, childTooLarge ? 0 : i];
                                _childFaceParentIndex[cFaces[i]] = pFace;
                            }
                        }
                    }
                }
            }
        }

        public void populateEdgeParentVectors(ChildTag[,] initialChildTags)
        {
            _childEdgeTag.Resize(_child.getNumEdges());
            _childEdgeParentIndex.Resize(_child.getNumEdges());

            populateEdgeParentFromParentFaces(initialChildTags);
            populateEdgeParentFromParentEdges(initialChildTags);
        }

        public void populateEdgeParentFromParentFaces(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                int cEdge = getFirstChildEdgeFromFaces();
                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
                {
                    ConstIndexArray cEdges = getFaceChildEdges(pFace);
                    if (cEdges.size() == 4)
                    {
                        _childEdgeTag[cEdge + 0] = initialChildTags[0, 0];
                        _childEdgeTag[cEdge + 1] = initialChildTags[0, 1];
                        _childEdgeTag[cEdge + 2] = initialChildTags[0, 2];
                        _childEdgeTag[cEdge + 3] = initialChildTags[0, 3];

                        _childEdgeParentIndex[cEdge + 0] = pFace;
                        _childEdgeParentIndex[cEdge + 1] = pFace;
                        _childEdgeParentIndex[cEdge + 2] = pFace;
                        _childEdgeParentIndex[cEdge + 3] = pFace;

                        cEdge += 4;
                    }
                    else
                    {
                        bool childTooLarge = (cEdges.size() > 4);
                        for (int i = 0; i < cEdges.size(); ++i, ++cEdge)
                        {
                            _childEdgeTag[cEdge] = initialChildTags[0, childTooLarge ? 0 : i];
                            _childEdgeParentIndex[cEdge] = pFace;
                        }
                    }
                }
            }
            else
            {
                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
                {
                    bool incomplete = !_parentFaceTag[pFace]._selected;

                    IndexArray cEdges = getFaceChildEdges(pFace);
                    if (!incomplete && (cEdges.size() == 4))
                    {
                        _childEdgeTag[cEdges[0]] = initialChildTags[0, 0];
                        _childEdgeTag[cEdges[1]] = initialChildTags[0, 1];
                        _childEdgeTag[cEdges[2]] = initialChildTags[0, 2];
                        _childEdgeTag[cEdges[3]] = initialChildTags[0, 3];

                        _childEdgeParentIndex[cEdges[0]] = pFace;
                        _childEdgeParentIndex[cEdges[1]] = pFace;
                        _childEdgeParentIndex[cEdges[2]] = pFace;
                        _childEdgeParentIndex[cEdges[3]] = pFace;
                    }
                    else
                    {
                        bool childTooLarge = (cEdges.size() > 4);
                        for (int i = 0; i < cEdges.size(); ++i)
                        {
                            if (Types.IndexIsValid(cEdges[i]))
                            {
                                _childEdgeTag[cEdges[i]] = initialChildTags[incomplete ? 1 : 0, childTooLarge ? 0 : i];
                                _childEdgeParentIndex[cEdges[i]] = pFace;
                            }
                        }
                    }
                }
            }
        }

        public void populateEdgeParentFromParentEdges(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                int cEdge = getFirstChildEdgeFromEdges();
                for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge, cEdge += 2)
                {
                    _childEdgeTag[cEdge + 0] = initialChildTags[0, 0];
                    _childEdgeTag[cEdge + 1] = initialChildTags[0, 1];

                    _childEdgeParentIndex[cEdge + 0] = pEdge;
                    _childEdgeParentIndex[cEdge + 1] = pEdge;
                }
            }
            else
            {
                for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
                {
                    bool incomplete = !_parentEdgeTag[pEdge]._selected;

                    IndexArray cEdges = getEdgeChildEdges(pEdge);
                    if (!incomplete)
                    {
                        _childEdgeTag[cEdges[0]] = initialChildTags[0, 0];
                        _childEdgeTag[cEdges[1]] = initialChildTags[0, 1];

                        _childEdgeParentIndex[cEdges[0]] = pEdge;
                        _childEdgeParentIndex[cEdges[1]] = pEdge;
                    }
                    else
                    {
                        for (int i = 0; i < 2; ++i)
                        {
                            if (Types.IndexIsValid(cEdges[i]))
                            {
                                _childEdgeTag[cEdges[i]] = initialChildTags[incomplete ? 1 : 0, i];
                                _childEdgeParentIndex[cEdges[i]] = pEdge;
                            }
                        }
                    }
                }
            }
        }

        public void populateVertexParentVectors(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                _childVertexTag.Resize(_child.getNumVertices(), initialChildTags[0, 0]);
            }
            else
            {
                _childVertexTag.Resize(_child.getNumVertices(), initialChildTags[1, 0]);
            }
            _childVertexParentIndex.Resize(_child.getNumVertices());

            populateVertexParentFromParentFaces(initialChildTags);
            populateVertexParentFromParentEdges(initialChildTags);
            populateVertexParentFromParentVertices(initialChildTags);
        }

        public void populateVertexParentFromParentFaces(ChildTag[,] initialChildTags)
        {
            if (getNumChildVerticesFromFaces() == 0)
            {
                return;
            }

            if (_uniform)
            {
                int cVert = getFirstChildVertexFromFaces();
                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace, ++cVert)
                {
                    //  Child tag was initialized as the complete and only child when allocated

                    _childVertexParentIndex[cVert] = pFace;
                }
            }
            else
            {
                ChildTag completeChildTag = initialChildTags[0, 0];

                for (int pFace = 0; pFace < _parent.getNumFaces(); ++pFace)
                {
                    int cVert = _faceChildVertIndex[pFace];
                    if (Types.IndexIsValid(cVert))
                    {
                        //  Child tag was initialized as incomplete -- reset if complete:
                        if (_parentFaceTag[pFace]._selected)
                        {
                            _childVertexTag[cVert] = completeChildTag;
                        }
                        _childVertexParentIndex[cVert] = pFace;
                    }
                }
            }
        }

        public void populateVertexParentFromParentEdges(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                int cVert = getFirstChildVertexFromEdges();
                for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge, ++cVert)
                {
                    //  Child tag was initialized as the complete and only child when allocated

                    _childVertexParentIndex[cVert] = pEdge;
                }
            }
            else
            {
                ChildTag completeChildTag = initialChildTags[0, 0];

                for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
                {
                    int cVert = _edgeChildVertIndex[pEdge];
                    if (Types.IndexIsValid(cVert))
                    {
                        //  Child tag was initialized as incomplete -- reset if complete:
                        if (_parentEdgeTag[pEdge]._selected)
                        {
                            _childVertexTag[cVert] = completeChildTag;
                        }
                        _childVertexParentIndex[cVert] = pEdge;
                    }
                }
            }
        }

        public void populateVertexParentFromParentVertices(ChildTag[,] initialChildTags)
        {
            if (_uniform)
            {
                int cVert = getFirstChildVertexFromVertices();
                for (int pVert = 0; pVert < _parent.getNumVertices(); ++pVert, ++cVert)
                {
                    //  Child tag was initialized as the complete and only child when allocated

                    _childVertexParentIndex[cVert] = pVert;
                }
            }
            else
            {
                ChildTag completeChildTag = initialChildTags[0, 0];

                for (int pVert = 0; pVert < _parent.getNumVertices(); ++pVert)
                {
                    int cVert = _vertChildVertIndex[pVert];
                    if (Types.IndexIsValid(cVert))
                    {
                        //  Child tag was initialized as incomplete but these should be complete:
                        if (_parentVertexTag[pVert]._selected)
                        {
                            _childVertexTag[cVert] = completeChildTag;
                        }
                        _childVertexParentIndex[cVert] = pVert;
                    }
                }
            }
        }

        //
        //  Methods to propagate/initialize child component tags from their parent component:
        //
        public void propagateComponentTags()
        {
            populateFaceTagVectors();
            populateEdgeTagVectors();
            populateVertexTagVectors();
        }

        public void populateFaceTagVectors()
        {
            _child._faceTags.Resize(_child.getNumFaces());

            populateFaceTagsFromParentFaces();
        }

        public void populateFaceTagsFromParentFaces()
        {
            //
            //  Tags for faces originating from faces are inherited from the parent face:
            //
            int cFace = getFirstChildFaceFromFaces();
            int cFaceEnd = cFace + getNumChildFacesFromFaces();
            for ( ; cFace < cFaceEnd; ++cFace)
            {
                _child._faceTags[cFace] = _parent._faceTags[_childFaceParentIndex[cFace]];
            }
        }

        public void populateEdgeTagVectors()
        {
            _child._edgeTags.Resize(_child.getNumEdges());

            populateEdgeTagsFromParentFaces();
            populateEdgeTagsFromParentEdges();
        }

        public void populateEdgeTagsFromParentFaces()
        {
            //
            //  Tags for edges originating from faces are all constant:
            //
            Level.ETag eTag = new Level.ETag();
            eTag.clear();

            int cEdge = getFirstChildEdgeFromFaces();
            int cEdgeEnd = cEdge + getNumChildEdgesFromFaces();
            for ( ; cEdge < cEdgeEnd; ++cEdge)
            {
                _child._edgeTags[cEdge] = eTag;
            }
        }

        public void populateEdgeTagsFromParentEdges()
        {
            //
            //  Tags for edges originating from edges are inherited from the parent edge:
            //
            int cEdge = getFirstChildEdgeFromEdges();
            int cEdgeEnd = cEdge + getNumChildEdgesFromEdges();
            for ( ; cEdge < cEdgeEnd; ++cEdge)
            {
                _child._edgeTags[cEdge] = _parent._edgeTags[_childEdgeParentIndex[cEdge]];
            }
        }

        public void populateVertexTagVectors()
        {
            _child._vertTags.Resize(_child.getNumVertices());

            populateVertexTagsFromParentFaces();
            populateVertexTagsFromParentEdges();
            populateVertexTagsFromParentVertices();

            if (!_uniform)
            {
                for (int cVert = 0; cVert < _child.getNumVertices(); ++cVert)
                {
                    if (_childVertexTag[cVert]._incomplete)
                    {
                        _child._vertTags[cVert]._incomplete = true;
                    }
                }
            }
        }

        public void populateVertexTagsFromParentFaces()
        {
            //
            //  Similarly, tags for vertices originating from faces are all constant -- with the
            //  unfortunate exception of refining level 0, where the faces may be N-sided and so
            //  introduce new vertices that need to be tagged as extra-ordinary:
            //
            if (getNumChildVerticesFromFaces() == 0)
            {
                return;
            }

            Level.VTag vTag = new Level.VTag();
            vTag.clear();
            vTag._rule = (Level.VTag.VTagSize)Crease.Rule.RULE_SMOOTH;

            int cVert = getFirstChildVertexFromFaces();
            int cVertEnd = cVert + getNumChildVerticesFromFaces();

            if (_parent._depth > 0)
            {
                for ( ; cVert < cVertEnd; ++cVert)
                {
                    _child._vertTags[cVert] = vTag;
                }
            }
            else
            {
                for ( ; cVert < cVertEnd; ++cVert)
                {
                    _child._vertTags[cVert] = vTag;

                    if (_parent.getNumFaceVertices(_childVertexParentIndex[cVert]) != _regFaceSize)
                    {
                        _child._vertTags[cVert]._xordinary = true;
                    }
                }
            }
        }

        public void populateVertexTagsFromParentEdges()
        {
            //
            //  Tags for vertices originating from edges are initialized according to the tags
            //  of the parent edge:
            //
            Level.VTag vTag = new Level.VTag();
            vTag.clear();

            for (int pEdge = 0; pEdge < _parent.getNumEdges(); ++pEdge)
            {
                int cVert = _edgeChildVertIndex[pEdge];
                if (!Types.IndexIsValid(cVert))
                {
                    continue;
                }

                //  From the cleared local VTag, we just need to assign properties dependent
                //  on the parent edge:
                Level.ETag pEdgeTag = _parent._edgeTags[pEdge];

                vTag._nonManifold = pEdgeTag._nonManifold;
                vTag._boundary = pEdgeTag._boundary;
                vTag._semiSharpEdges = pEdgeTag._semiSharp;
                vTag._infSharpEdges = pEdgeTag._infSharp;
                vTag._infSharpCrease = pEdgeTag._infSharp;
                vTag._infIrregular = pEdgeTag._infSharp && pEdgeTag._nonManifold;

                vTag._rule = (Level.VTag.VTagSize)((pEdgeTag._semiSharp || pEdgeTag._infSharp)
                               ? Crease.Rule.RULE_CREASE : Crease.Rule.RULE_SMOOTH);

                _child._vertTags[cVert] = vTag;
            }
        }

        public void populateVertexTagsFromParentVertices()
        {
            //
            //  Tags for vertices originating from vertices are inherited from the parent vertex:
            //
            int cVert = getFirstChildVertexFromVertices();
            int cVertEnd = cVert + getNumChildVerticesFromVertices();
            for ( ; cVert < cVertEnd; ++cVert)
            {
                _child._vertTags[cVert] = _parent._vertTags[_childVertexParentIndex[cVert]];
                _child._vertTags[cVert]._incidIrregFace = false;
            }
        }

        //
        //  Methods to subdivide the topology:
        //
        //  The main method to subdivide topology is fairly simple -- given a set of relations
        //  to populate it simply tests and populates each relation separately.  The method for
        //  each relation is responsible for appropriate allocation and initialization of all
        //  data involved, and these are virtual -- provided by a quad- or tri-split subclass.
        //
        public void subdivideTopology(Relations applyTo)
        {
            if (applyTo._faceVertices)
            {
                populateFaceVertexRelation();
            }
            if (applyTo._faceEdges)
            {
                populateFaceEdgeRelation();
            }
            if (applyTo._edgeVertices)
            {
                populateEdgeVertexRelation();
            }
            if (applyTo._edgeFaces)
            {
                populateEdgeFaceRelation();
            }
            if (applyTo._vertexFaces)
            {
                populateVertexFaceRelation();
            }
            if (applyTo._vertexEdges)
            {
                populateVertexEdgeRelation();
            }

            //
            //  Additional members of the child Level not specific to any relation...
            //      - note in the case of max-valence, the child's max-valence may be less
            //  than the parent if that maximal parent vertex was not included in the sparse
            //  refinement (possible when sparse refinement is more general).
            //      - it may also be more if the base level was fairly trivial, i.e. less
            //  than the regular valence, or contains non-manifold edges with many faces.
            //      - NOTE that when/if we support N-gons for tri-splitting, that the valence
            //  of edge-vertices introduced on the N-gon may be 7 rather than 6, while N may
            //  be less than both.
            //
            //  In general, we need a better way to deal with max-valence.  The fact that
            //  each topology relation is independent/optional complicates the issue of
            //  where to keep track of it...
            //
            if (_splitType == Sdc.Split.SPLIT_TO_QUADS)
            {
                _child._maxValence = Math.Max(_parent._maxValence, 4);
                _child._maxValence = Math.Max(_child._maxValence, 2 + _parent._maxEdgeFaces);
            }
            else
            {
                _child._maxValence = Math.Max(_parent._maxValence, 6);
                _child._maxValence = Math.Max(_child._maxValence, 2 + _parent._maxEdgeFaces * 2);
            }
        }

        public abstract void populateFaceVertexRelation();
        public abstract void populateFaceEdgeRelation();
        public abstract void populateEdgeVertexRelation();
        public abstract void populateEdgeFaceRelation();
        public abstract void populateVertexFaceRelation();
        public abstract void populateVertexEdgeRelation();

        //
        //  Methods to subdivide sharpness values:
        //
        public void subdivideSharpnessValues()
        {
            //
            //  Subdividing edge and vertex sharpness values are independent, but in order
            //  to maintain proper classification/tagging of components as semi-sharp, both
            //  must be computed and the neighborhood inspected to properly update the
            //  status.
            //
            //  It is possible to clear the semi-sharp status when propagating the tags and
            //  to reset it (potentially multiple times) when updating the sharpness values.
            //  The vertex subdivision Rule is also affected by this, which complicates the
            //  process.  So for now we apply a post-process to explicitly handle all
            //  semi-sharp vertices.
            //

            //  These methods will update sharpness tags local to the edges and vertices:
            subdivideEdgeSharpness();
            subdivideVertexSharpness();

            //  This method uses local sharpness tags (set above) to update vertex tags that
            //  reflect the neighborhood of the vertex (e.g. its rule):
            reclassifySemisharpVertices();
        }

        public void subdivideEdgeSharpness()
        {
            Crease creasing = new Crease(_options);

            _child._edgeSharpness.Clear();
            _child._edgeSharpness.Resize(_child.getNumEdges(), Crease.SHARPNESS_SMOOTH);

            //
            //  Edge sharpness is passed to child-edges using the parent edge and the
            //  parent vertex for which the child corresponds.  Child-edges are created
            //  from both parent faces and parent edges, but those child-edges created
            //  from a parent face should be within the face's interior and so smooth
            //  (and so previously initialized).
            //
            //  The presence/validity of each parent edges child vert indicates one or
            //  more child edges.
            //
            //  NOTE -- It is also useful at this time to classify the child vert of
            //  this edge based on the creasing information here, particularly when a
            //  non-trivial creasing method like Chaikin is used.  This is not being
            //  done now but is worth considering...
            //
            //  (StackBuffer<float,16> in C++, a plain array here -- only needed for non-uniform creasing)
            float[] pVertEdgeSharpness = null;
            if (!creasing.IsUniform())
            {
                pVertEdgeSharpness = new float[_parent.getMaxValence()];
            }

            int cEdge = getFirstChildEdgeFromEdges();
            int cEdgeEnd = cEdge + getNumChildEdgesFromEdges();
            for ( ; cEdge < cEdgeEnd; ++cEdge)
            {
                ref float cSharpness = ref _child._edgeSharpness[cEdge];
                ref Level.ETag cEdgeTag = ref _child._edgeTags[cEdge];

                if (cEdgeTag._infSharp)
                {
                    cSharpness = Crease.SHARPNESS_INFINITE;
                }
                else if (cEdgeTag._semiSharp)
                {
                    int pEdge = _childEdgeParentIndex[cEdge];
                    float pSharpness = _parent._edgeSharpness[pEdge];

                    if (creasing.IsUniform())
                    {
                        cSharpness = creasing.SubdivideUniformSharpness(pSharpness);
                    }
                    else
                    {
                        ConstIndexArray pEdgeVerts = _parent.getEdgeVertices(pEdge);
                        int pVert = pEdgeVerts[_childEdgeTag[cEdge]._indexInParent];
                        ConstIndexArray pVertEdges = _parent.getVertexEdges(pVert);

                        for (int i = 0; i < pVertEdges.size(); ++i)
                        {
                            pVertEdgeSharpness[i] = _parent._edgeSharpness[pVertEdges[i]];
                        }
                        cSharpness = creasing.SubdivideEdgeSharpnessAtVertex(pSharpness, pVertEdges.size(),
                                                                                 pVertEdgeSharpness);
                    }
                    if (!Crease.IsSharp(cSharpness))
                    {
                        cEdgeTag._semiSharp = false;
                    }
                }
            }
        }

        public void subdivideVertexSharpness()
        {
            Crease creasing = new Crease(_options);

            _child._vertSharpness.Clear();
            _child._vertSharpness.Resize(_child.getNumVertices(), Crease.SHARPNESS_SMOOTH);

            //
            //  All child-verts originating from faces or edges are initialized as smooth
            //  above.  Only those originating from vertices require "subdivided" values:
            //
            //  Only deal with the subrange of vertices originating from vertices:
            int cVertBegin = getFirstChildVertexFromVertices();
            int cVertEnd = cVertBegin + getNumChildVerticesFromVertices();

            for (int cVert = cVertBegin; cVert < cVertEnd; ++cVert)
            {
                ref float cSharpness = ref _child._vertSharpness[cVert];
                ref Level.VTag cVertTag = ref _child._vertTags[cVert];

                if (cVertTag._infSharp)
                {
                    cSharpness = Crease.SHARPNESS_INFINITE;
                }
                else if (cVertTag._semiSharp)
                {
                    int pVert = _childVertexParentIndex[cVert];
                    float pSharpness = _parent._vertSharpness[pVert];

                    cSharpness = creasing.SubdivideVertexSharpness(pSharpness);
                    if (!Crease.IsSharp(cSharpness))
                    {
                        cVertTag._semiSharp = false;
                    }
                }
            }
        }

        public void reclassifySemisharpVertices()
        {
            Crease creasing = new Crease(_options);

            //
            //  Inspect all vertices derived from edges -- for those whose parent edges were semisharp,
            //  reset the semisharp tag and the associated Rule according to the sharpness pair for the
            //  subdivided edges (note this may be better handled when the edge sharpness is computed):
            //
            int vertFromEdgeBegin = getFirstChildVertexFromEdges();
            int vertFromEdgeEnd = vertFromEdgeBegin + getNumChildVerticesFromEdges();

            for (int cVert = vertFromEdgeBegin; cVert < vertFromEdgeEnd; ++cVert)
            {
                ref Level.VTag cVertTag = ref _child._vertTags[cVert];
                if (!cVertTag._semiSharpEdges)
                {
                    continue;
                }

                int pEdge = _childVertexParentIndex[cVert];

                ConstIndexArray cEdges = getEdgeChildEdges(pEdge);

                if (_childVertexTag[cVert]._incomplete)
                {
                    //  One child edge likely missing -- assume Crease if remaining edge semi-sharp:
                    cVertTag._semiSharpEdges = (Types.IndexIsValid(cEdges[0]) && _child._edgeTags[cEdges[0]]._semiSharp) ||
                                               (Types.IndexIsValid(cEdges[1]) && _child._edgeTags[cEdges[1]]._semiSharp);
                    cVertTag._rule = (Level.VTag.VTagSize)(cVertTag._semiSharpEdges ? Crease.Rule.RULE_CREASE : Crease.Rule.RULE_SMOOTH);
                }
                else
                {
                    int sharpEdgeCount = (_child._edgeTags[cEdges[0]]._semiSharp ? 1 : 0) + (_child._edgeTags[cEdges[1]]._semiSharp ? 1 : 0);

                    cVertTag._semiSharpEdges = (sharpEdgeCount > 0);
                    cVertTag._rule = (Level.VTag.VTagSize)(creasing.DetermineVertexVertexRule(0.0f, sharpEdgeCount));
                }
            }

            //
            //  Inspect all vertices derived from vertices -- for those whose parent vertices were
            //  semisharp (inherited in the child vert's tag), inspect and reset the semisharp tag
            //  and the associated Rule (based on neighboring child edges around the child vertex).
            //
            //  We should never find such a vertex "incomplete" in a sparse refinement as a parent
            //  vertex is either selected or not, but never neighboring.  So the only complication
            //  here is whether the local topology of child edges exists -- it may have been pruned
            //  from the last level to reduce memory.  If so, we use the parent to identify the
            //  child edges.
            //
            //  In both cases, we count the number of sharp and semisharp child edges incident the
            //  child vertex and adjust the "semisharp" and "rule" tags accordingly.
            //
            int vertFromVertBegin = getFirstChildVertexFromVertices();
            int vertFromVertEnd = vertFromVertBegin + getNumChildVerticesFromVertices();

            for (int cVert = vertFromVertBegin; cVert < vertFromVertEnd; ++cVert)
            {
                int pVert = _childVertexParentIndex[cVert];
                Level.VTag pVertTag = _parent._vertTags[pVert];

                //  Skip if parent not semi-sharp:
                if (!pVertTag._semiSharp && !pVertTag._semiSharpEdges)
                {
                    continue;
                }

                //
                //  We need to inspect the child neighborhood's sharpness when either semi-sharp
                //  edges were present around the parent vertex, or the parent vertex sharpness
                //  decayed:
                //
                ref Level.VTag cVertTag = ref _child._vertTags[cVert];

                bool sharpVertexDecayed = pVertTag._semiSharp && !cVertTag._semiSharp;

                if (pVertTag._semiSharpEdges || sharpVertexDecayed)
                {
                    int infSharpEdgeCount = 0;
                    int semiSharpEdgeCount = 0;

                    bool cVertEdgesPresent = (_child.getNumVertexEdgesTotal() > 0);
                    if (cVertEdgesPresent)
                    {
                        ConstIndexArray cEdges = _child.getVertexEdges(cVert);

                        for (int i = 0; i < cEdges.size(); ++i)
                        {
                            Level.ETag cEdgeTag = _child._edgeTags[cEdges[i]];

                            infSharpEdgeCount += cEdgeTag._infSharp ? 1 : 0;
                            semiSharpEdgeCount += cEdgeTag._semiSharp ? 1 : 0;
                        }
                    }
                    else
                    {
                        ConstIndexArray pEdges = _parent.getVertexEdges(pVert);
                        ConstLocalIndexArray pVertInEdge = _parent.getVertexEdgeLocalIndices(pVert);

                        for (int i = 0; i < pEdges.size(); ++i)
                        {
                            ConstIndexArray cEdgePair = getEdgeChildEdges(pEdges[i]);

                            int cEdge = cEdgePair[pVertInEdge[i]];
                            Level.ETag cEdgeTag = _child._edgeTags[cEdge];

                            infSharpEdgeCount += cEdgeTag._infSharp ? 1 : 0;
                            semiSharpEdgeCount += cEdgeTag._semiSharp ? 1 : 0;
                        }
                    }
                    cVertTag._semiSharpEdges = (semiSharpEdgeCount > 0);

                    if (!cVertTag._semiSharp && !cVertTag._infSharp)
                    {
                        cVertTag._rule = (Level.VTag.VTagSize)(creasing.DetermineVertexVertexRule(0.0f,
                                                infSharpEdgeCount + semiSharpEdgeCount));
                    }
                }
            }
        }

        //
        //  Methods to subdivide face-varying channels:
        //
        public void subdivideFVarChannels()
        {
            System.Diagnostics.Debug.Assert(_child._fvarChannels.Count == 0);
            System.Diagnostics.Debug.Assert(this._fvarChannels.Count == 0);

            int channelCount = _parent.getNumFVarChannels();

            for (int channel = 0; channel < channelCount; ++channel)
            {
                FVarLevel parentFVar = _parent._fvarChannels[channel];

                FVarLevel childFVar = new FVarLevel(_child);
                FVarRefinement refineFVar = new FVarRefinement(this, parentFVar, childFVar);

                refineFVar.applyRefinement();

                _child._fvarChannels.Add(childFVar);
                this._fvarChannels.Add(refineFVar);
            }
        }

        //
        //  Marking of sparse child components -- including those selected and those neighboring...
        //
        //      For schemes requiring neighboring support, this is the equivalent of the "guarantee
        //  neighbors" in Hbr -- it ensures that all components required to define the limit of
        //  those "selected" are also generated in the refinement.
        //
        //  The difference with Hbr is that we do this in a single pass for all components once
        //  "selection" of components of interest has been completed.
        //
        //  Considering two approaches:
        //      1) By Vertex neighborhoods:
        //          - for each base vertex
        //              - for each incident face
        //                  - test and mark components for its child face
        //  or
        //      2) By Edge and Face contents:
        //          - for each base edge
        //              - test and mark local components
        //          - for each base face
        //              - test and mark local components
        //
        //  Given a typical quad mesh with N verts, N faces and 2*N edges, determine which is more
        //  efficient...
        //
        //  Going with (2) initially for simplicity -- certain aspects of (1) are awkward, i.e. the
        //  identification of child-edges to be marked (trivial in (2).  We are also guaranteed with
        //  (2) that we only visit each component once, i.e. each edge and each face.
        //
        //  Revising the above assessment... (2) has gotten WAY more complicated once the ability to
        //  select child faces is provided.  Given that feature is important to Manuel for support
        //  of the FarStencilTables we have to assume it will be needed.  So we'll try (1) out as it
        //  will be simpler to get it correct -- we can work on improving performance later.
        //
        //  Complexity added by child component selection:
        //      - the child vertex of the component can now be selected as part of a child face or
        //  edge, and so the parent face or edge is not fully selected.  So we've had to add another
        //  bit to the marking masks to indicate when a parent component is "fully selected".
        //      - selecting a child face creates the situation where child edges of parent edges do
        //  not have any selected vertex at their ends -- both can be neighboring.  This complicated
        //  the marking of neighboring child edges, which was otherwise trivial -- if any end vertex
        //  of a child edge (of a parent edge) was selected, the child edge was at least neighboring.
        //
        //  Final note on the marking technique:
        //      There are currently two values to the marking of child components, which are no
        //  longer that useful.  It is now sufficient, and not likely to be necessary, to distinguish
        //  between what was selected or added to support it.  Ultimately that will be determined by
        //  inspecting the selected flag on the parent component once the child-to-parent map is in
        //  place.
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

        public void markSparseChildComponentIndices()
        {
            //
            //  There is an explicit ordering here as the work done for vertices is a subset
            //  of what is required for edges, which in turn is a subset of what is required
            //  for faces.  This ordering and their related implementations tries to avoid
            //  doing redundant work and accomplishing everything necessary in a single
            //  iteration through each component type.
            //
            markSparseVertexChildren();
            markSparseEdgeChildren();
            markSparseFaceChildren();
        }

        public void markSparseVertexChildren()
        {
            System.Diagnostics.Debug.Assert(_parentVertexTag.Count > 0);

            //
            //  For each parent vertex:
            //      - mark the descending child vertex for each selected vertex
            //
            for (int pVert = 0; pVert < parent().getNumVertices(); ++pVert)
            {
                if (_parentVertexTag[pVert]._selected)
                {
                    markSparseIndexSelected(ref _vertChildVertIndex[pVert]);
                }
            }
        }

        public void markSparseEdgeChildren()
        {
            System.Diagnostics.Debug.Assert(_parentEdgeTag.Count > 0);

            //
            //  For each parent edge:
            //      - mark the descending child edges and vertex for each selected edge
            //      - test each end vertex of unselected edges to see if selected:
            //          - mark both the child edge and the middle child vertex if so
            //      - set transitional bit for all edges based on selection of incident faces
            //
            //  Note that no edges have been marked "fully selected" -- only their vertices have
            //  been marked and marking of their child edges deferred to visiting each edge only
            //  once here.
            //
            for (int pEdge = 0; pEdge < parent().getNumEdges(); ++pEdge)
            {
                IndexArray eChildEdges = getEdgeChildEdges(pEdge);
                ConstIndexArray eVerts = parent().getEdgeVertices(pEdge);

                ref SparseTag pEdgeTag = ref _parentEdgeTag[pEdge];

                if (pEdgeTag._selected)
                {
                    markSparseIndexSelected(ref eChildEdges[0]);
                    markSparseIndexSelected(ref eChildEdges[1]);
                    markSparseIndexSelected(ref _edgeChildVertIndex[pEdge]);
                }
                else
                {
                    if (_parentVertexTag[eVerts[0]]._selected)
                    {
                        markSparseIndexNeighbor(ref eChildEdges[0]);
                        markSparseIndexNeighbor(ref _edgeChildVertIndex[pEdge]);
                    }
                    if (_parentVertexTag[eVerts[1]]._selected)
                    {
                        markSparseIndexNeighbor(ref eChildEdges[1]);
                        markSparseIndexNeighbor(ref _edgeChildVertIndex[pEdge]);
                    }
                }

                //
                //  TAG the parent edges as "transitional" here if only one was selected (or in
                //  the more general non-manifold case, they are not all selected the same way).
                //  We use the transitional tags on the edges to TAG the parent face below.
                //
                //  Note -- this is best done now rather than as a post-process as we have more
                //  explicit information about the selected components.  Unless we also tag the
                //  parent faces as selected, we can't easily tell from the child-faces of the
                //  edge's incident faces which were generated by selection or neighboring...
                //
                ConstIndexArray eFaces = parent().getEdgeFaces(pEdge);
                if (eFaces.size() == 2)
                {
                    pEdgeTag._transitional = (byte)((_parentFaceTag[eFaces[0]]._selected !=
                                                     _parentFaceTag[eFaces[1]]._selected) ? 1 : 0);
                }
                else if (eFaces.size() < 2)
                {
                    pEdgeTag._transitional = 0;
                }
                else
                {
                    bool isFace0Selected = _parentFaceTag[eFaces[0]]._selected;

                    pEdgeTag._transitional = 0;
                    for (int i = 1; i < eFaces.size(); ++i)
                    {
                        if (_parentFaceTag[eFaces[i]]._selected != isFace0Selected)
                        {
                            pEdgeTag._transitional = 1;
                            break;
                        }
                    }
                }
            }
        }

        //  Protected members
        //  (C++ `friend void Level::print(const Refinement *) const` -- all members are public)

        //
        //  Data members -- the logical grouping of some of these (and methods that make use
        //  of them) may lead to grouping them into a few utility classes or structs...
        //

        //  Defined on construction:
        public Level _parent;
        public Level _child;
        public Sdc.Options _options;

        //  Defined by the subclass:
        public Sdc.Split _splitType;
        public int _regFaceSize;

        //  Determined by the refinement options:
        public bool _uniform;
        public bool _faceVertsFirst;

        //
        //  Inventory and ordering of the types of child components:
        //
        public int _childFaceFromFaceCount;  // arguably redundant (all faces originate from faces)
        public int _childEdgeFromFaceCount;
        public int _childEdgeFromEdgeCount;
        public int _childVertFromFaceCount;
        public int _childVertFromEdgeCount;
        public int _childVertFromVertCount;

        public int _firstChildFaceFromFace;  // arguably redundant (all faces originate from faces)
        public int _firstChildEdgeFromFace;
        public int _firstChildEdgeFromEdge;
        public int _firstChildVertFromFace;
        public int _firstChildVertFromEdge;
        public int _firstChildVertFromVert;

        //
        //  The parent-to-child mapping:
        //      These are vectors sized according to the number of parent components (and
        //  their topology) that contain references/indices to the child components that
        //  result from them by refinement.  When refinement is sparse, parent components
        //  that have not spawned all child components will have their missing children
        //  marked as invalid.
        //
        //  NOTE the "Array" members here.  Often vectors within the Level can be shared
        //  with the Refinement, and an Array instance is used to do so.  If not shared
        //  the subclass just initializes the Array members after allocating its own local
        //  vector members.
        //
        public IndexArray _faceChildFaceCountsAndOffsets;
        public IndexArray _faceChildEdgeCountsAndOffsets;

        public IndexVector _faceChildFaceIndices = new IndexVector();  // *cannot* always use face-vert counts/offsets
        public IndexVector _faceChildEdgeIndices = new IndexVector();  // can use face-vert counts/offsets
        public IndexVector _faceChildVertIndex = new IndexVector();

        public IndexVector _edgeChildEdgeIndices = new IndexVector();  // trivial/corresponding pair for each
        public IndexVector _edgeChildVertIndex = new IndexVector();

        public IndexVector _vertChildVertIndex = new IndexVector();

        //
        //  The child-to-parent mapping:
        //
        public IndexVector _childFaceParentIndex = new IndexVector();
        public IndexVector _childEdgeParentIndex = new IndexVector();
        public IndexVector _childVertexParentIndex = new IndexVector();

        public StdVector<ChildTag> _childFaceTag = new StdVector<ChildTag>();
        public StdVector<ChildTag> _childEdgeTag = new StdVector<ChildTag>();
        public StdVector<ChildTag> _childVertexTag = new StdVector<ChildTag>();

        //
        //  Tags for sparse selection of components:
        //
        public StdVector<SparseTag> _parentFaceTag = new StdVector<SparseTag>();
        public StdVector<SparseTag> _parentEdgeTag = new StdVector<SparseTag>();
        public StdVector<SparseTag> _parentVertexTag = new StdVector<SparseTag>();

        //
        //  Refinement data for face-varying channels present in the Levels being refined:
        //
        public List<FVarRefinement> _fvarChannels = new List<FVarRefinement>();
    }
}
