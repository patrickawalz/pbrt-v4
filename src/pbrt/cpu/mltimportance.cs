// (not in pbrt-v4) mltimportance.cs
//
// Importance functions of Markov chain rendering that are not the luminance of the path (Hoberock and Hart, "Arbitrary Importance
// Functions for Metropolis Light Transport", Computer Graphics Forum 29(6), 2010). The chain of mlt follows the importance I(x) = c(x) w(x)
// instead of c(x) = luminance of the contribution, where the weight w(x) is a positive function of the pixel that a path reaches.
// The image is still unbiased: every sample adds f(x) / I(x), times the integral b of I (estimated from the bootstrap samples).
// What the weight is can change between the stages of a render (multi-stage MLT): the noise that the stages before measured makes the
// next stage visit the noisy pixels more. The noise of a pixel is measured from K groups of independent chains, in the way of the
// adaptive samplers of Monte Carlo rendering (Overbeck et al. 2009, Rousselle et al. 2011, 2012; the STAR of Zwicker et al. 2015):
// the sample variance of the groups, which is robust to the one group that a firefly falls in, is divided by the number of visits
// of the pixel (so that it does not depend on the weights of the stage), pooled over the stages, and denoised in the logarithmic
// domain by a bilateral filter that the luminance of the image guides (variance denoising, as StatER of Hachisuka et al. 2025).
// A pixel is then visited in proportion to the standard deviation of its relative error per visit (Neyman allocation).

using System;
using System.IO;
using System.Threading;

namespace pbrt
{

    // MLTStageKind Definition: what a stage of the multi-stage render tackles. The pilot follows the luminance and measures the first noise;
    // a noise stage follows c w with w from the robust variance of the stages before (the noise that is left once the fireflies are
    // set aside); a tail stage follows c^beta w with w from the plain variance, which the rare bright paths dominate, and with the
    // large steps of the chain more probable, so that it looks for the paths that the others miss.
    public enum MLTStageKind
    {
        Pilot,
        Noise,
        Tail,
        Mixture
    }

    // MLTImportanceMap Definition: the weight w of every pixel of the film, never zero (a path that carries light always has
    // a positive importance, or the image would be biased)
    public sealed class MLTImportanceMap
    {
        // MLTImportanceMap Public Methods
        public MLTImportanceMap(Bounds2i bounds, Float range)
        {
            originX = bounds.pMin.x;
            originY = bounds.pMin.y;
            width = bounds.pMax.x - bounds.pMin.x;
            height = bounds.pMax.y - bounds.pMin.y;
            this.range = range;
            weights = new Float[width * height];
            noiseWeights = new Float[width * height];
            tailWeights = new Float[width * height];
            pooledVariance = new double[width * height];
            pooledPlainVariance = new double[width * height];
            pooledVisits = new double[width * height];
            pooledLuminance = new double[width * height];
            pooledRobustLuminance = new double[width * height];
            for (int i = 0; i < weights.Length; ++i)
            {
                weights[i] = 1;
                noiseWeights[i] = 1;
                tailWeights[i] = 1;
            }
        }

        // The weights that Weight returns from now on: one for the pilot, the weights of the noise or of the tail
        public void Select(MLTStageKind kind)
        {
            Float[] source = kind == MLTStageKind.Noise ? noiseWeights : (kind == MLTStageKind.Tail ? tailWeights : null);
            for (int i = 0; i < weights.Length; ++i)
            {
                weights[i] = source == null ? 1 : source[i];
            }
        }

        public int Width { get { return width; } }

        public int Height { get { return height; } }

        public Float Weight(Point2f pRaster)
        {
            int x = Math.Clamp((int)MathF.Floor(pRaster.x) - originX, 0, width - 1);
            int y = Math.Clamp((int)MathF.Floor(pRaster.y) - originY, 0, height - 1);
            return weights[y * width + x];
        }

