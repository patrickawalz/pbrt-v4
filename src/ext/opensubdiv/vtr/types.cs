//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/types.h
//
//  Index is plain int; LocalIndex is ushort.  The typedefs are expressed per file as, e.g.,
//      using IndexArray = OpenSubdiv.Vtr.Array<int>;
//      using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
//      using LocalIndexArray = OpenSubdiv.Vtr.Array<ushort>;
//      using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;
//      using IndexVector = OpenSubdiv.Vtr.StdVector<int>;
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Vtr
{
    public static class Types
    {
        //  typedef int Index;
        public const int INDEX_INVALID = -1;

        public static bool IndexIsValid(int index)
        {
            return (index != INDEX_INVALID);
        }

        //  typedef unsigned short LocalIndex;

        //  Declared as "int" since it's intended for more general use
        public const int VALENCE_LIMIT = ushort.MaxValue;
    }
}
