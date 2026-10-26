// // Derived from FLIP reference implementation: https://research.nvidia.com/sites/default/files/pubs/2020-07_FLIP/flip-cpp.zip
// 
// /***************************************************************************
// # Copyright (c) 2020, NVIDIA CORPORATION. All rights reserved.
// #
// # Redistribution and use in source and binary forms, with or without
// # modification, are permitted provided that the following conditions
// # are met:
// #  * Redistributions of source code must retain the above copyright
// #    notice, this list of conditions and the following disclaimer.
// #  * Redistributions in binary form must reproduce the above copyright
// #    notice, this list of conditions and the following disclaimer in the
// #    documentation and/or other materials provided with the distribution.
// #  * Neither the name of NVIDIA CORPORATION nor the names of its
// #    contributors may be used to endorse or promote products derived
// #    from this software without specific prior written permission.
// #
// # THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS ``AS IS'' AND ANY
// # EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// # IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR
// # PURPOSE ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR
// # CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL,
// # EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
// # PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR
// # PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY
// # OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// # (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// # OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
// ***************************************************************************/
// 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace pbrt
{
    // flip.h
    public struct FLIPOptions
    {
        public float ppd;
        public float monitorDistance;
        public float monitorWidth;
        public float monitorResolutionX;

        public static FLIPOptions Default()
        {
            FLIPOptions o = new FLIPOptions();
            o.ppd = 0;
            o.monitorDistance = 0.7f;
            o.monitorWidth = 0.7f;
            o.monitorResolutionX = 3840.0f;
            return o;
        }
    }
}

namespace flip_detail
{
    // C++ iostream default formatting of a floating-point value (%g, precision 6).
    internal static class Cpp
    {
        public static string G(double v, int prec = 6)
        {
            if (double.IsNaN(v)) return "nan";
            if (double.IsInfinity(v)) return v > 0 ? "inf" : "-inf";
            if (v == 0) return (1 / v < 0) ? "-0" : "0";
            string e = v.ToString("E" + (prec - 1), CultureInfo.InvariantCulture);
            int ePos = e.IndexOf('E');
            int exp = int.Parse(e.Substring(ePos + 1), CultureInfo.InvariantCulture);
            string r;
            if (exp < -4 || exp >= prec)
            {
                string mant = e.Substring(0, ePos);
                if (mant.Contains('.')) mant = mant.TrimEnd('0').TrimEnd('.');
                r = mant + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
            }
            else
            {
                r = v.ToString("F" + Math.Max(0, prec - 1 - exp), CultureInfo.InvariantCulture);
                if (r.Contains('.')) r = r.TrimEnd('0').TrimEnd('.');
            }
            return r;
        }

        public static string Fixed5(double v)
        {
            return v.ToString("F5", CultureInfo.InvariantCulture);
        }
    }

    // pooling.h

    public class histogram
    {
        private double mMinValue, mMaxValue;
        private ulong mValueCount, mErrorValueCount;
        private double mBucketSize;
        private ulong[] mvBuckets = new ulong[0];
        private ulong[] mBucketIdRange = new ulong[2];
        private ulong mBucketIdMax;

        public const ulong npos = ulong.MaxValue;  // std::string::npos

        public histogram(ulong buckets, double minValue = 0.0, double maxValue = 1.0)
        {
            this.init(buckets, minValue, maxValue);
        }

        public void init(ulong buckets, double minValue, double maxValue, ulong value = 0)
        {
            this.mMinValue = minValue;
            this.mMaxValue = maxValue;
            this.mValueCount = 0;
            this.mErrorValueCount = 0;
            this.mBucketIdRange[0] = npos;
            this.mBucketIdRange[1] = 0;
            this.resize(buckets);
            this.mvBuckets = new ulong[buckets];
            for (ulong i = 0; i < buckets; ++i) this.mvBuckets[i] = value;
        }

        public double getBucketSize() { return mBucketSize; }
        public ulong getBucketIdMin() { return mBucketIdRange[0]; }
        public ulong getBucketIdMax() { return mBucketIdRange[1]; }
        public ulong getBucketValue(ulong bucketId) { return mvBuckets[bucketId]; }
        public ulong size() { return (ulong)mvBuckets.Length; }
        public double getMinValue() { return this.mMinValue; }
        public double getMaxValue() { return this.mMaxValue; }
        public double getBucketStep() { return (this.mMaxValue - this.mMinValue) / this.mvBuckets.Length; }

        public void clear(ulong value = 0)
        {
            // mvBuckets.resize(mvBuckets.size(), value): the size is unchanged, so existing
            // values are kept.
        }

        public void resize(ulong buckets)
        {
            this.mBucketSize = (this.mMaxValue - this.mMinValue) / buckets;
            Array.Resize(ref this.mvBuckets, (int)buckets);
        }

        public ulong valueBucketId(double value)
        {
            if (value < this.mMinValue || value > this.mMaxValue)
                return npos;

            ulong bucketId = (ulong)(value / this.mBucketSize);

            if (bucketId == (ulong)this.mvBuckets.Length)
            {
                bucketId--;
            }
            return bucketId;
        }

        public void inc(double value, ulong amount = 1)
        {
            ulong bucketId = valueBucketId(value);
            if (bucketId != npos)
            {
                this.mvBuckets[bucketId] += amount;
                this.mValueCount += amount;
                this.mBucketIdRange[0] = Math.Min(this.mBucketIdRange[0], bucketId);
                this.mBucketIdRange[1] = Math.Max(this.mBucketIdRange[1], bucketId);
            }
            else
            {
                mErrorValueCount += amount;
            }
        }