        // The weights of the next stage from the stage that just ran, which had mutationsPerPixel mutations per pixel.
        // epsilon is the fraction of the mean luminance of the image that is added to the luminance of a pixel in the relative error,
        // so that dark pixels do not take all the samples; exponent is the power of the standard deviation by which the visits grow
        // (1 minimizes the sum of the relative variances, 2 equalizes the relative errors); blurRadius is the radius of the filter
        // of the variance (0: none); robust leaves out the group of a pixel that is farthest from the median (with four groups or more), unless clean
        // is given (the estimate without the paths that the outlier detector found), which then is the robust one.
        // The noise weights come from that robust variance, the tail weights from the plain variance of all the groups, divided by
        // the luminance to the power tailExponent (below one, the dim paths are visited more than under the luminance).
        public void UpdateFromStage(MLTNoiseEstimate noise, int mutationsPerPixel, Float exponent, Float epsilon, int blurRadius,
                                    bool robust, Float tailExponent, MLTNoiseEstimate clean = null)
        {
            int groups = noise.Groups;
            double[] estimates = new double[groups];
            double[] sorted = new double[groups];
            double meanLuminance = 0;
            double[] stageLuminance = new double[weights.Length];
            for (int i = 0; i < weights.Length; ++i)
            {
                double sum = 0;
                for (int k = 0; k < groups; ++k)
                {
                    estimates[k] = groups * noise.Sum(k, i) / mutationsPerPixel;
                    sum += estimates[k];
                }
                stageLuminance[i] = sum / groups;
                meanLuminance += stageLuminance[i];
            }
            meanLuminance /= weights.Length;
            if (!(meanLuminance > 0))
            {
                return;
            }

            // The variance per visit of the relative error of every pixel, pooled over the stages by the visits
            for (int i = 0; i < weights.Length; ++i)
            {
                double visits = noise.Visits(i);
                if (!(visits > 0))
                {
                    continue;
                }
                for (int k = 0; k < groups; ++k)
                {
                    estimates[k] = groups * noise.Sum(k, i) / mutationsPerPixel;
                }
                double plainSquares = 0;
                for (int k = 0; k < groups; ++k)
                {
                    plainSquares += (estimates[k] - stageLuminance[i]) * (estimates[k] - stageLuminance[i]);
                }
                double plainRelative = plainSquares / (groups - 1) * visits / groups /
                                       Math.Pow(stageLuminance[i] + epsilon * meanLuminance, 2);
                pooledPlainVariance[i] += visits * plainRelative;
                int used = groups;
                double center = stageLuminance[i];
                if (clean != null)
                {
                    // The detector has kept the outliers out of the clean estimate: no group is left out, the variance is that of the others
                    center = 0;
                    for (int k = 0; k < groups; ++k)
                    {
                        estimates[k] = groups * clean.Sum(k, i) / mutationsPerPixel;
                        center += estimates[k] / groups;
                    }
                }
                else if (robust && groups >= 4)
                {
                    Array.Copy(estimates, sorted, groups);
                    Array.Sort(sorted);
                    double median = 0.5 * (sorted[(groups - 1) / 2] + sorted[groups / 2]);
                    int farthest = 0;
                    for (int k = 1; k < groups; ++k)
                    {
                        if (Math.Abs(estimates[k] - median) > Math.Abs(estimates[farthest] - median))
                        {
                            farthest = k;
                        }
                    }
                    estimates[farthest] = estimates[groups - 1];
                    used = groups - 1;
                    center = 0;
                    for (int k = 0; k < used; ++k)
                    {
                        center += estimates[k];
                    }
                    center /= used;
                }
                double squares = 0;
                for (int k = 0; k < used; ++k)
                {
                    squares += (estimates[k] - center) * (estimates[k] - center);
                }
                double groupVariance = squares / (used - 1);
                // A group has visits / groups visits: the variance of its estimate is the variance per visit over that number
                double perVisit = groupVariance * visits / groups;
                double relative = perVisit / Math.Pow(center + epsilon * meanLuminance, 2);
                pooledVariance[i] += visits * relative;
                pooledVisits[i] += visits;
                pooledLuminance[i] += visits * stageLuminance[i];
                pooledRobustLuminance[i] += visits * center;
            }

            // Variance denoising: the pooled variances of every pixel, filtered in the logarithmic domain with the weights of the
            // distance and of the difference of the logarithms of the luminances
            double[] variance = new double[weights.Length];
            double[] plainVariance = new double[weights.Length];
            double[] luminance = new double[weights.Length];
            double[] robustLuminance = new double[weights.Length];
            double meanVariance = 0;
            double meanPlainVariance = 0;
            int counted = 0;
            for (int i = 0; i < weights.Length; ++i)
            {
                luminance[i] = pooledVisits[i] > 0 ? pooledLuminance[i] / pooledVisits[i] : 0;
                robustLuminance[i] = pooledVisits[i] > 0 ? pooledRobustLuminance[i] / pooledVisits[i] : 0;
                variance[i] = pooledVisits[i] > 0 ? pooledVariance[i] / pooledVisits[i] : 0;
                plainVariance[i] = pooledVisits[i] > 0 ? pooledPlainVariance[i] / pooledVisits[i] : 0;
                if (pooledVisits[i] > 0)
                {
                    meanVariance += variance[i];
                    meanPlainVariance += plainVariance[i];
                    ++counted;
                }
            }
            if (counted == 0 || (!(meanVariance > 0) && !(meanPlainVariance > 0)))
            {
                return;
            }
            meanVariance /= counted;
            meanPlainVariance /= counted;
            // The robust variance can be zero everywhere (one group with a firefly in every pixel): the floor then comes from the plain one
            double[] smooth = DenoiseVariance(variance, robustLuminance, meanVariance > 0 ? meanVariance : meanPlainVariance, meanLuminance, blurRadius);
            double[] smoothPlain = DenoiseVariance(plainVariance, luminance, meanPlainVariance > 0 ? meanPlainVariance : meanVariance, meanLuminance, blurRadius);

            // The visits of a pixel are proportional to variance^(exponent / 2), and they are the weight times the luminance
            // (to the power of the stage) that the pixel has under the importance of the stage
            double[] noiseTarget = new double[weights.Length];
            double[] tailTarget = new double[weights.Length];
            double sumNoise = 0;
            double sumTail = 0;
            for (int i = 0; i < weights.Length; ++i)
            {
                noiseTarget[i] = Math.Pow(smooth[i], 0.5 * exponent) / (robustLuminance[i] + 0.01 * meanLuminance);
                tailTarget[i] = Math.Pow(smoothPlain[i], 0.5 * exponent) / Math.Pow(luminance[i] + 0.01 * meanLuminance, tailExponent);
                sumNoise += noiseTarget[i];
                sumTail += tailTarget[i];
            }
            double meanNoise = sumNoise / weights.Length;
            double meanTail = sumTail / weights.Length;
            for (int i = 0; i < weights.Length; ++i)
            {
                noiseWeights[i] = (Float)Math.Clamp(noiseTarget[i] / meanNoise, 1.0 / range, (double)range);
                tailWeights[i] = (Float)Math.Clamp(tailTarget[i] / meanTail, 1.0 / range, (double)range);
            }
        }

