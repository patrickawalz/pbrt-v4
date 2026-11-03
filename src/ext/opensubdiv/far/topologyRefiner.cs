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
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;

namespace OpenSubdiv.Far.Internal
{
    //
    //  Internal utility class and function supporting feature adaptive selection of faces...
    //
    //
    //  FeatureMask is a simple set of bits identifying features to be selected during a level of
    //  adaptive refinement.  Adaptive refinement options passed the Refiner are interpreted as a
    //  specific set of features defined here.  Given options to reduce faces generated at deeper
    //  levels, a method to "reduce" the set of features is also provided here.
    //
    //  This class was specifically not nested in TopologyRefiner to allow simple non-class methods
    //  to make use of it in the core selection methods.  Those selection methods were similarly
    //  made non-class methods to ensure they conform to the feature set defined by the FeatureMask
    //  and not some internal class state.
    //
    //  (A struct in C#: it is copied by value, bitfields become bools.)
    //
    public struct FeatureMask
    {
        public void Clear()
        {
            selectXOrdinaryInterior = false;
            selectXOrdinaryBoundary = false;

            selectSemiSharpSingle = false;
            selectSemiSharpNonSingle = false;

            selectInfSharpRegularCrease = false;
            selectInfSharpRegularCorner = false;
            selectInfSharpIrregularDart = false;
            selectInfSharpIrregularCrease = false;
            selectInfSharpIrregularCorner = false;

            selectUnisolatedInteriorEdge = false;

            selectNonManifold = false;
            selectFVarFeatures = false;
        }

        public bool IsEmpty()
        {
            return !(selectXOrdinaryInterior ||
                     selectXOrdinaryBoundary ||
                     selectSemiSharpSingle ||
                     selectSemiSharpNonSingle ||
                     selectInfSharpRegularCrease ||
                     selectInfSharpRegularCorner ||
                     selectInfSharpIrregularDart ||
                     selectInfSharpIrregularCrease ||
                     selectInfSharpIrregularCorner ||
                     selectUnisolatedInteriorEdge ||
                     selectNonManifold ||
                     selectFVarFeatures);
        }

        public FeatureMask(TopologyRefiner.AdaptiveOptions options, int regFaceSize)
        {
            selectXOrdinaryInterior = false;
            selectXOrdinaryBoundary = false;
            selectSemiSharpSingle = false;
            selectSemiSharpNonSingle = false;
            selectInfSharpRegularCrease = false;
            selectInfSharpRegularCorner = false;
            selectInfSharpIrregularDart = false;
            selectInfSharpIrregularCrease = false;
            selectInfSharpIrregularCorner = false;
            selectUnisolatedInteriorEdge = false;
            selectNonManifold = false;
            selectFVarFeatures = false;

            Clear();
            InitializeFeatures(options, regFaceSize);
        }

        //  These are the two primary methods intended for use -- intialization via a set of Options
        //  and reduction of the subsequent feature set (which presumes prior initialization with the
        //  same set as give)
        //
        public void InitializeFeatures(TopologyRefiner.AdaptiveOptions options, int regFaceSize)
        {
            //
            //  Support for the "single-crease patch" case is limited to the subdivision scheme
            //  (currently only Catmull-Clark).  It has historically been applied to both semi-
            //  sharp and inf-sharp creases -- the semi-sharp application is still relevant,
            //  but the inf-sharp has been superceded.
            //
            //  The inf-sharp single-crease case now corresponds to an inf-sharp regular crease
            //  in the interior -- and since such regular creases on the boundary are never
            //  considered for selection (just as interior smoot regular faces are not), this
            //  feature is only relevant for the interior case.  So aside from it being used
            //  when regular inf-sharp features are all selected, it can also be used for the
            //  single-crease case.
            //
            bool useSingleCreasePatch = options.useSingleCreasePatch && (regFaceSize == 4);

            //  Extra-ordinary features (independent of the inf-sharp options):
            selectXOrdinaryInterior = true;
            selectXOrdinaryBoundary = true;

            //  Semi-sharp features -- the regular single crease case and all others:
            selectSemiSharpSingle = !useSingleCreasePatch;
            selectSemiSharpNonSingle = true;

            //  Inf-sharp features -- boundary extra-ordinary vertices are irreg creases:
            selectInfSharpRegularCrease = !(options.useInfSharpPatch || useSingleCreasePatch);
            selectInfSharpRegularCorner = !options.useInfSharpPatch;
            selectInfSharpIrregularDart = true;
            selectInfSharpIrregularCrease = true;
            selectInfSharpIrregularCorner = true;

            selectUnisolatedInteriorEdge = useSingleCreasePatch && !options.useInfSharpPatch;

            selectNonManifold = true;
            selectFVarFeatures = options.considerFVarChannels;
        }

