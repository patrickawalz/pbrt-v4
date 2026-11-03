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
using LocalIndexArray = OpenSubdiv.Vtr.Array<ushort>;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using TopologyCallback = OpenSubdiv.Vtr.Internal.Level.ValidationCallback;

namespace OpenSubdiv.Far
{
    /// TopologyRefinerFactoryBase is the base class for subclasses that are intended to
    /// construct TopologyRefiners directly from meshes in their native representations.
    /// The subclasses are parameterized by the mesh type \<class MESH\> and are expected
    /// to inherit the details related to assembly and validation provided here that are
    /// independent of the subclass' mesh type.
    ///
    public class TopologyRefinerFactoryBase
    {
        //
        //  Protected methods invoked by the subclass template to verify and process each
        //  stage of construction implemented by the subclass (public in C#):
        //

        //
        //  Methods for the Factory base class -- general enough to warrant including
        //  in the base class rather than the subclass template (and so replicated for
        //  each usage)
        //
        //
        public static bool prepareComponentTopologySizing(TopologyRefiner refiner)
        {
            Level baseLevel = refiner.getLevel(0);

            //
            //  At minimum we require face-vertices (the total count of which can be
            //  determined from the offsets accumulated during sizing pass) and we
            //  need to resize members related to them to be populated during
            //  assignment:
            //
            int vCount = baseLevel.getNumVertices();
            int fCount = baseLevel.getNumFaces();

            if (vCount == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefinerFactory<>::Create() -- " +
                    "mesh contains no vertices.");
                return false;
            }
            if (fCount == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefinerFactory<>::Create() -- " +
                    "meshes without faces not yet supported.");
                return false;
            }

