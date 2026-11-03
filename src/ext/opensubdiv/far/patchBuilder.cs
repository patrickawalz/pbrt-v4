//
//   Copyright 2018 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;

using IndexArray = OpenSubdiv.Vtr.Array<int>;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using LocalIndexArray = OpenSubdiv.Vtr.Array<ushort>;
using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;

namespace OpenSubdiv.Far
{
    //
    //  SourcePatch
    //
    //  This is a local utility class that captures the full local topology of an
    //  arbitrarily irregular patch, i.e. a patch which may have one or all corners
    //  irregular.  Given the topology at each corner the entire collection of
    //  points involved is identified and oriented consistently.
    //
    public class SourcePatch
    {
        public struct Corner
        {
            public ushort _numFaces;   // valence of corner vertex
            public ushort _patchFace;  // location of patch within incident faces

            public bool _boundary;
            public bool _sharp;
            public bool _dart;

            //  For internal bookkeeping -- consider hiding or moving elsewhere
            public bool _sharesWithPrev;
            public bool _sharesWithNext;
            public bool _val2Interior;
            public bool _val2Adjacent;
        }

        public SourcePatch()
        {
            _corners = new Corner[4];
            _numCorners = 0;
            _numSourcePoints = 0;
            _maxValence = 0;
            _maxRingSize = 0;
            _ringSizes = new int[4];
            _localRingSizes = new int[4];
            _localRingOffsets = new int[4];
        }

        //  To be called after all Corners have been initialized (hope to
        //  replace this with alternative constructor at some point)
        public void Finalize(int size)
        {
            //
            //  Determine the sizes of the rings and the total number of points
            //  involved.  In the process, identify which corners share ring points
            //  with their neighbors and accumulate maximal ring sizes and valence:
            //
            bool isQuad = (size == 4);
            int isQuadInt = isQuad ? 1 : 0;

            _numCorners = size;

            _maxValence = 0;
            _maxRingSize = 0;
            _numSourcePoints = _numCorners;

            for (int cIndex = 0; cIndex < _numCorners; ++cIndex)
            {
                //
                //  Need valence-2 information for neighbors as it affects sizing:
                //
                int cPrev = PatchBuilder.fastModN(cIndex + 2 + isQuadInt, _numCorners);
                int cNext = PatchBuilder.fastModN(cIndex + 1, _numCorners);

                bool prevIsVal2Interior = ((_corners[cPrev]._numFaces == 2) &&
                                           !_corners[cPrev]._boundary);
                bool thisIsVal2Interior = ((_corners[cIndex]._numFaces == 2) &&
                                           !_corners[cIndex]._boundary);
                bool nextIsVal2Interior = ((_corners[cNext]._numFaces == 2) &&
                                           !_corners[cNext]._boundary);

                ref Corner corner = ref _corners[cIndex];

                corner._val2Interior = thisIsVal2Interior;
                corner._val2Adjacent = prevIsVal2Interior || nextIsVal2Interior;

                //
                //  General cases are >= 3-face interior and >= 2-face boundary:
                //
                int cornerBoundary = corner._boundary ? 1 : 0;
                if ((corner._numFaces + cornerBoundary) > 2)
                {
                    //
                    //  Quads generally share with both prev and next, but triangles
                    //  never share with prev because of the necessary asymmetry of
                    //  the local ring points.
                    //
                    if (corner._boundary)
                    {
                        corner._sharesWithPrev = isQuad && (corner._patchFace != (corner._numFaces - 1));
                        corner._sharesWithNext = (corner._patchFace != 0);
                    }
                    else if (corner._dart)
                    {
                        ref Corner cP = ref _corners[cPrev];
                        ref Corner cN = ref _corners[cNext];

                        bool cPrevOnDartEdge = cP._boundary && (cP._patchFace == 0);
                        bool cNextOnDartEdge = cN._boundary && (cN._patchFace == cN._numFaces - 1);

                        corner._sharesWithPrev = isQuad && !cPrevOnDartEdge;
                        corner._sharesWithNext = !cNextOnDartEdge;
                    }
                    else
                    {
                        corner._sharesWithPrev = isQuad;
                        corner._sharesWithNext = true;
                    }

                    _ringSizes[cIndex] = corner._numFaces * (1 + isQuadInt) + cornerBoundary;
                    _localRingSizes[cIndex] = _ringSizes[cIndex] - (_numCorners - 1)
                                            - (corner._sharesWithPrev ? 1 : 0) - (corner._sharesWithNext ? 1 : 0);

                    if (corner._val2Adjacent && !corner._boundary)
                    {
                        _localRingSizes[cIndex] -= (prevIsVal2Interior ? 1 : 0);
                        _localRingSizes[cIndex] -= ((nextIsVal2Interior && isQuad) ? 1 : 0);
                    }
                }
                else
                {
                    corner._sharesWithPrev = false;
                    corner._sharesWithNext = false;

                    //  Single-face boundary/corner and valence-2 interior:
                    if (corner._numFaces == 1)
                    {
                        _ringSizes[cIndex] = _numCorners - 1;
                        _localRingSizes[cIndex] = 0;
                    }
                    else
                    {
                        _ringSizes[cIndex] = 2 * (1 + isQuadInt);
                        _localRingSizes[cIndex] = isQuadInt;
                    }
                }
                _localRingOffsets[cIndex] = _numSourcePoints;

                _maxValence = Math.Max(_maxValence, corner._numFaces + cornerBoundary);
                _maxRingSize = Math.Max(_maxRingSize, _ringSizes[cIndex]);

                _numSourcePoints += _localRingSizes[cIndex];
            }
        }

        public int GetNumSourcePoints()
        {
            return _numSourcePoints;
        }

        public int GetMaxValence()
        {
            return _maxValence;
        }

        public int GetMaxRingSize()
        {
            return _maxRingSize;
        }

        public int GetCornerRingSize(int corner)
        {
            return _ringSizes[corner];
        }

        public int GetCornerRingPoints(int corner, int[] ringPoints, int ringOffset = 0)
        {
            bool isQuad = (_numCorners == 4);
            int isQuadInt = isQuad ? 1 : 0;

            int cNext = PatchBuilder.fastModN(corner + 1, _numCorners);
            int cOpp = PatchBuilder.fastModN(corner + 1 + isQuadInt, _numCorners);
            int cPrev = PatchBuilder.fastModN(corner + 2 + isQuadInt, _numCorners);

            //
            //  Assemble the ring in a canonical ordering beginning with the points of
            //  the 2 or 3 other corners of the face followed by the local ring -- with
            //  any shared or compensating points (for valence-2 interior) preceding
            //  and following the points local to the ring.
            //
            int ringSize = 0;

            //  The adjacent corner points:
            ringPoints[ringOffset + ringSize++] = cNext;
            if (isQuad)
            {
                ringPoints[ringOffset + ringSize++] = cOpp;
            }
            ringPoints[ringOffset + ringSize++] = cPrev;

            //  Shared points preceding the local ring points:
            if (_corners[cPrev]._val2Interior && !_corners[corner]._boundary)
            {
                ringPoints[ringOffset + ringSize++] = isQuad ? cOpp : cNext;
            }
            if (_corners[corner]._sharesWithPrev)
            {
                ringPoints[ringOffset + ringSize++] = _localRingOffsets[cPrev] + _localRingSizes[cPrev] - 1;
            }

            //  The local ring points:
            for (int i = 0; i < _localRingSizes[corner]; ++i)
            {
                ringPoints[ringOffset + ringSize++] = _localRingOffsets[corner] + i;
            }

            //  Shared points following the local ring points:
            if (isQuad)
            {
                if (_corners[corner]._sharesWithNext)
                {
                    ringPoints[ringOffset + ringSize++] = _localRingOffsets[cNext];
                }
                if (_corners[cNext]._val2Interior && !_corners[corner]._boundary)
                {
                    ringPoints[ringOffset + ringSize++] = cOpp;
                }
            }
            else
            {
                if (_corners[corner]._sharesWithNext)
                {
                    if (_corners[cNext]._val2Interior && !_corners[corner]._boundary)
                    {
                        ringPoints[ringOffset + ringSize++] = cPrev;
                    }
                    else if (_localRingSizes[cNext] == 0)
                    {
                        ringPoints[ringOffset + ringSize++] = _localRingOffsets[cPrev];
                    }
                    else
                    {
                        ringPoints[ringOffset + ringSize++] = _localRingOffsets[cNext];
                    }
                }
            }
            System.Diagnostics.Debug.Assert(ringSize == _ringSizes[corner]);

            //  The assembled ordering matches the desired ordering if the patch-face
            //  is first, so rotate the assembled ring if that's not the case:
            //
            if (_corners[corner]._patchFace != 0)
            {
                int rotationOffset = ringSize - (1 + isQuadInt) * _corners[corner]._patchFace;
                System.Array.Reverse(ringPoints, ringOffset, rotationOffset);
                System.Array.Reverse(ringPoints, ringOffset + rotationOffset, ringSize - rotationOffset);
                System.Array.Reverse(ringPoints, ringOffset, ringSize);
            }
            return ringSize;
        }