        public void ReduceFeatures(TopologyRefiner.AdaptiveOptions options)
        {
            //  Disable typical xordinary vertices:
            selectXOrdinaryInterior = false;
            selectXOrdinaryBoundary = false;

            //  If minimizing inf-sharp patches, disable all but sharp/corner irregularities
            if (options.useInfSharpPatch)
            {
                selectInfSharpRegularCrease = false;
                selectInfSharpRegularCorner = false;
                selectInfSharpIrregularDart = false;
                selectInfSharpIrregularCrease = false;
            }
        }

        public bool selectXOrdinaryInterior;
        public bool selectXOrdinaryBoundary;

        public bool selectSemiSharpSingle;
        public bool selectSemiSharpNonSingle;

        public bool selectInfSharpRegularCrease;
        public bool selectInfSharpRegularCorner;
        public bool selectInfSharpIrregularDart;
        public bool selectInfSharpIrregularCrease;
        public bool selectInfSharpIrregularCorner;

        public bool selectUnisolatedInteriorEdge;

        public bool selectNonManifold;
        public bool selectFVarFeatures;
    }
}

namespace OpenSubdiv.Far
{
    using VTag = OpenSubdiv.Vtr.Internal.Level.VTag;
    using FeatureMask = OpenSubdiv.Far.Internal.FeatureMask;

    /// \brief Stores topology data for a specified set of refinement options.
    ///
    public class TopologyRefiner
    {
        //
        //  Relatively trivial construction/destruction -- the base level (level[0]) needs
        //  to be explicitly initialized after construction and refinement then applied
        //
        /// \brief Constructor
        public TopologyRefiner(SchemeType schemeType, Sdc.Options schemeOptions = default)
        {
            _subdivType = schemeType;
            _subdivOptions = schemeOptions;
            _isUniform = true;
            _hasHoles = false;
            _hasIrregFaces = false;
            _regFaceSize = SchemeTypeTraits.GetRegularFaceSize(schemeType);
            _maxLevel = 0;
            _uniformOptions = new UniformOptions(0);
            _adaptiveOptions = new AdaptiveOptions(0);
            _totalVertices = 0;
            _totalEdges = 0;
            _totalFaces = 0;
            _totalFaceVertices = 0;
            _maxValence = 0;
            _baseLevelOwned = true;

            //  Need to revisit allocation scheme here -- want to use smart-ptrs for these
            //  but will probably have to settle for explicit new/delete...
            _levels = new List<Level>(10);
            _refinements = new List<Refinement>();
            _farLevels = new List<TopologyLevel>(10);
            _levels.Add(new Level());
            assembleFarLevels();
        }

        //
        //  The copy constructor is protected and used by the factory to create a new instance
        //  from only the base level of the given instance -- it does not create a full copy.
        //  So members reflecting any refinement are default-initialized while those dependent
        //  on the base level are copied or explicitly initialized after its assignment.
        //
        public TopologyRefiner(TopologyRefiner source)
        {
            _subdivType = source._subdivType;
            _subdivOptions = source._subdivOptions;
            _isUniform = true;
            _hasHoles = source._hasHoles;
            _hasIrregFaces = source._hasIrregFaces;
            _regFaceSize = source._regFaceSize;
            _maxLevel = 0;
            _uniformOptions = new UniformOptions(0);
            _adaptiveOptions = new AdaptiveOptions(0);
            _baseLevelOwned = false;

            _levels = new List<Level>(10);
            _refinements = new List<Refinement>();
            _farLevels = new List<TopologyLevel>(10);
            _levels.Add(source._levels[0]);
            initializeInventory();

            assembleFarLevels();
        }

        //  The destructor (deleting the levels and refinements) has no equivalent in C#:
        //  they are released by the garbage collector.

        /// \brief Returns the subdivision scheme type
        public SchemeType GetSchemeType() { return _subdivType; }

        /// \brief Returns the subdivision scheme options
        public Sdc.Options GetSchemeOptions() { return _subdivOptions; }

        /// \brief Returns true if uniform refinement has been applied
        public bool IsUniform() { return _isUniform; }

        /// \brief Returns the number of refinement levels
        public int GetNumLevels() { return _farLevels.Count; }

        /// \brief Returns the highest level of refinement
        public int GetMaxLevel() { return _maxLevel; }