        public string toPython(ulong numPixels, float ppd, double meanValue, double stddevValue, double maxValue, double minValue, double weightedMedian, double firstWeightedQuartile, double thirdWeightedQuartile, bool optionLog)
        {
            StringBuilder ss = new StringBuilder();

            double bucketStep = getBucketStep();

            //  imports
            ss.Append("import matplotlib.pyplot as plt\n");
            ss.Append("import sys\n");
            ss.Append("import numpy as np\n");
            ss.Append("from matplotlib.ticker import (MultipleLocator)\n\n");

            ss.Append("dimensions = (25, 15)  #  centimeters\n\n");

            ss.Append("lineColor = 'blue'\n");
            ss.Append("fillColor = 'lightblue'\n");
            ss.Append("meanLineColor = 'gray'\n");
            ss.Append("stddevLineColor = 'orange'\n");
            ss.Append("weightedMedianLineColor = 'red'\n");
            ss.Append("quartileLineColor = 'purple'\n");
            ss.Append("cogLineColor = 'green'\n");
            ss.Append("fontSize = 14\n");
            ss.Append("numPixels = " + numPixels + "\n\n");

            ss.Append("ppd = " + Cpp.G((double)ppd) + "\n");
            ss.Append("meanValue = " + Cpp.G(meanValue) + "\n");
            ss.Append("stddevValue = " + Cpp.G(stddevValue) + "\n");
            ss.Append("maxValue = " + Cpp.G(maxValue) + "\n");
            ss.Append("minValue = " + Cpp.G(minValue) + "\n\n");
            ss.Append("weightedMedianValue = " + Cpp.G(weightedMedian) + "\n\n");
            ss.Append("firstWeightedQuartileValue = " + Cpp.G(firstWeightedQuartile) + "\n\n");
            ss.Append("thirdWeightedQuartileValue = " + Cpp.G(thirdWeightedQuartile) + "\n\n");

            //  X-axis
            ss.Append("dataX = [");
            for (ulong bucketId = 0; bucketId < (ulong)this.mvBuckets.Length; bucketId++)
            {
                ss.Append(bucketId > 0 ? ", " : "");
                ss.Append(Cpp.G(bucketStep * bucketId + 0.5 * bucketStep));
            }
            ss.Append("]\n\n");

            //  FLIP histogram
            ss.Append("dataFLIP = [");
            for (ulong bucketId = 0; bucketId < (ulong)this.mvBuckets.Length; bucketId++)
            {
                ss.Append(bucketId > 0 ? ", " : "");
                ss.Append(this.mvBuckets[bucketId]);
            }
            ss.Append("]\n\n");

            //  Weighted FLIP histogram
            ss.Append("bucketStep = " + Cpp.G(bucketStep) + "\n");
            ss.Append("weightedDataFLIP = np.empty(" + this.mvBuckets.Length + ")\n");
            ss.Append("moments = np.empty(" + this.mvBuckets.Length + ")\n");
            ss.Append("for i in range(" + this.mvBuckets.Length + ") :\n");
            ss.Append("\tweight = (i + 0.5) * bucketStep\n");
            ss.Append("\tweightedDataFLIP[i] = dataFLIP[i] * weight\n");
            ss.Append("\tmoments[i] = dataFLIP[i] * weight * weight\n");
            ss.Append("cog = sum(moments) / sum(weightedDataFLIP)\n");
            ss.Append("weightedDataFLIP /= (numPixels /(1024 * 1024))  # normalized with the number of megapixels in the image\n\n");
            if (optionLog)
            {
                ss.Append("for i in range(" + this.mvBuckets.Length + ") :\n");
                ss.Append("\tif weightedDataFLIP[i] > 0 :\n");
                ss.Append("\t\tweightedDataFLIP[i] = np.log10(weightedDataFLIP[i])  # avoid log of zero\n\n");
            }

            ss.Append("maxY = max(weightedDataFLIP)\n\n");

            ss.Append("sumWeightedDataFLIP = sum(weightedDataFLIP)\n\n");

            ss.Append("font = { 'family' : 'serif', 'style' : 'normal', 'weight' : 'normal', 'size' : fontSize }\n");
            ss.Append("lineHeight = fontSize / (dimensions[1] * 15)\n");
            ss.Append("plt.rc('font', **font)\n");
            ss.Append("fig = plt.figure()\n");
            ss.Append("axes = plt.axes()\n");
            ss.Append("axes.xaxis.set_minor_locator(MultipleLocator(0.1))\n");
            ss.Append("axes.xaxis.set_major_locator(MultipleLocator(0.2))\n\n");

            ss.Append("fig.set_size_inches(dimensions[0] / 2.54, dimensions[1] / 2.54)\n");

            if (optionLog)
                ss.Append("axes.set(title = 'Weighted \\uA7FBLIP Histogram', xlabel = '\\uA7FBLIP error', ylabel = 'log(weighted \\uA7FBLIP sum per megapixel)')\n\n");
            else
                ss.Append("axes.set(title = 'Weighted \\uA7FBLIP Histogram', xlabel = '\\uA7FBLIP error', ylabel = 'Weighted \\uA7FBLIP sum per megapixel')\n\n");

            ss.Append("plt.bar(dataX, weightedDataFLIP, width = " + Cpp.G(bucketStep) + ", color = fillColor, edgeColor = lineColor, lineWidth = 0.3)\n\n");

            ss.Append("plt.text(0.99, 1.0 - 1 * lineHeight, 'PPD: ' + str(f'{ppd:.1f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes, color='black')\n\n");
            ss.Append("plt.text(0.99, 1.0 - 2 * lineHeight, 'Weighted median: ' + str(f'{weightedMedianValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes, color=weightedMedianLineColor)\n\n");
            ss.Append("plt.text(0.99, 1.0 - 3 * lineHeight, 'Mean: ' + str(f'{meanValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes, color=meanLineColor)\n\n");
            ss.Append("plt.text(0.99, 1.0 - 4 * lineHeight, '1st weighted quartile: ' + str(f'{firstWeightedQuartileValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes, color=quartileLineColor)\n\n");
            ss.Append("plt.text(0.99, 1.0 - 5 * lineHeight, '3rd weighted quartile: ' + str(f'{thirdWeightedQuartileValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes, color=quartileLineColor)\n\n");
            ss.Append("plt.text(0.99, 1.0 - 6 * lineHeight, 'Min: ' + str(f'{minValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes)\n");
            ss.Append("plt.text(0.99, 1.0 - 7 * lineHeight, 'Max: ' + str(f'{maxValue:.4f}'), ha = 'right', fontsize = fontSize, transform = axes.transAxes)\n");

            ss.Append("axes.set_xlim(0.0, 1.0)\n");
            ss.Append("axes.set_ylim(0.0, maxY * 1.05)\n");
            ss.Append("axes.axvline(x = meanValue, color = meanLineColor, linewidth = 1.5)\n\n");
            ss.Append("axes.axvline(x = weightedMedianValue, color = weightedMedianLineColor, linewidth = 1.5)\n\n");
            ss.Append("axes.axvline(x = firstWeightedQuartileValue, color = quartileLineColor, linewidth = 1.5)\n\n");
            ss.Append("axes.axvline(x = thirdWeightedQuartileValue, color = quartileLineColor, linewidth = 1.5)\n\n");
            ss.Append("axes.axvline(x = minValue, color='black', linestyle = ':', linewidth = 1.5)\n\n");
            ss.Append("axes.axvline(x = maxValue, color='black', linestyle = ':', linewidth = 1.5)\n\n");

            ss.Append("if len(sys.argv) > 1:\n");
            ss.Append("\tif sys.argv[1] == '-save':\n");
            ss.Append("\t\tplt.savefig(sys.argv[2])\n");
            ss.Append("else:\n");
            ss.Append("\tplt.show()\n");

            ss.Append("\n");

            return ss.ToString();
        }
    }

    public class pooling
    {
        private ulong mValueCount;
        private double mValueSum;
        private double mSquareValueSum;
        private double mMinValue;
        private double mMaxValue;
        private double mVarianceBias = 0.0;
        private uint[] mMinCoord = new uint[2];
        private uint[] mMaxCoord = new uint[2];
        private histogram mHistogram = new histogram(100);

        public pooling() { clear(); }
        public pooling(ulong buckets, double varianceBias = 0.0) { mVarianceBias = varianceBias; mHistogram.resize(buckets); clear(); }

        public histogram getHistogram() { return mHistogram; }
        public double getMinValue() { return mMinValue; }
        public double getMaxValue() { return mMaxValue; }
        public double getMean() { return mValueSum / mValueCount; }
        public double getVariance()
        {
            bool isSampled = false;
            double variance = (mSquareValueSum - ((mValueSum * mValueSum) / mValueCount)) / (mValueCount - (ulong)(isSampled ? 1 : 0));
            variance = Math.Max(0.0, variance);  //  negative not allowed, but can happen due to precision issues
            return variance;
        }

        public double getStdDev() { return Math.Sqrt(this.getVariance()); }

        public double getCenterOfGravity()
        {
            double bucketStep = mHistogram.getBucketStep();
            double sumMoments = 0.0;
            double sumWeightedDataValue = 0.0;
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                double weight = (bucketId + 0.5) * bucketStep;
                double weightedValue = mHistogram.getBucketValue(bucketId) * weight;
                sumWeightedDataValue += weightedValue;
                sumMoments += weightedValue * weight;
            }
            double cog = sumMoments / sumWeightedDataValue;
            return cog;
        }

        public double getWeightedPercentile(double percent)  // percent = 0.5 gives you the weighted median. This function is approximative, since we do not have all the values. We find the bucket where the weighted median is located, and interpolate in there.
        {
            double weight;
            double weightedValue;
            double bucketStep = mHistogram.getBucketStep();
            double sumWeightedDataValue = 0.0;
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                weight = (bucketId + 0.5) * bucketStep;
                weightedValue = mHistogram.getBucketValue(bucketId) * weight;
                sumWeightedDataValue += weightedValue;
            }

