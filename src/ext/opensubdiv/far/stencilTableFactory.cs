//
//   Copyright 2013 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Vtr;
using OpenSubdiv.Far.Internal;
using ConstIndexArray = OpenSubdiv.Vtr.ConstArray<int>;

namespace OpenSubdiv.Far
{
    ///
    /// \brief A specialized factory for StencilTables
    ///
    /// The StencilTableFactoryReal creates StencilTables from the topology refiner
    /// (and, optionally, spliced tables).
    ///
    public class StencilTableFactoryReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public enum Mode
        {
            INTERPOLATE_VERTEX = 0,           ///< vertex primvar stencils
            INTERPOLATE_VARYING,              ///< varying primvar stencils
            INTERPOLATE_FACE_VARYING          ///< face-varying primvar stencils
        }

        //  C++ unscoped access to the enumerants (Options::interpolationMode is a uint bit-field):
        public const uint INTERPOLATE_VERTEX = (uint)Mode.INTERPOLATE_VERTEX;
        public const uint INTERPOLATE_VARYING = (uint)Mode.INTERPOLATE_VARYING;
        public const uint INTERPOLATE_FACE_VARYING = (uint)Mode.INTERPOLATE_FACE_VARYING;

        /// \brief Options controlling the creation of the StencilTable
        ///
        /// (The C++ bit-fields: interpolationMode 2 bits, maxLevel 4 bits; the 1-bit flags are bools.)
        public class Options
        {
            public Options()
            {
                interpolationMode = INTERPOLATE_VERTEX;
                generateOffsets = false;
                generateControlVerts = false;
                generateIntermediateLevels = true;
                factorizeIntermediateLevels = true;
                maxLevel = 10;
                fvarChannel = 0;
            }

            public uint interpolationMode;               ///< interpolation mode
            public bool generateOffsets;                 ///< populate optional "_offsets" field
            public bool generateControlVerts;            ///< generate stencils for control-vertices
            public bool generateIntermediateLevels;      ///< vertices at all levels or highest only
            public bool factorizeIntermediateLevels;     ///< accumulate stencil weights from control
                                                         ///< vertices or from the stencils of the previous level
            public uint maxLevel;                        ///< generate stencils up to 'maxLevel'
            public uint fvarChannel;                     ///< face-varying channel to use
        }

        //
        // StencilTable factory
        //

        /// \brief Instantiates StencilTable from TopologyRefiner that have been
        ///        refined uniformly or adaptively.
        ///
        /// @param refiner  The TopologyRefiner containing the topology
        ///
        /// @param options  Options controlling the creation of the table
        ///
        public static StencilTableReal<REAL> Create(
            TopologyRefiner refiner, Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            bool interpolateVertex = options.interpolationMode == INTERPOLATE_VERTEX;
            bool interpolateVarying = options.interpolationMode == INTERPOLATE_VARYING;
            bool interpolateFaceVarying = options.interpolationMode == INTERPOLATE_FACE_VARYING;

            int numControlVertices = !interpolateFaceVarying
                ? refiner.GetLevel(0).GetNumVertices()
                : refiner.GetLevel(0).GetNumFVarValues((int)options.fvarChannel);

            int maxlevel = Math.Min((int)options.maxLevel, refiner.GetMaxLevel());
            if (maxlevel == 0 && (!options.generateControlVerts))
            {
                StencilTableReal<REAL> result0 = new StencilTableReal<REAL>();
                result0._numControlVertices = numControlVertices;
                return result0;
            }

            StencilBuilder<REAL> builder = new StencilBuilder<REAL>(numControlVertices,
                                /*genControlVerts*/ true,
                                /*compactWeights*/  true);

            //
            // Interpolate stencils for each refinement level
            //
            PrimvarRefinerReal<REAL> primvarRefiner = new PrimvarRefinerReal<REAL>(refiner);

            StencilBuilder<REAL>.Index srcIndex = new StencilBuilder<REAL>.Index(builder, 0);
            StencilBuilder<REAL>.Index dstIndex = new StencilBuilder<REAL>.Index(builder, numControlVertices);

            for (int level = 1; level <= maxlevel; ++level)
            {
                if (interpolateVertex)
                {
                    primvarRefiner.Interpolate<StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index,
                                               StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index>(level, srcIndex, dstIndex);
                }
                else if (interpolateVarying)
                {
                    primvarRefiner.InterpolateVarying<StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index,
                                                      StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index>(level, srcIndex, dstIndex);
                }
                else
                {
                    primvarRefiner.InterpolateFaceVarying<StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index,
                                                          StencilBuilder<REAL>.Index, StencilBuilder<REAL>.Index>(level, srcIndex, dstIndex, (int)options.fvarChannel);
                }

                if (options.factorizeIntermediateLevels)
                {
                    srcIndex = dstIndex;
                }

                int dstVertex = !interpolateFaceVarying
                    ? refiner.GetLevel(level).GetNumVertices()
                    : refiner.GetLevel(level).GetNumFVarValues((int)options.fvarChannel);
                dstIndex = dstIndex[dstVertex];

                if (!options.factorizeIntermediateLevels)
                {
                    // All previous verts are considered as coarse verts, as a
                    // result, we don't update the srcIndex and update the coarse
                    // vertex count.
                    builder.SetCoarseVertCount(dstIndex.GetOffset());
                }
            }

            int firstOffset = numControlVertices;
            if (!options.generateIntermediateLevels)
                firstOffset = srcIndex.GetOffset();

            // Copy stencils from the StencilBuilder into the StencilTable.
            // Always initialize numControlVertices (useful for torus case)
            StencilTableReal<REAL> result =
                                new StencilTableReal<REAL>(numControlVertices,
                                                  builder.GetStencilOffsets(),
                                                  builder.GetStencilSizes(),
                                                  builder.GetStencilSources(),
                                                  builder.GetStencilWeights(),
                                                  options.generateControlVerts,
                                                  firstOffset);
            return result;
        }

