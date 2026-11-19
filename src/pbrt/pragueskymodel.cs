// (not in pbrt-v4) pragueskymodel.cs
//
// The Prague sky model: Wilkie, Vevoda, Bashford-Rogers, Hosek, Iser, Kolarova, Rittig and Krivanek, "A Fitted Radiance
// and Attenuation Model for Realistic Atmospheres" (SIGGRAPH 2021), and Vevoda, Bashford-Rogers, Kolarova and Wilkie, "A
// Wide Spectral Range Sky Radiance Model" (Pacific Graphics 2022). The radiance of the sky, the radiance of the sun
// through the atmosphere, the transmittance of the atmosphere and the polarisation of the sky, fitted to a brute-force
// simulation of the atmosphere and stored as a dataset file (not part of this repository: see docs/sky-lights.md).
//
// This is a port of the authors' reference implementation (PragueSkyModel.cpp of
// https://github.com/PetrVevoda/pragueskymodel, Copyright 2022 Charles University, Apache License, Version 2.0), with one
// difference in how the file is used. The reference keeps the whole dataset in memory and interpolates between the
// visibilities, the albedos, the altitudes and the elevations of the sun at every query. A light has one sun, one
// visibility and one albedo (by wavelength), and its observer is on the ground: so only the sixteen sets of coefficients
// around that configuration are read for each wavelength channel, by seeking in the file, and the interpolation between
// them is the reference's. The results are the same; a 574 MB dataset takes some megabytes of memory.
//
// The dataset. Radiance and polarisation are stored by configuration (visibility, albedo, altitude, solar elevation,
// wavelength channel); a configuration is a sum of products of two piecewise linear functions, of the angle from the sun
// and of the angle from the zenith (a tensor decomposition), for the radiance times a third toward the horizon. The
// transmittance is a second, smaller decomposition. A wavelength belongs to one channel: there is no interpolation between
// the channels.
//
// Trim writes a dataset with a part of the channels of another, for a renderer that needs the visible ones only.

using System;
using System.Collections.Generic;
using System.IO;

namespace pbrt
{
    public sealed class PragueSkyModel
    {
        public const double PlanetRadius = 6378000.0, SafetyAltitude = 50.0, SunRadius = 0.004654793;
        private const double DistanceToEdge = 1571524.413613, AtmosphereWidth = 100000.0;

        // The angles of a viewing direction that the model is asked with (radians): from the zenith, from the sun, from the
        // direction at right angles to the sun in its vertical (for a sun below the horizon), and from the zenith as seen from
        // the ground below the observer
        public struct Angles
        {
            public double theta, gamma, shadow, zero;
        }

        // The piecewise linear functions of a radiance or polarisation configuration
        private sealed class Metadata
        {
            public int rank, sunOffset, sunStride, zenithOffset, zenithStride, emphOffset, totalCoefsSingleConfig;
            public double[] sunBreaks, zenithBreaks, emphBreaks;
        }

        private struct InterpolationParameter
        {
            public double factor;
            public int index;
        }

        // The grids and the sections of a dataset file
        private sealed class Layout
        {
            public double[] visibilities, albedos, altitudes, elevations;
            public int channels;
            public double channelStart, channelWidth;
            public Metadata radiance = new Metadata(), polarisation;
            public long radianceStart, radianceConfigBytes, transmittanceStart, polarisationStart, polarisationConfigBytes;
            public int dDim, aDim, rankTrans;
            public double[] altitudesTrans, visibilitiesTrans;
            public long transmittanceUStart, transmittanceVStart;

            public long Configs()
            {
                return (long)channels * elevations.Length * altitudes.Length * albedos.Length * visibilities.Length;
            }
        }

        private static double[] ReadDoubles(BinaryReader reader, int count)
        {
            double[] values = new double[count];
            for (int i = 0; i < count; ++i)
            {
                values[i] = reader.ReadDouble();
            }
            return values;
        }

