//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 sdc/crease.h and sdc/crease.cpp
//
//  Note: C++ "float const*" parameters are float[] (always starting at index 0), and
//  may be null where the C++ passes a null pointer.
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Sdc
{
    public class Crease
    {
        public const float SHARPNESS_SMOOTH   =  0.0f;
        public const float SHARPNESS_INFINITE = 10.0f;

        public static bool IsSmooth(float sharpness)
        {
            return sharpness <= SHARPNESS_SMOOTH;
        }

        public static bool IsSharp(float sharpness)
        {
            return sharpness > SHARPNESS_SMOOTH;
        }

        public static bool IsInfinite(float sharpness)
        {
            return sharpness >= SHARPNESS_INFINITE;
        }

        public static bool IsSemiSharp(float sharpness)
        {
            return (SHARPNESS_SMOOTH < sharpness) && (sharpness < SHARPNESS_INFINITE);
        }

        public enum Rule
        {
            RULE_UNKNOWN = 0,
            RULE_SMOOTH  = (1 << 0),
            RULE_DART    = (1 << 1),
            RULE_CREASE  = (1 << 2),
            RULE_CORNER  = (1 << 3)
        }

        //  C++ style access to the nested enumerants as Crease.RULE_SMOOTH etc.:
        public const Rule RULE_UNKNOWN = Rule.RULE_UNKNOWN;
        public const Rule RULE_SMOOTH  = Rule.RULE_SMOOTH;
        public const Rule RULE_DART    = Rule.RULE_DART;
        public const Rule RULE_CREASE  = Rule.RULE_CREASE;
        public const Rule RULE_CORNER  = Rule.RULE_CORNER;

        private Options _options;

        public Crease()
        {
            _options = new Options();
        }

        public Crease(Options options)
        {
            _options = options;
        }

        public bool IsUniform()
        {
            return _options.GetCreasingMethod() == Options.CreasingMethod.CREASE_UNIFORM;
        }

        //
        //  Sharpness queries for boundary components:
        //
        public float SharpenBoundaryEdge(float edgeSharpness)
        {
            return SHARPNESS_INFINITE;
        }

        public float SharpenBoundaryVertex(float vertexSharpness)
        {
            return (_options.GetVtxBoundaryInterpolation() == Options.VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_AND_CORNER) ?
                    SHARPNESS_INFINITE : vertexSharpness;
        }

        protected float decrementSharpness(float sharpness)
        {
            if (IsSmooth(sharpness))
            {
                return Crease.SHARPNESS_SMOOTH;  // redundant but most common
            }
            if (IsInfinite(sharpness))
            {
                return Crease.SHARPNESS_INFINITE;
            }
            if (sharpness > 1.0f)
            {
                return (sharpness - 1.0f);
            }
            return Crease.SHARPNESS_SMOOTH;
        }

        public float SubdivideUniformSharpness(float vertexOrEdgeSharpness)
        {
            return decrementSharpness(vertexOrEdgeSharpness);
        }

        public float SubdivideVertexSharpness(float vertexSharpness)
        {
            return decrementSharpness(vertexSharpness);
        }

        public void GetSharpEdgePairOfCrease(float[] incidentEdgeSharpness, int incidentEdgeCount,
                                             int[] sharpEdgePair)
        {
            sharpEdgePair[0] = 0;
            while (IsSmooth(incidentEdgeSharpness[sharpEdgePair[0]]))
            {
                ++sharpEdgePair[0];
            }

            sharpEdgePair[1] = incidentEdgeCount - 1;
            while (IsSmooth(incidentEdgeSharpness[sharpEdgePair[1]]))
            {
                --sharpEdgePair[1];
            }
        }

        //
        //  Creasing queries dependent on sharpness values:
        //
        public Rule DetermineVertexVertexRule(float vertexSharpness, int sharpEdgeCount)
        {
            if (IsSharp(vertexSharpness))
            {
                return Crease.RULE_CORNER;
            }

            return (sharpEdgeCount > 2) ? Crease.RULE_CORNER : (Crease.Rule)(1 << sharpEdgeCount);
        }

        public Rule DetermineVertexVertexRule(float vertexSharpness,
                                              int incidentEdgeCount,
                                              float[] incidentEdgeSharpness)
        {
            if (IsSharp(vertexSharpness))
            {
                return Crease.RULE_CORNER;
            }

            int sharpEdgeCount = 0;
            for (int i = 0; i < incidentEdgeCount; ++i)
            {
                sharpEdgeCount += IsSharp(incidentEdgeSharpness[i]) ? 1 : 0;
            }
            return (sharpEdgeCount > 2) ? Crease.RULE_CORNER : (Crease.Rule)(1 << sharpEdgeCount);
        }

        public float ComputeFractionalWeightAtVertex(float parentVertexSharpness,
                                                     float childVertexSharpness,
                                                     int incidentEdgeCount,
                                                     float[] parentSharpness,
                                                     float[] childSharpness)
        {
            int   transitionCount = 0;
            float transitionSum   = 0.0f;

            if (IsSharp(parentVertexSharpness) && IsSmooth(childVertexSharpness))
            {
                transitionCount = 1;
                transitionSum   = parentVertexSharpness;
            }

            //
            //  We need the child-edge sharpness values for non-simple methods to ensure
            //  that the sharpness went from a non-zero value (potentially greater than
            //  1.0) to zero...
            //
            if (IsUniform() || (childSharpness == null))
            {
                for (int i = 0; i < incidentEdgeCount; ++i)
                {
                    if (IsSharp(parentSharpness[i]) && (parentSharpness[i] <= 1.0f))
                    {
                        transitionSum   += parentSharpness[i];
                        transitionCount ++;
                    }
                }
            }
            else
            {
                for (int i = 0; i < incidentEdgeCount; ++i)
                {
                    if (IsSharp(parentSharpness[i]) && IsSmooth(childSharpness[i]))
                    {
                        transitionSum   += parentSharpness[i];
                        transitionCount ++;
                    }
                }
            }
            if (transitionCount == 0)
            {
                return 0.0f;
            }
            float fractionalWeight = transitionSum / (float)transitionCount;
            return (fractionalWeight > 1.0f) ? 1.0f : fractionalWeight;
        }

        //
        //  Subdividing edge sharpness values (vertex sharpness is inline):
        //
        public float SubdivideEdgeSharpnessAtVertex(float edgeSharpness,
                                                    int incEdgeCountAtVertex,
                                                    float[] incEdgeSharpness)
        {
            if (IsUniform() || (incEdgeCountAtVertex < 2))
            {
                return decrementSharpness(edgeSharpness);
            }

            if (IsSmooth(edgeSharpness))
            {
                return Crease.SHARPNESS_SMOOTH;
            }
            if (IsInfinite(edgeSharpness))
            {
                return Crease.SHARPNESS_INFINITE;
            }

            float sharpSum   = 0.0f;
            int   sharpCount = 0;
            for (int i = 0; i < incEdgeCountAtVertex; ++i)
            {
                if (IsSemiSharp(incEdgeSharpness[i]))
                {
                    sharpCount ++;
                    sharpSum += incEdgeSharpness[i];
                }
            }
            if (sharpCount > 1)
            {
                //  Chaikin rule is 3/4 original sharpness + 1/4 average of the others

                float avgSharpnessAtVertex = (sharpSum - edgeSharpness) / (float)(sharpCount - 1);

                edgeSharpness = (0.75f * edgeSharpness) + (0.25f * avgSharpnessAtVertex);
            }
            edgeSharpness -= 1.0f;
            return IsSharp(edgeSharpness) ? edgeSharpness : Crease.SHARPNESS_SMOOTH;
        }

        public void SubdivideEdgeSharpnessesAroundVertex(int edgeCount,
                                                         float[] parentSharpness,
                                                         float[] childSharpness)
        {
            if (IsUniform() || (edgeCount < 2))
            {
                for (int i = 0; i < edgeCount; ++i)
                {
                    childSharpness[i] = decrementSharpness(parentSharpness[i]);
                }
                return;
            }

            //
            //  Chaikin creasing is most efficiently computed for all edges around a vertex at
            //  once as the subdivided value for each creased edge depends on the average of
            //  the other edges around the vertex.  So we can sum up the sharpness around the
            //  vertex once and use that for each edge, rather than iterating around the vertex
            //  for each incident edge.
            //
            if (_options.GetCreasingMethod() == Options.CreasingMethod.CREASE_CHAIKIN)
            {
                float sharpSum   = 0.0f;
                int   sharpCount = 0;
                for (int i = 0; i < edgeCount; ++i)
                {
                    if (IsSemiSharp(parentSharpness[i]))
                    {
                        sharpCount ++;
                        sharpSum += parentSharpness[i];
                    }
                }

                //
                //  The smooth case is most common -- specialize for it first:
                //
                if (sharpCount == 0)
                {
                    for (int i = 0; i < edgeCount; ++i)
                    {
                        childSharpness[i] = parentSharpness[i];
                    }
                }
                else
                {
                    for (int i = 0; i < edgeCount; ++i)
                    {
                        float pSharp = parentSharpness[i];

                        if (IsSmooth(pSharp))
                        {
                            childSharpness[i] = Crease.SHARPNESS_SMOOTH;
                        }
                        else if (IsInfinite(pSharp))
                        {
                            childSharpness[i] = Crease.SHARPNESS_INFINITE;
                        }
                        else if (sharpCount == 1)
                        {
                            //  Need special case here anyway to avoid divide by zero below...
                            childSharpness[i] = decrementSharpness(pSharp);
                        }
                        else
                        {
                            float pOtherAverage = (sharpSum - pSharp) / (float)(sharpCount - 1);

                            //  Chaikin rule is 3/4 original sharpness + 1/4 average of the others
                            float cSharp = ((0.75f * pSharp) + (0.25f * pOtherAverage)) - 1.0f;
                            if (IsSmooth(cSharp))
                            {
                                cSharp = Crease.SHARPNESS_SMOOTH;
                            }
                            childSharpness[i] = cSharp;
                        }
                    }
                }
            }
        }
    }
}
