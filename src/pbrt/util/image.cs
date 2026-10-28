// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
//
// Magi: C# port of pbrt-v4. File: util/image.h, util/image.cpp

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace pbrt
{
    public static unsafe partial class pbrt
    {
        // PixelFormat Inline Functions
        public static bool Is8Bit(PixelFormat format) { return format == PixelFormat.U256; }
        public static bool Is16Bit(PixelFormat format) { return format == PixelFormat.Half; }
        public static bool Is32Bit(PixelFormat format) { return format == PixelFormat.Float; }

        public static string ToString(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.U256: return "U256";
                case PixelFormat.Half: return "Half";
                case PixelFormat.Float: return "Float";
                default:
                    LOG_FATAL("Unhandled PixelFormat in FormatName()");
                    return "";
            }
        }

        public static int TexelBytes(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.U256: return 1;
                case PixelFormat.Half: return 2;
                case PixelFormat.Float: return 4;
                default:
                    LOG_FATAL("Unhandled PixelFormat in TexelBytes()");
                    return 0;
            }
        }

        public static pstd.optional<WrapMode> ParseWrapMode(string w)
        {
            if (w == "clamp")
                return WrapMode.Clamp;
            else if (w == "repeat")
                return WrapMode.Repeat;
            else if (w == "black")
                return WrapMode.Black;
            else if (w == "octahedralsphere")
                return WrapMode.OctahedralSphere;
            else
                return new pstd.optional<WrapMode>();
        }

        public static string ToString(WrapMode mode)
        {
            switch (mode)
            {
                case WrapMode.Clamp: return "clamp";
                case WrapMode.Repeat: return "repeat";
                case WrapMode.Black: return "black";
                case WrapMode.OctahedralSphere: return "octahedralsphere";
                default:
                    LOG_FATAL("Unhandled wrap mode");
                    return null;
            }
        }

        // Image Wrapping Inline Functions
        public static bool RemapPixelCoords(Point2i* pp, Point2i resolution, WrapMode2D wrapMode)
        {
            Point2i p = *pp;
            bool result = RemapPixelCoordsImpl(ref p, resolution, wrapMode);
            *pp = p;
            return result;
        }

        private static bool RemapPixelCoordsImpl(ref Point2i p, Point2i resolution, WrapMode2D wrapMode)
        {
            if (wrapMode.wrap[0] == WrapMode.OctahedralSphere)
            {
                CHECK(wrapMode.wrap[1] == WrapMode.OctahedralSphere);
                if (p[0] < 0)
                {
                    p[0] = -p[0];                     // mirror across u = 0
                    p[1] = resolution[1] - 1 - p[1];  // mirror across v = 0.5
                }
                else if (p[0] >= resolution[0])
                {
                    p[0] = 2 * resolution[0] - 1 - p[0];  // mirror across u = 1
                    p[1] = resolution[1] - 1 - p[1];      // mirror across v = 0.5
                }

                if (p[1] < 0)
                {
                    p[0] = resolution[0] - 1 - p[0];  // mirror across u = 0.5
                    p[1] = -p[1];                     // mirror across v = 0;
                }
                else if (p[1] >= resolution[1])
                {
                    p[0] = resolution[0] - 1 - p[0];      // mirror across u = 0.5
                    p[1] = 2 * resolution[1] - 1 - p[1];  // mirror across v = 1
                }

                // Bleh: things don't go as expected for 1x1 images.
                if (resolution[0] == 1)
                    p[0] = 0;
                if (resolution[1] == 1)
                    p[1] = 0;

                return true;
            }

            for (int c = 0; c < 2; ++c)
            {
                if (p[c] >= 0 && p[c] < resolution[c])
                    // in bounds
                    continue;

                switch (wrapMode.wrap[c])
                {
                    case WrapMode.Repeat:
                        p[c] = Mod(p[c], resolution[c]);
                        break;
                    case WrapMode.Clamp:
                        p[c] = Clamp(p[c], 0, resolution[c] - 1);
                        break;
                    case WrapMode.Black:
                        return false;
                    default:
                        LOG_FATAL("Unhandled WrapMode mode");
                        break;
                }
            }
            return true;
        }
    }

    // PixelFormat Definition
    public enum PixelFormat { U256, Half, Float }

    // WrapMode Definitions
    // (Magi: Clamp is listed first so that default(WrapMode2D) is Clamp/Clamp, which is what C++'s
    // default argument "= WrapMode::Clamp" needs; the numeric values are never used.)
    public enum WrapMode { Clamp, Black, Repeat, OctahedralSphere }

    // ResampleWeight Definition
    public unsafe struct ResampleWeight
    {
        public int firstPixel;
        public Float weight0, weight1, weight2, weight3;
        public Float this[int i]
        {
            get { return i == 0 ? weight0 : (i == 1 ? weight1 : (i == 2 ? weight2 : weight3)); }
            set { if (i == 0) weight0 = value; else if (i == 1) weight1 = value; else if (i == 2) weight2 = value; else weight3 = value; }
        }
    }

    public unsafe struct WrapMode2D
    {
        public WrapMode2D(WrapMode w) { wrap = new pstd.array2<WrapMode>(w, w); }
        public WrapMode2D(WrapMode x, WrapMode y) { wrap = new pstd.array2<WrapMode>(x, y); }
        public static implicit operator WrapMode2D(WrapMode w) { return new WrapMode2D(w); }

        public pstd.array2<WrapMode> wrap;
    }

    // ImageMetadata Definition
    public unsafe class ImageMetadata
    {

        public override string ToString()
        {
            return "[ ImageMetadata renderTimeSeconds: " + Str(renderTimeSeconds) + " cameraFromWorld: " + Str(cameraFromWorld) +
                   " NDCFromWorld: " + Str(NDCFromWorld) + " pixelBounds: " + Str(pixelBounds) +
                   " fullResolution: " + Str(fullResolution) + " samplesPerPixel: " + Str(samplesPerPixel) +
                   " MSE: " + Str(MSE) + " colorSpace: " + (colorSpace ? colorSpace.value().ToString() : "(unset)") + " ]";
        }

        public ImageMetadata Clone()
        {
            ImageMetadata m = (ImageMetadata)MemberwiseClone();
            m.strings = new SortedDictionary<string, string>(strings, StringComparer.Ordinal);
            m.stringVectors = new SortedDictionary<string, List<string>>(stringVectors, StringComparer.Ordinal);
            return m;
        }
        // ImageMetadata Public Methods
        public RGBColorSpace GetColorSpace()
        {
            if (colorSpace && colorSpace.value() != null)
                return colorSpace.value();
            return RGBColorSpace.sRGB;
        }

        private static string Str<T>(pstd.optional<T> o) { return o ? o.value().ToString() : "(unset)"; }

        // ImageMetadata Public Members
        public pstd.optional<float> renderTimeSeconds;
        public pstd.optional<SquareMatrix4> cameraFromWorld, NDCFromWorld;
        public pstd.optional<Bounds2i> pixelBounds;
        public pstd.optional<Point2i> fullResolution;
        public pstd.optional<int> samplesPerPixel;
        public pstd.optional<float> MSE;
        public pstd.optional<RGBColorSpace> colorSpace;
        public SortedDictionary<string, string> strings = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public SortedDictionary<string, List<string>> stringVectors =
            new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
    }

    // ImageChannelDesc Definition
    public unsafe class ImageChannelDesc
    {
        public static implicit operator bool(ImageChannelDesc d) { return d.size() > 0; }

        public size_t size() { return (size_t)offset.Count; }
        public bool IsIdentity()
        {
            for (int i = 0; i < offset.Count; ++i)
                if (offset[i] != i)
                    return false;
            return true;
        }
        public override string ToString()
        {
            return "[ ImageChannelDesc offset: [ " + string.Join(", ", offset) + " ] ]";
        }

        public List<int> offset = new List<int>();  // InlinedVector<int, 4>
    }

    // ImageChannelValues Definition
    public unsafe class ImageChannelValues : List<Float>  // InlinedVector<Float, 4>
    {
        public ImageChannelValues(size_t sz, Float v = default) : base((int)sz)
        {
            for (size_t i = 0; i < sz; ++i)
                Add(v);
        }

        public size_t size() { return (size_t)Count; }

        public static implicit operator Float(ImageChannelValues v)
        {
            CHECK_EQ(1, v.Count);
            return v[0];
        }
        public static implicit operator pstd.array3<Float>(ImageChannelValues v)
        {
            CHECK_EQ(3, v.Count);
            return new pstd.array3<Float>(v[0], v[1], v[2]);
        }

        public Float MaxValue()
        {
            Float m = this[0];
            for (int i = 1; i < Count; ++i)
                m = std.max(m, this[i]);
            return m;
        }
        public Float Average()
        {
            Float sum = 0;
            for (int i = 0; i < Count; ++i)
                sum += this[i];
            return sum / Count;
        }

        public override string ToString()
        {
            return "[ ImageChannelValues [ " + string.Join(", ", this) + " ] ]";
        }
    }

    // ImageAndMetadata Definition
    public unsafe struct ImageAndMetadata
    {
        public Image image;
        public ImageMetadata metadata;
        public ImageAndMetadata(Image image, ImageMetadata metadata) { this.image = image; this.metadata = metadata; }
    }

    // Image Definition
    public unsafe class Image
    {

        public PixelFormat Format() { return format; }
        public Point2i Resolution() { return resolution; }
        public int NChannels() { return (int)channelNames.size(); }
        public ColorEncoding Encoding() { return encoding; }

        public static implicit operator bool(Image im) { return im.resolution.x > 0 && im.resolution.y > 0; }

        // C++ copy construction / assignment
        public Image Clone()
        {
            Image r = new Image();
            r.CopyFrom(this);
            return r;
        }

        public void CopyFrom(Image o)
        {
            format = o.format;
            resolution = o.resolution;
            channelNames = new pstd.vector<string>(o.channelNames.ToArray());
            encoding = o.encoding;
            p8 = new pstd.vector<uint8_t>(o.p8.ToArray());
            p16 = new pstd.vector<Half>(o.p16.ToArray());
            p32 = new pstd.vector<float>(o.p32.ToArray());
        }

        public size_t PixelOffset(Point2i p)
        {
            DCHECK(InsideExclusive(p, new Bounds2i(new Point2i(0, 0), resolution)));
            return (size_t)(NChannels() * (p.y * resolution.x + p.x));
        }

        public Float GetChannel(Point2i p, int c, WrapMode2D wrapMode = default)
        {
            // Remap provided pixel coordinates before reading channel
            if (!RemapPixelCoords(&p, resolution, wrapMode))
                return 0;

            switch (format)
            {
                case PixelFormat.U256:
                    {  // Return _U256_-encoded pixel channel value
                        return encoding.ToLinearByte(p8[PixelOffset(p) + (size_t)c]);
                    }
                case PixelFormat.Half:
                    {  // Return _Half_-encoded pixel channel value
                        return (Float)(float)p16[PixelOffset(p) + (size_t)c];
                    }
                case PixelFormat.Float:
                    {  // Return _Float_-encoded pixel channel value
                        return p32[PixelOffset(p) + (size_t)c];
                    }
                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    return 0;
            }
        }

        public Float BilerpChannel(Point2f p, int c, WrapMode2D wrapMode = default)
        {
            // Compute discrete pixel coordinates and offsets for _p_
            Float x = p[0] * resolution.x - 0.5f, y = p[1] * resolution.y - 0.5f;
            int xi = (int)std.floor(x), yi = (int)std.floor(y);
            Float dx = x - xi, dy = y - yi;

            // Load pixel channel values and return bilinearly interpolated value
            pstd.array4<Float> v = new pstd.array4<Float>(GetChannel(new Point2i(xi, yi), c, wrapMode),
                                                          GetChannel(new Point2i(xi + 1, yi), c, wrapMode),
                                                          GetChannel(new Point2i(xi, yi + 1), c, wrapMode),
                                                          GetChannel(new Point2i(xi + 1, yi + 1), c, wrapMode));
            return ((1 - dx) * (1 - dy) * v[0] + dx * (1 - dy) * v[1] + (1 - dx) * dy * v[2] +
                    dx * dy * v[3]);
        }

        public ImageChannelDesc AllChannelsDesc()
        {
            ImageChannelDesc desc = new ImageChannelDesc();
            for (int i = 0; i < NChannels(); ++i)
                desc.offset.Add(i);
            return desc;
        }

        public Array2D<Float> GetSamplingDistribution(Func<Point2f, Float> dxdA, Bounds2f domain, Allocator alloc = null)
        {
            Array2D<Float> dist = new Array2D<Float>(resolution[0], resolution[1]);
            ParallelFor(0, resolution[1], (long y0, long y1) =>
            {
                for (int y = (int)y0; y < y1; ++y)
                {
                    for (int x = 0; x < resolution[0]; ++x)
                    {
                        // This is noticeably better than MaxValue: discuss / show
                        // example..
                        Float value = GetChannels(new Point2i(x, y)).Average();

                        // Assume Jacobian term is basically constant over the
                        // region.
                        Point2f p = domain.Lerp(
                            new Point2f((x + .5f) / resolution[0], (y + .5f) / resolution[1]));
                        dist[x, y] = value * dxdA(p);
                    }
                }
            });
            return dist;
        }
        public Array2D<Float> GetSamplingDistribution()
        {
            return GetSamplingDistribution((Point2f) => (Float)1,
                                           new Bounds2f(new Point2f(0, 0), new Point2f(1, 1)));
        }

        public Float LookupNearestChannel(Point2f p, int c, WrapMode2D wrapMode = default)
        {
            Point2i pi = new Point2i((int)(p.x * resolution.x), (int)(p.y * resolution.y));
            return GetChannel(pi, c, wrapMode);
        }

        public size_t BytesUsed() { return p8.size() + 2 * p16.size() + 4 * p32.size(); }

        public void SetChannel(Point2i p, int c, Float value)
        {
            // CHECK(!IsNaN(value));
            if (IsNaN(value))
            {
                LOG_ERROR($"NaN at pixel {p.x},{p.y} comp {c}");
                value = 0;
            }

            switch (format)
            {
                case PixelFormat.U256:
                    p8[PixelOffset(p) + (size_t)c] = encoding.FromLinearByte(value);
                    break;
                case PixelFormat.Half:
                    p16[PixelOffset(p) + (size_t)c] = new Half(value);
                    break;
                case PixelFormat.Float:
                    p32[PixelOffset(p) + (size_t)c] = value;
                    break;
                default:
                    LOG_FATAL("Unhandled PixelFormat in Image::SetChannel()");
                    break;
            }
        }

        // Image Method Definitions
        public bool HasAnyInfinitePixels()
        {
            if (format == PixelFormat.U256)
                return false;

            for (int y = 0; y < resolution.y; ++y)
                for (int x = 0; x < resolution.x; ++x)
                    for (int c = 0; c < NChannels(); ++c)
                        if (IsInf(GetChannel(new Point2i(x, y), c)))
                            return true;
            return false;
        }

        public bool HasAnyNaNPixels()
        {
            if (format == PixelFormat.U256)
                return false;

            for (int y = 0; y < resolution.y; ++y)
                for (int x = 0; x < resolution.x; ++x)
                    for (int c = 0; c < NChannels(); ++c)
                        if (IsNaN(GetChannel(new Point2i(x, y), c)))
                            return true;
            return false;
        }

        public Image GaussianFilter(ImageChannelDesc desc, int halfWidth, Float sigma)
        {
            // Compute filter weights
            Float[] wts = new Float[2 * halfWidth + 1];
            for (int d = 0; d < 2 * halfWidth + 1; ++d)
                wts[d] = Gaussian(d - halfWidth, 0, sigma);

            // Normalize weights
            Float wtSum = 0;
            for (int i = 0; i < wts.Length; ++i)
                wtSum += wts[i];
            for (int i = 0; i < wts.Length; ++i)
                wts[i] /= wtSum;

            // Separable blur; first blur in x into blurx, selecting out the
            // desired channels along the way.
            Image blurx = new Image(PixelFormat.Float, resolution, ChannelNames(desc));
            int nc = (int)desc.size();
            ParallelFor(0, resolution.y, (long y0, long y1) =>
            {
                for (int y = (int)y0; y < y1; ++y)
                {
                    for (int x = 0; x < resolution.x; ++x)
                    {
                        ImageChannelValues result = new ImageChannelValues(desc.size());
                        for (int r = -halfWidth; r <= halfWidth; ++r)
                        {
                            ImageChannelValues cv = GetChannels(new Point2i(x + r, y), desc);
                            for (int c = 0; c < nc; ++c)
                                result[c] += wts[r + halfWidth] * cv[c];
                        }
                        blurx.SetChannels(new Point2i(x, y), result);
                    }
                }
            });

            // Now blur in y from blur x to the result; blurx has just the
            // channels we want already.
            Image blury = new Image(PixelFormat.Float, resolution, ChannelNames(desc));
            ParallelFor(0, resolution.y, (long y0, long y1) =>
            {
                for (int y = (int)y0; y < y1; ++y)
                {
                    for (int x = 0; x < resolution.x; ++x)
                    {
                        ImageChannelValues result = new ImageChannelValues(desc.size());
                        for (int r = -halfWidth; r <= halfWidth; ++r)
                        {
                            ImageChannelValues cv = blurx.GetChannels(new Point2i(x, y + r));
                            for (int c = 0; c < nc; ++c)
                                result[c] += wts[r + halfWidth] * cv[c];
                        }
                        blury.SetChannels(new Point2i(x, y), result);
                    }
                }
            });
            return blury;
        }

        private static List<ResampleWeight> ResampleWeights(int oldRes, int newRes)
        {
            CHECK_GE(newRes, oldRes);
            List<ResampleWeight> wt = new List<ResampleWeight>(newRes);
            for (int i = 0; i < newRes; ++i)
                wt.Add(new ResampleWeight());
            Float filterRadius = 2, tau = 2;
            for (int i = 0; i < newRes; ++i)
            {
                // Compute image resampling weights for _i_th pixel
                Float center = (i + .5f) * oldRes / newRes;
                ResampleWeight w = wt[i];
                w.firstPixel = (int)std.floor((center - filterRadius) + 0.5f);
                for (int j = 0; j < 4; ++j)
                {
                    Float pos = w.firstPixel + j + .5f;
                    w[j] = WindowedSinc(pos - center, filterRadius, tau);
                }

                // Normalize filter weights for pixel resampling
                Float invSumWts = 1 / (w[0] + w[1] + w[2] + w[3]);
                for (int j = 0; j < 4; ++j)
                    w[j] *= invSumWts;
                wt[i] = w;
            }
            return wt;
        }

        public Image FloatResizeUp(Point2i newRes, WrapMode2D wrapMode)
        {
            CHECK_GE(newRes.x, resolution.x);
            CHECK_GE(newRes.y, resolution.y);
            Image resampledImage = new Image(PixelFormat.Float, newRes, channelNames.ToArray());
            // Compute $x$ and $y$ resampling weights for image resizing
            List<ResampleWeight> xWeights, yWeights;
            xWeights = ResampleWeights(resolution[0], newRes[0]);
            yWeights = ResampleWeights(resolution[1], newRes[1]);

            // Resize image in parallel, working by tiles
            ParallelFor2D(new Bounds2i(new Point2i(0, 0), newRes), (Bounds2i outExtent) =>
            {
                // Determine extent in source image and copy pixel values to _inBuf_
                Bounds2i inExtent = new Bounds2i(new Point2i(xWeights[outExtent.pMin.x].firstPixel,
                                                             yWeights[outExtent.pMin.y].firstPixel),
                                                 new Point2i(xWeights[outExtent.pMax.x - 1].firstPixel + 4,
                                                             yWeights[outExtent.pMax.y - 1].firstPixel + 4));
                float[] inBuf = new float[NChannels() * inExtent.Area()];
                CopyRectOut(inExtent, new pstd.span<float>(inBuf), wrapMode);

                // Resize image in the $x$ dimension
                // Compute image extents and allocate _xBuf_
                int nxOut = outExtent.pMax.x - outExtent.pMin.x;
                int nyOut = outExtent.pMax.y - outExtent.pMin.y;
                int nxIn = inExtent.pMax.x - inExtent.pMin.x;
                int nyIn = inExtent.pMax.y - inExtent.pMin.y;
                float[] xBuf = new float[NChannels() * nyIn * nxOut];

                int xBufOffset = 0;
                for (int yOut = inExtent.pMin.y; yOut < inExtent.pMax.y; ++yOut)
                {
                    for (int xOut = outExtent.pMin.x; xOut < outExtent.pMax.x; ++xOut)
                    {
                        // Resample image pixel _(xOut, yOut)_
                        DCHECK(xOut >= 0 && xOut < xWeights.Count);
                        ResampleWeight rsw = xWeights[xOut];
                        // Compute _inOffset_ into _inBuf_ for _(xOut, yOut)_
                        // w.r.t. inBuf
                        int xIn = rsw.firstPixel - inExtent.pMin.x;
                        DCHECK_GE(xIn, 0);
                        DCHECK_LT(xIn + 3, nxIn);
                        int yIn = yOut - inExtent.pMin.y;
                        int inOffset = NChannels() * (xIn + yIn * nxIn);
                        DCHECK_GE(inOffset, 0);
                        DCHECK_LT(inOffset + 3 * NChannels(), inBuf.Length);

                        for (int c = 0; c < NChannels(); ++c, ++xBufOffset, ++inOffset)
                            xBuf[xBufOffset] = (float)(rsw[0] * inBuf[inOffset] +
                                               rsw[1] * inBuf[inOffset + NChannels()] +
                                               rsw[2] * inBuf[inOffset + 2 * NChannels()] +
                                               rsw[3] * inBuf[inOffset + 3 * NChannels()]);
                    }
                }

                // Resize image in the $y$ dimension
                float[] outBuf = new float[NChannels() * nxOut * nyOut];
                for (int x = 0; x < nxOut; ++x)
                {
                    for (int y = 0; y < nyOut; ++y)
                    {
                        int yOut = y + outExtent.pMin.y;
                        DCHECK(yOut >= 0 && yOut < yWeights.Count);
                        ResampleWeight rsw = yWeights[yOut];

                        DCHECK_GE(rsw.firstPixel - inExtent.pMin.y, 0);
                        int xBufOffset2 =
                            NChannels() * (x + nxOut * (rsw.firstPixel - inExtent.pMin.y));
                        DCHECK_GE(xBufOffset2, 0);
                        int step = NChannels() * nxOut;
                        DCHECK_LT(xBufOffset2 + 3 * step, xBuf.Length);

                        int outOffset = NChannels() * (x + y * nxOut);
                        for (int c = 0; c < NChannels(); ++c, ++outOffset, ++xBufOffset2)
                            outBuf[outOffset] =
                                (float)std.max<Float>(0, (rsw[0] * xBuf[xBufOffset2] +
                                                          rsw[1] * xBuf[xBufOffset2 + step] +
                                                          rsw[2] * xBuf[xBufOffset2 + 2 * step] +
                                                          rsw[3] * xBuf[xBufOffset2 + 3 * step]));
                    }
                }

                // Copy resampled image pixels out into _resampledImage_
                resampledImage.CopyRectIn(outExtent, new pstd.span<float>(outBuf));
            });

            return resampledImage;
        }

        public static pstd.vector<Image> GeneratePyramid(Image image, WrapMode2D wrapMode, Allocator alloc = null)
        {
            PixelFormat origFormat = image.format;
            int nChannels = image.NChannels();
            ColorEncoding origEncoding = image.encoding;
            // Prepare _image_ for building pyramid
            if (!IsPowerOf2(image.resolution[0]) || !IsPowerOf2(image.resolution[1]))
                image = image.FloatResizeUp(
                    new Point2i(RoundUpPow2(image.resolution[0]), RoundUpPow2(image.resolution[1])),
                    wrapMode);
            else if (!Is32Bit(image.format))
                image = image.ConvertToFormat(PixelFormat.Float);
            CHECK(Is32Bit(image.format));

            // Initialize levels of pyramid from _image_
            int nLevels = 1 + Log2Int(std.max(image.resolution[0], image.resolution[1]));
            pstd.vector<Image> pyramid = new pstd.vector<Image>();
            pyramid.reserve((ulong)nLevels);
            for (int i = 0; i < nLevels - 1; ++i)
            {
                // Initialize $i+1$st level from $i$th level and copy $i$th into pyramid
                pyramid.push_back(
                    new Image(origFormat, image.resolution, image.channelNames.ToArray(), origEncoding, alloc));
                // Initialize _nextImage_ for $i+1$st level
                Point2i nextResolution = new Point2i(std.max(1, image.resolution[0] / 2),
                                                     std.max(1, image.resolution[1] / 2));
                Image nextImage = new Image(image.format, nextResolution, image.channelNames.ToArray(), origEncoding);

                // Compute offsets from pixels to the 4 pixels used for downsampling
                int[] srcDeltas = { 0, nChannels, nChannels * image.resolution[0],
                                    nChannels * (image.resolution[0] + 1) };
                if (image.resolution[0] == 1)
                {
                    srcDeltas[1] = 0;
                    srcDeltas[3] -= nChannels;
                }
                if (image.resolution[1] == 1)
                {
                    srcDeltas[2] = 0;
                    srcDeltas[3] -= nChannels * image.resolution[0];
                }

                // Downsample _image_ to create next level and update _pyramid_
                Image curImage = image;
                int level = i;
                ParallelFor(0, nextResolution[1], (long y) =>
                {
                    // Loop over pixels in scanline $y$ and downsample for the next pyramid level
                    int srcOffset = (int)curImage.PixelOffset(new Point2i(0, 2 * (int)y));
                    int nextOffset = (int)nextImage.PixelOffset(new Point2i(0, (int)y));
                    for (int x = 0; x < nextResolution[0]; ++x, srcOffset += nChannels)
                        for (int c = 0; c < nChannels; ++c, ++srcOffset, ++nextOffset)
                            nextImage.p32[nextOffset] =
                                (curImage.p32[srcOffset] + curImage.p32[srcOffset + srcDeltas[1]] +
                                 curImage.p32[srcOffset + srcDeltas[2]] +
                                 curImage.p32[srcOffset + srcDeltas[3]]) /
                                4;

                    // Copy two scanlines from _image_ out to its pyramid level
                    int yStart = 2 * (int)y;
                    int yEnd = std.min(2 * (int)y + 2, curImage.resolution[1]);
                    int offset = (int)curImage.PixelOffset(new Point2i(0, yStart));
                    int count = (yEnd - yStart) * nChannels * curImage.resolution[0];
                    pyramid[level].CopyRectIn(
                        new Bounds2i(new Point2i(0, yStart), new Point2i(curImage.resolution[0], yEnd)),
                        new pstd.span<float>(curImage.p32.data(), offset, count));
                });
                image = nextImage;
            }

            // Initialize top level of pyramid and return it
            CHECK(image.resolution[0] == 1 && image.resolution[1] == 1);
            pyramid.push_back(new Image(origFormat, new Point2i(1, 1), image.channelNames.ToArray(), origEncoding, alloc));
            pyramid[nLevels - 1].CopyRectIn(new Bounds2i(new Point2i(0, 0), new Point2i(1, 1)),
                                            new pstd.span<float>(image.p32.data(), 0, nChannels));
            return pyramid;
        }

        // Image Private Methods
        private static bool IsAllInBounds(Bounds2i extent, Image image)
        {
            return Intersect(extent, new Bounds2i(new Point2i(0, 0), image.Resolution())) == extent;
        }

        // ForExtent variant that passes a state value (e.g. a pstd.span, which a lambda cannot capture
        // since it is a ref struct) through to the operation.
        private static void ForExtent<TState>(Bounds2i extent, WrapMode2D wrapMode, Image image,
                                              TState state, Action<TState, int> op)
            where TState : allows ref struct
        {
            CHECK_LT(extent.pMin.x, extent.pMax.x);
            CHECK_LT(extent.pMin.y, extent.pMax.y);

            int nx = extent.pMax[0] - extent.pMin[0];
            int nc = image.NChannels();
            if (IsAllInBounds(extent, image))
            {
                // All in bounds
                for (int y = extent.pMin[1]; y < extent.pMax[1]; ++y)
                {
                    int offset = (int)image.PixelOffset(new Point2i(extent.pMin[0], y));
                    for (int x = 0; x < nx; ++x)
                        for (int c = 0; c < nc; ++c)
                            op(state, offset++);
                }
            }
            else
            {
                for (int y = extent.pMin[1]; y < extent.pMax[1]; ++y)
                {
                    for (int x = 0; x < nx; ++x)
                    {
                        Point2i p = new Point2i(extent.pMin[0] + x, y);
                        // FIXME: this will return false on Black wrap mode
                        CHECK(RemapPixelCoords(&p, image.Resolution(), wrapMode));
                        int offset = (int)image.PixelOffset(p);
                        for (int c = 0; c < nc; ++c)
                            op(state, offset++);
                    }
                }
            }
        }

        // Image Public Methods
        public Image(Allocator alloc = null)
        {
            p8 = new pstd.vector<uint8_t>();
            p16 = new pstd.vector<Half>();
            p32 = new pstd.vector<float>();
            channelNames = new pstd.vector<string>();
            format = PixelFormat.U256;
            resolution = new Point2i(0, 0);
        }

        public Image(pstd.vector<uint8_t> p8c, Point2i resolution, IList<string> channels, ColorEncoding encoding)
        {
            format = PixelFormat.U256;
            this.resolution = resolution;
            channelNames = new pstd.vector<string>(channels);
            this.encoding = encoding;
            p8 = p8c;
            p16 = new pstd.vector<Half>();
            p32 = new pstd.vector<float>();
            CHECK_EQ((size_t)p8.size(), (size_t)(NChannels() * resolution[0] * resolution[1]));
        }

        public Image(pstd.vector<Half> p16c, Point2i resolution, IList<string> channels)
        {
            format = PixelFormat.Half;
            this.resolution = resolution;
            channelNames = new pstd.vector<string>(channels);
            p8 = new pstd.vector<uint8_t>();
            p16 = p16c;
            p32 = new pstd.vector<float>();
            CHECK_EQ((size_t)p16.size(), (size_t)(NChannels() * resolution[0] * resolution[1]));
            CHECK(Is16Bit(format));
        }

        public Image(pstd.vector<float> p32c, Point2i resolution, IList<string> channels)
        {
            format = PixelFormat.Float;
            this.resolution = resolution;
            channelNames = new pstd.vector<string>(channels);
            p8 = new pstd.vector<uint8_t>();
            p16 = new pstd.vector<Half>();
            p32 = p32c;
            CHECK_EQ((size_t)p32.size(), (size_t)(NChannels() * resolution[0] * resolution[1]));
            CHECK(Is32Bit(format));
        }

        public Image(PixelFormat format, Point2i resolution, IList<string> channels, ColorEncoding encoding = null,
                     Allocator alloc = null)
        {
            this.format = format;
            this.resolution = resolution;
            channelNames = new pstd.vector<string>(channels);
            this.encoding = encoding;
            p8 = new pstd.vector<uint8_t>();
            p16 = new pstd.vector<Half>();
            p32 = new pstd.vector<float>();
            if (Is8Bit(format))
            {
                p8.resize((ulong)NChannels() * (ulong)resolution[0] * (ulong)resolution[1]);
                CHECK(encoding != null);
            }
            else if (Is16Bit(format))
                p16.resize((ulong)NChannels() * (ulong)resolution[0] * (ulong)resolution[1]);
            else if (Is32Bit(format))
                p32.resize((ulong)NChannels() * (ulong)resolution[0] * (ulong)resolution[1]);
            else
                LOG_FATAL("Unhandled format in Image::Image()");
        }

        public ImageChannelDesc GetChannelDesc(IList<string> requestedChannels)
        {
            ImageChannelDesc desc = new ImageChannelDesc();
            for (int i = 0; i < requestedChannels.Count; ++i)
                desc.offset.Add(0);
            for (int i = 0; i < requestedChannels.Count; ++i)
            {
                int j;
                for (j = 0; j < (int)channelNames.size(); ++j)
                    if (requestedChannels[i] == channelNames[j])
                    {
                        desc.offset[i] = j;
                        break;
                    }
                if (j == (int)channelNames.size())
                    return new ImageChannelDesc();
            }

            return desc;
        }

        public ImageChannelValues GetChannels(Point2i p, ImageChannelDesc desc, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)desc.offset.Count, (Float)0);
            if (!RemapPixelCoords(&p, resolution, wrapMode))
                return cv;

            size_t pixelOffset = PixelOffset(p);
            switch (format)
            {
                case PixelFormat.U256:
                    {
                        for (int i = 0; i < desc.offset.Count; ++i)
                            cv[i] = encoding.ToLinearByte(p8[pixelOffset + (size_t)desc.offset[i]]);
                        break;
                    }
                case PixelFormat.Half:
                    {
                        for (int i = 0; i < desc.offset.Count; ++i)
                            cv[i] = (Float)(float)p16[pixelOffset + (size_t)desc.offset[i]];
                        break;
                    }
                case PixelFormat.Float:
                    {
                        for (int i = 0; i < desc.offset.Count; ++i)
                            cv[i] = p32[pixelOffset + (size_t)desc.offset[i]];
                        break;
                    }
                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }

            return cv;
        }

        public ImageChannelValues GetChannels(Point2i p, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)NChannels(), (Float)0);
            if (!RemapPixelCoords(&p, resolution, wrapMode))
                return cv;

            size_t pixelOffset = PixelOffset(p);
            switch (format)
            {
                case PixelFormat.U256:
                    {
                        for (int i = 0; i < NChannels(); ++i)
                            cv[i] = encoding.ToLinearByte(p8[pixelOffset + (size_t)i]);
                        break;
                    }
                case PixelFormat.Half:
                    {
                        for (int i = 0; i < NChannels(); ++i)
                            cv[i] = (Float)(float)p16[pixelOffset + (size_t)i];
                        break;
                    }
                case PixelFormat.Float:
                    {
                        for (int i = 0; i < NChannels(); ++i)
                            cv[i] = p32[pixelOffset + (size_t)i];
                        break;
                    }
                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }

            return cv;
        }

        public void SetChannels(Point2i p, ImageChannelDesc desc, pstd.span<Float> values)
        {
            CHECK_LE((size_t)values.size(), (size_t)NChannels());
            for (size_t i = 0; i < values.size(); ++i)
                SetChannel(p, desc.offset[(int)i], values[(int)i]);
        }

        public void SetChannels(Point2i p, ImageChannelValues values)
        {
            CHECK_LE(values.Count, NChannels());
            int i = 0;
            foreach (Float v in values)
            {
                SetChannel(p, i, v);
                ++i;
            }
        }

        public void SetChannels(Point2i p, pstd.span<Float> values)
        {
            CHECK_LE((size_t)values.size(), (size_t)NChannels());
            for (size_t i = 0; i < values.size(); ++i)
                SetChannel(p, (int)i, values[(int)i]);
        }

        public Image ConvertToFormat(PixelFormat newFormat, ColorEncoding encoding = null)
        {
            if (newFormat == format)
                return Clone();

            Image newImage = new Image(newFormat, resolution, channelNames.ToArray(), encoding);
            for (int y = 0; y < resolution.y; ++y)
                for (int x = 0; x < resolution.x; ++x)
                    for (int c = 0; c < NChannels(); ++c)
                        newImage.SetChannel(new Point2i(x, y), c, GetChannel(new Point2i(x, y), c));
            return newImage;
        }

        public ImageChannelValues MAE(ImageChannelDesc desc, Image rref, Image errorImage = null)
        {
            double[] sumError = new double[desc.size()];

            ImageChannelDesc refDesc = rref.GetChannelDesc(ChannelNames(desc));
            CHECK((bool)refDesc);
            CHECK_EQ(Resolution(), rref.Resolution());

            if (errorImage != null)
                errorImage.CopyFrom(new Image(PixelFormat.Float, Resolution(), ChannelNames()));

            for (int y = 0; y < Resolution().y; ++y)
                for (int x = 0; x < Resolution().x; ++x)
                {
                    ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                    ImageChannelValues vref = rref.GetChannels(new Point2i(x, y), refDesc);

                    for (int c = 0; c < (int)desc.size(); ++c)
                    {
                        double error = Math.Abs((double)v[c] - (double)vref[c]);
                        if (IsInf(error))
                            continue;
                        sumError[c] += error;
                        if (errorImage != null)
                            errorImage.SetChannel(new Point2i(x, y), c, (Float)error);
                    }
                }

            ImageChannelValues errorV = new ImageChannelValues(desc.size());
            for (int c = 0; c < (int)desc.size(); ++c)
                errorV[c] = (Float)(sumError[c] / ((Float)Resolution().x * (Float)Resolution().y));
            return errorV;
        }

        public ImageChannelValues MSE(ImageChannelDesc desc, Image rref, Image mseImage = null)
        {
            double[] sumSE = new double[desc.size()];

            ImageChannelDesc refDesc = rref.GetChannelDesc(ChannelNames(desc));
            if (!refDesc)
                ErrorExit("Channels not found in image: [ " + string.Join(", ", ChannelNames(desc)) + " ]");

            CHECK_EQ(Resolution(), rref.Resolution());

            if (mseImage != null)
                mseImage.CopyFrom(new Image(PixelFormat.Float, Resolution(), ChannelNames()));

            for (int y = 0; y < Resolution().y; ++y)
                for (int x = 0; x < Resolution().x; ++x)
                {
                    ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                    ImageChannelValues vref = rref.GetChannels(new Point2i(x, y), refDesc);

                    for (int c = 0; c < (int)desc.size(); ++c)
                    {
                        double se = Sqr((double)v[c] - (double)vref[c]);
                        if (IsInf(se))
                            continue;
                        sumSE[c] += se;
                        if (mseImage != null)
                            mseImage.SetChannel(new Point2i(x, y), c, (Float)se);
                    }
                }

            ImageChannelValues mse = new ImageChannelValues(desc.size());
            for (int c = 0; c < (int)desc.size(); ++c)
                mse[c] = (Float)(sumSE[c] / ((Float)Resolution().x * (Float)Resolution().y));
            return mse;
        }

        public ImageChannelValues MRSE(ImageChannelDesc desc, Image rref, Image mrseImage = null)
        {
            double[] sumRSE = new double[desc.size()];

            ImageChannelDesc refDesc = rref.GetChannelDesc(ChannelNames(desc));
            CHECK((bool)refDesc);
            CHECK_EQ(Resolution(), rref.Resolution());

            if (mrseImage != null)
                mrseImage.CopyFrom(new Image(PixelFormat.Float, Resolution(), ChannelNames()));

            for (int y = 0; y < Resolution().y; ++y)
                for (int x = 0; x < Resolution().x; ++x)
                {
                    ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                    ImageChannelValues vref = rref.GetChannels(new Point2i(x, y), refDesc);

                    for (int c = 0; c < (int)desc.size(); ++c)
                    {
                        double rse = Sqr((double)v[c] - (double)vref[c]) / Sqr((double)vref[c] + 0.01);
                        if (IsInf(rse))
                            continue;
                        sumRSE[c] += rse;
                        if (mrseImage != null)
                            mrseImage.SetChannel(new Point2i(x, y), c, (Float)rse);
                    }
                }

            ImageChannelValues mrse = new ImageChannelValues(desc.size());
            for (int c = 0; c < (int)desc.size(); ++c)
                mrse[c] = (Float)(sumRSE[c] / ((Float)Resolution().x * (Float)Resolution().y));
            return mrse;
        }
        public List<string> ChannelNames()
        {
            return new List<string>(channelNames.ToArray());
        }

        public List<string> ChannelNames(ImageChannelDesc desc)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < (int)desc.size(); ++i)
                names.Add(channelNames[desc.offset[i]]);
            return names;
        }

        public ImageChannelValues Average(ImageChannelDesc desc)
        {
            double[] sum = new double[desc.size()];

            for (int y = 0; y < Resolution().y; ++y)
                for (int x = 0; x < Resolution().x; ++x)
                {
                    ImageChannelValues v = GetChannels(new Point2i(x, y), desc);
                    for (int c = 0; c < (int)desc.size(); ++c)
                        sum[c] += v[c];
                }

            ImageChannelValues average = new ImageChannelValues(desc.size());
            for (int c = 0; c < (int)desc.size(); ++c)
                average[c] = (Float)(sum[c] / ((Float)Resolution().x * (Float)Resolution().y));
            return average;
        }

        public void CopyRectOut(Bounds2i extent, pstd.span<float> buf, WrapMode2D wrapMode = default)
        {
            CHECK_GE((long)buf.size(), (long)(extent.Area() * NChannels()));

            int bufIter = 0;
            switch (format)
            {
                case PixelFormat.U256:
                    if (IsAllInBounds(extent, this))
                    {
                        // All in bounds
                        int count = NChannels() * (extent.pMax.x - extent.pMin.x);
                        for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
                        {
                            // Convert scanlines all at once.
                            int offset = (int)PixelOffset(new Point2i(extent.pMin.x, y));
#if PBRT_FLOAT_AS_DOUBLE
                            for (int i = 0; i < count; ++i)
                                buf[bufIter++] = (float)encoding.ToLinearByte(p8[offset + i]);
#else
                            encoding.ToLinear(new pstd.span<byte>(p8.data(), offset, count),
                                              buf.subspan((size_t)bufIter, (size_t)count));
                            bufIter += count;
#endif
                        }
                    }
                    else
                    {
                        int bi = bufIter;
                        ForExtent(extent, wrapMode, this, buf, (pstd.span<float> b, int offset) =>
                        {
                            b[bi] = (float)encoding.ToLinearByte(p8[offset]);
                            ++bi;
                        });
                    }
                    break;

                case PixelFormat.Half:
                    {
                        int bi = bufIter;
                        ForExtent(extent, wrapMode, this, buf, (pstd.span<float> b, int offset) => { b[bi++] = (float)p16[offset]; });
                    }
                    break;

                case PixelFormat.Float:
                    {
                        int bi = bufIter;
                        ForExtent(extent, wrapMode, this, buf, (pstd.span<float> b, int offset) => { b[bi++] = p32[offset]; });
                    }
                    break;

                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }
        }

        public void CopyRectIn(Bounds2i extent, pstd.span<float> buf)
        {
            CHECK_GE((long)buf.size(), (long)(extent.Area() * NChannels()));

            int bufIter = 0;
            switch (format)
            {
                case PixelFormat.U256:
                    if (IsAllInBounds(extent, this))
                    {
                        // All in bounds
                        int count = NChannels() * (extent.pMax.x - extent.pMin.x);
                        for (int y = extent.pMin.y; y < extent.pMax.y; ++y)
                        {
                            // Convert scanlines all at once.
                            int offset = (int)PixelOffset(new Point2i(extent.pMin.x, y));
#if PBRT_FLOAT_AS_DOUBLE
                            for (int i = 0; i < count; ++i)
                                p8[offset + i] = encoding.FromLinearByte(buf[bufIter++]);
#else
                            encoding.FromLinear(buf.subspan((size_t)bufIter, (size_t)count),
                                                new pstd.span<byte>(p8.data(), offset, count));
                            bufIter += count;
#endif
                        }
                    }
                    else
                    {
                        int bi = bufIter;
                        ForExtent(extent, WrapMode.Clamp, this, buf, (pstd.span<float> b, int offset) =>
                        {
                            Float v = b[bi++];
                            p8[offset] = encoding.FromLinearByte(v);
                        });
                    }
                    break;

                case PixelFormat.Half:
                    {
                        int bi = bufIter;
                        ForExtent(extent, WrapMode.Clamp, this, buf, (pstd.span<float> b, int offset) => { p16[offset] = new Half(b[bi++]); });
                    }
                    break;

                case PixelFormat.Float:
                    {
                        int bi = bufIter;
                        ForExtent(extent, WrapMode.Clamp, this, buf, (pstd.span<float> b, int offset) => { p32[offset] = b[bi++]; });
                    }
                    break;

                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }
        }

        public ImageChannelValues LookupNearest(Point2f p, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)NChannels(), (Float)0);
            for (int c = 0; c < NChannels(); ++c)
                cv[c] = LookupNearestChannel(p, c, wrapMode);
            return cv;
        }

        public ImageChannelValues LookupNearest(Point2f p, ImageChannelDesc desc, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)desc.offset.Count, (Float)0);
            for (int i = 0; i < desc.offset.Count; ++i)
                cv[i] = LookupNearestChannel(p, desc.offset[i], wrapMode);
            return cv;
        }

        public ImageChannelValues Bilerp(Point2f p, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)NChannels(), (Float)0);
            for (int c = 0; c < NChannels(); ++c)
                cv[c] = BilerpChannel(p, c, wrapMode);
            return cv;
        }

        public ImageChannelValues Bilerp(Point2f p, ImageChannelDesc desc, WrapMode2D wrapMode = default)
        {
            ImageChannelValues cv = new ImageChannelValues((size_t)desc.offset.Count, (Float)0);
            for (int i = 0; i < desc.offset.Count; ++i)
                cv[i] = BilerpChannel(p, desc.offset[i], wrapMode);
            return cv;
        }

        public void FlipY()
        {
            for (int y = 0; y < resolution.y / 2; ++y)
            {
                for (int x = 0; x < resolution.x; ++x)
                {
                    size_t o1 = PixelOffset(new Point2i(x, y)), o2 = PixelOffset(new Point2i(x, resolution.y - 1 - y));
                    for (int c = 0; c < NChannels(); ++c)
                    {
                        if (Is8Bit(format))
                        { uint8_t t = p8[o1 + (size_t)c]; p8[o1 + (size_t)c] = p8[o2 + (size_t)c]; p8[o2 + (size_t)c] = t; }
                        else if (Is16Bit(format))
                        { Half t = p16[o1 + (size_t)c]; p16[o1 + (size_t)c] = p16[o2 + (size_t)c]; p16[o2 + (size_t)c] = t; }
                        else if (Is32Bit(format))
                        { float t = p32[o1 + (size_t)c]; p32[o1 + (size_t)c] = p32[o2 + (size_t)c]; p32[o2 + (size_t)c] = t; }
                        else
                            LOG_FATAL("unexpected format");
                    }
                }
            }
        }

        public Image JointBilateralFilter(ImageChannelDesc toFilterDesc, int halfWidth, Float* xySigma,
                                          ImageChannelDesc jointDesc, ImageChannelValues jointSigma)
        {
            CHECK_EQ(jointDesc.size(), jointSigma.size());
            Image result = new Image(PixelFormat.Float, resolution, ChannelNames(toFilterDesc));

            List<Float> fx = new List<Float>(), fy = new List<Float>();
            for (int i = 0; i <= halfWidth; ++i)
            {
                fx.Add(Gaussian(i, 0, xySigma[0]));
                fy.Add(Gaussian(i, 0, xySigma[1]));
            }

            ParallelFor(0, resolution.y, (long y0, long y1) =>
            {
                for (int y = (int)y0; y < y1; ++y)
                {
                    for (int x = 0; x < resolution.x; ++x)
                    {
                        ImageChannelValues jointPixelChannels = GetChannels(new Point2i(x, y), jointDesc);
                        ImageChannelValues filteredSum = new ImageChannelValues(toFilterDesc.size(), (Float)0);
                        Float weightSum = 0;

                        for (int dy = -halfWidth + 1; dy < halfWidth; ++dy)
                        {
                            if (y + dy < 0 || y + dy >= resolution.y)
                                continue;
                            for (int dx = -halfWidth + 1; dx < halfWidth; ++dx)
                            {
                                for (int dx2 = -halfWidth + 1; dx2 < halfWidth; ++dx2)
                                {
                                    if (x + dx2 < 0 || x + dx2 >= resolution.x)
                                        continue;
                                    ImageChannelValues jointOtherChannels =
                                        GetChannels(new Point2i(x + dx2, y + dy), jointDesc);
                                    Float weight = fx[Math.Abs(dx2)] * fy[Math.Abs(dy)];
                                    for (int c = 0; c < (int)jointDesc.size(); ++c)
                                        weight *= Gaussian(jointPixelChannels[c],
                                                           jointOtherChannels[c], jointSigma[c]);
                                    weightSum += weight;

                                    ImageChannelValues filterChannels =
                                        GetChannels(new Point2i(x + dx2, y + dy), toFilterDesc);
                                    for (int c = 0; c < filterChannels.Count; ++c)
                                        filteredSum[c] += weight * filterChannels[c];
                                }
                            }
                        }
                        if (weightSum > 0)
                            for (int c = 0; c < filteredSum.Count; ++c)
                                filteredSum[c] /= weightSum;
                        result.SetChannels(new Point2i(x, y), filteredSum);
                    }
                }
            });

            return result;
        }

        // ImageIO
        public static ImageAndMetadata Read(string name, Allocator alloc = null, ColorEncoding encoding = null)
        {
            if (HasExtension(name, "exr"))
                return ReadEXR(name, alloc);
            else if (HasExtension(name, "png"))
                return ReadPNG(name, alloc, encoding);
            else if (HasExtension(name, "pfm"))
                return ReadPFM(name, alloc);
            else if (HasExtension(name, "hdr"))
                return ReadHDR(name, alloc);
            else if (HasExtension(name, "qoi"))
                return ReadQOI(name, alloc);
            else
            {
                int x, y, n;
                byte[] data = stb_image.stbi_load(name, out x, out y, out n, 0);
                if (data != null)
                {
                    pstd.vector<uint8_t> pixels = new pstd.vector<uint8_t>(data);
                    stb_image.stbi_image_free(data);
                    switch (n)
                    {
                        case 1:
                            return new ImageAndMetadata(
                                new Image(pixels, new Point2i(x, y), new string[] { "Y" }, ColorEncoding.sRGB),
                                new ImageMetadata());
                        case 2:
                            {
                                Image image = new Image(pixels, new Point2i(x, y), new string[] { "Y", "A" }, ColorEncoding.sRGB);
                                return new ImageAndMetadata(image.SelectChannels(image.GetChannelDesc(new string[] { "Y" })),
                                                            new ImageMetadata());
                            }
                        case 3:
                            return new ImageAndMetadata(
                                new Image(pixels, new Point2i(x, y), new string[] { "R", "G", "B" }, ColorEncoding.sRGB),
                                new ImageMetadata());
                        case 4:
                            {
                                Image image = new Image(pixels, new Point2i(x, y), new string[] { "R", "G", "B", "A" },
                                                        ColorEncoding.sRGB);
                                return new ImageAndMetadata(
                                    image.SelectChannels(image.GetChannelDesc(new string[] { "R", "G", "B" })),
                                    new ImageMetadata());
                            }
                        default:
                            ErrorExit($"{name}: {n} channel image unsupported.");
                            return default;
                    }
                }
                else
                {
                    ErrorExit($"{name}: no support for reading images with this extension");
                    return default;
                }
            }
        }

        public bool Write(string name, ImageMetadata metadata = null)
        {
            if (metadata == null)
                metadata = new ImageMetadata();
            if (metadata.pixelBounds)
                CHECK_EQ((size_t)metadata.pixelBounds.value().Area(), (size_t)resolution.x * (size_t)resolution.y);

            if (HasExtension(name, "exr"))
                return WriteEXR(name, metadata);

            // This copy will sometimes be wasteful but simplifies logic below...
            Image outImage = Clone();

            switch (NChannels())
            {
                case 1:
                    // all good; onward
                    break;
                case 2:
                    Error($"{name}: unable to write a 2 channel image in this format.");
                    return false;
                case 3:
                    {
                        ImageChannelDesc desc = outImage.GetChannelDesc(new string[] { "R", "G", "B" });
                        if (!desc)
                            // Still go for it with 3 channels.
                            Warning($"{name}: image has 3 channels but they are not R, G, and B. Image may be " +
                                    "garbled.");
                        else
                            // Reorder them as RGB.
                            outImage = outImage.SelectChannels(desc);
                        break;
                    }
                case 4:
                    {
                        ImageChannelDesc desc = outImage.GetChannelDesc(new string[] { "R", "G", "B", "A" });
                        if (!desc)
                            // Still go for it.
                            Warning($"{name}: image has 4 channels but they are not R, G, B, and A. Image may be " +
                                    "garbled.");
                        else
                            // Reorder them as RGBA.
                            outImage = outImage.SelectChannels(desc);
                        break;
                    }
                default:
                    {
                        ImageChannelDesc desc = outImage.GetChannelDesc(new string[] { "R", "G", "B" });
                        if (!desc)
                        {
                            Error($"{name}: multi-channel image does not have R, G, and B. Unable to write to " +
                                  "this format.");
                            return false;
                        }
                        else
                        {
                            string err = $"{name}: ignoring additional channels for this image format: ";
                            foreach (string ch in outImage.ChannelNames())
                                if (ch != "R" && ch != "G" && ch != "B")
                                    err += $"\"{ch}\" ";
                            Error(err);
                        }

                        outImage = outImage.SelectChannels(desc);
                        break;
                    }
            }
            CHECK(outImage.NChannels() == 1 || outImage.NChannels() == 3 ||
                  outImage.NChannels() == 4);

            ImageMetadata outMetadata = metadata.Clone();
            if (outImage.NChannels() != 1 && metadata.GetColorSpace() != RGBColorSpace.sRGB)
            {
                Warning($"{name}: converting pixel colors to sRGB to match output image format.");
                SquareMatrix3 m =
                    ConvertRGBColorSpace(metadata.GetColorSpace(), RGBColorSpace.sRGB);
                ImageChannelDesc rgbDesc = outImage.GetChannelDesc(new string[] { "R", "G", "B" });
                // May not have RGB for weird non-RGB 3 channel images...
                if (rgbDesc)
                {
                    for (int y = 0; y < outImage.Resolution().y; ++y)
                        for (int x = 0; x < outImage.Resolution().x; ++x)
                        {
                            ImageChannelValues channels = outImage.GetChannels(new Point2i(x, y), rgbDesc);
                            RGB rgb = m * new RGB(channels[0], channels[1], channels[2]);
                            outImage.SetChannels(new Point2i(x, y), rgbDesc,
                                                 new pstd.span<Float>(new Float[] { rgb.r, rgb.g, rgb.b }));
                        }
                    outMetadata.colorSpace = RGBColorSpace.sRGB;
                }
            }

            if (HasExtension(name, "pfm"))
                return outImage.WritePFM(name, outMetadata);
            else if (HasExtension(name, "png"))
                return outImage.WritePNG(name, outMetadata);
            else if (HasExtension(name, "qoi"))
                return outImage.WriteQOI(name, outMetadata);
            else
            {
                Error($"{name}: no support for writing images with this extension");
                return false;
            }
        }

        private bool WriteEXR(string name, ImageMetadata metadata)
        {
            if (Is8Bit(format))
                return ConvertToFormat(PixelFormat.Half).WriteEXR(name, metadata);
            CHECK(Is16Bit(format) || Is32Bit(format));

            try
            {
                Imath.Box2i displayWindow, dataWindow;
                if (metadata.fullResolution)
                    // Agan, -1 offsets to handle inclusive indexing in OpenEXR...
                    displayWindow = new Imath.Box2i(new Imath.V2i(0, 0),
                                                    new Imath.V2i(metadata.fullResolution.value().x - 1,
                                                                  metadata.fullResolution.value().y - 1));
                else
                    displayWindow = new Imath.Box2i(new Imath.V2i(0, 0),
                                                    new Imath.V2i(resolution.x - 1, resolution.y - 1));

                if (metadata.pixelBounds)
                    dataWindow = new Imath.Box2i(
                        new Imath.V2i(metadata.pixelBounds.value().pMin.x, metadata.pixelBounds.value().pMin.y),
                        new Imath.V2i(metadata.pixelBounds.value().pMax.x - 1,
                                      metadata.pixelBounds.value().pMax.y - 1));
                else
                    dataWindow = new Imath.Box2i(new Imath.V2i(0, 0),
                                                 new Imath.V2i(resolution.x - 1, resolution.y - 1));

                byte[] bytes = RawBytes();
                Imf.FrameBuffer fb = imageToFrameBuffer(this, AllChannelsDesc(), dataWindow, bytes);

                Imf.Header header = new Imf.Header(displayWindow, dataWindow);
                foreach (KeyValuePair<string, Imf.Slice> iter in fb.slices)
                    header.channels().insert(iter.Key, iter.Value.type);

                if (metadata.renderTimeSeconds)
                    header.insert("renderTimeSeconds",
                                  new Imf.FloatAttribute(metadata.renderTimeSeconds.value()));
                if (metadata.cameraFromWorld)
                {
                    float[] m = new float[16];
                    for (int i = 0; i < 4; ++i)
                        for (int j = 0; j < 4; ++j)
                            m[4 * i + j] = (float)metadata.cameraFromWorld.value()[i][j];
                    header.insert("worldToCamera", new Imf.M44fAttribute(m));
                }
                if (metadata.NDCFromWorld)
                {
                    float[] m = new float[16];
                    for (int i = 0; i < 4; ++i)
                        for (int j = 0; j < 4; ++j)
                            m[4 * i + j] = (float)metadata.NDCFromWorld.value()[i][j];
                    header.insert("worldToNDC", new Imf.M44fAttribute(m));
                }
                if (metadata.samplesPerPixel)
                    header.insert("samplesPerPixel",
                                  new Imf.IntAttribute(metadata.samplesPerPixel.value()));
                if (metadata.MSE)
                    header.insert("MSE", new Imf.FloatAttribute(metadata.MSE.value()));
                foreach (KeyValuePair<string, string> iter in metadata.strings)
                    header.insert(iter.Key, new Imf.StringAttribute(iter.Value));
                foreach (KeyValuePair<string, List<string>> iter in metadata.stringVectors)
                    header.insert(iter.Key, new Imf.StringVectorAttribute(iter.Value));

                // The OpenEXR spec says that the default is sRGB if no
                // chromaticities are provided.  It should be innocuous to write
                // the sRGB primaries anyway, but for completely indecipherable
                // reasons, OSX's Preview.app decides to gamma correct the pixels
                // in EXR files if it finds primaries.  So, we don't write them in
                // that case in the interests of nicer looking images on the
                // screen.
                if (metadata.GetColorSpace() != RGBColorSpace.sRGB)
                {
                    RGBColorSpace cs = metadata.GetColorSpace();
                    Imf.Chromaticities chromaticities = new Imf.Chromaticities(
                        new Imath.V2f(cs.r.x, cs.r.y), new Imath.V2f(cs.g.x, cs.g.y),
                        new Imath.V2f(cs.b.x, cs.b.y), new Imath.V2f(cs.w.x, cs.w.y));
                    header.insert("chromaticities", new Imf.ChromaticitiesAttribute(chromaticities));
                }

                Imf.OutputFile file = new Imf.OutputFile(name, header);
                file.setFrameBuffer(fb);
                file.writePixels(resolution.y);
            }
            catch (Exception exc)
            {
                Error($"{name}: error writing EXR: {exc.Message}");
                return false;
            }

            return true;
        }

        public Image SelectChannels(ImageChannelDesc desc, Allocator alloc = null)
        {
            List<string> descChannelNames = new List<string>();
            // TODO: descChannelNames = ChannelNames(desc)
            for (int i = 0; i < desc.offset.Count; ++i)
                descChannelNames.Add(channelNames[desc.offset[i]]);

            Image image = new Image(format, resolution, descChannelNames, encoding, alloc);
            int nSrc = NChannels(), nDst = desc.offset.Count;
            switch (format)
            {
                case PixelFormat.U256:
                    for (int y = 0; y < resolution.y; ++y)
                        for (int x = 0; x < resolution.x; ++x)
                        {
                            int src = (int)PixelOffset(new Point2i(x, y));
                            int dst = (int)image.PixelOffset(new Point2i(x, y));
                            for (int i = 0; i < nDst; ++i)
                                image.p8[dst + i] = p8[src + desc.offset[i]];
                        }
                    break;
                case PixelFormat.Half:
                    for (int y = 0; y < resolution.y; ++y)
                        for (int x = 0; x < resolution.x; ++x)
                        {
                            int src = (int)PixelOffset(new Point2i(x, y));
                            int dst = (int)image.PixelOffset(new Point2i(x, y));
                            for (int i = 0; i < nDst; ++i)
                                image.p16[dst + i] = p16[src + desc.offset[i]];
                        }
                    break;
                case PixelFormat.Float:
                    for (int y = 0; y < resolution.y; ++y)
                        for (int x = 0; x < resolution.x; ++x)
                        {
                            int src = (int)PixelOffset(new Point2i(x, y));
                            int dst = (int)image.PixelOffset(new Point2i(x, y));
                            for (int i = 0; i < nDst; ++i)
                                image.p32[dst + i] = p32[src + desc.offset[i]];
                        }
                    break;
                default:
                    LOG_FATAL("Unhandled PixelFormat");
                    break;
            }

            return image;
        }

        public Image Crop(Bounds2i bounds, Allocator alloc = null)
        {
            CHECK_GT(bounds.Area(), 0);
            CHECK(bounds.pMin.x >= 0 && bounds.pMin.y >= 0);
            Image image = new Image(format, new Point2i(bounds.pMax.x - bounds.pMin.x, bounds.pMax.y - bounds.pMin.y), channelNames.ToArray(), encoding,
                                    alloc);
            foreach (Point2i p in bounds)
                for (int c = 0; c < NChannels(); ++c)
                    image.SetChannel(new Point2i(p.x - bounds.pMin.x, p.y - bounds.pMin.y), c, GetChannel(p, c));
            return image;
        }

        public override string ToString()
        {
            return "[ Image format: " + pbrt.ToString(format) + " resolution: " + resolution +
                   " channelNames: [ " + string.Join(", ", channelNames.ToArray()) + " ] encoding: " +
                   (encoding != null ? encoding.ToString() : "(nullptr)") + " ]";
        }

        ///////////////////////////////////////////////////////////////////////////
        // OpenEXR
        private static Imf.FrameBuffer imageToFrameBuffer(Image image, ImageChannelDesc desc, Imath.Box2i dataWindow,
                                                          byte[] bytes)
        {
            long xStride = image.NChannels() * TexelBytes(image.Format());
            long yStride = image.Resolution().x * xStride;
            // Would be nice to use PixelOffset(-dw.min.x, -dw.min.y) but
            // it checks to make sure the coordinates are >= 0 (which
            // usually makes sense...)
            long originPtr = -dataWindow.min.x * xStride - dataWindow.min.y * yStride;

            Imf.FrameBuffer fb = new Imf.FrameBuffer();
            List<string> channelNames = image.ChannelNames();
            switch (image.Format())
            {
                case PixelFormat.Half:
                    foreach (int channelIndex in desc.offset)
                        fb.insert(channelNames[channelIndex],
                                  new Imf.Slice(Imf.PixelType.HALF, bytes, originPtr + channelIndex * 2, xStride, yStride));
                    break;
                case PixelFormat.Float:
                    foreach (int channelIndex in desc.offset)
                        fb.insert(channelNames[channelIndex],
                                  new Imf.Slice(Imf.PixelType.FLOAT, bytes, originPtr + channelIndex * 4, xStride, yStride));
                    break;
                default:
                    LOG_FATAL("Unexpected image format");
                    break;
            }
            return fb;
        }

        private byte[] RawBytes()
        {
            if (Is16Bit(format))
                return MemoryMarshal.AsBytes(new Span<Half>(p16.data(), 0, (int)p16.size())).ToArray();
            CHECK(Is32Bit(format));
            return MemoryMarshal.AsBytes(new Span<float>(p32.data(), 0, (int)p32.size())).ToArray();
        }

        private void SetRawBytes(byte[] bytes)
        {
            if (Is16Bit(format))
                MemoryMarshal.Cast<byte, Half>(new Span<byte>(bytes)).CopyTo(new Span<Half>(p16.data(), 0, (int)p16.size()));
            else
                MemoryMarshal.Cast<byte, float>(new Span<byte>(bytes)).CopyTo(new Span<float>(p32.data(), 0, (int)p32.size()));
        }

        private static ImageAndMetadata ReadEXR(string name, Allocator alloc)
        {
            try
            {
                Imf.InputFile file = new Imf.InputFile(name);
                Imath.Box2i dw = file.header().dataWindow;

                ImageMetadata metadata = new ImageMetadata();
                Imf.FloatAttribute renderTimeAttrib = file.header().findTypedAttribute<Imf.FloatAttribute>("renderTimeSeconds");
                if (renderTimeAttrib != null)
                    metadata.renderTimeSeconds = renderTimeAttrib.value();

                Imf.M44fAttribute worldToCameraAttrib = file.header().findTypedAttribute<Imf.M44fAttribute>("worldToCamera");
                if (worldToCameraAttrib != null)
                {
                    SquareMatrix4 m = new SquareMatrix4();
                    for (int i = 0; i < 4; ++i)
                        for (int j = 0; j < 4; ++j)
                            // Can't memcpy since Float may be a double...
                            m[i][j] = worldToCameraAttrib.getValue()[4 * i + j];
                    metadata.cameraFromWorld = m;
                }

                Imf.M44fAttribute worldToNDCAttrib = file.header().findTypedAttribute<Imf.M44fAttribute>("worldToNDC");
                if (worldToNDCAttrib != null)
                {
                    SquareMatrix4 m = new SquareMatrix4();
                    for (int i = 0; i < 4; ++i)
                        for (int j = 0; j < 4; ++j)
                            m[i][j] = worldToNDCAttrib.getValue()[4 * i + j];
                    metadata.NDCFromWorld = m;
                }

                // OpenEXR uses inclusive pixel bounds; adjust to non-inclusive
                // (the convention pbrt uses) in the values returned.
                metadata.pixelBounds = new Bounds2i(new Point2i(dw.min.x, dw.min.y), new Point2i(dw.max.x + 1, dw.max.y + 1));

                Imath.Box2i dispw = file.header().displayWindow;
                metadata.fullResolution =
                    new Point2i(dispw.max.x - dispw.min.x + 1, dispw.max.y - dispw.min.y + 1);

                Imf.IntAttribute sppAttrib = file.header().findTypedAttribute<Imf.IntAttribute>("samplesPerPixel");
                if (sppAttrib != null)
                    metadata.samplesPerPixel = sppAttrib.value();

                Imf.FloatAttribute mseAttrib = file.header().findTypedAttribute<Imf.FloatAttribute>("MSE");
                if (mseAttrib != null)
                    metadata.MSE = mseAttrib.value();

                // Find any string or string vector attributes
                foreach (KeyValuePair<string, Imf.Attribute> iter in file.header().attributes)
                {
                    if (iter.Value.typeName() == "string")
                    {
                        Imf.StringAttribute sv = (Imf.StringAttribute)iter.Value;
                        metadata.strings[iter.Key] = sv.value();
                    }
                    if (iter.Value.typeName() == "stringvector")
                    {
                        Imf.StringVectorAttribute sv = (Imf.StringVectorAttribute)iter.Value;
                        metadata.stringVectors[iter.Key] = sv.value();
                    }
                }

                // Figure out the color space
                Imf.ChromaticitiesAttribute chromaticitiesAttrib =
                    file.header().findTypedAttribute<Imf.ChromaticitiesAttribute>("chromaticities");
                if (chromaticitiesAttrib != null)
                {
                    Imf.Chromaticities c = chromaticitiesAttrib.value();
                    RGBColorSpace cs = RGBColorSpace.Lookup(
                        new Point2f(c.red.x, c.red.y), new Point2f(c.green.x, c.green.y),
                        new Point2f(c.blue.x, c.blue.y), new Point2f(c.white.x, c.white.y));
                    if (cs == null)
                    {
                        Warning("Couldn't find supported color space that matches " +
                                "chromaticities: " +
                                $"r ({c.red.x:F6}, {c.red.y:F6}) g ({c.green.x:F6}, {c.green.y:F6}) b ({c.blue.x:F6}, {c.blue.y:F6}), " +
                                $"w ({c.white.x:F6}, {c.white.y:F6}). Using sRGB.");
                        metadata.colorSpace = RGBColorSpace.sRGB;
                    }
                    else
                        metadata.colorSpace = cs;
                }

                int width = dw.max.x - dw.min.x + 1;
                int height = dw.max.y - dw.min.y + 1;

                List<string> channelNames = new List<string>();
                int nChannels = 0;
                Imf.PixelType pixelType = Imf.PixelType.HALF;
                foreach (KeyValuePair<string, Imf.Channel> iter in file.header().channels().channels)
                {
                    if (nChannels++ == 0)
                        pixelType = iter.Value.type;
                    else
                    {
                        // TODO: someday handle mixed types but seems like a
                        // bother...
                        if (pixelType != iter.Value.type)
                            LOG_FATAL("ReadEXR() doesn't currently support images with " +
                                      "multiple channel types.");
                    }
                    channelNames.Add(iter.Key);
                }

                CHECK(pixelType == Imf.PixelType.HALF || pixelType == Imf.PixelType.FLOAT);
                Image image = new Image(pixelType == Imf.PixelType.HALF ? PixelFormat.Half : PixelFormat.Float,
                                        new Point2i(width, height), channelNames, null, alloc);
                byte[] bytes = image.RawBytes();
                file.setFrameBuffer(imageToFrameBuffer(image, image.AllChannelsDesc(), dw, bytes));
                file.readPixels(dw.min.y, dw.max.y);
                image.SetRawBytes(bytes);

                LOG_VERBOSE($"Read EXR image {name} ({width} x {height})");
                return new ImageAndMetadata(image, metadata);
            }
            catch (Exception e)
            {
                ErrorExit($"Unable to read image file \"{name}\": {e.Message}");
            }

            return default;
        }

        // Quantization for 8-bit output formats
        private byte[] QuantizePixelsToU256(int* nOutOfGamut)
        {
            byte[] u256 = new byte[(size_t)NChannels() * (size_t)resolution.x * (size_t)resolution.y];
            for (int y = 0; y < resolution.y; ++y)
                for (int x = 0; x < resolution.x; ++x)
                    for (int c = 0; c < NChannels(); ++c)
                    {
                        Float dither = -.5f + BlueNoise(c, new Point2i(x, y));
                        Float v = GetChannel(new Point2i(x, y), c);
                        if (v < 0 || v > 1)
                            ++(*nOutOfGamut);
                        u256[NChannels() * (y * resolution.x + x) + c] = LinearToSRGB8(v, dither);
                    }
            return u256;
        }

        ///////////////////////////////////////////////////////////////////////////
        // PNG Function Definitions
        private static ImageAndMetadata ReadPNG(string name, Allocator alloc, ColorEncoding encoding)
        {
            byte[] contents = ReadFileBytes(name);

            if (encoding == null)
                encoding = ColorEncoding.sRGB;

            uint width, height;
            LodePNGState state = new LodePNGState();
            lodepng.lodepng_state_init(state);
            uint error = lodepng.lodepng_inspect(out width, out height, state, contents, (size_t)contents.Length);
            if (error != 0)
                ErrorExit($"{name}: {lodepng.lodepng_error_text(error)}");

            Image image = new Image(alloc);
            switch (state.info_png.color.colortype)
            {
                case LodePNGColorType.LCT_GREY:
                case LodePNGColorType.LCT_GREY_ALPHA:
                    {
                        byte[] buf;
                        uint bpp = state.info_png.color.bitdepth == 16 ? 16u : 8u;
                        error = lodepng.lodepng_decode_memory(out buf, out width, out height, contents,
                                                              (size_t)contents.Length, LodePNGColorType.LCT_GREY, bpp);
                        if (error != 0)
                            ErrorExit($"{name}: {lodepng.lodepng_error_text(error)}");

                        if (state.info_png.color.bitdepth == 16)
                        {
                            image = new Image(PixelFormat.Half, new Point2i((int)width, (int)height), new string[] { "Y" });
                            int bufIter = 0;
                            for (uint y = 0; y < height; ++y)
                                for (uint x = 0; x < width; ++x, bufIter += 2)
                                {
                                    // Convert from little endian.
                                    Float v = (((int)buf[bufIter] << 8) + (int)buf[bufIter + 1]) / 65535.0f;
                                    v = encoding.ToFloatLinear(v);
                                    image.SetChannel(new Point2i((int)x, (int)y), 0, v);
                                }
                            DCHECK(bufIter == buf.Length);
                        }
                        else
                        {
                            image = new Image(PixelFormat.U256, new Point2i((int)width, (int)height), new string[] { "Y" }, encoding);
                            Array.Copy(buf, image.p8.data(), buf.Length);
                        }
                        return new ImageAndMetadata(image, new ImageMetadata());
                    }
                default:
                    {
                        byte[] buf;
                        uint bpp = state.info_png.color.bitdepth == 16 ? 16u : 8u;
                        bool hasAlpha = (state.info_png.color.colortype == LodePNGColorType.LCT_RGBA);
                        // Force RGB if it's paletted or whatever.
                        error = lodepng.lodepng_decode_memory(out buf, out width, out height, contents,
                                                              (size_t)contents.Length,
                                                              hasAlpha ? LodePNGColorType.LCT_RGBA : LodePNGColorType.LCT_RGB, bpp);
                        if (error != 0)
                            ErrorExit($"{name}: {lodepng.lodepng_error_text(error)}");

                        ImageMetadata metadata = new ImageMetadata();
                        metadata.colorSpace = RGBColorSpace.sRGB;
                        if (state.info_png.color.bitdepth == 16)
                        {
                            if (hasAlpha)
                            {
                                image = new Image(PixelFormat.Half, new Point2i((int)width, (int)height),
                                                  new string[] { "R", "G", "B", "A" });
                                int bufIter = 0;
                                for (uint y = 0; y < height; ++y)
                                    for (uint x = 0; x < width; ++x, bufIter += 8)
                                    {
                                        DCHECK(bufIter < buf.Length);
                                        // Convert from little endian.
                                        Float[] rgba = {
                                            (((int)buf[bufIter] << 8) + (int)buf[bufIter + 1]) / 65535.0f,
                                            (((int)buf[bufIter + 2] << 8) + (int)buf[bufIter + 3]) / 65535.0f,
                                            (((int)buf[bufIter + 4] << 8) + (int)buf[bufIter + 5]) / 65535.0f,
                                            (((int)buf[bufIter + 6] << 8) + (int)buf[bufIter + 7]) / 65535.0f };
                                        for (int c = 0; c < 4; ++c)
                                        {
                                            rgba[c] = encoding.ToFloatLinear(rgba[c]);
                                            image.SetChannel(new Point2i((int)x, (int)y), c, rgba[c]);
                                        }
                                    }
                                DCHECK(bufIter == buf.Length);
                            }
                            else
                            {
                                image = new Image(PixelFormat.Half, new Point2i((int)width, (int)height),
                                                  new string[] { "R", "G", "B" });
                                int bufIter = 0;
                                for (uint y = 0; y < height; ++y)
                                    for (uint x = 0; x < width; ++x, bufIter += 6)
                                    {
                                        DCHECK(bufIter < buf.Length);
                                        // Convert from little endian.
                                        Float[] rgb = {
                                            (((int)buf[bufIter] << 8) + (int)buf[bufIter + 1]) / 65535.0f,
                                            (((int)buf[bufIter + 2] << 8) + (int)buf[bufIter + 3]) / 65535.0f,
                                            (((int)buf[bufIter + 4] << 8) + (int)buf[bufIter + 5]) / 65535.0f };
                                        for (int c = 0; c < 3; ++c)
                                        {
                                            rgb[c] = encoding.ToFloatLinear(rgb[c]);
                                            image.SetChannel(new Point2i((int)x, (int)y), c, rgb[c]);
                                        }
                                    }
                                DCHECK(bufIter == buf.Length);
                            }
                        }
                        else if (hasAlpha)
                        {
                            image = new Image(PixelFormat.U256, new Point2i((int)width, (int)height),
                                              new string[] { "R", "G", "B", "A" }, encoding);
                            Array.Copy(buf, image.p8.data(), buf.Length);
                        }
                        else
                        {
                            image = new Image(PixelFormat.U256, new Point2i((int)width, (int)height),
                                              new string[] { "R", "G", "B" }, encoding);
                            Array.Copy(buf, image.p8.data(), buf.Length);
                        }
                        return new ImageAndMetadata(image, metadata);
                    }
            }
        }

        private bool WritePNG(string filename, ImageMetadata metadata)
        {
            uint error = 0;
            int nOutOfGamut = 0;

            byte[] png;
            size_t pngSize;

            LodePNGColorType pngColor = LodePNGColorType.LCT_GREY;
            switch (NChannels())
            {
                case 1:
                    pngColor = LodePNGColorType.LCT_GREY;
                    break;
                case 3:
                    // TODO: it would be nice to store the color encoding used in the PNG metadata...
                    pngColor = LodePNGColorType.LCT_RGB;
                    break;
                case 4:
                    pngColor = LodePNGColorType.LCT_RGBA;
                    break;
                default:
                    LOG_FATAL("Unexpected number of channels in WritePNG()");
                    break;
            }

            if (format == PixelFormat.U256)
            {
                error = lodepng.lodepng_encode_memory(out png, out pngSize, p8.data(), (uint)resolution.x,
                                                      (uint)resolution.y, pngColor, 8 /* bitdepth */);
            }
            else
            {
                byte[] pix8 = QuantizePixelsToU256(&nOutOfGamut);
                error = lodepng.lodepng_encode_memory(out png, out pngSize, pix8, (uint)resolution.x,
                                                      (uint)resolution.y, pngColor, 8 /* bitdepth */);
            }

            if (nOutOfGamut > 0)
                Warning($"{filename}: {nOutOfGamut} out of gamut pixel channels clamped to [0,1].");

            if (error == 0)
            {
                try { File.WriteAllBytes(filename, png); }
                catch (Exception)
                {
                    Error($"{filename}: error writing PNG.");
                    return false;
                }
            }
            else
            {
                Error($"{filename}: {lodepng.lodepng_error_text(error)}");
                return false;
            }

            return true;
        }

        private bool WriteQOI(string filename, ImageMetadata metadata)
        {
            Image image = this;
            byte[] qoiPixels = null;

            qoi.qoi_desc desc = new qoi.qoi_desc();
            desc.width = (uint)resolution.x;
            desc.height = (uint)resolution.y;
            desc.channels = NChannels();
            if (Encoding() != null && !(Encoding() is LinearColorEncoding) &&
                !(Encoding() is sRGBColorEncoding))
            {
                Error($"{Encoding().ToString()}: only linear and sRGB encodings are supported by QOI.");  // filename);
                return false;
            }
            desc.colorspace = (Encoding() is LinearColorEncoding) ? qoi.QOI_LINEAR : qoi.QOI_SRGB;

            if (NChannels() == 4)
            {
                // Try to order it as QOI expects. Though continue on if we don't
                // find these particular channel names.a
                ImageChannelDesc cdesc = GetChannelDesc(new string[] { "R", "G", "B", "A" });
                if (cdesc)
                    image = SelectChannels(cdesc);
            }
            else if (NChannels() == 3)
            {
                // Similarly try to get the channels in order..
                ImageChannelDesc cdesc = GetChannelDesc(new string[] { "R", "G", "B" });
                if (cdesc)
                    image = SelectChannels(cdesc);
            }
            else
            {
                Error($"{filename}: only 3 and 4 channel images are supported for QOI");
                return false;
            }

            if (format == PixelFormat.U256)
                qoiPixels = qoi.qoi_encode(image.p8.data(), desc);
            else
            {
                int nOutOfGamut = 0;
                byte[] rgba8 = QuantizePixelsToU256(&nOutOfGamut);
                if (nOutOfGamut > 0)
                    Warning($"{filename}: {nOutOfGamut} out of gamut pixel channels clamped to [0,1].");

                qoiPixels = qoi.qoi_encode(rgba8, desc);
            }

            bool success = true;
            try { File.WriteAllBytes(filename, qoiPixels); }
            catch (Exception) { success = false; }
            if (!success)
                Error($"{filename}: error writing QOI file.");

            return success;
        }

        ///////////////////////////////////////////////////////////////////////////
        // PFM Function Definitions

        /*
         * PFM reader/writer code courtesy Jiawen "Kevin" Chen
         * (http://people.csail.mit.edu/jiawen/)
         */

        private static readonly bool hostLittleEndian = BitConverter.IsLittleEndian;

        private const int BUFFER_SIZE = 80;

        private static int isWhitespace(int c)
        {
            return (c == ' ' || c == '\n' || c == '\t') ? 1 : 0;
        }

        // Reads a "word" from the fp and puts it into buffer and adds a null
        // terminator.  i.e. it keeps reading until whitespace is reached.  Returns
        // the number of characters read *not* including the whitespace, and
        // returns -1 on an error.
        private static int readWord(FileStream fp, out string buffer, int bufferLength)
        {
            buffer = "";
            int n;
            int c;

            if (bufferLength < 1)
                return -1;

            n = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            c = fp.ReadByte();
            while (c != -1 && (isWhitespace(c) == 0) && n < bufferLength)
            {
                sb.Append((char)c);
                ++n;
                c = fp.ReadByte();
            }

            if (n < bufferLength)
            {
                buffer = sb.ToString();
                return n;
            }

            return -1;
        }

        private static ImageAndMetadata ReadPFM(string filename, Allocator alloc)
        {
            pstd.vector<float> rgb32 = new pstd.vector<float>();
            string buffer;
            size_t nFloats;
            int nChannels, width, height;
            float scale;
            bool fileLittleEndian;
            ImageMetadata metadata = new ImageMetadata();

            FileStream fp = FOpenRead(filename);
            if (fp == null)
                ErrorExit($"{filename}: unable to open PFM file");

            // read either "Pf" or "PF"
            if (readWord(fp, out buffer, BUFFER_SIZE) == -1)
                ErrorExit($"{filename}: unable to read PFM file");

            if (buffer == "Pf")
                nChannels = 1;
            else if (buffer == "PF")
                nChannels = 3;
            else
            {
                ErrorExit($"{filename}: unable to decode PFM file type \"{buffer}\"");
                return default;
            }

            // read the rest of the header
            // read width
            if (readWord(fp, out buffer, BUFFER_SIZE) == -1)
                goto fail;
            if (!Atoi(buffer, &width))
                ErrorExit($"{filename}: unable to decode width \"{buffer}\"");

            // read height
            if (readWord(fp, out buffer, BUFFER_SIZE) == -1)
                goto fail;
            if (!Atoi(buffer, &height))
                ErrorExit($"{filename}: unable to decode height \"{buffer}\"");

            // read scale
            if (readWord(fp, out buffer, BUFFER_SIZE) == -1)
                goto fail;
            if (!Atof(buffer, &scale))
                ErrorExit($"{filename}: unable to decode scale \"{buffer}\"");

            // read the data
            nFloats = (size_t)nChannels * (size_t)width * (size_t)height;
            rgb32.resize(nFloats);
            {
                byte[] rowBytes = new byte[sizeof(float) * nChannels * width];
                for (int y = height - 1; y >= 0; --y)
                {
                    int got = 0;
                    while (got < rowBytes.Length)
                    {
                        int r = fp.Read(rowBytes, got, rowBytes.Length - got);
                        if (r <= 0) break;
                        got += r;
                    }
                    if (got != rowBytes.Length)
                        goto fail;
                    Buffer.BlockCopy(rowBytes, 0, rgb32.data(), sizeof(float) * nChannels * y * width, rowBytes.Length);
                }
            }

            // apply endian conversian and scale if appropriate
            fileLittleEndian = (scale < 0.0f);
            if (hostLittleEndian ^ fileLittleEndian)
            {
                byte[] bytes = new byte[4];
                for (uint i = 0; i < nFloats; ++i)
                {
                    Buffer.BlockCopy(rgb32.data(), 4 * (int)i, bytes, 0, 4);
                    byte t0 = bytes[0]; bytes[0] = bytes[3]; bytes[3] = t0;
                    byte t1 = bytes[1]; bytes[1] = bytes[2]; bytes[2] = t1;
                    Buffer.BlockCopy(bytes, 0, rgb32.data(), 4 * (int)i, 4);
                }
            }
            if (Math.Abs(scale) != 1.0f)
                for (uint i = 0; i < nFloats; ++i)
                    rgb32[i] *= Math.Abs(scale);

            fp.Dispose();
            LOG_VERBOSE($"Read PFM image {filename} ({width} x {height})");
            metadata.colorSpace = RGBColorSpace.sRGB;
            if (nChannels == 1)
                return new ImageAndMetadata(new Image(rgb32, new Point2i(width, height), new string[] { "Y" }),
                                            metadata);
            else
                return new ImageAndMetadata(new Image(rgb32, new Point2i(width, height), new string[] { "R", "G", "B" }),
                                            metadata);

        fail:
            if (fp != null)
                fp.Dispose();
            ErrorExit($"{filename}: premature end of file in PFM file");
            return default;
        }

        private static ImageAndMetadata ReadHDR(string filename, Allocator alloc)
        {
            int x, y, n;
            float[] data = stb_image.stbi_loadf(filename, out x, out y, out n, 0);
            if (data == null)
            {
                ErrorExit($"{filename}: {stb_image.stbi_failure_reason()}");
                return default;
            }

            float[] px = new float[(size_t)x * (size_t)y * (size_t)n];
            Array.Copy(data, px, px.Length);
            pstd.vector<float> pixels = new pstd.vector<float>(px);
            stb_image.stbi_image_free(data);

            switch (n)
            {
                case 1:
                    return new ImageAndMetadata(new Image(pixels, new Point2i(x, y), new string[] { "Y" }), new ImageMetadata());
                case 2:
                    {
                        Image image = new Image(pixels, new Point2i(x, y), new string[] { "Y", "A" });
                        return new ImageAndMetadata(image.SelectChannels(image.GetChannelDesc(new string[] { "Y" })),
                                                    new ImageMetadata());
                    }
                case 3:
                    return new ImageAndMetadata(new Image(pixels, new Point2i(x, y), new string[] { "R", "G", "B" }),
                                                new ImageMetadata());
                case 4:
                    {
                        Image image = new Image(pixels, new Point2i(x, y), new string[] { "R", "G", "B", "A" });
                        return new ImageAndMetadata(
                            image.SelectChannels(image.GetChannelDesc(new string[] { "R", "G", "B" })), new ImageMetadata());
                    }
                default:
                    ErrorExit($"{filename}: {n} channel image unsupported.");
                    return default;
            }
        }

        private static ImageAndMetadata ReadQOI(string filename, Allocator alloc)
        {
            byte[] contents = ReadFileBytes(filename);
            qoi.qoi_desc desc = new qoi.qoi_desc();
            byte[] pixels = qoi.qoi_decode(contents, contents.Length, desc, 0 /* channels */);
            CHECK(pixels != null);  // qoi failure

            ImageMetadata metadata = new ImageMetadata();
            metadata.colorSpace = RGBColorSpace.sRGB;

            List<string> channelNames = new List<string> { "R", "G", "B" };
            if (desc.channels == 4)
                channelNames.Add("A");
            else
                CHECK_EQ(3, desc.channels);

            CHECK(desc.colorspace == qoi.QOI_SRGB || desc.colorspace == qoi.QOI_LINEAR);
            ColorEncoding encoding =
                (desc.colorspace == qoi.QOI_SRGB) ? ColorEncoding.sRGB : ColorEncoding.Linear;

            Image image = new Image(PixelFormat.U256, new Point2i((int)desc.width, (int)desc.height), channelNames,
                                    encoding, alloc);
            Array.Copy(pixels, image.p8.data(), (long)((size_t)desc.width * (size_t)desc.height * (size_t)desc.channels));

            return new ImageAndMetadata(image, metadata);
        }

        private bool WritePFM(string filename, ImageMetadata metadata)
        {
            FileStream fp = FOpenWrite(filename);
            if (fp == null)
            {
                Error($"{filename}: unable to open output PFM file.");
                return false;
            }

            if (NChannels() != 3)
            {
                Error($"{filename}: only 3-channel images are supported for PFM.");
                fp.Dispose();
                return false;
            }

            float[] scanline = new float[3 * resolution.x];
            byte[] scanBytes = new byte[sizeof(float) * 3 * resolution.x];
            float scale;

            try
            {
                // only write 3 channel PFMs here...
                byte[] hdr0 = System.Text.Encoding.ASCII.GetBytes("PF\n");
                fp.Write(hdr0, 0, hdr0.Length);

                // write the width and height, which must be positive
                byte[] hdr1 = System.Text.Encoding.ASCII.GetBytes($"{resolution.x} {resolution.y}\n");
                fp.Write(hdr1, 0, hdr1.Length);

                // write the scale, which encodes endianness
                scale = hostLittleEndian ? -1.0f : 1.0f;
                byte[] hdr2 = System.Text.Encoding.ASCII.GetBytes(scale.ToString("F6", System.Globalization.CultureInfo.InvariantCulture) + "\n");
                fp.Write(hdr2, 0, hdr2.Length);

                // write the data from bottom left to upper right as specified by
                // http://netpbm.sourceforge.net/doc/pfm.html
                // The raster is a sequence of pixels, packed one after another, with no
                // delimiters of any kind. They are grouped by row, with the pixels in each
                // row ordered left to right and the rows ordered bottom to top.
                for (int y = resolution.y - 1; y >= 0; y--)
                {
                    for (int x = 0; x < resolution.x; ++x)
                    {
                        if (NChannels() == 1)
                        {
                            Float v = GetChannel(new Point2i(x, y), 0);
                            scanline[3 * x] = scanline[3 * x + 1] = scanline[3 * x + 2] = (float)v;
                        }
                        else
                        {
                            CHECK_EQ(3, NChannels());
                            for (int c = 0; c < 3; ++c)
                                // Assign element-wise in case Float is typedefed as
                                // 'double'.
                                scanline[3 * x + c] = (float)GetChannel(new Point2i(x, y), c);
                        }
                    }
                    Buffer.BlockCopy(scanline, 0, scanBytes, 0, scanBytes.Length);
                    fp.Write(scanBytes, 0, scanBytes.Length);
                }

                fp.Dispose();
                return true;
            }
            catch (Exception)
            {
                Error($"Error writing PFM file \"{filename}\"");
                fp.Dispose();
                return false;
            }
        }

        // Image Private Members
        private PixelFormat format;
        private Point2i resolution;
        private pstd.vector<string> channelNames;
        private ColorEncoding encoding = null;
        public pstd.vector<uint8_t> p8;
        public pstd.vector<Half> p16;
        public pstd.vector<float> p32;
    }
}