        private static double[] ReadGrid(BinaryReader reader, string name, int least)
        {
            int count = reader.ReadInt32();
            if (count < least || count > 1 << 20)
            {
                throw new InvalidDataException("Prague sky dataset: reading failed at " + name);
            }
            return ReadDoubles(reader, count);
        }

        // Reads the grids of a dataset and finds its sections
        private static Layout ReadLayout(BinaryReader reader, long length)
        {
            Layout layout = new Layout();
            layout.visibilities = ReadGrid(reader, "visibilities", 1);
            layout.albedos = ReadGrid(reader, "albedos", 1);
            layout.altitudes = ReadGrid(reader, "altitudes", 1);
            layout.elevations = ReadGrid(reader, "elevations", 1);
            layout.channels = reader.ReadInt32();
            layout.channelStart = reader.ReadDouble();
            layout.channelWidth = reader.ReadDouble();
            if (layout.channels < 1 || layout.channelStart < 0 || !(layout.channelWidth > 0))
            {
                throw new InvalidDataException("Prague sky dataset: reading failed at the channels");
            }
            Metadata radiance = layout.radiance;
            radiance.rank = reader.ReadInt32();
            if (radiance.rank < 1)
            {
                throw new InvalidDataException("Prague sky dataset: reading failed at the rank of the radiance");
            }
            radiance.sunBreaks = ReadGrid(reader, "sun breaks", 2);
            radiance.zenithBreaks = ReadGrid(reader, "zenith breaks", 2);
            radiance.emphBreaks = ReadGrid(reader, "emphasis breaks", 2);
            radiance.sunOffset = 0;
            radiance.sunStride = radiance.sunBreaks.Length + radiance.zenithBreaks.Length;
            radiance.zenithOffset = radiance.sunBreaks.Length;
            radiance.zenithStride = radiance.sunStride;
            radiance.emphOffset = radiance.rank * radiance.sunStride;
            radiance.totalCoefsSingleConfig = radiance.emphOffset + radiance.emphBreaks.Length;
            layout.radianceStart = reader.BaseStream.Position;
            layout.radianceConfigBytes = ((long)(radiance.sunBreaks.Length + radiance.zenithBreaks.Length) * 2 + 8) * radiance.rank +
                                         radiance.emphBreaks.Length * 2;

            layout.transmittanceStart = layout.radianceStart + layout.radianceConfigBytes * layout.Configs();
            reader.BaseStream.Position = layout.transmittanceStart;
            layout.dDim = reader.ReadInt32();
            layout.aDim = reader.ReadInt32();
            int visibilitiesCount = reader.ReadInt32();
            int altitudesCount = reader.ReadInt32();
            layout.rankTrans = reader.ReadInt32();
            if (layout.dDim < 1 || layout.aDim < 1 || visibilitiesCount < 1 || altitudesCount < 1 || layout.rankTrans < 1)
            {
                throw new InvalidDataException("Prague sky dataset: reading failed at the transmittance");
            }
            layout.altitudesTrans = new double[altitudesCount];
            for (int i = 0; i < altitudesCount; ++i)
            {
                layout.altitudesTrans[i] = reader.ReadSingle();
            }
            layout.visibilitiesTrans = new double[visibilitiesCount];
            for (int i = 0; i < visibilitiesCount; ++i)
            {
                layout.visibilitiesTrans[i] = reader.ReadSingle();
            }
            layout.transmittanceUStart = reader.BaseStream.Position;
            layout.transmittanceVStart = layout.transmittanceUStart + 4L * layout.dDim * layout.aDim * layout.rankTrans * altitudesCount;
            long afterTransmittance = layout.transmittanceVStart + 4L * visibilitiesCount * layout.rankTrans * layout.channels * altitudesCount;

            // (a dataset without polarisation ends here)
            if (afterTransmittance + 4 <= length)
            {
                reader.BaseStream.Position = afterTransmittance;
                Metadata polarisation = new Metadata();
                polarisation.rank = reader.ReadInt32();
                polarisation.sunBreaks = ReadGrid(reader, "sun breaks of the polarisation", 1);
                polarisation.zenithBreaks = ReadGrid(reader, "zenith breaks of the polarisation", 1);
                polarisation.emphBreaks = null;
                polarisation.sunOffset = 0;
                polarisation.sunStride = polarisation.sunBreaks.Length + polarisation.zenithBreaks.Length;
                polarisation.zenithOffset = polarisation.sunBreaks.Length;
                polarisation.zenithStride = polarisation.sunStride;
                polarisation.totalCoefsSingleConfig = polarisation.rank * polarisation.sunStride;
                layout.polarisation = polarisation;
                layout.polarisationStart = reader.BaseStream.Position;
                layout.polarisationConfigBytes = 4L * polarisation.sunStride * polarisation.rank;
            }
            return layout;
        }

