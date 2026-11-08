// (not in pbrt-v4) Lorenz-Mie scattering of homogeneous spheres in double precision (the algorithm of Bohren and Huffman, "Absorption and
// Scattering of Light by Small Particles", 1983, Appendix A), integrated over a log-normal size distribution into the tables an engine needs:
// mass extinction coefficient, single scattering albedo, asymmetry parameter and the four independent elements of the Mueller matrix of a
// cloud of randomly placed spheres (P11, P12, P33, P34 per steradian, P11 integrates to one). Nothing is read from a file: the refractive index
// and the size distribution go in, the tables come out.
//
// Convention of the particle: the refractive index is m = n + i k with k >= 0 and the time dependence exp(-i omega t). The Stokes vector
// is (I, Q, U, V) with Q = I_parallel - I_perpendicular to the scattering plane; the Mueller matrix of a sphere is
//     [ P11 P12  0   0  ]
//     [ P12 P11  0   0  ]
//     [  0   0  P33 P34 ]
//     [  0   0 -P34 P33 ]

using System;
using System.Threading.Tasks;

namespace pbrt
{

    // MieSolver Definition
    public sealed class MieSolver
    {
        // The state that one thread needs: the work arrays grow to the largest size parameter that was asked for
        double[] derivativeReal = new double[0];
        double[] derivativeImaginary = new double[0];
        double[] piPrevious = new double[0];
        double[] piCurrent = new double[0];

