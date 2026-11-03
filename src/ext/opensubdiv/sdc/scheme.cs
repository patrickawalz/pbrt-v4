//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/scheme.h
//
//  Porting notes:
//   - C++ Scheme<SCHEME_TYPE> is a class template specialized per scheme.  Here it is a single
//     class constructed with the SchemeType (new Scheme(SchemeType.SCHEME_CATMARK, options)); the
//     per-scheme specializations live in bilinearScheme.cs / catmarkScheme.cs / loopScheme.cs as
//     static classes, and the Scheme methods dispatch on the scheme type.
//   - The template parameters FACE, EDGE, VERTEX and MASK are expressed as the interfaces
//     IFaceNeighborhood, IEdgeNeighborhood, IVertexNeighborhood and IMask<WEIGHT> below.  MASK
//     must be a reference type (class) since it is mutated through the interface.  WEIGHT is
//     float or double and is inferred from the IMask<WEIGHT> implemented by the mask class.
//   - float const* arguments are float[] (index 0 based).
using System;
using System.Collections.Generic;
using System.Numerics;

namespace OpenSubdiv.Sdc
{
    //
    //  The interface required of <typename FACE>:
    //
    public interface IFaceNeighborhood
    {
        int GetNumVertices();
    }

    //
    //  The interface required of <typename EDGE>:
    //
    public interface IEdgeNeighborhood
    {
        int   GetNumFaces();
        float GetSharpness();
        void  GetChildSharpnesses(Crease crease, float[] s);
        void  GetNumVerticesPerFace(int[] vertsPerFace);
    }

    //
    //  The interface required of <typename VERTEX>:
    //
    public interface IVertexNeighborhood
    {
        int   GetNumEdges();
        int   GetNumFaces();
        float GetSharpness();
        float[] GetSharpnessPerEdge(float[] pSharpness);
        float GetChildSharpness(Crease crease);
        float[] GetChildSharpnessPerEdge(Crease crease, float[] cSharpness);
    }

    //
    //  The interface required of <typename MASK>:
    //
    public interface IMask<WEIGHT>
    {
        int GetNumVertexWeights();
        int GetNumEdgeWeights();
        int GetNumFaceWeights();

        void SetNumVertexWeights(int count);
        void SetNumEdgeWeights(int count);
        void SetNumFaceWeights(int count);

        ref WEIGHT VertexWeight(int index);
        ref WEIGHT EdgeWeight(int index);
        ref WEIGHT FaceWeight(int index);

        bool AreFaceWeightsForFaceCenters();
        void SetFaceWeightsForFaceCenters(bool on);
    }

    //
    //  Local "mask" class used within the vertex-vertex mask query to hold one of the two
    //  possible masks required and to combine the local mask with the mask the caller provides.
    //
    public class LocalMask<WEIGHT> : IMask<WEIGHT> where WEIGHT : unmanaged, INumber<WEIGHT>
    {
        public LocalMask(WEIGHT[] weights, int vOffset, int eOffset, int fOffset)
        {
            _weights = weights;
            _vOffset = vOffset;
            _eOffset = eOffset;
            _fOffset = fOffset;
        }

        //
        //  Methods required for general mask assignments and queries:
        //
        public int GetNumVertexWeights() { return _vCount; }
        public int GetNumEdgeWeights()   { return _eCount; }
        public int GetNumFaceWeights()   { return _fCount; }

        public void SetNumVertexWeights(int count) { _vCount = count; }
        public void SetNumEdgeWeights(int count)   { _eCount = count; }
        public void SetNumFaceWeights(int count)   { _fCount = count; }

        public ref WEIGHT VertexWeight(int index) { return ref _weights[_vOffset + index]; }
        public ref WEIGHT EdgeWeight(int index)   { return ref _weights[_eOffset + index]; }
        public ref WEIGHT FaceWeight(int index)   { return ref _weights[_fOffset + index]; }

        public bool AreFaceWeightsForFaceCenters() { return _fWeightsForCenters; }
        public void SetFaceWeightsForFaceCenters(bool on) { _fWeightsForCenters = on; }

