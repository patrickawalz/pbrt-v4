// (not in pbrt-v4) cloudmediumtexture.cs
//
// The density of pbrt-v4's "cloud" medium (CloudMedium.Density) as a float texture, `Texture "name" "float" "cloud"`, so that
// a "texture" medium of it is the same cloud with what a texture medium has and the cloud medium has not: a grid of
// majorants over the box (TextureMedium asks the texture for its bounds over each voxel: RegionBounds), the smallest density
// and a control density of each voxel (decomposition tracking, the residual estimators of the transmittance), a hierarchy of
// finer grids, emission, motion, membership of an aggregate with its own grid.
//
//     Texture "puff" "float" "cloud" "float density" 4 "float wispiness" 1 "float frequency" 3
//     MakeNamedMedium "smoke" "string type" "texture" "string density" "puff" "point3 p0" [...] "point3 p1" [...]
//
// declared where the cloud medium would be (the texture works in the space of the transformation that is current at its
// declaration, as the medium does in its own). The parameters and their defaults are the cloud medium's: "density" 1,
// "wispiness" 1, "frequency" 5, "edgefalloff" 0, "uniform" false.
//
// The bounds over a box. The density is
//     d = clamp((1 - y) 4.5 density n, 0, 1) + 2 max(0, 0.5 - y),   clamped to [0, 1],   times the fade towards the faces,
// with n five octaves of noise (weights 1/2, 1/4, ... at 1.99 times the frequency each) looked up at frequency * p moved by
// two octaves of the gradient of the noise ("wispiness"). The altitude terms are monotonic in y and bounded exactly. Each
// octave of n is bounded over the box of its lookup points by the bounds of the noise over a box (pbrt.NoiseBounds, by
// intervals over the lattice cells; a box of more than three cells gets the bound of the noise anywhere). The box of the lookup
// points is that of frequency * p grown by the largest displacement that the wispiness gives: the gradient is a difference
// quotient of the noise, at most the largest slope of the noise along an axis, NoiseSlopeBound. The density is never below
// zero where the noise may be negative, so the smallest density of a box is above zero only where the second term is (the
// lower half of the cloud) or where the noise is positive over the whole box.

using System;

namespace pbrt
{
    using static pbrt;

    public sealed unsafe class CloudMediumTexture : FloatTexture
    {
        public CloudMediumTexture(TextureMapping3D mapping, Float density, Float wispiness, Float frequency, Float edgeFalloff, bool uniform)
        {
            this.mapping = mapping;
            this.density = density;
            this.wispiness = wispiness;
            this.frequency = frequency;
            this.edgeFalloff = edgeFalloff;
            this.uniform = uniform;
        }

        public override Float MinValue() { return 0; }
        public override Float MaxValue() { return 1; }

        public override Float Evaluate(TextureEvalContext ctx)
        {
            Point3f p = mapping.Map(ctx).p;
            if (uniform)
            {
                return Clamp(density, 0, 1) * EdgeFactor(p);
            }
            Point3f pp = frequency * p;
            if (wispiness > 0)
            {
                // Perturb cloud lookup point _pp_ using noise
                Float vomega = 0.05f * wispiness, vlambda = 10f;
                for (int i = 0; i < 2; ++i)
                {
                    pp += vomega * DNoise(vlambda * pp);
                    vomega *= 0.5f;
                    vlambda *= 1.99f;
                }
            }
            // Sum scales of noise to approximate cloud density
            Float d = 0;
            Float omega = 0.5f, lambda = 1f;
            for (int i = 0; i < Octaves; ++i)
            {
                d += omega * Noise(lambda * pp);
                omega *= 0.5f;
                lambda *= 1.99f;
            }

            // Model decrease in density with altitude and return final cloud density
            d = Clamp((1 - p.y) * 4.5f * density * d, 0, 1);
            d += 2 * std.max<Float>(0, 0.5f - p.y);
            d = Clamp(d, 0, 1);
            d *= EdgeFactor(p);
            return d;
        }