            double sum = 0;
            ulong weightedMedianIndex = 0;
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                weight = (bucketId + 0.5) * bucketStep;
                weightedValue = mHistogram.getBucketValue(bucketId) * weight;
                weightedMedianIndex = bucketId;
                if (sum + weightedValue > percent * sumWeightedDataValue)
                    break;
                sum += weightedValue;
            }

            weight = (weightedMedianIndex + 0.5) * bucketStep;
            weightedValue = mHistogram.getBucketValue(weightedMedianIndex) * weight;
            double discrepancy = percent * sumWeightedDataValue - sum;
            double linearWeight = discrepancy / weightedValue; // in [0,1]
            double percentile = (weightedMedianIndex + linearWeight) * bucketStep;
            return percentile;
        }

        public double getPercentile(double percent, ulong numCounts)  // percent = 0.5 gives you the median. This function is approximative, since we do not have all the values. We find the bucket where the median is located and returns its weight.
        {
            ulong sum = 0;
            ulong medianIndex = 0;
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                ulong count = mHistogram.getBucketValue(bucketId);
                medianIndex = bucketId;
                if (sum + count > (ulong)(percent * numCounts))
                    break;
                sum += count;
            }
            double bucketStep = mHistogram.getBucketStep();
            double percentile = (medianIndex + 0.5) * bucketStep;
            return percentile;
        }

        public void clear()
        {
            mValueCount = 0;
            mValueSum = 0.0;
            mSquareValueSum = 0.0;
            mMinValue = double.MaxValue;
            mMaxValue = 2.2250738585072014e-308;  // std::numeric_limits<double>::min()
            mHistogram.clear();
        }

        public void update(uint xcoord, uint ycoord, double value)
        {
            mValueCount++;
            mValueSum += value;
            mSquareValueSum += (value * value);
            mHistogram.inc(value);

            if (value < mMinValue)
            {
                mMinValue = value;
                mMinCoord[0] = xcoord;
                mMinCoord[1] = ycoord;
            }

            if (value > mMaxValue)
            {
                mMaxValue = value;
                mMaxCoord[0] = xcoord;
                mMaxCoord[1] = ycoord;
            }
        }

        private static string F5(double v) { return Cpp.Fixed5(v); }

        public string toString(bool verbose = false)
        {
            StringBuilder s = new StringBuilder();
            if (verbose)
            {
                s.Append("Mean = " + F5(this.getMean()) + ", Weighted median = " + F5(this.getWeightedPercentile(0.5)) + ", 1st weighted quartile = " + F5(this.getWeightedPercentile(0.25)) + ", 3rd weighted quartile = " + F5(this.getWeightedPercentile(0.75)) + ", Min value = " + F5(mMinValue) + " @ (" + mMinCoord[0] + "," + mMinCoord[1] + "), Max value =  " + F5(mMaxValue) + " @ (" + mMaxCoord[0] + ", " + mMaxCoord[1] + ")\n");
            }
            else
            {
                s.Append("Mean ; Weighted median ; 1st weighted quartile; 3rd weighted quartile ; Min value ; MinPosX; MinPosY; Max value; MaxPosX; MaxPosY\n");
                s.Append(F5(this.getMean()) + ";" + F5(this.getWeightedPercentile(0.5)) + ";" + F5(this.getWeightedPercentile(0.25)) + ";" + F5(this.getWeightedPercentile(0.75)) + ";" + F5(mMinValue) + ";" + mMinCoord[0] + ";" + mMinCoord[1] + ";" + F5(mMaxValue) + ";" + mMaxCoord[0] + ";" + mMaxCoord[1] + "\n");
            }
            return s.ToString();
        }

        public void save(string fileName, float ppd, int imgWidth, int imgHeight, bool verbose, bool optionLog, string referenceFileName, string testFileName)
        {
            StringBuilder file = new StringBuilder();
            if (verbose)
            {
                Console.Out.Write("Writing metric histogram to file <" + fileName + ".csv> and <" + fileName + ".py> with image resolution: " + imgWidth + "x" + imgHeight + "\n");
            }

            file.Append("#  FLIP pooling statistics for <" + referenceFileName + "> vs. <" + testFileName + ">\n");

            file.Append(this.toString(false));

            file.Append("#  histogram\n");
            file.Append("Min bucket" + ";" + "Max bucket" + "\n");
            file.Append(mHistogram.getBucketIdMin() + ";" + mHistogram.getBucketIdMax() + "\n");
            file.Append("Bucket no; ");
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                file.Append(bucketId + ";");
            }
            file.Append("\n");

            if (verbose)
            {
                Console.Out.Write(this.toString(true));
                Console.Out.Write("# Histogram bucket size = " + Cpp.G(mHistogram.getBucketSize()) + ", range = [" + mHistogram.getBucketIdMin() + ", " + mHistogram.getBucketIdMax() + "]\n");
                Console.Out.Write("Non-weighted bucket counts:\n");
            }

            ulong area = (ulong)imgWidth * (ulong)imgHeight;

            file.Append("Count; ");
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                ulong histogramValue = mHistogram.getBucketValue(bucketId);
                if (verbose)
                {
                    Console.Out.Write(bucketId + ": " + histogramValue + "\n");
                }
                file.Append(Cpp.G((double)(float)histogramValue) + ";");
            }
            file.Append("\n");

            file.Append("Weight; ");
            double bucketStep = mHistogram.getBucketStep();
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                file.Append(Cpp.G((0.5 + bucketId) * bucketStep) + ";");
            }
            file.Append("\n");

            file.Append("Weighted bucket count per megapixel; ");
            double megaPixelWeight = 1024.0 * 1024.0 / (double)area;
            for (ulong bucketId = 0; bucketId < mHistogram.size(); bucketId++)
            {
                file.Append(Cpp.G(((0.5 + bucketId) * bucketStep * mHistogram.getBucketValue(bucketId) * megaPixelWeight)) + ";");
            }
            file.Append("\n");

            File.WriteAllText(fileName + ".csv", file.ToString(), new UTF8Encoding(false));

            //  python output
            File.WriteAllText(fileName + ".py", mHistogram.toPython(area, ppd, getMean(), getStdDev(), getMaxValue(), getMinValue(), getWeightedPercentile(0.5), getWeightedPercentile(0.25), getWeightedPercentile(0.75), optionLog), new UTF8Encoding(false));
        }
    }

    // flip.cpp

    public struct color3
    {
        public float x, y, z;

        public color3(float _x, float _y, float _z) { x = _x; y = _y; z = _z; }

        public static readonly color3 D65 = new color3(0.950428545377181f, 1.0f, 1.088900370798128f);

        public void abs_() { x = MathF.Abs(x); y = MathF.Abs(y); z = MathF.Abs(z); }
        public void max_(float value) { x = Math.Max(x, value); y = Math.Max(y, value); z = Math.Max(z, value); }
        public void min_(float value) { x = Math.Min(x, value); y = Math.Min(y, value); z = Math.Min(z, value); }
        public void min_(color3 value) { x = Math.Min(x, value.x); y = Math.Min(y, value.y); z = Math.Min(z, value.z); }

        private static float sRGB2Linear(float sRGBColor)
        {
            if (sRGBColor <= 0.04045f)
                return sRGBColor / 12.92f;
            else
                return MathF.Pow((sRGBColor + 0.055f) / 1.055f, 2.4f);
        }

        private static float Linear2sRGB(float linearColor)
        {
            if (linearColor <= 0.0031308f)
                return linearColor * 12.92f;
            else
                return 1.055f * MathF.Pow(linearColor, 1.0f / 2.4f) - 0.055f;
        }

        public void sRGB2LinearRGB_()
        {
            x = sRGB2Linear(x);
            y = sRGB2Linear(y);
            z = sRGB2Linear(z);
            return;
        }

        public void LinearRGB2sRGB_()
        {
            x = Linear2sRGB(x);
            y = Linear2sRGB(y);
            z = Linear2sRGB(z);
            return;
        }

        public void LinearRGB2XYZ_()    // Source: https://www.image-engineering.de/library/technotes/958-how-to-convert-between-srgb-and-ciexyz
        {                                    // Assumes D65 standard illuminant
            float a11 = 10135552.0f / 24577794.0f;
            float a12 = 8788810.0f / 24577794.0f;
            float a13 = 4435075.0f / 24577794.0f;
            float a21 = 2613072.0f / 12288897.0f;
            float a22 = 8788810.0f / 12288897.0f;
            float a23 = 887015.0f / 12288897.0f;
            float a31 = 1425312.0f / 73733382.0f;
            float a32 = 8788810.0f / 73733382.0f;
            float a33 = 70074185.0f / 73733382.0f;
            color3 v = this;
            x = a11 * v.x + a12 * v.y + a13 * v.z;
            y = a21 * v.x + a22 * v.y + a23 * v.z;
            z = a31 * v.x + a32 * v.y + a33 * v.z;
            return;
        }

        public void XYZ2LinearRGB_()    // Return values in linear RGB, assuming D65 standard illuminant
        {
            float a11 = 3.241003232976358f;
            float a12 = -1.537398969488785f;
            float a13 = -0.498615881996363f;
            float a21 = -0.969224252202516f;
            float a22 = 1.875929983695176f;
            float a23 = 0.041554226340085f;
            float a31 = 0.055639419851975f;
            float a32 = -0.204011206123910f;
            float a33 = 1.057148977187533f;
            color3 v = this;
            x = a11 * v.x + a12 * v.y + a13 * v.z;
            y = a21 * v.x + a22 * v.y + a23 * v.z;
            z = a31 * v.x + a32 * v.y + a33 * v.z;
            return;
        }

        public void XYZ2YCxCz_() { XYZ2YCxCz_(D65); }
        public void XYZ2YCxCz_(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 xyz = this;
            xyz.x /= reference_illuminant.x;
            xyz.y /= reference_illuminant.y;
            xyz.z /= reference_illuminant.z;
            float Y = 116.0f * xyz.y - 16.0f;
            float Cx = 500.0f * (xyz.x - xyz.y);
            float Cz = 200.0f * (xyz.y - xyz.z);
            x = Y;
            y = Cx;
            z = Cz;
            return;
        }

        public void YCxCz2XYZ_() { YCxCz2XYZ_(D65); }
        public void YCxCz2XYZ_(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 YCxCz = this;
            float Yy = (YCxCz.x + 16.0f) / 116.0f;
            float Cx = YCxCz.y / 500.0f;
            float Cz = YCxCz.z / 200.0f;
            x = Yy + Cx;
            y = Yy;
            z = Yy - Cz;
            x *= reference_illuminant.x;
            y *= reference_illuminant.y;
            z *= reference_illuminant.z;
            return;
        }

        public void XYZ2CIELab_() { XYZ2CIELab_(D65); }
        public void XYZ2CIELab_(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 xyz = this;
            xyz.abs();
            xyz.x /= reference_illuminant.x;
            xyz.y /= reference_illuminant.y;
            xyz.z /= reference_illuminant.z;
            xyz.x = xyz.x > 0.008856 ? MathF.Pow(xyz.x, 1.0f / 3.0f) : 7.787f * xyz.x + 16.0f / 116.0f;
            xyz.y = xyz.y > 0.008856 ? MathF.Pow(xyz.y, 1.0f / 3.0f) : 7.787f * xyz.y + 16.0f / 116.0f;
            xyz.z = xyz.z > 0.008856 ? MathF.Pow(xyz.z, 1.0f / 3.0f) : 7.787f * xyz.z + 16.0f / 116.0f;
            x = 116.0f * xyz.y - 16.0f;
            y = 500.0f * (xyz.x - xyz.y);
            z = 200.0f * (xyz.y - xyz.z);
            return;
        }

        public void CIELab2XYZ_() { CIELab2XYZ_(D65); }
        public void CIELab2XYZ_(color3 reference_illuminant)     // the default illuminant is D65
        {
            color3 LAB = this;
            float Y = (LAB.x + 16.0f) / 116.0f;
            float X = LAB.y / 500.0f + Y;
            float Z = Y - LAB.z / 200.0f;
            X = ((X > 0.206897) ? X * X * X : (X - 16.0f / 116.0f) / 7.787f);
            Y = ((Y > 0.206897) ? Y * Y * Y : (Y - 16.0f / 116.0f) / 7.787f);
            Z = ((Z > 0.206897) ? Z * Z * Z : (Z - 16.0f / 116.0f) / 7.787f);
            x = X * reference_illuminant.x;
            y = Y * reference_illuminant.y;
            z = Z * reference_illuminant.z;
            return;
        }
    }

    // Chainable (ref-returning) forms of the color3 operations, matching the C++ member
    // functions that return color3&.
    public static class color3Ext
    {
        public static ref color3 abs(this ref color3 c) { c.abs_(); return ref c; }
        public static ref color3 max(this ref color3 c, float value) { c.max_(value); return ref c; }
        public static ref color3 min(this ref color3 c, float value) { c.min_(value); return ref c; }
        public static ref color3 min(this ref color3 c, color3 value) { c.min_(value); return ref c; }
        public static ref color3 sRGB2LinearRGB(this ref color3 c) { c.sRGB2LinearRGB_(); return ref c; }
        public static ref color3 LinearRGB2sRGB(this ref color3 c) { c.LinearRGB2sRGB_(); return ref c; }
        public static ref color3 LinearRGB2XYZ(this ref color3 c) { c.LinearRGB2XYZ_(); return ref c; }
        public static ref color3 XYZ2LinearRGB(this ref color3 c) { c.XYZ2LinearRGB_(); return ref c; }
        public static ref color3 XYZ2YCxCz(this ref color3 c) { c.XYZ2YCxCz_(); return ref c; }
        public static ref color3 XYZ2YCxCz(this ref color3 c, color3 ill) { c.XYZ2YCxCz_(ill); return ref c; }
        public static ref color3 YCxCz2XYZ(this ref color3 c) { c.YCxCz2XYZ_(); return ref c; }
        public static ref color3 YCxCz2XYZ(this ref color3 c, color3 ill) { c.YCxCz2XYZ_(ill); return ref c; }
        public static ref color3 XYZ2CIELab(this ref color3 c) { c.XYZ2CIELab_(); return ref c; }
        public static ref color3 XYZ2CIELab(this ref color3 c, color3 ill) { c.XYZ2CIELab_(ill); return ref c; }
        public static ref color3 CIELab2XYZ(this ref color3 c) { c.CIELab2XYZ_(); return ref c; }
        public static ref color3 CIELab2XYZ(this ref color3 c, color3 ill) { c.CIELab2XYZ_(ill); return ref c; }
    }

    public class image
    {
        public image() { mWidth = 0; mHeight = 0; }
        public image(int width, int height) { allocate(width, height); }
        public image(image o) { mWidth = o.mWidth; mHeight = o.mHeight; mPixels = (color3[])o.mPixels.Clone(); }

        public int mWidth = 0;
        public int mHeight = 0;
        public color3[] mPixels = new color3[0];

        public void allocate(int width, int height)
        {
            mWidth = width; mHeight = height;
            // mPixels.resize(mWidth * mHeight, color3(0, 0, 0)): existing elements are kept.
            Array.Resize(ref mPixels, mWidth * mHeight);
        }

        public ref color3 get(int x, int y) { return ref mPixels[y * mWidth + x]; }
        public void set(int x, int y, color3 color) { mPixels[y * mWidth + x] = color; }

        // flip.cpp globals
        public static float gPPD;
        public static float gMonitorDistance = 0.7f;
        public static float gMonitorWidth = 0.7f;
        public static float gMonitorResolutionX = 3840.0f;
        public const float gqc = 0.7f;
        public const float gpc = 0.4f;
        public const float gpt = 0.95f;
        public const float gw = 0.082f;
        public const float gqf = 0.5f;

        //  Pixels per degree (PPD)
        public static float calculatePPD(float dist, float resolutionX, float monitorWidth)
        {
            return dist * (resolutionX / monitorWidth) * ((float)Math.PI / 180.0f);
        }

        private static float HyAB(color3 refPixel, color3 testPixel)
        {
            float cityBlockDistanceL = MathF.Abs(refPixel.x - testPixel.x);
            float euclideanDistanceAB = MathF.Sqrt((refPixel.y - testPixel.y) * (refPixel.y - testPixel.y) + (refPixel.z - testPixel.z) * (refPixel.z - testPixel.z));
            return cityBlockDistanceL + euclideanDistanceAB;
        }

        private static float Gaussian(float x, float y, float sigma)
        {
            return MathF.Exp(-(x * x + y * y) / (2.0f * sigma * sigma));
        }

        private static float GaussSum(float x2, float a1, float b1, float a2, float b2)
        {
            float pi = (float)Math.PI;
            float pi_sq = (float)(Math.PI * Math.PI);
            return a1 * MathF.Sqrt(pi / b1) * MathF.Exp(-pi_sq * x2 / b1) + a2 * MathF.Sqrt(pi / b2) * MathF.Exp(-pi_sq * x2 / b2);
        }

        public void generateSpatialFilter(float ppd)
        {
            float deltaX = 1.0f / ppd;
            float pi_sq = (float)(Math.PI * Math.PI);
            // constants for Gaussians -- see paper for details.
            color3 a1 = new color3(1.0f, 1.0f, 34.1f);
            color3 b1 = new color3(0.0047f, 0.0053f, 0.04f);
            color3 a2 = new color3(0.0f, 0.0f, 13.5f);
            color3 b2 = new color3(1.0e-5f, 1.0e-5f, 0.025f);

            float maxScaleParameter = Math.Max(Math.Max(Math.Max(b1.x, b1.y), Math.Max(b1.z, b2.x)), Math.Max(b2.y, b2.z));
            int radius = (int)MathF.Ceiling(3.0f * MathF.Sqrt(maxScaleParameter / (2.0f * pi_sq)) * ppd); // Set radius based on largest scale parameter

            int width = 2 * radius + 1;
            allocate(width, width);

            color3 filterSum = new color3(0.0f, 0.0f, 0.0f);
            for (int y = 0; y < width; y++)
            {
                float iy = (y - radius) * deltaX;
                for (int x = 0; x < width; x++)
                {
                    float ix = (x - radius) * deltaX;

                    float dist2 = ix * ix + iy * iy;
                    color3 value = new color3(GaussSum(dist2, a1.x, b1.x, a2.x, b2.x), GaussSum(dist2, a1.y, b1.y, a2.y, b2.y), GaussSum(dist2, a1.z, b1.z, a2.z, b2.z));
                    this.set(x, y, value);
                    filterSum = new color3(filterSum.x + value.x, filterSum.y + value.y, filterSum.z + value.z);
                }
            }

            // normalize weights
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    color3 value = this.get(x, y);
                    this.set(x, y, new color3(value.x / filterSum.x, value.y / filterSum.y, value.z / filterSum.z));
                }
            }
        }

        public void convolve(image unfilteredImage, image filter)     // assumes a filter whose weights sum to one, and we use "replicate" for borders, i.e., clamp to borders
        {
            System.Diagnostics.Debug.Assert(filter.mWidth % 2 == 1 && filter.mHeight % 2 == 1);  // need to have odd-sized filter
            int halfFilterWidth = filter.mWidth / 2;
            int halfFilterHeight = filter.mHeight / 2;

            // apply filter
            for (int y = 0; y < mHeight; y++)
            {
                for (int x = 0; x < mWidth; x++)
                {
                    color3 colorSum = new color3(0.0f, 0.0f, 0.0f);

                    // filter with the kernel
                    for (int iy = -halfFilterHeight; iy <= halfFilterHeight; iy++)
                    {
                        int yy = Math.Min(Math.Max(0, y + iy), mHeight - 1);
                        for (int ix = -halfFilterWidth; ix <= halfFilterWidth; ix++)
                        {
                            int xx = Math.Min(Math.Max(0, x + ix), mWidth - 1);
                            // read from unfilteredImage and sum into colorSum and weightSum
                            color3 weights = filter.get(ix + halfFilterWidth, iy + halfFilterHeight);
                            color3 srcColor = unfilteredImage.get(xx, yy);
                            colorSum = new color3(colorSum.x + weights.x * srcColor.x, colorSum.y + weights.y * srcColor.y, colorSum.z + weights.z * srcColor.z);
                        }
                    }
                    this.set(x, y, new color3(colorSum.x, colorSum.y, colorSum.z));     // compute final filtered value (and store in mPixels)
                }
            }
        }

        private static float Hunt(float luminance, float chrominance)
        {
            return 0.01f * luminance * chrominance;
        }

        public void huntAdjustment()
        {
            for (int i = 0; i < mPixels.Length; ++i)
            {
                ref color3 pixel = ref mPixels[i];
                pixel.y = Hunt(pixel.x, pixel.y);
                pixel.z = Hunt(pixel.x, pixel.z);
            }
        }

        public void preprocess(image filter)     // implements spatial filter and Hunt adjustment
        {
            // convolve with CSF filters
            image inputImage = new image(this);              // copy the input image
            this.convolve(inputImage, filter);

            // the result after the spatial filter is in YCxCz. Clamp and transform this->mPixels to L*a*b
            for (int i = 0; i < this.mPixels.Length; ++i)
                this.mPixels[i].YCxCz2XYZ().XYZ2LinearRGB().min(1.0f).max(0.0f).LinearRGB2XYZ().XYZ2CIELab();

            huntAdjustment();
        }

        private static float computeMaxDistance()
        {
            color3 greenLab = new color3(0.0f, 1.0f, 0.0f);     // in RGB to start with
            color3 blueLab = new color3(0.0f, 0.0f, 1.0f);
            greenLab.LinearRGB2XYZ().XYZ2CIELab();          // now in Lab
            blueLab.LinearRGB2XYZ().XYZ2CIELab();
            color3 greenLabHunt = new color3(greenLab.x, Hunt(greenLab.x, greenLab.y), Hunt(greenLab.x, greenLab.z));
            color3 blueLabHunt = new color3(blueLab.x, Hunt(blueLab.x, blueLab.y), Hunt(blueLab.x, blueLab.z));

            return MathF.Pow(HyAB(greenLabHunt, blueLabHunt), gqc);
        }

        public void computeColorDifference(image preprocessedReference, image preprocessedTest)
        {
            this.allocate(preprocessedReference.mWidth, preprocessedReference.mHeight);

            float cmax = computeMaxDistance();
            float pccmax = gpc * cmax;

            // compute difference in HyAB and redistribute errors
            for (int y = 0; y < this.mHeight; y++)
            {
                for (int x = 0; x < this.mWidth; x++)
                {
                    // compute difference in HyAB
                    color3 refPixel = preprocessedReference.get(x, y);
                    color3 testPixel = preprocessedTest.get(x, y);
                    float error = HyAB(refPixel, testPixel);

                    error = MathF.Pow(error, gqc);

                    // Re-map error to the [0, 1] range. Values between 0 and pccmax are mapped to the range [0, gpt],
                    // while the rest are mapped to the range (gpt, 1]
                    if (error < pccmax)
                    {
                        error *= gpt / pccmax;
                    }
                    else
                    {
                        error = gpt + ((error - pccmax) / (cmax - pccmax)) * (1.0f - gpt);
                    }

                    this.set(x, y, new color3(error, 0.0f, 0.0f));
                }
            }
        }

        public void generateDetectionFilters(float ppd, bool pointDetector)
        {
            float stdDev = 0.5f * gw * ppd;
            int radius = (int)MathF.Ceiling(3.0f * stdDev);
            int width = 2 * radius + 1;

            this.allocate(width, width);

            float weightX, weightY;
            float negativeWeightsSumX = 0.0f;
            float positiveWeightsSumX = 0.0f;
            float negativeWeightsSumY = 0.0f;
            float positiveWeightsSumY = 0.0f;

            for (int y = 0; y < width; y++)
            {
                int yy = y - radius;
                for (int x = 0; x < width; x++)
                {
                    int xx = x - radius;
                    float G = Gaussian((float)xx, (float)yy, stdDev);
                    if (pointDetector)
                    {
                        weightX = ((float)xx * (float)xx / (stdDev * stdDev) - 1.0f) * G;
                        weightY = ((float)yy * (float)yy / (stdDev * stdDev) - 1.0f) * G;
                    }
                    else
                    {
                        weightX = -(float)xx * G;
                        weightY = -(float)yy * G;
                    }
                    this.set(x, y, new color3(weightX, weightY, 0.0f));

                    if (weightX > 0.0f)
                        positiveWeightsSumX += weightX;
                    else
                        negativeWeightsSumX += -weightX;

                    if (weightY > 0.0f)
                        positiveWeightsSumY += weightY;
                    else
                        negativeWeightsSumY += -weightY;
                }
            }

            // Normalize positive weights to sum to 1 and negative weights to sum to -1
            for (int y = 0; y < width; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    color3 p = this.get(x, y);

                    this.set(x, y, new color3(p.x / (p.x > 0.0f ? positiveWeightsSumX : negativeWeightsSumX), p.y / (p.y > 0.0f ? positiveWeightsSumY : negativeWeightsSumY), 0.0f));
                }
            }
        }

        public void computeFeatureDifference(image refImage, image testImage, float ppd)
        {
            this.allocate(refImage.mWidth, testImage.mHeight);

            // make copy of ref and test images and extract and normalize achromatic component
            image refGray = new image(refImage);
            image testGray = new image(testImage);
            for (int i = 0; i < refGray.mPixels.Length; ++i)
            {
                float c = (refGray.mPixels[i].x + 16.0f) / 116.0f;   // make it [0,1]
                refGray.mPixels[i] = new color3(c, c, 0.0f);         // luminance [0,1] stored in both x and y since we apply both horizontal and verticals filters at the same time
            }

            for (int i = 0; i < testGray.mPixels.Length; ++i)
            {
                float c = (testGray.mPixels[i].x + 16.0f) / 116.0f;   // make it [0,1]
                testGray.mPixels[i] = new color3(c, c, 0.0f);         // luminance [0,1] stored in both x and y since we apply both horizontal and verticals filters at the same time
            }

            image edgesReference = new image(this.mWidth, this.mHeight);
            image pointsReference = new image(this.mWidth, this.mHeight);
            image edgesTest = new image(this.mWidth, this.mHeight);
            image pointsTest = new image(this.mWidth, this.mHeight);

            image edgeFilters = new image();
            image pointFilters = new image();
            edgeFilters.generateDetectionFilters(ppd, false); // contains both horiontal and vertical filter
            pointFilters.generateDetectionFilters(ppd, true); // contains both horiontal and vertical filter

            edgesReference.convolve(refGray, edgeFilters);
            edgesTest.convolve(testGray, edgeFilters);
            pointsReference.convolve(refGray, pointFilters);
            pointsTest.convolve(testGray, pointFilters);

            float normalizationFactor = 1.0f / MathF.Sqrt(2.0f);
            color3 p;
            for (int y = 0; y < this.mHeight; y++)
            {
                for (int x = 0; x < this.mWidth; x++)
                {
                    p = edgesReference.get(x, y);
                    float edgeValueRef = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = edgesTest.get(x, y);
                    float edgeValueTest = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = pointsReference.get(x, y);
                    float pointValueRef = MathF.Sqrt(p.x * p.x + p.y * p.y);
                    p = pointsTest.get(x, y);
                    float pointValueTest = MathF.Sqrt(p.x * p.x + p.y * p.y);

                    float edgeDifference = MathF.Abs(edgeValueRef - edgeValueTest);
                    float pointDifference = MathF.Abs(pointValueRef - pointValueTest);

                    float featureDifference = MathF.Pow(normalizationFactor * Math.Max(edgeDifference, pointDifference), gqf);

                    this.set(x, y, new color3(featureDifference, 0.0f, 0.0f));
                }
            }
        }

        // Note: declared as computeFLIPError(testImage, refImage, verbose) but defined with the
        // parameters named (refImage, testImage), exactly as in the C++ sources.
        public void computeFLIPError(image refImage, image testImage, bool verbose)
        {
            // Transform refImage and testImage to YCxCz opponent space
            for (int i = 0; i < refImage.mPixels.Length; ++i)
            {
                refImage.mPixels[i].sRGB2LinearRGB().LinearRGB2XYZ().XYZ2YCxCz();
            }
            for (int i = 0; i < testImage.mPixels.Length; ++i)
            {
                testImage.mPixels[i].sRGB2LinearRGB().LinearRGB2XYZ().XYZ2YCxCz();
            }

            // Color pipeline
            // Preprocess images
            image preprocessedReference = new image(refImage);
            image preprocessedTest = new image(testImage);
            image filter = new image();
            filter.generateSpatialFilter(gPPD);
            if (verbose) { Console.Out.Write("Preprocessing reference image..."); Console.Out.Flush(); }
            preprocessedReference.preprocess(filter);
            if (verbose) { Console.Out.Write("ok.\nPreprocessing test image..."); Console.Out.Flush(); }
            preprocessedTest.preprocess(filter);
            if (verbose) { Console.Out.Write("ok.\nComputing error map..."); Console.Out.Flush(); }
            // Compute color difference
            image colorDifference = new image();
            colorDifference.computeColorDifference(preprocessedReference, preprocessedTest);

            // Feature pipeline
            image featureDifference = new image();
            featureDifference.computeFeatureDifference(refImage, testImage, gPPD);

            // Final error
            this.allocate(colorDifference.mWidth, colorDifference.mHeight);
            for (int y = 0; y < this.mHeight; y++)
            {
                for (int x = 0; x < this.mWidth; x++)
                {
                    float cdiff = colorDifference.get(x, y).x;
                    float fdiff = featureDifference.get(x, y).x;
                    float errorFLIP = MathF.Pow(cdiff, 1.0f - fdiff);
                    this.set(x, y, new color3(errorFLIP, errorFLIP, errorFLIP));
                }
            }
        }

        public static void handlePooling(image img, pooling pool)
        {
            // add all FLIP values in the error map to pooling
            for (int y = 0; y < img.mHeight; y++)
            {
                for (int x = 0; x < img.mWidth; x++)
                {
                    float value = img.get(x, y).x;
                    pool.update((uint)x, (uint)y, value);
                }
            }
        }

        static readonly color3[] magmaMap = new color3[256] {
    new color3(0.001462f, 0.000466f, 0.013866f), new color3(0.002258f, 0.001295f, 0.018331f), new color3(0.003279f, 0.002305f, 0.023708f), new color3(0.004512f, 0.003490f, 0.029965f),
    new color3(0.005950f, 0.004843f, 0.037130f), new color3(0.007588f, 0.006356f, 0.044973f), new color3(0.009426f, 0.008022f, 0.052844f), new color3(0.011465f, 0.009828f, 0.060750f),
    new color3(0.013708f, 0.011771f, 0.068667f), new color3(0.016156f, 0.013840f, 0.076603f), new color3(0.018815f, 0.016026f, 0.084584f), new color3(0.021692f, 0.018320f, 0.092610f),
    new color3(0.024792f, 0.020715f, 0.100676f), new color3(0.028123f, 0.023201f, 0.108787f), new color3(0.031696f, 0.025765f, 0.116965f), new color3(0.035520f, 0.028397f, 0.125209f),
    new color3(0.039608f, 0.031090f, 0.133515f), new color3(0.043830f, 0.033830f, 0.141886f), new color3(0.048062f, 0.036607f, 0.150327f), new color3(0.052320f, 0.039407f, 0.158841f),
    new color3(0.056615f, 0.042160f, 0.167446f), new color3(0.060949f, 0.044794f, 0.176129f), new color3(0.065330f, 0.047318f, 0.184892f), new color3(0.069764f, 0.049726f, 0.193735f),
    new color3(0.074257f, 0.052017f, 0.202660f), new color3(0.078815f, 0.054184f, 0.211667f), new color3(0.083446f, 0.056225f, 0.220755f), new color3(0.088155f, 0.058133f, 0.229922f),
    new color3(0.092949f, 0.059904f, 0.239164f), new color3(0.097833f, 0.061531f, 0.248477f), new color3(0.102815f, 0.063010f, 0.257854f), new color3(0.107899f, 0.064335f, 0.267289f),
    new color3(0.113094f, 0.065492f, 0.276784f), new color3(0.118405f, 0.066479f, 0.286321f), new color3(0.123833f, 0.067295f, 0.295879f), new color3(0.129380f, 0.067935f, 0.305443f),
    new color3(0.135053f, 0.068391f, 0.315000f), new color3(0.140858f, 0.068654f, 0.324538f), new color3(0.146785f, 0.068738f, 0.334011f), new color3(0.152839f, 0.068637f, 0.343404f),
    new color3(0.159018f, 0.068354f, 0.352688f), new color3(0.165308f, 0.067911f, 0.361816f), new color3(0.171713f, 0.067305f, 0.370771f), new color3(0.178212f, 0.066576f, 0.379497f),
    new color3(0.184801f, 0.065732f, 0.387973f), new color3(0.191460f, 0.064818f, 0.396152f), new color3(0.198177f, 0.063862f, 0.404009f), new color3(0.204935f, 0.062907f, 0.411514f),
    new color3(0.211718f, 0.061992f, 0.418647f), new color3(0.218512f, 0.061158f, 0.425392f), new color3(0.225302f, 0.060445f, 0.431742f), new color3(0.232077f, 0.059889f, 0.437695f),
    new color3(0.238826f, 0.059517f, 0.443256f), new color3(0.245543f, 0.059352f, 0.448436f), new color3(0.252220f, 0.059415f, 0.453248f), new color3(0.258857f, 0.059706f, 0.457710f),
    new color3(0.265447f, 0.060237f, 0.461840f), new color3(0.271994f, 0.060994f, 0.465660f), new color3(0.278493f, 0.061978f, 0.469190f), new color3(0.284951f, 0.063168f, 0.472451f),
    new color3(0.291366f, 0.064553f, 0.475462f), new color3(0.297740f, 0.066117f, 0.478243f), new color3(0.304081f, 0.067835f, 0.480812f), new color3(0.310382f, 0.069702f, 0.483186f),
    new color3(0.316654f, 0.071690f, 0.485380f), new color3(0.322899f, 0.073782f, 0.487408f), new color3(0.329114f, 0.075972f, 0.489287f), new color3(0.335308f, 0.078236f, 0.491024f),
    new color3(0.341482f, 0.080564f, 0.492631f), new color3(0.347636f, 0.082946f, 0.494121f), new color3(0.353773f, 0.085373f, 0.495501f), new color3(0.359898f, 0.087831f, 0.496778f),
    new color3(0.366012f, 0.090314f, 0.497960f), new color3(0.372116f, 0.092816f, 0.499053f), new color3(0.378211f, 0.095332f, 0.500067f), new color3(0.384299f, 0.097855f, 0.501002f),
    new color3(0.390384f, 0.100379f, 0.501864f), new color3(0.396467f, 0.102902f, 0.502658f), new color3(0.402548f, 0.105420f, 0.503386f), new color3(0.408629f, 0.107930f, 0.504052f),
    new color3(0.414709f, 0.110431f, 0.504662f), new color3(0.420791f, 0.112920f, 0.505215f), new color3(0.426877f, 0.115395f, 0.505714f), new color3(0.432967f, 0.117855f, 0.506160f),
    new color3(0.439062f, 0.120298f, 0.506555f), new color3(0.445163f, 0.122724f, 0.506901f), new color3(0.451271f, 0.125132f, 0.507198f), new color3(0.457386f, 0.127522f, 0.507448f),
    new color3(0.463508f, 0.129893f, 0.507652f), new color3(0.469640f, 0.132245f, 0.507809f), new color3(0.475780f, 0.134577f, 0.507921f), new color3(0.481929f, 0.136891f, 0.507989f),
    new color3(0.488088f, 0.139186f, 0.508011f), new color3(0.494258f, 0.141462f, 0.507988f), new color3(0.500438f, 0.143719f, 0.507920f), new color3(0.506629f, 0.145958f, 0.507806f),
    new color3(0.512831f, 0.148179f, 0.507648f), new color3(0.519045f, 0.150383f, 0.507443f), new color3(0.525270f, 0.152569f, 0.507192f), new color3(0.531507f, 0.154739f, 0.506895f),
    new color3(0.537755f, 0.156894f, 0.506551f), new color3(0.544015f, 0.159033f, 0.506159f), new color3(0.550287f, 0.161158f, 0.505719f), new color3(0.556571f, 0.163269f, 0.505230f),
    new color3(0.562866f, 0.165368f, 0.504692f), new color3(0.569172f, 0.167454f, 0.504105f), new color3(0.575490f, 0.169530f, 0.503466f), new color3(0.581819f, 0.171596f, 0.502777f),
    new color3(0.588158f, 0.173652f, 0.502035f), new color3(0.594508f, 0.175701f, 0.501241f), new color3(0.600868f, 0.177743f, 0.500394f), new color3(0.607238f, 0.179779f, 0.499492f),
    new color3(0.613617f, 0.181811f, 0.498536f), new color3(0.620005f, 0.183840f, 0.497524f), new color3(0.626401f, 0.185867f, 0.496456f), new color3(0.632805f, 0.187893f, 0.495332f),
    new color3(0.639216f, 0.189921f, 0.494150f), new color3(0.645633f, 0.191952f, 0.492910f), new color3(0.652056f, 0.193986f, 0.491611f), new color3(0.658483f, 0.196027f, 0.490253f),
    new color3(0.664915f, 0.198075f, 0.488836f), new color3(0.671349f, 0.200133f, 0.487358f), new color3(0.677786f, 0.202203f, 0.485819f), new color3(0.684224f, 0.204286f, 0.484219f),
    new color3(0.690661f, 0.206384f, 0.482558f), new color3(0.697098f, 0.208501f, 0.480835f), new color3(0.703532f, 0.210638f, 0.479049f), new color3(0.709962f, 0.212797f, 0.477201f),
    new color3(0.716387f, 0.214982f, 0.475290f), new color3(0.722805f, 0.217194f, 0.473316f), new color3(0.729216f, 0.219437f, 0.471279f), new color3(0.735616f, 0.221713f, 0.469180f),
    new color3(0.742004f, 0.224025f, 0.467018f), new color3(0.748378f, 0.226377f, 0.464794f), new color3(0.754737f, 0.228772f, 0.462509f), new color3(0.761077f, 0.231214f, 0.460162f),
    new color3(0.767398f, 0.233705f, 0.457755f), new color3(0.773695f, 0.236249f, 0.455289f), new color3(0.779968f, 0.238851f, 0.452765f), new color3(0.786212f, 0.241514f, 0.450184f),
    new color3(0.792427f, 0.244242f, 0.447543f), new color3(0.798608f, 0.247040f, 0.444848f), new color3(0.804752f, 0.249911f, 0.442102f), new color3(0.810855f, 0.252861f, 0.439305f),
    new color3(0.816914f, 0.255895f, 0.436461f), new color3(0.822926f, 0.259016f, 0.433573f), new color3(0.828886f, 0.262229f, 0.430644f), new color3(0.834791f, 0.265540f, 0.427671f),
    new color3(0.840636f, 0.268953f, 0.424666f), new color3(0.846416f, 0.272473f, 0.421631f), new color3(0.852126f, 0.276106f, 0.418573f), new color3(0.857763f, 0.279857f, 0.415496f),
    new color3(0.863320f, 0.283729f, 0.412403f), new color3(0.868793f, 0.287728f, 0.409303f), new color3(0.874176f, 0.291859f, 0.406205f), new color3(0.879464f, 0.296125f, 0.403118f),
    new color3(0.884651f, 0.300530f, 0.400047f), new color3(0.889731f, 0.305079f, 0.397002f), new color3(0.894700f, 0.309773f, 0.393995f), new color3(0.899552f, 0.314616f, 0.391037f),
    new color3(0.904281f, 0.319610f, 0.388137f), new color3(0.908884f, 0.324755f, 0.385308f), new color3(0.913354f, 0.330052f, 0.382563f), new color3(0.917689f, 0.335500f, 0.379915f),
    new color3(0.921884f, 0.341098f, 0.377376f), new color3(0.925937f, 0.346844f, 0.374959f), new color3(0.929845f, 0.352734f, 0.372677f), new color3(0.933606f, 0.358764f, 0.370541f),
    new color3(0.937221f, 0.364929f, 0.368567f), new color3(0.940687f, 0.371224f, 0.366762f), new color3(0.944006f, 0.377643f, 0.365136f), new color3(0.947180f, 0.384178f, 0.363701f),
    new color3(0.950210f, 0.390820f, 0.362468f), new color3(0.953099f, 0.397563f, 0.361438f), new color3(0.955849f, 0.404400f, 0.360619f), new color3(0.958464f, 0.411324f, 0.360014f),
    new color3(0.960949f, 0.418323f, 0.359630f), new color3(0.963310f, 0.425390f, 0.359469f), new color3(0.965549f, 0.432519f, 0.359529f), new color3(0.967671f, 0.439703f, 0.359810f),
    new color3(0.969680f, 0.446936f, 0.360311f), new color3(0.971582f, 0.454210f, 0.361030f), new color3(0.973381f, 0.461520f, 0.361965f), new color3(0.975082f, 0.468861f, 0.363111f),
    new color3(0.976690f, 0.476226f, 0.364466f), new color3(0.978210f, 0.483612f, 0.366025f), new color3(0.979645f, 0.491014f, 0.367783f), new color3(0.981000f, 0.498428f, 0.369734f),
    new color3(0.982279f, 0.505851f, 0.371874f), new color3(0.983485f, 0.513280f, 0.374198f), new color3(0.984622f, 0.520713f, 0.376698f), new color3(0.985693f, 0.528148f, 0.379371f),
    new color3(0.986700f, 0.535582f, 0.382210f), new color3(0.987646f, 0.543015f, 0.385210f), new color3(0.988533f, 0.550446f, 0.388365f), new color3(0.989363f, 0.557873f, 0.391671f),
    new color3(0.990138f, 0.565296f, 0.395122f), new color3(0.990871f, 0.572706f, 0.398714f), new color3(0.991558f, 0.580107f, 0.402441f), new color3(0.992196f, 0.587502f, 0.406299f),
    new color3(0.992785f, 0.594891f, 0.410283f), new color3(0.993326f, 0.602275f, 0.414390f), new color3(0.993834f, 0.609644f, 0.418613f), new color3(0.994309f, 0.616999f, 0.422950f),
    new color3(0.994738f, 0.624350f, 0.427397f), new color3(0.995122f, 0.631696f, 0.431951f), new color3(0.995480f, 0.639027f, 0.436607f), new color3(0.995810f, 0.646344f, 0.441361f),
    new color3(0.996096f, 0.653659f, 0.446213f), new color3(0.996341f, 0.660969f, 0.451160f), new color3(0.996580f, 0.668256f, 0.456192f), new color3(0.996775f, 0.675541f, 0.461314f),
    new color3(0.996925f, 0.682828f, 0.466526f), new color3(0.997077f, 0.690088f, 0.471811f), new color3(0.997186f, 0.697349f, 0.477182f), new color3(0.997254f, 0.704611f, 0.482635f),
    new color3(0.997325f, 0.711848f, 0.488154f), new color3(0.997351f, 0.719089f, 0.493755f), new color3(0.997351f, 0.726324f, 0.499428f), new color3(0.997341f, 0.733545f, 0.505167f),
    new color3(0.997285f, 0.740772f, 0.510983f), new color3(0.997228f, 0.747981f, 0.516859f), new color3(0.997138f, 0.755190f, 0.522806f), new color3(0.997019f, 0.762398f, 0.528821f),
    new color3(0.996898f, 0.769591f, 0.534892f), new color3(0.996727f, 0.776795f, 0.541039f), new color3(0.996571f, 0.783977f, 0.547233f), new color3(0.996369f, 0.791167f, 0.553499f),
    new color3(0.996162f, 0.798348f, 0.559820f), new color3(0.995932f, 0.805527f, 0.566202f), new color3(0.995680f, 0.812706f, 0.572645f), new color3(0.995424f, 0.819875f, 0.579140f),
    new color3(0.995131f, 0.827052f, 0.585701f), new color3(0.994851f, 0.834213f, 0.592307f), new color3(0.994524f, 0.841387f, 0.598983f), new color3(0.994222f, 0.848540f, 0.605696f),
    new color3(0.993866f, 0.855711f, 0.612482f), new color3(0.993545f, 0.862859f, 0.619299f), new color3(0.993170f, 0.870024f, 0.626189f), new color3(0.992831f, 0.877168f, 0.633109f),
    new color3(0.992440f, 0.884330f, 0.640099f), new color3(0.992089f, 0.891470f, 0.647116f), new color3(0.991688f, 0.898627f, 0.654202f), new color3(0.991332f, 0.905763f, 0.661309f),
    new color3(0.990930f, 0.912915f, 0.668481f), new color3(0.990570f, 0.920049f, 0.675675f), new color3(0.990175f, 0.927196f, 0.682926f), new color3(0.989815f, 0.934329f, 0.690198f),
    new color3(0.989434f, 0.941470f, 0.697519f), new color3(0.989077f, 0.948604f, 0.704863f), new color3(0.988717f, 0.955742f, 0.712242f), new color3(0.988367f, 0.962878f, 0.719649f),
    new color3(0.988033f, 0.970012f, 0.727077f), new color3(0.987691f, 0.977154f, 0.734536f), new color3(0.987387f, 0.984288f, 0.742002f), new color3(0.987053f, 0.991438f, 0.749504f)
        };

        public void remapToMagma()
        {
            for (int i = 0; i < mPixels.Length; ++i)
            {
                int index = (int)(Math.Min(Math.Max(mPixels[i].x, 0.0f), 1.0f) * 255.0f);
                mPixels[i] = magmaMap[index];
            }
        }
    }
}