        //
        //  Additional methods -- mainly the blending method for vertex-vertex masks:
        //
        public void CombineVertexVertexMasks(WEIGHT thisCoeff, WEIGHT dstCoeff, IMask<WEIGHT> dst)
        {
            //
            //  This implementation is convoluted by the potential sparsity of each mask.  Since
            //  it is specific to a vertex-vertex mask, we are guaranteed to have exactly one
            //  vertex-weight for both masks, but the edge- and face-weights are optional.  The
            //  child mask (the "source") should have a superset of the weights of the parent
            //  (the "destination") given its reduced sharpness, so we fortunately don't need to
            //  test all permutations.
            //
            dst.VertexWeight(0) = dstCoeff * dst.VertexWeight(0) + thisCoeff * this.VertexWeight(0);

            int edgeWeightCount = this.GetNumEdgeWeights();
            if (edgeWeightCount != 0)
            {
                if (dst.GetNumEdgeWeights() == 0)
                {
                    dst.SetNumEdgeWeights(edgeWeightCount);
                    for (int i = 0; i < edgeWeightCount; ++i)
                    {
                        dst.EdgeWeight(i) = thisCoeff * this.EdgeWeight(i);
                    }
                }
                else
                {
                    for (int i = 0; i < edgeWeightCount; ++i)
                    {
                        dst.EdgeWeight(i) = dstCoeff * dst.EdgeWeight(i) + thisCoeff * this.EdgeWeight(i);
                    }
                }
            }

            int faceWeightCount = this.GetNumFaceWeights();
            if (faceWeightCount != 0)
            {
                //
                //  If combining face weights, be sure their interpretation (i.e. face-centers
                //  or opposite vertices) is properly set in the destination mask:
                //
                if (dst.GetNumFaceWeights() == 0)
                {
                    dst.SetNumFaceWeights(faceWeightCount);
                    dst.SetFaceWeightsForFaceCenters(this.AreFaceWeightsForFaceCenters());

                    for (int i = 0; i < faceWeightCount; ++i)
                    {
                        dst.FaceWeight(i) = thisCoeff * this.FaceWeight(i);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.Assert(this.AreFaceWeightsForFaceCenters() == dst.AreFaceWeightsForFaceCenters());

                    for (int i = 0; i < faceWeightCount; ++i)
                    {
                        dst.FaceWeight(i) = dstCoeff * dst.FaceWeight(i) + thisCoeff * this.FaceWeight(i);
                    }
                }
            }
        }

        private WEIGHT[] _weights;
        private int _vOffset;
        private int _eOffset;
        private int _fOffset;
        private int _vCount;
        private int _eCount;
        private int _fCount;
        private bool _fWeightsForCenters;
    }

    public partial class Scheme
    {
        private SchemeType _schemeType;
        private Options _options;

        public Scheme(SchemeType schemeType)
        {
            _schemeType = schemeType;
            _options = new Options();
        }

        public Scheme(SchemeType schemeType, Options options)
        {
            _schemeType = schemeType;
            _options = options;
        }

        public SchemeType GetSchemeType()
        {
            return _schemeType;
        }

        public Options GetOptions()
        {
            return _options;
        }

        public void SetOptions(Options newOptions)
        {
            _options = newOptions;
        }

        //
        //  Static methods defining traits/properties of the scheme:
        //
        public static Split GetTopologicalSplitType(SchemeType schemeType)
        {
            switch (schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: return BilinearScheme.GetTopologicalSplitType();
                case SchemeType.SCHEME_CATMARK:  return CatmarkScheme.GetTopologicalSplitType();
                default:                         return LoopScheme.GetTopologicalSplitType();
            }
        }

        public static int GetRegularFaceSize(SchemeType schemeType)
        {
            switch (schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: return BilinearScheme.GetRegularFaceSize();
                case SchemeType.SCHEME_CATMARK:  return CatmarkScheme.GetRegularFaceSize();
                default:                         return LoopScheme.GetRegularFaceSize();
            }
        }

        public static int GetRegularVertexValence(SchemeType schemeType)
        {
            switch (schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: return BilinearScheme.GetRegularVertexValence();
                case SchemeType.SCHEME_CATMARK:  return CatmarkScheme.GetRegularVertexValence();
                default:                         return LoopScheme.GetRegularVertexValence();
            }
        }

        public static int GetLocalNeighborhoodSize(SchemeType schemeType)
        {
            switch (schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: return BilinearScheme.GetLocalNeighborhoodSize();
                case SchemeType.SCHEME_CATMARK:  return CatmarkScheme.GetLocalNeighborhoodSize();
                default:                         return LoopScheme.GetLocalNeighborhoodSize();
            }
        }

