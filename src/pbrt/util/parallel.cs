// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

// util/parallel.h, util/parallel.cpp
//
// std::mutex / std::unique_lock / std::condition_variable are provided as std.mutex, std.unique_lock
// and std.condition_variable (System.Threading.Monitor underneath).

using System;
using System.Collections.Generic;
using System.Threading;

public static partial class std
{
    public sealed class mutex
    {
        public void @lock() { Monitor.Enter(sync); }
        public bool try_lock() { return Monitor.TryEnter(sync); }
        public void unlock() { Monitor.Exit(sync); }
        internal readonly object sync = new object();
    }

    public sealed class unique_lock
    {
        public unique_lock() { }
        public unique_lock(mutex m)
        {
            this.m = m;
            @lock();
        }
        public void @lock()
        {
            m.@lock();
            owns = true;
        }
        public void unlock()
        {
            m.unlock();
            owns = false;
        }
        public bool owns_lock() { return owns; }
        // (std::unique_lock's move assignment)
        public void Assign(unique_lock o)
        {
            m = o.m;
            owns = o.owns;
            o.owns = false;
        }
        // (the destructor)
        public void Dispose()
        {
            if (owns)
                unlock();
        }
        internal mutex m;
        private bool owns = false;
    }

    public sealed class condition_variable
    {
        public void notify_all()
        {
            lock (sync)
            {
                ++generation;
                Monitor.PulseAll(sync);
            }
        }
        public void wait(unique_lock lk)
        {
            Monitor.Enter(sync);
            long myGeneration = generation;
            // (the cv's monitor is held before releasing the mutex so notifications are not lost)
            lk.unlock();
            while (generation == myGeneration)
                Monitor.Wait(sync);
            Monitor.Exit(sync);
            lk.@lock();
        }
        public void wait(unique_lock lk, Func<bool> pred)
        {
            while (!pred())
                wait(lk);
        }
        private readonly object sync = new object();
        private long generation = 0;
    }

    public sealed class lock_guard
    {
        public lock_guard(mutex m)
        {
            this.m = m;
            m.@lock();
        }
        public void Dispose() { m.unlock(); }
        private mutex m;
    }
}

namespace pbrt
{

    public static partial class pbrt
    {
        // Parallel Function Declarations
        public static void ParallelInit(int nThreads = -1)
        {
            CHECK(ParallelJob.threadPool == null);
            if (nThreads <= 0)
                nThreads = AvailableCores();
            ParallelJob.threadPool = new ThreadPool(nThreads);
        }

        public static void ParallelCleanup()
        {
            ParallelJob.threadPool.Dispose();
            ParallelJob.threadPool = null;
        }

        public static int AvailableCores()
        {
            return std.max<int>(1, Environment.ProcessorCount);
        }

        public static int RunningThreads()
        {
            return ParallelJob.threadPool != null ? (int)(1 + ParallelJob.threadPool.size()) : 1;
        }

        public static bool DoParallelWork()
        {
            CHECK(ParallelJob.threadPool != null);
            // lock should be held when this is called...
            return ParallelJob.threadPool.WorkOrReturn();
        }

        // Parallel Function Definitions
        public static void ParallelFor(long start, long end, Action<long, long> func)
        {
            CHECK(ParallelJob.threadPool != null);
            if (start == end)
                return;
            // Compute chunk size for parallel loop
            long chunkSize = std.max<long>(1, (end - start) / (8 * RunningThreads()));

            // Create and enqueue _ParallelForLoop1D_ for this loop
            ParallelForLoop1D loop = new ParallelForLoop1D(start, end, (int)chunkSize, func);
            std.unique_lock lk = ParallelJob.threadPool.AddToJobList(loop);

            // Help out with parallel loop iterations in the current thread
            while (!loop.Finished())
                ParallelJob.threadPool.WorkOrWait(lk, true);
            lk.Dispose();
        }