        /// \brief Returns the maximum vertex valence in all levels
        public int GetMaxValence() { return _maxValence; }

        /// \brief Returns true if faces have been tagged as holes
        public bool HasHoles() { return _hasHoles; }

        /// \brief Returns the total number of vertices in all levels
        public int GetNumVerticesTotal() { return _totalVertices; }

        /// \brief Returns the total number of edges in all levels
        public int GetNumEdgesTotal() { return _totalEdges; }

        /// \brief Returns the total number of edges in all levels
        public int GetNumFacesTotal() { return _totalFaces; }

        /// \brief Returns the total number of face vertices in all levels
        public int GetNumFaceVerticesTotal() { return _totalFaceVertices; }

        /// \brief Returns a handle to access data specific to a particular level
        public TopologyLevel GetLevel(int level) { return _farLevels[level]; }

        //
        // Uniform refinement
        //

        /// \brief Uniform refinement options
        public struct UniformOptions
        {
            public UniformOptions(int level)
            {
                refinementLevel = (uint)(level & 0xf);
                orderVerticesFromFacesFirst = false;
                fullTopologyInLastLevel = false;
            }

            /// \brief Set the level of refinement
            public void SetRefinementLevel(int level) { refinementLevel = (uint)(level & 0xf); }

            public uint refinementLevel;                 ///< Number of refinement iterations (4 bits)
            public bool orderVerticesFromFacesFirst;     ///< Order child vertices from faces first
            public bool fullTopologyInLastLevel;         ///< Skip topological relationships in the last
                                                         ///< level of refinement that are not needed for
                                                         ///< interpolation (keep false if using limit).
        }

        /// \brief Refine the topology uniformly
        public void RefineUniform(UniformOptions options)
        {
            if (_levels[0].getNumVertices() == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefiner::RefineUniform() -- base level is uninitialized.");
                return;
            }
            if (_refinements.Count != 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefiner::RefineUniform() -- previous refinements already applied.");
                return;
            }

            //
            //  Allocate the stack of levels and the refinements between them:
            //
            _uniformOptions = options;

            _isUniform = true;
            _maxLevel = (int)options.refinementLevel;

            Split splitType = SchemeTypeTraits.GetTopologicalSplitType(_subdivType);

            //
            //  Initialize refinement options for Vtr -- adjusting full-topology for the last level:
            //
            Refinement.Options refineOptions = new Refinement.Options();
            refineOptions._sparse = false;
            refineOptions._faceVertsFirst = options.orderVerticesFromFacesFirst;

            for (int i = 1; i <= (int)options.refinementLevel; ++i)
            {
                refineOptions._minimalTopology =
                    options.fullTopologyInLastLevel ? false : (i == (int)options.refinementLevel);

                Level parentLevel = getLevel(i - 1);
                Level childLevel = new Level();

                Refinement refinement = null;
                if (splitType == Split.SPLIT_TO_QUADS)
                {
                    refinement = new QuadRefinement(parentLevel, childLevel, _subdivOptions);
                }
                else
                {
                    refinement = new TriRefinement(parentLevel, childLevel, _subdivOptions);
                }
                refinement.refine(refineOptions);

                appendLevel(childLevel);
                appendRefinement(refinement);
            }
            assembleFarLevels();
        }

        /// \brief Returns the options specified on refinement
        public UniformOptions GetUniformOptions() { return _uniformOptions; }

        //
        // Adaptive refinement
        //

        /// \brief Adaptive refinement options
        public struct AdaptiveOptions
        {
            public AdaptiveOptions(int level)
            {
                isolationLevel = (uint)(level & 0xf);
                secondaryLevel = 0xf;
                useSingleCreasePatch = false;
                useInfSharpPatch = false;
                considerFVarChannels = false;
                orderVerticesFromFacesFirst = false;
            }

            /// \brief Set isolation level
            public void SetIsolationLevel(int level) { isolationLevel = (uint)(level & 0xf); }

            /// \brief Set secondary level
            public void SetSecondaryLevel(int level) { secondaryLevel = (uint)(level & 0xf); }

            public uint isolationLevel;                  ///< Number of iterations applied to isolate extraordinary vertices and creases (4 bits)
            public uint secondaryLevel;                  ///< Shallower level to stop isolation of smooth irregular features (4 bits)
            public bool useSingleCreasePatch;            ///< Use 'single-crease' patch and stop isolation where applicable
            public bool useInfSharpPatch;                ///< Use infinitely sharp patches and stop isolation where applicable
            public bool considerFVarChannels;            ///< Inspect face-varying channels and isolate when irregular features present
            public bool orderVerticesFromFacesFirst;     ///< Order child vertices from faces first instead of vertices first
        }