        // The noise and the tail weights of the pixel that a raster position is in (the mixture stage uses all of them at once)
        public void PixelWeights(Point2f pRaster, out Float noiseWeight, out Float tailWeight)
        {
            int x = Math.Clamp((int)MathF.Floor(pRaster.x) - originX, 0, width - 1);
            int y = Math.Clamp((int)MathF.Floor(pRaster.y) - originY, 0, height - 1);
            noiseWeight = noiseWeights[y * width + x];
            tailWeight = tailWeights[y * width + x];
        }

        // The weight of the noise and the tail maps of a pixel (for the tests and the maps that are written)
        public Float NoiseWeight(int x, int y) { return noiseWeights[y * width + x]; }

        public Float TailWeight(int x, int y) { return tailWeights[y * width + x]; }

        // The weights as a portable float map (the first row at the bottom)
        public void WritePFM(string filename)
        {
            WritePFM(filename, weights);
        }

        public void WritePFM(string filename, MLTStageKind kind)
        {
            WritePFM(filename, kind == MLTStageKind.Noise ? noiseWeights : (kind == MLTStageKind.Tail ? tailWeights : weights));
        }

        void WritePFM(string filename, Float[] map)
        {
            using (BinaryWriter writer = new BinaryWriter(File.Open(filename, FileMode.Create)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("Pf\n" + width + " " + height + "\n-1\n"));
                for (int y = height - 1; y >= 0; --y)
                {
                    for (int x = 0; x < width; ++x)
                    {
                        writer.Write(map[y * width + x]);
                    }
                }
            }
        }