        public static void ParallelFor2D(Bounds2i extent, Action<Bounds2i> func)
        {
            CHECK(ParallelJob.threadPool != null);

            if (extent.IsEmpty())
                return;
            if (extent.Area() == 1)
            {
                func(extent);
                return;
            }

            // Want at least 8 tiles per thread, subject to not too big and not too
            // small.
            // TODO: should we do non-square?
            int tileSize = Clamp((int)std.sqrt(extent.Diagonal().x * extent.Diagonal().y /
                                               (8 * RunningThreads())),
                                 1, 32);

            ParallelForLoop2D loop = new ParallelForLoop2D(extent, tileSize, func);
            std.unique_lock lk = ParallelJob.threadPool.AddToJobList(loop);

            // Help out with parallel loop iterations in the current thread
            while (!loop.Finished())
                ParallelJob.threadPool.WorkOrWait(lk, true);
            lk.Dispose();
        }

        // Parallel Inline Functions
        // (ParallelFor with a per-index function; the Action<int> overload keeps int-typed loop bodies working)
        public static void ParallelFor(long start, long end, Action<long> func)
        {
            ParallelFor(start, end, (long s, long e) =>
            {
                for (long i = s; i < e; ++i)
                    func(i);
            });
        }

        public static void ParallelFor(long start, long end, Action<int> func)
        {
            ParallelFor(start, end, (long s, long e) =>
            {
                for (long i = s; i < e; ++i)
                    func((int)i);
            });
        }

        public static void ParallelFor2D(Bounds2i extent, Action<Point2i> func)
        {
            ParallelFor2D(extent, (Bounds2i b) =>
            {
                foreach (Point2i p in b)
                    func(p);
            });
        }

        public static void ForEachThread(Action func)
        {
            if (ParallelJob.threadPool != null)
                ParallelJob.threadPool.ForEachThread(func);
        }

        public static void DisableThreadPool()
        {
            CHECK(ParallelJob.threadPool != null);
            ParallelJob.threadPool.Disable();
        }

        public static void ReenableThreadPool()
        {
            CHECK(ParallelJob.threadPool != null);
            ParallelJob.threadPool.Reenable();
        }

        // Asynchronous Task Launch Function Definitions
        // (the std::bind of func and args is a closure)
        public static AsyncJob<R> RunAsync<R>(Func<R> func)
        {
            // Create _AsyncJob_ for _func_ and _args_
            AsyncJob<R> job = new AsyncJob<R>(func);

            // Enqueue _job_ or run it immediately
            std.unique_lock lk = new std.unique_lock();
            if (RunningThreads() == 1)
                job.DoWork();
            else
                lk = ParallelJob.threadPool.AddToJobList(job);
            lk.Dispose();
            return job;
        }

        public static AsyncJob<R> RunAsync<A0, R>(Func<A0, R> func, A0 a0)
        {
            return RunAsync<R>(() => func(a0));
        }

        public static AsyncJob<R> RunAsync<A0, A1, R>(Func<A0, A1, R> func, A0 a0, A1 a1)
        {
            return RunAsync<R>(() => func(a0, a1));
        }
    }

    // ThreadLocal Definition
    public class ThreadLocal<T>
    {
        // ThreadLocal Public Methods
        public ThreadLocal()
        {
            hashTable = new Entry[4 * pbrt.RunningThreads()];
            create = () => Activator.CreateInstance<T>();
        }
        public ThreadLocal(Func<T> c)
        {
            hashTable = new Entry[4 * pbrt.RunningThreads()];
            create = c;
        }

