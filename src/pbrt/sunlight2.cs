// (not in pbrt-v4) sunlight2.cs
//
// "sun2": the light "sun" (lights.cs: a distant disc of a finite angular size, an infinite light, so it is seen by camera
// rays and found by BSDF and phase sampling) with three additions, each of them optional. Without them it is "sun":
//
//   LightSource "sun2" "point3 from" [ -0.5 -0.6 0.45 ] "point3 to" [ 0 0 0 ]
//       "string spectrum" "solar" "float turbidity" 3 "string limb" "neckel"
//
// "spectrum L", "float scale", "point3 from", "point3 to", "float angle" (0.5334 degrees), "bool limbdarkening" (true),
// "float illuminance": as for "sun".
//
// 1. "string spectrum" "solar": the sun above the atmosphere in place of "L", in absolute units: the table of the Prague
//    sky model (pragueskydata.cs, 280 to 2500 nm by 1 nm, 1277 W/m^2 over that range) as the irradiance of a surface that
//    faces the sun, whatever "angle" is. The radiance is in the units of the sky lights ("hosek", "prague") and of the
//    thermal emitters: W m^-2 sr^-1 nm^-1 times 683, so that the Y of the film is cd/m^2 and the sun gives about 129000
//    lux above the atmosphere. "scale" multiplies it. With "illuminance" the spectrum keeps its shape and the sun gives
//    that illuminance (above the atmosphere), as "sun" does with "L".
//
// 2. "float turbidity" (absent or 0: no atmosphere; from 1, pure air, by 2 to 3, clear, to 10, hazy): what of the sun's
//    own light comes through the atmosphere to an observer on the ground, by the elevation of the sun over the horizon of
//    "vector3 up" (0 0 1), in closed form:
//      air (Rayleigh)   optical depth 0.008569 l^-4 (1 + 0.0113 l^-2 + 0.00013 l^-4), l in micrometres (Hansen and
//                       Travis 1974)
//      aerosols         b l^-1.3 with b = 0.04608 turbidity - 0.04586 (Angstrom's law, as Preetham et al. 1999 have it)
//      ozone            the Chappuis band as a bell of two widths around 592 nm, 0.0403 at its peak for 300 Dobson units
//                       ("float ozone", 300): a fit of ours to the band, good to about 15 percent of what ozone takes,
//                       which is at most 4 percent at the zenith
//    each times its air mass: Kasten and Young (1989) for air and aerosols, 1 / (cos z + 0.50572 (96.07995 - z)^-1.6364)
//    with the zenith angle z in degrees, and that of a layer at 22 km for ozone. The transmittance is that of each point
//    of the disc (its lower edge is redder near the horizon). A sun below the horizon keeps the transmittance of the
//    horizon; there is no ground in this light. Not in it: water vapour and oxygen (narrow bands at 720, 760 and 820 nm),
//    the height of the observer, refraction, and the light that the atmosphere scatters, which is the sky ("hosek",
//    "prague", or a medium).
//
// 3. "string limb": "linear" (the default: that of "sun"), "neckel", or "none" (as "bool limbdarkening" false).
//    "neckel": the darkening of the solar disc toward its limb as measured, by wavelength: the polynomials of the fifth
//    order in the cosine mu of the angle at the sun's surface of Neckel and Labs, "Solar limb darkening 1986-1990" (Solar
//    Physics 153, 1994), with their coefficients as functions of the wavelength (Neckel, Solar Physics 229, 2005) for
//    303 to 373 nm, 385 to 423 nm and 423 to 1100 nm; between 373 and 385 nm (the Balmer jump) the nearer of the two, and
//    held at the ends. As "linear" it keeps the irradiance: the profile is divided by its mean over the disc.

using System;