        // `solarElevation`: of the sun at the ground, radians; `visibility`: the meteorological range at the ground, km;
        // `albedo`: of the ground, taken at the middle of each wavelength channel. Values beyond the dataset are held at its
        // ends, as the reference does.
        public PragueSkyModel(string filename, double solarElevation, double visibility, Spectrum albedo)
        {
            this.solarElevation = solarElevation;
            using (FileStream stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                layout = ReadLayout(reader, stream.Length);

                InterpolationParameter visibilityParameter = GetInterpolationParameter(visibility, layout.visibilities);
                // (the observer stands on the ground, which the model takes at the safety altitude: ComputeAngles)
                InterpolationParameter altitudeParameter = GetInterpolationParameter(SafetyAltitude, layout.altitudes);
                InterpolationParameter elevationParameter = GetInterpolationParameter(solarElevation * 180 / Math.PI, layout.elevations);
                visibilityFactor = visibilityParameter.factor;
                altitudeFactor = altitudeParameter.factor;
                elevationFactor = elevationParameter.factor;
                albedoFactor = new double[layout.channels];
                radianceCoefs = new float[layout.channels][][];
                polarisationCoefs = layout.polarisation != null ? new float[layout.channels][][] : null;
                Dictionary<long, float[]> radianceRead = new Dictionary<long, float[]>(), polarisationRead = new Dictionary<long, float[]>();
                for (int channel = 0; channel < layout.channels; ++channel)
                {
                    double centre = layout.channelStart + (channel + 0.5) * layout.channelWidth;
                    InterpolationParameter albedoParameter =
                        GetInterpolationParameter(Math.Min(Math.Max((double)albedo.Evaluate((Float)centre), 0.0), 1.0), layout.albedos);
                    albedoFactor[channel] = albedoParameter.factor;
                    radianceCoefs[channel] = new float[16][];
                    if (polarisationCoefs != null)
                    {
                        polarisationCoefs[channel] = new float[16][];
                    }
                    for (int i = 0; i < 16; ++i)
                    {
                        int visibilityIndex = Math.Min(visibilityParameter.index + i / 8, layout.visibilities.Length - 1);
                        int albedoIndex = Math.Min(albedoParameter.index + i % 8 / 4, layout.albedos.Length - 1);
                        int altitudeIndex = Math.Min(altitudeParameter.index + i % 4 / 2, layout.altitudes.Length - 1);
                        int elevationIndex = Math.Min(elevationParameter.index + i % 2, layout.elevations.Length - 1);
                        long config = channel + (long)layout.channels * elevationIndex +
                                      (long)layout.channels * layout.elevations.Length * altitudeIndex +
                                      (long)layout.channels * layout.elevations.Length * layout.altitudes.Length * albedoIndex +
                                      (long)layout.channels * layout.elevations.Length * layout.altitudes.Length * layout.albedos.Length * visibilityIndex;
                        if (!radianceRead.TryGetValue(config, out float[] coefs))
                        {
                            coefs = ReadRadianceConfig(reader, config);
                            radianceRead[config] = coefs;
                        }
                        radianceCoefs[channel][i] = coefs;
                        if (polarisationCoefs != null)
                        {
                            if (!polarisationRead.TryGetValue(config, out float[] polarised))
                            {
                                polarised = ReadPolarisationConfig(reader, config);
                                polarisationRead[config] = polarised;
                            }
                            polarisationCoefs[channel][i] = polarised;
                        }
                    }
                }

                // The transmittance: the base functions of the two altitudes about the observer, and all of the weights
                transVisibility = GetInterpolationParameter(visibility, layout.visibilitiesTrans);
                transAltitude = GetInterpolationParameter(SafetyAltitude, layout.altitudesTrans);
                int baseCount = layout.dDim * layout.aDim * layout.rankTrans;
                transU = new float[2][];
                for (int k = 0; k < 2; ++k)
                {
                    int altitudeIndex = Math.Min(transAltitude.index + k, layout.altitudesTrans.Length - 1);
                    stream.Position = layout.transmittanceUStart + 4L * baseCount * altitudeIndex;
                    transU[k] = new float[baseCount];
                    for (int i = 0; i < baseCount; ++i)
                    {
                        transU[k][i] = reader.ReadSingle();
                    }
                }
                int weightCount = layout.visibilitiesTrans.Length * layout.rankTrans * layout.channels * layout.altitudesTrans.Length;
                stream.Position = layout.transmittanceVStart;
                transV = new float[weightCount];
                for (int i = 0; i < weightCount; ++i)
                {
                    transV[i] = reader.ReadSingle();
                }
            }
        }

