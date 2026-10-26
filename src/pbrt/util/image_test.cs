// image_test.cpp

using System;
using System.Collections.Generic;
using testing;

namespace pbrt
{
    public static unsafe class ImageTests
    {
        private static string[] Names(params string[] n) { return n; }

        [TEST("Image", "Basics")]
        public static void Image_Basics()
        {
            ColorEncoding encoding = ColorEncoding.Linear;
            Image y8 = new Image(PixelFormat.U256, new Point2i(4, 8), Names("Y"), encoding);
            EXPECT_EQ(y8.NChannels(), 1);
            EXPECT_EQ(y8.BytesUsed(), y8.Resolution()[0] * y8.Resolution()[1]);

            Image y16 = new Image(PixelFormat.Half, new Point2i(4, 8), Names("Y"));
            EXPECT_EQ(y16.NChannels(), 1);
            EXPECT_EQ(y16.BytesUsed(), 2 * y16.Resolution()[0] * y16.Resolution()[1]);

            Image y32 = new Image(PixelFormat.Float, new Point2i(4, 8), Names("Y"));
            EXPECT_EQ(y32.NChannels(), 1);
            EXPECT_EQ(y32.BytesUsed(), 4 * y32.Resolution()[0] * y32.Resolution()[1]);

            Image rgb8 = new Image(PixelFormat.U256, new Point2i(4, 8), Names("R", "G", "B"), encoding);
            EXPECT_EQ(rgb8.NChannels(), 3);
            EXPECT_EQ(rgb8.BytesUsed(), 3 * rgb8.Resolution()[0] * rgb8.Resolution()[1]);

            Image rgb16 = new Image(PixelFormat.Half, new Point2i(4, 16), Names("R", "G", "B"));
            EXPECT_EQ(rgb16.NChannels(), 3);
            EXPECT_EQ(rgb16.BytesUsed(), 2 * 3 * rgb16.Resolution()[0] * rgb16.Resolution()[1]);

            Image rgb32 = new Image(PixelFormat.Float, new Point2i(4, 32), Names("R", "G", "B"));
            EXPECT_EQ(rgb32.NChannels(), 3);
            EXPECT_EQ(rgb32.BytesUsed(), 4 * 3 * rgb32.Resolution()[0] * rgb32.Resolution()[1]);
        }

        private static pstd.vector<float> GetFloatPixels(Point2i res, int nc)
        {
            pstd.vector<float> p = new pstd.vector<float>();
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < nc; ++c)
                        if (c == 0)
                            p.push_back((float)(std.sin(x / 4.0) * std.cos(y / 8.0)));
                        else
                            p.push_back((float)(-.25 + 2.0 * (c + 3 * x + 3 * y * res[0]) / (res[0] * res[1])));
            return p;
        }

