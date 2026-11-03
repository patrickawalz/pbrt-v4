//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far
{
    //
    //  Typedefs for indices that are inherited from the Vtr level -- eventually
    //  these primitive Vtr types may be declared at a lower, more public level.
    //
    //  In C#: Index is plain int, LocalIndex is ushort, and IndexArray etc. are
    //  OpenSubdiv.Vtr.Array<int> / ConstArray<int> / Array<ushort> / ConstArray<ushort>
    //  (declare per file with using aliases).  The free functions and constants of
    //  namespace Far live in this static class.
    //
    public static class FarTypes
    {
        public static bool IndexIsValid(int index)
        {
            return index != INDEX_INVALID;
        }

        public const int INDEX_INVALID = -1;
        public const int VALENCE_LIMIT = 65535;
    }
}
