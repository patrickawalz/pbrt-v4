//
//   Copyright 2018 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far
{
    //
    //  Core functions for computing Loop limit properties that are used
    //  in the conversion to multiple patch types.
    //
    public static class LoopLimits<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        //
        //  Minimal vertex neighborhood and mask classes for the Sdc::Scheme
        //  limit mask computation:
        //
        public class LimitVertex : IVertexNeighborhood
        {
            public LimitVertex()
            {
                _nFaces = 0;
                _nEdges = 0;
            }

            public LimitVertex(int faces, int edges)
            {
                _nFaces = faces;
                _nEdges = edges;
            }

            public void Assign(int faces, int edges)
            {
                _nFaces = faces;
                _nEdges = edges;
            }

            public int GetNumEdges()
            {
                return _nEdges;
            }

            public int GetNumFaces()
            {
                return _nFaces;
            }

            public float GetSharpness()
            {
                System.Diagnostics.Debug.Assert(false);
                return 0.0f;
            }

            public float[] GetSharpnessPerEdge(float[] sharpness)
            {
                sharpness[0] = Crease.SHARPNESS_INFINITE;
                for (int i = 1; i < _nEdges - 1; ++i)
                {
                    sharpness[i] = Crease.SHARPNESS_SMOOTH;
                }
                sharpness[_nEdges - 1] = Crease.SHARPNESS_INFINITE;
                return sharpness;
            }

            public float GetChildSharpness(Crease crease)
            {
                System.Diagnostics.Debug.Assert(false);
                return 0.0f;
            }

            public float[] GetChildSharpnessPerEdge(Crease crease, float[] cSharpness)
            {
                System.Diagnostics.Debug.Assert(false);
                return cSharpness;
            }

            private int _nFaces;
            private int _nEdges;
        }

        public class LimitMask : IMask<REAL>
        {
            public LimitMask(REAL[] w, int offset)
            {
                _weights = w;
                _offset = offset;
                _valence = 0;
            }

            //  Generic interface expected of <typename MASK>:
            public int GetNumVertexWeights()
            {
                return 1;
            }

            public int GetNumEdgeWeights()
            {
                return _valence;
            }

            public int GetNumFaceWeights()
            {
                return 0;
            }

            public void SetNumVertexWeights(int count)
            {
            }

            public void SetNumEdgeWeights(int count)
            {
                _valence = count;
            }

            public void SetNumFaceWeights(int count)
            {
            }

            public ref REAL VertexWeight(int index)
            {
                return ref _weights[_offset + 0];
            }

            public ref REAL EdgeWeight(int index)
            {
                return ref _weights[_offset + 1 + index];
            }

            public ref REAL FaceWeight(int index)
            {
                return ref _weights[_offset + 0];
            }

            public bool AreFaceWeightsForFaceCenters()
            {
                return false;
            }

            public void SetFaceWeightsForFaceCenters(bool on)
            {
            }

            private REAL[] _weights;
            private int _offset;
            private int _valence;
        }

        //
        //  The weight arrays are passed as (array, offset) pairs -- the tangent
        //  weight arrays may be null if not required:
        //
        public static void ComputeInteriorPointWeights(int valence, int faceInRing,
                    REAL[] pWeights, int pOffset,
                    REAL[] epWeights, int epOffset,
                    REAL[] emWeights, int emOffset)
        {
            int ringSize = valence + 1;

            LimitVertex vertex = new LimitVertex(valence, valence);

            bool computeTangentPoints = (epWeights != null) && (emWeights != null);
            if (!computeTangentPoints)
            {
                LimitMask pMask = new LimitMask(pWeights, pOffset);

                new Scheme(SchemeType.SCHEME_LOOP).ComputeVertexLimitMask<LimitVertex, REAL>(vertex, pMask,
                        Crease.Rule.RULE_SMOOTH);
            }
            else
            {
                //  Compute the position and two tangent masks, then rotate/permute
                //  the second tangent to align it with the edges of Ep and Em:
                int weightWidth = valence + 1;

                REAL[] tWeights = new REAL[2 * weightWidth];
                int t1Offset = 0;
                int t2Offset = ringSize;

                LimitMask pMask = new LimitMask(pWeights, pOffset);
                LimitMask t1Mask = new LimitMask(tWeights, t1Offset);
                LimitMask t2Mask = new LimitMask(tWeights, t2Offset);

                new Scheme(SchemeType.SCHEME_LOOP).ComputeVertexLimitMask<LimitVertex, REAL>(vertex, pMask, t1Mask, t2Mask,
                        Crease.Rule.RULE_SMOOTH);

                double theta = 2.0 * Math.PI / (double)valence;
                REAL tanScale = R((3.0 + 2.0 * Math.Cos(theta)) / (6.0 * valence));

                for (int i = 0; i < ringSize; ++i)
                {
                    tWeights[t2Offset + i] = pWeights[pOffset + i] + tWeights[t1Offset + i] * tanScale;
                }

                //  Rotate the ring of weights (excluding the first) for Ep and Em:
                int n1 = faceInRing;
                int n2 = valence - n1;
                epWeights[epOffset + 0] = tWeights[t2Offset + 0];
                System.Array.Copy(tWeights, t2Offset + 1 + n2, epWeights, epOffset + 1, n1);
                System.Array.Copy(tWeights, t2Offset + 1, epWeights, epOffset + 1 + n1, n2);

                n1 = (faceInRing + 1) % valence;
                n2 = valence - n1;
                emWeights[emOffset + 0] = tWeights[t2Offset + 0];
                System.Array.Copy(tWeights, t2Offset + 1 + n2, emWeights, emOffset + 1, n1);
                System.Array.Copy(tWeights, t2Offset + 1, emWeights, emOffset + 1 + n1, n2);
            }
        }

        public static void ComputeBoundaryPointWeights(int valence, int faceInRing,
                    REAL[] pWeights, int pOffset,
                    REAL[] epWeights, int epOffset,
                    REAL[] emWeights, int emOffset)
        {
            LimitVertex vertex = new LimitVertex(valence - 1, valence);

            bool computeTangentPoints = (epWeights != null) && (emWeights != null);
            if (!computeTangentPoints)
            {
                LimitMask pMask = new LimitMask(pWeights, pOffset);

                new Scheme(SchemeType.SCHEME_LOOP).ComputeVertexLimitMask<LimitVertex, REAL>(vertex, pMask,
                        Crease.Rule.RULE_CREASE);
            }
            else
            {
                int weightWidth = valence + 1;

                REAL[] tWeights = new REAL[2 * weightWidth];
                int t1Offset = 0;
                int t2Offset = weightWidth;

                REAL t1Leading = R(1.0 / 6.0);
                REAL t1Trailing = -R(1.0 / 6.0);

                REAL t2Scale = R(1.0 / 24.0);

                LimitMask pMask = new LimitMask(pWeights, pOffset);
                LimitMask t1Mask = new LimitMask(tWeights, t1Offset);
                LimitMask t2Mask = new LimitMask(tWeights, t2Offset);

                new Scheme(SchemeType.SCHEME_LOOP).ComputeVertexLimitMask<LimitVertex, REAL>(vertex, pMask, t1Mask, t2Mask,
                        Crease.Rule.RULE_CREASE);

                //
                //  Compute the weights for Ep and Em from the position and tangent
                //  weights -- trivial cases on the leading and trailing edges:
                //
                bool epOnLeadingEdge = (faceInRing == 0);
                bool emOnTrailingEdge = (faceInRing == (valence - 1));

                if (epOnLeadingEdge)
                {
                    System.Array.Clear(epWeights, epOffset, weightWidth);

                    epWeights[epOffset + 0] = R(2.0 / 3.0);
                    epWeights[epOffset + 1] = R(1.0 / 3.0);
                }
                else
                {
                    int iEdgeNext = faceInRing;
                    REAL faceAngle = R(Math.PI / (double)(valence - 1));
                    REAL faceAngleNext = faceAngle * REAL.CreateChecked(iEdgeNext);
                    REAL cosAngleNext = REAL.Cos(faceAngleNext);
                    REAL sinAngleNext = REAL.Sin(faceAngleNext);

                    for (int i = 0; i < weightWidth; ++i)
                    {
                        epWeights[epOffset + i] = t2Scale * tWeights[t2Offset + i] * sinAngleNext;
                    }
                    epWeights[epOffset + 0] += pWeights[pOffset + 0];
                    epWeights[epOffset + 1] += pWeights[pOffset + 1] + t1Leading * cosAngleNext;
                    epWeights[epOffset + valence] += pWeights[pOffset + valence] + t1Trailing * cosAngleNext;
                }

                if (emOnTrailingEdge)
                {
                    System.Array.Clear(emWeights, emOffset, weightWidth);

                    emWeights[emOffset + 0] = R(2.0 / 3.0);
                    emWeights[emOffset + valence] = R(1.0 / 3.0);
                }
                else
                {
                    int iEdgePrev = (faceInRing + 1) % valence;
                    REAL faceAngle = R(Math.PI / (double)(valence - 1));
                    REAL faceAnglePrev = faceAngle * REAL.CreateChecked(iEdgePrev);
                    REAL cosAnglePrev = REAL.Cos(faceAnglePrev);
                    REAL sinAnglePrev = REAL.Sin(faceAnglePrev);

                    for (int i = 0; i < weightWidth; ++i)
                    {
                        emWeights[emOffset + i] = t2Scale * tWeights[t2Offset + i] * sinAnglePrev;
                    }
                    emWeights[emOffset + 0] += pWeights[pOffset + 0];
                    emWeights[emOffset + 1] += pWeights[pOffset + 1] + t1Leading * cosAnglePrev;
                    emWeights[emOffset + valence] += pWeights[pOffset + valence] + t1Trailing * cosAnglePrev;
                }
            }
        }
    }

    //
    //  Simple utility functions for dealing with SparseMatrix that differ from
    //  (or are additional to) those in PatchBuilderSparseMatrixUtil:
    //
    public static class LoopPatchBuilderSparseMatrixUtil<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        public static bool _isZero(REAL w)
        {
            return (w == REAL.Zero);
        }

        public static void _combineSparsePointsInFullRow(SparseMatrixRow<REAL> p,
                                      REAL aCoeff, SparseMatrixRow<REAL> a,
                                      REAL bCoeff, SparseMatrixRow<REAL> b,
                                      int rowSize, REAL[] rowBuffer, int[] maskBuffer)
        {
            System.Array.Clear(maskBuffer, 0, rowSize);
            System.Array.Clear(rowBuffer, 0, rowSize);

            PatchBuilderSparseMatrixUtil<REAL>._addSparsePointToFullRow(rowBuffer, a, aCoeff, maskBuffer);
            PatchBuilderSparseMatrixUtil<REAL>._addSparsePointToFullRow(rowBuffer, b, bCoeff, maskBuffer);

            int nWeights = 0;
            for (int i = 0; i < rowSize; ++i)
            {
                if (maskBuffer[i] != 0)
                {
                    p.Assign(nWeights++, maskBuffer[i] - 1, rowBuffer[i]);
                }
            }

            System.Diagnostics.Debug.Assert(nWeights <= p.GetSize());
            for (int i = nWeights; i < p.GetSize(); ++i, ++nWeights)
            {
                p.Assign(i, 0, REAL.CreateChecked(0.0f));
            }
        }

        public static void _combineSparseMatrixRowsInFull(SparseMatrix<REAL> dstMatrix, int dstRowIndex,
               SparseMatrix<REAL> srcMatrix, int numSrcRows,
               int[] srcRowIndices, REAL[] srcRowWeights)
        {
            Vtr.Array<REAL> dstRowArray = dstMatrix.SetRowElements(dstRowIndex);

            //  The full row is accumulated in a local buffer, then stored in the matrix:
            int numColumns = dstMatrix.GetNumColumns();
            REAL[] dstRow = new REAL[numColumns];

            for (int i = 0; i < numSrcRows; ++i)
            {
                if (!_isZero(srcRowWeights[i]))
                {
                    PatchBuilderSparseMatrixUtil<REAL>._addSparseRowToFull(dstRow, srcMatrix, srcRowIndices[i], srcRowWeights[i]);
                }
            }
            for (int i = 0; i < numColumns; ++i)
            {
                dstRowArray[i] = dstRow[i];
            }
        }
    }

    //
    //  GregoryTriConverter
    //
    //  The GregoryTriConverter class essentially provides a change-of-basis
    //  matrix from source vertices in a Loop mesh to the 18 control points of
    //  a Gregory triangle patch (3 corners x {P, Ep, Em, Fp, Fm} followed by
    //  3 mid-edge points).
    //
    public class GregoryTriConverter<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        //
        //  Local nested class for GregoryTriConverter to cache information for the
        //  corners of the source patch.
        //
        public class CornerTopology
        {
            //  Basic flags copied from the SourcePatch
            public bool isBoundary;
            public bool isSharp;
            public bool isDart;
            public bool isRegular;
            public bool isVal2Int;
            public bool isCorner;

            //  Flags for edge- and face-points relating to adjacent corners:
            public bool epOnBoundary;
            public bool emOnBoundary;

            public bool fpIsRegular;
            public bool fmIsRegular;
            public bool fpIsCopied;
            public bool fmIsCopied;

            //  Other values stored for repeated use:
            public int valence;
            public int numFaces;
            public int faceInRing;

            public REAL faceAngle;
            public REAL cosFaceAngle;

            //  Its useful to have the ring for each corner immediately available:
            public int[] ringPoints = new int[30];
            public int ringPointsSize;

            public void SetSize(int size)
            {
                if (ringPoints.Length < size)
                {
                    ringPoints = new int[size];
                }
                ringPointsSize = size;
            }

            public int GetSize()
            {
                return ringPointsSize;
            }
        }

        public GregoryTriConverter()
        {
            _numSourcePoints = 0;
            _corners = new CornerTopology[3];
            for (int i = 0; i < 3; ++i)
            {
                _corners[i] = new CornerTopology();
            }
        }

        public GregoryTriConverter(SourcePatch sourcePatch) : this()
        {
            Initialize(sourcePatch);
        }

        public GregoryTriConverter(SourcePatch sourcePatch, SparseMatrix<REAL> sparseMatrix) : this()
        {
            Initialize(sourcePatch);
            Convert(sparseMatrix);
        }

        public void Initialize(SourcePatch sourcePatch)
        {
            //
            //  Allocate and gather the 1-rings for the corner vertices and other
            //  topological information for more immediate access:
            //
            int width = sourcePatch.GetNumSourcePoints();
            _numSourcePoints = width;
            _maxValence = sourcePatch.GetMaxValence();

            int boundaryCount = 0;
            int irregularCount = 0;
            int irregularCorner = -1;
            int irregularValence = -1;
            int sharpCount = 0;
            int val2IntCount = 0;

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                SourcePatch.Corner srcCorner = sourcePatch._corners[cIndex];

                CornerTopology corner = _corners[cIndex];

                corner.isBoundary = srcCorner._boundary;
                corner.isSharp = srcCorner._sharp;
                corner.isDart = srcCorner._dart;
                corner.isCorner = (srcCorner._numFaces == 1);
                corner.numFaces = srcCorner._numFaces;
                corner.faceInRing = srcCorner._patchFace;
                corner.isVal2Int = srcCorner._val2Interior;
                corner.valence = corner.numFaces + (corner.isBoundary ? 1 : 0);

                corner.isRegular = ((corner.numFaces << (corner.isBoundary ? 1 : 0)) == 6)
                                 && !corner.isSharp;
                if (corner.isRegular)
                {
                    corner.faceAngle = R(Math.PI / 3.0);
                    corner.cosFaceAngle = R(0.5f);
                }
                else
                {
                    corner.faceAngle =
                        (corner.isBoundary ? R(Math.PI) : R(2.0 * Math.PI))
                            / R(corner.numFaces);
                    corner.cosFaceAngle = REAL.Cos(corner.faceAngle);
                }

                corner.SetSize(sourcePatch.GetCornerRingSize(cIndex));
                sourcePatch.GetCornerRingPoints(cIndex, corner.ringPoints);

                //  Accumulate topology information to categorize the patch as a whole:
                boundaryCount += corner.isBoundary ? 1 : 0;
                if (!corner.isRegular)
                {
                    irregularCount++;
                    irregularCorner = cIndex;
                    irregularValence = corner.valence;
                }
                sharpCount += corner.isSharp ? 1 : 0;
                val2IntCount += corner.isVal2Int ? 1 : 0;
            }

            //  Make a second pass to assign tags dependent on adjacent corners
            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                CornerTopology corner = _corners[cIndex];

                int cNext = (cIndex + 1) % 3;
                int cPrev = (cIndex + 2) % 3;

                corner.epOnBoundary = false;
                corner.emOnBoundary = false;

                //
                //  Identify if the face points are regular or shared/copied from
                //  one of the pair:
                //
                corner.fpIsRegular = corner.isRegular && _corners[cNext].isRegular;
                corner.fmIsRegular = corner.isRegular && _corners[cPrev].isRegular;

                corner.fpIsCopied = false;
                corner.fmIsCopied = false;

                if (corner.isBoundary)
                {
                    corner.epOnBoundary = (corner.faceInRing == 0);
                    corner.emOnBoundary = (corner.faceInRing == (corner.numFaces - 1));

                    //  Both face points are same when one of the two corners' edges
                    //  is discontinuous -- one is then copied from the other (unless
                    //  regular)
                    if (corner.numFaces > 1)
                    {
                        if (corner.epOnBoundary)
                        {
                            corner.fpIsRegular = corner.fmIsRegular;
                            corner.fpIsCopied = !corner.fpIsRegular;
                        }
                        if (corner.emOnBoundary)
                        {
                            corner.fmIsRegular = corner.fpIsRegular;
                            corner.fmIsCopied = !corner.fmIsRegular;
                        }
                    }
                    else
                    {
                        //  The case of a corner patch is always regular
                        corner.fpIsRegular = true;
                        corner.fmIsRegular = true;
                    }
                }
            }
            _isIsolatedInteriorPatch = (irregularCount == 1) && (boundaryCount == 0) &&
                                       (irregularValence > 2) && (sharpCount == 0);
            if (_isIsolatedInteriorPatch)
            {
                _isolatedCorner = irregularCorner;
                _isolatedValence = irregularValence;
            }
            _hasVal2InteriorCorner = (val2IntCount > 0);
        }

        public bool IsIsolatedInteriorPatch()
        {
            return _isIsolatedInteriorPatch;
        }

        public bool HasVal2InteriorCorner()
        {
            return _hasVal2InteriorCorner;
        }

        public int GetIsolatedInteriorCorner()
        {
            return _isolatedCorner;
        }

        public int GetIsolatedInteriorValence()
        {
            return _isolatedValence;
        }

        public void Convert(SparseMatrix<REAL> matrix)
        {
            //
            //  Initialize the sparse matrix to accomodate the coefficients for each
            //  row/point -- identify common topological cases to treat more easily
            //
            if (_isIsolatedInteriorPatch)
            {
                resizeMatrixIsolatedIrregular(matrix, _isolatedCorner, _isolatedValence);
            }
            else
            {
                resizeMatrixUnisolated(matrix);
            }

            //
            //  Compute the corner and edge points P, Ep and Em first.  Since face
            //  points Fp and Fm involve edge points for two adjacent corners, their
            //  computation must follow:
            //
            int maxRingSize = 1 + _maxValence;
            int weightBufferSize = Math.Max(3 * maxRingSize, 2 * _numSourcePoints);

            REAL[] weightBuffer = new REAL[weightBufferSize];
            int[] indexBuffer = new int[weightBufferSize];

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                if (_corners[cIndex].isRegular)
                {
                    assignRegularEdgePoints(cIndex, matrix);
                }
                else
                {
                    computeIrregularEdgePoints(cIndex, matrix, weightBuffer);
                }
            }

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                if (_corners[cIndex].fpIsRegular || _corners[cIndex].fmIsRegular)
                {
                    assignRegularFacePoints(cIndex, matrix);
                }
                if (!_corners[cIndex].fpIsRegular || !_corners[cIndex].fmIsRegular)
                {
                    computeIrregularFacePoints(cIndex, matrix, weightBuffer, indexBuffer);
                }
            }

            //
            //  Compute the mid-edge points -- identify the regular and boundary
            //  cases and compute the remainder:
            //
            for (int eIndex = 0; eIndex < 3; ++eIndex)
            {
                CornerTopology c0 = _corners[eIndex];
                CornerTopology c1 = _corners[(eIndex + 1) % 3];

                bool isBoundaryEdge = c0.epOnBoundary && c1.emOnBoundary;
                bool isDartEdge = c0.epOnBoundary != c1.emOnBoundary;

                if (isBoundaryEdge || (c0.isRegular && c1.isRegular && !isDartEdge))
                {
                    assignRegularMidEdgePoint(eIndex, matrix);
                }
                else
                {
                    computeIrregularMidEdgePoint(eIndex, matrix, weightBuffer, indexBuffer);
                }
            }

            //  The edge points computed so far are cubic -- promote to quartic:
            promoteCubicEdgePointsToQuartic(matrix, weightBuffer, indexBuffer);

            if (_hasVal2InteriorCorner)
            {
                PatchBuilderSparseMatrixUtil<REAL>._removeValence2Duplicates(matrix);
            }
        }

        private void resizeMatrixIsolatedIrregular(
                SparseMatrix<REAL> matrix, int cornerIndex, int cornerValence)
        {
            int irregRingSize = 1 + cornerValence;

            int irregCorner = cornerIndex;
            int irregPlus = (cornerIndex + 1) % 3;
            int irregMinus = (cornerIndex + 2) % 3;

            int[] rowSizes = new int[18];
            int rowSizePtr = 0;

            rowSizePtr = irregCorner * 5;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;

            rowSizePtr = irregPlus * 5;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 5;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;

            rowSizePtr = irregMinus * 5;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 7;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;
            rowSizes[rowSizePtr++] = 5;

            rowSizes[15 + irregCorner] = 3 + irregRingSize;
            rowSizes[15 + irregPlus] = 4;
            rowSizes[15 + irregMinus] = 3 + irregRingSize;

            int numElements = 9 * irregRingSize + 74;

            PatchBuilderSparseMatrixUtil<REAL>._resizeMatrix(matrix, 18, _numSourcePoints, numElements, rowSizes);
        }

        private void resizeMatrixUnisolated(SparseMatrix<REAL> matrix)
        {
            int[] rowSizes = new int[18];

            int numElements = 0;

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                int rowSize = cIndex * 5;

                CornerTopology corner = _corners[cIndex];

                //  First, the corner and pair of edge points:
                if (corner.isRegular)
                {
                    if (!corner.isBoundary)
                    {
                        rowSizes[rowSize + 0] = 7;
                        rowSizes[rowSize + 1] = 7;
                        rowSizes[rowSize + 2] = 7;
                    }
                    else
                    {
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = corner.epOnBoundary ? 3 : 5;
                        rowSizes[rowSize + 2] = corner.emOnBoundary ? 3 : 5;
                    }
                }
                else
                {
                    if (corner.isSharp)
                    {
                        rowSizes[rowSize + 0] = 1;
                        rowSizes[rowSize + 1] = 2;
                        rowSizes[rowSize + 2] = 2;
                    }
                    else if (!corner.isBoundary)
                    {
                        int ringSize = 1 + corner.valence;
                        rowSizes[rowSize + 0] = ringSize;
                        rowSizes[rowSize + 1] = ringSize;
                        rowSizes[rowSize + 2] = ringSize;
                    }
                    else if (corner.numFaces > 1)
                    {
                        int ringSize = 1 + corner.valence;
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = corner.epOnBoundary ? 3 : ringSize;
                        rowSizes[rowSize + 2] = corner.emOnBoundary ? 3 : ringSize;
                    }
                    else
                    {
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = 3;
                        rowSizes[rowSize + 2] = 3;
                    }
                }
                numElements += rowSizes[rowSize + 0] + rowSizes[rowSize + 1] + rowSizes[rowSize + 2];

                //  Second, the pair of face points:
                rowSizes[rowSize + 3] = 5 - (corner.epOnBoundary ? 1 : 0) - (corner.emOnBoundary ? 1 : 0);
                rowSizes[rowSize + 4] = 5 - (corner.epOnBoundary ? 1 : 0) - (corner.emOnBoundary ? 1 : 0);
                if (!corner.fpIsRegular || !corner.fmIsRegular)
                {
                    int cNext = (cIndex + 1) % 3;
                    int cPrev = (cIndex + 2) % 3;
                    if (!corner.fpIsRegular)
                    {
                        rowSizes[rowSize + 3] = getIrregularFacePointSize(cIndex,
                                        corner.fpIsCopied ? cPrev : cNext);
                    }
                    if (!corner.fmIsRegular)
                    {
                        rowSizes[rowSize + 4] = getIrregularFacePointSize(cIndex,
                                        corner.fmIsCopied ? cNext : cPrev);
                    }
                }
                numElements += rowSizes[rowSize + 3] + rowSizes[rowSize + 4];

                //  Third, the mid-edge point for the edge of the corner:
                int cNextForEdge = (cIndex + 1) % 3;
                CornerTopology cornerNext = _corners[cNextForEdge];

                int midEdgeIndex = 15 + cIndex;
                if (corner.epOnBoundary && cornerNext.emOnBoundary)
                {
                    rowSizes[midEdgeIndex] = 2;
                }
                else if (corner.isRegular && cornerNext.isRegular &&
                        (corner.epOnBoundary == cornerNext.emOnBoundary))
                {
                    rowSizes[midEdgeIndex] = 4;
                }
                else
                {
                    rowSizes[midEdgeIndex] = getIrregularFacePointSize(cIndex, cNextForEdge);
                }
                numElements += rowSizes[midEdgeIndex];
            }

            PatchBuilderSparseMatrixUtil<REAL>._resizeMatrix(matrix, 18, _numSourcePoints, numElements, rowSizes);
        }

        private void assignRegularEdgePoints(int cIndex, SparseMatrix<REAL> matrix)
        {
            SparseMatrixRow<REAL> p = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 0);
            SparseMatrixRow<REAL> ep = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1);
            SparseMatrixRow<REAL> em = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 2);

            CornerTopology corner = _corners[cIndex];

            int[] cRing = corner.ringPoints;

            if (!corner.isBoundary)
            {
                REAL pScale = R(1.0 / 12.0);

                p.Assign(0, cIndex, R(0.5f));
                p.Assign(1, cRing[0], pScale);
                p.Assign(2, cRing[1], pScale);
                p.Assign(3, cRing[2], pScale);
                p.Assign(4, cRing[3], pScale);
                p.Assign(5, cRing[4], pScale);
                p.Assign(6, cRing[5], pScale);
                System.Diagnostics.Debug.Assert(p.GetSize() == 7);

                REAL[] eWeights = new REAL[6] { R(7.0f), R(5.0f), R(1.0f), R(-1.0f), R(1.0f), R(5.0f) };
                REAL eScale = R(1.0 / 36.0);

                int iEdgeEp = corner.faceInRing;
                int iEdgeEm = (corner.faceInRing + 1) % 6;

                ep.Assign(0, cIndex, R(0.5f));
                ep.Assign(1, cRing[iEdgeEp], eScale * eWeights[0]);
                ep.Assign(2, cRing[(iEdgeEp + 1) % 6], eScale * eWeights[1]);
                ep.Assign(3, cRing[(iEdgeEp + 2) % 6], eScale * eWeights[2]);
                ep.Assign(4, cRing[(iEdgeEp + 3) % 6], eScale * eWeights[3]);
                ep.Assign(5, cRing[(iEdgeEp + 4) % 6], eScale * eWeights[4]);
                ep.Assign(6, cRing[(iEdgeEp + 5) % 6], eScale * eWeights[5]);
                System.Diagnostics.Debug.Assert(ep.GetSize() == 7);

                em.Assign(0, cIndex, R(0.5f));
                em.Assign(1, cRing[iEdgeEm], eScale * eWeights[0]);
                em.Assign(2, cRing[(iEdgeEm + 1) % 6], eScale * eWeights[1]);
                em.Assign(3, cRing[(iEdgeEm + 2) % 6], eScale * eWeights[2]);
                em.Assign(4, cRing[(iEdgeEm + 3) % 6], eScale * eWeights[3]);
                em.Assign(5, cRing[(iEdgeEm + 4) % 6], eScale * eWeights[4]);
                em.Assign(6, cRing[(iEdgeEm + 5) % 6], eScale * eWeights[5]);
                System.Diagnostics.Debug.Assert(em.GetSize() == 7);
            }
            else
            {
                REAL oneThird = R(1.0 / 3.0);
                REAL twoThirds = R(2.0 / 3.0);
                REAL oneSixth = R(1.0 / 6.0);

                p.Assign(0, cIndex, twoThirds);
                p.Assign(1, cRing[0], oneSixth);
                p.Assign(2, cRing[3], oneSixth);
                System.Diagnostics.Debug.Assert(p.GetSize() == 3);

                if (corner.epOnBoundary)
                {
                    ep.Assign(0, cIndex, twoThirds);
                    ep.Assign(1, cRing[0], oneThird);
                    ep.Assign(2, cRing[3], R(0.0f));
                    System.Diagnostics.Debug.Assert(ep.GetSize() == 3);
                }
                else
                {
                    ep.Assign(0, cIndex, R(0.5f));
                    ep.Assign(1, cRing[1], oneSixth);
                    ep.Assign(2, cRing[2], oneSixth);
                    ep.Assign(3, cRing[corner.emOnBoundary ? 3 : 0], oneSixth);
                    ep.Assign(4, cRing[corner.emOnBoundary ? 0 : 3], R(0.0f));
                    System.Diagnostics.Debug.Assert(ep.GetSize() == 5);
                }
                if (corner.emOnBoundary)
                {
                    em.Assign(0, cIndex, twoThirds);
                    em.Assign(1, cRing[3], oneThird);
                    em.Assign(2, cRing[0], R(0.0f));
                    System.Diagnostics.Debug.Assert(em.GetSize() == 3);
                }
                else
                {
                    em.Assign(0, cIndex, R(0.5f));
                    em.Assign(1, cRing[1], oneSixth);
                    em.Assign(2, cRing[2], oneSixth);
                    em.Assign(3, cRing[corner.epOnBoundary ? 0 : 3], oneSixth);
                    em.Assign(4, cRing[corner.epOnBoundary ? 3 : 0], R(0.0f));
                    System.Diagnostics.Debug.Assert(em.GetSize() == 5);
                }
            }
        }

        private void computeIrregularEdgePoints(int cIndex,
                SparseMatrix<REAL> matrix, REAL[] weightBuffer)
        {
            SparseMatrixRow<REAL> p = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 0);
            SparseMatrixRow<REAL> ep = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1);
            SparseMatrixRow<REAL> em = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 2);

            CornerTopology corner = _corners[cIndex];

            if (corner.isSharp)
            {
                //
                //  The sharp case -- both interior and boundary...
                //
                p.Assign(0, cIndex, R(1.0f));
                System.Diagnostics.Debug.Assert(p.GetSize() == 1);

                // Approximating these for now, pending future investigation...
                ep.Assign(0, cIndex, R(2.0 / 3.0));
                ep.Assign(1, (cIndex + 1) % 3, R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 2);

                em.Assign(0, cIndex, R(2.0 / 3.0));
                em.Assign(1, (cIndex + 2) % 3, R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(em.GetSize() == 2);
            }
            else if (!corner.isBoundary)
            {
                //
                //  The irregular interior case:
                //
                computeIrregularInteriorEdgePoints(cIndex, p, ep, em, weightBuffer);
            }
            else if (corner.numFaces > 1)
            {
                //
                //  The irregular boundary case:
                //
                computeIrregularBoundaryEdgePoints(cIndex, p, ep, em, weightBuffer);
            }
            else
            {
                //
                //  The irregular/smooth corner case:
                //
                p.Assign(0, cIndex, R(4.0 / 6.0));
                p.Assign(1, (cIndex + 1) % 3, R(1.0 / 6.0));
                p.Assign(2, (cIndex + 2) % 3, R(1.0 / 6.0));
                System.Diagnostics.Debug.Assert(p.GetSize() == 3);

                ep.Assign(0, cIndex, R(2.0 / 3.0));
                ep.Assign(1, (cIndex + 1) % 3, R(1.0 / 3.0));
                ep.Assign(2, (cIndex + 2) % 3, R(0.0f));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 3);

                em.Assign(0, cIndex, R(2.0 / 3.0));
                em.Assign(1, (cIndex + 2) % 3, R(1.0 / 3.0));
                em.Assign(2, (cIndex + 1) % 3, R(0.0f));
                System.Diagnostics.Debug.Assert(em.GetSize() == 3);
            }
        }

        private void computeIrregularInteriorEdgePoints(
                int cIndex,
                SparseMatrixRow<REAL> p, SparseMatrixRow<REAL> ep, SparseMatrixRow<REAL> em,
                REAL[] ringWeights)
        {
            CornerTopology corner = _corners[cIndex];

            int valence = corner.valence;
            int weightWidth = 1 + valence;

            int pOffset = 0;
            int epOffset = weightWidth;
            int emOffset = weightWidth * 2;

            LoopLimits<REAL>.ComputeInteriorPointWeights(valence, corner.faceInRing,
                    ringWeights, pOffset, ringWeights, epOffset, ringWeights, emOffset);

            p.Assign(0, cIndex, ringWeights[pOffset + 0]);
            ep.Assign(0, cIndex, ringWeights[epOffset + 0]);
            em.Assign(0, cIndex, ringWeights[emOffset + 0]);

            for (int i = 1; i < weightWidth; ++i)
            {
                int pRingPoint = corner.ringPoints[i - 1];

                p.Assign(i, pRingPoint, ringWeights[pOffset + i]);
                ep.Assign(i, pRingPoint, ringWeights[epOffset + i]);
                em.Assign(i, pRingPoint, ringWeights[emOffset + i]);
            }
            System.Diagnostics.Debug.Assert(p.GetSize() == weightWidth);
            System.Diagnostics.Debug.Assert(ep.GetSize() == weightWidth);
            System.Diagnostics.Debug.Assert(em.GetSize() == weightWidth);
        }

        private void computeIrregularBoundaryEdgePoints(
                int cIndex,
                SparseMatrixRow<REAL> p, SparseMatrixRow<REAL> ep, SparseMatrixRow<REAL> em,
                REAL[] ringWeights)
        {
            CornerTopology corner = _corners[cIndex];

            int valence = corner.valence;
            int weightWidth = 1 + corner.valence;

            int pOffset = 0;
            int epOffset = weightWidth;
            int emOffset = weightWidth * 2;

            LoopLimits<REAL>.ComputeBoundaryPointWeights(valence, corner.faceInRing,
                        ringWeights, pOffset, ringWeights, epOffset, ringWeights, emOffset);

            //
            //  Identify the neighboring points of the boundary:
            //
            int N = weightWidth - 1;

            int p0 = cIndex;
            int p1 = corner.ringPoints[0];
            int pN = corner.ringPoints[valence - 1];

            p.Assign(0, p0, ringWeights[pOffset + 0]);
            p.Assign(1, p1, ringWeights[pOffset + 1]);
            p.Assign(2, pN, ringWeights[pOffset + N]);
            System.Diagnostics.Debug.Assert(p.GetSize() == 3);

            ep.Assign(0, p0, ringWeights[epOffset + 0]);
            if (corner.epOnBoundary)
            {
                ep.Assign(1, p1, ringWeights[epOffset + 1]);
                ep.Assign(2, pN, R(0.0f));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 3);
            }
            else
            {
                for (int i = 1; i < weightWidth; ++i)
                {
                    ep.Assign(i, corner.ringPoints[i - 1], ringWeights[epOffset + i]);
                }
                System.Diagnostics.Debug.Assert(ep.GetSize() == weightWidth);
            }

            em.Assign(0, p0, ringWeights[emOffset + 0]);
            if (corner.emOnBoundary)
            {
                em.Assign(1, pN, ringWeights[emOffset + N]);
                em.Assign(2, p1, R(0.0f));
                System.Diagnostics.Debug.Assert(em.GetSize() == 3);
            }
            else
            {
                for (int i = 1; i < weightWidth; ++i)
                {
                    em.Assign(i, corner.ringPoints[i - 1], ringWeights[emOffset + i]);
                }
                System.Diagnostics.Debug.Assert(em.GetSize() == weightWidth);
            }
        }

        private int getIrregularFacePointSize(int cIndexNear, int cIndexFar)
        {
            CornerTopology nearCorner = _corners[cIndexNear];
            CornerTopology farCorner = _corners[cIndexFar];

            if (nearCorner.isSharp && farCorner.isSharp)
            {
                return 2;
            }

            int nearSize = nearCorner.GetSize() - 3;
            int farSize = farCorner.GetSize() - 3;

            return 4 + (((nearSize > 0) && !nearCorner.isSharp) ? nearSize : 0)
                     + (((farSize > 0) && !farCorner.isSharp) ? farSize : 0);
        }

        private void computeIrregularFacePoint(
                int cIndexNear, int edgeInNearCornerRing, int cIndexFar,
                SparseMatrixRow<REAL> p, SparseMatrixRow<REAL> eNear, SparseMatrixRow<REAL> eFar, SparseMatrixRow<REAL> fNear,
                REAL signForSideOfEdge, REAL[] rowWeights, int[] columnMask)
        {
            CornerTopology cornerNear = _corners[cIndexNear];
            CornerTopology cornerFar = _corners[cIndexFar];

            int valence = cornerNear.valence;

            //
            //  Compute the coefficients for P, eNear and eFar:
            //
            REAL cosNear = cornerNear.cosFaceAngle;
            REAL cosFar = cornerFar.cosFaceAngle;

            REAL pCoeff = cosFar / R(4.0f);
            REAL eNearCoeff = (R(4.0f) - R(2.0f) * cosNear - cosFar) / R(4.0f);
            REAL eFarCoeff = R(2.0f) * cosNear / R(4.0f);

            //
            //  Initialize the full row and column mask and add the contributions
            //  of P, eNear and eFar:
            //
            int fullRowSize = _numSourcePoints;
            System.Array.Clear(columnMask, 0, fullRowSize);
            System.Array.Clear(rowWeights, 0, fullRowSize);

            PatchBuilderSparseMatrixUtil<REAL>._addSparsePointToFullRow(rowWeights, p, pCoeff, columnMask);
            PatchBuilderSparseMatrixUtil<REAL>._addSparsePointToFullRow(rowWeights, eNear, eNearCoeff, columnMask);
            PatchBuilderSparseMatrixUtil<REAL>._addSparsePointToFullRow(rowWeights, eFar, eFarCoeff, columnMask);

            //
            //  Add the contributions of the two points in the 1-ring that are on
            //  the edges adjacent to the interior edge:
            //
            int iEdgeInterior = edgeInNearCornerRing;
            int iEdgePrev = (iEdgeInterior + valence - 1) % valence;
            int iEdgeNext = (iEdgeInterior + 1) % valence;

            REAL rScale = R(0.25 * (7.0 / 18.0));

            rowWeights[cornerNear.ringPoints[iEdgePrev]] += -signForSideOfEdge * rScale;
            rowWeights[cornerNear.ringPoints[iEdgeNext]] += signForSideOfEdge * rScale;

            //
            //  Compress the full row of weights and column mask into the sparse
            //  row of the matrix:
            //
            int nWeights = 0;
            for (int i = 0; i < fullRowSize; ++i)
            {
                if (columnMask[i] != 0)
                {
                    fNear.Assign(nWeights++, columnMask[i] - 1, rowWeights[i]);
                }
            }

            //  Pad with zero weights if the size was increased due to valence-2
            //  corners (duplicates are merged later):
            if (_hasVal2InteriorCorner && (nWeights < fNear.GetSize()))
            {
                while (nWeights < fNear.GetSize())
                {
                    fNear.Assign(nWeights++, cIndexNear, R(0.0f));
                }
            }
            System.Diagnostics.Debug.Assert(fNear.GetSize() == nWeights);
        }

        private void assignRegularFacePoints(int cIndex, SparseMatrix<REAL> matrix)
        {
            CornerTopology corner = _corners[cIndex];

            int cNext = (cIndex + 1) % 3;
            int cPrev = (cIndex + 2) % 3;

            int[] cRing = corner.ringPoints;

            for (int fIsFm = 0; fIsFm < 2; ++fIsFm)
            {
                bool fIsRegular = (fIsFm != 0) ? corner.fmIsRegular : corner.fpIsRegular;
                if (!fIsRegular)
                {
                    continue;
                }

                SparseMatrixRow<REAL> f = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 3 + fIsFm);

                if (corner.isCorner)
                {
                    f.Assign(0, cIndex, R(0.5f));
                    f.Assign(1, cNext, R(0.25f));
                    f.Assign(2, cPrev, R(0.25f));
                    System.Diagnostics.Debug.Assert(f.GetSize() == 3);
                }
                else if (corner.epOnBoundary)
                {
                    f.Assign(0, cIndex, R(11.0 / 24.0));
                    f.Assign(1, cRing[0], R(7.0 / 24.0));
                    f.Assign(2, cRing[1], R(5.0 / 24.0));
                    f.Assign(3, cRing[2], R(1.0 / 24.0));
                    System.Diagnostics.Debug.Assert(f.GetSize() == 4);
                }
                else if (corner.emOnBoundary)
                {
                    f.Assign(0, cIndex, R(11.0 / 24.0));
                    f.Assign(1, cRing[3], R(7.0 / 24.0));
                    f.Assign(2, cRing[2], R(5.0 / 24.0));
                    f.Assign(3, cRing[1], R(1.0 / 24.0));
                    System.Diagnostics.Debug.Assert(f.GetSize() == 4);
                }
                else
                {
                    int eNext = corner.isBoundary ? 0 : ((corner.faceInRing + 5) % 6);
                    int ePrev = corner.isBoundary ? 3 : ((corner.faceInRing + 2) % 6);

                    f.Assign(0, cIndex, R(10.0 / 24.0));
                    f.Assign(1, cPrev, R(0.25f));
                    f.Assign(2, cNext, R(0.25f));
                    f.Assign(3, cRing[ePrev], R(1.0 / 24.0));
                    f.Assign(4, cRing[eNext], R(1.0 / 24.0));
                    System.Diagnostics.Debug.Assert(f.GetSize() == 5);
                }
            }
        }

        private void computeIrregularFacePoints(int cIndex,
                SparseMatrix<REAL> matrix, REAL[] rowWeights, int[] columnMask)
        {
            CornerTopology corner = _corners[cIndex];

            int cNext = (cIndex + 1) % 3;
            int cPrev = (cIndex + 2) % 3;

            SparseMatrixRow<REAL> epPrev = new SparseMatrixRow<REAL>(matrix, 5 * cPrev + 1);
            SparseMatrixRow<REAL> em = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 2);

            SparseMatrixRow<REAL> p = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 0);
            SparseMatrixRow<REAL> ep = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1);
            SparseMatrixRow<REAL> emNext = new SparseMatrixRow<REAL>(matrix, 5 * cNext + 2);

            SparseMatrixRow<REAL> fp = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 3);
            SparseMatrixRow<REAL> fm = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 4);

            //
            //  Compute the face points, unless regular or copied from the other
            //  of the pair:
            //
            if (!corner.fpIsRegular && !corner.fpIsCopied)
            {
                int iEdgeP = corner.faceInRing;
                computeIrregularFacePoint(cIndex, iEdgeP, cNext,
                        p, ep, emNext, fp, R(1.0), rowWeights, columnMask);
            }
            if (!corner.fmIsRegular && !corner.fmIsCopied)
            {
                int iEdgeM = (corner.faceInRing + 1) % corner.valence;
                computeIrregularFacePoint(cIndex, iEdgeM, cPrev,
                        p, em, epPrev, fm, R(-1.0), rowWeights, columnMask);
            }
            if (corner.fpIsCopied)
            {
                fp.Copy(fm);
            }
            if (corner.fmIsCopied)
            {
                fm.Copy(fp);
            }

            if (!corner.fpIsRegular)
            {
                System.Diagnostics.Debug.Assert(matrix.GetRowSize(5 * cIndex + 3) == fp.GetSize());
            }
            if (!corner.fmIsRegular)
            {
                System.Diagnostics.Debug.Assert(matrix.GetRowSize(5 * cIndex + 4) == fm.GetSize());
            }
        }

        private void assignRegularMidEdgePoint(int edgeIndex,
                                SparseMatrix<REAL> matrix)
        {
            SparseMatrixRow<REAL> M = new SparseMatrixRow<REAL>(matrix, 15 + edgeIndex);

            CornerTopology corner = _corners[edgeIndex];

            if (corner.epOnBoundary)
            {
                M.Assign(0, edgeIndex, R(0.5f));
                M.Assign(1, (edgeIndex + 1) % 3, R(0.5f));
                System.Diagnostics.Debug.Assert(M.GetSize() == 2);
            }
            else
            {
                int oppositeInRing = corner.isBoundary ?
                    (corner.faceInRing - 1) : ((corner.faceInRing + 5) % 6);
                int oppositeVertex = corner.ringPoints[oppositeInRing];

                M.Assign(0, edgeIndex, R(1.0 / 3.0));
                M.Assign(1, (edgeIndex + 1) % 3, R(1.0 / 3.0));
                M.Assign(2, (edgeIndex + 2) % 3, R(1.0 / 6.0));
                M.Assign(3, oppositeVertex, R(1.0 / 6.0));
                System.Diagnostics.Debug.Assert(M.GetSize() == 4);
            }
        }

        private void computeIrregularMidEdgePoint(int edgeIndex, SparseMatrix<REAL> matrix,
                                REAL[] rowWeights, int[] columnMask)
        {
            int cIndex0 = edgeIndex;
            int cIndex1 = (edgeIndex + 1) % 3;

            SparseMatrixRow<REAL> E0p = new SparseMatrixRow<REAL>(matrix, 5 * (cIndex0) + 1);
            SparseMatrixRow<REAL> E1m = new SparseMatrixRow<REAL>(matrix, 5 * (cIndex1) + 2);

            SparseMatrixRow<REAL> M = new SparseMatrixRow<REAL>(matrix, 15 + edgeIndex);

            LoopPatchBuilderSparseMatrixUtil<REAL>._combineSparsePointsInFullRow(M, R(0.5f), E0p, R(0.5f), E1m,
                                          _numSourcePoints, rowWeights, columnMask);
        }

        private void promoteCubicEdgePointsToQuartic(SparseMatrix<REAL> matrix,
                                REAL[] rowWeights, int[] columnMask)
        {
            REAL[] onBoundaryWeights = new REAL[3] { R(16), R(7), R(1) };
            REAL[] regBoundaryWeights = new REAL[5] { R(13), R(3), R(3), R(4), R(1) };
            REAL[] regInteriorWeights = new REAL[7] { R(12), R(4), R(3), R(1), R(0), R(1), R(3) };

            REAL oneOver24 = R(1.0 / 24.0);

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                CornerTopology corner = _corners[cIndex];

                SparseMatrixRow<REAL> P = new SparseMatrixRow<REAL>(matrix, 5 * cIndex);

                for (int ePair = 0; ePair < 2; ++ePair)
                {
                    SparseMatrixRow<REAL> E = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1 + ePair);

                    REAL[] weightsToReassign = null;

                    bool eOnBoundary = (ePair != 0) ? corner.emOnBoundary : corner.epOnBoundary;
                    if (eOnBoundary && !corner.isSharp)
                    {
                        System.Diagnostics.Debug.Assert(E.GetSize() == 3);
                        weightsToReassign = onBoundaryWeights;
                    }
                    else if (corner.isRegular)
                    {
                        if (corner.isBoundary)
                        {
                            System.Diagnostics.Debug.Assert(E.GetSize() == 5);
                            weightsToReassign = regBoundaryWeights;
                        }
                        else
                        {
                            System.Diagnostics.Debug.Assert(E.GetSize() == 7);
                            weightsToReassign = regInteriorWeights;
                        }
                    }

                    if (weightsToReassign != null)
                    {
                        for (int i = 0; i < E.GetSize(); ++i)
                        {
                            E.SetWeight(i, weightsToReassign[i] * oneOver24);
                        }
                    }
                    else
                    {
                        LoopPatchBuilderSparseMatrixUtil<REAL>._combineSparsePointsInFullRow(E, R(0.25f), P, R(0.75f), E,
                                                      _numSourcePoints, rowWeights, columnMask);
                    }
                }
            }
        }

        private int _numSourcePoints;
        private int _maxValence;

        private bool _isIsolatedInteriorPatch;
        private bool _hasVal2InteriorCorner;
        private int _isolatedCorner;
        private int _isolatedValence;

        private CornerTopology[] _corners;
    }

    //
    //  LoopPatchBuilder
    //
    public class LoopPatchBuilder : PatchBuilder
    {
        private static readonly PatchDescriptor.Type[] patchTypeFromBasisArray = new PatchDescriptor.Type[]
        {
            PatchDescriptor.Type.NON_PATCH,        // undefined
            PatchDescriptor.Type.LOOP,             // regular
            PatchDescriptor.Type.GREGORY_TRIANGLE, // Gregory
            PatchDescriptor.Type.TRIANGLES,        // linear
            PatchDescriptor.Type.NON_PATCH         // Bezier -- for future use
        };

        //
        //  Conversion of the source patch to a linear (triangle) patch:
        //
        public static void convertToLinear<REAL>(SourcePatch sourcePatch, SparseMatrix<REAL> matrix)
            where REAL : unmanaged, IFloatingPointIeee754<REAL>
        {
            int[] indexBuffer = new int[1 + sourcePatch.GetMaxRingSize()];
            REAL[] weightBuffer = new REAL[1 + sourcePatch.GetMaxRingSize()];

            //  Resize the matrix for the maximum number of entries in each row:
            int numElements = sourcePatch.GetCornerRingSize(0) +
                              sourcePatch.GetCornerRingSize(1) +
                              sourcePatch.GetCornerRingSize(2);

            matrix.Resize(3, sourcePatch.GetNumSourcePoints(), numElements);

            bool hasVal2InteriorCorner = false;

            for (int cIndex = 0; cIndex < 3; ++cIndex)
            {
                SourcePatch.Corner sourceCorner = sourcePatch._corners[cIndex];

                int ringSize = sourcePatch.GetCornerRingSize(cIndex);

                if (sourceCorner._sharp)
                {
                    matrix.SetRowSize(cIndex, 1);
                }
                else if (sourceCorner._boundary)
                {
                    matrix.SetRowSize(cIndex, 3);
                }
                else
                {
                    matrix.SetRowSize(cIndex, 1 + ringSize);
                }
                Vtr.Array<int> rowIndices = matrix.SetRowColumns(cIndex);
                Vtr.Array<REAL> rowWeights = matrix.SetRowElements(cIndex);

                indexBuffer[0] = cIndex;
                sourcePatch.GetCornerRingPoints(cIndex, indexBuffer, 1);

                if (sourceCorner._sharp)
                {
                    rowIndices[0] = cIndex;
                    rowWeights[0] = REAL.CreateChecked(1.0f);
                }
                else if (sourceCorner._boundary)
                {
                    LoopLimits<REAL>.ComputeBoundaryPointWeights(
                            1 + sourceCorner._numFaces, sourceCorner._patchFace,
                            weightBuffer, 0, null, 0, null, 0);

                    rowIndices[0] = indexBuffer[0];
                    rowIndices[1] = indexBuffer[1];
                    rowIndices[2] = indexBuffer[ringSize];

                    rowWeights[0] = weightBuffer[0];
                    rowWeights[1] = weightBuffer[1];
                    rowWeights[2] = weightBuffer[ringSize];
                }
                else
                {
                    LoopLimits<REAL>.ComputeInteriorPointWeights(
                            sourceCorner._numFaces, sourceCorner._patchFace,
                            weightBuffer, 0, null, 0, null, 0);

                    for (int i = 0; i < rowIndices.size(); ++i)
                    {
                        rowIndices[i] = indexBuffer[i];
                    }
                    for (int i = 0; i < rowWeights.size(); ++i)
                    {
                        rowWeights[i] = weightBuffer[i];
                    }
                }
                hasVal2InteriorCorner |= sourceCorner._val2Interior;
            }

            if (hasVal2InteriorCorner)
            {
                PatchBuilderSparseMatrixUtil<REAL>._removeValence2Duplicates(matrix);
            }
        }

        //
        //  Conversion of the source patch to a Gregory triangle patch:
        //
        public static void convertToGregory<REAL>(SourcePatch sourcePatch, SparseMatrix<REAL> matrix)
            where REAL : unmanaged, IFloatingPointIeee754<REAL>
        {
            GregoryTriConverter<REAL> gregoryConverter = new GregoryTriConverter<REAL>(sourcePatch);
            gregoryConverter.Convert(matrix);
        }

        //
        //  Conversion of the source patch to a Loop (quartic) patch -- computed
        //  from the Gregory patch with a constant conversion matrix:
        //
        private static readonly float[][] gregoryToLoopMatrix = new float[][]
        {
            new float[] { 8.214411f, 7.571190f, -7.690082f, 2.237840f, -1.118922f, -16.428828f, 0.666666f, 0.666666f, 2.237835f, 6.309870f, 0.666666f, -1.690100f, -0.428812f, -0.428805f, 0.214407f },
            new float[] { -0.304687f, 0.609374f, 6.752593f, 0.609374f, -0.304687f, 0.609378f, -3.333333f, -3.333333f, 0.609378f, -1.247389f, -3.333333f, -1.247389f, 3.276037f, 3.276037f, -1.638020f },
            new float[] { -1.118922f, 2.237840f, -7.690082f, 7.571190f, 8.214411f, 2.237835f, 0.666666f, 0.666666f, -16.428828f, -1.690100f, 0.666666f, 6.309870f, -0.428805f, -0.428812f, 0.214407f },
            new float[] { 8.214411f, -16.428828f, 6.309870f, -0.428812f, 0.214407f, 7.571190f, 0.666666f, 0.666666f, -0.428805f, -7.690082f, 0.666666f, -1.690100f, 2.237840f, 2.237835f, -1.118922f },
            new float[] { -0.813368f, 1.626735f, -0.773435f, -1.039929f, 0.519965f, 1.626735f, 0.666666f, 0.666666f, -1.039930f, -0.773435f, 0.666666f, 1.226558f, -1.039929f, -1.039930f, 0.519965f },
            new float[] { 0.519965f, -1.039929f, -0.773435f, 1.626735f, -0.813368f, -1.039930f, 0.666666f, 0.666666f, 1.626735f, 1.226558f, 0.666666f, -0.773435f, -1.039930f, -1.039929f, 0.519965f },
            new float[] { 0.214407f, -0.428812f, 6.309870f, -16.428828f, 8.214411f, -0.428805f, 0.666666f, 0.666666f, 7.571190f, -1.690100f, 0.666666f, -7.690082f, 2.237835f, 2.237840f, -1.118922f },
            new float[] { -0.304687f, 0.609378f, -1.247389f, 3.276037f, -1.638020f, 0.609374f, -3.333333f, -3.333333f, 3.276037f, 6.752593f, -3.333333f, -1.247389f, 0.609374f, 0.609378f, -0.304687f },
            new float[] { 0.519965f, -1.039930f, 1.226558f, -1.039930f, 0.519965f, -1.039929f, 0.666666f, 0.666666f, -1.039929f, -0.773435f, 0.666666f, -0.773435f, 1.626735f, 1.626735f, -0.813368f },
            new float[] { -1.638020f, 3.276037f, -1.247389f, 0.609378f, -0.304687f, 3.276037f, -3.333333f, -3.333333f, 0.609374f, -1.247389f, -3.333333f, 6.752593f, 0.609378f, 0.609374f, -0.304687f },
            new float[] { -1.118922f, 2.237835f, -1.690100f, -0.428805f, 0.214407f, 2.237840f, 0.666666f, 0.666666f, -0.428812f, -7.690082f, 0.666666f, 6.309870f, 7.571190f, -16.428828f, 8.214411f },
            new float[] { 0.214407f, -0.428805f, -1.690100f, 2.237835f, -1.118922f, -0.428812f, 0.666666f, 0.666666f, 2.237840f, 6.309870f, 0.666666f, -7.690082f, -16.428828f, 7.571190f, 8.214411f }
        };

        private static readonly int[] gRowIndices = new int[15] { 0, 1, 15, 7, 5, 2, 4, 8, 6, 17, 14, 16, 11, 12, 10 };

        public static void convertToLoop<REAL>(SourcePatch sourcePatch, SparseMatrix<REAL> matrix)
            where REAL : unmanaged, IFloatingPointIeee754<REAL>
        {
            SparseMatrix<REAL> G = new SparseMatrix<REAL>();
            convertToGregory<REAL>(sourcePatch, G);

            PatchBuilderSparseMatrixUtil<REAL>._initializeFullMatrix(matrix, 12, G.GetNumColumns());

            for (int i = 0; i < 12; ++i)
            {
                REAL[] gRowWeights = new REAL[15];
                for (int j = 0; j < 15; ++j)
                {
                    gRowWeights[j] = REAL.CreateChecked(gregoryToLoopMatrix[i][j]);
                }
                LoopPatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(matrix, i, G, 15, gRowIndices, gRowWeights);
            }
        }

        public LoopPatchBuilder(TopologyRefiner refiner, Options options) :
            base(refiner, options)
        {
            _regPatchType = patchTypeFromBasisArray[(int)_options.regBasisType];
            _irregPatchType = (_options.irregBasisType == BasisType.BASIS_UNSPECIFIED)
                            ? _regPatchType
                            : patchTypeFromBasisArray[(int)_options.irregBasisType];

            _nativePatchType = PatchDescriptor.Type.LOOP;
            _linearPatchType = PatchDescriptor.Type.TRIANGLES;
        }

        protected override PatchDescriptor.Type patchTypeFromBasis(BasisType basis)
        {
            return patchTypeFromBasisArray[(int)basis];
        }

        protected override int convertToPatchType(SourcePatch sourcePatch,
                                                  PatchDescriptor.Type patchType,
                                                  SparseMatrix<float> matrix)
        {
            return convertSourcePatch<float>(sourcePatch, patchType, matrix);
        }

        protected override int convertToPatchType(SourcePatch sourcePatch,
                                                  PatchDescriptor.Type patchType,
                                                  SparseMatrix<double> matrix)
        {
            return convertSourcePatch<double>(sourcePatch, patchType, matrix);
        }

        private int convertSourcePatch<REAL>(SourcePatch sourcePatch,
                                             PatchDescriptor.Type patchType,
                                             SparseMatrix<REAL> matrix) where REAL : unmanaged, IFloatingPointIeee754<REAL>
        {
            System.Diagnostics.Debug.Assert(_schemeType == SchemeType.SCHEME_LOOP);

            if (patchType == PatchDescriptor.Type.LOOP)
            {
                convertToLoop<REAL>(sourcePatch, matrix);
            }
            else if (patchType == PatchDescriptor.Type.TRIANGLES)
            {
                convertToLinear<REAL>(sourcePatch, matrix);
            }
            else if (patchType == PatchDescriptor.Type.GREGORY_TRIANGLE)
            {
                convertToGregory<REAL>(sourcePatch, matrix);
            }
            else
            {
                System.Diagnostics.Debug.Assert(false, "Unknown or unsupported patch type");
            }
            return matrix.GetNumRows();
        }
    }
}
