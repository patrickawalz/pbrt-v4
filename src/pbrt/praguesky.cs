// (not in pbrt-v4) praguesky.cs
//
// The Prague sky model (pragueskymodel.cs) as a light.
//
//   LightSource "prague" "string filename" "PragueSkyModelDatasetSWIR.dat" "vector3 sundir" [ 0.3 0.2 0.5 ]
//       "float visibility" 60 "spectrum albedo" [ 300 0.3 900 0.3 ] "string polarisation" "dataset"
//
// An infinite light of the radiance of the model above the horizon (what it shares with the other sky models is in
// skydomelight.cs): "up" is the zenith and "sundir" points to the sun, which may be as far as 4.2 degrees below the
// horizon (twilight). "filename" is a dataset of the model for an observer on the ground; "visibility" is the
// meteorological range at the ground in kilometres (the datasets go from 20, hazy, to 131.8, very clear) and "albedo"
// that of the ground, both held at the ends of what the dataset has. "bool sun" (true) adds the solar disc: the sun
// above the atmosphere times the transmittance of the model, without darkening toward the limb. "bool ground" puts a
// ground of the albedo below the horizon, lit by the sky and the sun. "scale" multiplies the radiance, which is in the
// units of the thermal emitters (W m^-2 sr^-1 nm^-1 times 683: the Y of the film is cd/m^2).
//
// "float altitude" (0) is the height of the observer above the ground in metres, to 15000, for a dataset that has
// altitudes (the full one). Above the ground the model gives the light of every direction: below the horizon the ground
// and the air before it, as the model has them, and "ground" is not used. "filename" may be a file of several datasets
// (praguedata combine): the one with the most channels of what the film samples is taken among those that have the
// altitude of the observer, so that a file of the full dataset and of a wider ground dataset gives the wider channels to
// an observer on the ground.
//
// A wavelength has the radiance of the channel of the dataset that it is in (40 nm wide): the model does not interpolate
// between its channels. The channels that the film can find (360 to 830 nm) are the ones used.
//
// Where the dataset is looked for: "filename" as it is given (beside the scene file, or a whole path); then a file of
// that name beside the program (the directory of pbrt's own files); then in the directory that the environment variable
// PRAGUE_SKY_DATASETS names. Without "filename", the first of these that is in one of the three places:
// PragueSkyFullWide.dat, PragueSkyVisible.dat, PragueSkyGround14.dat, PragueSkyModelDatasetSWIR.dat,
// PragueSkyModelDatasetFull.dat, PragueSkyModelDatasetGround.dat; with an "altitude" only the two that have altitudes
// (PragueSkyFullWide.dat, PragueSkyModelDatasetFull.dat), and with "polarisation" not the last, which has none. So a
// dataset put beside the program serves every scene. Any dataset of the model is read: the authors' three and what
// praguedata makes of them (docs/sky-lights.md says where they are).
//
// "string polarisation" "dataset": the sky dome is polarised as the dataset says, by wavelength: linearly, to a degree
// with a sign, positive across the plane of the sun and the direction and negative in it (the real sky has both, with
// neutral points between). Only a dataset that holds polarisation can give it. The solar disc and the ground are not
// polarised.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class PragueSkyLight : SkyDomeLight
    {
        // The lowest sun of the model, radians
        public const double LowestSun = -4.2 * Math.PI / 180;

        // `zenith`, `sunDirection`: render space, unit length; `filename`: the dataset; `visibility`: km; `albedo`: of the
        // ground; `scale`: of the radiance of the model; `sun`: with the solar disc; `ground`: with the ground below the
        // horizon; `polarised`: the sky dome has the polarisation of the dataset; `altitude`: of the observer above the
        // ground, metres (above the ground the model gives the light of every direction, and there is no ground of the
        // light's own)
        public PragueSkyLight(Vector3f zenith, Vector3f sunDirection, string filename, double visibility, Spectrum albedo, Float scale, bool sun,
                              bool ground, bool polarised, double altitude = 0)
            : base(zenith, sunDirection, scale, sun, ground && !(altitude > 0))
        {
            aloft = altitude > 0;
            model = new PragueSkyModel(filename, solarElevation, visibility, albedo, altitude);
            this.polarised = polarised && model.HasPolarisation;
            // The channels that the film can find
            firstChannel = Math.Max((int)Math.Floor((Lambda_min - model.ChannelStart) / model.ChannelWidth), 0);
            int lastChannel = Math.Min((int)Math.Floor((Lambda_max - model.ChannelStart) / model.ChannelWidth), model.Channels - 1);
            channelCount = Math.Max(lastChannel - firstChannel + 1, 0);
            double[] bandAlbedo = new double[channelCount];
            for (int band = 0; band < channelCount; ++band)
            {
                bandAlbedo[band] = Math.Min(Math.Max((double)albedo.Evaluate((Float)BandWavelength(band)), 0.0), 1.0);
            }
            Initialise(bandAlbedo, true);
        }

        public bool HasChannels
        {
            get { return channelCount > 0; }
        }

        public bool HasPolarisation
        {
            get { return model.HasPolarisation; }
        }

        protected override int BandCount
        {
            get { return channelCount; }
        }

        protected override double BandWavelength(int band)
        {
            return model.ChannelStart + (firstChannel + band + 0.5) * model.ChannelWidth;
        }

        protected override double BandWidth
        {
            get { return model.ChannelWidth; }
        }

        protected override double SolarRadius
        {
            get { return PragueSkyModel.SunRadius; }
        }

        protected override void Evaluate(Vector3f local, double theta, double gamma, double wavelength, out double sky, out double solar)
        {
            PragueSkyModel.Angles angles = model.ComputeAngles(local.x, local.y, local.z);
            // (the angle from the sun as the light has it, which is the more accurate where it is small)
            angles.gamma = gamma;
            sky = model.SkyRadiance(angles, wavelength);
            solar = gamma <= PragueSkyModel.SunRadius ? model.SunRadiance(angles, wavelength) : 0;
        }

        // (a wavelength is in one channel)
        protected override void BandsAt(double wavelength, out int low, out int high, out double weightHigh)
        {
            high = -1;
            weightHigh = 0;
            low = (int)Math.Floor((wavelength - model.ChannelStart) / model.ChannelWidth) - firstChannel;
            if (low < 0 || low >= channelCount)
            {
                low = -1;
            }
        }

        protected override bool WholeSphere
        {
            get { return aloft; }
        }

        protected override bool IsPolarised
        {
            get { return polarised; }
        }

        protected override double DegreeAt(Vector3f local, double theta, double gamma, double wavelength)
        {
            PragueSkyModel.Angles angles = model.ComputeAngles(local.x, local.y, local.z);
            angles.gamma = gamma;
            return model.Polarisation(angles, wavelength);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ PragueSkyLight %s sun: %s scale: %f ]", BaseToString(), sunDirection, scale);
        }

        public static PragueSkyLight Create(Transform renderFromLight, ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            string given = parameters.GetOneString("filename", "");
            double visibility = parameters.GetOneFloat("visibility", 59.4f);
            Spectrum albedo = parameters.GetOneSpectrum("albedo", new ConstantSpectrum(0.3f), SpectrumType.Albedo, alloc);
            Float scale = parameters.GetOneFloat("scale", 1.0f);
            bool sun = parameters.GetOneBool("sun", true);
            bool ground = parameters.GetOneBool("ground", false);
            string polarisation = parameters.GetOneString("polarisation", "none");
            double altitude = parameters.GetOneFloat("altitude", 0.0f);
            if (altitude < 0 || altitude > 15000)
            {
                pbrt.ErrorExit(loc, "\"prague\": \"altitude\" must be between 0 and 15000 (metres).");
            }
            if (altitude > 0 && ground)
            {
                pbrt.ErrorExit(loc, "\"prague\": above the ground (\"altitude\") the model has the ground in it: \"ground\" is for an observer on the ground.");
            }
            if (polarisation != "none" && polarisation != "dataset")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: polarisation unknown (none, dataset).", polarisation));
            }
            string filename = FindDataset(given, altitude > 0, polarisation == "dataset", out string searched);
            if (filename == null)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("\"prague\": no dataset of the Prague sky model was found (%s). Looked in: %s. The datasets are at " +
                                                      "https://github.com/patrickawalz/pbrt-v4/releases/tag/prague-sky-datasets-v1; one beside the program " +
                                                      "(PragueSkyVisible.dat for an observer on the ground) serves every scene.",
                                                      given.Length > 0 ? given : "no \"filename\"", searched));
            }
            if (!(visibility > 0))
            {
                pbrt.ErrorExit(loc, "\"prague\": \"visibility\" must be positive (kilometres).");
            }
            Directions(renderFromLight, parameters, loc, "prague", LowestSun, out Vector3f zenith, out Vector3f toSun);
            PragueSkyLight light = null;
            try
            {
                light = new PragueSkyLight(zenith, toSun, filename, visibility, albedo, 683.002f * scale, sun, ground, polarisation == "dataset", altitude);
            }
            catch (System.IO.InvalidDataException e)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: %s.", filename, e.Message));
            }
            catch (System.IO.IOException e)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: the dataset could not be read (%s).", filename, e.Message));
            }
            if (!light.HasChannels)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: the dataset has no wavelength that the film samples.", filename));
            }
            if (polarisation == "dataset" && !light.HasPolarisation)
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: the dataset holds no polarisation.", filename));
            }
            return light;
        }

        // The dataset to read (null: none found; `searched` then says where it was looked for): the name given, beside the
        // scene, beside the program or in the directory of PRAGUE_SKY_DATASETS; without a name, the first of the known
        // datasets that is in one of them and has what is asked (altitudes, polarisation)
        public static string FindDataset(string given, bool altitudes, bool polarisation, out string searched)
        {
            List<string> directories = new List<string>();
            directories.Add(System.AppContext.BaseDirectory);
            string environment = System.Environment.GetEnvironmentVariable("PRAGUE_SKY_DATASETS");
            if (!string.IsNullOrEmpty(environment))
            {
                directories.Add(environment);
            }
            searched = "the directory of the scene, " + string.Join(", ", directories);
            if (given.Length > 0)
            {
                string beside = pbrt.ResolveFilename(given);
                if (System.IO.File.Exists(beside))
                {
                    return beside;
                }
                foreach (string directory in directories)
                {
                    string candidate = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(given));
                    if (System.IO.File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                return null;
            }
            List<string> names = new List<string>();
            names.Add("PragueSkyFullWide.dat");
            if (!altitudes)
            {
                names.Add("PragueSkyVisible.dat");
                names.Add("PragueSkyGround14.dat");
                names.Add("PragueSkyModelDatasetSWIR.dat");
            }
            names.Add("PragueSkyModelDatasetFull.dat");
            if (!altitudes && !polarisation)
            {
                names.Add("PragueSkyModelDatasetGround.dat");
            }
            foreach (string name in names)
            {
                string beside = pbrt.ResolveFilename(name);
                if (System.IO.File.Exists(beside))
                {
                    return beside;
                }
                foreach (string directory in directories)
                {
                    string candidate = System.IO.Path.Combine(directory, name);
                    if (System.IO.File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        private readonly PragueSkyModel model;
        private readonly bool polarised, aloft;
        private readonly int firstChannel, channelCount;
    }
}
