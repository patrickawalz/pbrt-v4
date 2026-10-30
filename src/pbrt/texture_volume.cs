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
        public FloatGridTexture(TextureMapping3D mapping, int nx, int ny, int nz, Float[] values, WrapMode wrapMode)
        {
            this.mapping = mapping;
            this.nx = nx; this.ny = ny; this.nz = nz;
            this.values = values;
            this.wrapMode = wrapMode;
            lo = Infinity;
            hi = -Infinity;
            for (int i = 0; i < values.Length; ++i)
            {
                lo = std.min(lo, values[i]);
                hi = std.max(hi, values[i]);
            }
            // A black border reads zeros outside the grid
            if (wrapMode == WrapMode.Black)
            {
                lo = std.min(lo, 0);
                hi = std.max(hi, 0);
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
                        return 0;
                    break;
            }
            return values[x + nx * (y + ny * z)];
        }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
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
            if (!pbrt.TexelIntervals(mapped.pMin.x, mapped.pMax.x, nx, wrapMode, xa, xb, out int cx, out bool zx) ||
                !pbrt.TexelIntervals(mapped.pMin.y, mapped.pMax.y, ny, wrapMode, ya, yb, out int cy, out bool zy) ||
                !pbrt.TexelIntervals(mapped.pMin.z, mapped.pMax.z, nz, wrapMode, za, zb, out int cz, out bool zz))
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
                bLo = std.min(bLo, 0);
                bHi = std.max(bHi, 0);
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
                int channels = im.image.NChannels();
                for (int i = 0; i < values.Length; ++i)
                {
                    Float sum = 0;
                    for (int c = 0; c < channels; ++c)
                        sum += im.image.GetChannel(new Point2i(i % nx, i / nx), c);
                    values[i] = sum / channels;
                }
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
            string wrap = parameters.GetOneString("wrap", "black");
            pstd.optional<WrapMode> wrapMode = pbrt.ParseWrapMode(wrap);
            if (!wrapMode || wrapMode.value() == WrapMode.OctahedralSphere)
                pbrt.ErrorExit(loc, pbrt.StringPrintf("densitygrid: wrap mode \"%s\" is not supported.", wrap));
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new FloatGridTexture(mapping, nx, ny, nz, values, wrapMode.value());
        }

        private static Float Lerp(Float t, Float a, Float b) { return (1 - t) * a + t * b; }

        private TextureMapping3D mapping;
        private int nx, ny, nz;
        private Float[] values;
        private WrapMode wrapMode;
        private Float lo, hi;
    }

    // FloatTriplanarTexture Definition
    // Projects a 2D texture along the three axes of the texture's 3D mapping and blends the results with fixed weights.
    // The projected texture sees (y,z), (x,z) and (x,y) as the x and y of its points, so it takes a "planar" mapping
    // with its default axes. (Weights that depend on the normal would make the field depend on the view direction.)
    public sealed unsafe class FloatTriplanarTexture : FloatTexture
    {
        public FloatTriplanarTexture(TextureMapping3D mapping, FloatTexture tex, Float wx, Float wy, Float wz)
        {
            this.mapping = mapping;
            this.tex = tex;
            Float sum = wx + wy + wz;
            weights = new Float[] { wx / sum, wy / sum, wz / sum };
        }

        public override Float MinValue() { return tex.MinValue(); }
        public override Float MaxValue() { return tex.MaxValue(); }

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
                    sum += weights[axis] * tex.Evaluate(ProjectedContext(p, axis));
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
                tex.RegionBounds(new Bounds3f(a, b), out Float tlo, out Float thi);
                lo += weights[axis] * tlo;
                hi += weights[axis] * thi;
            }
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FloatTriplanarTexture tex: %s weights: %f %f %f ]", tex, weights[0], weights[1],
                                     weights[2]);
        }

        public static FloatTriplanarTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters,
                                                   FileLoc loc, Allocator alloc)
        {
            FloatTexture tex = parameters.GetFloatTexture("tex", 1f, alloc);
            Float wx = parameters.GetOneFloat("xweight", 1f), wy = parameters.GetOneFloat("yweight", 1f),
                  wz = parameters.GetOneFloat("zweight", 1f);
            if (wx < 0 || wy < 0 || wz < 0 || wx + wy + wz <= 0)
                pbrt.ErrorExit(loc, "triplanar: the weights must be non-negative and not all zero.");
            TextureMapping3D mapping = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new FloatTriplanarTexture(mapping, tex, wx, wy, wz);
        }

        private TextureMapping3D mapping;
        private FloatTexture tex;
        private Float[] weights;
    }
}