        /// \brief Instantiates StencilTable by concatenating an array of existing
        ///        stencil tables.
        ///
        /// \note This factory checks that the tables point to the same set of
        ///       supporting control vertices - no re-indexing is done.
        ///       GetNumControlVertices() *must* return the same value for all input
        ///       tables.
        ///
        /// @param numTables  Number of input StencilTables
        ///
        /// @param tables     Array of input StencilTables
        ///
        public static StencilTableReal<REAL> Create(
            int numTables, StencilTableReal<REAL>[] tables)
        {
            // XXXtakahito:
            // This function returns NULL for empty inputs or erroneous condition.
            // It's convenient for skipping varying stencils etc, however,
            // other Create() API returns an empty stencil instead of NULL.
            // They need to be consistent.

            if ((numTables <= 0) || (tables == null))
            {
                return null;
            }

            int ncvs = -1,
                nstencils = 0,
                nelems = 0;

            for (int i = 0; i < numTables; ++i)
            {
                StencilTableReal<REAL> st = tables[i];
                // allow the tables could have a null entry.
                if (st == null) continue;

                if (ncvs >= 0 && st.GetNumControlVertices() != ncvs)
                {
                    return null;
                }
                ncvs = st.GetNumControlVertices();
                nstencils += st.GetNumStencils();
                nelems += (int)st.GetControlIndices().size();
            }

            if (ncvs == -1)
            {
                return null;
            }

            StencilTableReal<REAL> result = new StencilTableReal<REAL>();
            result.resize(nstencils, nelems);

            int[] sizes = result._sizes.Data;
            int sizesOffset = 0;
            int[] indices = result._indices.Data;
            int indicesOffset = 0;
            REAL[] weights = result._weights.Data;
            int weightsOffset = 0;
            for (int i = 0; i < numTables; ++i)
            {
                StencilTableReal<REAL> st = tables[i];
                if (st == null) continue;

                int st_nstencils = st.GetNumStencils(),
                    st_nelems = (int)st._indices.size();
                System.Array.Copy(st._sizes.Data, 0, sizes, sizesOffset, st_nstencils);
                System.Array.Copy(st._indices.Data, 0, indices, indicesOffset, st_nelems);
                System.Array.Copy(st._weights.Data, 0, weights, weightsOffset, st_nelems);

                sizesOffset += st_nstencils;
                indicesOffset += st_nelems;
                weightsOffset += st_nelems;
            }

            result._numControlVertices = ncvs;

            // have to re-generate offsets from scratch
            result.generateOffsets();

            return result;
        }

        /// \brief Utility function for splicing local point stencils.
        ///
        /// The function returns a new stencil table with the local point stencils
        /// appended to the base stencil table (see the C++ documentation).
        ///
        /// @param refiner            The TopologyRefiner containing the topology
        ///
        /// @param baseStencilTable   Input StencilTable for refined vertices
        ///
        /// @param localPointStencilTable
        ///                           StencilTable for the change of basis patch points.
        ///
        /// @param factorize          If true, factorize the local point stencils
        ///                           with the base stencils
        ///
        public static StencilTableReal<REAL> AppendLocalPointStencilTable(
            TopologyRefiner refiner,
            StencilTableReal<REAL> baseStencilTable,
            StencilTableReal<REAL> localPointStencilTable,
            bool factorize = true)
        {
            return appendLocalPointStencilTable(
                refiner,
                baseStencilTable,
                localPointStencilTable,
                /*channel*/-1,
                factorize);
        }

