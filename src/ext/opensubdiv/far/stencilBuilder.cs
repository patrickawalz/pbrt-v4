//
//   Copyright 2015 Pixar
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//

using System;
using System.Collections.Generic;
using System.Numerics;
using OpenSubdiv.Vtr;

namespace OpenSubdiv.Far.Internal
{
    public static class StencilBuilderUtil
    {
        public static bool isWeightZero<REAL>(REAL w) where REAL : unmanaged, INumber<REAL>
        {
            return (w == REAL.Zero);
        }
    }

    public struct Point1stDerivWeight<REAL> :
        IMultiplyOperators<Point1stDerivWeight<REAL>, Point1stDerivWeight<REAL>, Point1stDerivWeight<REAL>>,
        IMultiplicativeIdentity<Point1stDerivWeight<REAL>, Point1stDerivWeight<REAL>>
        where REAL : unmanaged, INumber<REAL>
    {
        public REAL p;
        public REAL du;
        public REAL dv;

        //  (the default constructor Point1stDerivWeight() zero-initializes)

        public Point1stDerivWeight(REAL w)
        {
            p = w;
            du = w;
            dv = w;
        }

        public Point1stDerivWeight(REAL w, REAL wDu, REAL wDv)
        {
            p = w;
            du = wDu;
            dv = wDv;
        }

        public static Point1stDerivWeight<REAL> operator *(Point1stDerivWeight<REAL> lhs,
                                                          Point1stDerivWeight<REAL> rhs)
        {
            lhs.p *= rhs.p;
            lhs.du *= rhs.du;
            lhs.dv *= rhs.dv;
            return lhs;
        }

        //  The C++ in-place operator+= :
        public void operator +=(Point1stDerivWeight<REAL> rhs)
        {
            p += rhs.p;
            du += rhs.du;
            dv += rhs.dv;
        }

        //  Point1stDerivWeight<REAL>(W(1.0)) : all components set to one
        public static Point1stDerivWeight<REAL> MultiplicativeIdentity
        {
            get
            {
                return new Point1stDerivWeight<REAL>(REAL.One);
            }
        }
    }

    public struct Point2ndDerivWeight<REAL> :
        IMultiplyOperators<Point2ndDerivWeight<REAL>, Point2ndDerivWeight<REAL>, Point2ndDerivWeight<REAL>>,
        IMultiplicativeIdentity<Point2ndDerivWeight<REAL>, Point2ndDerivWeight<REAL>>
        where REAL : unmanaged, INumber<REAL>
    {
        public REAL p;
        public REAL du;
        public REAL dv;
        public REAL duu;
        public REAL duv;
        public REAL dvv;

        public Point2ndDerivWeight(REAL w)
        {
            p = w;
            du = w;
            dv = w;
            duu = w;
            duv = w;
            dvv = w;
        }

        public Point2ndDerivWeight(REAL w, REAL wDu, REAL wDv,
                                   REAL wDuu, REAL wDuv, REAL wDvv)
        {
            p = w;
            du = wDu;
            dv = wDv;
            duu = wDuu;
            duv = wDuv;
            dvv = wDvv;
        }

        public static Point2ndDerivWeight<REAL> operator *(Point2ndDerivWeight<REAL> lhs,
                                                          Point2ndDerivWeight<REAL> rhs)
        {
            lhs.p *= rhs.p;
            lhs.du *= rhs.du;
            lhs.dv *= rhs.dv;
            lhs.duu *= rhs.duu;
            lhs.duv *= rhs.duv;
            lhs.dvv *= rhs.dvv;
            return lhs;
        }

        //  The C++ in-place operator+= :
        public void operator +=(Point2ndDerivWeight<REAL> rhs)
        {
            p += rhs.p;
            du += rhs.du;
            dv += rhs.dv;
            duu += rhs.duu;
            duv += rhs.duv;
            dvv += rhs.dvv;
        }

        public static Point2ndDerivWeight<REAL> MultiplicativeIdentity
        {
            get
            {
                return new Point2ndDerivWeight<REAL>(REAL.One);
            }
        }
    }

