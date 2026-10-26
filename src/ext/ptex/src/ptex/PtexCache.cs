// PtexCache.h, PtexCache.cpp
/*
PTEX SOFTWARE
Copyright 2014 Disney Enterprises, Inc.  All rights reserved
(see LICENSE.txt in ext/ptex)
*/

// Ptex Cache Implementation
//
// Ownership: the cache owns all files and data. If a texture is in use, the cache will not
// touch it. When it is no longer in use, it may be kept or deleted to keep resource usage
// under the set limits. Deletions are done in lru order.

using System;
using System.Collections.Generic;
using System.IO;

namespace Ptex
{
    // Intrusive LRU list item (to be used only by PtexLruList). The C++ finds the containing
    // object by subtracting the member offset; here the item records its owner.
    public sealed class PtexLruItem
    {
        PtexLruItem _prev;
        PtexLruItem _next;
        public object owner;

        void extract()
        {
            _next._prev = _prev;
            _prev._next = _next;
            _next = _prev = this;
        }

        public PtexLruItem(object ownerArg = null) { _prev = this; _next = this; owner = ownerArg; }

        // add item to end of list (pointed to by _prev)
        public void push(PtexLruItem item)
        {
            item.extract();
            _prev._next = item;
            item._next = this;
            item._prev = _prev;
            _prev = item;
        }

        // remove item from front of list (pointed to by _next)
        public PtexLruItem pop()
        {
            if (_next == this) return null;
            PtexLruItem item = _next;
            _next.extract();
            return item;
        }
    }

    // Intrusive LRU list (with LRU item stored as member of T)
    public sealed class PtexLruList<T> where T : class
    {
        PtexLruItem _end = new PtexLruItem();
        Func<T, PtexLruItem> _item;

        public PtexLruList(Func<T, PtexLruItem> item) { _item = item; }

        public void push(T node)
        {
            // the item is added to the intrusive pointer specified by the selector
            _end.push(_item(node));
        }

        public T pop()
        {
            PtexLruItem it = _end.pop();
            return it != null ? (T)it.owner : null;
        }
    }

    public unsafe class PtexCachedReader : PtexReader
    {
        PtexReaderCache _cache;
        int _refCount;
        ulong _memUsedAccountedFor;
        ulong _opensAccountedFor;
        ulong _blockReadsAccountedFor;
        internal PtexLruItem _openFilesItem;
        internal PtexLruItem _activeFilesItem;

        bool trylock()
        {
            return PtexPlatform.AtomicCompareAndSwap(ref _refCount, 0, -1);
        }

        void unlock()
        {
            PtexPlatform.AtomicStore(ref _refCount, 0);
        }

        public PtexCachedReader(bool premultiply, PtexInputHandler inputHandler, PtexErrorHandler errorHandler, PtexReaderCache cache)
            : base(premultiply, inputHandler, errorHandler)
        {
            _cache = cache;
            _refCount = 1;
            _memUsedAccountedFor = 0;
            _opensAccountedFor = 0;
            _blockReadsAccountedFor = 0;
            _openFilesItem = new PtexLruItem(this);
            _activeFilesItem = new PtexLruItem(this);
        }

        public void @ref()
        {
            while (true)
            {
                int oldCount = System.Threading.Volatile.Read(ref _refCount);
                if (oldCount >= 0 && PtexPlatform.AtomicCompareAndSwap(ref _refCount, oldCount, oldCount + 1))
                    return;
            }
        }

        public int unref()
        {
            return PtexPlatform.AtomicDecrement(ref _refCount);
        }

        public override void release()
        {
            if (0 == unref())
            {
                _cache.logRecentlyUsed(this);
            }
        }

        public bool tryPrune(out ulong memUsedChange)
        {
            if (trylock())
            {
                prune();
                memUsedChange = getMemUsedChange();
                unlock();
                return true;
            }
            memUsedChange = 0;
            return false;
        }

