// (not in pbrt-v4) Next event estimation for the emission of a texture medium.
//
// The volpath integrator gathers the emission of a medium at every collision of a path (Huang et al. 2021 call this
// the "free-path" estimator). That strategy cannot find emission from small bright regions seen from a scattering
// vertex except by luck. This light is the other strategy: from a scattering vertex x it picks a point p of the medium
// with a probability density proportional to a bound of the emission, and estimates
//
//   integral over directions w of f(w) cos(theta) integral over t of T(x, x + t w) E(x + t w) dt dw
//     = integral over the volume of f cos T(x, p) E(p) / |p - x|^2 dp.
//
// It is reported as a light whose radiance is E(p) and whose density is pdf(p) |p - x|^2, which is a density over
// direction and distance. The shadow ray then estimates T(x, p) by ratio tracking, as for any other light, and the
// integrator combines the two strategies with the spectral MIS weights (see VolPathIntegrator).
//
// The density only needs to be positive wherever the emission is: the density of a voxel is proportional to the bound
// of the emission over it, and the point is uniform inside the voxel.

using System;
using System.Collections.Generic;

namespace pbrt
{
    public sealed unsafe class VolumeEmissionLight : LightBase
    {
        public VolumeEmissionLight(TextureMedium medium, int res)
            : base(LightType.Area, new Transform(), new MediumInterface(medium))
        {
            this.medium = medium;
            this.res = std.max(res, 1);
        }

        public TextureMedium Medium() { return medium; }

        // Whether the volumetric path tracer samples the emission along lines (see PdfDirection) and not at points
        public bool UsesFnee() { return fnee; }
        public void SetFnee(bool fnee) { this.fnee = fnee; }

        // The grid is built when the light is first used, because the textures of a medium are resolved after it is created
        private void EnsureBuilt()
        {
            if (cdf != null)
                return;
            lock (buildLock)
            {
                if (cdf != null)
                    return;
                Bounds3f bounds = medium.MediumBounds();
                Transform renderFromMedium = medium.RenderFromMedium();
                Vector3f ex = renderFromMedium.Apply(new Vector3f(bounds.pMax.x - bounds.pMin.x, 0, 0));
                Vector3f ey = renderFromMedium.Apply(new Vector3f(0, bounds.pMax.y - bounds.pMin.y, 0));
                Vector3f ez = renderFromMedium.Apply(new Vector3f(0, 0, bounds.pMax.z - bounds.pMin.z));
                Float renderVolume = std.abs(pbrt.Dot(pbrt.Cross(ex, ey), ez));
                voxelVolume = renderVolume / ((Float)res * res * res);
                Float[] weights = new Float[res * res * res];
                Float[] sums = new Float[res * res * res + 1];
                Float total = 0;
                for (int z = 0; z < res; ++z)
                {
                    for (int y = 0; y < res; ++y)
                    {
                        for (int x = 0; x < res; ++x)
                        {
                            Point3f p0 = new Point3f((Float)x / res, (Float)y / res, (Float)z / res);
                            Point3f p1 = new Point3f((Float)(x + 1) / res, (Float)(y + 1) / res, (Float)(z + 1) / res);
                            Bounds3f region = new Bounds3f(bounds.Lerp(p0), bounds.Lerp(p1));
                            Float w = std.max<Float>(0, medium.EmissionBound(region));
                            if (IsNaN(w) || IsInf(w))
                                pbrt.ErrorExit("The emission of a medium that is sampled as a light has a non-finite bound.");
                            int index = x + res * (y + res * z);
                            weights[index] = w;
                            total += w;
                            sums[index + 1] = total;
                        }
                    }
                }
                weight = weights;
                totalWeight = total;
                boundsMedium = bounds;
                cdf = sums;
            }
        }

        public bool HasEmission() { EnsureBuilt(); return totalWeight > 0; }

        // Probability density, with respect to volume in render space, of sampling the render-space point p
        public Float PdfPoint(Point3f p)
        {
            EnsureBuilt();
            if (!(totalWeight > 0))
                return 0;
            Vector3f o = boundsMedium.Offset(medium.RenderFromMedium().ApplyInverse(p));
            if (o.x < 0 || o.x >= 1 || o.y < 0 || o.y >= 1 || o.z < 0 || o.z >= 1)
                return 0;
            int x = std.min((int)(o.x * res), res - 1), y = std.min((int)(o.y * res), res - 1), z = std.min((int)(o.z * res), res - 1);
            return weight[x + res * (y + res * z)] / (totalWeight * voxelVolume);
        }