    //  The interface of the WACCUM accumulator types of WeightTable<REAL>::AddWithWeight():
    public interface IWeightAccumulator<W>
    {
        void PushBack(W weight);
        void Add(int i, W weight);
        W Get(int index);
    }

    /// Stencil table constructor set.
    ///
    public class WeightTable<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public WeightTable(int coarseVerts,
                           bool genCtrlVertStencils,
                           bool compactWeights)
        {
            _dests = new StdVector<int>();
            _sources = new StdVector<int>();
            _weights = new StdVector<REAL>();
            _duWeights = new StdVector<REAL>();
            _dvWeights = new StdVector<REAL>();
            _duuWeights = new StdVector<REAL>();
            _duvWeights = new StdVector<REAL>();
            _dvvWeights = new StdVector<REAL>();
            _indices = new StdVector<int>();
            _sizes = new StdVector<int>();

            _size = 0;
            _lastOffset = 0;
            _coarseVertCount = coarseVerts;
            _compactWeights = compactWeights;

            // These numbers were chosen by profiling production assets at uniform
            // level 3.
            int n = Math.Max(coarseVerts,
                        Math.Min((int)(5 * 1024 * 1024),
                            coarseVerts * 2));
            _dests.Reserve(n);
            _sources.Reserve(n);
            _weights.Reserve(n);

            if (!genCtrlVertStencils)
                return;

            // Generate trivial control vert stencils
            _sources.Resize(coarseVerts);
            _weights.Resize(coarseVerts);
            _dests.Resize(coarseVerts);
            _indices.Resize(coarseVerts);
            _sizes.Resize(coarseVerts);

            for (int i = 0; i < coarseVerts; i++)
            {
                _indices[i] = i;
                _sizes[i] = 1;
                _dests[i] = i;
                _sources[i] = i;
                _weights[i] = REAL.One;
            }

            _size = (int)_sources.size();
            _lastOffset = _size - 1;
        }

        public void AddWithWeight<W, WACCUM>(int src, int dest, W weight, WACCUM weights)
            where W : IMultiplyOperators<W, W, W>, IMultiplicativeIdentity<W, W>
            where WACCUM : IWeightAccumulator<W>
        {
            // Factorized stencils are expressed purely in terms of the control
            // mesh verts. Without this flattening, level_i's weights would point
            // to level_i-1, which would point to level_i-2, until the final level
            // points to the control verts.
            //
            // So here, we check if the incoming vert (src) is in the control mesh,
            // if it is, we can simply merge it without attempting to resolve it
            // first.
            if (src < _coarseVertCount)
            {
                merge(src, dest, weight, W.MultiplicativeIdentity, _lastOffset, _size, weights);
                return;
            }

            // src is not in the control mesh, so resolve all contributing coarse
            // verts (src itself is made up of many control vert weights).
            //
            // Find the src stencil and number of contributing CVs.
            int len = _sizes[src];
            int start = _indices[src];

            for (int i = start; i < start + len; i++)
            {
                // Invariant: by processing each level in order and each vertex in
                // dependent order, any src stencil vertex reference is guaranteed
                // to consist only of coarse verts: therefore resolving src verts
                // must yield verts in the coarse mesh.
                System.Diagnostics.Debug.Assert(_sources[i] < _coarseVertCount);

                // Merge each of src's contributing verts into this stencil.
                merge(_sources[i], dest, weights.Get(i), weight,
                                    _lastOffset, _size, weights);
            }
        }

        public struct Point1stDerivAccumulator : IWeightAccumulator<Point1stDerivWeight<REAL>>
        {
            private WeightTable<REAL> _tbl;