        public bool tryPurge(out ulong memUsedChange)
        {
            if (trylock())
            {
                purge();
                memUsedChange = getMemUsedChange();
                unlock();
                return true;
            }
            setPendingPurge();
            memUsedChange = 0;
            return false;
        }

        public ulong getMemUsedChange()
        {
            ulong memUsedTmp = _memUsed;
            ulong result = unchecked(memUsedTmp - _memUsedAccountedFor);
            _memUsedAccountedFor = memUsedTmp;
            return result;
        }

        public ulong getOpensChange()
        {
            ulong opensTmp = _opens;
            ulong result = unchecked(opensTmp - _opensAccountedFor);
            _opensAccountedFor = opensTmp;
            return result;
        }

        public ulong getBlockReadsChange()
        {
            ulong blockReadsTmp = _blockReads;
            ulong result = unchecked(blockReadsTmp - _blockReadsAccountedFor);
            _blockReadsAccountedFor = blockReadsTmp;
            return result;
        }
    }

    // Cache for reading Ptex texture files
    public unsafe class PtexReaderCache : PtexCache
    {
        public PtexReaderCache(int maxFiles, ulong maxMem, bool premultiply, PtexInputHandler inputHandler, PtexErrorHandler errorHandler)
        {
            _maxFiles = (ulong)maxFiles;
            _maxMem = maxMem;
            _io = inputHandler;
            _err = errorHandler;
            _premultiply = premultiply;
            _memUsed = 512;  // sizeof(*this)
            _filesOpen = 0;
            _mruLists = new MruList[2] { new MruList(), new MruList() };
            _mruList = _mruLists[0];
            _prevMruList = _mruLists[1];
            _peakMemUsed = 0;
            _peakFilesOpen = 0;
            _fileOpens = 0;
            _blockReads = 0;
            _openFiles = new PtexLruList<PtexCachedReader>(r => r._openFilesItem);
            _activeFiles = new PtexLruList<PtexCachedReader>(r => r._activeFilesItem);
        }

        public override void release() { }  // (garbage collected)

        public override void setSearchPath(string path)
        {
            // record path
            _searchpath = path != null ? path : "";

            // split into dirs
            _searchdirs.Clear();
            if (path != null)
            {
                int cp = 0;
                while (true)
                {
                    int delim = path.IndexOf(':', cp);
                    if (delim < 0)
                    {
                        if (cp < path.Length) _searchdirs.Add(path.Substring(cp));
                        break;
                    }
                    int len = delim - cp;
                    if (len != 0) _searchdirs.Add(path.Substring(cp, len));
                    cp = delim + 1;
                }
            }
        }

        public override string getSearchPath()
        {
            return _searchpath;
        }

        bool findFile(ref string filename, ref string error)
        {
            bool isAbsolute = (filename.Length > 0 && filename[0] == '/');
            if (isAbsolute || _searchdirs.Count == 0) return true;  // no need to search

            // file is relative, search in searchpath
            for (int i = 0, size = _searchdirs.Count; i < size; i++)
            {
                string buffer = _searchdirs[i] + "/" + filename;
                if (File.Exists(buffer) || Directory.Exists(buffer))
                {
                    filename = buffer;
                    return true;
                }
            }
            // not found
            error = "Can't find ptex file: " + filename;
            return false;
        }

