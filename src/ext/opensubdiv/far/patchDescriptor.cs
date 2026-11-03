//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far
{
    /// \brief Describes the type of a patch
    ///
    /// Uniquely identifies all the different types of patches
    ///
    public struct PatchDescriptor
    {
        public enum Type
        {
            NON_PATCH = 0,     ///< undefined

            POINTS,            ///< points (useful for cage drawing)
            LINES,             ///< lines  (useful for cage drawing)

            QUADS,             ///< 4-sided quadrilateral (bilinear)
            TRIANGLES,         ///< 3-sided triangle

            LOOP,              ///< regular triangular patch for the Loop scheme

            REGULAR,           ///< regular B-Spline patch for the Catmark scheme
            GREGORY,
            GREGORY_BOUNDARY,
            GREGORY_BASIS,
            GREGORY_TRIANGLE
        }

        /// \brief Constructor
        public PatchDescriptor(int type)
        {
            _type = (uint)type;
        }

        /// \brief Constructor
        public PatchDescriptor(Type type)
        {
            _type = (uint)type;
        }

        /// \brief Returns the type of the patch
        public Type GetType()
        {
            return (Type)_type;
        }

        /// \brief Returns true if the type is an adaptive (non-linear) patch
        public static bool IsAdaptive(Type type)
        {
            return type > Type.TRIANGLES;
        }

        /// \brief Returns true if the type is an adaptive patch
        public bool IsAdaptive()
        {
            return IsAdaptive(this.GetType());
        }

        /// \brief Returns the number of control vertices expected for a patch of the
        /// type described
        public static short GetNumControlVertices(Type type)
        {
            switch (type)
            {
                case Type.REGULAR: return GetRegularPatchSize();
                case Type.LOOP: return 12;
                case Type.QUADS: return 4;
                case Type.GREGORY:
                case Type.GREGORY_BOUNDARY: return GetGregoryPatchSize();
                case Type.GREGORY_BASIS: return GetGregoryBasisPatchSize();
                case Type.GREGORY_TRIANGLE: return 18;
                case Type.TRIANGLES: return 3;
                case Type.LINES: return 2;
                case Type.POINTS: return 1;
                default: return -1;
            }
        }

        /// \brief Deprecated @see PatchDescriptor#GetNumControlVertices
        public static short GetNumFVarControlVertices(Type type)
        {
            return PatchDescriptor.GetNumControlVertices(type);
        }

        /// \brief Returns the number of control vertices expected for a patch of the
        /// type described
        public short GetNumControlVertices()
        {
            return GetNumControlVertices(this.GetType());
        }

        /// \brief Deprecated @see PatchDescriptor#GetNumControlVertices
        public short GetNumFVarControlVertices()
        {
            return GetNumFVarControlVertices(this.GetType());
        }

        /// \brief Number of control vertices of Regular Patches in table.
        public static short GetRegularPatchSize()
        {
            return 16;
        }

        /// \brief Number of control vertices of Gregory (and Gregory Boundary) Patches in table.
        public static short GetGregoryPatchSize()
        {
            return 4;
        }

        /// \brief Number of control vertices of Gregory patch basis (20)
        public static short GetGregoryBasisPatchSize()
        {
            return 20;
        }

        // it is possible for linear patches to represent irregularities for both
        // Catmark and Loop, and the Bilinear scheme is adaptively refined into
        // linear quads (e.g. a pentagon becoming five quads).
        //
        private static readonly PatchDescriptor[] _loopDescriptors = new PatchDescriptor[]
        {
            new PatchDescriptor(Type.LOOP),
            new PatchDescriptor(Type.GREGORY_TRIANGLE),
        };

        private static readonly PatchDescriptor[] _catmarkDescriptors = new PatchDescriptor[]
        {
            new PatchDescriptor(Type.REGULAR),
            new PatchDescriptor(Type.GREGORY),
            new PatchDescriptor(Type.GREGORY_BOUNDARY),
            new PatchDescriptor(Type.GREGORY_BASIS),
        };

        /// \brief Returns a vector of all the legal patch descriptors for the
        ///        given adaptive subdivision scheme
        public static ConstArray<PatchDescriptor> GetAdaptivePatchDescriptors(SchemeType type)
        {
            switch (type)
            {
                case SchemeType.SCHEME_BILINEAR:
                    return new ConstArray<PatchDescriptor>(null, 0, 0);
                case SchemeType.SCHEME_CATMARK:
                    return new ConstArray<PatchDescriptor>(_catmarkDescriptors,
                        0, _catmarkDescriptors.Length);
                case SchemeType.SCHEME_LOOP:
                    return new ConstArray<PatchDescriptor>(_loopDescriptors,
                        0, _loopDescriptors.Length);
                default:
                    System.Diagnostics.Debug.Assert(false);
                    break;
            }
            return new ConstArray<PatchDescriptor>(null, 0, 0);
        }

        /// \brief Allows ordering of patches by type
        public static bool operator <(PatchDescriptor lhs, PatchDescriptor other)
        {
            return (lhs._type < other._type);
        }

        public static bool operator >(PatchDescriptor lhs, PatchDescriptor other)
        {
            return (lhs._type > other._type);
        }

        /// \brief True if the descriptors are identical
        public static bool operator ==(PatchDescriptor lhs, PatchDescriptor other)
        {
            return lhs._type == other._type;
        }

        public static bool operator !=(PatchDescriptor lhs, PatchDescriptor other)
        {
            return lhs._type != other._type;
        }

        public override bool Equals(object obj)
        {
            return obj is PatchDescriptor other && this == other;
        }

        public override int GetHashCode()
        {
            return (int)_type;
        }

        // debug helper
        public void print()
        {
            string[] types = new string[13]
            {
                "NON_PATCH", "POINTS", "LINES", "QUADS", "TRIANGLES", "LOOP",
                "REGULAR", "GREGORY", "GREGORY_BOUNDARY", "GREGORY_BASIS",
                "GREGORY_TRIANGLE", null, null
            };

            Console.Write("    type {0}\n", types[_type]);
        }

        private uint _type;
    }
}