        // MLTImportanceMap Private Methods
        double[] DenoiseVariance(double[] variance, double[] luminance, double meanVariance, double meanLuminance, int radius)
        {
            double floorVariance = 1e-4 * meanVariance;
            double[] logVariance = new double[variance.Length];
            double[] guide = new double[variance.Length];
            for (int i = 0; i < variance.Length; ++i)
            {
                logVariance[i] = Math.Log(variance[i] + floorVariance);
                guide[i] = Math.Log(luminance[i] + 0.05 * meanLuminance);
            }
            if (radius <= 0)
            {
                double[] same = new double[variance.Length];
                for (int i = 0; i < variance.Length; ++i)
                {
                    same[i] = Math.Exp(logVariance[i]);
                }
                return same;
            }
            double sigmaSpace = 0.5 * radius + 0.5;
            const double sigmaGuide = 0.35;
            double[] result = new double[variance.Length];
            for (int y = 0; y < height; ++y)
            {
                for (int x = 0; x < width; ++x)
                {
                    int center = y * width + x;
                    double sum = 0, total = 0;
                    for (int dy = -radius; dy <= radius; ++dy)
                    {
                        int yy = Math.Clamp(y + dy, 0, height - 1);
                        for (int dx = -radius; dx <= radius; ++dx)
                        {
                            int xx = Math.Clamp(x + dx, 0, width - 1);
                            int index = yy * width + xx;
                            double difference = guide[index] - guide[center];
                            double w = Math.Exp(-(dx * dx + dy * dy) / (2 * sigmaSpace * sigmaSpace) -
                                                difference * difference / (2 * sigmaGuide * sigmaGuide));
                            sum += w * logVariance[index];
                            total += w;
                        }
                    }
                    result[center] = Math.Exp(sum / total);
                }
            }
            return result;
        }

        // MLTImportanceMap Private Members
        readonly int originX, originY, width, height;
        readonly Float range;
        readonly Float[] weights, noiseWeights, tailWeights;
        readonly double[] pooledVariance, pooledPlainVariance, pooledVisits, pooledLuminance, pooledRobustLuminance;
    }

    // MLTNoiseEstimate Definition: the luminance that K groups of the chains of a stage (chain i is in group i mod K) splat to every
    // pixel, and the visits (the mass of the splats) of the pixel. The groups are independent estimates of the same image.
    public sealed class MLTNoiseEstimate
    {
        // MLTNoiseEstimate Public Methods
        public MLTNoiseEstimate(MLTImportanceMap map, int groups)
        {
            width = map.Width;
            height = map.Height;
            this.groups = groups;
            sums = new double[groups][];
            for (int k = 0; k < groups; ++k)
            {
                sums[k] = new double[width * height];
            }
            visits = new double[width * height];
        }

        public int Groups { get { return groups; } }

        public double Sum(int group, int pixel) { return sums[group][pixel]; }

        public double Visits(int pixel) { return visits[pixel]; }

        // mass is the probability that the splat stands for (accept or one minus it); value is the luminance that it adds to the pixel
        public void Add(int pixelX, int pixelY, int group, double mass, double value)
        {
            int index = Math.Clamp(pixelY, 0, height - 1) * width + Math.Clamp(pixelX, 0, width - 1);
            AtomicAdd(ref sums[group][index], value);
            AtomicAdd(ref visits[index], mass);
        }

        // MLTNoiseEstimate Private Methods
        static void AtomicAdd(ref double target, double value)
        {
            double initial, computed;
            do
            {
                initial = Volatile.Read(ref target);
                computed = initial + value;
            }
            while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
        }

        // MLTNoiseEstimate Private Members
        readonly int width, height, groups;
        readonly double[][] sums;
        readonly double[] visits;
    }