        // The coefficients of a radiance configuration: half floats, the zenith ones over a scale that the file gives
        private float[] ReadRadianceConfig(BinaryReader reader, long config)
        {
            Metadata metadata = layout.radiance;
            reader.BaseStream.Position = layout.radianceStart + layout.radianceConfigBytes * config;
            float[] coefs = new float[metadata.totalCoefsSingleConfig];
            int offset = 0;
            for (int r = 0; r < metadata.rank; ++r)
            {
                for (int i = 0; i < metadata.sunBreaks.Length; ++i)
                {
                    coefs[offset++] = (float)(double)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
                }
                double zenithScale = reader.ReadDouble();
                for (int i = 0; i < metadata.zenithBreaks.Length; ++i)
                {
                    coefs[offset++] = (float)((double)BitConverter.UInt16BitsToHalf(reader.ReadUInt16()) / zenithScale);
                }
            }
            for (int i = 0; i < metadata.emphBreaks.Length; ++i)
            {
                coefs[offset++] = (float)(double)BitConverter.UInt16BitsToHalf(reader.ReadUInt16());
            }
            return coefs;
        }

        private float[] ReadPolarisationConfig(BinaryReader reader, long config)
        {
            reader.BaseStream.Position = layout.polarisationStart + layout.polarisationConfigBytes * config;
            float[] coefs = new float[layout.polarisation.totalCoefsSingleConfig];
            for (int i = 0; i < coefs.Length; ++i)
            {
                coefs[i] = reader.ReadSingle();
            }
            return coefs;
        }

        public int Channels
        {
            get { return layout.channels; }
        }

        public double ChannelStart
        {
            get { return layout.channelStart; }
        }

        public double ChannelWidth
        {
            get { return layout.channelWidth; }
        }

        public bool HasPolarisation
        {
            get { return layout.polarisation != null; }
        }

        // The angles of a viewing direction of unit length in the frame of the sky: z to the zenith, the sun in the half
        // plane of x and z with x positive. The observer is on the ground at the origin.
        public Angles ComputeAngles(double x, double y, double z)
        {
            Angles angles;
            double distanceToView = PlanetRadius + SafetyAltitude;
            double sunX = Math.Cos(solarElevation), sunZ = Math.Sin(solarElevation);
            // (the direction as it is seen from the ground below the observer)
            double correction = Math.Sqrt(distanceToView * distanceToView - PlanetRadius * PlanetRadius) / distanceToView;
            double correctX = x, correctY = y, correctZ = z + correction;
            double length = Math.Sqrt(correctX * correctX + correctY * correctY + correctZ * correctZ);
            correctX /= length;
            correctY /= length;
            correctZ /= length;
            angles.gamma = Arccos(x * sunX + z * sunZ);
            double shadowAngle = solarElevation + Math.PI * 0.5;
            angles.shadow = Arccos(correctX * Math.Cos(shadowAngle) + correctZ * Math.Sin(shadowAngle));
            angles.zero = Arccos(correctZ);
            angles.theta = Arccos(z);
            return angles;
        }

