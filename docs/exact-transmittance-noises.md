# Noises with exact transmittance (not in pbrt-v4)

Three procedural densities whose integral along a ray is a closed form, so the transmittance of a medium made of them is one
evaluation and the distance to a collision is the root of a function that only grows. No majorant, no null collision, no stepping.

| | texture / medium | file | never negative | cost of an integral |
|---|---|---|---|---|
| cosine noise | `"cosinenoise"` | `src/pbrt/cosinenoise.cs` | only at a high `"offset"` | one cosine a wave |
| squared cosine noise | `"cosinenoise"` with `"bool squared" true` | same | always | one sine and cosine a wave, a product a pair |
| kernel noise | `"kernelnoise"` | `src/pbrt/kernelnoise.cs` | always | the kernels of the cells the ray passes |

Cosine noise is Balint and Mantiuk, "Closed Form Transmittance in Heterogeneous Media Using Cosine Noise" (CESCG 2019): sections 4
(transmittance) and 5 (distance sampling by inverting the integral) are both implemented. Where the sum of cosines goes below zero
the paper clamps and accepts a bias; here the closed form is kept as a control and what the clamp adds is estimated by ratio
tracking, cell by cell of a grid, so the result stays unbiased (that case needs the grid, and a box to put it on).

Squared cosine noise is the square of the same sum. A square of a sum of cosines is a sum of cosines, so the closed form is kept,
and a square is never negative: the clamp, the residual and the grid all go away at every offset.

Kernel noise is a sparse convolution noise of the bumps `(1 - r^2/R^2)^3`, one to a lattice cell (with the probability
`"coverage"`), in octaves. Along a ray a bump is a polynomial of the sixth degree. It is never negative, has true empty space, and
is local: the cost follows the length of the ray in cells.

## Use

    MakeNamedMedium "cloud" "string type" "kernelnoise" "rgb sigma_a" [...] "rgb sigma_s" [...] "float density" 5
        "float frequency" 2.5 "integer octaves" 3 "float coverage" 0.5
    MakeNamedMedium "haze" "string type" "cosinenoise" "bool squared" true "float density" 2.5 "float frequency" 2.5
    Texture "puffs" "float" "kernelnoise" "float frequency" 4

Both media fill the shape they are the interior of, or the box `"p0"`/`"p1"` of their own space. `"string transmittance"` is
`"closedform"` unless given. `"string sampling"` is `"closedform"` (the inversion) or `"majorant"` (tracking against bounds of the
density, kept for comparison and for the trackers that work with majorants).

## Measured

Magi (a C# port of pbrt-v4), volpath, a sphere of the medium of optical thickness of a few mean free paths under a distant light and
a sky, 360x270, 64 spp, independent sampler, seed 1, two cores. The baseline is the same medium tracked against a 64^3 grid of exact
per-cell bounds with ratio-tracking transmittance. Error is against 256 spp of the closed form (seed 7), with the noise of the
reference taken out.

| density | closed form | baseline | variance x time, baseline / closed form |
|---|---|---|---|
| cosine noise, never negative | 58.1 s, RMSE 0.0684 | 62.0 s, RMSE 0.0738 | 1.29 |
| cosine noise, clamped (`"offset"` 0.5) | 119.6 s, RMSE 0.0679 | 123.6 s, RMSE 0.0702 | 1.12 |
| squared cosine noise | 76.0 s, RMSE 0.0660 | 68.0 s, RMSE 0.0700 | 1.04 |
| kernel noise | 127.3 s, RMSE 0.0692 | 93.3 s, RMSE 0.0730 | 0.84 |

The means of every pair agree to the noise. In this scene the error is mostly that of multiple scattering, which neither method
changes, so the gain of an exact transmittance is small, and for the kernel noise the inversion costs more than a good grid saves.

What the closed form does not need is the grid. Without a box (the medium shaped by the sphere alone) the baseline has only the
bound of the density over all of space, and the kernel noise scene at 8 spp takes 17.5 s with the closed form and 110.0 s by
tracking.

Per shadow ray the difference is plain: the closed form has no variance at all where nothing is clamped, against a standard
deviation of 0.10 to 0.15 of one ratio-tracking estimate (`CosineNoise.ClampedDensityIsUnbiased`).