            //  Make sure no face was defined that would lead to a valence overflow --
            //  the max valence has been initialized with the maximum number of
            //  face-vertices:
            if (baseLevel.getMaxValence() > FarTypes.VALENCE_LIMIT)
            {
                string msg = "Failure in TopologyRefinerFactory<>::Create() -- " +
                    "face with " + baseLevel.getMaxValence() + " vertices > " + FarTypes.VALENCE_LIMIT + " max.";
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR, msg);
                return false;
            }

            int fVertCount = baseLevel.getNumFaceVertices(fCount - 1) +
                             baseLevel.getOffsetOfFaceVertices(fCount - 1);

            if (fVertCount == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefinerFactory<>::Create() -- " +
                    "mesh contains no face-vertices.");
                return false;
            }
            if ((refiner.GetSchemeType() == SchemeType.SCHEME_LOOP) &&
                (fVertCount != (3 * fCount)))
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefinerFactory<>::Create() -- " +
                    "non-triangular faces not supported by Loop scheme.");
                return false;
            }
            baseLevel.resizeFaceVertices(fVertCount);

            //
            //  If edges were sized, all other topological relations must be sized
            //  with it, in which case we allocate those members to be populated.
            //  Otherwise, sizing of the other topology members is deferred until
            //  the face-vertices are assigned and the resulting relationships
            //  determined:
            //
            int eCount = baseLevel.getNumEdges();

            if (eCount > 0)
            {
                baseLevel.resizeFaceEdges(baseLevel.getNumFaceVerticesTotal());
                baseLevel.resizeEdgeVertices();
                baseLevel.resizeEdgeFaces(baseLevel.getNumEdgeFaces(eCount - 1) +
                                          baseLevel.getOffsetOfEdgeFaces(eCount - 1));
                baseLevel.resizeVertexFaces(baseLevel.getNumVertexFaces(vCount - 1) +
                                            baseLevel.getOffsetOfVertexFaces(vCount - 1));
                baseLevel.resizeVertexEdges(baseLevel.getNumVertexEdges(vCount - 1) +
                                            baseLevel.getOffsetOfVertexEdges(vCount - 1));

                System.Diagnostics.Debug.Assert(baseLevel.getNumFaceEdgesTotal() > 0);
                System.Diagnostics.Debug.Assert(baseLevel.getNumEdgeVerticesTotal() > 0);
                System.Diagnostics.Debug.Assert(baseLevel.getNumEdgeFacesTotal() > 0);
                System.Diagnostics.Debug.Assert(baseLevel.getNumVertexFacesTotal() > 0);
                System.Diagnostics.Debug.Assert(baseLevel.getNumVertexEdgesTotal() > 0);
            }
            return true;
        }

        public static bool prepareComponentTopologyAssignment(
            TopologyRefiner refiner, bool fullValidation,
            TopologyCallback callback, object callbackData)
        {
            Level baseLevel = refiner.getLevel(0);

            bool completeMissingTopology = (baseLevel.getNumEdges() == 0);
            if (completeMissingTopology)
            {
                if (!baseLevel.completeTopologyFromFaceVertices())
                {
                    string msg = "Failure in TopologyRefinerFactory<>::Create() -- " +
                        "vertex with valence " + baseLevel.getMaxValence() + " > " + FarTypes.VALENCE_LIMIT + " max.";
                    Errors.Error(ErrorType.FAR_RUNTIME_ERROR, msg);
                    return false;
                }
            }
            else
            {
                if (baseLevel.getMaxValence() == 0)
                {
                    Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                        "Failure in TopologyRefinerFactory<>::Create() -- " +
                        "maximum valence not assigned.");
                    return false;
                }
            }

            if (fullValidation)
            {
                if (!baseLevel.validateTopology(callback, callbackData))
                {
                    if (completeMissingTopology)
                    {
                        Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                            "Failure in TopologyRefinerFactory<>::Create() -- " +
                            "invalid topology detected from partial specification.");
                    }
                    else
                    {
                        Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                            "Failure in TopologyRefinerFactory<>::Create() -- " +
                            "invalid topology detected as fully specified.");
                    }
                    return false;
                }
            }

            //  Now that we have a valid base level, initialize the Refiner's
            //  component inventory:
            refiner.initializeInventory();
            return true;
        }

        public static bool prepareComponentTagsAndSharpness(TopologyRefiner refiner)
        {
            //
            //  This method combines the initialization of internal component tags
            //  with the sharpening of edges and vertices according to the given
            //  boundary interpolation rule in the Options.
            //  Since both involve traversing the edge and vertex lists and noting
            //  the presence of boundaries -- best to do both at once...
            //
            Level baseLevel = refiner.getLevel(0);

            Sdc.Options options = refiner.GetSchemeOptions();
            Crease creasing = new Crease(options);

            bool makeBoundaryFacesHoles =
                (options.GetVtxBoundaryInterpolation() ==
                    Sdc.Options.VtxBoundaryInterpolation.VTX_BOUNDARY_NONE) &&
                (SchemeTypeTraits.GetLocalNeighborhoodSize(
                    refiner.GetSchemeType()) > 0);

            bool sharpenCornerVerts =
                (options.GetVtxBoundaryInterpolation() ==
                    Sdc.Options.VtxBoundaryInterpolation.VTX_BOUNDARY_EDGE_AND_CORNER);

            bool sharpenNonManFeatures = true;

            //
            //  Before initializing edge and vertex tags, tag any qualifying boundary
            //  faces as holes before the sharpness of incident vertices and edges is
            //  affected by boundary interpolation rules.
            //
            //  Faces will be excluded (tagged as holes) if they contain a vertex on a
            //  boundary that did not have all of its incident boundary edges sharpened
            //  (not just the boundary edges within the face), so inspect the vertices
            //  and tag their incident faces when necessary:
            //
            if (makeBoundaryFacesHoles)
            {
                for (int vIndex = 0; vIndex < baseLevel.getNumVertices();
                        ++vIndex)
                {
                    ConstIndexArray vEdges = baseLevel.getVertexEdges(vIndex);
                    ConstIndexArray vFaces = baseLevel.getVertexFaces(vIndex);

                    //  Ignore manifold interior vertices:
                    if ((vEdges.size() == vFaces.size()) &&
                        (baseLevel.getVertexTag(vIndex)._nonManifold == 0))
                    {
                        continue;
                    }

                    bool excludeFaces = false;
                    for (int i = 0; !excludeFaces && (i < vEdges.size()); ++i)
                    {
                        excludeFaces = (baseLevel.getNumEdgeFaces(vEdges[i]) == 1) &&
                            !Crease.IsInfinite(
                                baseLevel.getEdgeSharpness(vEdges[i]));
                    }
                    if (excludeFaces)
                    {
                        for (int i = 0; i < vFaces.size(); ++i)
                        {
                            baseLevel.getFaceTag(vFaces[i])._hole = 1;
                        }
                        //  Need to tag Refiner (the Level does not keep track of this)
                        refiner._hasHoles = true;
                    }
                }
            }

            //
            //  Process the Edge tags first, as Vertex tags (notably the Rule) are
            //  dependent on properties of their incident edges.
            //
            for (int eIndex = 0; eIndex < baseLevel.getNumEdges(); ++eIndex)
            {
                ref Level.ETag eTag = ref baseLevel.getEdgeTag(eIndex);

                ref float eSharpness = ref baseLevel.getEdgeSharpness(eIndex);

                eTag._boundary = (baseLevel.getNumEdgeFaces(eIndex) < 2) ? 1u : 0u;
                if ((eTag._boundary != 0) || ((eTag._nonManifold != 0) && sharpenNonManFeatures))
                {
                    eSharpness = Crease.SHARPNESS_INFINITE;
                }
                eTag._infSharp = Crease.IsInfinite(eSharpness) ? 1u : 0u;
                eTag._semiSharp = (Crease.IsSharp(eSharpness) && (eTag._infSharp == 0)) ? 1u : 0u;
            }

            //
            //  Process the Vertex tags now -- for some tags (semi-sharp and its rule)
            //  we need to inspect all incident edges:
            //
            int schemeRegularInteriorValence =
                SchemeTypeTraits.GetRegularVertexValence(refiner.GetSchemeType());
            int schemeRegularBoundaryValence = schemeRegularInteriorValence / 2;

            for (int vIndex = 0; vIndex < baseLevel.getNumVertices(); ++vIndex)
            {
                ref Level.VTag vTag = ref baseLevel.getVertexTag(vIndex);

                ref float vSharpness = ref baseLevel.getVertexSharpness(vIndex);

                ConstIndexArray vEdges = baseLevel.getVertexEdges(vIndex);
                ConstIndexArray vFaces = baseLevel.getVertexFaces(vIndex);

                //
                //  Take inventory of properties of incident edges that affect this
                //  vertex:
                //
                int boundaryEdgeCount = 0;
                int infSharpEdgeCount = 0;
                int semiSharpEdgeCount = 0;
                int nonManifoldEdgeCount = 0;
                for (int i = 0; i < vEdges.size(); ++i)
                {
                    Level.ETag eTag = baseLevel.getEdgeTag(vEdges[i]);

                    boundaryEdgeCount += (int)eTag._boundary;
                    infSharpEdgeCount += (int)eTag._infSharp;
                    semiSharpEdgeCount += (int)eTag._semiSharp;
                    nonManifoldEdgeCount += (int)eTag._nonManifold;
                }
                int sharpEdgeCount = infSharpEdgeCount + semiSharpEdgeCount;

                //
                //  Sharpen the vertex before using it in conjunction with incident edge
                //  properties to determine the semi-sharp tag and rule:
                //
                bool isTopologicalCorner = (vFaces.size() == 1) && (vEdges.size() == 2);
                bool isSharpenedCorner = isTopologicalCorner && sharpenCornerVerts;
                if (isSharpenedCorner)
                {
                    vSharpness = Crease.SHARPNESS_INFINITE;
                }
                else if ((vTag._nonManifold != 0) && sharpenNonManFeatures &&
                         !Crease.IsInfinite(vSharpness))
                {
                    //
                    //  We avoid sharpening non-manifold vertices when they occur on
                    //  interior non-manifold creases, i.e. a pair of opposing non-
                    //  manifold edges with more than two incident faces.  In these
                    //  cases there are more incident faces than edges (1 more for
                    //  each additional "fin") and no boundaries. Closer inspection
                    //  of manifold subsets around the vertex is required to truly
                    //  determine the crease case, so avoid it using pre-conditions
                    //  that are available here:
                    //
                    bool isNonManCrease = (nonManifoldEdgeCount == 2) &&
                                          (boundaryEdgeCount == 0) &&
                                          (vFaces.size() > vEdges.size()) &&
                                          baseLevel.testVertexNonManifoldCrease(vIndex);
                    if (!isNonManCrease)
                    {
                        vSharpness = Crease.SHARPNESS_INFINITE;
                    }
                }

                vTag._infSharp = Crease.IsInfinite(vSharpness) ? 1u : 0u;
                vTag._semiSharp = Crease.IsSemiSharp(vSharpness) ? 1u : 0u;
                vTag._semiSharpEdges = (semiSharpEdgeCount > 0) ? 1u : 0u;

                vTag._rule = (Level.VTag.VTagSize)
                    creasing.DetermineVertexVertexRule(vSharpness, sharpEdgeCount);

                //
                //  Assign topological tags -- note that the "xordinary" tag is not
                //  assigned if non-manifold:
                //
                vTag._boundary = (boundaryEdgeCount > 0) ? 1u : 0u;
                vTag._corner = (isTopologicalCorner && (vTag._infSharp != 0)) ? 1u : 0u;
                if (vTag._nonManifold != 0)
                {
                    vTag._xordinary = 0;
                }
                else if (vTag._corner != 0)
                {
                    vTag._xordinary = 0;
                }
                else if (vTag._boundary != 0)
                {
                    vTag._xordinary = (vFaces.size() != schemeRegularBoundaryValence) ? 1u : 0u;
                }
                else
                {
                    vTag._xordinary = (vFaces.size() != schemeRegularInteriorValence) ? 1u : 0u;
                }
                vTag._incomplete = 0;

                //
                //  Assign tags specific to inf-sharp features to identify regular
                //  topologies partitioned by inf-sharp creases -- must be no semi-
                //  harp features here (and manifold for now):
                //
                vTag._infSharpEdges = (infSharpEdgeCount > 0) ? 1u : 0u;
                vTag._infSharpCrease = 0;
                vTag._infIrregular = ((vTag._infSharp != 0) || (vTag._infSharpEdges != 0)) ? 1u : 0u;

                if (vTag._infSharpEdges != 0)
                {
                    //  Ignore semi-sharp vertex sharpness when computing the
                    //  inf-sharp Rule:
                    Crease.Rule infRule = creasing.DetermineVertexVertexRule(
                        ((vTag._infSharp != 0) ? vSharpness : 0.0f), infSharpEdgeCount);

                    if (infRule == Crease.Rule.RULE_CREASE)
                    {
                        vTag._infSharpCrease = 1;

                        //  A "regular" inf-crease can only occur along a manifold
                        //  regular boundary or by bisecting a manifold interior
                        //  region (it is also possible along non-manifold vertices
                        //  in some cases, but that requires much more effort to
                        //  detect -- perhaps later...)
                        //
                        if ((vTag._xordinary == 0) && (vTag._nonManifold == 0))
                        {
                            if (vTag._boundary != 0)
                            {
                                vTag._infIrregular = 0;
                            }
                            else
                            {
                                System.Diagnostics.Debug.Assert((schemeRegularInteriorValence == 4) ||
                                    (schemeRegularInteriorValence == 6));

                                if (schemeRegularInteriorValence == 4)
                                {
                                    vTag._infIrregular =
                                        (baseLevel.getEdgeTag(vEdges[0])._infSharp !=
                                         baseLevel.getEdgeTag(vEdges[2])._infSharp) ? 1u : 0u;
                                }
                                else if (schemeRegularInteriorValence == 6)
                                {
                                    vTag._infIrregular =
                                        ((baseLevel.getEdgeTag(vEdges[0])._infSharp !=
                                          baseLevel.getEdgeTag(vEdges[3])._infSharp) ||
                                         (baseLevel.getEdgeTag(vEdges[1])._infSharp !=
                                          baseLevel.getEdgeTag(vEdges[4])._infSharp)) ? 1u : 0u;
                                }
                            }
                        }
                    }
                    else if (infRule == Crease.Rule.RULE_CORNER)
                    {
                        //  A regular set of inf-corners occurs when all edges are
                        //  sharp and not a smooth corner:
                        //
                        if ((infSharpEdgeCount == vEdges.size() &&
                            ((vEdges.size() > 2) || (vTag._infSharp != 0))))
                        {
                            vTag._infIrregular = 0;
                        }
                    }
                }

                //
                //  If any irregular faces are present, mark whether or not a vertex
                //  is incident any irregular face:
                //
                if (refiner._hasIrregFaces)
                {
                    int regSize = refiner._regFaceSize;
                    for (int i = 0; i < vFaces.size(); ++i)
                    {
                        if (baseLevel.getFaceVertices(vFaces[i]).size() != regSize)
                        {
                            vTag._incidIrregFace = 1;
                            break;
                        }
                    }
                }
            }
            return true;
        }

        public static bool prepareFaceVaryingChannels(TopologyRefiner refiner)
        {
            Level baseLevel = refiner.getLevel(0);

            int regVertexValence =
                SchemeTypeTraits.GetRegularVertexValence(refiner.GetSchemeType());
            int regBoundaryValence = regVertexValence / 2;

            for (int channel = 0; channel < refiner.GetNumFVarChannels(); ++channel)
            {
                if (baseLevel.getNumFVarValues(channel) == 0)
                {
                    string msg = "Failure in TopologyRefinerFactory<>::Create() -- " +
                        "face-varying channel " + channel + " has no values.";
                    Errors.Error(ErrorType.FAR_RUNTIME_ERROR, msg);
                    return false;
                }
                baseLevel.completeFVarChannelTopology(channel, regBoundaryValence);
            }
            return true;
        }
    }

    /// C# replacement of the C++ template specialisation machinery: a mesh type MESH
    /// implements the static members below (the "specialisations" of
    /// TopologyRefinerFactory<MESH>); unimplemented ones use the generic stubs of the
    /// C++ template (default interface implementations).
    ///
    public interface ITopologyRefinerFactoryMesh<MESH>
    {
        /// \brief  Specify the number of vertices, faces, face-vertices, etc.
        static virtual bool resizeComponentTopology(TopologyRefiner refiner, MESH mesh)
        {
            Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                "Failure in TopologyRefinerFactory<>::resizeComponentTopology() -- no specialization provided.");

            //
            //  Sizing the topology tables:
            //      This method is for determining the sizes of the various topology tables (and other
            //  data) associated with the mesh.  Once completed, appropriate memory will be allocated
            //  and an additional method invoked to populate it accordingly.
            //
            //  (See the C++ generic stub for the full description.)
            //
            return false;
        }

        /// \brief  Specify the relationships between vertices, faces, etc. ie the
        /// face-vertices, vertex-faces, edge-vertices, etc.
        static virtual bool assignComponentTopology(TopologyRefiner refiner, MESH mesh)
        {
            Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                "Failure in TopologyRefinerFactory<>::assignComponentTopology() -- no specialization provided.");

            //
            //  Assigning the topology tables:
            //      Once the topology tables have been allocated, the six required topological
            //  relations can be directly populated using the TopologyRefinerFactory<MESH>.getBase*() methods.
            //
            //  (See the C++ generic stub for the full description.)
            //
            return false;
        }

        /// \brief  (Optional) Specify edge or vertex sharpness or face holes
        static virtual bool assignComponentTags(TopologyRefiner refiner, MESH mesh)
        {
            //
            //  Optional tagging:
            //      This is where any additional feature tags -- sharpness, holes, etc. -- can be
            //  specified using:
            //
            //      void setBaseEdgeSharpness(TopologyRefiner& newRefiner, Index edge, float sharpness)
            //      void setBaseVertexSharpness(TopologyRefiner& newRefiner, Index vertex, float sharpness)
            //
            //      void setBaseFaceHole(TopologyRefiner& newRefiner, Index face, bool hole)
            //
            return true;
        }

        /// \brief  (Optional) Specify face-varying data per face
        static virtual bool assignFaceVaryingTopology(TopologyRefiner refiner, MESH mesh)
        {
            //
            //  Optional assigning face-varying topology tables:
            //
            //  Create independent face-varying primitive variable channels:
            //      int createBaseFVarChannel(TopologyRefiner& newRefiner, int numValues)
            //
            //  For each channel, populate the face-vertex values:
            //      IndexArray setBaseFaceFVarValues(TopologyRefiner& newRefiner, Index face, int channel = 0)
            //
            return true;
        }

        /// \brief  (Optional) Control run-time topology validation and error reporting
        static virtual void reportInvalidTopology(Level.TopologyError errCode, string msg, MESH mesh)
        {
            //
            //  Optional topology validation error reporting:
            //      This method is called whenever the factory encounters topology validation
            //  errors. By default, nothing is reported
            //
        }
    }

    ///\brief Factory for constructing TopologyRefiners from specific mesh classes.
    ///
    /// TopologyRefinerFactory<MESH> is the factory class template to convert an instance of
    /// TopologyRefiner from an arbitrary mesh class.  While a class template, the implementation
    /// is not (cannot) be complete, so specialization of a few methods is required (it is a
    /// stateless factory, so no instance and only static methods).
    ///
    /// In C# the "specialisation" is provided by MESH implementing ITopologyRefinerFactoryMesh<MESH>.
    ///
    public class TopologyRefinerFactory<MESH> : TopologyRefinerFactoryBase
        where MESH : ITopologyRefinerFactoryMesh<MESH>
    {
        /// \brief Options related to the construction of each TopologyRefiner.
        ///
        public class Options
        {
            public Options(SchemeType sdcType = SchemeType.SCHEME_CATMARK, Sdc.Options sdcOptions = default)
            {
                schemeType = sdcType;
                schemeOptions = sdcOptions;
                validateFullTopology = false;
            }

            public SchemeType schemeType;             ///< The subdivision scheme type identifier
            public Sdc.Options schemeOptions;         ///< The full set of options for the scheme,
                                                      ///< e.g. boundary interpolation rules...
            public bool validateFullTopology;         ///< Apply more extensive validation of
                                                      ///< the constructed topology -- intended
                                                      ///< for debugging.
        }

        /// \brief Instantiates a TopologyRefiner from client-provided topological
        ///        representation.
        ///
        ///  If only the face-vertices topological relationships are specified
        ///  with this factory, edge relationships have to be inferred, which
        ///  requires additional processing. If the client topological rep can
        ///  provide this information, it is highly recommended to do so.
        ///
        /// @param mesh       Client's topological representation (or a converter)
        ///
        /// @param options    Options controlling the creation of the TopologyRefiner
        ///
        /// @return           A new instance of TopologyRefiner or null for failure
        ///
        public static TopologyRefiner Create(MESH mesh, Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            TopologyRefiner refiner = new TopologyRefiner(options.schemeType, options.schemeOptions);

            if (!populateBaseLevel(refiner, mesh, options))
            {
                return null;
            }

            //  Eventually want to move the Refiner's inventory initialization here.  Currently it
            //  is handled after topology assignment, but if the inventory is to include additional
            //  features (e.g. holes, etc.) it is better off deferred to here.

            return refiner;
        }

        /// \brief Instantiates a TopologyRefiner from the base level of an
        ///        existing instance.
        ///
        ///  This allows lightweight copies of the same topology to be refined
        ///  differently for each new instance.  As with other classes that refer
        ///  to an existing TopologyRefiner, it must generally exist for the entire
        ///  lifetime of the new instance.  In this case, the base level of the
        ///  original instance must be preserved.
        ///
        /// @param baseLevel  An existing TopologyRefiner to share base level.
        ///
        /// @return           A new instance of TopologyRefiner or null for failure
        ///
        public static TopologyRefiner Create(TopologyRefiner source)
        {
            return new TopologyRefiner(source);
        }

        //@{
        ///  @name  Methods to be provided to complete assembly of the TopologyRefiner
        ///
        ///  (Forwarders to the MESH implementation of ITopologyRefinerFactoryMesh<MESH>.)
        ///
        public static bool resizeComponentTopology(TopologyRefiner newRefiner, MESH mesh)
        {
            return MESH.resizeComponentTopology(newRefiner, mesh);
        }

        public static bool assignComponentTopology(TopologyRefiner newRefiner, MESH mesh)
        {
            return MESH.assignComponentTopology(newRefiner, mesh);
        }

        public static bool assignComponentTags(TopologyRefiner newRefiner, MESH mesh)
        {
            return MESH.assignComponentTags(newRefiner, mesh);
        }

        public static bool assignFaceVaryingTopology(TopologyRefiner newRefiner, MESH mesh)
        {
            return MESH.assignFaceVaryingTopology(newRefiner, mesh);
        }

        public static void reportInvalidTopology(Level.TopologyError errCode, string msg, MESH mesh)
        {
            MESH.reportInvalidTopology(errCode, msg, mesh);
        }
        //@}

        //
        //  Not to be specialized:
        //
        public static bool populateBaseLevel(TopologyRefiner refiner, MESH mesh, Options options)
        {
            //
            //  Construction of a specialized topology refiner involves four steps, each of which
            //  involves a method specialized for MESH followed by one that takes an action in
            //  response to it or in preparation for the next step.
            //
            //  Both the specialized methods and those that follow them may find fault in the
            //  construction and trigger failure at any time:
            //

            //
            //  Sizing of the topology -- this is a required specialization for MESH.  This defines
            //  an inventory of all components and their relations that is used to allocate buffers
            //  to be efficiently populated in the subsequent topology assignment step.
            //
            if (!resizeComponentTopology(refiner, mesh)) return false;
            if (!prepareComponentTopologySizing(refiner)) return false;

            //
            //  Assignment of the topology -- this is a required specialization for MESH.  If edges
            //  are specified, all other topological relations are expected to be defined for them.
            //  Otherwise edges and remaining topology will be completed from the face-vertices:
            //
            bool validate = options.validateFullTopology;
            TopologyCallback callback = reportInvalidTopologyAdapter;
            object userData = mesh;

            if (!assignComponentTopology(refiner, mesh)) return false;
            if (!prepareComponentTopologyAssignment(refiner, validate, callback, userData)) return false;

            //
            //  User assigned and internal tagging of components -- an optional specialization for
            //  MESH.  Allows the specification of sharpness values, holes, etc.
            //
            if (!assignComponentTags(refiner, mesh)) return false;
            if (!prepareComponentTagsAndSharpness(refiner)) return false;

            //
            //  Defining channels of face-varying primvar data -- an optional specialization for MESH.
            //
            if (!assignFaceVaryingTopology(refiner, mesh)) return false;
            if (!prepareFaceVaryingChannels(refiner)) return false;

            return true;
        }

        //
        //  An oversight in the interfaces of the error reporting function between the factory
        //  class and the Vtr::Level requires this adapter function to avoid warnings.
        //
        //  The static class method requires a reference as the MESH argument, but the interface
        //  for Vtr::Level requires a pointer (void*). So this adapter with a MESH* argument is
        //  used to effectively cast the function pointer required by Vtr::Level error reporting:
        //
        private static void reportInvalidTopologyAdapter(Level.TopologyError errCode, string msg, object mesh)
        {
            reportInvalidTopology(errCode, msg, (MESH)mesh);
        }

        //@{
        ///  @name  Base level assembly methods to be used within resizeComponentTopology()
        ///
        /// \brief Specify the number of vertices to be accommodated
        public static void setNumBaseVertices(TopologyRefiner newRefiner, int count)
        {
            newRefiner._levels[0].resizeVertices(count);
        }

        /// \brief Specify the number of faces to be accommodated
        public static void setNumBaseFaces(TopologyRefiner newRefiner, int count)
        {
            newRefiner._levels[0].resizeFaces(count);
        }

        /// \brief Specify the number of edges to be accommodated
        public static void setNumBaseEdges(TopologyRefiner newRefiner, int count)
        {
            newRefiner._levels[0].resizeEdges(count);
        }

        /// \brief Specify the number of vertices incident each face
        public static void setNumBaseFaceVertices(TopologyRefiner newRefiner, int f, int count)
        {
            newRefiner._levels[0].resizeFaceVertices(f, count);
            newRefiner._hasIrregFaces = newRefiner._hasIrregFaces || (count != newRefiner._regFaceSize);
        }

        /// \brief Specify the number of faces incident each edge
        public static void setNumBaseEdgeFaces(TopologyRefiner newRefiner, int e, int count)
        {
            newRefiner._levels[0].resizeEdgeFaces(e, count);
        }

        /// \brief Specify the number of faces incident each vertex
        public static void setNumBaseVertexFaces(TopologyRefiner newRefiner, int v, int count)
        {
            newRefiner._levels[0].resizeVertexFaces(v, count);
        }

        /// \brief Specify the number of edges incident each vertex
        public static void setNumBaseVertexEdges(TopologyRefiner newRefiner, int v, int count)
        {
            newRefiner._levels[0].resizeVertexEdges(v, count);
        }

        public static int getNumBaseVertices(TopologyRefiner newRefiner)
        {
            return newRefiner._levels[0].getNumVertices();
        }

        public static int getNumBaseFaces(TopologyRefiner newRefiner)
        {
            return newRefiner._levels[0].getNumFaces();
        }

        public static int getNumBaseEdges(TopologyRefiner newRefiner)
        {
            return newRefiner._levels[0].getNumEdges();
        }
        //@}

        //@{
        ///  @name  Base level assembly methods to be used within assignComponentTopology()
        ///
        /// \brief Assign the vertices incident each face
        public static IndexArray getBaseFaceVertices(TopologyRefiner newRefiner, int f)
        {
            return newRefiner._levels[0].getFaceVertices(f);
        }

        /// \brief Assign the edges incident each face
        public static IndexArray getBaseFaceEdges(TopologyRefiner newRefiner, int f)
        {
            return newRefiner._levels[0].getFaceEdges(f);
        }

        /// \brief Assign the vertices incident each edge
        public static IndexArray getBaseEdgeVertices(TopologyRefiner newRefiner, int e)
        {
            return newRefiner._levels[0].getEdgeVertices(e);
        }

        /// \brief Assign the faces incident each edge
        public static IndexArray getBaseEdgeFaces(TopologyRefiner newRefiner, int e)
        {
            return newRefiner._levels[0].getEdgeFaces(e);
        }

        /// \brief Assign the faces incident each vertex
        public static IndexArray getBaseVertexFaces(TopologyRefiner newRefiner, int v)
        {
            return newRefiner._levels[0].getVertexFaces(v);
        }

        /// \brief Assign the edges incident each vertex
        public static IndexArray getBaseVertexEdges(TopologyRefiner newRefiner, int v)
        {
            return newRefiner._levels[0].getVertexEdges(v);
        }

        /// \brief Assign the local indices of an edge within each of its incident faces
        public static LocalIndexArray getBaseEdgeFaceLocalIndices(TopologyRefiner newRefiner, int e)
        {
            return newRefiner._levels[0].getEdgeFaceLocalIndices(e);
        }

        /// \brief Assign the local indices of a vertex within each of its incident faces
        public static LocalIndexArray getBaseVertexFaceLocalIndices(TopologyRefiner newRefiner, int v)
        {
            return newRefiner._levels[0].getVertexFaceLocalIndices(v);
        }

        /// \brief Assign the local indices of a vertex within each of its incident edges
        public static LocalIndexArray getBaseVertexEdgeLocalIndices(TopologyRefiner newRefiner, int v)
        {
            return newRefiner._levels[0].getVertexEdgeLocalIndices(v);
        }

        /// \brief Determine all local indices by inspection (only for pure manifold meshes)
        public static void populateBaseLocalIndices(TopologyRefiner newRefiner)
        {
            newRefiner._levels[0].populateLocalIndices();
        }

        /// \brief Tag an edge as non-manifold
        public static void setBaseEdgeNonManifold(TopologyRefiner newRefiner, int e, bool b)
        {
            newRefiner._levels[0].setEdgeNonManifold(e, b);
        }

        /// \brief Tag a vertex as non-manifold
        public static void setBaseVertexNonManifold(TopologyRefiner newRefiner, int v, bool b)
        {
            newRefiner._levels[0].setVertexNonManifold(v, b);
        }
        //@}

        //@{
        ///  @name  Base level assembly methods to be used within assignComponentTags()
        ///
        /// \brief Identify an edge to be assigned a sharpness value given a vertex pair
        public static int findBaseEdge(TopologyRefiner newRefiner, int v0, int v1)
        {
            return newRefiner._levels[0].findEdge(v0, v1);
        }

        /// \brief Assign a sharpness value to a given edge
        public static void setBaseEdgeSharpness(TopologyRefiner newRefiner, int e, float s)
        {
            newRefiner._levels[0].getEdgeSharpness(e) = s;
        }

        /// \brief Assign a sharpness value to a given vertex
        public static void setBaseVertexSharpness(TopologyRefiner newRefiner, int v, float s)
        {
            newRefiner._levels[0].getVertexSharpness(v) = s;
        }

        /// \brief Tag a face as a hole
        public static void setBaseFaceHole(TopologyRefiner newRefiner, int f, bool b)
        {
            newRefiner._levels[0].setFaceHole(f, b);
            newRefiner._hasHoles = newRefiner._hasHoles || b;
        }
        //@}

        //@{
        ///  @name  Base level assembly methods to be used within assignFaceVaryingTopology()
        ///
        /// \brief  Create a new face-varying channel with the given number of values
        public static int createBaseFVarChannel(TopologyRefiner newRefiner, int numValues)
        {
            return newRefiner._levels[0].createFVarChannel(numValues, newRefiner._subdivOptions);
        }

        /// \brief  Create a new face-varying channel with the given number of values and independent interpolation options
        public static int createBaseFVarChannel(TopologyRefiner newRefiner, int numValues, Sdc.Options fvarOptions)
        {
            Sdc.Options newOptions = newRefiner._subdivOptions;
            newOptions.SetFVarLinearInterpolation(fvarOptions.GetFVarLinearInterpolation());
            return newRefiner._levels[0].createFVarChannel(numValues, newOptions);
        }

        /// \brief Assign the face-varying values for the corners of each face
        public static IndexArray getBaseFaceFVarValues(TopologyRefiner newRefiner, int face, int channel = 0)
        {
            return newRefiner._levels[0].getFaceFVarValues(face, channel);
        }
        //@}
    }
}
