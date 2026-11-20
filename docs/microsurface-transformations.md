# Microsurface transformations

Atanasov, Koylazov, Dimov and Wilkie, "Microsurface Transformations" (EGSR 2022). Implemented from the paper; the authors' MIT
reference (`microsurface_transformations.h`) was read to confirm the conventions. Opt-in: nothing changes without `transform`.

A microsurface mapped by a matrix of the tangent plane, `M = [m00 m01; m10 m11]` (heights kept), is a microsurface again:

    D(h)     = |det M| D0(M^T h / |M^T h|) / |M^T h|^4
    G1(v)    = G1_0(M^-1 v / |M^-1 v|)
    sigma(v) = |M^-1 v| sigma0(M^-1 v / |M^-1 v|)          (projected area, every direction: the single sided form too)
    visible normal of v = M^-T u / |M^-T u|, u a visible normal of the original for M^-1 v / |M^-1 v|

All closed form; the visible normals are sampled exactly through the sampler of the original.

## Where

- `util/transformeddistribution.cs`: `TransformedDistribution : MicrofacetDistribution`, wraps any distribution (mixtures and
  strata included; `PDF`, `DG` and `Sample_wm` are passed through with their Jacobians, so a `LayeredDistribution` inside works).
  No V-cavity form (throws, as `MixtureDistribution` does).
- `distributionblend.cs`: `"float transform" [m00 m01 m10 m11]`, `"float transform1"`, ... per component, with the prefix of the
  material (`"interface.transform"`, ...). Every material that reads a blend takes it.
- `util/facetforge.cs`, `facetforge.cs`: `"float transform[N]"` per NDF component of every level, the null NDFs too
  (`vmf`, `nullstudentt`). `literalnull` is ignored for a transformed component (the thinned walk is used).

## What it is and is not

- `[s 0 0 t]` divides the roughnesses by `s` and `t`; a rotation turns the distribution in the tangent frame.
- For ggx, beckmann and studentt alone, any matrix (a shear too) is the same family with two roughnesses in a turned frame.
  New shapes come from transforming the parts of a mixture or of strata separately, and from the distributions that have no
  anisotropic form here: charlie, vmf, nullstudentt.
- The transformation is of the geometry, not of the light paths: a walk on the transformed surface is not the transformed walk
  (mirror reflection does not commute with a shear). Facet Forge and `MultiScatterConductorBxDF` walk the transformed surface
  through D, sigma and the visible normals, and agree.

## Tests (`TransformedDistribution`, 6 tests, 12 s)

- random matrices, ggx / beckmann / studentt / mixture: projected area along the normal off 1 by < 1.1e-7; shadowing constraint
  < 2.1e-7; visible normals against Sigma (above and below the horizon) within single precision
- stretch = roughnesses divided (1e-6), rotation = turned frame (1e-6)
- BxDF checks: conductor and dielectric on a sheared ggx, conductor on a transformed mixture and on strata with a sheared part
- conductor on a transformed ggx = first reflection of Facet Forge with the same transform (4.8e-6); albedo of the walks
  0.9328 / 0.9328, 0.9267 / 0.9268, 0.9281 / 0.9282 (+- 0.0001)
- transformed vmf in Facet Forge: projected area against a quadrature 1.5e-5, mean visible normal 1e-3 (200k samples),
  white furnace 1.0000
- scene syntax

## Heitz walks

`multiscatterconductor`, `multiscatterdielectric` and `multiscatterdiffuse` take `transform` through the same blend syntax.
`TransformedDistribution.WalksOfHeitz`: on a mixture of two sheared parts, reflected albedo of the dielectric 0.0397 / 0.0394,
0.0764 / 0.0748, 0.1673 / 0.1684 (+- 0.0009 to 0.0017) and albedo of the diffuse 0.7815 / 0.7811, 0.7806 / 0.7810,
0.7794 / 0.7793 (+- 0.0003) against Facet Forge; whole albedo of the dielectric 1.0000; conductor white furnace 1.0000.

## Visible normals of the vmf of Facet Forge (`nullsampler`)

Tokuyoshi, "A Numerically Stable Implementation of the von Mises-Fisher Distribution on S2" (2025). `"string nullsampler"`:
`"lobe"` (the default of the material) draws a normal from the vMF lobe by the stable inversion
(`r = log1p(u expm1(-2 kappa)) / kappa`, `cos = 1 + r`, `sin = sqrt(-(r r + 2 r))`, `kappa = 2 / alpha^2`) and keeps it with the
probability `max(0, wi . m)`; `"disc"` is the rejection from the uniform disc of the reference code. Same density; the share of
the proposals kept is `sigma(wi) / alpha^2` instead of `sigma(wi)`. `NullTable.lobeSampler` is false when a table is built in
code. `nullstudentt` and `literalnull` keep the disc.

`TransformedDistribution.VmfLobeSampler`, 20000 normals: alpha 0.5: 4 to 9 times faster; 0.2: 21 to 28; 0.05: 467 to 544;
0.02: 1977 to 2439. Mean normal against a quadrature within the noise for both; neither ran out of proposals. Plate of vmf 0.05,
path, 16 spp: 118 s with the disc, 7.8 s with the lobe; image means 0.107491 / 0.107490, RMSE against 256 spp 0.06256 both.
