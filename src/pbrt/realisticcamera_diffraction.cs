// (not in pbrt-v4) realisticcamera_diffraction.cs
//
// Diffraction at the aperture of the realistic camera. The image is the geometric image (what the ray tracing of the lens gives)
// convolved with the point spread function of diffraction at the pupil of the film point:
//   h(xi) = |FT of the pupil function|^2,  xi the displacement in the film plane,
// the Fraunhofer integral in the direction cosines (sx, sy) of the rays that leave the film point,
//   U(xi) = integral P(s) exp(-i k s . xi) d^2 s.
// The pupil function P is 1 where a ray of the film point gets through the lens (the traced pupil: the stop, its image if there
// is one, and the vignetting of the other elements) and 0 elsewhere. The point spread function of a wavelength is that of any
// other, scaled: h_lambda(xi) = (lambda0 / lambda)^2 h_lambda0(xi lambda0 / lambda). The aberrations of the lens are not part of
// P: the traced rays already carry them, and a kernel with the wavefront error would count them twice.
using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe partial class RealisticCamera
    {
        // The film points (in each direction of the film) with a point spread function; in between the density is the bilinear
        // mixture of the four around the point
        const int DiffractionFieldPoints = 7;
        // The samples of the pupil in each direction, and the size of the Fourier transform (the point spread function has
        // DiffractionResolution^2 cells)
        const int DiffractionPupilSamples = 96;
        const int DiffractionResolution = 512;
        // The wavelength of the tables, in nanometres
        const Float DiffractionWavelength = 550;

        sealed class DiffractionTable
        {
            // The cumulative distribution of the cells of the point spread function (row by row)
            public float[] cumulative;
            // The width of a cell in metres at the wavelength of the table
            public double cellSize;
            // The area of the pupil in the direction cosines, and the sine of the circle of the same area
            public double pupilArea;
        }

        bool diffraction;
        DiffractionTable[,] diffractionTables;

        public override bool HasDiffraction()
        {
            return diffraction;
        }

        // The Fourier transform of n values (n a power of two)
        static void Fft1D(double[] re, double[] im, int n)
        {
            for (int i = 1, j = 0; i < n; ++i)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;
                if (i < j)
                {
                    double t = re[i];
                    re[i] = re[j];
                    re[j] = t;
                    t = im[i];
                    im[i] = im[j];
                    im[j] = t;
                }
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2 * Math.PI / length;
                double wr = Math.Cos(angle);
                double wi = Math.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    double cr = 1;
                    double ci = 0;
                    for (int k = 0; k < length / 2; ++k)
                    {
                        int a = start + k;
                        int b = a + length / 2;
                        double tr = re[b] * cr - im[b] * ci;
                        double ti = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - tr;
                        im[b] = im[a] - ti;
                        re[a] += tr;
                        im[a] += ti;
                        double next = cr * wr - ci * wi;
                        ci = cr * wi + ci * wr;
                        cr = next;
                    }
                }
            }
        }

        // The table of the film point pFilm (camera space), null if no ray of the point gets through the lens
        DiffractionTable BuildDiffractionTable(Point3f pFilm)
        {
            Float rearZ = LensRearZ();
            Vector3f toRear = new Vector3f(-pFilm.x, -pFilm.y, rearZ);
            double distance = Math.Sqrt(toRear.x * toRear.x + toRear.y * toRear.y + toRear.z * toRear.z);
            double centreX = toRear.x / distance;
            double centreY = toRear.y / distance;
            double half = 1.6 * RearElementRadius() / rearZ;

            // The box of the direction cosines of the rays that get through
            const int coarse = 128;
            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;
            for (int b = 0; b < coarse; ++b)
            {
                for (int a = 0; a < coarse; ++a)
                {
                    double sx = centreX - half + 2 * half * (a + 0.5) / coarse;
                    double sy = centreY - half + 2 * half * (b + 0.5) / coarse;
                    if (sx * sx + sy * sy >= 1)
                    {
                        continue;
                    }

                    Ray ray = new Ray(pFilm, new Vector3f((Float)sx, (Float)sy, (Float)Math.Sqrt(1 - sx * sx - sy * sy)));
                    if (TraceLensesFromFilm(ray, null) != 0)
                    {
                        minX = Math.Min(minX, sx);
                        maxX = Math.Max(maxX, sx);
                        minY = Math.Min(minY, sy);
                        maxY = Math.Max(maxY, sy);
                    }
                }
            }

            if (minX > maxX)
            {
                return null;
            }

            double cell = 2 * half / coarse;
            double span = Math.Max(maxX - minX, maxY - minY) + 4 * cell;
            double deltaS = span / DiffractionPupilSamples;
            double originX = (minX + maxX) / 2 - span / 2;
            double originY = (minY + maxY) / 2 - span / 2;

            int m = DiffractionResolution;
            double[] re = new double[m * m];
            double[] im = new double[m * m];
            int inside = 0;
            for (int b = 0; b < DiffractionPupilSamples; ++b)
            {
                for (int a = 0; a < DiffractionPupilSamples; ++a)
                {
                    double sx = originX + (a + 0.5) * deltaS;
                    double sy = originY + (b + 0.5) * deltaS;
                    if (sx * sx + sy * sy >= 1)
                    {
                        continue;
                    }

                    Ray ray = new Ray(pFilm, new Vector3f((Float)sx, (Float)sy, (Float)Math.Sqrt(1 - sx * sx - sy * sy)));
                    Float weight = TraceLensesFromFilm(ray, null);
                    if (weight > 0)
                    {
                        re[b * m + a] = Math.Sqrt(weight);
                        ++inside;
                    }
                }
            }

            double[] rowRe = new double[m];
            double[] rowIm = new double[m];
            for (int y = 0; y < m; ++y)
            {
                Array.Copy(re, y * m, rowRe, 0, m);
                Array.Copy(im, y * m, rowIm, 0, m);
                Fft1D(rowRe, rowIm, m);
                Array.Copy(rowRe, 0, re, y * m, m);
                Array.Copy(rowIm, 0, im, y * m, m);
            }

            for (int x = 0; x < m; ++x)
            {
                for (int y = 0; y < m; ++y)
                {
                    rowRe[y] = re[y * m + x];
                    rowIm[y] = im[y * m + x];
                }

                Fft1D(rowRe, rowIm, m);
                for (int y = 0; y < m; ++y)
                {
                    re[y * m + x] = rowRe[y];
                    im[y * m + x] = rowIm[y];
                }
            }

            // The intensity, with the zero frequency in the middle, as a cumulative distribution
            double[] intensity = new double[m * m];
            double total = 0;
            for (int y = 0; y < m; ++y)
            {
                for (int x = 0; x < m; ++x)
                {
                    int shiftedX = (x + m / 2) % m;
                    int shiftedY = (y + m / 2) % m;
                    double value = re[y * m + x] * re[y * m + x] + im[y * m + x] * im[y * m + x];
                    intensity[shiftedY * m + shiftedX] = value;
                    total += value;
                }
            }

            float[] cumulative = new float[m * m];
            double sum = 0;
            for (int i = 0; i < m * m; ++i)
            {
                sum += intensity[i];
                cumulative[i] = (float)(sum / total);
            }

            cumulative[m * m - 1] = 1;
            return new DiffractionTable
            {
                cumulative = cumulative,
                cellSize = DiffractionWavelength * 1e-9 / (m * deltaS),
                pupilArea = inside * deltaS * deltaS
            };
        }

        void BuildDiffractionTables()
        {
            int n = DiffractionFieldPoints;
            diffractionTables = new DiffractionTable[n, n];
            pbrt.ParallelFor(0, n * n, (int index) =>
            {
                int i = index % n;
                int j = index / n;
                Point2f s = new Point2f((Float)i / (n - 1), (Float)j / (n - 1));
                Point2f pFilm2 = physicalExtent.Lerp(s);
                diffractionTables[i, j] = BuildDiffractionTable(new Point3f(-pFilm2.x, pFilm2.y, 0));
            });
        }

        // The displacement on the film (in pixels) of a sample of the point spread function of the film point pRaster for light of
        // a wavelength in nanometres; false (no displacement) where no ray of the film point gets through the lens. uSelect
        // chooses among the four tables around the point, uCell the cell of the table, uInCell the point in the cell.
        public override bool SampleDiffractionOffset(Point2f pRaster, Float wavelength, Float uSelect, Float uCell, Point2f uInCell,
                                                     out Vector2f offsetRaster)
        {
            offsetRaster = new Vector2f(0, 0);
            if (!diffraction)
            {
                return false;
            }

            int n = DiffractionFieldPoints;
            Float sx = pbrt.Clamp(pRaster.x / film.FullResolution().x, 0, 1) * (n - 1);
            Float sy = pbrt.Clamp(pRaster.y / film.FullResolution().y, 0, 1) * (n - 1);
            int i0 = std.min((int)sx, n - 2);
            int j0 = std.min((int)sy, n - 2);
            Float tx = sx - i0;
            Float ty = sy - j0;
            Float w00 = (1 - tx) * (1 - ty);
            Float w10 = tx * (1 - ty);
            Float w01 = (1 - tx) * ty;
            int i = i0;
            int j = j0;
            if (uSelect >= w00 + w10 + w01)
            {
                ++i;
                ++j;
            }
            else if (uSelect >= w00 + w10)
            {
                ++j;
            }
            else if (uSelect >= w00)
            {
                ++i;
            }

            DiffractionTable table = diffractionTables[i, j];
            if (table == null)
            {
                return false;
            }

            // The cell: the first with a cumulative probability above uCell
            float[] cumulative = table.cumulative;
            int low = 0;
            int high = cumulative.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (cumulative[middle] > uCell)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            int m = DiffractionResolution;
            double scale = table.cellSize * wavelength / DiffractionWavelength;
            double xi = (low % m - m / 2 + uInCell.x - 0.5) * scale;
            double eta = (low / m - m / 2 + uInCell.y - 0.5) * scale;
            offsetRaster = new Vector2f((Float)(-xi / (physicalExtent.pMax.x - physicalExtent.pMin.x) * film.FullResolution().x),
                                        (Float)(eta / (physicalExtent.pMax.y - physicalExtent.pMin.y) * film.FullResolution().y));
            return true;
        }

        // (for the tests) The area of the pupil of a table in the direction cosines and the width of its cells in metres (at
        // 550 nm); false where the table is empty
        public bool DiffractionTableInfo(int i, int j, out double pupilArea, out double cellSize)
        {
            pupilArea = 0;
            cellSize = 0;
            if (diffractionTables == null || diffractionTables[i, j] == null)
            {
                return false;
            }

            pupilArea = diffractionTables[i, j].pupilArea;
            cellSize = diffractionTables[i, j].cellSize;
            return true;
        }
    }
}
