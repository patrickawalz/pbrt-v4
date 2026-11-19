// (not in pbrt-v4) laserlight2.cs
//
// The beam of "laser" (laserlight.cs: a Gaussian beam, sampled exactly) with any spectrum, or with an image across it:
//
//   LightSource "laser2" "point3 from" [ 0 0 0 ] "point3 to" [ 0 0 1 ] "rgb L" [ 1 0.2 0.1 ] "float power" 5
//       "float waist" 0.002 "float divergence" 0.001
//   LightSource "laser2" ... "texture L" "pattern" "float imagewidth" 0.01
//   LightSource "laser2" ... "string filename" "slide.png" "float imagewidth" 0.01
//
// What differs from "laser". The light is no spectral line: "L" is any spectrum, and the film samples its wavelengths
// as it does without a laser. The far-field half angle "divergence" is given, the same for all wavelengths (that of a
// laser follows from its wavelength; a beam of many wavelengths is no single mode). "power" is that of the spot light
// of pbrt: the luminous power of the beam, in the photometric scale of the other lights.
//
// "texture L" names a spectrum texture of the scene, "filename" an image: either is a slide in the plane of the light.
// A texture is asked at the point of the plane (in render space, for the textures of space) with the coordinates (u, v)
// of the slide, from (0, 0) at its lower left to (1, 1); the slide of a texture is square. The ray that leaves a point
// of the plane carries the colour of the slide there, "imagewidth" being the width of the image in scene units (its height by its aspect; four beam radii if
// not given), and "vector3 up" the direction of its top (0 1 0; taken across the beam): seen along the beam with that
// direction up, as a camera of pbrt looks, the image is upright and not mirrored. Outside the image nothing is let through. "power" is the power of the beam before the slide, for a white
// one. The slide is sharp at the light and blurs with the distance, by the distance times the divergence: the rays
// that leave one point spread by that angle. The diffraction of the pattern is not modelled.
//
// Everything else is the beam of "laser": the Gaussians, the waist and "waistdistance", the polarisation, the sampling
// toward a point and from the light, the bounds, and the distance where a ray crosses the beam.

using System;

namespace pbrt
{
    public sealed unsafe class LaserLight2 : GaussianBeamLight
    {
        // `L` times `scale`: the power of the beam per nanometre; with an image, `scale` times the spectrum of the colour of
        // the image at the point of the plane of the light, `imageWidth` being the width of the image in scene units
        public LaserLight2(Transform renderFromLight, MediumInterface mediumInterface, double waist, double divergence,
                           double waistDistance, Spectrum L, Float scale, Image image, RGBColorSpace imageColorSpace,
                           SpectrumTexture texture, double imageWidth, Vector3f up, double stokes1, double stokes2, double stokes3, Vector3f axis)
            : base(renderFromLight, mediumInterface, waist, divergence, waistDistance, stokes1, stokes2, stokes3, axis)
        {
            this.scale = scale;
            this.image = image;
            this.imageColorSpace = imageColorSpace;
            this.texture = texture;
            this.imageWidth = imageWidth;
            if (image == null && texture == null)
            {
                this.L = LookupSpectrum(L);
                luminous = scale * pbrt.SpectrumToPhotometric(L) / CIE_Y_integral;
                return;
            }
            imageHeight = image != null ? imageWidth * image.Resolution().y / image.Resolution().x : imageWidth;
            // The top of the slide in the plane of the light, and its right: up cross the direction of the beam, as the
            // right of a camera of pbrt is (LookAt)
            Vector3f upLight = renderFromLight.ApplyInverse(up);
            double length = Math.Sqrt((double)upLight.x * upLight.x + (double)upLight.y * upLight.y);
            if (length > 1e-6)
            {
                upX = upLight.x / length;
                upY = upLight.y / length;
            }
            else
            {
                upX = 0;
                upY = 1;
            }

            // The luminance that the slide lets through: the mean of its luminance under the Gaussian of the beam in the
            // plane of the light, over a grid of points (the pixels of an image). It is what the light samplers weigh the
            // light by, and is kept above zero: a slide that the grid finds black may let light through between its points.
            int columns = image != null ? image.Resolution().x : 64, rows = image != null ? image.Resolution().y : 64;
            SampledWavelengths lambda = SampledWavelengths.SampleVisible(0.5f);
            SampledSpectrum efficiency = Spectra.Y().Sample(lambda) / lambda.PDF();
            SampledSpectrum white = new RGBIlluminantSpectrum(imageColorSpace, new RGB(1, 1, 1)).Sample(lambda);
            double whiteLuminance = (efficiency * white).Average();
            double sum = 0;
            for (int y = 0; y < rows; ++y)
            {
                for (int x = 0; x < columns; ++x)
                {
                    double alongRight = ((x + 0.5) / columns - 0.5) * imageWidth;
                    double alongUp = (0.5 - (y + 0.5) / rows) * imageHeight;
                    double sourceX = alongRight * upY + alongUp * upX, sourceY = -alongRight * upX + alongUp * upY;
                    double weight = 2 / (Math.PI * sourceWidthSquared) *
                                    Math.Exp(-2 * (sourceX * sourceX + sourceY * sourceY) / sourceWidthSquared) *
                                    (imageWidth / columns) * (imageHeight / rows);
                    sum += weight * (efficiency * Slide(lambda, sourceX, sourceY)).Average() / whiteLuminance;
                }
            }
            luminous = (Float)(scale * pbrt.SpectrumToPhotometric(imageColorSpace.illuminant) / CIE_Y_integral * Math.Max(sum, 1e-4));
        }