        public ref T Get()
        {
            int tid = Thread.CurrentThread.ManagedThreadId;
            uint hash = (uint)tid.GetHashCode();
            hash %= (uint)hashTable.Length;
            int step = 1;
            int tries = 0;
            mutex.EnterReadLock();
            while (true)
            {
                pbrt.CHECK_LT(++tries, hashTable.Length);  // full hash table
                if (hashTable[hash] != null && hashTable[hash].tid == tid)
                {
                    // Found it
                    Entry e = hashTable[hash];
                    mutex.ExitReadLock();
                    return ref e.value;
                }
                else if (hashTable[hash] == null)
                {
                    mutex.ExitReadLock();
                    // Get reader-writer lock before calling the callback so that the user
                    // doesn't have to worry about writing a thread-safe callback.
                    mutex.EnterWriteLock();
                    T newItem = create();
                    if (hashTable[hash] != null)
                    {
                        // someone else got there first--keep looking, but now
                        // with a writer lock.
                        while (true)
                        {
                            hash += (uint)step;
                            ++step;
                            if (hash >= hashTable.Length)
                                hash %= (uint)hashTable.Length;
                            if (hashTable[hash] == null)
                                break;
                        }
                    }
                    Entry ne = new Entry();
                    ne.tid = tid;
                    ne.value = newItem;
                    hashTable[hash] = ne;
                    mutex.ExitWriteLock();
                    return ref ne.value;
                }
                hash += (uint)step;
                ++step;
                if (hash >= hashTable.Length)
                    hash %= (uint)hashTable.Length;
            }
        }

        public void ForAll(Action<T> func)
        {
            mutex.EnterWriteLock();
            foreach (Entry entry in hashTable)
            {
                if (entry != null)
                    func(entry.value);
            }
            mutex.ExitWriteLock();
        }

        // ThreadLocal Private Members
        private sealed class Entry
        {
            public int tid;
            public T value;
        }
        private ReaderWriterLockSlim mutex = new ReaderWriterLockSlim();
        private Entry[] hashTable;
        private Func<T> create;
    }

    // AtomicFloat Definition
    public struct AtomicFloat
    {
        // AtomicFloat Public Methods
        public AtomicFloat(float v = 0) { bits = FloatToBits(v); }

        public static implicit operator float(AtomicFloat a) { return BitsToFloat(Volatile.Read(ref a.bits)); }

        public float Get() { return BitsToFloat(Volatile.Read(ref bits)); }

        public float Set(float v)  // operator=
        {
            Volatile.Write(ref bits, FloatToBits(v));
            return v;
        }

        public void Add(float v)
        {
            uint oldBits = Volatile.Read(ref bits), newBits;
            while (true)
            {
                newBits = FloatToBits(BitsToFloat(oldBits) + v);
                uint seen = Interlocked.CompareExchange(ref bits, newBits, oldBits);
                if (seen == oldBits) break;
                oldBits = seen;
            }
        }

        public override string ToString() { return $"{Get()}"; }

        // AtomicFloat Private Members
        private uint bits;
    }

    // AtomicDouble Definition
    public struct AtomicDouble
    {
        // AtomicDouble Public Methods
        public AtomicDouble(double v = 0) { bits = FloatToBits(v); }

        public static implicit operator double(AtomicDouble a) { return BitsToFloat(Volatile.Read(ref a.bits)); }

        public double Get() { return BitsToFloat(Volatile.Read(ref bits)); }

        public double Set(double v)  // operator=
        {
            Volatile.Write(ref bits, FloatToBits(v));
            return v;
        }

        public void Add(double v)
        {
            ulong oldBits = Volatile.Read(ref bits), newBits;
            while (true)
            {
                newBits = FloatToBits(BitsToFloat(oldBits) + v);
                ulong seen = Interlocked.CompareExchange(ref bits, newBits, oldBits);
                if (seen == oldBits) break;
                oldBits = seen;
            }
        }

        public override string ToString() { return $"{Get()}"; }

        // AtomicDouble Private Members
        private ulong bits;
    }

    // Barrier Definition
    public class Barrier
    {
        // Barrier Public Methods
        public Barrier(int n) { numToBlock = n; numToExit = n; }

        // All block. Returns true to only one thread (which should delete the
        // barrier).
        public bool Block()
        {
            std.unique_lock lk = new std.unique_lock(mutex);

            --numToBlock;
            pbrt.CHECK_GE(numToBlock, 0);

            if (numToBlock > 0)
                cv.wait(lk, () => numToBlock == 0);
            else
                cv.notify_all();

            bool r = --numToExit == 0;
            lk.Dispose();
            return r;
        }