        /// \brief Feature Adaptive topological refinement
        public void RefineAdaptive(AdaptiveOptions options,
                                   ConstIndexArray baseFacesToRefine = default)
        {
            if (_levels[0].getNumVertices() == 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefiner::RefineAdaptive() -- base level is uninitialized.");
                return;
            }
            if (_refinements.Count != 0)
            {
                Errors.Error(ErrorType.FAR_RUNTIME_ERROR,
                    "Failure in TopologyRefiner::RefineAdaptive() -- previous refinements already applied.");
                return;
            }

            //
            //  Initialize member and local variables from the adaptive options:
            //
            _isUniform = false;
            _adaptiveOptions = options;

            //
            //  Initialize the feature-selection options based on given options -- with two sets
            //  of levels isolating different sets of features, initialize the two feature sets
            //  up front and use the appropriate one for each level:
            //
            int nonLinearScheme = SchemeTypeTraits.GetLocalNeighborhoodSize(_subdivType);

            int shallowLevel = Math.Min((int)options.secondaryLevel, (int)options.isolationLevel);
            int deeperLevel = (int)options.isolationLevel;

            int potentialMaxLevel = (nonLinearScheme != 0) ? deeperLevel : (_hasIrregFaces ? 1 : 0);

            FeatureMask moreFeaturesMask = new FeatureMask(options, _regFaceSize);
            FeatureMask lessFeaturesMask = moreFeaturesMask;

            if (shallowLevel < potentialMaxLevel)
            {
                lessFeaturesMask.ReduceFeatures(options);
            }

            //
            //  If face-varying channels are considered, make sure non-linear channels are present
            //  and turn off consideration if none present:
            //
            if (moreFeaturesMask.selectFVarFeatures && (nonLinearScheme != 0))
            {
                bool nonLinearChannelsPresent = false;
                for (int channel = 0; channel < _levels[0].getNumFVarChannels(); ++channel)
                {
                    nonLinearChannelsPresent |= !_levels[0].getFVarLevel(channel).isLinear();
                }
                if (!nonLinearChannelsPresent)
                {
                    moreFeaturesMask.selectFVarFeatures = false;
                    lessFeaturesMask.selectFVarFeatures = false;
                }
            }

            //
            //  Initialize refinement options for Vtr -- full topology is always generated in
            //  the last level as expected usage is for patch retrieval:
            //
            Refinement.Options refineOptions = new Refinement.Options();

            refineOptions._sparse = true;
            refineOptions._minimalTopology = false;
            refineOptions._faceVertsFirst = options.orderVerticesFromFacesFirst;

            Split splitType = SchemeTypeTraits.GetTopologicalSplitType(_subdivType);

            for (int i = 1; i <= potentialMaxLevel; ++i)
            {
                Level parentLevel = getLevel(i - 1);
                Level childLevel = new Level();

                Refinement refinement = null;
                if (splitType == Split.SPLIT_TO_QUADS)
                {
                    refinement = new QuadRefinement(parentLevel, childLevel, _subdivOptions);
                }
                else
                {
                    refinement = new TriRefinement(parentLevel, childLevel, _subdivOptions);
                }

                //
                //  Initialize a Selector to mark a sparse set of components for refinement -- choose
                //  the feature selection mask appropriate to the level:
                //
                SparseSelector selector = new SparseSelector(refinement);

                FeatureMask levelFeatures = (i <= shallowLevel) ? moreFeaturesMask
                                                                : lessFeaturesMask;

                if (i > 1)
                {
                    selectFeatureAdaptiveComponents(selector, levelFeatures, default);
                }
                else if (nonLinearScheme != 0)
                {
                    selectFeatureAdaptiveComponents(selector, levelFeatures, baseFacesToRefine);
                }
                else
                {
                    selectLinearIrregularFaces(selector, baseFacesToRefine);
                }

                if (selector.isSelectionEmpty())
                {
                    break;
                }
                else
                {
                    refinement.refine(refineOptions);

                    appendLevel(childLevel);
                    appendRefinement(refinement);
                }
            }
            _maxLevel = _refinements.Count;

            assembleFarLevels();
        }

        /// \brief Returns the options specified on refinement
        public AdaptiveOptions GetAdaptiveOptions() { return _adaptiveOptions; }

        /// \brief Unrefine the topology, keeping only the base level to save memory.
        public void Unrefine()
        {
            if (_levels.Count != 0)
            {
                _levels.RemoveRange(1, _levels.Count - 1);
                initializeInventory();
            }
            _refinements.Clear();
            _maxLevel = 0;

            assembleFarLevels();
        }

