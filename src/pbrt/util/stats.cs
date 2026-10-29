// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/stats.h, util/stats.cpp
//
// The STAT_* macros declare a thread_local variable and a StatRegisterer at namespace scope.
// In C# each use site declares them directly:
//
//   [ThreadStatic] static long nTests;
//   static StatRegisterer STATS_REGnTests = new StatRegisterer((StatsAccumulator accum) =>
//       { accum.ReportCounter("Title", nTests); nTests = 0; });
//
// (Registration happens when the declaring class is first used rather than at program start.)

using System;
using System.Collections.Generic;
using System.IO;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // Statistics Local Variables

        public static bool pixelStatsEnabled = false;
        [ThreadStatic] private static ThreadStatsState threadStatsStateInstance;
        private static ThreadStatsState threadStatsState
        {
            get
            {
                if (threadStatsStateInstance == null)
                    threadStatsStateInstance = new ThreadStatsState();
                return threadStatsStateInstance;
            }
        }

        internal static StatsAccumulator statsAccumulator = new StatsAccumulator();

        internal static Bounds2i imageBounds;
        public static string pixelStatsBaseName;

        private static readonly object reportMutex = new object();

        // Statistics Function Definitions
        public static void ReportThreadStats()
        {
            lock (reportMutex)
            {
                StatRegisterer.CallCallbacks(statsAccumulator);
                // (A .NET thread that never rendered a pixel has an empty time image; skip it rather than
                //  tripping the resolution CHECK in AccumulatePixelStats.)
                if (pixelStatsEnabled && threadStatsState.accum.stats.time.Resolution() != new Point2i(0, 0))
                {
                    statsAccumulator.AccumulatePixelStats(threadStatsState.accum);
                    threadStatsState.accum = new PixelStatsAccumulator();
                }
            }
        }

        public static void StatsReportPixelStart(Point2i p)
        {
            if (!pixelStatsEnabled)
                return;
            CHECK(threadStatsState.active == false);
            threadStatsState.active = true;
            threadStatsState.p = p;
            threadStatsState.start = System.Diagnostics.Stopwatch.StartNew();
        }

        public static void StatsReportPixelEnd(Point2i p)
        {
            if (!pixelStatsEnabled)
                return;

            ThreadStatsState tss = threadStatsState;
            CHECK(tss.active == true && tss.p == p);
            tss.active = false;

            float deltaMS = (float)tss.start.Elapsed.TotalMilliseconds;
            tss.accum.ReportPixelMS(p, deltaMS);

            StatRegisterer.CallPixelCallbacks(p, tss.accum);
        }

        public static void StatsEnablePixelStats(Bounds2i b, string baseName)
        {
            pixelStatsEnabled = true;
            imageBounds = b;
            pixelStatsBaseName = baseName;
        }

        public static void PrintStats(TextWriter dest)
        {
            statsAccumulator.Print(dest);
        }

        public static bool PrintCheckRare(TextWriter dest)
        {
            return statsAccumulator.PrintCheckRare(dest);
        }

        public static void ClearStats()
        {
            statsAccumulator.Clear();
        }

        public static void StatsWritePixelImages()
        {
            statsAccumulator.WritePixelImages();
        }

        internal static void getCategoryAndTitle(string str, out string category, out string title)
        {
            category = "";
            title = "";
            List<string> comps = SplitString(str, '/');
            if (comps.Count == 1)
                title = comps[0];
            else
            {
                CHECK_EQ(comps.Count, 2);
                category = comps[0];
                title = comps[1];
            }
        }
    }

    // (kept out of pbrt so registration from pbrt' own static initializers works)
    internal static class StatRegistry
    {
        internal static readonly object statsLock = new object();
        internal static List<Action<StatsAccumulator>> statFuncs = new List<Action<StatsAccumulator>>();
        internal static List<Action<Point2i, int, PixelStatsAccumulator>> pixelStatFuncs =
            new List<Action<Point2i, int, PixelStatsAccumulator>>();
    }

    // StatRegisterer Definition
    public unsafe class StatRegisterer
    {

        public static void CallCallbacks(StatsAccumulator accum)
        {
            Action<StatsAccumulator>[] funcs;
            lock (StatRegistry.statsLock)
                funcs = StatRegistry.statFuncs.ToArray();
            foreach (Action<StatsAccumulator> func in funcs)
                func(accum);
        }
        // StatRegisterer Public Methods
        public StatRegisterer(Action<StatsAccumulator> func, Action<Point2i, int, PixelStatsAccumulator> pfunc = null)
        {
            lock (StatRegistry.statsLock)
            {
                if (func != null)
                    StatRegistry.statFuncs.Add(func);
                if (pfunc != null)
                    StatRegistry.pixelStatFuncs.Add(pfunc);
            }
        }

        public static void CallPixelCallbacks(Point2i p, PixelStatsAccumulator accum)
        {
            Action<Point2i, int, PixelStatsAccumulator>[] funcs;
            lock (StatRegistry.statsLock)
                funcs = StatRegistry.pixelStatFuncs.ToArray();
            for (int i = 0; i < funcs.Length; ++i)
                funcs[i](p, i, accum);
        }
    }

    public unsafe class StatIntDistribution
    {
        public int64_t sum = 0, count = 0;
        public int64_t min = std.numeric_limits_long.max;
        public int64_t max = std.numeric_limits_long.lowest;
        public void Add(int64_t value)  // operator<<
        {
            sum += value;
            count += 1;
            min = (value < min) ? value : min;
            max = (value > max) ? value : max;
        }
    }

    // ThreadStatsState Definition
    public unsafe class ThreadStatsState
    {
        public Point2i p;
        public bool active;
        public System.Diagnostics.Stopwatch start;
        public PixelStatsAccumulator accum = new PixelStatsAccumulator();
    }

    // PixelStatsAccumulator Definition
    public unsafe class PixelStatsAccumulator
    {
        // PixelStatsAccumulator::PixelStats Definition
        internal class PixelStats
        {
            public Image time = new Image();
            public List<string> counterNames = new List<string>();
            public List<Image> counterImages = new List<Image>();
            public List<string> ratioNames = new List<string>();
            public List<Image> ratioImages = new List<Image>();
        }

        public PixelStatsAccumulator()
        {
            stats = new PixelStats();
        }

        // PixelStatsAccumulator Method Definitions
        public void ReportPixelMS(Point2i p, float ms)
        {
            Point2i res = (Point2i)pbrt.imageBounds.Diagonal();
            if (stats.time.Resolution() != res)
                stats.time = new Image(PixelFormat.Float, res, new string[] { "ms" });

            Point2i pp = new Point2i(p.x - pbrt.imageBounds.pMin.x, p.y - pbrt.imageBounds.pMin.y);
            stats.time.SetChannel(pp, 0, stats.time.GetChannel(pp, 0) + ms);
        }

        public void ReportCounter(Point2i p, int statIndex, string name, int64_t val)
        {
            if (statIndex >= stats.counterImages.Count)
            {
                while (stats.counterImages.Count < statIndex + 1) stats.counterImages.Add(new Image());
                while (stats.counterNames.Count < statIndex + 1) stats.counterNames.Add("");
                stats.counterNames[statIndex] = name;
            }

            Image im = stats.counterImages[statIndex];
            Point2i res = (Point2i)pbrt.imageBounds.Diagonal();
            if (im.Resolution() != res)
            {
                im = new Image(PixelFormat.Float, res, new string[] { "count" });
                stats.counterImages[statIndex] = im;
            }

            Point2i pp = new Point2i(p.x - pbrt.imageBounds.pMin.x, p.y - pbrt.imageBounds.pMin.y);
            im.SetChannel(pp, 0, im.GetChannel(pp, 0) + val);
        }

        public void ReportRatio(Point2i p, int statIndex, string name, int64_t num, int64_t denom)
        {
            if (statIndex >= stats.ratioImages.Count)
            {
                while (stats.ratioImages.Count < statIndex + 1) stats.ratioImages.Add(new Image());
                while (stats.ratioNames.Count < statIndex + 1) stats.ratioNames.Add("");
                stats.ratioNames[statIndex] = name;
            }

            Image im = stats.ratioImages[statIndex];
            Point2i res = (Point2i)pbrt.imageBounds.Diagonal();
            if (im.Resolution() != res)
            {
                im = new Image(PixelFormat.Float, res, new string[] { "numerator", "denominator", "ratio" });
                stats.ratioImages[statIndex] = im;
            }

            Point2i pp = new Point2i(p.x - pbrt.imageBounds.pMin.x, p.y - pbrt.imageBounds.pMin.y);
            im.SetChannel(pp, 0, im.GetChannel(pp, 0) + num);
            im.SetChannel(pp, 1, im.GetChannel(pp, 1) + denom);
            if (im.GetChannel(pp, 0) == 0)
                im.SetChannel(pp, 2, 0);
            else
                im.SetChannel(pp, 2, im.GetChannel(pp, 0) / im.GetChannel(pp, 1));
        }

        internal PixelStats stats = null;
    }

    // StatsAccumulator Definition
    public unsafe class StatsAccumulator
    {
        // StatsAccumulator::Stats Definition
        private class Stats
        {
            public SortedDictionary<string, int64_t> counters = new SortedDictionary<string, int64_t>(StringComparer.Ordinal);
            public SortedDictionary<string, int64_t> memoryCounters = new SortedDictionary<string, int64_t>(StringComparer.Ordinal);
            public class Distribution<T>
            {
                public int64_t count = 0;
                public T min, max, sum;
            }
            public SortedDictionary<string, Distribution<int64_t>> intDistributions =
                new SortedDictionary<string, Distribution<int64_t>>(StringComparer.Ordinal);
            public SortedDictionary<string, Distribution<double>> floatDistributions =
                new SortedDictionary<string, Distribution<double>>(StringComparer.Ordinal);
            public SortedDictionary<string, (int64_t first, int64_t second)> percentages =
                new SortedDictionary<string, (int64_t, int64_t)>(StringComparer.Ordinal);
            public SortedDictionary<string, (int64_t first, int64_t second)> ratios =
                new SortedDictionary<string, (int64_t, int64_t)>(StringComparer.Ordinal);
            public class RareCheck
            {
                public RareCheck(Float f = 0) { maxFrequency = f; }
                public Float maxFrequency;
                public int64_t numTrue = 0, total = 0;
            }
            public SortedDictionary<string, RareCheck> rareChecks = new SortedDictionary<string, RareCheck>(StringComparer.Ordinal);

            public Image pixelTime = new Image();
            public List<string> pixelCounterNames = new List<string>();
            public List<Image> pixelCounterImages = new List<Image>();
            public List<string> pixelRatioNames = new List<string>();
            public List<Image> pixelRatioImages = new List<Image>();
        }

        public void ReportCounter(string name, int64_t val)
        {
            int64_t cur;
            stats.counters.TryGetValue(name, out cur);
            stats.counters[name] = cur + val;
        }

        // StatsAccumulator Public Methods
        public StatsAccumulator()
        {
            stats = new Stats();
        }

        public void ReportMemoryCounter(string name, int64_t val)
        {
            int64_t cur;
            stats.memoryCounters.TryGetValue(name, out cur);
            stats.memoryCounters[name] = cur + val;
        }

        public void ReportPercentage(string name, int64_t num, int64_t denom)
        {
            (int64_t first, int64_t second) cur;
            stats.percentages.TryGetValue(name, out cur);
            stats.percentages[name] = (cur.first + num, cur.second + denom);
        }

        public void ReportRatio(string name, int64_t num, int64_t denom)
        {
            (int64_t first, int64_t second) cur;
            stats.ratios.TryGetValue(name, out cur);
            stats.ratios[name] = (cur.first + num, cur.second + denom);
        }

        public void ReportRareCheck(string condition, Float maxFrequency, int64_t numTrue, int64_t total)
        {
            if (!stats.rareChecks.ContainsKey(condition))
                stats.rareChecks[condition] = new Stats.RareCheck(maxFrequency);
            Stats.RareCheck rc = stats.rareChecks[condition];
            rc.numTrue += numTrue;
            rc.total += total;
        }

        public void ReportIntDistribution(string name, int64_t sum, int64_t count, int64_t min, int64_t max)
        {
            Stats.Distribution<int64_t> distrib;
            if (!stats.intDistributions.TryGetValue(name, out distrib))
            {
                distrib = new Stats.Distribution<int64_t>();
                distrib.min = std.numeric_limits_long.max;
                distrib.max = std.numeric_limits_long.lowest;
                stats.intDistributions[name] = distrib;
            }
            distrib.sum += sum;
            distrib.count += count;
            distrib.min = Math.Min(distrib.min, min);
            distrib.max = Math.Max(distrib.max, max);
        }

        public void ReportFloatDistribution(string name, double sum, int64_t count, double min, double max)
        {
            Stats.Distribution<double> distrib;
            if (!stats.floatDistributions.TryGetValue(name, out distrib))
            {
                distrib = new Stats.Distribution<double>();
                distrib.min = std.numeric_limits_double.max;
                distrib.max = std.numeric_limits_double.lowest;
                stats.floatDistributions[name] = distrib;
            }
            distrib.sum += sum;
            distrib.count += count;
            distrib.min = Math.Min(distrib.min, min);
            distrib.max = Math.Max(distrib.max, max);
        }

        public void AccumulatePixelStats(PixelStatsAccumulator accum)
        {
            Point2i res = (Point2i)pbrt.imageBounds.Diagonal();
            if (stats.pixelTime.Resolution() == new Point2i(0, 0))
                stats.pixelTime = new Image(PixelFormat.Float, res, new string[] { "ms" });
            else
                CHECK_EQ(stats.pixelTime.Resolution(), res);
            CHECK_EQ(stats.pixelTime.Resolution(), accum.stats.time.Resolution());

            for (int y = 0; y < stats.pixelTime.Resolution().y; ++y)
                for (int x = 0; x < stats.pixelTime.Resolution().x; ++x)
                    stats.pixelTime.SetChannel(new Point2i(x, y), 0,
                                               (stats.pixelTime.GetChannel(new Point2i(x, y), 0) +
                                                accum.stats.time.GetChannel(new Point2i(x, y), 0)));

            while (stats.pixelCounterImages.Count < accum.stats.counterImages.Count)
                stats.pixelCounterImages.Add(new Image());
            while (stats.pixelCounterNames.Count < accum.stats.counterNames.Count)
                stats.pixelCounterNames.Add("");
            while (stats.pixelRatioImages.Count < accum.stats.ratioImages.Count)
                stats.pixelRatioImages.Add(new Image());
            while (stats.pixelRatioNames.Count < accum.stats.ratioNames.Count)
                stats.pixelRatioNames.Add("");

            for (int i = 0; i < accum.stats.counterImages.Count; ++i)
            {
                if (stats.pixelCounterNames[i].Length == 0)
                    stats.pixelCounterNames[i] = accum.stats.counterNames[i];

                Image threadImage = accum.stats.counterImages[i];
                if (threadImage.Resolution() == new Point2i(0, 0))
                    continue;
                Image accumImage = stats.pixelCounterImages[i];
                if (accumImage.Resolution() == new Point2i(0, 0))
                {
                    accumImage = new Image(PixelFormat.Float, threadImage.Resolution(), new string[] { "count" });
                    stats.pixelCounterImages[i] = accumImage;
                }
                for (int y = 0; y < threadImage.Resolution().y; ++y)
                    for (int x = 0; x < threadImage.Resolution().x; ++x)
                        accumImage.SetChannel(new Point2i(x, y), 0,
                                              (accumImage.GetChannel(new Point2i(x, y), 0) +
                                               threadImage.GetChannel(new Point2i(x, y), 0)));
            }
            for (int i = 0; i < accum.stats.ratioImages.Count; ++i)
            {
                if (stats.pixelRatioNames[i].Length == 0)
                    stats.pixelRatioNames[i] = accum.stats.ratioNames[i];

                Image threadImage = accum.stats.ratioImages[i];
                if (threadImage.Resolution() == new Point2i(0, 0))
                    continue;
                Image accumImage = stats.pixelRatioImages[i];
                if (accumImage.Resolution() == new Point2i(0, 0))
                {
                    accumImage = new Image(PixelFormat.Float, threadImage.Resolution(),
                                           new string[] { "numerator", "denominator", "ratio" });
                    stats.pixelRatioImages[i] = accumImage;
                }
                for (int y = 0; y < threadImage.Resolution().y; ++y)
                    for (int x = 0; x < threadImage.Resolution().x; ++x)
                    {
                        Point2i pp = new Point2i(x, y);
                        accumImage.SetChannel(pp, 0, (accumImage.GetChannel(pp, 0) + threadImage.GetChannel(pp, 0)));
                        accumImage.SetChannel(pp, 1, (accumImage.GetChannel(pp, 1) + threadImage.GetChannel(pp, 1)));
                        if (accumImage.GetChannel(pp, 0) == 0)
                            accumImage.SetChannel(pp, 2, 0.0f);
                        else
                            accumImage.SetChannel(pp, 2, (accumImage.GetChannel(pp, 0) / accumImage.GetChannel(pp, 1)));
                    }
            }
        }

        private static string printBytes(size_t bytes)
        {
            float kb = (float)((double)bytes / 1024.0);
            if (Math.Abs(kb) < 1024.0f)
                return pbrt.StringPrintf("%9.2f kB", kb);

            float mib = kb / 1024.0f;
            if (Math.Abs(mib) < 1024.0f)
                return pbrt.StringPrintf("%9.2f MiB", mib);

            float gib = mib / 1024.0f;
            return pbrt.StringPrintf("%9.2f GiB", gib);
        }

        public void Print(TextWriter dest)
        {
            dest.Write("Statistics:\n");
            SortedDictionary<string, List<string>> toPrint = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            Action<string, string> add = (string cat, string line) =>
            {
                List<string> l;
                if (!toPrint.TryGetValue(cat, out l)) { l = new List<string>(); toPrint[cat] = l; }
                l.Add(line);
            };

            foreach (KeyValuePair<string, int64_t> counter in stats.counters)
            {
                if (counter.Value == 0)
                    continue;
                string category, title;
                pbrt.getCategoryAndTitle(counter.Key, out category, out title);
                add(category, pbrt.StringPrintf("%-42s               %12d", title, counter.Value));
            }

            size_t totalMemoryReported = 0;
            foreach (KeyValuePair<string, int64_t> counter in stats.memoryCounters)
            {
                if (counter.Value == 0)
                    continue;
                totalMemoryReported += (size_t)counter.Value;

                string category, title;
                pbrt.getCategoryAndTitle(counter.Key, out category, out title);
                add(category, pbrt.StringPrintf("%-42s                  %s", title, printBytes((size_t)counter.Value)));
            }
            int64_t unreportedBytes = (int64_t)pbrt.GetCurrentRSS() - (int64_t)totalMemoryReported;
            if (unreportedBytes > 0)
                add("Memory", pbrt.StringPrintf("%-42s                  %s", "Unreported / unused",
                                                   printBytes((size_t)unreportedBytes)));

            foreach (KeyValuePair<string, Stats.Distribution<int64_t>> distrib in stats.intDistributions)
            {
                string name = distrib.Key;
                if (distrib.Value.count == 0)
                    continue;
                string category, title;
                pbrt.getCategoryAndTitle(name, out category, out title);
                double avg = (double)distrib.Value.sum / (double)distrib.Value.count;
                add(category, pbrt.StringPrintf(
                    "%-42s                      %.3f avg [range %d - %d]", title, avg, distrib.Value.min, distrib.Value.max));
            }
            foreach (KeyValuePair<string, Stats.Distribution<double>> distrib in stats.floatDistributions)
            {
                string name = distrib.Key;
                if (distrib.Value.count == 0)
                    continue;
                string category, title;
                pbrt.getCategoryAndTitle(name, out category, out title);
                double avg = (double)distrib.Value.sum / (double)distrib.Value.count;
                add(category, pbrt.StringPrintf("%-42s                      %.3f avg [range %f - %f]", title,
                                                   avg, distrib.Value.min, distrib.Value.max));
            }
            foreach (KeyValuePair<string, (int64_t first, int64_t second)> percentage in stats.percentages)
            {
                if (percentage.Value.second == 0)
                    continue;
                int64_t num = percentage.Value.first;
                int64_t denom = percentage.Value.second;
                string category, title;
                pbrt.getCategoryAndTitle(percentage.Key, out category, out title);
                add(category, pbrt.StringPrintf("%-42s%12d / %12d (%.2f%%)", title, num, denom,
                                                   (100.0f * num) / denom));
            }
            foreach (KeyValuePair<string, (int64_t first, int64_t second)> ratio in stats.ratios)
            {
                if (ratio.Value.second == 0)
                    continue;
                int64_t num = ratio.Value.first;
                int64_t denom = ratio.Value.second;
                string category, title;
                pbrt.getCategoryAndTitle(ratio.Key, out category, out title);
                add(category, pbrt.StringPrintf("%-42s%12d / %12d (%.2fx)", title, num, denom,
                                                   (double)num / (double)denom));
            }

            foreach (KeyValuePair<string, List<string>> categories in toPrint)
            {
                dest.Write("  " + categories.Key + "\n");
                foreach (string item in categories.Value)
                    dest.Write("    " + item + "\n");
            }
        }

        public void WritePixelImages()
        {
            // FIXME: do this where?
            CHECK(stats.pixelTime.Write(pbrt.pixelStatsBaseName + "-time.exr"));

            Func<string, string> rewriteSlashes = (string s) => s.Replace('/', '_');

            for (int i = 0; i < stats.pixelCounterImages.Count; ++i)
            {
                string n = pbrt.pixelStatsBaseName + "-" + rewriteSlashes(stats.pixelCounterNames[i]) + ".exr";

                Func<Image, bool> AllZero = (Image im) =>
                {
                    for (int y = 0; y < im.Resolution().y; ++y)
                        for (int x = 0; x < im.Resolution().x; ++x)
                            if (im.GetChannel(new Point2i(x, y), 0) != 0)
                                return false;
                    return true;
                };
                if (!AllZero(stats.pixelCounterImages[i]))
                    CHECK(stats.pixelCounterImages[i].Write(n));
            }

            for (int i = 0; i < stats.pixelRatioImages.Count; ++i)
            {
                string n = pbrt.pixelStatsBaseName + "-" + rewriteSlashes(stats.pixelRatioNames[i]) + ".exr";

                Func<Image, bool> AllZero = (Image im) =>
                {
                    for (int y = 0; y < im.Resolution().y; ++y)
                        for (int x = 0; x < im.Resolution().x; ++x)
                            if (im.GetChannels(new Point2i(x, y)).MaxValue() != 0)
                                return false;
                    return true;
                };
                if (!AllZero(stats.pixelRatioImages[i]))
                    CHECK(stats.pixelRatioImages[i].Write(n));
            }
        }

        public bool PrintCheckRare(TextWriter dest)
        {
            bool anyFailed = false;
            foreach (KeyValuePair<string, Stats.RareCheck> iter in stats.rareChecks)
            {
                Stats.RareCheck rc = iter.Value;
                Float trueFreq = (Float)((double)rc.numTrue / (double)rc.total);
                Float varianceEstimate = (Float)(1 / (double)(rc.total - 1) * trueFreq * (1 - trueFreq));
                if (trueFreq - 2 * varianceEstimate >= rc.maxFrequency)
                {
                    dest.Write(pbrt.StringPrintf(
                        "%s @ %.9g failures was %fx over limit %.9g (%d samples, sigma est %.9g)\n",
                        iter.Key, trueFreq, trueFreq / rc.maxFrequency, rc.maxFrequency, rc.total, varianceEstimate));
                    anyFailed = true;
                }
            }
            return anyFailed;
        }

        public void Clear()
        {
            stats.counters.Clear();
            stats.memoryCounters.Clear();
            stats.intDistributions.Clear();
            stats.floatDistributions.Clear();
            stats.percentages.Clear();
            stats.ratios.Clear();
        }

        // StatsAccumulator Private Data
        private Stats stats = null;
    }
}