        // The amplitude functions of one sphere at the angles whose cosines are given. The arrays of the result are written for every angle:
        // s1 and s2 are the complex amplitudes (real and imaginary parts). Qext and Qsca are efficiencies, asymmetry is <cos theta>.
        public void Sphere(double x, double mReal, double mImaginary, double[] cosines, double[] s1Real, double[] s1Imaginary, double[] s2Real,
                           double[] s2Imaginary, out double efficiencyExtinction, out double efficiencyScattering, out double asymmetry)
        {
            int angleCount = cosines == null ? 0 : cosines.Length;
            double yReal = mReal * x, yImaginary = mImaginary * x;
            double stop = x + 4.0 * Math.Pow(x, 1.0 / 3.0) + 2.0;
            int nStop = (int)stop;
            double yModulus = Math.Sqrt(yReal * yReal + yImaginary * yImaginary);
            int nMx = (int)Math.Max(stop, yModulus) + 15;

            if (derivativeReal.Length < nMx + 1)
            {
                derivativeReal = new double[nMx + 1];
                derivativeImaginary = new double[nMx + 1];
            }
            if (piCurrent.Length < angleCount)
            {
                piPrevious = new double[angleCount];
                piCurrent = new double[angleCount];
            }

            // The logarithmic derivative D_n(m x) by downward recurrence
            double inverseModulusSquared = 1.0 / (yReal * yReal + yImaginary * yImaginary);
            double inverseYReal = yReal * inverseModulusSquared, inverseYImaginary = -yImaginary * inverseModulusSquared;
            derivativeReal[nMx] = 0.0;
            derivativeImaginary[nMx] = 0.0;
            for (int n = nMx; n >= 1; --n)
            {
                double rnYReal = n * inverseYReal, rnYImaginary = n * inverseYImaginary;
                double tReal = derivativeReal[n] + rnYReal, tImaginary = derivativeImaginary[n] + rnYImaginary;
                double tModulusSquared = 1.0 / (tReal * tReal + tImaginary * tImaginary);
                derivativeReal[n - 1] = rnYReal - tReal * tModulusSquared;
                derivativeImaginary[n - 1] = rnYImaginary + tImaginary * tModulusSquared;
            }

            for (int j = 0; j < angleCount; ++j)
            {
                s1Real[j] = 0.0;
                s1Imaginary[j] = 0.0;
                s2Real[j] = 0.0;
                s2Imaginary[j] = 0.0;
                piPrevious[j] = 0.0;
                piCurrent[j] = 1.0;
            }

            // Riccati-Bessel functions by upward recurrence
            double psiPrevious = Math.Cos(x), psi = Math.Sin(x);
            double chiPrevious = -Math.Sin(x), chi = Math.Cos(x);
            double xiPreviousReal = psi, xiPreviousImaginary = -chi;
            double sumScattering = 0.0, sumExtinction = 0.0, sumAsymmetry = 0.0;
            double aPreviousReal = 0.0, aPreviousImaginary = 0.0, bPreviousReal = 0.0, bPreviousImaginary = 0.0;
            double inverseM2 = 1.0 / (mReal * mReal + mImaginary * mImaginary);

            for (int n = 1; n <= nStop; ++n)
            {
                double psiNext = (2.0 * n - 1.0) * psi / x - psiPrevious;
                double chiNext = (2.0 * n - 1.0) * chi / x - chiPrevious;
                double xiReal = psiNext, xiImaginary = -chiNext;

                // D_n / m and m D_n
                double dReal = derivativeReal[n], dImaginary = derivativeImaginary[n];
                double dOverMReal = (dReal * mReal + dImaginary * mImaginary) * inverseM2;
                double dOverMImaginary = (dImaginary * mReal - dReal * mImaginary) * inverseM2;
                double mDReal = mReal * dReal - mImaginary * dImaginary;
                double mDImaginary = mReal * dImaginary + mImaginary * dReal;
                double nOverX = n / x;

                // a_n = ((D/m + n/x) psi_n - psi_{n-1}) / ((D/m + n/x) xi_n - xi_{n-1})
                double aNumeratorReal = (dOverMReal + nOverX) * psiNext - psi;
                double aNumeratorImaginary = dOverMImaginary * psiNext;
                double aDenominatorReal = (dOverMReal + nOverX) * xiReal - dOverMImaginary * xiImaginary - xiPreviousReal;
                double aDenominatorImaginary = (dOverMReal + nOverX) * xiImaginary + dOverMImaginary * xiReal - xiPreviousImaginary;
                double aScale = 1.0 / (aDenominatorReal * aDenominatorReal + aDenominatorImaginary * aDenominatorImaginary);
                double aReal = (aNumeratorReal * aDenominatorReal + aNumeratorImaginary * aDenominatorImaginary) * aScale;
                double aImaginary = (aNumeratorImaginary * aDenominatorReal - aNumeratorReal * aDenominatorImaginary) * aScale;

                // b_n = ((m D + n/x) psi_n - psi_{n-1}) / ((m D + n/x) xi_n - xi_{n-1})
                double bNumeratorReal = (mDReal + nOverX) * psiNext - psi;
                double bNumeratorImaginary = mDImaginary * psiNext;
                double bDenominatorReal = (mDReal + nOverX) * xiReal - mDImaginary * xiImaginary - xiPreviousReal;
                double bDenominatorImaginary = (mDReal + nOverX) * xiImaginary + mDImaginary * xiReal - xiPreviousImaginary;
                double bScale = 1.0 / (bDenominatorReal * bDenominatorReal + bDenominatorImaginary * bDenominatorImaginary);
                double bReal = (bNumeratorReal * bDenominatorReal + bNumeratorImaginary * bDenominatorImaginary) * bScale;
                double bImaginary = (bNumeratorImaginary * bDenominatorReal - bNumeratorReal * bDenominatorImaginary) * bScale;

                sumScattering += (2.0 * n + 1.0) * (aReal * aReal + aImaginary * aImaginary + bReal * bReal + bImaginary * bImaginary);
                sumExtinction += (2.0 * n + 1.0) * (aReal + bReal);
                if (n > 1)
                {
                    sumAsymmetry += (n - 1.0) * (n + 1.0) / n * (aPreviousReal * aReal + aPreviousImaginary * aImaginary +
                                                                   bPreviousReal * bReal + bPreviousImaginary * bImaginary) +
                                    (2.0 * n - 1.0) / ((n - 1.0) * n) * (aPreviousReal * bPreviousReal + aPreviousImaginary * bPreviousImaginary);
                }

                // The angular functions pi_n and tau_n and the amplitude functions
                double factor = (2.0 * n + 1.0) / (n * (n + 1.0));
                for (int j = 0; j < angleCount; ++j)
                {
                    double mu = cosines[j];
                    double pi = piCurrent[j];
                    double tau = n * mu * pi - (n + 1.0) * piPrevious[j];
                    s1Real[j] += factor * (aReal * pi + bReal * tau);
                    s1Imaginary[j] += factor * (aImaginary * pi + bImaginary * tau);
                    s2Real[j] += factor * (aReal * tau + bReal * pi);
                    s2Imaginary[j] += factor * (aImaginary * tau + bImaginary * pi);
                    double piNext = ((2.0 * n + 1.0) * mu * pi - (n + 1.0) * piPrevious[j]) / n;
                    piPrevious[j] = pi;
                    piCurrent[j] = piNext;
                }

                psiPrevious = psi;
                psi = psiNext;
                chiPrevious = chi;
                chi = chiNext;
                xiPreviousReal = xiReal;
                xiPreviousImaginary = xiImaginary;
                aPreviousReal = aReal;
                aPreviousImaginary = aImaginary;
                bPreviousReal = bReal;
                bPreviousImaginary = bImaginary;
            }

            efficiencyScattering = 2.0 / (x * x) * sumScattering;
            efficiencyExtinction = 2.0 / (x * x) * sumExtinction;
            asymmetry = sumScattering > 0.0 ? 2.0 * sumAsymmetry / sumScattering : 0.0;
        }
    }