        // u.x picks the voxel and, once rescaled, the x coordinate inside it; u.y and uz are the other two coordinates
        private bool SamplePointInVolume(Point2f u, Float uz, out Point3f p, out Float pdfPoint)
        {
            p = default;
            pdfPoint = 0;
            EnsureBuilt();
            if (!(totalWeight > 0))
                return false;
            Float target = u.x * totalWeight;
            int lo = 0, hi = res * res * res;
            // The first voxel whose cumulative weight exceeds the target
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (cdf[mid + 1] <= target)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            int index = std.min(lo, res * res * res - 1);
            if (!(weight[index] > 0))
                return false;
            Float ux = std.min((target - cdf[index]) / weight[index], OneMinusEpsilon);
            int ix = index % res, iy = (index / res) % res, iz = index / (res * res);
            Point3f pMedium = boundsMedium.Lerp(new Point3f((ix + ux) / res, (iy + u.y) / res, (iz + uz) / res));
            p = medium.RenderFromMedium().Apply(pMedium);
            pdfPoint = weight[index] / (totalWeight * voxelVolume);
            return true;
        }

        // Forward next event estimation: a direction from x toward a point of the volume. The emission is then integrated
        // along the whole ray in that direction, so the density that counts is the one of the direction.
        public bool SampleDirection(Point3f x, Point2f u, Float uz, out Vector3f w, out Float pdf)
        {
            w = default;
            pdf = 0;
            if (!SamplePointInVolume(u, uz, out Point3f p, out Float pdfPoint))
                return false;
            Vector3f d = p - x;
            Float distance2 = pbrt.LengthSquared(d);
            if (!(distance2 > 0))
                return false;
            w = d / std.sqrt(distance2);
            pdf = PdfDirection(x, w);
            return pdf > 0;
        }

        // The density, per solid angle at x, of the directions that SampleDirection() picks: the density of the points
        // times t^2, integrated over the ray. The density is constant over a voxel, so each voxel that the ray crosses adds
        // its density times the integral of t^2 over the crossing.
        public Float PdfDirection(Point3f x, Vector3f w)
        {
            EnsureBuilt();
            if (!(totalWeight > 0))
                return 0;
            Transform renderFromMedium = medium.RenderFromMedium();
            // The ray in the units of the grid; the ray parameter is still the distance in render space
            Point3f oMedium = renderFromMedium.ApplyInverse(x);
            Vector3f dMedium = renderFromMedium.ApplyInverse(w);
            Float[] o = new Float[3], d = new Float[3];
            Float[] pMin = new Float[] { boundsMedium.pMin.x, boundsMedium.pMin.y, boundsMedium.pMin.z };
            Float[] pMax = new Float[] { boundsMedium.pMax.x, boundsMedium.pMax.y, boundsMedium.pMax.z };
            Float[] oWorld = new Float[] { oMedium.x, oMedium.y, oMedium.z };
            Float[] dWorld = new Float[] { dMedium.x, dMedium.y, dMedium.z };
            Float tEnter = 0, tExit = Infinity;
            for (int axis = 0; axis < 3; ++axis)
            {
                Float scale = res / (pMax[axis] - pMin[axis]);
                o[axis] = (oWorld[axis] - pMin[axis]) * scale;
                d[axis] = dWorld[axis] * scale;
                if (d[axis] == 0)
                {
                    if (o[axis] < 0 || o[axis] >= res)
                        return 0;
                    continue;
                }
                Float ta = (0 - o[axis]) / d[axis], tb = (res - o[axis]) / d[axis];
                if (ta > tb)
                {
                    Float swap = ta;
                    ta = tb;
                    tb = swap;
                }
                tEnter = std.max(tEnter, ta);
                tExit = std.min(tExit, tb);
                if (!(tEnter < tExit))
                    return 0;
            }

            int[] cell = new int[3], step = new int[3];
            Float[] tNext = new Float[3], tDelta = new Float[3];
            for (int axis = 0; axis < 3; ++axis)
            {
                Float position = o[axis] + d[axis] * tEnter;
                cell[axis] = std.min(std.max((int)Math.Floor(position), 0), res - 1);
                if (d[axis] > 0)
                {
                    step[axis] = 1;
                    tDelta[axis] = 1 / d[axis];
                    tNext[axis] = tEnter + (cell[axis] + 1 - position) / d[axis];
                }
                else if (d[axis] < 0)
                {
                    step[axis] = -1;
                    tDelta[axis] = -1 / d[axis];
                    tNext[axis] = tEnter + (cell[axis] - position) / d[axis];
                }
                else
                {
                    step[axis] = 0;
                    tDelta[axis] = Infinity;
                    tNext[axis] = Infinity;
                }
            }

            double sum = 0;
            Float t = tEnter;
            while (t < tExit)
            {
                int axisNext = tNext[0] < tNext[1] ? (tNext[0] < tNext[2] ? 0 : 2) : (tNext[1] < tNext[2] ? 1 : 2);
                Float tEndCell = std.min(tNext[axisNext], tExit);
                double w0 = weight[cell[0] + res * (cell[1] + res * cell[2])];
                if (w0 > 0)
                    sum += w0 * ((double)tEndCell * tEndCell * tEndCell - (double)t * t * t) / 3;
                t = tEndCell;
                cell[axisNext] += step[axisNext];
                if (cell[axisNext] < 0 || cell[axisNext] >= res)
                    break;
                tNext[axisNext] += tDelta[axisNext];
            }
            return (Float)(sum / ((double)totalWeight * voxelVolume));
        }

