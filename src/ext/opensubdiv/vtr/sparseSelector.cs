//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
using System;
using System.Collections.Generic;

using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;

namespace OpenSubdiv.Vtr
{
    //
    //  SparseSelector:
    //      Class supporting "selection" of components in a Level for sparse Refinement.
    //  The term "selection" here implies interest in the limit for that component, i.e.
    //  the limit point for a selected vertex, the limit patch for a face, etc.  So this
    //  class is responsible for ensuring that all neighboring components required to
    //  support the limit of those selected are included in the refinement.
    //
    //  This class is associated with (and constructed given) a Refinement and its role
    //  is to initialize that Refinement instance for eventual sparse refinement.  So it
    //  is a friend of and expected to modify the Refinement as part of the selection.
    //  Given its simplicity and scope it may be worth nesting it in Vtr::Refinement.
    //
    //  While all three component types -- vertices, edges and faces -- can be selected,
    //  only selection of faces is currently used and actively supported as part of the
    //  feature-adaptive refinement.
    //
    public class SparseSelector
    {
        public SparseSelector(Refinement refine)
        {
            _refine = refine;
            _selected = false;
        }

        public void setRefinement(Refinement refine)
        {
            _refine = refine;
        }

        public Refinement getRefinement()
        {
            return _refine;
        }

        public bool isSelectionEmpty()
        {
            return !_selected;
        }

        //
        //  Component selection methods:
        //      Marking of selection is retained in the SparseTags of the Refinement.  The
        //  selection simply marks the parent components -- not any child components that may
        //  be derived from them.  That is done later when we need to additionally identify
        //  all of the "neighboring" child components that must exist at the next subdivision
        //  level in order to fully define supported further refinement of selected components.
        //
        //  Methods for selecting (and marking) components for refinement.  All component indices
        //  refer to components in the parent:
        //
        public void selectVertex(int parentVertex)
        {
            initializeSelection();

            //  Don't bother to test-and-set here, just set
            markVertexSelected(parentVertex);
        }

        public void selectEdge(int parentEdge)
        {
            initializeSelection();

            if (!wasEdgeSelected(parentEdge))
            {
                markEdgeSelected(parentEdge);

                //  Mark the two end vertices:
                ConstIndexArray eVerts = _refine.parent().getEdgeVertices(parentEdge);
                markVertexSelected(eVerts[0]);
                markVertexSelected(eVerts[1]);
            }
        }

        public void selectFace(int parentFace)
        {
            initializeSelection();

            if (!wasFaceSelected(parentFace))
            {
                markFaceSelected(parentFace);

                //  Mark the face's incident verts and edges as selected:
                ConstIndexArray fEdges = _refine.parent().getFaceEdges(parentFace),
                                fVerts = _refine.parent().getFaceVertices(parentFace);

                for (int i = 0; i < fVerts.size(); ++i)
                {
                    markEdgeSelected(fEdges[i]);
                    markVertexSelected(fVerts[i]);
                }
            }
        }

        //  (private in C++; the default constructor is not exposed)

        private bool wasVertexSelected(int pVertex)
        {
            return _refine.getParentVertexSparseTag(pVertex)._selected;
        }

        private bool wasEdgeSelected(int pEdge)
        {
            return _refine.getParentEdgeSparseTag(pEdge)._selected;
        }

        private bool wasFaceSelected(int pFace)
        {
            return _refine.getParentFaceSparseTag(pFace)._selected;
        }

        private void markVertexSelected(int pVertex)
        {
            _refine.getParentVertexSparseTag(pVertex)._selected = true;
        }

        private void markEdgeSelected(int pEdge)
        {
            _refine.getParentEdgeSparseTag(pEdge)._selected = true;
        }

        private void markFaceSelected(int pFace)
        {
            _refine.getParentFaceSparseTag(pFace)._selected = true;
        }

        private void initializeSelection()
        {
            if (!_selected)
            {
                _refine.initializeSparseSelectionTags();
                _selected = true;
            }
        }

        private Refinement _refine;
        private bool _selected;
    }
}
