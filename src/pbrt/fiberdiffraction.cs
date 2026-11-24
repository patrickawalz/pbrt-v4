// (not in pbrt-v4) fiberdiffraction.cs
//
// The light that a fiber bends around itself (diffraction), added to a fiber of rays as in
//
//   Mengqi Xia, Bruce Walter, Christophe Hery, Olivier Maury, Eric Michielssen, Steve Marschner. 2023. A Practical Wave
//   Optics Reflection Model for Hair and Fur. ACM Trans. Graph. 42, 4, Article 1 (their equations 22 and 23),
//
// after the simulations of Xia et al. 2020 (A Wave Optics Based Fiber Scattering Model): a fiber takes out of a beam about
// twice the light that meets it, and sends the other half on, in a narrow lobe about the direction in which the light was
// going, as a slit of the width of the fiber does:
//
//   S = (S_ray + f_diffract) / 2,    f_diffract(theta, phi_d, a) = a cos theta sinc^2(a cos theta sin phi_d),
//
// with a the diameter of the fiber in wavelengths, phi_d the angle around the fiber from the direction of the light, and
// along the fiber the Mp of HairBxDF. The fibers of the scene are to be made twice as wide for it (the paper's rule: the
// half that is diffracted is light that passed the fiber by).
//
// The lobe is in closed form here, where the paper has a table of it and another to draw from: its part around the fiber is
// used with its exact integral, cos phi_d a c sinc^2(a c sin phi_d) / Z(a c) with Z(A) the integral of sinc^2 from -A to A
// (by the sine integral), which is the paper's lobe to within its own rounding of that integral to 1 (and of cos phi_d to
// 1: the lobe is a fraction of a degree wide for a hair); and it is drawn by rejection from min(1, 1 / (pi x)^2).
// It depends on the wavelength: each of the wavelengths of a ray has its own lobe.
//
// smooth (not in the paper): beyond three quarters of the way to its first dark line (|x| > 3/4, x = a c sin phi_d, where
// sin^2(pi x) is one half), the bright and dark lines of the lobe are left out for their mean, 1 / (2 pi^2 x^2), which the
// lobe joins there without a step; with its own integral, Z_s(A) = Z(3/4) + (4/3 - 1/A) / pi^2. The lines are where
// the lobes of two wavelengths differ most (a direction bright for one is dark for the next), which a few samples of a
// pixel show as colored speckle; fibers of many diameters blur them in the same way.

using System;

namespace pbrt
{
    // FiberPassBxDF Definition
    // The part of a strand drawn twice as wide that is not the fiber, where a pixel is smaller than the fiber (FurMaterial,
    // "field" "multiscale"): the light goes straight on.
    public sealed unsafe class FiberPassBxDF : BxDF
    {
        // FiberPassBxDF Public Methods
        public FiberPassBxDF()
        {
        }

        public static string Name()
        {
            return "FiberPassBxDF";
        }

        public override string ToString()
        {
            return "[ FiberPassBxDF ]";
        }

        public override void Regularize()
        {
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.Transmission | BxDFFlags.Specular;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            return new SampledSpectrum(0f);
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            return 0;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if ((sampleFlags & BxDFReflTransFlags.Transmission) == 0 || pbrt.AbsCosTheta(wo) == 0)
            {
                return default;
            }
            Vector3f wi = -wo;
            return new BSDFSample(new SampledSpectrum(1 / pbrt.AbsCosTheta(wi)), wi, 1, BxDFFlags.SpecularTransmission);
        }
    }

    // FiberDiffractionBxDF Definition
    public sealed unsafe class FiberDiffractionBxDF : BxDF
    {
        // FiberDiffractionBxDF Public Methods

        // sizes: the diameter of the fiber in wavelengths, for each wavelength of the ray; variance: of Mp along the fiber;
        // estimated: the fiber of rays gives estimates of its value and density (FurBxDF)
        // paperIntegral: the lobe as the paper writes it, a c sinc^2(a c sin phi_d), without its integral divided out and
        // without cos phi_d (its samples are those of the lobe with them, which is all but the same)
        public FiberDiffractionBxDF(BxDF rays, SampledSpectrum sizes, Float variance, bool estimated, bool smooth, bool paperIntegral)
        {
            this.smooth = smooth;
            this.paperIntegral = paperIntegral;
            this.rays = rays;
            this.sizes = sizes;
            this.variance = variance;
            this.estimated = estimated;
        }

