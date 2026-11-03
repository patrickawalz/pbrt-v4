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
    //  Core functions for computing Catmark limit properties that are used
    //  in the conversion to multiple patch types.
    //
    //  Regarding support for multiple precision, like Sdc, some intermediate
    //  calculations are performed in double and cast to float.
    //
    public static class CatmarkLimits<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        //
        //  Lookup table and formula for the scale factor applied to limit
        //  tangents that arises from eigen values of the subdivision matrix.
        //  Historically 30 values have been stored -- up to valence 29.
        //
        private static readonly double[] efTable = new double[]
        {
            0.0,                    0.0,                    0.0,
            8.1281572906372312e-01, 0.5,                    3.6364406329142801e-01,
            2.8751379706077085e-01, 2.3868786685851678e-01, 2.0454364190756097e-01,
            1.7922903958061159e-01, 1.5965737079986253e-01, 1.4404233443011302e-01,
            1.3127568415883017e-01, 1.2063172212675841e-01, 1.1161437506676930e-01,
            1.0387245516114274e-01, 9.7150019090724835e-02, 9.1255917505950648e-02,
            8.6044378511602668e-02, 8.1402211336798411e-02, 7.7240129516184072e-02,
            7.3486719751997026e-02, 7.0084157479797987e-02, 6.6985104030725440e-02,
            6.4150420569810074e-02, 6.1547457638637268e-02, 5.9148757447233989e-02,
            5.6931056818776957e-02, 5.4874512279256417e-02, 5.2962091433796134e-02
        };

        private static double computeCoefficient(int valence)
        {
            System.Diagnostics.Debug.Assert(valence > 0);
            if (valence < 30)
            {
                return efTable[valence];
            }

            double invValence = 1.0 / valence;
            double cosT = Math.Cos(2.0 * Math.PI * invValence);
            double divisor = (cosT + 5.0) + Math.Sqrt((cosT + 9.0) * (cosT + 1.0));

            return (16.0 * invValence / divisor);
        }

        public static void ComputeInteriorPointWeights(int valence, int faceInRing,
                    Span<REAL> pWeights, Span<REAL> epWeights, Span<REAL> emWeights)
        {
            //
            //  For the limit tangents of an interior vertex, the second tangent is a
            //  rotation of the first, i.e. the coefficients for the ring around the
            //  vertex can be simply shifted by two.  So there is really no need to
            //  compute it explicitly here.  The single tangent can similarly be
            //  oriented along the corresponding edges for Ep and Em and scaled and
            //  offset by P accordingly.
            //
            bool computeEdgePoints = !epWeights.IsEmpty && !emWeights.IsEmpty;

            double fValence = (double)valence;
            double oneOverValence = 1.0f / fValence;
            double oneOverValPlus5 = 1.0f / (fValence + 5.0f);

            double pCoeff = oneOverValence * oneOverValPlus5;
            double tanCoeff = computeCoefficient(valence) * 0.5f * oneOverValPlus5;

            double faceAngle = 2.0 * Math.PI * oneOverValence;

            //
            //  Assign position weights directly while accumulating an intermediate set
            //  of weights for the limit tangent.  And skip over the first weight for
            //  the corner vertex once assigned (zero for tangents) so that we don't
            //  have to deal with the off-by-one offset within the loop:
            //
            int weightWidth = 1 + 2 * valence;
            REAL[] tanWeights = new REAL[weightWidth];

            pWeights[0] = R(fValence * oneOverValPlus5);

            Span<REAL> pW = pWeights.Slice(1);
            Span<REAL> tW = new Span<REAL>(tanWeights, 1, weightWidth - 1);
            for (int i = 0; i < valence; ++i)
            {
                pW[2 * i] = R(pCoeff * 4.0f);
                pW[2 * i + 1] = R(pCoeff);

                if (computeEdgePoints)
                {
                    int iPrev = (i + valence - 1) % valence;
                    int iNext = (i + 1) % valence;

                    double cosICoeff = tanCoeff * Math.Cos(faceAngle * i);

                    tW[2 * iPrev] += R(cosICoeff * 2.0f);
                    tW[2 * iPrev + 1] += R(cosICoeff);
                    tW[2 * i] += R(cosICoeff * 4.0f);
                    tW[2 * i + 1] += R(cosICoeff);
                    tW[2 * iNext] += R(cosICoeff * 2.0f);
                }
            }

            //
            //  Rotate/permute the scaled tangent weights along edges and add to P:
            //
            if (computeEdgePoints)
            {
                int epOffset = 2 * ((valence - faceInRing) % valence);
                int emOffset = 2 * ((valence - faceInRing - 1 + valence) % valence);

                epWeights[0] = pWeights[0];
                emWeights[0] = pWeights[0];
                for (int i = 1; i < weightWidth; ++i)
                {
                    int ip = i + epOffset;
                    if (ip >= weightWidth)
                    {
                        ip -= weightWidth - 1;
                    }

                    int im = i + emOffset;
                    if (im >= weightWidth)
                    {
                        im -= weightWidth - 1;
                    }

                    epWeights[i] = pWeights[i] + tanWeights[ip];
                    emWeights[i] = pWeights[i] + tanWeights[im];
                }
            }
        }

        public static void ComputeBoundaryPointWeights(int valence, int faceInRing,
                    Span<REAL> pWeights, Span<REAL> epWeights, Span<REAL> emWeights)
        {
            int numFaces = valence - 1;
            double faceAngle = Math.PI / numFaces;

            int weightWidth = 2 * valence;

            int N = weightWidth - 1;

            //
            //  Position weights are trivial:
            //
            pWeights.Slice(0, weightWidth).Clear();

            pWeights[0] = R(4.0 / 6.0);
            pWeights[1] = R(1.0 / 6.0);
            pWeights[N] = R(1.0 / 6.0);

            if (epWeights.IsEmpty && emWeights.IsEmpty)
            {
                return;
            }

            //
            //  Ep and Em weights are computed by combining weights for the boundary
            //  and interior tangents.  The boundary tangent is trivially represented
            //  by two non-zero weights, so allocate and compute weights for the
            //  interior tangent:
            //
            double tBoundaryCoeff_1 = (1.0 / 6.0);
            double tBoundaryCoeff_N = (-1.0 / 6.0);

            REAL[] tanWeights = new REAL[weightWidth];
            {
                double k = (double)numFaces;
                double theta = faceAngle;
                double c = Math.Cos(theta);
                double s = Math.Sin(theta);
                double div3 = 1.0 / 3.0;
                double div3kc = 1.0f / (3.0f * k + c);
                double gamma = -4.0f * s * div3kc;
                double alpha_0k = -((1.0f + 2.0f * c) * Math.Sqrt(1.0f + c)) * div3kc
                                / Math.Sqrt(1.0f - c);
                double beta_0 = s * div3kc;

                tanWeights[0] = R(gamma * div3);
                tanWeights[1] = R(alpha_0k * div3);
                tanWeights[2] = R(beta_0 * div3);
                tanWeights[N] = R(alpha_0k * div3);

                for (int i = 1; i < valence - 1; ++i)
                {
                    double sinThetaI = Math.Sin(theta * i);
                    double sinThetaIplus1 = Math.Sin(theta * (i + 1));

                    double alpha = 4.0f * sinThetaI * div3kc;
                    double beta = (sinThetaI + sinThetaIplus1) * div3kc;

                    tanWeights[1 + 2 * i] = R(alpha * div3);
                    tanWeights[1 + 2 * i + 1] = R(beta * div3);
                }
            }

            //
            //  Compute Ep weights -- trivial case if on the leading face and edge:
            //
            if (faceInRing == 0)
            {
                //  Ep is on boundary edge and has only two weights:  w[1] and w[N]
                epWeights.Slice(0, weightWidth).Clear();

                epWeights[0] = R(2.0 / 3.0);
                epWeights[1] = R(1.0 / 3.0);
            }
            else
            {
                //  Ep is on interior edge and has all weights
                int iEdgeNext = faceInRing;
                double faceAngleNext = faceAngle * iEdgeNext;
                double cosAngleNext = Math.Cos(faceAngleNext);
                double sinAngleNext = Math.Sin(faceAngleNext);

                for (int i = 0; i < weightWidth; ++i)
                {
                    epWeights[i] = R(double.CreateChecked(tanWeights[i]) * sinAngleNext);
                }
                epWeights[0] += pWeights[0];
                epWeights[1] += pWeights[1] + R(tBoundaryCoeff_1 * cosAngleNext);
                epWeights[N] += pWeights[N] + R(tBoundaryCoeff_N * cosAngleNext);
            }

            //
            //  Compute Em weights -- trivial case if on the trailing face and edge:
            //
            if (faceInRing == (numFaces - 1))
            {
                //  Em is on boundary edge and has only two weights:  w[1] and w[N]
                emWeights.Slice(0, weightWidth).Clear();

                emWeights[0] = R(2.0 / 3.0);
                emWeights[N] = R(1.0 / 3.0);
            }
            else
            {
                //  Em is on interior edge and has all weights
                int iEdgePrev = (faceInRing + 1) % valence;
                double faceAnglePrev = faceAngle * iEdgePrev;
                double cosAnglePrev = Math.Cos(faceAnglePrev);
                double sinAnglePrev = Math.Sin(faceAnglePrev);

                for (int i = 0; i < weightWidth; ++i)
                {
                    emWeights[i] = R(double.CreateChecked(tanWeights[i]) * sinAnglePrev);
                }
                emWeights[0] += pWeights[0];
                emWeights[1] += pWeights[1] + R(tBoundaryCoeff_1 * cosAnglePrev);
                emWeights[N] += pWeights[N] + R(tBoundaryCoeff_N * cosAnglePrev);
            }
        }
    }

    //
    //  SparseMatrixRow
    //
    //  This is a utility class representing a row of a SparseMatrix -- which
    //  in turn corresponds to a point of a resulting patch.  Instances of this
    //  class are intended to encapsulate the contributions of a point and be
    //  passed to functions as such.
    //
    public class SparseMatrixRow<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        public SparseMatrixRow(SparseMatrix<REAL> matrix, int row)
        {
            _size = matrix.GetRowSize(row);
            _indices = matrix.SetRowColumns(row);
            _weights = matrix.SetRowElements(row);
        }

        public int GetSize()
        {
            return _size;
        }

        public void Assign(int rowEntry, int index, REAL weight)
        {
            _indices[rowEntry] = index;
            _weights[rowEntry] = weight;
        }

        public void Copy(SparseMatrixRow<REAL> other)
        {
            System.Diagnostics.Debug.Assert(GetSize() == other.GetSize());
            for (int i = 0; i < _size; ++i)
            {
                _indices[i] = other._indices[i];
            }
            for (int i = 0; i < _size; ++i)
            {
                _weights[i] = other._weights[i];
            }
        }

        public int _size;
        public Vtr.Array<int> _indices;
        public Vtr.Array<REAL> _weights;
    }

    //
    //  Simple utility functions for dealing with SparseMatrix:
    //
    public static class PatchBuilderSparseMatrixUtil<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        public static void _initializeFullMatrix(SparseMatrix<REAL> M, int nRows, int nColumns)
        {
            M.Resize(nRows, nColumns, nRows * nColumns);

            //  Fill row 0 with index for every column:
            M.SetRowSize(0, nColumns);
            Vtr.Array<int> row0Columns = M.SetRowColumns(0);
            for (int i = 0; i < nColumns; ++i)
            {
                row0Columns[i] = i;
            }

            //  Copy row 0's indices into all other rows:
            for (int row = 1; row < nRows; ++row)
            {
                M.SetRowSize(row, nColumns);
                Vtr.Array<int> dstRowColumns = M.SetRowColumns(row);
                for (int i = 0; i < nColumns; ++i)
                {
                    dstRowColumns[i] = row0Columns[i];
                }
            }
        }

        public static void _resizeMatrix(SparseMatrix<REAL> matrix,
                      int numRows, int numColumns, int numElements,
                      int[] rowSizes)
        {
            matrix.Resize(numRows, numColumns, numElements);
            for (int i = 0; i < numRows; ++i)
            {
                matrix.SetRowSize(i, rowSizes[i]);
            }
            System.Diagnostics.Debug.Assert(matrix.GetNumElements() == numElements);
        }

        public static void _addSparsePointToFullRow(REAL[] fullRow,
                                 SparseMatrixRow<REAL> p,
                                 REAL s, int[] indexMask)
        {
            for (int i = 0; i < p.GetSize(); ++i)
            {
                int index = p._indices[i];

                fullRow[index] += s * p._weights[i];

                indexMask[index] = 1 + index;
            }
        }

        public static void _addSparseRowToFull(REAL[] fullRow,
                            SparseMatrix<REAL> M, int sparseRow, REAL s)
        {
            ConstArray<int> indices = M.GetRowColumns(sparseRow);
            ConstArray<REAL> weights = M.GetRowElements(sparseRow);

            for (int i = 0; i < indices.size(); ++i)
            {
                fullRow[indices[i]] += s * weights[i];
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
                _addSparseRowToFull(dstRow, srcMatrix, srcRowIndices[i], srcRowWeights[i]);
            }
            for (int i = 0; i < numColumns; ++i)
            {
                dstRowArray[i] = dstRow[i];
            }
        }

        //
        //  The valence-2 interior case poses problems for the way patch points
        //  are computed as combinations of source points and stored as a row in
        //  a SparseMatrix.  An interior vertex of valence-2 causes duplicate
        //  vertices to appear in the 1-rings of its neighboring vertices and we
        //  want the entries of a SparseMatrix row to be unique.
        //
        public static void _removeValence2Duplicates(SparseMatrix<REAL> M)
        {
            //  This will later be determined by the PatchBuilder member:
            int regFaceSize = 4;

            SparseMatrix<REAL> T = new SparseMatrix<REAL>();
            T.Resize(M.GetNumRows(), M.GetNumColumns(), M.GetNumElements());

            int nRows = M.GetNumRows();
            for (int row = 0; row < nRows; ++row)
            {
                int srcRowSize = M.GetRowSize(row);

                ConstArray<int> srcIndices = M.GetRowColumns(row);
                ConstArray<REAL> srcWeights = M.GetRowElements(row);

                //  Scan the entries to see if there are duplicates -- copy
                //  the row if not, otherwise, need to compress it:
                bool[] cornerUsed = new bool[4] { false, false, false, false };

                int srcDupCount = 0;
                for (int i = 0; i < srcRowSize; ++i)
                {
                    int srcIndex = srcIndices[i];
                    if (srcIndex < regFaceSize)
                    {
                        srcDupCount += cornerUsed[srcIndex] ? 1 : 0;
                        cornerUsed[srcIndex] = true;
                    }
                }

                //  Size this row for the destination and copy or compress:
                T.SetRowSize(row, srcRowSize - srcDupCount);

                Vtr.Array<int> dstIndices = T.SetRowColumns(row);
                Vtr.Array<REAL> dstWeights = T.SetRowElements(row);

                if (srcDupCount != 0)
                {
                    int[] cornerDstPos = new int[4] { -1, -1, -1, -1 };

                    int dstPos = 0;
                    for (int i = 0; i < srcRowSize; ++i)
                    {
                        int srcIndex = srcIndices[i];
                        REAL srcWeight = srcWeights[i];

                        if (srcIndex < regFaceSize)
                        {
                            if (cornerDstPos[srcIndex] >= 0)
                            {
                                dstWeights[cornerDstPos[srcIndex]] += srcWeight;
                                continue;
                            }
                            else
                            {
                                cornerDstPos[srcIndex] = dstPos;
                            }
                        }
                        dstIndices[dstPos] = srcIndex;
                        dstWeights[dstPos] = srcWeight;
                        ++dstPos;
                    }
                }
                else
                {
                    for (int i = 0; i < srcRowSize; ++i)
                    {
                        dstIndices[i] = srcIndices[i];
                    }
                    for (int i = 0; i < srcRowSize; ++i)
                    {
                        dstWeights[i] = srcWeights[i];
                    }
                }
            }
            M.Swap(T);
        }
    }

    //
    //  GregoryConverter
    //
    //  The GregoryConverter class essentially provides a change-of-basis matrix
    //  from source vertices in a Catmull-Clark mesh to the 20 control points of a
    //  Gregory patch.
    //
    //  Control points are labeled using the convention from:  "Approximating
    //  Subdivision Surfaces with Gregory Patches for Hardware Tessellation" Loop,
    //  Schaefer, Ni, Castano (ACM ToG Siggraph Asia 2009)
    //
    //     P3         e3-      e2+         P2
    //        x--------x--------x--------x
    //        |        |        |        |
    //        |        |        |        |
    //        |        | f3-    | f2+    |
    //        |        x        x        |
    //    e3+ x------x            x------x e2-
    //        |     f3+          f2-     |
    //        |                          |
    //        |                          |
    //        |     f0-          f1+     |
    //    e0- x------x            x------x e1+
    //        |        x        x        |
    //        |        | f0+    | f1-    |
    //        |        |        |        |
    //        |        |        |        |
    //        x--------x--------x--------x
    //     P0         e0+      e1-         P1
    //
    public class GregoryConverter<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        //
        //  Local nested class for GregoryConverter to cache information for the
        //  corners of the source patch.  It copies some information from the
        //  SourcePatch so that we don't have to keep it around, but it contains
        //  additional information relevant to the determination of the Gregory
        //  points -- most notably classifications of the face-points and the
        //  sines/cosines of angles for the face corners that are used repeatedly.
        //
        public class CornerTopology
        {
            //  Basic flags copied from the SourcePatch
            public bool isBoundary;
            public bool isSharp;
            public bool isDart;
            public bool isRegular;
            public bool isVal2Int;

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
            public REAL sinFaceAngle;

            //  Its useful to have the ring for each corner immediately available:
            //  (the array has at least 40 entries, as the C++ StackBuffer does,
            //  and ringPointsSize is the logical size)
            public int[] ringPoints = new int[40];
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

        public GregoryConverter()
        {
            _numSourcePoints = 0;
            _corners = new CornerTopology[4];
            for (int i = 0; i < 4; ++i)
            {
                _corners[i] = new CornerTopology();
            }
        }

        public GregoryConverter(SourcePatch sourcePatch) : this()
        {
            Initialize(sourcePatch);
        }

        public GregoryConverter(SourcePatch sourcePatch, SparseMatrix<REAL> sparseMatrix) : this()
        {
            Initialize(sourcePatch);
            Convert(sparseMatrix);
        }

        //
        //  Construction and initialization/computation of the change-of-basis
        //  matrix to a Gregory patch.
        //
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

            for (int cIndex = 0; cIndex < 4; ++cIndex)
            {
                SourcePatch.Corner srcCorner = sourcePatch._corners[cIndex];

                CornerTopology corner = _corners[cIndex];

                corner.isBoundary = srcCorner._boundary;
                corner.isSharp = srcCorner._sharp;
                corner.isDart = srcCorner._dart;
                corner.numFaces = srcCorner._numFaces;
                corner.faceInRing = srcCorner._patchFace;
                corner.isVal2Int = srcCorner._val2Interior;
                corner.valence = corner.numFaces + (corner.isBoundary ? 1 : 0);

                corner.isRegular = ((corner.numFaces << (corner.isBoundary ? 1 : 0)) == 4)
                                 && !corner.isSharp;
                if (corner.isRegular)
                {
                    corner.faceAngle = R(Math.PI / 2.0);
                    corner.cosFaceAngle = R(0.0f);
                    corner.sinFaceAngle = R(1.0f);
                }
                else
                {
                    corner.faceAngle =
                        (corner.isBoundary ? R(Math.PI) : (R(2.0f) * R(Math.PI)))
                            / R(corner.numFaces);
                    corner.cosFaceAngle = REAL.Cos(corner.faceAngle);
                    corner.sinFaceAngle = REAL.Sin(corner.faceAngle);
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
            for (int cIndex = 0; cIndex < 4; ++cIndex)
            {
                CornerTopology corner = _corners[cIndex];

                int cNext = (cIndex + 1) & 0x3;
                int cPrev = (cIndex + 3) & 0x3;

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
            //  (and note that specializing the popoluation of the matrix may also be
            //  worthwhile in such cases)
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
            int maxRingSize = 1 + 2 * _maxValence;
            int weightBufferSize = Math.Max(3 * maxRingSize, 2 * _numSourcePoints);

            REAL[] weightBuffer = new REAL[weightBufferSize];
            int[] indexBuffer = new int[weightBufferSize];

            for (int cIndex = 0; cIndex < 4; ++cIndex)
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

            for (int cIndex = 0; cIndex < 4; ++cIndex)
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
            if (_hasVal2InteriorCorner)
            {
                PatchBuilderSparseMatrixUtil<REAL>._removeValence2Duplicates(matrix);
            }
        }

        //
        //  Methods to resize the matrix before populating it:
        //
        private void resizeMatrixIsolatedIrregular(
                SparseMatrix<REAL> matrix, int cornerIndex, int cornerValence)
        {
            int irregRingSize = 1 + 2 * cornerValence;

            int irregCorner = cornerIndex;
            int irregPlus = (cornerIndex + 1) & 0x3;
            int irregOpposite = (cornerIndex + 2) & 0x3;
            int irregMinus = (cornerIndex + 3) & 0x3;

            int[] rowSizes = new int[20];
            int rowSizePtr = 0;

            rowSizePtr = irregCorner * 5;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;
            rowSizes[rowSizePtr++] = irregRingSize;

            rowSizePtr = irregPlus * 5;
            rowSizes[rowSizePtr++] = 9;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 4;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;

            rowSizePtr = irregOpposite * 5;
            rowSizes[rowSizePtr++] = 9;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 4;
            rowSizes[rowSizePtr++] = 4;

            rowSizePtr = irregMinus * 5;
            rowSizes[rowSizePtr++] = 9;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 6;
            rowSizes[rowSizePtr++] = 3 + irregRingSize;
            rowSizes[rowSizePtr++] = 4;

            int numElements = 7 * irregRingSize + 85;

            PatchBuilderSparseMatrixUtil<REAL>._resizeMatrix(matrix, 20, _numSourcePoints, numElements, rowSizes);
        }

        private void resizeMatrixUnisolated(SparseMatrix<REAL> matrix)
        {
            int[] rowSizes = new int[20];

            int numElements = 0;

            for (int cIndex = 0; cIndex < 4; ++cIndex)
            {
                int rowSize = cIndex * 5;

                CornerTopology corner = _corners[cIndex];

                //  First, the corner and pair of edge points:
                if (corner.isRegular)
                {
                    if (!corner.isBoundary)
                    {
                        rowSizes[rowSize + 0] = 9;
                        rowSizes[rowSize + 1] = 6;
                        rowSizes[rowSize + 2] = 6;
                    }
                    else
                    {
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = corner.epOnBoundary ? 2 : 6;
                        rowSizes[rowSize + 2] = corner.emOnBoundary ? 2 : 6;
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
                        int ringSize = 1 + 2 * corner.valence;
                        rowSizes[rowSize + 0] = ringSize;
                        rowSizes[rowSize + 1] = ringSize;
                        rowSizes[rowSize + 2] = ringSize;
                    }
                    else if (corner.numFaces > 1)
                    {
                        int ringSize = 1 + corner.valence + corner.numFaces;
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = corner.epOnBoundary ? 2 : ringSize;
                        rowSizes[rowSize + 2] = corner.emOnBoundary ? 2 : ringSize;
                    }
                    else
                    {
                        rowSizes[rowSize + 0] = 3;
                        rowSizes[rowSize + 1] = 2;
                        rowSizes[rowSize + 2] = 2;
                    }
                }
                numElements += rowSizes[rowSize + 0] + rowSizes[rowSize + 1] + rowSizes[rowSize + 2];

                //  Second, the pair of face points:
                rowSizes[rowSize + 3] = 4;
                rowSizes[rowSize + 4] = 4;
                if (!corner.fpIsRegular || !corner.fmIsRegular)
                {
                    int cNext = (cIndex + 1) & 0x3;
                    int cPrev = (cIndex + 3) & 0x3;
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
            }

            PatchBuilderSparseMatrixUtil<REAL>._resizeMatrix(matrix, 20, _numSourcePoints, numElements, rowSizes);
        }

        //
        //  Methods to compute the various rows of points in the matrix:
        //
        private void assignRegularEdgePoints(int cIndex, SparseMatrix<REAL> matrix)
        {
            SparseMatrixRow<REAL> p = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 0);
            SparseMatrixRow<REAL> ep = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1);
            SparseMatrixRow<REAL> em = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 2);

            CornerTopology corner = _corners[cIndex];

            int[] cRing = corner.ringPoints;

            if (!corner.isBoundary)
            {
                p.Assign(0, cIndex, R(4.0 / 9.0));
                p.Assign(1, cRing[0], R(1.0 / 9.0));
                p.Assign(2, cRing[2], R(1.0 / 9.0));
                p.Assign(3, cRing[4], R(1.0 / 9.0));
                p.Assign(4, cRing[6], R(1.0 / 9.0));
                p.Assign(5, cRing[1], R(1.0 / 36.0));
                p.Assign(6, cRing[3], R(1.0 / 36.0));
                p.Assign(7, cRing[5], R(1.0 / 36.0));
                p.Assign(8, cRing[7], R(1.0 / 36.0));
                System.Diagnostics.Debug.Assert(p.GetSize() == 9);

                //  Identify the edges along Ep and Em and those opposite them:
                int iEdgeEp = 2 * corner.faceInRing;
                int iEdgeEm = 2 * ((corner.faceInRing + 1) & 0x3);
                int iEdgeOp = 2 * ((corner.faceInRing + 2) & 0x3);
                int iEdgeOm = 2 * ((corner.faceInRing + 3) & 0x3);

                ep.Assign(0, cIndex, R(4.0 / 9.0));
                ep.Assign(1, cRing[iEdgeEp], R(2.0 / 9.0));
                ep.Assign(2, cRing[iEdgeEm], R(1.0 / 9.0));
                ep.Assign(3, cRing[iEdgeOm], R(1.0 / 9.0));
                ep.Assign(4, cRing[iEdgeEp + 1], R(1.0 / 18.0));
                ep.Assign(5, cRing[iEdgeOm + 1], R(1.0 / 18.0));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 6);

                em.Assign(0, cIndex, R(4.0 / 9.0));
                em.Assign(1, cRing[iEdgeEm], R(2.0 / 9.0));
                em.Assign(2, cRing[iEdgeEp], R(1.0 / 9.0));
                em.Assign(3, cRing[iEdgeOp], R(1.0 / 9.0));
                em.Assign(4, cRing[iEdgeEp + 1], R(1.0 / 18.0));
                em.Assign(5, cRing[iEdgeEm + 1], R(1.0 / 18.0));
                System.Diagnostics.Debug.Assert(em.GetSize() == 6);
            }
            else
            {
                //  Decide which point corresponds to interior vs exterior tangent:
                SparseMatrixRow<REAL> eBoundary = corner.epOnBoundary ? ep : em;
                SparseMatrixRow<REAL> eInterior = corner.epOnBoundary ? em : ep;
                int iBoundary = corner.epOnBoundary ? 0 : 4;

                p.Assign(0, cIndex, R(2.0 / 3.0));
                p.Assign(1, cRing[0], R(1.0 / 6.0));
                p.Assign(2, cRing[4], R(1.0 / 6.0));
                System.Diagnostics.Debug.Assert(p.GetSize() == 3);

                eBoundary.Assign(0, cIndex, R(2.0 / 3.0));
                eBoundary.Assign(1, cRing[iBoundary], R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(eBoundary.GetSize() == 2);

                eInterior.Assign(0, cIndex, R(4.0 / 9.0));
                eInterior.Assign(1, cRing[2], R(2.0 / 9.0));
                eInterior.Assign(2, cRing[0], R(1.0 / 9.0));
                eInterior.Assign(3, cRing[4], R(1.0 / 9.0));
                eInterior.Assign(4, cRing[1], R(1.0 / 18.0));
                eInterior.Assign(5, cRing[3], R(1.0 / 18.0));
                System.Diagnostics.Debug.Assert(eInterior.GetSize() == 6);
            }
        }

        private void computeIrregularEdgePoints(int cIndex,
                SparseMatrix<REAL> matrix, REAL[] weightBuffer)
        {
            SparseMatrixRow<REAL> p = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 0);
            SparseMatrixRow<REAL> ep = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 1);
            SparseMatrixRow<REAL> em = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 2);

            //
            //  The corner and edge points P, Ep and Em  are completely determined
            //  by the 1-ring of vertices around (and including) the corner vertex.
            //  We combine full sets of coefficients for the vertex and its 1-ring.
            //
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
                ep.Assign(1, (cIndex + 1) & 0x3, R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 2);

                em.Assign(0, cIndex, R(2.0 / 3.0));
                em.Assign(1, (cIndex + 3) & 0x3, R(1.0 / 3.0));
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
                p.Assign(1, (cIndex + 1) & 0x3, R(1.0 / 6.0));
                p.Assign(2, (cIndex + 3) & 0x3, R(1.0 / 6.0));
                System.Diagnostics.Debug.Assert(p.GetSize() == 3);

                ep.Assign(0, cIndex, R(2.0 / 3.0));
                ep.Assign(1, (cIndex + 1) & 0x3, R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(ep.GetSize() == 2);

                em.Assign(0, cIndex, R(2.0 / 3.0));
                em.Assign(1, (cIndex + 3) & 0x3, R(1.0 / 3.0));
                System.Diagnostics.Debug.Assert(em.GetSize() == 2);
            }
        }

        private void computeIrregularInteriorEdgePoints(
                int cIndex,
                SparseMatrixRow<REAL> p, SparseMatrixRow<REAL> ep, SparseMatrixRow<REAL> em,
                REAL[] ringWeights)
        {
            CornerTopology corner = _corners[cIndex];

            int valence = corner.valence;
            int weightWidth = 1 + 2 * valence;

            Span<REAL> pWeights = new Span<REAL>(ringWeights, 0, weightWidth);
            Span<REAL> epWeights = new Span<REAL>(ringWeights, weightWidth, weightWidth);
            Span<REAL> emWeights = new Span<REAL>(ringWeights, weightWidth * 2, weightWidth);

            CatmarkLimits<REAL>.ComputeInteriorPointWeights(valence, corner.faceInRing,
                    pWeights, epWeights, emWeights);

            p.Assign(0, cIndex, pWeights[0]);
            ep.Assign(0, cIndex, epWeights[0]);
            em.Assign(0, cIndex, emWeights[0]);

            for (int i = 1; i < weightWidth; ++i)
            {
                int pRingPoint = corner.ringPoints[i - 1];

                p.Assign(i, pRingPoint, pWeights[i]);
                ep.Assign(i, pRingPoint, epWeights[i]);
                em.Assign(i, pRingPoint, emWeights[i]);
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
            int weightWidth = 1 + corner.valence + corner.numFaces;

            Span<REAL> pWeights = new Span<REAL>(ringWeights, 0, weightWidth);
            Span<REAL> epWeights = new Span<REAL>(ringWeights, weightWidth, weightWidth);
            Span<REAL> emWeights = new Span<REAL>(ringWeights, weightWidth * 2, weightWidth);

            CatmarkLimits<REAL>.ComputeBoundaryPointWeights(valence, corner.faceInRing,
                        pWeights, epWeights, emWeights);

            //
            //  Identify the neighboring points of the boundary:
            //
            int N = weightWidth - 1;

            int p0 = cIndex;
            int p1 = corner.ringPoints[0];
            int pN = corner.ringPoints[2 * (valence - 1)];

            p.Assign(0, p0, pWeights[0]);
            p.Assign(1, p1, pWeights[1]);
            p.Assign(2, pN, pWeights[N]);
            System.Diagnostics.Debug.Assert(p.GetSize() == 3);

            ep.Assign(0, p0, epWeights[0]);
            if (corner.epOnBoundary)
            {
                ep.Assign(1, p1, epWeights[1]);
                System.Diagnostics.Debug.Assert(ep.GetSize() == 2);
            }
            else
            {
                for (int i = 1; i < weightWidth; ++i)
                {
                    ep.Assign(i, corner.ringPoints[i - 1], epWeights[i]);
                }
                System.Diagnostics.Debug.Assert(ep.GetSize() == weightWidth);
            }

            em.Assign(0, p0, emWeights[0]);
            if (corner.emOnBoundary)
            {
                em.Assign(1, pN, emWeights[N]);
                System.Diagnostics.Debug.Assert(em.GetSize() == 2);
            }
            else
            {
                //  Note: the C++ loop bound here is "i <= weightWidth", whose final
                //  iteration reads/writes one entry beyond the row (the write lands
                //  in the first entry of the following face-point row, which is
                //  always fully overwritten afterwards) -- the in-range entries are
                //  all that affects the result.
                for (int i = 1; i < weightWidth; ++i)
                {
                    em.Assign(i, corner.ringPoints[i - 1], emWeights[i]);
                }
                System.Diagnostics.Debug.Assert(em.GetSize() == weightWidth);
            }
        }

        private int getIrregularFacePointSize(int cIndexNear, int cIndexFar)
        {
            CornerTopology corner = _corners[cIndexNear];
            CornerTopology adjCorner = _corners[cIndexFar];

            if (corner.isSharp && adjCorner.isSharp)
            {
                return 2;
            }

            int thisSize = corner.isSharp
                         ? 6
                         : (1 + corner.GetSize());

            int adjSize = (adjCorner.isRegular || adjCorner.isSharp)
                        ? 0
                        : (1 + adjCorner.GetSize() - 6);

            return thisSize + adjSize;
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

            REAL pCoeff = cosFar / R(3.0f);
            REAL eNearCoeff = (R(3.0f) - R(2.0f) * cosNear - cosFar) / R(3.0f);
            REAL eFarCoeff = R(2.0f) * cosNear / R(3.0f);

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
            //  Add the contributions of the four points in the 1-ring that are on
            //  the edges adjacent to the interior edge:
            //
            int iEdgeInterior = edgeInNearCornerRing;
            int iEdgePrev = (iEdgeInterior + valence - 1) % valence;
            int iEdgeNext = (iEdgeInterior + 1) % valence;

            rowWeights[cornerNear.ringPoints[2 * iEdgePrev]] += -signForSideOfEdge / R(9.0f);
            rowWeights[cornerNear.ringPoints[2 * iEdgePrev + 1]] += -signForSideOfEdge / R(18.0f);
            rowWeights[cornerNear.ringPoints[2 * iEdgeInterior + 1]] += signForSideOfEdge / R(18.0f);
            rowWeights[cornerNear.ringPoints[2 * iEdgeNext]] += signForSideOfEdge / R(9.0f);

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
            SparseMatrixRow<REAL> fp = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 3);
            SparseMatrixRow<REAL> fm = new SparseMatrixRow<REAL>(matrix, 5 * cIndex + 4);

            CornerTopology corner = _corners[cIndex];

            int cNext = (cIndex + 1) & 0x3;
            int cOpp = (cIndex + 2) & 0x3;
            int cPrev = (cIndex + 3) & 0x3;

            if (corner.fpIsRegular)
            {
                fp.Assign(0, cIndex, R(4.0 / 9.0));
                fp.Assign(1, cPrev, R(2.0 / 9.0));
                fp.Assign(2, cNext, R(2.0 / 9.0));
                fp.Assign(3, cOpp, R(1.0 / 9.0));
                System.Diagnostics.Debug.Assert(fp.GetSize() == 4);
            }
            if (corner.fmIsRegular)
            {
                fm.Assign(0, cIndex, R(4.0 / 9.0));
                fm.Assign(1, cPrev, R(2.0 / 9.0));
                fm.Assign(2, cNext, R(2.0 / 9.0));
                fm.Assign(3, cOpp, R(1.0 / 9.0));
                System.Diagnostics.Debug.Assert(fm.GetSize() == 4);
            }
        }

        private void computeIrregularFacePoints(int cIndex,
                SparseMatrix<REAL> matrix, REAL[] rowWeights, int[] columnMask)
        {
            CornerTopology corner = _corners[cIndex];

            int cNext = (cIndex + 1) & 0x3;
            int cPrev = (cIndex + 3) & 0x3;

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

        private int _numSourcePoints;
        private int _maxValence;

        private bool _isIsolatedInteriorPatch;
        private bool _hasVal2InteriorCorner;
        private int _isolatedCorner;
        private int _isolatedValence;

        private CornerTopology[] _corners;
    }

    //
    //  BSplineConverter
    //
    //  Conversion of the source patch to the 16 control points of a B-spline
    //  (regular) patch, derived from the Gregory conversion or computed directly
    //  for the common isolated irregular corner.
    //
    public class BSplineConverter<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        public BSplineConverter()
        {
            _sourcePatch = null;
            _gregoryConverter = new GregoryConverter<REAL>();
        }

        public BSplineConverter(SourcePatch sourcePatch) : this()
        {
            Initialize(sourcePatch);
        }

        public BSplineConverter(SourcePatch sourcePatch, SparseMatrix<REAL> matrix) : this()
        {
            Initialize(sourcePatch);
            Convert(matrix);
        }

        public void Initialize(SourcePatch sourcePatch)
        {
            _sourcePatch = sourcePatch;
            _gregoryConverter.Initialize(sourcePatch);
        }

        public void Convert(SparseMatrix<REAL> matrix)
        {
            if (_gregoryConverter.IsIsolatedInteriorPatch())
            {
                convertIrregularCorner(_gregoryConverter.GetIsolatedInteriorCorner(),
                                       matrix);
            }
            else
            {
                SparseMatrix<REAL> gregoryMatrix = new SparseMatrix<REAL>();
                _gregoryConverter.Convert(gregoryMatrix);

                convertFromGregory(gregoryMatrix, matrix);
            }
        }

        private static readonly REAL[] wCorner = new REAL[9] { R(49.0), R(-42.0), R(-42.0), R(36.0), R(-14.0), R(-14.0), R(12.0), R(12.0), R(4.0) };
        private static readonly REAL[] wBoundary = new REAL[6] { R(-14.0), R(12.0), R(7.0), R(-6.0), R(4.0), R(-2.0) };
        private static readonly REAL[] wInterior = new REAL[4] { R(4.0), R(-2.0), R(-2.0), R(1.0) };

        private static readonly int[][] pIndices = new int[][]
        {
            new int[] {  3,  1,  2,  0,  8, 18,  7, 16, 13 },
            new int[] {  8,  6,  7,  5,  3, 13, 12,  1, 18 },
            new int[] { 13, 11, 12, 10, 18,  8, 17,  6,  3 },
            new int[] { 18, 16, 17, 15, 13,  3,  2, 11,  8 }
        };
        private static readonly int[][] epIndices = new int[][]
        {
            new int[] {  3,  1,  8,  7, 18, 13 },
            new int[] {  8,  6, 13, 12,  3, 18 },
            new int[] { 13, 11, 18, 17,  8,  3 },
            new int[] { 18, 16,  3,  2, 13,  8 }
        };
        private static readonly int[][] emIndices = new int[][]
        {
            new int[] {  3,  2, 18, 16,  8, 13 },
            new int[] {  8,  7,  3,  1, 13, 18 },
            new int[] { 13, 12,  8,  6, 18,  3 },
            new int[] { 18, 17, 13, 11,  3,  8 }
        };
        private static readonly int[][] fIndices = new int[][]
        {
            new int[] {  3,  8, 18, 13 },
            new int[] {  8, 13,  3, 18 },
            new int[] { 13, 18,  8,  3 },
            new int[] { 18,  3, 13,  8 }
        };

        private void convertFromGregory(SparseMatrix<REAL> G, SparseMatrix<REAL> B)
        {
            //
            //  Initialize the full 16 x N matrix and combine the rows of the Gregory
            //  matrix, using the weights for the corner, boundary and interior points
            //  of the B-spline patch:
            //
            PatchBuilderSparseMatrixUtil<REAL>._initializeFullMatrix(B, 16, G.GetNumColumns());

            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 0, G, 9, pIndices[0], wCorner);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 1, G, 6, epIndices[0], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 2, G, 6, emIndices[1], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 3, G, 9, pIndices[1], wCorner);

            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 4, G, 6, emIndices[0], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 5, G, 4, fIndices[0], wInterior);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 6, G, 4, fIndices[1], wInterior);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 7, G, 6, epIndices[1], wBoundary);

            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 8, G, 6, epIndices[3], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 9, G, 4, fIndices[3], wInterior);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 10, G, 4, fIndices[2], wInterior);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 11, G, 6, emIndices[2], wBoundary);

            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 12, G, 9, pIndices[3], wCorner);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 13, G, 6, emIndices[3], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 14, G, 6, epIndices[2], wBoundary);
            PatchBuilderSparseMatrixUtil<REAL>._combineSparseMatrixRowsInFull(B, 15, G, 9, pIndices[2], wCorner);
        }

        private void buildIrregularCornerMatrix(
                int irregularCornerValence,
                int numSourcePoints, int[] rowsForXPoints,
                SparseMatrix<REAL> matrix)
        {
            //
            //  Rows for the X points have the full ring (plus corner) and the two
            //  with the additional external point have one more -- all others are
            //  single entries:
            //
            int ringSizePlusCorner = 1 + 2 * irregularCornerValence;

            int numElements = 7 * ringSizePlusCorner + 11;

            int[] rowSizes = new int[16];
            for (int i = 0; i < 16; ++i)
            {
                rowSizes[i] = 1;
            }
            rowSizes[rowsForXPoints[0]] = ringSizePlusCorner;
            rowSizes[rowsForXPoints[1]] = ringSizePlusCorner;
            rowSizes[rowsForXPoints[2]] = ringSizePlusCorner;
            rowSizes[rowsForXPoints[3]] = ringSizePlusCorner;
            rowSizes[rowsForXPoints[4]] = ringSizePlusCorner;
            rowSizes[rowsForXPoints[5]] = ringSizePlusCorner + 1;
            rowSizes[rowsForXPoints[6]] = ringSizePlusCorner + 1;

            matrix.Resize(16, numSourcePoints, numElements);
            for (int i = 0; i < 16; ++i)
            {
                matrix.SetRowSize(i, rowSizes[i]);

                Vtr.Array<REAL> rowElements = matrix.SetRowElements(i);
                if (rowSizes[i] == 1)
                {
                    rowElements[0] = R(1.0);
                }
                else
                {
                    for (int j = 0; j < rowSizes[i]; ++j)
                    {
                        rowElements[j] = R(0.0);
                    }
                }
            }
        }

        private static readonly int[][] xRowsAll = new int[][]
        {
            new int[] {  0,  1,  4,  2,  8,  3, 12 },
            new int[] {  3,  7,  2, 11,  1, 15,  0 },
            new int[] { 15, 14, 11, 13,  7, 12,  3 },
            new int[] { 12,  8, 13,  4, 14,  0, 15 }
        };

        private static readonly int[][] extPointRowsAll = new int[][]
        {
            new int[] {  7, 11, 15, 14, 13 },
            new int[] { 14, 13, 12,  8,  4 },
            new int[] {  8,  4,  0,  1,  2 },
            new int[] {  1,  2,  3,  7, 11 }
        };

        private void convertIrregularCorner(int irregularCorner,
                                             SparseMatrix<REAL> matrix)
        {
            //
            //  Gather the limit point weights for the irregular corner:
            //
            int valence = _sourcePatch._corners[irregularCorner]._numFaces;
            int faceInRing = _sourcePatch._corners[irregularCorner]._patchFace;

            int ringSizePlusCorner = 1 + 2 * valence;

            REAL[] limitPointWeights = new REAL[3 * ringSizePlusCorner];

            Span<REAL> wP = new Span<REAL>(limitPointWeights, 0, ringSizePlusCorner);
            Span<REAL> wEp = new Span<REAL>(limitPointWeights, ringSizePlusCorner, ringSizePlusCorner);
            Span<REAL> wEm = new Span<REAL>(limitPointWeights, 2 * ringSizePlusCorner, ringSizePlusCorner);

            System.Diagnostics.Debug.Assert(valence > 2);
            CatmarkLimits<REAL>.ComputeInteriorPointWeights(valence, faceInRing, wP, wEp, wEm);

            //
            //  Identify the rows to be populated (X points) and initialize the matrix:
            //
            int[] xRows = xRowsAll[irregularCorner];

            int numSourcePoints = _sourcePatch.GetNumSourcePoints();

            buildIrregularCornerMatrix(valence, numSourcePoints, xRows, matrix);

            Vtr.Array<REAL> wX0 = matrix.SetRowElements(xRows[0]);
            Vtr.Array<REAL> wX1 = matrix.SetRowElements(xRows[1]);
            Vtr.Array<REAL> wX2 = matrix.SetRowElements(xRows[2]);
            Vtr.Array<REAL> wX3 = matrix.SetRowElements(xRows[3]);
            Vtr.Array<REAL> wX4 = matrix.SetRowElements(xRows[4]);
            Vtr.Array<REAL> wX5 = matrix.SetRowElements(xRows[5]);
            Vtr.Array<REAL> wX6 = matrix.SetRowElements(xRows[6]);

            //
            //  Compute indices of the ring points relative to the patch face:
            //
            int faceInRingPlus1 = (faceInRing + 1) % valence;
            int faceInRingPlus2 = (faceInRing + 2) % valence;
            int faceInRingMinus1 = (faceInRing + valence - 1) % valence;

            int p0inRing = 0;
            int p1inRing = 1 + 2 * faceInRing;
            int p2inRing = 1 + 2 * faceInRing + 1;
            int p3inRing = 1 + 2 * faceInRingPlus1;
            int p15inRing = 1 + 2 * faceInRingPlus1 + 1;
            int p4inRing = 1 + 2 * faceInRingPlus2;
            int p6inRing = 1 + 2 * faceInRingMinus1;
            int p7inRing = 1 + 2 * faceInRingMinus1 + 1;
            int p8inRing = ringSizePlusCorner;
            int p14inRing = ringSizePlusCorner;

            //
            //  Initialize the weights for the X points (the combination of the
            //  Gregory-like terms that are then subtracted from the limit values):
            //
            wX1[p0inRing] = wX2[p0inRing] = R(16.0f);
            wX1[p1inRing] = wX2[p3inRing] = R(8.0f);
            wX1[p2inRing] = wX2[p2inRing] = R(2.0f);
            wX1[p3inRing] = wX2[p1inRing] = R(4.0f);
            wX1[p6inRing] = wX2[p4inRing] = R(1.0f);
            wX1[p7inRing] = wX2[p15inRing] = R(2.0f);

            wX3[p0inRing] = wX4[p0inRing] = R(8.0f);
            wX3[p1inRing] = wX4[p3inRing] = R(4.0f);
            wX3[p2inRing] = wX4[p2inRing] = R(1.0f);
            wX3[p3inRing] = wX4[p1inRing] = R(2.0f);
            wX3[p6inRing] = wX4[p4inRing] = R(2.0f);
            wX3[p7inRing] = wX4[p15inRing] = R(4.0f);

            wX5[p6inRing] = wX6[p4inRing] = R(-1.0f);
            wX5[p8inRing] = wX6[p14inRing] = R(1.0f);

            wX0[p0inRing] = R(16.0f);
            wX0[p1inRing] = R(4.0f);
            wX0[p2inRing] = R(1.0f);
            wX0[p3inRing] = R(4.0f);

            REAL oneThird = R(1.0 / 3.0);
            for (int i = 0; i < ringSizePlusCorner; ++i)
            {
                wX1[i] = (R(36.0f) * wEp[i] - wX1[i]) * oneThird;
                wX2[i] = (R(36.0f) * wEm[i] - wX2[i]) * oneThird;

                wX3[i] = -wEp[i] * R(6.0f) + wX3[i] * oneThird;
                wX4[i] = -wEm[i] * R(6.0f) + wX4[i] * oneThird;

                wX5[i] += wX1[i];
                wX6[i] += wX2[i];

                wX0[i] = wP[i] * R(36.0f) - wX0[i] - (wX2[i] + wX1[i]) * R(4.0f) - (wX3[i] + wX4[i]);
            }

            //
            //  Assign the indices of the source points for each row.  Gather the
            //  ring for the irregular corner (with the corner prepended):
            //
            int[] ringPoints = new int[ringSizePlusCorner];
            ringPoints[0] = irregularCorner;
            _sourcePatch.GetCornerRingPoints(irregularCorner, ringPoints, 1);

            //  Identify the indices of the remaining points of the patch (those
            //  beyond the corner and its ring) in the remaining source points:
            int[] pPoints = new int[16];
            int pNext = ringPoints[p7inRing] + 1;
            for (int i = 8; i < 16; ++i, ++pNext)
            {
                pPoints[i] = (pNext < numSourcePoints) ? pNext
                           : (pNext - numSourcePoints + 4);
            }

            Vtr.Array<int>[] xIndices = new Vtr.Array<int>[7];
            for (int i = 0; i < 7; ++i)
            {
                xIndices[i] = matrix.SetRowColumns(xRows[i]);
                for (int j = 0; j < ringSizePlusCorner; ++j)
                {
                    xIndices[i][j] = ringPoints[j];
                }
            }
            xIndices[5][ringSizePlusCorner] = pPoints[8];
            xIndices[6][ringSizePlusCorner] = pPoints[14];

            //  Assign the indices for the single-entry rows of the 4 corners... :
            matrix.SetRowColumns(5)[0] = 0;
            matrix.SetRowColumns(6)[0] = 1;
            matrix.SetRowColumns(9)[0] = 3;
            matrix.SetRowColumns(10)[0] = 2;

            //  ... and for the exterior points:
            int[] extPointRows = extPointRowsAll[irregularCorner];

            matrix.SetRowColumns(extPointRows[0])[0] = pPoints[9];
            matrix.SetRowColumns(extPointRows[1])[0] = pPoints[10];
            matrix.SetRowColumns(extPointRows[2])[0] = pPoints[11];
            matrix.SetRowColumns(extPointRows[3])[0] = pPoints[12];
            matrix.SetRowColumns(extPointRows[4])[0] = pPoints[13];
        }

        private SourcePatch _sourcePatch;
        private GregoryConverter<REAL> _gregoryConverter;
    }

    //
    //  LinearConverter
    //
    //  Conversion of the source patch to the 4 corner points of a linear patch
    //  (the limit positions of the corners).
    //
    public class LinearConverter<REAL> where REAL : unmanaged, IFloatingPointIeee754<REAL>
    {
        private static REAL R(double value)
        {
            return REAL.CreateChecked(value);
        }

        public LinearConverter()
        {
            _sourcePatch = null;
        }

        public LinearConverter(SourcePatch sourcePatch)
        {
            Initialize(sourcePatch);
        }

        public LinearConverter(SourcePatch sourcePatch, SparseMatrix<REAL> matrix)
        {
            Initialize(sourcePatch);
            Convert(matrix);
        }

        public void Initialize(SourcePatch sourcePatch)
        {
            _sourcePatch = sourcePatch;
        }

        public void Convert(SparseMatrix<REAL> matrix)
        {
            int[] indexBuffer = new int[1 + _sourcePatch.GetMaxRingSize()];
            REAL[] weightBuffer = new REAL[1 + _sourcePatch.GetMaxRingSize()];

            //  Resize the matrix for the maximum number of entries in each row:
            int numElements = 4 * (1 + _sourcePatch.GetMaxRingSize());

            matrix.Resize(4, _sourcePatch.GetNumSourcePoints(), numElements);

            bool hasVal2InteriorCorner = false;

            for (int cIndex = 0; cIndex < 4; ++cIndex)
            {
                if (_sourcePatch._corners[cIndex]._sharp)
                {
                    matrix.SetRowSize(cIndex, 1);
                    matrix.SetRowColumns(cIndex)[0] = cIndex;
                    matrix.SetRowElements(cIndex)[0] = R(1.0f);
                    continue;
                }

                SourcePatch.Corner sourceCorner = _sourcePatch._corners[cIndex];

                int ringSize = _sourcePatch.GetCornerRingSize(cIndex);
                if (sourceCorner._boundary)
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
                _sourcePatch.GetCornerRingPoints(cIndex, indexBuffer, 1);

                if (sourceCorner._boundary)
                {
                    CatmarkLimits<REAL>.ComputeBoundaryPointWeights(
                        1 + sourceCorner._numFaces, sourceCorner._patchFace,
                        weightBuffer, Span<REAL>.Empty, Span<REAL>.Empty);

                    rowIndices[0] = indexBuffer[0];
                    rowIndices[1] = indexBuffer[1];
                    rowIndices[2] = indexBuffer[ringSize];

                    rowWeights[0] = weightBuffer[0];
                    rowWeights[1] = weightBuffer[1];
                    rowWeights[2] = weightBuffer[ringSize];
                }
                else
                {
                    CatmarkLimits<REAL>.ComputeInteriorPointWeights(
                        sourceCorner._numFaces, sourceCorner._patchFace,
                        weightBuffer, Span<REAL>.Empty, Span<REAL>.Empty);

                    for (int i = 0; i < 1 + ringSize; ++i)
                    {
                        rowIndices[i] = indexBuffer[i];
                    }
                    for (int i = 0; i < 1 + ringSize; ++i)
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

        private SourcePatch _sourcePatch;
    }

    //
    //  CatmarkPatchBuilder
    //
    //  Required virtual methods are included, along with any customizations
    //  local to their implementation.
    //
    public class CatmarkPatchBuilder : PatchBuilder
    {
        private static readonly PatchDescriptor.Type[] patchTypeFromBasisArray = new PatchDescriptor.Type[]
        {
            PatchDescriptor.Type.NON_PATCH,      // undefined
            PatchDescriptor.Type.REGULAR,        // regular
            PatchDescriptor.Type.GREGORY_BASIS,  // Gregory
            PatchDescriptor.Type.QUADS,          // linear
            PatchDescriptor.Type.NON_PATCH       // Bezier -- for future use
        };

        public CatmarkPatchBuilder(TopologyRefiner refiner, Options options) :
            base(refiner, options)
        {
            _regPatchType = patchTypeFromBasisArray[(int)_options.regBasisType];
            _irregPatchType = (_options.irregBasisType == BasisType.BASIS_UNSPECIFIED)
                            ? _regPatchType
                            : patchTypeFromBasisArray[(int)_options.irregBasisType];

            _nativePatchType = patchTypeFromBasisArray[(int)BasisType.BASIS_REGULAR];
            _linearPatchType = patchTypeFromBasisArray[(int)BasisType.BASIS_LINEAR];
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
            System.Diagnostics.Debug.Assert(_schemeType == SchemeType.SCHEME_CATMARK);

            if (patchType == PatchDescriptor.Type.GREGORY_BASIS)
            {
                new GregoryConverter<REAL>(sourcePatch, matrix);
            }
            else if (patchType == PatchDescriptor.Type.REGULAR)
            {
                new BSplineConverter<REAL>(sourcePatch, matrix);
            }
            else if (patchType == PatchDescriptor.Type.QUADS)
            {
                new LinearConverter<REAL>(sourcePatch, matrix);
            }
            else
            {
                System.Diagnostics.Debug.Assert(false, "Unknown or unsupported patch type");
            }
            return matrix.GetNumRows();
        }
    }
}
