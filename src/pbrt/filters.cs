// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// filters.h, filters.cpp

using System;

namespace pbrt
{

    // FilterSample Definition
    public struct FilterSample
    {
        public Point2f p;
        public Float weight;
        public FilterSample(Point2f p, Float weight) { this.p = p; this.weight = weight; }
    }

    public sealed unsafe class FilterSampler
    {
        // FilterSampler Public Methods
        public FilterSampler(Filter filter, Allocator alloc = null)
        {
            domain = new Bounds2f(new Point2f(-filter.Radius()), new Point2f(filter.Radius()));
            f = new Array2D<Float>((int)(32 * filter.Radius().x), (int)(32 * filter.Radius().y), alloc);
            distrib = new PiecewiseConstant2D(alloc);
            // Tabularize unnormalized filter function in _f_
            for (int y = 0; y < f.YSize(); ++y)
                for (int x = 0; x < f.XSize(); ++x)
                {
                    Point2f p = domain.Lerp(new Point2f((x + 0.5f) / f.XSize(), (y + 0.5f) / f.YSize()));
                    f[x, y] = filter.Evaluate(p);
                }

            // Compute sampling distribution for filter
            distrib = new PiecewiseConstant2D(f, domain, alloc);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FilterSampler domain: %s f: %s distrib: %s ]", domain, f, distrib);
        }

        public FilterSample Sample(Point2f u)
        {
            Float pdf;
            Point2i pi;
            Point2f p = distrib.Sample(u, &pdf, &pi);
            return new FilterSample(p, f[pi.x, pi.y] / pdf);
        }

