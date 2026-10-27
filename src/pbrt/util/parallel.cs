// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/parallel.h, util/parallel.cpp
//
// pbrt's hand-written ThreadPool / ParallelJob machinery is replaced by System.Threading.Tasks;
// the public API (ParallelFor, ParallelFor2D, ThreadLocal, AtomicFloat, AsyncJob, RunAsync, ...)
// and pbrt's 2D tiling heuristic are kept.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace pbrt
{

    public static partial class pbrt
    {
        // Parallel Declarations
        private static int g_nThreads = 0;  // 0: ParallelInit not called

        public static void ParallelInit(int nThreads = -1)
        {
            CHECK(g_nThreads == 0);
            if (nThreads <= 0)
                nThreads = AvailableCores();
            g_nThreads = nThreads;
        }

        public static void ParallelCleanup()
        {
            g_nThreads = 0;
        }

        public static int AvailableCores()
        {
            return std.max<int>(1, Environment.ProcessorCount);
        }

        public static int RunningThreads()
        {
            return g_nThreads > 0 ? g_nThreads : 1;
        }

        // Parallel for-loops
        public static void ParallelFor(long start, long end, Action<long, long> func)
        {
            if (start >= end)
                return;
            long nItems = end - start;
            // Compute chunk size for parallel loop
            long chunkSize = Math.Max(1L, nItems / (8 * RunningThreads()));
            long nChunks = (nItems + chunkSize - 1) / chunkSize;
            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = RunningThreads();
            // (localFinally runs on each worker thread when it is done: pbrt's worker threads
            // call ReportThreadStats() when they exit)
            Parallel.For<int>(0L, nChunks, opts, () => 0, (long chunk, ParallelLoopState st, int local) =>
            {
                long idx0 = start + chunk * chunkSize;
                long idx1 = std.min(idx0 + chunkSize, end);
                func(idx0, idx1);
                return 0;
            }, (int local) => ReportThreadStats());
        }

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

        public static void ParallelFor2D(Bounds2i extent, Action<Bounds2i> func)
        {
            if (extent.IsEmpty())
                return;
            if (extent.Area() == 1)
            {
                func(extent);
                return;
            }

            // Compute tile size for 2D parallel loop
            int tileSize = Clamp((int)std.sqrt((Float)(extent.Diagonal().x * extent.Diagonal().y) /
                                               (8 * RunningThreads())),
                                 1, 32);
            Point2i nTiles = new Point2i((extent.pMax.x - extent.pMin.x + tileSize - 1) / tileSize,
                                         (extent.pMax.y - extent.pMin.y + tileSize - 1) / tileSize);
            ParallelOptions opts = new ParallelOptions();
            opts.MaxDegreeOfParallelism = RunningThreads();
            Parallel.For<int>(0, nTiles.x * nTiles.y, opts, () => 0, (int index, ParallelLoopState st, int local) =>
            {
                int x0 = extent.pMin.x + (index % nTiles.x) * tileSize;
                int x1 = std.min(x0 + tileSize, extent.pMax.x);
                int y0 = extent.pMin.y + (index / nTiles.x) * tileSize;
                int y1 = std.min(y0 + tileSize, extent.pMax.y);
                func(new Bounds2i(new Point2i(x0, y0), new Point2i(x1, y1)));
                return 0;
            }, (int local) => ReportThreadStats());
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
            int n = RunningThreads();
            using (System.Threading.Barrier barrier = new System.Threading.Barrier(n))
            {
                Task[] tasks = new Task[n];
                for (int i = 0; i < n; ++i)
                    tasks[i] = Task.Factory.StartNew(() => { barrier.SignalAndWait(); func(); },
                                                     TaskCreationOptions.LongRunning);
                Task.WaitAll(tasks);
            }
        }

        public static void DisableThreadPool() { }
        public static void ReenableThreadPool() { }

        public static bool DoParallelWork() { return false; }

        // RunAsync(func, args...) -> AsyncJob<R>*
        public static AsyncJob<R> RunAsync<R>(Func<R> func)
        {
            AsyncJob<R> job = new AsyncJob<R>(func);
            if (RunningThreads() == 1)
                job.DoWork();
            else
                job.Start();
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
        public ThreadLocal() : this(() => Activator.CreateInstance<T>()) { }

        public ThreadLocal(Func<T> c)
        {
            holder = new System.Threading.ThreadLocal<Holder>(() => new Holder { value = c() }, trackAllValues: true);
        }

        public ref T Get()
        {
            // Return the thread-local value (stored in a box so ref semantics work for structs)
            return ref holder.Value.value;
        }

        public void ForAll(Action<T> func)
        {
            foreach (Holder h in holder.Values)
                func(h.value);
        }

        private sealed class Holder { public T value; }
        private readonly System.Threading.ThreadLocal<Holder> holder;
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

        // Returns true to exactly one thread (the last one to leave)
        public bool Block()
        {
            lock (sync)
            {
                CHECK_GT(numToBlock, 0);
                --numToBlock;
                if (numToBlock > 0)
                {
                    while (numToBlock > 0)
                        Monitor.Wait(sync);
                }
                else
                    Monitor.PulseAll(sync);

                // All threads are ready; the last to leave returns true
                return --numToExit == 0;
            }
        }

        private readonly object sync = new object();
        private int numToBlock, numToExit;
    }

    // AsyncJob Definition
    public class AsyncJob<T>
    {
        // AsyncJob Public Methods
        public AsyncJob(Func<T> w) { func = w; }

        internal void Start()
        {
            task = Task.Run(() => { DoWorkInternal(); });
        }

        public bool HaveWork() { return !started; }

        public bool IsReady()
        {
            lock (mutex) { return resultSet; }
        }

        public T GetResult()
        {
            Wait();
            lock (mutex) { return result; }
        }

        public pstd.optional<T> TryGetResult(object extMutex)
        {
            lock (mutex)
            {
                if (resultSet)
                    return new pstd.optional<T>(result);
            }
            return default;
        }

        public void Wait()
        {
            lock (mutex)
            {
                while (!resultSet)
                    Monitor.Wait(mutex);
            }
        }

        public void DoWork()
        {
            T r = func();
            lock (mutex)
            {
                CHECK(!resultSet);
                result = r;
                resultSet = true;
                Monitor.PulseAll(mutex);
            }
        }

        private void DoWorkInternal()
        {
            started = true;
            DoWork();
        }

        public override string ToString() { return $"[ AsyncJob started: {(started ? "true" : "false")} ]"; }

        // AsyncJob Private Members
        private Func<T> func;
        private volatile bool started = false;
        private T result;
        private bool resultSet = false;
        private readonly object mutex = new object();
        private Task task;
    }

}  // namespace pbrt
