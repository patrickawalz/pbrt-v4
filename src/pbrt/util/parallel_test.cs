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
    }
}
