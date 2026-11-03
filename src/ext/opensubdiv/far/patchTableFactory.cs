//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 far/patchTableFactory.h and far/patchTableFactory.cpp
//
//  Conventions used here (see PORTING.md):
//    - C++ pointers into PatchTable arrays (iptr, pptr, ...) are int offsets into the backing arrays
//    - Index const sourcePoints[] + Index patchPoints[] arguments are (int[] array, int offset) pairs
//    - the C++ templates over REAL are generic methods constrained to unmanaged, INumber<REAL>
//    - PatchTableBuilder and its helper classes hold only managed state, so the C++ destructors
//      (which only delete members) are not ported as IDisposable
//
using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;

using IndexArray = OpenSubdiv.Vtr.Array<int>;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using IndexVector = OpenSubdiv.Vtr.StdVector<int>;
using PatchParamArray = OpenSubdiv.Vtr.Array<OpenSubdiv.Far.PatchParam>;

namespace OpenSubdiv.Far
{
    ///
    /// \brief Factory for constructing a PatchTable from a TopologyRefiner
    ///
    public class PatchTableFactory
    {
        /// \brief Public options for the PatchTable factory
        public struct Options
        {
            /// \brief Choices for approximating irregular patches (end-caps)
            public enum EndCapType
            {
                ENDCAP_NONE = 0,        ///< unspecified
                ENDCAP_BILINEAR_BASIS,  ///< use linear patches (simple quads or tris)
                ENDCAP_BSPLINE_BASIS,   ///< use BSpline-like patches (same patch type as regular)
                ENDCAP_GREGORY_BASIS,   ///< use Gregory patches (highest quality, recommended default)
                ENDCAP_LEGACY_GREGORY   ///< legacy option for 2.x style Gregory patches (Catmark only)
            }

            public const EndCapType ENDCAP_NONE = EndCapType.ENDCAP_NONE;
            public const EndCapType ENDCAP_BILINEAR_BASIS = EndCapType.ENDCAP_BILINEAR_BASIS;
            public const EndCapType ENDCAP_BSPLINE_BASIS = EndCapType.ENDCAP_BSPLINE_BASIS;
            public const EndCapType ENDCAP_GREGORY_BASIS = EndCapType.ENDCAP_GREGORY_BASIS;
            public const EndCapType ENDCAP_LEGACY_GREGORY = EndCapType.ENDCAP_LEGACY_GREGORY;

            //  Note: C# structs need the explicit constructors below to get the C++ defaults;
            //  default(Options) is all zero.
            public Options() : this(10)
            {
            }

            public Options(uint maxIsolation)
            {
                generateAllLevels = false;
                includeBaseLevelIndices = true;
                includeFVarBaseLevelIndices = false;
                triangulateQuads = false;
                useSingleCreasePatch = false;
                useInfSharpPatch = false;
                maxIsolationLevel = maxIsolation & 0xf;
                endCapType = (uint)EndCapType.ENDCAP_GREGORY_BASIS;
                shareEndCapPatchPoints = true;
                generateVaryingTables = true;
                generateVaryingLocalPoints = true;
                generateFVarTables = false;
                patchPrecisionDouble = false;
                fvarPatchPrecisionDouble = false;
                generateFVarLegacyLinearPatches = true;
                generateLegacySharpCornerPatches = true;
                numFVarChannels = -1;
                fvarChannelIndices = null;
            }

            /// \brief Get endcap patch type
            public EndCapType GetEndCapType()
            {
                return (EndCapType)endCapType;
            }

            /// \brief Set endcap patch type
            public void SetEndCapType(EndCapType e)
            {
                endCapType = (uint)e & 0x7;
            }

            /// \brief Set maximum isolation level
            public void SetMaxIsolationLevel(uint level)
            {
                maxIsolationLevel = level & 0xf;
            }

            /// \brief Set precision of vertex patches
            public void SetPatchPrecision<REAL>()
            {
                patchPrecisionDouble = (typeof(REAL) == typeof(double));
            }

            /// \brief Set precision of face-varying patches
            public void SetFVarPatchPrecision<REAL>()
            {
                fvarPatchPrecisionDouble = (typeof(REAL) == typeof(double));
            }

            /// \brief Determine adaptive refinement options to match this patch table options
            public TopologyRefiner.AdaptiveOptions GetRefineAdaptiveOptions()
            {
                TopologyRefiner.AdaptiveOptions adaptiveOptions =
                    new TopologyRefiner.AdaptiveOptions((int)maxIsolationLevel);

                adaptiveOptions.useInfSharpPatch = useInfSharpPatch;
                adaptiveOptions.useSingleCreasePatch = useSingleCreasePatch;
                adaptiveOptions.considerFVarChannels = generateFVarTables &&
                                                       !generateFVarLegacyLinearPatches;
                return adaptiveOptions;
            }

            public bool generateAllLevels;            ///< Generate levels from 'firstLevel' to 'maxLevel' (Uniform mode only)
            public bool includeBaseLevelIndices;      ///< Include base level in patch point indices (Uniform mode only)
            public bool includeFVarBaseLevelIndices;  ///< Include base level in face-varying patch point indices (Uniform mode only)
            public bool triangulateQuads;             ///< Triangulate 'QUADS' primitives (Uniform mode only)

            public bool useSingleCreasePatch;         ///< Use single crease patch
            public bool useInfSharpPatch;             ///< Use infinitely-sharp patch
            public uint maxIsolationLevel;            ///< Cap adaptive feature isolation to the given level (max. 10) (4 bits)

            public uint endCapType;                   ///< EndCapType (3 bits)
            public bool shareEndCapPatchPoints;       ///< Share endcap patch points among adjacent endcap patches.

            public bool generateVaryingTables;        ///< Generate varying patch tables
            public bool generateVaryingLocalPoints;   ///< Generate local points with varying patches
            public bool generateFVarTables;           ///< Generate face-varying patch tables
            public bool patchPrecisionDouble;         ///< Generate double-precision stencils for vertex patches
            public bool fvarPatchPrecisionDouble;     ///< Generate double-precision stencils for face-varying patches
            public bool generateFVarLegacyLinearPatches;  ///< Generate all linear face-varying patches (legacy)
            public bool generateLegacySharpCornerPatches; ///< Generate sharp regular patches at smooth corners (legacy)

            public int numFVarChannels;               ///< Number of channel indices and interpolation modes passed
            public int[] fvarChannelIndices;          ///< List containing the indices of the channels selected for the factory
        }

        /// \brief Instantiates a PatchTable from a client-provided TopologyRefiner.
        public static PatchTable Create(TopologyRefiner refiner)
        {
            return Create(refiner, new Options(), new ConstIndexArray(null, 0, 0));
        }

        public static PatchTable Create(TopologyRefiner refiner, Options options)
        {
            return Create(refiner, options, new ConstIndexArray(null, 0, 0));
        }

        public static PatchTable Create(TopologyRefiner refiner,
                                        Options options,
                                        ConstIndexArray selectedFaces)
        {
            PatchTableBuilder builder = new PatchTableBuilder(refiner, options, selectedFaces);

            if (builder.UniformPolygonsSpecified())
            {
                builder.BuildUniformPolygons();
            }
            else
            {
                builder.BuildPatches();
            }
            return builder.GetPatchTable();
        }

        //
        //  Implementation of PatchTableFactory::PatchFaceTag -- unintentionally
        //  exposed to the public interface, no longer internally used and so
        //  planned for deprecation
        //
        public struct PatchFaceTag
        {
            //  bitfield widths of the C++ source: 1, 1, 4, 4, 2, 3, 3, 1
            public uint _hasPatch;
            public uint _isRegular;
            public uint _transitionMask;
            public uint _boundaryMask;
            public uint _boundaryIndex;
            public uint _boundaryCount;
            public uint _hasBoundaryEdge;
            public uint _isSingleCrease;

            public void clear()
            {
                this = default(PatchFaceTag);
            }

            public void assignTransitionPropertiesFromEdgeMask(int tMask)
            {
                _transitionMask = (uint)tMask;
            }

            public void assignBoundaryPropertiesFromEdgeMask(int eMask)
            {
                int[] edgeMaskToCount =
                    new int[16] { 0, 1, 1, 2, 1, -1, 2, -1, 1, 2, -1, -1, 2, -1, -1, -1 };
                int[] edgeMaskToIndex =
                    new int[16] { -1, 0, 1, 1, 2, -1, 2, -1, 3, 0, -1, -1, 3, -1, -1, -1 };

                System.Diagnostics.Debug.Assert(edgeMaskToCount[eMask] != -1);
                System.Diagnostics.Debug.Assert(edgeMaskToIndex[eMask] != -1);

                _boundaryMask = (uint)eMask;
                _hasBoundaryEdge = (eMask > 0) ? 1u : 0u;
                _boundaryCount = (uint)edgeMaskToCount[eMask];
                _boundaryIndex = (uint)edgeMaskToIndex[eMask];
            }

            public void assignBoundaryPropertiesFromVertexMask(int vMask)
            {
                // This is only intended to support the case of a single boundary vertex
                // with no boundary edges, which can only occur with an irregular vertex
                int[] singleBitVertexMaskToCount =
                    new int[16] { 0, 1, 1, -1, 1, -1, -1, -1, 1, -1, -1, -1, -1, -1, -1, -1 };
                int[] singleBitVertexMaskToIndex =
                    new int[16] { 0, 0, 1, -1, 2, -1, -1, -1, 3, -1, -1, -1, -1, -1, -1, -1 };

                System.Diagnostics.Debug.Assert(_hasBoundaryEdge == 0);
                System.Diagnostics.Debug.Assert(singleBitVertexMaskToCount[vMask] != -1);
                System.Diagnostics.Debug.Assert(singleBitVertexMaskToIndex[vMask] != -1);

                _boundaryMask = (uint)vMask;
                _boundaryCount = (uint)singleBitVertexMaskToCount[vMask];
                _boundaryIndex = (uint)singleBitVertexMaskToIndex[vMask];
            }
        }

        //  typedef std::vector<PatchFaceTag> PatchTagVector;  (unused: StdVector<PatchFaceTag>)
    }

    //
    //  The main PatchTableBuilder class with context
    //
    //  Helper class aggregating transient contextual data structures during the
    //  creation of a patch table.  This helps keeping the factory class stateless.
    //
    public class PatchTableBuilder
    {
        //
        //  Helpers for floating point equality tests and index assignment (anonymous namespace
        //  functions of the C++ source)
        //
        public static bool isSharpnessEqual(float s1, float s2)
        {
            return (s1 == s2);
        }