            public Point1stDerivAccumulator(WeightTable<REAL> tbl)
            {
                _tbl = tbl;
            }
            public void PushBack(Point1stDerivWeight<REAL> weight)
            {
                _tbl._weights.PushBack(weight.p);
                _tbl._duWeights.PushBack(weight.du);
                _tbl._dvWeights.PushBack(weight.dv);
            }
            public void Add(int i, Point1stDerivWeight<REAL> weight)
            {
                _tbl._weights[i] += weight.p;
                _tbl._duWeights[i] += weight.du;
                _tbl._dvWeights[i] += weight.dv;
            }
            public Point1stDerivWeight<REAL> Get(int index)
            {
                return new Point1stDerivWeight<REAL>(_tbl._weights[index],
                                                     _tbl._duWeights[index],
                                                     _tbl._dvWeights[index]);
            }
        }
        public Point1stDerivAccumulator GetPoint1stDerivAccumulator()
        {
            return new Point1stDerivAccumulator(this);
        }

        public struct Point2ndDerivAccumulator : IWeightAccumulator<Point2ndDerivWeight<REAL>>
        {
            private WeightTable<REAL> _tbl;

            public Point2ndDerivAccumulator(WeightTable<REAL> tbl)
            {
                _tbl = tbl;
            }
            public void PushBack(Point2ndDerivWeight<REAL> weight)
            {
                _tbl._weights.PushBack(weight.p);
                _tbl._duWeights.PushBack(weight.du);
                _tbl._dvWeights.PushBack(weight.dv);
                _tbl._duuWeights.PushBack(weight.duu);
                _tbl._duvWeights.PushBack(weight.duv);
                _tbl._dvvWeights.PushBack(weight.dvv);
            }
            public void Add(int i, Point2ndDerivWeight<REAL> weight)
            {
                _tbl._weights[i] += weight.p;
                _tbl._duWeights[i] += weight.du;
                _tbl._dvWeights[i] += weight.dv;
                _tbl._duuWeights[i] += weight.duu;
                _tbl._duvWeights[i] += weight.duv;
                _tbl._dvvWeights[i] += weight.dvv;
            }
            public Point2ndDerivWeight<REAL> Get(int index)
            {
                return new Point2ndDerivWeight<REAL>(_tbl._weights[index],
                                                     _tbl._duWeights[index],
                                                     _tbl._dvWeights[index],
                                                     _tbl._duuWeights[index],
                                                     _tbl._duvWeights[index],
                                                     _tbl._dvvWeights[index]);
            }
        }
        public Point2ndDerivAccumulator GetPoint2ndDerivAccumulator()
        {
            return new Point2ndDerivAccumulator(this);
        }

        public struct ScalarAccumulator : IWeightAccumulator<REAL>
        {
            private WeightTable<REAL> _tbl;

            public ScalarAccumulator(WeightTable<REAL> tbl)
            {
                _tbl = tbl;
            }
            public void PushBack(REAL weight)
            {
                _tbl._weights.PushBack(weight);
            }
            public void Add(int i, REAL w)
            {
                _tbl._weights[i] += w;
            }
            public REAL Get(int index)
            {
                return _tbl._weights[index];
            }
        }
        public ScalarAccumulator GetScalarAccumulator()
        {
            return new ScalarAccumulator(this);
        }

        public StdVector<int> GetOffsets() { return _indices; }

        public StdVector<int> GetSizes() { return _sizes; }

        public StdVector<int> GetSources() { return _sources; }

        public StdVector<REAL> GetWeights() { return _weights; }

        public StdVector<REAL> GetDuWeights() { return _duWeights; }

        public StdVector<REAL> GetDvWeights() { return _dvWeights; }

        public StdVector<REAL> GetDuuWeights() { return _duuWeights; }

        public StdVector<REAL> GetDuvWeights() { return _duvWeights; }

        public StdVector<REAL> GetDvvWeights() { return _dvvWeights; }