        /// \brief Same as AppendLocalPointStencilTable() for varying stencils
        public static StencilTableReal<REAL> AppendLocalPointStencilTableVarying(
            TopologyRefiner refiner,
            StencilTableReal<REAL> baseStencilTable,
            StencilTableReal<REAL> localPointStencilTable,
            bool factorize = true)
        {
            return AppendLocalPointStencilTable(
                refiner, baseStencilTable, localPointStencilTable, factorize);
        }

        /// \brief Same as AppendLocalPointStencilTable() for face-varying stencils
        public static StencilTableReal<REAL> AppendLocalPointStencilTableFaceVarying(
            TopologyRefiner refiner,
            StencilTableReal<REAL> baseStencilTable,
            StencilTableReal<REAL> localPointStencilTable,
            int channel = 0,
            bool factorize = true)
        {
            return appendLocalPointStencilTable(
                refiner,
                baseStencilTable,
                localPointStencilTable,
                channel,
                factorize);
        }

        // Generate stencils for the coarse control-vertices (single weight = 1.0f)
        public static void generateControlVertStencils(
            int numControlVerts,
            StencilReal<REAL> dst)
        {
            // Control vertices contribute a single index with a weight of 1.0
            for (int i = 0; i < numControlVerts; ++i)
            {
                dst._size[dst._sizeOffset] = 1;
                dst._indices[dst._indicesOffset] = i;
                dst._weights[dst._weightsOffset] = REAL.One;
                dst.Next();
            }
        }

