//
//   Copyright 2015 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;

namespace OpenSubdiv.Far
{
    ///
    ///  The interface required of the element type of the primvar buffers (the C++ templates
    ///  require the same of their <class T> / <class U> buffer arguments):
    ///
    ///       class MyDestination {
    ///           void Clear();
    ///           void AddWithWeight(MySource const & value, REAL weight);
    ///           void AddWithWeight(MyDestination const & value, REAL weight);
    ///       };
    ///
    ///  REAL is the weight type of the PrimvarRefinerReal<REAL> (float or double).
    ///  Since structs are copied, the elements are always accessed in place via the
    ///  ref-returning indexer of a PrimvarBuffer<T>.
    ///
    public interface IPrimvar<T, REAL>
    {
        void Clear();
        void AddWithWeight(T src, REAL weight);
    }

    ///  Convenience interface for float weights (as used with PrimvarRefiner = PrimvarRefinerReal<float>)
    public interface IPrimvar<T> : IPrimvar<T, float>
    {
    }

    ///
    ///  The interface required of a primvar buffer (the C++ "operator[]" of the <class T> / <class U>
    ///  buffer arguments).  The indexer returns the element in place (by reference) so that the
    ///  element's Clear()/AddWithWeight() mutate the buffer.  Besides arrays (PrimvarBuffer<T>),
    ///  facades such as StencilBuilder<REAL>.Index implement it.
    ///
    public interface IPrimvarBuffer<T>
    {
        ref T this[int index] { get; }
    }

    ///
    ///  C# replacement of the C++ buffer arguments ("T const & src" / "U & dst": a pointer or a
    ///  container with operator[]): an array with a start offset.  An implicit conversion from T[]
    ///  (offset 0) is provided, and new PrimvarBuffer<T>(array, offset) is the equivalent of "array + offset".
    ///
    public readonly struct PrimvarBuffer<T> : IPrimvarBuffer<T>
    {
        public readonly T[] Data;
        public readonly int Offset;

        public PrimvarBuffer(T[] data, int offset = 0)
        {
            Data = data;
            Offset = offset;
        }

        public ref T this[int index]
        {
            get
            {
                return ref Data[Offset + index];
            }
        }

        public static implicit operator PrimvarBuffer<T>(T[] data)
        {
            return new PrimvarBuffer<T>(data, 0);
        }
    }

    ///
    ///  \brief Applies refinement operations to generic primvar data.
    ///
    public class PrimvarRefinerReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public PrimvarRefinerReal(TopologyRefiner refiner)
        {
            _refiner = refiner;
        }

        public TopologyRefiner GetTopologyRefiner()
        {
            return _refiner;
        }

        //@{
        ///  @name Primvar data interpolation
        ///
        /// \note Interpolation methods template both the source and destination
        ///       data buffer classes. Client-code is expected to provide interfaces
        ///       that implement the functions specific to its primitive variable
        ///       data layout (see IPrimvar<T, REAL>).
        ///
        ///       Primitive variable buffers are expected to be arrays of instances,
        ///       passed as PrimvarBuffer<T> (T[] plus start offset) or directly as T[].
        ///

