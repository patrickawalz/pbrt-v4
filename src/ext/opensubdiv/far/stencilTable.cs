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

namespace OpenSubdiv.Far
{
    ///
    /// \brief Vertex stencil descriptor
    ///
    /// Allows access and manipulation of a single stencil in a StencilTable.
    ///
    /// (The C++ int * / Index * / REAL * pointers into the table arrays become
    ///  (array, offset) pairs.)
    ///
    public class StencilReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        /// \brief Default constructor.
        public StencilReal()
        {
        }

        /// \brief Constructor.
        ///
        /// @param size      Table pointer to the number of vertices in the stencil
        ///
        /// @param indices   Table pointer to the vertex indices of the stencil
        ///
        /// @param weights   Table pointer to the vertex weights of the stencil
        ///
        public StencilReal(int[] size, int sizeOffset, int[] indices, int indicesOffset, REAL[] weights, int weightsOffset)
        {
            _size = size;
            _sizeOffset = sizeOffset;
            _indices = indices;
            _indicesOffset = indicesOffset;
            _weights = weights;
            _weightsOffset = weightsOffset;
        }

        /// \brief Copy constructor.
        public StencilReal(StencilReal<REAL> other)
        {
            _size = other._size;
            _sizeOffset = other._sizeOffset;
            _indices = other._indices;
            _indicesOffset = other._indicesOffset;
            _weights = other._weights;
            _weightsOffset = other._weightsOffset;
        }

        /// \brief Returns the size of the stencil
        public int GetSize()
        {
            return _size[_sizeOffset];
        }

        /// \brief Returns the size of the stencil as a reference (the C++ int *)
        public ref int GetSizePtr()
        {
            return ref _size[_sizeOffset];
        }

        /// \brief Returns the control vertices' indices
        ///        (the C++ Index const * -- the array view has GetSize() entries)
        public ConstArray<int> GetVertexIndices()
        {
            return new ConstArray<int>(_indices, _indicesOffset, GetSize());
        }

        /// \brief Returns the interpolation weights
        ///        (the C++ REAL const * -- the array view has GetSize() entries)
        public ConstArray<REAL> GetWeights()
        {
            return new ConstArray<REAL>(_weights, _weightsOffset, GetSize());
        }

        /// \brief Advance to the next stencil in the table
        public void Next()
        {
            int stride = _size[_sizeOffset];
            ++_sizeOffset;
            _indicesOffset += stride;
            _weightsOffset += stride;
        }