        //@{
        ///  @name Accessors to the topology information:

        /// \brief Returns the number of face-varying channels in the tables
        public int GetNumFVarChannels()
        {
            return _levels[0].getNumFVarChannels();
        }

        /// \brief Returns the face-varying interpolation rule set for a given channel
        public Sdc.Options.FVarLinearInterpolation GetFVarLinearInterpolation(int channel = 0)
        {
            return _levels[0].getFVarOptions(channel).GetFVarLinearInterpolation();
        }

        /// \brief Returns the total number of face-varying values in all levels
        public int GetNumFVarValuesTotal(int channel = 0)
        {
            int sum = 0;
            for (int i = 0; i < (int)_levels.Count; ++i)
            {
                sum += _levels[i].getNumFVarValues(channel);
            }
            return sum;
        }

        //@}

        //  Levels and Refinements available internally (avoids need for more friends)
        public Level getLevel(int l) { return _levels[l]; }

        public Refinement getRefinement(int l) { return _refinements[l]; }

        //
        //  Initializing and updating the component inventory:
        //
        public void initializeInventory()
        {
            if (_levels.Count != 0)
            {
                System.Diagnostics.Debug.Assert(_levels.Count == 1);

                Level baseLevel = _levels[0];

                _totalVertices = baseLevel.getNumVertices();
                _totalEdges = baseLevel.getNumEdges();
                _totalFaces = baseLevel.getNumFaces();
                _totalFaceVertices = baseLevel.getNumFaceVerticesTotal();

                _maxValence = baseLevel.getMaxValence();
            }
            else
            {
                _totalVertices = 0;
                _totalEdges = 0;
                _totalFaces = 0;
                _totalFaceVertices = 0;

                _maxValence = 0;
            }
        }

        public void updateInventory(Level newLevel)
        {
            _totalVertices += newLevel.getNumVertices();
            _totalEdges += newLevel.getNumEdges();
            _totalFaces += newLevel.getNumFaces();
            _totalFaceVertices += newLevel.getNumFaceVerticesTotal();

            _maxValence = Math.Max(_maxValence, newLevel.getMaxValence());
        }

        public void appendLevel(Level newLevel)
        {
            _levels.Add(newLevel);

            updateInventory(newLevel);
        }

        public void appendRefinement(Refinement newRefinement)
        {
            _refinements.Add(newRefinement);
        }

        public void assembleFarLevels()
        {
            //  std::vector<TopologyLevel>::resize(): keep existing entries, default-construct new ones
            while (_farLevels.Count > _levels.Count)
            {
                _farLevels.RemoveAt(_farLevels.Count - 1);
            }
            while (_farLevels.Count < _levels.Count)
            {
                _farLevels.Add(new TopologyLevel());
            }

            _farLevels[0]._refToParent = null;
            _farLevels[0]._level = _levels[0];
            _farLevels[0]._refToChild = null;

            int nRefinements = (int)_refinements.Count;
            if (nRefinements != 0)
            {
                _farLevels[0]._refToChild = _refinements[0];

                for (int i = 1; i < nRefinements; ++i)
                {
                    _farLevels[i]._refToParent = _refinements[i - 1];
                    _farLevels[i]._level = _levels[i];
                    _farLevels[i]._refToChild = _refinements[i];
                }

                _farLevels[nRefinements]._refToParent = _refinements[nRefinements - 1];
                _farLevels[nRefinements]._level = _levels[nRefinements];
                _farLevels[nRefinements]._refToChild = null;
            }
        }