        private std.mutex mutex = new std.mutex();
        private std.condition_variable cv = new std.condition_variable();
        private int numToBlock, numToExit;
    }

    // ParallelJob Definition
    public abstract class ParallelJob
    {
        // ParallelJob Public Methods
        // (virtual ~ParallelJob() { DCHECK(removed); } has no C# equivalent)
        public abstract bool HaveWork();
        public abstract void RunStep(std.unique_lock lk);
        public bool Finished() { return !HaveWork() && activeWorkers == 0; }
        public abstract override string ToString();

        // ParallelJob Public Members
        public static ThreadPool threadPool;

        protected string BaseToString()
        {
            return pbrt.StringPrintf("activeWorkers: %d removed: %s", activeWorkers, removed);
        }

        // ParallelJob Private Members
        // (private with friend class ThreadPool in C++)
        internal int activeWorkers = 0;
        internal ParallelJob prev = null, next = null;
        internal bool removed = false;
    }

    // ThreadPool Definition
    //
    // (not in pbrt-v4) The pool of pbrt-v4, with three things of its own, for progressive rendering:
    //
    //     stealing     A loop does not hand its chunks out one at a time under the pool's mutex. Each thread has a range of the
    //                  chunks of the loop, takes them from the front of it, and when it has none takes the back half of the
    //                  range of another (ParallelRangeLoop). The mutex is for the list of jobs and for sleeping.
    //     threads      AddThread() and RemoveThread() while jobs run. A thread that is added has no range, so the first thing
    //                  it does is steal one: nothing has to be told that it came. A thread that is removed is parked at the
    //                  end of the chunk it is in, on a PauseTokenSource of its own, and what is left of its range is stolen by
    //                  the others; AddThread() wakes a parked thread before it starts a new one.
    //     pause        PauseSource pauses every thread at the end of its chunk, the thread of the caller of a loop too, until
    //                  it is resumed. Loops need no code for it.
    public class ThreadPool
    {
        // ThreadPool::WorkerThread Definition
        private sealed class WorkerThread
        {
            public Thread thread;
            public int slot;
            // (paused while the thread is removed from the pool)
            public PauseTokenSource removed = new PauseTokenSource();
            public bool parked;
        }

        // (the number of threads at work beside the calling thread: those that are not parked)
        public long size() { return Volatile.Read(ref activeThreads); }
        // ThreadPool Method Definitions
        public ThreadPool(int nThreads)
        {
            for (int i = 0; i < nThreads - 1; ++i)
                StartThread();
        }

        private void StartThread()
        {
            WorkerThread worker = new WorkerThread();
            worker.slot = Interlocked.Increment(ref slots);
            worker.thread = new Thread(() => Worker(worker));
            worker.thread.IsBackground = true;
            workers.Add(worker);
            Interlocked.Increment(ref activeThreads);
            worker.thread.Start();
        }

        // Adds a thread to the pool, a parked one if there is one; returns the number of threads at work, the caller's among them
        public int AddThread()
        {
            std.lock_guard lg = new std.lock_guard(mutex);
            WorkerThread parked = null;
            foreach (WorkerThread worker in workers)
            {
                if (worker.parked)
                {
                    parked = worker;
                    break;
                }
            }
            if (parked != null)
            {
                parked.parked = false;
                Interlocked.Increment(ref activeThreads);
                parked.removed.Resume();
            }
            else
            {
                StartThread();
            }
            jobListCondition.notify_all();
            int count = 1 + activeThreads;
            lg.Dispose();
            return count;
        }

        // Removes a thread from the pool: it finishes the chunk it is in and is parked. The calling thread of a loop is not one
        // of the pool's and stays. Returns the number of threads at work.
        public int RemoveThread()
        {
            std.lock_guard lg = new std.lock_guard(mutex);
            for (int i = workers.Count - 1; i >= 0; --i)
            {
                if (!workers[i].parked)
                {
                    workers[i].parked = true;
                    Interlocked.Decrement(ref activeThreads);
                    workers[i].removed.Pause();
                    break;
                }
            }
            int count = 1 + activeThreads;
            lg.Dispose();
            return count;
        }

