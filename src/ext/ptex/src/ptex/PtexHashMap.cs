// PtexHashMap.h
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Contains PtexHashMap, a lightweight multi-threaded hash table.

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Ptex
{
    // The interface implemented by the key types of PtexHashMap (StringKey, IntKey)
    public interface IPtexKey<K>
    {
        void copy(K key);
        void move(K key);
        bool matches(K key);
        bool isEmpty();
        uint hash();
    }

    public static unsafe class PtexHashMapFunctions
    {
        public static uint memHash(byte[] val, int len)
        {
            int len64 = len & ~7;
            ulong[] val64 = new ulong[4];
            val64[0] = 0;
            for (int k = 0; k < (len & 7); ++k)  // memcpy(&val64[0], &val[len64], len & 7)
                val64[0] |= (ulong)val[len64 + k] << (8 * k);
            ulong[] hashval = { 0, 0, 0, 0 };
            unchecked
            {
                hashval[0] = val64[0] * 16777619;

                for (int i = 0; i + 32 <= len64; i += 32)
                {
                    for (int j = 0; j < 4; ++j)
                    {
                        val64[j] = BitConverter.ToUInt64(val, i + j * 8);
                        hashval[j] = (hashval[j] * 16777619) ^ val64[j];
                    }
                }
                hashval[0] = (hashval[0] * 16777619) ^ hashval[1];
                hashval[2] = (hashval[2] * 16777619) ^ hashval[3];
                hashval[0] = (hashval[0] * 16777619) ^ hashval[2];
            }
            return (uint)hashval[0];
        }

        public static bool memCompare(byte[] a, byte[] b, int len)
        {
            // returns true if different (the C++ returns a nonzero value)
            for (int i = 0; i < len; ++i)
                if (a[i] != b[i]) return true;
            return false;
        }
    }

    public sealed class StringKey : IPtexKey<StringKey>
    {
        volatile byte[] _val;
        volatile uint _len;
        volatile uint _hash;
        volatile bool _ownsVal;

        public StringKey() { _val = null; _len = 0; _hash = 0; _ownsVal = false; }
        public StringKey(string val)
        {
            _val = System.Text.Encoding.UTF8.GetBytes(val);
            _len = (uint)_val.Length;
            _hash = PtexHashMapFunctions.memHash(_val, (int)_len);
            _ownsVal = false;
        }

        public void copy(StringKey key)
        {
            byte[] newval = new byte[key._len + 1];
            Array.Copy(key._val, newval, key._len);
            _val = newval;
            _len = key._len;
            _hash = key._hash;
            _ownsVal = true;
        }

        public void move(StringKey key)
        {
            _val = key._val;
            _len = key._len;
            _hash = key._hash;
            _ownsVal = key._ownsVal;
            key._ownsVal = false;
        }

        public bool matches(StringKey key)
        {
            byte[] v = _val;
            return key._hash == _hash && key._len == _len && v != null && !PtexHashMapFunctions.memCompare(key._val, v, (int)_len);
        }

        public bool isEmpty() { return _val == null; }

        public uint hash() { return _hash; }
    }

    public sealed class IntKey : IPtexKey<IntKey>
    {
        int _val;

        public IntKey() { _val = 0; }
        public IntKey(int val) { _val = val; }
        public void copy(IntKey key) { _val = key._val; }
        public void move(IntKey key) { _val = key._val; }
        public bool matches(IntKey key) { return _val == key._val; }
        public bool isEmpty() { return _val == 0; }
        public uint hash() { return (uint)((_val * 7919) & ~0xf); }
    }

    public sealed class PtexHashMap<Key, Value> where Key : class, IPtexKey<Key>, new() where Value : class
    {
        sealed class Entry
        {
            public Entry() { key = new Key(); value = null; }
            public readonly Key key;
            public volatile Value value;
        }

        void initContents()
        {
            _numEntries = 16;
            _size = 0;
            Entry[] entries = new Entry[_numEntries];
            for (int i = 0; i < entries.Length; ++i) entries[i] = new Entry();
            _entries = entries;
        }

        void deleteContents()
        {
            for (uint i = 0; i < _numEntries; ++i)
            {
                if (_entries[i].value != null && _entries[i].value is IDisposable d) d.Dispose();  // delete _entries[i].value
            }
            _entries = null;
            _oldEntries.Clear();
        }

        public PtexHashMap()
        {
            initContents();
        }

        public void clear()
        {
            deleteContents();
            initContents();
        }

        public uint size() { return _size; }

        public Value get(Key key)
        {
            uint mask = _numEntries - 1;
            Entry[] entries = getEntries();
            uint hash = key.hash();

            Value result = null;
            for (uint i = hash; ; ++i)
            {
                Entry e = entries[i & mask];
                if (e.key.matches(key))
                {
                    result = e.value;
                    break;
                }
                if (e.value == null)
                {
                    break;
                }
            }

            return result;
        }

        public Value tryInsert(Key key, Value value, ref ulong newMemUsed)
        {
            Entry[] entries = lockEntriesAndGrowIfNeeded(ref newMemUsed);
            uint mask = _numEntries - 1;
            uint hash = key.hash();

            Value result = null;
            for (uint i = hash; ; ++i)
            {
                Entry e = entries[i & mask];
                if (e.value == null)
                {
                    e.value = value;
                    ++_size;
                    PtexPlatform.PtexMemoryFence();
                    e.key.copy(key);
                    result = e.value;
                    break;
                }
                while (e.key.isEmpty()) ;
                if (e.key.matches(key))
                {
                    result = e.value;
                    break;
                }
            }
            unlockEntries(entries);
            return result;
        }

        public void foreach_(Action<Value> fn)
        {
            Entry[] entries = getEntries();
            for (uint i = 0; i < _numEntries; ++i)
            {
                Value v = entries[i].value;
                if (v != null) fn(v);
            }
        }

        Entry[] getEntries()
        {
            while (true)
            {
                Entry[] entries = _entries;
                if (entries != null) return entries;
            }
        }

        Entry[] lockEntries()
        {
            while (true)
            {
                Entry[] entries = _entries;
                if (entries != null && PtexPlatform.AtomicCompareAndSwap(ref _entries, entries, null))
                {
                    return entries;
                }
            }
        }

        void unlockEntries(Entry[] entries)
        {
            PtexPlatform.AtomicStore(ref _entries, entries);
        }

        Entry[] lockEntriesAndGrowIfNeeded(ref ulong newMemUsed)
        {
            Entry[] entries = lockEntries();
            if (_size * 2 >= _numEntries)
            {
                entries = grow(entries, ref newMemUsed);
            }
            return entries;
        }

        Entry[] grow(Entry[] oldEntries, ref ulong newMemUsed)
        {
            _oldEntries.Add(oldEntries);
            uint numNewEntries = _numEntries * 2;
            Entry[] entries = new Entry[numNewEntries];
            for (int i = 0; i < entries.Length; ++i) entries[i] = new Entry();
            newMemUsed = numNewEntries * 16;  // sizeof(Entry) in the C++ (Key + Value on a 64-bit target)
            uint mask = numNewEntries - 1;
            for (uint oldIndex = 0; oldIndex < _numEntries; ++oldIndex)
            {
                Entry oldEntry = oldEntries[oldIndex];
                if (oldEntry.value != null)
                {
                    for (int newIndex = (int)oldEntry.key.hash(); ; ++newIndex)
                    {
                        Entry newEntry = entries[(uint)newIndex & mask];
                        if (newEntry.value == null)
                        {
                            newEntry.key.move(oldEntry.key);
                            newEntry.value = oldEntry.value;
                            break;
                        }
                    }
                }
            }
            _numEntries = numNewEntries;
            return entries;
        }

        Entry[] _entries;
        volatile uint _numEntries;
        volatile uint _size;
        List<Entry[]> _oldEntries = new List<Entry[]>();
    }
}