        public static int assignSharpnessIndex(float sharpness, StdVector<float> sharpnessValues)
        {
            // linear search
            for (int i = 0; i < (int)sharpnessValues.Count; ++i)
            {
                if (isSharpnessEqual(sharpnessValues[i], sharpness))
                {
                    return i;
                }
            }
            sharpnessValues.PushBack(sharpness);
            return (int)sharpnessValues.Count - 1;
        }

        public static bool isBoundaryFace(Level level, int face)
        {
            return ((bool)level.getFaceCompositeVTag(face)._boundary);
        }

        public static void offsetIndices(int[] indices, int indicesOffset, int size, int offset)
        {
            for (int i = 0; i < size; ++i)
            {
                indices[indicesOffset + i] += offset;
            }
        }

        //
        //  Public interface intended for use by the PatchTableFactory -- all
        //  else is solely for internal use:
        //
        public PatchTableBuilder(TopologyRefiner refiner, PatchTableFactory.Options opts,
                                 ConstIndexArray faces)
        {
            _refiner = refiner;
            _options = opts;
            _selectedFaces = faces;
            _table = null;
            _patchBuilder = null;
            _ptexIndices = new PtexIndices(refiner);
            _numRegularPatches = 0;
            _numIrregularPatches = 0;
            _legacyGregoryHelper = null;

            _patches = new List<PatchTuple>();
            _levelVertOffsets = new List<int>();
            _levelFVarValueOffsets = new List<List<int>>();
            _fvarChannelIndices = new List<int>();

            if (_options.generateFVarTables)
            {
                // If client-code does not select specific channels, default to all
                // the channels in the refiner.
                if (_options.numFVarChannels == -1)
                {
                    int numChannels = _refiner.GetNumFVarChannels();
                    for (int fvc = 0; fvc < numChannels; ++fvc)
                    {
                        _fvarChannelIndices.Add(fvc); // std::iota
                    }
                }
                else
                {
                    for (int i = 0; i < _options.numFVarChannels; ++i)
                    {
                        _fvarChannelIndices.Add(_options.fvarChannelIndices[i]);
                    }
                }
            }

            //
            //  Will need to translate PatchTableFactory options to the newer set of
            //  PatchBuilder options in the near future.  And the state variables are
            //  a potentially complex combination of options and so are handled after
            //  the PatchBuilder construction (to potentially help) rather than in
            //  the initializer list.
            //
            PatchBuilder.Options patchOptions = new PatchBuilder.Options();

            patchOptions.regBasisType = PatchBuilder.BasisType.BASIS_REGULAR;
            switch (_options.GetEndCapType())
            {
                case PatchTableFactory.Options.EndCapType.ENDCAP_BILINEAR_BASIS:
                    patchOptions.irregBasisType = PatchBuilder.BasisType.BASIS_LINEAR;
                    break;
                case PatchTableFactory.Options.EndCapType.ENDCAP_BSPLINE_BASIS:
                    patchOptions.irregBasisType = PatchBuilder.BasisType.BASIS_REGULAR;
                    break;
                case PatchTableFactory.Options.EndCapType.ENDCAP_GREGORY_BASIS:
                    patchOptions.irregBasisType = PatchBuilder.BasisType.BASIS_GREGORY;
                    break;
                default:
                    //  The PatchBuilder will infer if left un-specified
                    patchOptions.irregBasisType = PatchBuilder.BasisType.BASIS_UNSPECIFIED;
                    break;
            }
            patchOptions.fillMissingBoundaryPoints = true;
            patchOptions.approxInfSharpWithSmooth = !_options.useInfSharpPatch;
            patchOptions.approxSmoothCornerWithSharp =
                _options.generateLegacySharpCornerPatches;

            _patchBuilder = PatchBuilder.Create(_refiner, patchOptions);

            //
            //  Initialize member variables that capture specified options:
            //
            _requiresRegularLocalPoints =
                (patchOptions.regBasisType != PatchBuilder.BasisType.BASIS_REGULAR);
            _requiresIrregularLocalPoints =
                (_options.GetEndCapType() != PatchTableFactory.Options.EndCapType.ENDCAP_LEGACY_GREGORY);
            _requiresLocalPoints =
                _requiresIrregularLocalPoints || _requiresRegularLocalPoints;

            _requiresSharpnessArray = _options.useSingleCreasePatch;

            _requiresFVarPatches = (_fvarChannelIndices.Count != 0);

            _requiresVaryingPatches = _options.generateVaryingTables;
            _requiresVaryingLocalPoints = _options.generateVaryingTables &&
                                          _options.generateVaryingLocalPoints;

            //  Option to be made public in future:
            bool options_generateNonLinearUniformPatches = false;

            _buildUniformLinear = _refiner.IsUniform() && !options_generateNonLinearUniformPatches;

            //
            //  Create and initialize the new PatchTable instance to be assembled:
            //
            _table = new PatchTable(_refiner.GetMaxValence());

            _table._numPtexFaces = _ptexIndices.GetNumFaces();

            _table._vertexPrecisionIsDouble = _options.patchPrecisionDouble;
            _table._varyingPrecisionIsDouble = _options.patchPrecisionDouble;
            _table._faceVaryingPrecisionIsDouble = _options.fvarPatchPrecisionDouble;

            _table._varyingDesc = new PatchDescriptor(_patchBuilder.GetLinearPatchType());

            //  State and helper to support LegacyGregory arrays in the PatchTable:
            _requiresLegacyGregoryTables = !_refiner.IsUniform() &&
                (_options.GetEndCapType() == PatchTableFactory.Options.EndCapType.ENDCAP_LEGACY_GREGORY);

            if (_requiresLegacyGregoryTables)
            {
                _legacyGregoryHelper = new LegacyGregoryHelper(_refiner);
            }
        }

        //  (the C++ destructor deletes _patchBuilder and _legacyGregoryHelper: nothing to do in C#)

        public bool UniformPolygonsSpecified()
        {
            return _buildUniformLinear;
        }

        public void BuildUniformPolygons()
        {
            // Default behavior is to include base level vertices in the patch vertices
            // for vertex and varying patches, but not face-varying.  Consider exposing
            // these as public options in future so that clients can create consistent
            // behavior:
            bool includeBaseLevelIndices = _options.includeBaseLevelIndices;
            bool includeBaseLevelFVarIndices = _options.includeFVarBaseLevelIndices;

            // ensure that triangulateQuads is only set for quadrilateral schemes
            bool triangulateQuads =
                _options.triangulateQuads && (_patchBuilder.GetRegularFaceSize() == 4);

            // level=0 may contain n-gons, which are not supported in PatchTable.
            // even if generateAllLevels = true, we start from level 1.
            int maxlevel = _refiner.GetMaxLevel(),
                firstlevel = _options.generateAllLevels ? 1 : maxlevel,
                nlevels = maxlevel - firstlevel + 1;

            PatchDescriptor.Type ptype = triangulateQuads
                                       ? PatchDescriptor.Type.TRIANGLES
                                       : _patchBuilder.GetLinearPatchType();

            //
            //  Allocate and initialize the table's members.
            //
            _table._isUniformLinear = true;
            _table.reservePatchArrays(nlevels);

            PatchDescriptor desc = new PatchDescriptor(ptype);

            // generate patch arrays
            for (int level = firstlevel, poffset = 0, voffset = 0; level <= maxlevel; ++level)
            {
                TopologyLevel refLevel = _refiner.GetLevel(level);

                int npatches = refLevel.GetNumFaces();
                if (_refiner.HasHoles())
                {
                    for (int i = npatches - 1; i >= 0; --i)
                    {
                        npatches -= refLevel.IsFaceHole(i) ? 1 : 0;
                    }
                }
                System.Diagnostics.Debug.Assert(npatches >= 0);
                if (triangulateQuads)
                {
                    npatches *= 2;
                }

                _table.pushPatchArray(desc, npatches, ref voffset, ref poffset);
            }

            // Allocate various tables
            allocateVertexTables();

            if (_requiresFVarPatches)
            {
                allocateFVarChannels();
            }

            //
            //  Now populate the patches:
            //
            int[] patchVerts = _table._patchVerts.Data;
            PatchParam[] paramTable = _table._paramTable.Data;

            int iptr = 0;
            int pptr = 0;
            int[][] fvarValues = null;
            PatchParam[][] fvarParams = null;
            int[] fptr = null;
            int[] fpptr = null;

            int levelVertOffset = includeBaseLevelIndices
                                ? _refiner.GetLevel(0).GetNumVertices() : 0;

            int[] levelFVarVertOffsets = null;
            if (_requiresFVarPatches)
            {
                int numFVarChannelsRequired = _fvarChannelIndices.Count;
                levelFVarVertOffsets = new int[numFVarChannelsRequired];

                fvarValues = new int[numFVarChannelsRequired][];
                fvarParams = new PatchParam[numFVarChannelsRequired][];
                fptr = new int[numFVarChannelsRequired];
                fpptr = new int[numFVarChannelsRequired];

                for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                {
                    IndexArray fvarChannelValues = _table.getFVarValues(fvc);
                    fvarValues[fvc] = fvarChannelValues._array;
                    fptr[fvc] = fvarChannelValues.begin();

                    PatchParamArray fvarChannelParams = _table.getFVarPatchParams(fvc);
                    fvarParams[fvc] = fvarChannelParams._array;
                    fpptr[fvc] = fvarChannelParams.begin();

                    if (includeBaseLevelFVarIndices)
                    {
                        int refinerChannel = _fvarChannelIndices[fvc];
                        levelFVarVertOffsets[fvc] =
                            _refiner.GetLevel(0).GetNumFVarValues(refinerChannel);
                    }
                }
            }

            for (int level = 1; level <= maxlevel; ++level)
            {
                TopologyLevel refLevel = _refiner.GetLevel(level);

                int nfaces = refLevel.GetNumFaces();

                if (level >= firstlevel)
                {
                    for (int face = 0; face < nfaces; ++face)
                    {
                        if (_refiner.HasHoles() && refLevel.IsFaceHole(face))
                        {
                            continue;
                        }

                        ConstIndexArray fverts = refLevel.GetFaceVertices(face);

                        for (int vert = 0; vert < fverts.size(); ++vert)
                        {
                            patchVerts[iptr++] = levelVertOffset + fverts[vert];
                        }

                        PatchParam pparam = _patchBuilder.ComputePatchParam(
                            level, face, _ptexIndices);
                        paramTable[pptr++] = pparam;

                        if (_requiresFVarPatches)
                        {
                            for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                            {
                                int refinerChannel = _fvarChannelIndices[fvc];
                                ConstIndexArray fvalues =
                                    refLevel.GetFaceFVarValues(face, refinerChannel);
                                for (int vert = 0; vert < fvalues.size(); ++vert)
                                {
                                    System.Diagnostics.Debug.Assert(fptr[fvc] + vert <
                                        _table.getFVarValues(fvc).end());
                                    fvarValues[fvc][fptr[fvc] + vert] =
                                        levelFVarVertOffsets[fvc] + fvalues[vert];
                                }
                                fptr[fvc] += fvalues.size();
                                fvarParams[fvc][fpptr[fvc]++] = pparam;
                            }
                        }

                        if (triangulateQuads)
                        {
                            // Triangulate the quadrilateral:
                            //     {v0,v1,v2,v3} -> {v0,v1,v2},{v3,v0,v2}.
                            patchVerts[iptr] = patchVerts[iptr - 4]; // copy v0 index
                            ++iptr;
                            patchVerts[iptr] = patchVerts[iptr - 3]; // copy v2 index
                            ++iptr;
                            paramTable[pptr++] = pparam;

                            if (_requiresFVarPatches)
                            {
                                for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                                {
                                    fvarValues[fvc][fptr[fvc]] = fvarValues[fvc][fptr[fvc] - 4]; // copy fv0 index
                                    ++fptr[fvc];
                                    fvarValues[fvc][fptr[fvc]] = fvarValues[fvc][fptr[fvc] - 3]; // copy fv2 index
                                    ++fptr[fvc];
                                    fvarParams[fvc][fpptr[fvc]++] = pparam;
                                }
                            }
                        }
                    }
                }
                if (_options.generateAllLevels)
                {
                    levelVertOffset += _refiner.GetLevel(level).GetNumVertices();

                    if (_requiresFVarPatches)
                    {
                        for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                        {
                            int refinerChannel = _fvarChannelIndices[fvc];
                            levelFVarVertOffsets[fvc] +=
                                _refiner.GetLevel(level).GetNumFVarValues(refinerChannel);
                        }
                    }
                }
            }
        }