        // What pauses the pool
        public PauseTokenSource PauseSource
        {
            get { return pause; }
        }

        // Whether the calling thread is to leave the loop it is taking chunks from: the pool is paused, or the thread has been
        // removed (and is not inside a chunk, whose loops it still has to see through)
        internal bool ShouldYield()
        {
            if (pause.IsPaused)
                return true;
            WorkerThread worker = currentWorker;
            return worker != null && chunkDepth == 0 && worker.removed.IsPaused;
        }

        // The slot of the calling thread in the ranges of the loops: one for every thread that has ever taken a chunk
        internal int Slot()
        {
            if (threadSlot == 0)
                threadSlot = currentWorker != null ? currentWorker.slot : Interlocked.Increment(ref slots);
            return threadSlot;
        }

        // The slots of the threads that a loop made now can expect: the caller's and those of the threads at work
        internal int[] ExpectedSlots()
        {
            std.lock_guard lg = new std.lock_guard(mutex);
            List<int> expected = new List<int>();
            expected.Add(Slot());
            foreach (WorkerThread worker in workers)
            {
                if (!worker.parked && worker.slot != threadSlot)
                    expected.Add(worker.slot);
            }
            lg.Dispose();
            return expected.ToArray();
        }

        internal int SlotCapacity()
        {
            return Volatile.Read(ref slots) + 1;
        }

        private void Worker(WorkerThread worker)
        {
            pbrt.LOG_VERBOSE("Started execution in worker thread");
            currentWorker = worker;

            std.unique_lock lk = new std.unique_lock(mutex);
            while (!shutdownThreads)
            {
                if (worker.removed.IsPaused)
                {
                    // Parked, until AddThread() or the end of the pool. What the thread has counted is handed in first:
                    // ForEachThread() does not come to a parked thread.
                    lk.unlock();
                    pbrt.ReportThreadStats();
                    worker.removed.Token.WaitWhilePaused();
                    lk.@lock();
                    continue;
                }
                WorkOrWait(lk, false);
            }

            lk.Dispose();
            pbrt.LOG_VERBOSE("Exiting worker thread");
        }

        public std.unique_lock AddToJobList(ParallelJob job)
        {
            std.unique_lock lk = new std.unique_lock(mutex);
            // Add _job_ to head of _jobList_
            if (jobList != null)
                jobList.prev = job;
            job.next = jobList;
            jobList = job;

            jobListCondition.notify_all();
            return lk;
        }

        public void WorkOrWait(std.unique_lock lk, bool isEnqueuingThread)
        {
            pbrt.DCHECK(lk.owns_lock());
            // (not in pbrt-v4) Wait out a pause of the pool
            if (pause.IsPaused)
            {
                lk.unlock();
                pause.Token.WaitWhilePaused();
                lk.@lock();
                return;
            }
            // Return if this is a worker thread and the thread pool is disabled
            if (!isEnqueuingThread && disabled)
            {
                jobListCondition.wait(lk);
                return;
            }

            ParallelJob job = jobList;
            while (job != null && !job.HaveWork())
                job = job.next;
            if (job != null)
            {
                // Execute work for _job_
                job.activeWorkers++;
                job.RunStep(lk);
                // Handle post-job-execution details
                pbrt.DCHECK(!lk.owns_lock());
                lk.@lock();
                job.activeWorkers--;
                if (job.Finished())
                    jobListCondition.notify_all();
            }
            else
                // Wait for new work to arrive or the job to finish
                jobListCondition.wait(lk);
        }

        public void RemoveFromJobList(ParallelJob job)
        {
            pbrt.DCHECK(!job.removed);

            if (job.prev != null)
                job.prev.next = job.next;
            else
            {
                pbrt.DCHECK(jobList == job);
                jobList = job.next;
            }
            if (job.next != null)
                job.next.prev = job.prev;

            job.removed = true;
        }