namespace pbrt
{
    public sealed unsafe class SunLight2 : LightBase
    {
        // Linear limb-darkening coefficient u(lambda) in I(mu) = I0 (1 - u (1 - mu)). Approximate
        // (solar continuum, about 0.93 at 400 nm to 0.43 at 900 nm); wavelength dependent so the
        // limb is redder than the centre.
        static readonly Float[] limbWavelength = { 300, 400, 450, 500, 550, 600, 700, 800, 900, 1100 };
        static readonly Float[] limbU = { 1.00f, 0.93f, 0.80f, 0.72f, 0.65f, 0.60f, 0.53f, 0.47f, 0.43f, 0.36f };

        static Float LimbU(Float lambdaNm)
        {
            int n = limbWavelength.Length;
            if (lambdaNm <= limbWavelength[0])
            {
                return limbU[0];
            }
            if (lambdaNm >= limbWavelength[n - 1])
            {
                return limbU[n - 1];
            }
            int i = 0;
            while (lambdaNm > limbWavelength[i + 1])
            {
                ++i;
            }
            Float t = (lambdaNm - limbWavelength[i]) / (limbWavelength[i + 1] - limbWavelength[i]);
            return (1 - t) * limbU[i] + t * limbU[i + 1];
        }

        // The limb darkening of Neckel and Labs: the intensity at the cosine mu over that of the centre of the disc,
        // sum of a_k mu^k, at a wavelength in nm; `mean`: its mean over the disc, sum of 2 a_k / (k + 2)
        public static double NeckelLimb(double lambdaNm, double mu, out double mean)
        {
            double l = Math.Clamp(lambdaNm, 303.327, 1100.0) / 1000;
            double inverse = 1 / l;
            double inverse5 = inverse * inverse * inverse * inverse * inverse;
            double a0, a1, a2, a3, a4, a5;
            if (lambdaNm < 379)
            {
                a0 = 0.35601 - 0.085217 * inverse;
                a1 = 1.11529 + 0.085217 * inverse - 0.001871 * inverse5;
                a2 = -0.67237 + 0.003589 * inverse5;
                a3 = 0.18696 - 0.002415 * inverse5;
                a4 = 0.00038 + 0.000897 * inverse5;
                a5 = 0.01373 - 0.000200 * inverse5;
            }
            else if (lambdaNm < 422.57)
            {
                a0 = 0.09900 + 0.010643 * inverse;
                a1 = 1.96884 - 0.010643 * inverse - 0.009166 * inverse5;
                a2 = -2.80511 + 0.024873 * inverse5;
                a3 = 3.32780 - 0.029317 * inverse5;
                a4 = -2.17878 + 0.018273 * inverse5;
                a5 = 0.58825 - 0.004663 * inverse5;
            }
            else
            {
                a0 = 0.75267 - 0.265577 * inverse;
                a1 = 0.93874 + 0.265577 * inverse - 0.004095 * inverse5;
                a2 = -1.89287 + 0.012582 * inverse5;
                a3 = 2.42234 - 0.017117 * inverse5;
                a4 = -1.71150 + 0.011977 * inverse5;
                a5 = 0.49062 - 0.003347 * inverse5;
            }
            mean = a0 + 2 * a1 / 3 + a2 / 2 + 2 * a3 / 5 + a4 / 3 + 2 * a5 / 7;
            return Math.Max(0, a0 + mu * (a1 + mu * (a2 + mu * (a3 + mu * (a4 + mu * a5)))));
        }

        // What of the sun's own light comes through the atmosphere at a wavelength in nm, for the cosine of the zenith
        // angle of the direction to the sun (held at the horizon below it), the turbidity and the ozone in Dobson units
        public static double AtmosphericTransmittance(double lambdaNm, double cosZenith, double turbidity, double ozone)
        {
            double cosine = Math.Clamp(cosZenith, 0.0, 1.0);
            double zenithDegrees = Math.Acos(cosine) * 180 / Math.PI;
            // (Kasten and Young 1989)
            double airMass = 1 / (cosine + 0.50572 * Math.Pow(96.07995 - zenithDegrees, -1.6364));
            // (a layer at the height h over a planet of the radius r: (1 + h / r) / sqrt(cos^2 + 2 h / r), h 22 km)
            const double ozoneHeight = 22000.0 / 6378000.0;
            double ozoneMass = (1 + ozoneHeight) / Math.Sqrt(cosine * cosine + 2 * ozoneHeight);
            double l = lambdaNm / 1000;
            double l2 = l * l;
            double rayleigh = 0.008569 / (l2 * l2) * (1 + 0.0113 / l2 + 0.00013 / (l2 * l2));
            double beta = Math.Max(0, 0.04608 * turbidity - 0.04586);
            double aerosol = beta * Math.Pow(l, -1.3);
            double offset = lambdaNm - 592;
            double width = offset < 0 ? 54 : 62;
            double chappuis = 0.0403 * (ozone / 300) * Math.Exp(-offset * offset / (2 * width * width));
            return Math.Exp(-(rayleigh + aerosol) * airMass - chappuis * ozoneMass);
        }