namespace pbrt
{
    public static class flip
    {
        public static void ComputeFLIPError(float[] testRGB, float[] referenceRGB,
                                            float[] flipError, int xRes, int yRes, FLIPOptions options)
        {
            if (options.ppd > 0)
                flip_detail.image.gPPD = options.ppd;
            else
            {
                flip_detail.image.gMonitorDistance = options.monitorDistance;
                flip_detail.image.gMonitorWidth = options.monitorWidth;
                flip_detail.image.gMonitorResolutionX = options.monitorResolutionX;
                flip_detail.image.gPPD = flip_detail.image.calculatePPD(flip_detail.image.gMonitorDistance, flip_detail.image.gMonitorResolutionX, flip_detail.image.gMonitorWidth);
            }

            flip_detail.image testImage = new flip_detail.image(xRes, yRes), referenceImage = new flip_detail.image(xRes, yRes), flipImage = new flip_detail.image(xRes, yRes);
            for (int y = 0; y < yRes; ++y)
                for (int x = 0; x < xRes; ++x)
                {
                    int offset = 3 * (x + y * xRes);
                    testImage.set(x, y, new flip_detail.color3(testRGB[offset], testRGB[offset + 1], testRGB[offset + 2]));
                    referenceImage.set(x, y, new flip_detail.color3(referenceRGB[offset], referenceRGB[offset + 1], referenceRGB[offset + 2]));
                }

            flipImage.computeFLIPError(testImage, referenceImage, false /* verbose */);

            for (int y = 0; y < yRes; ++y)
                for (int x = 0; x < xRes; ++x)
                    flipError[x + y * xRes] = flipImage.get(x, y).x;
        }
    }
}