        public bool WorkOrReturn()
        {
            if (pause.IsPaused)
                return false;
            std.unique_lock lk = new std.unique_lock(mutex);

            ParallelJob job = jobList;
            while (job != null && !job.HaveWork())
                job = job.next;
            if (job == null)
            {
                lk.Dispose();
                return false;
            }

            // Execute work for _job_
            job.activeWorkers++;
            job.RunStep(lk);
            pbrt.DCHECK(!lk.owns_lock());
            lk.@lock();
            job.activeWorkers--;
            if (job.Finished())
                jobListCondition.notify_all();

            lk.Dispose();
            return true;
        }

        public void ForEachThread(Action func)
        {
            // (not in pbrt-v4: the threads at work. A thread that is parked has handed in what it had when it was parked.)
            int count = (int)size() + 1;
            Barrier barrier = new Barrier(count);

            // (a chunk for each thread, and no thread finishes its chunk before every thread has one)
            ParallelForLoop1D loop = new ParallelForLoop1D(0, count, 1, (long start, long end) =>
            {
                func();
                if (barrier.Block())
                    barrier = null;  // delete barrier;
            });
            std.unique_lock lk = AddToJobList(loop);
            while (!loop.Finished())
                WorkOrWait(lk, true);
            lk.Dispose();
        }

        public void Disable()
        {
            pbrt.CHECK(!disabled);
            disabled = true;
            pbrt.CHECK(jobList == null);  // Nothing should be running when Disable() is called.
        }

        public void Reenable()
        {
            pbrt.CHECK(disabled);
            disabled = false;
        }

        // ~ThreadPool()
        public void Dispose()
        {
            if (workers.Count == 0)
                return;

            {
                std.lock_guard lg = new std.lock_guard(mutex);
                shutdownThreads = true;
                // (the parked threads and a paused pool are let go)
                foreach (WorkerThread worker in workers)
                    worker.removed.Resume();
                pause.Resume();
                jobListCondition.notify_all();
                lg.Dispose();
            }

            foreach (WorkerThread worker in workers)
                worker.thread.Join();
        }

        public override string ToString()
        {
            string s = pbrt.StringPrintf("[ ThreadPool threads.size(): %d shutdownThreads: %s ",
                                         workers.Count, shutdownThreads);
            if (mutex.try_lock())
            {
                s += "jobList: [ ";
                ParallelJob job = jobList;
                while (job != null)
                {
                    s += job.ToString() + " ";
                    job = job.next;
                }
                s += "] ";
                mutex.unlock();
            }
            else
                s += "(job list mutex locked) ";
            return s + "]";
        }

        // ThreadPool Private Members
        private List<WorkerThread> workers = new List<WorkerThread>();
        private int activeThreads = 0;
        private int slots = 0;
        private PauseTokenSource pause = new PauseTokenSource();
        [ThreadStatic] private static WorkerThread currentWorker;
        [ThreadStatic] private static int threadSlot;
        // (how many chunks of loops the calling thread is inside)
        [ThreadStatic] internal static int chunkDepth;
        private std.mutex mutex = new std.mutex();
        private bool shutdownThreads = false;
        private bool disabled = false;
        private ParallelJob jobList = null;
        private std.condition_variable jobListCondition = new std.condition_variable();
    }

