//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/loopScheme.h  (specializations for Scheme<SCHEME_LOOP>)
using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSubdiv.Sdc
{
    public static class LoopScheme
    {
        //
        //  Loop traits:
        //
        public static Split GetTopologicalSplitType() { return Split.SPLIT_TO_TRIS; }
        public static int GetRegularFaceSize() { return 3; }
        public static int GetRegularVertexValence() { return 6; }
        public static int GetLocalNeighborhoodSize() { return 1; }

        //
        //  Methods to assign the two types of masks for an edge-vertex --
        //  Crease and Smooth.
        //
        //  The Crease case does not really need to be specialized, though it may be
        //  preferable to define all explicitly here.
        //
        public static void assignCreaseMaskForEdge<EDGE, WEIGHT>(Options options, EDGE edge, IMask<WEIGHT> mask)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            mask.SetNumVertexWeights(2);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.5f);
            mask.VertexWeight(1) = Scheme.Cf<WEIGHT>(0.5f);
        }

        public static void assignSmoothMaskForEdge<EDGE, WEIGHT>(Options options, EDGE edge, IMask<WEIGHT> mask)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int faceCount = edge.GetNumFaces();

            mask.SetNumVertexWeights(2);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(faceCount);
            mask.SetFaceWeightsForFaceCenters(false);

            //
            //  This is where we run into the issue of "face weights" -- we want to weight the
            //  face-centers for Catmark, but face-centers are not generated for Loop.  So do
            //  we make assumptions on how the mask is used, assign some property to the mask
            //  to indicate how they were assigned, or take input from the mask itself?
            //
            //  Regardless, we have two choices:
            //      - face-weights are for the vertices opposite the edge (as in Hbr):
            //          vertex weights = 0.375f;
            //          face weights   = 0.125f;
            //
            //      - face-weights are for the face centers:
            //          vertex weights = 0.125f;
            //          face weights   = 0.375f;
            //
            //  Coincidentally the coefficients are the same but reversed.
            //
            WEIGHT vWeight = mask.AreFaceWeightsForFaceCenters() ? Scheme.Cf<WEIGHT>(0.125f) : Scheme.Cf<WEIGHT>(0.375f);
            WEIGHT fWeight = mask.AreFaceWeightsForFaceCenters() ? Scheme.Cf<WEIGHT>(0.375f) : Scheme.Cf<WEIGHT>(0.125f);

            mask.VertexWeight(0) = vWeight;
            mask.VertexWeight(1) = vWeight;

            if (faceCount == 2)
            {
                mask.FaceWeight(0) = fWeight;
                mask.FaceWeight(1) = fWeight;
            }
            else
            {
                //  The non-manifold case is not clearly defined -- we adjust the above
                //  face-weight to preserve the ratio of edge-center and face-centers:
                fWeight *= Scheme.Cf<WEIGHT>(2.0f) / WEIGHT.CreateChecked(faceCount);
                for (int i = 0; i < faceCount; ++i)
                {
                    mask.FaceWeight(i) = fWeight;
                }
            }
        }

        //
        //  Methods to assign the three types of masks for a vertex-vertex --
        //  Corner, Crease and Smooth (Dart is the same as Smooth).
        //
        //  Corner and Crease do not really need to be specialized, though it may be
        //  preferable to define all explicitly here.
        //
        public static void assignCornerMaskForVertex<VERTEX, WEIGHT>(Options options, VERTEX vertex, IMask<WEIGHT> mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            mask.SetNumVertexWeights(1);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(1.0f);
        }

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
            int valence = vertex.GetNumFaces();

            mask.SetNumVertexWeights(1);
            mask.SetNumEdgeWeights(valence);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            //  Specialize for the regular case:  1/16 per edge-vert, 5/8 for the vert itself:
            WEIGHT eWeight = Scheme.Cf<WEIGHT>(0.0625f);
            WEIGHT vWeight = Scheme.Cf<WEIGHT>(0.625f);

            if (valence != 6)
            {
                //  From HbrLoopSubdivision<T>::Subdivide(mesh, vertex):
                //     - could use some lookup tables here for common irregular valence (5, 7, 8)
                //       or all of these cosine calls will be adding up...

                double dValence   = (double) valence;
                double invValence = 1.0f / dValence;
                double cosTheta   = Math.Cos(Math.PI * 2.0f * invValence);

                double beta = 0.25f * cosTheta + 0.375f;

                eWeight = Scheme.Cd<WEIGHT>((0.625f - (beta * beta)) * invValence);
                vWeight = Scheme.Cd<WEIGHT>(1.0f - (Scheme.ToDouble(eWeight) * dValence));
            }

            mask.VertexWeight(0) = vWeight;
            for (int i = 0; i < valence; ++i)
            {
                mask.EdgeWeight(i) = eWeight;
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

            //
            //  The refinement mask for a crease vertex is (1/8, 3/4, 1/8) and for a crease
            //  edge is (1/2, 1/2) -- producing a uniform B-spline curve along the crease
            //  (boundary) whether the vertex or its crease is regular or not.  The limit
            //  mask is therefore (1/6, 2/3, 1/6) for ALL cases.
            //
            //  An alternative limit mask (1/5, 3/5, 1/5) is often published for use either
            //  for irregular crease vertices or for all crease/boundary vertices, but this
            //  is based on an alternate refinement mask for the edge -- (3/8, 5/8) versus
            //  the usual (1/2, 1/2) -- and will not produce the B-spline curve desired.
            //
            WEIGHT vWeight = Scheme.Cd<WEIGHT>(4.0 / 6.0);
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

            posMask.SetNumVertexWeights(1);
            posMask.SetNumEdgeWeights(valence);
            posMask.SetNumFaceWeights(0);
            posMask.SetFaceWeightsForFaceCenters(false);

            //  Specialize for the regular case:  1/12 per edge-vert, 1/2 for the vert itself:
            if (valence == 6)
            {
                WEIGHT eWeight = Scheme.Cd<WEIGHT>(1.0 / 12.0);
                WEIGHT vWeight = Scheme.Cf<WEIGHT>(0.5f);

                posMask.VertexWeight(0) = vWeight;

                posMask.EdgeWeight(0) = eWeight;
                posMask.EdgeWeight(1) = eWeight;
                posMask.EdgeWeight(2) = eWeight;
                posMask.EdgeWeight(3) = eWeight;
                posMask.EdgeWeight(4) = eWeight;
                posMask.EdgeWeight(5) = eWeight;
            }
            else
            {
                double dValence   = (double) valence;
                double invValence = 1.0f / dValence;
                double cosTheta   = Math.Cos(Math.PI * 2.0f * invValence);

                double beta  = 0.25f * cosTheta + 0.375f;
                double gamma = (0.625f - (beta * beta)) * invValence;

                WEIGHT eWeight = Scheme.Cd<WEIGHT>(1.0f / (dValence + 3.0f / (8.0f * gamma)));
                WEIGHT vWeight = Scheme.Cd<WEIGHT>(1.0f - (Scheme.ToDouble(eWeight) * dValence));

                posMask.VertexWeight(0) = vWeight;
                for (int i = 0; i < valence; ++i)
                {
                    posMask.EdgeWeight(i) = eWeight;
                }
            }
        }

        //
        //  Limit masks for tangents:
        //
        //  See the original loopScheme.h for the long note on tangent magnitudes (scale factors
        //  of 3/2 for crease tangents, 3.0 for corner and low valence cross-tangents).
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

            //  See note above regarding scale factor of 3.0:
            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-3.0f);
            tan1Mask.EdgeWeight(0)   = Scheme.Cf<WEIGHT>( 3.0f);
            tan1Mask.EdgeWeight(1)   = Scheme.Cf<WEIGHT>( 0.0f);

            tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-3.0f);
            tan2Mask.EdgeWeight(0)   = Scheme.Cf<WEIGHT>( 0.0f);
            tan2Mask.EdgeWeight(1)   = Scheme.Cf<WEIGHT>( 3.0f);

            //  Should be at least 2 edges -- be sure to clear weights for any more:
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
            int valence = vertex.GetNumEdges();

            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(valence);
            tan1Mask.SetNumFaceWeights(0);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);
            for (int i = 0; i < valence; ++i)
            {
                tan1Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }

            //  See the note above regarding scale factor of 1.5:
            tan1Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>( 1.5f);
            tan1Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cf<WEIGHT>(-1.5f);

            //
            //  Second, the tangent across the interior faces:
            //      Note this is ambiguous for an interior vertex.  We currently return
            //  the tangent for the surface in the counter-clockwise span between the
            //  leading and trailing edges that form the crease.  Given the expected
            //  computation of a surface normal as Tan1 X Tan2, this tangent should be
            //  oriented "inward" from the crease/boundary -- across the surface rather
            //  than outward and away from it.
            //
            //  (See the original for the discussion of the orientation of the general formula.)
            //
            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(valence);
            tan2Mask.SetNumFaceWeights(0);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            for (int i = 0; i < creaseEnds[0]; ++i)
            {
                tan2Mask.EdgeWeight(i) = Scheme.Cf<WEIGHT>(0.0f);
            }
            int interiorEdgeCount = creaseEnds[1] - creaseEnds[0] - 1;
            if (interiorEdgeCount == 2)
            {
                //  See note above regarding scale factor of (sin(60 degs) == sqrt(3)/2:

                WEIGHT Root3    = Scheme.Cd<WEIGHT>(1.73205080756887729352);
                WEIGHT Root3by2 = Scheme.Cd<WEIGHT>(Scheme.ToDouble(Root3) * 0.5);

                tan2Mask.VertexWeight(0) = -Root3;

                tan2Mask.EdgeWeight(creaseEnds[0]) = -Root3by2;
                tan2Mask.EdgeWeight(creaseEnds[1]) = -Root3by2;

                tan2Mask.EdgeWeight(creaseEnds[0] + 1) = Root3;
                tan2Mask.EdgeWeight(creaseEnds[0] + 2) = Root3;
            }
            else if (interiorEdgeCount > 2)
            {
                //  See notes above regarding scale factor of -3.0 (-1 for orientation,
                //  2.0 for considering the region as a half-disk, and 1.5 in keeping
                //  with the crease tangent):

                double theta = Math.PI / (interiorEdgeCount + 1);

                tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);

                WEIGHT cWeight = Scheme.Cd<WEIGHT>(-3.0f * Math.Sin(theta));
                tan2Mask.EdgeWeight(creaseEnds[0]) = cWeight;
                tan2Mask.EdgeWeight(creaseEnds[1]) = cWeight;

                double eCoeff  = -3.0f * 2.0f * (Math.Cos(theta) - 1.0f);
                for (int i = 1; i <= interiorEdgeCount; ++i)
                {
                    tan2Mask.EdgeWeight(creaseEnds[0] + i) = Scheme.Cd<WEIGHT>(eCoeff * Math.Sin(i * theta));
                }
            }
            else if (interiorEdgeCount == 1)
            {
                //  See notes above regarding scale factor of 3.0:

                tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-3.0f);

                tan2Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>(0.0f);
                tan2Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cf<WEIGHT>(0.0f);

                tan2Mask.EdgeWeight(creaseEnds[0] + 1) = Scheme.Cf<WEIGHT>(3.0f);
            }
            else
            {
                //  See notes above regarding scale factor of 3.0:

                tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(-6.0f);

                tan2Mask.EdgeWeight(creaseEnds[0]) = Scheme.Cf<WEIGHT>(3.0f);
                tan2Mask.EdgeWeight(creaseEnds[1]) = Scheme.Cf<WEIGHT>(3.0f);
            }
            for (int i = creaseEnds[1] + 1; i < valence; ++i)
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

            tan1Mask.SetNumVertexWeights(1);
            tan1Mask.SetNumEdgeWeights(valence);
            tan1Mask.SetNumFaceWeights(0);
            tan1Mask.SetFaceWeightsForFaceCenters(false);

            tan2Mask.SetNumVertexWeights(1);
            tan2Mask.SetNumEdgeWeights(valence);
            tan2Mask.SetNumFaceWeights(0);
            tan2Mask.SetFaceWeightsForFaceCenters(false);

            tan1Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);
            tan2Mask.VertexWeight(0) = Scheme.Cf<WEIGHT>(0.0f);

            if (valence == 6)
            {
                WEIGHT Root3by2 = Scheme.Cd<WEIGHT>(0.5 * 1.73205080756887729352);

                tan1Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>( 1.0f);
                tan1Mask.EdgeWeight(1) = Scheme.Cf<WEIGHT>( 0.5f);
                tan1Mask.EdgeWeight(2) = Scheme.Cf<WEIGHT>(-0.5f);
                tan1Mask.EdgeWeight(3) = Scheme.Cf<WEIGHT>(-1.0f);
                tan1Mask.EdgeWeight(4) = Scheme.Cf<WEIGHT>(-0.5f);
                tan1Mask.EdgeWeight(5) = Scheme.Cf<WEIGHT>( 0.5f);

                tan2Mask.EdgeWeight(0) = Scheme.Cf<WEIGHT>( 0.0f);
                tan2Mask.EdgeWeight(1) =  Root3by2;
                tan2Mask.EdgeWeight(2) =  Root3by2;
                tan2Mask.EdgeWeight(3) = Scheme.Cf<WEIGHT>( 0.0f);
                tan2Mask.EdgeWeight(4) = -Root3by2;
                tan2Mask.EdgeWeight(5) = -Root3by2;
            }
            else
            {
                double alpha = 2.0f * Math.PI / valence;
                for (int i = 0; i < valence; ++i)
                {
                    double alphaI = alpha * i;
                    tan1Mask.EdgeWeight(i) = Scheme.Cd<WEIGHT>(Math.Cos(alphaI));
                    tan2Mask.EdgeWeight(i) = Scheme.Cd<WEIGHT>(Math.Sin(alphaI));
                }
            }
        }
    }
}
