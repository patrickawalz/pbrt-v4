//
//   Copyright 2015 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using OpenSubdiv.Sdc;
using OpenSubdiv.Vtr;
using OpenSubdiv.Vtr.Internal;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;
using ConstLocalIndexArray = OpenSubdiv.Vtr.ConstArray<ushort>;

namespace OpenSubdiv.Far
{
    /// \brief A simple interface to a level of a TopologyRefiner (see the C++ header
    ///        for the full description of the accessors).
    ///
    public class TopologyLevel
    {
        //@{
        ///  @name Methods to inspect the overall inventory of components:

        /// \brief Return the number of vertices in this level
        public int GetNumVertices() { return _level.getNumVertices(); }

        /// \brief Return the number of faces in this level
        public int GetNumFaces() { return _level.getNumFaces(); }

        /// \brief Return the number of edges in this level
        public int GetNumEdges() { return _level.getNumEdges(); }

        /// \brief Return the total number of face-vertices, i.e. the sum of all vertices for all faces
        public int GetNumFaceVertices() { return _level.getNumFaceVerticesTotal(); }
        //@}

        //@{
        ///  @name Methods to inspect topological relationships for a given component:

        /// \brief Access the vertices incident a given face
        public ConstIndexArray GetFaceVertices(int f) { return _level.getFaceVertices(f); }

        /// \brief Access the edges incident a given face
        public ConstIndexArray GetFaceEdges(int f) { return _level.getFaceEdges(f); }

        /// \brief Access the vertices incident a given edge
        public ConstIndexArray GetEdgeVertices(int e) { return _level.getEdgeVertices(e); }

        /// \brief Access the faces incident a given edge
        public ConstIndexArray GetEdgeFaces(int e) { return _level.getEdgeFaces(e); }

        /// \brief Access the faces incident a given vertex
        public ConstIndexArray GetVertexFaces(int v) { return _level.getVertexFaces(v); }

        /// \brief Access the edges incident a given vertex
        public ConstIndexArray GetVertexEdges(int v) { return _level.getVertexEdges(v); }

        /// \brief Access the local indices of a vertex with respect to its incident faces
        public ConstLocalIndexArray GetVertexFaceLocalIndices(int v) { return _level.getVertexFaceLocalIndices(v); }

        /// \brief Access the local indices of a vertex with respect to its incident edges
        public ConstLocalIndexArray GetVertexEdgeLocalIndices(int v) { return _level.getVertexEdgeLocalIndices(v); }

        /// \brief Access the local indices of an edge with respect to its incident faces
        public ConstLocalIndexArray GetEdgeFaceLocalIndices(int e) { return _level.getEdgeFaceLocalIndices(e); }

        /// \brief Identify the edge matching the given vertex pair
        public int FindEdge(int v0, int v1) { return _level.findEdge(v0, v1); }
        //@}

        //@{
        ///  @name Methods to inspect other topological properties of individual components:

        /// \brief Return if the edge is non-manifold
        public bool IsEdgeNonManifold(int e) { return _level.isEdgeNonManifold(e); }

        /// \brief Return if the vertex is non-manifold
        public bool IsVertexNonManifold(int v) { return _level.isVertexNonManifold(v); }

        /// \brief Return if the edge is a boundary
        public bool IsEdgeBoundary(int e) { return _level.getEdgeTag(e)._boundary != 0; }

        /// \brief Return if the vertex is on a boundary
        public bool IsVertexBoundary(int v) { return _level.getVertexTag(v)._boundary != 0; }

        /// \brief Return if the vertex is a corner (one incident face)
        public bool IsVertexCorner(int v) { return (_level.getNumVertexFaces(v) == 1); }

        /// \brief Return if the vertex has a regular valence
        public bool IsVertexValenceRegular(int v) { return (_level.getVertexTag(v)._xordinary == 0) || IsVertexCorner(v); }
        //@}

        //@{
        ///  @name Methods to inspect feature tags for individual components:

        /// \brief Return the sharpness assigned to a given edge
        public float GetEdgeSharpness(int e) { return _level.getEdgeSharpness(e); }

        /// \brief Return the sharpness assigned to a given vertex
        public float GetVertexSharpness(int v) { return _level.getVertexSharpness(v); }