    // MieSpecies Definition
    //
    // The optical properties of one aerosol species (a log-normal size distribution of spheres of one refractive index) at the wavelengths
    // of a table, with the angular tables of the Mueller matrix and the cumulative distribution of the angle for sampling.
    public sealed class MieSpecies
    {
        public readonly double WavelengthStart, WavelengthStep;
        public readonly int WavelengthCount;
        public readonly double[] MassExtinction, Albedo, Asymmetry;   // per wavelength; m^2/g
        public readonly double[] Angles;                              // radians, ascending from 0 to pi
        public readonly double[][] P11, P12, P33, P34;                // [wavelength][angle], per steradian, integral of P11 is one
        readonly double[][] cumulative;                               // [wavelength][angle]: integral of 2 pi sin(theta) P11 up to the node
        readonly double[] sinAngles, cosAngles;

        // The angles of the tables in degrees: fine and geometric near the forward direction, where the lobe of large particles is narrow
        public static double[] DefaultAngles()
        {
            System.Collections.Generic.List<double> degrees = new System.Collections.Generic.List<double>();
            degrees.Add(0.0);
            int geometric = 40;
            for (int i = 0; i < geometric; ++i)
            {
                degrees.Add(0.01 * Math.Pow(0.5 / 0.01, (double)i / (geometric - 1)));
            }
            for (double d = 0.55; d < 2.0 - 1e-9; d += 0.05)
            {
                degrees.Add(d);
            }
            for (double d = 2.0; d < 180.0 - 1e-9; d += 0.25)
            {
                degrees.Add(d);
            }
            degrees.Add(180.0);
            double[] radians = new double[degrees.Count];
            for (int i = 0; i < radians.Length; ++i)
            {
                radians[i] = degrees[i] * Math.PI / 180.0;
            }
            return radians;
        }

        // Log-normal size distribution (radii in micrometres, density in g/cm^3); the integral over the radius has radiusCount points
        public MieSpecies(double[] refractiveWavelength, double[] refractiveReal, double[] refractiveImaginary, double radiusMinimum,
                          double radiusMaximum, double radiusMode, double sigma, double density, double wavelengthStart, double wavelengthStep,
                          int wavelengthCount, int radiusCount)
            : this(refractiveWavelength, refractiveReal, refractiveImaginary, LogarithmicRadii(radiusMinimum, radiusMaximum, radiusCount),
                   LogNormalWeights(radiusMinimum, radiusMaximum, radiusMode, sigma, radiusCount), density, wavelengthStart, wavelengthStep,
                   wavelengthCount)
        {
        }

        // The radii (micrometres) equally spaced in the logarithm between the two limits
        public static double[] LogarithmicRadii(double radiusMinimum, double radiusMaximum, int radiusCount)
        {
            double[] radii = new double[radiusCount];
            double logMinimum = Math.Log(radiusMinimum), logMaximum = Math.Log(radiusMaximum);
            for (int i = 0; i < radiusCount; ++i)
            {
                radii[i] = Math.Exp(logMinimum + (logMaximum - logMinimum) * i / (radiusCount - 1));
            }
            return radii;
        }