        public pstd.optional<LightLiSample> SampleLiVolume(LightSampleContext ctx, Point2f u, Float uz, SampledWavelengths lambda)
        {
            if (!SamplePointInVolume(u, uz, out Point3f p, out Float pdfPoint))
                return new pstd.optional<LightLiSample>();
            Vector3f d = p - ctx.p();
            Float distance2 = pbrt.LengthSquared(d);
            if (!(distance2 > 0))
                return new pstd.optional<LightLiSample>();
            Vector3f wi = d / std.sqrt(distance2);
            SampledSpectrum L = medium.EmissionAt(p, lambda);
            if (!L)
                return new pstd.optional<LightLiSample>();
            return new LightLiSample(L, wi, pdfPoint * distance2, new Interaction(p, new MediumInterface(medium)));
        }

        public override pstd.optional<LightLiSample> SampleLi(LightSampleContext ctx, Point2f u, SampledWavelengths lambda,
                                                              bool allowIncompletePDF = false)
        {
            // The third coordinate of the point comes from a hash of the other two when the caller has no more samples
            return SampleLiVolume(ctx, u, (Float)((uint)pbrt.Hash(u.x, u.y) * 2.3283064365386963e-10f), lambda);
        }

        // The emission is found by volume sampling only
        public override Float PDF_Li(LightSampleContext ctx, Vector3f wi, bool allowIncompletePDF = false) { return 0; }

        public override SampledSpectrum Phi(SampledWavelengths lambda)
        {
            EnsureBuilt();
            return new SampledSpectrum(4 * Pi * totalWeight * voxelVolume);
        }

        public override pstd.optional<LightBounds> Bounds()
        {
            EnsureBuilt();
            Transform renderFromMedium = medium.RenderFromMedium();
            Bounds3f b = new Bounds3f();
            for (int i = 0; i < 8; ++i)
                b = pbrt.Union(b, renderFromMedium.Apply(boundsMedium.Corner(i)));
            Float phi = 4 * Pi * totalWeight * voxelVolume;
            return new LightBounds(b, new Vector3f(0, 0, 1), phi, std.cos(Pi), std.cos(Pi / 2), false);
        }

        // The emission is the same in all directions: a point of the volume with the density PdfPoint(), and a direction that
        // is uniform over the sphere. The returned sample has no surface normal, so its cosine factor is 1. uz is the
        // third coordinate of the point.
        public pstd.optional<LightLeSample> SampleLeVolume(Point2f u1, Point2f u2, Float uz, SampledWavelengths lambda,
                                                           Float time)
        {
            if (!SamplePointInVolume(u1, uz, out Point3f p, out Float pdfPoint))
                return new pstd.optional<LightLeSample>();
            SampledSpectrum L = medium.EmissionAt(p, lambda);
            if (!L)
                return new pstd.optional<LightLeSample>();
            Vector3f w = pbrt.SampleUniformSphere(u2);
            return new LightLeSample(L, new Ray(p, w, time, medium), pdfPoint, pbrt.UniformSpherePDF());
        }

        public override pstd.optional<LightLeSample> SampleLe(Point2f u1, Point2f u2, ref SampledWavelengths lambda, Float time)
        {
            // The third coordinate of the point comes from a hash of the others when the caller has no more samples
            return SampleLeVolume(u1, u2, (Float)((uint)pbrt.Hash(u1.x, u1.y, u2.x) * 2.3283064365386963e-10f), lambda, time);
        }

        public override void PDF_Le(Ray ray, Float* pdfPos, Float* pdfDir)
        {
            *pdfPos = PdfPoint(ray.o);
            *pdfDir = pbrt.UniformSpherePDF();
        }

        public override string ToString() { return pbrt.StringPrintf("[ VolumeEmissionLight res: %d ]", res); }

        private TextureMedium medium;
        private int res;
        private bool fnee;
        private Float[] weight, cdf;
        private Float totalWeight, voxelVolume;
        private Bounds3f boundsMedium;
        private readonly object buildLock = new object();
    }
}