        /// \brief Return if the edge is infinitely-sharp
        public bool IsEdgeInfSharp(int e) { return _level.getEdgeTag(e)._infSharp != 0; }

        /// \brief Return if the vertex is infinitely-sharp
        public bool IsVertexInfSharp(int v) { return _level.getVertexTag(v)._infSharp != 0; }

        /// \brief Return if the edge is semi-sharp
        public bool IsEdgeSemiSharp(int e) { return _level.getEdgeTag(e)._semiSharp != 0; }

        /// \brief Return if the vertex is semi-sharp
        public bool IsVertexSemiSharp(int v) { return _level.getVertexTag(v)._semiSharp != 0; }

        /// \brief Return if a given face has been tagged as a hole
        public bool IsFaceHole(int f) { return _level.isFaceHole(f); }

        /// \brief Return the subdivision rule assigned a given vertex
        public Crease.Rule GetVertexRule(int v) { return _level.getVertexRule(v); }
        //@}

        //@{
        ///  @name Methods to inspect face-varying data:

        /// \brief Return the number of face-varying channels (should be same for all levels)
        public int GetNumFVarChannels() { return _level.getNumFVarChannels(); }

        /// \brief Return the total number of face-varying values in a particular channel
        public int GetNumFVarValues(int channel = 0) { return _level.getNumFVarValues(channel); }

        /// \brief Access the face-varying values associated with a particular face
        public ConstIndexArray GetFaceFVarValues(int f, int channel = 0)
        {
            return _level.getFaceFVarValues(f, channel);
        }

        /// \brief Return if face-varying topology around a vertex matches
        public bool DoesVertexFVarTopologyMatch(int v, int channel = 0)
        {
            return _level.doesVertexFVarTopologyMatch(v, channel);
        }

        /// \brief Return if face-varying topology across the edge only matches
        public bool DoesEdgeFVarTopologyMatch(int e, int channel = 0)
        {
            return _level.doesEdgeFVarTopologyMatch(e, channel);
        }

        /// \brief Return if face-varying topology around a face matches
        public bool DoesFaceFVarTopologyMatch(int f, int channel = 0)
        {
            return _level.doesFaceFVarTopologyMatch(f, channel);
        }

        //@}

        //@{
        ///  @name Methods to inspect parent-child relationships:

        /// \brief Access the child faces (in the next level) incident a given face
        public ConstIndexArray GetFaceChildFaces(int f) { return _refToChild.getFaceChildFaces(f); }

        /// \brief Access the child edges (in the next level) incident a given face
        public ConstIndexArray GetFaceChildEdges(int f) { return _refToChild.getFaceChildEdges(f); }

        /// \brief Access the child edges (in the next level) incident a given edge
        public ConstIndexArray GetEdgeChildEdges(int e) { return _refToChild.getEdgeChildEdges(e); }

        /// \brief Return the child vertex (in the next level) of a given face
        public int GetFaceChildVertex(int f) { return _refToChild.getFaceChildVertex(f); }

        /// \brief Return the child vertex (in the next level) of a given edge
        public int GetEdgeChildVertex(int e) { return _refToChild.getEdgeChildVertex(e); }

        /// \brief Return the child vertex (in the next level) of a given vertex
        public int GetVertexChildVertex(int v) { return _refToChild.getVertexChildVertex(v); }

        /// \brief Return the parent face (in the previous level) of a given face
        public int GetFaceParentFace(int f) { return _refToParent.getChildFaceParentFace(f); }
        //@}

        //@{
        ///  @name Debugging aides:

        /// \brief Validate the topological relationships in this level (intended for debugging)
        public bool ValidateTopology() { return _level.validateTopology(); }

        /// \brief Prints topological relationships to the console (intended for debugging)
        public void PrintTopology(bool children = true)
        {
            _level.print((children && (_refToChild != null)) ? _refToChild : null);
        }
        //@}

        //  friend class TopologyRefiner  --  public in C#
        public Level _level;
        public Refinement _refToParent;
        public Refinement _refToChild;

        //  Not intended for public use, but required by std::vector, etc...
        public TopologyLevel()
        {
        }
    }
}