        // Internal method to splice local point stencils
        public static StencilTableReal<REAL> appendLocalPointStencilTable(
            TopologyRefiner refiner,
            StencilTableReal<REAL> baseStencilTable,
            StencilTableReal<REAL> localPointStencilTable,
            int channel,
            bool factorize)
        {
            // require the local point stencils exist and be non-empty
            if ((localPointStencilTable == null) ||
                (localPointStencilTable.GetNumStencils() == 0))
            {
                return null;
            }

            int nControlVerts = channel < 0
                ? refiner.GetLevel(0).GetNumVertices()
                : refiner.GetLevel(0).GetNumFVarValues(channel);

            //  if no base stencils or empty, return copy of local point stencils
            if ((baseStencilTable == null) ||
                (baseStencilTable.GetNumStencils() == 0))
            {
                StencilTableReal<REAL> result0 =
                        new StencilTableReal<REAL>(localPointStencilTable);
                result0._numControlVertices = nControlVerts;
                return result0;
            }

            // baseStencilTable can be built with or without singular stencils
            // (single weight of 1.0f) as place-holders for coarse mesh vertices.

            int controlVertsIndexOffset = 0;
            int nBaseStencils = baseStencilTable.GetNumStencils();
            int nBaseStencilsElements = (int)baseStencilTable._indices.size();
            {
                int nverts = channel < 0
                    ? refiner.GetNumVerticesTotal()
                    : refiner.GetNumFVarValuesTotal(channel);
                if (nBaseStencils == nverts)
                {
                    // the table contains stencils for the control vertices
                    //
                    //  <-----------------  nverts ------------------>
                    //
                    //  +---------------+----------------------------+-----------------+
                    //  | control verts | refined verts   : (max lv) |   local points  |
                    //  +---------------+----------------------------+-----------------+
                    //  |          base stencil table                |    LP stencils  |
                    //  +--------------------------------------------+-----------------+
                    //                         ^                           /
                    //                          \_________________________/
                    //
                    //
                    controlVertsIndexOffset = 0;
                }
                else if (nBaseStencils == (nverts - nControlVerts))
                {
                    // the table does not contain stencils for the control vertices
                    //
                    //  <-----------------  nverts ------------------>
                    //                  <------ nBaseStencils ------->
                    //  +---------------+----------------------------+-----------------+
                    //  | control verts | refined verts   : (max lv) |   local points  |
                    //  +---------------+----------------------------+-----------------+
                    //                  |     base stencil table     |    LP stencils  |
                    //                  +----------------------------+-----------------+
                    //                                 ^                   /
                    //                                  \_________________/
                    //  <-------------->
                    //                 controlVertsIndexOffset
                    //
                    controlVertsIndexOffset = nControlVerts;
                }
                else
                {
                    // these are not the stencils you are looking for.
                    System.Diagnostics.Debug.Assert(false);
                    return null;
                }
            }

            // copy all local point stencils to proto stencils, and factorize if needed.
            int nLocalPointStencils = localPointStencilTable.GetNumStencils();
            int nLocalPointStencilsElements = 0;

            StencilBuilder<REAL> builder = new StencilBuilder<REAL>(nControlVerts,
                                /*genControlVerts*/ false,
                                /*compactWeights*/  factorize);
            StencilBuilder<REAL>.Index origin = new StencilBuilder<REAL>.Index(builder, 0);
            StencilBuilder<REAL>.Index dst = origin;
            StencilBuilder<REAL>.Index srcIdx = origin;

            for (int i = 0; i < nLocalPointStencils; ++i)
            {
                StencilReal<REAL> src = localPointStencilTable.GetStencil(i);
                dst = origin[i];
                for (int j = 0; j < src.GetSize(); ++j)
                {
                    int index = src.GetVertexIndices()[j];
                    REAL weight = src.GetWeights()[j];
                    if (StencilBuilderUtil.isWeightZero<REAL>(weight)) continue;

                    if (factorize)
                    {
                        dst.AddWithWeight(
                            // subtracting controlVertsIndex if the baseStencil doesn't
                            // include control vertices (see above diagram)
                            // since currently local point stencils are created with
                            // absolute indices including control (level=0) vertices.
                            baseStencilTable.GetStencil(index - controlVertsIndexOffset),
                            weight);
                    }
                    else
                    {
                        srcIdx = origin[index + controlVertsIndexOffset];
                        dst.AddWithWeight(srcIdx, weight);
                    }
                }
                nLocalPointStencilsElements += builder.GetNumVertsInStencil(i);
            }

            // create new stencil table
            StencilTableReal<REAL> result = new StencilTableReal<REAL>();
            result._numControlVertices = nControlVerts;
            result.resize(nBaseStencils + nLocalPointStencils,
                          nBaseStencilsElements + nLocalPointStencilsElements);

            int[] sizes = result._sizes.Data;
            int sizesOffset = 0;
            int[] indices = result._indices.Data;
            int indicesOffset = 0;
            REAL[] weights = result._weights.Data;
            int weightsOffset = 0;

            // put base stencils first
            System.Array.Copy(baseStencilTable._sizes.Data, 0, sizes, sizesOffset,
                              nBaseStencils);
            System.Array.Copy(baseStencilTable._indices.Data, 0, indices, indicesOffset,
                              nBaseStencilsElements);
            System.Array.Copy(baseStencilTable._weights.Data, 0, weights, weightsOffset,
                              nBaseStencilsElements);

            sizesOffset += nBaseStencils;
            indicesOffset += nBaseStencilsElements;
            weightsOffset += nBaseStencilsElements;

            // endcap stencils second
            for (int i = 0; i < nLocalPointStencils; ++i)
            {
                int size = builder.GetNumVertsInStencil(i);
                int idx = builder.GetStencilOffsets()[i];
                for (int j = 0; j < size; ++j)
                {
                    indices[indicesOffset++] = builder.GetStencilSources()[idx + j];
                    weights[weightsOffset++] = builder.GetStencilWeights()[idx + j];
                }
                sizes[sizesOffset++] = size;
            }

            // have to re-generate offsets from scratch
            result.generateOffsets();

            return result;
        }
    }

    ///
    /// \brief A specialized factory for LimitStencilTables
    ///
    /// The LimitStencilTableFactoryReal creates a LimitStencilTable from a
    /// TopologyRefiner and a set of locations on the limit surface.
    ///
    public class LimitStencilTableFactoryReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        // C++ calls the templated PatchTable::EvaluateBasis<REAL>; the C# PatchTable has
        // float and double overloads, so dispatch on REAL here (kind: 0 vertex, 1 face-varying, 2 varying).
        private static void evaluateBasisDispatch(PatchTable patchtable, int kind, PatchTable.PatchHandle handle,
            REAL s, REAL t, REAL[] wP, REAL[] wDs = null, REAL[] wDt = null,
            REAL[] wDss = null, REAL[] wDst = null, REAL[] wDtt = null, int channel = 0)
        {
            if (typeof(REAL) == typeof(float))
            {
                float fs = float.CreateChecked(s);
                float ft = float.CreateChecked(t);
                float[] fP = (float[])(object)wP;
                float[] fDs = (float[])(object)wDs;
                float[] fDt = (float[])(object)wDt;
                float[] fDss = (float[])(object)wDss;
                float[] fDst = (float[])(object)wDst;
                float[] fDtt = (float[])(object)wDtt;
                if (kind == 0)
                {
                    patchtable.EvaluateBasis(handle, fs, ft, fP, fDs, fDt, fDss, fDst, fDtt);
                }
                else if (kind == 1)
                {
                    patchtable.EvaluateBasisFaceVarying(handle, fs, ft, fP, fDs, fDt, fDss, fDst, fDtt, channel);
                }
                else
                {
                    patchtable.EvaluateBasisVarying(handle, fs, ft, fP, fDs, fDt, fDss, fDst, fDtt);
                }
            }
            else
            {
                double ds = double.CreateChecked(s);
                double dt = double.CreateChecked(t);
                double[] dP = (double[])(object)wP;
                double[] dDs = (double[])(object)wDs;
                double[] dDt = (double[])(object)wDt;
                double[] dDss = (double[])(object)wDss;
                double[] dDst = (double[])(object)wDst;
                double[] dDtt = (double[])(object)wDtt;
                if (kind == 0)
                {
                    patchtable.EvaluateBasis(handle, ds, dt, dP, dDs, dDt, dDss, dDst, dDtt);
                }
                else if (kind == 1)
                {
                    patchtable.EvaluateBasisFaceVarying(handle, ds, dt, dP, dDs, dDt, dDss, dDst, dDtt, channel);
                }
                else
                {
                    patchtable.EvaluateBasisVarying(handle, ds, dt, dP, dDs, dDt, dDss, dDst, dDtt);
                }
            }
        }

        public enum Mode
        {
            INTERPOLATE_VERTEX = 0,           ///< vertex primvar stencils
            INTERPOLATE_VARYING,              ///< varying primvar stencils
            INTERPOLATE_FACE_VARYING          ///< face-varying primvar stencils
        }

        //  C++ unscoped access to the enumerants (Options::interpolationMode is a uint bit-field):
        public const uint INTERPOLATE_VERTEX = (uint)Mode.INTERPOLATE_VERTEX;
        public const uint INTERPOLATE_VARYING = (uint)Mode.INTERPOLATE_VARYING;
        public const uint INTERPOLATE_FACE_VARYING = (uint)Mode.INTERPOLATE_FACE_VARYING;

        /// \brief Options controlling the creation of the LimitStencilTable
        public class Options
        {
            public Options()
            {
                interpolationMode = INTERPOLATE_VERTEX;
                generate1stDerivatives = true;
                generate2ndDerivatives = false;
                fvarChannel = 0;
            }

            public uint interpolationMode;               ///< interpolation mode
            public bool generate1stDerivatives;          ///< Generate weights for 1st derivatives
            public bool generate2ndDerivatives;          ///< Generate weights for 2nd derivatives
            public uint fvarChannel;                     ///< face-varying channel to use
        }

        /// \brief Descriptor for limit surface locations
        public class LocationArray
        {
            public LocationArray()
            {
                ptexIdx = -1;
                numLocations = 0;
                s = null;
                t = null;
            }

            public int ptexIdx;          ///< ptex face index
            public int numLocations;     ///< number of (u,v) coordinates in the array

            public REAL[] s;             ///< array of u coordinates
            public REAL[] t;             ///< array of v coordinates
        }

        //  typedef std::vector<LocationArray> LocationArrayVec;

        /// \brief Instantiates LimitStencilTable from a TopologyRefiner that has
        ///        been refined either uniformly or adaptively.
        ///
        /// @param refiner         The TopologyRefiner containing the topology
        ///
        /// @param locationArrays  An array of locations to be evaluated
        ///
        /// @param cvStencils      A set of StencilTable generated from the
        ///                        TopologyRefiner (optional: prevents redundant
        ///                        instantiation of the table if available)
        ///
        /// @param patchTable      A PatchTable generated from the TopologyRefiner
        ///                        (optional: prevents redundant instantiation of
        ///                        the table if available)
        ///
        /// @param options         Options controlling the creation of the table
        ///
        public static LimitStencilTableReal<REAL> Create(
            TopologyRefiner refiner,
            List<LocationArray> locationArrays,
            StencilTableReal<REAL> cvStencilsIn = null,
            PatchTable patchTableIn = null,
            Options options = null)
        {
            if (options == null)
            {
                options = new Options();
            }

            // Compute the total number of stencils to generate
            int numStencils = 0, numLimitStencils = 0;
            for (int i = 0; i < (int)locationArrays.Count; ++i)
            {
                System.Diagnostics.Debug.Assert(locationArrays[i].numLocations >= 0);
                numStencils += locationArrays[i].numLocations;
            }
            if (numStencils <= 0)
            {
                return null;
            }

            bool uniform = refiner.IsUniform();
            int maxlevel = refiner.GetMaxLevel();

            bool interpolateVertex = (options.interpolationMode == INTERPOLATE_VERTEX);
            bool interpolateVarying = (options.interpolationMode == INTERPOLATE_VARYING);
            bool interpolateFaceVarying = (options.interpolationMode == INTERPOLATE_FACE_VARYING);
            int fvarChannel = (int)options.fvarChannel;

            //
            //  Quick sanity checks for given PatchTable and/or StencilTables:
            //
            int nRefinedStencils = 0;
            if (uniform)
            {
                //  Uniform stencils must include at least the last level points:
                nRefinedStencils = interpolateFaceVarying
                                 ? refiner.GetLevel(maxlevel).GetNumFVarValues(fvarChannel)
                                 : refiner.GetLevel(maxlevel).GetNumVertices();
            }
            else
            {
                //  Adaptive stencils must include at least all refined points:
                nRefinedStencils = interpolateFaceVarying
                                 ? refiner.GetNumFVarValuesTotal(fvarChannel)
                                 : refiner.GetNumVerticesTotal();
            }
            if ((cvStencilsIn != null) && (cvStencilsIn.GetNumStencils() < nRefinedStencils))
            {
                //  Too few stencils in given StencilTable
                return null;
            }
            if ((patchTableIn != null) && (patchTableIn.IsFeatureAdaptive() == uniform))
            {
                //  Adaptive/uniform mismatch with given PatchTable and refiner
                return null;
            }

            // If an appropriate StencilTable was given, use it, otherwise, create a new one
            StencilTableReal<REAL> cvstencils = cvStencilsIn;
            if (cvstencils == null)
            {
                //
                // Generate stencils for the control vertices - this is necessary to
                // properly factorize patches with control vertices at level 0 (natural
                // regular patches, such as in a torus)
                // note: the control vertices of the mesh are added as single-index
                //       stencils of weight 1.0f
                //
                StencilTableFactoryReal<REAL>.Options stencilTableOptions = new StencilTableFactoryReal<REAL>.Options();
                stencilTableOptions.generateIntermediateLevels = uniform ? false : true;
                stencilTableOptions.generateControlVerts = true;
                stencilTableOptions.generateOffsets = true;
                stencilTableOptions.interpolationMode = options.interpolationMode;
                stencilTableOptions.fvarChannel = options.fvarChannel;

                cvstencils = StencilTableFactoryReal<REAL>.Create(refiner, stencilTableOptions);
            }

            // If an appropriate PatchTable was given, use it, otherwise, create a new one
            PatchTable patchtable = patchTableIn;
            if (patchtable == null)
            {
                //
                // Ideally we could create a sparse PatchTable here for the given
                // Locations, but that requires inverting the ptex/base-face relation.
                // so the caller must deal with that and provide such a PatchTable
                //
                PatchTableFactory.Options patchTableOptions = new PatchTableFactory.Options();
                patchTableOptions.SetPatchPrecision<REAL>();
                patchTableOptions.includeBaseLevelIndices = true;
                patchTableOptions.generateVaryingTables = interpolateVarying;
                patchTableOptions.generateFVarTables = interpolateFaceVarying;
                if (interpolateFaceVarying)
                {
                    patchTableOptions.includeFVarBaseLevelIndices = true;
                    patchTableOptions.numFVarChannels = 1;
                    patchTableOptions.fvarChannelIndices = new int[] { fvarChannel };
                    patchTableOptions.generateFVarLegacyLinearPatches = uniform ||
                        !refiner.GetAdaptiveOptions().considerFVarChannels;
                }
                patchTableOptions.SetEndCapType(
                    PatchTableFactory.Options.EndCapType.ENDCAP_GREGORY_BASIS);
                patchTableOptions.useInfSharpPatch = !uniform &&
                    refiner.GetAdaptiveOptions().useInfSharpPatch;

                patchtable = PatchTableFactory.Create(refiner, patchTableOptions);
            }

            // Append local point stencils and further verfiy size of given StencilTable:
            StencilTableReal<REAL> localstencils = null;
            if (interpolateVertex)
            {
                localstencils = patchtable.GetLocalPointStencilTable<REAL>();
            }
            else if (interpolateFaceVarying)
            {
                localstencils = patchtable.GetLocalPointFaceVaryingStencilTable<REAL>(fvarChannel);
            }
            else
            {
                localstencils = patchtable.GetLocalPointVaryingStencilTable<REAL>();
            }

            if ((localstencils != null) && (cvstencils.GetNumStencils() == nRefinedStencils))
            {
                StencilTableReal<REAL> refinedstencils = cvstencils;
                if (interpolateFaceVarying)
                {
                    cvstencils = StencilTableFactoryReal<REAL>.AppendLocalPointStencilTableFaceVarying(
                            refiner, refinedstencils, localstencils, fvarChannel);
                }
                else
                {
                    cvstencils = StencilTableFactoryReal<REAL>.AppendLocalPointStencilTable(
                            refiner, refinedstencils, localstencils);
                }
                //  if (!cvStencilsIn) delete refinedstencils;   -- garbage collected
            }

            System.Diagnostics.Debug.Assert(patchtable != null && cvstencils != null);

            // Create a patch-map to locate sub-patches faster
            PatchMap patchmap = new PatchMap(patchtable);

            //
            // Generate limit stencils for locations
            //
            int nControlVertices = interpolateFaceVarying
                                 ? refiner.GetLevel(0).GetNumFVarValues(fvarChannel)
                                 : refiner.GetLevel(0).GetNumVertices();

            StencilBuilder<REAL> builder = new StencilBuilder<REAL>(nControlVertices,
                                /*genControlVerts*/ false,
                                /*compactWeights*/  true);
            StencilBuilder<REAL>.Index origin = new StencilBuilder<REAL>.Index(builder, 0);
            StencilBuilder<REAL>.Index dst = origin;

            //
            //  Generally use the patches corresponding to the interpolation mode, but Uniform
            //  PatchTables do not have varying patches -- use the equivalent linear vertex
            //  patches in this case:
            //
            bool useVertexPatches = interpolateVertex || (interpolateVarying && uniform);
            bool useFVarPatches = interpolateFaceVarying;

            REAL[] wP = new REAL[20];
            REAL[] wDs = new REAL[20];
            REAL[] wDt = new REAL[20];
            REAL[] wDss = new REAL[20];
            REAL[] wDst = new REAL[20];
            REAL[] wDtt = new REAL[20];

            for (int i = 0; i < (int)locationArrays.Count; ++i)
            {
                LocationArray array = locationArrays[i];
                System.Diagnostics.Debug.Assert(array.ptexIdx >= 0);

                for (int j = 0; j < array.numLocations; ++j)
                { // for each face we're working on
                    REAL s = array.s[j],
                         t = array.t[j]; // for each target (s,t) point on that face

                    PatchTable.PatchHandle handle =
                                                patchmap.FindPatch(array.ptexIdx, double.CreateChecked(s), double.CreateChecked(t));
                    if (handle != null)
                    {
                        ConstIndexArray cvs;
                        if (useVertexPatches)
                        {
                            cvs = patchtable.GetPatchVertices(handle);
                        }
                        else if (useFVarPatches)
                        {
                            cvs = patchtable.GetPatchFVarValues(handle, fvarChannel);
                        }
                        else
                        {
                            cvs = patchtable.GetPatchVaryingVertices(handle);
                        }

                        StencilTableReal<REAL> src = cvstencils;
                        dst = origin[numLimitStencils];

                        if (options.generate2ndDerivatives)
                        {
                            if (useVertexPatches)
                            {
                                evaluateBasisDispatch(patchtable, 0,
                                        handle, s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                            }
                            else if (useFVarPatches)
                            {
                                evaluateBasisDispatch(patchtable, 1,
                                        handle, s, t, wP, wDs, wDt, wDss, wDst, wDtt, fvarChannel);
                            }
                            else
                            {
                                evaluateBasisDispatch(patchtable, 2,
                                        handle, s, t, wP, wDs, wDt, wDss, wDst, wDtt);
                            }

                            dst.Clear();
                            for (int k = 0; k < cvs.size(); ++k)
                            {
                                dst.AddWithWeight(src[cvs[k]], wP[k], wDs[k], wDt[k], wDss[k], wDst[k], wDtt[k]);
                            }
                        }
                        else if (options.generate1stDerivatives)
                        {
                            if (useVertexPatches)
                            {
                                evaluateBasisDispatch(patchtable, 0,
                                        handle, s, t, wP, wDs, wDt);
                            }
                            else if (useFVarPatches)
                            {
                                evaluateBasisDispatch(patchtable, 1,
                                        handle, s, t, wP, wDs, wDt, null, null, null, fvarChannel);
                            }
                            else
                            {
                                evaluateBasisDispatch(patchtable, 2,
                                        handle, s, t, wP, wDs, wDt);
                            }

                            dst.Clear();
                            for (int k = 0; k < cvs.size(); ++k)
                            {
                                dst.AddWithWeight(src[cvs[k]], wP[k], wDs[k], wDt[k]);
                            }
                        }
                        else
                        {
                            if (useVertexPatches)
                            {
                                evaluateBasisDispatch(patchtable, 0,
                                        handle, s, t, wP);
                            }
                            else if (useFVarPatches)
                            {
                                evaluateBasisDispatch(patchtable, 1,
                                        handle, s, t, wP, null, null, null, null, null, fvarChannel);
                            }
                            else
                            {
                                evaluateBasisDispatch(patchtable, 2,
                                        handle, s, t, wP);
                            }

                            dst.Clear();
                            for (int k = 0; k < cvs.size(); ++k)
                            {
                                dst.AddWithWeight(src[cvs[k]], wP[k]);
                            }
                        }

                        ++numLimitStencils;
                    }
                }
            }

            //  if (! cvStencilsIn) delete cvstencils;  -- garbage collected
            //  if (! patchTableIn) delete patchtable;  -- garbage collected

            //
            // Copy the proto-stencils into the limit stencil table
            //
            LimitStencilTableReal<REAL> result = new LimitStencilTableReal<REAL>(
                                                  nControlVertices,
                                                  builder.GetStencilOffsets(),
                                                  builder.GetStencilSizes(),
                                                  builder.GetStencilSources(),
                                                  builder.GetStencilWeights(),
                                                  builder.GetStencilDuWeights(),
                                                  builder.GetStencilDvWeights(),
                                                  builder.GetStencilDuuWeights(),
                                                  builder.GetStencilDuvWeights(),
                                                  builder.GetStencilDvvWeights(),
                                                  /*ctrlVerts*/false,
                                                  /*fristOffset*/0);
            return result;
        }
    }

    //
    //  Public wrapper classes for the templates
    //  (The C++ reinterpret_casts to the derived table classes become conversions of the result.)
    //
    public class StencilTableFactory : StencilTableFactoryReal<float>
    {
        public static new StencilTable Create(
            TopologyRefiner refiner, Options options = null)
        {
            StencilTableReal<float> result = StencilTableFactoryReal<float>.Create(refiner, options);
            return (result != null) ? new StencilTable(result) : null;
        }

        public static new StencilTable Create(
            int numTables, StencilTable[] tables)
        {
            StencilTableReal<float> result = StencilTableFactoryReal<float>.Create(numTables, tables);
            return (result != null) ? new StencilTable(result) : null;
        }

        public static new StencilTable AppendLocalPointStencilTable(
            TopologyRefiner refiner,
            StencilTable baseStencilTable,
            StencilTable localPointStencilTable,
            bool factorize = true)
        {
            StencilTableReal<float> result = StencilTableFactoryReal<float>.AppendLocalPointStencilTable(refiner,
                    baseStencilTable,
                    localPointStencilTable,
                    factorize);
            return (result != null) ? new StencilTable(result) : null;
        }

        public static new StencilTable AppendLocalPointStencilTableVarying(
            TopologyRefiner refiner,
            StencilTable baseStencilTable,
            StencilTable localPointStencilTable,
            bool factorize = true)
        {
            StencilTableReal<float> result = StencilTableFactoryReal<float>.AppendLocalPointStencilTableVarying(refiner,
                    baseStencilTable,
                    localPointStencilTable,
                    factorize);
            return (result != null) ? new StencilTable(result) : null;
        }

        public static new StencilTable AppendLocalPointStencilTableFaceVarying(
            TopologyRefiner refiner,
            StencilTable baseStencilTable,
            StencilTable localPointStencilTable,
            int channel = 0,
            bool factorize = true)
        {
            StencilTableReal<float> result = StencilTableFactoryReal<float>.AppendLocalPointStencilTableFaceVarying(refiner,
                    baseStencilTable,
                    localPointStencilTable,
                    channel, factorize);
            return (result != null) ? new StencilTable(result) : null;
        }
    }

    public class LimitStencilTableFactory : LimitStencilTableFactoryReal<float>
    {
        public static new LimitStencilTable Create(
            TopologyRefiner refiner,
            List<LocationArray> locationArrays,
            StencilTable cvStencils = null,
            PatchTable patchTable = null,
            Options options = null)
        {
            LimitStencilTableReal<float> result = LimitStencilTableFactoryReal<float>.Create(
                    refiner,
                    locationArrays,
                    cvStencils,
                    patchTable,
                    options);
            return (result != null) ? new LimitStencilTable(result) : null;
        }
    }
}