        private static double Arccos(double cosine)
        {
            return Math.Acos(Math.Min(Math.Max(cosine, -1.0), 1.0));
        }

        private static InterpolationParameter GetInterpolationParameter(double queryValue, double[] breaks)
        {
            double clamped = Math.Min(Math.Max(queryValue, breaks[0]), breaks[breaks.Length - 1]);
            // (the first break after the first that is above the value, by bisection)
            int low = 1, high = breaks.Length;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (clamped < breaks[middle])
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            int next = low;
            InterpolationParameter parameter;
            parameter.index = next - 1;
            parameter.factor = next == breaks.Length ? 0.0 : (clamped - breaks[next - 1]) / (breaks[next] - breaks[next - 1]);
            return parameter;
        }

        private static double EvalPL(float[] coefs, int offset, double factor)
        {
            return ((double)coefs[offset + 1] - coefs[offset]) * factor + coefs[offset];
        }

        // One configuration at the angles: the sum of the products of the function of the angle from the sun and of that of
        // the angle from the zenith, for the radiance times the function toward the horizon
        private static double Reconstruct(InterpolationParameter gamma, InterpolationParameter alpha, InterpolationParameter zero,
                                          float[] coefs, Metadata metadata)
        {
            double result = 0.0;
            for (int r = 0; r < metadata.rank; ++r)
            {
                double sunParameter = EvalPL(coefs, metadata.sunOffset + r * metadata.sunStride + gamma.index, gamma.factor);
                double zenithParameter = EvalPL(coefs, metadata.zenithOffset + r * metadata.zenithStride + alpha.index, alpha.factor);
                result += sunParameter * zenithParameter;
            }
            if (metadata.emphBreaks != null)
            {
                result *= EvalPL(coefs, metadata.emphOffset + zero.index, zero.factor);
                result = Math.Max(result, 0.0);
            }
            return result;
        }

        // The interpolation of the sixteen configurations of a channel: by visibility, albedo, altitude and elevation, in
        // that order of the levels, and only the lower one of a pair whose factor is next to nothing
        private double Interpolate(int offset, int level, InterpolationParameter gamma, InterpolationParameter alpha,
                                   InterpolationParameter zero, float[][] coefs, Metadata metadata, double albedo)
        {
            if (level == 4)
            {
                return Reconstruct(gamma, alpha, zero, coefs[offset], metadata);
            }
            double factor = level == 0 ? visibilityFactor : (level == 1 ? albedo : (level == 2 ? altitudeFactor : elevationFactor));
            double resultLow = Interpolate(offset, level + 1, gamma, alpha, zero, coefs, metadata, albedo);
            if (factor < 1e-6)
            {
                return resultLow;
            }
            double resultHigh = Interpolate(offset + (1 << (3 - level)), level + 1, gamma, alpha, zero, coefs, metadata, albedo);
            return (1.0 - factor) * resultLow + factor * resultHigh;
        }

        private double EvaluateModel(Angles angles, double wavelength, float[][][] data, Metadata metadata)
        {
            if (wavelength < layout.channelStart || wavelength >= layout.channelStart + layout.channels * layout.channelWidth)
            {
                return 0.0;
            }
            int channel = (int)Math.Floor((wavelength - layout.channelStart) / layout.channelWidth);
            InterpolationParameter gamma = GetInterpolationParameter(angles.gamma, metadata.sunBreaks);
            InterpolationParameter alpha, zero = default;
            if (metadata.emphBreaks != null)
            {
                alpha = GetInterpolationParameter(solarElevation < 0.0 ? angles.shadow : angles.zero, metadata.zenithBreaks);
                zero = GetInterpolationParameter(angles.zero, metadata.emphBreaks);
            }
            else
            {
                alpha = GetInterpolationParameter(angles.zero, metadata.zenithBreaks);
            }
            return Interpolate(0, 0, gamma, alpha, zero, data[channel], metadata, albedoFactor[channel]);
        }