        public void BuildPatches()
        {
            identifyPatches();
            populatePatches();
        }

        public PatchTable GetPatchTable()
        {
            return _table;
        }

        //  Simple struct to store <face,level> pair for a patch:
        public struct PatchTuple
        {
            public PatchTuple(int face, int level)
            {
                faceIndex = face;
                levelIndex = level;
            }

            public int faceIndex;
            public int levelIndex;
        }

        //  Struct comprising a collection of topological properties for a patch that
        //  may be shared (between vertex and face-varying patches):
        public class PatchInfo
        {
            public PatchInfo()
            {
                isRegular = false;
                isRegSingleCrease = false;
                regBoundaryMask = 0;
                regSharpness = 0.0f;
                irregCornerSpans = new Level.VSpan[4];
                paramBoundaryMask = 0;
                fMatrix = new SparseMatrix<float>();
                dMatrix = new SparseMatrix<double>();
            }

            public bool isRegular;
            public bool isRegSingleCrease;
            public int regBoundaryMask;
            public float regSharpness;
            public Level.VSpan[] irregCornerSpans;
            public int paramBoundaryMask;

            public SparseMatrix<float> fMatrix;
            public SparseMatrix<double> dMatrix;
        }

        //
        //  Internal LocalPointHelper class
        //
        //  A LocalPointHelper manages the number, sharing of and StencilTable for
        //  the local points of a patch or one of its face-varying channels.  An
        //  instance of the helper does not know anything about the properties of
        //  the Builder classes that use it.  It can combine local points for any
        //  patch types all in one, so the regular and irregular patch types can
        //  both be local and differ, e.g. Bezier for regular and Gregory for
        //  irregular, and points can be effectively shared, appropriate stencils
        //  created, etc.
        //
        //  While not dependent on a particular patch type, some methods do require
        //  a patch type argument in order to know what can be done with its local
        //  points, e.g. which points can be shared with adjacent patches.
        //
        public class LocalPointHelper
        {
            public struct Options
            {
                public Options()
                {
                    shareLocalPoints = false;
                    reuseSourcePoints = false;
                    createStencilTable = true;
                    createVaryingTable = false;
                    doubleStencilTable = false;
                }

                public bool shareLocalPoints;
                public bool reuseSourcePoints;
                public bool createStencilTable;
                public bool createVaryingTable;
                public bool doubleStencilTable;
            }

            public LocalPointHelper(TopologyRefiner refiner, Options options,
                                    int fvarChannel, int numLocalPointsExpected)
            {
                _refiner = refiner;
                _options = options;
                _fvarChannel = fvarChannel;
                _numLocalPoints = 0;
                _stencilTable = new PatchTable.StencilTablePtr();
                _stencilTableVarying = new PatchTable.StencilTablePtr();
                _sharedCornerPoints = new List<IndexVector>();
                _sharedEdgePoints = new List<IndexVector>();

                _localPointOffset = (_fvarChannel < 0)
                                  ? _refiner.GetNumVerticesTotal()
                                  : _refiner.GetNumFVarValuesTotal(_fvarChannel);

                if (_options.createStencilTable)
                {
                    if (_options.doubleStencilTable)
                    {
                        initializeStencilTable<double>(numLocalPointsExpected);
                    }
                    else
                    {
                        initializeStencilTable<float>(numLocalPointsExpected);
                    }
                }
            }

            //  (the C++ destructor deletes the stencil tables: nothing to do in C#)

            public int GetNumLocalPoints()
            {
                return _numLocalPoints;
            }

            public PatchTable.StencilTablePtr AcquireStencilTable()
            {
                return _options.doubleStencilTable
                    ? acquireStencilTable<double>(ref _stencilTable)
                    : acquireStencilTable<float>(ref _stencilTable);
            }

            public void initializeStencilTable<REAL>(int numLocalPointsExpected) where REAL : unmanaged, INumber<REAL>
            {
                //
                //  Reserving space for the local-point stencils has been a source of
                //  problems in the past so we rely on the PatchTableBuilder to provide
                //  a reasonable estimate to the LocalPointHelper on construction.  For
                //  large meshes a limit on the size initially reserve is capped.
                //
                //  The average number of entries per stencil has been historically set
                //  at 16, which seemed high and was reduced on further investigation.
                //
                StencilTableReal<REAL> stencilTable = new StencilTableReal<REAL>(0);
                StencilTableReal<REAL> varyingTable = _options.createVaryingTable
                                                    ? new StencilTableReal<REAL>(0) : null;

                //  Historic note:  limits to 100M (=800M bytes) entries for reserved size
                long MaxEntriesToReserve = 100L * 1024 * 1024;
                long AvgEntriesPerStencil = 9;  // originally 16

                long numStencilsExpected = numLocalPointsExpected;
                long numStencilEntriesExpected = numStencilsExpected * AvgEntriesPerStencil;

                long numEntriesToReserve = Math.Min(numStencilEntriesExpected,
                                                    MaxEntriesToReserve);

                if (numEntriesToReserve != 0)
                {
                    stencilTable.reserve(
                        (int)numStencilsExpected, (int)numEntriesToReserve);
                    if (varyingTable != null)
                    {
                        //  Varying stencils have only one entry per point
                        varyingTable.reserve(
                            (int)numStencilsExpected, (int)numStencilsExpected);
                    }
                }

                _stencilTable = PatchTable.StencilTablePtr.From<REAL>(stencilTable);
                _stencilTableVarying = PatchTable.StencilTablePtr.From<REAL>(varyingTable);
            }

            public PatchTable.StencilTablePtr acquireStencilTable<REAL>(ref PatchTable.StencilTablePtr stencilTableMember) where REAL : unmanaged, INumber<REAL>
            {
                StencilTableReal<REAL> stencilTable = stencilTableMember.Get<REAL>();
                if (stencilTable != null)
                {
                    if (stencilTable.GetNumStencils() > 0)
                    {
                        stencilTable.finalize();
                    }
                    else
                    {
                        stencilTable = null;
                    }
                }
                stencilTableMember.Set();
                return PatchTable.StencilTablePtr.From<REAL>(stencilTable);
            }

            public int findSharedCornerPoint(int levelIndex,
                    int vertIndex, int localPointIndex)
            {
                if (_sharedCornerPoints.Count == 0)
                {
                    for (int i = 0; i < _refiner.GetNumLevels(); ++i)
                    {
                        _sharedCornerPoints.Add(new IndexVector());
                    }
                }
                IndexVector vertexPoints = _sharedCornerPoints[levelIndex];
                if (vertexPoints.empty())
                {
                    Level level = _refiner.getLevel(levelIndex);
                    if (_fvarChannel < 0)
                    {
                        vertexPoints.Resize(level.getNumVertices(), FarTypes.INDEX_INVALID);
                    }
                    else
                    {
                        vertexPoints.Resize(level.getNumFVarValues(_fvarChannel), FarTypes.INDEX_INVALID);
                    }
                }

                ref int assignedIndex = ref vertexPoints[vertIndex];
                if (!FarTypes.IndexIsValid(assignedIndex))
                {
                    assignedIndex = localPointIndex;
                }
                return assignedIndex;
            }

            public int findSharedEdgePoint(int levelIndex,
                    int edgeIndex, int edgeEnd, int localPointIndex)
            {
                if (_sharedEdgePoints.Count == 0)
                {
                    for (int i = 0; i < _refiner.GetNumLevels(); ++i)
                    {
                        _sharedEdgePoints.Add(new IndexVector());
                    }
                }
                IndexVector edgePoints = _sharedEdgePoints[levelIndex];
                if (edgePoints.empty())
                {
                    edgePoints.Resize(
                        2 * _refiner.GetLevel(levelIndex).GetNumEdges(), FarTypes.INDEX_INVALID);
                }

                ref int assignedIndex = ref edgePoints[2 * edgeIndex + edgeEnd];
                if (!FarTypes.IndexIsValid(assignedIndex))
                {
                    assignedIndex = localPointIndex;
                }
                else
                {
                }
                return assignedIndex;
            }

