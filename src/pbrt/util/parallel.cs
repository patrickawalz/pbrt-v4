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
        public void notify_all() { Monitor.PulseAll(m.sync); }
        public void wait(unique_lock lk)
        {
            m = lk.m;
            Monitor.Wait(lk.m.sync);
        }
        public void wait(unique_lock lk, Func<bool> pred)
        {
            while (!pred())
                wait(lk);
        }
        private mutex m;
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
    public class ThreadPool
    {
        // ThreadPool Method Definitions
        public ThreadPool(int nThreads)
        {
            for (int i = 0; i < nThreads - 1; ++i)
            {
                Thread t = new Thread(Worker);
                t.IsBackground = true;
                t.Start();
                threads.Add(t);
            }
        }

        public long size() { return threads.Count; }

        private void Worker()
        {
            pbrt.LOG_VERBOSE("Started execution in worker thread");

            std.unique_lock lk = new std.unique_lock(mutex);
            while (!shutdownThreads)
                WorkOrWait(lk, false);

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
            Barrier barrier = new Barrier((int)(threads.Count + 1));

            pbrt.ParallelFor(0, threads.Count + 1, (long i) =>
            {
                func();
                if (barrier.Block())
                    barrier = null;  // delete barrier;
            });
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
            if (threads.Count == 0)
                return;

            {
                std.lock_guard lg = new std.lock_guard(mutex);
                shutdownThreads = true;
                jobListCondition.notify_all();
                lg.Dispose();
            }

            foreach (Thread thread in threads)
                thread.Join();
        }

        public override string ToString()
        {
            string s = pbrt.StringPrintf("[ ThreadPool threads.size(): %d shutdownThreads: %s ",
                                         threads.Count, shutdownThreads);
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
        private List<Thread> threads = new List<Thread>();
        private std.mutex mutex = new std.mutex();
        private bool shutdownThreads = false;
        private bool disabled = false;
        private ParallelJob jobList = null;
        private std.condition_variable jobListCondition = new std.condition_variable();
    }

    // ParallelForLoop1D Definition
    public class ParallelForLoop1D : ParallelJob
    {
        // ParallelForLoop1D Public Methods
        public ParallelForLoop1D(long startIndex, long endIndex, int chunkSize, Action<long, long> func)
        {
            this.func = func;
            this.nextIndex = startIndex;
            this.endIndex = endIndex;
            this.chunkSize = chunkSize;
        }

        public override bool HaveWork() { return nextIndex < endIndex; }

        public override void RunStep(std.unique_lock lk)
        {
            // Determine the range of loop iterations to run in this step
            long indexStart = nextIndex;
            long indexEnd = std.min(indexStart + chunkSize, endIndex);
            nextIndex = indexEnd;

            // Remove job from list if all work has been started
            if (!HaveWork())
                threadPool.RemoveFromJobList(this);

            // Release lock and execute loop iterations in _[indexStart, indexEnd)_
            lk.unlock();
            func(indexStart, indexEnd);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ParallelForLoop1D nextIndex: %d endIndex: %d " +
                                     "chunkSize: %d ]",
                                     nextIndex, endIndex, chunkSize);
        }

        // ParallelForLoop1D Private Members
        private Action<long, long> func;
        private long nextIndex, endIndex;
        private int chunkSize;
    }

    // ParallelForLoop2D Definition
    public class ParallelForLoop2D : ParallelJob
    {
        public ParallelForLoop2D(Bounds2i extent, int chunkSize, Action<Bounds2i> func)
        {
            this.func = func;
            this.extent = extent;
            this.nextStart = extent.pMin;
            this.chunkSize = chunkSize;
        }

        public override bool HaveWork() { return nextStart.y < extent.pMax.y; }

        public override void RunStep(std.unique_lock lk)
        {
            // Compute extent for this step
            Point2i end = nextStart + new Vector2i(chunkSize, chunkSize);
            Bounds2i b = pbrt.Intersect(new Bounds2i(nextStart, end), extent);
            pbrt.CHECK(!b.IsEmpty());

            // Advance to be ready for the next extent.
            nextStart.x += chunkSize;
            if (nextStart.x >= extent.pMax.x)
            {
                nextStart.x = extent.pMin.x;
                nextStart.y += chunkSize;
            }

            if (!HaveWork())
                threadPool.RemoveFromJobList(this);

            lk.unlock();

            // Run the loop iteration
            func(b);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ ParallelForLoop2D extent: %s nextStart: %s " +
                                     "chunkSize: %d ]",
                                     extent, nextStart, chunkSize);
        }

        private Action<Bounds2i> func;
        private readonly Bounds2i extent;
        private Point2i nextStart;
        private int chunkSize;
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