        // The irradiance of the sun above the atmosphere at a wavelength in nm, W m^-2 nm^-1: the radiance of the table of
        // the Prague sky model over the disc that it is for
        public static double SolarIrradiance(double lambdaNm)
        {
            double[] table = PragueSkyData.sunRadiance;
            double index = (lambdaNm - PragueSkyData.SunRadianceStart) / PragueSkyData.SunRadianceStep;
            if (index < 0 || index > table.Length - 1)
            {
                return 0;
            }
            int low = Math.Min((int)index, table.Length - 2);
            double t = index - low;
            double sine = Math.Sin(PragueSkyModel.SunRadius);
            return ((1 - t) * table[low] + t * table[low + 1]) * Math.PI * sine * sine;
        }

        // limb: 0 none, 1 linear (that of "sun"), 2 Neckel and Labs; solar: the sun above the atmosphere in place of Lemit;
        // turbidity: 0 for no atmosphere; zenith: render space, unit length
        public SunLight2(Transform renderFromLight, Spectrum Lemit, Float scale, Float angularDiameterDegrees, int limb, bool solar, double turbidity,
                         double ozone, Vector3f zenith)
            : base(LightType.Infinite, renderFromLight, new MediumInterface())
        {
            this.Lemit = Lemit != null ? LookupSpectrum(Lemit) : null;
            this.scale = scale;
            this.limb = limb;
            this.solar = solar;
            this.turbidity = turbidity;
            this.ozone = ozone;
            this.zenith = zenith;
            double alpha = 0.5 * angularDiameterDegrees * Math.PI / 180.0;
            sinHalfAlpha2 = (Float)Math.Pow(Math.Sin(0.5 * alpha), 2);
            solidAngle = (Float)(2 * Math.PI * 2 * Math.Pow(Math.Sin(0.5 * alpha), 2));
            sunDirection = pbrt.Normalize(renderFromLight.Apply(new Vector3f(0, 0, 1)));
            Vector3f t1, t2;
            pbrt.CoordinateSystem(sunDirection, &t1, &t2);
            tangent1 = t1;
            tangent2 = t2;
        }

        // What the whole disc sends: scale times the spectrum, as the irradiance of a surface that faces the sun (above
        // the atmosphere)
        SampledSpectrum Emitted(SampledWavelengths lambda)
        {
            if (!solar)
            {
                return scale * Lemit.Sample(lambda);
            }
            SampledSpectrum E = new SampledSpectrum(0);
            for (int i = 0; i < NSpectrumSamples; ++i)
            {
                E[i] = scale * (Float)SolarIrradiance(lambda[i]);
            }
            return E;
        }

