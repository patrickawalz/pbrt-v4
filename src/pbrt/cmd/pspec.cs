// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0

// pspec.cpp

// Computes power spectra of a variety point sets used by pbrt's samplers.

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
#else
global using Float = System.Single;
#endif
global using int32_t = System.Int32;
global using uint32_t = System.UInt32;
global using uint64_t = System.UInt64;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using pbrt;
using static global::pbrt.pbrt;

public static unsafe class PspecMain
{
    static void usage(string msg = "")
    {
        Console.Error.Write("\n");
        if (msg.Length != 0)
            Console.Error.Write("pspec: " + msg + "\n\n");

        Console.Error.Write(@"usage: pspec <sampler> [<options...>]

Where <sampler> is one of:
    cwd.pts:         Each file named ""pts-*"" in the current directory is read to
                     find the sample values for a single point set. Files are
                     plain text and should just be whitespace-separated sample
                     values between 0 and 1.
    grid:            A regular grid of sample points, with floor(sqrt(npoints))
                     samples.
    halton:          The first two dimensions of the Halton sequence.
    halton.owen:     The first two dimensions of the Halton sequence, randomized
                     with Owen scrambling.
    halton.permutedigits:
                     The first two dimensions of the Halton sequence, randomized
                     using random digit permutations.
    independent:     Independent uniform random samples.
    lhs:             Latin hypercube sampling.
    pmj02bn:         Progressive multi-jittered (0,2) blue noise points. (Note:
                     pbrt uses precomputed tables for these and only has five,
                     so nsets > 5 does not make sense in this case.)
    sobol:           The first two dimensions of the Sobol' sequence.
    sobol.fastowen:  The first two dimensions of the Sobol' sequence, randomized
                     using a fast hashing approach that operates on all bits in
                     parallel.
    sobol.owen:      The first two dimensions of the Sobol' sequence, randomized
                     using Owen scrambling.
    sobol.permutedigits:
                     The first two dimensions of the Sobol' sequence, randomized
                     with bitwise permutations.
    sobol.z:         Randomized Morton z-curve Sobol' corresponding to the
                     ZSobolSampler.
    stdin.binary:    Sample values are read from standard input as binary 32-bit
                     floats. Multiple point sets may be provided by providing
                     successive point sets one after the previous.
    stdin.dat:       Sample values are read from standard input as plain text
                     numbers.  Multiple point sets may be provided by separating
                     them with a '#' character.
    stratified:      A grid of stratified sample points, with floor(sqrt(npoints))
                     in each dimension.

Options:
  --npoints <n>        Number of sample points to generate in each set.
                       (Default: 1024).
  --nsets <n>          Number of independent sets of sample points.
                       (Default: 4).
  --outbase <name>     Base filename to use for saving output.  The power
                       spectrum is saved in a filewith the given name and an
                       "".exr"" suffix, and the power spectrum is saved with an
                       "".txt"" suffix.
                       (Default: <sampler>-<npoints>pts-<nsets>sets.)
  --resolution <res>   Resolution of image for power spectrum. (Default: 1500).

");
    }

    // Minimal stand-in for C stdio's character stream plus scanf("%f")/fscanf("%f").
    sealed class CharStream
    {
        Stream s;
        int peeked = -2;
        public CharStream(Stream s) { this.s = new BufferedStream(s); }
        public int Peek() { if (peeked == -2) peeked = s.ReadByte(); return peeked; }
        public int Get() { int c = Peek(); peeked = -2; return c; }

        // Returns true and sets v if a float could be scanned.
        public bool ScanFloat(out float v)
        {
            v = 0;
            int c;
            while ((c = Peek()) != -1 && (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v'))
                Get();
            StringBuilder sb = new StringBuilder();
            c = Peek();
            if (c == '+' || c == '-') { sb.Append((char)Get()); }
            bool digits = false;
            while ((c = Peek()) >= '0' && c <= '9') { sb.Append((char)Get()); digits = true; }
            if (Peek() == '.')
            {
                sb.Append((char)Get());
                while ((c = Peek()) >= '0' && c <= '9') { sb.Append((char)Get()); digits = true; }
            }
            if (!digits) return false;
            c = Peek();
            if (c == 'e' || c == 'E')
            {
                sb.Append((char)Get());
                c = Peek();
                if (c == '+' || c == '-') sb.Append((char)Get());
                while ((c = Peek()) >= '0' && c <= '9') sb.Append((char)Get());
            }
            return float.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }

    static CharStream stdinStream = null;
    static CharStream StdIn()
    {
        if (stdinStream == null) stdinStream = new CharStream(Console.OpenStandardInput());
        return stdinStream;
    }

    static List<string> cwdFiles = null;
    static int cwdOffset = 0;

    static pstd.optional<List<Point2f>> GenerateSamples(string samplerName, int nPoints, int iter)
    {
        List<Point2f> points = new List<Point2f>(nPoints);

        if (samplerName == "stdin.binary")
        {
            Stream stdin = Console.OpenStandardInput();
            byte[] buf = new byte[8];
            for (int i = 0; i < nPoints; ++i)
            {
                int got = 0;
                while (got < 8)
                {
                    int r = stdin.Read(buf, got, 8 - got);
                    if (r <= 0) break;
                    got += r;
                }
                if (got != 8)
                {
                    if (i > 1)
                        ErrorExit(StringPrintf("Partial point set provided in standard input: have %d points at EOF.", i));
                    return new pstd.optional<List<Point2f>>();
                }
                points.Add(new Point2f(BitConverter.ToSingle(buf, 0), BitConverter.ToSingle(buf, 4)));
            }
        }
        else if (samplerName == "stdin.dat")
        {
            CharStream cs = StdIn();
            for (int i = 0; i < nPoints; ++i)
            {
                float s0, s1 = 0;
                bool ok = cs.ScanFloat(out s0) && cs.ScanFloat(out s1);
                if (!ok)
                {
                    if (i > 1)
                        ErrorExit(StringPrintf("Partial point set provided in standard input: have %d points at EOF.", i));
                    return new pstd.optional<List<Point2f>>();
                }
                points.Add(new Point2f(s0, s1));
            }
            int ch;
            do
            {
                ch = cs.Get();
            } while (ch != '#' && ch != -1);
        }
        else if (samplerName == "cwd.pts")
        {
            if (cwdFiles == null)
                cwdFiles = MatchingFilenames("pts-");
            if (cwdFiles.Count == 0)
                ErrorExit("No *.dat files found in current directory.");

            for (; ; )
            {
                if (cwdOffset == cwdFiles.Count)
                    return new pstd.optional<List<Point2f>>();

                FileStream fs = null;
                try { fs = new FileStream(cwdFiles[cwdOffset], FileMode.Open, FileAccess.Read); }
                catch (Exception) { }
                if (fs == null)
                    ErrorExit(StringPrintf("%s: unable to open file", cwdFiles[cwdOffset]));

                CharStream cs = new CharStream(fs);
                bool retry = false;
                for (int i = 0; i < nPoints; ++i)
                {
                    float s0, s1 = 0;
                    bool ok = cs.ScanFloat(out s0) && cs.ScanFloat(out s1);
                    if (!ok)
                    {
                        Warning(StringPrintf("%s: premature EOF. Read %d / %d points. Ignoring file.",
                                             cwdFiles[iter], i, nPoints));
                        ++cwdOffset;
                        fs.Dispose();
                        points.Clear();
                        retry = true;
                        break;
                    }
                    points.Add(new Point2f(s0, s1));
                }
                if (retry) continue;
                ++cwdOffset;
                fs.Dispose();
                break;
            }
        }
        else if (samplerName == "grid")
        {
            int sqrtSamples = (int)Math.Sqrt((double)nPoints);
            nPoints = Sqr(sqrtSamples);

            for (int i = 0; i < sqrtSamples; ++i)
                for (int j = 0; j < sqrtSamples; ++j)
                    points.Add(new Point2f((Float)i / sqrtSamples, (Float)j / sqrtSamples));
        }
        else if (samplerName == "lhs")
        {
            RNG rng = new RNG((ulong)Options.seed, (ulong)iter);
            // Sample points along the diagonal
            for (int i = 0; i < nPoints; ++i)
            {
                // The C++ evaluates the Point2f constructor arguments right to left (gcc).
                Float uy = rng.Uniform<Float>();
                Float ux = rng.Uniform<Float>();
                points.Add(new Point2f((i + ux) / nPoints, (i + uy) / nPoints));
            }
            // Suffle x
            for (int i = 0; i < nPoints; ++i)
            {
                int other = i + (int)rng.Uniform<uint32_t>((uint32_t)(nPoints - i));
                Point2f pi = points[i], po = points[other];
                Float tmp = pi.x;
                pi.x = po.x;
                po.x = tmp;
                points[i] = pi;
                points[other] = po;
            }
        }
        else if (samplerName == "halton")
        {
            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(RadicalInverse(0, (uint64_t)i), RadicalInverse(1, (uint64_t)i)));
        }
        else if (samplerName == "halton.permutedigits")
        {
            RNG rng = new RNG((ulong)Options.seed, (ulong)iter);
            DigitPermutation perm2 = new DigitPermutation(2, rng.Uniform<uint32_t>(), null);
            DigitPermutation perm3 = new DigitPermutation(3, rng.Uniform<uint32_t>(), null);

            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(ScrambledRadicalInverse(0, (uint64_t)i, perm2),
                                       ScrambledRadicalInverse(1, (uint64_t)i, perm3)));
        }
        else if (samplerName == "halton.owen")
        {
            RNG rng = new RNG((ulong)Options.seed, (ulong)iter);
            uint32_t r0 = rng.Uniform<uint32_t>();
            uint32_t r1 = rng.Uniform<uint32_t>();

            for (int i = 0; i < nPoints; ++i)
                points.Add(new Point2f(SobolSample(i, 0, new OwenScrambler(r0)),
                                       OwenScrambledRadicalInverse(1, (uint64_t)i, r1)));
        }
        else if (samplerName == "sobol.z")
        {
            if (!IsPowerOf2(nPoints))
                ErrorExit("Must use power of 2 points for \"sobol.z\".");

            int n = 1 << (Log2Int(nPoints) / 2);
            int spp = nPoints / Sqr(n);
            ZSobolSampler sampler = new ZSobolSampler(spp, new Point2i(n, n), RandomizeStrategy.Owen, Options.seed);

            for (int y = 0; y < n; ++y)
                for (int x = 0; x < n; ++x)
                {
                    sampler.StartPixelSample(new Point2i(x, y), 0, 0);
                    for (int s = 0; s < spp; ++s)
                    {
                        Point2f u = sampler.Get2D();
                        points.Add(new Point2f((x + u[0]) / n, (y + u[1]) / n));
                    }
                }
        }
        else
        {
            Sampler sampler;
            if (samplerName == "independent")
                sampler = new IndependentSampler(nPoints, Options.seed);
            else if (samplerName == "stratified")
            {
                int sqrtSamples = (int)Math.Sqrt((double)nPoints);
                nPoints = Sqr(sqrtSamples);
                sampler = new StratifiedSampler(sqrtSamples, sqrtSamples, true, Options.seed);
            }
            else if (samplerName == "pmj02bn")
                sampler = new PMJ02BNSampler(nPoints, Options.seed);
            else if (samplerName == "sobol")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.None, Options.seed);
            else if (samplerName == "sobol.permutedigits")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.PermuteDigits, Options.seed);
            else if (samplerName == "sobol.fastowen")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.FastOwen, Options.seed);
            else if (samplerName == "sobol.owen")
                sampler = new PaddedSobolSampler(nPoints, RandomizeStrategy.Owen, Options.seed);
            else
            {
                usage(StringPrintf("%s: sampler unknown", samplerName));
                Environment.Exit(1);
                return new pstd.optional<List<Point2f>>();
            }

            for (int i = 0; i < nPoints; ++i)
            {
                sampler.StartPixelSample(new Point2i(0, 0), i, 0);
                Point2f u = sampler.Get2D();
                points.Add(u);
            }
        }