        // The number of particles per unit of the logarithm of the radius (up to a common factor) of a log-normal distribution
        public static double[] LogNormalWeights(double radiusMinimum, double radiusMaximum, double radiusMode, double sigma, int radiusCount)
        {
            double[] radii = LogarithmicRadii(radiusMinimum, radiusMaximum, radiusCount);
            double[] weights = new double[radiusCount];
            double logMode = Math.Log(radiusMode), logSigma = Math.Log(sigma);
            for (int i = 0; i < radiusCount; ++i)
            {
                double z = (Math.Log(radii[i]) - logMode) / logSigma;
                weights[i] = Math.Exp(-0.5 * z * z);
            }
            return weights;
        }

        // The same for the gamma distribution of Hansen and Travis, n(r) ~ r^((1 - 3 b) / b) exp(-r / (a b)) with the effective radius a and the
        // effective variance b (the density per unit of the logarithm of the radius carries one more factor r), used for cloud droplets
        public static double[] GammaWeights(double radiusMinimum, double radiusMaximum, double effectiveRadius, double effectiveVariance, int radiusCount)
        {
            double[] radii = LogarithmicRadii(radiusMinimum, radiusMaximum, radiusCount);
            double[] weights = new double[radiusCount];
            for (int i = 0; i < radiusCount; ++i)
            {
                double ratio = radii[i] / effectiveRadius;
                weights[i] = Math.Exp(((1.0 - 2.0 * effectiveVariance) / effectiveVariance) * Math.Log(ratio) - ratio / effectiveVariance);
            }
            return weights;
        }

