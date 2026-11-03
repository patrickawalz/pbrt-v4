// Helper replacing std::vector<T> for the OpenSubdiv C# port (see PORTING.md).
using System;
using System.Collections.Generic;

namespace OpenSubdiv.Vtr
{
    //
    //  A minimal std::vector<T> equivalent.  Data is the backing array (its Length is the capacity and
    //  is >= Count); Count is the number of valid elements.  Elements created by Resize are
    //  default-initialized (zero), as with value-initialized std::vector elements.
    //
    public sealed class StdVector<T>
    {
        public T[] Data;
        public int Count;

        public StdVector()
        {
            Data = System.Array.Empty<T>();
            Count = 0;
        }

        public StdVector(int count)
        {
            Data = new T[count];
            Count = count;
        }

        public StdVector(int count, T value)
        {
            Data = new T[count];
            Count = count;
            System.Array.Fill(Data, value);
        }

        public StdVector(StdVector<T> other)
        {
            Data = new T[other.Count];
            System.Array.Copy(other.Data, Data, other.Count);
            Count = other.Count;
        }

        public ref T this[int i]
        {
            get { return ref Data[i]; }
        }

        public int size()
        {
            return Count;
        }

        public int Size()
        {
            return Count;
        }

        public bool empty()
        {
            return Count == 0;
        }

        public bool Empty()
        {
            return Count == 0;
        }

        public int Capacity()
        {
            return Data.Length;
        }

        public void Reserve(int n)
        {
            if (n > Data.Length)
            {
                System.Array.Resize(ref Data, n);
            }
        }

        public void Resize(int n)
        {
            if (n > Data.Length)
            {
                int newCap = Math.Max(n, Data.Length * 2);
                System.Array.Resize(ref Data, newCap);
            }
            if (n < Count)
            {
                //  Elements beyond the new size are discarded; clear them so a later grow sees zeros
                System.Array.Clear(Data, n, Count - n);
            }
            Count = n;
        }

        public void Resize(int n, T value)
        {
            int oldCount = Count;
            Resize(n);
            for (int i = oldCount; i < n; ++i)
            {
                Data[i] = value;
            }
        }

        public void Clear()
        {
            System.Array.Clear(Data, 0, Count);
            Count = 0;
        }

        /// std::vector<T>().swap(v):  release all storage.
        public void ShrinkToFit()
        {
            if (Count == 0)
            {
                Data = System.Array.Empty<T>();
            }
            else if (Data.Length != Count)
            {
                System.Array.Resize(ref Data, Count);
            }
        }

        public void PushBack(T value)
        {
            if (Count == Data.Length)
            {
                System.Array.Resize(ref Data, Math.Max(4, Data.Length * 2));
            }
            Data[Count++] = value;
        }

        public void PopBack()
        {
            Data[--Count] = default(T);
        }

        public ref T Back()
        {
            return ref Data[Count - 1];
        }

        public ref T Front()
        {
            return ref Data[0];
        }

        public void Assign(int n, T value)
        {
            Data = new T[n];
            System.Array.Fill(Data, value);
            Count = n;
        }

        public void Swap(StdVector<T> other)
        {
            T[] d = Data;
            Data = other.Data;
            other.Data = d;
            int c = Count;
            Count = other.Count;
            other.Count = c;
        }

        /// Copy of the valid elements as a plain array.
        public T[] ToArray()
        {
            T[] r = new T[Count];
            System.Array.Copy(Data, r, Count);
            return r;
        }

        //  Array / ConstArray views (the C++ "&v[offset]" with a size):
        public Array<T> Arr()
        {
            return new Array<T>(Data, 0, Count);
        }

        public Array<T> Arr(int offset, int size)
        {
            return new Array<T>(Data, offset, size);
        }

        public ConstArray<T> ConstArr()
        {
            return new ConstArray<T>(Data, 0, Count);
        }

        public ConstArray<T> ConstArr(int offset, int size)
        {
            return new ConstArray<T>(Data, offset, size);
        }
    }
}