        //  The SourcePatch is fully defined by its Corner members
        public Corner[] _corners;
        public int _numCorners;

        //  Additional members (derived from Corners) to help assemble corner rings:
        public int _numSourcePoints;
        public int _maxValence;
        public int _maxRingSize;

        public int[] _ringSizes;
        public int[] _localRingSizes;
        public int[] _localRingOffsets;
    }

    //
    //  PatchBuilder
    //
    //  This is the main class to assist the identification of limit surface
    //  patches from faces in a TopologyRefiner for assembly into other, larger
    //  datatypes.
    //
    public abstract class PatchBuilder
    {
        //
        //  A PatchBuilder is constructed given a patch "basis" rather than a
        //  "type" to use with the subdivision scheme involved.  The relevant
        //  explicit patch types will be determined from the basis and scheme:
        //
        public enum BasisType
        {
            BASIS_UNSPECIFIED,
            BASIS_REGULAR,
            BASIS_GREGORY,
            BASIS_LINEAR,
            BASIS_BEZIER   // to be supported in future
        }

        //
        //  Required Options specify a patch basis to use for both regular and
        //  irregular patches -- sparing the client the need to repeatedly
        //  specify these for each face considered.  Other options are included
        //  to support legacy approximations:
        //
        public struct Options
        {
            public BasisType regBasisType;
            public BasisType irregBasisType;
            public bool fillMissingBoundaryPoints;
            public bool approxInfSharpWithSmooth;
            public bool approxSmoothCornerWithSharp;
        }

        public struct SingleCreaseInfo
        {
            public int creaseEdgeInFace;
            public float creaseSharpness;
        }

        //
        //  Local helper functions for topology queries:
        //

        //
        //  Inline methods to encapsulate fast and specific modulus operations.
        //  The mod-4 case is trivial.  For mod-N, since we are always doing
        //  the test (i + 1) % N, or (i - 1 + N) % N, the subtraction suffices.
        //
        public static int fastMod4(int x)
        {
            return x & 0x3;
        }

        private static readonly int[] mod3Array = new int[] { 0, 1, 2, 0, 1, 2 };

        public static int fastMod3(int x)
        {
            return mod3Array[x];
        }

        public static int fastModN(int x, int N)
        {
            return (x < N) ? x : (x - N);
        }

        //
        //  Local helper functions for identifying the subset of a ring around a
        //  corner that contributes to a patch -- parameterized by a mask that
        //  indicates what kind of edge is to delimit the span.
        //
        public static Level.ETag getSingularEdgeMask(bool includeAllInfSharpEdges = false)
        {
            Level.ETag eTagMask = new Level.ETag();
            eTagMask.clear();
            eTagMask._boundary = true;
            eTagMask._nonManifold = true;
            eTagMask._infSharp = includeAllInfSharpEdges;
            return eTagMask;
        }

        public static bool isEdgeSingular(Level level, FVarLevel fvarLevel,
                                          int eIndex, Level.ETag eTagMask)
        {
            Level.ETag eTag = level.getEdgeTag(eIndex);
            if (fvarLevel != null)
            {
                eTag = fvarLevel.getEdgeTag(eIndex).combineWithLevelETag(eTag);
            }
            return (eTag.getBits() & eTagMask.getBits()) > 0;
        }

        public static void identifyManifoldCornerSpan(Level level, int fIndex,
                                                      int fCorner, Level.ETag eTagMask,
                                                      ref Level.VSpan vSpan, int fvc = -1)
        {
            FVarLevel fvarLevel = (fvc < 0) ? null : level.getFVarLevel(fvc);

            ConstIndexArray fVerts = level.getFaceVertices(fIndex);
            ConstIndexArray fEdges = level.getFaceEdges(fIndex);

            ConstIndexArray vEdges = level.getVertexEdges(fVerts[fCorner]);
            int nEdges = vEdges.size();

            int iLeadingStart = vEdges.FindIndex(fEdges[fCorner]);
            int iTrailingStart = fastModN(iLeadingStart + 1, nEdges);

            vSpan.clear();
            vSpan._numFaces = 1;
            vSpan._cornerInSpan = 0;

            int iLeading = iLeadingStart;
            while (!isEdgeSingular(level, fvarLevel, vEdges[iLeading], eTagMask))
            {
                ++vSpan._numFaces;
                ++vSpan._cornerInSpan;
                iLeading = fastModN(iLeading + nEdges - 1, nEdges);
                if (iLeading == iTrailingStart)
                {
                    break;
                }
            }

            int iTrailing = iTrailingStart;
            if (iTrailing != iLeading)
            {
                while (!isEdgeSingular(level, fvarLevel, vEdges[iTrailing], eTagMask))
                {
                    ++vSpan._numFaces;
                    iTrailing = fastModN(iTrailing + 1, nEdges);
                    if (iTrailing == iLeadingStart)
                    {
                        break;
                    }
                }
            }
            vSpan._startFace = (ushort)iLeading;
        }

        public static void identifyNonManifoldCornerSpan(Level level, int fIndex,
                                                         int fCorner, Level.ETag eTagMask,
                                                         ref Level.VSpan vSpan, int fvc = -1)
        {
            FVarLevel fvarLevel = (fvc < 0) ? null : level.getFVarLevel(fvc);

            ConstIndexArray fEdges = level.getFaceEdges(fIndex);

            int eLeadingStart = fEdges[fCorner];
            int eTrailingStart = fEdges[(fCorner + fEdges.size() - 1) % fEdges.size()];

            vSpan.clear();
            vSpan._numFaces = 1;
            vSpan._cornerInSpan = 0;

            int startFace = fIndex;
            int startCorner = fCorner;

            //  Traverse clockwise to find the leading edge of the span -- keeping
            //  track of the starting face and corner to use later:
            int fLeading = fIndex;
            int eLeading = eLeadingStart;
            while (!isEdgeSingular(level, fvarLevel, eLeading, eTagMask))
            {
                ++vSpan._numFaces;
                ++vSpan._cornerInSpan;

                //  Identify the face opposite the current leading edge, identify
                //  the edges of the next face, and then the next leading edge:
                //
                ConstIndexArray eFaces = level.getEdgeFaces(eLeading);
                System.Diagnostics.Debug.Assert(eFaces.size() == 2);
                fLeading = (eFaces[0] == fLeading) ? eFaces[1] : eFaces[0];
                fEdges = level.getFaceEdges(fLeading);

                startFace = fLeading;
                startCorner = (fEdges.FindIndex(eLeading) + 1) % fEdges.size();

                eLeading = fEdges[startCorner];
                if (eLeading == eTrailingStart)
                {
                    vSpan._periodic = !isEdgeSingular(level, fvarLevel, eLeading, eTagMask);
                    break;
                }
            }

            //  Traverse counter-clockwise to find the trailing edge of the span (unless
            //  the above traversal reached where it started):
            int fTrailing = fIndex;
            int eTrailing = eTrailingStart;
            if (eTrailing != eLeading)
            {
                while (!isEdgeSingular(level, fvarLevel, eTrailing, eTagMask))
                {
                    ++vSpan._numFaces;

                    //  Identify the face opposite the current trailing edge, identify
                    //  the edges of the next face, and then the next trailing edge:
                    //
                    ConstIndexArray eFaces = level.getEdgeFaces(eTrailing);
                    System.Diagnostics.Debug.Assert(eFaces.size() == 2);
                    fTrailing = (eFaces[0] == fTrailing) ? eFaces[1] : eFaces[0];
                    fEdges = level.getFaceEdges(fTrailing);

                    eTrailing = fEdges[(fEdges.FindIndex(eTrailing) + fEdges.size() - 1) % fEdges.size()];
                    if (eTrailing == eLeadingStart)
                    {
                        vSpan._periodic = !isEdgeSingular(level, fvarLevel, eTrailing, eTagMask);
                        break;
                    }
                }
            }

            //  Identify the span's starting point relative to the incident components
            //  of the vertex, using the start face and corner of the leading edge:
            //
            int vIndex = level.getFaceVertices(fIndex)[fCorner];

            ConstIndexArray vFaces = level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(vIndex);

            vSpan._startFace = (ushort)vFaces.size();
            for (int i = 0; i < vFaces.size(); ++i)
            {
                if ((vFaces[i] == startFace) && (vInFaces[i] == startCorner))
                {
                    vSpan._startFace = (ushort)i;
                    break;
                }
            }
            System.Diagnostics.Debug.Assert(vSpan._startFace < vFaces.size());
        }