            public void appendLocalPointStencil<REAL>(
                SparseMatrix<REAL> conversionMatrix,
                int stencilRow,
                int[] sourcePoints,
                int sourcePointOffset) where REAL : unmanaged, INumber<REAL>
            {
                int stencilSize = conversionMatrix.GetRowSize(stencilRow);
                ConstArray<int> matrixColumns = conversionMatrix.GetRowColumns(stencilRow);
                ConstArray<REAL> matrixWeights = conversionMatrix.GetRowElements(stencilRow);

                StencilTableReal<REAL> stencilTable = _stencilTable.Get<REAL>();

                stencilTable._sizes.PushBack(stencilSize);
                for (int i = 0; i < stencilSize; ++i)
                {
                    stencilTable._weights.PushBack(matrixWeights[i]);
                    stencilTable._indices.PushBack(
                        sourcePoints[matrixColumns[i]] + sourcePointOffset);
                }
            }

            public void appendLocalPointStencils<REAL>(
                SparseMatrix<REAL> conversionMatrix,
                int[] sourcePoints,
                int sourcePointOffset) where REAL : unmanaged, INumber<REAL>
            {
                //
                //  Resize the StencilTable members to accomodate all rows and elements from
                //  the given set of points represented by the matrix
                //
                StencilTableReal<REAL> stencilTable = _stencilTable.Get<REAL>();

                int numNewStencils = conversionMatrix.GetNumRows();
                int numNewElements = conversionMatrix.GetNumElements();

                int numOldStencils = stencilTable._sizes.Count;
                int numOldElements = stencilTable._indices.Count;

                //  Assign the sizes for the new stencils:
                stencilTable._sizes.Resize(numOldStencils + numNewStencils);

                for (int i = 0; i < numNewStencils; ++i)
                {
                    stencilTable._sizes[numOldStencils + i] = conversionMatrix.GetRowSize(i);
                }

                //  Assign remapped indices for the stencils:
                stencilTable._indices.Resize(numOldElements + numNewElements);

                ConstArray<int> mtxIndices = conversionMatrix.GetColumns();

                for (int i = 0; i < numNewElements; ++i)
                {
                    stencilTable._indices[numOldElements + i] =
                        sourcePoints[mtxIndices[i]] + sourcePointOffset;
                }

                //  Copy the stencil weights direct from the matrix elements:
                stencilTable._weights.Resize(numOldElements + numNewElements);

                ConstArray<REAL> mtxWeights = conversionMatrix.GetElements();

                System.Array.Copy(mtxWeights._array, mtxWeights._offset,
                                  stencilTable._weights.Data, numOldElements, numNewElements);
            }

            //
            //  Its unfortunate that varying stencils for local points were ever created
            //  and external dependency on them forces a certain coordination here.  Each
            //  patch type is expected to have a varying value computed for each patch
            //  point and shaders retrieve the varying value associated with particular
            //  points.  So we need to store that mapping from control point to varying
            //  point (or corner of the patch) somewhere.  We are trying to avoid adding
            //  more to the PatchDescriptor interface, so we'll keep it here for now in
            //  the hope we may be able to eliminate the need for it.
            //
            private static readonly int[] linearIndices = new int[4] { 0, 1, 2, 3 };
            private static readonly int[] bsplineIndices =
                new int[16] { 0, 0, 1, 1, 0, 0, 1, 1, 3, 3, 2, 2, 3, 3, 2, 2 };
            private static readonly int[] boxsplineIndices =
                new int[12] { 0, 0, 1, 0, 0, 1, 1, 2, 2, 1, 2, 2 };
            private static readonly int[] gregoryIndices =
                new int[20] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 3, 3, 3, 3, 3 };
            private static readonly int[] gregoryTriIndices =
                new int[18] { 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 0, 1, 2 };

            public static int[] GetVaryingIndicesPerType(PatchDescriptor.Type type)
            {
                //  Note that we can use the linear and gregory vectors here for
                //  both quads and tris
                if (type == PatchDescriptor.Type.GREGORY_BASIS)
                {
                    return gregoryIndices;
                }
                else if (type == PatchDescriptor.Type.GREGORY_TRIANGLE)
                {
                    return gregoryTriIndices;
                }
                else if (type == PatchDescriptor.Type.REGULAR)
                {
                    return bsplineIndices;
                }
                else if (type == PatchDescriptor.Type.LOOP)
                {
                    return boxsplineIndices;
                }
                else if (type == PatchDescriptor.Type.QUADS)
                {
                    return linearIndices;
                }
                else if (type == PatchDescriptor.Type.TRIANGLES)
                {
                    return linearIndices;
                }
                return null;
            }

            public void appendLocalPointVaryingStencil<REAL>(
                int[] varyingIndices, int patchPointIndex,
                int[] sourcePoints, int sourcePointOffset) where REAL : unmanaged, INumber<REAL>
            {
                int varyingPoint =
                    sourcePoints[varyingIndices[patchPointIndex]] + sourcePointOffset;

                StencilTableReal<REAL> t = _stencilTableVarying.Get<REAL>();
                t._sizes.PushBack(1);
                t._indices.PushBack(varyingPoint);
                t._weights.PushBack(REAL.One);
            }

            private const short SHARE_CORNER = 0x10;
            private const short SHARE_EDGE_BEGIN = 0x20;
            private const short SHARE_EDGE_END = 0x40;
            private const short SHARE_EDGE = SHARE_EDGE_BEGIN | SHARE_EDGE_END;
            private const short SHARE_INDEX_MASK = 0x0f;

            private static readonly short[] linearQuadBits =
                new short[5] { 0x10, 0x11, 0x12, 0x13, SHARE_CORNER };
            private static readonly short[] gregoryQuadBits =
                new short[21] { 0x10, 0x20, 0x43, 0, 0,
                                0x11, 0x21, 0x40, 0, 0,
                                0x12, 0x22, 0x41, 0, 0,
                                0x13, 0x23, 0x42, 0, 0,
                                SHARE_CORNER | SHARE_EDGE };

            public static short[] GetShareBitsPerType(PatchDescriptor.Type type)
            {
                if (type == PatchDescriptor.Type.QUADS)
                {
                    return linearQuadBits;
                }
                else if (type == PatchDescriptor.Type.GREGORY_BASIS)
                {
                    return gregoryQuadBits;
                }
                return null;
            }

            public int AppendLocalPatchPoints<REAL>(
                int levelIndex, int faceIndex,
                SparseMatrix<REAL> matrix,
                PatchDescriptor.Type patchType,
                int[] sourcePoints,
                int sourcePointOffset,
                int[] patchPoints,
                int patchPointsOffset) where REAL : unmanaged, INumber<REAL>
            {
                //
                //  If sharing local points, verify the type of this patch supports
                //  sharing and disable it if not:
                //
                int numPatchPoints = matrix.GetNumRows();

                int firstNewLocalPoint = _localPointOffset + _numLocalPoints;
                int nextNewLocalPoint = firstNewLocalPoint;

                short[] shareBitsPerPoint = _options.shareLocalPoints ?
                                            GetShareBitsPerType(patchType) : null;

                bool shareLocalPointsForThisPatch = (shareBitsPerPoint != null);

                int[] varyingIndices = null;
                if (_stencilTableVarying)
                {
                    varyingIndices = GetVaryingIndicesPerType(patchType);
                }

                bool applyVertexStencils = (_stencilTable.Get<REAL>() != null);
                bool applyVaryingStencils = (varyingIndices != null);

                //
                //  When point-sharing is not enabled, all patch points are generally
                //  new local points -- the exception to this occurs when "re-using"
                //  source points, i.e. the resulting patch can be a mixture of source
                //  and local points (typically when the irregular patch type is the
                //  same as the regular patch type native to the scheme).
                //
                if (!shareLocalPointsForThisPatch)
                {
                    if (!_options.reuseSourcePoints)
                    {
                        if (applyVertexStencils)
                        {
                            appendLocalPointStencils(
                                matrix, sourcePoints, sourcePointOffset);

                            if (applyVaryingStencils)
                            {
                                for (int i = 0; i < numPatchPoints; ++i)
                                {
                                    appendLocalPointVaryingStencil<REAL>(
                                        varyingIndices, i, sourcePoints, sourcePointOffset);
                                }
                            }
                        }
                        for (int i = 0; i < numPatchPoints; ++i)
                        {
                            patchPoints[patchPointsOffset + i] = nextNewLocalPoint++;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < numPatchPoints; ++i)
                        {
                            if (_options.reuseSourcePoints && (matrix.GetRowSize(i) == 1))
                            {
                                patchPoints[patchPointsOffset + i] = sourcePoints[matrix.GetRowColumns(i)[0]]
                                                                   + sourcePointOffset;
                                continue;
                            }
                            if (applyVertexStencils)
                            {
                                appendLocalPointStencil(
                                    matrix, i, sourcePoints, sourcePointOffset);

                                if (applyVaryingStencils)
                                {
                                    appendLocalPointVaryingStencil<REAL>(
                                        varyingIndices, i, sourcePoints, sourcePointOffset);
                                }
                            }
                            patchPoints[patchPointsOffset + i] = nextNewLocalPoint++;
                        }
                    }
                }
                else
                {
                    //  Gather topology info according to the sharing for this patch type
                    //
                    Level level = _refiner.getLevel(levelIndex);

                    ConstIndexArray fCorners = new ConstIndexArray(null, 0, 0);
                    ConstIndexArray fVerts = new ConstIndexArray(null, 0, 0);
                    ConstIndexArray fEdges = new ConstIndexArray(null, 0, 0);
                    bool[] fEdgeReversed = new bool[4];
                    bool[] fEdgeBoundary = new bool[4];

                    short shareBitsForType = shareBitsPerPoint[numPatchPoints];
                    if (shareBitsForType != 0)
                    {
                        if ((shareBitsForType & SHARE_CORNER) != 0)
                        {
                            fCorners = (_fvarChannel < 0)
                                     ? (ConstIndexArray)level.getFaceVertices(faceIndex)
                                     : (ConstIndexArray)level.getFaceFVarValues(faceIndex, _fvarChannel);
                        }
                        if ((shareBitsForType & SHARE_EDGE) != 0)
                        {
                            fEdges = level.getFaceEdges(faceIndex);
                            fVerts = (_fvarChannel < 0) ? fCorners
                                                        : (ConstIndexArray)level.getFaceVertices(faceIndex);

                            Level.ETag[] fEdgeTags = new Level.ETag[4];
                            level.getFaceETags(faceIndex, fEdgeTags, _fvarChannel);
                            for (int i = 0; i < fEdges.size(); ++i)
                            {
                                fEdgeReversed[i] = level.getEdgeVertices(fEdges[i])[0] != fVerts[i];
                                fEdgeBoundary[i] = fEdgeTags[i]._boundary;
                            }
                        }
                    }

                    //  Inspect the sharing bits for each point -- if set, see if a local
                    //  point for the corresponding vertex/fvar-value or edge was
                    //  previously used:
                    //
                    for (int i = 0; i < numPatchPoints; ++i)
                    {
                        if (_options.reuseSourcePoints && (matrix.GetRowSize(i) == 1))
                        {
                            patchPoints[patchPointsOffset + i] = sourcePoints[matrix.GetRowColumns(i)[0]]
                                                               + sourcePointOffset;
                            continue;
                        }

                        int patchPoint = nextNewLocalPoint;
                        if (shareBitsPerPoint[i] != 0)
                        {
                            int index = shareBitsPerPoint[i] & SHARE_INDEX_MASK;

                            if ((shareBitsPerPoint[i] & SHARE_CORNER) != 0)
                            {
                                patchPoint = findSharedCornerPoint(
                                    levelIndex, fCorners[index], nextNewLocalPoint);
                            }
                            else if (!fEdgeBoundary[index])
                            {
                                int edgeEnd = (((shareBitsPerPoint[i] & SHARE_EDGE_END) > 0) !=
                                               fEdgeReversed[index]) ? 1 : 0;
                                patchPoint = findSharedEdgePoint(
                                    levelIndex, fEdges[index], edgeEnd, nextNewLocalPoint);
                            }
                        }

                        if (patchPoint == nextNewLocalPoint)
                        {
                            if (applyVertexStencils)
                            {
                                appendLocalPointStencil(
                                    matrix, i, sourcePoints, sourcePointOffset);

                                if (applyVaryingStencils)
                                {
                                    appendLocalPointVaryingStencil<REAL>(
                                        varyingIndices, i, sourcePoints, sourcePointOffset);
                                }
                            }
                            nextNewLocalPoint++;
                        }
                        patchPoints[patchPointsOffset + i] = patchPoint;
                    }
                }

                int numNewLocalPoints = nextNewLocalPoint - firstNewLocalPoint;
                _numLocalPoints += numNewLocalPoints;
                return numNewLocalPoints;
            }