        // Computes the species from the refractive index table (wavelength in nm, real part, imaginary part >= 0, interpolated linearly and
        // constant outside) and the particle radii (micrometres, equally spaced in the logarithm) with their weights per unit of the logarithm
        // of the radius, at the wavelengths start, start + step, ... (nm); density in g/cm^3
        public MieSpecies(double[] refractiveWavelength, double[] refractiveReal, double[] refractiveImaginary, double[] radii, double[] weights,
                          double density, double wavelengthStart, double wavelengthStep, int wavelengthCount)
        {
            int radiusCount = radii.Length;
            WavelengthStart = wavelengthStart;
            WavelengthStep = wavelengthStep;
            WavelengthCount = wavelengthCount;
            Angles = DefaultAngles();
            int angleCount = Angles.Length;
            double[] cosines = new double[angleCount];
            sinAngles = new double[angleCount];
            cosAngles = new double[angleCount];
            for (int j = 0; j < angleCount; ++j)
            {
                cosAngles[j] = Math.Cos(Angles[j]);
                sinAngles[j] = Math.Sin(Angles[j]);
                cosines[j] = cosAngles[j];
            }
            cosines[0] = 1.0;
            cosines[angleCount - 1] = -1.0;
            MassExtinction = new double[wavelengthCount];
            Albedo = new double[wavelengthCount];
            Asymmetry = new double[wavelengthCount];
            P11 = new double[wavelengthCount][];
            P12 = new double[wavelengthCount][];
            P33 = new double[wavelengthCount][];
            P34 = new double[wavelengthCount][];
            cumulative = new double[wavelengthCount][];

            double volumeSum = 0.0, weightedAreaMaximum = 0.0;
            for (int i = 0; i < radiusCount; ++i)
            {
                volumeSum += weights[i] * 4.0 / 3.0 * Math.PI * radii[i] * radii[i] * radii[i];
                weightedAreaMaximum = Math.Max(weightedAreaMaximum, weights[i] * Math.PI * radii[i] * radii[i]);
            }

            Parallel.For(0, wavelengthCount, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, (int w) =>
            {
                MieSolver solver = new MieSolver();
                double lambdaNm = wavelengthStart + w * wavelengthStep;
                double lambdaMicrons = lambdaNm * 1e-3;
                double mReal = Interpolate(refractiveWavelength, refractiveReal, lambdaNm);
                double mImaginary = Interpolate(refractiveWavelength, refractiveImaginary, lambdaNm);
                double[] s1Real = new double[angleCount], s1Imaginary = new double[angleCount];
                double[] s2Real = new double[angleCount], s2Imaginary = new double[angleCount];
                double[] sum11 = new double[angleCount], sum12 = new double[angleCount], sum33 = new double[angleCount], sum34 = new double[angleCount];
                double extinctionSum = 0.0, scatteringSum = 0.0, asymmetrySum = 0.0;
                for (int i = 0; i < radiusCount; ++i)
                {
                    double x = 2.0 * Math.PI * radii[i] / lambdaMicrons;
                    double area = Math.PI * radii[i] * radii[i];
                    // Radii that contribute less than 1e-9 of the largest cross section get the efficiencies only
                    bool significant = weights[i] * area > 1e-9 * weightedAreaMaximum;
                    double efficiencyExtinction, efficiencyScattering, asymmetry;
                    solver.Sphere(x, mReal, mImaginary, significant ? cosines : null, s1Real, s1Imaginary, s2Real, s2Imaginary,
                                  out efficiencyExtinction, out efficiencyScattering, out asymmetry);
                    extinctionSum += weights[i] * efficiencyExtinction * area;
                    scatteringSum += weights[i] * efficiencyScattering * area;
                    asymmetrySum += weights[i] * efficiencyScattering * asymmetry * area;
                    if (significant)
                    {
                        for (int j = 0; j < angleCount; ++j)
                        {
                            double s1Squared = s1Real[j] * s1Real[j] + s1Imaginary[j] * s1Imaginary[j];
                            double s2Squared = s2Real[j] * s2Real[j] + s2Imaginary[j] * s2Imaginary[j];
                            // S2 S1* = (s2r s1r + s2i s1i) + i (s2i s1r - s2r s1i)
                            double productReal = s2Real[j] * s1Real[j] + s2Imaginary[j] * s1Imaginary[j];
                            double productImaginary = s2Imaginary[j] * s1Real[j] - s2Real[j] * s1Imaginary[j];
                            sum11[j] += weights[i] * 0.5 * (s2Squared + s1Squared);
                            sum12[j] += weights[i] * 0.5 * (s2Squared - s1Squared);
                            sum33[j] += weights[i] * productReal;
                            sum34[j] += weights[i] * productImaginary;
                        }
                    }
                }
                MassExtinction[w] = extinctionSum / (density * volumeSum);
                Albedo[w] = scatteringSum / extinctionSum;
                Asymmetry[w] = asymmetrySum / scatteringSum;

                // The phase matrix elements per steradian: S / (k^2 C_sca), then normalised so that the integral of P11 over the sphere is one
                // (the angular grid is finite: the difference to the exact normalisation is the quadrature error)
                double[] p11 = new double[angleCount], p12 = new double[angleCount], p33 = new double[angleCount], p34 = new double[angleCount];
                double cumulativeSum = 0.0;
                double[] running = new double[angleCount];
                for (int j = 0; j < angleCount; ++j)
                {
                    p11[j] = sum11[j];
                    p12[j] = sum12[j];
                    p33[j] = sum33[j];
                    p34[j] = sum34[j];
                }
                cumulativeSum = Integrate(p11, running);
                double normalisation = 1.0 / cumulativeSum;
                for (int j = 0; j < angleCount; ++j)
                {
                    p11[j] *= normalisation;
                    p12[j] *= normalisation;
                    p33[j] *= normalisation;
                    p34[j] *= normalisation;
                    running[j] *= normalisation;
                }
                P11[w] = p11;
                P12[w] = p12;
                P33[w] = p33;
                P34[w] = p34;
                cumulative[w] = running;
            });
        }

        // The integral of 2 pi sin(theta) p(theta) over theta with p linear between the nodes: running[j] is the integral up to node j
        double Integrate(double[] p, double[] running)
        {
            running[0] = 0.0;
            for (int j = 0; j + 1 < Angles.Length; ++j)
            {
                running[j + 1] = running[j] + CellIntegral(p, j);
            }
            return running[Angles.Length - 1];
        }

