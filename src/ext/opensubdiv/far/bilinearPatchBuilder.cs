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
    //  BilinearPatchBuilder
    //
    //  Required virtual methods are included, along with any customizations
    //  local to their implementation.
    //
    public class BilinearPatchBuilder : PatchBuilder
    {
        private static readonly PatchDescriptor.Type[] patchTypeFromBasisArray = new PatchDescriptor.Type[]
        {
            PatchDescriptor.Type.NON_PATCH,      // undefined
            PatchDescriptor.Type.QUADS,          // regular
            PatchDescriptor.Type.GREGORY_BASIS,  // Gregory
            PatchDescriptor.Type.QUADS,          // linear
            PatchDescriptor.Type.NON_PATCH       // Bezier -- for future use
        };

        public BilinearPatchBuilder(TopologyRefiner refiner, Options options) :
            base(refiner, options)
        {
            _regPatchType = patchTypeFromBasisArray[(int)_options.regBasisType];
            _irregPatchType = (_options.irregBasisType == BasisType.BASIS_UNSPECIFIED)
                            ? _regPatchType
                            : patchTypeFromBasisArray[(int)_options.irregBasisType];

            _nativePatchType = PatchDescriptor.Type.QUADS;
            _linearPatchType = PatchDescriptor.Type.QUADS;
        }

        protected override PatchDescriptor.Type patchTypeFromBasis(BasisType basis)
        {
            return patchTypeFromBasisArray[(int)basis];
        }

        private int convertSourcePatch<REAL>(SourcePatch sourcePatch,
                                             PatchDescriptor.Type patchType,
                                             SparseMatrix<REAL> matrix) where REAL : unmanaged, IFloatingPointIeee754<REAL>
        {
            System.Diagnostics.Debug.Assert(false, "Conversion from Bilinear patches to other bases not yet supported");

            if (sourcePatch.GetNumSourcePoints() == 0)
            {
                return -1;
            }
            if (patchType == PatchDescriptor.Type.NON_PATCH)
            {
                return -1;
            }
            if (matrix.GetNumRows() <= 0)
            {
                return -1;
            }
            return -1;
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
    }
}
