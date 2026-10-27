// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// cmd/imgtool.cpp

#if PBRT_FLOAT_AS_DOUBLE
global using Float = System.Double;
#else
global using Float = System.Single;
#endif
global using int32_t = System.Int32;
global using uint32_t = System.UInt32;
global using int64_t = System.Int64;
global using uint64_t = System.UInt64;
global using uint8_t = System.Byte;
global using size_t = System.UInt64;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using pbrt;
using static global::pbrt.pbrt;

public sealed class CommandUsage
{
    public CommandUsage(string usage, string description, string options)
    {
        this.usage = usage;
        this.description = description;
        this.options = options;
    }
    public string usage;
    public string description;
    public string options;
}

public static unsafe partial class ImgToolMain
{
    // std::map<std::string, CommandUsage>: ordered by key.
    static SortedDictionary<string, CommandUsage> commandUsage = MakeCommandUsage();

    static SortedDictionary<string, CommandUsage> MakeCommandUsage()
    {
        SortedDictionary<string, CommandUsage> commandUsage = new SortedDictionary<string, CommandUsage>(StringComparer.Ordinal);
        commandUsage["assemble"] = new CommandUsage("assemble [options] <filenames...>", "Assemble multiple images representing cropped regions of a larger\n    image into a single composite image.", "\n    --outfile <name>   Output image filename.\n");
        commandUsage["average"] = new CommandUsage("average [options] <filename base>", "Generate an image that is the result of averaging multiple images.\n    The constituent images are specified with a path that gives their\n    prefix.", "\n    --outfile <name>   Output image filename.\n");
        commandUsage["cat"] = new CommandUsage("cat [options] <filename>", "Print the pixel values of the specified image to standard output.", "\n    --csv              Output pixel values in CSV format.\n    --list             Output pixel values in a brace-delimited list\n                       (Mathematica-compatible).\n    --sort             Sort output by pixel luminance.\n");
        commandUsage["bloom"] = new CommandUsage("bloom [options] <filename>", "Apply a bloom effect to the specified image where pixels that are\n    brighter than a threshold are blurred and added to nearby pixels.", "\n    --iterations <n>   Number of filtering iterations used to generate the bloom\n                       image. Default: 5\n    --level <n>        Minimum RGB value for a pixel for it to contribute to bloom.\n                       Default: Infinity (i.e., no bloom is applied)\n    --outfile <name>   Output image filename.\n    --scale <s>        Amount by which the bloom image is scaled before being\n                       added to the original image. Default: 0.3\n    --width <w>        Width of Gaussian used to generate bloom images.\n                       Default: 15\n");
        commandUsage["convert"] = new CommandUsage("convert [options] <filename>", "Convert an image, possibly going from one format to another.\n    A variety of image processing operations can be performed as well.", "\n    --aces-filmic      Apply the ACES filmic s-curve to map values to [0,1].\n    --bw               Convert to black and white (average channels)\n    --channels <names> Process the provided comma-delineated set of channels.\n                       Default: R,G,B.\n    --clamp <value>    Rescale pixel components if necessary so that they do not\n                       exceed <value>. Default: no clamping.\n    --crop <x0,x1,y0,y1> Crop image to the given dimensions. Default: no crop.\n    --colorspace <n>   Convert image to given colorspace.\n                       (Options: \"ACES2065-1\", \"Rec2020\", \"DCI-P3\", \"sRGB\")\n    --despike <v>      For any pixels with a luminance value greater than <v>,\n                       replace the pixel with the median of the 3x3 neighboring\n                       pixels. Default: infinity (i.e., disabled).\n    --flipy            Flip the image along the y axis\n    --fp16             Convert image to fp16 pixel format.\n    --gamma <v>        Apply a gamma curve with exponent v. (Default: 1 (none)).\n    --maxluminance <n> Luminance value mapped to white by tonemapping.\n                       Default: 1\n    --outfile <name>   Output image filename.\n    --preservecolors   By default, out-of-gammut colors have each component\n                       clamped to [0,1] when written to non-HDR formats. With\n                       this option enabled, such colors are scaled by their\n                       maximum component, which preserves the relative ratio\n                       between RGB components.\n    --repeatpix <n>    Repeat each pixel value n times in both directions\n    --scale <scale>    Scale pixel values by given amount\n    --tonemap          Apply tonemapping to the image (Reinhard et al.'s\n                       photographic tone mapping operator)\n");
        commandUsage["diff"] = new CommandUsage("diff [options] <filename>", "Compute the difference between an image and a reference image\n    according to a variety of metrics and optionally output a\n    difference image.", "\n    --channels <names> Comma-separated list of channels to include in comparison.\n                       Default: R,G,B.\n    --crop <x0,x1,y0,y1> Crop images before performing diff.\n    --difftol <v>      Acceptable image difference percentage before differences\n                       are reported. Default: 0\n    --metric <name>    Error metric to use. (Options: \"MAE\", \"MSE\", \"MRSE\", \"FLIP\")\n    --outfile <name>   Filename to use for saving an image that encodes the\n                       absolute value of per-pixel differences.\n    --reference <name> Filename for reference image\n");
        commandUsage["error"] = new CommandUsage("error [options] <filename prefix>", "Compute the average error of a set of images with respect to a\n   reference image. All image files starting with <filename prefix>\n    are used to compute error.", "\n    --crop <x0,x1,y0,y1> Crop images before performing diff.\n    --errorfile <name>   Output average error image.\n    --metric <name>      Error metric to use. (Options: \"MAE\", MSE\", \"MRSE\")\n    --reference <name>   Reference image filename.\n");
        commandUsage["falsecolor"] = new CommandUsage("falsecolor [options] <filename>", "Generate a false color image encoding the magnitude of the\n    average of the channels in each pixel in the provided image.", "\n    --maxvalue <v>     Value to map to the last value in the color ramp.\n                       (Default: maximum pixel value in the image.)\n    --outfile <name>   Filename for output image.\n    --plusminus        Visualize green > 0, red < 0.\n    --ramp             Generate an image of the color ramp; <filename> is ignored\n                       if specified.\n");
        commandUsage["info"] = new CommandUsage("info <filename>", "Prints out image information including resolution, colorspace and pixel stats", "\n    (No options)\n");
        commandUsage["makeequiarea"] = new CommandUsage("makeequiarea [options] <filename>", "Convert a equirectangular environment map (as used in pbrt-v3)\n    to an equi-area parameterization (as used in pbrt-v4).", "\n    --outfile <name>   Filename of lat-long (equirect) environment map image.\n    --resolution <n>   Resolution of output image. Default: calculated\n                       from resolution of provided image.\n");
        commandUsage["makeemitters"] = new CommandUsage("makeemitters [options] <filename>", "Generate the description of a small quadrilateral emitter for\n    every pixel in the image.", "\n    --downsample <n>   Downsample the image by a factor of n in both dimensions\n                       (using simple box filtering). Default: 1.\n");
        commandUsage["makesky"] = new CommandUsage("makesky [options] <filename>", "Generate an environment map based on the Hosek-Wilkie sky model.", "\n    --albedo <a>       Albedo of ground-plane (range 0-1). Default: 0.5\n    --elevation <e>    Elevation of the sun in degrees (range 0-90). Default: 10\n    --outfile <name>   Filename to store environment map in.\n    --turbidity <t>    Atmospheric turbidity (range 1.7-10). Default: 3\n    --resolution <r>   Resolution of generated environment map. Default: 2048\n");
        commandUsage["splitn"] = new CommandUsage("splitn [options] <filenames>", "Generate a composite image from the specified images by taking\n    a strip of pixels from each image, in the order given. An additional\n    image of cropped regions of each of the images may be optionally\n    generated as well.", "\n    --crop <x,y>       Upper-left coordinate of one of the crops. May be specified\n                       multiple times.\n    --cropsize <n>     Pixel extent of crops in x and y. Default: 96\n    --outfile <name>   Filename to store final image in. Crop images are stored in\n                       the file \"crop-<name>\".\n");
        commandUsage["scalenormalmap"] = new CommandUsage("scalenormalmap [options] <filename>", "Scale the provided normal map by applying the given factor for x and y\n    and output the resulting normal map.", "\n    --scale <s>        Scale factor. Default: 1\n    --outfile <name>   Filename to store final image in.\n");
        commandUsage["whitebalance"] = new CommandUsage("whitebalance [options] <filename>", "Apply white balancing to the specified image.", "\n    --illuminant <n>   Apply white balance for the given standard illuminant\n                       (e.g. D65, D50, A, F1, F2, ...)\n    --outfile <name>   Filename to store result image.\n    --primaries <x,y>  Apply white balance for the primaries (x,y).\n    --temperature <T>  Apply white balance for a color temperature T.\n");
        return commandUsage;
    }

    static void usage(string cmd, string msg = null)
    {
        if (msg != null)
        {
            Console.Error.Write("imgtool " + cmd + ": ");
            Console.Error.Write(msg);
            Console.Error.Write("\n\n");
        }

        CommandUsage cu;
        bool found = commandUsage.TryGetValue(cmd, out cu);
        CHECK(found);
        Console.Error.Write("usage: imgtool " + cu.usage + "\n\n");
        if (cu.options.Length != 0)
            Console.Error.Write("options:" + cu.options + "\n");

        Environment.Exit(1);
    }

    static void help()
    {
        Console.Error.Write("usage: imgtool <command> [options]\n\n");
        Console.Error.Write("where <command> is:\n\n");
        foreach (KeyValuePair<string, CommandUsage> cmd in commandUsage)
            Console.Error.Write(cmd.Key + ": " + cmd.Value.description + "\n\n");
        Console.Error.Write("\n");
        Console.Error.Write("\"imgtool help <command>\" provides detailed information " +
                            "about <command>.\n");
    }

    static int help(List<string> args)
    {
        if (args.Count == 0)
        {
            help();
            return 0;
        }
        foreach (string cmd in args)
        {
            CommandUsage cu;
            if (!commandUsage.TryGetValue(cmd, out cu))
            {
                Console.Error.Write("imgtool help: command \"" + cmd + "\" not known.\n");
                help();
                return 1;
            }
            else
            {
                Console.Error.Write("usage: imgtool " + cu.usage + "\n\n");
                Console.Error.Write("options:" + cu.options + "\n");
            }
        }
        return 0;
    }

    static bool checkImageCompatibility(string fn1, Image im1, RGBColorSpace cs1, string fn2,
                                        Image im2, RGBColorSpace cs2)
    {
        if (im1.Resolution() != im2.Resolution())
        {
            Error(StringPrintf("%s: image resolution (%d, %d) doesn't match \"%s\" (%d, %d).", fn1,
                  im1.Resolution().x, im1.Resolution().y, fn2,
                  im2.Resolution().x, im2.Resolution().y));
            return false;
        }
        if (im1.NChannels() != im2.NChannels())
        {
            Error(StringPrintf("%s: image channel count %d doesn't match \"%s\", %d.", fn1,
                  im1.NChannels(), fn2, im2.NChannels()));
            return false;
        }
        if (!im1.ChannelNames().SequenceEqual(im2.ChannelNames()))
        {
            Func<List<string>, string> print = (List<string> n) =>
            {
                string s = n[0];
                for (int i = 1; i < n.Count; ++i)
                {
                    s += ", ";
                    s += n[i];
                }
                return s;
            };
            Error(StringPrintf("%s: warning: image channel names \"%s\" don't match \"%s\" with \"%s\".",
                  fn1, print(im1.ChannelNames()), fn2, print(im2.ChannelNames())));
            return false;
        }

        if (cs1 != cs2)
        {
            Error(StringPrintf("%s: color space does not match \"%s\".", fn1, fn2));
            return false;
        }

        return true;
    }

    static pstd.span<Float> S(params Float[] v) { return new pstd.span<Float>(v); }

    static int makesky(List<string> args)
    {
        string outfile = "";
        Float albedo = 0.5f;
        Float turbidity = 3.0f;
        Float elevation = 10;
        int resolution = 2048;

        Action<string> onError = (string err) =>
        {
            usage("makesky", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outfile, onError) ||
                ParseArg(args, ref iter, end, "albedo", ref albedo, onError) ||
                ParseArg(args, ref iter, end, "turbidity", ref turbidity, onError) ||
                ParseArg(args, ref iter, end, "elevation", ref elevation, onError) ||
                ParseArg(args, ref iter, end, "resolution", ref resolution, onError))
            {
                // success
            }
            else
                onError(StringPrintf("argument %s invalid", args[iter]));
        }

        if (outfile.Length == 0)
            usage("makesky", "--outfile must be specified");
        if (albedo < 0.0 || albedo > 1.0)
            usage("makesky", "--albedo must be between 0 and 1");
        if (turbidity < 1.7 || turbidity > 10.0)
            usage("makesky", "--turbidity must be between 1.7 and 10.");
        if (elevation < 0.0 || elevation > 90.0)
            usage("makesky", "--elevation must be between 0. and 90.");
        elevation = Radians(elevation);
        if (resolution < 1)
            usage("makesky", "--resolution must be >= 1");

        // Vector pointing at the sun. Note that elevation is measured from the
        // horizon--not the zenith, as it is elsewhere in pbrt.
        Vector3f sunDir = new Vector3f(0.0f, std.cos(elevation), std.sin(elevation));

        Image img = new Image(PixelFormat.Float, new Point2i(resolution, resolution), new List<string> { "R", "G", "B" });

