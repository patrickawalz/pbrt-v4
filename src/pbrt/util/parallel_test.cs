// parallel_test.cpp

using System;
using System.Collections.Generic;
using System.Threading;
using testing;

namespace pbrt
{
    public static unsafe class ParallelTests
    {
        [TEST("Parallel", "Basics")]
        public static void Parallel_Basics()
        {
            int counter = 0;
            ParallelFor(0, 1000, (long i) => { Interlocked.Increment(ref counter); });
            EXPECT_EQ(1000, counter);

            counter = 0;
            ParallelFor(10, 1010, (long start, long end) =>
            {
                EXPECT_GT(end, start);
                EXPECT_TRUE(start >= 10 && start < 1010);
                EXPECT_TRUE(end > 10 && end <= 1010);
                for (long i = start; i < end; ++i)
                    Interlocked.Increment(ref counter);
            });
            EXPECT_EQ(1000, counter);

            counter = 0;
            ParallelFor2D(new Bounds2i(new Point2i(0, 0), new Point2i(15, 14)), (Point2i p) => { Interlocked.Increment(ref counter); });
            EXPECT_EQ(15 * 14, counter);
        }

        [TEST("Parallel", "DoNothing")]
        public static void Parallel_DoNothing()
        {
            int counter = 0;
            ParallelFor(0, 0, (long i) => { Interlocked.Increment(ref counter); });
            EXPECT_EQ(0, counter);

            counter = 0;
            ParallelFor2D(new Bounds2i(new Point2i(0, 0), new Point2i(0, 0)), (Bounds2i b) => { Interlocked.Increment(ref counter); });
            EXPECT_EQ(0, counter);
        }

        [TEST("Parallel", "ForEachThread")]
        public static void Parallel_ForEachThread()
        {
            int count = RunningThreads();
            ForEachThread(() => { Interlocked.Decrement(ref count); });
            EXPECT_EQ(0, count);
        }

        [TEST("ThreadLocal", "Consistency")]
        public static void ThreadLocal_Consistency()
        {
            ThreadLocal<int> tids = new ThreadLocal<int>(() => Thread.CurrentThread.ManagedThreadId);

            long dummy = 0;
            Action<long> busywork = (long index) =>
            {
                // Do some busy work to burn some time
                // Use the index to do a varying amount of computation
                long result = 0;
                for (int i = 0; i <= index; ++i)
                    for (int j = 0; j <= index; ++j)
                        result += i * j;
                // Store result in atomic to prevent optimization
                Interlocked.Add(ref dummy, result);
            };

            ParallelFor(0, 1000, (long index) =>
            {
                EXPECT_EQ(Thread.CurrentThread.ManagedThreadId, tids.Get());
                busywork(index);
            });

            List<AsyncJob<int>> jobs = new List<AsyncJob<int>>();
            for (int i = 0; i < 100; ++i)
            {
                int ii = i;
                jobs.Add(RunAsync<int>(() =>
                {
                    EXPECT_EQ(Thread.CurrentThread.ManagedThreadId, tids.Get());
                    busywork(ii);
                    return ii;
                }));
            }
            for (int i = 0; i < 100; ++i)
                jobs[i].GetResult();

            ParallelFor(0, 1000, (long index) =>
            {
                EXPECT_EQ(Thread.CurrentThread.ManagedThreadId, tids.Get());
                busywork(index);
            });
        }

        // (not in pbrt-v4) Every index of a loop is visited once, whatever is done to the pool while it runs: threads added,
        // threads removed, the pool paused and resumed
        [TEST("Parallel", "ThreadsComeAndGo")]
        public static void Parallel_ThreadsComeAndGo()
        {
            ThreadPool pool = ParallelJob.threadPool;
            int before = RunningThreads();
            int count = 20000;
            int[] visits = new int[count];
            bool done = false;
            Thread meddler = new Thread(() =>
            {
                Random random = new Random(7);
                int added = 0;
                while (!Volatile.Read(ref done))
                {
                    switch (random.Next(4))
                    {
                        case 0:
                            if (added < 6)
                            {
                                pool.AddThread();
                                ++added;
                            }
                            break;
                        case 1:
                            if (added > -(before - 1))
                            {
                                pool.RemoveThread();
                                --added;
                            }
                            break;
                        case 2:
                            pool.PauseSource.Pause();
                            Thread.Sleep(2);
                            pool.PauseSource.Resume();
                            break;
                        default:
                            Thread.Sleep(1);
                            break;
                    }
                }
                // As it was
                while (added > 0)
                {
                    pool.RemoveThread();
                    --added;
                }
                while (added < 0)
                {
                    pool.AddThread();
                    ++added;
                }
            });
            meddler.Start();
            for (int round = 0; round < 20; ++round)
            {
                ParallelFor(0, count, (long index) =>
                {
                    Interlocked.Increment(ref visits[index]);
                    if (index % 500 == 0)
                    {
                        Thread.Sleep(1);
                    }
                });
                ParallelFor2D(new Bounds2i(new Point2i(3, 5), new Point2i(103, 205)), (Point2i p) =>
                {
                    Interlocked.Increment(ref visits[(p.y - 5) * 100 + (p.x - 3)]);
                });
            }
            Volatile.Write(ref done, true);
            meddler.Join();
            for (int i = 0; i < count; ++i)
            {
                CHECK_EQ(visits[i], 40);
            }
            EXPECT_EQ(RunningThreads(), before);
        }

        // (not in pbrt-v4) A paused pool runs nothing, and a thread that is removed leaves the chunks of its range to the others
        [TEST("Parallel", "PauseAndRemove")]
        public static void Parallel_PauseAndRemove()
        {
            ThreadPool pool = ParallelJob.threadPool;
            int before = RunningThreads();
            int ran = 0;
            pool.PauseSource.Pause();
            Thread loop = new Thread(() =>
            {
                ParallelFor(0, 1000, (long index) => { Interlocked.Increment(ref ran); });
            });
            loop.Start();
            Thread.Sleep(150);
            EXPECT_EQ(Volatile.Read(ref ran), 0);
            EXPECT_TRUE(loop.IsAlive);
            pool.PauseSource.Resume();
            EXPECT_TRUE(loop.Join(20000));
            EXPECT_EQ(ran, 1000);

            // With every thread of the pool removed the calling thread does the whole loop; with threads added it has help
            int removed = 0;
            while (RunningThreads() > 1)
            {
                pool.RemoveThread();
                ++removed;
            }
            EXPECT_EQ(RunningThreads(), 1);
            HashSet<int> threads = new HashSet<int>();
            ParallelFor(0, 2000, (long index) =>
            {
                lock (threads)
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                }
            });
            EXPECT_EQ(threads.Count, 1);
            int with = pool.AddThread();
            EXPECT_EQ(with, 2);
            EXPECT_EQ(pool.AddThread(), 3);
            threads.Clear();
            ParallelFor(0, 300, (long index) =>
            {
                Thread.Sleep(1);
                lock (threads)
                {
                    threads.Add(Environment.CurrentManagedThreadId);
                }
            });
            EXPECT_GE(threads.Count, 2);
            // ForEachThread is every thread at work, and not the parked ones
            int each = 0;
            pool.RemoveThread();
            ForEachThread(() => { Interlocked.Increment(ref each); });
            EXPECT_EQ(each, 2);
            // As it was
            while (RunningThreads() > before)
            {
                pool.RemoveThread();
            }
            while (RunningThreads() < before)
            {
                pool.AddThread();
            }
            EXPECT_EQ(RunningThreads(), before);
        }
    }
}