            //  Member variables:
            public TopologyRefiner _refiner;
            public Options _options;

            public int _fvarChannel;
            public int _numLocalPoints;
            public int _localPointOffset;

            public List<IndexVector> _sharedCornerPoints;
            public List<IndexVector> _sharedEdgePoints;

            public PatchTable.StencilTablePtr _stencilTable;

            //  This was hopefully transitional but will persist -- the should be
            //  no need for Varying local points or stencils associated with them.
            public PatchTable.StencilTablePtr AcquireStencilTableVarying()
            {
                return _options.doubleStencilTable
                    ? acquireStencilTable<double>(ref _stencilTableVarying)
                    : acquireStencilTable<float>(ref _stencilTableVarying);
            }

            public PatchTable.StencilTablePtr _stencilTableVarying;
        }

        //
        //  Internal LegacyGregoryHelper class
        //
        //  This local class helps to populate the arrays in the PatchTable
        //  associated with legacy-Gregory patches, i.e. the quad-offset and
        //  vertex-valence tables.  These patches are always associated with
        //  faces at the last level of refinement, so only the face index in
        //  that level is required to identify them.
        //
        public class LegacyGregoryHelper
        {
            public LegacyGregoryHelper(TopologyRefiner ref_)
            {
                _refiner = ref_;
                _interiorFaceIndices = new List<int>();
                _boundaryFaceIndices = new List<int>();
            }

            public int GetNumBoundaryPatches()
            {
                return _boundaryFaceIndices.Count;
            }

            public int GetNumInteriorPatches()
            {
                return _interiorFaceIndices.Count;
            }

            public void AddPatchFace(int level, int face)
            {
                if ((bool)_refiner.getLevel(level).getFaceCompositeVTag(face)._boundary)
                {
                    _boundaryFaceIndices.Add(face);
                }
                else
                {
                    _interiorFaceIndices.Add(face);
                }
            }

            //  The local struct QuadOffset of the C++ FinalizeQuadOffsets
            private static int QuadOffsetAssign(Level level, int faceIndex,
                                                uint[] offsets, int offsetsStart)
            {
                ConstIndexArray fVerts = level.getFaceVertices(faceIndex);

                for (int i = 0; i < 4; ++i)
                {
                    ConstIndexArray vFaces = level.getVertexFaces(fVerts[i]);

                    int faceInVFaces = vFaces.FindIndex(faceIndex);

                    // we have to use number of incident edges to modulo the local
                    // index as there could be 2 consecutive edges in the face
                    // belonging to the patch
                    int vOffset0 = faceInVFaces;
                    int vOffset1 = (faceInVFaces + 1) %
                                        level.getVertexEdges(fVerts[i]).size();

                    offsets[offsetsStart + i] = (uint)(vOffset0 | (vOffset1 << 8));
                }
                return 4;
            }

            public void FinalizeQuadOffsets(StdVector<uint> qTable)
            {
                int numInteriorPatches = _interiorFaceIndices.Count;
                int numBoundaryPatches = _boundaryFaceIndices.Count;
                int numTotalPatches = numInteriorPatches + numBoundaryPatches;

                if (numTotalPatches > 0)
                {
                    qTable.Resize(numTotalPatches * 4);

                    // all patches assumed to be at the last level
                    Level maxLevel = _refiner.getLevel(_refiner.GetMaxLevel());

                    int p = 0;
                    for (int i = 0; i < numInteriorPatches; ++i)
                    {
                        p += QuadOffsetAssign(maxLevel, _interiorFaceIndices[i], qTable.Data, p);
                    }
                    for (int i = 0; i < numBoundaryPatches; ++i)
                    {
                        p += QuadOffsetAssign(maxLevel, _boundaryFaceIndices[i], qTable.Data, p);
                    }
                }
            }

            public void FinalizeVertexValence(StdVector<int> vTable, int lastLevelOffset)
            {
                //  Populate the "vertex valence" table for Gregory patches -- this table
                //  contains the one-ring of vertices around each vertex.  Currently it is
                //  extremely wasteful for the following reasons:
                //      - it allocates 2*maxvalence+1 for ALL vertices
                //      - it initializes the one-ring for ALL vertices
                //  We use the full size expected (not sure what else relies on that) but
                //  we avoid initializing the vast majority of vertices that are not
                //  associated with gregory patches -- only populating those in the last
                //  level (an older version attempted to avoid vertices not involved with
                //  Gregory patches)
                //
                int vWidth = 2 * _refiner.GetMaxValence() + 1;
                vTable.Resize(checked((int)((long)_refiner.GetNumVerticesTotal() * vWidth)));

                Level lastLevel = _refiner.getLevel(_refiner.GetMaxLevel());

                int vTableEntry = lastLevelOffset * vWidth;
                for (int vIndex = 0; vIndex < lastLevel.getNumVertices(); ++vIndex)
                {
                    //  Gather the one-ring around the vertex and set its resulting size
                    //  (note negative size used to distinguish between boundary/interior):
                    //
                    int ringDest = vTableEntry + 1;
                    int ringSize =
                        lastLevel.gatherQuadRegularRingAroundVertex(vIndex, vTable.Data, -1, ringDest);
                    for (int j = 0; j < ringSize; ++j)
                    {
                        vTable.Data[ringDest + j] += lastLevelOffset;
                    }

                    if ((ringSize & 1) != 0)
                    {
                        // boundary: duplicate end vertex index and store negative valence
                        ringSize++;
                        vTable.Data[vTableEntry + ringSize] = vTable.Data[vTableEntry + ringSize - 1];
                        vTable.Data[vTableEntry + 0] = -ringSize / 2;
                    }
                    else
                    {
                        vTable.Data[vTableEntry + 0] = ringSize / 2;
                    }
                    vTableEntry += vWidth;
                }
            }

            public TopologyRefiner _refiner;
            public List<int> _interiorFaceIndices;
            public List<int> _boundaryFaceIndices;
        }

        //
        //  Builder methods for internal use:
        //
        //  Simple queries:
        //
        public int getRefinerFVarChannel(int fvcInTable)
        {
            return (fvcInTable >= 0) ? _fvarChannelIndices[fvcInTable] : -1;
        }

        public bool isFVarChannelLinear(int fvcInTable)
        {
            if (_options.generateFVarLegacyLinearPatches)
            {
                return true;
            }
            return (_refiner.GetFVarLinearInterpolation(
                getRefinerFVarChannel(fvcInTable)) == Sdc.Options.FVarLinearInterpolation.FVAR_LINEAR_ALL);
        }

        public bool doesFVarTopologyMatch(PatchTuple patch, int fvcInTable)
        {
            return _patchBuilder.DoesFaceVaryingPatchMatch(
                patch.levelIndex, patch.faceIndex,
                getRefinerFVarChannel(fvcInTable));
        }