        public void SetCoarseVertCount(int numVerts)
        {
            _coarseVertCount = numVerts;
        }

        // Merge a vertex weight into the stencil table, if there is an existing
        // weight for a given source vertex it will be combined.
        //
        // PERFORMANCE: caution, this function is super hot.
        private void merge<W, WACCUM>(int src, int dst, W weight,
                   // Delaying weight*factor multiplication hides memory latency of
                   // accessing weight[i], yielding more stable performance.
                   W weightFactor,
                   // Similarly, passing offset & tableSize as params yields higher
                   // performance than accessing the class members directly.
                   int lastOffset, int tableSize, WACCUM weights)
            where W : IMultiplyOperators<W, W, W>, IMultiplicativeIdentity<W, W>
            where WACCUM : IWeightAccumulator<W>
        {
            // The lastOffset is the vertex we're currently processing, by
            // leveraging this we need not lookup the dest stencil size or offset.
            //
            // Additionally, if the client does not want the resulting verts
            // compacted, do not attempt to combine weights.
            if (_compactWeights && !_dests.empty() && _dests[lastOffset] == dst)
            {
                // tableSize is exactly _sources.size(), but using tableSize is
                // significantly faster.
                for (int i = lastOffset; i < tableSize; i++)
                {
                    // If we find an existing vertex that matches src, we need to
                    // combine the weights to avoid duplicate entries for src.
                    if (_sources[i] == src)
                    {
                        weights.Add(i, weight * weightFactor);
                        return;
                    }
                }
            }

            // We haven't seen src yet, insert it as a new vertex weight.
            add(src, dst, weight * weightFactor, weights);
        }

        // Add a new vertex weight to the stencil table.
        private void add<W, WACCUM>(int src, int dst, W weight, WACCUM weights)
            where W : IMultiplyOperators<W, W, W>, IMultiplicativeIdentity<W, W>
            where WACCUM : IWeightAccumulator<W>
        {
            // The _dests array has num(weights) elements mapping each individual
            // element back to a specific stencil. The array is constructed in such
            // a way that the current stencil being built is always at the end of
            // the array, so if the dests array is empty or back() doesn't match
            // dst, then we just started building a new stencil.
            if (_dests.empty() || dst != _dests.Back())
            {
                // _indices and _sizes always have num(stencils) elements so that
                // stencils can be directly looked up by their index in these
                // arrays. So here, ensure that they are large enough to hold the
                // new stencil about to be built.
                if (dst + 1 > (int)_indices.size())
                {
                    _indices.Resize(dst + 1);
                    _sizes.Resize(dst + 1);
                }
                // Initialize the new stencil's meta-data (offset, size).
                _indices[dst] = (int)_sources.size();
                _sizes[dst] = 0;
                // Keep track of where the current stencil begins, which lets us
                // avoid having to look it up later.
                _lastOffset = (int)_sources.size();
            }
            // Cache the number of elements as an optimization, it's faster than
            // calling size() on any of the vectors.
            _size++;

            // Increment the current stencil element size.
            _sizes[dst]++;
            // Track this element as belonging to the stencil "dst".
            _dests.PushBack(dst);

            // Store the actual stencil data.
            _sources.PushBack(src);
            weights.PushBack(weight);
        }

        // The following vectors are explicitly stored as non-interleaved elements
        // to reduce cache misses.

        // Stencil to destination vertex map.
        private StdVector<int> _dests;

        // The actual stencil data.
        private StdVector<int> _sources;
        private StdVector<REAL> _weights;
        private StdVector<REAL> _duWeights;
        private StdVector<REAL> _dvWeights;
        private StdVector<REAL> _duuWeights;
        private StdVector<REAL> _duvWeights;
        private StdVector<REAL> _dvvWeights;

        // Index data used to recover stencil-to-vertex mapping.
        private StdVector<int> _indices;
        private StdVector<int> _sizes;

