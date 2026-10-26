// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// util/progressreporter.h, util/progressreporter.cpp

using System;
using System.Diagnostics;
using System.Threading;

namespace pbrt
{

    // Timer Definition
    public class Timer
    {
        public Timer() { start = Stopwatch.GetTimestamp(); }

        public double ElapsedSeconds()
        {
            long now = Stopwatch.GetTimestamp();
            long elapseduS = (long)((now - start) * 1000000.0 / Stopwatch.Frequency);
            return elapseduS / 1000000.0;
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ Timer start(ns): %d ]", (long)(start * (1e9 / Stopwatch.Frequency)));
        }

        private long start;
    }

    // ProgressReporter Definition
    public class ProgressReporter
    {
        // ProgressReporter Public Methods
        public ProgressReporter() { quiet = true; }

        public ProgressReporter(long totalWork, string title, bool quiet, bool gpu = false)
        {
            this.totalWork = std.max<long>(1, totalWork);
            this.title = title;
            this.quiet = quiet;
            workDone = 0;
            exitThread = 0;
            Globals.CHECK(gpu == false);

            // Launch thread to periodically update progress bar
            if (!quiet)
            {
                updateThread = new Thread(() => { printBar(); });
                updateThread.IsBackground = true;
                updateThread.Start();
            }
        }

        ~ProgressReporter()
        {
            Done();
        }

        public void Update(long num = 1)
        {
            if (num == 0 || quiet)
                return;
            Interlocked.Add(ref workDone, num);
        }

        public double ElapsedSeconds()
        {
            return finishTime.HasValue ? finishTime.Value : timer.ElapsedSeconds();
        }

        public void Done()
        {
            if (!quiet)
            {
                // Only let one thread shut things down.
                finishTime = (float)ElapsedSeconds();
                if (Interlocked.CompareExchange(ref exitThread, 1, 0) == 0)
                {
                    Interlocked.Exchange(ref workDone, totalWork);
                    if (updateThread != null && updateThread.IsAlive)
                        updateThread.Join();
                    Console.Out.Write("\n");
                    Console.Out.Flush();
                }
            }
        }

        public override string ToString()
        {
            return Globals.StringPrintf("[ ProgressReporter totalWork: %d title: %s " +
                                        "timer: %s workDone: %d exitThread: %s",
                                        totalWork, title, timer, Interlocked.Read(ref workDone),
                                        Volatile.Read(ref exitThread) != 0);
        }

        // ProgressReporter Private Methods
        private void printBar()
        {
            int barLength = TerminalWidth() - 28;
            int totalPlusses = std.max<int>(2, barLength - title.Length);
            int plussesPrinted = 0;

            // Initialize progress string
            char[] buf = new char[title.Length + totalPlusses + 64];
            string head = "\r" + title + ": [";
            int curSpace = head.Length;
            for (int i = 0; i < head.Length; ++i)
                buf[i] = head[i];
            int s = curSpace;
            for (int i = 0; i < totalPlusses; ++i)
                buf[s++] = ' ';
            buf[s++] = ']';
            buf[s++] = ' ';
            int bufLen = s;
            Console.Out.Write(new string(buf, 0, bufLen));
            Console.Out.Flush();

            int sleepDuration = 250;  // milliseconds

            int iterCount = 0;
            bool reallyExit = false;  // make sure we do one more go-round to get the final report
            while (!reallyExit)
            {
                if (Volatile.Read(ref exitThread) != 0)
                    reallyExit = true;
                else
                    Thread.Sleep(sleepDuration);

                // Periodically increase sleepDuration to reduce overhead of
                // updates.
                ++iterCount;
                if (iterCount == 10)
                    // Up to 0.5s after ~2.5s elapsed
                    sleepDuration *= 2;
                else if (iterCount == 70)
                    // Up to 1s after an additional ~30s have elapsed.
                    sleepDuration *= 2;
                else if (iterCount == 520)
                    // After 15m, jump up to 5s intervals
                    sleepDuration *= 5;

                Float percentDone = (Float)Interlocked.Read(ref workDone) / (Float)totalWork;
                int plussesNeeded = (int)std.round(totalPlusses * percentDone);
                while (plussesPrinted < plussesNeeded)
                {
                    buf[curSpace++] = '+';
                    ++plussesPrinted;
                }
                Console.Out.Write(new string(buf, 0, bufLen));

                // Update elapsed time and estimated time to completion
                Float elapsed = (Float)ElapsedSeconds();
                Float estRemaining = elapsed / percentDone - elapsed;
                if (Volatile.Read(ref exitThread) != 0)
                    Console.Out.Write(Globals.StringPrintf(" (%.1fs)       ", finishTime.Value));
                else if (percentDone == 1f)
                    Console.Out.Write(Globals.StringPrintf(" (%.1fs)       ", elapsed));
                else if (!Float.IsInfinity(estRemaining) && !Float.IsNaN(estRemaining))
                    Console.Out.Write(Globals.StringPrintf(" (%.1fs|%.1fs)  ", elapsed, std.max<Float>(0, estRemaining)));
                else
                    Console.Out.Write(Globals.StringPrintf(" (%.1fs|?s)  ", elapsed));
                Console.Out.Flush();
            }
        }

        private static int TerminalWidth()
        {
            try
            {
                if (Console.IsOutputRedirected)
                    return 80;  // ENOTTY is fine and expected
                int w = Console.WindowWidth;
                return w > 0 ? w : 80;
            }
            catch (Exception)
            {
                return 80;
            }
        }

        // ProgressReporter Private Members
        private long totalWork;
        private string title;
        private bool quiet;
        private Timer timer = new Timer();
        private long workDone;
        private int exitThread;
        private Thread updateThread;
        private float? finishTime;
    }

}  // namespace pbrt