        //  Simple conveniences for the span search functions:
        public static int countManifoldCornerSpan(Level level, int fIndex, int fCorner,
                                                  Level.ETag eTagMask, int fvc = -1)
        {
            Level.VSpan vSpan = new Level.VSpan();
            identifyManifoldCornerSpan(level, fIndex, fCorner, eTagMask, ref vSpan, fvc);
            return vSpan._numFaces;
        }

        public static int countNonManifoldCornerSpan(Level level, int fIndex, int fCorner,
                                                     Level.ETag eTagMask, int fvc = -1)
        {
            Level.VSpan vSpan = new Level.VSpan();
            identifyNonManifoldCornerSpan(level, fIndex, fCorner, eTagMask, ref vSpan, fvc);
            return vSpan._numFaces;
        }

        //
        //  Gathering the one-ring of vertices from triangles surrounding a vertex:
        //      - the neighborhood of the vertex is assumed to be tri-regular (manifold)
        //
        //  Ordering of resulting vertices:
        //      The surrounding one-ring follows the ordering of the incident faces.  For each
        //  incident tri, the vertex opposite its leading edge is added.  If the vertex is on a
        //  boundary, a second vertex on the boundary edge will be contributed from the last face.
        //
        public static int gatherTriRegularRingAroundVertex(Level level,
            int vIndex, int[] ringPoints, int fvarChannel)
        {
            ConstIndexArray vEdges = level.getVertexEdges(vIndex);

            ConstIndexArray vFaces = level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(vIndex);

            bool isBoundary = (vEdges.size() > vFaces.size());

            int ringIndex = 0;
            for (int i = 0; i < vFaces.size(); ++i)
            {
                //
                //  For each tri, we want the the vertex at the end of the leading edge:
                //
                ConstIndexArray fPoints = (fvarChannel < 0)
                                        ? level.getFaceVertices(vFaces[i])
                                        : level.getFaceFVarValues(vFaces[i], fvarChannel);

                int vInThisFace = vInFaces[i];

                ringPoints[ringIndex++] = fPoints[fastMod3(vInThisFace + 1)];

                if (isBoundary && (i == (vFaces.size() - 1)))
                {
                    ringPoints[ringIndex++] = fPoints[fastMod3(vInThisFace + 2)];
                }
            }
            return ringIndex;
        }