        // The radiance of the sky dome: W m^-2 sr^-1 nm^-1
        public double SkyRadiance(Angles angles, double wavelength)
        {
            return EvaluateModel(angles, wavelength, radianceCoefs, layout.radiance);
        }

        // The radiance of the solar disc through the atmosphere (nothing outside the disc): W m^-2 sr^-1 nm^-1
        public double SunRadiance(Angles angles, double wavelength)
        {
            double[] table = PragueSkyData.sunRadiance;
            double end = PragueSkyData.SunRadianceStart + PragueSkyData.SunRadianceStep * table.Length;
            if (wavelength < PragueSkyData.SunRadianceStart || wavelength >= end || angles.gamma > SunRadius)
            {
                return 0.0;
            }
            double index = (wavelength - PragueSkyData.SunRadianceStart) / PragueSkyData.SunRadianceStep;
            int whole = (int)Math.Floor(index);
            double fraction = index - whole;
            double radiance = table[whole] * (1.0 - fraction) + table[Math.Min(whole + 1, table.Length - 1)] * fraction;
            return radiance * Transmittance(angles, wavelength, double.MaxValue);
        }

        // The degree of linear polarisation of the sky, with a sign: positive across the plane of the sun and the direction,
        // negative in it. Only for a dataset that has it.
        public double Polarisation(Angles angles, double wavelength)
        {
            return -EvaluateModel(angles, wavelength, polarisationCoefs, layout.polarisation);
        }

        private static double IntersectRayWithCircle2D(double rayDirX, double rayDirY, double rayPosY, double circleRadius)
        {
            double qA = rayDirX * rayDirX + rayDirY * rayDirY;
            double qB = 2.0 * rayPosY * rayDirY;
            double qC = rayPosY * rayPosY - circleRadius * circleRadius;
            double discriminant = qB * qB - 4.0 * qA * qC;
            if (discriminant <= 0.0)
            {
                return -1.0;
            }
            discriminant = Math.Sqrt(discriminant);
            double d1 = (-qB + discriminant) / (2.0 * qA);
            double d2 = (-qB - discriminant) / (2.0 * qA);
            return d1 > 0.0 && d2 > 0.0 ? Math.Min(d1, d2) : Math.Max(d1, d2);
        }

        private static InterpolationParameter GetInterpolationParameterTrans(double value, int parameterCount, int power)
        {
            InterpolationParameter parameter;
            parameter.index = Math.Min((int)(value * parameterCount), parameterCount - 1);
            parameter.factor = 0.0;
            if (parameter.index < parameterCount - 1)
            {
                double c1 = Math.Pow((double)parameter.index / parameterCount, power);
                double c2 = Math.Pow((double)(parameter.index + 1) / parameterCount, power);
                parameter.factor = Math.Min(Math.Max((Math.Pow(value, power) - c1) / (c2 - c1), 0.0), 1.0);
            }
            return parameter;
        }