        private static pstd.vector<uint8_t> GetU8Pixels(Point2i res, int nc)
        {
            pstd.vector<uint8_t> p = new pstd.vector<uint8_t>();
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < nc; ++c)
                        if (c == 0)
                            p.push_back(unchecked((uint8_t)(x + res[0] * y)));
                        else
                            p.push_back(unchecked((uint8_t)(x * y + c * (x - y))));
            return p;
        }

        private static Float modelQuantization(Float value, PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.U256:
                    return Clamp((value * 255.0f) + 0.5f, 0.0f, 255.0f) * (1.0f / 255.0f);
                case PixelFormat.Half:
                    return (Float)new Half(value);
                case PixelFormat.Float:
                    return value;
                default:
                    LOG_FATAL("Unhandled pixel format");
                    return 0;
            }
        }

        private static readonly PixelFormat[] kFormats = { PixelFormat.U256, PixelFormat.Half, PixelFormat.Float };

        [TEST("Image", "GetSetY")]
        public static void Image_GetSetY()
        {
            Point2i res = new Point2i(9, 3);
            pstd.vector<float> yPixels = GetFloatPixels(res, 1);

            foreach (PixelFormat format in kFormats)
            {
                Image image = new Image(format, res, Names("Y"), ColorEncoding.Linear);
                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                    {
                        image.SetChannel(new Point2i(x, y), 0, yPixels[(ulong)(y * res[0] + x)]);
                    }

                ImageChannelDesc yDesc = image.GetChannelDesc(Names("Y"));
                EXPECT_TRUE((bool)yDesc);
                EXPECT_FALSE((bool)image.GetChannelDesc(Names("Y0L0")));

                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), 0);
                        ImageChannelValues cv = image.GetChannels(new Point2i(x, y));
                        EXPECT_EQ(1, cv.size());
                        EXPECT_EQ(v, cv[0]);

                        cv = image.GetChannels(new Point2i(x, y), yDesc);
                        EXPECT_EQ(1, cv.size());
                        EXPECT_EQ(v, cv[0]);

                        if (format == PixelFormat.U256)
                            EXPECT_LT(std.abs(v - Clamp(yPixels[(ulong)(y * res[0] + x)], 0.0f, 1.0f)), 0.501f / 255.0f,
                                      v + " vs " + Clamp(yPixels[(ulong)(y * res[0] + x)], 0.0f, 1.0f));
                        else
                            EXPECT_EQ(v, modelQuantization(yPixels[(ulong)(y * res[0] + x)], format));
                    }
            }
        }

        [TEST("Image", "GetSetRGB")]
        public static void Image_GetSetRGB()
        {
            Point2i res = new Point2i(7, 32);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);

            foreach (PixelFormat format in kFormats)
            {
                Image image = new Image(format, res, Names("R", "G", "B"), ColorEncoding.Linear);
                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                        for (int c = 0; c < 3; ++c)
                            image.SetChannel(new Point2i(x, y), c, rgbPixels[(ulong)(3 * y * res[0] + 3 * x + c)]);

                ImageChannelDesc rgbDesc = image.GetChannelDesc(Names("R", "G", "B"));
                EXPECT_TRUE((bool)rgbDesc);
                EXPECT_FALSE((bool)image.GetChannelDesc(Names("R", "Gxxx", "B")));

                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                    {
                        ImageChannelValues rgb = image.GetChannels(new Point2i(x, y));
                        EXPECT_EQ(3, rgb.size());

                        ImageChannelValues rgb2 = image.GetChannels(new Point2i(x, y), rgbDesc);
                        EXPECT_EQ(rgb[0], rgb2[0]);
                        EXPECT_EQ(rgb[1], rgb2[1]);
                        EXPECT_EQ(rgb[2], rgb2[2]);

                        for (int c = 0; c < 3; ++c)
                        {
                            EXPECT_EQ(rgb[c], image.GetChannel(new Point2i(x, y), c));
                            int offset = 3 * y * res[0] + 3 * x + c;
                            if (format == PixelFormat.U256)
                                EXPECT_LT(std.abs(rgb[c] - Clamp(rgbPixels[(ulong)offset], 0.0f, 1.0f)), 0.501f / 255.0f);
                            else
                            {
                                Float qv = modelQuantization(rgbPixels[(ulong)offset], format);
                                EXPECT_EQ(rgb[c], qv);
                            }
                        }
                    }
            }
        }

        [TEST("Image", "GetSetBGR")]
        public static void Image_GetSetBGR()
        {
            Point2i res = new Point2i(7, 32);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);

            foreach (PixelFormat format in kFormats)
            {
                Image image = new Image(format, res, Names("R", "G", "B"), ColorEncoding.Linear);
                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                        for (int c = 0; c < 3; ++c)
                            image.SetChannel(new Point2i(x, y), c, rgbPixels[(ulong)(3 * y * res[0] + 3 * x + c)]);

                ImageChannelDesc bgrDesc = image.GetChannelDesc(Names("B", "G", "R"));
                EXPECT_TRUE((bool)bgrDesc);

                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                    {
                        ImageChannelValues rgb = image.GetChannels(new Point2i(x, y));
                        EXPECT_EQ(3, rgb.size());

                        ImageChannelValues bgr = image.GetChannels(new Point2i(x, y), bgrDesc);
                        EXPECT_EQ(rgb[0], bgr[2]);
                        EXPECT_EQ(rgb[1], bgr[1]);
                        EXPECT_EQ(rgb[2], bgr[0]);

                        for (int c = 0; c < 3; ++c)
                        {
                            EXPECT_EQ(rgb[c], image.GetChannel(new Point2i(x, y), c));
                            int offset = 3 * y * res[0] + 3 * x + c;
                            if (format == PixelFormat.U256)
                                EXPECT_LT(std.abs(rgb[c] - Clamp(rgbPixels[(ulong)offset], 0.0f, 1.0f)), 0.501f / 255.0f);
                            else
                            {
                                Float qv = modelQuantization(rgbPixels[(ulong)offset], format);
                                EXPECT_EQ(rgb[c], qv);
                            }
                        }
                    }
            }
        }

        [TEST("Image", "CopyRectOut")]
        public static void Image_CopyRectOut()
        {
            Point2i res = new Point2i(29, 14);

            foreach (PixelFormat format in kFormats)
            {
                for (int nc = 1; nc < 4; ++nc)
                {
                    pstd.vector<float> orig = GetFloatPixels(res, nc);

                    List<string> channelNames = new List<string> { "A" };
                    for (int i = 1; i < nc; ++i)
                        channelNames.Add(((char)('A' + 1)).ToString());  // (sic) same as the C++ test

                    Image image = new Image(format, res, channelNames, ColorEncoding.Linear);

                    ulong origIter = 0;
                    for (int y = 0; y < res[1]; ++y)
                        for (int x = 0; x < res[0]; ++x)
                            for (int c = 0; c < nc; ++c, ++origIter)
                                image.SetChannel(new Point2i(x, y), c, orig[origIter]);

                    Bounds2i extent = new Bounds2i(new Point2i(2, 3), new Point2i(5, 10));
                    float[] buf = new float[extent.Area() * nc];

                    image.CopyRectOut(extent, new pstd.span<float>(buf, 0, buf.Length));

                    // Iterate through the points in the extent and the buffer
                    // together.
                    int bufIter = 0;
                    foreach (Point2i p in extent)
                    {
                        for (int c = 0; c < nc; ++c)
                        {
                            ASSERT_FALSE(bufIter == buf.Length);
                            EXPECT_EQ(buf[bufIter], image.GetChannel(p, c));
                            ++bufIter;
                        }
                    }
                }
            }
        }

        [TEST("Image", "CopyRectIn")]
        public static void Image_CopyRectIn()
        {
            Point2i res = new Point2i(17, 32);
            RNG rng = new RNG();

            foreach (PixelFormat format in kFormats)
            {
                for (int nc = 1; nc < 4; ++nc)
                {
                    pstd.vector<float> orig = GetFloatPixels(res, nc);

                    List<string> channelNames = new List<string> { "A" };
                    for (int i = 1; i < nc; ++i)
                        channelNames.Add(((char)('A' + 1)).ToString());

                    Image image = new Image(format, res, channelNames, ColorEncoding.Linear);
                    ulong origIter = 0;
                    for (int y = 0; y < res[1]; ++y)
                        for (int x = 0; x < res[0]; ++x)
                            for (int c = 0; c < nc; ++c, ++origIter)
                                image.SetChannel(new Point2i(x, y), c, orig[origIter]);

                    Bounds2i extent = new Bounds2i(new Point2i(10, 23), new Point2i(17, 28));
                    float[] buf = new float[extent.Area() * nc];
                    for (int i = 0; i < buf.Length; ++i)
                        buf[i] = rng.Uniform<Float>();

                    image.CopyRectIn(extent, new pstd.span<float>(buf, 0, buf.Length));

                    // Iterate through the points in the extent and the buffer
                    // together.
                    int bufIter = 0;
                    foreach (Point2i p in extent)
                    {
                        for (int c = 0; c < nc; ++c)
                        {
                            ASSERT_FALSE(bufIter == buf.Length);
                            if (format == PixelFormat.U256)
                            {
                                Float err = std.abs(image.GetChannel(p, c) - Clamp(buf[bufIter], 0.0f, 1.0f));
                                EXPECT_LT(err, 0.501f / 255.0f);
                            }
                            else
                            {
                                Float qv = modelQuantization(buf[bufIter], format);
                                EXPECT_EQ(qv, image.GetChannel(p, c));
                            }
                            ++bufIter;
                        }
                    }
                }
            }
        }

        [TEST("Image", "PfmIO")]
        public static void Image_PfmIO()
        {
            Point2i res = new Point2i(16, 49);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);

            Image image = new Image(rgbPixels, res, Names("R", "G", "B"));
            EXPECT_TRUE(image.Write("test.pfm"));
            ImageAndMetadata read = Image.Read("test.pfm");
            EXPECT_TRUE(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

            EXPECT_EQ(image.Resolution(), read.image.Resolution());
            EXPECT_EQ(PixelFormat.Float, read.image.Format());
            EXPECT_EQ(3, read.image.NChannels());

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < 3; ++c)
                        EXPECT_EQ(image.GetChannel(new Point2i(x, y), c), read.image.GetChannel(new Point2i(x, y), c));

            EXPECT_TRUE(RemoveFile("test.pfm"));
        }

        [TEST("Image", "ExrIO")]
        public static void Image_ExrIO()
        {
            Point2i res = new Point2i(16, 49);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);

            foreach (PixelFormat format in kFormats)
            {
                Image image = new Image(format, res, Names("R", "G", "B"), ColorEncoding.Linear);
                image.CopyRectIn(new Bounds2i(new Point2i(0, 0), res), rgbPixels);

                // Check CopyRectIn()
                ulong offset = 0;
                for (int y = 0; y < res.y; ++y)
                    for (int x = 0; x < res.x; ++x)
                        for (int c = 0; c < 3; ++c, ++offset)
                        {
                            if (format == PixelFormat.U256)
                                ;
                            else if (format == PixelFormat.Half)
                            {
                                ASSERT_EQ((Float)new Half(rgbPixels[offset]), image.GetChannel(new Point2i(x, y), c));
                            }
                            else if (format == PixelFormat.Float)
                            {
                                ASSERT_EQ(rgbPixels[offset], image.GetChannel(new Point2i(x, y), c));
                            }
                        }

                ImageMetadata metadata = new ImageMetadata();
                metadata.colorSpace = RGBColorSpace.ACES2065_1;
                EXPECT_TRUE(image.Write("test.exr", metadata));

                ImageAndMetadata read = Image.Read("test.exr");

                EXPECT_EQ(image.Resolution(), read.image.Resolution());
                EXPECT_TRUE(RGBColorSpace.ACES2065_1 == read.metadata.GetColorSpace());
                if (!Is8Bit(format))
                    EXPECT_EQ(read.image.Format(), format);

                ImageChannelDesc rgbDesc = read.image.GetChannelDesc(Names("R", "G", "B"));
                ASSERT_TRUE((bool)rgbDesc);

                for (int y = 0; y < res[1]; ++y)
                    for (int x = 0; x < res[0]; ++x)
                    {
                        ImageChannelValues v = read.image.GetChannels(new Point2i(x, y), rgbDesc);
                        for (int c = 0; c < 3; ++c)
                            if (Is8Bit(format))
                                EXPECT_EQ((Float)new Half(image.GetChannel(new Point2i(x, y), c)), v[c]);
                            else if (Is16Bit(format))
                                EXPECT_EQ((Float)new Half(image.GetChannel(new Point2i(x, y), c)), v[c]);
                            else
                                EXPECT_EQ(image.GetChannel(new Point2i(x, y), c), v[c],
                                          " @ (" + x + ", " + y + ", ch " + c + ")");
                    }

                EXPECT_TRUE(RemoveFile("test.exr"));
            }
        }

        [TEST("Image", "ExrNoMetadata")]
        public static void Image_ExrNoMetadata()
        {
            Point2i res = new Point2i(16, 32);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);
            Image image = new Image(rgbPixels, res, Names("R", "G", "B"));

            string filename = "nometadata.exr";
            ImageMetadata writeMetadata = new ImageMetadata();
            writeMetadata.colorSpace = RGBColorSpace.ACES2065_1;
            EXPECT_TRUE(image.Write(filename, writeMetadata));

            ImageAndMetadata read = Image.Read(filename);
            ImageMetadata metadata = read.metadata;
            EXPECT_TRUE(RGBColorSpace.ACES2065_1 == metadata.GetColorSpace());

            // All of the metadata should be unset
            EXPECT_FALSE(metadata.renderTimeSeconds.has_value());
            EXPECT_FALSE(metadata.cameraFromWorld.has_value());
            EXPECT_FALSE(metadata.NDCFromWorld.has_value());
            EXPECT_TRUE(metadata.pixelBounds.has_value());
            EXPECT_EQ(metadata.pixelBounds.value(), new Bounds2i(new Point2i(0, 0), res));
            EXPECT_TRUE(metadata.fullResolution.has_value());
            EXPECT_EQ(metadata.fullResolution.value(), res);
            EXPECT_EQ(0, metadata.strings.Count);
            EXPECT_EQ(0, metadata.stringVectors.Count);

            EXPECT_TRUE(RemoveFile(filename));
        }

        [TEST("Image", "ExrMetadata")]
        public static void Image_ExrMetadata()
        {
            Point2i res = new Point2i(16, 32);
            pstd.vector<float> rgbPixels = GetFloatPixels(res, 3);
            Image image = new Image(rgbPixels, res, Names("R", "G", "B"));

            string filename = "metadata.exr";
            ImageMetadata outMetadata = new ImageMetadata();
            outMetadata.colorSpace = RGBColorSpace.Rec2020;
            outMetadata.renderTimeSeconds = 1234.0f;
            SquareMatrix4 w2c = new SquareMatrix4(3, 1, 4, 1, 5, 9, 2, Pi, 2, 7, 1, 8, 2, 8, 1, (Float)std.exp(1.0f));
            SquareMatrix4 w2n = new SquareMatrix4(1.5f, 2.5f, 3.5f, 4.75f, 5.333f, 6.2135f, -351.2f, -552.0f, 63.2f, 47.2f, Pi,
                                                  (Float)std.cos(1.0f), 0, -14, 6, 1e-10f);
            // Must be the same area as image resolution.
            Bounds2i pb = new Bounds2i(new Point2i(2, 10), new Point2i(18, 42));
            Point2i fullRes = new Point2i(1000, 200);
            SortedDictionary<string, List<string>> stringVectors = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            stringVectors["yolo"] = new List<string> { "foo", "bar" };

            outMetadata.cameraFromWorld = w2c;
            outMetadata.NDCFromWorld = w2n;
            outMetadata.pixelBounds = pb;
            outMetadata.fullResolution = fullRes;
            outMetadata.strings["pbrt"] = "v4";
            outMetadata.stringVectors = stringVectors;
            EXPECT_TRUE(image.Write(filename, outMetadata));

            ImageAndMetadata read = Image.Read(filename);
            ImageMetadata inMetadata = read.metadata;

            EXPECT_TRUE(RGBColorSpace.Rec2020 == inMetadata.GetColorSpace());

            EXPECT_TRUE(inMetadata.renderTimeSeconds.has_value());
            EXPECT_EQ(1234, inMetadata.renderTimeSeconds.value());

            EXPECT_TRUE(inMetadata.cameraFromWorld.has_value());
            EXPECT_EQ(inMetadata.cameraFromWorld.value(), w2c);

            EXPECT_TRUE(inMetadata.NDCFromWorld.has_value());
            EXPECT_EQ(inMetadata.NDCFromWorld.value(), w2n);

            EXPECT_TRUE(inMetadata.pixelBounds.has_value());
            EXPECT_EQ(inMetadata.pixelBounds.value(), pb);

            EXPECT_TRUE(inMetadata.fullResolution.has_value());
            EXPECT_EQ(inMetadata.fullResolution.value(), fullRes);

            EXPECT_EQ(1, inMetadata.strings.Count);
            EXPECT_EQ("v4", inMetadata.strings["pbrt"]);

            EXPECT_EQ(1, inMetadata.stringVectors.Count);
            List<string> yolo;
            bool found = stringVectors.TryGetValue("yolo", out yolo);
            EXPECT_TRUE(found);
            EXPECT_EQ("foo", yolo[0]);
            EXPECT_EQ("bar", yolo[1]);

            EXPECT_TRUE(RemoveFile(filename));
        }

        [TEST("Image", "PngYIO")]
        public static void Image_PngYIO()
        {
            Point2i res = new Point2i(11, 50);
            pstd.vector<uint8_t> rgbPixels = GetU8Pixels(res, 1);

            Image image = new Image(rgbPixels, res, Names("Y"), ColorEncoding.sRGB);
            EXPECT_TRUE(image.Write("test.png"));
            ImageAndMetadata read = Image.Read("test.png");

            EXPECT_EQ(image.Resolution(), read.image.Resolution());
            EXPECT_EQ(read.image.Format(), PixelFormat.U256);
            ASSERT_TRUE(read.image.Encoding() != null);

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    EXPECT_EQ(SRGB8ToLinear(rgbPixels[(ulong)(y * res[0] + x)]),
                              read.image.GetChannel(new Point2i(x, y), 0),
                              " x " + x + ", y " + y + ", orig " + rgbPixels[(ulong)(y * res[0] + x)]);

            EXPECT_TRUE(RemoveFile("test.png"));
        }

        private static void CheckRGB8RoundTrip(string filename, pstd.vector<uint8_t> rgbPixels, Point2i res)
        {
            Image image = new Image(rgbPixels, res, Names("R", "G", "B"), ColorEncoding.sRGB);
            EXPECT_TRUE(image.Write(filename));
            ImageAndMetadata read = Image.Read(filename);

            EXPECT_EQ(image.Resolution(), read.image.Resolution());
            EXPECT_EQ(read.image.Format(), PixelFormat.U256);
            ASSERT_TRUE(read.image.Encoding() != null);
            ASSERT_TRUE(read.metadata.colorSpace.has_value());
            ASSERT_TRUE(read.metadata.colorSpace.value() != null);
            EXPECT_TRUE(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                    for (int c = 0; c < 3; ++c)
                    {
                        EXPECT_EQ(SRGB8ToLinear(rgbPixels[(ulong)(c + 3 * (y * res[0] + x))]),
                                  image.GetChannel(new Point2i(x, y), c),
                                  " x " + x + ", y " + y + ", c " + c + ", orig " + rgbPixels[(ulong)(3 * y * res[0] + 3 * x + c)]);
                    }

            EXPECT_TRUE(RemoveFile(filename));
        }

        [TEST("Image", "PngRgbIO")]
        public static void Image_PngRgbIO()
        {
            Point2i res = new Point2i(11, 50);
            CheckRGB8RoundTrip("test.png", GetU8Pixels(res, 3), res);
        }

        [TEST("Image", "PngEmojiIO")]
        public static void Image_PngEmojiIO()
        {
            Point2i res = new Point2i(11, 50);
            // trex.png (UTF-8 bytes held in Latin-1 chars, like std::string)
            string filename = "ð\u009F¦\u0096.png";
            CheckRGB8RoundTrip(filename, GetU8Pixels(res, 3), res);
        }

        [TEST("Image", "QoiRgbIO")]
        public static void Image_QoiRgbIO()
        {
            Point2i res = new Point2i(11, 50);
            CheckRGB8RoundTrip("test.qoi", GetU8Pixels(res, 3), res);
        }

        [TEST("Image", "QoiRgbaIO")]
        public static void Image_QoiRgbaIO()
        {
            Point2i res = new Point2i(11, 50);
            pstd.vector<uint8_t> rgbPixels = GetU8Pixels(res, 4);

            // Intentionally out of normal order...
            Image image = new Image(rgbPixels, res, Names("A", "R", "G", "B"), ColorEncoding.sRGB);
            EXPECT_TRUE(image.Write("test-rgba.qoi"));
            ImageAndMetadata read = Image.Read("test-rgba.qoi");

            EXPECT_EQ(image.Resolution(), read.image.Resolution());
            EXPECT_EQ(read.image.Format(), PixelFormat.U256);
            ASSERT_TRUE(read.image.Encoding() != null);
            ASSERT_TRUE(read.metadata.colorSpace.has_value());
            ASSERT_TRUE(read.metadata.colorSpace.value() != null);
            EXPECT_TRUE(RGBColorSpace.sRGB == read.metadata.GetColorSpace());

            ImageChannelDesc desc = image.GetChannelDesc(Names("A", "R", "G", "B"));
            ASSERT_TRUE((bool)desc);

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    ImageChannelValues v = image.GetChannels(new Point2i(x, y), desc);
                    for (int c = 0; c < 4; ++c)
                        EXPECT_EQ(SRGB8ToLinear(rgbPixels[(ulong)(c + 4 * (y * res[0] + x))]), v[c],
                                  " x " + x + ", y " + y + ", c " + c + ", orig " + rgbPixels[(ulong)(c + 4 * (y * res[0] + x))]);
                }

            EXPECT_TRUE(RemoveFile("test-rgba.qoi"));
        }

        [TEST("Image", "SampleSimple")]
        public static void Image_SampleSimple()
        {
            pstd.vector<float> texels = new pstd.vector<float>();
            texels.push_back(0); texels.push_back(1); texels.push_back(0); texels.push_back(0);
            Image zeroOne = new Image(texels, new Point2i(2, 2), Names("Y"));
            PiecewiseConstant2D distrib = new PiecewiseConstant2D(zeroOne.GetSamplingDistribution());
            RNG rng = new RNG();
            for (int i = 0; i < 1000; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Float pdf;
                Point2f p = distrib.Sample(u, &pdf);
                // Due to bilerp on lookup, the non-zero range goes out a bit.
                EXPECT_GE(p.x, 0.5);
                EXPECT_LE(p.y, 0.5);
            }
        }

        [TEST("Image", "SampleLinear")]
        public static void Image_SampleLinear()
        {
            int w = 500, h = 500;
            pstd.vector<float> v = new pstd.vector<float>();
            for (int y = 0; y < h; ++y)
            {
                Float fy = (Float)((y + .5) / h);
                for (int x = 0; x < w; ++x)
                {
                    Float fx = (Float)((x + .5) / w);
                    // This integrates to 1 over [0,1]^2
                    Float f = fx + fy;
                    v.push_back(f);
                }
            }

            Image image = new Image(v, new Point2i(w, h), Names("Y"));
            PiecewiseConstant2D distrib = new PiecewiseConstant2D(image.GetSamplingDistribution());
            RNG rng = new RNG();
            for (int i = 0; i < 1000; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Float pdf;
                Point2f p = distrib.Sample(u, &pdf);
                Float f = p.x + p.y;
                // Allow some error since PiecewiseConstant2D uses a piecewise constant
                // sampling distribution.
                EXPECT_LE(std.abs(f - pdf), 3e-3, u + ", f: " + f + ", pdf: " + pdf);
            }
        }

        [TEST("Image", "SampleSinCos")]
        public static void Image_SampleSinCos()
        {
            int w = 500, h = 500;
            Func<Point2f, Float> f = (Point2f p) => (Float)std.abs(std.sin(3.0 * p.x) * Sqr(std.cos(4.0 * p.y)));
            // Integral of f over [0,1]^2
            Float integral = (Float)(1.0 / 24.0 * Sqr(std.sin(1.5)) * (8 + std.sin(8.0)));

            pstd.vector<float> v = new pstd.vector<float>();
            for (int y = 0; y < h; ++y)
            {
                Float fy = (Float)((y + .5) / h);
                for (int x = 0; x < w; ++x)
                {
                    Float fx = (Float)((x + .5) / w);
                    v.push_back(f(new Point2f(fx, fy)));
                }
            }

            Image image = new Image(v, new Point2i(w, h), Names("Y"));
            PiecewiseConstant2D distrib = new PiecewiseConstant2D(image.GetSamplingDistribution());
            RNG rng = new RNG();
            for (int i = 0; i < 1000; ++i)
            {
                Float uy = rng.Uniform<Float>(), ux = rng.Uniform<Float>();
                Point2f u = new Point2f(ux, uy);
                Float pdf;
                Point2f p = distrib.Sample(u, &pdf);
                Float fp = f(p);
                // Allow some error since PiecewiseConstant2D uses a piecewise constant
                // sampling distribution.
                EXPECT_LE(std.abs(fp - pdf * integral), .02f, u + ", fp: " + fp + ", pdf: " + pdf);
            }
        }

        [TEST("Image", "Wrap2D")]
        public static void Image_Wrap2D()
        {
            pstd.vector<float> texels = new pstd.vector<float>();
            float[] t = { 0, 1, 0, 0, 0, 0, 0, 0, 0 };
            foreach (float tv in t) texels.push_back(tv);
            Image zeroOne = new Image(texels, new Point2i(3, 3), Names("Y"));

            EXPECT_EQ(1, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Clamp)));
            EXPECT_EQ(1, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Black, WrapMode.Clamp)));
            EXPECT_EQ(0, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Black, WrapMode.Repeat)));
            EXPECT_EQ(0, zeroOne.GetChannel(new Point2i(1, -1), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Black)));

            EXPECT_EQ(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Clamp)));
            EXPECT_EQ(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Repeat, WrapMode.Clamp)));
            EXPECT_EQ(1, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Black, WrapMode.Repeat)));
            EXPECT_EQ(0, zeroOne.GetChannel(new Point2i(1, 3), 0, new WrapMode2D(WrapMode.Clamp, WrapMode.Black)));

            EXPECT_EQ(0.5, zeroOne.BilerpChannel(new Point2f(0.5f, 0.0f), 0, WrapMode.Repeat));
            EXPECT_EQ(0.5, zeroOne.BilerpChannel(new Point2f(0.5f, 0.0f), 0, WrapMode.Black));
            EXPECT_EQ(1, zeroOne.BilerpChannel(new Point2f(0.5f, 0.0f), 0, WrapMode.Clamp));
        }

        [TEST("Image", "Select")]
        public static void Image_Select()
        {
            Point2i res = new Point2i(4, 9);
            pstd.vector<float> pix = GetFloatPixels(res, 4);
            Image image = new Image(pix, res, Names("A", "B", "G", "R"));

            ImageChannelDesc aDesc = image.GetChannelDesc(Names("A"));
            EXPECT_TRUE((bool)aDesc);
            Image aImage = image.SelectChannels(aDesc);

            EXPECT_EQ(aImage.Resolution(), image.Resolution());
            EXPECT_EQ(1, aImage.NChannels());
            EXPECT_EQ(1, aImage.ChannelNames().Count);
            EXPECT_EQ("A", aImage.ChannelNames()[0]);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    EXPECT_EQ(aImage.GetChannel(new Point2i(x, y), 0), image.GetChannel(new Point2i(x, y), 0));

            ImageChannelDesc rgDesc = image.GetChannelDesc(Names("R", "G"));
            EXPECT_TRUE((bool)rgDesc);
            Image rgImage = image.SelectChannels(rgDesc);
            EXPECT_EQ(rgImage.Resolution(), image.Resolution());
            EXPECT_EQ(2, rgImage.NChannels());
            EXPECT_EQ(2, rgImage.ChannelNames().Count);
            EXPECT_EQ("R", rgImage.ChannelNames()[0]);
            EXPECT_EQ("G", rgImage.ChannelNames()[1]);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    EXPECT_EQ(rgImage.GetChannel(new Point2i(x, y), 0), image.GetChannel(new Point2i(x, y), 3));
                    EXPECT_EQ(rgImage.GetChannel(new Point2i(x, y), 1), image.GetChannel(new Point2i(x, y), 2));
                }
        }

        ///////////////////////////////////////////////////////////////////////////

        private static string inTestDir(string path) { return path; }

        private static void TestRoundTrip(string fn)
        {
            Point2i res = new Point2i(16, 29);
            Image image = new Image(PixelFormat.Float, res, Names("R", "G", "B"));
            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    Float[] vals = { (Float)x / (Float)(res[0] - 1), (Float)y / (Float)(res[1] - 1), -1.5f };
                    image.SetChannels(new Point2i(x, y), new pstd.span<Float>(vals, 0, 3));
                }

            string filename = inTestDir(fn);
            ASSERT_TRUE(image.Write(filename));

            ImageAndMetadata readImage = Image.Read(filename);
            ASSERT_EQ(readImage.image.Resolution(), res);

            ImageChannelDesc rgbDesc = readImage.image.GetChannelDesc(Names("R", "G", "B"));
            ASSERT_TRUE((bool)rgbDesc);

            for (int y = 0; y < res[1]; ++y)
                for (int x = 0; x < res[0]; ++x)
                {
                    ImageChannelValues rgb = readImage.image.GetChannels(new Point2i(x, y), rgbDesc);

                    for (int c = 0; c < 3; ++c)
                    {
                        float wrote = image.GetChannel(new Point2i(x, y), c);
                        float delta = wrote - rgb[c];
                        string msg = filename + ":(" + x + ", " + y + ") c = " + c + " wrote " + wrote + ", read " + rgb[c] +
                                     ", delta = " + delta;
                        if (HasExtension(filename, "pfm"))
                        {
                            // Everything should come out exact.
                            EXPECT_EQ(0, delta, msg);
                        }
                        else if (HasExtension(filename, "exr"))
                        {
                            if (c == 2)
                                // -1.5 is exactly representable as a float.
                                EXPECT_EQ(0, delta, msg);
                            else
                                EXPECT_LT(std.abs(delta), .001, msg);
                        }
                        else
                        {
                            // 8 bit format...
                            if (c == 2)
                                // -1.5 should be clamped to zero.
                                EXPECT_EQ(0, rgb[c], msg + " (expected 0 back)");
                            else
                                // Allow a fair amount of slop, since there's an sRGB
                                // conversion before quantization to 8-bits...
                                EXPECT_LT(std.abs(delta), .02, msg);
                        }
                    }
                }

            // Clean up
            EXPECT_TRUE(RemoveFile(filename));
        }

        [TEST("ImageIO", "RoundTripEXR")]
        public static void ImageIO_RoundTripEXR() { TestRoundTrip("out.exr"); }

        [TEST("ImageIO", "RoundTripPFM")]
        public static void ImageIO_RoundTripPFM() { TestRoundTrip("out.pfm"); }

        [TEST("ImageIO", "RoundTripPNG")]
        public static void ImageIO_RoundTripPNG() { TestRoundTrip("out.png"); }

        [TEST("ImageIO", "RoundTripQOI")]
        public static void ImageIO_RoundTripQOI() { TestRoundTrip("out.qoi"); }
    }
}
