//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;
using IndexArray = OpenSubdiv.Vtr.Array<int>;

namespace OpenSubdiv.Far
{
    /// \brief  A simple reference to raw topology data for use with TopologyRefinerFactory
    ///
    /// TopologyDescriptor is a simple struct containing references to raw topology data used
    /// to construct a TopologyRefiner.  It is not a requirement but a convenience for use
    /// with TopologyRefinerFactory when mesh topology is not available in an existing mesh
    /// data structure.  It should be functionally complete and simple to use, but for more
    /// demanding situations, writing a custom Factory is usually warranted.
    ///
    /// In C# the raw pointers become arrays (null for a null pointer).
    ///
    public class TopologyDescriptor : ITopologyRefinerFactoryMesh<TopologyDescriptor>
    {
        public int numVertices;
        public int numFaces;

        public int[] numVertsPerFace;
        public int[] vertIndicesPerFace;

        public int numCreases;
        public int[] creaseVertexIndexPairs;
        public float[] creaseWeights;

        public int numCorners;
        public int[] cornerVertexIndices;
        public float[] cornerWeights;

        public int numHoles;
        public int[] holeIndices;

        public bool isLeftHanded;

        //  Face-varying data channel -- value indices correspond to vertex indices,
        //  i.e. one for every vertex of every face:
        //
        public struct FVarChannel
        {
            public int numValues;
            public int[] valueIndices;
        }

        public int numFVarChannels;
        public FVarChannel[] fvarChannels;

        //
        //  Definitions for TopologyDescriptor:
        //
        public TopologyDescriptor()
        {
            //  memset(this, 0, sizeof(TopologyDescriptor)) -- all members default to zero/null/false
        }

        //
        //  Definitions/specializations for its RefinerFactory<TopologyDescriptor>:
        //
        public static bool resizeComponentTopology(TopologyRefiner refiner, TopologyDescriptor desc)
        {
            TopologyRefinerFactory<TopologyDescriptor>.setNumBaseVertices(refiner, desc.numVertices);
            TopologyRefinerFactory<TopologyDescriptor>.setNumBaseFaces(refiner, desc.numFaces);

            for (int face = 0; face < desc.numFaces; ++face)
            {
                TopologyRefinerFactory<TopologyDescriptor>.setNumBaseFaceVertices(refiner, face, desc.numVertsPerFace[face]);
            }
            return true;
        }

        public static bool assignComponentTopology(TopologyRefiner refiner, TopologyDescriptor desc)
        {
            for (int face = 0, idx = 0; face < desc.numFaces; ++face)
            {
                IndexArray dstFaceVerts = TopologyRefinerFactory<TopologyDescriptor>.getBaseFaceVertices(refiner, face);

                if (desc.isLeftHanded)
                {
                    dstFaceVerts[0] = desc.vertIndicesPerFace[idx++];
                    for (int vert = dstFaceVerts.size() - 1; vert > 0; --vert)
                    {
                        dstFaceVerts[vert] = desc.vertIndicesPerFace[idx++];
                    }
                }
                else
                {
                    for (int vert = 0; vert < dstFaceVerts.size(); ++vert)
                    {
                        dstFaceVerts[vert] = desc.vertIndicesPerFace[idx++];
                    }
                }
            }
            return true;
        }

        public static bool assignComponentTags(TopologyRefiner refiner, TopologyDescriptor desc)
        {
            if ((desc.numCreases > 0) && (desc.creaseVertexIndexPairs != null) && (desc.creaseWeights != null))
            {
                int[] vertIndexPairs = desc.creaseVertexIndexPairs;
                int vertIndexPairsOffset = 0;
                for (int edge = 0; edge < desc.numCreases; ++edge, vertIndexPairsOffset += 2)
                {
                    int idx = TopologyRefinerFactory<TopologyDescriptor>.findBaseEdge(refiner,
                        vertIndexPairs[vertIndexPairsOffset + 0], vertIndexPairs[vertIndexPairsOffset + 1]);

                    if (idx != FarTypes.INDEX_INVALID)
                    {
                        TopologyRefinerFactory<TopologyDescriptor>.setBaseEdgeSharpness(refiner, idx, desc.creaseWeights[edge]);
                    }
                    else
                    {
                        string msg = "Edge " + edge + " specified to be sharp does not exist (" +
                            vertIndexPairs[vertIndexPairsOffset + 0] + ", " +
                            vertIndexPairs[vertIndexPairsOffset + 1] + ")";
                        TopologyRefinerFactory<TopologyDescriptor>.reportInvalidTopology(
                            Level.TopologyError.TOPOLOGY_INVALID_CREASE_EDGE, msg, desc);
                    }
                }
            }

            if ((desc.numCorners > 0) && (desc.cornerVertexIndices != null) && (desc.cornerWeights != null))
            {
                for (int vert = 0; vert < desc.numCorners; ++vert)
                {
                    int idx = desc.cornerVertexIndices[vert];

                    if (idx >= 0 && idx < TopologyRefinerFactory<TopologyDescriptor>.getNumBaseVertices(refiner))
                    {
                        TopologyRefinerFactory<TopologyDescriptor>.setBaseVertexSharpness(refiner, idx, desc.cornerWeights[vert]);
                    }
                    else
                    {
                        string msg = "Vertex " + idx + " specified to be sharp does not exist";
                        TopologyRefinerFactory<TopologyDescriptor>.reportInvalidTopology(
                            Level.TopologyError.TOPOLOGY_INVALID_CREASE_VERT, msg, desc);
                    }
                }
            }
            if (desc.numHoles > 0)
            {
                for (int i = 0; i < desc.numHoles; ++i)
                {
                    TopologyRefinerFactory<TopologyDescriptor>.setBaseFaceHole(refiner, desc.holeIndices[i], true);
                }
            }
            return true;
        }

        public static bool assignFaceVaryingTopology(TopologyRefiner refiner, TopologyDescriptor desc)
        {
            if (desc.numFVarChannels > 0)
            {
                for (int channel = 0; channel < desc.numFVarChannels; ++channel)
                {
                    int numFVarValues = desc.fvarChannels[channel].numValues;
                    int[] srcFVarValues = desc.fvarChannels[channel].valueIndices;

                    TopologyRefinerFactory<TopologyDescriptor>.createBaseFVarChannel(refiner, numFVarValues);

                    for (int face = 0, srcNext = 0; face < desc.numFaces; ++face)
                    {
                        IndexArray dstFaceFVarValues = TopologyRefinerFactory<TopologyDescriptor>.getBaseFaceFVarValues(refiner, face, channel);

                        if (desc.isLeftHanded)
                        {
                            dstFaceFVarValues[0] = srcFVarValues[srcNext++];
                            for (int vert = dstFaceFVarValues.size() - 1; vert > 0; --vert)
                            {
                                dstFaceFVarValues[vert] = srcFVarValues[srcNext++];
                            }
                        }
                        else
                        {
                            for (int vert = 0; vert < dstFaceFVarValues.size(); ++vert)
                            {
                                dstFaceFVarValues[vert] = srcFVarValues[srcNext++];
                            }
                        }
                    }
                }
            }
            return true;
        }

        public static void reportInvalidTopology(Level.TopologyError errCode, string msg, TopologyDescriptor mesh)
        {
            Errors.Warning(msg);
        }
    }
}