        public static string Name()
        {
            return "FiberDiffractionBxDF";
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FiberDiffractionBxDF rays: %s ]", rays);
        }

        public override void Regularize()
        {
            rays.Regularize();
        }

        public override BxDFFlags Flags()
        {
            return rays.Flags();
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            SampledSpectrum lobe = Lobe(wo, wi, out Float density);
            if (pbrt.AbsCosTheta(wi) > 0)
            {
                lobe /= pbrt.AbsCosTheta(wi);
            }
            return (rays.f(wo, wi, mode) + lobe) * 0.5f;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            Lobe(wo, wi, out Float density);
            return 0.5f * (rays.PDF(wo, wi, mode, sampleFlags) + density);
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            if (uc >= 0.5f)
            {
                // A direction of the fiber of rays
                pstd.optional<BSDFSample> sampled = rays.Sample_f(wo, 2 * (uc - 0.5f), u, mode, sampleFlags);
                if (!sampled)
                {
                    return default;
                }
                BSDFSample sample = sampled.value();
                SampledSpectrum lobe = Lobe(wo, sample.wi, out Float density);
                if (pbrt.AbsCosTheta(sample.wi) > 0)
                {
                    lobe /= pbrt.AbsCosTheta(sample.wi);
                }
                sample.f = (sample.f + lobe) * 0.5f;
                sample.pdf = 0.5f * (sample.pdf + density);
                return sample;
            }
            // A direction of the lobe: along the fiber by Mp about the direction of the light, and around it by the lobe of one of
            // the wavelengths
            RNG rng = new RNG(pbrt.Hash(pbrt.GetOptions().seed, wo), pbrt.Hash(uc, u));
            Float sinTheta_o = wo.x, cosTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_o));
            Float cosTheta = 1 + variance * (Float)Math.Log(Math.Max(u[0], 1e-5f) + (1 - u[0]) * Math.Exp(-2 / variance));
            Float sinTheta = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta));
            Float sinTheta_i = -cosTheta * sinTheta_o + sinTheta * (Float)Math.Cos(2 * Math.PI * u[1]) * cosTheta_o;
            Float cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            int wavelength = Math.Min(pbrt.NSpectrumSamples - 1, (int)(rng.Uniform<Float>() * pbrt.NSpectrumSamples));
            double width = sizes[wavelength] * cosTheta_o;
            if (!(width > 0))
            {
                return default;
            }
            double x = 0;
            bool drawn = false;
            for (int attempt = 0; attempt < 64 && !drawn; ++attempt)
            {
                x = Envelope(width, rng.Uniform<Float>());
                double bound = Math.Min(1, 1 / (Math.PI * Math.PI * x * x));
                drawn = rng.Uniform<Float>() * bound <= Profile(x);
            }
            if (!drawn)
            {
                return default;
            }
            // (phi_d is from the direction that the light goes in: across the fiber from wo)
            Float phi_i = (Float)(Math.Atan2(wo.z, wo.y) + Math.PI + Math.Asin(Math.Max(-1, Math.Min(1, x / width))));
            Vector3f wi = new Vector3f(sinTheta_i, cosTheta_i * (Float)Math.Cos(phi_i), cosTheta_i * (Float)Math.Sin(phi_i));
            SampledSpectrum value = Lobe(wo, wi, out Float lobeDensity);
            if (!(lobeDensity > 0) || pbrt.AbsCosTheta(wi) == 0)
            {
                return default;
            }
            BSDFSample diffracted = new BSDFSample((rays.f(wo, wi, mode) + value / pbrt.AbsCosTheta(wi)) * 0.5f, wi, 0.5f * (rays.PDF(wo, wi, mode, sampleFlags) + lobeDensity),
                                                   Flags());
            diffracted.pdfIsProportional = estimated;
            return diffracted;
        }

        // The lobe at wi for each solid angle (times |cos theta_i| of the BSDF, as the lobes of HairBxDF are before their
        // division), for each wavelength; and its mean over the wavelengths, the density of its samples
        public SampledSpectrum Lobe(Vector3f wo, Vector3f wi, out Float density)
        {
            density = 0;
            SampledSpectrum lobe = new SampledSpectrum(0f);
            Float sinTheta_o = wo.x, cosTheta_o = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_o));
            Float sinTheta_i = wi.x, cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            double around = Math.Atan2(wi.z, wi.y) - Math.Atan2(wo.z, wo.y) - Math.PI;
            while (around > Math.PI)
            {
                around -= 2 * Math.PI;
            }
            while (around < -Math.PI)
            {
                around += 2 * Math.PI;
            }
            if (Math.Abs(around) >= 0.5 * Math.PI)
            {
                return lobe;
            }
            Float along = HairBxDF.Mp(cosTheta_i, cosTheta_o, sinTheta_i, sinTheta_o, variance);
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                double width = sizes[i] * cosTheta_o;
                if (!(width > 0))
                {
                    continue;
                }
                double value = Math.Cos(around) * width * Profile(width * Math.Sin(around)) / (smooth ? SmoothWhole(width) : Whole(width));
                lobe[i] = along * (Float)(paperIntegral ? width * Profile(width * Math.Sin(around)) : value);
                density += along * (Float)value / pbrt.NSpectrumSamples;
            }
            return lobe;
        }

        // The lobe across the fiber before its integral is divided out: sinc^2, or with its lines beyond |x| = 3/4 left out for
        // their mean
        private double Profile(double x)
        {
            return smooth && Math.Abs(x) > 0.75 ? 1 / (2 * Math.PI * Math.PI * x * x) : Sinc2(x);
        }

        // The integral of the smooth lobe from -A to A
        public static double SmoothWhole(double A)
        {
            return A <= 0.75 ? Whole(A) : Whole(0.75) + (4.0 / 3 - 1 / A) / (Math.PI * Math.PI);
        }

        // sinc^2(x) = (sin(pi x) / (pi x))^2
        public static double Sinc2(double x)
        {
            double argument = Math.PI * x;
            if (Math.Abs(argument) < 1e-4)
            {
                return 1 - argument * argument / 3;
            }
            double sinc = Math.Sin(argument) / argument;
            return sinc * sinc;
        }

        // The integral of sinc^2 from -A to A: (2 / pi) (Si(2 pi A) - sin^2(pi A) / (pi A))
        public static double Whole(double A)
        {
            double argument = Math.PI * A, sine = Math.Sin(argument);
            return 2 / Math.PI * (SineIntegral(2 * argument) - sine * sine / argument);
        }

        // Si(x), the integral of sin(t) / t from 0 to x: its series for small x, and pi / 2 - f cos x - g sin x with the
        // rational f and g of Abramowitz and Stegun (5.2.38, 5.2.39) beyond
        public static double SineIntegral(double x)
        {
            if (x < 6)
            {
                double term = x, sum = x;
                for (int n = 1; n < 40; ++n)
                {
                    term *= -x * x / ((2 * n) * (2 * n + 1));
                    sum += term / (2 * n + 1);
                }
                return sum;
            }
            double x2 = x * x;
            double f = (x2 * (x2 * (x2 * (x2 + 38.027264) + 265.187033) + 335.677320) + 38.102495) /
                       (x2 * (x2 * (x2 * (x2 + 40.021433) + 322.624911) + 570.236280) + 157.105423) / x;
            double g = (x2 * (x2 * (x2 * (x2 + 42.242855) + 302.757865) + 352.018498) + 21.821899) /
                       (x2 * (x2 * (x2 * (x2 + 48.196927) + 482.485984) + 1114.978885) + 449.690326) / x2;
            return 0.5 * Math.PI - f * Math.Cos(x) - g * Math.Sin(x);
        }

        // FiberDiffractionBxDF Private Methods

        // A value in [-A, A] with a density in proportion to min(1, 1 / (pi x)^2)
        private static double Envelope(double A, double u)
        {
            double knee = 1 / Math.PI;
            if (A <= knee)
            {
                return (2 * u - 1) * A;
            }
            // (the middle, of the mass 2 / pi, and the two tails, each of the mass 1 / pi - 1 / (pi^2 A))
            double tail = knee - 1 / (Math.PI * Math.PI * A), total = 2 * knee + 2 * tail;
            double mass = u * total;
            if (mass < tail)
            {
                // The left tail from -A: the mass up to x is (1 / pi^2) (1 / |x| - 1 / A)
                return -1 / (Math.PI * Math.PI * mass + 1 / A);
            }
            mass -= tail;
            if (mass < 2 * knee)
            {
                return -knee + mass;
            }
            mass -= 2 * knee;
            return 1 / (Math.PI - Math.PI * Math.PI * mass);
        }

        // FiberDiffractionBxDF Private Members
        private readonly BxDF rays;
        private readonly SampledSpectrum sizes;
        private readonly Float variance;
        private readonly bool estimated, smooth, paperIntegral;
    }
}
