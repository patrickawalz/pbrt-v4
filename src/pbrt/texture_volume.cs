// (not in pbrt-v4) Textures for driving volumes: a 3D sampled-grid texture and a triplanar projection of a 2D texture.
// Both are fields of the position alone, so a medium driven by them stays unbiased, and both give rigorous region
// bounds so the majorant grid of a medium hugs them.

using System;
using System.Collections.Generic;

namespace pbrt
{
    // FloatGridTexture Definition
    // A 3D grid of samples spanning the unit cube of the texture's 3D mapping, reconstructed with the trilinear
    // interpolation of SampledGrid (sample centers at half-integers), so a medium driven by it behaves like a
    // "uniformgrid" medium. Lookups outside the cube follow the wrap mode (black by default, as in SampledGrid).
    public sealed unsafe class FloatGridTexture : FloatTexture
    {
        public FloatGridTexture(TextureMapping3D mapping, int nx, int ny, int nz, Float[] values, WrapMode wrapMode, Float borderValue = 0, bool nodes = false)
        {
            this.mapping = mapping;
            this.nx = nx; this.ny = ny; this.nz = nz;
            this.values = values;
            this.wrapMode = wrapMode;
            this.borderValue = borderValue;
            this.nodes = nodes;
            lo = Infinity;
            hi = -Infinity;
            for (int i = 0; i < values.Length; ++i)
            {
                lo = std.min(lo, values[i]);
                hi = std.max(hi, values[i]);
            }
            // A constant border (black, or white as in LuxCore) reads its value outside the grid
            if (wrapMode == WrapMode.Black)
            {
                lo = std.min(lo, borderValue);
                hi = std.max(hi, borderValue);
            }
        }

        public override Float MinValue() { return lo; }
        public override Float MaxValue() { return hi; }

        // The sample at a grid index, following the wrap mode
        private Float Lookup(int x, int y, int z)
        {
            switch (wrapMode)
            {
                case WrapMode.Repeat:
                    x = ((x % nx) + nx) % nx;
                    y = ((y % ny) + ny) % ny;
                    z = ((z % nz) + nz) % nz;
                    break;
                case WrapMode.Clamp:
                    x = std.min(std.max(x, 0), nx - 1);
                    y = std.min(std.max(y, 0), ny - 1);
                    z = std.min(std.max(z, 0), nz - 1);
                    break;
                default:
                    if (x < 0 || x >= nx || y < 0 || y >= ny || z < 0 || z >= nz)
                        return borderValue;
                    break;
            }
            return values[x + nx * (y + ny * z)];
        }