        //
        //  Local utility functions for selecting features in faces for adaptive refinement:
        //
        //  First are a couple of low-level utility methods to perform the same analysis
        //  at a corner or the entire face for specific detection of inf-sharp or boundary
        //  features.  These are shared between the analysis of the main face and those in
        //  face-varying channels (which only differ from the main face in the presence of
        //  face-varying boundaries).
        //
        //  The first can be applied equally to an individual corner or to the entire face
        //  (using its composite tag).  The second applies to the entire face, making use
        //  of the first, and is the main entry point for dealng with inf-sharp features.
        //
        //  Note we can use the composite tag here even though it arises from all corners
        //  of the face and so does not represent a specific corner.  When at least one
        //  smooth interior vertex exists, it limits the combinations that can exist on the
        //  remaining corners (though quads and tris cannot be treated equally here).
        //
        //  If any inf-sharp features are to be selected, identify them first as irregular
        //  or not, then qualify them more specifically.  (Remember that a regular vertex
        //  may have its neighboring faces partitioned into irregular regions in the
        //  presence of inf-sharp edges.  Similarly an irregular vertex may have its
        //  neighborhood partitioned into regular regions.)
        //
        private static bool doesInfSharpVTagHaveFeatures(VTag compVTag,
                                                         FeatureMask featureMask)
        {
            //  Note that even though the given VTag may represent an individual corner, we
            //  use more general bitwise tests here (particularly the Rule) so that we can
            //  pass in a composite tag for the entire face and have the same tests applied:
            //
            if (compVTag._infIrregular != 0)
            {
                if ((compVTag._rule & (uint)Crease.Rule.RULE_CORNER) != 0)
                {
                    return featureMask.selectInfSharpIrregularCorner;
                }
                else if ((compVTag._rule & (uint)Crease.Rule.RULE_CREASE) != 0)
                {
                    return (compVTag._boundary != 0) ? featureMask.selectXOrdinaryBoundary :
                                                       featureMask.selectInfSharpIrregularCrease;
                }
                else if ((compVTag._rule & (uint)Crease.Rule.RULE_DART) != 0)
                {
                    return featureMask.selectInfSharpIrregularDart;
                }
            }
            else if (compVTag._boundary != 0)
            {
                //  Remember that regular boundary features should never be selected, except
                //  for a boundary crease sharpened (and so a Corner) by an interior edge:

                if ((compVTag._rule & (uint)Crease.Rule.RULE_CORNER) != 0)
                {
                    return (compVTag._corner != 0) ? false : featureMask.selectInfSharpRegularCorner;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                if ((compVTag._rule & (uint)Crease.Rule.RULE_CORNER) != 0)
                {
                    return featureMask.selectInfSharpRegularCorner;
                }
                else
                {
                    return featureMask.selectInfSharpRegularCrease;
                }
            }
            return false;
        }

        private static bool doesInfSharpFaceHaveFeatures(VTag compVTag,
                                                         VTag[] vTags, int numVerts,
                                                         FeatureMask featureMask)
        {
            //
            //  For quads, if at least one smooth corner of a regular face, features
            //  are isolated enough to make use of the composite tag alone (unless
            //  boundary isolation is enabled, in which case trivially return).
            //
            //  For tris, the presence of boundaries creates more ambiguity, so we
            //  need to exclude that case and inspect corner features individually.
            //
            bool isolateQuadBoundaries = false;

            bool atLeastOneSmoothCorner = ((compVTag._rule & (uint)Crease.Rule.RULE_SMOOTH) != 0);
            if (numVerts == 4)
            {
                if (atLeastOneSmoothCorner)
                {
                    return doesInfSharpVTagHaveFeatures(compVTag, featureMask);
                }
                else if (isolateQuadBoundaries)
                {
                    return true;
                }
                else if (featureMask.selectUnisolatedInteriorEdge)
                {
                    //  Needed for single-crease approximation to inf-sharp interior edge:
                    for (int i = 0; i < 4; ++i)
                    {
                        if ((vTags[i]._infSharpEdges != 0) && (vTags[i]._boundary == 0))
                        {
                            return true;
                        }
                    }
                }
            }
            else
            {
                if (atLeastOneSmoothCorner && (compVTag._boundary == 0))
                {
                    return doesInfSharpVTagHaveFeatures(compVTag, featureMask);
                }
            }

            for (int i = 0; i < numVerts; ++i)
            {
                if ((vTags[i]._rule & (uint)Crease.Rule.RULE_SMOOTH) == 0)
                {
                    if (doesInfSharpVTagHaveFeatures(vTags[i], featureMask))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        //
        //  This is the core method/function for analyzing a face and deciding whether or not
        //  to included it during feature-adaptive refinement.
        //
        //  Topological analysis of the face exploits tags that are applied to corner vertices
        //  and carried through the refinement hierarchy.  The tags were designed with this
        //  in mind and also to be combined via bitwise-OR to make collective decisions about
        //  the neighborhood of the entire face.
        //
        //  After a few trivial acceptances/rejections, feature detection is divided up into
        //  semi-sharp and inf-sharp cases -- note that both may be present, but semi-sharp
        //  features have an implicit precedence until they decay and so are handled first.
        //  They are also fairly trivial to deal with (most often requiring selection) while
        //  the presence of boundaries and additional options complicates the inf-sharp case.
        //  Since the inf-sharp logic needs to be applied in face-varying cases, it exists in
        //  a separate method.
        //
        //  This was originally written specific to the quad-centric Catmark scheme and was
        //  since generalized to support Loop given the enhanced tagging of components based
        //  on the scheme.  Any enhancements here should be aware of the intended generality.
        //  Ultimately it may not be worth trying to keep this general and we will be better
        //  off specializing it for each scheme.  The fact that this method is intimately tied
        //  to patch generation also begs for it to become part of a class that encompasses
        //  both the feature adaptive tagging and the identification of the intended patches
        //  that result from it.
        //
        private static bool doesFaceHaveFeatures(Level level, int face,
                                                 FeatureMask featureMask, int regFaceSize)
        {
            ConstIndexArray fVerts = level.getFaceVertices(face);

            //  Irregular faces (base level) are unconditionally included:
            if (fVerts.size() != regFaceSize)
            {
                return true;
            }

            //  Gather and combine the VTags:
            VTag[] vTags = new VTag[4];
            level.getFaceVTags(face, vTags);

            VTag compFaceVTag = VTag.BitwiseOr(vTags, fVerts.size());

            //  Faces incident irregular faces (base level) are unconditionally included:
            if (compFaceVTag._incidIrregFace != 0)
            {
                return true;
            }
            //  Incomplete faces (incomplete neighborhood) are unconditionally excluded:
            if (compFaceVTag._incomplete != 0)
            {
                return false;
            }

            //  Select non-manifold features if specified, otherwise treat as inf-sharp:
            if ((compFaceVTag._nonManifold != 0) && featureMask.selectNonManifold)
            {
                return true;
            }

            //  Select (smooth) xord vertices if specified, boundaries handled with inf-sharp:
            if ((compFaceVTag._xordinary != 0) && featureMask.selectXOrdinaryInterior)
            {
                if (compFaceVTag._rule == (uint)Crease.Rule.RULE_SMOOTH)
                {
                    return true;
                }
                else if (level.getDepth() < 2)
                {
                    for (int i = 0; i < fVerts.size(); ++i)
                    {
                        if ((vTags[i]._xordinary != 0) && (vTags[i]._rule == (uint)Crease.Rule.RULE_SMOOTH))
                        {
                            return true;
                        }
                    }
                }
            }

            //  If all smooth corners, no remaining features to select (x-ordinary dealt with):
            if (compFaceVTag._rule == (uint)Crease.Rule.RULE_SMOOTH)
            {
                return false;
            }

            //  Semi-sharp features -- select all immediately or test the single-crease case:
            if ((compFaceVTag._semiSharp != 0) || (compFaceVTag._semiSharpEdges != 0))
            {
                if (featureMask.selectSemiSharpSingle && featureMask.selectSemiSharpNonSingle)
                {
                    return true;
                }
                else if (level.isSingleCreasePatch(face))
                {
                    return featureMask.selectSemiSharpSingle;
                }
                else
                {
                    return featureMask.selectSemiSharpNonSingle;
                }
            }

            //  Inf-sharp features (including boundaries) -- delegate to shared method:
            if ((compFaceVTag._infSharp != 0) || (compFaceVTag._infSharpEdges != 0))
            {
                return doesInfSharpFaceHaveFeatures(compFaceVTag, vTags, fVerts.size(), featureMask);
            }
            return false;
        }

        //
        //  Analyzing the face-varying topology for selection is considerably simpler that
        //  for the face and its vertices -- in part due to the fact that these faces lie on
        //  face-varying boundaries, and also due to assumptions about prior inspection:
        //
        //      - it is assumed the face topologgy does not match, so the face must lie on
        //        a FVar boundary, i.e. inf-sharp
        //
        //      - it is assumed the face vertices were already inspected, so cases such as
        //        semi-sharp or smooth interior x-ordinary features have already triggered
        //        selection
        //
        //  That leaves the inspection of inf-sharp features, for the tags from the face
        //  varying channel -- code that is shared with the main face.
        //
        private static bool doesFaceHaveDistinctFaceVaryingFeatures(Level level, int face,
                                    FeatureMask featureMask, int fvarChannel)
        {
            ConstIndexArray fVerts = level.getFaceVertices(face);

            System.Diagnostics.Debug.Assert(!level.doesFaceFVarTopologyMatch(face, fvarChannel));

            //  We can't use the composite VTag for the face here as it only includes the FVar
            //  values specific to this face.  We need to account for all FVar values around
            //  each corner of the face -- including those in potentially completely disjoint
            //  sets -- to ensure that adjacent faces remain compatibly refined (i.e. differ
            //  by only one level), so we use the composite tags for the corner vertices:
            //
            VTag[] vTags = new VTag[4];

            for (int i = 0; i < fVerts.size(); ++i)
            {
                vTags[i] = level.getVertexCompositeFVarVTag(fVerts[i], fvarChannel);
            }
            VTag compVTag = VTag.BitwiseOr(vTags, fVerts.size());

            //  Incomplete faces (incomplete neighborhood) are unconditionally excluded:
            if (compVTag._incomplete != 0)
            {
                return false;
            }

            //  Select non-manifold features if specified, otherwise treat as inf-sharp:
            if ((compVTag._nonManifold != 0) && featureMask.selectNonManifold)
            {
                return true;
            }

            //  Any remaining locally extra-ordinary face-varying boundaries warrant selection:
            if ((compVTag._xordinary != 0) && featureMask.selectXOrdinaryInterior)
            {
                return true;
            }

            //  Given faces with differing FVar topology are on boundaries, defer to inf-sharp:
            return doesInfSharpFaceHaveFeatures(compVTag, vTags, fVerts.size(), featureMask);
        }

        //
        //   Method for selecting components for sparse refinement based on the feature-adaptive needs
        //   of patch generation.
        //
        //   It assumes we have a freshly initialized SparseSelector (i.e. nothing already selected)
        //   and will select all relevant topological features for inclusion in the subsequent sparse
        //   refinement.
        //
        public void selectFeatureAdaptiveComponents(SparseSelector selector,
                                                    FeatureMask featureMask,
                                                    ConstIndexArray facesToRefine)
        {
            //
            //  Inspect each face and the properties tagged at all of its corners:
            //
            Level level = selector.getRefinement().parent();

            int numFacesToRefine = (facesToRefine.size() != 0) ? facesToRefine.size() : level.getNumFaces();

            int numFVarChannels = featureMask.selectFVarFeatures ? level.getNumFVarChannels() : 0;

            for (int fIndex = 0; fIndex < numFacesToRefine; ++fIndex)
            {
                int face = (facesToRefine.size() != 0) ? facesToRefine[fIndex] : (int)fIndex;

                if (HasHoles() && level.isFaceHole(face)) continue;

                //
                //  Test if the face has any of the specified features present.  If not, and FVar
                //  channels are to be considered, look for features in the FVar channels:
                //
                bool selectFace = doesFaceHaveFeatures(level, face, featureMask, _regFaceSize);

                if (!selectFace && featureMask.selectFVarFeatures)
                {
                    for (int channel = 0; !selectFace && (channel < numFVarChannels); ++channel)
                    {
                        //  Only test the face for this channel if the topology does not match:
                        if (!level.doesFaceFVarTopologyMatch(face, channel))
                        {
                            selectFace = doesFaceHaveDistinctFaceVaryingFeatures(
                                                level, face, featureMask, channel);
                        }
                    }
                }
                if (selectFace)
                {
                    selector.selectFace(face);
                }
            }
        }

        public void selectLinearIrregularFaces(SparseSelector selector,
                                               ConstIndexArray facesToRefine)
        {
            //
            //  Inspect each face and select only irregular faces:
            //
            Level level = selector.getRefinement().parent();

            int numFacesToRefine = (facesToRefine.size() != 0) ? facesToRefine.size() : level.getNumFaces();

            for (int fIndex = 0; fIndex < numFacesToRefine; ++fIndex)
            {
                int face = (facesToRefine.size() != 0) ? facesToRefine[fIndex] : (int)fIndex;

                if (HasHoles() && level.isFaceHole(face)) continue;

                if (level.getFaceVertices(face).size() != _regFaceSize)
                {
                    selector.selectFace(face);
                }
            }
        }

        //  Members (private in C++; public here in place of the C++ friends):
        public SchemeType _subdivType;
        public Sdc.Options _subdivOptions;

        public bool _isUniform;
        public bool _hasHoles;
        public bool _hasIrregFaces;
        public int _regFaceSize;      // 3 bits
        public int _maxLevel;         // 4 bits

        //  Options assigned on refinement:
        public UniformOptions _uniformOptions;
        public AdaptiveOptions _adaptiveOptions;

        //  Cumulative properties of all levels:
        public int _totalVertices;
        public int _totalEdges;
        public int _totalFaces;
        public int _totalFaceVertices;
        public int _maxValence;

        //  Note the base level may be shared with another instance
        public bool _baseLevelOwned;

        public List<Level> _levels;
        public List<Refinement> _refinements;

        public List<TopologyLevel> _farLevels;
    }
}