        //
        //  Methods for identifying and assigning patch-related data:
        //
        public void identifyPatchTopology(PatchTuple patch, PatchInfo patchInfo,
                                          int fvarInTable = -1)
        {
            int patchLevel = patch.levelIndex;
            int patchFace = patch.faceIndex;

            int fvarInRefiner = getRefinerFVarChannel(fvarInTable);

            patchInfo.isRegular = _patchBuilder.IsPatchRegular(
                patchLevel, patchFace, fvarInTable);

            bool useDoubleMatrix = (fvarInRefiner < 0)
                                 ? _options.patchPrecisionDouble
                                 : _options.fvarPatchPrecisionDouble;

            if (patchInfo.isRegular)
            {
                patchInfo.regBoundaryMask = _patchBuilder.GetRegularPatchBoundaryMask(
                    patchLevel, patchFace, fvarInRefiner);

                patchInfo.isRegSingleCrease = false;
                patchInfo.regSharpness = 0.0f;
                patchInfo.paramBoundaryMask = patchInfo.regBoundaryMask;

                //  If converting to another basis, get the change-of-basis matrix:
                if (_requiresRegularLocalPoints)
                {
                    // _patchBuilder->GetRegularConversionMatrix(...);
                }

                //
                //  Test regular interior patches for a single-crease patch when it
                //  was specified.
                //
                //  Note that the PatchTable clamps the sharpness of single-crease
                //  patches to that of the maximimu refinement level, so any single-
                //  crease patches at the last level will be reduced to regular
                //  patches (maintaining continuity with other semi-sharp patches
                //  also reduced to regular).
                //
                if (_requiresSharpnessArray &&
                        (patchInfo.regBoundaryMask == 0) && (fvarInRefiner < 0))
                {
                    if (patchLevel < (int)_options.maxIsolationLevel)
                    {
                        PatchBuilder.SingleCreaseInfo creaseInfo = new PatchBuilder.SingleCreaseInfo();
                        if (_patchBuilder.IsRegularSingleCreasePatch(
                                patchLevel, patchFace, ref creaseInfo))
                        {
                            creaseInfo.creaseSharpness =
                                Math.Min(creaseInfo.creaseSharpness,
                                    (float)(_options.maxIsolationLevel - (uint)patchLevel));

                            patchInfo.isRegSingleCrease = true;
                            patchInfo.regSharpness = creaseInfo.creaseSharpness;
                            patchInfo.paramBoundaryMask = (1 << creaseInfo.creaseEdgeInFace);
                        }
                    }
                }
            }
            else if (_requiresIrregularLocalPoints)
            {
                _patchBuilder.GetIrregularPatchCornerSpans(
                    patchLevel, patchFace, patchInfo.irregCornerSpans, fvarInRefiner);

                if (useDoubleMatrix)
                {
                    _patchBuilder.GetIrregularPatchConversionMatrix<double>(
                        patchLevel, patchFace, patchInfo.irregCornerSpans, patchInfo.dMatrix);
                }
                else
                {
                    _patchBuilder.GetIrregularPatchConversionMatrix<float>(
                        patchLevel, patchFace, patchInfo.irregCornerSpans, patchInfo.fMatrix);
                }
                patchInfo.paramBoundaryMask = 0;
            }
        }

        public int assignPatchPointsAndStencils(PatchTuple patch,
                PatchInfo patchInfo, int[] patchPoints, int patchPointsOffset,
                LocalPointHelper localHelper, int fvarInTable = -1)
        {
            //
            //  This is where the interesting/complicated new work will take place
            //  when a change-of-basis is determined necessary and previously assigned
            //  to the PatchInfo
            //
            //  No change-of-basis means no local points or stencils associated with
            //  them, which should be trivial but should also only be true in the
            //  regular case.  (It is also the case that no local points will be
            //  generated for irregular patches when the LegacyGregory option is
            //  used -- so that possibility is still accounted for here.)
            //
            //  Regarding the return result, it should just be the size of the patch
            //  associated with the regular/irregular patch type chosen.  This could
            //  be retrieved from the PatchBuilder or PatchDescriptors -- alternatively
            //  a return value could be removed and the client left to increment by
            //  such a fixed step (which is already the case for FVar channels)
            //
            //  The more interesting size here is the number of local points/stencils
            //  added.
            //
            int fvarInRefiner = getRefinerFVarChannel(fvarInTable);

            int sourcePointOffset = (fvarInTable < 0)
                                  ? _levelVertOffsets[patch.levelIndex]
                                  : _levelFVarValueOffsets[fvarInTable][patch.levelIndex];

            bool useDoubleMatrix = (fvarInTable < 0)
                                 ? _options.patchPrecisionDouble
                                 : _options.fvarPatchPrecisionDouble;

            int numPatchPoints = 0;
            if (patchInfo.isRegular)
            {
                if (!_requiresRegularLocalPoints)
                {
                    numPatchPoints = _patchBuilder.GetRegularPatchPoints(
                            patch.levelIndex, patch.faceIndex,
                            patchInfo.regBoundaryMask,
                            new Span<int>(patchPoints, patchPointsOffset, patchPoints.Length - patchPointsOffset),
                            fvarInRefiner);

                    //  PatchBuilder set to fill missing boundary points so offset all
                    offsetIndices(patchPoints, patchPointsOffset, numPatchPoints, sourcePointOffset);
                }
                else
                {
                    //
                    //  Future support for regular patches converted to another basis.
                    //  Note the "source points" are not returned in the same
                    //  orientation and there may be fewer than expected number in the
                    //  case of boundaries:
                    //
                    //  (commented out in the C++ source)
                }
            }
            else if (_requiresIrregularLocalPoints)
            {
                int numSourcePoints = 0;
                if (useDoubleMatrix)
                {
                    numSourcePoints = patchInfo.dMatrix.GetNumColumns();
                    numPatchPoints = patchInfo.dMatrix.GetNumRows();
                }
                else
                {
                    numSourcePoints = patchInfo.fMatrix.GetNumColumns();
                    numPatchPoints = patchInfo.fMatrix.GetNumRows();
                }

                int[] sourcePoints = new int[numSourcePoints];

                _patchBuilder.GetIrregularPatchSourcePoints(
                        patch.levelIndex, patch.faceIndex,
                        patchInfo.irregCornerSpans, sourcePoints, fvarInRefiner);

                if (useDoubleMatrix)
                {
                    localHelper.AppendLocalPatchPoints<double>(
                            patch.levelIndex, patch.faceIndex,
                            patchInfo.dMatrix, _patchBuilder.GetIrregularPatchType(),
                            sourcePoints, sourcePointOffset, patchPoints, patchPointsOffset);
                }
                else
                {
                    localHelper.AppendLocalPatchPoints<float>(
                            patch.levelIndex, patch.faceIndex,
                            patchInfo.fMatrix, _patchBuilder.GetIrregularPatchType(),
                            sourcePoints, sourcePointOffset, patchPoints, patchPointsOffset);
                }
            }
            return numPatchPoints;
        }

        public int assignFacePoints(PatchTuple patch,
                                    int[] patchPoints, int patchPointsOffset,
                                    int fvarInTable = -1)
        {
            Level level = _refiner.getLevel(patch.levelIndex);

            int facePointOffset = (fvarInTable < 0)
                                ? _levelVertOffsets[patch.levelIndex]
                                : _levelFVarValueOffsets[fvarInTable][patch.levelIndex];

            int fvarInRefiner = getRefinerFVarChannel(fvarInTable);

            ConstIndexArray facePoints = (fvarInRefiner < 0)
                            ? (ConstIndexArray)level.getFaceVertices(patch.faceIndex)
                            : (ConstIndexArray)level.getFaceFVarValues(patch.faceIndex, fvarInRefiner);

            for (int i = 0; i < facePoints.size(); ++i)
            {
                patchPoints[patchPointsOffset + i] = facePoints[i] + facePointOffset;
            }
            return facePoints.size();
        }

        //
        //  Reserves tables based on contents of the PatchArrayVector in the PatchTable:
        //
        public void allocateVertexTables()
        {
            int ncvs = 0, npatches = 0;
            for (int i = 0; i < _table.GetNumPatchArrays(); ++i)
            {
                npatches += _table.GetNumPatches(i);
                ncvs += _table.GetNumControlVertices(i);
            }

            if (ncvs == 0 || npatches == 0)
            {
                return;
            }

            _table._patchVerts.Resize(ncvs);
            _table._paramTable.Resize(npatches);

            if (_requiresVaryingPatches && !_buildUniformLinear)
            {
                _table.allocateVaryingVertices(
                    new PatchDescriptor(_patchBuilder.GetLinearPatchType()), npatches);
            }
            if (_requiresSharpnessArray)
            {
                _table._sharpnessIndices.Resize(npatches, FarTypes.INDEX_INVALID);
            }
        }

        //
        //  Allocate face-varying tables
        //
        public void allocateFVarChannels()
        {
            int npatches = _table.GetNumPatchesTotal();

            _table.allocateFVarPatchChannels((int)_fvarChannelIndices.Count);

            // Initialize each channel
            for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
            {
                int refinerChannel = _fvarChannelIndices[fvc];

                Sdc.Options.FVarLinearInterpolation interpolation =
                    _refiner.GetFVarLinearInterpolation(refinerChannel);

                _table.setFVarPatchChannelLinearInterpolation(interpolation, fvc);

                PatchDescriptor.Type regPatchType = _patchBuilder.GetLinearPatchType();
                PatchDescriptor.Type irregPatchType = regPatchType;

                if (_buildUniformLinear)
                {
                    if (_options.triangulateQuads)
                    {
                        regPatchType = PatchDescriptor.Type.TRIANGLES;
                        irregPatchType = regPatchType;
                    }
                }
                else
                {
                    if (!isFVarChannelLinear(fvc))
                    {
                        regPatchType = _patchBuilder.GetRegularPatchType();
                        irregPatchType = _patchBuilder.GetIrregularPatchType();
                    }
                }

                _table.allocateFVarPatchChannelValues(
                        new PatchDescriptor(regPatchType), new PatchDescriptor(irregPatchType),
                        npatches, fvc);
            }
        }

        //
        //  Identify all patches required for faces at all levels -- appending the
        //  <level,face> pairs to identify each patch for later construction, while
        //  accumulating the number of regular vs irregular patches to size tables.
        //
        public void appendPatch(int levelIndex, int faceIndex)
        {
            _patches.Add(new PatchTuple(faceIndex, levelIndex));

            // Count the patches here to simplify subsequent allocation.
            if (_patchBuilder.IsPatchRegular(levelIndex, faceIndex))
            {
                ++_numRegularPatches;
            }
            else
            {
                ++_numIrregularPatches;

                //  LegacyGregory needs to distinguish boundary vs interior
                if (_requiresLegacyGregoryTables)
                {
                    _legacyGregoryHelper.AddPatchFace(levelIndex, faceIndex);
                }
            }
        }

        public void findDescendantPatches(int levelIndex, int faceIndex, int targetLevel)
        {
            //
            //  If we have reached the target level or a leaf, append the patch (if
            //  the face qualifies), otherwise recursively search the children:
            //
            if ((levelIndex == targetLevel) || _patchBuilder.IsFaceALeaf(levelIndex, faceIndex))
            {
                if (_patchBuilder.IsFaceAPatch(levelIndex, faceIndex))
                {
                    appendPatch(levelIndex, faceIndex);
                }
            }
            else
            {
                TopologyLevel level = _refiner.GetLevel(levelIndex);

                ConstIndexArray childFaces = level.GetFaceChildFaces(faceIndex);
                for (int i = 0; i < childFaces.size(); ++i)
                {
                    if (FarTypes.IndexIsValid(childFaces[i]))
                    {
                        findDescendantPatches(levelIndex + 1, childFaces[i], targetLevel);
                    }
                }
            }
        }

