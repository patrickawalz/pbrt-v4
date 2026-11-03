//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/options.h
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Sdc
{
    ///
    ///  Options: a light-weight set of bit-field-like options (stored as bytes) that
    ///  define the shape of a particular limit surface.  Passed around by value (struct).
    ///
    public struct Options
    {
        public enum VtxBoundaryInterpolation
        {
            VTX_BOUNDARY_NONE = 0,        ///< no boundary interpolation, except where boundary edges were explicitly sharpened
            VTX_BOUNDARY_EDGE_ONLY,       ///< all boundary edges sharpened and interpolated
            VTX_BOUNDARY_EDGE_AND_CORNER  ///< all boundary edges and corner vertices sharpened and interpolated
        }

        public enum FVarLinearInterpolation
        {
            FVAR_LINEAR_NONE = 0,         ///< smooth everywhere ("edge only")
            FVAR_LINEAR_CORNERS_ONLY,     ///< sharpen corners only
            FVAR_LINEAR_CORNERS_PLUS1,    ///< ("edge corner")
            FVAR_LINEAR_CORNERS_PLUS2,    ///< ("edge and corner + propagate corner")
            FVAR_LINEAR_BOUNDARIES,       ///< sharpen all boundaries ("always sharp")
            FVAR_LINEAR_ALL               ///< bilinear interpolation ("bilinear")
        }

        public enum CreasingMethod
        {
            CREASE_UNIFORM = 0,           ///< Catmark rule
            CREASE_CHAIKIN                ///< Chaikin rule
        }

        public enum TriangleSubdivision
        {
            TRI_SUB_CATMARK = 0,          ///< Catmark weights (Catmark scheme only)
            TRI_SUB_SMOOTH                ///< "smooth triangle" weights (Catmark scheme only)
        }

        //  C++ style access to the nested enumerants as Options.VTX_BOUNDARY_NONE etc.:
        public const VtxBoundaryInterpolation VTX_BOUNDARY_NONE = VtxBoundaryInterpolation.VTX_BOUNDARY_NONE;
        public const VtxBoundaryInterpolation VTX_BOUNDARY_EDGE_ONLY = VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_ONLY;
        public const VtxBoundaryInterpolation VTX_BOUNDARY_EDGE_AND_CORNER = VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_AND_CORNER;

        public const FVarLinearInterpolation FVAR_LINEAR_NONE = FVarLinearInterpolation.FVAR_LINEAR_NONE;
        public const FVarLinearInterpolation FVAR_LINEAR_CORNERS_ONLY = FVarLinearInterpolation.FVAR_LINEAR_CORNERS_ONLY;
        public const FVarLinearInterpolation FVAR_LINEAR_CORNERS_PLUS1 = FVarLinearInterpolation.FVAR_LINEAR_CORNERS_PLUS1;
        public const FVarLinearInterpolation FVAR_LINEAR_CORNERS_PLUS2 = FVarLinearInterpolation.FVAR_LINEAR_CORNERS_PLUS2;
        public const FVarLinearInterpolation FVAR_LINEAR_BOUNDARIES = FVarLinearInterpolation.FVAR_LINEAR_BOUNDARIES;
        public const FVarLinearInterpolation FVAR_LINEAR_ALL = FVarLinearInterpolation.FVAR_LINEAR_ALL;

        public const CreasingMethod CREASE_UNIFORM = CreasingMethod.CREASE_UNIFORM;
        public const CreasingMethod CREASE_CHAIKIN = CreasingMethod.CREASE_CHAIKIN;

        public const TriangleSubdivision TRI_SUB_CATMARK = TriangleSubdivision.TRI_SUB_CATMARK;
        public const TriangleSubdivision TRI_SUB_SMOOTH = TriangleSubdivision.TRI_SUB_SMOOTH;

        //
        //  Use a small integer type to pack these rather than bitfields.  The C++ default
        //  constructor assigns non-zero defaults (_fvarLinInterp = FVAR_LINEAR_ALL), so the
        //  fields are stored such that a default(Options) struct reproduces them:  the
        //  fvarLinInterp is stored XOR'd with FVAR_LINEAR_ALL.
        //
        private byte _vtxBoundInterp;
        private byte _fvarLinInterpX;
        private byte _creasingMethod;
        private byte _triangleSub;

        private const byte FVAR_DEFAULT_X = (byte)FVarLinearInterpolation.FVAR_LINEAR_ALL;

        /// Construct with the C++ default values (use this instead of default(Options)).
        public static Options Create()
        {
            Options o = new Options();
            o._vtxBoundInterp = (byte)VtxBoundaryInterpolation.VTX_BOUNDARY_NONE;
            o._fvarLinInterpX = (byte)((byte)FVarLinearInterpolation.FVAR_LINEAR_ALL ^ FVAR_DEFAULT_X);
            o._creasingMethod = (byte)CreasingMethod.CREASE_UNIFORM;
            o._triangleSub = (byte)TriangleSubdivision.TRI_SUB_CATMARK;
            return o;
        }

        //
        //  Trivial get/set methods:
        //

        /// Get vertex boundary interpolation rule
        public VtxBoundaryInterpolation GetVtxBoundaryInterpolation()
        {
            return (VtxBoundaryInterpolation)_vtxBoundInterp;
        }

        /// Set vertex boundary interpolation rule
        public void SetVtxBoundaryInterpolation(VtxBoundaryInterpolation b)
        {
            _vtxBoundInterp = (byte)b;
        }

        /// Get face-varying interpolation rule
        public FVarLinearInterpolation GetFVarLinearInterpolation()
        {
            return (FVarLinearInterpolation)(_fvarLinInterpX ^ FVAR_DEFAULT_X);
        }

        /// Set face-varying interpolation rule
        public void SetFVarLinearInterpolation(FVarLinearInterpolation b)
        {
            _fvarLinInterpX = (byte)((byte)b ^ FVAR_DEFAULT_X);
        }

        /// Get edge crease rule
        public CreasingMethod GetCreasingMethod()
        {
            return (CreasingMethod)_creasingMethod;
        }

        /// Set edge crease rule
        public void SetCreasingMethod(CreasingMethod c)
        {
            _creasingMethod = (byte)c;
        }

        /// Get triangle subdivision weights rule (Catmark scheme only !)
        public TriangleSubdivision GetTriangleSubdivision()
        {
            return (TriangleSubdivision)_triangleSub;
        }

        /// Set triangle subdivision weights rule (Catmark scheme only !)
        public void SetTriangleSubdivision(TriangleSubdivision t)
        {
            _triangleSub = (byte)t;
        }
    }
}