        public override PtexTexture get(string filename, ref string error)
        {
            // lookup reader in map
            StringKey key = new StringKey(filename);
            PtexCachedReader reader = _files.get(key);
            bool isNew = false;

            if (reader != null)
            {
                if (!reader.ok()) return null;
                if (reader.pendingPurge())
                {
                    // a previous purge attempt was made and file was busy.  Try again now.
                    purge(reader);
                }
                reader.@ref();
            }
            else
            {
                reader = new PtexCachedReader(_premultiply, _io, _err, this);
                isNew = true;
            }

            bool needOpen = reader.needToOpen();
            if (needOpen)
            {
                string pathToOpen = filename;
                // search for the file (unless we have an I/O handler)
                if (_io != null || findFile(ref pathToOpen, ref error))
                {
                    reader.open(pathToOpen, ref error);
                }
                else
                {
                    // flag reader as invalid so we don't try to open it again on next lookup
                    reader.invalidate();
                }
            }

            if (isNew)
            {
                ulong newMemUsed = 0;
                PtexCachedReader newreader = reader;
                reader = _files.tryInsert(key, reader, ref newMemUsed);
                adjustMemUsed(newMemUsed);
                if (reader != newreader)
                {
                    // another thread got here first
                    reader.@ref();
                    newreader.Dispose();
                }
            }

            if (!reader.ok())
            {
                reader.unref();
                return null;
            }

            if (needOpen)
            {
                reader.logOpen();
            }

            return reader;
        }

        public override void purge(PtexTexture texture)
        {
            PtexCachedReader reader = (PtexCachedReader)texture;
            reader.unref();
            purge(reader);
            reader.@ref();
        }

        public override void purge(string filename)
        {
            StringKey key = new StringKey(filename);
            PtexCachedReader reader = _files.get(key);
            if (reader != null) purge(reader);
        }

        public void purge(PtexCachedReader reader)
        {
            ulong memUsedChange;
            if (reader.tryPurge(out memUsedChange))
            {
                adjustMemUsed(memUsedChange);
            }
        }

        public override void purgeAll()
        {
            ulong memUsedChangeTotal = 0;
            _files.foreach_(reader =>
            {
                ulong memUsedChange;
                if (reader.tryPurge(out memUsedChange))
                {
                    memUsedChangeTotal += memUsedChange;
                }
            });
            adjustMemUsed(memUsedChangeTotal);
        }

        public override void getStats(out Stats stats)
        {
            stats = new Stats();
            stats.memUsed = _memUsed;
            stats.peakMemUsed = _peakMemUsed;
            stats.filesOpen = _filesOpen;
            stats.peakFilesOpen = _peakFilesOpen;
            stats.filesAccessed = _files.size();
            stats.fileReopens = _fileOpens < stats.filesAccessed ? 0 : _fileOpens - stats.filesAccessed;
            stats.blockReads = _blockReads;
        }

        public void adjustMemUsed(ulong amount)
        {
            if (amount != 0)
            {
                ulong memUsed = PtexPlatform.AtomicAdd(ref _memUsed, amount);
                _peakMemUsed = Math.Max(_peakMemUsed, memUsed);
            }
        }

        public void adjustFilesOpen(ulong amount)
        {
            if (amount != 0)
            {
                ulong filesOpen = PtexPlatform.AtomicAdd(ref _filesOpen, amount);
                _peakFilesOpen = Math.Max(_peakFilesOpen, filesOpen);
            }
        }

        public void logRecentlyUsed(PtexCachedReader reader)
        {
            while (true)
            {
                MruList mruList = _mruList;
                int slot = PtexPlatform.AtomicIncrement(ref mruList.next) - 1;
                if (slot < numMruFiles)
                {
                    System.Threading.Volatile.Write(ref mruList.files[slot], reader);
                    return;
                }
                // no mru slot available, process mru list and try again
                do processMru();
                while (System.Threading.Volatile.Read(ref _mruList).next >= numMruFiles);
            }
        }

