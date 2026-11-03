//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far
{
    /// \brief Patch parameterization (see the C++ header for the full description
    /// of the bitfield layout).
    ///
    ///  Field0     | Bits | Content
    ///  faceId     | 28   | the faceId of the patch
    ///  transition | 4    | transition edge mask encoding
    ///
    ///  Field1     | Bits | Content
    ///  level      | 4    | the subdivision level of the patch
    ///  nonquad    | 1    | whether patch is refined from a non-quad face
    ///  regular    | 1    | whether patch is regular
    ///  unused     | 1    | unused
    ///  boundary   | 5    | boundary edge mask encoding
    ///  v          | 10   | log2 value of u parameter at first patch corner
    ///  u          | 10   | log2 value of v parameter at first patch corner
    ///
    public struct PatchParam
    {
        /// \brief Sets the values of the bit fields
        public void Set(int faceid, short u, short v,
                        ushort depth, bool nonquad,
                        ushort boundary, ushort transition,
                        bool regular = false)
        {
            field0 = pack((uint)faceid, 28, 0) |
                     pack(transition, 4, 28);

            field1 = pack((uint)u, 10, 22) |
                     pack((uint)v, 10, 12) |
                     pack(boundary, 5, 7) |
                     pack(regular ? 1u : 0u, 1, 5) |
                     pack(nonquad ? 1u : 0u, 1, 4) |
                     pack(depth, 4, 0);
        }

        /// \brief Resets everything to 0
        public void Clear()
        {
            field0 = field1 = 0;
        }

        /// \brief Returns the faceid
        public int GetFaceId()
        {
            return (int)unpack(field0, 28, 0);
        }

        /// \brief Returns the log2 value of the u parameter at
        /// the first corner of the patch
        public ushort GetU()
        {
            return (ushort)unpack(field1, 10, 22);
        }

        /// \brief Returns the log2 value of the v parameter at
        /// the first corner of the patch
        public ushort GetV()
        {
            return (ushort)unpack(field1, 10, 12);
        }

        /// \brief Returns the transition edge encoding for the patch.
        public ushort GetTransition()
        {
            return (ushort)unpack(field0, 4, 28);
        }

        /// \brief Returns the boundary edge encoding for the patch.
        public ushort GetBoundary()
        {
            return (ushort)unpack(field1, 5, 7);
        }

        /// \brief True if the parent base face is a non-quad
        public bool NonQuadRoot()
        {
            return (unpack(field1, 1, 4) != 0);
        }

        /// \brief Returns the level of subdivision of the patch
        public ushort GetDepth()
        {
            return (ushort)unpack(field1, 4, 0);
        }

        /// \brief Returns the fraction of unit parametric space covered by this face.
        public float GetParamFraction()
        {
            return 1.0f / (float)(1 << (GetDepth() - (NonQuadRoot() ? 1 : 0)));
        }

        /// \brief A (u,v) pair in the fraction of parametric space covered by this
        /// face is mapped into a normalized parametric space.
        public void Normalize<REAL>(ref REAL u, ref REAL v) where REAL : unmanaged, INumber<REAL>
        {
            REAL fracInv = REAL.CreateChecked(1.0f / GetParamFraction());

            u = u * fracInv - REAL.CreateChecked(GetU());
            v = v * fracInv - REAL.CreateChecked(GetV());
        }

        /// \brief A (u,v) pair in a normalized parametric space is mapped back into the
        /// fraction of parametric space covered by this face.
        public void Unnormalize<REAL>(ref REAL u, ref REAL v) where REAL : unmanaged, INumber<REAL>
        {
            REAL frac = REAL.CreateChecked(GetParamFraction());

            u = (u + REAL.CreateChecked(GetU())) * frac;
            v = (v + REAL.CreateChecked(GetV())) * frac;
        }

        /// \brief Returns if a triangular patch is parametrically rotated 180 degrees
        public bool IsTriangleRotated()
        {
            return (GetU() + GetV()) >= (1 << GetDepth());
        }

        public void NormalizeTriangle<REAL>(ref REAL u, ref REAL v) where REAL : unmanaged, INumber<REAL>
        {
            if (IsTriangleRotated())
            {
                REAL fracInv = REAL.CreateChecked(1.0f / GetParamFraction());

                int depthFactor = 1 << GetDepth();
                u = REAL.CreateChecked(depthFactor - GetU()) - (u * fracInv);
                v = REAL.CreateChecked(depthFactor - GetV()) - (v * fracInv);
            }
            else
            {
                Normalize(ref u, ref v);
            }
        }

        public void UnnormalizeTriangle<REAL>(ref REAL u, ref REAL v) where REAL : unmanaged, INumber<REAL>
        {
            if (IsTriangleRotated())
            {
                REAL frac = REAL.CreateChecked(GetParamFraction());

                int depthFactor = 1 << GetDepth();
                u = (REAL.CreateChecked(depthFactor - GetU()) - u) * frac;
                v = (REAL.CreateChecked(depthFactor - GetV()) - v) * frac;
            }
            else
            {
                Unnormalize(ref u, ref v);
            }
        }

        /// \brief Returns whether the patch is regular
        public bool IsRegular()
        {
            return (unpack(field1, 1, 5) != 0);
        }

        public uint field0;
        public uint field1;

        private static uint pack(uint value, int width, int offset)
        {
            return (uint)((value & (uint)((1 << width) - 1)) << offset);
        }

        private static uint unpack(uint value, int width, int offset)
        {
            return (uint)((value >> offset) & (uint)((1 << width) - 1));
        }
    }
}
