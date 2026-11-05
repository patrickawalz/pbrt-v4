// (not in pbrt-v4) patchsampling.cs
//
// Sampling the surface of a patch (a subdivision patch, a curved patch, a displaced patch) by area, for area lights. The surface is a map from the parameters
// (u, v) of the patch, so a sample drawn with the density p_uv has the area density p_uv / J with J = |dP/du x dP/dv|, as in Section 3.2 and Section 7 of Xu,
// Zhou, Budge "Geometric Queries on Tessellation-Free Displaced Surfaces". The parameters are drawn from a piecewise constant density over a grid of cells,
// proportional to the area estimated at the center of each cell (the descent through the cells of the hierarchy of Section 7.1 flattened into one table);
// the estimate only decides where the samples go, the Jacobian J at the sampled point makes the density exact, so the sampling is unbiased.

using System;

namespace pbrt
{
    public delegate SdfVector PatchPositionFunction(double u, double v);

    public sealed class PatchAreaSampler
    {
        // position: the point of the surface at the parameters (u, v) in render space; resolution: the cells along each axis
        public PatchAreaSampler(PatchPositionFunction position, int resolution)
        {
            this.position = position;
            this.resolution = Math.Max(1, resolution);
            int cells = this.resolution * this.resolution;
            double[] weights = new double[cells];
            double sum = 0;
            for (int j = 0; j < this.resolution; ++j)
            {
                for (int i = 0; i < this.resolution; ++i)
                {
                    double weight = Jacobian((i + 0.5) / this.resolution, (j + 0.5) / this.resolution);
                    weights[j * this.resolution + i] = weight;
                    sum += weight;
                }
            }
            area = sum / cells;
            if (!(sum > 0))
            {
                // A surface of no area: the density of the parameters is uniform
                for (int k = 0; k < cells; ++k)
                {
                    weights[k] = 1;
                }
                sum = cells;
            }
            // Every cell gets a share, so that every point can be sampled (the density stays positive where the estimate at the center is small)
            double floor = sum * 1e-3 / cells;
            cumulative = new double[cells];
            double running = 0;
            for (int k = 0; k < cells; ++k)
            {
                running += Math.Max(weights[k], floor);
                cumulative[k] = running;
            }
            total = running;
        }

        // The area of the surface
        public double Area
        {
            get { return area; }
        }

        // |dP/du x dP/dv| at (u, v) from central differences of the position
        public double Jacobian(double u, double v)
        {
            Differences(u, v, out SdfVector pu, out SdfVector pv);
            return SdfVector.Cross(pu, pv).Length();
        }

        // The derivatives of the position at (u, v) by central differences (one-sided at the border of the patch)
        public void Differences(double u, double v, out SdfVector pu, out SdfVector pv)
        {
            const double h = 1e-4;
            double ua = Math.Max(0.0, u - h), ub = Math.Min(1.0, u + h);
            double va = Math.Max(0.0, v - h), vb = Math.Min(1.0, v + h);
            pu = (position(ub, v) - position(ua, v)) / (ub - ua);
            pv = (position(u, vb) - position(u, va)) / (vb - va);
        }

        // Draws (u, v) with the density p_uv of the parameters
        public void Sample(Point2f sample, out double u, out double v, out double density)
        {
            double target = sample.x * total;
            int low = 0, high = cumulative.Length - 1;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (cumulative[middle] > target)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            int cell = low;
            double start = cell == 0 ? 0 : cumulative[cell - 1];
            double share = cumulative[cell] - start;
            double remapped = share > 0 ? Math.Min((target - start) / share, 1 - 1e-12) : 0.5;
            int i = cell % resolution, j = cell / resolution;
            u = (i + remapped) / resolution;
            v = (j + Math.Min((double)sample.y, 1 - 1e-12)) / resolution;
            density = share / total * resolution * resolution;
        }