        // The integral over the cell j of 2 pi sin(theta) p(theta) where p is the linear interpolation of the node values
        double CellIntegral(double[] p, int j)
        {
            double width = Angles[j + 1] - Angles[j];
            double slope = (p[j + 1] - p[j]) / width;
            return 2.0 * Math.PI * (-(p[j + 1] * cosAngles[j + 1] - p[j] * cosAngles[j]) + slope * (sinAngles[j + 1] - sinAngles[j]));
        }

        static double Interpolate(double[] xs, double[] ys, double x)
        {
            if (x <= xs[0])
            {
                return ys[0];
            }
            if (x >= xs[xs.Length - 1])
            {
                return ys[ys.Length - 1];
            }
            int i = 0;
            while (xs[i + 1] <= x)
            {
                ++i;
            }
            double f = (x - xs[i]) / (xs[i + 1] - xs[i]);
            return ys[i] * (1.0 - f) + ys[i + 1] * f;
        }

        // The position of a wavelength in the table: the lower node and the weight of the upper one (clamped to the table)
        // A per-wavelength quantity of the species (MassExtinction, Albedo, Asymmetry) linearly interpolated at the wavelength
        public double At(double[] values, double lambdaNm)
        {
            int node;
            double fraction;
            Locate(lambdaNm, out node, out fraction);
            return node + 1 < values.Length ? (1.0 - fraction) * values[node] + fraction * values[node + 1] : values[node];
        }

        public void Locate(double lambdaNm, out int node, out double fraction)
        {
            double t = (lambdaNm - WavelengthStart) / WavelengthStep;
            if (t <= 0.0)
            {
                node = 0;
                fraction = 0.0;
                return;
            }
            node = (int)t;
            if (node >= WavelengthCount - 1)
            {
                node = WavelengthCount - 2;
                fraction = 1.0;
                return;
            }
            fraction = t - node;
        }

        // The cell of the angle table that contains theta and the position inside it
        public int FindCell(double theta, out double fraction)
        {
            int low = 0, high = Angles.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) >> 1;
                if (Angles[middle] <= theta)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            fraction = (theta - Angles[low]) / (Angles[low + 1] - Angles[low]);
            return low;
        }

        // The interpolated element (an array [wavelength][angle]) at the table node and the position inside the cell
        public static double Lookup(double[][] table, int node, double nodeFraction, int cell, double cellFraction)
        {
            double[] lower = table[node], upper = table[node + 1];
            double atLower = lower[cell] * (1.0 - cellFraction) + lower[cell + 1] * cellFraction;
            double atUpper = upper[cell] * (1.0 - cellFraction) + upper[cell + 1] * cellFraction;
            return atLower * (1.0 - nodeFraction) + atUpper * nodeFraction;
        }

        // Samples the scattering angle from the phase function of one table node (a wavelength of the table): returns theta and the density
        // of the sample per steradian, which is exactly the interpolated P11 that Lookup() returns for that node
        public double SampleAngle(int node, double u, out double pdfPerSteradian)
        {
            double[] running = cumulative[node];
            int low = 0, high = Angles.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) >> 1;
                if (running[middle] <= u)
                {
                    low = middle;
                }
                else
                {
                    high = middle;
                }
            }
            double[] p = P11[node];
            double remaining = u - running[low];
            double theta0 = Angles[low], width = Angles[low + 1] - Angles[low];
            double slope = (p[low + 1] - p[low]) / width;
            // Solve the integral from theta0 to theta of 2 pi sin(t) (p0 + slope (t - theta0)) dt = remaining by bisection (the integrand is >= 0)
            double lowT = 0.0, highT = width;
            double g0 = -(p[low] * cosAngles[low]);
            for (int iteration = 0; iteration < 60; ++iteration)
            {
                double t = 0.5 * (lowT + highT);
                double theta = theta0 + t;
                double value = p[low] + slope * t;
                double integral = 2.0 * Math.PI * (-(value * Math.Cos(theta)) + slope * Math.Sin(theta) - g0 - slope * sinAngles[low]);
                if (integral < remaining)
                {
                    lowT = t;
                }
                else
                {
                    highT = t;
                }
            }
            double thetaSample = theta0 + 0.5 * (lowT + highT);
            pdfPerSteradian = Math.Max(p[low] + slope * (thetaSample - theta0), 0.0);
            return thetaSample;
        }
    }

}  // namespace pbrt