        // Acceleration members to avoid pointer chasing and reverse loops.
        private int _size;
        private int _lastOffset;
        private int _coarseVertCount;
        private bool _compactWeights;
    }

    public class StencilBuilder<REAL> where REAL : unmanaged, INumber<REAL>
    {
        public StencilBuilder(int coarseVertCount,
                              bool genCtrlVertStencils = true,
                              bool compactWeights = true)
        {
            _weightTable = new WeightTable<REAL>(coarseVertCount,
                                                 genCtrlVertStencils,
                                                 compactWeights);
            _handles = new Index[0];
        }

        // (destructor: nothing to do in C#)

        // TODO: noncopyable.

        public int GetNumVerticesTotal()
        {
            return (int)_weightTable.GetWeights().size();
        }

        public int GetNumVertsInStencil(int stencilIndex)
        {
            if (stencilIndex > _weightTable.GetSizes().size() - 1)
                return 0;

            return (int)_weightTable.GetSizes()[stencilIndex];
        }

        public void SetCoarseVertCount(int numVerts)
        {
            _weightTable.SetCoarseVertCount(numVerts);
        }

        // Mapping from stencil[i] to its starting offset in the sources[] and weights[] arrays;
        public StdVector<int> GetStencilOffsets()
        {
            return _weightTable.GetOffsets();
        }

        // The number of contributing sources and weights in stencil[i]
        public StdVector<int> GetStencilSizes()
        {
            return _weightTable.GetSizes();
        }

        // The absolute source vertex offsets.
        public StdVector<int> GetStencilSources()
        {
            return _weightTable.GetSources();
        }

        // The individual vertex weights, each weight is paired with one source.
        public StdVector<REAL> GetStencilWeights()
        {
            return _weightTable.GetWeights();
        }

        public StdVector<REAL> GetStencilDuWeights()
        {
            return _weightTable.GetDuWeights();
        }

        public StdVector<REAL> GetStencilDvWeights()
        {
            return _weightTable.GetDvWeights();
        }

        public StdVector<REAL> GetStencilDuuWeights()
        {
            return _weightTable.GetDuuWeights();
        }

        public StdVector<REAL> GetStencilDuvWeights()
        {
            return _weightTable.GetDuvWeights();
        }

        public StdVector<REAL> GetStencilDvvWeights()
        {
            return _weightTable.GetDvvWeights();
        }

        // Vertex Facade.
        //
        // (A struct: it is a light-weight handle (owner, index).  It implements IPrimvar and
        //  IPrimvarBuffer so that it can be used as a primvar buffer of the PrimvarRefiner;
        //  the indexer returns a reference to a persistent handle slot kept by the owner.)
        public struct Index : IPrimvar<Index, REAL>, IPrimvarBuffer<Index>
        {
            public Index(StencilBuilder<REAL> owner, int index)
            {
                _owner = owner;
                _index = index;
            }

            // Add with point/vertex weight only.
            public void AddWithWeight(Index src, REAL weight)
            {
                // Ignore no-op weights.
                if (StencilBuilderUtil.isWeightZero(weight))
                {
                    return;
                }
                _owner._weightTable.AddWithWeight(src._index, _index, weight,
                                            _owner._weightTable.GetScalarAccumulator());
            }

            public void AddWithWeight(StencilReal<REAL> src, REAL weight)
            {
                if (StencilBuilderUtil.isWeightZero(weight))
                {
                    return;
                }

                int srcSize = src.GetSize();
                ConstArray<int> srcIndices = src.GetVertexIndices();
                ConstArray<REAL> srcWeights = src.GetWeights();

                for (int i = 0; i < srcSize; ++i)
                {
                    REAL w = srcWeights[i];
                    if (StencilBuilderUtil.isWeightZero(w))
                    {
                        continue;
                    }

                    int srcIndex = srcIndices[i];

                    REAL wgt = weight * w;
                    _owner._weightTable.AddWithWeight(srcIndex, _index, wgt,
                                        _owner._weightTable.GetScalarAccumulator());
                }
            }

            // Add with first derivative.
            public void AddWithWeight(StencilReal<REAL> src,
                REAL weight, REAL du, REAL dv)
            {
                if (StencilBuilderUtil.isWeightZero(weight) && StencilBuilderUtil.isWeightZero(du) && StencilBuilderUtil.isWeightZero(dv))
                {
                    return;
                }

                int srcSize = src.GetSize();
                ConstArray<int> srcIndices = src.GetVertexIndices();
                ConstArray<REAL> srcWeights = src.GetWeights();

                for (int i = 0; i < srcSize; ++i)
                {
                    REAL w = srcWeights[i];
                    if (StencilBuilderUtil.isWeightZero(w))
                    {
                        continue;
                    }

                    int srcIndex = srcIndices[i];

                    Point1stDerivWeight<REAL> wgt = new Point1stDerivWeight<REAL>(weight, du, dv) * new Point1stDerivWeight<REAL>(w);
                    _owner._weightTable.AddWithWeight(srcIndex, _index, wgt,
                                       _owner._weightTable.GetPoint1stDerivAccumulator());
                }
            }

            // Add with first and second derivatives.
            public void AddWithWeight(StencilReal<REAL> src,
                REAL weight, REAL du, REAL dv, REAL duu, REAL duv, REAL dvv)
            {
                if (StencilBuilderUtil.isWeightZero(weight) && StencilBuilderUtil.isWeightZero(du) && StencilBuilderUtil.isWeightZero(dv) &&
                    StencilBuilderUtil.isWeightZero(duu) && StencilBuilderUtil.isWeightZero(duv) && StencilBuilderUtil.isWeightZero(dvv))
                {
                    return;
                }

                int srcSize = src.GetSize();
                ConstArray<int> srcIndices = src.GetVertexIndices();
                ConstArray<REAL> srcWeights = src.GetWeights();

                for (int i = 0; i < srcSize; ++i)
                {
                    REAL w = srcWeights[i];
                    if (StencilBuilderUtil.isWeightZero(w))
                    {
                        continue;
                    }

                    int srcIndex = srcIndices[i];

                    Point2ndDerivWeight<REAL> wgt = new Point2ndDerivWeight<REAL>(weight, du, dv, duu, duv, dvv) * new Point2ndDerivWeight<REAL>(w);
                    _owner._weightTable.AddWithWeight(srcIndex, _index, wgt,
                                       _owner._weightTable.GetPoint2ndDerivAccumulator());
                }
            }

            // Index operator[](int index) const { return Index(_owner, index+_index); }
            // (returns a reference to the persistent handle of the owner for vertex index+_index)
            public ref Index this[int index]
            {
                get
                {
                    return ref _owner.getHandle(index + _index);
                }
            }

            public int GetOffset() { return _index; }

            public void Clear() { /*nothing to do here*/ }

            //  void AddWithWeight(Index const&, REAL) is the IPrimvar<Index, REAL> member above.

            private StencilBuilder<REAL> _owner;
            private int _index;
        }

        //  Persistent Index handles (see Index.this[]): _handles[k] == Index(this, k)
        private ref Index getHandle(int k)
        {
            if (k >= _handles.Length)
            {
                int oldLength = _handles.Length;
                System.Array.Resize(ref _handles, Math.Max(k + 1, oldLength * 2));
                for (int i = oldLength; i < _handles.Length; ++i)
                {
                    _handles[i] = new Index(this, i);
                }
            }
            return ref _handles[k];
        }

        //  Returns the handle for the base (index 0) of the vertices -- i.e. the C++ "Index(this, 0)":
        public Index GetIndex(int index)
        {
            return new Index(this, index);
        }

        private WeightTable<REAL> _weightTable;
        private Index[] _handles;
    }
}