    // ParallelRangeLoop Definition
    //
    // (not in pbrt-v4) A loop over chunks numbered from zero, shared out by stealing. Each thread has a range [begin, end) of
    // chunks, the two numbers in one 64-bit word. A thread takes the first chunk of its own range; one that has none takes the
    // back half of the largest range there is, runs the first chunk of that and keeps the rest; both are one compare-and-swap.
    // A range only ever names chunks that have not been run, so a word that a thread read and finds unchanged is still good.
    public abstract class ParallelRangeLoop : ParallelJob
    {
        protected ParallelRangeLoop(long chunks)
        {
            pbrt.CHECK_LT(chunks, 1L << 32);
            unstarted = chunks;
            // The chunks are divided among the threads that are at work now; a thread that comes later steals
            int[] expected = threadPool.ExpectedSlots();
            ranges = new ulong[Math.Max(threadPool.SlotCapacity() + 16, 64)];
            long each = chunks / expected.Length, extra = chunks % expected.Length, begin = 0;
            for (int i = 0; i < expected.Length; ++i)
            {
                long count = each + (i < extra ? 1 : 0);
                if (expected[i] < ranges.Length)
                    ranges[expected[i]] = Pack((uint)begin, (uint)(begin + count));
                else
                    // (cannot be: the capacity is beyond every slot there is)
                    pbrt.CHECK(false);
                begin += count;
            }
        }

        public override bool HaveWork() { return Volatile.Read(ref unstarted) > 0; }

        public override void RunStep(std.unique_lock lk)
        {
            // Release lock and run chunks until there are none to take, or the thread is to stop for now
            lk.unlock();
            int slot = threadPool.Slot();
            while (!threadPool.ShouldYield())
            {
                long chunk = Take(slot);
                if (chunk < 0)
                    break;
                Interlocked.Decrement(ref unstarted);
                ++ThreadPool.chunkDepth;
                try
                {
                    RunChunk(chunk);
                }
                finally
                {
                    --ThreadPool.chunkDepth;
                }
            }

            // Remove job from list if all work has been started
            lk.@lock();
            if (!HaveWork() && !removed)
                threadPool.RemoveFromJobList(this);
            lk.unlock();
        }

        protected abstract void RunChunk(long chunk);

        private static ulong Pack(uint begin, uint end)
        {
            return ((ulong)begin << 32) | end;
        }

        // A chunk for the thread of the slot, or -1 if no range has one
        private long Take(int slot)
        {
            bool owner = slot < ranges.Length;
            if (owner)
            {
                // The first chunk of its own range
                while (true)
                {
                    ulong range = Volatile.Read(ref ranges[slot]);
                    uint begin = (uint)(range >> 32), end = (uint)range;
                    if (begin >= end)
                        break;
                    if (Interlocked.CompareExchange(ref ranges[slot], Pack(begin + 1, end), range) == range)
                        return begin;
                }
            }
            while (true)
            {
                // The largest range of another
                int victim = -1;
                uint most = 0;
                for (int i = 0; i < ranges.Length; ++i)
                {
                    ulong r = Volatile.Read(ref ranges[i]);
                    uint b = (uint)(r >> 32), e = (uint)r;
                    if (e > b && e - b > most)
                    {
                        most = e - b;
                        victim = i;
                    }
                }
                if (victim < 0)
                    return -1;
                ulong range = Volatile.Read(ref ranges[victim]);
                uint begin = (uint)(range >> 32), end = (uint)range;
                if (begin >= end)
                    continue;
                // Its back half (one chunk, for a thread that has no slot to keep the rest in)
                uint take = owner ? (end - begin + 1) / 2 : 1;
                if (Interlocked.CompareExchange(ref ranges[victim], Pack(begin, end - take), range) != range)
                    continue;
                if (take > 1)
                    Volatile.Write(ref ranges[slot], Pack(end - take + 1, end));
                return end - take;
            }
        }

        // ParallelRangeLoop Private Members
        private readonly ulong[] ranges;
        private long unstarted;
    }

    // ParallelForLoop1D Definition
    public class ParallelForLoop1D : ParallelRangeLoop
    {
        // ParallelForLoop1D Public Methods
        public ParallelForLoop1D(long startIndex, long endIndex, int chunkSize, Action<long, long> func)
            : base((endIndex - startIndex + chunkSize - 1) / chunkSize)
        {
            this.func = func;
            this.startIndex = startIndex;
            this.endIndex = endIndex;
            this.chunkSize = chunkSize;
        }