        // Radiance for a point at normalised radius^2 q (0 centre, 1 limb), in the direction w to it.
        SampledSpectrum Radiance(Float q, Vector3f w, SampledWavelengths lambda)
        {
            SampledSpectrum L = Emitted(lambda);
            SampledSpectrum f = new SampledSpectrum(1);
            if (limb != 0)
            {
                Float mu = (Float)Math.Sqrt(Math.Max(0, 1 - q));
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    if (limb == 1)
                    {
                        Float u = LimbU(lambda[i]);
                        // Normalised so that the disk irradiance equals scale*L: mean of (1-u(1-mu)) is 1-u/3.
                        f[i] = (1 - u * (1 - mu)) / (1 - u / 3);
                    }
                    else
                    {
                        double profile = NeckelLimb(lambda[i], mu, out double mean);
                        f[i] = (Float)(profile / mean);
                    }
                }
            }
            if (turbidity > 0)
            {
                double cosZenith = pbrt.Dot(pbrt.Normalize(w), zenith);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    f[i] *= (Float)AtmosphericTransmittance(lambda[i], cosZenith, turbidity, ozone);
                }
            }
            return L * f / solidAngle;
        }

        // sin^2(theta/2) of the angle between a direction and the sun centre, accurate for tiny angles.
        Float HalfSine2(Vector3f d)
        {
            d = pbrt.Normalize(d);
            Vector3f e = d - sunDirection;
            return (e.x * e.x + e.y * e.y + e.z * e.z) * 0.25f;
        }

        public override SampledSpectrum Le(Ray ray, SampledWavelengths lambda)
        {
            Float s2 = HalfSine2(ray.d);
            if (s2 >= sinHalfAlpha2)
            {
                return new SampledSpectrum(0);
            }
            return Radiance(s2 / sinHalfAlpha2, ray.d, lambda);
        }

        Vector3f SampleDirection(Point2f u, out Float q)
        {
            q = u.x;
            Float s2 = q * sinHalfAlpha2;
            Float sinTheta = 2 * (Float)Math.Sqrt(s2 * (1 - s2));
            Float cosTheta = 1 - 2 * s2;
            Float phi = 2 * Pi * u.y;
            return sunDirection * cosTheta +
                   (tangent1 * (Float)Math.Cos(phi) + tangent2 * (Float)Math.Sin(phi)) * sinTheta;
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u,
                                                              SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            Float q;
            Vector3f wi = SampleDirection(u, out q);
            Point3f pOutside = ctx.p() + wi * (2 * sceneRadius);
            return new LightLiSample(Radiance(q, wi, lambda), wi, 1 / solidAngle,
                                     new Interaction(pOutside, (MediumInterface)null));
        }

        public override Float PDF_Li(LightSampleContext ctx, Vector3f w, bool allowIncompletePDF = false)
        {
            return HalfSine2(w) < sinHalfAlpha2 ? 1 / solidAngle : 0;
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = 1 / (Pi * pbrt.Sqr(sceneRadius));
            *pdfDir = HalfSine2(-ray.d) < sinHalfAlpha2 ? 1 / solidAngle : 0;
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            return default;
        }

        public override void Preprocess(Bounds3f sceneBounds)
        {
            Point3f c;
            Float r;
            sceneBounds.BoundingSphere(&c, &r);
            sceneCenter = c;
            sceneRadius = r;
        }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            SampledSpectrum phi = Emitted(lambda) * Pi * pbrt.Sqr(sceneRadius);
            if (turbidity > 0)
            {
                // (through the atmosphere, by the centre of the disc)
                double cosZenith = pbrt.Dot(sunDirection, zenith);
                for (int i = 0; i < NSpectrumSamples; ++i)
                {
                    phi[i] *= (Float)AtmosphericTransmittance(lambda[i], cosZenith, turbidity, ozone);
                }
            }
            return phi;
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda,
                                                              Float time)
        {
            Float q;
            Vector3f toSun = SampleDirection(u1, out q);
            Frame wFrame = Frame.FromZ(toSun);
            Point2f cd = pbrt.SampleUniformDiskConcentric(u2);
            Point3f pDisk = sceneCenter + sceneRadius * wFrame.FromLocal(new Vector3f(cd.x, cd.y, 0));
            Ray ray = new Ray(pDisk + sceneRadius * toSun, -toSun, time);
            return new LightLeSample(Radiance(q, toSun, lambda), ray, 1 / (Pi * pbrt.Sqr(sceneRadius)), 1 / solidAngle);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SunLight2 %s scale: %f solid angle: %f solar: %s turbidity: %f limb: %d ]", BaseToString(), scale, solidAngle, solar,
                                     (Float)turbidity, limb);
        }

        public static SunLight2 Create(Transform renderFromLight, ParameterDictionary parameters,
                                       RGBColorSpace colorSpace, FileLoc loc, Allocator alloc)
        {
            string spectrum = parameters.GetOneString("spectrum", "");
            if (spectrum != "" && spectrum != "solar")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: spectrum unknown (solar; or give \"L\").", spectrum));
            }
            bool solar = spectrum == "solar";
            if (solar && parameters.GetSpectrumArray("L", SpectrumType.Illuminant, alloc).Count > 0)
            {
                pbrt.ErrorExit(loc, "\"sun2\": \"string spectrum\" \"solar\" is in place of \"L\": give one of the two.");
            }
            Spectrum L = solar ? null : parameters.GetOneSpectrum("L", colorSpace.illuminant, SpectrumType.Illuminant, alloc);
            Float sc = parameters.GetOneFloat("scale", 1);
            Point3f from = parameters.GetOnePoint3f("from", new Point3f(0, 0, 0));
            Point3f to = parameters.GetOnePoint3f("to", new Point3f(0, 0, 1));
            Float angle = parameters.GetOneFloat("angle", 0.5334f);
            bool darkening = parameters.GetOneBool("limbdarkening", true);
            string limbName = parameters.GetOneString("limb", darkening ? "linear" : "none");
            if (limbName != "none" && limbName != "linear" && limbName != "neckel")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: limb unknown (none, linear, neckel).", limbName));
            }
            int limb = limbName == "none" ? 0 : limbName == "linear" ? 1 : 2;
            double turbidity = parameters.GetOneFloat("turbidity", 0);
            if (turbidity != 0 && !(turbidity >= 1))
            {
                pbrt.ErrorExit(loc, "\"sun2\": \"turbidity\" is from 1 (pure air); absent or 0 for no atmosphere.");
            }
            double ozone = parameters.GetOneFloat("ozone", 300);
            if (ozone < 0)
            {
                pbrt.ErrorExit(loc, "\"sun2\": \"ozone\" (Dobson units) cannot be negative.");
            }
            Vector3f up = parameters.GetOneVector3f("up", new Vector3f(0, 0, 1));
            if (pbrt.LengthSquared(up) == 0)
            {
                pbrt.ErrorExit(loc, "\"sun2\": \"up\" has no length.");
            }
            Vector3f zenith = pbrt.Normalize(renderFromLight.Apply(up));

            Vector3f w = pbrt.Normalize(from - to);
            Vector3f v1, v2;
            pbrt.CoordinateSystem(w, &v1, &v2);
            SquareMatrix4 m = new SquareMatrix4(v1.x, v2.x, w.x, 0, v1.y, v2.y, w.y, 0, v1.z, v2.z, w.z, 0, 0, 0, 0, 1);
            Transform finalRenderFromLight = renderFromLight * new Transform(m);

            Float E_v = parameters.GetOneFloat("illuminance", -1);
            if (solar)
            {
                // (W m^-2 nm^-1 to the units of the sky lights; the illuminance of the table: its Y times 683)
                if (E_v > 0)
                {
                    double y = 0;
                    for (int lambda = 360; lambda <= 830; ++lambda)
                    {
                        y += Spectra.Y().Evaluate(lambda) * SolarIrradiance(lambda);
                    }
                    sc *= (Float)(E_v / (683.002 * y));
                }
                sc *= 683.002f;
            }
            else
            {
                sc /= pbrt.SpectrumToPhotometric(L);
                if (E_v > 0)
                {
                    sc *= E_v;
                }
            }
            return new SunLight2(finalRenderFromLight, L, sc, angle, limb, solar, turbidity, ozone, zenith);
        }

        DenselySampledSpectrum Lemit;
        Float scale;
        int limb;
        bool solar;
        double turbidity, ozone;
        Vector3f zenith;
        Float sinHalfAlpha2, solidAngle;
        Vector3f sunDirection, tangent1, tangent2;
        Point3f sceneCenter;
        Float sceneRadius;
    }
}
