//
//   Copyright 2014 DreamWorks Animation LLC.
//
//   Licensed under the terms set forth in the LICENSE.txt file available at
//   https://opensubdiv.org/license.
//
// C# port of OpenSubdiv 3.8.0 vtr/array.h
//
//  ConstArray<T> and Array<T> are readonly structs wrapping a (T[] array, int offset, int size) triple
//  in place of the C++ (pointer, size) pair.  begin() returns the offset into the backing array.
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Vtr
{
    //
    //  This class provides a simple array-like interface -- a subset std::vector's interface -- for
    //  a sequence of elements stored in contiguous memory.  It provides a unified representation for
    //  referencing data on the stack, all or a subset of std::vector<>, or anywhere else in memory.
    //
    public readonly struct ConstArray<TYPE>
    {
        public readonly TYPE[] _array;
        public readonly int _offset;
        public readonly int _size;

        public ConstArray(TYPE[] array, int offset, int sizeArg)
        {
            _array = array;
            _offset = offset;
            _size = sizeArg;
        }

        public int size()
        {
            return _size;
        }

        public bool empty()
        {
            return _size == 0;
        }

        public bool IsEmpty
        {
            get { return _size == 0; }
        }

        public TYPE this[int index]
        {
            get { return _array[_offset + index]; }
        }

        /// Offset of the first element in the backing array (the C++ begin() pointer).
        public int begin()
        {
            return _offset;
        }

        /// Offset one past the last element in the backing array (the C++ end() pointer).
        public int end()
        {
            return _offset + _size;
        }

        public TYPE[] Data
        {
            get { return _array; }
        }

        public int FindIndexIn4Tuple(TYPE value)
        {
            System.Diagnostics.Debug.Assert(_size >= 4);
            EqualityComparer<TYPE> cmp = EqualityComparer<TYPE>.Default;
            if (cmp.Equals(value, _array[_offset + 0]))
            {
                return 0;
            }
            if (cmp.Equals(value, _array[_offset + 1]))
            {
                return 1;
            }
            if (cmp.Equals(value, _array[_offset + 2]))
            {
                return 2;
            }
            if (cmp.Equals(value, _array[_offset + 3]))
            {
                return 3;
            }
            System.Diagnostics.Debug.Assert(false, "FindIndexIn4Tuple() did not find expected value!");
            return -1;
        }

        public int FindIndex(TYPE value)
        {
            EqualityComparer<TYPE> cmp = EqualityComparer<TYPE>.Default;
            for (int i = 0; i < size(); ++i)
            {
                if (cmp.Equals(value, _array[_offset + i]))
                {
                    return i;
                }
            }
            return -1;
        }
    }

    public readonly struct Array<TYPE>
    {
        public readonly TYPE[] _array;
        public readonly int _offset;
        public readonly int _size;

        public Array(TYPE[] array, int offset, int sizeArg)
        {
            _array = array;
            _offset = offset;
            _size = sizeArg;
        }

        public int size()
        {
            return _size;
        }

        public bool empty()
        {
            return _size == 0;
        }

        public bool IsEmpty
        {
            get { return _size == 0; }
        }

        public ref TYPE this[int index]
        {
            get { return ref _array[_offset + index]; }
        }

        public int begin()
        {
            return _offset;
        }

        public int end()
        {
            return _offset + _size;
        }

        public TYPE[] Data
        {
            get { return _array; }
        }

        public int FindIndexIn4Tuple(TYPE value)
        {
            return ((ConstArray<TYPE>)this).FindIndexIn4Tuple(value);
        }

        public int FindIndex(TYPE value)
        {
            return ((ConstArray<TYPE>)this).FindIndex(value);
        }

        //  Pointer-compatible conversion (Array<T> -> ConstArray<T>):
        public static implicit operator ConstArray<TYPE>(Array<TYPE> a)
        {
            return new ConstArray<TYPE>(a._array, a._offset, a._size);
        }
    }
}