        void processMru()
        {
            // use a non-blocking lock so we can proceed as soon as space has been freed in the mru list
            // (which happens almost immediately in the processMru thread that has the lock)
            if (!_mruLock.trylock()) return;
            if (_mruList.next < numMruFiles)
            {
                _mruLock.unlock();
                return;
            }

            // switch mru buffers and reset slot counter so other threads can proceed immediately
            MruList mruList = _mruList;
            PtexPlatform.AtomicStore(ref _mruList, _prevMruList);
            _prevMruList = mruList;

            // extract relevant stats and add to open/active list
            ulong memUsedChange = 0, filesOpenChange = 0;
            for (int i = 0; i < numMruFiles; ++i)
            {
                PtexCachedReader reader;
                do { reader = System.Threading.Volatile.Read(ref mruList.files[i]); } while (reader == null);  // loop on (unlikely) race condition
                mruList.files[i] = null;
                memUsedChange += reader.getMemUsedChange();
                ulong opens = reader.getOpensChange();
                ulong blockReads = reader.getBlockReadsChange();
                filesOpenChange += opens;
                if (opens != 0 || blockReads != 0)
                {
                    _fileOpens += opens;
                    _blockReads += blockReads;
                    _openFiles.push(reader);
                }
                if (_maxMem != 0)
                {
                    _activeFiles.push(reader);
                }
            }
            PtexPlatform.AtomicStore(ref mruList.next, 0);
            adjustMemUsed(memUsedChange);
            adjustFilesOpen(filesOpenChange);

            bool shouldPruneFiles = _filesOpen > _maxFiles;
            bool shouldPruneData = _maxMem != 0 && _memUsed > _maxMem;

            if (shouldPruneFiles)
            {
                pruneFiles();
            }
            if (shouldPruneData)
            {
                pruneData();
            }
            _mruLock.unlock();
        }

        void pruneFiles()
        {
            ulong numToClose = _filesOpen - _maxFiles;
            if (numToClose > 0)
            {
                while (numToClose != 0)
                {
                    PtexCachedReader reader = _openFiles.pop();
                    if (reader == null) { _filesOpen = 0; break; }
                    if (reader.tryClose())
                    {
                        --numToClose;
                        --_filesOpen;
                    }
                }
            }
        }

        void pruneData()
        {
            ulong memUsedChangeTotal = 0;
            ulong memUsed = _memUsed;
            while (unchecked(memUsed + memUsedChangeTotal) > _maxMem)
            {
                PtexCachedReader reader = _activeFiles.pop();
                if (reader == null) break;
                ulong memUsedChange;
                if (reader.tryPrune(out memUsedChange))
                {
                    // Note: after clearing, memUsedChange is negative
                    unchecked { memUsedChangeTotal += memUsedChange; }
                }
            }
            adjustMemUsed(memUsedChangeTotal);
        }

        ulong _maxFiles;
        ulong _maxMem;
        PtexInputHandler _io;
        PtexErrorHandler _err;
        string _searchpath = "";
        List<string> _searchdirs = new List<string>();
        PtexHashMap<StringKey, PtexCachedReader> _files = new PtexHashMap<StringKey, PtexCachedReader>();
        bool _premultiply;
        ulong _memUsed;
        ulong _filesOpen;
        Mutex _mruLock = new Mutex();

        const int numMruFiles = 50;
        sealed class MruList
        {
            public int next;
            public PtexCachedReader[] files = new PtexCachedReader[numMruFiles];
        }
        MruList[] _mruLists;
        MruList _mruList;
        MruList _prevMruList;

        PtexLruList<PtexCachedReader> _openFiles;
        PtexLruList<PtexCachedReader> _activeFiles;

        ulong _peakMemUsed;
        ulong _peakFilesOpen;
        ulong _fileOpens;
        ulong _blockReads;
    }

    public abstract partial class PtexCache
    {
        // Create a cache with the specified limits. maxFiles <= 0 selects the default (100);
        // maxMem == 0 means unlimited memory.
        public static PtexCache create(int maxFiles, ulong maxMem, bool premultiply,
                                       PtexInputHandler inputHandler,
                                       PtexErrorHandler errorHandler)
        {
            // set default files to 100
            if (maxFiles <= 0) maxFiles = 100;

            return new PtexReaderCache(maxFiles, maxMem, premultiply, inputHandler, errorHandler);
        }
    }
}
