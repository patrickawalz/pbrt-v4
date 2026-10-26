// Copyright Contributors to the OpenVDB Project
// SPDX-License-Identifier: MPL-2.0
// nanovdb/util/GridHandle.h
//
// NOT ported: getDeviceGrid/deviceUpload/deviceDownload (CUDA device buffers).

using System;
using System.Numerics;

namespace nanovdb
{
    // The requirements the C++ places on BufferT (HostBuffer, NanoVDBBuffer, ...)
    public unsafe interface IGridBuffer<TSelf>
    {
        byte* data();
        ulong size();
        bool empty();
        void clear();
        // static BufferT create(uint64_t size, const BufferT* context = nullptr)
        static abstract TSelf create(ulong size, TSelf context);
    }

    // This class serves to manage a raw memory buffer of a NanoVDB Grid.
    public unsafe class GridHandle<BufferT> : IDisposable where BufferT : class, IGridBuffer<BufferT>, new()
    {
        BufferT mBuffer = new BufferT();

        // Move constructor from a buffer
        public GridHandle(BufferT buffer) { mBuffer = buffer; }
        // Empty ctor
        public GridHandle() { }

        // Move copy assignment operation
        public void MoveFrom(GridHandle<BufferT> other)
        {
            mBuffer = other.mBuffer;
            other.mBuffer = new BufferT();
        }
        public void Dispose() { reset(); }
        // clear the buffer
        public void reset() { mBuffer.clear(); }

        // Return a reference to the buffer
        public BufferT buffer() { return mBuffer; }

        public byte* data() { return mBuffer.data(); }
        // Returns the size in bytes of the raw memory buffer managed by this GridHandle's allocator.
        public ulong size() { return mBuffer.size(); }

        // Return true if this handle is empty, i.e. has no allocated memory
        public bool empty() { return size() == 0; }
        // Return true if this handle contains a grid
        public static implicit operator bool(GridHandle<BufferT> h) { return h != null && !h.empty(); }

        // Returns a pointer to the grid meta data. The returned view is null if the
        // GridHandle was not initialized
        public GridMetaData gridMetaData() { return new GridMetaData(data()); }

        // Returns the GridType handled by this instance, and GridType::End if empty
        public GridType gridType()
        {
            GridMetaData ptr = gridMetaData();
            return !ptr.IsNull ? ptr.gridType() : GridType.End;
        }

        // Return the number of grids contained in this buffer
        public uint gridCount()
        {
            GridMetaData ptr = gridMetaData();
            return !ptr.IsNull ? ptr.gridCount() : 0;
        }

        // Returns the n'th NanoVDB grid encoded in this GridHandle. The returned view is
        // null if the GridHandle was not initialized, n is invalid or if the template
        // parameter does not match the specified grid!
        public NanoGrid<ValueT> grid<ValueT>(uint n = 0) where ValueT : unmanaged, INumber<ValueT>
        {
            return getGrid<ValueT>(n);
        }

        NanoGrid<ValueT> getGrid<ValueT>(uint index) where ValueT : unmanaged, INumber<ValueT>
        {
            byte* data = mBuffer.data();
            NanoGrid<ValueT> grid = new NanoGrid<ValueT>(data);
            if (grid.p == null || index >= grid.gridCount())  // un-initialized or index is out of range
                return new NanoGrid<ValueT>(null);
            while (index != grid.gridIndex())
            {
                data += grid.gridSize();
                grid = new NanoGrid<ValueT>(data);
            }
            return grid.gridType() == NanoVDB.mapToGridType<ValueT>() ? grid : new NanoGrid<ValueT>(null);
        }
    }
}
