//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/catmarkScheme.h  (specializations for Scheme<SCHEME_CATMARK>)
using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSubdiv.Sdc
{
    public static class CatmarkScheme
    {
        //
        //  Catmark traits:
        //
        public static Split GetTopologicalSplitType() { return Split.SPLIT_TO_QUADS; }
        public static int GetRegularFaceSize() { return 4; }
        public static int GetRegularVertexValence() { return 4; }
        public static int GetLocalNeighborhoodSize() { return 1; }

        //
        //  Masks for edge-vertices:  the hard Crease mask does not need to be specialized
        //  (simply the midpoint), so all that is left is the Smooth case.
        //
        //  The Smooth mask is complicated by the need to support the "triangle subdivision"
        //  option, which applies different weighting in the presence of triangles.
        //
        public static void assignSmoothMaskForEdge<EDGE, WEIGHT>(Options options, EDGE edge, IMask<WEIGHT> mask)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int faceCount = edge.GetNumFaces();

            mask.SetNumVertexWeights(2);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(faceCount);
            mask.SetFaceWeightsForFaceCenters(true);

            //
            //  Determine if we need to inspect incident faces and apply alternate weighting for
            //  triangles -- and if so, determine which of the two are triangles.
            //
            bool face0IsTri = false;
            bool face1IsTri = false;
            bool useTriangleOption = (options.GetTriangleSubdivision() == Options.TriangleSubdivision.TRI_SUB_SMOOTH);
            if (useTriangleOption)
            {
                if (faceCount == 2)
                {
                    //
                    //  Ideally we want to avoid this inspection when we have already subdivided at
                    //  least once -- need something in the Edge interface to help avoid this, e.g.
                    //  an IsRegular() query, the subdivision level...
                    //
                    int[] vertsPerFace = new int[2];
                    edge.GetNumVerticesPerFace(vertsPerFace);

                    face0IsTri = (vertsPerFace[0] == 3);
                    face1IsTri = (vertsPerFace[1] == 3);
                    useTriangleOption = face0IsTri || face1IsTri;
                }
                else
                {
                    useTriangleOption = false;
                }
            }

            if (!useTriangleOption)
            {
                mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.25f);
                mask.VertexWeight(1) = Scheme.Cf<WEIGHT>(0.25f);

                if (faceCount == 2)
                {
                    mask.FaceWeight(0) = Scheme.Cf<WEIGHT>(0.25f);
                    mask.FaceWeight(1) = Scheme.Cf<WEIGHT>(0.25f);
                }
                else
                {
                    WEIGHT fWeight = Scheme.Cf<WEIGHT>(0.5f) / WEIGHT.CreateChecked(faceCount);
                    for (int i = 0; i < faceCount; ++i)
                    {
                        mask.FaceWeight(i) = fWeight;
                    }
                }
            }
            else
            {
                //
                //  This mimics the implementation in Hbr in terms of order of operations.
                //
                WEIGHT CATMARK_SMOOTH_TRI_EDGE_WEIGHT = Scheme.Cd<WEIGHT>(0.470);

                WEIGHT f0Weight = face0IsTri ? CATMARK_SMOOTH_TRI_EDGE_WEIGHT : Scheme.Cf<WEIGHT>(0.25f);
                WEIGHT f1Weight = face1IsTri ? CATMARK_SMOOTH_TRI_EDGE_WEIGHT : Scheme.Cf<WEIGHT>(0.25f);

                WEIGHT fWeight = Scheme.Cf<WEIGHT>(0.5f) * (f0Weight + f1Weight);
                WEIGHT vWeight = Scheme.Cf<WEIGHT>(0.5f) * (Scheme.Cf<WEIGHT>(1.0f) - Scheme.Cf<WEIGHT>(2.0f) * fWeight);

                mask.VertexWeight(0) = vWeight;
                mask.VertexWeight(1) = vWeight;

                mask.FaceWeight(0) = fWeight;
                mask.FaceWeight(1) = fWeight;
            }
        }

        //
        //  Masks for vertex-vertices:  the hard Corner mask does not need to be specialized
        //  (simply the vertex itself), leaving the Crease and Smooth cases (Dart is smooth):
        //
        public static void assignCreaseMaskForVertex<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> mask,
                                                                     int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int valence = vertex.GetNumEdges();

            mask.SetNumVertexWeights(1);
            mask.SetNumEdgeWeights(valence);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            WEIGHT vWeight = Scheme.Cf<WEIGHT>(0.75f);
            WEIGHT eWeight = Scheme.Cf<WEIGHT>(0.125f);

            mask.VertexWeight(0) = vWeight;
            for (int i = 0; i < valence; ++i)
            {
                mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
            mask.EdgeWeight(creaseEnds[0]) = eWeight;
            mask.EdgeWeight(creaseEnds[1]) = eWeight;
        }

        public static void assignSmoothMaskForVertex<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            //
            //  A Smooth vertex must be manifold and interior -- manifold boundary vertices will be
            //  Creases and non-manifold vertices of any kind will be Corners or Creases.  If smooth
            //  rules for non-manifold vertices are ever defined, this will need adjusting:
            //
            System.Diagnostics.Debug.Assert(vertex.GetNumFaces() == vertex.GetNumEdges());

            int valence = vertex.GetNumFaces();

            mask.SetNumVertexWeights(1);
            mask.SetNumEdgeWeights(valence);
            mask.SetNumFaceWeights(valence);
            mask.SetFaceWeightsForFaceCenters(true);

            WEIGHT vWeight = WEIGHT.CreateChecked(valence - 2) / WEIGHT.CreateChecked(valence);
            WEIGHT fWeight = Scheme.Cf<WEIGHT>(1.0f) / WEIGHT.CreateChecked(valence * valence);
            WEIGHT eWeight = fWeight;

            mask.VertexWeight(0) = vWeight;
            for (int i = 0; i < valence; ++i)
            {
                mask.EdgeWeight(i) = eWeight;
                mask.FaceWeight(i) = fWeight;
            }
        }

        //
        //  Limit masks for position:
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

        public static void assignCreaseLimitMask<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> posMask,
                                                                 int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int valence = vertex.GetNumEdges();

            posMask.SetNumVertexWeights(1);
            posMask.SetNumEdgeWeights(valence);
            posMask.SetNumFaceWeights(0);
            posMask.SetFaceWeightsForFaceCenters(false);

            WEIGHT vWeight = Scheme.Cd<WEIGHT>(2.0 / 3.0);
            WEIGHT eWeight = Scheme.Cd<WEIGHT>(1.0 / 6.0);

            posMask.VertexWeight(0) = vWeight;
            for (int i = 0; i < valence; ++i)
            {
                posMask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
            posMask.EdgeWeight(creaseEnds[0]) = eWeight;
            posMask.EdgeWeight(creaseEnds[1]) = eWeight;
        }

        public static void assignSmoothLimitMask<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> posMask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int valence = vertex.GetNumFaces();
            if (valence == 2)
            {
                assignCornerLimitMask(options, vertex, posMask);
                return;
            }

            posMask.SetNumVertexWeights(1);
            posMask.SetNumEdgeWeights(valence);
            posMask.SetNumFaceWeights(valence);
            posMask.SetFaceWeightsForFaceCenters(false);

            //  Specialize for the regular case:
            if (valence == 4)
            {
                WEIGHT fWeight = Scheme.Cd<WEIGHT>(1.0 / 36.0);
                WEIGHT eWeight = Scheme.Cd<WEIGHT>(1.0 /  9.0);
                WEIGHT vWeight = Scheme.Cd<WEIGHT>(4.0 /  9.0);

                posMask.VertexWeight(0) = vWeight;

                posMask.EdgeWeight(0) = eWeight;
                posMask.EdgeWeight(1) = eWeight;
                posMask.EdgeWeight(2) = eWeight;
                posMask.EdgeWeight(3) = eWeight;

                posMask.FaceWeight(0) = fWeight;
                posMask.FaceWeight(1) = fWeight;
                posMask.FaceWeight(2) = fWeight;
                posMask.FaceWeight(3) = fWeight;
            }
            else
            {
                WEIGHT Valence = WEIGHT.CreateChecked(valence);

                WEIGHT fWeight = Scheme.Cf<WEIGHT>(1.0f) / (Valence * (Valence + Scheme.Cf<WEIGHT>(5.0f)));
                WEIGHT eWeight = Scheme.Cf<WEIGHT>(4.0f) * fWeight;
                WEIGHT vWeight = Scheme.Cf<WEIGHT>(1.0f) - Valence * (eWeight + fWeight);

                posMask.VertexWeight(0) = vWeight;
                for (int i = 0; i < valence; ++i)
                {
                    posMask.EdgeWeight(i) = eWeight;
                    posMask.FaceWeight(i) = fWeight;
                }
            }
        }

        //
        //  Limit masks for tangents -- these are stubs for now, or have a temporary
        //  implementation
        //
        public static void assignCornerLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int valence = vertex.GetNumEdges();

            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(valence);
            tan1Mask.SetNumFaceWeights(0);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(valence);
            tan2Mask.SetNumFaceWeights(0);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            //  Should be at least 2 edges -- be sure to clear weights for any more:
            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-1.0f);
            tan1Mask.EdgeWeight(0)   = Scheme.Cf<WEIGHT>( 1.0f);
            tan1Mask.EdgeWeight(1)   = Scheme.Cf<WEIGHT>( 0.0f);

            tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-1.0f);
            tan2Mask.EdgeWeight(0)   = Scheme.Cf<WEIGHT>( 0.0f);
            tan2Mask.EdgeWeight(1)   = Scheme.Cf<WEIGHT>( 1.0f);

            for (int i = 2; i < valence; ++i)
            {
                tan1Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
                tan2Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
        }

        public static void assignCreaseLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            //
            //  First, the tangent along the crease:
            //      The first crease edge is considered the "leading" edge of the span
            //  of surface for which we are evaluating tangents and the second edge the
            //  "trailing edge".  By convention, the tangent along the crease is oriented
            //  in the direction of the leading edge.
            //
            int numEdges = vertex.GetNumEdges();
            int numFaces = vertex.GetNumFaces();

            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(numEdges);
            tan1Mask.SetNumFaceWeights(numFaces);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);
            for (int i = 0; i < numEdges; ++i)
            {
                tan1Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
            for (int i = 0; i < numFaces; ++i)
            {
                tan1Mask.FaceWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }

            tan1Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>( 0.5f);
            tan1Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cf<WEIGHT>(-0.5f);

            //
            //  Second, the tangent across the interior faces:
            //      Note this is ambiguous for an interior vertex.  We currently return
            //  the tangent for the surface in the counter-clockwise span between the
            //  leading and trailing edges that form the crease.  Given the expected
            //  computation of a surface normal as Tan1 X Tan2, this tangent should be
            //  oriented "inward" from the crease/boundary -- across the surface rather
            //  than outward and away from it.
            //
            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(numEdges);
            tan2Mask.SetNumFaceWeights(numFaces);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            //  Prepend weights of 0 preceding the crease:
            for (int i = 0; i < creaseEnds[0]; ++i)
            {
                tan2Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
                tan2Mask.FaceWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }

            //  Assign weights to crease edge and interior points:
            int interiorEdgeCount = creaseEnds[1] - creaseEnds[0] - 1;
            if (interiorEdgeCount == 1)
            {
                //  The regular case -- uniform B-spline cross-tangent:

                tan2Mask.VertexWeight(0) = Scheme.Cd<WEIGHT>(-4.0 / 6.0);

                tan2Mask.EdgeWeight(creaseEnds[0])     = Scheme.Cd<WEIGHT>(-1.0 / 6.0);
                tan2Mask.EdgeWeight(creaseEnds[0] + 1) = Scheme.Cd<WEIGHT>( 4.0 / 6.0);
                tan2Mask.EdgeWeight(creaseEnds[1])     = Scheme.Cd<WEIGHT>(-1.0 / 6.0);

                tan2Mask.FaceWeight(creaseEnds[0])     = Scheme.Cd<WEIGHT>(1.0 / 6.0);
                tan2Mask.FaceWeight(creaseEnds[0] + 1) = Scheme.Cd<WEIGHT>(1.0 / 6.0);
            }
            else if (interiorEdgeCount > 1)
            {
                //  The irregular case -- formulae from Biermann et al:

                double k     = (double) (interiorEdgeCount + 1);
                double theta = Math.PI / k;

                double cosTheta = Math.Cos(theta);
                double sinTheta = Math.Sin(theta);

                //  Loop/Schaefer use a different divisor here (3*k + cos(theta)):
                double commonDenom = 1.0f / (k * (3.0f + cosTheta));
                double R = (cosTheta + 1.0f) / sinTheta;

                double vertexWeight = 4.0f * R * (cosTheta - 1.0f);
                double creaseWeight = -R * (1.0f + 2.0f * cosTheta);

                tan2Mask.VertexWeight(0) = Scheme.Cd<WEIGHT>(vertexWeight * commonDenom);

                tan2Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cd<WEIGHT>(creaseWeight * commonDenom);
                tan2Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cd<WEIGHT>(creaseWeight * commonDenom);

                tan2Mask.FaceWeight(creaseEnds[0]) = Scheme.Cd<WEIGHT>(sinTheta * commonDenom);

                double sinThetaI      = 0.0f;
                double sinThetaIplus1 = sinTheta;
                for (int i = 1; i < k; ++i)
                {
                    sinThetaI      = sinThetaIplus1;
                    sinThetaIplus1 = Math.Sin((i + 1) * theta);

                    tan2Mask.EdgeWeight(creaseEnds[0] + i) = Scheme.Cd<WEIGHT>((4.0f * sinThetaI) * commonDenom);
                    tan2Mask.FaceWeight(creaseEnds[0] + i) = Scheme.Cd<WEIGHT>((sinThetaI + sinThetaIplus1) * commonDenom);
                }
            }
            else
            {
                //  Special case for a single face -- simple average of boundary edges:

                tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-6.0f);

                tan2Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>(3.0f);
                tan2Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cf<WEIGHT>(3.0f);

                tan2Mask.FaceWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>(0.0f);
            }

            //  Append weights of 0 following the crease:
            for (int i = creaseEnds[1]; i < numFaces; ++i)
            {
                tan2Mask.FaceWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
            for (int i = creaseEnds[1] + 1; i < numEdges; ++i)
            {
                tan2Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
        }

        public static void assignSmoothLimitTangentMasks<VERTEX, WEIGHT>(Options options, VERTEX vertex,
                IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int valence = vertex.GetNumFaces();
            if (valence == 2)
            {
                assignCornerLimitTangentMasks(options, vertex, tan1Mask, tan2Mask);
                return;
            }

            //  Compute tan1 initially -- tan2 is simply a rotation:
            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(valence);
            tan1Mask.SetNumFaceWeights(valence);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);

            if (valence == 4)
            {
                tan1Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>( 4.0f);
                tan1Mask.EdgeWeight(1) = Scheme.Cf<WEIGHT>( 0.0f);
                tan1Mask.EdgeWeight(2) = Scheme.Cf<WEIGHT>(-4.0f);
                tan1Mask.EdgeWeight(3) = Scheme.Cf<WEIGHT>( 0.0f);

                tan1Mask.FaceWeight(0) = Scheme.Cf<WEIGHT>( 1.0f);
                tan1Mask.FaceWeight(1) = Scheme.Cf<WEIGHT>(-1.0f);
                tan1Mask.FaceWeight(2) = Scheme.Cf<WEIGHT>(-1.0f);
                tan1Mask.FaceWeight(3) = Scheme.Cf<WEIGHT>( 1.0f);
            }
            else
            {
                double theta = 2.0f * Math.PI / (double)valence;

                double cosTheta     = Math.Cos(theta);
                double cosHalfTheta = Math.Cos(theta * 0.5f);

                double lambda = (5.0 / 16.0) + (1.0 / 16.0) *
                        (cosTheta + cosHalfTheta * Math.Sqrt(2.0f * (9.0f + cosTheta)));

                double edgeWeightScale = 4.0f;
                double faceWeightScale = 1.0f / (4.0f * lambda - 1.0f);

                for (int i = 0; i < valence; ++i)
                {
                    double cosThetaI      = Math.Cos(  i  * theta);
                    double cosThetaIplus1 = Math.Cos((i + 1) * theta);

                    tan1Mask.EdgeWeight(i) = Scheme.Cd<WEIGHT>(edgeWeightScale * cosThetaI);
                    tan1Mask.FaceWeight(i) = Scheme.Cd<WEIGHT>(faceWeightScale * (cosThetaI + cosThetaIplus1));
                }
            }

            //  Now rotate/copy tan1 weights to tan2:
            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(valence);
            tan2Mask.SetNumFaceWeights(valence);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);
            if (valence == 4)
            {
                tan2Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>( 0.0f);
                tan2Mask.EdgeWeight(1) = Scheme.Cf<WEIGHT>( 4.0f);
                tan2Mask.EdgeWeight(2) = Scheme.Cf<WEIGHT>( 0.0f);
                tan2Mask.EdgeWeight(3) = Scheme.Cf<WEIGHT>(-4.0f);

                tan2Mask.FaceWeight(0) = Scheme.Cf<WEIGHT>( 1.0f);
                tan2Mask.FaceWeight(1) = Scheme.Cf<WEIGHT>( 1.0f);
                tan2Mask.FaceWeight(2) = Scheme.Cf<WEIGHT>(-1.0f);
                tan2Mask.FaceWeight(3) = Scheme.Cf<WEIGHT>(-1.0f);
            }
            else
            {
                tan2Mask.EdgeWeight(0) = tan1Mask.EdgeWeight(valence - 1);
                tan2Mask.FaceWeight(0) = tan1Mask.FaceWeight(valence - 1);
                for (int i = 1; i < valence; ++i)
                {
                    tan2Mask.EdgeWeight(i) = tan1Mask.EdgeWeight(i - 1);
                    tan2Mask.FaceWeight(i) = tan1Mask.FaceWeight(i - 1);
                }
            }
        }
    }
}
