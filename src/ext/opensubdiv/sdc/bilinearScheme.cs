//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/bilinearScheme.h  (specializations for Scheme<SCHEME_BILINEAR>)
using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSubdiv.Sdc
{
    public static class BilinearScheme
    {
        //
        //  Bilinear traits:
        //
        public static Split GetTopologicalSplitType() { return Split.SPLIT_TO_QUADS; }
        public static int GetRegularFaceSize() { return 4; }
        public static int GetRegularVertexValence() { return 4; }
        public static int GetLocalNeighborhoodSize() { return 0; }

        //
        //  Limit masks for position -- the limit position of all vertices is the refined vertex.
        //
        public static void assignCornerLimitMask<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> posMask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            posMask.SetNumVertexWeights(1);
            posMask.SetNumEdgeWeights(0);
            posMask.SetNumFaceWeights(0);
            posMask.SetFaceWeightsForFaceCenters(false);

            posMask.VertexWeight(0) = Scheme.Cf<WEIGHT>(1.0f);
        }

        public static void assignCreaseLimitMask<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> posMask, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            assignCornerLimitMask(options, vertex, posMask);
        }

        public static void assignSmoothLimitMask<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> posMask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            assignCornerLimitMask(options, vertex, posMask);
        }

        //
        //  Limit masks for tangents -- these are ambiguous around all vertices.  Provide
        //  the tangents based on the incident edges of the first face.
        //
        public static void assignCornerLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(2);
            tan1Mask.SetNumFaceWeights(0);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(2);
            tan2Mask.SetNumFaceWeights(0);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-1.0f);
            tan1Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>(1.0f);
            tan1Mask.EdgeWeight(1) = Scheme.Cf<WEIGHT>(0.0f);

            tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-1.0f);
            tan2Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>(0.0f);
            tan2Mask.EdgeWeight(1) = Scheme.Cf<WEIGHT>(1.0f);
        }

        public static void assignCreaseLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            assignCornerLimitTangentMasks(options, vertex, tan1Mask, tan2Mask);
        }

        public static void assignSmoothLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            assignCornerLimitTangentMasks(options, vertex, tan1Mask, tan2Mask);
        }
    }
}
