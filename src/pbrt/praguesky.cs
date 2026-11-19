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
// A wavelength has the radiance of the channel of the dataset that it is in (40 nm wide): the model does not interpolate
// between its channels. The channels that the film can find (360 to 830 nm) are the ones used.
//
// "string polarisation" "dataset": the sky dome is polarised as the dataset says, by wavelength: linearly, to a degree
// with a sign, positive across the plane of the sun and the direction and negative in it (the real sky has both, with
// neutral points between). Only a dataset that holds polarisation can give it. The solar disc and the ground are not
// polarised.

using System;

namespace pbrt
{
    public sealed unsafe class PragueSkyLight : SkyDomeLight
    {
        // The lowest sun of the model, radians
        public const double LowestSun = -4.2 * Math.PI / 180;

        // `zenith`, `sunDirection`: render space, unit length; `filename`: the dataset; `visibility`: km; `albedo`: of the
        // ground; `scale`: of the radiance of the model; `sun`: with the solar disc; `ground`: with the ground below the
        // horizon; `polarised`: the sky dome has the polarisation of the dataset
        public PragueSkyLight(Vector3f zenith, Vector3f sunDirection, string filename, double visibility, Spectrum albedo, Float scale, bool sun,
                              bool ground, bool polarised)
            : base(zenith, sunDirection, scale, sun, ground)
        {
            model = new PragueSkyModel(filename, solarElevation, visibility, albedo);
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
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            double visibility = parameters.GetOneFloat("visibility", 59.4f);
            Spectrum albedo = parameters.GetOneSpectrum("albedo", new ConstantSpectrum(0.3f), SpectrumType.Albedo, alloc);
            Float scale = parameters.GetOneFloat("scale", 1.0f);
            bool sun = parameters.GetOneBool("sun", true);
            bool ground = parameters.GetOneBool("ground", false);
            string polarisation = parameters.GetOneString("polarisation", "none");
            if (filename.Length == 0)
            {
                pbrt.ErrorExit(loc, "\"prague\": \"filename\" must name a dataset of the Prague sky model.");
            }
            if (polarisation != "none" && polarisation != "dataset")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: polarisation unknown (none, dataset).", polarisation));
            }
            if (!(visibility > 0))
            {
                pbrt.ErrorExit(loc, "\"prague\": \"visibility\" must be positive (kilometres).");
            }
            Directions(renderFromLight, parameters, loc, "prague", LowestSun, out Vector3f zenith, out Vector3f toSun);
            PragueSkyLight light = null;
            try
            {
                light = new PragueSkyLight(zenith, toSun, filename, visibility, albedo, 683.002f * scale, sun, ground, polarisation == "dataset");
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

        private readonly PragueSkyModel model;
        private readonly bool polarised;
        private readonly int firstChannel, channelCount;
    }
}