        protected override SampledSpectrum Emitted(SampledWavelengths lambda, double sourceX, double sourceY)
        {
            if (image == null && texture == null)
            {
                return scale * L.Sample(lambda);
            }
            return scale * Slide(lambda, sourceX, sourceY);
        }

        // What the slide lets through at a point of the plane of the light, for a beam of unit power per nanometre
        private SampledSpectrum Slide(SampledWavelengths lambda, double sourceX, double sourceY)
        {
            double alongRight = sourceX * upY - sourceY * upX, alongUp = sourceX * upX + sourceY * upY;
            Point2f uv = new Point2f((Float)(0.5 + alongRight / imageWidth), (Float)(0.5 - alongUp / imageHeight));
            if (!(uv.x >= 0) || !(uv.x <= 1) || !(uv.y >= 0) || !(uv.y <= 1))
            {
                return new SampledSpectrum(0.0f);
            }
            if (texture != null)
            {
                // (the v of a texture goes up)
                Point3f p = renderFromLight.Apply(new Point3f((Float)sourceX, (Float)sourceY, 0));
                TextureEvalContext ctx = new TextureEvalContext(p, new Vector3f(0, 0, 0), new Vector3f(0, 0, 0), new Normal3f(0, 0, 0),
                                                                new Point2f(uv.x, 1 - uv.y), 0, 0, 0, 0, 0);
                return pbrt.ClampZero(new UniversalTextureEvaluator().Apply(texture, ctx, lambda));
            }
            RGB rgb = default;
            for (int c = 0; c < 3; ++c)
            {
                rgb[c] = image.BilerpChannel(uv, c);
            }
            RGBIlluminantSpectrum spectrum = new RGBIlluminantSpectrum(imageColorSpace, pbrt.ClampZero(rgb));
            return spectrum.Sample(lambda);
        }

        protected override Float Luminous()
        {
            return luminous;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LaserLight2 %s waist: %f divergence: %f scale: %f ]", BaseToString(), waist, divergence, scale);
        }