        protected override void RunChunk(long chunk)
        {
            // Determine the range of loop iterations to run in this step
            long indexStart = startIndex + chunk * chunkSize;
            long indexEnd = std.min(indexStart + chunkSize, endIndex);
            func(indexStart, indexEnd);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ParallelForLoop1D startIndex: %d endIndex: %d " +
                                     "chunkSize: %d ]",
                                     startIndex, endIndex, chunkSize);
        }

        // ParallelForLoop1D Private Members
        private Action<long, long> func;
        private long startIndex, endIndex;
        private int chunkSize;
    }

    // ParallelForLoop2D Definition
    public class ParallelForLoop2D : ParallelRangeLoop
    {
        public ParallelForLoop2D(Bounds2i extent, int chunkSize, Action<Bounds2i> func)
            : base((long)((extent.Diagonal().x + chunkSize - 1) / chunkSize) * ((extent.Diagonal().y + chunkSize - 1) / chunkSize))
        {
            this.func = func;
            this.extent = extent;
            this.chunkSize = chunkSize;
            this.across = (extent.Diagonal().x + chunkSize - 1) / chunkSize;
        }

        protected override void RunChunk(long chunk)
        {
            // Compute extent for this step
            Point2i start = extent.pMin + new Vector2i((int)(chunk % across) * chunkSize, (int)(chunk / across) * chunkSize);
            Point2i end = start + new Vector2i(chunkSize, chunkSize);
            Bounds2i b = pbrt.Intersect(new Bounds2i(start, end), extent);
            pbrt.CHECK(!b.IsEmpty());

            // Run the loop iteration
            func(b);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ParallelForLoop2D extent: %s chunkSize: %d ]", extent, chunkSize);
        }

        private Action<Bounds2i> func;
        private readonly Bounds2i extent;
        private int chunkSize;
        private int across;
    }

    // AsyncJob Definition
    public class AsyncJob<T> : ParallelJob
    {
        // AsyncJob Public Methods
        public AsyncJob(Func<T> w) { func = w; }

        public override bool HaveWork() { return !started; }

        public override void RunStep(std.unique_lock lk)
        {
            threadPool.RemoveFromJobList(this);
            started = true;

            lk.unlock();

            // Execute asynchronous work and notify waiting threads of its completion
            T r = func();
            std.unique_lock ul = new std.unique_lock(mutex);
            result = new pstd.optional<T>(r);
            cv.notify_all();
            ul.Dispose();
        }

        public bool IsReady()
        {
            std.lock_guard lg = new std.lock_guard(mutex);
            bool r = result.has_value();
            lg.Dispose();
            return r;
        }

        public T GetResult()
        {
            Wait();
            std.lock_guard lg = new std.lock_guard(mutex);
            T r = result.value();
            lg.Dispose();
            return r;
        }

        public pstd.optional<T> TryGetResult(std.mutex extMutex)
        {
            {
                std.lock_guard lg = new std.lock_guard(mutex);
                if (result.has_value())
                {
                    pstd.optional<T> r = result;
                    lg.Dispose();
                    return r;
                }
                lg.Dispose();
            }

            extMutex.unlock();
            pbrt.DoParallelWork();
            extMutex.@lock();
            return new pstd.optional<T>();
        }

        public void Wait()
        {
            while (!IsReady() && pbrt.DoParallelWork())
                ;
            std.unique_lock lk = new std.unique_lock(mutex);
            if (!result.has_value())
                cv.wait(lk, () => result.has_value());
            lk.Dispose();
        }

        public void DoWork()
        {
            T r = func();
            std.unique_lock l = new std.unique_lock(mutex);
            pbrt.CHECK(!result.has_value());
            result = new pstd.optional<T>(r);
            cv.notify_all();
            l.Dispose();
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ AsyncJob started: %s ]", started);
        }

        // AsyncJob Private Members
        private Func<T> func;
        private bool started = false;
        private pstd.optional<T> result;
        private std.mutex mutex = new std.mutex();
        private std.condition_variable cv = new std.condition_variable();
    }
}