        // `layer`: 0 the altitude of the data below the observer, 1 the one above
        private double ReconstructTrans(int visibilityIndex, int layer, InterpolationParameter altitude, InterpolationParameter distance,
                                        int channel)
        {
            int altitudeIndex = Math.Min(transAltitude.index + layer, layout.altitudesTrans.Length - 1);
            int weights = ((visibilityIndex * layout.altitudesTrans.Length + altitudeIndex) * layout.channels + channel) * layout.rankTrans;
            float[] baseCoefs = transU[layer];
            double t0 = 0, t1 = 0, t2 = 0, t3 = 0;
            int index = 0;
            for (int a = altitude.index; a <= altitude.index + 1; ++a)
            {
                if (a >= layout.aDim)
                {
                    continue;
                }
                for (int d = distance.index; d <= distance.index + 1; ++d)
                {
                    if (d >= layout.dDim)
                    {
                        continue;
                    }
                    int start = (d * layout.aDim + a) * layout.rankTrans;
                    double sum = 0;
                    for (int i = 0; i < layout.rankTrans; ++i)
                    {
                        sum += (double)baseCoefs[start + i] * transV[weights + i];
                    }
                    if (index == 0)
                    {
                        t0 = sum;
                    }
                    else if (index == 1)
                    {
                        t1 = sum;
                    }
                    else if (index == 2)
                    {
                        t2 = sum;
                    }
                    else
                    {
                        t3 = sum;
                    }
                    ++index;
                }
            }
            if (distance.factor > 0.0)
            {
                t0 = (1.0 - distance.factor) * t0 + distance.factor * t1;
                t1 = (1.0 - distance.factor) * t2 + distance.factor * t3;
            }
            t0 = Math.Max(t0, 0.0);
            if (altitude.factor > 0.0)
            {
                t1 = Math.Max(t1, 0.0);
                t0 = (1.0 - altitude.factor) * t0 + altitude.factor * t1;
            }
            return t0;
        }

        private double InterpolateTrans(int visibilityIndex, InterpolationParameter altitude, InterpolationParameter distance, int channel)
        {
            double trans = ReconstructTrans(visibilityIndex, 0, altitude, distance, channel);
            if (transAltitude.factor > 0.0)
            {
                double transHigh = ReconstructTrans(visibilityIndex, 1, altitude, distance, channel);
                trans = (1.0 - transAltitude.factor) * trans + transAltitude.factor * transHigh;
            }
            return trans;
        }

        // The transmittance of the atmosphere along the direction, as far as `distance` (metres) or its end
        public double Transmittance(Angles angles, double wavelength, double distance)
        {
            if (wavelength < layout.channelStart || wavelength >= layout.channelStart + layout.channels * layout.channelWidth)
            {
                return 0.0;
            }
            int channel = (int)Math.Floor((wavelength - layout.channelStart) / layout.channelWidth);

            // Where the ray ends: at the ground, or else at the edge of the atmosphere (the observer is at the safety altitude,
            // which is not what the reference calls a low altitude)
            double rayDirX = Math.Sin(angles.theta), rayDirY = Math.Cos(angles.theta);
            double rayPosY = PlanetRadius + SafetyAltitude;
            double distanceToIntersection = IntersectRayWithCircle2D(rayDirX, rayDirY, rayPosY, PlanetRadius);
            if (distanceToIntersection < 0.0)
            {
                distanceToIntersection = IntersectRayWithCircle2D(rayDirX, rayDirY, rayPosY, PlanetRadius + AtmosphereWidth);
            }
            distanceToIntersection = Math.Min(distanceToIntersection, distance);
            double x = rayDirX * distanceToIntersection, y = rayDirY * distanceToIntersection + rayPosY;
            double fromCentre = Math.Sqrt(x * x + y * y);
            double altitudeValue = Math.Pow(Math.Min(Math.Max(fromCentre - PlanetRadius, 0.0), AtmosphereWidth) / AtmosphereWidth, 1.0 / 3.0);
            double distanceValue = Math.Min(1.0, Math.Sqrt(Math.Sqrt(Arccos(y / fromCentre) * PlanetRadius / DistanceToEdge)));
            InterpolationParameter altitude = GetInterpolationParameterTrans(altitudeValue, layout.aDim, 3);
            InterpolationParameter far = GetInterpolationParameterTrans(distanceValue, layout.dDim, 4);

            double trans = InterpolateTrans(transVisibility.index, altitude, far, channel);
            if (transVisibility.factor > 0.0)
            {
                double transHigh = InterpolateTrans(transVisibility.index + 1, altitude, far, channel);
                trans = (1.0 - transVisibility.factor) * trans + transVisibility.factor * transHigh;
            }
            return trans * trans;
        }

