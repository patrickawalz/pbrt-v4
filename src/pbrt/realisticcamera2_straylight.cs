// (not in pbrt-v4) realisticcamera2_straylight.cs
//
// The stray light of RealisticCamera2: the light that gets to the film by another way than the one the lens was designed for.
//
//   Fresnel losses   every interface transmits 1 - R of the light that meets it ("fresnel", implied by the others)
//   Coatings         R of an interface between air and glass is that of a stack of thin films ("coating", "coatings")
//   Ghosts           the light that the interfaces reflect, and the sensor, which reflects as a dielectric whose
//                    reflectance at normal incidence is "sensorreflectance" ("ghosts")
//   Dirt             a thin scattering layer on the front element: a fraction "dirt" of the light that crosses it is
//                    scattered by the Henyey-Greenstein phase function of asymmetry "dirtg" (veiling glare)
//   Barrel           the light that misses the clear aperture of an element meets the mount, the wall of the barrel or the
//                    blades of the stop, which reflect a fraction "barrelalbedo" of it diffusely
//
// A ray of the film is one of four kinds of path, which are disjoint, and GenerateRay() chooses one kind for each sample:
//
//   Main     only refracted, no barrel, not scattered by the dirt: the image of RealisticCamera, times the transmittances
//   Dirt     only refracted, no barrel, scattered by the dirt
//   Ghost    reflected by some interface (or the sensor), no barrel; the dirt scatters it or not
//   Barrel   one or more reflections off the barrel; anything else before and after
//
// A ghost is named by the interfaces that reflect it, counted along the path: the first reflection is at the a-th interface
// that the ray meets, the second at the b-th after that, and so on (an interface that reflects totally is not counted: there
// the ray has no choice). So a ghost is sampled by choosing those counts and then tracing the one path that they allow: the
// weight is the product of the reflectances and transmittances over the probability of the counts. The ghosts of two
// reflections, which carry most of the light, are chosen in proportion to the light that they were measured to carry when the
// camera was made (a part of the choices stays uniform, so that every ghost can be chosen).
//
// The light is polarised by every interface, and the next one treats the two polarisations differently: a path carries
// its Stokes vector (the reflectances and transmittances of the two polarisations, and the retardance between them, in the
// frame of each plane of incidence), and its weight is that of unpolarised light through all of it. The barrel depolarises;
// the dirt keeps the polarisation. For the integrators that carry polarised light, PolarisedSensor() gives the Mueller
// matrix of the path that a ray was made with (the path is traced again for it), with that weight divided out.
//
// The layers of a coating have the dispersion of their material ("MgF2", "Al2O3", "ZrO2", "SiO2" or a glass of the
// catalogue), around the index that the film is given.
//
// A ray of stray light of the film all but never finds a small light, and the flare of a small light is most of what there is
// to see, so the stray light has to be sampled from the lights as well.
//
// "path" and "volpath" leave the emission of the lights out of the rays of stray light and sample it from the lights
// (Camera.HasStrayLight): a point of the front element, a point of a light, and the same three kinds of path traced from the
// scene to the film, where the light is splatted. "straylighttracing" false turns that off (the rays of stray light then find
// the lights by themselves, which is the same image with much more noise).
//
// "bdpt" does the same for the light that a ray of stray light meets first (it has no strategy that connects a light to the
// camera), and so does "mlt" over either of them, where that light sample is one more strategy of the paths of length 0.
// The bidirectional integrators have the rest of the stray light in the importance functions. SampleWi() chooses the kind of path and, for
// a ghost, its reflections (counted from the scene), and traces it to the film: its importance is the weight of that one
// path over the probability of the choice. A path is then one of the main path or a ghost with given reflections, sampled
// either from the film (GenerateRay) or from the scene (SampleWi), and PDF_We() gives the density that makes the weights of
// the two right: with the probabilities Pf and Ps of the choices of the two,
//   pdfDir = (Pf / Ps) pdf_pupil z^2 |n . w| / (A_film cos^4) / pdfPos,
// the density of the ray from the film (the radiance theorem holds for the reflections too) with the choices in it. A ray does
// not tell which path it was, so GenerateRay() and SampleWi() leave their last rays, with their densities and their paths,
// for PDF_We() and PolarisedSensor() to find.
// The paths that the dirt or the barrel scatter have no such density (the same ray comes from a continuum of them): they are
// given a density near zero, which leaves them to SampleWi() wherever a path can be connected to the lens, and to the rays of
// the film where it cannot (a light seen in a mirror or through glass).
//
// Not covered: ghosts of more than MaximumGhostReflections partial reflections.
using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe partial class RealisticCamera2
    {
        // A layer of a coating: its index of refraction at CoatingDesignWavelength, its thickness in nanometres and the
        // Sellmeier coefficients (B1 B2 B3 C1 C2 C3, C in micrometres squared) of its dispersion, null for none. With
        // coefficients the index at a wavelength is eta + n(wavelength) - n(CoatingDesignWavelength), the dispersion of the
        // material around the index of the film (a thin film is less dense than the bulk material); with eta 0 it is that of
        // the material itself.
        public sealed class CoatingLayer
        {
            public Float eta;
            public Float thickness;
            public double[] sellmeier;
        }

        // The wavelength in nanometres that the coatings are made for (the quarter and half waves of the built-in ones)
        public const Float CoatingDesignWavelength = 550;

        // The Sellmeier coefficients of a material of a coating by name (ignoring case, spaces, hyphens and underscores), or of a
        // glass of the catalogue; null for an unknown name. Magnesium fluoride (the ordinary ray) and sapphire (the ordinary
        // ray) are the usual published fits; cubic zirconia is the fit of Wood and Nassau (1982) as remembered, which gives
        // the index 2.158 at the d line that they report.
        public static double[] CoatingMaterial(string name)
        {
            string key = name.ToUpperInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            switch (key)
            {
                case "MGF2":
                {
                    return new double[] { 0.48755108, 0.39875031, 2.3120353, 0.001882178, 0.008951888, 566.13559 };
                }
                case "AL2O3":
                case "SAPPHIRE":
                {
                    return new double[] { 1.4313493, 0.65054713, 5.3414021, 0.0052799261, 0.0142382647, 325.017834 };
                }
                case "ZRO2":
                {
                    return new double[] { 1.347091, 2.117788, 9.452943, 0.062543 * 0.062543, 0.166739 * 0.166739, 24.320570 * 24.320570 };
                }
                case "SIO2":
                {
                    return CatalogueGlass("fused silica");
                }
                default:
                {
                    return CatalogueGlass(name);
                }
            }
        }

        // The index of refraction of a layer at a wavelength in nanometres
        public static Float CoatingLayerEta(CoatingLayer layer, Float wavelength)
        {
            if (layer.sellmeier == null)
            {
                return layer.eta;
            }

            double index = SellmeierIndex(layer.sellmeier, wavelength * 1e-3);
            if (layer.eta == 0)
            {
                return (Float)index;
            }

            return (Float)(layer.eta + index - SellmeierIndex(layer.sellmeier, CoatingDesignWavelength * 1e-3));
        }

        // The parameters of the stray light
        public sealed class StrayLightParameters
        {
            public bool fresnel;
            public bool ghosts;
            public Float sensorReflectance;
            public Float dirt;
            public Float dirtG = 0.9f;
            public Float barrelAlbedo;
            public Float strayProbability = 0.5f;
            public bool lightTracing = true;
            // The layers of the coating of each interface of the lens file, from the air to the glass; null for an interface
            // without a coating, and null for no coating at all
            public List<CoatingLayer[]> coatings;
        }

        // The probabilities of the number of partial reflections of a ghost: 0 (totally reflected only) to 4, and of 5 or more,
        // where each further reflection has the probability GhostContinue
        static readonly Float[] ghostCountProbability = { 0.02f, 0.08f, 0.72f, 0.05f, 0.10f };
        const Float GhostManyProbability = 0.03f;
        const Float GhostContinue = 0.3f;
        public const int MaximumGhostReflections = 16;
        // The part of the ghosts of two reflections that is chosen like the others, and not by the measured light
        const Float GhostGenericFraction = 0.02f;
        // The probability that the count of interfaces up to a reflection is beyond the number of interfaces and the sensor,
        // and the ratio of the probabilities of the successive counts there
        const Float GhostTailProbability = 0.02f;
        const Float GhostTailRatio = 0.5f;
        // The interactions of a path with the interfaces and the barrel before it is given up
        const int MaximumWalkSteps = 4096;
        // The disc of the rear element that the stray paths are sampled over, in rear element radii
        const Float StrayPupilScale = 1.5f;
        // The part of the choices of a ghost of two reflections that is uniform over the pairs of interfaces
        const Float GhostUniformFraction = 0.25f;
        const int GhostPairSamples = 2048;

        // The density that PDF_We() gives a path that the dirt or the barrel scattered (see above)
        const Float ScatteredPathDensity = 1e-10f;

        // What it takes to trace a path of stray light again (for the Mueller matrix of its optics, see PolarisedSensor)
        struct StrayReplay
        {
            public bool valid;
            public Ray start;
            public bool fromScene;
            public fixed int plan[MaximumGhostReflections];
            public int planCount;
            public bool natural;
            public bool allowTotalReflection;
            public RNG rng;
            public Float wavelength;
            public SampledWavelengths lambda;
            // The dirt of the front element scattered the light: its directions before and after, in lens space
            public bool scattered;
            public Vector3f dirtEnter;
            public Vector3f dirtExit;
        }

        // The rays that GenerateRay(), SampleWi() and StrayImportance() made last on this thread: the density of the direction
        // for PDF_We() (0 for the main paths, which have theirs there) and the path for PolarisedSensor()
        struct StrayRecord
        {
            public Point3f o;
            public Vector3f d;
            public Float pdfDir;
            public StrayReplay replay;
        }

        const int StrayRecordCount = 4;
        [ThreadStatic] static StrayRecord[] strayRecords;
        [ThreadStatic] static int strayRecordNext;

        static void RememberStrayRay(Point3f o, Vector3f d, Float pdfDir, StrayReplay replay)
        {
            if (strayRecords == null)
            {
                strayRecords = new StrayRecord[StrayRecordCount];
            }

            strayRecords[strayRecordNext] = new StrayRecord { o = o, d = d, pdfDir = pdfDir, replay = replay };
            strayRecordNext = (strayRecordNext + 1) % StrayRecordCount;
        }

        // The record of a ray: the last one that matches, as the same ray may have been made more than once
        static bool RecallStrayRay(Ray ray, out StrayRecord found)
        {
            found = default;
            if (strayRecords == null)
            {
                return false;
            }

            Vector3f d = pbrt.Normalize(ray.d);
            for (int age = 1; age <= StrayRecordCount; ++age)
            {
                StrayRecord record = strayRecords[(strayRecordNext - age + 2 * StrayRecordCount) % StrayRecordCount];
                if (record.replay.valid && pbrt.DistanceSquared(record.o, ray.o) < 1e-10f * (1 + pbrt.LengthSquared((Vector3f)ray.o)) &&
                    pbrt.Dot(record.d, d) > 1 - 1e-5f)
                {
                    found = record;
                    return true;
                }
            }

            return false;
        }

        static StrayReplay MakeReplay(Ray start, bool fromScene, int* plan, int planCount, bool natural, bool allowTotalReflection,
                                      RNG rng, Float wavelength, SampledWavelengths lambda)
        {
            StrayReplay replay = default;
            replay.valid = true;
            replay.start = start;
            replay.fromScene = fromScene;
            for (int i = 0; i < planCount; ++i)
            {
                replay.plan[i] = plan[i];
            }

            replay.planCount = planCount;
            replay.natural = natural;
            replay.allowTotalReflection = allowTotalReflection;
            replay.rng = rng;
            replay.wavelength = wavelength;
            replay.lambda = lambda;
            return replay;
        }

        // The Mueller matrix of the optics of a recorded path, with its first element (the weight that the path already has)
        // divided out, and the direction of the light at the film in lens space
        bool ReplayOptics(StrayReplay replay, out PolarisedLightSystem.Attenuation optics, out Vector3f lightAtFilm)
        {
            optics = default;
            lightAtFilm = default;
            RNG rng = replay.rng;
            int* plan = stackalloc int[MaximumGhostReflections];
            for (int i = 0; i < replay.planCount; ++i)
            {
                plan[i] = replay.plan[i];
            }

            SampledSpectrum weight = Walk(replay.start, replay.fromScene, plan, replay.planCount, replay.natural,
                                          replay.allowTotalReflection, ref rng, replay.wavelength, replay.lambda, true,
                                          out StrayPath path);
            if (!weight)
            {
                return false;
            }

            optics = path.optics;
            if (replay.scattered)
            {
                // The scattering keeps the polarisation, in the frame of the plane of the two directions; the light meets the
                // dirt before the rest of the lens
                SampledSpectrum one = new SampledSpectrum(1.0f);
                SampledSpectrum zero = new SampledSpectrum(0.0f);
                PolarisedLightSystem.Attenuation dirt = PolarisedLightSystem.Attenuation.Spherical(zero, one, zero);
                dirt = dirt.Scattered(RenderFromLens(replay.dirtExit), RenderFromLens(-replay.dirtEnter), true);
                optics = optics * dirt;
            }

            SampledSpectrum intensity = optics.GetIntensity();
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                if (intensity[i] == 0)
                {
                    intensity[i] = 1;
                }
            }

            optics /= intensity;
            lightAtFilm = replay.fromScene ? path.ray.d : -pbrt.Normalize(replay.start.d);
            return true;
        }

        // PolarisedSensor() with the stray light: the optics of the path that the ray was made with
        bool StrayPolarisedSensor(Ray ray, out PolarisedLightSystem.Attenuation optics, out Vector3f sensorDirection,
                                  out Vector3f sensorUp)
        {
            optics = default;
            sensorDirection = default;
            sensorUp = default;
            if (!RecallStrayRay(ray, out StrayRecord record) || !ReplayOptics(record.replay, out optics, out Vector3f lightAtFilm))
            {
                return false;
            }

            sensorDirection = pbrt.Normalize(RenderFromLens(lightAtFilm));
            sensorUp = RenderFromCamera(new Vector3f(0, -1, 0), ray.time);
            return true;
        }

        StrayLightParameters stray;
        // Whether GenerateRay() traces with Walk(): set when the camera is complete
        bool strayReady;
        Float[] interfaceZ;
        // The thicknesses in nanometres of the layers of the coating of each interface (null for none)
        Float[][] coatingThickness;
        // The index of refraction that gives a dielectric the reflectance of the sensor at normal incidence
        Float sensorEta = 1;
        Float probabilityMain = 1;
        Float probabilityDirt;
        Float probabilityGhost;
        Float probabilityBarrel;
        // The number of interfaces that refract (not the stop) and the sensor: the counts of the ghosts of two reflections
        // that have a measured probability are 1 to ghostCounts
        int ghostCounts;
        // The probability of the ghost of two reflections with the counts (a, b), at (a - 1) * ghostCounts + (b - 1), and the
        // cumulative ones: for the paths from the film (0) and for those from the scene (1)
        Float[][] ghostPairProbability = new Float[2][];
        Float[][] ghostPairCumulative = new Float[2][];

        void InitializeStrayLight(StrayLightParameters parameters)
        {
            stray = parameters;
            int count = (int)elementInterfaces.size();
            interfaceZ = new Float[count];
            Float z = 0;
            for (int i = count - 1; i >= 0; --i)
            {
                z -= elementInterfaces[(ulong)i].thickness;
                interfaceZ[i] = z;
            }

            if (stray.coatings != null)
            {
                coatingThickness = new Float[count][];
                for (int i = 0; i < count; ++i)
                {
                    CoatingLayer[] layers = stray.coatings[i];
                    if (layers != null)
                    {
                        coatingThickness[i] = new Float[layers.Length];
                        for (int layer = 0; layer < layers.Length; ++layer)
                        {
                            coatingThickness[i][layer] = layers[layer].thickness;
                        }
                    }
                }

                stray.fresnel = true;
            }

            if (stray.sensorReflectance > 0)
            {
                Float root = std.sqrt(stray.sensorReflectance);
                sensorEta = (1 + root) / (1 - root);
            }

            ghostCounts = 1;
            for (int i = 0; i < count; ++i)
            {
                if (elementInterfaces[(ulong)i].curvatureRadius != 0)
                {
                    ++ghostCounts;
                }
            }

            if (stray.ghosts || stray.dirt > 0 || stray.barrelAlbedo > 0)
            {
                stray.fresnel = true;
            }

            Float weightDirt = stray.dirt > 0 ? 1 : 0;
            Float weightGhost = stray.ghosts ? 2 : 0;
            Float weightBarrel = stray.barrelAlbedo > 0 ? 1 : 0;
            Float total = weightDirt + weightGhost + weightBarrel;
            if (total > 0)
            {
                probabilityMain = 1 - stray.strayProbability;
                probabilityDirt = stray.strayProbability * weightDirt / total;
                probabilityGhost = stray.strayProbability * weightGhost / total;
                probabilityBarrel = stray.strayProbability * weightBarrel / total;
            }

            if (stray.ghosts)
            {
                BuildGhostPairs(false);
                BuildGhostPairs(true);
            }

            strayReady = stray.fresnel;
        }

        // The index of refraction of the medium on the film side of an interface (-1 is the scene)
        Float MediumEta(int index, Float wavelength)
        {
            if (index < 0 || elementInterfaces[(ulong)index].eta == 0)
            {
                return 1;
            }

            return ElementEta(elementInterfaces[(ulong)index], wavelength);
        }

        // The reflection and the transmission of an interface for light that meets it at the cosine cosI in the medium of
        // index etaI, at each wavelength of the sample: the reflectances of the two polarisations (the transmittances are one
        // minus them: nothing absorbs), and the retardances (the phase of s minus that of p) of the reflection and of the
        // transmission
        struct InterfaceFresnel
        {
            public SampledSpectrum Rs;
            public SampledSpectrum Rp;
            public SampledSpectrum retardanceR;
            public SampledSpectrum retardanceT;
        }

        // The indices of refraction of the layers of the coatings at the wavelengths of the sample that this thread traces:
        // [interface][wavelength][layer]
        [ThreadStatic] static Float[][][] coatingEtaCache;
        [ThreadStatic] static bool[] coatingEtaValid;
        [ThreadStatic] static RealisticCamera2 coatingEtaOwner;
        [ThreadStatic] static SampledWavelengths coatingEtaLambda;

        Float[][] CoatingEta(int index, SampledWavelengths lambda)
        {
            bool same = coatingEtaOwner == this;
            for (int i = 0; i < pbrt.NSpectrumSamples && same; ++i)
            {
                same = coatingEtaLambda[i] == lambda[i];
            }

            if (!same)
            {
                int count = (int)elementInterfaces.size();
                if (coatingEtaOwner != this)
                {
                    coatingEtaCache = new Float[count][][];
                    coatingEtaValid = new bool[count];
                    coatingEtaOwner = this;
                }

                Array.Clear(coatingEtaValid, 0, count);
                coatingEtaLambda = lambda;
            }

            if (!coatingEtaValid[index])
            {
                CoatingLayer[] layers = stray.coatings[index];
                if (coatingEtaCache[index] == null)
                {
                    coatingEtaCache[index] = new Float[pbrt.NSpectrumSamples][];
                    for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                    {
                        coatingEtaCache[index][i] = new Float[layers.Length];
                    }
                }

                for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                {
                    for (int layer = 0; layer < layers.Length; ++layer)
                    {
                        coatingEtaCache[index][i][layer] = CoatingLayerEta(layers[layer], lambda[i]);
                    }
                }

                coatingEtaValid[index] = true;
            }

            return coatingEtaCache[index];
        }

        InterfaceFresnel FresnelOfInterface(int index, Float cosI, Float etaI, Float etaT, SampledWavelengths lambda)
        {
            InterfaceFresnel fresnel = default;
            if (etaI == etaT)
            {
                return fresnel;
            }

            CoatingLayer[] layers = stray.coatings != null ? stray.coatings[index] : null;
            if (layers == null || (etaI != 1 && etaT != 1))
            {
                pbrt.FrDielectricPolarised(cosI, etaT / etaI, out Float Fs, out Float Fp, out Float Ds, out Float Dp);
                fresnel.Rs = new SampledSpectrum(Fs);
                fresnel.Rp = new SampledSpectrum(Fp);
                fresnel.retardanceR = new SampledSpectrum(Ds - Dp);
                return fresnel;
            }

            Float[][] layerEta = CoatingEta(index, lambda);
            // The layers are listed from the air: the light that arrives in the glass meets them the other way round
            bool reversed = etaI != 1;
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                pbrt.FrThinFilmPolarised(cosI, etaI, layerEta[i], coatingThickness[index], reversed, lambda[i], new pstd.complex(etaT),
                                         out Float Fs, out Float Fp, out Float Ds, out Float Dp, out Float DsT, out Float DpT);
                fresnel.Rs[i] = Fs;
                fresnel.Rp[i] = Fp;
                fresnel.retardanceR[i] = Ds - Dp;
                fresnel.retardanceT[i] = DsT - DpT;
            }

            return fresnel;
        }

        // The Stokes vector that a walk carries, at each wavelength of the sample. A walk from the scene carries the light
        // (unpolarised at the start); a walk from the film carries what the film is sensitive to, the first row of the Mueller
        // matrix of the path so far. Either way the first component at the end is the weight of the path for unpolarised light.
        // The x axis of its frame is axis, which is perpendicular to the direction of the light.
        struct StrayStokes
        {
            public SampledSpectrum i;
            public SampledSpectrum q;
            public SampledSpectrum u;
            public SampledSpectrum v;
            public Vector3f axis;
            public bool polarised;

            // The same vector in the frame whose x axis is newAxis, for light that goes along direction
            public void Rotate(Vector3f newAxis, Vector3f direction)
            {
                if (polarised)
                {
                    Float cos = pbrt.Dot(axis, newAxis);
                    Float sin = pbrt.Dot(pbrt.Cross(direction, axis), newAxis);
                    Float cos2 = cos * cos - sin * sin;
                    Float sin2 = 2 * sin * cos;
                    SampledSpectrum q0 = q;
                    SampledSpectrum u0 = u;
                    q = cos2 * q0 + sin2 * u0;
                    u = cos2 * u0 - sin2 * q0;
                }

                axis = newAxis;
            }

            // After an element with the factors Fs and Fp of the two polarisations and the retardance between them, in the
            // frame of its s axis. fromFilm is the walk against the light, which multiplies the row by the matrix.
            public void Apply(SampledSpectrum Fs, SampledSpectrum Fp, SampledSpectrum retardance, bool fromFilm)
            {
                for (int k = 0; k < pbrt.NSpectrumSamples; ++k)
                {
                    Float a = (Fs[k] + Fp[k]) / 2;
                    Float b = (Fs[k] - Fp[k]) / 2;
                    Float i0 = i[k];
                    Float q0 = q[k];
                    i[k] = a * i0 + b * q0;
                    q[k] = b * i0 + a * q0;
                    if (polarised)
                    {
                        Float root = std.sqrt(Fs[k] * Fp[k]);
                        Float c = root * std.cos(retardance[k]);
                        Float s = root * std.sin(retardance[k]);
                        Float u0 = u[k];
                        Float v0 = v[k];
                        if (fromFilm)
                        {
                            u[k] = c * u0 - s * v0;
                            v[k] = s * u0 + c * v0;
                        }
                        else
                        {
                            u[k] = c * u0 + s * v0;
                            v[k] = c * v0 - s * u0;
                        }
                    }
                }

                polarised = true;
            }

            public void Depolarise()
            {
                q = new SampledSpectrum(0.0f);
                u = new SampledSpectrum(0.0f);
                v = new SampledSpectrum(0.0f);
                polarised = false;
            }

            public void Scale(Float factor)
            {
                i *= factor;
                q *= factor;
                u *= factor;
                v *= factor;
            }
        }

        // What a walk gives beside its weight: the ray at its end (in lens space), how often the barrel and the interfaces
        // reflected it, how many interfaces it met (those that reflect totally are not counted) and, when asked for, the Mueller
        // matrix of the path
        public struct StrayPath
        {
            public Ray ray;
            public int barrelEvents;
            public int reflections;
            public int met;
            public PolarisedLightSystem.Attenuation optics;
        }

        // A reflection or a transmission of a walk: the directions of the walk before and after, the normal, the factors of
        // the two polarisations and the retardance
        void ApplyStrayEvent(ref StrayStokes state, ref PolarisedLightSystem.Attenuation chain, bool buildOptics, bool fromScene,
                             Vector3f dIn, Vector3f dOut, Vector3f normal, SampledSpectrum Fs, SampledSpectrum Fp,
                             SampledSpectrum retardance)
        {
            // The light goes the way of a walk from the scene and against a walk from the film
            Vector3f lightEnter = fromScene ? dIn : -dOut;
            Vector3f lightExit = fromScene ? dOut : -dIn;
            // The s axis, perpendicular to the plane of incidence; at normal incidence any axis will do
            Vector3f axis = pbrt.Cross(normal, dIn);
            if (pbrt.LengthSquared(axis) > 0)
            {
                state.Rotate(pbrt.Normalize(axis), fromScene ? dIn : -dIn);
            }

            state.Apply(Fs, Fp, retardance, !fromScene);
            if (buildOptics)
            {
                PolarisedLightSystem.Attenuation element = PolarisedLightSystem.Attenuation.Fresnel(
                    new SampledSpectrum(1.0f), Fs, Fp, retardance, new SampledSpectrum(0.0f));
                element = element.Scattered(RenderFromLens(lightExit), RenderFromLens(-lightEnter), RenderFromLens(normal), true);
                // Later elements act after the earlier ones
                chain = fromScene ? element * chain : chain * element;
            }
        }

        // A ray of lens space that leaves the film, through the lens. The planned walk (natural false) reflects at the
        // interfaces whose counts along the path are in plan (increasing; the sensor is counted, an interface that reflects
        // totally is not), refracts at the others and ends at the barrel. Without allowTotalReflection it also ends at a total
        // reflection. The natural walk reflects or refracts at each interface with the probability of the reflectance, and off
        // the barrel with the probability of its albedo. Returns the weight of the ray that leaves the front element (path.ray,
        // in lens space) for unpolarised light, zero if none does: the polarisation that each interface gives the light is
        // carried to the next. A walk fromScene starts in front of the front element and ends on the sensor instead.
        public SampledSpectrum Walk(Ray rLens, bool fromScene, int* plan, int planCount, bool natural, bool allowTotalReflection,
                                    ref RNG rng, Float wavelength, SampledWavelengths lambda, bool buildOptics, out StrayPath path)
        {
            path = default;
            SampledSpectrum zero = new SampledSpectrum(0.0f);
            SampledSpectrum one = new SampledSpectrum(1.0f);
            StrayStokes state = default;
            state.i = one;
            PolarisedLightSystem.Attenuation chain = PolarisedLightSystem.Attenuation.Identity();
            int count = (int)elementInterfaces.size();
            // The space the ray is in: cell i is between the interfaces i - 1 and i, cell 0 is the scene, the last the camera body
            int cell = fromScene ? 0 : count;
            int planIndex = 0;
            rLens.d = pbrt.Normalize(rLens.d);
            for (int step = 0; step < MaximumWalkSteps; ++step)
            {
                if (rLens.d.z == 0)
                {
                    return zero;
                }

                bool towardsScene = rLens.d.z < 0;
                int k = towardsScene ? cell - 1 : cell;
                if (k < 0)
                {
                    if (fromScene || planIndex != planCount)
                    {
                        return zero;
                    }

                    path.ray = rLens;
                    path.optics = chain;
                    return state.i;
                }

                if (k == count)
                {
                    // The sensor: it reflects as a dielectric whose reflectance at normal incidence is sensorReflectance
                    Point3f pSensor = rLens.Apply(-rLens.o.z / rLens.d.z);
                    if (!pbrt.Inside(new Point2f(pSensor.x, pSensor.y), physicalExtent))
                    {
                        return zero;
                    }

                    Float sensorFs = 0;
                    Float sensorFp = 0;
                    Float sensorRetardance = 0;
                    if (stray.sensorReflectance > 0)
                    {
                        pbrt.FrDielectricPolarised(std.abs(rLens.d.z), sensorEta, out sensorFs, out sensorFp, out Float sensorDs,
                                                   out Float sensorDp);
                        sensorRetardance = sensorDs - sensorDp;
                    }

                    Float sensorAverage = (sensorFs + sensorFp) / 2;
                    bool reflectSensor;
                    if (natural)
                    {
                        reflectSensor = sensorAverage > 0 && rng.Uniform<Float>() < sensorAverage;
                    }
                    else
                    {
                        ++path.met;
                        reflectSensor = sensorAverage > 0 && planIndex < planCount && plan[planIndex] == path.met;
                        if (reflectSensor)
                        {
                            ++planIndex;
                        }
                    }

                    if (!reflectSensor)
                    {
                        // The light that the sensor does not reflect ends here: from the scene that is the light of the image
                        if (!fromScene || planIndex != planCount)
                        {
                            return zero;
                        }

                        if (natural)
                        {
                            // The walk goes on with the probability of the reflectance, and the light is counted at each
                            // arrival: this one stands for all of them
                            state.Scale(1 / (1 - sensorAverage));
                        }

                        path.ray = new Ray(new Point3f(pSensor.x, pSensor.y, 0), rLens.d);
                        path.optics = chain;
                        return state.i;
                    }

                    Vector3f dSensor = new Vector3f(rLens.d.x, rLens.d.y, -rLens.d.z);
                    ApplyStrayEvent(ref state, ref chain, buildOptics, fromScene, rLens.d, dSensor, new Vector3f(0, 0, -1),
                                    new SampledSpectrum(sensorFs), new SampledSpectrum(sensorFp), new SampledSpectrum(sensorRetardance));
                    if (natural)
                    {
                        state.Scale(1 / sensorAverage);
                    }

                    rLens.o = new Point3f(pSensor.x, pSensor.y, 0);
                    rLens.d = dSensor;
                    ++path.reflections;
                    continue;
                }

                LensElementInterface element = elementInterfaces[(ulong)k];
                Float elementZ = interfaceZ[k];
                bool isStop = element.curvatureRadius == 0;
                Float t = 0;
                Normal3f n = default;
                bool hit;
                if (isStop)
                {
                    t = (elementZ - rLens.o.z) / rLens.d.z;
                    hit = t >= 0;
                }
                else
                {
                    hit = IntersectElement(element, elementZ, rLens, &t, &n);
                }

                Point3f pHit = hit ? rLens.Apply(t) : default;
                bool inside = hit && pbrt.Sqr(pHit.x) + pbrt.Sqr(pHit.y) <= pbrt.Sqr(element.apertureRadius);
                if (inside && isStop && element.apertureImage != null)
                {
                    Point2f uv = new Point2f((pHit.x / element.apertureRadius + 1) / 2, (pHit.y / element.apertureRadius + 1) / 2);
                    Float opening = element.apertureImage.BilerpChannel(uv, 0, WrapMode.Black);
                    if (opening == 0)
                    {
                        inside = false;
                    }
                    else
                    {
                        state.Scale(opening);
                    }
                }

                if (!inside)
                {
                    // The mount of the element, the blades of the stop or the wall of the barrel: they reflect diffusely, and
                    // what they reflect is unpolarised
                    if (!natural || stray.barrelAlbedo == 0 || cell == 0 || cell == count ||
                        rng.Uniform<Float>() >= stray.barrelAlbedo)
                    {
                        return zero;
                    }

                    Float wallRadius = std.max(elementInterfaces[(ulong)(cell - 1)].apertureRadius,
                                               elementInterfaces[(ulong)cell].apertureRadius);
                    Float a = pbrt.Sqr(rLens.d.x) + pbrt.Sqr(rLens.d.y);
                    Float b = 2 * (rLens.o.x * rLens.d.x + rLens.o.y * rLens.d.y);
                    Float c = pbrt.Sqr(rLens.o.x) + pbrt.Sqr(rLens.o.y) - pbrt.Sqr(wallRadius);
                    Float tWall = pbrt.Infinity;
                    Float discriminant = b * b - 4 * a * c;
                    if (a > 0 && discriminant >= 0)
                    {
                        tWall = (-b + std.sqrt(discriminant)) / (2 * a);
                    }

                    Point3f pBarrel;
                    Vector3f nBarrel;
                    if (hit && t < tWall)
                    {
                        pBarrel = pHit;
                        nBarrel = new Vector3f(0, 0, towardsScene ? 1 : -1);
                    }
                    else if (tWall > 0 && tWall < pbrt.Infinity)
                    {
                        pBarrel = rLens.Apply(tWall);
                        nBarrel = pbrt.Normalize(new Vector3f(-pBarrel.x, -pBarrel.y, 0));
                    }
                    else
                    {
                        return zero;
                    }

                    Point2f uBarrel = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                    Vector3f local = pbrt.SampleCosineHemisphere(uBarrel);
                    rLens.d = pbrt.Normalize(Frame.FromZ(nBarrel).FromLocal(local));
                    rLens.o = pBarrel + 1e-7f * nBarrel;
                    state.Depolarise();
                    if (buildOptics)
                    {
                        PolarisedLightSystem.Attenuation diffuse = PolarisedLightSystem.Attenuation.Lambert(one);
                        chain = fromScene ? diffuse * chain : chain * diffuse;
                    }

                    ++path.barrelEvents;
                    continue;
                }

                rLens.o = pHit;
                if (isStop)
                {
                    cell = towardsScene ? k : k + 1;
                    continue;
                }

                Float etaI = towardsScene ? MediumEta(k, wavelength) : MediumEta(k - 1, wavelength);
                Float etaT = towardsScene ? MediumEta(k - 1, wavelength) : MediumEta(k, wavelength);
                Vector3f wo = -rLens.d;
                Vector3f normal = new Vector3f(n.x, n.y, n.z);
                Float cosI = pbrt.Dot(wo, normal);
                Vector3f wt;
                bool canRefract = pbrt.Refract(wo, n, etaT / etaI, null, &wt);
                if (canRefract)
                {
                    wt = pbrt.Normalize(wt);
                }

                Vector3f wr = pbrt.Normalize(pbrt.Reflect(wo, normal));
                bool tracked = natural || stray.fresnel;
                InterfaceFresnel fresnel = tracked ? FresnelOfInterface(k, cosI, etaI, etaT, lambda) : default;
                bool reflect;
                if (!canRefract)
                {
                    // Total reflection: all of the light, with a retardance
                    if (!natural && !allowTotalReflection)
                    {
                        return zero;
                    }

                    reflect = true;
                    if (tracked)
                    {
                        ApplyStrayEvent(ref state, ref chain, buildOptics, fromScene, rLens.d, wr, normal, one, one, fresnel.retardanceR);
                    }
                }
                else
                {
                    Float probability = 0;
                    if (natural)
                    {
                        probability = ((fresnel.Rs + fresnel.Rp) * 0.5f).Average();
                        reflect = rng.Uniform<Float>() < probability;
                    }
                    else
                    {
                        ++path.met;
                        reflect = planIndex < planCount && plan[planIndex] == path.met;
                        if (reflect)
                        {
                            ++planIndex;
                        }
                    }

                    if (tracked)
                    {
                        if (reflect)
                        {
                            ApplyStrayEvent(ref state, ref chain, buildOptics, fromScene, rLens.d, wr, normal, fresnel.Rs, fresnel.Rp,
                                            fresnel.retardanceR);
                        }
                        else
                        {
                            ApplyStrayEvent(ref state, ref chain, buildOptics, fromScene, rLens.d, wt, normal, one - fresnel.Rs,
                                            one - fresnel.Rp, fresnel.retardanceT);
                        }
                    }

                    if (natural)
                    {
                        state.Scale(1 / (reflect ? probability : 1 - probability));
                    }
                }

                if (reflect)
                {
                    rLens.d = wr;
                    ++path.reflections;
                }
                else
                {
                    rLens.d = wt;
                    cell = towardsScene ? k : k + 1;
                }
            }

            return zero;
        }

        // The light that the ghosts of two reflections were measured to carry, for each pair of counts
        void BuildGhostPairs(bool fromScene)
        {
            // The directions of the rays from the scene: uniform in a cone somewhat wider than the field of the film
            Float cosCone = std.cos(std.atan(1.5f * film.Diagonal() / (2 * LensRearZ())));
            int pairs = ghostCounts * ghostCounts;
            double[] carried = new double[pairs];
            pbrt.ParallelFor(0, pairs, (int pair) =>
            {
                RNG rng = new RNG((ulong)pair);
                int* plan = stackalloc int[2];
                plan[0] = pair / ghostCounts + 1;
                plan[1] = plan[0] + pair % ghostCounts + 1;
                double sum = 0;
                for (int i = 0; i < GhostPairSamples; ++i)
                {
                    SampledWavelengths lambda = SampledWavelengths.SampleVisible(rng.Uniform<Float>());
                    Ray ray;
                    Float cosine;
                    if (fromScene)
                    {
                        Point3f pFront = SampleFrontElement(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Vector3f d = pbrt.SampleUniformCone(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), cosCone);
                        cosine = pbrt.AbsDot(FrontElementNormal(pFront), new Vector3f(d.x, d.y, -d.z));
                        ray = new Ray(new Point3f(pFront.x, pFront.y, -pFront.z) - StrayStartDistance() * d, d);
                    }
                    else
                    {
                        Point2f pFilm2 = physicalExtent.Lerp(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Point2f pDisc = StrayPupilScale * RearElementRadius() *
                                        pbrt.SampleUniformDiskConcentric(new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                        Point3f pFilm = new Point3f(pFilm2.x, pFilm2.y, 0);
                        Vector3f d = new Point3f(pDisc.x, pDisc.y, -LensRearZ()) - pFilm;
                        cosine = pbrt.Pow4(pbrt.Normalize(d).z);
                        ray = new Ray(pFilm, d);
                    }

                    SampledSpectrum weight = Walk(ray, fromScene, plan, 2, false, true, ref rng, dispersive ? lambda[0] : 0, lambda,
                                                  false, out StrayPath _);
                    sum += weight.Average() * cosine;
                }

                carried[pair] = sum;
            });

            double total = 0;
            for (int pair = 0; pair < pairs; ++pair)
            {
                total += carried[pair];
            }

            Float[] probabilities = new Float[pairs];
            Float[] cumulatives = new Float[pairs];
            double cumulative = 0;
            for (int pair = 0; pair < pairs; ++pair)
            {
                double probability = total > 0
                    ? (1 - GhostUniformFraction) * carried[pair] / total + GhostUniformFraction / pairs
                    : 1.0 / pairs;
                probabilities[pair] = (Float)probability;
                cumulative += probability;
                cumulatives[pair] = (Float)cumulative;
            }

            ghostPairProbability[fromScene ? 1 : 0] = probabilities;
            ghostPairCumulative[fromScene ? 1 : 0] = cumulatives;
        }

        // How far in front of the front element a walk from the scene starts, so that it meets the element
        Float StrayStartDistance()
        {
            return 1e-3f * std.max(1, LensFrontZ());
        }

        // The probability of a count of interfaces from one reflection to the next, for the ghosts that are not chosen by the
        // measured light: uniform up to ghostCounts, geometric beyond
        Float GhostCountProbability(int gap)
        {
            if (gap <= ghostCounts)
            {
                return (1 - GhostTailProbability) / ghostCounts;
            }

            return GhostTailProbability * (1 - GhostTailRatio) * std.pow(GhostTailRatio, (Float)(gap - ghostCounts - 1));
        }

        int SampleGhostCount(ref RNG rng)
        {
            if (rng.Uniform<Float>() >= GhostTailProbability)
            {
                return std.min((int)(rng.Uniform<Float>() * ghostCounts), ghostCounts - 1) + 1;
            }

            int gap = ghostCounts + 1;
            while (rng.Uniform<Float>() < GhostTailRatio)
            {
                ++gap;
            }

            return gap;
        }

        // The reflections of a ghost, as the counts of the interfaces that the ray meets up to each: returns the number of
        // them and the probability of the choice. u chooses among the ghosts of two reflections, and is made uniform again.
        // The probability that SampleGhostPlan() chooses the reflections of plan
        public Float GhostPlanProbability(bool fromScene, int* plan, int reflections)
        {
            Float probability;
            if (reflections < ghostCountProbability.Length)
            {
                probability = ghostCountProbability[reflections];
            }
            else
            {
                probability = GhostManyProbability * std.pow(GhostContinue, (Float)(reflections - ghostCountProbability.Length));
                if (reflections < MaximumGhostReflections)
                {
                    probability *= 1 - GhostContinue;
                }
            }

            Float generic = 1;
            int previous = 0;
            for (int i = 0; i < reflections; ++i)
            {
                generic *= GhostCountProbability(plan[i] - previous);
                previous = plan[i];
            }

            if (reflections != 2)
            {
                return probability * generic;
            }

            Float[] pairProbability = ghostPairProbability[fromScene ? 1 : 0];
            int first = plan[0];
            int second = plan[1] - plan[0];
            Float measured = first <= ghostCounts && second <= ghostCounts ? pairProbability[(first - 1) * ghostCounts + second - 1] : 0;
            return probability * ((1 - GhostGenericFraction) * measured + GhostGenericFraction * generic);
        }

        // The same reflections counted from the other end of a path that met `met` interfaces in all on its way from the
        // film (the walk from the scene also counts its arrival at the sensor, the walk from the film does not count its start)
        public static void ReverseGhostPlan(int* plan, int reflections, int metFromFilm, int* reversed)
        {
            for (int i = 0; i < reflections; ++i)
            {
                reversed[i] = metFromFilm + 1 - plan[reflections - 1 - i];
            }
        }

        public int SampleGhostPlan(bool fromScene, ref Float u, ref RNG rng, int* plan, out Float probability)
        {
            Float[] pairProbability = ghostPairProbability[fromScene ? 1 : 0];
            Float[] pairCumulative = ghostPairCumulative[fromScene ? 1 : 0];
            // The number of reflections
            int reflections = 0;
            Float x = rng.Uniform<Float>();
            bool chosen = false;
            for (int i = 0; i < ghostCountProbability.Length && !chosen; ++i)
            {
                if (x < ghostCountProbability[i])
                {
                    reflections = i;
                    chosen = true;
                }
                else
                {
                    x -= ghostCountProbability[i];
                }
            }

            if (!chosen)
            {
                reflections = ghostCountProbability.Length;
                while (reflections < MaximumGhostReflections && rng.Uniform<Float>() < GhostContinue)
                {
                    ++reflections;
                }
            }

            // The counts
            int* gaps = stackalloc int[MaximumGhostReflections];
            if (reflections == 2 && rng.Uniform<Float>() >= GhostGenericFraction)
            {
                int pair = 0;
                while (pair < pairCumulative.Length - 1 && u >= pairCumulative[pair])
                {
                    ++pair;
                }

                Float below = pair > 0 ? pairCumulative[pair - 1] : 0;
                u = std.min((u - below) / pairProbability[pair], pbrt.OneMinusEpsilon);
                gaps[0] = pair / ghostCounts + 1;
                gaps[1] = pair % ghostCounts + 1;
            }
            else
            {
                for (int i = 0; i < reflections; ++i)
                {
                    gaps[i] = SampleGhostCount(ref rng);
                }
            }

            int met = 0;
            for (int i = 0; i < reflections; ++i)
            {
                met += gaps[i];
                plan[i] = met;
            }

            probability = GhostPlanProbability(fromScene, plan, reflections);
            return reflections;
        }

        // The transmittance of the main path of a ray of the film (camera space), with the dirt. The path is that of the ray of
        // render space renderRay, and is left for PolarisedSensor() if `remember`.
        public SampledSpectrum MainTransmittance(Ray rFilm, Float wavelength, SampledWavelengths lambda, Ray renderRay, bool remember)
        {
            RNG rng = new RNG();
            Ray rLens = new Ray(new Point3f(rFilm.o.x, rFilm.o.y, -rFilm.o.z), new Vector3f(rFilm.d.x, rFilm.d.y, -rFilm.d.z));
            if (remember)
            {
                RememberStrayRay(renderRay.o, pbrt.Normalize(renderRay.d), 0,
                                 MakeReplay(rLens, false, null, 0, false, false, rng, wavelength, lambda));
            }

            return (1 - stray.dirt) * Walk(rLens, false, null, 0, false, false, ref rng, wavelength, lambda, false, out StrayPath _);
        }

        // GenerateRay() with the stray light
        pstd.optional<CameraRay> GenerateStrayRay(CameraSample sample, ref SampledWavelengths lambda)
        {
            Float wavelength = 0;
            if (dispersive)
            {
                lambda.TerminateSecondary();
                wavelength = lambda[0];
            }

            Point2f s = new Point2f(sample.pFilm.x / film.FullResolution().x, sample.pFilm.y / film.FullResolution().y);
            Point2f pFilm2 = physicalExtent.Lerp(s);
            Point3f pFilm = new Point3f(-pFilm2.x, pFilm2.y, 0);

            // The kind of path, by the first lens sample, which is made uniform again
            Float u = sample.pLens.x;
            int kind;
            Float probability;
            if (u < probabilityMain)
            {
                kind = 0;
                probability = probabilityMain;
            }
            else if (u < probabilityMain + probabilityDirt)
            {
                kind = 1;
                u -= probabilityMain;
                probability = probabilityDirt;
            }
            else if (u < probabilityMain + probabilityDirt + probabilityGhost)
            {
                kind = 2;
                u -= probabilityMain + probabilityDirt;
                probability = probabilityGhost;
            }
            else
            {
                kind = 3;
                u -= probabilityMain + probabilityDirt + probabilityGhost;
                probability = probabilityBarrel;
            }

            if (probability <= 0)
            {
                return default;
            }

            u = std.min(u / probability, pbrt.OneMinusEpsilon);
            // The other choices of the path: the same for the rays of the neighbouring film points of a ray differential
            RNG rng = new RNG(pbrt.Hash(sample.pLens.x, sample.pLens.y, sample.time));
            int* plan = stackalloc int[MaximumGhostReflections];
            int planCount = 0;
            if (kind == 2)
            {
                planCount = SampleGhostPlan(false, ref u, ref rng, plan, out Float planProbability);
                probability *= planProbability;
            }

            // The point of the pupil: within the bounds of the exit pupil for the paths that only refract, the disc of the
            // rear element for the others
            Point3f pPupil;
            Float pdf;
            if (kind <= 1)
            {
                pstd.optional<ExitPupilSample> eps = SampleExitPupil(new Point2f(pFilm.x, pFilm.y), new Point2f(u, sample.pLens.y));
                if (!eps)
                {
                    return default;
                }

                pPupil = eps.value().pPupil;
                pdf = eps.value().pdf;
            }
            else
            {
                Float radius = StrayPupilScale * RearElementRadius();
                Point2f pDisc = radius * pbrt.SampleUniformDiskConcentric(new Point2f(u, sample.pLens.y));
                pPupil = new Point3f(pDisc.x, pDisc.y, LensRearZ());
                pdf = 1 / (Pi * radius * radius);
            }

            Ray rFilm = new Ray(pFilm, pPupil - pFilm);
            Ray rLens = new Ray(new Point3f(rFilm.o.x, rFilm.o.y, -rFilm.o.z), new Vector3f(rFilm.d.x, rFilm.d.y, -rFilm.d.z));
            StrayReplay replay = MakeReplay(rLens, false, plan, planCount, kind == 3, kind == 2, rng, wavelength, lambda);
            SampledSpectrum weight = Walk(rLens, false, plan, planCount, kind == 3, kind == 2, ref rng, wavelength, lambda, false,
                                          out StrayPath path);
            Ray rOut = path.ray;
            // A ghost is reflected (the path that is only refracted is a main path), a barrel path meets the barrel
            if (!weight || (kind == 2 && path.reflections == 0) || (kind == 3 && path.barrelEvents == 0))
            {
                return default;
            }

            // The dirt of the front element
            bool scattered;
            if (kind == 0)
            {
                scattered = false;
                weight *= 1 - stray.dirt;
            }
            else if (kind == 1)
            {
                scattered = true;
                weight *= stray.dirt;
            }
            else
            {
                scattered = stray.dirt > 0 && rng.Uniform<Float>() < stray.dirt;
            }

            if (scattered)
            {
                Point2f uDirt = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                Vector3f wi = pbrt.SampleHenyeyGreenstein(-rOut.d, stray.dirtG, uDirt);
                if (wi.z >= 0)
                {
                    return default;
                }

                // The layer scatters a fraction of the light that crosses it, whatever the direction: the radiance of the
                // ray is that of the new direction times the ratio of the cosines
                Vector3f nFront = (Vector3f)FrontElementNormal(new Point3f(rOut.o.x, rOut.o.y, -rOut.o.z));
                nFront = new Vector3f(nFront.x, nFront.y, -nFront.z);
                weight *= pbrt.AbsDot(nFront, wi) / pbrt.AbsDot(nFront, rOut.d);
                // The light arrives against the new direction of the ray and leaves the dirt against the old one
                replay.scattered = true;
                replay.dirtEnter = -wi;
                replay.dirtExit = -rOut.d;
                rOut.d = wi;
            }

            Ray ray = new Ray(new Point3f(rOut.o.x, rOut.o.y, -rOut.o.z), new Vector3f(rOut.d.x, rOut.d.y, -rOut.d.z));
            ray.time = SampleTime(sample.time);
            ray = RenderFromCamera(ray);
            ray.d = pbrt.Normalize(ray.d);

            Float cosTheta = pbrt.Normalize(rFilm.d).z;
            weight *= pbrt.Pow4(cosTheta) / (pdf * pbrt.Sqr(LensRearZ()) * probability);

            // The density of the direction for PDF_We() (the main paths have theirs there), and the path for PolarisedSensor()
            Float pdfDir = 0;
            if (kind != 0)
            {
                pdfDir = ScatteredPathDensity;
                if (kind == 2 && !scattered)
                {
                    int* reversed = stackalloc int[MaximumGhostReflections];
                    ReverseGhostPlan(plan, planCount, path.met, reversed);
                    Float fromFilm = GhostPlanProbability(false, plan, planCount);
                    Float fromScene = GhostPlanProbability(true, reversed, planCount);
                    Vector3f nLens = (Vector3f)FrontElementNormal(new Point3f(rOut.o.x, rOut.o.y, -rOut.o.z));
                    Float cosFront = pbrt.AbsDot(new Vector3f(nLens.x, nLens.y, -nLens.z), rOut.d);
                    pdfDir = fromFilm / fromScene * pdf * pbrt.Sqr(LensRearZ()) * cosFront * FrontElementArea() /
                             (physicalExtent.Area() * pbrt.Pow4(cosTheta));
                }
            }

            RememberStrayRay(ray.o, ray.d, pdfDir, replay);
            return new CameraRay(ray, weight);
        }

        public override bool HasStrayLight()
        {
            return strayReady && stray.lightTracing && probabilityMain < 1;
        }

        public override bool StraySample(CameraSample sample)
        {
            return sample.pLens.x >= probabilityMain;
        }

        public override bool SampleStrayLens(Point2f u, Float time, ref SampledWavelengths lambda, out Interaction lens, out Float pdf)
        {
            if (dispersive)
            {
                lambda.TerminateSecondary();
            }

            Point3f pCamera = SampleFrontElement(u);
            Normal3f n = (Normal3f)pbrt.Normalize(RenderFromCamera((Vector3f)FrontElementNormal(pCamera), time));
            lens = new Interaction(RenderFromCamera(pCamera, time), n, time, medium);
            pdf = 1 / FrontElementArea();
            return true;
        }

        // A path of stray light of the given kind (1 dirt, 2 ghost, 3 barrel) for the light that enters the front element at
        // pCamera along dCamera (camera space): returns its weight (zero if the light does not arrive), the film point, the
        // probability of the reflections that were chosen for a ghost (1 for the others) and the density of the direction that
        // PDF_We() has for the path
        SampledSpectrum StrayFromScene(Point3f pCamera, Vector3f dCamera, int kind, Float uPlan, ref RNG rng, Float wavelength,
                                       SampledWavelengths lambda, out Point2f pRaster, out Float planProbability, out Float pdfDir,
                                       out StrayReplay replay)
        {
            pRaster = default;
            planProbability = 1;
            pdfDir = ScatteredPathDensity;
            replay = default;
            int* plan = stackalloc int[MaximumGhostReflections];
            int planCount = 0;
            if (kind == 2)
            {
                planCount = SampleGhostPlan(true, ref uPlan, ref rng, plan, out planProbability);
            }

            // The dirt of the front element
            SampledSpectrum weight = new SampledSpectrum(1.0f);
            Vector3f dLens = new Vector3f(dCamera.x, dCamera.y, -dCamera.z);
            Vector3f dArrives = dLens;
            bool scattered;
            if (kind == 1)
            {
                scattered = true;
                weight *= stray.dirt;
            }
            else
            {
                scattered = stray.dirt > 0 && rng.Uniform<Float>() < stray.dirt;
            }

            if (scattered)
            {
                Point2f uDirt = new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>());
                dLens = pbrt.SampleHenyeyGreenstein(-dLens, stray.dirtG, uDirt);
                if (dLens.z <= 0)
                {
                    return new SampledSpectrum(0.0f);
                }
            }

            Ray rLens = new Ray(new Point3f(pCamera.x, pCamera.y, -pCamera.z) - StrayStartDistance() * dLens, dLens);
            replay = MakeReplay(rLens, true, plan, planCount, kind == 3, kind == 2, rng, wavelength, lambda);
            replay.scattered = scattered;
            replay.dirtEnter = dArrives;
            replay.dirtExit = dLens;
            weight *= Walk(rLens, true, plan, planCount, kind == 3, kind == 2, ref rng, wavelength, lambda, false, out StrayPath path);
            Ray rFilm = path.ray;
            int met = path.met;
            if (!weight || (kind == 2 && path.reflections == 0) || (kind == 3 && path.barrelEvents == 0))
            {
                return new SampledSpectrum(0.0f);
            }

            Point2f pFilm2 = new Point2f(-rFilm.o.x, rFilm.o.y);
            Vector2f s = physicalExtent.Offset(pFilm2);
            pRaster = new Point2f(s.x * film.FullResolution().x, s.y * film.FullResolution().y);
            if (!pbrt.Inside(pRaster, film.SampleBounds()))
            {
                return new SampledSpectrum(0.0f);
            }

            if (kind == 2 && !scattered)
            {
                // The ray of the film that is this path: it is sampled if it crosses the disc of the rear element
                Float tPupil = LensRearZ() / rFilm.d.z;
                Float radius = StrayPupilScale * RearElementRadius();
                if (pbrt.Sqr(rFilm.o.x - tPupil * rFilm.d.x) + pbrt.Sqr(rFilm.o.y - tPupil * rFilm.d.y) < pbrt.Sqr(radius))
                {
                    int* reversed = stackalloc int[MaximumGhostReflections];
                    ReverseGhostPlan(plan, planCount, met - 1, reversed);
                    Float fromFilm = GhostPlanProbability(false, reversed, planCount);
                    Float cosFront = pbrt.AbsDot(FrontElementNormal(pCamera), dCamera);
                    pdfDir = fromFilm / planProbability * pbrt.Sqr(LensRearZ()) * cosFront * FrontElementArea() /
                             (Pi * radius * radius * physicalExtent.Area() * pbrt.Pow4(rFilm.d.z));
                }
            }

            return weight;
        }

        // SampleWi() with the stray light
        pstd.optional<CameraWiSample> SampleStrayWi(Interaction reference, Point2f u, ref SampledWavelengths lambda)
        {
            Float wavelength = 0;
            if (dispersive)
            {
                lambda.TerminateSecondary();
                wavelength = lambda[0];
            }

            Point3f pCamera = SampleFrontElement(u);
            Point3f pLensRender = RenderFromCamera(pCamera, reference.time);
            Normal3f n = (Normal3f)pbrt.Normalize(RenderFromCamera((Vector3f)FrontElementNormal(pCamera), reference.time));
            Interaction lensIntr = new Interaction(pLensRender, n, reference.time, medium);
            Vector3f wi = lensIntr.p() - reference.p();
            Float dist = pbrt.Length(wi);
            wi /= dist;
            Float pdf = pbrt.Sqr(dist) / (pbrt.AbsDot(lensIntr.n, wi) * FrontElementArea());

            // The kind of path, with the probabilities that GenerateRay() has for them
            RNG rng = new RNG(pbrt.Hash(u.x, u.y, reference.p().x, reference.p().y, reference.p().z));
            Float x = rng.Uniform<Float>();
            int kind;
            Float probability;
            if (x < probabilityMain)
            {
                kind = 0;
                probability = probabilityMain;
            }
            else if (x < probabilityMain + probabilityDirt)
            {
                kind = 1;
                probability = probabilityDirt;
            }
            else if (x < probabilityMain + probabilityDirt + probabilityGhost)
            {
                kind = 2;
                probability = probabilityGhost;
            }
            else
            {
                kind = 3;
                probability = probabilityBarrel;
            }

            Point2f pRaster;
            if (kind == 0)
            {
                SampledSpectrum WiMain = We(new Ray(lensIntr.SpawnRay(-wi)), ref lambda, &pRaster);
                if (!WiMain)
                {
                    return default;
                }

                return new CameraWiSample(WiMain / probability, wi, pdf, pRaster, reference, lensIntr);
            }

            // The light goes along wi, from the front element to the film
            Vector3f dCamera = pbrt.Normalize(CameraFromRender(wi, reference.time));
            if (probability <= 0 || dCamera.z >= 0)
            {
                return default;
            }

            SampledSpectrum weight = StrayFromScene(pCamera, dCamera, kind, rng.Uniform<Float>(), ref rng, wavelength, lambda,
                                                    out pRaster, out Float planProbability, out Float pdfDir, out StrayReplay replay);
            if (!weight)
            {
                return default;
            }

            RememberStrayRay(lensIntr.p(), -wi, pdfDir, replay);
            SampledSpectrum Wi = weight / (physicalExtent.Area() * probability * planProbability);
            return new CameraWiSample(Wi, wi, pdf, pRaster, reference, lensIntr);
        }

        public override SampledSpectrum StrayImportance(Interaction lens, Vector3f wi, Point2f u, ref SampledWavelengths lambda,
                                                        out Point2f pRaster)
        {
            pRaster = default;
            Float wavelength = dispersive ? lambda[0] : 0;
            // The light goes along -wi, from the front element to the film
            Vector3f dCamera = pbrt.Normalize(CameraFromRender(-wi, lens.time));
            Point3f pCamera = CameraFromRender(lens.p(), lens.time);
            if (dCamera.z >= 0)
            {
                return new SampledSpectrum(0.0f);
            }

            Float cosFront = pbrt.AbsDot(FrontElementNormal(pCamera), dCamera);

            // The kind of path: every kind but the main paths
            Float strayTotal = probabilityDirt + probabilityGhost + probabilityBarrel;
            Float x = u.x * strayTotal;
            int kind;
            Float probability;
            if (x < probabilityDirt)
            {
                kind = 1;
                probability = probabilityDirt;
            }
            else if (x < probabilityDirt + probabilityGhost)
            {
                kind = 2;
                x -= probabilityDirt;
                probability = probabilityGhost;
            }
            else
            {
                kind = 3;
                x -= probabilityDirt + probabilityGhost;
                probability = probabilityBarrel;
            }

            if (probability <= 0)
            {
                return new SampledSpectrum(0.0f);
            }

            x = std.min(x / probability, pbrt.OneMinusEpsilon);
            probability /= strayTotal;
            RNG rng = new RNG(pbrt.Hash(u.x, u.y, pCamera.x, pCamera.y));
            SampledSpectrum weight = StrayFromScene(pCamera, dCamera, kind, x, ref rng, wavelength, lambda, out pRaster,
                                                    out Float planProbability, out Float _, out StrayReplay replay);
            if (!weight)
            {
                return weight;
            }

            // The ray that the light arrives along, for PolarisedSensor()
            RememberStrayRay(lens.p(), pbrt.Normalize(wi), 0, replay);
            return weight * (cosFront / (physicalExtent.Area() * probability * planProbability));
        }
    }
}