        //  Instance versions of the above for the scheme this object was constructed with:
        public Split GetTopologicalSplitType()
        {
            return GetTopologicalSplitType(_schemeType);
        }

        public int GetRegularFaceSize()
        {
            return GetRegularFaceSize(_schemeType);
        }

        public int GetRegularVertexValence()
        {
            return GetRegularVertexValence(_schemeType);
        }

        public int GetLocalNeighborhoodSize()
        {
            return GetLocalNeighborhoodSize(_schemeType);
        }

        //  Helpers for converting literal constants to the (generic) mask Weight type:
        public static WEIGHT Cf<WEIGHT>(float value) where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            return WEIGHT.CreateChecked(value);
        }

        public static WEIGHT Cd<WEIGHT>(double value) where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            return WEIGHT.CreateChecked(value);
        }

        public static double ToDouble<WEIGHT>(WEIGHT value) where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            return double.CreateChecked(value);
        }

        //
        //  Supporting internal methods -- dispatched by scheme type:
        //
        //  Crease and corner masks are common to most schemes -- the rest need to be provided
        //  for each Scheme specialization.
        //
        public void assignCreaseMaskForEdge<EDGE, WEIGHT>(EDGE edge, IMask<WEIGHT> mask)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            //  Loop's specialization is identical to the generic one:
            mask.SetNumVertexWeights(2);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            mask.VertexWeight(0) = Cf<WEIGHT>(0.5f);
            mask.VertexWeight(1) = Cf<WEIGHT>(0.5f);
        }

        public void assignCornerMaskForVertex<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            mask.SetNumVertexWeights(1);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            mask.VertexWeight(0) = Cf<WEIGHT>(1.0f);
        }

        public void assignSmoothMaskForEdge<EDGE, WEIGHT>(EDGE edge, IMask<WEIGHT> mask)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_CATMARK: CatmarkScheme.assignSmoothMaskForEdge(_options, edge, mask); break;
                case SchemeType.SCHEME_LOOP:    LoopScheme.assignSmoothMaskForEdge(_options, edge, mask); break;
                default: System.Diagnostics.Debug.Assert(false); break;
            }
        }

        public void assignCreaseMaskForVertex<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> mask, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_CATMARK: CatmarkScheme.assignCreaseMaskForVertex(_options, vertex, mask, creaseEnds); break;
                case SchemeType.SCHEME_LOOP:    LoopScheme.assignCreaseMaskForVertex(_options, vertex, mask, creaseEnds); break;
                default: System.Diagnostics.Debug.Assert(false); break;
            }
        }

        public void assignSmoothMaskForVertex<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> mask)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_CATMARK: CatmarkScheme.assignSmoothMaskForVertex(_options, vertex, mask); break;
                case SchemeType.SCHEME_LOOP:    LoopScheme.assignSmoothMaskForVertex(_options, vertex, mask); break;
                default: System.Diagnostics.Debug.Assert(false); break;
            }
        }

        //
        //  Limit masks for position and tangents at vertices -- three cases for each:
        //
        public void assignCornerLimitMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> pos)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignCornerLimitMask(_options, vertex, pos); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignCornerLimitMask(_options, vertex, pos); break;
                default:                         LoopScheme.assignCornerLimitMask(_options, vertex, pos); break;
            }
        }

        public void assignCreaseLimitMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> pos, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignCreaseLimitMask(_options, vertex, pos, creaseEnds); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignCreaseLimitMask(_options, vertex, pos, creaseEnds); break;
                default:                         LoopScheme.assignCreaseLimitMask(_options, vertex, pos, creaseEnds); break;
            }
        }

        public void assignSmoothLimitMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> pos)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignSmoothLimitMask(_options, vertex, pos); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignSmoothLimitMask(_options, vertex, pos); break;
                default:                         LoopScheme.assignSmoothLimitMask(_options, vertex, pos); break;
            }
        }

        public void assignCornerLimitTangentMasks<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> tan1, IMask<WEIGHT> tan2)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignCornerLimitTangentMasks(_options, vertex, tan1, tan2); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignCornerLimitTangentMasks(_options, vertex, tan1, tan2); break;
                default:                         LoopScheme.assignCornerLimitTangentMasks(_options, vertex, tan1, tan2); break;
            }
        }

        public void assignCreaseLimitTangentMasks<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> tan1, IMask<WEIGHT> tan2, int[] creaseEnds)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignCreaseLimitTangentMasks(_options, vertex, tan1, tan2, creaseEnds); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignCreaseLimitTangentMasks(_options, vertex, tan1, tan2, creaseEnds); break;
                default:                         LoopScheme.assignCreaseLimitTangentMasks(_options, vertex, tan1, tan2, creaseEnds); break;
            }
        }

        public void assignSmoothLimitTangentMasks<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> tan1, IMask<WEIGHT> tan2)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            switch (_schemeType)
            {
                case SchemeType.SCHEME_BILINEAR: BilinearScheme.assignSmoothLimitTangentMasks(_options, vertex, tan1, tan2); break;
                case SchemeType.SCHEME_CATMARK:  CatmarkScheme.assignSmoothLimitTangentMasks(_options, vertex, tan1, tan2); break;
                default:                         LoopScheme.assignSmoothLimitTangentMasks(_options, vertex, tan1, tan2); break;
            }
        }

        //
        //  The computation of a face-vertex mask is trivial and consistent for all schemes:
        //
        public void ComputeFaceVertexMask<FACE, WEIGHT>(FACE face, IMask<WEIGHT> mask)
            where FACE : IFaceNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            int vertCount = face.GetNumVertices();

            mask.SetNumVertexWeights(vertCount);
            mask.SetNumEdgeWeights(0);
            mask.SetNumFaceWeights(0);
            mask.SetFaceWeightsForFaceCenters(false);

            WEIGHT vWeight = Cf<WEIGHT>(1.0f) / WEIGHT.CreateChecked(vertCount);
            for (int i = 0; i < vertCount; ++i)
            {
                mask.VertexWeight(i) = vWeight;
            }
        }

        //
        //  The computation of an edge-vertex mask requires inspection of sharpness values to
        //  determine if smooth or a crease, and also to detect and apply a transition from a
        //  crease to smooth.  Bilinear is specialized to trivialize it to the crease case.
        //
        public void ComputeEdgeVertexMask<EDGE, WEIGHT>(EDGE edge, IMask<WEIGHT> mask,
                                                        Crease.Rule parentRule = Crease.Rule.RULE_UNKNOWN,
                                                        Crease.Rule childRule = Crease.Rule.RULE_UNKNOWN)
            where EDGE : IEdgeNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            if (_schemeType == SchemeType.SCHEME_BILINEAR)
            {
                //  This should be inline, otherwise trivially replicate it:
                assignCreaseMaskForEdge(edge, mask);
                return;
            }

            //
            //  If the parent was specified or determined to be Smooth, we can quickly return
            //  with a Smooth mask.  Otherwise the parent is a crease -- if the child was
            //  specified as a crease, or the parent sharpness >= 1.0 (making any fractional
            //  weighting favor the crease entirely) we can quickly return with a Crease mask.
            //
            if ((parentRule == Crease.Rule.RULE_SMOOTH) ||
               ((parentRule == Crease.Rule.RULE_UNKNOWN) && (edge.GetSharpness() <= 0.0f)))
            {
                assignSmoothMaskForEdge(edge, mask);
                return;
            }
            if ((childRule == Crease.Rule.RULE_CREASE) || (edge.GetSharpness() >= 1.0f))
            {
                assignCreaseMaskForEdge(edge, mask);
                return;
            }

            //
            //  We have a Crease on the parent and the child was either specified as Smooth
            //  or was not specified at all -- deal with the unspecified case first (again
            //  returning a Crease mask if the child is also determined to be a Crease) and
            //  continue if we have a transition to Smooth.
            //
            if (childRule == Crease.Rule.RULE_UNKNOWN)
            {
                Crease crease = new Crease(_options);

                bool childIsCrease = false;
                if (parentRule == Crease.Rule.RULE_CREASE)
                {
                    //  Child unknown as default value but parent Rule specified as Crease
                    childIsCrease = true;
                }
                else if (crease.IsUniform())
                {
                    //  Sharpness < 1.0 is guaranteed to decay to 0.0 for Uniform child edges
                    childIsCrease = false;
                }
                else
                {
                    //  Sharpness <= 1.0 does not necessarily decay to 0.0 for both child edges...
                    float[] cEdgeSharpness = new float[2];
                    edge.GetChildSharpnesses(crease, cEdgeSharpness);
                    childIsCrease = (cEdgeSharpness[0] > 0.0f) && (cEdgeSharpness[1] > 0.0f);
                }
                if (childIsCrease)
                {
                    assignCreaseMaskForEdge(edge, mask);
                    return;
                }
            }

            //
            //  We are now left with the Crease-to-Smooth case -- compute the Smooth mask
            //  for the child and augment it with the transitional Crease of the parent.
            //
            //  A general combination of separately assigned masks here (as done in the vertex-
            //  vertex case) is overkill -- trivially combine the 0.5f vertex coefficient for
            //  the Crease of the parent with the vertex weights and attenuate the face weights
            //  accordingly.
            //
            assignSmoothMaskForEdge(edge, mask);

            WEIGHT pWeight = Cf<WEIGHT>(edge.GetSharpness());
            WEIGHT cWeight = Cf<WEIGHT>(1.0f) - pWeight;

            mask.VertexWeight(0) = pWeight * Cf<WEIGHT>(0.5f) + cWeight * mask.VertexWeight(0);
            mask.VertexWeight(1) = pWeight * Cf<WEIGHT>(0.5f) + cWeight * mask.VertexWeight(1);

            int faceCount = mask.GetNumFaceWeights();
            for (int i = 0; i < faceCount; ++i)
            {
                mask.FaceWeight(i) *= cWeight;
            }
        }

        //
        //  The computation of a vertex-vertex mask requires inspection of creasing sharpness values
        //  to determine what subdivision Rules apply to the parent and its child vertex, and also to
        //  detect and apply a transition between two differing Rules.  Bilinear is specialized to
        //  remove all unnecessary complexity relating to creasing, Rules, etc.
        //
        public void ComputeVertexVertexMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> mask,
                                                            Crease.Rule pRule = Crease.Rule.RULE_UNKNOWN,
                                                            Crease.Rule cRule = Crease.Rule.RULE_UNKNOWN)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            if (_schemeType == SchemeType.SCHEME_BILINEAR)
            {
                //  This should be inline, otherwise trivially replicate it:
                assignCornerMaskForVertex(vertex, mask);
                return;
            }

            //  Quick assignment and return for the most common case:
            if ((pRule == Crease.Rule.RULE_SMOOTH) || (pRule == Crease.Rule.RULE_DART))
            {
                assignSmoothMaskForVertex(vertex, mask);
                return;
            }
            //  If unspecified, assign the child rule to match the parent rule if specified:
            if ((cRule == Crease.Rule.RULE_UNKNOWN) && (pRule != Crease.Rule.RULE_UNKNOWN))
            {
                cRule = pRule;
            }
            int valence = vertex.GetNumEdges();

            //
            //  Determine if we need the parent edge sharpness values -- identify/gather if so
            //  and use it to compute the parent rule if unspecified:
            //
            float[] pEdgeSharpnessBuffer = new float[valence];
            float[] pEdgeSharpness = null;
            float   pVertexSharpness = 0.0f;

            bool requireParentSharpness = (pRule == Crease.Rule.RULE_UNKNOWN) ||
                                          (pRule == Crease.Rule.RULE_CREASE) ||
                                          (pRule != cRule);
            if (requireParentSharpness)
            {
                pVertexSharpness = vertex.GetSharpness();
                pEdgeSharpness   = vertex.GetSharpnessPerEdge(pEdgeSharpnessBuffer);

                if (pRule == Crease.Rule.RULE_UNKNOWN)
                {
                    pRule = new Crease(_options).DetermineVertexVertexRule(pVertexSharpness, valence, pEdgeSharpness);
                }
            }
            if ((pRule == Crease.Rule.RULE_SMOOTH) || (pRule == Crease.Rule.RULE_DART))
            {
                assignSmoothMaskForVertex(vertex, mask);
                return;  //  As done on entry, we can return immediately if parent is Smooth/Dart
            }
            else if (pRule == Crease.Rule.RULE_CREASE)
            {
                int[] creaseEnds = new int[2];
                new Crease(_options).GetSharpEdgePairOfCrease(pEdgeSharpness, valence, creaseEnds);

                assignCreaseMaskForVertex(vertex, mask, creaseEnds);
            }
            else
            {
                assignCornerMaskForVertex(vertex, mask);
            }
            if (cRule == pRule)
            {
                return;
            }

            //
            //  Identify/gather child sharpness to combine masks for the two differing Rules:
            //
            Crease crease = new Crease(_options);

            float[] cEdgeSharpnessBuffer = new float[valence];
            float[] cEdgeSharpness = vertex.GetChildSharpnessPerEdge(crease, cEdgeSharpnessBuffer);
            float   cVertexSharpness = vertex.GetChildSharpness(crease);

            if (cRule == Crease.Rule.RULE_UNKNOWN)
            {
                cRule = crease.DetermineVertexVertexRule(cVertexSharpness, valence, cEdgeSharpness);
                if (cRule == pRule)
                {
                    return;
                }
            }

            //
            //  Initialize a local child mask, compute the fractional weight from parent and child
            //  sharpness values and combine the two masks:
            //
            WEIGHT[] cMaskWeights = new WEIGHT[1 + 2 * valence];
            LocalMask<WEIGHT> cMask = new LocalMask<WEIGHT>(cMaskWeights, 0, 1, 1 + valence);

            if ((cRule == Crease.Rule.RULE_SMOOTH) || (cRule == Crease.Rule.RULE_DART))
            {
                assignSmoothMaskForVertex(vertex, cMask);
            }
            else if (cRule == Crease.Rule.RULE_CREASE)
            {
                int[] creaseEnds = new int[2];
                new Crease(_options).GetSharpEdgePairOfCrease(cEdgeSharpness, valence, creaseEnds);

                assignCreaseMaskForVertex(vertex, cMask, creaseEnds);
            }
            else
            {
                assignCornerMaskForVertex(vertex, cMask);
            }

            WEIGHT pWeight = Cf<WEIGHT>(crease.ComputeFractionalWeightAtVertex(pVertexSharpness, cVertexSharpness,
                                                           valence, pEdgeSharpness, cEdgeSharpness));
            WEIGHT cWeight = Cf<WEIGHT>(1.0f) - pWeight;

            cMask.CombineVertexVertexMasks(cWeight, pWeight, mask);
        }

        //
        //  The computation of limit masks for vertices:
        //
        public void ComputeVertexLimitMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> mask, Crease.Rule rule)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            if ((rule == Crease.Rule.RULE_SMOOTH) || (rule == Crease.Rule.RULE_DART))
            {
                assignSmoothLimitMask(vertex, mask);
            }
            else if (rule == Crease.Rule.RULE_CREASE)
            {
                float[] edgeSharpness = new float[vertex.GetNumEdges()];
                vertex.GetSharpnessPerEdge(edgeSharpness);

                int[] creaseEnds = new int[2];
                new Crease(_options).GetSharpEdgePairOfCrease(edgeSharpness, vertex.GetNumEdges(), creaseEnds);

                assignCreaseLimitMask(vertex, mask, creaseEnds);
            }
            else
            {
                assignCornerLimitMask(vertex, mask);
            }
        }

        public void ComputeVertexLimitMask<VERTEX, WEIGHT>(VERTEX vertex, IMask<WEIGHT> posMask,
                                                           IMask<WEIGHT> tan1Mask, IMask<WEIGHT> tan2Mask,
                                                           Crease.Rule rule)
            where VERTEX : IVertexNeighborhood
            where WEIGHT : unmanaged, INumber<WEIGHT>
        {
            if ((rule == Crease.Rule.RULE_SMOOTH) || (rule == Crease.Rule.RULE_DART))
            {
                assignSmoothLimitMask(vertex, posMask);
                assignSmoothLimitTangentMasks(vertex, tan1Mask, tan2Mask);
            }
            else if (rule == Crease.Rule.RULE_CREASE)
            {
                float[] edgeSharpness = new float[vertex.GetNumEdges()];
                vertex.GetSharpnessPerEdge(edgeSharpness);

                int[] creaseEnds = new int[2];
                new Crease(_options).GetSharpEdgePairOfCrease(edgeSharpness, vertex.GetNumEdges(), creaseEnds);

                assignCreaseLimitMask(vertex, posMask, creaseEnds);
                assignCreaseLimitTangentMasks(vertex, tan1Mask, tan2Mask, creaseEnds);
            }
            else
            {
                assignCornerLimitMask(vertex, posMask);
                assignCornerLimitTangentMasks(vertex, tan1Mask, tan2Mask);
            }
        }
    }
}