        public void identifyPatches()
        {
            //
            //  First initialize the offsets for all levels
            //
            _levelVertOffsets.Add(0);

            for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
            {
                _levelFVarValueOffsets.Add(new List<int>());
            }
            for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
            {
                _levelFVarValueOffsets[fvc].Add(0);
            }

            for (int levelIndex = 0; levelIndex < _refiner.GetNumLevels(); ++levelIndex)
            {
                Level level = _refiner.getLevel(levelIndex);

                _levelVertOffsets.Add(
                    _levelVertOffsets[_levelVertOffsets.Count - 1] + level.getNumVertices());

                for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                {
                    int refinerChannel = _fvarChannelIndices[fvc];

                    List<int> fvarOffsets = _levelFVarValueOffsets[fvc];
                    fvarOffsets.Add(
                        fvarOffsets[fvarOffsets.Count - 1]
                        + level.getNumFVarValues(refinerChannel));
                }
            }

            //
            //  If a set of selected base faces is present, identify the patches
            //  depth first.  Otherwise search breadth first through the levels:
            //
            int uniformLevel = _refiner.IsUniform() ? (int)_options.maxIsolationLevel : -1;

            _patches.Capacity = Math.Max(_patches.Capacity, _refiner.GetNumFacesTotal());

            if (_selectedFaces.size() != 0)
            {
                for (int i = 0; i < (int)_selectedFaces.size(); ++i)
                {
                    findDescendantPatches(0, _selectedFaces[i], uniformLevel);
                }
            }
            else if (uniformLevel >= 0)
            {
                int numFaces = _refiner.getLevel(uniformLevel).getNumFaces();

                for (int faceIndex = 0; faceIndex < numFaces; ++faceIndex)
                {
                    if (_patchBuilder.IsFaceAPatch(uniformLevel, faceIndex))
                    {
                        appendPatch(uniformLevel, faceIndex);
                    }
                }
            }
            else
            {
                for (int levelIndex = 0; levelIndex < _refiner.GetNumLevels(); ++levelIndex)
                {
                    int numFaces = _refiner.getLevel(levelIndex).getNumFaces();

                    for (int faceIndex = 0; faceIndex < numFaces; ++faceIndex)
                    {
                        if (_patchBuilder.IsFaceAPatch(levelIndex, faceIndex) &&
                            _patchBuilder.IsFaceALeaf(levelIndex, faceIndex))
                        {
                            appendPatch(levelIndex, faceIndex);
                        }
                    }
                }
            }
        }

        //
        //  State needed to populate an array in the patch table.
        //  Offsets in this class are initialized after the patch array
        //  data buffers have been allocated and are then incremented as we
        //  populate data into the patch table. Currently, we'll have at
        //  most 3 patch arrays: Regular, Irregular, and IrregularBoundary.
        //
        public class PatchArrayBuilder
        {
            public PatchArrayBuilder()
            {
                patchType = PatchDescriptor.Type.NON_PATCH;
                numPatches = 0;
                iptr = 0;
                pptr = 0;
                sptr = 0;
                vptr = 0;
                fptr = null;
                fpptr = null;
            }

            public PatchDescriptor.Type patchType;
            public int numPatches;
            public int iptr;     // offset into _patchVerts
            public int pptr;     // offset into _paramTable
            public int sptr;     // offset into _sharpnessIndices
            public int vptr;     // offset into _varyingVerts
            public int[] fptr;   // fvar indices: offset into the channel's patchValues
            public int[] fpptr;  // fvar patch-params: offset into the channel's patchParam
        }

        //
        //  Populate patches that were previously identified.
        //
        public void populatePatches()
        {
            PatchArrayBuilder[] arrayBuilders = new PatchArrayBuilder[3];
            for (int i = 0; i < 3; ++i)
            {
                arrayBuilders[i] = new PatchArrayBuilder();
            }

            // Regular patches patches will be packed into the first patch array
            // Irregular patches will be packed into arrays according to optional
            // specification -- sharing the array with regular patches or packed
            // into an array of their own.
            int ARRAY_REGULAR = 0;
            int ARRAY_IRREGULAR = 1;
            int ARRAY_BOUNDARY = 2; // only used by LegacyGregory

            arrayBuilders[ARRAY_REGULAR].patchType = _patchBuilder.GetRegularPatchType();
            arrayBuilders[ARRAY_REGULAR].numPatches = _numRegularPatches;

            int numPatchArrays = (_numRegularPatches > 0) ? 1 : 0;

            if (_numIrregularPatches > 0)
            {
                if (!_requiresLegacyGregoryTables)
                {
                    //
                    //  Pack irregular patches into same array as regular or separately:
                    //
                    if (_patchBuilder.GetRegularPatchType() ==
                            _patchBuilder.GetIrregularPatchType())
                    {
                        ARRAY_IRREGULAR = ARRAY_REGULAR;
                        numPatchArrays = 1;  // needed in case no regular patches
                    }
                    else
                    {
                        ARRAY_IRREGULAR = numPatchArrays;
                        numPatchArrays++;
                    }
                    arrayBuilders[ARRAY_IRREGULAR].patchType =
                        _patchBuilder.GetIrregularPatchType();
                    arrayBuilders[ARRAY_IRREGULAR].numPatches += _numIrregularPatches;
                }
                else
                {
                    //
                    // Arrays for Legacy-Gregory tables -- irregular patches are split
                    // into two arrays for interior and boundary patches
                    //
                    ARRAY_IRREGULAR = numPatchArrays;
                    arrayBuilders[ARRAY_IRREGULAR].patchType =
                        PatchDescriptor.Type.GREGORY;
                    arrayBuilders[ARRAY_IRREGULAR].numPatches =
                        _legacyGregoryHelper.GetNumInteriorPatches();
                    numPatchArrays += (arrayBuilders[ARRAY_IRREGULAR].numPatches > 0) ? 1 : 0;

                    ARRAY_BOUNDARY = numPatchArrays;
                    arrayBuilders[ARRAY_BOUNDARY].patchType =
                        PatchDescriptor.Type.GREGORY_BOUNDARY;
                    arrayBuilders[ARRAY_BOUNDARY].numPatches =
                        _legacyGregoryHelper.GetNumBoundaryPatches();
                    numPatchArrays += (arrayBuilders[ARRAY_BOUNDARY].numPatches > 0) ? 1 : 0;
                }
            }

            // Create patch arrays
            _table.reservePatchArrays(numPatchArrays);

            int voffset = 0, poffset = 0, qoffset = 0;
            for (int arrayIndex = 0; arrayIndex < numPatchArrays; ++arrayIndex)
            {
                PatchArrayBuilder arrayBuilder = arrayBuilders[arrayIndex];

                _table.pushPatchArray(new PatchDescriptor(arrayBuilder.patchType),
                    arrayBuilder.numPatches, ref voffset, ref poffset, ref qoffset);
            }

            // Allocate patch array data buffers
            allocateVertexTables();

            if (_requiresFVarPatches)
            {
                allocateFVarChannels();
            }

            // Initialize pointers (offsets) used while populating patch array data buffers
            for (int arrayIndex = 0; arrayIndex < numPatchArrays; ++arrayIndex)
            {
                PatchArrayBuilder arrayBuilder = arrayBuilders[arrayIndex];

                arrayBuilder.iptr = _table.getPatchArrayVertices(arrayIndex).begin();
                arrayBuilder.pptr = _table.getPatchParams(arrayIndex).begin();
                if (_requiresSharpnessArray)
                {
                    arrayBuilder.sptr = _table.getSharpnessIndices(arrayIndex);
                }
                if (_requiresVaryingPatches)
                {
                    arrayBuilder.vptr = _table.getPatchArrayVaryingVertices(arrayIndex).begin();
                }
                if (_requiresFVarPatches)
                {
                    arrayBuilder.fptr = new int[_fvarChannelIndices.Count];
                    arrayBuilder.fpptr = new int[_fvarChannelIndices.Count];
                    for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                    {
                        int pidx = _table.getPatchIndex(arrayIndex, 0);
                        int ofs = pidx * _table.GetFVarValueStride(fvc);

                        arrayBuilder.fptr[fvc] = ofs;
                        arrayBuilder.fpptr[fvc] = pidx;
                    }
                }
            }

            //
            //  Initializing StencilTable and other helpers.  Note Varying local points
            //  are managed by the LocalPointHelper for Vertex patches (as Varying local
            //  points are tightly coupled to their local points)
            //
            LocalPointHelper vertexLocalPointHelper = null;
            LocalPointHelper[] fvarLocalPointHelpers = null;

            if (_requiresLocalPoints)
            {
                LocalPointHelper.Options opts = new LocalPointHelper.Options();
                opts.createStencilTable = true;
                opts.createVaryingTable = _requiresVaryingLocalPoints;
                opts.doubleStencilTable = _options.patchPrecisionDouble;
                opts.shareLocalPoints = _options.shareEndCapPatchPoints;
                opts.reuseSourcePoints = (_patchBuilder.GetIrregularPatchType() ==
                                          _patchBuilder.GetNativePatchType());

                vertexLocalPointHelper = new LocalPointHelper(
                        _refiner, opts, -1, estimateLocalPointCount(opts, -1));

                if (_requiresFVarPatches)
                {
                    opts.createStencilTable = true;
                    opts.createVaryingTable = false;
                    opts.doubleStencilTable = _options.fvarPatchPrecisionDouble;

                    fvarLocalPointHelpers = new LocalPointHelper[_fvarChannelIndices.Count];
                    for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                    {
                        fvarLocalPointHelpers[fvc] = new LocalPointHelper(
                                _refiner, opts, getRefinerFVarChannel(fvc),
                                estimateLocalPointCount(opts, fvc));
                    }
                }
            }

            //  Populate patch data buffers
            //
            //  Intentionally declare local vairables to contain patch topology info
            //  outside the loop to avoid repeated memory de-allocation/re-allocation
            //  associated with a change-of-basis.
            PatchInfo patchInfo = new PatchInfo();
            PatchInfo fvarPatchInfo = new PatchInfo();

            bool fvarPrecisionMatches = (_options.patchPrecisionDouble ==
                                         _options.fvarPatchPrecisionDouble);

            for (int patchIndex = 0; patchIndex < (int)_patches.Count; ++patchIndex)
            {
                PatchTuple patch = _patches[patchIndex];

                //
                //  Identify and assign points, stencils, sharpness, etc. for this patch:
                //
                identifyPatchTopology(patch, patchInfo);

                PatchArrayBuilder arrayBuilder = arrayBuilders[ARRAY_REGULAR];
                if (!patchInfo.isRegular)
                {
                    arrayBuilder = arrayBuilders[ARRAY_IRREGULAR];
                }

                if (_requiresLegacyGregoryTables && !patchInfo.isRegular)
                {
                    if (isBoundaryFace(
                            _refiner.getLevel(patch.levelIndex), patch.faceIndex))
                    {
                        arrayBuilder = arrayBuilders[ARRAY_BOUNDARY];
                    }
                    arrayBuilder.iptr += assignFacePoints(patch,
                        _table._patchVerts.Data, arrayBuilder.iptr);
                }
                else
                {
                    arrayBuilder.iptr += assignPatchPointsAndStencils(patch,
                        patchInfo, _table._patchVerts.Data, arrayBuilder.iptr,
                        vertexLocalPointHelper);
                }

                if (_requiresSharpnessArray)
                {
                    _table._sharpnessIndices[arrayBuilder.sptr++] = assignSharpnessIndex(patchInfo.regSharpness,
                        _table._sharpnessValues);
                }

                //
                //  Identify the PatchParam -- which may vary slightly for face-varying
                //  channels but will still be used to initialize them when so to avoid
                //  recomputing from scratch
                //
                PatchParam patchParam =
                    _patchBuilder.ComputePatchParam(patch.levelIndex, patch.faceIndex,
                          _ptexIndices, patchInfo.isRegular, patchInfo.paramBoundaryMask,
                          true/* condition to compute transition mask */);

                _table._paramTable[arrayBuilder.pptr++] = patchParam;

                //
                //  Assignment of face-varying patches is split between the comon/trivial
                //  case of linear patches and the more complex non-linear cases:
                //
                System.Diagnostics.Debug.Assert((_fvarChannelIndices.Count > 0) == _requiresFVarPatches);

                for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                {
                    PatchTable.FVarPatchChannel fvarChannel = _table.getFVarPatchChannel(fvc);

                    if (isFVarChannelLinear(fvc))
                    {
                        assignFacePoints(patch, fvarChannel.patchValues.Data, arrayBuilder.fptr[fvc], fvc);

                        fvarChannel.patchParam[arrayBuilder.fpptr[fvc]] = patchParam;
                    }
                    else
                    {
                        //
                        //  For non-linear patches, reuse patch information when the
                        //  topology of the face in face-varying space matches the
                        //  original patch:
                        //
                        bool fvcTopologyMatches = fvarPrecisionMatches &&
                                                  doesFVarTopologyMatch(patch, fvc);

                        PatchInfo fvcPatchInfo = fvcTopologyMatches
                                               ? patchInfo : fvarPatchInfo;

                        if (!fvcTopologyMatches)
                        {
                            identifyPatchTopology(patch, fvcPatchInfo, fvc);
                        }

                        assignPatchPointsAndStencils(patch, fvcPatchInfo,
                                fvarChannel.patchValues.Data, arrayBuilder.fptr[fvc],
                                fvarLocalPointHelpers[fvc], fvc);

                        //  Initialize and assign the face-varying PatchParam:
                        ref PatchParam fvcPatchParam = ref fvarChannel.patchParam[arrayBuilder.fpptr[fvc]];

                        fvcPatchParam.Set(
                           patchParam.GetFaceId(),
                           (short)patchParam.GetU(), (short)patchParam.GetV(),
                           patchParam.GetDepth(),
                           patchParam.NonQuadRoot(),
                           (ushort)fvcPatchInfo.paramBoundaryMask,
                           patchParam.GetTransition(),
                           fvcPatchInfo.isRegular);
                    }
                    arrayBuilder.fpptr[fvc]++;
                    arrayBuilder.fptr[fvc] += _table.GetFVarValueStride(fvc);
                }

                if (_requiresVaryingPatches)
                {
                    arrayBuilder.vptr += assignFacePoints(patch,
                        _table._varyingVerts.Data, arrayBuilder.vptr);
                }
            }

            //
            //  Finalizing and destroying StencilTable and other helpers:
            //
            if (_requiresLocalPoints)
            {
                _table._localPointStencils =
                    vertexLocalPointHelper.AcquireStencilTable();
                if (_requiresVaryingLocalPoints)
                {
                    _table._localPointVaryingStencils =
                        vertexLocalPointHelper.AcquireStencilTableVarying();
                }

                if (_requiresFVarPatches)
                {
                    _table._localPointFaceVaryingStencils.Resize(_fvarChannelIndices.Count);
                    for (int fvc = 0; fvc < (int)_fvarChannelIndices.Count; ++fvc)
                    {
                        _table._localPointFaceVaryingStencils[fvc] =
                                fvarLocalPointHelpers[fvc].AcquireStencilTable();
                    }
                }
            }
            if (_requiresLegacyGregoryTables)
            {
                _legacyGregoryHelper.FinalizeQuadOffsets(_table._quadOffsetsTable);
                _legacyGregoryHelper.FinalizeVertexValence(_table._vertexValenceTable,
                                               _levelVertOffsets[_refiner.GetMaxLevel()]);
            }
        }