        private static void Copy(Stream input, Stream output, long start, long count)
        {
            byte[] buffer = new byte[1 << 20];
            input.Position = start;
            while (count > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0)
                {
                    throw new EndOfStreamException("Prague sky dataset: the file ends before its data");
                }
                output.Write(buffer, 0, read);
                count -= read;
            }
        }

        // Writes the dataset `inputName` to `outputName` with only the wavelength channels that hold a wavelength between
        // `firstWavelength` and `lastWavelength` (nm). Everything else is as it was: the result is a dataset of the same
        // format, for this reader and for the reference implementation.
        public static void Trim(string inputName, string outputName, double firstWavelength, double lastWavelength)
        {
            using (FileStream input = new FileStream(inputName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(input))
            using (FileStream output = new FileStream(outputName, FileMode.Create, FileAccess.Write))
            using (BinaryWriter writer = new BinaryWriter(output))
            {
                Layout layout = ReadLayout(reader, input.Length);
                int first = Math.Max((int)Math.Floor((firstWavelength - layout.channelStart) / layout.channelWidth), 0);
                int last = Math.Min((int)Math.Floor((lastWavelength - layout.channelStart) / layout.channelWidth), layout.channels - 1);
                if (last < first)
                {
                    throw new ArgumentException("Prague sky dataset: no channel between the two wavelengths");
                }
                int kept = last - first + 1;
                long outerConfigs = layout.Configs() / layout.channels;

                // The grids as they are, but for the channels
                long gridsEnd = 4 + 8L * layout.visibilities.Length + 4 + 8L * layout.albedos.Length + 4 + 8L * layout.altitudes.Length + 4 +
                                8L * layout.elevations.Length;
                Copy(input, output, 0, gridsEnd);
                writer.Write(kept);
                writer.Write(layout.channelStart + first * layout.channelWidth);
                writer.Write(layout.channelWidth);
                writer.Flush();
                Copy(input, output, gridsEnd + 4 + 8 + 8, layout.radianceStart - (gridsEnd + 4 + 8 + 8));

                // The radiance: the channel is the innermost index of a configuration
                for (long outer = 0; outer < outerConfigs; ++outer)
                {
                    Copy(input, output, layout.radianceStart + layout.radianceConfigBytes * (outer * layout.channels + first),
                         layout.radianceConfigBytes * kept);
                }

                // The transmittance: its grids and base functions as they are; of the weights (by visibility, altitude,
                // channel) the channels that are kept
                Copy(input, output, layout.transmittanceStart, layout.transmittanceVStart - layout.transmittanceStart);
                long weightsOuter = (long)layout.visibilitiesTrans.Length * layout.altitudesTrans.Length;
                for (long outer = 0; outer < weightsOuter; ++outer)
                {
                    Copy(input, output, layout.transmittanceVStart + 4L * layout.rankTrans * (outer * layout.channels + first),
                         4L * layout.rankTrans * kept);
                }

                if (layout.polarisation != null)
                {
                    long afterTransmittance = layout.transmittanceVStart + 4L * layout.rankTrans * weightsOuter * layout.channels;
                    Copy(input, output, afterTransmittance, layout.polarisationStart - afterTransmittance);
                    for (long outer = 0; outer < outerConfigs; ++outer)
                    {
                        Copy(input, output, layout.polarisationStart + layout.polarisationConfigBytes * (outer * layout.channels + first),
                             layout.polarisationConfigBytes * kept);
                    }
                }
            }
        }

        private readonly Layout layout;
        private readonly double solarElevation;
        private readonly double visibilityFactor, altitudeFactor, elevationFactor;
        private readonly double[] albedoFactor;
        // (by channel, the sixteen configurations about the one of the light)
        private readonly float[][][] radianceCoefs, polarisationCoefs;
        private readonly InterpolationParameter transVisibility, transAltitude;
        private readonly float[][] transU;
        private readonly float[] transV;
    }
}