        public override void RegionBounds(Bounds3f region, out Float lo, out Float hi)
        {
            // The box of the points of the texture that the region maps to
            Bounds3f box = new Bounds3f();
            for (int i = 0; i < 8; ++i)
            {
                TextureEvalContext ctx = new TextureEvalContext();
                ctx.p = region.Corner(i);
                Point3f corner = mapping.Map(ctx).p;
                box = (i == 0) ? new Bounds3f(corner) : pbrt.Union(box, corner);
            }
            Float fadeLo, fadeHi;
            EdgeBounds(box, out fadeLo, out fadeHi);
            if (uniform)
            {
                Float flat = Clamp(density, 0, 1);
                lo = flat * fadeLo;
                hi = flat * fadeHi;
                return;
            }
            // The noise: each octave over the box of its lookup points
            Float grown = 0;
            if (wispiness > 0)
            {
                // (0.05 wispiness (1 + 1/2) times the largest slope of the noise)
                grown = 0.05f * wispiness * 1.5f * NoiseSlopeBound;
            }
            Vector3f margin = new Vector3f(grown, grown, grown);
            Bounds3f lookups = new Bounds3f(frequency * box.pMin - margin, frequency * box.pMax + margin);
            if (frequency < 0)
            {
                lookups = new Bounds3f(frequency * box.pMax - margin, frequency * box.pMin + margin);
            }
            Float noiseLo = 0, noiseHi = 0, omega = 0.5f, lambda = 1f;
            for (int i = 0; i < Octaves; ++i)
            {
                NoiseBounds(new Bounds3f(lambda * lookups.pMin, lambda * lookups.pMax), out Float termLo, out Float termHi);
                noiseLo += omega * termLo;
                noiseHi += omega * termHi;
                omega *= 0.5f;
                lambda *= 1.99f;
            }
            // The altitude: (1 - y) 4.5 density over the heights of the box, times the noise
            Float a0 = (1 - box.pMin.y) * 4.5f * density, a1 = (1 - box.pMax.y) * 4.5f * density;
            Float aLo = std.min(a0, a1), aHi = std.max(a0, a1);
            Float productLo = std.min(std.min(aLo * noiseLo, aLo * noiseHi), std.min(aHi * noiseLo, aHi * noiseHi));
            Float productHi = std.max(std.max(aLo * noiseLo, aLo * noiseHi), std.max(aHi * noiseLo, aHi * noiseHi));
            Float lowLo = 2 * std.max<Float>(0, 0.5f - box.pMax.y), lowHi = 2 * std.max<Float>(0, 0.5f - box.pMin.y);
            lo = Clamp(Clamp(productLo, 0, 1) + lowLo, 0, 1) * fadeLo;
            hi = Clamp(Clamp(productHi, 0, 1) + lowHi, 0, 1) * fadeHi;
            // (the arithmetic of the bounds and that of the value round differently)
            lo = std.max<Float>(0, lo - 1e-5f);
            hi = std.min<Float>(1, hi + 1e-5f);
        }

        // Fades out toward the sides of the box (and the bottom) so that the cloud has no flat faces; one when the falloff is off
        private Float EdgeFactor(Point3f p)
        {
            if (edgeFalloff <= 0)
            {
                return 1;
            }
            Float edge = std.min(std.min(p.x, 1 - p.x), std.min(p.z, 1 - p.z)) / edgeFalloff;
            edge = std.min(edge, p.y / (0.5f * edgeFalloff));
            edge = Clamp(edge, 0, 1);
            return edge * edge * (3 - 2 * edge);
        }

        // min(t, 1 - t) over [t0, t1]
        private static void NearestFaceBounds(Float t0, Float t1, out Float lo, out Float hi)
        {
            Float m0 = std.min(t0, 1 - t0), m1 = std.min(t1, 1 - t1);
            lo = std.min(m0, m1);
            hi = t0 <= 0.5f && t1 >= 0.5f ? 0.5f : std.max(m0, m1);
        }

        private void EdgeBounds(Bounds3f box, out Float lo, out Float hi)
        {
            lo = 1;
            hi = 1;
            if (edgeFalloff <= 0)
            {
                return;
            }
            Float xLo, xHi, zLo, zHi;
            NearestFaceBounds(box.pMin.x, box.pMax.x, out xLo, out xHi);
            NearestFaceBounds(box.pMin.z, box.pMax.z, out zLo, out zHi);
            Float edgeLo = std.min(std.min(xLo, zLo) / edgeFalloff, box.pMin.y / (0.5f * edgeFalloff));
            Float edgeHi = std.min(std.min(xHi, zHi) / edgeFalloff, box.pMax.y / (0.5f * edgeFalloff));
            edgeLo = Clamp(edgeLo, 0, 1);
            edgeHi = Clamp(edgeHi, 0, 1);
            lo = edgeLo * edgeLo * (3 - 2 * edgeLo);
            hi = edgeHi * edgeHi * (3 - 2 * edgeHi);
        }

        public static CloudMediumTexture Create(Transform renderFromTexture, TextureParameterDictionary parameters, FileLoc loc, Allocator alloc)
        {
            TextureMapping3D map = TextureMapping3D.Create(parameters, renderFromTexture, loc, alloc);
            return new CloudMediumTexture(map, parameters.GetOneFloat("density", 1), parameters.GetOneFloat("wispiness", 1), parameters.GetOneFloat("frequency", 5),
                                          parameters.GetOneFloat("edgefalloff", 0), parameters.GetOneBool("uniform", false));
        }

        public override string ToString()
        {
            return StringPrintf("[ CloudMediumTexture mapping: %s density: %f wispiness: %f frequency: %f ]", mapping, density, wispiness, frequency);
        }

        // The largest slope of Noise() along an axis. In a lattice cell the noise is the interpolation, with the weights
        // w(t) = 6t^5 - 15t^4 + 10t^3 along each axis, of the eight corner values g . (p - corner), g having two components of
        // size one. Its derivative along x is the interpolation of the g_x (at most 1) plus w'(x) times the interpolation over y
        // and z of the differences of the corner values across x (each value at most 2 in size, a difference at most 4), and
        // w' is at most 15/8: 1 + 4 * 15/8. The noise is continuous with its derivative across the cells. (The slopes that
        // the noise has are far below this; the bound only has to hold.)
        public const Float NoiseSlopeBound = 8.5f;

        private const int Octaves = 5;

        private TextureMapping3D mapping;
        private Float density, wispiness, frequency, edgeFalloff;
        private bool uniform;
    }
}