        public int estimateLocalPointCount(
                LocalPointHelper.Options options,
                int fvarChannel)
        {
            if ((fvarChannel >= 0) && isFVarChannelLinear(fvarChannel))
            {
                return 0;
            }

            //
            //  Estimate the local points required for the Vertex topology in all cases
            //  as it may be used in the estimates for face-varying channels:
            //
            int estLocalPoints = 0;

            if (_requiresRegularLocalPoints)
            {
                //
                //  Either all regular patches are being converted to a non-native
                //  basis, or boundary points are being extrapolated in the regular
                //  basis.  The latter case typically involves a small fraction of
                //  patches and points, so we don't estimate any local points and
                //  leave it up to incremental allocation later to account for them.
                //
                int numPointsPerPatch = new PatchDescriptor(
                        _patchBuilder.GetRegularPatchType()).GetNumControlVertices();

                if (_patchBuilder.GetRegularPatchType() !=
                        _patchBuilder.GetNativePatchType())
                {
                    estLocalPoints += _numRegularPatches * numPointsPerPatch;
                }
            }
            if (_requiresIrregularLocalPoints)
            {
                bool oldEstimate = false;
                int numIrregularPatches = oldEstimate ?
                    _refiner.GetLevel(_refiner.GetMaxLevel()).GetNumFaces() :
                    _numIrregularPatches;

                //
                //  If converting to a basis other than regular, its difficult to
                //  predict the degree of point-sharing that may occur, and in general,
                //  the maximal benefit is small so we don't attempt to compensate for
                //  it here.  If converting to the same basis as regular, roughly half
                //  of the points of the patch are involved in approximating the
                //  irregularity (and cannot be shared) so don't include the source
                //  points that will be used in such patches.
                //
                int numPointsPerPatch = new PatchDescriptor(
                    _patchBuilder.GetIrregularPatchType()).GetNumControlVertices();

                if (options.reuseSourcePoints && (_patchBuilder.GetIrregularPatchType() ==
                                                  _patchBuilder.GetNativePatchType()))
                {
                    numPointsPerPatch /= 2;
                }
                estLocalPoints += numIrregularPatches * numPointsPerPatch;
            }

            //
            //  Its difficult to estimate the differences in number of irregularities
            //  between the vertex topology and a face-varying channel without more
            //  detailed inspection.
            //
            //  A much higher number of fvar-values than vertices is an indication that
            //  the number of differences increases, and that generally lowers the
            //  number of irregular patches due to more regular patches on face-varying
            //  boundaries, but not always.  The use of some interpolation types, e.g.
            //  LINEAR_BOUNDARIES, combined with inf-sharp patches can increase the
            //  number of irregularities significantly.
            //
            if ((fvarChannel >= 0) && (_refiner.GetNumLevels() > 1))
            {
                //
                //  We're seeing face-varying stencil table sizes about 1/4 the size of
                //  the vertex stencil table for what seem like typical cases...
                //
                //  Identify the ratio of fvar-values to vertices that typically leads
                //  to these reductions and reduce the number of expected local points
                //  proportionally.  Use the number of fvar-values at level 1:  level 0
                //  can be misleading as there can be many FEWER than the number of
                //  vertices, but subsequent levels will have at least one unique
                //  fvar-value per vertex, and later levels will have a much higher
                //  percentage shared as a result of refinement.
                //
                int fvc = getRefinerFVarChannel(fvarChannel);

                if (_refiner.GetLevel(1).GetNumFVarValues(fvc) >
                        _refiner.GetLevel(1).GetNumVertices())
                {
                    //  Scale this based on ratio of fvar-values to vertices...
                    float fvarReduction = 0.5f;
                    estLocalPoints = (int)((float)estLocalPoints * fvarReduction);
                }
            }
            return estLocalPoints;
        }

        //  Refiner and Options passed on construction:
        public TopologyRefiner _refiner;
        public PatchTableFactory.Options _options;
        public ConstIndexArray _selectedFaces;

        // Flags indicating the need for processing based on provided options
        public bool _requiresLocalPoints;
        public bool _requiresRegularLocalPoints;
        public bool _requiresIrregularLocalPoints;
        public bool _requiresSharpnessArray;
        public bool _requiresFVarPatches;
        public bool _requiresVaryingPatches;
        public bool _requiresVaryingLocalPoints;
        public bool _buildUniformLinear;

        // The PatchTable being constructed and classes to help its construction:
        public PatchTable _table;
        public PatchBuilder _patchBuilder;
        public PtexIndices _ptexIndices;

        // Vector of tuples for each patch identified during topology traversal
        // and the numbers of irregular and irregular patches identifed:
        public List<PatchTuple> _patches;
        public int _numRegularPatches;
        public int _numIrregularPatches;

        // Vectors for remapping indices of vertices and fvar values as well
        // as the fvar channels (when a subset is chosen)
        public List<int> _levelVertOffsets;
        public List<List<int>> _levelFVarValueOffsets;
        public List<int> _fvarChannelIndices;

        // State and helpers for legacy features
        public bool _requiresLegacyGregoryTables;
        public LegacyGregoryHelper _legacyGregoryHelper;
    }
}
