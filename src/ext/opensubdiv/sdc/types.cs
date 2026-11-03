//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/types.h and sdc/typeTraits.cpp
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Sdc
{
    ///
    ///  Enumerated type for all subdivision schemes supported by OpenSubdiv
    ///
    public enum SchemeType
    {
        SCHEME_BILINEAR,
        SCHEME_CATMARK,
        SCHEME_LOOP
    }

    ///
    ///  Enumerated type for all face splitting schemes
    ///
    public enum Split
    {
        SPLIT_TO_QUADS,  ///< Used by Catmark and Bilinear
        SPLIT_TO_TRIS,   ///< Used by Loop
        SPLIT_HYBRID     ///< Not currently used (potential future extension)
    }

    ///
    ///  Traits associated with the types of all subdivision schemes -- parameterized by
    ///  the scheme type.  All traits are also defined in the scheme itself.
    ///  (Includes the static table from typeTraits.cpp.)
    ///
    public static class SchemeTypeTraits
    {
        public struct TraitsEntry
        {
            public string _name;

            public Split _splitType;
            public int   _regularFaceSize;
            public int   _regularVertexValence;
            public int   _localNeighborhood;
        }

        public static readonly TraitsEntry[] staticTraitsTable = new TraitsEntry[3]
        {
            new TraitsEntry
            {
                _name = "bilinear",
                _splitType = Scheme.GetTopologicalSplitType(SchemeType.SCHEME_BILINEAR),
                _regularFaceSize = Scheme.GetRegularFaceSize(SchemeType.SCHEME_BILINEAR),
                _regularVertexValence = Scheme.GetRegularVertexValence(SchemeType.SCHEME_BILINEAR),
                _localNeighborhood = Scheme.GetLocalNeighborhoodSize(SchemeType.SCHEME_BILINEAR)
            },
            new TraitsEntry
            {
                _name = "catmark",
                _splitType = Scheme.GetTopologicalSplitType(SchemeType.SCHEME_CATMARK),
                _regularFaceSize = Scheme.GetRegularFaceSize(SchemeType.SCHEME_CATMARK),
                _regularVertexValence = Scheme.GetRegularVertexValence(SchemeType.SCHEME_CATMARK),
                _localNeighborhood = Scheme.GetLocalNeighborhoodSize(SchemeType.SCHEME_CATMARK)
            },
            new TraitsEntry
            {
                _name = "loop",
                _splitType = Scheme.GetTopologicalSplitType(SchemeType.SCHEME_LOOP),
                _regularFaceSize = Scheme.GetRegularFaceSize(SchemeType.SCHEME_LOOP),
                _regularVertexValence = Scheme.GetRegularVertexValence(SchemeType.SCHEME_LOOP),
                _localNeighborhood = Scheme.GetLocalNeighborhoodSize(SchemeType.SCHEME_LOOP)
            }
        };

        public static SchemeType GetType(SchemeType schemeType)
        {
            return schemeType;
        }

        public static Split GetTopologicalSplitType(SchemeType schemeType)
        {
            return staticTraitsTable[(int)schemeType]._splitType;
        }

        public static int GetRegularFaceSize(SchemeType schemeType)
        {
            return staticTraitsTable[(int)schemeType]._regularFaceSize;
        }

        public static int GetRegularVertexValence(SchemeType schemeType)
        {
            return staticTraitsTable[(int)schemeType]._regularVertexValence;
        }

        public static int GetLocalNeighborhoodSize(SchemeType schemeType)
        {
            return staticTraitsTable[(int)schemeType]._localNeighborhood;
        }

        public static string GetName(SchemeType schemeType)
        {
            return staticTraitsTable[(int)schemeType]._name;
        }
    }
}