        // (not in pbrt-v4) LuxCore's density grid: the samples are at the nodes i / n, not at voxel centers, and the
        // neighbor of a cell is always clamped to the grid, even when the wrap mode is repeat
        private Float EvaluateNodes(Point3f p)
        {
            Float fx, fy, fz;
            int ix, iy, iz;
            switch (wrapMode)
            {
                case WrapMode.Repeat:
                    fx = p.x * nx; ix = (int)std.floor(fx); fx -= ix; ix = ((ix % nx) + nx) % nx;
                    fy = p.y * ny; iy = (int)std.floor(fy); fy -= iy; iy = ((iy % ny) + ny) % ny;
                    fz = p.z * nz; iz = (int)std.floor(fz); fz -= iz; iz = ((iz % nz) + nz) % nz;
                    break;
                case WrapMode.Clamp:
                    fx = std.min(std.max(p.x, 0), 1) * nx; ix = std.min((int)std.floor(fx), nx - 1); fx -= ix;
                    fy = std.min(std.max(p.y, 0), 1) * ny; iy = std.min((int)std.floor(fy), ny - 1); fy -= iy;
                    fz = std.min(std.max(p.z, 0), 1) * nz; iz = std.min((int)std.floor(fz), nz - 1); fz -= iz;
                    break;
                default:
                    if (!(p.x >= 0 && p.x < 1 && p.y >= 0 && p.y < 1 && p.z >= 0 && p.z < 1))
                        return borderValue;
                    fx = p.x * nx; ix = (int)std.floor(fx); fx -= ix;
                    fy = p.y * ny; iy = (int)std.floor(fy); fy -= iy;
                    fz = p.z * nz; iz = (int)std.floor(fz); fz -= iz;
                    break;
            }
            int ix1 = std.min(ix + 1, nx - 1), iy1 = std.min(iy + 1, ny - 1), iz1 = std.min(iz + 1, nz - 1);
            Float d00 = Lerp(fx, values[ix + nx * (iy + ny * iz)], values[ix1 + nx * (iy + ny * iz)]);
            Float d10 = Lerp(fx, values[ix + nx * (iy1 + ny * iz)], values[ix1 + nx * (iy1 + ny * iz)]);
            Float d01 = Lerp(fx, values[ix + nx * (iy + ny * iz1)], values[ix1 + nx * (iy + ny * iz1)]);
            Float d11 = Lerp(fx, values[ix + nx * (iy1 + ny * iz1)], values[ix1 + nx * (iy1 + ny * iz1)]);
            return Lerp(fz, Lerp(fy, d00, d10), Lerp(fy, d01, d11));
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            if (nodes)
                return EvaluateNodes(p);
            // Compute voxel coordinates and offsets for _p_
            Float fx = p.x * nx - .5f, fy = p.y * ny - .5f, fz = p.z * nz - .5f;
            int ix = (int)std.floor(fx), iy = (int)std.floor(fy), iz = (int)std.floor(fz);
            Float dx = fx - ix, dy = fy - iy, dz = fz - iz;

            // Trilinear interpolation of the eight neighboring samples
            Float d00 = Lerp(dx, Lookup(ix, iy, iz), Lookup(ix + 1, iy, iz));
            Float d10 = Lerp(dx, Lookup(ix, iy + 1, iz), Lookup(ix + 1, iy + 1, iz));
            Float d01 = Lerp(dx, Lookup(ix, iy, iz + 1), Lookup(ix + 1, iy, iz + 1));
            Float d11 = Lerp(dx, Lookup(ix, iy + 1, iz + 1), Lookup(ix + 1, iy + 1, iz + 1));
            return Lerp(dz, Lerp(dy, d00, d10), Lerp(dy, d01, d11));
        }

        // The extremes of the samples a trilinear lookup in the region can read
        public override void RegionBounds(Bounds3f region, out Float rlo, out Float rhi)
        {
            rlo = lo;
            rhi = hi;
            Bounds3f mapped = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                Point3f p = mapping.Map(ctx).p;
                mapped = (i == 0) ? new Bounds3f(p) : pbrt.Union(mapped, p);
            }
            int[] xa = new int[2], xb = new int[2], ya = new int[2], yb = new int[2], za = new int[2], zb = new int[2];
            // Node sampling reads the cells floor(s n) and floor(s n) + 1, which are the centered cells of s + 1 / (2 n)
            Vector3f shift = nodes ? new Vector3f(.5f / nx, .5f / ny, .5f / nz) : new Vector3f(0, 0, 0);
            if (!pbrt.TexelIntervals(mapped.pMin.x + shift.x, mapped.pMax.x + shift.x, nx, wrapMode, xa, xb, out int cx, out bool zx) ||
                !pbrt.TexelIntervals(mapped.pMin.y + shift.y, mapped.pMax.y + shift.y, ny, wrapMode, ya, yb, out int cy, out bool zy) ||
                !pbrt.TexelIntervals(mapped.pMin.z + shift.z, mapped.pMax.z + shift.z, nz, wrapMode, za, zb, out int cz, out bool zz))
                return;
            Float bLo = Infinity, bHi = -Infinity;
            for (int k = 0; k < cz; ++k)
            {
                for (int j = 0; j < cy; ++j)
                {
                    for (int i = 0; i < cx; ++i)
                    {
                        for (int z = za[k]; z <= zb[k]; ++z)
                        {
                            for (int y = ya[j]; y <= yb[j]; ++y)
                            {
                                int row = nx * (y + ny * z);
                                for (int x = xa[i]; x <= xb[i]; ++x)
                                {
                                    Float v = values[x + row];
                                    bLo = std.min(bLo, v);
                                    bHi = std.max(bHi, v);
                                }
                            }
                        }
                    }
                }
            }
            if (zx || zy || zz || cx == 0 || cy == 0 || cz == 0)
            {
                bLo = std.min(bLo, borderValue);
                bHi = std.max(bHi, borderValue);
            }
            rlo = std.max(rlo, bLo);
            rhi = std.min(rhi, bHi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatGridTexture res: %d x %d x %d mapping: %s ]", nx, ny, nz, mapping);
        }