        return new pstd.optional<List<Point2f>>(points);
    }

    // printf("%f") of a double, the way glibc prints it.
    static string F(double v)
    {
        if (double.IsNaN(v)) return "-nan";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }

    public static int Main(string[] argv)
    {
        if (argv.Length < 1)
        {
            usage();
            return 1;
        }

        List<string> args = GetCommandLineArguments(argv);

        string samplerName = "";
        int nPoints = 1024;
        int nSets = 0;
        int res = 1500;
        string baseOutFilename = "";

        Action<string> onError = (string err) =>
        {
            usage(err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "npoints", ref nPoints, onError) ||
                ParseArg(args, ref iter, end, "resolution", ref res, onError) ||
                ParseArg(args, ref iter, end, "outbase", ref baseOutFilename, onError) ||
                ParseArg(args, ref iter, end, "nsets", ref nSets, onError))
                ;
            else if (samplerName.Length == 0)
            {
                if (args[iter][0] == '-')
                {
                    usage();
                    return 0;
                }
                samplerName = args[iter];
            }
            else
            {
                usage(StringPrintf("unknown argument \"%s\"", args[iter]));
                return 1;
            }
        }

        if (samplerName.Length == 0)
        {
            usage("Must specify name of sampler.\n");
            return 1;
        }

        if ((res & 1) == 0)
            ++res;

        PBRTOptions options = new PBRTOptions();
        options.quiet = true;
        InitPBRT(options);

        Allocator alloc = null;

        Image pspec = new Image(PixelFormat.Float, new Point2i(res, res), new List<string> { "power" }, null, alloc);
        ProgressReporter progress = new ProgressReporter(nSets, "Analyzing", nSets == 1);

        int actualNSets = 0;
        while (true)
        {
            Options.seed = (int)MixBits((ulong)actualNSets);

            // Generate points
            pstd.optional<List<Point2f>> pointsOpt = GenerateSamples(samplerName, nPoints, actualNSets);
            if (!pointsOpt)
                break;
            List<Point2f> points = pointsOpt.value();
            ++actualNSets;

            // Fourier transform
            ParallelFor(0, res, (int y) =>
            {
                for (int x = 0; x < res; ++x)
                {
                    Point2f uv = new Point2f(0, 0);
                    Float wx = x - res / 2, wy = y - res / 2;
                    foreach (Point2f u in points)
                    {
                        Float exp = -2 * Pi * (wx * u[0] + wy * u[1]);
                        uv[0] += std.cos(exp);
                        uv[1] += std.sin(exp);
                    }

                    // Update power spectrum
                    pspec.SetChannel(new Point2i(x, y), 0,
                                     (pspec.GetChannel(new Point2i(x, y), 0) + (Sqr(uv[0]) + Sqr(uv[1]))));
                }
            });

            progress.Update();

            if (nSets != 0 && actualNSets == nSets)
                break;
        }

        int nSetsFinal = actualNSets;
        ParallelFor(0, res, (int y) =>
        {
            for (int x = 0; x < res; ++x)
            {
                // Early float cast so no integer overflow...
                pspec.SetChannel(new Point2i(x, y), 0,
                                 (pspec.GetChannel(new Point2i(x, y), 0) / ((Float)nPoints * (Float)nSetsFinal)));
            }
        });

        progress.Done();

        if (baseOutFilename.Length == 0)
            baseOutFilename = StringPrintf("%s-%dpts-%dsets", samplerName, nPoints, actualNSets);

        pspec.Write(baseOutFilename + ".exr");

        // Compute radial average
        Float[] sumPower = new Float[res / 2];
        int[] nPower = new int[res / 2];
        for (int y = 0; y < res; ++y)
            for (int x = 0; x < res; ++x)
            {
                int dx = Math.Abs(x - res / 2), dy = Math.Abs(y - res / 2);
                if (dx == 0 && dy == 0)
                    // skip the central spike
                    continue;
                int bucket = (int)Math.Sqrt((double)(dx * dx + dy * dy));
                if (bucket >= sumPower.Length)
                    continue;
                sumPower[bucket] += pspec.GetChannel(new Point2i(x, y), 0);
                ++nPower[bucket];
            }

        StreamWriter f = null;
        try { f = new StreamWriter(baseOutFilename + ".txt", false, new UTF8Encoding(false)); }
        catch (Exception) { }
        if (f == null)
            ErrorExit(StringPrintf("%s: could not open output file", baseOutFilename + ".txt"));
        for (int i = 1; i < res / 2; ++i)
            f.Write(i + " " + F((double)(sumPower[i] / nPower[i])) + "\n");
        f.Dispose();

        CleanupPBRT();

        return 0;
    }
}