        // FilterSampler Private Members
        private Bounds2f domain;
        private Array2D<Float> f;
        private PiecewiseConstant2D distrib;
    }

    // BoxFilter Definition
    public sealed unsafe class BoxFilter : Filter
    {
        // BoxFilter Public Methods
        public BoxFilter(Vector2f radius) { this.radius = radius; }
        public BoxFilter() : this(new Vector2f(0.5f, 0.5f)) { }

        public static BoxFilter Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float xw = parameters.GetOneFloat("xradius", 0.5f);
            Float yw = parameters.GetOneFloat("yradius", 0.5f);
            return new BoxFilter(new Vector2f(xw, yw));
        }

        public override Vector2f Radius() { return radius; }

        public override string ToString() { return pbrt.StringPrintf("[ BoxFilter radius: %s ]", radius); }

        public override Float Evaluate(Point2f p)
        {
            return (std.abs(p.x) <= radius.x && std.abs(p.y) <= radius.y) ? 1 : 0;
        }

        public override FilterSample Sample(Point2f u)
        {
            Point2f p = new Point2f(pbrt.Lerp(u[0], -radius.x, radius.x), pbrt.Lerp(u[1], -radius.y, radius.y));
            return new FilterSample(p, (Float)1);
        }

        public override Float Integral() { return 2 * radius.x * 2 * radius.y; }

        private Vector2f radius;
    }

    // GaussianFilter Definition
    public sealed unsafe class GaussianFilter : Filter
    {
        // GaussianFilter Public Methods
        public GaussianFilter(Vector2f radius, Float sigma = 0.5f, Allocator alloc = null)
        {
            this.radius = radius;
            this.sigma = sigma;
            expX = pbrt.Gaussian(radius.x, 0, sigma);
            expY = pbrt.Gaussian(radius.y, 0, sigma);
            sampler = new FilterSampler(this, alloc);
        }

        public static GaussianFilter Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            // Find common filter parameters
            Float xw = parameters.GetOneFloat("xradius", 1.5f);
            Float yw = parameters.GetOneFloat("yradius", 1.5f);
            Float sigma = parameters.GetOneFloat("sigma", 0.5f);  // equivalent to old alpha = 2
            return new GaussianFilter(new Vector2f(xw, yw), sigma, alloc);
        }

        public override Vector2f Radius() { return radius; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ GaussianFilter radius: %s sigma: %f expX: %f expY: %f sampler: %s ]", radius,
                                        sigma, expX, expY, sampler);
        }

        public override Float Evaluate(Point2f p)
        {
            return (std.max<Float>(0, pbrt.Gaussian(p.x, 0, sigma) - expX) *
                    std.max<Float>(0, pbrt.Gaussian(p.y, 0, sigma) - expY));
        }

        public override Float Integral()
        {
            return ((pbrt.GaussianIntegral(-radius.x, radius.x, 0, sigma) - 2 * radius.x * expX) *
                    (pbrt.GaussianIntegral(-radius.y, radius.y, 0, sigma) - 2 * radius.y * expY));
        }

        public override FilterSample Sample(Point2f u) { return sampler.Sample(u); }

        // GaussianFilter Private Members
        private Vector2f radius;
        private Float sigma, expX, expY;
        private FilterSampler sampler;
    }

    // MitchellFilter Definition
    public sealed unsafe class MitchellFilter : Filter
    {
        // MitchellFilter Public Methods
        public MitchellFilter(Vector2f radius, Float b = 1f / 3f, Float c = 1f / 3f, Allocator alloc = null)
        {
            this.radius = radius; this.b = b; this.c = c;
            sampler = new FilterSampler(this, alloc);
        }

        public static MitchellFilter Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            // Find common filter parameters
            Float xw = parameters.GetOneFloat("xradius", 2f);
            Float yw = parameters.GetOneFloat("yradius", 2f);
            Float B = parameters.GetOneFloat("B", 1f / 3f);
            Float C = parameters.GetOneFloat("C", 1f / 3f);
            return new MitchellFilter(new Vector2f(xw, yw), B, C, alloc);
        }

        public override Vector2f Radius() { return radius; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ MitchellFilter radius: %s b: %f c: %f sampler: %s ]", radius, b, c, sampler);
        }

        public override Float Evaluate(Point2f p)
        {
            return Mitchell1D(2 * p.x / radius.x) * Mitchell1D(2 * p.y / radius.y);
        }

        public override FilterSample Sample(Point2f u) { return sampler.Sample(u); }

        public override Float Integral() { return radius.x * radius.y / 4; }

        // MitchellFilter Private Methods
        private Float Mitchell1D(Float x)
        {
            x = std.abs(x);
            if (x <= 1)
                return ((12 - 9 * b - 6 * c) * x * x * x + (-18 + 12 * b + 6 * c) * x * x + (6 - 2 * b)) * (1f / 6f);
            else if (x <= 2)
                return ((-b - 6 * c) * x * x * x + (6 * b + 30 * c) * x * x + (-12 * b - 48 * c) * x + (8 * b + 24 * c)) *
                       (1f / 6f);
            else
                return 0;
        }

        // MitchellFilter Private Members
        private Vector2f radius;
        private Float b, c;
        private FilterSampler sampler;
    }

    // LanczosSincFilter Definition
    public sealed unsafe class LanczosSincFilter : Filter
    {
        // LanczosSincFilter Public Methods
        public LanczosSincFilter(Vector2f radius, Float tau = 3f, Allocator alloc = null)
        {
            this.radius = radius; this.tau = tau;
            sampler = new FilterSampler(this, alloc);
        }

        public static LanczosSincFilter Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Float xw = parameters.GetOneFloat("xradius", 4f);
            Float yw = parameters.GetOneFloat("yradius", 4f);
            Float tau = parameters.GetOneFloat("tau", 3f);
            return new LanczosSincFilter(new Vector2f(xw, yw), tau, alloc);
        }

        public override Vector2f Radius() { return radius; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ LanczosSincFilter radius: %s tau: %f sampler: %s ]", radius, tau, sampler);
        }

        public override Float Evaluate(Point2f p)
        {
            return pbrt.WindowedSinc(p.x, radius.x, tau) * pbrt.WindowedSinc(p.y, radius.y, tau);
        }

        public override FilterSample Sample(Point2f u) { return sampler.Sample(u); }

        // Sinc Filter Method Definitions
        public override Float Integral()
        {
            Float sum = 0;
            int sqrtSamples = 64;
            int nSamples = sqrtSamples * sqrtSamples;
            Float area = 2 * radius.x * 2 * radius.y;
            RNG rng = new RNG();
            for (int y = 0; y < sqrtSamples; ++y)
            {
                for (int x = 0; x < sqrtSamples; ++x)
                {
                    Point2f u = new Point2f((x + rng.Uniform<Float>()) / sqrtSamples, (y + rng.Uniform<Float>()) / sqrtSamples);
                    Point2f p = new Point2f(pbrt.Lerp(u.x, -radius.x, radius.x), pbrt.Lerp(u.y, -radius.y, radius.y));
                    sum += Evaluate(p);
                }
            }
            return sum / nSamples * area;
        }

        // LanczosSincFilter Private Members
        private Vector2f radius;
        private Float tau;
        private FilterSampler sampler;
    }

    // TriangleFilter Definition
    public sealed unsafe class TriangleFilter : Filter
    {
        // TriangleFilter Public Methods
        public TriangleFilter(Vector2f radius) { this.radius = radius; }

        public static TriangleFilter Create(ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            // Find common filter parameters
            Float xw = parameters.GetOneFloat("xradius", 2f);
            Float yw = parameters.GetOneFloat("yradius", 2f);
            return new TriangleFilter(new Vector2f(xw, yw));
        }

        public override Vector2f Radius() { return radius; }

        public override string ToString() { return pbrt.StringPrintf("[ TriangleFilter radius: %s ]", radius); }

        public override Float Evaluate(Point2f p)
        {
            return std.max<Float>(0, radius.x - std.abs(p.x)) * std.max<Float>(0, radius.y - std.abs(p.y));
        }

        public override FilterSample Sample(Point2f u)
        {
            return new FilterSample(new Point2f(pbrt.SampleTent(u[0], radius.x), pbrt.SampleTent(u[1], radius.y)), (Float)1);
        }

        public override Float Integral() { return pbrt.Sqr(radius.x) * pbrt.Sqr(radius.y); }

        private Vector2f radius;
    }

    public abstract unsafe partial class Filter
    {
        public static Filter Create(string name, ParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            Filter filter = null;
            if (name == "box")
                filter = BoxFilter.Create(parameters, loc, alloc);
            else if (name == "gaussian")
                filter = GaussianFilter.Create(parameters, loc, alloc);
            else if (name == "mitchell")
                filter = MitchellFilter.Create(parameters, loc, alloc);
            else if (name == "sinc")
                filter = LanczosSincFilter.Create(parameters, loc, alloc);
            else if (name == "triangle")
                filter = TriangleFilter.Create(parameters, loc, alloc);
            else
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: filter type unknown.", name));

            if (filter == null)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: unable to create filter.", name));

            parameters.ReportUnused();
            return filter;
        }
    }

}  // namespace pbrt