        public static int gatherRegularPartialRingAroundVertex(Level level,
            int vIndex, Level.VSpan span, int[] ringPoints, int fvarChannel)
        {
            bool isManifold = !level.isVertexNonManifold(vIndex);

            ConstIndexArray vFaces = level.getVertexFaces(vIndex);
            ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(vIndex);

            int nFaces = span._numFaces;
            int startFace = span._startFace;

            int nextFace = vFaces[startFace];
            int vInNextFace = vInFaces[startFace];

            int ringIndex = 0;
            for (int i = 0; i < nFaces; ++i)
            {
                int thisFace = nextFace;
                int vInThisFace = vInNextFace;

                ConstIndexArray fPoints = (fvarChannel < 0)
                                        ? level.getFaceVertices(thisFace)
                                        : level.getFaceFVarValues(thisFace, fvarChannel);

                bool isQuad = (fPoints.size() == 4);
                if (isQuad)
                {
                    ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 1)];
                    ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 2)];
                }
                else
                {
                    ringPoints[ringIndex++] = fPoints[fastMod3(vInThisFace + 1)];
                }

                if (i == (nFaces - 1))
                {
                    if (!span._periodic)
                    {
                        if (isQuad)
                        {
                            ringPoints[ringIndex++] = fPoints[fastMod4(vInThisFace + 3)];
                        }
                        else
                        {
                            ringPoints[ringIndex++] = fPoints[fastMod3(vInThisFace + 2)];
                        }
                    }
                }
                else if (isManifold)
                {
                    int iNext = fastModN(startFace + i + 1, vFaces.size());

                    nextFace = vFaces[iNext];
                    vInNextFace = vInFaces[iNext];
                }
                else
                {
                    int nextInThisFace = fastModN(vInThisFace + fPoints.size() - 1, fPoints.size());

                    int nextEdge = level.getFaceEdges(thisFace)[nextInThisFace];
                    ConstIndexArray eFaces = level.getEdgeFaces(nextEdge);

                    nextFace = (eFaces[0] == thisFace) ? eFaces[1] : eFaces[0];
                    vInNextFace = level.getFaceEdges(nextFace).FindIndex(nextEdge);
                }
            }
            return ringIndex;
        }

        //
        //  Functions to encode/decode the 5-bit boundary mask for a triangular patch
        //  from the two 3-bit boundary vertex and bounday edge masks.  When referring
        //  to a "boundary vertex" in the encoded bits, we are referring to a vertex on
        //  a boundary while its incident edges of the triangle are not boundaries --
        //  topologically distinct from a vertex at the end of a boundary edge.
        //
        //  The 5-bit encoding is as follows:
        //
        //      - the upper 2 bits indicate how to interpret the lower 3 bits:
        //          0 - as boundary edges only (all boundary vertices are implicit)
        //          1 - as "boundary vertices" only (no boundary edges)
        //          2 - a single boundary edge with opposite "boundary vertex"
        //
        //      - the lower 3 bits are set according to boundary features present
        //
        //  There are a total of 18 possible boundary configurations:
        //
        //      - no boundaries at all (1 case)
        //      - one boundary edge (3 cases)
        //      - two boundary edges (3 cases)
        //      - three boundary edges (1 case)
        //      - one boundary vertex (3 cases)
        //      - two boundary vertices (3 cases)
        //      - three boundarey vertices (1 case)
        //      - one boundary edge with opposite boundary vertex (3 cases)
        //
        public static int unpackTriBoundaryMaskLower(int mask)
        {
            return mask & 0x7;
        }

        public static int unpackTriBoundaryMaskUpper(int mask)
        {
            return (mask >> 3) & 0x3;
        }

        public static int packTriBoundaryMask(int upper, int lower)
        {
            return (upper << 3) | lower;
        }

        public static int encodeTriBoundaryMask(int eBits, int vBits)
        {
            int upperBits = 0;
            int lowerBits = eBits;

            if (vBits != 0)
            {
                if (eBits == 0)
                {
                    upperBits = 1;
                    lowerBits = vBits;
                }
                else if ((vBits == 7) && ((eBits == 1) || (eBits == 2) || (eBits == 4)))
                {
                    upperBits = 2;
                    lowerBits = eBits;
                }
            }
            return packTriBoundaryMask(upperBits, lowerBits);
        }

        private static readonly int[] eBitsToVBits = new int[] { 0, 3, 6, 7, 5, 7, 7, 7 };

        public static void decodeTriBoundaryMask(int mask, ref int eBits, ref int vBits)
        {
            int lowerBits = unpackTriBoundaryMaskLower(mask);
            int upperBits = unpackTriBoundaryMaskUpper(mask);

            switch (upperBits)
            {
                case 0:
                    eBits = lowerBits;
                    vBits = eBitsToVBits[eBits];
                    break;
                case 1:
                    eBits = 0;
                    vBits = lowerBits;
                    break;
                case 2:
                    eBits = lowerBits;
                    vBits = 0x7;
                    break;
            }
        }

        public static int getNextFaceInVertFaces(Level level, int thisFaceInVFaces,
                                                 ConstIndexArray vFaces,
                                                 ConstLocalIndexArray vInFaces,
                                                 bool manifold, ref int vInNextFace)
        {
            int nextFace;
            if (manifold)
            {
                int nextFaceInVFaces = fastModN(thisFaceInVFaces + 1, vFaces.size());

                nextFace = vFaces[nextFaceInVFaces];
                vInNextFace = vInFaces[nextFaceInVFaces];
            }
            else
            {
                int thisFace = vFaces[thisFaceInVFaces];
                int vInThisFace = vInFaces[thisFaceInVFaces];

                ConstIndexArray fEdges = level.getFaceEdges(thisFace);

                int nextEdge = fEdges[fastModN((vInThisFace + fEdges.size() - 1), fEdges.size())];

                ConstIndexArray eFaces = level.getEdgeFaces(nextEdge);
                System.Diagnostics.Debug.Assert(eFaces.size() == 2);

                nextFace = (eFaces[0] == thisFace) ? eFaces[1] : eFaces[0];

                int edgeInNextFace = level.getFaceEdges(nextFace).FindIndex(nextEdge);

                vInNextFace = edgeInNextFace;
            }
            return nextFace;
        }

        public static int getPrevFaceInVertFaces(Level level, int thisFaceInVFaces,
                                                 ConstIndexArray vFaces,
                                                 ConstLocalIndexArray vInFaces,
                                                 bool manifold, ref int vInPrevFace)
        {
            int prevFace;
            if (manifold)
            {
                int prevFaceInVFaces = (thisFaceInVFaces != 0 ? thisFaceInVFaces : vFaces.size()) - 1;

                prevFace = vFaces[prevFaceInVFaces];
                vInPrevFace = vInFaces[prevFaceInVFaces];
            }
            else
            {
                int thisFace = vFaces[thisFaceInVFaces];
                int vInThisFace = vInFaces[thisFaceInVFaces];

                ConstIndexArray fEdges = level.getFaceEdges(thisFace);

                int prevEdge = fEdges[vInThisFace];

                ConstIndexArray eFaces = level.getEdgeFaces(prevEdge);
                System.Diagnostics.Debug.Assert(eFaces.size() == 2);

                prevFace = (eFaces[0] == thisFace) ? eFaces[1] : eFaces[0];

                int edgeInPrevFace = level.getFaceEdges(prevFace).FindIndex(prevEdge);

                vInPrevFace = fastModN(edgeInPrevFace + 1, fEdges.size());
            }
            return prevFace;
        }

        public static ConstIndexArray getFacePoints(Level level, int faceIndex, int fvarChannel)
        {
            return (fvarChannel < 0) ? level.getFaceVertices(faceIndex)
                                     : level.getFaceFVarValues(faceIndex, fvarChannel);
        }

        //
        //  Factory method and constructor:
        //
        public static PatchBuilder Create(TopologyRefiner refiner, Options options)
        {
            switch (refiner.GetSchemeType())
            {
                case SchemeType.SCHEME_BILINEAR:
                    return new BilinearPatchBuilder(refiner, options);
                case SchemeType.SCHEME_CATMARK:
                    return new CatmarkPatchBuilder(refiner, options);
                case SchemeType.SCHEME_LOOP:
                    return new LoopPatchBuilder(refiner, options);
            }
            System.Diagnostics.Debug.Assert(false, "Unrecognized Sdc::SchemeType for PatchBuilder construction");
            return null;
        }

        protected PatchBuilder(TopologyRefiner refiner, Options options)
        {
            _refiner = refiner;
            _options = options;

            //
            //  Initialize members with properties of the subdivision scheme and patch
            //  choices for quick retrieval:
            //
            _schemeType = refiner.GetSchemeType();
            _schemeRegFaceSize = SchemeTypeTraits.GetRegularFaceSize(_schemeType);
            _schemeIsLinear = SchemeTypeTraits.GetLocalNeighborhoodSize(_schemeType) == 0;

            //  Initialization of members involving patch types is deferred to the
            //  subclass for the scheme
        }

        //
        //  High-level queries related to the subdivision scheme of the refiner, the
        //  patch types associated with it and those chosen to represent its faces:
        //
        public int GetRegularFaceSize()
        {
            return _schemeRegFaceSize;
        }

        public BasisType GetRegularBasisType()
        {
            return _options.regBasisType;
        }

        public BasisType GetIrregularBasisType()
        {
            return _options.irregBasisType;
        }

        public PatchDescriptor.Type GetRegularPatchType()
        {
            return _regPatchType;
        }

        public PatchDescriptor.Type GetIrregularPatchType()
        {
            return _irregPatchType;
        }

        public PatchDescriptor.Type GetNativePatchType()
        {
            return _nativePatchType;
        }

        public PatchDescriptor.Type GetLinearPatchType()
        {
            return _linearPatchType;
        }

        public bool DoesFaceVaryingPatchMatch(int level, int face, int fvc)
        {
            return _refiner.getLevel(level).doesFaceFVarTopologyMatch(face, fvc);
        }

        //
        //  Topology inspections methods for a particular face in the hierarchy:
        //
        public bool IsFaceAPatch(int levelIndex, int faceIndex)
        {
            Level level = _refiner.getLevel(levelIndex);

            //  Faces tagged as holes are not patches (no limit surface)
            if (_refiner.HasHoles() && level.isFaceHole(faceIndex))
            {
                return false;
            }

            //  Base faces are patches unless an irregular face or incident one:
            if (levelIndex == 0)
            {
                if (_schemeIsLinear)
                {
                    return level.getFaceVertices(faceIndex).size() == _schemeRegFaceSize;
                }
                else
                {
                    return !level.getFaceCompositeVTag(faceIndex)._incidIrregFace;
                }
            }

            //  Refined faces are patches unless "incomplete", i.e. they exist solely to
            //  support an adjacent patch (can only use the more commonly used combined
            //  VTag for all corners for quads -- need a Refinement tag for tris):
            if (_schemeRegFaceSize == 4)
            {
                return !level.getFaceCompositeVTag(faceIndex)._incomplete;
            }
            else
            {
                Refinement refinement = _refiner.getRefinement(levelIndex - 1);
                return !refinement.getChildFaceTag(faceIndex)._incomplete;
            }
        }

        public bool IsFaceALeaf(int levelIndex, int faceIndex)
        {
            //  All faces in the last level are leaves
            if (levelIndex < _refiner.GetMaxLevel())
            {
                //  Faces selected for further refinement are not leaves
                if (_refiner.getRefinement(levelIndex).
                                getParentFaceSparseTag(faceIndex)._selected)
                {
                    return false;
                }
            }
            return true;
        }

        public bool IsPatchRegular(int levelIndex, int faceIndex, int fvc = -1)
        {
            if (_schemeIsLinear)
            {
                //  The previous face-is-a-patch test precludes an irregular patch
                return true;
            }

            Level level = _refiner.getLevel(levelIndex);

            //
            //  Retrieve individual VTags for the four corners and combine, as we may
            //  need the individual VTags for closer inspection.
            //
            //  Immediately return regular status based on xordinary bit if completely
            //  smooth at all corners, i.e. no inf-sharp corners or boundaries present
            //  (which also rules out the presence of non-manifold vertices)
            //
            Level.VTag[] vTags = new Level.VTag[4];
            level.getFaceVTags(faceIndex, vTags, fvc);

            Level.VTag fCompVTag = Level.VTag.BitwiseOr(vTags, _schemeRegFaceSize);

            if (!fCompVTag._infSharp && !fCompVTag._infSharpEdges)
            {
                return !fCompVTag._xordinary;
            }

            //
            //  Irregular features will exist at corners that are either non-manifold,
            //  extra-ordinary, or that are tagged with inf-sharp irregularities (may
            //  be regular even if extra-ordinary or vice versa -- depending on the
            //  specific inf-sharp edges present around the vertex).
            //
            //  Build a bit-mask for the irregular features -- if the composite tag
            //  has no irregular features, we can immediately return.
            //
            bool testInfSharpFeatures = !_options.approxInfSharpWithSmooth;

            Level.VTag irregFeatureTag = new Level.VTag(0);
            irregFeatureTag._nonManifold = true;
            irregFeatureTag._xordinary = true;
            irregFeatureTag._infIrregular = testInfSharpFeatures;

            int irregFeatureMask = irregFeatureTag.getBits();

            if ((fCompVTag.getBits() & irregFeatureMask) == 0)
            {
                return true;
            }

            //
            //  If the irregular feature is isolated, we can use the combined corner
            //  tags to determine regularity -- unless specified options require a
            //  closer inspection of the single irregular feature:
            //
            bool mayHaveIrregFaces = _refiner._hasIrregFaces;
            int needsExtraIsoLevel = (fCompVTag._xordinary && mayHaveIrregFaces) ? 1 : 0;

            bool featureIsIsolated = levelIndex > needsExtraIsoLevel;
            if (featureIsIsolated)
            {
                bool featureRequiresFurtherInspection = fCompVTag._nonManifold ||
                        (_options.approxSmoothCornerWithSharp &&
                                fCompVTag._xordinary && fCompVTag._boundary) ||
                        (testInfSharpFeatures &&
                                fCompVTag._infIrregular && fCompVTag._infSharpEdges);

                if (!featureRequiresFurtherInspection)
                {
                    if (testInfSharpFeatures)
                    {
                        return !fCompVTag._infIrregular;
                    }
                    else
                    {
                        return !fCompVTag._xordinary;
                    }
                }
            }

            //
            //  Inspect all or the single isolated corner.  Use the irregular feature
            //  mask to quickly skip regular corners and return on the first irregular
            //  feature encountered:
            //
            int nRegBoundaryFaces = (_schemeRegFaceSize == 4) ? 2 : 3;

            for (int i = 0; i < _schemeRegFaceSize; ++i)
            {
                Level.VTag vTag = vTags[i];

                if ((vTag.getBits() & irregFeatureMask) == 0)
                {
                    continue;
                }

                if (vTag._nonManifold)
                {
                    //  Identify the span containing the face and assess:
                    int nSpanFaces = countNonManifoldCornerSpan(level, faceIndex, i,
                        getSingularEdgeMask(testInfSharpFeatures), fvc);
                    if (vTag._infSharp)
                    {
                        if (nSpanFaces != 1)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (nSpanFaces != (nRegBoundaryFaces))
                        {
                            return false;
                        }
                    }
                    continue;
                }

                if (vTag._xordinary)
                {
                    //  A smooth xordinary vertex is always irregular
                    if (!vTag._infSharpEdges)
                    {
                        return false;
                    }

                    //  A smooth corner vertex may be interpreted as regular:
                    if (_options.approxSmoothCornerWithSharp &&
                            vTag._boundary && !vTag._infSharp)
                    {
                        Level.ETag[] eTags = new Level.ETag[4];
                        level.getFaceETags(faceIndex, eTags, fvc);
                        int iPrev = (i != 0) ? (i - 1) : (_schemeRegFaceSize - 1);
                        if (eTags[i]._boundary && eTags[iPrev]._boundary)
                        {
                            continue;
                        }
                    }

                    //  All others irregular, unless further inspecting inf-sharp
                    if (!testInfSharpFeatures)
                    {
                        return false;
                    }
                }

                if (vTag._infIrregular)
                {
                    //  Inf-sharp vertex with no inf-sharp edges:
                    if (!vTag._infSharpEdges)
                    {
                        return false;
                    }

                    //  Irregular boundary crease:
                    if (vTag._infSharpCrease && vTag._boundary)
                    {
                        return false;
                    }

                    //  Identify the span containing the face and assess:
                    int nSpanFaces = countManifoldCornerSpan(level, faceIndex, i,
                        getSingularEdgeMask(true), fvc);
                    if (vTag._infSharpCrease)
                    {
                        if (nSpanFaces != (nRegBoundaryFaces))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        if (nSpanFaces != 1)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public int GetRegularPatchBoundaryMask(int levelIndex, int faceIndex, int fvarChannel = -1)
        {
            if (_schemeIsLinear)
            {
                //  Boundaries for patches not dependent on the 1-ring are ignored
                return 0;
            }

            Level level = _refiner.getLevel(levelIndex);

            //  Gather tags for the four corners and edges.  Regardless of the
            //  options for treating non-manifold or inf-sharp patches, for a
            //  regular patch we can infer all that we need from these tags:
            //
            Level.VTag[] vTags = new Level.VTag[4];
            Level.ETag[] eTags = new Level.ETag[4];

            level.getFaceVTags(faceIndex, vTags, fvarChannel);

            Level.VTag fTag = Level.VTag.BitwiseOr(vTags, _schemeRegFaceSize);
            if (!fTag._infSharpEdges)
            {
                return 0;
            }

            level.getFaceETags(faceIndex, eTags, fvarChannel);

            //
            //  Test the edge tags for boundary features.  For quads this is
            //  sufficient, so return the edge bits.
            //
            bool testInfSharpFeatures = !_options.approxInfSharpWithSmooth;

            Level.ETag eFeatureTag = new Level.ETag(0);
            eFeatureTag._boundary = true;
            eFeatureTag._infSharp = testInfSharpFeatures;
            eFeatureTag._nonManifold = true;

            int eFeatureMask = eFeatureTag.getBits();

            int eBits = ((((eTags[0].getBits() & eFeatureMask) != 0) ? 1 : 0) << 0) |
                        ((((eTags[1].getBits() & eFeatureMask) != 0) ? 1 : 0) << 1) |
                        ((((eTags[2].getBits() & eFeatureMask) != 0) ? 1 : 0) << 2);
            if (_schemeRegFaceSize == 4)
            {
                eBits |= ((((eTags[3].getBits() & eFeatureMask) != 0) ? 1 : 0) << 3);
                return eBits;
            }

            //
            //  For triangles, test the vertex tags for boundary features (we
            //  can have boundary vertices with no boundary edges) and return
            //  the encoded result of the two sets of 3 bits:
            //
            Level.VTag vFeatureTag = new Level.VTag(0);
            vFeatureTag._boundary = true;
            vFeatureTag._infSharpEdges = testInfSharpFeatures;
            vFeatureTag._nonManifold = true;

            int vFeatureMask = vFeatureTag.getBits();

            int vBits = ((((vTags[0].getBits() & vFeatureMask) != 0) ? 1 : 0) << 0) |
                        ((((vTags[1].getBits() & vFeatureMask) != 0) ? 1 : 0) << 1) |
                        ((((vTags[2].getBits() & vFeatureMask) != 0) ? 1 : 0) << 2);

            return ((eBits != 0) || (vBits != 0)) ? encodeTriBoundaryMask(eBits, vBits) : 0;
        }

        public void GetIrregularPatchCornerSpans(int levelIndex, int faceIndex,
                Level.VSpan[] cornerSpans, int fvarChannel = -1)
        {
            Level level = _refiner.getLevel(levelIndex);

            //  Retrieve tags and identify other information for the corner vertices:
            Level.VTag[] vTags = new Level.VTag[4];
            level.getFaceVTags(faceIndex, vTags, fvarChannel);

            FVarLevel.ValueTag[] fvarTags = new FVarLevel.ValueTag[4];
            if (fvarChannel >= 0)
            {
                level.getFVarLevel(fvarChannel).getFaceValueTags(faceIndex, fvarTags);
            }

            //
            //  For each corner, identify the span of interest around the vertex,
            //  using the complete neighborhood when possible (which does not require
            //  a search):
            //
            bool testInfSharpFeatures = !_options.approxInfSharpWithSmooth;

            Level.ETag singularEdgeMask = getSingularEdgeMask(testInfSharpFeatures);

            for (int i = 0; i < _schemeRegFaceSize; ++i)
            {
                Level.VTag vTag = vTags[i];

                bool isNonManifold = vTag._nonManifold;

                bool isFVarMisMatch = (fvarChannel >= 0) && fvarTags[i]._mismatch;

                bool testInfSharpEdges = testInfSharpFeatures &&
                        vTag._infSharpEdges && (vTag._rule != (ushort)Crease.Rule.RULE_DART);

                //
                //  Identify a discontinuity in the one-ring, otherwise use an
                //  unassigned (cleared) span to indicate use of the full ring:
                //
                if (testInfSharpEdges || isFVarMisMatch || isNonManifold)
                {
                    if (isNonManifold)
                    {
                        identifyNonManifoldCornerSpan(level, faceIndex,
                                i, singularEdgeMask, ref cornerSpans[i], fvarChannel);
                    }
                    else
                    {
                        identifyManifoldCornerSpan(level, faceIndex,
                                i, singularEdgeMask, ref cornerSpans[i], fvarChannel);
                    }
                }
                else
                {
                    cornerSpans[i].clear();
                }

                //  Sharpen the span if a corner or subject to inf-sharp features:
                if (vTag._corner)
                {
                    //  Corners tagged in FVar space need additional qualification:
                    if (isFVarMisMatch)
                    {
                        cornerSpans[i]._sharp = (cornerSpans[i]._numFaces == 1) || isNonManifold;
                    }
                    else
                    {
                        cornerSpans[i]._sharp = true;
                    }
                }
                else if (isNonManifold)
                {
                    cornerSpans[i]._sharp = vTag._infSharp;
                }
                else if (testInfSharpFeatures)
                {
                    cornerSpans[i]._sharp = testInfSharpEdges
                            ? !vTag._infSharpCrease : vTag._infSharp;
                }

                //  Legacy option -- reinterpret a smooth corner as sharp:
                bool smoothCorner = !cornerSpans[i]._sharp;
                if (smoothCorner && _options.approxSmoothCornerWithSharp && vTag._xordinary &&
                        vTag._boundary && !vTag._infSharp && !vTag._nonManifold)
                {
                    int nFaces = cornerSpans[i].isAssigned()
                        ? cornerSpans[i]._numFaces
                        : level.getVertexFaces(level.getFaceVertices(faceIndex)[i]).size();
                    cornerSpans[i]._sharp = (nFaces == 1);
                }
            }
        }

        //
        //  Patch-level control point retrieval and methods for converting source
        //  points to a set of local points in a different basis
        //
        protected int getRegularFacePoints(int levelIndex, int faceIndex,
                Span<int> patchPoints, int fvarChannel)
        {
            Level level = _refiner.getLevel(levelIndex);

            ConstIndexArray facePoints = (fvarChannel < 0)
                                       ? level.getFaceVertices(faceIndex)
                                       : level.getFaceFVarValues(faceIndex, fvarChannel);

            for (int i = 0; i < facePoints.size(); ++i)
            {
                patchPoints[i] = facePoints[i];
            }
            return facePoints.size();
        }

        private static readonly int[][] quadPatchPointsPerCorner = new int[][]
        {
            new int[] {  5,  4,  0,  1 },
            new int[] {  6,  2,  3,  7 },
            new int[] { 10, 11, 15, 14 },
            new int[] {  9, 13, 12,  8 }
        };

        protected int getQuadRegularPatchPoints(int levelIndex, int faceIndex,
                int regBoundaryMask, Span<int> patchPoints,
                int fvarChannel)
        {
            if (regBoundaryMask < 0)
            {
                regBoundaryMask = GetRegularPatchBoundaryMask(levelIndex, faceIndex);
            }
            bool interiorPatch = (regBoundaryMask == 0);

            int eMask = regBoundaryMask;

            Level level = _refiner.getLevel(levelIndex);

            ConstIndexArray fVerts = level.getFaceVertices(faceIndex);
            ConstIndexArray fPoints = getFacePoints(level, faceIndex, fvarChannel);

            int boundaryPoint = FarTypes.INDEX_INVALID;
            if (!interiorPatch && _options.fillMissingBoundaryPoints)
            {
                boundaryPoint = fPoints[0];
            }

            for (int i = 0; i < 4; ++i)
            {
                int v = fVerts[i];

                int[] cornerPointIndices = quadPatchPointsPerCorner[i];

                ConstIndexArray vFaces = level.getVertexFaces(v);
                ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(v);

                //  Identify the patch face in the ring of incident faces.  (There's
                //  no need to deal with multiple occurrences of the face in the ring
                //  here -- as can happen with non-manifold vertices -- as such corners
                //  will be sharp, regular boundaries and not need the incident faces.)
                //
                int f = faceIndex;
                int fInVFaces = vFaces.FindIndex(f);

                //  Identify the exterior points for this corner from the appropriate
                //  incident face:
                //
                bool interiorCorner = interiorPatch || (((eMask & (1 << i)) |
                                                         (eMask & (1 << fastMod4(i + 3)))) == 0);
                if (interiorCorner)
                {
                    int fOppInVFaces = fastMod4(fInVFaces + 2);

                    int fOpp = vFaces[fOppInVFaces];
                    int vInFOpp = vInFaces[fOppInVFaces];
                    ConstIndexArray fOppPoints = getFacePoints(level, fOpp, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = fOppPoints[fastMod4(vInFOpp + 1)];
                    patchPoints[cornerPointIndices[2]] = fOppPoints[fastMod4(vInFOpp + 2)];
                    patchPoints[cornerPointIndices[3]] = fOppPoints[fastMod4(vInFOpp + 3)];
                }
                else if (((eMask & (1 << i)) != 0) && ((eMask & (1 << fastMod4(i + 3))) != 0))
                {
                    //  Two indicent boundary edges -- no incident faces
                    patchPoints[cornerPointIndices[1]] = boundaryPoint;
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = boundaryPoint;
                }
                else if ((eMask & (1 << i)) != 0)
                {
                    //  Leading/outgoing boundary edge -- need next face:
                    int vInFNext = 0;
                    int fNext = getNextFaceInVertFaces(level, fInVFaces, vFaces, vInFaces,
                                           !level.getVertexTag(v)._nonManifold, ref vInFNext);

                    ConstIndexArray fNextPoints = getFacePoints(level, fNext, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = fNextPoints[fastMod4(vInFNext + 3)];
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = boundaryPoint;
                }
                else
                {
                    //  Trailing/incoming boundary edge -- need previous face:
                    int vInFPrev = 0;
                    int fPrev = getPrevFaceInVertFaces(level, fInVFaces, vFaces, vInFaces,
                                           !level.getVertexTag(v)._nonManifold, ref vInFPrev);

                    ConstIndexArray fPrevPoints = getFacePoints(level, fPrev, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = boundaryPoint;
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = fPrevPoints[fastMod4(vInFPrev + 1)];
                }
                patchPoints[cornerPointIndices[0]] = fPoints[i];
            }
            return 16;
        }

        private static readonly int[][] triPatchPointsPerCorner = new int[][]
        {
            new int[] { 4, 7, 3, 0 },
            new int[] { 5, 1, 2, 6 },
            new int[] { 8, 9, 11, 10 }
        };

        protected int getTriRegularPatchPoints(int levelIndex, int faceIndex,
                int regBoundaryMask, Span<int> patchPoints,
                int fvarChannel)
        {
            if (regBoundaryMask < 0)
            {
                regBoundaryMask = GetRegularPatchBoundaryMask(levelIndex, faceIndex);
            }
            bool interiorPatch = (regBoundaryMask == 0);

            int vMask = 0;
            int eMask = 0;
            if (!interiorPatch)
            {
                decodeTriBoundaryMask(regBoundaryMask, ref eMask, ref vMask);
            }

            Level level = _refiner.getLevel(levelIndex);

            ConstIndexArray fVerts = level.getFaceVertices(faceIndex);
            ConstIndexArray fPoints = getFacePoints(level, faceIndex, fvarChannel);

            int boundaryPoint = FarTypes.INDEX_INVALID;
            if (!interiorPatch && _options.fillMissingBoundaryPoints)
            {
                boundaryPoint = fPoints[0];
            }

            for (int i = 0; i < 3; ++i)
            {
                int v = fVerts[i];

                int[] cornerPointIndices = triPatchPointsPerCorner[i];

                ConstIndexArray vFaces = level.getVertexFaces(v);
                ConstLocalIndexArray vInFaces = level.getVertexFaceLocalIndices(v);

                //  Identify the patch face in the ring of incident faces.  (There's
                //  no need to deal with multiple occurrences of the face in the ring
                //  here -- as can happen with non-manifold vertices -- as such corners
                //  will be sharp, regular boundaries and not need the incident faces.)
                //
                int f = faceIndex;
                int fInVFaces = vFaces.FindIndex(f);

                //  Identify the exterior points for this corner from the appropriate
                //  incident faces:
                //
                bool interiorCorner = interiorPatch || ((vMask & (1 << i)) == 0);
                if (interiorCorner)
                {
                    int f2InVFaces = fastModN(fInVFaces + 2, 6);
                    int f3InVFaces = fastModN(fInVFaces + 3, 6);

                    int f2 = vFaces[f2InVFaces];
                    int f3 = vFaces[f3InVFaces];

                    int vInf2 = vInFaces[f2InVFaces];
                    int vInf3 = vInFaces[f3InVFaces];

                    ConstIndexArray f2Points = getFacePoints(level, f2, fvarChannel);
                    ConstIndexArray f3Points = getFacePoints(level, f3, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = f2Points[fastMod3(vInf2 + 1)];
                    patchPoints[cornerPointIndices[2]] = f3Points[fastMod3(vInf3 + 1)];
                    patchPoints[cornerPointIndices[3]] = f3Points[fastMod3(vInf3 + 2)];
                }
                else if (((eMask & (1 << i)) != 0) && ((eMask & (1 << fastMod3(i + 2))) != 0))
                {
                    //  Two indicent boundary edges -- no incident faces

                    patchPoints[cornerPointIndices[1]] = boundaryPoint;
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = boundaryPoint;
                }
                else if ((eMask & (1 << i)) != 0)
                {
                    //  Leading/outgoing boundary edge, i.e. f0 of {f0,f1,f2}, need f2:
                    int f2InVFaces = fastModN(fInVFaces + 2, vFaces.size());

                    int f2 = vFaces[f2InVFaces];
                    int vInf2 = vInFaces[f2InVFaces];

                    if (level.getVertexTag(v)._nonManifold)
                    {
                        int f1 = getNextFaceInVertFaces(level, fInVFaces,
                                                    vFaces, vInFaces, false, ref vInf2);
                        f2 = getNextFaceInVertFaces(level, vFaces.FindIndex(f1),
                                                    vFaces, vInFaces, false, ref vInf2);
                    }
                    ConstIndexArray f2Points = getFacePoints(level, f2, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = f2Points[fastMod3(vInf2 + 1)];
                    patchPoints[cornerPointIndices[2]] = f2Points[fastMod3(vInf2 + 2)];
                    patchPoints[cornerPointIndices[3]] = boundaryPoint;
                }
                else if ((eMask & (1 << fastMod3(i + 2))) != 0)
                {
                    //  Trailing/incoming boundary edge, i.e. f2 of {f0,f1,f2}, need f0:
                    int f0InVFaces = fastModN(fInVFaces + vFaces.size() - 2, vFaces.size());

                    int f0 = vFaces[f0InVFaces];
                    int vInf0 = vInFaces[f0InVFaces];

                    if (level.getVertexTag(v)._nonManifold)
                    {
                        int f1 = getPrevFaceInVertFaces(level, fInVFaces,
                                                    vFaces, vInFaces, false, ref vInf0);
                        f0 = getPrevFaceInVertFaces(level, vFaces.FindIndex(f1),
                                                    vFaces, vInFaces, false, ref vInf0);
                    }
                    ConstIndexArray f0Points = getFacePoints(level, f0, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = boundaryPoint;
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = f0Points[fastMod3(vInf0 + 1)];
                }
                else
                {
                    //  Boundary vertex on edge, i.e. f1 of {f0,f1,f2}, need next face f2:
                    int vInf2 = 0;
                    int f2 = getNextFaceInVertFaces(level, fInVFaces, vFaces, vInFaces,
                                               !level.getVertexTag(v)._nonManifold, ref vInf2);

                    ConstIndexArray f2Points = getFacePoints(level, f2, fvarChannel);

                    patchPoints[cornerPointIndices[1]] = f2Points[fastMod3(vInf2 + 2)];
                    patchPoints[cornerPointIndices[2]] = boundaryPoint;
                    patchPoints[cornerPointIndices[3]] = boundaryPoint;
                }
                patchPoints[cornerPointIndices[0]] = fPoints[i];
            }
            return 12;
        }

        public int GetRegularPatchPoints(int levelIndex, int faceIndex,
                int regBoundaryMask, Span<int> patchPoints,
                int fvarChannel = -1)
        {
            if (_schemeIsLinear)
            {
                return getRegularFacePoints(
                    levelIndex, faceIndex, patchPoints, fvarChannel);
            }
            else if (_schemeRegFaceSize == 4)
            {
                return getQuadRegularPatchPoints(
                    levelIndex, faceIndex, regBoundaryMask, patchPoints, fvarChannel);
            }
            else
            {
                return getTriRegularPatchPoints(
                    levelIndex, faceIndex, regBoundaryMask, patchPoints, fvarChannel);
            }
        }

        protected int assembleIrregularSourcePatch(
                int levelIndex, int faceIndex, Level.VSpan[] cornerSpans,
                SourcePatch sourcePatch)
        {
            //
            //  Initialize the four Patch corners and finalize the patch:
            //
            Level level = _refiner.getLevel(levelIndex);

            ConstIndexArray fVerts = level.getFaceVertices(faceIndex);

            for (int corner = 0; corner < fVerts.size(); ++corner)
            {
                //
                //  Retrieve corner properties from the VSpan when explicitly assigned.
                //  Otherwise, identify properties from the incident faces and tags and
                //  find the face for the patch within the set of incident faces:
                //
                Level.VTag vTag = level.getVertexTag(fVerts[corner]);

                ref SourcePatch.Corner patchCorner = ref sourcePatch._corners[corner];

                if (cornerSpans[corner].isAssigned())
                {
                    patchCorner._numFaces = (ushort)cornerSpans[corner]._numFaces;
                    patchCorner._patchFace = (ushort)cornerSpans[corner]._cornerInSpan;
                    patchCorner._boundary = !cornerSpans[corner]._periodic;
                }
                else
                {
                    ConstIndexArray vFaces = level.getVertexFaces(fVerts[corner]);

                    patchCorner._numFaces = (ushort)vFaces.size();
                    patchCorner._patchFace = (ushort)vFaces.FindIndex(faceIndex);
                    patchCorner._boundary = vTag._boundary;
                }
                patchCorner._sharp = cornerSpans[corner]._sharp;
                patchCorner._dart = (vTag._rule == (ushort)Crease.Rule.RULE_DART) && vTag._infSharpEdges;
            }
            sourcePatch.Finalize(fVerts.size());

            return sourcePatch.GetNumSourcePoints();
        }

        //
        //  Gather patch points from around the face of a level given a previously
        //  initialized SourcePatch.  This is historically specific to an irregular
        //  patch and still relies on the cornerSpans (which may or may not have been
        //  initialized when the SourcePatch was created) rather than inspecting the
        //  corners of the SourcePatch.
        //
        protected int gatherIrregularSourcePoints(
                int levelIndex, int faceIndex,
                Level.VSpan[] cornerSpans, SourcePatch sourcePatch,
                Span<int> patchVerts, int fvarChannel)
        {
            //
            //  Allocate temporary space for rings around the corners in both the Level
            //  and the Patch, then retrieve corresponding rings and assign the source
            //  vertices to the given array of patch points
            //
            int numSourceVerts = sourcePatch.GetNumSourcePoints();

            int[] sourceRingVertices = new int[sourcePatch.GetMaxRingSize()];
            int[] patchRingPoints = new int[sourcePatch.GetMaxRingSize()];

            Level level = _refiner.getLevel(levelIndex);

            ConstIndexArray faceVerts = level.getFaceVertices(faceIndex);
            for (int corner = 0; corner < sourcePatch._numCorners; ++corner)
            {
                int cornerVertex = faceVerts[corner];

                //  Gather the ring of source points from the Vtr level:
                int sourceRingSize = 0;
                if (cornerSpans[corner].isAssigned())
                {
                    sourceRingSize = gatherRegularPartialRingAroundVertex(level,
                        cornerVertex, cornerSpans[corner], sourceRingVertices,
                        fvarChannel);
                }
                else if (sourcePatch._numCorners == 4)
                {
                    sourceRingSize = level.gatherQuadRegularRingAroundVertex(
                        cornerVertex, sourceRingVertices,
                        fvarChannel);
                }
                else
                {
                    sourceRingSize = gatherTriRegularRingAroundVertex(level,
                        cornerVertex, sourceRingVertices,
                        fvarChannel);
                }

                //  Gather the ring of local points from the patch:
                int patchRingSize = sourcePatch.GetCornerRingPoints(
                        corner, patchRingPoints);
                System.Diagnostics.Debug.Assert(patchRingSize == sourceRingSize);

                //  Identify source points for corresponding local patch points of ring:
                for (int i = 0; i < patchRingSize; ++i)
                {
                    System.Diagnostics.Debug.Assert(patchRingPoints[i] < numSourceVerts);
                    patchVerts[patchRingPoints[i]] = sourceRingVertices[i];
                }
            }
            return numSourceVerts;
        }

        public int GetIrregularPatchSourcePoints(
                int levelIndex, int faceIndex, Level.VSpan[] cornerSpans,
                Span<int> sourcePoints, int fvarChannel = -1)
        {
            SourcePatch sourcePatch = new SourcePatch();
            assembleIrregularSourcePatch(
                    levelIndex, faceIndex, cornerSpans, sourcePatch);

            return gatherIrregularSourcePoints(levelIndex, faceIndex,
                cornerSpans, sourcePatch, sourcePoints, fvarChannel);
        }

        //
        //  Template conversion methods for the matrix type
        //
        public int GetIrregularPatchConversionMatrix<REAL>(
                int levelIndex, int faceIndex,
                Level.VSpan[] cornerSpans,
                SparseMatrix<REAL> conversionMatrix) where REAL : unmanaged, System.Numerics.INumber<REAL>
        {
            SourcePatch sourcePatch = new SourcePatch();
            assembleIrregularSourcePatch(
                    levelIndex, faceIndex, cornerSpans, sourcePatch);

            if (conversionMatrix is SparseMatrix<float> matrixFloat)
            {
                return convertToPatchType(
                    sourcePatch, GetIrregularPatchType(), matrixFloat);
            }
            return convertToPatchType(
                sourcePatch, GetIrregularPatchType(), (SparseMatrix<double>)(object)conversionMatrix);
        }

        //
        //  Queries related to "single-crease" patches -- currently a subset of
        //  regular interior patches:
        //
        public bool IsRegularSingleCreasePatch(int levelIndex, int faceIndex,
                ref SingleCreaseInfo creaseInfo)
        {
            if (_schemeRegFaceSize != 4)
            {
                return false;
            }

            Level level = _refiner.getLevel(levelIndex);

            return level.isSingleCreasePatch(faceIndex,
                        out creaseInfo.creaseSharpness, out creaseInfo.creaseEdgeInFace);
        }

        //
        //  Computing the PatchParam -- note the regrettable dependency on
        //  PtexIndices but PatchParam is essentially tied to it indefinitely.
        //  Better to pass it in than have the PatchBuilder build its own
        //  PtexIndices.
        //
        public PatchParam ComputePatchParam(int levelIndex, int faceIndex,
                PtexIndices ptexIndices, bool isRegular = true,
                int boundaryMask = 0, bool computeTransitionMask = false)
        {
            // Move up the hierarchy accumulating u,v indices to the coarse level:
            int depth = levelIndex;
            int childIndexInParent = 0,
                u = 0,
                v = 0,
                ofs = 1;

            int regFaceSize = _schemeRegFaceSize;

            bool irregBase =
                _refiner.GetLevel(depth).GetFaceVertices(faceIndex).size() !=
                regFaceSize;

            // For triangle refinement, the parameterization is rotated at
            // the fourth triangle subface at each level. The u and v values
            // computed for rotated triangles will be negative while we are
            // walking through the refinement levels.
            bool rotatedTriangle = false;

            int childFaceIndex = faceIndex;
            for (int i = depth; i > 0; --i)
            {
                Refinement refinement = _refiner.getRefinement(i - 1);
                Level parentLevel = _refiner.getLevel(i - 1);

                int parentFaceIndex =
                    refinement.getChildFaceParentFace(childFaceIndex);

                irregBase =
                    parentLevel.getFaceVertices(parentFaceIndex).size() !=
                    regFaceSize;

                if (_schemeRegFaceSize == 3)
                {
                    // For now, we don't consider irregular faces for
                    // triangle refinement.

                    childIndexInParent =
                        refinement.getChildFaceInParentFace(childFaceIndex);

                    if (rotatedTriangle)
                    {
                        switch (childIndexInParent)
                        {
                            case 0: break;
                            case 1: { u -= ofs; } break;
                            case 2: { v -= ofs; } break;
                            case 3: { u += ofs; v += ofs; rotatedTriangle = false; } break;
                        }
                    }
                    else
                    {
                        switch (childIndexInParent)
                        {
                            case 0: break;
                            case 1: { u += ofs; } break;
                            case 2: { v += ofs; } break;
                            case 3: { u -= ofs; v -= ofs; rotatedTriangle = true; } break;
                        }
                    }
                    ofs = (ushort)(ofs << 1);
                }
                else if (!irregBase)
                {
                    childIndexInParent =
                        refinement.getChildFaceInParentFace(childFaceIndex);

                    switch (childIndexInParent)
                    {
                        case 0: break;
                        case 1: { u += ofs; } break;
                        case 2: { u += ofs; v += ofs; } break;
                        case 3: { v += ofs; } break;
                    }
                    ofs = (ushort)(ofs << 1);
                }
                else
                {
                    // If the root face is not a quad, we need to offset the ptex index
                    // CCW to match the correct child face
                    ConstIndexArray children =
                        refinement.getFaceChildFaces(parentFaceIndex);

                    for (int j = 0; j < children.size(); ++j)
                    {
                        if (children[j] == childFaceIndex)
                        {
                            childIndexInParent = j;
                            break;
                        }
                    }
                }
                childFaceIndex = parentFaceIndex;
            }
            if (rotatedTriangle)
            {
                // If the triangle is tagged as rotated at this point then the
                // computed u and v parameters will both be negative and we map
                // them onto positive values in the opposite diagonal of the
                // parameter space.
                u += ofs;
                v += ofs;
            }
            int baseFaceIndex = childFaceIndex;

            //  Need to store ptex index from base face and child of an irregular face:
            int ptexIndex = ptexIndices.GetFaceId(baseFaceIndex);
            System.Diagnostics.Debug.Assert(ptexIndex != -1);
            if (irregBase)
            {
                ptexIndex += childIndexInParent;
            }

            //  Compute/identify the transition mask if requested, otherwise leave it 0:
            int transitionMask = 0;
            if (computeTransitionMask && (levelIndex < _refiner.GetMaxLevel()))
            {
                transitionMask = (int)_refiner.getRefinement(levelIndex).
                                    getParentFaceSparseTag(faceIndex)._transitional;
            }

            PatchParam param = new PatchParam();
            param.Set(ptexIndex, (short)u, (short)v, (ushort)depth, irregBase,
                      (ushort)boundaryMask, (ushort)transitionMask,
                      isRegular);
            return param;
        }

        //
        //  Virtual methods to be provided by subclass for each scheme:
        //
        protected abstract PatchDescriptor.Type patchTypeFromBasis(BasisType basis);

        //  Note overloading of the conversion for SparseMatrix<REAL>:
        protected abstract int convertToPatchType(SourcePatch sourcePatch,
                                                  PatchDescriptor.Type patchType,
                                                  SparseMatrix<float> matrix);
        protected abstract int convertToPatchType(SourcePatch sourcePatch,
                                                  PatchDescriptor.Type patchType,
                                                  SparseMatrix<double> matrix);

        protected TopologyRefiner _refiner;
        protected Options _options;

        protected SchemeType _schemeType;
        protected int _schemeRegFaceSize;
        protected bool _schemeIsLinear;

        protected PatchDescriptor.Type _regPatchType;
        protected PatchDescriptor.Type _irregPatchType;
        protected PatchDescriptor.Type _nativePatchType;
        protected PatchDescriptor.Type _linearPatchType;
    }
}