        // The density p_uv of the parameters at (u, v)
        public double Density(double u, double v)
        {
            int i = Math.Max(0, Math.Min(resolution - 1, (int)(u * resolution)));
            int j = Math.Max(0, Math.Min(resolution - 1, (int)(v * resolution)));
            int cell = j * resolution + i;
            double share = cumulative[cell] - (cell == 0 ? 0 : cumulative[cell - 1]);
            return share / total * resolution * resolution;
        }

        // The density in the measure of the area at (u, v): p_uv / J
        public double AreaDensity(double u, double v)
        {
            double jacobian = Jacobian(u, v);
            return jacobian > 0 ? Density(u, v) / jacobian : 0;
        }

        // The parameters (u, v) of the point of the surface that is nearest to the target: the best of a grid of 17 x 17 points, then Gauss-Newton steps.
        // Returns the distance to the surface point found.
        public double Locate(SdfVector target, out double u, out double v)
        {
            const int n = 16;
            double best = double.MaxValue;
            u = 0.5;
            v = 0.5;
            for (int j = 0; j <= n; ++j)
            {
                for (int i = 0; i <= n; ++i)
                {
                    double d = (position((double)i / n, (double)j / n) - target).Length();
                    if (d < best)
                    {
                        best = d;
                        u = (double)i / n;
                        v = (double)j / n;
                    }
                }
            }
            for (int iteration = 0; iteration < 40; ++iteration)
            {
                SdfVector r = position(u, v) - target;
                Differences(u, v, out SdfVector pu, out SdfVector pv);
                double a = SdfVector.Dot(pu, pu), b = SdfVector.Dot(pu, pv), c = SdfVector.Dot(pv, pv);
                double e = -SdfVector.Dot(pu, r), f = -SdfVector.Dot(pv, r);
                double det = a * c - b * b;
                if (!(det > 1e-30 * a * c))
                {
                    break;
                }
                double du = (c * e - b * f) / det, dv = (a * f - b * e) / det;
                double step = Math.Max(Math.Abs(du), Math.Abs(dv));
                if (step > 0.25)
                {
                    du *= 0.25 / step;
                    dv *= 0.25 / step;
                }
                u = Math.Min(1, Math.Max(0, u + du));
                v = Math.Min(1, Math.Max(0, v + dv));
                if (step < 1e-12)
                {
                    break;
                }
            }
            return (position(u, v) - target).Length();
        }

        private readonly PatchPositionFunction position;
        private readonly int resolution;
        private readonly double[] cumulative;
        private readonly double total;
        private readonly double area;
    }

    // The conversions from the area measure to the solid angle at a reference point that the shapes share (pbrt's fallback for shapes of no closed form)
    public static class PatchSampling
    {
        // The sample of the solid angle from the sample of the area
        public static pstd.optional<ShapeSample> ToSolidAngle(pstd.optional<ShapeSample> areaSample, ShapeSampleContext ctx)
        {
            if (!areaSample)
            {
                return default;
            }
            ShapeSample result = areaSample.value();
            result.intr.time = ctx.time;
            Vector3f wi = result.intr.p() - ctx.p();
            if (pbrt.LengthSquared(wi) == 0)
            {
                return default;
            }
            wi = pbrt.Normalize(wi);
            result.pdf /= pbrt.AbsDot(result.intr.n, -wi) / pbrt.DistanceSquared(ctx.p(), result.intr.p());
            if (pbrt.IsInf(result.pdf) || float.IsNaN(result.pdf))
            {
                return default;
            }
            return result;
        }

        // The density of the solid angle at the reference point of a point of the area density areaPdf
        public static Float AreaToSolidAngle(double areaPdf, Normal3f n, Point3f p, ShapeSampleContext ctx, Vector3f wi)
        {
            Float cosine = pbrt.AbsDot(n, -wi);
            if (cosine == 0)
            {
                return 0;
            }
            Float pdf = (Float)(areaPdf * pbrt.DistanceSquared(ctx.p(), p) / cosine);
            return pbrt.IsInf(pdf) || float.IsNaN(pdf) ? 0 : pdf;
        }
    }
}