        /// \brief Apply vertex interpolation weights to a primvar buffer for a single
        ///        level of refinement.
        ///
        /// The destination buffer must allocate an array of data for all the
        /// refined vertices, i.e. at least refiner.GetLevel(level).GetNumVertices()
        ///
        /// @param level  The refinement level
        ///
        /// @param src    Source primvar buffer (control vertex data)
        ///
        /// @param dst    Destination primvar buffer (refined vertex data)
        ///
        //
        //  Public entry points to the methods.  Queries of the scheme type and its
        //  use as a template parameter in subsequent implementation will be factored
        //  out of a later release:
        //
        public void Interpolate<T, U, TBUF, UBUF>(int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            System.Diagnostics.Debug.Assert(level > 0 && level <= (int)_refiner._refinements.Count);

            switch (_refiner._subdivType)
            {
            case SchemeType.SCHEME_CATMARK:
            case SchemeType.SCHEME_LOOP:
            case SchemeType.SCHEME_BILINEAR:
                interpFromFaces<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst);
                interpFromEdges<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst);
                interpFromVerts<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst);
                break;
            }
        }

        public void Interpolate<T, U>(int level, PrimvarBuffer<T> src, PrimvarBuffer<U> dst)
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Interpolate<T, U, PrimvarBuffer<T>, PrimvarBuffer<U>>(level, src, dst);
        }

        public void Interpolate<T, U>(int level, T[] src, U[] dst)
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Interpolate(level, new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0));
        }

        /// \brief Apply only varying interpolation weights to a primvar buffer
        ///        for a single level of refinement.
        ///
        /// This method can useful if the varying primvar data does not need to be
        /// re-computed over time.
        ///
        /// The destination buffer must allocate an array of data for all the
        /// refined vertices, i.e. at least refiner.GetLevel(level).GetNumVertices()
        ///
        /// @param level  The refinement level
        ///
        /// @param src    Source primvar buffer (control vertex data)
        ///
        /// @param dst    Destination primvar buffer (refined vertex data)
        ///
        public void InterpolateVarying<T, U, TBUF, UBUF>(int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>
        {
            System.Diagnostics.Debug.Assert(level > 0 && level <= (int)_refiner._refinements.Count);

            Refinement refinement = _refiner.getRefinement(level - 1);
            Level parent = refinement.parent();

            //
            //  Group values to interpolate based on origin -- note that there may
            //  be none originating from faces:
            //
            if (refinement.getNumChildVerticesFromFaces() > 0)
            {
                for (int face = 0; face < parent.getNumFaces(); ++face)
                {
                    int cVert = refinement.getFaceChildVertex(face);
                    if (FarTypes.IndexIsValid(cVert))
                    {
                        //  Apply the weights to the parent face's vertices:
                        ConstIndexArray fVerts = parent.getFaceVertices(face);

                        REAL fVaryingWeight = Cf(1.0f) / REAL.CreateChecked(fVerts.size());

                        dst[cVert].Clear();
                        for (int i = 0; i < fVerts.size(); ++i)
                        {
                            dst[cVert].AddWithWeight(src[fVerts[i]], fVaryingWeight);
                        }
                    }
                }
            }
            for (int edge = 0; edge < parent.getNumEdges(); ++edge)
            {
                int cVert = refinement.getEdgeChildVertex(edge);
                if (FarTypes.IndexIsValid(cVert))
                {
                    //  Apply the weights to the parent edges's vertices
                    ConstIndexArray eVerts = parent.getEdgeVertices(edge);

                    dst[cVert].Clear();
                    dst[cVert].AddWithWeight(src[eVerts[0]], Cf(0.5f));
                    dst[cVert].AddWithWeight(src[eVerts[1]], Cf(0.5f));
                }
            }
            for (int vert = 0; vert < parent.getNumVertices(); ++vert)
            {
                int cVert = refinement.getVertexChildVertex(vert);
                if (FarTypes.IndexIsValid(cVert))
                {
                    //  Essentially copy the parent vertex:
                    dst[cVert].Clear();
                    dst[cVert].AddWithWeight(src[vert], Cf(1.0f));
                }
            }
        }

        public void InterpolateVarying<T, U>(int level, PrimvarBuffer<T> src, PrimvarBuffer<U> dst)
            where U : IPrimvar<T, REAL>
        {
            InterpolateVarying<T, U, PrimvarBuffer<T>, PrimvarBuffer<U>>(level, src, dst);
        }

        public void InterpolateVarying<T, U>(int level, T[] src, U[] dst)
            where U : IPrimvar<T, REAL>
        {
            InterpolateVarying(level, new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0));
        }

        /// \brief Refine uniform (per-face) primvar data between levels.
        ///
        /// Data is simply copied from a parent face to its child faces and does not involve
        /// any weighting.  Setting the source primvar data for the base level to be the index
        /// of each face allows the propagation of the base face to primvar data for child
        /// faces in all levels.
        ///
        /// The destination buffer must allocate an array of data for all the refined faces,
        /// i.e. at least refiner.GetLevel(level).GetNumFaces()
        ///
        /// @param level  The refinement level
        ///
        /// @param src    Source primvar buffer
        ///
        /// @param dst    Destination primvar buffer
        ///
        public void InterpolateFaceUniform<T, TBUF, UBUF>(int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<T>
        {
            System.Diagnostics.Debug.Assert(level > 0 && level <= (int)_refiner._refinements.Count);

            Refinement refinement = _refiner.getRefinement(level - 1);
            Level child = refinement.child();

            for (int cFace = 0; cFace < child.getNumFaces(); ++cFace)
            {
                int pFace = refinement.getChildFaceParentFace(cFace);

                dst[cFace] = src[pFace];
            }
        }

        public void InterpolateFaceUniform<T>(int level, PrimvarBuffer<T> src, PrimvarBuffer<T> dst)
        {
            InterpolateFaceUniform<T, PrimvarBuffer<T>, PrimvarBuffer<T>>(level, src, dst);
        }

        public void InterpolateFaceUniform<T>(int level, T[] src, T[] dst)
        {
            InterpolateFaceUniform(level, new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<T>(dst, 0));
        }

        /// \brief Apply face-varying interpolation weights to a primvar buffer
        ///        associated with a particular face-varying channel.
        ///
        /// Unlike vertex and varying primvar buffers, there is not a 1-to-1 correspondence
        /// between vertices and face-varying values -- typically there are more face-varying
        /// values than vertices.  Each face-varying channel is also independent in how its
        /// values relate to the vertices.
        ///
        /// The destination buffer must allocate an array of data for all the refined values,
        /// i.e. at least refiner.GetLevel(level).GetNumFVarValues(channel).
        ///
        public void InterpolateFaceVarying<T, U, TBUF, UBUF>(int level, TBUF src, UBUF dst, int channel = 0)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            System.Diagnostics.Debug.Assert(level > 0 && level <= (int)_refiner._refinements.Count);

            switch (_refiner._subdivType)
            {
            case SchemeType.SCHEME_CATMARK:
            case SchemeType.SCHEME_LOOP:
            case SchemeType.SCHEME_BILINEAR:
                interpFVarFromFaces<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst, channel);
                interpFVarFromEdges<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst, channel);
                interpFVarFromVerts<T, U, TBUF, UBUF>(_refiner._subdivType, level, src, dst, channel);
                break;
            }
        }

        public void InterpolateFaceVarying<T, U>(int level, PrimvarBuffer<T> src, PrimvarBuffer<U> dst, int channel = 0)
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            InterpolateFaceVarying<T, U, PrimvarBuffer<T>, PrimvarBuffer<U>>(level, src, dst, channel);
        }

        public void InterpolateFaceVarying<T, U>(int level, T[] src, U[] dst, int channel = 0)
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            InterpolateFaceVarying(level, new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0), channel);
        }

        /// \brief Apply limit weights to a primvar buffer
        ///
        /// The source buffer must refer to an array of previously interpolated
        /// vertex data for the last refinement level.  The destination buffer
        /// must allocate an array for all vertices at the last refinement level,
        /// i.e. at least refiner.GetLevel(refiner.GetMaxLevel()).GetNumVertices()
        ///
        /// @param src     Source primvar buffer (refined data) for last level
        ///
        /// @param dstPos  Destination primvar buffer (data at the limit)
        ///
        public void Limit<T, U, TBUF, UBUF>(TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>
        {
            if (_refiner.getLevel(_refiner.GetMaxLevel()).getNumVertexEdgesTotal() == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in PrimvarRefiner::Limit() -- " +
                    "last level of refinement does not include full topology.");
                return;
            }

            switch (_refiner._subdivType)
            {
            case SchemeType.SCHEME_CATMARK:
            case SchemeType.SCHEME_LOOP:
            case SchemeType.SCHEME_BILINEAR:
                limit<T, U, U, U, TBUF, UBUF, UBUF, UBUF>(_refiner._subdivType, src, dst, default, default, false);
                break;
            }
        }

        public void Limit<T, U>(PrimvarBuffer<T> src, PrimvarBuffer<U> dst)
            where U : IPrimvar<T, REAL>
        {
            Limit<T, U, PrimvarBuffer<T>, PrimvarBuffer<U>>(src, dst);
        }

        public void Limit<T, U>(T[] src, U[] dst)
            where U : IPrimvar<T, REAL>
        {
            Limit(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0));
        }

        public void Limit<T, U, U1, U2, TBUF, UBUF, U1BUF, U2BUF>(TBUF src, UBUF dstPos, U1BUF dstTan1, U2BUF dstTan2)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U1BUF : IPrimvarBuffer<U1>
            where U2BUF : IPrimvarBuffer<U2>
            where U : IPrimvar<T, REAL>
            where U1 : IPrimvar<T, REAL>
            where U2 : IPrimvar<T, REAL>
        {
            if (_refiner.getLevel(_refiner.GetMaxLevel()).getNumVertexEdgesTotal() == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in PrimvarRefiner::Limit() -- " +
                    "last level of refinement does not include full topology.");
                return;
            }

            switch (_refiner._subdivType)
            {
            case SchemeType.SCHEME_CATMARK:
            case SchemeType.SCHEME_LOOP:
            case SchemeType.SCHEME_BILINEAR:
                limit<T, U, U1, U2, TBUF, UBUF, U1BUF, U2BUF>(_refiner._subdivType, src, dstPos, dstTan1, dstTan2, true);
                break;
            }
        }

        public void Limit<T, U, U1, U2>(PrimvarBuffer<T> src, PrimvarBuffer<U> dstPos, PrimvarBuffer<U1> dstTan1, PrimvarBuffer<U2> dstTan2)
            where U : IPrimvar<T, REAL>
            where U1 : IPrimvar<T, REAL>
            where U2 : IPrimvar<T, REAL>
        {
            Limit<T, U, U1, U2, PrimvarBuffer<T>, PrimvarBuffer<U>, PrimvarBuffer<U1>, PrimvarBuffer<U2>>(src, dstPos, dstTan1, dstTan2);
        }

        public void Limit<T, U, U1, U2>(T[] src, U[] dstPos, U1[] dstTan1, U2[] dstTan2)
            where U : IPrimvar<T, REAL>
            where U1 : IPrimvar<T, REAL>
            where U2 : IPrimvar<T, REAL>
        {
            Limit(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dstPos, 0),
                  new PrimvarBuffer<U1>(dstTan1, 0), new PrimvarBuffer<U2>(dstTan2, 0));
        }

        public void LimitFaceVarying<T, U, TBUF, UBUF>(TBUF src, UBUF dst, int channel = 0)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>
        {
            if (_refiner.getLevel(_refiner.GetMaxLevel()).getNumVertexEdgesTotal() == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in PrimvarRefiner::LimitFaceVarying() -- " +
                    "last level of refinement does not include full topology.");
                return;
            }

            switch (_refiner._subdivType)
            {
            case SchemeType.SCHEME_CATMARK:
            case SchemeType.SCHEME_LOOP:
            case SchemeType.SCHEME_BILINEAR:
                limitFVar<T, U, TBUF, UBUF>(_refiner._subdivType, src, dst, channel);
                break;
            }
        }

        public void LimitFaceVarying<T, U>(PrimvarBuffer<T> src, PrimvarBuffer<U> dst, int channel = 0)
            where U : IPrimvar<T, REAL>
        {
            LimitFaceVarying<T, U, PrimvarBuffer<T>, PrimvarBuffer<U>>(src, dst, channel);
        }

        public void LimitFaceVarying<T, U>(T[] src, U[] dst, int channel = 0)
            where U : IPrimvar<T, REAL>
        {
            LimitFaceVarying(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0), channel);
        }

        //@}

        //  Conversion of a float constant to the weight type (the C++ float literals promote to REAL)
        private static REAL Cf(float value)
        {
            return REAL.CreateChecked(value);
        }

        //
        //  Internal implementation methods -- grouping vertices to be interpolated
        //  based on the type of parent component from which they originated:
        //
        //  (The C++ template parameter SCHEME becomes the Sdc.Scheme instance constructed
        //  from the scheme type.)
        //
        private void interpFromFaces<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);
            Level parent = refinement.parent();

            if (refinement.getNumChildVerticesFromFaces() == 0) return;

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            REAL[] fVertWeights = new REAL[Math.Max(parent.getMaxValence(), 16)];

            for (int face = 0; face < parent.getNumFaces(); ++face)
            {
                int cVert = refinement.getFaceChildVertex(face);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                //  Declare and compute mask weights for this vertex relative to its parent face:
                ConstIndexArray fVerts = parent.getFaceVertices(face);

                Mask fMask = new Mask(fVertWeights, 0, null, 0, null, 0);
                FaceInterface fHood = new FaceInterface(fVerts.size());

                scheme.ComputeFaceVertexMask(fHood, fMask);

                //  Apply the weights to the parent face's vertices:
                dst[cVert].Clear();

                for (int i = 0; i < fVerts.size(); ++i)
                {
                    dst[cVert].AddWithWeight(src[fVerts[i]], fVertWeights[i]);
                }
            }
        }

        private void interpFromEdges<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);
            Level parent = refinement.parent();
            Level child = refinement.child();

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            EdgeInterface eHood = new EdgeInterface(parent);

            REAL[] eVertWeights = new REAL[2];
            REAL[] eFaceWeights = new REAL[Math.Max(parent.getMaxEdgeFaces(), 8)];

            for (int edge = 0; edge < parent.getNumEdges(); ++edge)
            {
                int cVert = refinement.getEdgeChildVertex(edge);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                //  Declare and compute mask weights for this vertex relative to its parent edge:
                ConstIndexArray eVerts = parent.getEdgeVertices(edge),
                                eFaces = parent.getEdgeFaces(edge);

                Mask eMask = new Mask(eVertWeights, 0, null, 0, eFaceWeights, 0);

                eHood.SetIndex(edge);

                Crease.Rule pRule = (parent.getEdgeSharpness(edge) > 0.0f) ? Crease.Rule.RULE_CREASE : Crease.Rule.RULE_SMOOTH;
                Crease.Rule cRule = child.getVertexRule(cVert);

                scheme.ComputeEdgeVertexMask(eHood, eMask, pRule, cRule);

                //  Apply the weights to the parent edges's vertices and (if applicable) to
                //  the child vertices of its incident faces:
                dst[cVert].Clear();
                dst[cVert].AddWithWeight(src[eVerts[0]], eVertWeights[0]);
                dst[cVert].AddWithWeight(src[eVerts[1]], eVertWeights[1]);

                if (eMask.GetNumFaceWeights() > 0)
                {
                    for (int i = 0; i < eFaces.size(); ++i)
                    {
                        if (eMask.AreFaceWeightsForFaceCenters())
                        {
                            System.Diagnostics.Debug.Assert(refinement.getNumChildVerticesFromFaces() > 0);
                            int cVertOfFace = refinement.getFaceChildVertex(eFaces[i]);

                            System.Diagnostics.Debug.Assert(FarTypes.IndexIsValid(cVertOfFace));
                            dst[cVert].AddWithWeight(dst[cVertOfFace], eFaceWeights[i]);
                        }
                        else
                        {
                            int pFace = eFaces[i];
                            ConstIndexArray pFaceEdges = parent.getFaceEdges(pFace),
                                            pFaceVerts = parent.getFaceVertices(pFace);

                            int eInFace = 0;
                            for (; pFaceEdges[eInFace] != edge; ++eInFace) ;

                            int vInFace = eInFace + 2;
                            if (vInFace >= pFaceVerts.size()) vInFace -= pFaceVerts.size();

                            int pVertNext = pFaceVerts[vInFace];
                            dst[cVert].AddWithWeight(src[pVertNext], eFaceWeights[i]);
                        }
                    }
                }
            }
        }

        private void interpFromVerts<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);
            Level parent = refinement.parent();
            Level child = refinement.child();

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            VertexInterface vHood = new VertexInterface(parent, child);

            REAL[] weightBuffer = new REAL[Math.Max(2 * parent.getMaxValence(), 32)];

            for (int vert = 0; vert < parent.getNumVertices(); ++vert)
            {
                int cVert = refinement.getVertexChildVertex(vert);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                //  Declare and compute mask weights for this vertex relative to its parent edge:
                ConstIndexArray vEdges = parent.getVertexEdges(vert),
                                vFaces = parent.getVertexFaces(vert);

                REAL[] vVertWeight = new REAL[1];
                REAL[] vEdgeWeights = weightBuffer;
                int vEdgeWeightsOffset = 0;
                REAL[] vFaceWeights = weightBuffer;
                int vFaceWeightsOffset = vEdgeWeightsOffset + vEdges.size();

                Mask vMask = new Mask(vVertWeight, 0, vEdgeWeights, vEdgeWeightsOffset, vFaceWeights, vFaceWeightsOffset);

                vHood.SetIndex(vert, cVert);

                Crease.Rule pRule = parent.getVertexRule(vert);
                Crease.Rule cRule = child.getVertexRule(cVert);

                scheme.ComputeVertexVertexMask(vHood, vMask, pRule, cRule);

                //  Apply the weights to the parent vertex, the vertices opposite its incident
                //  edges, and the child vertices of its incident faces:
                //
                //  In order to improve numerical precision, it's better to apply smaller weights
                //  first, so begin with the face-weights followed by the edge-weights and the
                //  vertex weight last.
                dst[cVert].Clear();

                if (vMask.GetNumFaceWeights() > 0)
                {
                    System.Diagnostics.Debug.Assert(vMask.AreFaceWeightsForFaceCenters());

                    for (int i = 0; i < vFaces.size(); ++i)
                    {
                        int cVertOfFace = refinement.getFaceChildVertex(vFaces[i]);
                        System.Diagnostics.Debug.Assert(FarTypes.IndexIsValid(cVertOfFace));
                        dst[cVert].AddWithWeight(dst[cVertOfFace], vFaceWeights[vFaceWeightsOffset + i]);
                    }
                }
                if (vMask.GetNumEdgeWeights() > 0)
                {
                    for (int i = 0; i < vEdges.size(); ++i)
                    {
                        ConstIndexArray eVerts = parent.getEdgeVertices(vEdges[i]);
                        int pVertOppositeEdge = (eVerts[0] == vert) ? eVerts[1] : eVerts[0];

                        dst[cVert].AddWithWeight(src[pVertOppositeEdge], vEdgeWeights[vEdgeWeightsOffset + i]);
                    }
                }
                dst[cVert].AddWithWeight(src[vert], vVertWeight[0]);
            }
        }

        //
        // Internal face-varying implementation details:
        //
        private void interpFVarFromFaces<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst, int channel)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);

            if (refinement.getNumChildVerticesFromFaces() == 0) return;

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            Level parentLevel = refinement.parent();
            Level childLevel = refinement.child();

            FVarLevel parentFVar = parentLevel.getFVarLevel(channel);
            FVarLevel childFVar = childLevel.getFVarLevel(channel);

            REAL[] fValueWeights = new REAL[Math.Max(parentLevel.getMaxValence(), 16)];

            for (int face = 0; face < parentLevel.getNumFaces(); ++face)
            {
                int cVert = refinement.getFaceChildVertex(face);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                int cVertValue = childFVar.getVertexValueOffset(cVert);

                //  The only difference for face-varying here is that we get the values associated
                //  with each face-vertex directly from the FVarLevel, rather than using the parent
                //  face-vertices directly.  If any face-vertex has any sibling values, then we may
                //  get the wrong one using the face-vertex index directly.

                //  Declare and compute mask weights for this vertex relative to its parent face:
                ConstIndexArray fValues = parentFVar.getFaceValues(face);

                Mask fMask = new Mask(fValueWeights, 0, null, 0, null, 0);
                FaceInterface fHood = new FaceInterface(fValues.size());

                scheme.ComputeFaceVertexMask(fHood, fMask);

                //  Apply the weights to the parent face's vertices:
                dst[cVertValue].Clear();

                for (int i = 0; i < fValues.size(); ++i)
                {
                    dst[cVertValue].AddWithWeight(src[fValues[i]], fValueWeights[i]);
                }
            }
        }

        private void interpFVarFromEdges<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst, int channel)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            Level parentLevel = refinement.parent();
            Level childLevel = refinement.child();

            FVarRefinement refineFVar = refinement.getFVarRefinement(channel);
            FVarLevel parentFVar = parentLevel.getFVarLevel(channel);
            FVarLevel childFVar = childLevel.getFVarLevel(channel);

            //
            //  Allocate and initialize (if linearly interpolated) interpolation weights for
            //  the edge mask:
            //
            REAL[] eVertWeights = new REAL[2];
            REAL[] eFaceWeights = new REAL[Math.Max(parentLevel.getMaxEdgeFaces(), 8)];

            Mask eMask = new Mask(eVertWeights, 0, null, 0, eFaceWeights, 0);

            bool isLinearFVar = parentFVar.isLinear() || (_refiner._subdivType == SchemeType.SCHEME_BILINEAR);
            if (isLinearFVar)
            {
                eMask.SetNumVertexWeights(2);
                eMask.SetNumEdgeWeights(0);
                eMask.SetNumFaceWeights(0);

                eVertWeights[0] = Cf(0.5f);
                eVertWeights[1] = Cf(0.5f);
            }

            EdgeInterface eHood = new EdgeInterface(parentLevel);

            for (int edge = 0; edge < parentLevel.getNumEdges(); ++edge)
            {
                int cVert = refinement.getEdgeChildVertex(edge);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                ConstIndexArray cVertValues = childFVar.getVertexValues(cVert);

                bool fvarEdgeVertMatchesVertex = childFVar.valueTopologyMatches(cVertValues[0]);
                if (fvarEdgeVertMatchesVertex)
                {
                    //
                    //  If smoothly interpolated, compute new weights for the edge mask:
                    //
                    if (!isLinearFVar)
                    {
                        eHood.SetIndex(edge);

                        Crease.Rule pRule = (parentLevel.getEdgeSharpness(edge) > 0.0f)
                                                ? Crease.Rule.RULE_CREASE : Crease.Rule.RULE_SMOOTH;
                        Crease.Rule cRule = childLevel.getVertexRule(cVert);

                        scheme.ComputeEdgeVertexMask(eHood, eMask, pRule, cRule);
                    }

                    //  Apply the weights to the parent edge's vertices and (if applicable) to
                    //  the child vertices of its incident faces:
                    //
                    //  Even though the face-varying topology matches the vertex topology, we need
                    //  to be careful here when getting values corresponding to the two end-vertices.
                    //  While the edge may be continuous, the vertices at their ends may have
                    //  discontinuities elsewhere in their neighborhood (i.e. on the "other side"
                    //  of the end-vertex) and so have sibling values associated with them.  In most
                    //  cases the topology for an end-vertex will match and we can use it directly,
                    //  but we must still check and retrieve as needed.
                    //
                    //  Indices for values corresponding to face-vertices are guaranteed to match,
                    //  so we can use the child-vertex indices directly.
                    //
                    //  And by "directly", we always use getVertexValue(vertexIndex) to reference
                    //  values in the "src" to account for the possible indirection that may exist at
                    //  level 0 -- where there may be fewer values than vertices and an additional
                    //  indirection is necessary.  We can use a vertex index directly for "dst" when
                    //  it matches.
                    //
                    int[] eVertValues = new int[2];

                    parentFVar.getEdgeFaceValues(edge, 0, eVertValues);

                    int cVertValue = cVertValues[0];

                    dst[cVertValue].Clear();
                    dst[cVertValue].AddWithWeight(src[eVertValues[0]], eVertWeights[0]);
                    dst[cVertValue].AddWithWeight(src[eVertValues[1]], eVertWeights[1]);

                    if (eMask.GetNumFaceWeights() > 0)
                    {
                        ConstIndexArray eFaces = parentLevel.getEdgeFaces(edge);

                        for (int i = 0; i < eFaces.size(); ++i)
                        {
                            if (eMask.AreFaceWeightsForFaceCenters())
                            {
                                int cVertOfFace = refinement.getFaceChildVertex(eFaces[i]);
                                System.Diagnostics.Debug.Assert(FarTypes.IndexIsValid(cVertOfFace));

                                int cValueOfFace = childFVar.getVertexValueOffset(cVertOfFace);
                                dst[cVertValue].AddWithWeight(dst[cValueOfFace], eFaceWeights[i]);
                            }
                            else
                            {
                                int pFace = eFaces[i];
                                ConstIndexArray pFaceEdges = parentLevel.getFaceEdges(pFace),
                                                pFaceVerts = parentLevel.getFaceVertices(pFace);

                                int eInFace = 0;
                                for (; pFaceEdges[eInFace] != edge; ++eInFace) ;

                                //  Edge "i" spans vertices [i,i+1] so we want i+2...
                                int vInFace = eInFace + 2;
                                if (vInFace >= pFaceVerts.size()) vInFace -= pFaceVerts.size();

                                int pValueNext = parentFVar.getFaceValues(pFace)[vInFace];
                                dst[cVertValue].AddWithWeight(src[pValueNext], eFaceWeights[i]);
                            }
                        }
                    }
                }
                else
                {
                    //
                    //  Mismatched edge-verts should just be linearly interpolated between the pairs of
                    //  values for each sibling of the child edge-vertex -- the question is:  which face
                    //  holds that pair of values for a given sibling?
                    //
                    //  In the manifold case, the sibling and edge-face indices will correspond.  We
                    //  will eventually need to update this to account for > 3 incident faces.
                    //
                    for (int i = 0; i < cVertValues.size(); ++i)
                    {
                        int[] eVertValues = new int[2];
                        int eFaceIndex = refineFVar.getChildValueParentSource(cVert, i);
                        System.Diagnostics.Debug.Assert(eFaceIndex == i);

                        parentFVar.getEdgeFaceValues(edge, eFaceIndex, eVertValues);

                        int cVertValue = cVertValues[i];

                        dst[cVertValue].Clear();
                        dst[cVertValue].AddWithWeight(src[eVertValues[0]], Cf(0.5f));
                        dst[cVertValue].AddWithWeight(src[eVertValues[1]], Cf(0.5f));
                    }
                }
            }
        }

        private void interpFVarFromVerts<T, U, TBUF, UBUF>(SchemeType SCHEME, int level, TBUF src, UBUF dst, int channel)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>, IPrimvar<U, REAL>
        {
            Refinement refinement = _refiner.getRefinement(level - 1);

            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            Level parentLevel = refinement.parent();
            Level childLevel = refinement.child();

            FVarRefinement refineFVar = refinement.getFVarRefinement(channel);
            FVarLevel parentFVar = parentLevel.getFVarLevel(channel);
            FVarLevel childFVar = childLevel.getFVarLevel(channel);

            bool isLinearFVar = parentFVar.isLinear() || (_refiner._subdivType == SchemeType.SCHEME_BILINEAR);

            REAL[] weightBuffer = new REAL[Math.Max(2 * parentLevel.getMaxValence(), 32)];

            int[] vEdgeValues = new int[Math.Max(parentLevel.getMaxValence(), 16)];

            VertexInterface vHood = new VertexInterface(parentLevel, childLevel);

            for (int vert = 0; vert < parentLevel.getNumVertices(); ++vert)
            {
                int cVert = refinement.getVertexChildVertex(vert);
                if (!FarTypes.IndexIsValid(cVert))
                    continue;

                ConstIndexArray pVertValues = parentFVar.getVertexValues(vert),
                                cVertValues = childFVar.getVertexValues(cVert);

                bool fvarVertVertMatchesVertex = childFVar.valueTopologyMatches(cVertValues[0]);
                if (isLinearFVar && fvarVertVertMatchesVertex)
                {
                    dst[cVertValues[0]].Clear();
                    dst[cVertValues[0]].AddWithWeight(src[pVertValues[0]], Cf(1.0f));
                    continue;
                }

                if (fvarVertVertMatchesVertex)
                {
                    //
                    //  Declare and compute mask weights for this vertex relative to its parent edge:
                    //
                    //  (We really need to encapsulate this somewhere else for use here and in the
                    //  general case)
                    //
                    ConstIndexArray vEdges = parentLevel.getVertexEdges(vert);

                    REAL[] vVertWeight = new REAL[1];
                    REAL[] vEdgeWeights = weightBuffer;
                    int vEdgeWeightsOffset = 0;
                    REAL[] vFaceWeights = weightBuffer;
                    int vFaceWeightsOffset = vEdgeWeightsOffset + vEdges.size();

                    Mask vMask = new Mask(vVertWeight, 0, vEdgeWeights, vEdgeWeightsOffset, vFaceWeights, vFaceWeightsOffset);

                    vHood.SetIndex(vert, cVert);

                    Crease.Rule pRule = parentLevel.getVertexRule(vert);
                    Crease.Rule cRule = childLevel.getVertexRule(cVert);

                    scheme.ComputeVertexVertexMask(vHood, vMask, pRule, cRule);

                    //  Apply the weights to the parent vertex, the vertices opposite its incident
                    //  edges, and the child vertices of its incident faces:
                    //
                    //  Even though the face-varying topology matches the vertex topology, we need
                    //  to be careful here when getting values corresponding to vertices at the
                    //  ends of edges.  While the edge may be continuous, the end vertex may have
                    //  discontinuities elsewhere in their neighborhood (i.e. on the "other side"
                    //  of the end-vertex) and so have sibling values associated with them.  In most
                    //  cases the topology for an end-vertex will match and we can use it directly,
                    //  but we must still check and retrieve as needed.
                    //
                    //  Indices for values corresponding to face-vertices are guaranteed to match,
                    //  so we can use the child-vertex indices directly.
                    //
                    //  And by "directly", we always use getVertexValue(vertexIndex) to reference
                    //  values in the "src" to account for the possible indirection that may exist at
                    //  level 0 -- where there may be fewer values than vertices and an additional
                    //  indirection is necessary.  We can use a vertex index directly for "dst" when
                    //  it matches.
                    //
                    //  As with applying the mask to vertex data, in order to improve numerical
                    //  precision, it's better to apply smaller weights first, so begin with the
                    //  face-weights followed by the edge-weights and the vertex weight last.
                    //
                    int pVertValue = pVertValues[0];
                    int cVertValue = cVertValues[0];

                    dst[cVertValue].Clear();
                    if (vMask.GetNumFaceWeights() > 0)
                    {
                        System.Diagnostics.Debug.Assert(vMask.AreFaceWeightsForFaceCenters());

                        ConstIndexArray vFaces = parentLevel.getVertexFaces(vert);

                        for (int i = 0; i < vFaces.size(); ++i)
                        {
                            int cVertOfFace = refinement.getFaceChildVertex(vFaces[i]);
                            System.Diagnostics.Debug.Assert(FarTypes.IndexIsValid(cVertOfFace));

                            int cValueOfFace = childFVar.getVertexValueOffset(cVertOfFace);
                            dst[cVertValue].AddWithWeight(dst[cValueOfFace], vFaceWeights[vFaceWeightsOffset + i]);
                        }
                    }
                    if (vMask.GetNumEdgeWeights() > 0)
                    {
                        parentFVar.getVertexEdgeValues(vert, vEdgeValues);

                        for (int i = 0; i < vEdges.size(); ++i)
                        {
                            dst[cVertValue].AddWithWeight(src[vEdgeValues[i]], vEdgeWeights[vEdgeWeightsOffset + i]);
                        }
                    }
                    dst[cVertValue].AddWithWeight(src[pVertValue], vVertWeight[0]);
                }
                else
                {
                    //
                    //  Each FVar value associated with a vertex will be either a corner or a crease,
                    //  or potentially in transition from corner to crease:
                    //      - if the CHILD is a corner, there can be no transition so we have a corner
                    //      - otherwise if the PARENT is a crease, both will be creases (no transition)
                    //      - otherwise the parent must be a corner and the child a crease (transition)
                    //
                    ConstArray<FVarLevel.ValueTag> pValueTags = parentFVar.getVertexValueTags(vert);
                    ConstArray<FVarLevel.ValueTag> cValueTags = childFVar.getVertexValueTags(cVert);

                    for (int cSiblingIndex = 0; cSiblingIndex < cVertValues.size(); ++cSiblingIndex)
                    {
                        int pSiblingIndex = refineFVar.getChildValueParentSource(cVert, cSiblingIndex);
                        System.Diagnostics.Debug.Assert(pSiblingIndex == cSiblingIndex);

                        ushort cSibling = (ushort)cSiblingIndex;
                        ushort pSibling = (ushort)pSiblingIndex;

                        int pVertValue = pVertValues[pSibling];
                        int cVertValue = cVertValues[cSibling];

                        dst[cVertValue].Clear();
                        if (isLinearFVar || cValueTags[cSibling].isCorner())
                        {
                            dst[cVertValue].AddWithWeight(src[pVertValue], Cf(1.0f));
                        }
                        else
                        {
                            //
                            //  We have either a crease or a transition from corner to crease -- in
                            //  either case, we need the end values for the full/fractional crease:
                            //
                            int[] pEndValues = new int[2];
                            parentFVar.getVertexCreaseEndValues(vert, pSibling, pEndValues);

                            REAL vWeight = Cf(0.75f);
                            REAL eWeight = Cf(0.125f);

                            //
                            //  If semi-sharp we need to apply fractional weighting -- if made sharp because
                            //  of the other sibling (dependent-sharp) use the fractional weight from that
                            //  other sibling (should only occur when there are 2):
                            //
                            if (pValueTags[pSibling].isSemiSharp())
                            {
                                REAL wCorner = pValueTags[pSibling].isDepSharp()
                                              ? Cf(refineFVar.getFractionalWeight(vert, (ushort)((pSibling == 0) ? 1 : 0), cVert, (ushort)((cSibling == 0) ? 1 : 0)))
                                              : Cf(refineFVar.getFractionalWeight(vert, pSibling, cVert, cSibling));
                                REAL wCrease = Cf(1.0f) - wCorner;

                                vWeight = wCrease * Cf(0.75f) + wCorner;
                                eWeight = wCrease * Cf(0.125f);
                            }
                            dst[cVertValue].AddWithWeight(src[pEndValues[0]], eWeight);
                            dst[cVertValue].AddWithWeight(src[pEndValues[1]], eWeight);
                            dst[cVertValue].AddWithWeight(src[pVertValue], vWeight);
                        }
                    }
                }
            }
        }

        private void limit<T, U, U1, U2, TBUF, UBUF, U1BUF, U2BUF>(SchemeType SCHEME, TBUF src, UBUF dstPos,
                                         U1BUF dstTan1, U2BUF dstTan2, bool hasTangents)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U1BUF : IPrimvarBuffer<U1>
            where U2BUF : IPrimvarBuffer<U2>
            where U : IPrimvar<T, REAL>
            where U1 : IPrimvar<T, REAL>
            where U2 : IPrimvar<T, REAL>
        {
            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            Level level = _refiner.getLevel(_refiner.GetMaxLevel());

            int maxWeightsPerMask = 1 + 2 * level.getMaxValence();
            //  (hasTangents is passed in place of the (dstTan1Ptr && dstTan2Ptr) test)
            int numMasks = 1 + (hasTangents ? 2 : 0);

            int[] indexBuffer = new int[Math.Max(maxWeightsPerMask, 33)];
            REAL[] weightBuffer = new REAL[Math.Max(numMasks * maxWeightsPerMask, 99)];

            //  Offsets into weightBuffer in place of the Weight * pointers:
            int vPosWeights = 0,
                ePosWeights = vPosWeights + 1,
                fPosWeights = ePosWeights + level.getMaxValence();
            int vTan1Weights = vPosWeights + maxWeightsPerMask,
                eTan1Weights = ePosWeights + maxWeightsPerMask,
                fTan1Weights = fPosWeights + maxWeightsPerMask;
            int vTan2Weights = vTan1Weights + maxWeightsPerMask,
                eTan2Weights = eTan1Weights + maxWeightsPerMask,
                fTan2Weights = fTan1Weights + maxWeightsPerMask;

            Mask posMask = new Mask(weightBuffer, vPosWeights, weightBuffer, ePosWeights, weightBuffer, fPosWeights);
            Mask tan1Mask = new Mask(weightBuffer, vTan1Weights, weightBuffer, eTan1Weights, weightBuffer, fTan1Weights);
            Mask tan2Mask = new Mask(weightBuffer, vTan2Weights, weightBuffer, eTan2Weights, weightBuffer, fTan2Weights);

            //  This is a bit obscure -- assigning both parent and child as last level -- but
            //  this mask type was intended for another purpose.  Consider one for the limit:
            VertexInterface vHood = new VertexInterface(level, level);

            for (int vert = 0; vert < level.getNumVertices(); ++vert)
            {
                ConstIndexArray vEdges = level.getVertexEdges(vert);

                //  Incomplete vertices (present in sparse refinement) do not have their full
                //  topological neighborhood to determine a proper limit -- just leave the
                //  vertex at the refined location and continue to the next:
                if (level.getVertexTag(vert)._incomplete || (vEdges.size() == 0))
                {
                    dstPos[vert].Clear();
                    dstPos[vert].AddWithWeight(src[vert], Cf(1.0f));
                    if (hasTangents)
                    {
                        dstTan1[vert].Clear();
                        dstTan2[vert].Clear();
                    }
                    continue;
                }

                //
                //  Limit masks require the subdivision Rule for the vertex in order to deal
                //  with infinitely sharp features correctly -- including boundaries and corners.
                //  The vertex neighborhood is minimally defined with vertex and edge counts.
                //
                Crease.Rule vRule = level.getVertexRule(vert);

                //  This is a bit obscure -- child vertex index will be ignored here
                vHood.SetIndex(vert, vert);

                if (hasTangents)
                {
                    scheme.ComputeVertexLimitMask(vHood, posMask, tan1Mask, tan2Mask, vRule);
                }
                else
                {
                    scheme.ComputeVertexLimitMask(vHood, posMask, vRule);
                }

                //
                //  Gather the neighboring vertices of this vertex -- the vertices opposite its
                //  incident edges, and the opposite vertices of its incident faces:
                //
                int[] eIndices = indexBuffer;
                int eIndicesOffset = 0;
                int[] fIndices = indexBuffer;
                int fIndicesOffset = vEdges.size();

                for (int i = 0; i < vEdges.size(); ++i)
                {
                    ConstIndexArray eVerts = level.getEdgeVertices(vEdges[i]);

                    eIndices[eIndicesOffset + i] = (eVerts[0] == vert) ? eVerts[1] : eVerts[0];
                }
                if ((posMask.GetNumFaceWeights() != 0) || (hasTangents && (tan1Mask.GetNumFaceWeights() != 0)))
                {
                    ConstIndexArray vFaces = level.getVertexFaces(vert);
                    ConstLocalIndexArray vInFace = level.getVertexFaceLocalIndices(vert);

                    for (int i = 0; i < vFaces.size(); ++i)
                    {
                        ConstIndexArray fVerts = level.getFaceVertices(vFaces[i]);

                        ushort vOppInFace = (ushort)(vInFace[i] + 2);
                        if (vOppInFace >= fVerts.size()) vOppInFace -= (ushort)fVerts.size();

                        fIndices[fIndicesOffset + i] = level.getFaceVertices(vFaces[i])[vOppInFace];
                    }
                }

                //
                //  Combine the weights and indices for position and tangents.  As with applying
                //  refinement masks to vertex data, in order to improve numerical precision, it's
                //  better to apply smaller weights first, so begin with the face-weights followed
                //  by the edge-weights and the vertex weight last.
                //
                dstPos[vert].Clear();
                for (int i = 0; i < posMask.GetNumFaceWeights(); ++i)
                {
                    dstPos[vert].AddWithWeight(src[fIndices[fIndicesOffset + i]], weightBuffer[fPosWeights + i]);
                }
                for (int i = 0; i < posMask.GetNumEdgeWeights(); ++i)
                {
                    dstPos[vert].AddWithWeight(src[eIndices[eIndicesOffset + i]], weightBuffer[ePosWeights + i]);
                }
                dstPos[vert].AddWithWeight(src[vert], weightBuffer[vPosWeights + 0]);

                //
                //  Apply the tangent masks -- both will have the same number of weights and
                //  indices (one tangent may be "padded" to accommodate the other), but these
                //  may differ from those of the position:
                //
                if (hasTangents)
                {
                    System.Diagnostics.Debug.Assert(tan1Mask.GetNumFaceWeights() == tan2Mask.GetNumFaceWeights());
                    System.Diagnostics.Debug.Assert(tan1Mask.GetNumEdgeWeights() == tan2Mask.GetNumEdgeWeights());

                    dstTan1[vert].Clear();
                    dstTan2[vert].Clear();
                    for (int i = 0; i < tan1Mask.GetNumFaceWeights(); ++i)
                    {
                        dstTan1[vert].AddWithWeight(src[fIndices[fIndicesOffset + i]], weightBuffer[fTan1Weights + i]);
                        dstTan2[vert].AddWithWeight(src[fIndices[fIndicesOffset + i]], weightBuffer[fTan2Weights + i]);
                    }
                    for (int i = 0; i < tan1Mask.GetNumEdgeWeights(); ++i)
                    {
                        dstTan1[vert].AddWithWeight(src[eIndices[eIndicesOffset + i]], weightBuffer[eTan1Weights + i]);
                        dstTan2[vert].AddWithWeight(src[eIndices[eIndicesOffset + i]], weightBuffer[eTan2Weights + i]);
                    }
                    dstTan1[vert].AddWithWeight(src[vert], weightBuffer[vTan1Weights + 0]);
                    dstTan2[vert].AddWithWeight(src[vert], weightBuffer[vTan2Weights + 0]);
                }
            }
        }

        private void limitFVar<T, U, TBUF, UBUF>(SchemeType SCHEME, TBUF src, UBUF dst, int channel)
            where TBUF : IPrimvarBuffer<T>
            where UBUF : IPrimvarBuffer<U>
            where U : IPrimvar<T, REAL>
        {
            Scheme scheme = new Scheme(SCHEME, _refiner._subdivOptions);

            Level level = _refiner.getLevel(_refiner.GetMaxLevel());
            FVarLevel fvarChannel = level.getFVarLevel(channel);

            int maxWeightsPerMask = 1 + 2 * level.getMaxValence();

            REAL[] weightBuffer = new REAL[Math.Max(maxWeightsPerMask, 33)];
            int[] vEdgeBuffer = new int[Math.Max(level.getMaxValence(), 16)];

            //  This is a bit obscure -- assign both parent and child as last level
            VertexInterface vHood = new VertexInterface(level, level);

            for (int vert = 0; vert < level.getNumVertices(); ++vert)
            {
                ConstIndexArray vEdges = level.getVertexEdges(vert);
                ConstIndexArray vValues = fvarChannel.getVertexValues(vert);

                //  Incomplete vertices (present in sparse refinement) do not have their full
                //  topological neighborhood to determine a proper limit -- just leave the
                //  values (perhaps more than one per vertex) at the refined location.
                //
                //  The same can be done if the face-varying channel is purely linear.
                //
                bool isIncomplete = (level.getVertexTag(vert)._incomplete || (vEdges.size() == 0));
                if (isIncomplete || fvarChannel.isLinear())
                {
                    for (int i = 0; i < vValues.size(); ++i)
                    {
                        int vValue = vValues[i];

                        dst[vValue].Clear();
                        dst[vValue].AddWithWeight(src[vValue], Cf(1.0f));
                    }
                    continue;
                }

                bool fvarVertMatchesVertex = fvarChannel.valueTopologyMatches(vValues[0]);
                if (fvarVertMatchesVertex)
                {
                    //  Assign the mask weights to the common buffer and compute the mask:
                    //
                    int vWeights = 0,
                        eWeights = vWeights + 1,
                        fWeights = eWeights + vEdges.size();

                    Mask vMask = new Mask(weightBuffer, vWeights, weightBuffer, eWeights, weightBuffer, fWeights);

                    vHood.SetIndex(vert, vert);

                    scheme.ComputeVertexLimitMask(vHood, vMask, level.getVertexRule(vert));

                    //
                    //  Apply mask to corresponding FVar values for neighboring vertices:
                    //
                    int vValue = vValues[0];

                    dst[vValue].Clear();
                    if (vMask.GetNumFaceWeights() > 0)
                    {
                        System.Diagnostics.Debug.Assert(!vMask.AreFaceWeightsForFaceCenters());

                        ConstIndexArray vFaces = level.getVertexFaces(vert);
                        ConstLocalIndexArray vInFace = level.getVertexFaceLocalIndices(vert);

                        for (int i = 0; i < vFaces.size(); ++i)
                        {
                            ConstIndexArray faceValues = fvarChannel.getFaceValues(vFaces[i]);
                            ushort vOppInFace = (ushort)(vInFace[i] + 2);
                            if (vOppInFace >= faceValues.size()) vOppInFace = (ushort)(vOppInFace - faceValues.size());

                            int vValueOppositeFace = faceValues[vOppInFace];

                            dst[vValue].AddWithWeight(src[vValueOppositeFace], weightBuffer[fWeights + i]);
                        }
                    }
                    if (vMask.GetNumEdgeWeights() > 0)
                    {
                        int[] vEdgeValues = vEdgeBuffer;
                        fvarChannel.getVertexEdgeValues(vert, vEdgeValues);

                        for (int i = 0; i < vEdges.size(); ++i)
                        {
                            dst[vValue].AddWithWeight(src[vEdgeValues[i]], weightBuffer[eWeights + i]);
                        }
                    }
                    dst[vValue].AddWithWeight(src[vValue], weightBuffer[vWeights + 0]);
                }
                else
                {
                    //
                    //  Sibling FVar values associated with a vertex will be either a corner or a crease:
                    //
                    for (int i = 0; i < vValues.size(); ++i)
                    {
                        int vValue = vValues[i];

                        dst[vValue].Clear();
                        if (fvarChannel.getValueTag(vValue).isCorner())
                        {
                            dst[vValue].AddWithWeight(src[vValue], Cf(1.0f));
                        }
                        else
                        {
                            int[] vEndValues = new int[2];
                            fvarChannel.getVertexCreaseEndValues(vert, (ushort)i, vEndValues);

                            dst[vValue].AddWithWeight(src[vEndValues[0]], Cf(1.0f / 6.0f));
                            dst[vValue].AddWithWeight(src[vEndValues[1]], Cf(1.0f / 6.0f));
                            dst[vValue].AddWithWeight(src[vValue], Cf(2.0f / 3.0f));
                        }
                    }
                }
            }
        }

        private readonly TopologyRefiner _refiner;

        //
        //  Local class to fulfill interface for <typename MASK> in the Scheme mask queries.
        //  The C++ Weight* pointers are (REAL[] array, int offset) pairs.
        //
        private class Mask : IMask<REAL>
        {
            public Mask(REAL[] v, int vOffset, REAL[] e, int eOffset, REAL[] f, int fOffset)
            {
                _vertWeights = v;
                _vertOffset = vOffset;
                _edgeWeights = e;
                _edgeOffset = eOffset;
                _faceWeights = f;
                _faceOffset = fOffset;
                _vertCount = 0;
                _edgeCount = 0;
                _faceCount = 0;
                _faceWeightsForFaceCenters = false;
            }

            //  Generic interface expected of <typename MASK>:
            public int GetNumVertexWeights() { return _vertCount; }
            public int GetNumEdgeWeights() { return _edgeCount; }
            public int GetNumFaceWeights() { return _faceCount; }

            public void SetNumVertexWeights(int count) { _vertCount = count; }
            public void SetNumEdgeWeights(int count) { _edgeCount = count; }
            public void SetNumFaceWeights(int count) { _faceCount = count; }

            public ref REAL VertexWeight(int index) { return ref _vertWeights[_vertOffset + index]; }
            public ref REAL EdgeWeight(int index) { return ref _edgeWeights[_edgeOffset + index]; }
            public ref REAL FaceWeight(int index) { return ref _faceWeights[_faceOffset + index]; }

            public bool AreFaceWeightsForFaceCenters() { return _faceWeightsForFaceCenters; }
            public void SetFaceWeightsForFaceCenters(bool on) { _faceWeightsForFaceCenters = on; }

            private REAL[] _vertWeights;
            private int _vertOffset;
            private REAL[] _edgeWeights;
            private int _edgeOffset;
            private REAL[] _faceWeights;
            private int _faceOffset;

            private int _vertCount;
            private int _edgeCount;
            private int _faceCount;

            private bool _faceWeightsForFaceCenters;
        }
    }

    public class PrimvarRefiner : PrimvarRefinerReal<float>
    {
        public PrimvarRefiner(TopologyRefiner refiner)
            : base(refiner)
        {
        }
    }

    ///  Double precision counterpart (PrimvarRefinerReal<double>)
    public class PrimvarRefinerD : PrimvarRefinerReal<double>
    {
        public PrimvarRefinerD(TopologyRefiner refiner)
            : base(refiner)
        {
        }
    }
}