        //  friends: StencilTableFactoryReal<REAL>, LimitStencilTableFactoryReal<REAL> (public in C#)
        public int[] _size;
        public int _sizeOffset;
        public int[] _indices;
        public int _indicesOffset;
        public REAL[] _weights;
        public int _weightsOffset;
    }

    public class Stencil : StencilReal<float>
    {
        public Stencil() : base()
        {
        }

        public Stencil(StencilReal<float> other) : base(other)
        {
        }

        public Stencil(int[] size, int sizeOffset, int[] indices, int indicesOffset, float[] weights, int weightsOffset)
            : base(size, sizeOffset, indices, indicesOffset, weights, weightsOffset)
        {
        }
    }

    ///
    /// \brief Table of subdivision stencils.
    ///
    /// Stencils are the most direct method of evaluation of locations on the limit
    /// of a surface. Every point of a limit surface can be computed by linearly
    /// blending a collection of coarse control vertices.
    ///
    public class StencilTableReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public StencilTableReal(int numControlVerts,
                                StdVector<int> offsets,
                                StdVector<int> sizes,
                                StdVector<int> sources,
                                StdVector<REAL> weights,
                                bool includeCoarseVerts,
                                int firstOffset)
        {
            _offsets = new StdVector<int>();
            _sizes = new StdVector<int>();
            _indices = new StdVector<int>();
            _weights = new StdVector<REAL>();

            _numControlVertices = numControlVerts;
            copyStencilData<REAL>(numControlVerts,
                                  includeCoarseVerts,
                                  firstOffset,
                                  offsets, _offsets,
                                  sizes, _sizes,
                                  sources, _indices,
                                  weights, _weights);
        }

        /// \brief Copy constructor (the implicit C++ copy constructor)
        public StencilTableReal(StencilTableReal<REAL> other)
        {
            _numControlVertices = other._numControlVertices;
            _sizes = new StdVector<int>(other._sizes);
            _offsets = new StdVector<int>(other._offsets);
            _indices = new StdVector<int>(other._indices);
            _weights = new StdVector<REAL>(other._weights);
        }

        public StencilTableReal()
        {
            _numControlVertices = 0;
            _offsets = new StdVector<int>();
            _sizes = new StdVector<int>();
            _indices = new StdVector<int>();
            _weights = new StdVector<REAL>();
        }

        public StencilTableReal(int numControlVerts)
        {
            _numControlVertices = numControlVerts;
            _offsets = new StdVector<int>();
            _sizes = new StdVector<int>();
            _indices = new StdVector<int>();
            _weights = new StdVector<REAL>();
        }

        // (virtual destructor: nothing to do in C#)

        /// \brief Returns the number of stencils in the table
        public int GetNumStencils()
        {
            return (int)_sizes.size();
        }

        /// \brief Returns the number of control vertices indexed in the table
        public int GetNumControlVertices()
        {
            return _numControlVertices;
        }

        /// \brief Returns a Stencil at index i in the table
        public StencilReal<REAL> GetStencil(int i)
        {
            System.Diagnostics.Debug.Assert((!_offsets.empty()) && i < (int)_offsets.size());

            int ofs = _offsets[i];

            return new StencilReal<REAL>(_sizes.Data, i,
                                         _indices.Data, ofs,
                                         _weights.Data, ofs);
        }

        /// \brief Returns the number of control vertices of each stencil in the table
        public StdVector<int> GetSizes()
        {
            return _sizes;
        }

        /// \brief Returns the offset to a given stencil (factory helper)
        public StdVector<int> GetOffsets()
        {
            return _offsets;
        }

        /// \brief Returns the indices of the control vertices
        public StdVector<int> GetControlIndices()
        {
            return _indices;
        }

        /// \brief Returns the stencil interpolation weights
        public StdVector<REAL> GetWeights()
        {
            return _weights;
        }

        /// \brief Returns a Stencil at index i in the table
        public StencilReal<REAL> this[int index]
        {
            get
            {
                return GetStencil(index);
            }
        }

        /// \brief Updates point values based on the control values
        ///
        /// \note The destination buffers are assumed to have allocated at least
        ///       \c GetNumStencils() elements.
        ///
        /// @param srcValues  Buffer with primvar data for the control vertices
        ///
        /// @param dstValues  Destination buffer for the interpolated primvar data
        ///
        /// @param start      (optional) start index of the first stencil to update
        ///
        /// @param end        (optional) end index of the last stencil to update
        ///
        public void UpdateValues<T, U>(PrimvarBuffer<T> srcValues, PrimvarBuffer<U> dstValues, int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(srcValues, dstValues, _weights, start, end);
        }

        /// \brief Updates point values based on the control values (and the refined values)
        ///
        /// @param srcBase    Buffer with primvar data for the control vertices
        ///
        /// @param numBase    Number of vertices in the base buffer
        ///
        /// @param srcRef     Buffer with primvar data for the refined vertices
        ///
        /// @param dstValues  Destination buffer for the interpolated primvar data
        ///
        public void UpdateValues<T1, T2, U>(PrimvarBuffer<T1> srcBase, int numBase, PrimvarBuffer<T2> srcRef,
            PrimvarBuffer<U> dstValues, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(srcBase, numBase, srcRef, dstValues, _weights, start, end);
        }

        //  Pointer interface for backward compatibility
        public void UpdateValues<T, U>(T[] src, U[] dst, int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(dst, 0), _weights, start, end);
        }

        public void UpdateValues<T1, T2, U>(T1[] srcBase, int numBase, T2[] srcRef,
            U[] dst, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(dst, 0), _weights, start, end);
        }

        /// \brief Clears the stencils from the table
        public void Clear()
        {
            _numControlVertices = 0;
            _sizes.Clear();
            _offsets.Clear();
            _indices.Clear();
            _weights.Clear();
        }

        // Update values by applying cached stencil weights to new control values
        public void update<T, U>(PrimvarBuffer<T> srcValues, PrimvarBuffer<U> dstValues,
            StdVector<REAL> valueWeights, int start, int end)
            where U : IPrimvar<T, REAL>
        {
            //  at(0) throws on an empty vector
            if (_sizes.size() == 0 || _indices.size() == 0 || valueWeights.size() == 0)
            {
                throw new ArgumentOutOfRangeException("vector::_M_range_check");
            }
            int sizes = 0;
            int indices = 0;
            int weights = 0;

            if (start > 0)
            {
                System.Diagnostics.Debug.Assert(start < (int)_offsets.size());
                sizes += start;
                indices += _offsets[start];
                weights += _offsets[start];
            }
            else
            {
                start = 0;
            }

            int nstencils = ((end < start) ? GetNumStencils() : end) - start;

            for (int i = 0; i < nstencils; ++i, ++sizes)
            {
                dstValues[start + i].Clear();
                for (int j = 0; j < _sizes[sizes]; ++j, ++indices, ++weights)
                {
                    dstValues[start + i].AddWithWeight(srcValues[_indices[indices]], valueWeights[weights]);
                }
            }
        }

        public void update<T1, T2, U>(PrimvarBuffer<T1> srcBase, int numBase,
            PrimvarBuffer<T2> srcRef, PrimvarBuffer<U> dstValues,
            StdVector<REAL> valueWeights, int start, int end)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            //  at(0) throws on an empty vector
            if (_sizes.size() == 0 || _indices.size() == 0 || valueWeights.size() == 0)
            {
                throw new ArgumentOutOfRangeException("vector::_M_range_check");
            }
            int sizes = 0;
            int indices = 0;
            int weights = 0;

            if (start > 0)
            {
                System.Diagnostics.Debug.Assert(start < (int)_offsets.size());
                sizes += start;
                indices += _offsets[start];
                weights += _offsets[start];
            }
            else
            {
                start = 0;
            }

            int nstencils = ((end < start) ? GetNumStencils() : end) - start;

            for (int i = 0; i < nstencils; ++i, ++sizes)
            {
                dstValues[start + i].Clear();
                for (int j = 0; j < _sizes[sizes]; ++j, ++indices, ++weights)
                {
                    if (_indices[indices] < numBase)
                    {
                        dstValues[start + i].AddWithWeight(srcBase[_indices[indices]], valueWeights[weights]);
                    }
                    else
                    {
                        dstValues[start + i].AddWithWeight(srcRef[_indices[indices] - numBase], valueWeights[weights]);
                    }
                }
            }
        }

        // Populate the offsets table from the stencil sizes in _sizes (factory helper)
        public void generateOffsets()
        {
            int offset = 0;
            int noffsets = (int)_sizes.size();
            _offsets.Resize(noffsets);
            for (int i = 0; i < (int)_sizes.size(); ++i)
            {
                _offsets[i] = offset;
                offset += _sizes[i];
            }
        }

        // Resize the table arrays (factory helper)
        public virtual void resize(int nstencils, int nelems)
        {
            _sizes.Resize(nstencils);
            _indices.Resize(nelems);
            _weights.Resize(nelems);
        }

        // Reserves the table arrays (factory helper)
        public void reserve(int nstencils, int nelems)
        {
            _sizes.Reserve(nstencils);
            _indices.Reserve(nelems);
            _weights.Reserve(nelems);
        }

        // Reallocates the table arrays to remove excess capacity (factory helper)
        public void shrinkToFit()
        {
            //  std::vector<int>(_sizes).swap(_sizes) etc.
            _sizes.ShrinkToFit();
            _indices.ShrinkToFit();
            _weights.ShrinkToFit();
        }

        // Performs any final operations on internal tables (factory helper)
        public void finalize()
        {
            shrinkToFit();
            generateOffsets();
        }

        //
        //  Copies the stencil data (the stencils are probably not in order, so we must copy/sort them).
        //
        public static void copyStencilData<W>(int numControlVerts,
                        bool includeCoarseVerts,
                        int firstOffset,
                        StdVector<int> offsets,
                        StdVector<int> _offsets,
                        StdVector<int> sizes,
                        StdVector<int> _sizes,
                        StdVector<int> sources,
                        StdVector<int> _sources,
                        StdVector<W> weights,
                        StdVector<W> _weights,
                        StdVector<W> duWeights = null,
                        StdVector<W> _duWeights = null,
                        StdVector<W> dvWeights = null,
                        StdVector<W> _dvWeights = null,
                        StdVector<W> duuWeights = null,
                        StdVector<W> _duuWeights = null,
                        StdVector<W> duvWeights = null,
                        StdVector<W> _duvWeights = null,
                        StdVector<W> dvvWeights = null,
                        StdVector<W> _dvvWeights = null)
        {
            int start = includeCoarseVerts ? 0 : firstOffset;

            _offsets.Resize(offsets.size());
            _sizes.Resize(sizes.size());
            _sources.Resize(sources.size());
            _weights.Resize(weights.size());
            if (_duWeights != null)
                _duWeights.Resize(duWeights.size());
            if (_dvWeights != null)
                _dvWeights.Resize(dvWeights.size());
            if (_duuWeights != null)
                _duuWeights.Resize(duuWeights.size());
            if (_duvWeights != null)
                _duvWeights.Resize(duvWeights.size());
            if (_dvvWeights != null)
                _dvvWeights.Resize(dvvWeights.size());

            // The stencils are probably not in order, so we must copy/sort them.
            // Note here that loop index 'i' represents stencil_i for vertex_i.
            int curOffset = 0;

            int stencilCount = 0,
                weightCount = 0;

            for (int i = start; i < offsets.size(); i++)
            {
                // Once we've copied out all the control verts, jump to the offset
                // where the actual stencils begin.
                if (includeCoarseVerts && (int)i == numControlVerts)
                    i = firstOffset;

                // Copy the stencil.
                int sz = sizes[i];
                int off = offsets[i];

                _offsets[stencilCount] = curOffset;
                _sizes[stencilCount] = sz;

                System.Array.Copy(sources.Data, off, _sources.Data, curOffset, sz);
                System.Array.Copy(weights.Data, off, _weights.Data, curOffset, sz);

                if (_duWeights != null && !_duWeights.empty())
                {
                    System.Array.Copy(duWeights.Data, off, _duWeights.Data, curOffset, sz);
                }
                if (_dvWeights != null && !_dvWeights.empty())
                {
                    System.Array.Copy(dvWeights.Data, off, _dvWeights.Data, curOffset, sz);
                }

                if (_duuWeights != null && !_duuWeights.empty())
                {
                    System.Array.Copy(duuWeights.Data, off, _duuWeights.Data, curOffset, sz);
                }
                if (_duvWeights != null && !_duvWeights.empty())
                {
                    System.Array.Copy(duvWeights.Data, off, _duvWeights.Data, curOffset, sz);
                }
                if (_dvvWeights != null && !_dvvWeights.empty())
                {
                    System.Array.Copy(dvvWeights.Data, off, _dvvWeights.Data, curOffset, sz);
                }

                curOffset += sz;
                stencilCount++;
                weightCount += sz;
            }

            _offsets.Resize(stencilCount);
            _sizes.Resize(stencilCount);
            _sources.Resize(weightCount);

            if (_duWeights != null && !_duWeights.empty())
                _duWeights.Resize(weightCount);
            if (_dvWeights != null && !_dvWeights.empty())
                _dvWeights.Resize(weightCount);

            if (_duuWeights != null && !_duuWeights.empty())
                _duuWeights.Resize(weightCount);
            if (_duvWeights != null && !_duvWeights.empty())
                _duvWeights.Resize(weightCount);
            if (_dvvWeights != null && !_dvvWeights.empty())
                _dvvWeights.Resize(weightCount);
        }

        //  friends: StencilTableFactoryReal<REAL>, Far::PatchTableBuilder (public in C#)
        public int _numControlVertices;              // number of control vertices

        public StdVector<int> _sizes;                // number of coefficients for each stencil
        public StdVector<int> _offsets;              // offset to the start of each stencil
        public StdVector<int> _indices;              // indices of contributing coarse vertices
        public StdVector<REAL> _weights;             // stencil weight coefficients
    }

    public class StencilTable : StencilTableReal<float>
    {
        public new Stencil GetStencil(int index)
        {
            return new Stencil(base.GetStencil(index));
        }

        public new Stencil this[int index]
        {
            get
            {
                return new Stencil(base.GetStencil(index));
            }
        }

        public StencilTable() : base()
        {
        }

        public StencilTable(int numControlVerts) : base(numControlVerts)
        {
        }

        /// \brief C# replacement of the C++ reinterpret_cast<StencilTable const *> of the factory wrappers:
        ///        takes over the contents of a StencilTableReal<float>.
        public StencilTable(StencilTableReal<float> other) : base()
        {
            _numControlVertices = other._numControlVertices;
            _sizes = other._sizes;
            _offsets = other._offsets;
            _indices = other._indices;
            _weights = other._weights;
        }

        public StencilTable(int numControlVerts,
                            StdVector<int> offsets,
                            StdVector<int> sizes,
                            StdVector<int> sources,
                            StdVector<float> weights,
                            bool includeCoarseVerts,
                            int firstOffset)
            : base(numControlVerts, offsets,
                   sizes, sources, weights, includeCoarseVerts, firstOffset)
        {
        }
    }

    ///
    /// \brief Limit point stencil descriptor
    ///
    /// Allows access and manipulation of a single stencil in a LimitStencilTable.
    ///
    public class LimitStencilReal<REAL> : StencilReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        /// \brief Constructor.
        public LimitStencilReal(int[] size, int sizeOffset,
                                int[] indices, int indicesOffset,
                                REAL[] weights, int weightsOffset,
                                REAL[] duWeights = null, int duWeightsOffset = 0,
                                REAL[] dvWeights = null, int dvWeightsOffset = 0,
                                REAL[] duuWeights = null, int duuWeightsOffset = 0,
                                REAL[] duvWeights = null, int duvWeightsOffset = 0,
                                REAL[] dvvWeights = null, int dvvWeightsOffset = 0)
            : base(size, sizeOffset, indices, indicesOffset, weights, weightsOffset)
        {
            _duWeights = duWeights;
            _duWeightsOffset = duWeightsOffset;
            _dvWeights = dvWeights;
            _dvWeightsOffset = dvWeightsOffset;
            _duuWeights = duuWeights;
            _duuWeightsOffset = duuWeightsOffset;
            _duvWeights = duvWeights;
            _duvWeightsOffset = duvWeightsOffset;
            _dvvWeights = dvvWeights;
            _dvvWeightsOffset = dvvWeightsOffset;
        }

        /// \brief Copy constructor (the implicit C++ copy constructor).
        public LimitStencilReal(LimitStencilReal<REAL> other)
            : base(other)
        {
            _duWeights = other._duWeights;
            _duWeightsOffset = other._duWeightsOffset;
            _dvWeights = other._dvWeights;
            _dvWeightsOffset = other._dvWeightsOffset;
            _duuWeights = other._duuWeights;
            _duuWeightsOffset = other._duuWeightsOffset;
            _duvWeights = other._duvWeights;
            _duvWeightsOffset = other._duvWeightsOffset;
            _dvvWeights = other._dvvWeights;
            _dvvWeightsOffset = other._dvvWeightsOffset;
        }

        /// \brief Returns the u derivative weights
        public ConstArray<REAL> GetDuWeights()
        {
            return new ConstArray<REAL>(_duWeights, _duWeightsOffset, GetSize());
        }

        /// \brief Returns the v derivative weights
        public ConstArray<REAL> GetDvWeights()
        {
            return new ConstArray<REAL>(_dvWeights, _dvWeightsOffset, GetSize());
        }

        /// \brief Returns the uu derivative weights
        public ConstArray<REAL> GetDuuWeights()
        {
            return new ConstArray<REAL>(_duuWeights, _duuWeightsOffset, GetSize());
        }

        /// \brief Returns the uv derivative weights
        public ConstArray<REAL> GetDuvWeights()
        {
            return new ConstArray<REAL>(_duvWeights, _duvWeightsOffset, GetSize());
        }

        /// \brief Returns the vv derivative weights
        public ConstArray<REAL> GetDvvWeights()
        {
            return new ConstArray<REAL>(_dvvWeights, _dvvWeightsOffset, GetSize());
        }

        /// \brief Advance to the next stencil in the table
        public new void Next()
        {
            int stride = _size[_sizeOffset];
            ++_sizeOffset;
            _indicesOffset += stride;
            _weightsOffset += stride;
            if (_duWeights != null) _duWeightsOffset += stride;
            if (_dvWeights != null) _dvWeightsOffset += stride;
            if (_duuWeights != null) _duuWeightsOffset += stride;
            if (_duvWeights != null) _duvWeightsOffset += stride;
            if (_dvvWeights != null) _dvvWeightsOffset += stride;
        }

        //  friends: StencilTableFactoryReal<REAL>, LimitStencilTableFactoryReal<REAL> (public in C#)
        public REAL[] _duWeights;       // pointer to stencil u derivative limit weights
        public int _duWeightsOffset;
        public REAL[] _dvWeights;       // pointer to stencil v derivative limit weights
        public int _dvWeightsOffset;
        public REAL[] _duuWeights;      // pointer to stencil uu derivative limit weights
        public int _duuWeightsOffset;
        public REAL[] _duvWeights;      // pointer to stencil uv derivative limit weights
        public int _duvWeightsOffset;
        public REAL[] _dvvWeights;      // pointer to stencil vv derivative limit weights
        public int _dvvWeightsOffset;
    }

    public class LimitStencil : LimitStencilReal<float>
    {
        public LimitStencil(LimitStencilReal<float> other) : base(other)
        {
        }

        public LimitStencil(int[] size, int sizeOffset,
                            int[] indices, int indicesOffset,
                            float[] weights, int weightsOffset,
                            float[] duWeights = null, int duWeightsOffset = 0,
                            float[] dvWeights = null, int dvWeightsOffset = 0,
                            float[] duuWeights = null, int duuWeightsOffset = 0,
                            float[] duvWeights = null, int duvWeightsOffset = 0,
                            float[] dvvWeights = null, int dvvWeightsOffset = 0)
            : base(size, sizeOffset, indices, indicesOffset, weights, weightsOffset,
                   duWeights, duWeightsOffset, dvWeights, dvWeightsOffset,
                   duuWeights, duuWeightsOffset, duvWeights, duvWeightsOffset,
                   dvvWeights, dvvWeightsOffset)
        {
        }
    }

    ///
    /// \brief Table of limit subdivision stencils.
    ///
    public class LimitStencilTableReal<REAL> : StencilTableReal<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public LimitStencilTableReal(
                        int numControlVerts,
                        StdVector<int> offsets,
                        StdVector<int> sizes,
                        StdVector<int> sources,
                        StdVector<REAL> weights,
                        StdVector<REAL> duWeights,
                        StdVector<REAL> dvWeights,
                        StdVector<REAL> duuWeights,
                        StdVector<REAL> duvWeights,
                        StdVector<REAL> dvvWeights,
                        bool includeCoarseVerts,
                        int firstOffset)
            : base(numControlVerts)
        {
            _duWeights = new StdVector<REAL>();
            _dvWeights = new StdVector<REAL>();
            _duuWeights = new StdVector<REAL>();
            _duvWeights = new StdVector<REAL>();
            _dvvWeights = new StdVector<REAL>();

            copyStencilData<REAL>(numControlVerts,
                                  includeCoarseVerts,
                                  firstOffset,
                                  offsets, this._offsets,
                                  sizes, this._sizes,
                                  sources, this._indices,
                                  weights, this._weights,
                                  duWeights, _duWeights,
                                  dvWeights, _dvWeights,
                                  duuWeights, _duuWeights,
                                  duvWeights, _duvWeights,
                                  dvvWeights, _dvvWeights);
        }

        public LimitStencilTableReal(int numControlVerts)
            : base(numControlVerts)
        {
            _duWeights = new StdVector<REAL>();
            _dvWeights = new StdVector<REAL>();
            _duuWeights = new StdVector<REAL>();
            _duvWeights = new StdVector<REAL>();
            _dvvWeights = new StdVector<REAL>();
        }

        /// \brief Returns a LimitStencil at index i in the table
        public LimitStencilReal<REAL> GetLimitStencil(int i)
        {
            System.Diagnostics.Debug.Assert((!this.GetOffsets().empty()) && i < (int)this.GetOffsets().size());

            int ofs = this.GetOffsets()[i];

            if (!_duWeights.empty() && !_dvWeights.empty() &&
                !_duuWeights.empty() && !_duvWeights.empty() && !_dvvWeights.empty())
            {
                return new LimitStencilReal<REAL>(
                                     this.GetSizes().Data, i,
                                     this.GetControlIndices().Data, ofs,
                                     this.GetWeights().Data, ofs,
                                     GetDuWeights().Data, ofs,
                                     GetDvWeights().Data, ofs,
                                     GetDuuWeights().Data, ofs,
                                     GetDuvWeights().Data, ofs,
                                     GetDvvWeights().Data, ofs);
            }
            else if (!_duWeights.empty() && !_dvWeights.empty())
            {
                return new LimitStencilReal<REAL>(
                                     this.GetSizes().Data, i,
                                     this.GetControlIndices().Data, ofs,
                                     this.GetWeights().Data, ofs,
                                     GetDuWeights().Data, ofs,
                                     GetDvWeights().Data, ofs);
            }
            else
            {
                return new LimitStencilReal<REAL>(
                                     this.GetSizes().Data, i,
                                     this.GetControlIndices().Data, ofs,
                                     this.GetWeights().Data, ofs);
            }
        }

        /// \brief Returns a LimitStencil at index i in the table
        public new LimitStencilReal<REAL> this[int index]
        {
            get
            {
                return GetLimitStencil(index);
            }
        }

        /// \brief Returns the 'u' derivative stencil interpolation weights
        public StdVector<REAL> GetDuWeights()
        {
            return _duWeights;
        }

        /// \brief Returns the 'v' derivative stencil interpolation weights
        public StdVector<REAL> GetDvWeights()
        {
            return _dvWeights;
        }

        /// \brief Returns the 'uu' derivative stencil interpolation weights
        public StdVector<REAL> GetDuuWeights()
        {
            return _duuWeights;
        }

        /// \brief Returns the 'uv' derivative stencil interpolation weights
        public StdVector<REAL> GetDuvWeights()
        {
            return _duvWeights;
        }

        /// \brief Returns the 'vv' derivative stencil interpolation weights
        public StdVector<REAL> GetDvvWeights()
        {
            return _dvvWeights;
        }

        /// \brief Updates derivative values based on the control values
        ///
        /// @param srcValues  Buffer with primvar data for the control vertices
        ///
        /// @param uderivs    Destination buffer for the interpolated 'u' derivative primvar data
        ///
        /// @param vderivs    Destination buffer for the interpolated 'v' derivative primvar data
        ///
        public void UpdateDerivs<T, U>(PrimvarBuffer<T> srcValues, PrimvarBuffer<U> uderivs, PrimvarBuffer<U> vderivs,
            int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(srcValues, uderivs, _duWeights, start, end);
            this.update(srcValues, vderivs, _dvWeights, start, end);
        }

        public void UpdateDerivs<T1, T2, U>(PrimvarBuffer<T1> srcBase, int numBase, PrimvarBuffer<T2> srcRef,
            PrimvarBuffer<U> uderivs, PrimvarBuffer<U> vderivs, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(srcBase, numBase, srcRef, uderivs, _duWeights, start, end);
            this.update(srcBase, numBase, srcRef, vderivs, _dvWeights, start, end);
        }

        //  Pointer interface for backward compatibility
        public void UpdateDerivs<T, U>(T[] src, U[] uderivs, U[] vderivs,
            int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(uderivs, 0), _duWeights, start, end);
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(vderivs, 0), _dvWeights, start, end);
        }

        public void UpdateDerivs<T1, T2, U>(T1[] srcBase, int numBase, T2[] srcRef,
            U[] uderivs, U[] vderivs, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(uderivs, 0), _duWeights, start, end);
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(vderivs, 0), _dvWeights, start, end);
        }

        /// \brief Updates 2nd derivative values based on the control values
        public void Update2ndDerivs<T, U>(PrimvarBuffer<T> srcValues,
            PrimvarBuffer<U> uuderivs, PrimvarBuffer<U> uvderivs, PrimvarBuffer<U> vvderivs,
            int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(srcValues, uuderivs, _duuWeights, start, end);
            this.update(srcValues, uvderivs, _duvWeights, start, end);
            this.update(srcValues, vvderivs, _dvvWeights, start, end);
        }

        public void Update2ndDerivs<T1, T2, U>(PrimvarBuffer<T1> srcBase, int numBase, PrimvarBuffer<T2> srcRef,
            PrimvarBuffer<U> uuderivs, PrimvarBuffer<U> uvderivs, PrimvarBuffer<U> vvderivs, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(srcBase, numBase, srcRef, uuderivs, _duuWeights, start, end);
            this.update(srcBase, numBase, srcRef, uvderivs, _duvWeights, start, end);
            this.update(srcBase, numBase, srcRef, vvderivs, _dvvWeights, start, end);
        }

        //  Pointer interface for backward compatibility
        public void Update2ndDerivs<T, U>(T[] src, U[] uuderivs, U[] uvderivs, U[] vvderivs,
            int start = -1, int end = -1)
            where U : IPrimvar<T, REAL>
        {
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(uuderivs, 0), _duuWeights, start, end);
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(uvderivs, 0), _duvWeights, start, end);
            this.update(new PrimvarBuffer<T>(src, 0), new PrimvarBuffer<U>(vvderivs, 0), _dvvWeights, start, end);
        }

        public void Update2ndDerivs<T1, T2, U>(T1[] srcBase, int numBase, T2[] srcRef,
            U[] uuderivs, U[] uvderivs, U[] vvderivs, int start = -1, int end = -1)
            where U : IPrimvar<T1, REAL>, IPrimvar<T2, REAL>
        {
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(uuderivs, 0), _duuWeights, start, end);
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(uvderivs, 0), _duvWeights, start, end);
            this.update(new PrimvarBuffer<T1>(srcBase, 0), numBase, new PrimvarBuffer<T2>(srcRef, 0),
                        new PrimvarBuffer<U>(vvderivs, 0), _dvvWeights, start, end);
        }

        /// \brief Clears the stencils from the table
        public new void Clear()
        {
            base.Clear();
            _duWeights.Clear();
            _dvWeights.Clear();
            _duuWeights.Clear();
            _duvWeights.Clear();
            _dvvWeights.Clear();
        }

        //  friend LimitStencilTableFactoryReal<REAL> (public in C#)

        // Resize the table arrays (factory helper)
        public override void resize(int nstencils, int nelems)
        {
            base.resize(nstencils, nelems);
            _duWeights.Resize(nelems);
            _dvWeights.Resize(nelems);
        }

        public StdVector<REAL> _duWeights;    // u  derivative limit stencil weights
        public StdVector<REAL> _dvWeights;    // v  derivative limit stencil weights
        public StdVector<REAL> _duuWeights;   // uu derivative limit stencil weights
        public StdVector<REAL> _duvWeights;   // uv derivative limit stencil weights
        public StdVector<REAL> _dvvWeights;   // vv derivative limit stencil weights
    }

    public class LimitStencilTable : LimitStencilTableReal<float>
    {
        /// \brief C# replacement of the C++ reinterpret_cast<LimitStencilTable const *> of the factory wrappers:
        ///        takes over the contents of a LimitStencilTableReal<float>.
        public LimitStencilTable(LimitStencilTableReal<float> other) : base(other._numControlVertices)
        {
            _sizes = other._sizes;
            _offsets = other._offsets;
            _indices = other._indices;
            _weights = other._weights;
            _duWeights = other._duWeights;
            _dvWeights = other._dvWeights;
            _duuWeights = other._duuWeights;
            _duvWeights = other._duvWeights;
            _dvvWeights = other._dvvWeights;
        }

        public new LimitStencil GetLimitStencil(int index)
        {
            return new LimitStencil(base.GetLimitStencil(index));
        }

        public new LimitStencil this[int index]
        {
            get
            {
                return new LimitStencil(base.GetLimitStencil(index));
            }
        }

        public LimitStencilTable(int numControlVerts,
                        StdVector<int> offsets,
                        StdVector<int> sizes,
                        StdVector<int> sources,
                        StdVector<float> weights,
                        StdVector<float> duWeights,
                        StdVector<float> dvWeights,
                        StdVector<float> duuWeights,
                        StdVector<float> duvWeights,
                        StdVector<float> dvvWeights,
                        bool includeCoarseVerts,
                        int firstOffset)
            : base(numControlVerts,
                   offsets, sizes, sources, weights,
                   duWeights, dvWeights, duuWeights, duvWeights, dvvWeights,
                   includeCoarseVerts, firstOffset)
        {
        }
    }
}