        // They assert wavelengths are in this range...
        int nLambda = 1 + (720 - 320) / 32;
        Float[] lambda = new Float[nLambda];
        for (int i = 0; i < nLambda; ++i)
            lambda[i] = Lerp(i / (Float)(nLambda - 1), 320, 720);

        // Assume a uniform spectral albedo
        ArHosekSkyModelState skymodel_state =
            ArHosekSkyModel.arhosekskymodelstate_alloc_init(elevation, turbidity, albedo);

        RGBColorSpace colorSpace = RGBColorSpace.ACES2065_1;

        ParallelFor(0, resolution, (long start, long end2) =>
        {
            Float[] skyv = new Float[lambda.Length];
            for (long iy = start; iy < end2; ++iy)
            {
                Float y = (iy + 0.5f) / resolution;
                for (int ix = 0; ix < resolution; ++ix)
                {
                    Float x = (ix + 0.5f) / resolution;
                    Vector3f v = EqualAreaSquareToSphere(new Point2f(x, y));
                    if (v.z <= 0)
                        // downward hemisphere
                        continue;

                    Float theta = SphericalTheta(v);

                    // Compute the angle between the pixel's direction and the sun
                    // direction.
                    Float gamma = SafeACos(Dot(v, sunDir));
                    DCHECK(gamma >= 0 && gamma <= Pi);

                    for (int i = 0; i < lambda.Length; ++i)
                        skyv[i] = (Float)ArHosekSkyModel.arhosekskymodel_solar_radiance(skymodel_state, theta, gamma,
                                                                                     lambda[i]);

                    PiecewiseLinearSpectrum spec = new PiecewiseLinearSpectrum(new pstd.span<Float>(lambda),
                                                                               new pstd.span<Float>(skyv));
                    XYZ xyz = SpectrumToXYZ(spec);
                    RGB rgb = colorSpace.ToRGB(xyz);

                    for (int c = 0; c < 3; ++c)
                        img.SetChannel(new Point2i(ix, (int)iy), c, rgb[c]);
                }
            }
        });

        ImageMetadata metadata = new ImageMetadata();
        metadata.colorSpace = new pstd.optional<RGBColorSpace>(colorSpace);
        SortedDictionary<string, List<string>> stringVectors = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        stringVectors["makesky.albedo"] = new List<string> { albedo.ToString("F6", CultureInfo.InvariantCulture) };
        stringVectors["makesky.elevation"] = new List<string> { elevation.ToString("F6", CultureInfo.InvariantCulture) };
        stringVectors["makesky.turbidity"] = new List<string> { turbidity.ToString("F6", CultureInfo.InvariantCulture) };
        metadata.stringVectors = stringVectors;
        CHECK(img.Write(outfile, metadata));