        public static LaserLight2 Create(Transform renderFromLight, Medium medium, TextureParameterDictionary parameters,
                                         RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            Float scale = parameters.GetOneFloat("scale", 1.0f);
            Float power = parameters.GetOneFloat("power", 1.0f);
            double waist = parameters.GetOneFloat("waist", 0.0005f);
            double divergence = parameters.GetOneFloat("divergence", 0.001f);
            double waistDistance = parameters.GetOneFloat("waistdistance", 0.0f);
            Vector3f axis = parameters.GetOneVector3f("polarisationaxis", new Vector3f(0, 0, 1));
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            if (!(waist > 0) || !(divergence > 0) || !(power >= 0))
            {
                pbrt.ErrorExit(loc, "\"laser2\": \"waist\" and \"divergence\" must be positive, and \"power\" not negative.");
            }
            Polarisation(parameters, loc, out double stokes1, out double stokes3);
            Transform finalRenderFromLight = Placement(renderFromLight, parameters);
            Vector3f axisRender = pbrt.Normalize(renderFromLight.Apply(axis));

            double beamRadius = Math.Sqrt(waist * waist + waistDistance * waistDistance * divergence * divergence);
            Vector3f up = renderFromLight.Apply(parameters.GetOneVector3f("up", new Vector3f(0, 1, 0)));
            bool named = ((ParameterDictionary)parameters).GetTexture("L").Length != 0;
            if (named)
            {
                if (filename.Length != 0)
                {
                    pbrt.ErrorExit(loc, "\"laser2\": both a texture \"L\" and \"filename\" are given.");
                }
                SpectrumTexture texture = parameters.GetSpectrumTextureOrNull("L", SpectrumType.Illuminant, alloc);
                double width = parameters.GetOneFloat("imagewidth", (Float)(4 * beamRadius));
                if (!(width > 0))
                {
                    pbrt.ErrorExit(loc, "\"laser2\": \"imagewidth\" must be positive.");
                }
                scale *= power / pbrt.SpectrumToPhotometric(colorSpace.illuminant);
                return new LaserLight2(finalRenderFromLight, new MediumInterface(medium), waist, divergence, waistDistance, null, scale,
                                       null, colorSpace, texture, width, up, stokes1, 0, stokes3, axisRender);
            }
            if (filename.Length == 0)
            {
                Spectrum L = parameters.GetOneSpectrum("L", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
                // (the spectrum to a luminous power of one, as the lights of pbrt do, then to "power")
                scale *= power / pbrt.SpectrumToPhotometric(L);
                return new LaserLight2(finalRenderFromLight, new MediumInterface(medium), waist, divergence, waistDistance, L, scale,
                                       null, null, null, 0, up, stokes1, 0, stokes3, axisRender);
            }

            if (parameters.GetSpectrumArray("L", SpectrumType.Illuminant, alloc).Count != 0)
            {
                pbrt.ErrorExit(loc, "\"laser2\": both \"L\" and \"filename\" are given; the image has the colour.");
            }
            ImageAndMetadata imageAndMetadata = Image.Read(filename, alloc);
            if (imageAndMetadata.image.HasAnyInfinitePixels() || imageAndMetadata.image.HasAnyNaNPixels())
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: image has pixel values that are not finite and so is not suitable as a light.",
                                                      filename));
            }
            RGBColorSpace imageColorSpace = imageAndMetadata.metadata.GetColorSpace();
            ImageChannelDesc channelDesc = imageAndMetadata.image.GetChannelDesc(new string[] { "R", "G", "B" });
            if (!channelDesc)
            {
                pbrt.ErrorExit(loc, "Image provided to \"laser2\" light must have R, G, and B channels.");
            }
            Image image = imageAndMetadata.image.SelectChannels(channelDesc, alloc);
            double imageWidth = parameters.GetOneFloat("imagewidth", (Float)(4 * beamRadius));
            if (!(imageWidth > 0))
            {
                pbrt.ErrorExit(loc, "\"laser2\": \"imagewidth\" must be positive.");
            }
            scale *= power / pbrt.SpectrumToPhotometric(imageColorSpace.illuminant);
            return new LaserLight2(finalRenderFromLight, new MediumInterface(medium), waist, divergence, waistDistance, null, scale,
                                   image, imageColorSpace, null, imageWidth, up, stokes1, 0, stokes3, axisRender);
        }

        private readonly DenselySampledSpectrum L;
        private readonly Float scale, luminous;
        private readonly Image image;
        private readonly SpectrumTexture texture;
        private readonly RGBColorSpace imageColorSpace;
        private readonly double imageWidth, imageHeight, upX, upY;
    }
}