        // Samples come from "float density" (x fastest, then y, then z) or from "string filename", an image of
        // nx x (ny * nz) pixels that stacks the z slices, each of ny rows, from the top down
        public static FloatGridTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc,
                                              Allocator alloc)
        {
            int nx = parameters.GetOneInt("nx", 1), ny = parameters.GetOneInt("ny", 1), nz = parameters.GetOneInt("nz", 1);
            if (nx < 1 || ny < 1 || nz < 1)
                pbrt.ErrorExit(loc, "densitygrid: \"nx\", \"ny\" and \"nz\" must be positive.");
            Float[] values = new Float[nx * ny * nz];
            string filename = pbrt.ResolveFilename(parameters.GetOneString("filename", ""));
            if (filename.Length != 0)
            {
                ImageAndMetadata im = Image.Read(filename, alloc);
                Point2i res = im.image.Resolution();
                if (res.x != nx || res.y != ny * nz)
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: %s is %d x %d but %d x %d (nx x ny*nz) was expected.",
                                                          filename, res.x, res.y, nx, ny * nz));
                // Like LuxCore, a color image gives its luminance
                int channels = im.image.NChannels();
                for (int i = 0; i < values.Length; ++i)
                {
                    Point2i pixel = new Point2i(i % nx, i / nx);
                    values[i] = (channels >= 3) ? Luminance(im.image.GetChannel(pixel, 0), im.image.GetChannel(pixel, 1),
                                                            im.image.GetChannel(pixel, 2))
                                                : im.image.GetChannel(pixel, 0);
                }
            }
            else if (parameters.GetRGBArray("density").Count != 0)
            {
                // "rgb density": one color per voxel, used as its luminance (as in LuxCore)
                List<RGB> data = parameters.GetRGBArray("density");
                if (data.Count != values.Length)
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: %d values given for a %d x %d x %d grid.", data.Count,
                                                          nx, ny, nz));
                for (int i = 0; i < values.Length; ++i)
                    values[i] = Luminance(data[i].r, data[i].g, data[i].b);
            }
            else
            {
                List<Float> data = parameters.GetFloatArray("density");
                if (data.Count != values.Length)
                    pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: %d values given for a %d x %d x %d grid.", data.Count,
                                                          nx, ny, nz));
                for (int i = 0; i < values.Length; ++i)
                    values[i] = data[i];
            }
            string sampling0 = parameters.GetOneString("sampling", "centers");
            // The default wrap mode is LuxCore's "repeat" for node sampling, and a black border (as in a "uniformgrid"
            // medium) for the default center sampling
            string wrap = parameters.GetOneString("wrap", sampling0 == "nodes" ? "repeat" : "black");
            // "white" is a black-style border that reads 1, as in LuxCore's density grid
            Float borderValue = (wrap == "white") ? 1f : 0f;
            pstd.optional<WrapMode> wrapMode = pbrt.ParseWrapMode(wrap == "white" ? "black" : wrap);
            if (!wrapMode || wrapMode.value() == WrapMode.OctahedralSphere)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: wrap mode \"%s\" is not supported.", wrap));
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            string sampling = sampling0;
            if (sampling != "centers" && sampling != "nodes")
                pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: sampling \"%s\" is unknown.", sampling));
            return new FloatGridTexture(mapping, nx, ny, nz, values, wrapMode.value(), borderValue, sampling == "nodes");
        }

        // LuxCore's Spectrum::Y()
        public static Float Luminance(Float r, Float g, Float b) { return 0.212671f * r + 0.715160f * g + 0.072169f * b; }

        public int NX() { return nx; }
        public int NY() { return ny; }
        public int NZ() { return nz; }

        private static Float Lerp(Float t, Float a, Float b) { return (1 - t) * a + t * b; }

        private TextureMapping3D mapping;
        private int nx, ny, nz;
        private Float[] values;
        private WrapMode wrapMode;
        private bool nodes;
        private Float lo, hi, borderValue;
    }

    // SpectrumGridTexture Definition
    // A grid of colors (LuxCore's "densitygrid" read as a spectrum), for coefficients that vary in color through the
    // volume: the red, green and blue channels are three grids that share a mapping, trilinear interpolation and wrap mode
    public sealed unsafe class SpectrumGridTexture : SpectrumTexture
    {
        public SpectrumGridTexture(FloatGridTexture red, FloatGridTexture green, FloatGridTexture blue,
                                   SpectrumType spectrumType, RGBColorSpace colorSpace)
        {
            channels = new FloatGridTexture[] { red, green, blue };
            this.spectrumType = spectrumType;
            this.colorSpace = colorSpace;
        }

        public override Float MaxValue()
        {
            return pbrt.RGBSpectrumBound(spectrumType, colorSpace,
                                         std.max(channels[0].MaxValue(), std.max(channels[1].MaxValue(), channels[2].MaxValue())));
        }

        public override Float RegionMax(Bounds3f region)
        {
            Float m = 0;
            for (int c = 0; c < 3; ++c)
            {
                channels[c].RegionBounds(region, out Float lo, out Float hi);
                m = std.max(m, hi);
            }
            return pbrt.RGBSpectrumBound(spectrumType, colorSpace, m);
        }

        public override SampledSpectrum Evaluate(TextureEvalContext ctx, SampledWavelengths lambda)
        {
            RGB rgb = pbrt.ClampZero(new RGB(channels[0].Evaluate(ctx), channels[1].Evaluate(ctx), channels[2].Evaluate(ctx)));
            if (spectrumType == SpectrumType.Unbounded)
                return new RGBUnboundedSpectrum(colorSpace, rgb).Sample(lambda);
            if (spectrumType == SpectrumType.Albedo)
                return new RGBAlbedoSpectrum(colorSpace, pbrt.Clamp(rgb, 0, 1)).Sample(lambda);
            return new RGBIlluminantSpectrum(colorSpace, rgb).Sample(lambda);
        }

        public override string ToString() { return "[ SpectrumGridTexture ]"; }

        public static SpectrumGridTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                 SpectrumType spectrumType, FileLoc loc, Allocator alloc)
        {
            int nx = parameters.GetOneInt("nx", 1), ny = parameters.GetOneInt("ny", 1), nz = parameters.GetOneInt("nz", 1);
            List<RGB> data = parameters.GetRGBArray("density");
            if (nx < 1 || ny < 1 || nz < 1 || data.Count != nx * ny * nz)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: %d colors given for a %d x %d x %d grid.", data.Count, nx, ny, nz));
            string sampling = parameters.GetOneString("sampling", "centers");
            string wrap = parameters.GetOneString("wrap", sampling == "nodes" ? "repeat" : "black");
            Float borderValue = (wrap == "white") ? 1f : 0f;
            pstd.optional<WrapMode> wrapMode = pbrt.ParseWrapMode(wrap == "white" ? "black" : wrap);
            if (!wrapMode || wrapMode.value() == WrapMode.OctahedralSphere)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: wrap mode \"%s\" is not supported.", wrap));
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            Float[][] values = new Float[3][] { new Float[data.Count], new Float[data.Count], new Float[data.Count] };
            for (int i = 0; i < data.Count; ++i)
            {
                values[0][i] = data[i].r;
                values[1][i] = data[i].g;
                values[2][i] = data[i].b;
            }
            FloatGridTexture[] grids = new FloatGridTexture[3];
            for (int c = 0; c < 3; ++c)
                grids[c] = new FloatGridTexture(mapping, nx, ny, nz, values[c], wrapMode.value(), borderValue, sampling == "nodes");
            return new SpectrumGridTexture(grids[0], grids[1], grids[2], spectrumType, RGBColorSpace.sRGB);
        }

        private FloatGridTexture[] channels;
        private SpectrumType spectrumType;
        private RGBColorSpace colorSpace;
    }

    // FloatTriplanarTexture Definition
    // Projects a 2D texture along the three axes of the texture's 3D mapping and blends the results with fixed weights.
    // The projected texture sees (y,z), (x,z) and (x,y) as the x and y of its points, so it takes a "planar" mapping
    // with its default axes. (Weights that depend on the normal would make the field depend on the view direction.)
    public sealed unsafe class FloatTriplanarTexture : FloatTexture
    {
        public FloatTriplanarTexture(TextureMapping3D mapping, FloatTexture tex, Float wx, Float wy, Float wz)
            : this(mapping, tex, tex, tex, wx, wy, wz)
        {
        }

        // As in LuxCore, each projection can have its own texture (texture1 is seen along x, texture2 along y and
        // texture3 along z)
        public FloatTriplanarTexture(TextureMapping3D mapping, FloatTexture tex1, FloatTexture tex2, FloatTexture tex3,
                                     Float wx, Float wy, Float wz)
        {
            this.mapping = mapping;
            textures = new FloatTexture[] { tex1, tex2, tex3 };
            Float sum = wx + wy + wz;
            weights = new Float[] { wx / sum, wy / sum, wz / sum };
        }

        public override Float MinValue()
        {
            Float lo = 0;
            for (int axis = 0; axis < 3; ++axis)
                if (weights[axis] != 0)
                    lo += weights[axis] * textures[axis].MinValue();
            return lo;
        }

        public override Float MaxValue()
        {
            Float hi = 0;
            for (int axis = 0; axis < 3; ++axis)
                if (weights[axis] != 0)
                    hi += weights[axis] * textures[axis].MaxValue();
            return hi;
        }

        private static TextureEvalContext ProjectedContext(Point3f p, int axis)
        {
            TextureEvalContext ctx = new TextureEvalContext();
            // The projection along x keeps (y,z), along y keeps (x,z) and along z keeps (x,y)
            ctx.p = axis == 0 ? new Point3f(p.y, p.z, 0) : axis == 1 ? new Point3f(p.x, p.z, 0) : new Point3f(p.x, p.y, 0);
            return ctx;
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            Float sum = 0;
            for (int axis = 0; axis < 3; ++axis)
                if (weights[axis] != 0)
                    sum += weights[axis] * textures[axis].Evaluate(ProjectedContext(p, axis));
            return sum;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            Bounds3f mapped = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                Point3f p = mapping.Map(ctx).p;
                mapped = (i == 0) ? new Bounds3f(p) : pbrt.Union(mapped, p);
            }
            lo = 0;
            hi = 0;
            for (int axis = 0; axis < 3; ++axis)
            {
                if (weights[axis] == 0)
                    continue;
                Point3f a = ProjectedContext(mapped.pMin, axis).p, b = ProjectedContext(mapped.pMax, axis).p;
                textures[axis].RegionBounds(new Bounds3f(a, b), out Float tlo, out Float thi);
                lo += weights[axis] * tlo;
                hi += weights[axis] * thi;
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatTriplanarTexture tex1: %s tex2: %s tex3: %s weights: %f %f %f ]", textures[0],
                                     textures[1], textures[2], weights[0], weights[1], weights[2]);
        }

        public static FloatTriplanarTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                   FileLoc loc, Allocator alloc)
        {
            FloatTexture tex = parameters.GetFloatTexture("tex", 1f, alloc);
            FloatTexture tex1 = parameters.GetFloatTextureOrNull("texture1", alloc) ?? tex;
            FloatTexture tex2 = parameters.GetFloatTextureOrNull("texture2", alloc) ?? tex;
            FloatTexture tex3 = parameters.GetFloatTextureOrNull("texture3", alloc) ?? tex;
            Float wx = parameters.GetOneFloat("xweight", 1f), wy = parameters.GetOneFloat("yweight", 1f),
                  wz = parameters.GetOneFloat("zweight", 1f);
            if (wx < 0 || wy < 0 || wz < 0 || wx + wy + wz <= 0)
                pbrt.ErrorExit(loc, "triplanar: the weights must be non-negative and not all zero.");
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new FloatTriplanarTexture(mapping, tex1, tex2, tex3, wx, wy, wz);
        }

        private TextureMapping3D mapping;
        private FloatTexture[] textures;
        private Float[] weights;
    }
}