    // MLTOutlierDetector Definition: the outlier detector of Selectively Metropolised Monte Carlo (Bitterli and Jarosz, "Selectively Metropolised
    // Monte Carlo light transport simulation", ACM Transactions on Graphics 38(6), 2019). Every pixel has cascaded buffers B_0 ... B_m
    // that count the samples of the pixel in exponentially spaced luminance ranges (centered on alpha beta^j, alpha 1/2 and beta 2); a sample
    // splats to the two buffers around its luminance, in proportion to the distance. A sample is an outlier if the counts of the buffers,
    // interpolated at its luminance, are below tau = max(3, spp / m), or if its luminance is beyond the range (1000). The classification
    // reads the counts of the stages before, and the samples of a stage are recorded apart and added when the stage ends (the paper's double buffer),
    // so the classification does not change during a stage.
    public sealed class MLTOutlierDetector
    {
        // MLTOutlierDetector Public Methods
        public MLTOutlierDetector(Bounds2i bounds)
        {
            originX = bounds.pMin.x;
            originY = bounds.pMin.y;
            width = bounds.pMax.x - bounds.pMin.x;
            height = bounds.pMax.y - bounds.pMin.y;
            counts = new float[width * height * bufferCount];
            pending = new double[width * height * bufferCount];
        }

        // Whether any samples have been counted: before that nothing is an outlier
        public bool Ready { get { return samplesPerPixel > 0; } }

        public int BufferCount { get { return bufferCount; } }

        public void Record(Point2f pRaster, Float luminance)
        {
            int index = PixelIndex(pRaster);
            Position(luminance, out int lower, out Float fraction);
            AtomicAdd(ref pending[index * bufferCount + lower], 1 - fraction);
            if (fraction > 0)
            {
                AtomicAdd(ref pending[index * bufferCount + lower + 1], fraction);
            }
        }

        // The recorded samples join the counts; samplesPerStage is the number of samples per pixel that were recorded
        public void Commit(double samplesPerStage)
        {
            for (int i = 0; i < counts.Length; ++i)
            {
                counts[i] += (float)pending[i];
                pending[i] = 0;
            }
            samplesPerPixel += samplesPerStage;
        }

        public bool IsOutlier(Point2f pRaster, Float luminance)
        {
            if (!Ready || !(luminance > 0))
            {
                return false;
            }
            if (luminance > rangeLimit)
            {
                return true;
            }
            int index = PixelIndex(pRaster);
            Position(luminance, out int lower, out Float fraction);
            double count = (1 - fraction) * counts[index * bufferCount + lower];
            if (fraction > 0)
            {
                count += fraction * counts[index * bufferCount + lower + 1];
            }
            double threshold = Math.Max(3.0, samplesPerPixel / bufferCount);
            return count < threshold;
        }

        // MLTOutlierDetector Private Methods
        int PixelIndex(Point2f pRaster)
        {
            int x = Math.Clamp((int)MathF.Floor(pRaster.x) - originX, 0, width - 1);
            int y = Math.Clamp((int)MathF.Floor(pRaster.y) - originY, 0, height - 1);
            return y * width + x;
        }

        // The lower buffer of a luminance and the share of the upper one
        static void Position(Float luminance, out int lower, out Float fraction)
        {
            Float position = MathF.Log2(MathF.Max(luminance, 1e-8f) / alpha);
            if (position <= 0)
            {
                lower = 0;
                fraction = 0;
                return;
            }
            int j = (int)MathF.Floor(position);
            if (j >= bufferCount - 1)
            {
                lower = bufferCount - 1;
                fraction = 0;
                return;
            }
            lower = j;
            fraction = position - j;
        }

        static void AtomicAdd(ref double target, double value)
        {
            double initial, computed;
            do
            {
                initial = Volatile.Read(ref target);
                computed = initial + value;
            }
            while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
        }

        // MLTOutlierDetector Private Members
        const Float alpha = 0.5f;
        const Float rangeLimit = 1000;
        // alpha beta^(bufferCount - 1) is the range limit: 0.5 * 2^11 = 1024
        const int bufferCount = 12;
        readonly int originX, originY, width, height;
        readonly float[] counts;
        readonly double[] pending;
        double samplesPerPixel;
    }

}  // namespace pbrt
