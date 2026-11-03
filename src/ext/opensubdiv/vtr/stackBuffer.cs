//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/stackBuffer.h
//
//  The C++ StackBuffer<TYPE, SIZE, POD> uses inline storage up to SIZE elements.  The C# version is
//  StackBuffer<TYPE> (the SIZE and POD template arguments are dropped) backed by a plain array;
//  elements are default-initialized.  It converts implicitly to TYPE[] (the C++ operator TYPE*) and has an
//  indexer.
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Vtr.Internal
{
    public class StackBuffer<TYPE>
    {
        private TYPE[] _data;
        private int _size;

        public StackBuffer()
        {
            _data = System.Array.Empty<TYPE>();
            _size = 0;
        }

        public StackBuffer(int size)
        {
            _data = new TYPE[size];
            _size = size;
        }

        public static implicit operator TYPE[](StackBuffer<TYPE> buffer)
        {
            return buffer._data;
        }

        public ref TYPE this[int index]
        {
            get { return ref _data[index]; }
        }

        public TYPE[] Data
        {
            get { return _data; }
        }

        public int GetSize()
        {
            return _size;
        }

        public void SetSize(int size)
        {
            if (size > _data.Length)
            {
                _data = new TYPE[size];
            }
            _size = size;
        }

        public void Reserve(int capacity)
        {
            if (capacity > _data.Length)
            {
                _data = new TYPE[capacity];
            }
        }
    }
}