        return 0;
    }

    static int assemble(List<string> args)
    {
        if (args.Count == 0)
            usage("assemble", "no filenames provided to \"assemble\"?");
        string outfile = "";
        List<string> infiles = new List<string>();

        Action<string> onError = (string err) => { usage("assemble", err); };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outfile, onError))
                ;  // success
            else if (args[iter][0] == '-')
                usage("assemble", StringPrintf("%s: unknown command flag", args[iter]));
            else
            {
                infiles.Add(args[iter]);
            }
        }

        if (outfile.Length == 0)
            usage("assemble", "--outfile not provided for \"assemble\"");

        Image fullImage = new Image();
        List<bool> seenPixel = new List<bool>();
        int seenMultiple = 0;
        Bounds2i fullBounds = new Bounds2i();
        RGBColorSpace colorSpace = null;
        foreach (string file in infiles)
        {
            if (!HasExtension(file, "exr"))
                usage("assemble", "only EXR images include the image bounding boxes that " +
                                  "\"assemble\" needs.");

            ImageAndMetadata im = Image.Read(file);
            Image image = im.image;
            ImageMetadata metadata = im.metadata;

            if (!metadata.fullResolution)
            {
                Error(StringPrintf("%s: doesn't have full resolution in image metadata. Skipping.", file));
                continue;
            }
            if (!metadata.pixelBounds)
            {
                Error(StringPrintf("%s: doesn't have pixel bounds in image metadata. Skipping.", file));
                continue;
            }

            if (fullImage.Resolution() == new Point2i(0, 0))
            {
                // First image read.
                fullImage = new Image(image.Format(), metadata.fullResolution.value(),
                                      image.ChannelNames(), image.Encoding());
                colorSpace = metadata.GetColorSpace();
                seenPixel = new List<bool>(new bool[fullImage.Resolution().x * fullImage.Resolution().y]);
                fullBounds = new Bounds2i(new Point2i(0, 0), fullImage.Resolution());
            }
            else
            {
                // Make sure that this image's info is compatible with the
                // first image's.
                if (metadata.fullResolution.value() != fullImage.Resolution())
                {
                    Warning(StringPrintf("%s: full resolution (%d, %d) in EXR file doesn't match the full " +
                            "resolution of first EXR file (%d, %d). " +
                            "Ignoring this file.",
                            file, metadata.fullResolution.value().x, metadata.fullResolution.value().y,
                            fullImage.Resolution().x, fullImage.Resolution().y));
                    continue;
                }
                if (Union(metadata.pixelBounds.value(), fullBounds) != fullBounds)
                {
                    Warning(StringPrintf("%s: pixel bounds (%d, %d) - (%d, %d) in EXR file isn't inside " +
                            "the full image (0, 0) - (%d, %d). " +
                            "Ignoring this file.",
                            file, metadata.pixelBounds.value().pMin.x, metadata.pixelBounds.value().pMin.y,
                            metadata.pixelBounds.value().pMax.x, metadata.pixelBounds.value().pMax.y,
                            fullBounds.pMax.x, fullBounds.pMax.y));
                    continue;
                }
                if (fullImage.NChannels() != image.NChannels())
                {
                    Warning(StringPrintf("%s: %d channel image; expecting %d channels.", file,
                            image.NChannels(), fullImage.NChannels()));
                    continue;
                }
                RGBColorSpace cs = metadata.GetColorSpace();
                if (cs != colorSpace)
                {
                    Warning(StringPrintf(
                        "%s: color space (%s) doesn't match first image's color space (%s).",
                        file, cs.ToString(), colorSpace.ToString()));
                    continue;
                }
            }

            // Copy pixels.
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Point2i fullp = new Point2i(x + metadata.pixelBounds.value().pMin.x,
                                                y + metadata.pixelBounds.value().pMin.y);
                    CHECK(InsideExclusive(fullp, fullBounds));
                    int fullOffset = fullp.x + fullp.y * fullImage.Resolution().x;
                    if (seenPixel[fullOffset])
                        ++seenMultiple;
                    seenPixel[fullOffset] = true;
                    for (int c = 0; c < fullImage.NChannels(); ++c)
                        fullImage.SetChannel(fullp, c, image.GetChannel(new Point2i(x, y), c));
                }
        }

        int unseenPixels = 0;
        for (int y = 0; y < fullImage.Resolution().y; ++y)
            for (int x = 0; x < fullImage.Resolution().x; ++x)
                if (!seenPixel[y * fullImage.Resolution().x + x])
                    ++unseenPixels;

        if (seenMultiple > 0)
            Warning(StringPrintf("%s: %d pixels present in multiple images.", outfile, seenMultiple));
        if (unseenPixels > 0)
            Warning(StringPrintf("%s: %d pixels not present in any images.", outfile, unseenPixels));

        ImageMetadata outMetadata = new ImageMetadata();
        outMetadata.colorSpace = new pstd.optional<RGBColorSpace>(colorSpace);

        fullImage.Write(outfile, outMetadata);

        return 0;
    }

    static void drawBox(Bounds2i b, int borderWidth, RGB rgb, Image result)
    {
        ImageChannelDesc desc = result.GetChannelDesc(new List<string> { "R", "G", "B" });
        CHECK((bool)desc);

        for (int y = b.pMin.y - borderWidth; y < b.pMin.y; ++y)
            for (int x = b.pMin.x - borderWidth; x < b.pMax.x + borderWidth; ++x)
                for (int ch = 0; ch < 3; ++ch)
                    result.SetChannels(new Point2i(x, y), desc, S(rgb.r, rgb.g, rgb.b));
        for (int y = b.pMax.y; y < b.pMax.y + borderWidth; ++y)
            for (int x = b.pMin.x - borderWidth; x < b.pMax.x + borderWidth; ++x)
                for (int ch = 0; ch < 3; ++ch)
                    result.SetChannels(new Point2i(x, y), desc, S(rgb.r, rgb.g, rgb.b));
        for (int x = b.pMin.x - borderWidth; x < b.pMin.x; ++x)
            for (int y = b.pMin.y; y < b.pMax.y; ++y)
                for (int ch = 0; ch < 3; ++ch)
                    result.SetChannels(new Point2i(x, y), desc, S(rgb.r, rgb.g, rgb.b));
        for (int x = b.pMax.x; x < b.pMax.x + borderWidth; ++x)
            for (int y = b.pMin.y; y < b.pMax.y; ++y)
                for (int ch = 0; ch < 3; ++ch)
                    result.SetChannels(new Point2i(x, y), desc, S(rgb.r, rgb.g, rgb.b));
    }

    static int splitn(List<string> args)
    {
        if (args.Count == 0)
            usage("splitn", "no filenames provided to \"splitn\"?");
        string outfile = "";
        List<string> infiles = new List<string>();
        List<Bounds2i> crops = new List<Bounds2i>();
        int cropSize = 96;

        string crop = "";
        Action<string> onError = (string err) => { usage("splitn", err); };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outfile, onError) ||
                ParseArg(args, ref iter, end, "cropsize", ref cropSize, onError))
                ;  // success
            else if (ParseArg(args, ref iter, end, "crop", ref crop, onError))
            {
                List<int> c = SplitStringToInts(crop, ',');
                if (c.Count != 2)
                    usage("splitn", "2 values not provided to --crop");
                crops.Add(new Bounds2i(new Point2i(c[0], c[1]), new Point2i(c[0] + cropSize, c[1] + cropSize)));
            }
            else if (args[iter][0] == '-')
                usage("splitn", StringPrintf("%s: unknown command flag", args[iter]));
            else
            {
                infiles.Add(args[iter]);
            }
        }

        if (outfile.Length == 0)
            usage("splitn", "--outfile not provided for \"splitn\"");

        List<Image> images = new List<Image>();
        RGBColorSpace colorSpace = null;
        foreach (string file in infiles)
        {
            ImageAndMetadata im = Image.Read(file);
            Image image = im.image;

            if (colorSpace == null)
                colorSpace = im.metadata.GetColorSpace();
            else if (!checkImageCompatibility(infiles[0], images[0], colorSpace, file,
                                              image, im.metadata.GetColorSpace()))
                return 0;  // C++: return false

            images.Add(image);
        }

        Image result = new Image(images[0].Format(), images[0].Resolution(),
                                 images[0].ChannelNames(), images[0].Encoding());
        float m = 15.0f;
        int pad = 6;
        for (int y = 0; y < result.Resolution().y; ++y)
        {
            int x = 0;
            for (int im = 0; im < images.Count; ++im)
            {
                if (im == images.Count - 1)
                    for (; x < result.Resolution().x; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, images[im].GetChannel(new Point2i(x, y), c));
                else
                {
                    int x1 = (int)((float)(im + 1) / (float)images.Count * result.Resolution().x +
                              (2 * ((float)y / (float)result.Resolution().y) - 1) *
                                  result.Resolution().x / -m);

                    for (; x < x1 - pad / 2; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, images[im].GetChannel(new Point2i(x, y), c));
                    for (; x < x1 + pad / 2; ++x)
                        for (int c = 0; c < result.NChannels(); ++c)
                            result.SetChannel(new Point2i(x, y), c, 0);
                }
            }
        }

        // Crops
        RGB[] edges = { new RGB(0.8f, .15f, .15f), new RGB(.15f, 0.8f, .15f), new RGB(.15f, .15f, 0.8f) };
        int borderWidth = 5;
        int xCropRes =
            (cropSize + 2 * borderWidth) * images.Count + pad * (images.Count - 1);
        int yCropRes = (cropSize + 2 * borderWidth) * crops.Count + pad * (crops.Count - 1);
        if (xCropRes < 1)
            xCropRes = 1;
        if (yCropRes < 1)
            yCropRes = 1;
        Image cropsImage = new Image(PixelFormat.Float, new Point2i(xCropRes, yCropRes), new List<string> { "R", "G", "B" });
        for (int y = 0; y < yCropRes; ++y)
            for (int x = 0; x < xCropRes; ++x)
                for (int c = 0; c < 3; ++c)
                    cropsImage.SetChannel(new Point2i(x, y), c, 1.0f);

        CHECK_LE((ulong)crops.Count, (ulong)edges.Length);
        ImageChannelDesc cropDesc = cropsImage.GetChannelDesc(new List<string> { "R", "G", "B" });
        CHECK((bool)cropDesc);

        for (int c = 0; c < crops.Count; ++c)
        {
            Bounds2i crp = crops[c];
            drawBox(crp, borderWidth, edges[c], result);

            // extract crops from images
            for (int i = 0; i < images.Count; ++i)
            {
                // output start point
                int y0 = c * (cropSize + 2 * borderWidth + pad);
                int x0 = i * (cropSize + 2 * borderWidth + pad);
                drawBox(new Bounds2i(new Point2i(x0 + borderWidth, y0 + borderWidth),
                                     new Point2i(x0 + borderWidth + cropSize, y0 + borderWidth + cropSize)),
                        borderWidth, edges[c], cropsImage);

                ImageChannelDesc imageDesc = images[i].GetChannelDesc(new List<string> { "R", "G", "B" });
                CHECK((bool)imageDesc);
                foreach (Point2i p in crp)
                {
                    Point2i pOut =
                        new Point2i(x0 + borderWidth, y0 + borderWidth) + (p - crp.pMin);
                    ImageChannelValues cv = images[i].GetChannels(p, imageDesc);
                    cropsImage.SetChannels(pOut, cropDesc, new pstd.span<Float>(cv.ToArray()));
                }
            }
        }

        // TODO: we could try to be more clever about carrying through more
        // metadata, when applicable.
        ImageMetadata outMetadata = new ImageMetadata();
        outMetadata.colorSpace = new pstd.optional<RGBColorSpace>(colorSpace);

        result.Write(outfile, outMetadata);
        if (crops.Count != 0)
            cropsImage.Write("crops-" + outfile, outMetadata);

        return 0;
    }

    static int scalenormalmap(List<string> args)
    {
        string filename = "", outfile = "";
        Float scale = 1;

        Action<string> onError = (string err) =>
        {
            usage("error", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, end, "outfile", ref outfile, onError))
            {
                // success
            }
            else if (filename.Length == 0 && args[iter][0] != '-')
            {
                filename = args[iter];
            }
            else
                usage("error", StringPrintf("%s: unknown argument", args[iter]));
        }

        if (filename.Length == 0)
            usage("error", "Must provide base filename.");

        ImageAndMetadata im = Image.Read(filename);
        Image image = im.image;
        ImageChannelDesc rgbDesc = image.GetChannelDesc(new List<string> { "R", "G", "B" });
        if (!rgbDesc)
        {
            Error(StringPrintf("%s: doesn't have R, G, B channels.", filename));
            return 1;
        }

        Image scaledImage = image.Clone();
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                ImageChannelValues rgb = image.GetChannels(new Point2i(x, y), rgbDesc);
                for (int c = 0; c < 3; ++c)
                    rgb[c] = 2 * rgb[c] - 1;

                rgb[0] *= scale;
                rgb[1] *= scale;
                rgb[2] = SafeSqrt(1 - Sqr(rgb[0]) - Sqr(rgb[1]));

                for (int c = 0; c < 3; ++c)
                    rgb[c] = (rgb[c] + 1) / 2;

                scaledImage.SetChannels(new Point2i(x, y), rgbDesc, new pstd.span<Float>(rgb.ToArray()));
            }

        if (!scaledImage.Write(outfile, im.metadata))
            return 1;

        return 0;
    }

    static int cat(List<string> args)
    {
        if (args.Count == 0)
            usage("cat", "no filenames provided to \"cat\"?");
        bool sort = false;
        bool csv = false;
        bool list = false;

        for (int iter = 0; iter < args.Count; ++iter)
        {
            if (args[iter] == "--sort" || args[iter] == "-sort")
            {
                sort = !sort;
                continue;
            }
            else if (args[iter] == "--csv" || args[iter] == "-csv")
            {
                csv = !csv;
                continue;
            }
            else if (args[iter] == "--list" || args[iter] == "-list")
            {
                list = !list;
                continue;
            }

            if (sort && csv)
            {
                Console.Error.Write("imgtool: --sort and --csv don't make sense to use " +
                                    "together.\n");
                return 1;
            }
            if (sort && list)
            {
                Console.Error.Write("imgtool: --sort and --list don't make sense to " +
                                    "use together.\n");
                return 1;
            }

            ImageAndMetadata im = Image.Read(args[iter]);
            ImageMetadata metadata = im.metadata;
            Image image = im.image;

            Bounds2i pixelBounds = metadata.pixelBounds ? metadata.pixelBounds.value()
                                                        : new Bounds2i(new Point2i(0, 0), image.Resolution());
            if (sort)
            {
                List<KeyValuePair<Point2i, ImageChannelValues>> sorted = new List<KeyValuePair<Point2i, ImageChannelValues>>();
                foreach (Point2i p in pixelBounds)
                {
                    ImageChannelValues v = image.GetChannels(new Point2i(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y));
                    sorted.Add(new KeyValuePair<Point2i, ImageChannelValues>(p, v));
                }

                // (std::sort is not stable; ties here may be ordered differently than in C++.)
                sorted = sorted.OrderBy((KeyValuePair<Point2i, ImageChannelValues> a) => a.Value.Average()).ToList();
                foreach (KeyValuePair<Point2i, ImageChannelValues> v in sorted)
                {
                    ImageChannelValues values = v.Value;
                    if (!csv)
                        Printf("(%d, %d): ", v.Key.x, v.Key.y);
                    for (int i = 0; i < (int)values.size(); ++i)
                        Printf("%f%c", values[i], (i == (int)values.size() - 1) ? '\n' : ',');
                }
            }
            else
            {
                if (list)
                {
                    CHECK_EQ(image.NChannels(), 1);
                    for (int y = pixelBounds.pMin.y; y < pixelBounds.pMax.y; ++y)
                    {
                        for (int x = pixelBounds.pMin.x; x < pixelBounds.pMax.x; ++x)
                            Console.Out.Write(CF(
                                       image.GetChannel(
                                       new Point2i(x - pixelBounds.pMin.x, y - pixelBounds.pMin.y), 0)) + " ");
                        Printf("\n");
                    }
                }
                else
                {
                    foreach (Point2i p in pixelBounds)
                    {
                        ImageChannelValues values = image.GetChannels(
                            new Point2i(p.x - pixelBounds.pMin.x, p.y - pixelBounds.pMin.y));
                        if (!csv)
                            Printf("(%d, %d): ", p.x, p.y);
                        for (int i = 0; i < (int)values.size(); ++i)
                            Printf("%f%c", values[i], (i == (int)values.size() - 1) ? '\n' : ',');
                    }
                }
            }
        }
        return 0;
    }

    static int average(List<string> args)
    {
        string avgFile = "", filenameBase = "";

        Action<string> onError = (string err) =>
        {
            usage("average", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref avgFile, onError))
            {
                // success
            }
            else if (filenameBase.Length == 0 && args[iter][0] != '-')
            {
                filenameBase = args[iter];
            }
            else
                usage("average", StringPrintf("%s: unknown argument", args[iter]));
        }

        if (filenameBase.Length == 0)
            usage("average", "must provide base filename.");
        if (avgFile.Length == 0)
            usage("average", "must provide --outfile.");

        List<string> filenames = MatchingFilenames(filenameBase);
        if (filenames.Count == 0)
        {
            Error(StringPrintf("%s: no matching filenames!", filenameBase));
            return 1;
        }

        // Compute average image
        ThreadLocal<Image> avgImages = new ThreadLocal<Image>(() => new Image());
        ThreadLocal<RGBColorSpace> colorSpaces = new ThreadLocal<RGBColorSpace>(() => null);
        bool failed = false;

        ParallelFor(0, filenames.Count, (long i) =>
        {
            ImageAndMetadata imRead = Image.Read(filenames[(int)i]);
            Image im = imRead.image;

            ref Image avg = ref avgImages.Get();
            if (avg.Resolution() == new Point2i(0, 0))
            {
                avg = new Image(PixelFormat.Float, im.Resolution(), im.ChannelNames());
                colorSpaces.Get() = imRead.metadata.GetColorSpace();
            }
            else if (!checkImageCompatibility(filenames[(int)i], im,
                                              imRead.metadata.GetColorSpace(), filenames[0],
                                              avg, colorSpaces.Get()))
            {
                failed = true;
                return;
            }

            for (int y = 0; y < avg.Resolution().y; ++y)
                for (int x = 0; x < avg.Resolution().x; ++x)
                    for (int c = 0; c < avg.NChannels(); ++c)
                    {
                        Float v = im.GetChannel(new Point2i(x, y), c) / filenames.Count;
                        if (std.isnan(v))
                            LOG_FATAL(StringPrintf("NAN Pixel at %s in %s", new Point2f(x, y), filenames[(int)i]));
                        if (std.isinf(v))
                            v = 0;
                        avg.SetChannel(new Point2i(x, y), c, avg.GetChannel(new Point2i(x, y), c) + v);
                    }
        });

        if (failed)
            return 1;

        // Average per-thread average images
        Image avgImage = new Image();
        avgImages.ForAll((Image im) =>
        {
            CHECK_NE(im.Resolution(), new Point2i(0, 0));
            if (avgImage.Resolution() == new Point2i(0, 0))
            {
                // First valid one
                avgImage = im.Clone();
            }
            else
            {
                for (int y = 0; y < avgImage.Resolution().y; ++y)
                    for (int x = 0; x < avgImage.Resolution().x; ++x)
                        for (int c = 0; c < avgImage.NChannels(); ++c)
                        {
                            Float v = im.GetChannel(new Point2i(x, y), c);
                            if (!std.isinf(v))
                                avgImage.SetChannel(new Point2i(x, y), c,
                                                    avgImage.GetChannel(new Point2i(x, y), c) + v);
                        }
            }
        });

        ImageMetadata avgMetadata = new ImageMetadata();
        colorSpaces.ForAll((RGBColorSpace cs) =>
        {
            // Will redundantly set it, but whatever.
            avgMetadata.colorSpace = new pstd.optional<RGBColorSpace>(cs);
        });

        if (!avgImage.Write(avgFile, avgMetadata))
            return 1;

        return 0;
    }

    static int error(List<string> args)
    {
        string referenceFile = "", errorFile = "", metric = "MSE";
        string filenameBase = "";
        int[] cropWindow = { -1, 0, -1, 0 };

        Action<string> onError = (string err) =>
        {
            usage("error", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "reference", ref referenceFile, onError) ||
                ParseArg(args, ref iter, end, "errorfile", ref errorFile, onError) ||
                ParseArg(args, ref iter, end, "metric", ref metric, onError) ||
                ParseArg(args, ref iter, end, "crop", new pstd.span<int>(cropWindow), onError))
            {
                // success
            }
            else if (filenameBase.Length == 0 && args[iter][0] != '-')
            {
                filenameBase = args[iter];
            }
            else
                usage("error", StringPrintf("%s: unknown argument", args[iter]));
        }

        if (filenameBase.Length == 0)
            usage("error", "Must provide base filename.");
        if (metric != "MSE" && metric != "MRSE" && metric != "MAE")
            usage("error", StringPrintf("%s: --metric must be \"MAE\", \"MSE\", or \"MRSE\".",
                  metric));

        List<string> filenames = MatchingFilenames(filenameBase);
        if (filenames.Count == 0)
        {
            Error(StringPrintf("%s: no matching filenames!", filenameBase));
            return 1;
        }

        if (referenceFile.Length == 0)
            usage("error", "must provide --reference file.");
        ImageAndMetadata rf = Image.Read(referenceFile);
        Image referenceImage = rf.image;

        // If last 2 are negative, they're taken as deltas
        if (cropWindow[1] < 0)
            cropWindow[1] = cropWindow[0] - cropWindow[1];
        if (cropWindow[3] < 0)
            cropWindow[3] = cropWindow[2] - cropWindow[3];
        Func<Image, Image> crop = (Image image) =>
        {
            if (cropWindow[0] >= 0 && cropWindow[2] >= 0)
                image = image.Crop(
                    new Bounds2i(new Point2i(cropWindow[0], cropWindow[2]), new Point2i(cropWindow[1], cropWindow[3])));
            return image;
        };

        referenceImage = crop(referenceImage);

        // Compute error and error image
        // (using MultiChannelVarianceEstimator = std::vector<VarianceEstimator<double>>)
        ThreadLocal<Array2D<VarianceEstimator<double>[]>> pixelVariances = new ThreadLocal<Array2D<VarianceEstimator<double>[]>>(() =>
        {
            Array2D<VarianceEstimator<double>[]> amcve = new Array2D<VarianceEstimator<double>[]>(referenceImage.Resolution().x,
                                                                                                  referenceImage.Resolution().y);
            for (int y = 0; y < referenceImage.Resolution().y; ++y)
                for (int x = 0; x < referenceImage.Resolution().x; ++x)
                    amcve[x, y] = new VarianceEstimator<double>[referenceImage.NChannels()];
            return amcve;
        });

        ThreadLocal<double> sumErrors = new ThreadLocal<double>(() => 0.0);
        int[] spp = new int[filenames.Count];
        ParallelFor(0, filenames.Count, (long i) =>
        {
            ImageAndMetadata imRead = Image.Read(filenames[(int)i]);
            Image im = imRead.image;
            im = crop(im);

            CHECK(imRead.metadata.samplesPerPixel.has_value());
            spp[i] = imRead.metadata.samplesPerPixel.value();

            Image diffImage = new Image();
            ImageChannelValues err = new ImageChannelValues((size_t)referenceImage.NChannels());
            if (metric == "MAE")
                err = im.MAE(im.AllChannelsDesc(), referenceImage, diffImage);
            else if (metric == "MSE")
                err = im.MSE(im.AllChannelsDesc(), referenceImage, diffImage);
            else
                err = im.MRSE(im.AllChannelsDesc(), referenceImage, diffImage);
            sumErrors.Get() += err.Average();

            Array2D<VarianceEstimator<double>[]> pv = pixelVariances.Get();
            for (int y = 0; y < im.Resolution().y; ++y)
                for (int x = 0; x < im.Resolution().x; ++x)
                {
                    VarianceEstimator<double>[] pixelVariance = pv[x, y];
                    for (int c = 0; c < im.NChannels(); ++c)
                        if (metric == "MRSE")
                            pixelVariance[c].Add(
                                im.GetChannel(new Point2i(x, y), c) /
                                (0.01f + referenceImage.GetChannel(new Point2i(x, y), c)));
                        else
                            pixelVariance[c].Add(im.GetChannel(new Point2i(x, y), c));
                }
        });

        for (int i = 1; i < filenames.Count; ++i)
        {
            if (spp[i] != spp[0])
            {
                Error(StringPrintf("%s: spp %d mismatch. %s has %d.", filenames[i], spp[i], filenames[0],
                      spp[0]));
                return 1;
            }
        }

        double sumError = 0.0;
        sumErrors.ForAll((double err) => { sumError += err; });

        Array2D<VarianceEstimator<double>[]> pixelVarianceTotal = new Array2D<VarianceEstimator<double>[]>(referenceImage.Resolution().x,
                                                                                                          referenceImage.Resolution().y);
        for (int y = 0; y < referenceImage.Resolution().y; ++y)
            for (int x = 0; x < referenceImage.Resolution().x; ++x)
                pixelVarianceTotal[x, y] = new VarianceEstimator<double>[referenceImage.NChannels()];

        pixelVariances.ForAll((Array2D<VarianceEstimator<double>[]> pixVar) =>
        {
            for (int y = 0; y < referenceImage.Resolution().y; ++y)
                for (int x = 0; x < referenceImage.Resolution().x; ++x)
                    for (int c = 0; c < referenceImage.NChannels(); ++c)
                        pixelVarianceTotal[x, y][c].Merge(pixVar[x, y][c]);
        });

        Image errorImage = new Image(PixelFormat.Float,
                                     new Point2i(referenceImage.Resolution().x, referenceImage.Resolution().y),
                                     new List<string> { metric });
        for (int y = 0; y < referenceImage.Resolution().y; ++y)
            for (int x = 0; x < referenceImage.Resolution().x; ++x)
            {
                Float varSum = 0;
                for (int c = 0; c < referenceImage.NChannels(); ++c)
                    varSum += (Float)pixelVarianceTotal[x, y][c].Variance();
                errorImage.SetChannel(new Point2i(x, y), 0, varSum / referenceImage.NChannels());
            }

        // MSE is the average over all of the pixels
        double errorV = sumError / (filenames.Count - 1);
        Printf("%s estimate = %.9g\n", metric, errorV);

        if (errorFile.Length != 0 && !errorImage.Write(errorFile))
        {
            return 1;
        }

        return 0;
    }

    static int diff(List<string> args)
    {
        string outFile = "", imageFile = "", referenceFile = "", metric = "MSE";
        string channels = "R,G,B";
        int[] cropWindow = { -1, 0, -1, 0 };

        Action<string> onError = (string err) =>
        {
            usage("diff", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, end, "reference", ref referenceFile, onError) ||
                ParseArg(args, ref iter, end, "metric", ref metric, onError) ||
                ParseArg(args, ref iter, end, "channels", ref channels, onError) ||
                ParseArg(args, ref iter, end, "crop", new pstd.span<int>(cropWindow), onError))
            {
                // success
            }
            else if (args[iter][0] == '-')
            {
                usage("diff", StringPrintf("%s: unknown command flag", args[iter]));
            }
            else if (imageFile.Length != 0)
            {
                usage("diff", StringPrintf("%s: excess argument", args[iter]));
            }
            else
            {
                imageFile = args[iter];
            }
        }

        if (imageFile.Length == 0)
            usage("diff", "must specify image to compute difference with.");

        if (referenceFile.Length == 0)
            usage("diff", "must specify --reference image");

        if (metric != "MAE" && metric != "MSE" && metric != "MRSE" && metric != "FLIP")
            usage("diff", StringPrintf("%s: --metric must be \"MAE\", \"MSE\", \"MRSE\", or \"FLIP\".",
                  metric));

        ImageAndMetadata refRead = Image.Read(referenceFile);
        Image refImage = refRead.image;
        ImageMetadata refMetadata = refRead.metadata;

        // If last 2 are negative, they're taken as deltas
        if (cropWindow[1] < 0)
            cropWindow[1] = cropWindow[0] - cropWindow[1];
        if (cropWindow[3] < 0)
            cropWindow[3] = cropWindow[2] - cropWindow[3];
        if (cropWindow[0] >= 0 && cropWindow[2] >= 0)
            refImage = refImage.Crop(
                new Bounds2i(new Point2i(cropWindow[0], cropWindow[2]), new Point2i(cropWindow[1], cropWindow[3])));

        ImageAndMetadata im = Image.Read(imageFile);
        Image image = im.image;

        // Select channels
        List<string> splitChannels = SplitString(channels, ',');
        ImageChannelDesc refDesc = refImage.GetChannelDesc(splitChannels);
        ImageChannelDesc imgDesc = image.GetChannelDesc(splitChannels);
        if (refDesc.size() != (size_t)splitChannels.Count)
        {
            Error(StringPrintf("%s: image does not have \"%s\" channels.", referenceFile, channels));
            return 1;
        }
        refImage = refImage.SelectChannels(refDesc);

        if (imgDesc.size() != (size_t)splitChannels.Count)
        {
            Error(StringPrintf("%s: image does not have \"%s\" channels.", imageFile, channels));
            return 1;
        }
        image = image.SelectChannels(imgDesc);

        // Crop before comparing resolutions.
        if (cropWindow[0] >= 0 && cropWindow[2] >= 0)
            image = image.Crop(
                new Bounds2i(new Point2i(cropWindow[0], cropWindow[2]), new Point2i(cropWindow[1], cropWindow[3])));

        if (!checkImageCompatibility(imageFile, image, im.metadata.GetColorSpace(),
                                     referenceFile, refImage, refMetadata.GetColorSpace()))
            return 1;

        // Clamp Infs
        int nClamped = 0, nRefClamped = 0;
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
                for (int c = 0; c < image.NChannels(); ++c)
                {
                    if (std.isinf(image.GetChannel(new Point2i(x, y), c)))
                    {
                        ++nClamped;
                        image.SetChannel(new Point2i(x, y), c, 0);
                    }
                    if (std.isinf(refImage.GetChannel(new Point2i(x, y), c)))
                    {
                        ++nRefClamped;
                        refImage.SetChannel(new Point2i(x, y), c, 0);
                    }
                }
        if (nClamped > 0)
            Warning(StringPrintf("%s: clamped %d infinite pixel values.", imageFile, nClamped));
        if (nRefClamped > 0)
            Warning(StringPrintf("%s: clamped %d infinite pixel values.", referenceFile, nRefClamped));

        // Image averages. Compute before FLIP potentially goes and clamps things...
        Float refAverage = refImage.Average(refImage.AllChannelsDesc()).Average();
        Float imageAverage = image.Average(image.AllChannelsDesc()).Average();

        Image errorImage = new Image();
        ImageChannelValues error = new ImageChannelValues((size_t)refImage.NChannels());
        if (metric == "MAE")
            error = image.MAE(image.AllChannelsDesc(), refImage, errorImage);
        else if (metric == "MSE")
            error = image.MSE(image.AllChannelsDesc(), refImage, errorImage);
        else if (metric == "MRSE")
            error = image.MRSE(image.AllChannelsDesc(), refImage, errorImage);
        else
        {
            // FLIP
            if (refImage.NChannels() != 3)
            {
                Error(StringPrintf("%s: only 3 channel images are currently supported for FLIP.",
                      referenceFile));
                return 1;
            }

            errorImage = new Image(PixelFormat.Float, image.Resolution(), new List<string> { "Error" });
            FLIPOptions opt = FLIPOptions.Default(); /* use defaults for now */
            image = image.ConvertToFormat(PixelFormat.Float);
            refImage = refImage.ConvertToFormat(PixelFormat.Float);

            // Clamp to [0,1]...
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    for (int c = 0; c < image.NChannels(); ++c)
                    {
                        image.SetChannel(new Point2i(x, y), c, Clamp(image.GetChannel(new Point2i(x, y), c), 0, 1));
                        refImage.SetChannel(new Point2i(x, y), c,
                                            Clamp(refImage.GetChannel(new Point2i(x, y), c), 0, 1));
                    }

            // The C++ passes the images' raw interleaved float storage; build the same layout.
            int xr = image.Resolution().x, yr = image.Resolution().y;
            float[] imgRaw = new float[3 * xr * yr];
            float[] refRaw = new float[3 * xr * yr];
            float[] flipOut = new float[xr * yr];
            for (int y = 0; y < yr; ++y)
                for (int x = 0; x < xr; ++x)
                    for (int c = 0; c < 3; ++c)
                    {
                        imgRaw[3 * (x + y * xr) + c] = (float)image.GetChannel(new Point2i(x, y), c);
                        refRaw[3 * (x + y * xr) + c] = (float)refImage.GetChannel(new Point2i(x, y), c);
                    }

            flip.ComputeFLIPError(imgRaw, refRaw, flipOut, xr, yr, opt);

            for (int y = 0; y < yr; ++y)
                for (int x = 0; x < xr; ++x)
                    errorImage.SetChannel(new Point2i(x, y), 0, flipOut[x + y * xr]);

            for (int c = 0; c < image.NChannels(); ++c)
                error[c] = 0;
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    for (int c = 0; c < image.NChannels(); ++c)
                        error[c] += errorImage.GetChannel(new Point2i(x, y), 0);
            for (int c = 0; c < image.NChannels(); ++c)
                error[c] /= image.Resolution().x * image.Resolution().y;
        }

        if (error.MaxValue() == 0)
            // Same same.
            return 0;

        float delta = 100.0f * (imageAverage - refAverage) / refAverage;
        string deltaString = StringPrintf("%f%% delta", delta);
        if (Math.Abs(delta) > 0.1)
            deltaString = Red(deltaString);
        else if (Math.Abs(delta) > 0.001)
            deltaString = Yellow(deltaString);
        Printf("Images differ:\n\t%s %s\n\tavg = %f / %f (%s), %s = %f\n", imageFile,
               referenceFile, imageAverage, refAverage, deltaString, metric, error.Average());

        if (outFile.Length != 0)
        {
            if (!errorImage.Write(outFile, im.metadata))
                return 1;
        }

        return 1;
    }

    // C printf("%f"): fixed notation with six digits after the decimal point.
    static string CF(double v)
    {
        if (double.IsNaN(v)) return "-nan";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        return v.ToString("F6", CultureInfo.InvariantCulture);
    }

    static void printImageStats(string name, Image image, ImageMetadata metadata)
    {
        Console.Out.Write(StringPrintf("%s:\n\tresolution (%d, %d)\n", name, image.Resolution().x, image.Resolution().y));
        Printf("\tpixel format: %s\n", image.Format());

        Console.Out.Write("\tcolor space : ");
        if (metadata.colorSpace && metadata.colorSpace.value() != null)
        {
            RGBColorSpace c = metadata.colorSpace.value();
            if (c == RGBColorSpace.sRGB)
                Console.Out.Write("sRGB");
            else if (c == RGBColorSpace.DCI_P3)
                Console.Out.Write("DCI-P3");
            else if (c == RGBColorSpace.Rec2020)
                Console.Out.Write("rec2020");
            else if (c == RGBColorSpace.ACES2065_1)
                Console.Out.Write("ACES");
            else
            {
                Console.Out.Write(" r: " + CF(c.r.x) + " " + CF(c.r.y) + " g: " + CF(c.g.x) + " " + CF(c.g.y) +
                       " b: " + CF(c.b.x) + " " + CF(c.b.y) + " w: " + CF(c.w.x) + " " + CF(c.w.y));
            }
            Console.Out.Write("\n");
        }
        else
            Console.Out.Write(" (unspecified)\n");

        if (metadata.fullResolution)
            Console.Out.Write(StringPrintf("\tfull resolution (%d, %d)\n", metadata.fullResolution.value().x,
                   metadata.fullResolution.value().y));
        if (metadata.pixelBounds)
            Console.Out.Write(StringPrintf("\tpixel bounds (%d, %d) - (%d, %d)\n", metadata.pixelBounds.value().pMin.x,
                   metadata.pixelBounds.value().pMin.y, metadata.pixelBounds.value().pMax.x,
                   metadata.pixelBounds.value().pMax.y));
        if (metadata.renderTimeSeconds)
        {
            float s = metadata.renderTimeSeconds.value();
            int h = (int)s / 3600;
            s -= h * 3600;
            int m = (int)s / 60;
            s -= m * 60;

            Console.Out.Write(StringPrintf("\trender time: %dh %dm %.02fs (total %.02fs)\n", h, m, s,
                   metadata.renderTimeSeconds.value()));
        }
        if (metadata.cameraFromWorld)
            Console.Out.Write("\tcamera from world: " + metadata.cameraFromWorld.value().ToString() + "\n");
        if (metadata.NDCFromWorld)
            Console.Out.Write("\tNDC from world: " + metadata.NDCFromWorld.value().ToString() + "\n");
        if (metadata.samplesPerPixel)
            Console.Out.Write(StringPrintf("\tsamples per pixel: %d\n", metadata.samplesPerPixel.value()));

        if (metadata.MSE)
            Console.Out.Write(StringPrintf("\tMSE vs. reference image: %g\n", metadata.MSE.value()));

        foreach (KeyValuePair<string, string> iter in metadata.strings)
            Console.Out.Write(StringPrintf("\t\"%s\": \"%s\"\n", iter.Key, iter.Value));

        foreach (KeyValuePair<string, List<string>> iter in metadata.stringVectors)
        {
            Console.Out.Write(StringPrintf("\t\"%s\": [ ", iter.Key));
            foreach (string str in iter.Value)
                Console.Out.Write(StringPrintf("\"%s\" ", str));
            Console.Out.Write("]\n");
        }

        Console.Out.Write("\tChannels:\n");

        List<string> channelNames = image.ChannelNames();
        foreach (string channel in channelNames)
        {
            Float min = Infinity, max = -Infinity;
            double sum = 0.0;
            int nNaN = 0, nInf = 0, nValid = 0;
            ImageChannelDesc desc = image.GetChannelDesc(new List<string> { channel });

            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                {
                    Float v = image.GetChannels(new Point2i(x, y), desc);

                    if (std.isnan(v))
                        ++nNaN;
                    else if (std.isinf(v))
                        ++nInf;
                    else
                    {
                        min = std.min(min, v);
                        max = std.max(max, v);
                        sum += v;
                        ++nValid;
                    }
                }

            Console.Out.Write(StringPrintf("\t    %20s: min %12g max %12g avg %12g (%d infinite, %d " +
                   "not-a-number)\n",
                   channel, min, max, sum / nValid, nInf, nNaN));
        }
    }

    static readonly RGB[] falseColorValues = new RGB[] {
    new RGB(0.267004f, 0.004874f, 0.329415f), new RGB(0.26851f, 0.009605f, 0.335427f),
    new RGB(0.269944f, 0.014625f, 0.341379f), new RGB(0.271305f, 0.019942f, 0.347269f),
    new RGB(0.272594f, 0.025563f, 0.353093f), new RGB(0.273809f, 0.031497f, 0.358853f),
    new RGB(0.274952f, 0.037752f, 0.364543f), new RGB(0.276022f, 0.044167f, 0.370164f),
    new RGB(0.277018f, 0.050344f, 0.375715f), new RGB(0.277941f, 0.056324f, 0.381191f),
    new RGB(0.278791f, 0.062145f, 0.386592f), new RGB(0.279566f, 0.067836f, 0.391917f),
    new RGB(0.280267f, 0.073417f, 0.397163f), new RGB(0.280894f, 0.078907f, 0.402329f),
    new RGB(0.281446f, 0.08432f, 0.407414f),  new RGB(0.281924f, 0.089666f, 0.412415f),
    new RGB(0.282327f, 0.094955f, 0.417331f), new RGB(0.282656f, 0.100196f, 0.42216f),
    new RGB(0.28291f, 0.105393f, 0.426902f),  new RGB(0.283091f, 0.110553f, 0.431554f),
    new RGB(0.283197f, 0.11568f, 0.436115f),  new RGB(0.283229f, 0.120777f, 0.440584f),
    new RGB(0.283187f, 0.125848f, 0.44496f),  new RGB(0.283072f, 0.130895f, 0.449241f),
    new RGB(0.282884f, 0.13592f, 0.453427f),  new RGB(0.282623f, 0.140926f, 0.457517f),
    new RGB(0.28229f, 0.145912f, 0.46151f),   new RGB(0.281887f, 0.150881f, 0.465405f),
    new RGB(0.281412f, 0.155834f, 0.469201f), new RGB(0.280868f, 0.160771f, 0.472899f),
    new RGB(0.280255f, 0.165693f, 0.476498f), new RGB(0.279574f, 0.170599f, 0.479997f),
    new RGB(0.278826f, 0.17549f, 0.483397f),  new RGB(0.278012f, 0.180367f, 0.486697f),
    new RGB(0.277134f, 0.185228f, 0.489898f), new RGB(0.276194f, 0.190074f, 0.493001f),
    new RGB(0.275191f, 0.194905f, 0.496005f), new RGB(0.274128f, 0.199721f, 0.498911f),
    new RGB(0.273006f, 0.20452f, 0.501721f),  new RGB(0.271828f, 0.209303f, 0.504434f),
    new RGB(0.270595f, 0.214069f, 0.507052f), new RGB(0.269308f, 0.218818f, 0.509577f),
    new RGB(0.267968f, 0.223549f, 0.512008f), new RGB(0.26658f, 0.228262f, 0.514349f),
    new RGB(0.265145f, 0.232956f, 0.516599f), new RGB(0.263663f, 0.237631f, 0.518762f),
    new RGB(0.262138f, 0.242286f, 0.520837f), new RGB(0.260571f, 0.246922f, 0.522828f),
    new RGB(0.258965f, 0.251537f, 0.524736f), new RGB(0.257322f, 0.25613f, 0.526563f),
    new RGB(0.255645f, 0.260703f, 0.528312f), new RGB(0.253935f, 0.265254f, 0.529983f),
    new RGB(0.252194f, 0.269783f, 0.531579f), new RGB(0.250425f, 0.27429f, 0.533103f),
    new RGB(0.248629f, 0.278775f, 0.534556f), new RGB(0.246811f, 0.283237f, 0.535941f),
    new RGB(0.244972f, 0.287675f, 0.53726f),  new RGB(0.243113f, 0.292092f, 0.538516f),
    new RGB(0.241237f, 0.296485f, 0.539709f), new RGB(0.239346f, 0.300855f, 0.540844f),
    new RGB(0.237441f, 0.305202f, 0.541921f), new RGB(0.235526f, 0.309527f, 0.542944f),
    new RGB(0.233603f, 0.313828f, 0.543914f), new RGB(0.231674f, 0.318106f, 0.544834f),
    new RGB(0.229739f, 0.322361f, 0.545706f), new RGB(0.227802f, 0.326594f, 0.546532f),
    new RGB(0.225863f, 0.330805f, 0.547314f), new RGB(0.223925f, 0.334994f, 0.548053f),
    new RGB(0.221989f, 0.339161f, 0.548752f), new RGB(0.220057f, 0.343307f, 0.549413f),
    new RGB(0.21813f, 0.347432f, 0.550038f),  new RGB(0.21621f, 0.351535f, 0.550627f),
    new RGB(0.214298f, 0.355619f, 0.551184f), new RGB(0.212395f, 0.359683f, 0.55171f),
    new RGB(0.210503f, 0.363727f, 0.552206f), new RGB(0.208623f, 0.367752f, 0.552675f),
    new RGB(0.206756f, 0.371758f, 0.553117f), new RGB(0.204903f, 0.375746f, 0.553533f),
    new RGB(0.203063f, 0.379716f, 0.553925f), new RGB(0.201239f, 0.38367f, 0.554294f),
    new RGB(0.19943f, 0.387607f, 0.554642f),  new RGB(0.197636f, 0.391528f, 0.554969f),
    new RGB(0.19586f, 0.395433f, 0.555276f),  new RGB(0.1941f, 0.399323f, 0.555565f),
    new RGB(0.192357f, 0.403199f, 0.555836f), new RGB(0.190631f, 0.407061f, 0.556089f),
    new RGB(0.188923f, 0.41091f, 0.556326f),  new RGB(0.187231f, 0.414746f, 0.556547f),
    new RGB(0.185556f, 0.41857f, 0.556753f),  new RGB(0.183898f, 0.422383f, 0.556944f),
    new RGB(0.182256f, 0.426184f, 0.55712f),  new RGB(0.180629f, 0.429975f, 0.557282f),
    new RGB(0.179019f, 0.433756f, 0.55743f),  new RGB(0.177423f, 0.437527f, 0.557565f),
    new RGB(0.175841f, 0.44129f, 0.557685f),  new RGB(0.174274f, 0.445044f, 0.557792f),
    new RGB(0.172719f, 0.448791f, 0.557885f), new RGB(0.171176f, 0.45253f, 0.557965f),
    new RGB(0.169646f, 0.456262f, 0.55803f),  new RGB(0.168126f, 0.459988f, 0.558082f),
    new RGB(0.166617f, 0.463708f, 0.558119f), new RGB(0.165117f, 0.467423f, 0.558141f),
    new RGB(0.163625f, 0.471133f, 0.558148f), new RGB(0.162142f, 0.474838f, 0.55814f),
    new RGB(0.160665f, 0.47854f, 0.558115f),  new RGB(0.159194f, 0.482237f, 0.558073f),
    new RGB(0.157729f, 0.485932f, 0.558013f), new RGB(0.15627f, 0.489624f, 0.557936f),
    new RGB(0.154815f, 0.493313f, 0.55784f),  new RGB(0.153364f, 0.497f, 0.557724f),
    new RGB(0.151918f, 0.500685f, 0.557587f), new RGB(0.150476f, 0.504369f, 0.55743f),
    new RGB(0.149039f, 0.508051f, 0.55725f),  new RGB(0.147607f, 0.511733f, 0.557049f),
    new RGB(0.14618f, 0.515413f, 0.556823f),  new RGB(0.144759f, 0.519093f, 0.556572f),
    new RGB(0.143343f, 0.522773f, 0.556295f), new RGB(0.141935f, 0.526453f, 0.555991f),
    new RGB(0.140536f, 0.530132f, 0.555659f), new RGB(0.139147f, 0.533812f, 0.555298f),
    new RGB(0.13777f, 0.537492f, 0.554906f),  new RGB(0.136408f, 0.541173f, 0.554483f),
    new RGB(0.135066f, 0.544853f, 0.554029f), new RGB(0.133743f, 0.548535f, 0.553541f),
    new RGB(0.132444f, 0.552216f, 0.553018f), new RGB(0.131172f, 0.555899f, 0.552459f),
    new RGB(0.129933f, 0.559582f, 0.551864f), new RGB(0.128729f, 0.563265f, 0.551229f),
    new RGB(0.127568f, 0.566949f, 0.550556f), new RGB(0.126453f, 0.570633f, 0.549841f),
    new RGB(0.125394f, 0.574318f, 0.549086f), new RGB(0.124395f, 0.578002f, 0.548287f),
    new RGB(0.123463f, 0.581687f, 0.547445f), new RGB(0.122606f, 0.585371f, 0.546557f),
    new RGB(0.121831f, 0.589055f, 0.545623f), new RGB(0.121148f, 0.592739f, 0.544641f),
    new RGB(0.120565f, 0.596422f, 0.543611f), new RGB(0.120092f, 0.600104f, 0.54253f),
    new RGB(0.119738f, 0.603785f, 0.5414f),   new RGB(0.119512f, 0.607464f, 0.540218f),
    new RGB(0.119423f, 0.611141f, 0.538982f), new RGB(0.119483f, 0.614817f, 0.537692f),
    new RGB(0.119699f, 0.61849f, 0.536347f),  new RGB(0.120081f, 0.622161f, 0.534946f),
    new RGB(0.120638f, 0.625828f, 0.533488f), new RGB(0.12138f, 0.629492f, 0.531973f),
    new RGB(0.122312f, 0.633153f, 0.530398f), new RGB(0.123444f, 0.636809f, 0.528763f),
    new RGB(0.12478f, 0.640461f, 0.527068f),  new RGB(0.126326f, 0.644107f, 0.525311f),
    new RGB(0.128087f, 0.647749f, 0.523491f), new RGB(0.130067f, 0.651384f, 0.521608f),
    new RGB(0.132268f, 0.655014f, 0.519661f), new RGB(0.134692f, 0.658636f, 0.517649f),
    new RGB(0.137339f, 0.662252f, 0.515571f), new RGB(0.14021f, 0.665859f, 0.513427f),
    new RGB(0.143303f, 0.669459f, 0.511215f), new RGB(0.146616f, 0.67305f, 0.508936f),
    new RGB(0.150148f, 0.676631f, 0.506589f), new RGB(0.153894f, 0.680203f, 0.504172f),
    new RGB(0.157851f, 0.683765f, 0.501686f), new RGB(0.162016f, 0.687316f, 0.499129f),
    new RGB(0.166383f, 0.690856f, 0.496502f), new RGB(0.170948f, 0.694384f, 0.493803f),
    new RGB(0.175707f, 0.6979f, 0.491033f),   new RGB(0.180653f, 0.701402f, 0.488189f),
    new RGB(0.185783f, 0.704891f, 0.485273f), new RGB(0.19109f, 0.708366f, 0.482284f),
    new RGB(0.196571f, 0.711827f, 0.479221f), new RGB(0.202219f, 0.715272f, 0.476084f),
    new RGB(0.20803f, 0.718701f, 0.472873f),  new RGB(0.214f, 0.722114f, 0.469588f),
    new RGB(0.220124f, 0.725509f, 0.466226f), new RGB(0.226397f, 0.728888f, 0.462789f),
    new RGB(0.232815f, 0.732247f, 0.459277f), new RGB(0.239374f, 0.735588f, 0.455688f),
    new RGB(0.24607f, 0.73891f, 0.452024f),   new RGB(0.252899f, 0.742211f, 0.448284f),
    new RGB(0.259857f, 0.745492f, 0.444467f), new RGB(0.266941f, 0.748751f, 0.440573f),
    new RGB(0.274149f, 0.751988f, 0.436601f), new RGB(0.281477f, 0.755203f, 0.432552f),
    new RGB(0.288921f, 0.758394f, 0.428426f), new RGB(0.296479f, 0.761561f, 0.424223f),
    new RGB(0.304148f, 0.764704f, 0.419943f), new RGB(0.311925f, 0.767822f, 0.415586f),
    new RGB(0.319809f, 0.770914f, 0.411152f), new RGB(0.327796f, 0.77398f, 0.40664f),
    new RGB(0.335885f, 0.777018f, 0.402049f), new RGB(0.344074f, 0.780029f, 0.397381f),
    new RGB(0.35236f, 0.783011f, 0.392636f),  new RGB(0.360741f, 0.785964f, 0.387814f),
    new RGB(0.369214f, 0.788888f, 0.382914f), new RGB(0.377779f, 0.791781f, 0.377939f),
    new RGB(0.386433f, 0.794644f, 0.372886f), new RGB(0.395174f, 0.797475f, 0.367757f),
    new RGB(0.404001f, 0.800275f, 0.362552f), new RGB(0.412913f, 0.803041f, 0.357269f),
    new RGB(0.421908f, 0.805774f, 0.35191f),  new RGB(0.430983f, 0.808473f, 0.346476f),
    new RGB(0.440137f, 0.811138f, 0.340967f), new RGB(0.449368f, 0.813768f, 0.335384f),
    new RGB(0.458674f, 0.816363f, 0.329727f), new RGB(0.468053f, 0.818921f, 0.323998f),
    new RGB(0.477504f, 0.821444f, 0.318195f), new RGB(0.487026f, 0.823929f, 0.312321f),
    new RGB(0.496615f, 0.826376f, 0.306377f), new RGB(0.506271f, 0.828786f, 0.300362f),
    new RGB(0.515992f, 0.831158f, 0.294279f), new RGB(0.525776f, 0.833491f, 0.288127f),
    new RGB(0.535621f, 0.835785f, 0.281908f), new RGB(0.545524f, 0.838039f, 0.275626f),
    new RGB(0.555484f, 0.840254f, 0.269281f), new RGB(0.565498f, 0.84243f, 0.262877f),
    new RGB(0.575563f, 0.844566f, 0.256415f), new RGB(0.585678f, 0.846661f, 0.249897f),
    new RGB(0.595839f, 0.848717f, 0.243329f), new RGB(0.606045f, 0.850733f, 0.236712f),
    new RGB(0.616293f, 0.852709f, 0.230052f), new RGB(0.626579f, 0.854645f, 0.223353f),
    new RGB(0.636902f, 0.856542f, 0.21662f),  new RGB(0.647257f, 0.8584f, 0.209861f),
    new RGB(0.657642f, 0.860219f, 0.203082f), new RGB(0.668054f, 0.861999f, 0.196293f),
    new RGB(0.678489f, 0.863742f, 0.189503f), new RGB(0.688944f, 0.865448f, 0.182725f),
    new RGB(0.699415f, 0.867117f, 0.175971f), new RGB(0.709898f, 0.868751f, 0.169257f),
    new RGB(0.720391f, 0.87035f, 0.162603f),  new RGB(0.730889f, 0.871916f, 0.156029f),
    new RGB(0.741388f, 0.873449f, 0.149561f), new RGB(0.751884f, 0.874951f, 0.143228f),
    new RGB(0.762373f, 0.876424f, 0.137064f), new RGB(0.772852f, 0.877868f, 0.131109f),
    new RGB(0.783315f, 0.879285f, 0.125405f), new RGB(0.79376f, 0.880678f, 0.120005f),
    new RGB(0.804182f, 0.882046f, 0.114965f), new RGB(0.814576f, 0.883393f, 0.110347f),
    new RGB(0.82494f, 0.88472f, 0.106217f),   new RGB(0.83527f, 0.886029f, 0.102646f),
    new RGB(0.845561f, 0.887322f, 0.099702f), new RGB(0.85581f, 0.888601f, 0.097452f),
    new RGB(0.866013f, 0.889868f, 0.095953f), new RGB(0.876168f, 0.891125f, 0.09525f),
    new RGB(0.886271f, 0.892374f, 0.095374f), new RGB(0.89632f, 0.893616f, 0.096335f),
    new RGB(0.906311f, 0.894855f, 0.098125f), new RGB(0.916242f, 0.896091f, 0.100717f),
    new RGB(0.926106f, 0.89733f, 0.104071f),  new RGB(0.935904f, 0.89857f, 0.108131f),
    new RGB(0.945636f, 0.899815f, 0.112838f), new RGB(0.9553f, 0.901065f, 0.118128f),
    new RGB(0.964894f, 0.902323f, 0.123941f), new RGB(0.974417f, 0.90359f, 0.130215f),
    new RGB(0.983868f, 0.904867f, 0.136897f), new RGB(0.993248f, 0.906157f, 0.143936f),
    };

    static int falsecolor(List<string> args)
    {
        string outFile = "", inFile = "";
        bool plusMinus = false, ramp = false;
        Float maxValue = -Infinity;

        Action<string> onError = (string err) =>
        {
            usage("falsecolor", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, end, "plusminus", ref plusMinus, onError) ||
                ParseArg(args, ref iter, end, "ramp", ref ramp, onError) ||
                ParseArg(args, ref iter, end, "maxValue", ref maxValue, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && args[iter][0] != '-')
            {
                inFile = args[iter];
            }
            else
            {
                usage("falsecolor", StringPrintf("%s: unknown command flag", args[iter]));
            }
        }

        if (!ramp && inFile.Length == 0)
            usage("falsecolor", "expecting input image filename.");
        if (outFile.Length == 0)
            usage("falsecolor", "expecting --outfile filename.");

        Image image;
        if (ramp)
        {
            int xres = 10, yres = 300;
            image = new Image(PixelFormat.Float, new Point2i(xres, yres), new List<string> { "Y" });
            for (int y = 0; y < yres; ++y)
                for (int x = 0; x < xres; ++x)
                    image.SetChannel(new Point2i(x, y), 0, (Float)(yres - 1 - y) / (Float)(yres - 1));
        }
        else
            image = Image.Read(inFile).image;

        if (maxValue == -Infinity)
            for (int y = 0; y < image.Resolution().y; ++y)
                for (int x = 0; x < image.Resolution().x; ++x)
                    maxValue =
                        std.max(maxValue, std.abs(image.GetChannels(new Point2i(x, y)).Average()));

        Image outImage = new Image(PixelFormat.Half, image.Resolution(), new List<string> { "R", "G", "B" });
        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                Float relativeValue = image.GetChannels(new Point2i(x, y)).Average() / maxValue;
                RGB rgb;
                if (plusMinus)
                {
                    if (relativeValue > 0)
                        rgb = new RGB(0, relativeValue, 0);
                    else
                        rgb = new RGB(std.abs(relativeValue), 0, 0);
                }
                else
                {
                    relativeValue = Clamp(relativeValue, 0, 1);
                    int index = (int)(relativeValue * falseColorValues.Length);
                    index = std.min(index, falseColorValues.Length - 1);
                    rgb = falseColorValues[index];
                }

                outImage.SetChannels(new Point2i(x, y), S(SRGBToLinear(rgb[0]), SRGBToLinear(rgb[1]),
                                                          SRGBToLinear(rgb[2])));
            }

        if (!outImage.Write(outFile))
            return 1;

        return 0;
    }

    static int info(List<string> args)
    {
        int err = 0;
        foreach (string fn in args)
        {
            ImageAndMetadata im = Image.Read(fn);
            printImageStats(fn, im.image, im.metadata);
        }
        return err;
    }

    static int bloom(List<string> args)
    {
        string inFile = "", outFile = "";
        Float level = Infinity;
        int width = 15;
        Float scale = (Float).3;
        int iterations = 5;

        Action<string> onError = (string err) =>
        {
            usage("bloom", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, end, "level", ref level, onError) ||
                ParseArg(args, ref iter, end, "width", ref width, onError) ||
                ParseArg(args, ref iter, end, "iterations", ref iterations, onError) ||
                ParseArg(args, ref iter, end, "scale", ref scale, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && args[iter][0] != '-')
            {
                inFile = args[iter];
            }
            else
            {
                onError(StringPrintf("argument %s invalid", args[iter]));
            }
        }

        if (outFile.Length == 0)
            usage("bloom", "--outfile must be specified");
        if (inFile.Length == 0)
            usage("bloom", "input filename must be specified");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;

        List<Image> blurred = new List<Image>();

        // First, threshold the source image
        int nSurvivors = 0;
        Point2i res = image.Resolution();
        int nc = image.NChannels();
        Image thresholdedImage = new Image(PixelFormat.Float, image.Resolution(), image.ChannelNames());
        for (int y = 0; y < res.y; ++y)
        {
            for (int x = 0; x < res.x; ++x)
            {
                bool overThreshold = false;
                for (int c = 0; c < nc; ++c)
                    if (image.GetChannel(new Point2i(x, y), c) > level)
                        overThreshold = true;
                if (overThreshold)
                {
                    ++nSurvivors;
                    for (int c = 0; c < nc; ++c)
                        thresholdedImage.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(x, y), c));
                }
                else
                    for (int c = 0; c < nc; ++c)
                        thresholdedImage.SetChannel(new Point2i(x, y), c, 0.0f);
            }
        }
        if (nSurvivors == 0)
        {
            Warning(StringPrintf("No pixels were above bloom threshold %f", level));
            return 1;
        }
        blurred.Add(thresholdedImage);

        if ((width % 2) == 0)
        {
            ++width;
            Warning(StringPrintf("Bloom width must be an odd value. Rounding up to %d.", width));
        }
        int radius = width / 2;

        // Blur thresholded image.
        Float sigma = (Float)(radius / 2.0);  // TODO: make a parameter

        for (int iter = 0; iter < iterations; ++iter)
        {
            Image blur =
                blurred[blurred.Count - 1].GaussianFilter(image.AllChannelsDesc(), radius, sigma);
            blurred.Add(blur);
        }

        // Finally, add all of the blurred images, scaled, to the original.
        for (int y = 0; y < res.y; ++y)
        {
            for (int x = 0; x < res.x; ++x)
            {
                for (int c = 0; c < nc; ++c)
                {
                    Float blurredSum = 0.0f;
                    // Skip the thresholded image, since it's already
                    // present in the original; just add pixels from the
                    // blurred ones.
                    for (int j = 1; j < blurred.Count; ++j)
                        blurredSum += blurred[j].GetChannel(new Point2i(x, y), c);
                    image.SetChannel(
                        new Point2i(x, y), c,
                        image.GetChannel(new Point2i(x, y), c) + (scale / iterations) * blurredSum);
                }
            }
        }

        image.Write(outFile, imRead.metadata);

        return 0;
    }

    static int convert(List<string> args)
    {
        bool acesFilmic = false;
        float scale = 1.0f, gamma = 1.0f;
        int repeat = 1;
        bool flipy = false;
        bool tonemap = false;
        Float maxY = 1;
        Float despikeLimit = Infinity;
        bool preserveColors = false;
        bool bw = false;
        bool fp16 = false;
        string inFile = "", outFile = "";
        string colorspace = "";
        string channelNames = "";
        int[] cropWindow = { -1, 0, -1, 0 };
        Float clamp = Infinity;

        Action<string> onError = (string err) =>
        {
            usage("convert", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "acesfilmic", ref acesFilmic, onError) ||
                ParseArg(args, ref iter, end, "bw", ref bw, onError) ||
                ParseArg(args, ref iter, end, "channels", ref channelNames, onError) ||
                ParseArg(args, ref iter, end, "clamp", ref clamp, onError) ||
                ParseArg(args, ref iter, end, "colorspace", ref colorspace, onError) ||
                ParseArg(args, ref iter, end, "crop", new pstd.span<int>(cropWindow), onError) ||
                ParseArg(args, ref iter, end, "despike", ref despikeLimit, onError) ||
                ParseArg(args, ref iter, end, "fp16", ref fp16, onError) ||
                ParseArg(args, ref iter, end, "flipy", ref flipy, onError) ||
                ParseArg(args, ref iter, end, "gamma", ref gamma, onError) ||
                ParseArg(args, ref iter, end, "maxluminance", ref maxY, onError) ||
                ParseArg(args, ref iter, end, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, end, "preservecolors", ref preserveColors, onError) ||
                ParseArg(args, ref iter, end, "repeatpix", ref repeat, onError) ||
                ParseArg(args, ref iter, end, "scale", ref scale, onError) ||
                ParseArg(args, ref iter, end, "tonemap", ref tonemap, onError))
            {
                // success
            }
            else if (args[iter][0] != '-' && inFile.Length == 0)
            {
                inFile = args[iter];
            }
            else
                usage("convert", StringPrintf("%s: unknown command flag", args[iter]));
        }

        if (maxY <= 0)
            usage("convert", "--maxluminance value must be greater than zero");
        if (repeat <= 0)
            usage("convert", "--repeatpix value must be greater than zero");
        if (scale == 0)
            usage("convert", "--scale value must be non-zero");
        if (outFile.Length == 0)
            usage("convert", "--outfile filename must be specified");
        if (inFile.Length == 0)
            usage("convert", "input filename not specified");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;
        ImageMetadata metadata = imRead.metadata;

        ImageMetadata outMetadata = new ImageMetadata();
        // These are the only entries it makes sense to copy along (so far).
        outMetadata.colorSpace = metadata.colorSpace;
        outMetadata.cameraFromWorld = metadata.cameraFromWorld;
        outMetadata.NDCFromWorld = metadata.NDCFromWorld;
        outMetadata.pixelBounds = metadata.pixelBounds;
        outMetadata.fullResolution = metadata.fullResolution;

        if (channelNames.Length == 0)
        {
            // If the input image has AOVs and the target image is a regular
            // format, then just grab R,G,B...
            bool hasAOVs = false;
            foreach (string name in image.ChannelNames())
                if (name != "R" && name != "G" && name != "B" && name != "A")
                {
                    hasAOVs = true;
                    break;
                }

            if (hasAOVs && !HasExtension(outFile, "exr"))
            {
                Warning(StringPrintf("%s: image has non-RGBA channels but converting to an image format " +
                        "that can't store them. Converting RGB only.",
                        inFile));
                channelNames = "R,G,B";
            }
        }

        if (channelNames.Length != 0)
        {
            List<string> splitChannelNames = SplitString(channelNames, ',');
            ImageChannelDesc desc = image.GetChannelDesc(splitChannelNames);
            if (!desc)
            {
                Error(StringPrintf("%s: image doesn't have channels \"%s\".", inFile, channelNames));
                return 1;
            }
            image = image.SelectChannels(desc);
        }

        Point2i res = image.Resolution();
        int nc = image.NChannels();

        // Crop
        // If last 2 are negative, they're taken as deltas
        if (cropWindow[1] < 0)
            cropWindow[1] = cropWindow[0] - cropWindow[1];
        if (cropWindow[3] < 0)
            cropWindow[3] = cropWindow[2] - cropWindow[3];
        if (cropWindow[0] >= 0 && cropWindow[2] >= 0)
        {
            Bounds2i cropBounds = new Bounds2i(new Point2i(cropWindow[0], cropWindow[2]),
                                               new Point2i(cropWindow[1], cropWindow[3]));

            Point2i fullRes =
                metadata.fullResolution ? metadata.fullResolution.value() : image.Resolution();

            if (metadata.pixelBounds && !Inside(cropBounds, metadata.pixelBounds.value()))
            {
                Bounds2i pb = metadata.pixelBounds.value();
                Error(StringPrintf("%s: crop window bounds (%d,%d)-(%d,%d) are not inside " +
                      "image's pixel bounds (%d,%d)-(%d,%d).",
                      inFile, cropBounds.pMin.x, cropBounds.pMin.y, cropBounds.pMax.x,
                      cropBounds.pMax.y, pb.pMin.x, pb.pMin.y, pb.pMax.x, pb.pMax.y));
                return 1;
            }
            else if (!Inside(cropBounds, new Bounds2i(new Point2i(0, 0), fullRes)))
            {
                Error(StringPrintf("%s: crop window bounds (%d,%d)-(%d,%d) are not inside " +
                      "image's resolution (%d,%d).",
                      inFile, cropBounds.pMin.x, cropBounds.pMin.y, cropBounds.pMax.x,
                      cropBounds.pMax.y, fullRes.x, fullRes.y));
                return 1;
            }

            outMetadata.fullResolution = new pstd.optional<Point2i>(fullRes);
            outMetadata.pixelBounds = new pstd.optional<Bounds2i>(cropBounds);

            // Adjust crop bounds for call to Crop()
            if (metadata.pixelBounds)
            {
                Point2i pm = metadata.pixelBounds.value().pMin;
                cropBounds.pMin = new Point2i(cropBounds.pMin.x - pm.x, cropBounds.pMin.y - pm.y);
                cropBounds.pMax = new Point2i(cropBounds.pMax.x - pm.x, cropBounds.pMax.y - pm.y);
            }
            image = image.Crop(cropBounds);

            res = image.Resolution();
        }

        // Count how many of the following operations modify pixel values
        // rather than copying existing ones.
        int nModifyingOps = ((colorspace.Length != 0) ? 1 : 0) + ((scale != 1) ? 1 : 0) + ((gamma != 1) ? 1 : 0) +
                            (tonemap ? 1 : 0) + (preserveColors ? 1 : 0) + (acesFilmic ? 1 : 0);
        // Convert to a 32-bit format for maximum accuracy if we're applying
        // multiple operations to the pixel values.
        if (nModifyingOps > 1 && !Is32Bit(image.Format()))
            image = image.ConvertToFormat(PixelFormat.Float);

        if (clamp < Infinity)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float maxValue = 0;
                    for (int c = 0; c < nc; ++c)
                        maxValue = std.max(maxValue, image.GetChannel(new Point2i(x, y), c));
                    if (maxValue > clamp)
                    {
                        Float sc = clamp / maxValue;
                        for (int c = 0; c < nc; ++c)
                            image.SetChannel(new Point2i(x, y), c, sc * image.GetChannel(new Point2i(x, y), c));
                    }
                }
        }

        if (colorspace.Length != 0)
        {
            RGBColorSpace dest = RGBColorSpace.GetNamed(colorspace);
            if (dest == null)
            {
                Error(StringPrintf("%s: color space unknown.", colorspace));
                return 1;
            }
            ImageChannelDesc rgbDesc = image.GetChannelDesc(new List<string> { "R", "G", "B" });
            if (!rgbDesc)
            {
                Error(StringPrintf("%s: doesn't have R, G, B channels.", inFile));
                return 1;
            }

            RGBColorSpace srcColorSpace = metadata.GetColorSpace();
            SquareMatrix3 m = ConvertRGBColorSpace(srcColorSpace, dest);
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    ImageChannelValues channels = image.GetChannels(new Point2i(x, y), rgbDesc);
                    RGB rgb = m * new RGB(channels[0], channels[1], channels[2]);
                    image.SetChannels(new Point2i(x, y), rgbDesc, S(rgb.r, rgb.g, rgb.b));
                }
            outMetadata.colorSpace = new pstd.optional<RGBColorSpace>(dest);
        }

        if (bw)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float sum = 0;
                    for (int c = 0; c < nc; ++c)
                        sum += image.GetChannel(new Point2i(x, y), c);
                    sum /= nc;
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, sum);
                }
        }

        if (despikeLimit < Infinity)
        {
            Image filteredImg = image.Clone();
            int despikeCount = 0;
            List<ImageChannelValues> neighbors = new List<ImageChannelValues>();
            for (int i = 0; i < 9; ++i)
                neighbors.Add(new ImageChannelValues((size_t)image.NChannels()));

            for (int y = 0; y < res.y; ++y)
            {
                for (int x = 0; x < res.x; ++x)
                {
                    if (image.GetChannels(new Point2i(x, y)).Average() < despikeLimit)
                        continue;

                    // Copy all of the valid neighbor pixels into neighbors[].
                    ++despikeCount;
                    int validNeighbors = 0;
                    for (int dy = -1; dy <= 1; ++dy)
                    {
                        if (y + dy < 0 || y + dy >= res.y)
                            continue;
                        for (int dx = -1; dx <= 1; ++dx)
                        {
                            if (x + dx < 0 || x + dx > res.x)
                                continue;
                            neighbors[validNeighbors++] = image.GetChannels(new Point2i(x + dx, y + dy));
                        }
                    }

                    // Find the median of the neighbors, sorted by average value.
                    // (std::nth_element is unspecified for ties; a stable sort is used here.)
                    int mid = validNeighbors / 2;
                    List<ImageChannelValues> part = neighbors.GetRange(0, validNeighbors)
                        .OrderBy((ImageChannelValues a) => a.Average()).ToList();
                    for (int i = 0; i < validNeighbors; ++i)
                        neighbors[i] = part[i];
                    filteredImg.SetChannels(new Point2i(x, y), neighbors[mid]);
                }
            }
            Image tmpImg = image;
            image = filteredImg;
            filteredImg = tmpImg;
            Console.Error.Write(StringPrintf("%s: despiked %d pixels\n", inFile, despikeCount));
        }

        if (scale != 1)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, scale * image.GetChannel(new Point2i(x, y), c));
        }

        if (gamma != 1)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(
                            new Point2i(x, y), c,
                            std.pow(std.max((Float)0, image.GetChannel(new Point2i(x, y), c)), gamma));
        }

        if (tonemap)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float lum = image.GetChannels(new Point2i(x, y)).Average();
                    // Reinhard et al. photographic tone mapping operator.
                    Float sc = (1 + lum / (maxY * maxY)) / (1 + lum);
                    for (int c = 0; c < nc; ++c)
                        image.SetChannel(new Point2i(x, y), c, sc * image.GetChannel(new Point2i(x, y), c));
                }
        }

        if (preserveColors)
        {
            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    Float m = image.GetChannel(new Point2i(x, y), 0);
                    for (int c = 1; c < nc; ++c)
                        m = std.max(m, image.GetChannel(new Point2i(x, y), c));
                    if (m > 1)
                    {
                        for (int c = 0; c < nc; ++c)
                            image.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(x, y), c) / m);
                    }
                }
        }

        if (acesFilmic)
        {
            // Approximation via
            // https://knarkowicz.wordpress.com/2016/01/06/aces-filmic-tone-mapping-curve/
            Func<Float, Float> ACESFilm = (Float x) =>
            {
                if (x <= 0)
                    return 0;
                Float a = 2.51f;
                Float b = 0.03f;
                Float c = 2.43f;
                Float d = 0.59f;
                Float e = 0.14f;
                return Clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0, 1);
            };

            for (int y = 0; y < res.y; ++y)
                for (int x = 0; x < res.x; ++x)
                {
                    for (int c = 0; c < nc; ++c)
                    {
                        Float v = image.GetChannel(new Point2i(x, y), c);
                        v = ACESFilm(v);
                        image.SetChannel(new Point2i(x, y), c, v);
                    }
                }
        }

        if (repeat > 1)
        {
            Image scaledImage = new Image(image.Format(), new Point2i(res.x * repeat, res.y * repeat),
                                          image.ChannelNames(), image.Encoding());
            for (int y = 0; y < repeat * res.y; ++y)
            {
                int yy = y / repeat;
                for (int x = 0; x < repeat * res.x; ++x)
                {
                    int xx = x / repeat;
                    for (int c = 0; c < nc; ++c)
                        scaledImage.SetChannel(new Point2i(x, y), c, image.GetChannel(new Point2i(xx, yy), c));
                }
            }
            image = scaledImage;
            res = image.Resolution();

            if (outMetadata.fullResolution)
            {
                Point2i fr = outMetadata.fullResolution.value();
                outMetadata.fullResolution = new pstd.optional<Point2i>(new Point2i(fr.x * repeat, fr.y * repeat));
            }
            if (outMetadata.pixelBounds)
            {
                Bounds2i pb = outMetadata.pixelBounds.value();
                pb.pMin = new Point2i(pb.pMin.x * repeat, pb.pMin.y * repeat);
                pb.pMax = new Point2i(pb.pMax.x * repeat, pb.pMax.y * repeat);
                outMetadata.pixelBounds = new pstd.optional<Bounds2i>(pb);
            }
        }

        if (flipy)
        {
            image.FlipY();
            if (outMetadata.pixelBounds && outMetadata.fullResolution)
            {
                // Flip the pixel bounds in the metadata as well
                Bounds2i pb = outMetadata.pixelBounds.value();
                int[] by = {
                    outMetadata.fullResolution.value().y - 1 - pb.pMax.y,
                    outMetadata.fullResolution.value().y - 1 - pb.pMin.y };
                pb.pMin.y = by[0];
                pb.pMax.y = by[1];
                outMetadata.pixelBounds = new pstd.optional<Bounds2i>(pb);
            }
        }

        if (fp16)
            image = image.ConvertToFormat(PixelFormat.Half);

        if (!image.Write(outFile, outMetadata))
            return 1;

        return 0;
    }

    static int whitebalance(List<string> args)
    {
        string inFile = "", outFile = "";
        Float temperature = 0;
        Float[] xy = { 0, 0 };
        string illuminant = "";

        Action<string> onError = (string err) =>
        {
            usage("whitebalance", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "outfile", ref outFile, onError) ||
                ParseArg(args, ref iter, end, "primaries", new pstd.span<Float>(xy), onError) ||
                ParseArg(args, ref iter, end, "illuminant", ref illuminant, onError) ||
                ParseArg(args, ref iter, end, "temperature", ref temperature, onError))
            {
                // success
            }
            else if (inFile.Length == 0 && args[iter][0] != '-')
            {
                inFile = args[iter];
            }
            else
            {
                onError(StringPrintf("argument %s invalid", args[iter]));
            }
        }

        if (outFile.Length == 0)
            usage("whitebalance", "--outfile must be specified");
        if (inFile.Length == 0)
            usage("whitebalance", "input filename must be specified");

        if ((((illuminant.Length != 0) ? 1 : 0) + ((temperature > 0) ? 1 : 0) + ((xy[0] != 0) ? 1 : 0)) > 1)
            usage("whitebalance",
                  "can only provide one of --illuminant, --primaries, --temperature");
        if (illuminant.Length == 0 && temperature == 0 && xy[0] == 0)
            usage("whitebalance",
                  "must provide one of --illuminant, --primaries, or --temperature");

        ImageAndMetadata imRead = Image.Read(inFile);
        Image image = imRead.image;

        ImageChannelDesc rgbDesc = image.GetChannelDesc(new List<string> { "R", "G", "B" });
        if (!rgbDesc)
        {
            Error(StringPrintf("%s: doesn't have R, G, B channels.", inFile));
            return 1;
        }

        RGBColorSpace colorSpace = imRead.metadata.GetColorSpace();
        Point2f srcWhite, targetWhite = colorSpace.w;
        if (illuminant.Length != 0)
        {
            string name = "stdillum-" + illuminant;
            Spectrum illum = GetNamedSpectrum(name);
            if (illum == null)
            {
                Error(StringPrintf("%s: illuminant unknown.", name));
                return 1;
            }
            srcWhite = SpectrumToXYZ(illum).xy();
        }
        else if (temperature > 0)
        {
            // Sensor::Create uses Spectra::D() rather than BlackbodySpectrum
            // here---make consistent?
            DenselySampledSpectrum illum = Spectra.D(temperature, null);
            srcWhite = SpectrumToXYZ(illum).xy();
        }
        else
            srcWhite = new Point2f(xy[0], xy[1]);

        SquareMatrix3 ccMatrix = colorSpace.RGBFromXYZ *
                                 WhiteBalance(srcWhite, targetWhite) *
                                 colorSpace.XYZFromRGB;

        for (int y = 0; y < image.Resolution().y; ++y)
            for (int x = 0; x < image.Resolution().x; ++x)
            {
                ImageChannelValues channels = image.GetChannels(new Point2i(x, y), rgbDesc);
                RGB rgb = ccMatrix * new RGB(channels[0], channels[1], channels[2]);
                image.SetChannels(new Point2i(x, y), rgbDesc, S(rgb.r, rgb.g, rgb.b));
            }

        image.Write(outFile, imRead.metadata);

        return 0;
    }

    static int makeemitters(List<string> args)
    {
        string filename = "";
        int downsampleRate = 1;

        Action<string> onError = (string err) =>
        {
            usage("makeemitters", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "downsample", ref downsampleRate, onError))
            {
                // success
            }
            else if (args[iter][0] == '-')
                usage("makeemitters", StringPrintf("%s: unknown command flag", args[iter]));
            else if (filename.Length == 0)
            {
                filename = args[iter];
            }
            else
                usage("makeemitters", "multiple input filenames provided.");
        }

        if (filename.Length == 0)
            usage("makeemitters", "missing image filename");

        ImageAndMetadata im = Image.Read(filename);
        Image image = im.image;

        ImageChannelDesc rgbDesc = image.GetChannelDesc(new List<string> { "R", "G", "B" });
        if (!rgbDesc)
        {
            Error(StringPrintf("%s: didn't find R, G, and B channels", filename));
            return 1;
        }

        Point2i res = image.Resolution();
        float aspect = (float)res.x / (float)res.y;
        Console.Out.Write("AttributeBegin\n");
        Console.Out.Write("Material \"matte\" \"rgb Kd\" [0 0 0]\n");
        for (int y = 0; y < image.Resolution().y; y += downsampleRate)
            for (int x = 0; x < image.Resolution().x; x += downsampleRate)
            {
                ImageChannelValues pSum = new ImageChannelValues(rgbDesc.size());
                for (int dy = 0; dy < downsampleRate; ++dy)
                    for (int dx = 0; dx < downsampleRate; ++dx)
                    {
                        Point2i pp = new Point2i(x + dx, y + dy);
                        if (pp.x >= res.x && pp.y >= res.y)
                            continue;

                        ImageChannelValues p = image.GetChannels(pp, rgbDesc);
                        for (int c = 0; c < (int)p.size(); ++c)
                            pSum[c] += p[c];
                    }
                for (int c = 0; c < (int)pSum.size(); ++c)
                    pSum[c] /= downsampleRate * downsampleRate;

                Console.Out.Write("AreaLightSource \"diffuse\" \"rgb L\" [ " + CF(pSum[0]) + " " + CF(pSum[1]) + " " +
                       CF(pSum[2]) + " ]\n");

                float x0 = aspect * (1 - (float)x / image.Resolution().x) - aspect / 2;
                float x1 = aspect * (1 - (float)std.min(x + downsampleRate, res.x) /
                                             image.Resolution().x) -
                           aspect / 2;
                float y0 = 1 - (float)y / image.Resolution().y;
                float y1 =
                    1 - (float)std.min(y + downsampleRate, res.y) / image.Resolution().y;
                Console.Out.Write("Shape \"bilinear\" \"point3 P\" [ " + CF(x0) + " " + CF(y0) + " 0 " + CF(x1) + " " + CF(y0) +
                       " 0 " + CF(x0) + " " + CF(y1) + " 0 " + CF(x1) + " " + CF(y1) + " 0 ]\n");
            }
        Console.Out.Write("AttributeEnd\n");

        return 0;
    }

    static int makeequiarea(List<string> args)
    {
        string inFilename = "", outFilename = "";
        int resolution = 0;

        Action<string> onError = (string err) =>
        {
            usage("makeequiarea", err);
            Environment.Exit(1);
        };
        int end = args.Count;
        for (int iter = 0; iter < end; ++iter)
        {
            if (ParseArg(args, ref iter, end, "resolution", ref resolution, onError) ||
                ParseArg(args, ref iter, end, "outfile", ref outFilename, onError))
            {
                // success
            }
            else if (args[iter][0] == '-')
                usage("makeequiarea", StringPrintf("%s: unknown command flag", args[iter]));
            else if (inFilename.Length == 0)
            {
                inFilename = args[iter];
            }
            else
                usage("makeequiarea", "multiple input filenames provided.");
        }
        if (inFilename.Length == 0)
            usage("makeequiarea", "input image filename must be provided.");
        if (outFilename.Length == 0)
            usage("makeequiarea", "output image filename must be provided.");

        ImageAndMetadata latlong = Image.Read(inFilename);
        Image latlongImage = latlong.image;

        if (2 * latlongImage.Resolution().y != latlongImage.Resolution().x)
            Warning(StringPrintf("%s: esolution (%d, %d) doesn't have a 2:1 aspect ratio. " +
                    "It is doubtful that this is a lat-long environment map.",
                    inFilename, latlongImage.Resolution().x, latlongImage.Resolution().y));

        if (resolution == 0)
            resolution = latlongImage.Resolution().x;

        // TODO: should we check that lat-long has RGB here?
        // Should we only copy over RGB?
        Image equiRectImage = new Image(latlongImage.Format(), new Point2i(resolution, resolution),
                                        latlongImage.ChannelNames(), latlongImage.Encoding());

        GaussianFilter filter = new GaussianFilter(new Vector2f(1.5f, 1.5f), 2);
        int sqrtSamples = 6;
        WrapMode2D latlongWrap = new WrapMode2D(WrapMode.Repeat, WrapMode.Clamp);
        int resolutionC = resolution;

        ParallelFor(0, resolutionC, (long v0, long v1) =>
        {
            RNG rng = new RNG((ulong)v0);
            for (int v = (int)v0; v < (int)v1; ++v)
                for (int u = 0; u < resolutionC; ++u)
                {
                    Float sumWeight = 0;
                    ImageChannelValues sumSamples = new ImageChannelValues((size_t)latlongImage.NChannels(), 0.0f);

                    for (int dv = 0; dv < sqrtSamples; ++dv)
                        for (int du = 0; du < sqrtSamples; ++du)
                        {
                            // Stratified samples
                            // (gcc evaluates function arguments right to left; match that so the
                            // RNG stream is consumed in the same order as the C++ build.)
                            Float s2y = (dv + rng.Uniform<Float>()) / sqrtSamples;
                            Float s2x = (du + rng.Uniform<Float>()) / sqrtSamples;
                            Point2f s2 = new Point2f(s2x, s2y);
                            FilterSample fs = filter.Sample(s2);
                            // Map to a point in the equirect map for the current
                            // pixel.
                            Point2f pSquare = new Point2f((u + 0.5f + fs.p.x) / resolutionC,
                                                          (v + 0.5f + fs.p.y) / resolutionC);
                            pSquare = WrapEqualAreaSquare(pSquare);

                            // Get corresponding pixel values from the lat-long map.
                            Vector3f dir = EqualAreaSquareToSphere(pSquare);
                            Float theta = SphericalTheta(dir), phi = SphericalPhi(dir);
                            Point2f p = new Point2f(phi / (2 * Pi), theta / Pi);
                            ImageChannelValues values = latlongImage.Bilerp(p, latlongWrap);

                            // Accumulate
                            for (int i = 0; i < (int)values.size(); ++i)
                                sumSamples[i] += fs.weight * values[i];
                            sumWeight += fs.weight;
                        }
                    for (int i = 0; i < (int)sumSamples.size(); ++i)
                        sumSamples[i] = std.max((Float)0, sumSamples[i] / sumWeight);
                    equiRectImage.SetChannels(new Point2i(u, v), sumSamples);
                }
        });

        ImageMetadata equiRectMetadata = new ImageMetadata();
        equiRectMetadata.cameraFromWorld = latlong.metadata.cameraFromWorld;
        equiRectMetadata.colorSpace = latlong.metadata.colorSpace;
        equiRectMetadata.strings = latlong.metadata.strings;
        equiRectMetadata.stringVectors = latlong.metadata.stringVectors;
        equiRectImage.Write(outFilename, equiRectMetadata);

        return 0;
    }

    // denoise-optix and scale-optix exist in the C++ only under PBRT_BUILD_GPU_RENDERER
    // (OptiX denoiser, CUDA); they are not available in this port.

    public static int Main(string[] argv)
    {
        try
        {
            return MainImpl(argv);
        }
        catch (FatalException)
        {
            // ErrorExit() has already printed the message; the C++ calls exit(1).
            return 1;
        }
    }

    static int MainImpl(string[] argv)
    {
        PBRTOptions opt = new PBRTOptions();
        InitPBRT(opt);

        if (argv.Length < 1)
        {
            help();
            return 0;
        }

        List<string> args = GetCommandLineArguments(argv);
        string cmd = args[0];
        args.RemoveAt(0);

        int ret = 0;
        if (cmd == "average")
            return average(args);
        else if (cmd == "assemble")
            return assemble(args);
        else if (cmd == "bloom")
            return bloom(args);
        else if (cmd == "cat")
            return cat(args);
        else if (cmd == "convert")
            return convert(args);
        else if (cmd == "diff")
            return diff(args);
        else if (cmd == "error")
            return error(args);
        else if (cmd == "falsecolor")
            return falsecolor(args);
        else if (cmd == "help" || cmd == "-help" || cmd == "--help" || cmd == "-h")
            help(args);
        else if (cmd == "info")
            return info(args);
        else if (cmd == "makeequiarea")
            return makeequiarea(args);
        else if (cmd == "makeemitters")
            return makeemitters(args);
        else if (cmd == "makesky")
            return makesky(args);
        else if (cmd == "whitebalance")
            return whitebalance(args);
        else if (cmd == "scalenormalmap")
            return scalenormalmap(args);
        else if (cmd == "splitn")
            return splitn(args);
        else if (cmd == "noisybit")
        {
            // hack for brute force computation of ideal filter weights.
            string filename = "";
            int[] pixel = { 0, 0 };
            int width = 10;
            Float sigma = 1;
            int nInstances = 100;
            Action<string> onError = (string err) =>
            {
                usage("noisybit", err);
                Environment.Exit(1);
            };
            int end = args.Count;
            for (int iter = 0; iter < end; ++iter)
            {
                if (ParseArg(args, ref iter, end, "pixel", new pstd.span<int>(pixel), onError) ||
                    ParseArg(args, ref iter, end, "width", ref width, onError) ||
                    ParseArg(args, ref iter, end, "sigma", ref sigma, onError) ||
                    ParseArg(args, ref iter, end, "n", ref nInstances, onError))
                    ;  // yaay
                else if (filename.Length == 0)
                {
                    filename = args[iter];
                }
                else
                    onError(StringPrintf("unexpected argument \"%s\"", args[iter]));
            }
            CHECK(filename.Length != 0);
            ImageAndMetadata imRead = Image.Read(filename);
            ImageChannelDesc rgbDesc = imRead.image.GetChannelDesc(new List<string> { "R", "G", "B" });
            CHECK((bool)rgbDesc);
            Image image = imRead.image.SelectChannels(rgbDesc);
            if (pixel[0] - width < 0 || pixel[0] + width >= image.Resolution().x ||
                pixel[0] - width < 0 || pixel[0] + width >= image.Resolution().y)
            {
                Error(StringPrintf(
                    "%s: pixel (%d, %d) with width %d doesn't work with resolution (%d, %d).",
                    filename, pixel[0], pixel[0], width, image.Resolution().x,
                    image.Resolution().y));
                return 1;
            }
            int nPixels = Sqr(2 * width + 1);
            CHECK_GE(3 * nInstances, nPixels);  // want to be overconstrained
            RNG rng = new RNG();

            StringBuilder m = new StringBuilder();
            for (int i = 0; i < nInstances; ++i)
                for (int c = 0; c < 3; ++c)
                {
                    for (int dx = -width; dx <= width; ++dx)
                        for (int dy = -width; dy <= width; ++dy)
                        {
                            Float noise = SampleNormal(rng.Uniform<Float>(), 0, (Float).1);
                            m.Append(((dx > -width || dy > -width) ? "," : " ") +
                                    CF(image.GetChannel(new Point2i(pixel[0] + dx, pixel[1] + dy), c)
                                        + noise) + " ");
                        }
                    m.Append("\n");
                }
            File.WriteAllText("m.csv", m.ToString());
            // what it should equal
            StringBuilder b = new StringBuilder();
            for (int i = 0; i < nInstances; ++i)
                for (int c = 0; c < 3; ++c)
                    b.Append(CF(image.GetChannel(new Point2i(pixel[0], pixel[1]), c)) + "\n");
            File.WriteAllText("b.csv", b.ToString());
        }
        else
        {
            Console.Error.Write(StringPrintf("imgtool: unknown command \"%s\".\n", cmd));
            help();
            CleanupPBRT();
            return 1;
        }
        CleanupPBRT();
        return ret;
    }
}
