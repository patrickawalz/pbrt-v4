//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/level.h and vtr/level.cpp
//
//  Porting notes:
//   - C++ bit-fields (VTag, ETag, FTag, FVarLevel::ETag/ValueTag) are stored packed in an integer
//     with the same bit layout as GCC/little-endian, and each field is exposed as a property of type
//     TagBit (1-bit flags; convertible to/from bool and uint) or VTag.VTagSize (the 4-bit rule).
//   - const/non-const overloaded accessors are a single method returning the mutable form
//     (IndexArray / LocalIndexArray / ref float / ref VTag ...), which converts implicitly to the
//     const form where needed.
//   - Output pointers (e.g. "Index patchPoints[]") are int[] starting at element 0; the gather*
//     methods also have an overload with a trailing "int dstOffset" for the C++ "ptr + offset" case.
//   - friend access (Refinement etc.) is expressed by making all members public.
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
    //  A single bit-field flag.  Behaves like the C++ "unsigned : 1" members:  assignable from bool
    //  or an unsigned/constant value (only the low bit is kept) and usable as bool or uint.
    //
    public readonly struct TagBit : IEquatable<TagBit>
    {
        public readonly byte Value;

        public TagBit(uint value)
        {
            Value = (byte)(value & 1u);
        }

        public static implicit operator TagBit(bool b)
        {
            return new TagBit(b ? 1u : 0u);
        }

        public static implicit operator TagBit(uint v)
        {
            return new TagBit(v);
        }

        public static implicit operator bool(TagBit t)
        {
            return t.Value != 0;
        }

        public static implicit operator uint(TagBit t)
        {
            return t.Value;
        }

        public static explicit operator int(TagBit t)
        {
            return t.Value;
        }

        public static explicit operator ushort(TagBit t)
        {
            return t.Value;
        }

        public static explicit operator byte(TagBit t)
        {
            return t.Value;
        }

        public static bool operator true(TagBit t)
        {
            return t.Value != 0;
        }

        public static bool operator false(TagBit t)
        {
            return t.Value == 0;
        }

        public static TagBit operator |(TagBit a, TagBit b)
        {
            return new TagBit((uint)(a.Value | b.Value));
        }

        public static TagBit operator &(TagBit a, TagBit b)
        {
            return new TagBit((uint)(a.Value & b.Value));
        }

        public static bool operator ==(TagBit a, TagBit b)
        {
            return a.Value == b.Value;
        }

        public static bool operator !=(TagBit a, TagBit b)
        {
            return a.Value != b.Value;
        }

        public bool Equals(TagBit other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return (obj is TagBit) && Equals((TagBit)obj);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    //
    //  Level:
    //      A refinement level includes a vectorized representation of the topology
    //  for a particular subdivision level.  The topology is "complete" in that any
    //  level can be used as the base level of another subdivision hierarchy and can
    //  be considered a complete mesh independent of its ancestors.  It currently
    //  does contain a "depth" member -- as some inferences can then be made about
    //  the topology (i.e. all quads or all tris if not level 0).
    //
    //  The representation of topology here is to store six topological relationships
    //  in tables of integers.  Each is stored in its own array(s) so the result is
    //  a SOA representation of the topology.  The six relations are:
    //
    //      - face-verts:  vertices incident/comprising a face
    //      - face-edges:  edges incident a face
    //      - edge-verts:  vertices incident/comprising an edge
    //      - edge-faces:  faces incident an edge
    //      - vert-faces:  faces incident a vertex
    //      - vert-edges:  edges incident a vertex
    //
    public class Level
    {
        //
        //  Tags for components.  Bit layout (LSB first) matches the C++ declaration order.
        //
        public struct VTag
        {
            //  typedef unsigned short VTagSize;  (expressed as a struct so that the 4-bit rule can be
            //  cast to/from Sdc.Crease.Rule and compared with ushort/uint constants)
            public struct VTagSize
            {
                public ushort Value;

                public VTagSize(ushort value)
                {
                    Value = value;
                }

                public static implicit operator VTagSize(uint v)
                {
                    return new VTagSize((ushort)v);
                }

                public static implicit operator VTagSize(Crease.Rule rule)
                {
                    return new VTagSize((ushort)rule);
                }

                public static implicit operator ushort(VTagSize s)
                {
                    return s.Value;
                }

                public static bool operator ==(VTagSize a, VTagSize b)
                {
                    return a.Value == b.Value;
                }

                public static bool operator !=(VTagSize a, VTagSize b)
                {
                    return a.Value != b.Value;
                }

                public override bool Equals(object obj)
                {
                    return (obj is VTagSize) && (((VTagSize)obj).Value == Value);
                }

                public override int GetHashCode()
                {
                    return Value;
                }
            }

            public ushort _bits;

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
                _bits = (ushort)((_bits & ~(1 << shift)) | (value.Value << shift));
            }

            public TagBit _nonManifold    { get { return getBit(0);  } set { setBit(0, value);  } }  // fixed
            public TagBit _xordinary      { get { return getBit(1);  } set { setBit(1, value);  } }  // fixed
            public TagBit _boundary       { get { return getBit(2);  } set { setBit(2, value);  } }  // fixed
            public TagBit _corner         { get { return getBit(3);  } set { setBit(3, value);  } }  // fixed
            public TagBit _infSharp       { get { return getBit(4);  } set { setBit(4, value);  } }  // fixed
            public TagBit _semiSharp      { get { return getBit(5);  } set { setBit(5, value);  } }  // variable
            public TagBit _semiSharpEdges { get { return getBit(6);  } set { setBit(6, value);  } }  // variable

            //  4 bits, variable when _semiSharp
            public VTagSize _rule
            {
                get { return new VTagSize((ushort)((_bits >> 7) & 0xf)); }
                set { _bits = (ushort)((_bits & ~(0xf << 7)) | ((value.Value & 0xf) << 7)); }
            }

            public TagBit _incomplete     { get { return getBit(11); } set { setBit(11, value); } }  // variable only set in refined levels
            public TagBit _incidIrregFace { get { return getBit(12); } set { setBit(12, value); } }  // variable only set in base level

            public TagBit _infSharpEdges  { get { return getBit(13); } set { setBit(13, value); } }  // fixed
            public TagBit _infSharpCrease { get { return getBit(14); } set { setBit(14, value); } }  // fixed
            public TagBit _infIrregular   { get { return getBit(15); } set { setBit(15, value); } }  // fixed

            public VTag(ushort bits)
            {
                _bits = bits;
            }

            public ushort getBits()
            {
                return _bits;
            }

            public static VTag BitwiseOr(VTag[] vTags, int size = 4)
            {
                ushort tagBits = vTags[0].getBits();
                for (int i = 1; i < size; ++i)
                {
                    tagBits |= vTags[i].getBits();
                }
                return new VTag(tagBits);
            }
        }

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

            public TagBit _nonManifold { get { return getBit(0); } set { setBit(0, value); } }  // fixed
            public TagBit _boundary    { get { return getBit(1); } set { setBit(1, value); } }  // fixed
            public TagBit _infSharp    { get { return getBit(2); } set { setBit(2, value); } }  // fixed
            public TagBit _semiSharp   { get { return getBit(3); } set { setBit(3, value); } }  // variable

            public ETag(byte bits)
            {
                _bits = bits;
            }

            public byte getBits()
            {
                return _bits;
            }

            public static ETag BitwiseOr(ETag[] eTags, int size = 4)
            {
                byte tagBits = eTags[0].getBits();
                for (int i = 1; i < size; ++i)
                {
                    tagBits |= eTags[i].getBits();
                }
                return new ETag(tagBits);
            }
        }

        public struct FTag
        {
            public byte _bits;

            public void clear()
            {
                _bits = 0;
            }

            public TagBit _hole  // fixed
            {
                get { return new TagBit((uint)_bits); }
                set { _bits = (byte)((_bits & ~1) | value.Value); }
            }
        }

        //
        //  A "span" of faces around a vertex (used when gathering partial rings):
        //
        public struct VSpan
        {
            public ushort _numFaces;
            public ushort _startFace;
            public ushort _cornerInSpan;

            public bool _periodic;
            public bool _sharp;

            public void clear()
            {
                _numFaces = 0;
                _startFace = 0;
                _cornerInSpan = 0;
                _periodic = false;
                _sharp = false;
            }

            public bool isAssigned()
            {
                return _numFaces > 0;
            }
        }

        public enum TopologyError
        {
            TOPOLOGY_MISSING_EDGE_FACES = 0,
            TOPOLOGY_MISSING_EDGE_VERTS,
            TOPOLOGY_MISSING_FACE_EDGES,
            TOPOLOGY_MISSING_FACE_VERTS,
            TOPOLOGY_MISSING_VERT_FACES,
            TOPOLOGY_MISSING_VERT_EDGES,

            TOPOLOGY_FAILED_CORRELATION_EDGE_FACE,
            TOPOLOGY_FAILED_CORRELATION_FACE_VERT,
            TOPOLOGY_FAILED_CORRELATION_FACE_EDGE,

            TOPOLOGY_FAILED_ORIENTATION_INCIDENT_EDGE,
            TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACE,
            TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACES_EDGES,

            TOPOLOGY_DEGENERATE_EDGE,
            TOPOLOGY_NON_MANIFOLD_EDGE,

            TOPOLOGY_INVALID_CREASE_EDGE,
            TOPOLOGY_INVALID_CREASE_VERT
        }

        //  C++ style access to the nested enumerants as Level.TOPOLOGY_MISSING_EDGE_FACES etc.:
        public const TopologyError TOPOLOGY_MISSING_EDGE_FACES = TopologyError.TOPOLOGY_MISSING_EDGE_FACES;
        public const TopologyError TOPOLOGY_MISSING_EDGE_VERTS = TopologyError.TOPOLOGY_MISSING_EDGE_VERTS;
        public const TopologyError TOPOLOGY_MISSING_FACE_EDGES = TopologyError.TOPOLOGY_MISSING_FACE_EDGES;
        public const TopologyError TOPOLOGY_MISSING_FACE_VERTS = TopologyError.TOPOLOGY_MISSING_FACE_VERTS;
        public const TopologyError TOPOLOGY_MISSING_VERT_FACES = TopologyError.TOPOLOGY_MISSING_VERT_FACES;
        public const TopologyError TOPOLOGY_MISSING_VERT_EDGES = TopologyError.TOPOLOGY_MISSING_VERT_EDGES;
        public const TopologyError TOPOLOGY_FAILED_CORRELATION_EDGE_FACE = TopologyError.TOPOLOGY_FAILED_CORRELATION_EDGE_FACE;
        public const TopologyError TOPOLOGY_FAILED_CORRELATION_FACE_VERT = TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_VERT;
        public const TopologyError TOPOLOGY_FAILED_CORRELATION_FACE_EDGE = TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_EDGE;
        public const TopologyError TOPOLOGY_FAILED_ORIENTATION_INCIDENT_EDGE = TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_EDGE;
        public const TopologyError TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACE = TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACE;
        public const TopologyError TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACES_EDGES = TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACES_EDGES;
        public const TopologyError TOPOLOGY_DEGENERATE_EDGE = TopologyError.TOPOLOGY_DEGENERATE_EDGE;
        public const TopologyError TOPOLOGY_NON_MANIFOLD_EDGE = TopologyError.TOPOLOGY_NON_MANIFOLD_EDGE;
        public const TopologyError TOPOLOGY_INVALID_CREASE_EDGE = TopologyError.TOPOLOGY_INVALID_CREASE_EDGE;
        public const TopologyError TOPOLOGY_INVALID_CREASE_VERT = TopologyError.TOPOLOGY_INVALID_CREASE_VERT;

        public delegate void ValidationCallback(TopologyError errCode, string msg, object clientData);

        //
        //  Members (public here in place of C++ friend access):
        //
        public int _faceCount;
        public int _edgeCount;
        public int _vertCount;

        public int _depth;

        public int _maxEdgeFaces;
        public int _maxValence;

        public IndexVector _faceVertCountsAndOffsets = new IndexVector();  // 2 per face, redundant after level 0
        public IndexVector _faceVertIndices = new IndexVector();           // 3 or 4 per face, variable at level 0
        public IndexVector _faceEdgeIndices = new IndexVector();           // matches face-vert indices
        public StdVector<FTag> _faceTags = new StdVector<FTag>();          // 1 per face:  includes "hole" tag

        public IndexVector _edgeVertIndices = new IndexVector();           // 2 per edge
        public IndexVector _edgeFaceCountsAndOffsets = new IndexVector();  // 2 per edge
        public IndexVector _edgeFaceIndices = new IndexVector();           // varies with faces per edge
        public StdVector<ushort> _edgeFaceLocalIndices = new StdVector<ushort>();  // varies with faces per edge

        public StdVector<float> _edgeSharpness = new StdVector<float>();   // 1 per edge
        public StdVector<ETag> _edgeTags = new StdVector<ETag>();          // 1 per edge:  manifold, boundary, etc.

        public IndexVector _vertFaceCountsAndOffsets = new IndexVector();  // 2 per vertex
        public IndexVector _vertFaceIndices = new IndexVector();           // varies with valence
        public StdVector<ushort> _vertFaceLocalIndices = new StdVector<ushort>();  // varies with valence, 8-bit for now

        public IndexVector _vertEdgeCountsAndOffsets = new IndexVector();  // 2 per vertex
        public IndexVector _vertEdgeIndices = new IndexVector();           // varies with valence
        public StdVector<ushort> _vertEdgeLocalIndices = new StdVector<ushort>();  // varies with valence, 8-bit for now

        public StdVector<float> _vertSharpness = new StdVector<float>();   // 1 per vertex
        public StdVector<VTag> _vertTags = new StdVector<VTag>();          // 1 per vertex:  manifold, Sdc::Rule, etc.

        public StdVector<FVarLevel> _fvarChannels = new StdVector<FVarLevel>();

        //
        //  Simple (for now) constructor:
        //
        public Level()
        {
            _faceCount = 0;
            _edgeCount = 0;
            _vertCount = 0;
            _depth = 0;
            _maxEdgeFaces = 0;
            _maxValence = 0;
        }

        //
        //  Simple counts and accessors:
        //
        public int getDepth() { return _depth; }

        public int getNumVertices() { return _vertCount; }
        public int getNumFaces() { return _faceCount; }
        public int getNumEdges() { return _edgeCount; }

        //  Total lengths of relations stored in the tables:
        public int getNumFaceVerticesTotal() { return _faceVertIndices.Count; }
        public int getNumFaceEdgesTotal() { return _faceEdgeIndices.Count; }
        public int getNumEdgeVerticesTotal() { return _edgeVertIndices.Count; }
        public int getNumEdgeFacesTotal() { return _edgeFaceIndices.Count; }
        public int getNumVertexFacesTotal() { return _vertFaceIndices.Count; }
        public int getNumVertexEdgesTotal() { return _vertEdgeIndices.Count; }

        //  Maximum valences of faces and edges:
        public int getMaxValence() { return _maxValence; }
        public int getMaxEdgeFaces() { return _maxEdgeFaces; }

        //  Methods to access the relation tables/indices -- note that for some relations
        //  we store an additional "local index", e.g. for the case of vert-faces if one
        //  of the faces F[i] is incident a vertex V, then L[i] is the "local index" in F[i]
        //  of V.  Once most patch-related components are better understood, we should be
        //  able to...
        //
        public IndexArray getFaceVertices(int faceIndex)
        {
            return new IndexArray(_faceVertIndices.Data, _faceVertCountsAndOffsets[faceIndex * 2 + 1],
                                  _faceVertCountsAndOffsets[faceIndex * 2]);
        }

        public IndexArray getFaceEdges(int faceIndex)
        {
            return new IndexArray(_faceEdgeIndices.Data, _faceVertCountsAndOffsets[faceIndex * 2 + 1],
                                  _faceVertCountsAndOffsets[faceIndex * 2]);
        }

        public IndexArray getEdgeVertices(int edgeIndex)
        {
            return new IndexArray(_edgeVertIndices.Data, edgeIndex * 2, 2);
        }

        public IndexArray getEdgeFaces(int edgeIndex)
        {
            return new IndexArray(_edgeFaceIndices.Data,
                                  _edgeFaceCountsAndOffsets[edgeIndex * 2 + 1],
                                  _edgeFaceCountsAndOffsets[edgeIndex * 2]);
        }

        public IndexArray getVertexFaces(int vertIndex)
        {
            return new IndexArray(_vertFaceIndices.Data, _vertFaceCountsAndOffsets[vertIndex * 2 + 1],
                                  _vertFaceCountsAndOffsets[vertIndex * 2]);
        }

        public IndexArray getVertexEdges(int vertIndex)
        {
            return new IndexArray(_vertEdgeIndices.Data, _vertEdgeCountsAndOffsets[vertIndex * 2 + 1],
                                  _vertEdgeCountsAndOffsets[vertIndex * 2]);
        }

        public LocalIndexArray getEdgeFaceLocalIndices(int edgeIndex)
        {
            return new LocalIndexArray(_edgeFaceLocalIndices.Data,
                                       _edgeFaceCountsAndOffsets[edgeIndex * 2 + 1],
                                       _edgeFaceCountsAndOffsets[edgeIndex * 2]);
        }

        public LocalIndexArray getVertexFaceLocalIndices(int vertIndex)
        {
            return new LocalIndexArray(_vertFaceLocalIndices.Data, _vertFaceCountsAndOffsets[vertIndex * 2 + 1],
                                       _vertFaceCountsAndOffsets[vertIndex * 2]);
        }

        public LocalIndexArray getVertexEdgeLocalIndices(int vertIndex)
        {
            return new LocalIndexArray(_vertEdgeLocalIndices.Data, _vertEdgeCountsAndOffsets[vertIndex * 2 + 1],
                                       _vertEdgeCountsAndOffsets[vertIndex * 2]);
        }

        //  Replace these with access to sharpness buffers/arrays:
        public ref float getEdgeSharpness(int edgeIndex)
        {
            return ref _edgeSharpness.Data[edgeIndex];
        }

        public ref float getVertexSharpness(int vertIndex)
        {
            return ref _vertSharpness.Data[vertIndex];
        }

        public Crease.Rule getVertexRule(int vertIndex)
        {
            return (Crease.Rule)(ushort)_vertTags.Data[vertIndex]._rule;
        }

        //  Find the edge connecting two vertices, if one exists:
        public int findEdge(int v0Index, int v1Index)
        {
            return this.findEdge(v0Index, v1Index, this.getVertexEdges(v0Index));
        }

        //  Holes and tags:
        public void setFaceHole(int faceIndex, bool b)
        {
            _faceTags.Data[faceIndex]._hole = b;
        }

        public bool isFaceHole(int faceIndex)
        {
            return _faceTags.Data[faceIndex]._hole;
        }

        //  Face-varying channels:
        public Options getFVarOptions(int channel)
        {
            return _fvarChannels[channel].getOptions();
        }

        public int getNumFVarChannels() { return _fvarChannels.Count; }

        public int getNumFVarValues(int channel)
        {
            return _fvarChannels[channel].getNumValues();
        }

        public IndexArray getFaceFVarValues(int faceIndex, int channel)
        {
            return _fvarChannels[channel].getFaceValues(faceIndex);
        }

        public FVarLevel getFVarLevel(int channel) { return _fvarChannels[channel]; }

        public void setEdgeNonManifold(int edgeIndex, bool b)
        {
            _edgeTags.Data[edgeIndex]._nonManifold = b;
        }

        public bool isEdgeNonManifold(int edgeIndex)
        {
            return _edgeTags.Data[edgeIndex]._nonManifold;
        }

        public void setVertexNonManifold(int vertIndex, bool b)
        {
            _vertTags.Data[vertIndex]._nonManifold = b;
        }

        public bool isVertexNonManifold(int vertIndex)
        {
            return _vertTags.Data[vertIndex]._nonManifold;
        }

        public ref VTag getVertexTag(int vertIndex) { return ref _vertTags.Data[vertIndex]; }
        public ref ETag getEdgeTag(int edgeIndex) { return ref _edgeTags.Data[edgeIndex]; }
        public ref FTag getFaceTag(int faceIndex) { return ref _faceTags.Data[faceIndex]; }

        //
        //  Methods to resize/populate the tables (formerly protected, used by Refinement and factories):
        //
        public void resizeFaces(int faceCount)
        {
            _faceCount = faceCount;
            _faceVertCountsAndOffsets.Resize(2 * faceCount);

            _faceTags.Resize(faceCount);
            System.Array.Clear(_faceTags.Data, 0, _faceCount);
        }

        public void resizeFaceVertices(int totalFaceVertCount)
        {
            _faceVertIndices.Resize(totalFaceVertCount);
        }

        public void resizeFaceEdges(int totalFaceEdgeCount)
        {
            _faceEdgeIndices.Resize(totalFaceEdgeCount);
        }

        public void resizeEdges(int edgeCount)
        {
            _edgeCount = edgeCount;
            _edgeFaceCountsAndOffsets.Resize(2 * edgeCount);

            _edgeSharpness.Resize(edgeCount);
            _edgeTags.Resize(edgeCount);

            if (edgeCount > 0)
            {
                System.Array.Clear(_edgeTags.Data, 0, _edgeCount);
            }
        }

        public void resizeEdgeVertices()
        {
            _edgeVertIndices.Resize(2 * _edgeCount);
        }

        public void resizeEdgeFaces(int totalEdgeFaceCount)
        {
            _edgeFaceIndices.Resize(totalEdgeFaceCount);
            _edgeFaceLocalIndices.Resize(totalEdgeFaceCount);
        }

        public void resizeVertices(int vertCount)
        {
            _vertCount = vertCount;
            _vertFaceCountsAndOffsets.Resize(2 * vertCount);
            _vertEdgeCountsAndOffsets.Resize(2 * vertCount);

            _vertSharpness.Resize(vertCount);
            _vertTags.Resize(vertCount);
            System.Array.Clear(_vertTags.Data, 0, _vertCount);
        }

        public void resizeVertexFaces(int totalVertFaceCount)
        {
            _vertFaceIndices.Resize(totalVertFaceCount);
            _vertFaceLocalIndices.Resize(totalVertFaceCount);
        }

        public void resizeVertexEdges(int totalVertEdgeCount)
        {
            _vertEdgeIndices.Resize(totalVertEdgeCount);
            _vertEdgeLocalIndices.Resize(totalVertEdgeCount);
        }

        public void setMaxValence(int valence)
        {
            _maxValence = valence;
        }

        //  Counts and offsets:
        public int getNumFaceVertices(int faceIndex) { return _faceVertCountsAndOffsets[2 * faceIndex]; }
        public int getOffsetOfFaceVertices(int faceIndex) { return _faceVertCountsAndOffsets[2 * faceIndex + 1]; }

        public int getNumFaceEdges(int faceIndex) { return getNumFaceVertices(faceIndex); }
        public int getOffsetOfFaceEdges(int faceIndex) { return getOffsetOfFaceVertices(faceIndex); }

        public int getNumEdgeVertices(int edgeIndex) { return 2; }
        public int getOffsetOfEdgeVertices(int edgeIndex) { return 2 * edgeIndex; }

        public int getNumEdgeFaces(int edgeIndex) { return _edgeFaceCountsAndOffsets[2 * edgeIndex]; }
        public int getOffsetOfEdgeFaces(int edgeIndex) { return _edgeFaceCountsAndOffsets[2 * edgeIndex + 1]; }

        public int getNumVertexFaces(int vertIndex) { return _vertFaceCountsAndOffsets[2 * vertIndex]; }
        public int getOffsetOfVertexFaces(int vertIndex) { return _vertFaceCountsAndOffsets[2 * vertIndex + 1]; }

        public int getNumVertexEdges(int vertIndex) { return _vertEdgeCountsAndOffsets[2 * vertIndex]; }
        public int getOffsetOfVertexEdges(int vertIndex) { return _vertEdgeCountsAndOffsets[2 * vertIndex + 1]; }

        public ConstIndexArray getFaceVertices()
        {
            return new ConstIndexArray(_faceVertIndices.Data, 0, _faceVertIndices.Count);
        }

        public void resizeFaceVertices(int faceIndex, int count)
        {
            int pairIndex = faceIndex * 2;

            _faceVertCountsAndOffsets[pairIndex] = count;
            _faceVertCountsAndOffsets[pairIndex + 1] = (faceIndex == 0) ? 0 :
                    (_faceVertCountsAndOffsets[pairIndex - 2] + _faceVertCountsAndOffsets[pairIndex - 1]);

            _maxValence = Math.Max(_maxValence, count);
        }

        public void resizeEdgeFaces(int edgeIndex, int count)
        {
            int pairIndex = edgeIndex * 2;

            _edgeFaceCountsAndOffsets[pairIndex] = count;
            _edgeFaceCountsAndOffsets[pairIndex + 1] = (edgeIndex == 0) ? 0 :
                    (_edgeFaceCountsAndOffsets[pairIndex - 2] + _edgeFaceCountsAndOffsets[pairIndex - 1]);

            _maxEdgeFaces = Math.Max(_maxEdgeFaces, count);
        }

        public void trimEdgeFaces(int edgeIndex, int count)
        {
            _edgeFaceCountsAndOffsets[edgeIndex * 2] = count;
        }

        public void resizeVertexFaces(int vertIndex, int count)
        {
            int pairIndex = vertIndex * 2;

            _vertFaceCountsAndOffsets[pairIndex] = count;
            _vertFaceCountsAndOffsets[pairIndex + 1] = (vertIndex == 0) ? 0 :
                    (_vertFaceCountsAndOffsets[pairIndex - 2] + _vertFaceCountsAndOffsets[pairIndex - 1]);
        }

        public void trimVertexFaces(int vertIndex, int count)
        {
            _vertFaceCountsAndOffsets[vertIndex * 2] = count;
        }

        public void resizeVertexEdges(int vertIndex, int count)
        {
            int pairIndex = vertIndex * 2;

            _vertEdgeCountsAndOffsets[pairIndex] = count;
            _vertEdgeCountsAndOffsets[pairIndex + 1] = (vertIndex == 0) ? 0 :
                    (_vertEdgeCountsAndOffsets[pairIndex - 2] + _vertEdgeCountsAndOffsets[pairIndex - 1]);

            _maxValence = Math.Max(_maxValence, count);
        }

        public void trimVertexEdges(int vertIndex, int count)
        {
            _vertEdgeCountsAndOffsets[vertIndex * 2] = count;
        }

        public IndexArray shareFaceVertCountsAndOffsets()
        {
            return new IndexArray(_faceVertCountsAndOffsets.Data, 0, _faceVertCountsAndOffsets.Count);
        }

        public static string getTopologyErrorString(TopologyError errCode)
        {
            switch (errCode)
            {
                case TopologyError.TOPOLOGY_MISSING_EDGE_FACES:
                    return "MISSING_EDGE_FACES";
                case TopologyError.TOPOLOGY_MISSING_EDGE_VERTS:
                    return "MISSING_EDGE_VERTS";
                case TopologyError.TOPOLOGY_MISSING_FACE_EDGES:
                    return "MISSING_FACE_EDGES";
                case TopologyError.TOPOLOGY_MISSING_FACE_VERTS:
                    return "MISSING_FACE_VERTS";
                case TopologyError.TOPOLOGY_MISSING_VERT_FACES:
                    return "MISSING_VERT_FACES";
                case TopologyError.TOPOLOGY_MISSING_VERT_EDGES:
                    return "MISSING_VERT_EDGES";

                case TopologyError.TOPOLOGY_FAILED_CORRELATION_EDGE_FACE:
                    return "FAILED_CORRELATION_EDGE_FACE";
                case TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_VERT:
                    return "FAILED_CORRELATION_FACE_VERT";
                case TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_EDGE:
                    return "FAILED_CORRELATION_FACE_EDGE";

                case TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_EDGE:
                    return "FAILED_ORIENTATION_INCIDENT_EDGE";
                case TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACE:
                    return "FAILED_ORIENTATION_INCIDENT_FACE";
                case TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACES_EDGES:
                    return "FAILED_ORIENTATION_INCIDENT_FACES_EDGES";

                case TopologyError.TOPOLOGY_DEGENERATE_EDGE:
                    return "DEGENERATE_EDGE";
                case TopologyError.TOPOLOGY_NON_MANIFOLD_EDGE:
                    return "NON_MANIFOLD_EDGE";

                case TopologyError.TOPOLOGY_INVALID_CREASE_EDGE:
                    return "INVALID_CREASE_EDGE";
                case TopologyError.TOPOLOGY_INVALID_CREASE_VERT:
                    return "INVALID_CREASE_VERT";

                default:
                    System.Diagnostics.Debug.Assert(false);
                    break;
            }
            return null;
        }

        //
        //  Debugging method to validate topology, i.e. verify appropriate symmetry
        //  between the relations, etc.
        //
        public bool validateTopology(ValidationCallback callback = null, object clientData = null)
        {
            //  The C++ REPORT macro:
            Action<TopologyError, string> REPORT = (TopologyError code, string text) =>
            {
                if (callback != null)
                {
                    string errStr = getTopologyErrorString(code);
                    string msg = errStr + " - " + text;
                    callback(code, msg, clientData);
                }
            };

            //
            //  Verify internal topological consistency (eventually a Level method?):
            //      - each face-vert has corresponding vert-face (and child)
            //      - each face-edge has corresponding edge-face
            //      - each edge-vert has corresponding vert-edge (and child)
            //  The above three are enough for most cases, but it is still possible
            //  the latter relation in each above has no correspondent in the former,
            //  so apply the symmetric tests:
            //      - each edge-face has corresponding face-edge
            //      - each vert-face has corresponding face-vert
            //      - each vert-edge has corresponding edge-vert
            //  We are still left with the possibility of duplicate references in
            //  places we don't want them.  Currently a component can exist multiple
            //  times in a component of higher dimension.
            //      - each vert-face <face,child> pair is unique
            //      - each vert-edge <edge,child> pair is unique
            //
            bool returnOnFirstError = true;
            bool isValid = true;

            //  Verify each face-vert has corresponding vert-face and child:
            if ((getNumFaceVerticesTotal() == 0) || (getNumVertexFacesTotal() == 0))
            {
                if (getNumFaceVerticesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_FACE_VERTS, "missing face-verts");
                }
                if (getNumVertexFacesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_VERT_FACES, "missing vert-faces");
                }
                return false;
            }
            for (int fIndex = 0; fIndex < getNumFaces(); ++fIndex)
            {
                ConstIndexArray fVerts = getFaceVertices(fIndex);
                int fVertCount = fVerts.size();

                for (int i = 0; i < fVertCount; ++i)
                {
                    int vIndex = fVerts[i];

                    ConstIndexArray vFaces = getVertexFaces(vIndex);
                    ConstLocalIndexArray vInFace = getVertexFaceLocalIndices(vIndex);

                    bool vertFaceOfFaceExists = false;
                    for (int j = 0; j < vFaces.size(); ++j)
                    {
                        if ((vFaces[j] == fIndex) && (vInFace[j] == i))
                        {
                            vertFaceOfFaceExists = true;
                            break;
                        }
                    }
                    if (!vertFaceOfFaceExists)
                    {
                        REPORT(TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_VERT,
                            string.Format("face {0} correlation of vert {1} failed", fIndex, i));
                        if (returnOnFirstError)
                        {
                            return false;
                        }
                        isValid = false;
                    }
                }
            }

            //  Verify each face-edge has corresponding edge-face:
            if ((getNumEdgeFacesTotal() == 0) || (getNumFaceEdgesTotal() == 0))
            {
                if (getNumEdgeFacesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_EDGE_FACES, "missing edge-faces");
                }
                if (getNumFaceEdgesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_FACE_EDGES, "missing face-edges");
                }
                return false;
            }
            for (int fIndex = 0; fIndex < getNumFaces(); ++fIndex)
            {
                ConstIndexArray fEdges = getFaceEdges(fIndex);
                int fEdgeCount = fEdges.size();

                for (int i = 0; i < fEdgeCount; ++i)
                {
                    int eIndex = fEdges[i];

                    ConstIndexArray eFaces = getEdgeFaces(eIndex);
                    ConstLocalIndexArray eInFace = getEdgeFaceLocalIndices(eIndex);

                    bool edgeFaceOfFaceExists = false;
                    for (int j = 0; j < eFaces.size(); ++j)
                    {
                        if ((eFaces[j] == fIndex) && (eInFace[j] == i))
                        {
                            edgeFaceOfFaceExists = true;
                            break;
                        }
                    }
                    if (!edgeFaceOfFaceExists)
                    {
                        REPORT(TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_EDGE,
                             string.Format("face {0} correlation of edge {1} failed", fIndex, i));
                        if (returnOnFirstError)
                        {
                            return false;
                        }
                        isValid = false;
                    }
                }
            }

            //  Verify each edge-vert has corresponding vert-edge and child:
            if ((getNumEdgeVerticesTotal() == 0) || (getNumVertexEdgesTotal() == 0))
            {
                if (getNumEdgeVerticesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_EDGE_VERTS, "missing edge-verts");
                }
                if (getNumVertexEdgesTotal() == 0)
                {
                    REPORT(TopologyError.TOPOLOGY_MISSING_VERT_EDGES, "missing vert-edges");
                }
                return false;
            }
            for (int eIndex = 0; eIndex < getNumEdges(); ++eIndex)
            {
                ConstIndexArray eVerts = getEdgeVertices(eIndex);

                for (int i = 0; i < 2; ++i)
                {
                    int vIndex = eVerts[i];

                    ConstIndexArray vEdges = getVertexEdges(vIndex);
                    ConstLocalIndexArray vInEdge = getVertexEdgeLocalIndices(vIndex);

                    bool vertEdgeOfEdgeExists = false;
                    for (int j = 0; j < vEdges.size(); ++j)
                    {
                        if ((vEdges[j] == eIndex) && (vInEdge[j] == i))
                        {
                            vertEdgeOfEdgeExists = true;
                            break;
                        }
                    }
                    if (!vertEdgeOfEdgeExists)
                    {
                        REPORT(TopologyError.TOPOLOGY_FAILED_CORRELATION_FACE_VERT,
                            string.Format("edge {0} correlation of vert {1} failed", eIndex, i));
                        if (returnOnFirstError)
                        {
                            return false;
                        }
                        isValid = false;
                    }
                }
            }

            //  Verify that vert-faces and vert-edges are properly ordered and in sync:
            //      - currently this requires the relations exactly match those that we construct from
            //        the ordering method, i.e. we do not allow rotations for interior vertices.
            int[] vFacesOrdered = new int[_maxValence];
            int[] vEdgesOrdered = new int[_maxValence];

            for (int vIndex = 0; vIndex < getNumVertices(); ++vIndex)
            {
                if (_vertTags[vIndex]._incomplete || _vertTags[vIndex]._nonManifold)
                {
                    continue;
                }

                ConstIndexArray vFaces = getVertexFaces(vIndex);
                ConstIndexArray vEdges = getVertexEdges(vIndex);

                if (vFaces.size() > vFacesOrdered.Length)
                {
                    vFacesOrdered = new int[vFaces.size()];
                }
                if (vEdges.size() > vEdgesOrdered.Length)
                {
                    vEdgesOrdered = new int[vEdges.size()];
                }

                if (!orderVertexFacesAndEdges(vIndex, vFacesOrdered, vEdgesOrdered))
                {
                    REPORT(TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACES_EDGES,
                        string.Format("vertex {0} cannot orient incident faces and edges", vIndex));
                    if (returnOnFirstError)
                    {
                        return false;
                    }
                    isValid = false;
                }
                for (int i = 0; i < vFaces.size(); ++i)
                {
                    if (vFaces[i] != vFacesOrdered[i])
                    {
                        REPORT(TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_FACE,
                            string.Format("vertex {0} orientation failure at incident face {1}", vIndex, i));
                        if (returnOnFirstError)
                        {
                            return false;
                        }
                        isValid = false;
                        break;
                    }
                }
                for (int i = 0; i < vEdges.size(); ++i)
                {
                    if (vEdges[i] != vEdgesOrdered[i])
                    {
                        REPORT(TopologyError.TOPOLOGY_FAILED_ORIENTATION_INCIDENT_EDGE,
                            string.Format("vertex {0} orientation failure at incident edge {1}", vIndex, i));
                        if (returnOnFirstError)
                        {
                            return false;
                        }
                        isValid = false;
                        break;
                    }
                }
            }

            //  Verify non-manifold tags are appropriately assigned to edges and vertices:
            //      - note we have to validate orientation of vertex neighbors to do this rigorously
            for (int eIndex = 0; eIndex < getNumEdges(); ++eIndex)
            {
                ETag eTag = _edgeTags[eIndex];
                if (eTag._nonManifold)
                {
                    continue;
                }

                ConstIndexArray eVerts = getEdgeVertices(eIndex);
                if (eVerts[0] == eVerts[1])
                {
                    REPORT(TopologyError.TOPOLOGY_DEGENERATE_EDGE,
                        string.Format("Error in eIndex = {0}:  degenerate edge not tagged marked non-manifold", eIndex));
                    if (returnOnFirstError)
                    {
                        return false;
                    }
                    isValid = false;
                }

                ConstIndexArray eFaces = getEdgeFaces(eIndex);
                if ((eFaces.size() < 1) || (eFaces.size() > 2))
                {
                    REPORT(TopologyError.TOPOLOGY_NON_MANIFOLD_EDGE,
                        string.Format("edge {0} with {1} incident faces not tagged non-manifold", eIndex, eFaces.size()));
                    if (returnOnFirstError)
                    {
                        return false;
                    }
                    isValid = false;
                }
            }
            return isValid;
        }

        //
        //  Helper functions for debugging output -- (printf() in the original)
        //
        private static void printIndexArray<INT_TYPE>(ConstArray<INT_TYPE> array)
        {
            Console.Write("{0} [{1}", array.size(), array[0]);
            for (int i = 1; i < array.size(); ++i)
            {
                Console.Write(" {0}", array[i]);
            }
            Console.Write("]\n");
        }

        private static string ruleString(Crease.Rule rule)
        {
            switch (rule)
            {
                case Crease.Rule.RULE_UNKNOWN: return "<uninitialized>";
                case Crease.Rule.RULE_SMOOTH:  return "Smooth";
                case Crease.Rule.RULE_DART:    return "Dart";
                case Crease.Rule.RULE_CREASE:  return "Crease";
                case Crease.Rule.RULE_CORNER:  return "Corner";
                default:
                    System.Diagnostics.Debug.Assert(false);
                    break;
            }
            return null;
        }

        private static bool isSharpnessEqual(float s1, float s2)
        {
            return (s1 == s2);
        }

        private static string F6(float value)
        {
            return value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
        }

        public void print(Refinement pRefinement = null)
        {
            bool printFaceVerts      = true;
            bool printFaceEdges      = true;
            bool printFaceChildVerts = false;
            bool printFaceTags       = true;

            bool printEdgeVerts      = true;
            bool printEdgeFaces      = true;
            bool printEdgeChildVerts = true;
            bool printEdgeSharpness  = true;
            bool printEdgeTags       = true;

            bool printVertFaces      = true;
            bool printVertEdges      = true;
            bool printVertChildVerts = false;
            bool printVertSharpness  = true;
            bool printVertTags       = true;

            Console.Write("Level (0x{0:x}):\n", System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this));
            Console.Write("  Depth = {0}\n", _depth);

            Console.Write("  Primary component counts:\n");
            Console.Write("    faces = {0}\n", _faceCount);
            Console.Write("    edges = {0}\n", _edgeCount);
            Console.Write("    verts = {0}\n", _vertCount);

            Console.Write("  Topology relation sizes:\n");

            Console.Write("    Face relations:\n");
            Console.Write("      face-vert counts/offset = {0}\n", _faceVertCountsAndOffsets.Count);
            Console.Write("      face-vert indices = {0}\n", _faceVertIndices.Count);
            if (_faceVertIndices.Count != 0)
            {
                for (int i = 0; printFaceVerts && i < getNumFaces(); ++i)
                {
                    Console.Write("        face {0,4} verts:  ", i);
                    printIndexArray<int>(getFaceVertices(i));
                }
            }
            Console.Write("      face-edge indices = {0}\n", _faceEdgeIndices.Count);
            if (_faceEdgeIndices.Count != 0)
            {
                for (int i = 0; printFaceEdges && i < getNumFaces(); ++i)
                {
                    Console.Write("        face {0,4} edges:  ", i);
                    printIndexArray<int>(getFaceEdges(i));
                }
            }
            Console.Write("      face tags = {0}\n", _faceTags.Count);
            for (int i = 0; printFaceTags && i < _faceTags.Count; ++i)
            {
                FTag fTag = _faceTags[i];
                Console.Write("        face {0,4}:", i);
                Console.Write("  hole = {0}", (int)fTag._hole);
                Console.Write("\n");
            }
            if (pRefinement != null)
            {
                Console.Write("      face child-verts = {0}\n", pRefinement._faceChildVertIndex.Count);
                for (int i = 0; printFaceChildVerts && i < pRefinement._faceChildVertIndex.Count; ++i)
                {
                    Console.Write("        face {0,4} child vert:  {1}\n", i, pRefinement._faceChildVertIndex[i]);
                }
            }

            Console.Write("    Edge relations:\n");
            Console.Write("      edge-vert indices = {0}\n", _edgeVertIndices.Count);
            if (_edgeVertIndices.Count != 0)
            {
                for (int i = 0; printEdgeVerts && i < getNumEdges(); ++i)
                {
                    Console.Write("        edge {0,4} verts:  ", i);
                    printIndexArray<int>(getEdgeVertices(i));
                }
            }
            Console.Write("      edge-face counts/offset = {0}\n", _edgeFaceCountsAndOffsets.Count);
            Console.Write("      edge-face indices       = {0}\n", _edgeFaceIndices.Count);
            Console.Write("      edge-face local-indices = {0}\n", _edgeFaceLocalIndices.Count);
            if (_edgeFaceIndices.Count != 0)
            {
                for (int i = 0; printEdgeFaces && i < getNumEdges(); ++i)
                {
                    Console.Write("        edge {0,4} faces:  ", i);
                    printIndexArray<int>(getEdgeFaces(i));

                    Console.Write("             face-edges:  ");
                    printIndexArray<ushort>(getEdgeFaceLocalIndices(i));
                }
            }
            if (pRefinement != null)
            {
                Console.Write("      edge child-verts = {0}\n", pRefinement._edgeChildVertIndex.Count);
                for (int i = 0; printEdgeChildVerts && i < pRefinement._edgeChildVertIndex.Count; ++i)
                {
                    Console.Write("        edge {0,4} child vert:  {1}\n", i, pRefinement._edgeChildVertIndex[i]);
                }
            }
            Console.Write("      edge sharpness = {0}\n", _edgeSharpness.Count);
            for (int i = 0; printEdgeSharpness && i < _edgeSharpness.Count; ++i)
            {
                Console.Write("        edge {0,4} sharpness:  {1}\n", i, F6(_edgeSharpness[i]));
            }
            Console.Write("      edge tags = {0}\n", _edgeTags.Count);
            for (int i = 0; printEdgeTags && i < _edgeTags.Count; ++i)
            {
                ETag eTag = _edgeTags[i];
                Console.Write("        edge {0,4}:", i);
                Console.Write("  boundary = {0}", (int)eTag._boundary);
                Console.Write(", nonManifold = {0}", (int)eTag._nonManifold);
                Console.Write(", semiSharp = {0}", (int)eTag._semiSharp);
                Console.Write(", infSharp = {0}", (int)eTag._infSharp);
                Console.Write("\n");
            }

            Console.Write("    Vert relations:\n");
            Console.Write("      vert-face counts/offset = {0}\n", _vertFaceCountsAndOffsets.Count);
            Console.Write("      vert-face indices       = {0}\n", _vertFaceIndices.Count);
            Console.Write("      vert-face local-indices = {0}\n", _vertFaceLocalIndices.Count);
            if (_vertFaceIndices.Count != 0)
            {
                for (int i = 0; printVertFaces && i < getNumVertices(); ++i)
                {
                    Console.Write("        vert {0,4} faces:  ", i);
                    printIndexArray<int>(getVertexFaces(i));

                    Console.Write("             face-verts:  ");
                    printIndexArray<ushort>(getVertexFaceLocalIndices(i));
                }
            }
            Console.Write("      vert-edge counts/offset = {0}\n", _vertEdgeCountsAndOffsets.Count);
            Console.Write("      vert-edge indices       = {0}\n", _vertEdgeIndices.Count);
            Console.Write("      vert-edge local-indices = {0}\n", _vertEdgeLocalIndices.Count);
            if (_vertEdgeIndices.Count != 0)
            {
                for (int i = 0; printVertEdges && i < getNumVertices(); ++i)
                {
                    Console.Write("        vert {0,4} edges:  ", i);
                    printIndexArray<int>(getVertexEdges(i));

                    Console.Write("             edge-verts:  ");
                    printIndexArray<ushort>(getVertexEdgeLocalIndices(i));
                }
            }
            if (pRefinement != null)
            {
                Console.Write("      vert child-verts = {0}\n", pRefinement._vertChildVertIndex.Count);
                for (int i = 0; printVertChildVerts && i < pRefinement._vertChildVertIndex.Count; ++i)
                {
                    Console.Write("        vert {0,4} child vert:  {1}\n", i, pRefinement._vertChildVertIndex[i]);
                }
            }
            Console.Write("      vert sharpness = {0}\n", _vertSharpness.Count);
            for (int i = 0; printVertSharpness && i < _vertSharpness.Count; ++i)
            {
                Console.Write("        vert {0,4} sharpness:  {1}\n", i, F6(_vertSharpness[i]));
            }
            Console.Write("      vert tags = {0}\n", _vertTags.Count);
            for (int i = 0; printVertTags && i < _vertTags.Count; ++i)
            {
                VTag vTag = _vertTags[i];
                Console.Write("        vert {0,4}:", i);
                Console.Write("  rule = {0}", ruleString((Crease.Rule)(ushort)vTag._rule));
                Console.Write(", boundary = {0}", (int)vTag._boundary);
                Console.Write(", corner = {0}", (int)vTag._corner);
                Console.Write(", xordinary = {0}", (int)vTag._xordinary);
                Console.Write(", nonManifold = {0}", (int)vTag._nonManifold);
                Console.Write(", infSharp = {0}", (int)vTag._infSharp);
                Console.Write(", infSharpEdges = {0}", (int)vTag._infSharpEdges);
                Console.Write(", infSharpCrease = {0}", (int)vTag._infSharpCrease);
                Console.Write(", infIrregular = {0}", (int)vTag._infIrregular);
                Console.Write(", semiSharp = {0}", (int)vTag._semiSharp);
                Console.Write(", semiSharpEdges = {0}", (int)vTag._semiSharpEdges);
                Console.Write("\n");
            }
            Console.Out.Flush();
        }

        //
        //  Methods for retrieving and combining tags:
        //
        public bool doesVertexFVarTopologyMatch(int vIndex, int fvarChannel)
        {
            return getFVarLevel(fvarChannel).valueTopologyMatches(
                     getFVarLevel(fvarChannel).getVertexValueOffset(vIndex));
        }

        public bool doesEdgeFVarTopologyMatch(int eIndex, int fvarChannel)
        {
            return getFVarLevel(fvarChannel).edgeTopologyMatches(eIndex);
        }

        public bool doesFaceFVarTopologyMatch(int fIndex, int fvarChannel)
        {
            return !getFVarLevel(fvarChannel).getFaceCompositeValueTag(fIndex).isMismatch();
        }

        public void getFaceVTags(int fIndex, VTag[] vTags, int fvarChannel = -1)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);
            if (fvarChannel < 0)
            {
                for (int i = 0; i < fVerts.size(); ++i)
                {
                    vTags[i] = getVertexTag(fVerts[i]);
                }
            }
            else
            {
                FVarLevel fvarLevel = getFVarLevel(fvarChannel);
                ConstIndexArray fValues = fvarLevel.getFaceValues(fIndex);
                for (int i = 0; i < fVerts.size(); ++i)
                {
                    int valueIndex = fvarLevel.findVertexValueIndex(fVerts[i], fValues[i]);
                    FVarLevel.ValueTag valueTag = fvarLevel.getValueTag(valueIndex);

                    vTags[i] = valueTag.combineWithLevelVTag(getVertexTag(fVerts[i]));
                }
            }
        }

        public void getFaceETags(int fIndex, ETag[] eTags, int fvarChannel = -1)
        {
            ConstIndexArray fEdges = getFaceEdges(fIndex);
            if (fvarChannel < 0)
            {
                for (int i = 0; i < fEdges.size(); ++i)
                {
                    eTags[i] = getEdgeTag(fEdges[i]);
                }
            }
            else
            {
                FVarLevel fvarLevel = getFVarLevel(fvarChannel);
                for (int i = 0; i < fEdges.size(); ++i)
                {
                    FVarLevel.ETag fvarETag = fvarLevel.getEdgeTag(fEdges[i]);

                    eTags[i] = fvarETag.combineWithLevelETag(getEdgeTag(fEdges[i]));
                }
            }
        }

        public VTag getFaceCompositeVTag(ConstIndexArray fVerts)
        {
            ushort tagBits = _vertTags[fVerts[0]].getBits();
            for (int i = 1; i < fVerts.size(); ++i)
            {
                tagBits |= _vertTags[fVerts[i]].getBits();
            }
            return new VTag(tagBits);
        }

        public VTag getFaceCompositeVTag(int fIndex, int fvarChannel = -1)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);
            if (fvarChannel < 0)
            {
                return getFaceCompositeVTag(fVerts);
            }
            else
            {
                FVarLevel fvarLevel = getFVarLevel(fvarChannel);
                FVarLevel.ValueTag[] fvarTags = new FVarLevel.ValueTag[fVerts.size()];
                fvarLevel.getFaceValueTags(fIndex, fvarTags);

                ushort tagBits = fvarTags[0].combineWithLevelVTag(_vertTags[fVerts[0]]).getBits();
                for (int i = 1; i < fVerts.size(); ++i)
                {
                    tagBits |= fvarTags[i].combineWithLevelVTag(_vertTags[fVerts[i]]).getBits();
                }
                return new VTag(tagBits);
            }
        }

        public VTag getVertexCompositeFVarVTag(int vIndex, int fvarChannel)
        {
            FVarLevel fvarLevel = getFVarLevel(fvarChannel);

            ConstArray<FVarLevel.ValueTag> fvTags = fvarLevel.getVertexValueTags(vIndex);

            VTag vTag = getVertexTag(vIndex);
            if (fvTags[0].isMismatch())
            {
                ushort tagBits = fvTags[0].combineWithLevelVTag(vTag).getBits();
                for (int i = 1; i < fvTags.size(); ++i)
                {
                    tagBits |= fvTags[i].combineWithLevelVTag(vTag).getBits();
                }
                return new VTag(tagBits);
            }
            else
            {
                return vTag;
            }
        }

        //
        //  High-level topology gathering functions -- used mainly in patch construction.  These
        //  may eventually be moved elsewhere, possibly to classes specialized for quad- and tri-
        //  patch identification and construction, but for now somewhere more accessible than the
        //  patch tables factory is preferable.
        //
        //  Note a couple of nuisances...
        //      - debatable whether we should include the face's face-verts in the face functions
        //          - we refer to the result as a "patch" when we do
        //          - otherwise a "ring" of vertices is more appropriate
        //
        private static int fastMod4(int value)
        {
            return (value & 0x3);
        }

        private static int otherOfTwo(ConstIndexArray arrayOfTwo, int value)
        {
            return arrayOfTwo[(value == arrayOfTwo[0]) ? 1 : 0];
        }

        private static readonly int[] rotationSequence = new int[7] { 0, 1, 2, 3, 0, 1, 2 };

        //
        //  Gathering the one-ring of vertices from quads surrounding a vertex:
        //      - the neighborhood of the vertex is assumed to be quad-regular (manifold)
        //
        //  Ordering of resulting vertices:
        //      The surrounding one-ring follows the ordering of the incident faces.  For each
        //  incident quad, the two vertices in CCW order within that quad are added.  If the
        //  vertex is on a boundary, a third vertex on the boundary edge will be contributed from
        //  the last face.
        //
        public int gatherQuadRegularRingAroundVertex(int vIndex, int[] ringPoints, int fvarChannel = -1)
        {
            return gatherQuadRegularRingAroundVertex(vIndex, ringPoints, fvarChannel, 0);
        }

        public int gatherQuadRegularRingAroundVertex(int vIndex, int[] ringPoints, int fvarChannel, int dstOffset)
        {
            Level level = this;

            ConstIndexArray vEdges = level.getVertexEdges(vIndex);

            ConstIndexArray vFaces = level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(vIndex);

            bool isBoundary = (vEdges.size() > vFaces.size());

            int ringIndex = dstOffset;
            for (int i = 0; i < vFaces.size(); ++i)
            {
                //
                //  For every incident quad, we want the two vertices clockwise in each face, i.e.
                //  the vertex at the end of the leading edge and the vertex opposite this one:
                //
                ConstIndexArray fPoints = (fvarChannel < 0)
                                        ? (ConstIndexArray)level.getFaceVertices(vFaces[i])
                                        : (ConstIndexArray)level.getFaceFVarValues(vFaces[i], fvarChannel);

                int vInThisFace = vInFaces[i];

                ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 1)];
                ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 2)];

                if (isBoundary && (i == (vFaces.size() - 1)))
                {
                    ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 3)];
                }
            }
            return ringIndex - dstOffset;
        }

        public int gatherQuadRegularPartialRingAroundVertex(int vIndex, VSpan span, int[] ringPoints, int fvarChannel = -1)
        {
            return gatherQuadRegularPartialRingAroundVertex(vIndex, span, ringPoints, fvarChannel, 0);
        }

        public int gatherQuadRegularPartialRingAroundVertex(int vIndex, VSpan span, int[] ringPoints, int fvarChannel, int dstOffset)
        {
            Level level = this;

            System.Diagnostics.Debug.Assert(!level.isVertexNonManifold(vIndex));

            ConstIndexArray vFaces = level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(vIndex);

            int nFaces = span._numFaces;
            int startFace = span._startFace;

            int ringIndex = dstOffset;
            for (int i = 0; i < nFaces; ++i)
            {
                //
                //  For every incident quad, we want the two vertices clockwise in each face, i.e.
                //  the vertex at the end of the leading edge and the vertex opposite this one:
                //
                int fIncident = (startFace + i) % vFaces.size();

                ConstIndexArray fPoints = (fvarChannel < 0)
                                        ? (ConstIndexArray)level.getFaceVertices(vFaces[fIncident])
                                        : (ConstIndexArray)level.getFaceFVarValues(vFaces[fIncident], fvarChannel);

                int vInThisFace = vInFaces[fIncident];

                ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 1)];
                ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 2)];

                if ((i == nFaces - 1) && !span._periodic)
                {
                    ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 3)];
                }
            }
            return ringIndex - dstOffset;
        }

        //
        //  Gathering the 4 vertices of a quad:
        //
        //        |     |
        //      --0-----3--
        //        |x   x|
        //        |x   x|
        //      --1-----2--
        //        |     |
        //
        public int gatherQuadLinearPatchPoints(int thisFace, int[] patchPoints, int rotation = 0, int fvarChannel = -1)
        {
            return gatherQuadLinearPatchPoints(thisFace, patchPoints, rotation, fvarChannel, 0);
        }

        public int gatherQuadLinearPatchPoints(int thisFace, int[] patchPoints, int rotation, int fvarChannel, int dstOffset)
        {
            Level level = this;

            System.Diagnostics.Debug.Assert((0 <= rotation) && (rotation < 4));
            int rotatedVerts = rotation;  //  offset into rotationSequence

            ConstIndexArray facePoints = (fvarChannel < 0) ?
                                         (ConstIndexArray)level.getFaceVertices(thisFace) :
                                         (ConstIndexArray)level.getFaceFVarValues(thisFace, fvarChannel);

            patchPoints[dstOffset + 0] = facePoints[rotationSequence[rotatedVerts + 0]];
            patchPoints[dstOffset + 1] = facePoints[rotationSequence[rotatedVerts + 1]];
            patchPoints[dstOffset + 2] = facePoints[rotationSequence[rotatedVerts + 2]];
            patchPoints[dstOffset + 3] = facePoints[rotationSequence[rotatedVerts + 3]];

            return 4;
        }

        //
        //  Gathering the 16 vertices of a quad-regular interior patch:
        //      - the neighborhood of the face is assumed to be quad-regular
        //
        //  Ordering of resulting vertices:
        //      It was debatable whether to include the vertices of the original face for a complete
        //  "patch" or just the surrounding ring -- clearly we ended up with a function for the entire
        //  patch, but that may change.
        //      The latter ring of vertices around the face (potentially returned on its own) was
        //  oriented with respect to the face.  The ring of 12 vertices is gathered as 4 groups of 3
        //  vertices -- one for each corner vertex, and each group forming the quad opposite each
        //  corner vertex when combined with that corner vertex.  The four vertices of the face begin
        //  the patch.
        //
        //         |     |     |     |
        //      ---5-----4-----15----14---
        //         |     |     |     |
        //         |     |     |     |
        //      ---6-----0-----3-----13---
        //         |     |x   x|     |
        //         |     |x   x|     |
        //      ---7-----1-----2-----12---
        //         |     |     |     |
        //         |     |     |     |
        //      ---8-----9-----10----11---
        //         |     |     |     |
        //
        public int gatherQuadRegularInteriorPatchPoints(int thisFace, int[] patchPoints, int rotation = 0, int fvarChannel = -1)
        {
            return gatherQuadRegularInteriorPatchPoints(thisFace, patchPoints, rotation, fvarChannel, 0);
        }

        public int gatherQuadRegularInteriorPatchPoints(int thisFace, int[] patchPoints, int rotation, int fvarChannel, int dstOffset)
        {
            Level level = this;

            System.Diagnostics.Debug.Assert((0 <= rotation) && (rotation < 4));
            int rotatedVerts = rotation;  //  offset into rotationSequence

            ConstIndexArray thisFaceVerts = level.getFaceVertices(thisFace);

            ConstIndexArray facePoints = (fvarChannel < 0) ? thisFaceVerts :
                                         (ConstIndexArray)level.getFaceFVarValues(thisFace, fvarChannel);

            patchPoints[dstOffset + 0] = facePoints[rotationSequence[rotatedVerts + 0]];
            patchPoints[dstOffset + 1] = facePoints[rotationSequence[rotatedVerts + 1]];
            patchPoints[dstOffset + 2] = facePoints[rotationSequence[rotatedVerts + 2]];
            patchPoints[dstOffset + 3] = facePoints[rotationSequence[rotatedVerts + 3]];

            //
            //  For each of the four corner vertices, there is a face diagonally opposite
            //  the given/central face.  Each of these faces contains three points of the
            //  entire ring of points around that given/central face.
            //
            int pointIndex = 4;
            for (int i = 0; i < 4; ++i)
            {
                int v = thisFaceVerts[rotationSequence[rotatedVerts + i]];

                ConstIndexArray vFaces = level.getVertexFaces(v);
                ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(v);

                int thisFaceInVFaces = vFaces.FindIndexIn4Tuple(thisFace);
                int intFaceInVFaces = fastMod4(thisFaceInVFaces + 2);

                int intFace = vFaces[intFaceInVFaces];
                int vInIntFace = vInFaces[intFaceInVFaces];

                facePoints = (fvarChannel < 0) ? (ConstIndexArray)level.getFaceVertices(intFace) :
                             (ConstIndexArray)level.getFaceFVarValues(intFace, fvarChannel);

                patchPoints[dstOffset + pointIndex++] = facePoints[fastMod4(vInIntFace + 1)];
                patchPoints[dstOffset + pointIndex++] = facePoints[fastMod4(vInIntFace + 2)];
                patchPoints[dstOffset + pointIndex++] = facePoints[fastMod4(vInIntFace + 3)];
            }
            System.Diagnostics.Debug.Assert(pointIndex == 16);
            return 16;
        }

        //
        //  Gathering the 12 vertices of a quad-regular boundary patch:
        //      - the neighborhood of the face is assumed to be quad-regular
        //      - the single edge of the face that lies on the boundary is specified
        //      - only one edge of the face is a boundary edge
        //
        //  Ordering of resulting vertices:
        //      It was debatable whether to include the vertices of the original face for a complete
        //  "patch" or just the surrounding ring -- clearly we ended up with a function for the entire
        //  patch, but that may change.
        //      The latter ring of vertices around the face (potentially returned on its own) was
        //  oriented beginning from the leading CCW boundary edge and ending at the trailing edge.
        //  The four vertices of the face begin the patch and are oriented similarly to this outer
        //  ring -- forming an inner ring that begins and ends in the same manner.
        //
        //      ---4-----0-----3-----11---
        //         |     |x   x|     |
        //         |     |x   x|     |
        //      ---5-----1-----2-----10---
        //         |     |v0 v1|     |
        //         |     |     |     |
        //      ---6-----7-----8-----9----
        //         |     |     |     |
        //
        public int gatherQuadRegularBoundaryPatchPoints(int face, int[] patchPoints, int boundaryEdgeInFace, int fvarChannel = -1)
        {
            return gatherQuadRegularBoundaryPatchPoints(face, patchPoints, boundaryEdgeInFace, fvarChannel, 0);
        }

        public int gatherQuadRegularBoundaryPatchPoints(int face, int[] patchPoints, int boundaryEdgeInFace, int fvarChannel, int dstOffset)
        {
            Level level = this;

            int interiorEdgeInFace = fastMod4(boundaryEdgeInFace + 2);

            //
            //  V0 and V1 are the two interior vertices (opposite the boundary edge) around
            //  which we will gather most of the ring:
            //
            int intV0InFace = interiorEdgeInFace;
            int intV1InFace = fastMod4(interiorEdgeInFace + 1);

            ConstIndexArray faceVerts = level.getFaceVertices(face);

            int v0 = faceVerts[intV0InFace];
            int v1 = faceVerts[intV1InFace];

            ConstIndexArray v0Faces = level.getVertexFaces(v0);
            ConstIndexArray v1Faces = level.getVertexFaces(v1);

            ConstLocalIndexArray v0InFaces = level.getVertexFaceLocalIndices(v0);
            ConstLocalIndexArray v1InFaces = level.getVertexFaceLocalIndices(v1);

            int boundaryFaceInV0Faces = -1;
            int boundaryFaceInV1Faces = -1;
            for (int i = 0; i < 4; ++i)
            {
                if (face == v0Faces[i]) boundaryFaceInV0Faces = i;
                if (face == v1Faces[i]) boundaryFaceInV1Faces = i;
            }
            System.Diagnostics.Debug.Assert((boundaryFaceInV0Faces >= 0) && (boundaryFaceInV1Faces >= 0));

            //  Identify the four faces of interest -- previous to and opposite V0 and
            //  opposite and next from V1 -- relative to V0 and V1:
            int prevFaceInV0Faces = fastMod4(boundaryFaceInV0Faces + 1);
            int intFaceInV0Faces = fastMod4(boundaryFaceInV0Faces + 2);
            int intFaceInV1Faces = fastMod4(boundaryFaceInV1Faces + 2);
            int nextFaceInV1Faces = fastMod4(boundaryFaceInV1Faces + 3);

            //  Identify the indices of the four faces:
            int prevFace = v0Faces[prevFaceInV0Faces];
            int intV0Face = v0Faces[intFaceInV0Faces];
            int intV1Face = v1Faces[intFaceInV1Faces];
            int nextFace = v1Faces[nextFaceInV1Faces];

            //  Identify V0 and V1 relative to these four faces:
            ushort v0InPrevFace = v0InFaces[prevFaceInV0Faces];
            ushort v0InIntFace = v0InFaces[intFaceInV0Faces];
            ushort v1InIntFace = v1InFaces[intFaceInV1Faces];
            ushort v1InNextFace = v1InFaces[nextFaceInV1Faces];

            //
            //  Now that all faces of interest have been found, identify the point
            //  indices within each face (i.e. the vertex or fvar-value index arrays)
            //  and copy them into the patch points:
            //
            ConstIndexArray thisFacePoints,
                            prevFacePoints,
                            intV0FacePoints,
                            intV1FacePoints,
                            nextFacePoints;

            if (fvarChannel < 0)
            {
                thisFacePoints = faceVerts;
                prevFacePoints = level.getFaceVertices(prevFace);
                intV0FacePoints = level.getFaceVertices(intV0Face);
                intV1FacePoints = level.getFaceVertices(intV1Face);
                nextFacePoints = level.getFaceVertices(nextFace);
            }
            else
            {
                thisFacePoints = level.getFaceFVarValues(face, fvarChannel);
                prevFacePoints = level.getFaceFVarValues(prevFace, fvarChannel);
                intV0FacePoints = level.getFaceFVarValues(intV0Face, fvarChannel);
                intV1FacePoints = level.getFaceFVarValues(intV1Face, fvarChannel);
                nextFacePoints = level.getFaceFVarValues(nextFace, fvarChannel);
            }

            patchPoints[dstOffset + 0] = thisFacePoints[fastMod4(boundaryEdgeInFace + 1)];
            patchPoints[dstOffset + 1] = thisFacePoints[fastMod4(boundaryEdgeInFace + 2)];
            patchPoints[dstOffset + 2] = thisFacePoints[fastMod4(boundaryEdgeInFace + 3)];
            patchPoints[dstOffset + 3] = thisFacePoints[boundaryEdgeInFace];

            patchPoints[dstOffset + 4] = prevFacePoints[fastMod4(v0InPrevFace + 2)];

            patchPoints[dstOffset + 5] = intV0FacePoints[fastMod4(v0InIntFace + 1)];
            patchPoints[dstOffset + 6] = intV0FacePoints[fastMod4(v0InIntFace + 2)];
            patchPoints[dstOffset + 7] = intV0FacePoints[fastMod4(v0InIntFace + 3)];

            patchPoints[dstOffset + 8] = intV1FacePoints[fastMod4(v1InIntFace + 1)];
            patchPoints[dstOffset + 9] = intV1FacePoints[fastMod4(v1InIntFace + 2)];
            patchPoints[dstOffset + 10] = intV1FacePoints[fastMod4(v1InIntFace + 3)];

            patchPoints[dstOffset + 11] = nextFacePoints[fastMod4(v1InNextFace + 2)];

            return 12;
        }

        //
        //  Gathering the 9 vertices of a quad-regular corner patch:
        //      - the neighborhood of the face is assumed to be quad-regular
        //      - the single corner vertex is specified
        //      - only one vertex of the face is a corner
        //
        //  Ordering of resulting vertices:
        //      It was debatable whether to include the vertices of the original face for a complete
        //  "patch" or just the surrounding ring -- clearly we ended up with a function for the entire
        //  patch, but that may change.
        //      Like the boundary case, the latter ring of vertices around the face was oriented
        //  beginning from the leading CCW boundary edge and ending at the trailing edge.  The four
        //  face vertices begin the patch, and begin with the corner vertex.
        //
        //      0-----3-----8---
        //      |x   x|     |
        //      |x   x|     |
        //      1-----2-----7---
        //      |     |     |
        //      |     |     |
        //      4-----5-----6---
        //      |     |     |
        //
        public int gatherQuadRegularCornerPatchPoints(int face, int[] patchPoints, int cornerVertInFace, int fvarChannel = -1)
        {
            return gatherQuadRegularCornerPatchPoints(face, patchPoints, cornerVertInFace, fvarChannel, 0);
        }

        public int gatherQuadRegularCornerPatchPoints(int face, int[] patchPoints, int cornerVertInFace, int fvarChannel, int dstOffset)
        {
            Level level = this;

            int interiorFaceVert = fastMod4(cornerVertInFace + 2);

            ConstIndexArray faceVerts = level.getFaceVertices(face);
            int intVert = faceVerts[interiorFaceVert];

            ConstIndexArray intVertFaces = level.getVertexFaces(intVert);
            ConstLocalIndexArray intVertInFaces = level.getVertexFaceLocalIndices(intVert);

            int cornerFaceInIntVertFaces = -1;
            for (int i = 0; i < intVertFaces.size(); ++i)
            {
                if (face == intVertFaces[i])
                {
                    cornerFaceInIntVertFaces = i;
                    break;
                }
            }
            System.Diagnostics.Debug.Assert(cornerFaceInIntVertFaces >= 0);

            //  Identify the three faces relative to the interior vertex:
            int prevFaceInIntVertFaces = fastMod4(cornerFaceInIntVertFaces + 1);
            int intFaceInIntVertFaces = fastMod4(cornerFaceInIntVertFaces + 2);
            int nextFaceInIntVertFaces = fastMod4(cornerFaceInIntVertFaces + 3);

            //  Identify the indices of the three other faces:
            int prevFace = intVertFaces[prevFaceInIntVertFaces];
            int intFace = intVertFaces[intFaceInIntVertFaces];
            int nextFace = intVertFaces[nextFaceInIntVertFaces];

            //  Identify the interior vertex relative to these three faces:
            ushort intVertInPrevFace = intVertInFaces[prevFaceInIntVertFaces];
            ushort intVertInIntFace = intVertInFaces[intFaceInIntVertFaces];
            ushort intVertInNextFace = intVertInFaces[nextFaceInIntVertFaces];

            //
            //  Now that all faces of interest have been found, identify the point
            //  indices within each face (i.e. the vertex or fvar-value index arrays)
            //  and copy them into the patch points:
            //
            ConstIndexArray thisFacePoints,
                            prevFacePoints,
                            intFacePoints,
                            nextFacePoints;

            if (fvarChannel < 0)
            {
                thisFacePoints = faceVerts;
                prevFacePoints = level.getFaceVertices(prevFace);
                intFacePoints = level.getFaceVertices(intFace);
                nextFacePoints = level.getFaceVertices(nextFace);
            }
            else
            {
                thisFacePoints = level.getFaceFVarValues(face, fvarChannel);
                prevFacePoints = level.getFaceFVarValues(prevFace, fvarChannel);
                intFacePoints = level.getFaceFVarValues(intFace, fvarChannel);
                nextFacePoints = level.getFaceFVarValues(nextFace, fvarChannel);
            }

            patchPoints[dstOffset + 0] = thisFacePoints[cornerVertInFace];
            patchPoints[dstOffset + 1] = thisFacePoints[fastMod4(cornerVertInFace + 1)];
            patchPoints[dstOffset + 2] = thisFacePoints[fastMod4(cornerVertInFace + 2)];
            patchPoints[dstOffset + 3] = thisFacePoints[fastMod4(cornerVertInFace + 3)];

            patchPoints[dstOffset + 4] = prevFacePoints[fastMod4(intVertInPrevFace + 2)];

            patchPoints[dstOffset + 5] = intFacePoints[fastMod4(intVertInIntFace + 1)];
            patchPoints[dstOffset + 6] = intFacePoints[fastMod4(intVertInIntFace + 2)];
            patchPoints[dstOffset + 7] = intFacePoints[fastMod4(intVertInIntFace + 3)];

            patchPoints[dstOffset + 8] = nextFacePoints[fastMod4(intVertInNextFace + 2)];

            return 9;
        }

        //
        //  Gathering the 12 vertices of a tri-regular interior patch:
        //      - the neighborhood of the face is assumed to be tri-regular
        //
        //  Ordering of resulting vertices:
        //      The three vertices of the triangle begin the sequence, followed by counter-clockwise
        //  traversal of the outer ring of vertices -- beginning with the vertex incident V0 such
        //  that the ring is symmetric about the triangle.
        //
        //                   3           11
        //                   X - - - - - X
        //                 /   \       /   \
        //               /       \ 0 /       \
        //          4  X - - - - - X - - - - - X 10
        //           /   \       / * \       /   \
        //         /       \   / * * * \   /       \
        //    5  X - - - - - X - - - - - X - - - - - X  9
        //         \       / 1 \       / 2 \       /
        //           \   /       \   /       \   /
        //             X - - - - - X - - - - - X
        //             6           7           8
        //
        public int gatherTriRegularInteriorPatchPoints(int fIndex, int[] points, int rotation = 0)
        {
            return gatherTriRegularInteriorPatchPoints(fIndex, points, rotation, 0);
        }

        public int gatherTriRegularInteriorPatchPoints(int fIndex, int[] points, int rotation, int dstOffset)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);
            ConstIndexArray fEdges = getFaceEdges(fIndex);

            int index0 = 0;
            int index1 = 1;
            int index2 = 2;
            if (rotation != 0)
            {
                index0 = rotation % 3;
                index1 = (rotation + 1) % 3;
                index2 = (rotation + 2) % 3;
            }

            int v0 = fVerts[index0];
            int v1 = fVerts[index1];
            int v2 = fVerts[index2];

            ConstIndexArray v0Edges = getVertexEdges(v0);
            ConstIndexArray v1Edges = getVertexEdges(v1);
            ConstIndexArray v2Edges = getVertexEdges(v2);

            int e0InV0Edges = v0Edges.FindIndex(fEdges[index0]);
            int e1InV1Edges = v1Edges.FindIndex(fEdges[index1]);
            int e2InV2Edges = v2Edges.FindIndex(fEdges[index2]);

            points[dstOffset + 0] = v0;
            points[dstOffset + 1] = v1;
            points[dstOffset + 2] = v2;

            points[dstOffset + 11] = otherOfTwo(getEdgeVertices(v0Edges[(e0InV0Edges + 3) % 6]), v0);
            points[dstOffset + 3] = otherOfTwo(getEdgeVertices(v0Edges[(e0InV0Edges + 4) % 6]), v0);
            points[dstOffset + 4] = otherOfTwo(getEdgeVertices(v0Edges[(e0InV0Edges + 5) % 6]), v0);

            points[dstOffset + 5] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 3) % 6]), v1);
            points[dstOffset + 6] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 4) % 6]), v1);
            points[dstOffset + 7] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 5) % 6]), v1);

            points[dstOffset + 8] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 3) % 6]), v2);
            points[dstOffset + 9] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 4) % 6]), v2);
            points[dstOffset + 10] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 5) % 6]), v2);

            return 12;
        }

        //
        //  Gathering the 9 vertices of a tri-regular "boundary edge" patch:
        //      - the neighborhood of the face is assumed to be tri-regular
        //      - referred to as "boundary edge" as the boundary occurs on the edge of the triangle
        //
        //  Boundary edge:
        //
        //                   6           5
        //                   X - - - - - X
        //                 /   \       /   \
        //               /       \ 2 /       \
        //          7  X - - - - - X - - - - - X  4
        //           /   \       / * \       /   \
        //         /       \   / * * * \   /       \
        //    8  X - - - - - X - - - - - X - - - - - X  3
        //                   0           1
        //
        public int gatherTriRegularBoundaryEdgePatchPoints(int fIndex, int[] points, int boundaryFaceEdge)
        {
            return gatherTriRegularBoundaryEdgePatchPoints(fIndex, points, boundaryFaceEdge, 0);
        }

        public int gatherTriRegularBoundaryEdgePatchPoints(int fIndex, int[] points, int boundaryFaceEdge, int dstOffset)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);

            int v0 = fVerts[boundaryFaceEdge];
            int v1 = fVerts[(boundaryFaceEdge + 1) % 3];
            int v2 = fVerts[(boundaryFaceEdge + 2) % 3];

            ConstIndexArray v0Edges = getVertexEdges(v0);
            ConstIndexArray v1Edges = getVertexEdges(v1);
            ConstIndexArray v2Edges = getVertexEdges(v2);

            int e1InV2Edges = v2Edges.FindIndex(v1Edges[2]);

            points[dstOffset + 0] = v0;
            points[dstOffset + 1] = v1;
            points[dstOffset + 2] = v2;

            points[dstOffset + 3] = otherOfTwo(getEdgeVertices(v1Edges[0]), v1);

            points[dstOffset + 4] = otherOfTwo(getEdgeVertices(v2Edges[(e1InV2Edges + 1) % 6]), v2);
            points[dstOffset + 5] = otherOfTwo(getEdgeVertices(v2Edges[(e1InV2Edges + 2) % 6]), v2);
            points[dstOffset + 6] = otherOfTwo(getEdgeVertices(v2Edges[(e1InV2Edges + 3) % 6]), v2);
            points[dstOffset + 7] = otherOfTwo(getEdgeVertices(v2Edges[(e1InV2Edges + 4) % 6]), v2);

            points[dstOffset + 8] = otherOfTwo(getEdgeVertices(v0Edges[3]), v0);

            return 9;
        }

        //
        //  Gathering the 10 vertices of a tri-regular "boundary vertex" patch:
        //      - the neighborhood of the face is assumed to be tri-regular
        //      - referred to as "boundary vertex" as the boundary occurs on the vertex of the triangle
        //
        //  Boundary vertex:
        //
        //                         0
        //          3  X - - - - - X - - - - - X  9
        //           /   \       / * \       /   \
        //         /       \   / * * * \   /       \
        //    4  X - - - - - X - - - - - X - - - - - X  8
        //         \       / 1 \       / 2 \       /
        //           \   /       \   /       \   /
        //             X - - - - - X - - - - - X
        //             5           6           7
        //
        public int gatherTriRegularBoundaryVertexPatchPoints(int fIndex, int[] points, int boundaryFaceVert)
        {
            return gatherTriRegularBoundaryVertexPatchPoints(fIndex, points, boundaryFaceVert, 0);
        }

        public int gatherTriRegularBoundaryVertexPatchPoints(int fIndex, int[] points, int boundaryFaceVert, int dstOffset)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);
            ConstIndexArray fEdges = getFaceEdges(fIndex);

            int v0 = fVerts[boundaryFaceVert];
            int v1 = fVerts[(boundaryFaceVert + 1) % 3];
            int v2 = fVerts[(boundaryFaceVert + 2) % 3];

            int e1 = fEdges[boundaryFaceVert];
            int e2 = fEdges[(boundaryFaceVert + 2) % 3];

            ConstIndexArray v1Edges = getVertexEdges(v1);
            ConstIndexArray v2Edges = getVertexEdges(v2);

            int e1InV1Edges = v1Edges.FindIndex(e1);
            int e2InV2Edges = v2Edges.FindIndex(e2);

            points[dstOffset + 0] = v0;
            points[dstOffset + 1] = v1;
            points[dstOffset + 2] = v2;

            points[dstOffset + 3] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 1) % 6]), v1);
            points[dstOffset + 4] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 2) % 6]), v1);
            points[dstOffset + 5] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 3) % 6]), v1);
            points[dstOffset + 6] = otherOfTwo(getEdgeVertices(v1Edges[(e1InV1Edges + 4) % 6]), v1);

            points[dstOffset + 7] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 3) % 6]), v2);
            points[dstOffset + 8] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 4) % 6]), v2);
            points[dstOffset + 9] = otherOfTwo(getEdgeVertices(v2Edges[(e2InV2Edges + 5) % 6]), v2);

            return 10;
        }

        //
        //  Gathering the 6 vertices of a tri-regular "corner vertex" patch:
        //      - the neighborhood of the face is assumed to be tri-regular
        //      - referred to as "corner vertex" to disambiguate it from another corner case
        //          - another boundary case shares the edge with the corner triangle
        //
        //  Corner vertex:
        //
        //                         0
        //                         X
        //                       / * \
        //                     / * * * \
        //                   X - - - - - X
        //                 / 1 \       / 2 \
        //               /       \   /       \
        //             X - - - - - X - - - - - X
        //             3           4           5
        //
        public int gatherTriRegularCornerVertexPatchPoints(int fIndex, int[] points, int cornerFaceVert)
        {
            return gatherTriRegularCornerVertexPatchPoints(fIndex, points, cornerFaceVert, 0);
        }

        public int gatherTriRegularCornerVertexPatchPoints(int fIndex, int[] points, int cornerFaceVert, int dstOffset)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);

            int v0 = fVerts[cornerFaceVert];
            int v1 = fVerts[(cornerFaceVert + 1) % 3];
            int v2 = fVerts[(cornerFaceVert + 2) % 3];

            ConstIndexArray v1Edges = getVertexEdges(v1);
            ConstIndexArray v2Edges = getVertexEdges(v2);

            points[dstOffset + 0] = v0;
            points[dstOffset + 1] = v1;
            points[dstOffset + 2] = v2;

            points[dstOffset + 3] = otherOfTwo(getEdgeVertices(v1Edges[0]), v1);
            points[dstOffset + 4] = otherOfTwo(getEdgeVertices(v1Edges[1]), v1);
            points[dstOffset + 5] = otherOfTwo(getEdgeVertices(v2Edges[3]), v2);

            return 6;
        }

        //
        //  Gathering the 8 vertices of a tri-regular "corner edge" patch:
        //      - the neighborhood of the face is assumed to be tri-regular
        //      - referred to as "corner edge" to disambiguate it from the vertex corner case
        //          - this faces shares the edge with a corner triangle
        //
        //  Corner edge:
        //
        //                   6           5
        //                   X - - - - - X
        //                 /   \       /   \
        //               /       \ 2 /       \
        //          7  X - - - - - X - - - - - X  4
        //               \       / * \       /
        //                 \   / * * * \   /
        //                   X - - - - - X
        //                   0 \       / 1
        //                       \   /
        //                         X
        //                         3
        //
        public int gatherTriRegularCornerEdgePatchPoints(int fIndex, int[] points, int cornerFaceEdge)
        {
            return gatherTriRegularCornerEdgePatchPoints(fIndex, points, cornerFaceEdge, 0);
        }

        public int gatherTriRegularCornerEdgePatchPoints(int fIndex, int[] points, int cornerFaceEdge, int dstOffset)
        {
            ConstIndexArray fVerts = getFaceVertices(fIndex);

            int v0 = fVerts[cornerFaceEdge];
            int v1 = fVerts[(cornerFaceEdge + 1) % 3];
            int v2 = fVerts[(cornerFaceEdge + 2) % 3];

            ConstIndexArray v0Edges = getVertexEdges(v0);
            ConstIndexArray v1Edges = getVertexEdges(v1);

            points[dstOffset + 0] = v0;
            points[dstOffset + 1] = v1;
            points[dstOffset + 2] = v2;

            points[dstOffset + 3] = otherOfTwo(getEdgeVertices(v1Edges[3]), v1);
            points[dstOffset + 4] = otherOfTwo(getEdgeVertices(v1Edges[0]), v1);
            points[dstOffset + 7] = otherOfTwo(getEdgeVertices(v0Edges[3]), v0);

            ConstIndexArray v4Edges = getVertexEdges(points[dstOffset + 4]);
            ConstIndexArray v7Edges = getVertexEdges(points[dstOffset + 7]);

            points[dstOffset + 5] = otherOfTwo(getEdgeVertices(v4Edges[v4Edges.size() - 3]), v1);
            points[dstOffset + 6] = otherOfTwo(getEdgeVertices(v7Edges[2]), v1);

            return 8;
        }

        public bool isSingleCreasePatch(int face)
        {
            float sharpnessOut;
            int rotationOut;
            return isSingleCreasePatch(face, out sharpnessOut, out rotationOut);
        }

        public bool isSingleCreasePatch(int face, out float sharpnessOut, out int sharpEdgeInFaceOut)
        {
            sharpnessOut = 0.0f;
            sharpEdgeInFaceOut = 0;

            //  Using the composite tag for all face vertices, first make sure that some
            //  face-vertices are Crease vertices, and quickly reject this case based on the
            //  presence of other features.  Ultimately we want a regular interior face with
            //  two (adjacent) Crease vertics and two Smooth vertices.  This first test
            //  quickly ensures a regular interior face with some number of Crease vertices
            //  and any remaining as Smooth.
            //
            ConstIndexArray fVerts = getFaceVertices(face);

            VTag allCornersTag = getFaceCompositeVTag(fVerts);
            int allCornersRule = (ushort)allCornersTag._rule;
            if ((allCornersRule & (int)Crease.Rule.RULE_CREASE) == 0 ||
                (allCornersRule & (int)Crease.Rule.RULE_CORNER) != 0 ||
                (allCornersRule & (int)Crease.Rule.RULE_DART) != 0 ||
                allCornersTag._boundary ||
                allCornersTag._xordinary ||
                allCornersTag._nonManifold)
            {
                return false;
            }

            //  Identify the crease vertices in a 4-bit mask and use it as an index to
            //  verify that we have exactly two adjacent crease vertices while identifying
            //  the edge between them -- reject any case not returning a valid edge.
            //
            int creaseCornerMask = (((ushort)getVertexTag(fVerts[0])._rule == (ushort)Crease.Rule.RULE_CREASE ? 1 : 0) << 0) |
                                   (((ushort)getVertexTag(fVerts[1])._rule == (ushort)Crease.Rule.RULE_CREASE ? 1 : 0) << 1) |
                                   (((ushort)getVertexTag(fVerts[2])._rule == (ushort)Crease.Rule.RULE_CREASE ? 1 : 0) << 2) |
                                   (((ushort)getVertexTag(fVerts[3])._rule == (ushort)Crease.Rule.RULE_CREASE ? 1 : 0) << 3);
            int[] sharpEdgeFromCreaseMask = new int[16] { -1, -1, -1,  0, -1, -1,  1, -1,
                                                          -1,  3, -1, -1,  2, -1, -1, -1 };

            int sharpEdgeInFace = sharpEdgeFromCreaseMask[creaseCornerMask];
            if (sharpEdgeInFace < 0)
            {
                return false;
            }

            //  Reject if the crease at the two crease vertices A and B is not regular, i.e.
            //  any pair of opposing edges does not have the same sharpness value (one pair
            //  sharp, the other smooth).  The resulting two regular creases must be "colinear"
            //  (sharing the edge between them, and so its common sharpness value) otherwise
            //  we would have more than two crease vertices.
            //
            ConstIndexArray vAEdges = getVertexEdges(fVerts[sharpEdgeInFace]);
            ConstIndexArray vBEdges = getVertexEdges(fVerts[fastMod4(sharpEdgeInFace + 1)]);

            if (!isSharpnessEqual(getEdgeSharpness(vAEdges[0]), getEdgeSharpness(vAEdges[2])) ||
                !isSharpnessEqual(getEdgeSharpness(vAEdges[1]), getEdgeSharpness(vAEdges[3])) ||
                !isSharpnessEqual(getEdgeSharpness(vBEdges[0]), getEdgeSharpness(vBEdges[2])) ||
                !isSharpnessEqual(getEdgeSharpness(vBEdges[1]), getEdgeSharpness(vBEdges[3])))
            {
                return false;
            }
            sharpnessOut = getEdgeSharpness(getFaceEdges(face)[sharpEdgeInFace]);
            sharpEdgeInFaceOut = sharpEdgeInFace;
            return true;
        }

        //
        //  What follows is an internal class and methods to complete all
        //  topological relations when only the face-vertex relations are defined.
        //
        //  This is an internal helper class to manage the assembly of the topological relations
        //  that do not have a predictable size, i.e. faces-per-edge, faces-per-vertex and
        //  edges-per-vertex.  Level manages these with two vectors:
        //
        //      - a vector of integer pairs for the "counts" and "offsets"
        //      - a vector of incident members accessed by the "offset" of each
        //
        //  The "dynamic relation" allocates the latter vector of members based on a typical
        //  number of members per component, e.g. we expect valence 4 vertices in a typical
        //  quad-mesh, and so an "expected" number might be 6 to accommodate a few x-ordinary
        //  vertices.  The member vector is allocated with this number per component and the
        //  counts and offsets initialized to refer to them -- but with the counts set to 0.
        //  The count will be incremented as members are identified and entered, and if any
        //  component "overflows" the expected number of members, the members are moved to a
        //  separate vector in an std::map for the component.
        //
        //  Once all incident members have been added, the main vector is compressed and may
        //  need to merge entries from the map in the process.
        //
        private class DynamicRelation
        {
            public int _compCount;
            public int _memberCountPerComp;

            public IndexVector _countsAndOffsets;
            public IndexVector _regIndices;

            //  typedef std::map<Index, IndexVector> IrregIndexMap;
            public Dictionary<int, IndexVector> _irregIndices = new Dictionary<int, IndexVector>();

            public DynamicRelation(IndexVector countAndOffsets, IndexVector indices, int membersPerComp)
            {
                _compCount = 0;
                _memberCountPerComp = membersPerComp;
                _countsAndOffsets = countAndOffsets;
                _regIndices = indices;

                _compCount = (int)_countsAndOffsets.Count / 2;

                for (int i = 0; i < _compCount; ++i)
                {
                    _countsAndOffsets[2 * i] = 0;
                    _countsAndOffsets[2 * i + 1] = i * _memberCountPerComp;
                }
                _regIndices.Resize(_compCount * _memberCountPerComp);
            }

            //  Methods dealing with the members for each component:
            public IndexArray getCompMembers(int compIndex)
            {
                int count = _countsAndOffsets[2 * compIndex];
                if (count > _memberCountPerComp)
                {
                    IndexVector irregMembers = _irregIndices[compIndex];
                    return new IndexArray(irregMembers.Data, 0, (int)irregMembers.Count);
                }
                else
                {
                    int offset = _countsAndOffsets[2 * compIndex + 1];
                    return new IndexArray(_regIndices.Data, offset, count);
                }
            }

            public void appendCompMember(int compIndex, int memberValue)
            {
                int count = _countsAndOffsets[2 * compIndex];
                int offset = _countsAndOffsets[2 * compIndex + 1];

                if (count < _memberCountPerComp)
                {
                    _regIndices[offset + count] = memberValue;
                }
                else
                {
                    //  std::map::operator[] creates a default entry when absent:
                    IndexVector irregMembers;
                    if (!_irregIndices.TryGetValue(compIndex, out irregMembers))
                    {
                        irregMembers = new IndexVector();
                        _irregIndices[compIndex] = irregMembers;
                    }

                    if (count > _memberCountPerComp)
                    {
                        irregMembers.PushBack(memberValue);
                    }
                    else
                    {
                        irregMembers.Resize(_memberCountPerComp + 1);
                        System.Array.Copy(_regIndices.Data, offset, irregMembers.Data, 0, _memberCountPerComp);
                        irregMembers[_memberCountPerComp] = memberValue;
                    }
                }
                _countsAndOffsets[2 * compIndex]++;
            }

            //  Methods dealing with the components:
            public void appendComponent()
            {
                _countsAndOffsets.PushBack(0);
                _countsAndOffsets.PushBack(_compCount * _memberCountPerComp);

                ++_compCount;
                _regIndices.Resize(_compCount * _memberCountPerComp);
            }

            public int compressMemberIndices()
            {
                if (_irregIndices.Count == 0)
                {
                    int memberCount = _countsAndOffsets[0];
                    int memberMax = _countsAndOffsets[0];
                    for (int i = 1; i < _compCount; ++i)
                    {
                        int count = _countsAndOffsets[2 * i];
                        int offset = _countsAndOffsets[2 * i + 1];

                        System.Array.Copy(_regIndices.Data, offset, _regIndices.Data, memberCount, count);

                        _countsAndOffsets[2 * i + 1] = memberCount;
                        memberCount += count;
                        memberMax = Math.Max(memberMax, count);
                    }
                    _regIndices.Resize(memberCount);
                    return memberMax;
                }
                else
                {
                    //  Assign new offsets-per-component while determining if we can trivially compress in place:
                    bool cannotBeCompressedInPlace = false;

                    int memberCount = _countsAndOffsets[0];
                    for (int i = 1; i < _compCount; ++i)
                    {
                        _countsAndOffsets[2 * i + 1] = memberCount;

                        cannotBeCompressedInPlace |= (memberCount > (_memberCountPerComp * i));

                        memberCount += _countsAndOffsets[2 * i];
                    }
                    cannotBeCompressedInPlace |= (memberCount > (_memberCountPerComp * _compCount));

                    //  Copy members into the original or temporary vector accordingly:
                    IndexVector tmpIndices = new IndexVector();
                    if (cannotBeCompressedInPlace)
                    {
                        tmpIndices.Resize(memberCount);
                    }
                    IndexVector dstIndices = cannotBeCompressedInPlace ? tmpIndices : _regIndices;

                    int memberMax = _memberCountPerComp;
                    for (int i = 0; i < _compCount; ++i)
                    {
                        int count = _countsAndOffsets[2 * i];

                        int dstOffset = _countsAndOffsets[2 * i + 1];
                        int[] srcMembers;
                        int srcOffset;

                        if (count <= _memberCountPerComp)
                        {
                            srcMembers = _regIndices.Data;
                            srcOffset = i * _memberCountPerComp;
                        }
                        else
                        {
                            srcMembers = _irregIndices[i].Data;
                            srcOffset = 0;
                            memberMax = Math.Max(memberMax, count);
                        }
                        System.Array.Copy(srcMembers, srcOffset, dstIndices.Data, dstOffset, count);
                    }
                    if (cannotBeCompressedInPlace)
                    {
                        _regIndices.Swap(tmpIndices);
                    }
                    else
                    {
                        _regIndices.Resize(memberCount);
                    }
                    return memberMax;
                }
            }
        }

        //
        //  Methods to populate the missing topology relations of the Level:
        //
        public int findEdge(int v0Index, int v1Index, ConstIndexArray v0Edges)
        {
            if (v0Index != v1Index)
            {
                for (int j = 0; j < v0Edges.size(); ++j)
                {
                    ConstIndexArray eVerts = this.getEdgeVertices(v0Edges[j]);
                    if ((eVerts[0] == v1Index) || (eVerts[1] == v1Index))
                    {
                        return v0Edges[j];
                    }
                }
            }
            else
            {
                for (int j = 0; j < v0Edges.size(); ++j)
                {
                    ConstIndexArray eVerts = this.getEdgeVertices(v0Edges[j]);
                    if (eVerts[0] == eVerts[1])
                    {
                        return v0Edges[j];
                    }
                }
            }
            return Types.INDEX_INVALID;
        }

        public bool completeTopologyFromFaceVertices()
        {
            //
            //  It's assumed (a pre-condition) that face-vertices have been fully specified and that we
            //  are to construct the remaining relations:  including the edge list.  We may want to
            //  support the existence of the edge list too in future:
            //
            int vCount = this.getNumVertices();
            int fCount = this.getNumFaces();
            int eCount = this.getNumEdges();
            System.Diagnostics.Debug.Assert((vCount > 0) && (fCount > 0) && (eCount == 0));

            //  May be unnecessary depending on how the vertices and faces were defined, but worth a
            //  call to ensure all data related to verts and faces is available -- this will be a
            //  harmless call if all has been taken care of.
            //
            //  Remember to resize edges similarly after the edge list has been assembled...
            this.resizeVertices(vCount);
            this.resizeFaces(fCount);
            this.resizeEdges(0);

            //
            //  Resize face-edges to match face-verts and reserve for edges based on an estimate:
            //
            this._faceEdgeIndices.Resize(this.getNumFaceVerticesTotal());

            int eCountEstimate = (vCount << 1);

            this._edgeVertIndices.Reserve(eCountEstimate * 2);
            this._edgeFaceIndices.Reserve(eCountEstimate * 2);

            this._edgeFaceCountsAndOffsets.Reserve(eCountEstimate * 2);

            //
            //  Create the dynamic relations to be populated (edge-faces will remain empty as reserved
            //  above since there are currently no edges) and iterate through the faces to do so:
            //
            const int avgSize = 6;

            DynamicRelation dynEdgeFaces = new DynamicRelation(this._edgeFaceCountsAndOffsets, this._edgeFaceIndices, 2);
            DynamicRelation dynVertFaces = new DynamicRelation(this._vertFaceCountsAndOffsets, this._vertFaceIndices, avgSize);
            DynamicRelation dynVertEdges = new DynamicRelation(this._vertEdgeCountsAndOffsets, this._vertEdgeIndices, avgSize);

            //  Inspect each edge created and identify those that are non-manifold as we go:
            IndexVector nonManifoldEdges = new IndexVector();

            for (int fIndex = 0; fIndex < fCount; ++fIndex)
            {
                IndexArray fVerts = this.getFaceVertices(fIndex);
                IndexArray fEdges = this.getFaceEdges(fIndex);

                for (int i = 0; i < fVerts.size(); ++i)
                {
                    int v0Index = fVerts[i];
                    int v1Index = fVerts[(i + 1) % fVerts.size()];

                    //
                    //  If not degenerate, search for a previous occurrence of this edge [v0,v1]
                    //  in v0's incident edge members.  Otherwise, set the edge index as invalid
                    //  to trigger creation of a new/unique instance of the degenerate edge:
                    //
                    int eIndex;
                    if (v0Index != v1Index)
                    {
                        eIndex = this.findEdge(v0Index, v1Index, dynVertEdges.getCompMembers(v0Index));
                    }
                    else
                    {
                        eIndex = Types.INDEX_INVALID;
                        nonManifoldEdges.PushBack(this._edgeCount);
                    }

                    //
                    //  If the edge already exists, see if it is non-manifold, i.e. it has already been
                    //  added to two faces, or this face has the edge in the same orientation as the
                    //  first face (indicating opposite winding orders between the two faces).
                    //
                    //  Otherwise, create a new edge, append the new vertex pair [v0,v1] and update
                    //  the incidence relations for the edge and its end vertices and this face.
                    //
                    //  Regardless of whether or not the edge was new, update the edge-faces, the
                    //  face-edges and the vertex-faces for this vertex.
                    //
                    if (Types.IndexIsValid(eIndex))
                    {
                        IndexArray eFaces = dynEdgeFaces.getCompMembers(eIndex);
                        if (eFaces[eFaces.size() - 1] == fIndex)
                        {
                            //  If the edge already occurs in this face, create a new instance:
                            nonManifoldEdges.PushBack(eIndex);
                            nonManifoldEdges.PushBack(this._edgeCount);
                            eIndex = Types.INDEX_INVALID;
                        }
                        else if (eFaces.size() > 1)
                        {
                            nonManifoldEdges.PushBack(eIndex);
                        }
                        else if (v0Index == this.getEdgeVertices(eIndex)[0])
                        {
                            nonManifoldEdges.PushBack(eIndex);
                        }
                    }
                    if (!Types.IndexIsValid(eIndex))
                    {
                        eIndex = (int)this._edgeCount;

                        this._edgeCount++;
                        this._edgeVertIndices.PushBack(v0Index);
                        this._edgeVertIndices.PushBack(v1Index);

                        dynEdgeFaces.appendComponent();

                        dynVertEdges.appendCompMember(v0Index, eIndex);
                        dynVertEdges.appendCompMember(v1Index, eIndex);
                    }

                    dynEdgeFaces.appendCompMember(eIndex, fIndex);
                    dynVertFaces.appendCompMember(v0Index, fIndex);

                    fEdges[i] = eIndex;
                }
            }

            //
            //  Compress the incident member vectors while determining the maximum for each.
            //  Use these to set maximum relation count members and to test for valence or
            //  other incident member overflow:  max edge-faces is simple, but for max-valence,
            //  remember it was first initialized with the maximum of face-verts, so use its
            //  existing value -- and some non-manifold cases can have #faces > #edges, so be
            //  sure to consider both.
            //
            int maxEdgeFaces = dynEdgeFaces.compressMemberIndices();
            int maxVertFaces = dynVertFaces.compressMemberIndices();
            int maxVertEdges = dynVertEdges.compressMemberIndices();

            _maxEdgeFaces = maxEdgeFaces;

            System.Diagnostics.Debug.Assert(_maxValence > 0);
            _maxValence = Math.Max(maxVertFaces, _maxValence);
            _maxValence = Math.Max(maxVertEdges, _maxValence);

            //  If max-edge-faces too large, max-valence must also be, so just need the one:
            if (_maxValence > Types.VALENCE_LIMIT)
            {
                return false;
            }

            //
            //  At this point all incident members are associated with each component.  We still
            //  need to populate the "local indices" for each and orient manifold components in
            //  counter-clockwise order.  First tag non-manifold edges and their incident
            //  vertices so that we can trivially skip orienting these -- though some vertices
            //  will be determined non-manifold as a result of a failure to orient them (and
            //  will be marked accordingly when so detected).
            //
            //  Finally, the local indices are assigned.  This is trivial for manifold components
            //  as if component V is in component F, V will only occur once in F.  For non-manifold
            //  cases V may occur multiple times in F -- we rely on such instances being successive
            //  based on their original assignment above, which simplifies the task.
            //
            //  First resize edges to the new count to ensure anything related to edges is created:
            eCount = this.getNumEdges();
            this.resizeEdges(eCount);

            for (int i = 0; i < (int)nonManifoldEdges.Count; ++i)
            {
                int eIndex = nonManifoldEdges[i];

                _edgeTags[eIndex]._nonManifold = true;

                IndexArray eVerts = getEdgeVertices(eIndex);
                _vertTags[eVerts[0]]._nonManifold = true;
                _vertTags[eVerts[1]]._nonManifold = true;
            }

            orientIncidentComponents();

            populateLocalIndices();

            return true;
        }

        public void populateLocalIndices()
        {
            //
            //  We have three sets of local indices -- edge-faces, vert-faces and vert-edges:
            //
            int eCount = this.getNumEdges();
            int vCount = this.getNumVertices();

            this._vertFaceLocalIndices.Resize(this._vertFaceIndices.Count);
            this._vertEdgeLocalIndices.Resize(this._vertEdgeIndices.Count);
            this._edgeFaceLocalIndices.Resize(this._edgeFaceIndices.Count);

            for (int vIndex = 0; vIndex < vCount; ++vIndex)
            {
                IndexArray vFaces = this.getVertexFaces(vIndex);
                LocalIndexArray vInFaces = this.getVertexFaceLocalIndices(vIndex);

                //
                //  We keep track of the last face during the iteration to detect when two
                //  (or more) successive faces are the same -- indicating a degenerate edge
                //  or other non-manifold situation.  If so, we continue to search from the
                //  point of the last face's local index:
                //
                int vFaceLast = Types.INDEX_INVALID;
                for (int i = 0; i < vFaces.size(); ++i)
                {
                    IndexArray fVerts = this.getFaceVertices(vFaces[i]);

                    int vStart = (vFaces[i] == vFaceLast) ? ((int)vInFaces[i - 1] + 1) : 0;

                    //  std::find(fVerts.begin() + vStart, fVerts.end(), vIndex) - fVerts.begin()
                    int vInFaceIndex = vStart;
                    while ((vInFaceIndex < fVerts.size()) && (fVerts[vInFaceIndex] != vIndex))
                    {
                        ++vInFaceIndex;
                    }
                    vInFaces[i] = (ushort)vInFaceIndex;

                    vFaceLast = vFaces[i];
                }
            }

            for (int vIndex = 0; vIndex < vCount; ++vIndex)
            {
                IndexArray vEdges = this.getVertexEdges(vIndex);
                LocalIndexArray vInEdges = this.getVertexEdgeLocalIndices(vIndex);

                for (int i = 0; i < vEdges.size(); ++i)
                {
                    IndexArray eVerts = this.getEdgeVertices(vEdges[i]);

                    //
                    //  For degenerate edges, the first occurrence of the edge (which
                    //  are presumed successive) will get local index 0, the second 1.
                    //
                    if (eVerts[0] != eVerts[1])
                    {
                        vInEdges[i] = (ushort)((vIndex == eVerts[1]) ? 1 : 0);
                    }
                    else
                    {
                        vInEdges[i] = (ushort)(((i != 0) && (vEdges[i] == vEdges[i - 1])) ? 1 : 0);
                    }
                }
                _maxValence = Math.Max(_maxValence, vEdges.size());
            }

            for (int eIndex = 0; eIndex < eCount; ++eIndex)
            {
                IndexArray eFaces = this.getEdgeFaces(eIndex);
                LocalIndexArray eInFaces = this.getEdgeFaceLocalIndices(eIndex);

                //
                //  We keep track of the last face during the iteration to detect when two
                //  (or more) successive faces are the same -- indicating a degenerate edge
                //  or other non-manifold situation.  If so, we continue to search from the
                //  point of the last face's local index:
                //
                int eFaceLast = Types.INDEX_INVALID;
                for (int i = 0; i < eFaces.size(); ++i)
                {
                    IndexArray fEdges = this.getFaceEdges(eFaces[i]);

                    int eStart = (eFaces[i] == eFaceLast) ? ((int)eInFaces[i - 1] + 1) : 0;

                    int eInFaceIndex = eStart;
                    while ((eInFaceIndex < fEdges.size()) && (fEdges[eInFaceIndex] != eIndex))
                    {
                        ++eInFaceIndex;
                    }
                    eInFaces[i] = (ushort)eInFaceIndex;

                    eFaceLast = eFaces[i];
                }
            }
        }

        public void orientIncidentComponents()
        {
            int vCount = getNumVertices();

            for (int vIndex = 0; vIndex < vCount; ++vIndex)
            {
                ref VTag vTag = ref _vertTags[vIndex];
                if (!vTag._nonManifold)
                {
                    if (!orderVertexFacesAndEdges(vIndex))
                    {
                        vTag._nonManifold = true;
                    }
                }
            }
        }

        private static int findInArray(ConstIndexArray array, int value)
        {
            int i = 0;
            while ((i < array.size()) && (array[i] != value))
            {
                ++i;
            }
            return i;
        }

        //  Output arrays are separate here (rather than two ranges of one buffer in C++) and must be
        //  large enough to hold the number of incident faces and edges respectively:
        public bool orderVertexFacesAndEdges(int vIndex, int[] vFacesOrdered, int[] vEdgesOrdered)
        {
            ConstIndexArray vEdges = this.getVertexEdges(vIndex);
            ConstIndexArray vFaces = this.getVertexFaces(vIndex);

            int fCount = vFaces.size();
            int eCount = vEdges.size();

            if ((fCount == 0) || (eCount < 2) || ((eCount - fCount) > 1))
            {
                return false;
            }

            //
            //  Note we have already eliminated the possibility of incident degenerate edges
            //  and other bad edges earlier -- marking its vertices non-manifold as a result
            //  and explicitly avoiding this method:
            //
            int fStart = Types.INDEX_INVALID;
            int eStart = Types.INDEX_INVALID;
            int fvStart = 0;

            if (eCount == fCount)
            {
                //  Interior case -- start with the first face

                fStart = vFaces[0];
                fvStart = findInArray(this.getFaceVertices(fStart), vIndex);
                eStart = this.getFaceEdges(fStart)[fvStart];
            }
            else
            {
                //  Boundary case -- start with (identify) the leading of two boundary edges:

                for (int i = 0; i < eCount; ++i)
                {
                    ConstIndexArray eFaces = this.getEdgeFaces(vEdges[i]);
                    if (eFaces.size() == 1)
                    {
                        eStart = vEdges[i];
                        fStart = eFaces[0];
                        fvStart = findInArray(this.getFaceVertices(fStart), vIndex);

                        //  Singular edge -- look for forward edge to this vertex:
                        if (eStart == (this.getFaceEdges(fStart)[fvStart]))
                        {
                            break;
                        }
                    }
                }
            }

            //
            //  We have identified a starting face, face-vert and leading edge from
            //  which to walk counter clockwise to identify manifold neighbors.  If
            //  this vertex is really locally manifold, we will end up back at the
            //  starting edge or at the other singular edge of a boundary:
            //
            int eCountOrdered = 1;
            int fCountOrdered = 1;

            vFacesOrdered[0] = fStart;
            vEdgesOrdered[0] = eStart;

            int eFirst = eStart;

            while (eCountOrdered < eCount)
            {
                //
                //  Find the next edge, i.e. the one counter-clockwise to the last:
                //
                ConstIndexArray fVerts = this.getFaceVertices(fStart);
                ConstIndexArray fEdges = this.getFaceEdges(fStart);

                int feStart = fvStart;
                int feNext = (feStart != 0) ? (feStart - 1) : (fVerts.size() - 1);
                int eNext = fEdges[feNext];

                //  Two non-manifold situations detected:
                //      - two subsequent edges the same, i.e. a "repeated edge" in a face
                //      - back at the start before all edges processed
                if ((eNext == eStart) || (eNext == eFirst))
                {
                    return false;
                }

                //
                //  Add the next edge and if more faces to visit (not at the end of
                //  a boundary) look to its opposite face:
                //
                vEdgesOrdered[eCountOrdered++] = eNext;

                if (fCountOrdered < fCount)
                {
                    ConstIndexArray eFaces = this.getEdgeFaces(eNext);

                    if (eFaces.size() == 0)
                    {
                        return false;
                    }
                    if ((eFaces.size() == 1) && (eFaces[0] == fStart))
                    {
                        return false;
                    }

                    fStart = eFaces[(eFaces[0] == fStart) ? 1 : 0];
                    fvStart = findInArray(this.getFaceEdges(fStart), eNext);

                    vFacesOrdered[fCountOrdered++] = fStart;
                }
                eStart = eNext;
            }
            System.Diagnostics.Debug.Assert(eCountOrdered == eCount);
            System.Diagnostics.Debug.Assert(fCountOrdered == fCount);
            return true;
        }

        public bool orderVertexFacesAndEdges(int vIndex)
        {
            IndexArray vFaces = this.getVertexFaces(vIndex);
            IndexArray vEdges = this.getVertexEdges(vIndex);

            int[] vFacesOrdered = new int[vFaces.size()];
            int[] vEdgesOrdered = new int[vEdges.size()];

            if (orderVertexFacesAndEdges(vIndex, vFacesOrdered, vEdgesOrdered))
            {
                System.Array.Copy(vFacesOrdered, 0, vFaces.Data, vFaces.begin(), vFaces.size());
                System.Array.Copy(vEdgesOrdered, 0, vEdges.Data, vEdges.begin(), vEdges.size());
                return true;
            }
            return false;
        }

        public bool testVertexNonManifoldCrease(int vIndex)
        {
            //
            //  Identify the two non-manifold edges for closer inspection -- the
            //  two edges must have more than two incident faces and the same
            //  number of incident faces. All other edges are manifold interior:
            //
            int[] nonManEdges = new int[2] { -1, -1 };

            ConstIndexArray vEdges = getVertexEdges(vIndex);
            for (int i = 0; i < vEdges.size(); ++i)
            {
                if (isEdgeNonManifold(vEdges[i]))
                {
                    if (nonManEdges[1] >= 0)
                    {
                        return false;
                    }
                    nonManEdges[(nonManEdges[0] >= 0) ? 1 : 0] = vEdges[i];
                }
                else if (getEdgeFaces(vEdges[i]).size() != 2)
                {
                    return false;
                }
            }
            if (getEdgeFaces(nonManEdges[0]).size() != getEdgeFaces(nonManEdges[1]).size())
            {
                return false;
            }

            //
            //  For each of the two non-manifold edges, inspect their incident faces
            //  to confirm that the manifold subset connected to each is delimited by
            //  the two edges (not one):
            //
            int numFacesTraversed = 0;

            for (int i = 0; i < 2; ++i)
            {
                int eIndex = nonManEdges[i];
                int eStart = nonManEdges[(i != 0) ? 1 : 0];
                int eEnd = nonManEdges[(i == 0) ? 1 : 0];

                ConstIndexArray eFaces = getEdgeFaces(eIndex);
                ConstLocalIndexArray eInFace = getEdgeFaceLocalIndices(eIndex);

                //
                //  For each face incident the two non-manifold edges, identify
                //  manifold subset of faces associated with each. First test
                //  the orientation of the edge in the starting face -- skipping
                //  this face if the edge is reversed as it should be at the end
                //  of a subset starting from the other crease edge:
                //
                for (int j = 0; j < eFaces.size(); ++j)
                {
                    int fStart = eFaces[j];

                    ConstIndexArray fVerts = getFaceVertices(fStart);
                    if (fVerts[eInFace[j]] != vIndex)
                    {
                        continue;
                    }

                    //
                    //  For each successive leading edge of a manifold sequence of
                    //  faces, identify the face following that edge (if not the
                    //  first) and edge that precedes it in the face -- continuing
                    //  until the ending edge is reached:
                    //
                    for (int eNext = eStart, fNext = fStart; eNext != eEnd; )
                    {
                        if (eNext != eStart)
                        {
                            ConstIndexArray fPair = getEdgeFaces(eNext);
                            fNext = fPair[(fPair[0] == fNext) ? 1 : 0];
                        }
                        numFacesTraversed++;

                        ConstIndexArray fEdges = getFaceEdges(fNext);
                        int iNext = fEdges.FindIndex(eNext);

                        eNext = fEdges[(iNext != 0) ? (iNext - 1) : (fEdges.size() - 1)];
                        if (eNext == eStart)
                        {
                            return false;
                        }
                    }
                }
            }
            return (numFacesTraversed == getVertexFaces(vIndex).size());
        }

        //
        //  In development -- methods for accessing face-varying data channels...
        //
        public int createFVarChannel(int fvarValueCount, Options fvarOptions)
        {
            FVarLevel fvarLevel = new FVarLevel(this);

            fvarLevel.setOptions(fvarOptions);
            fvarLevel.resizeValues(fvarValueCount);
            fvarLevel.resizeComponents();

            _fvarChannels.PushBack(fvarLevel);
            return (int)_fvarChannels.Count - 1;
        }

        public void destroyFVarChannel(int channel)
        {
            //  C++ deletes the FVarLevel and erases it from the vector:
            for (int i = channel; i < _fvarChannels.Count - 1; ++i)
            {
                _fvarChannels[i] = _fvarChannels[i + 1];
            }
            _fvarChannels.PopBack();
        }

        public void completeFVarChannelTopology(int channel, int regBoundaryValence)
        {
            _fvarChannels[channel].completeTopologyFromFaceValues(regBoundaryValence);
        }
    }
}
