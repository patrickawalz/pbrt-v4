# Glints from a normal map: the `glint` material

    Material "glint" "string normalmap" "flakes.exr" "float uscale" 48 "float vscale" 24
        "spectrum eta" "metal-Au-eta" "spectrum k" "metal-Au-k"

    Material "glint" "string interface" "dielectric" "float eta" 1.5
        "string normalmap" "flakes.exr" "float uscale" 48 "float vscale" 24

Code: `src/pbrt/glint.cs` (`PositionNormalManifold`, `GlintBxDF`, `GlintMaterial`), tests `src/pbrt/glint_test.cs`.

The normal map is the microsurface of the material: every point of it is a mirror facet. A pixel sees the facets of
its footprint on the map, and the material evaluates their distribution exactly, so the glints of a small light (a
point light included) are found by a connection to the light. A plain normal map on a mirror shows nothing under a
point light, and under a small light it shows the glints only by chance.

## Parameters

| Parameter | Default | |
|---|---|---|
| `string normalmap` | (required) | The map (as for any material; here it is not a change of the shading normal) |
| `string interface` | `conductor` | `conductor` or `dielectric` |
| `spectrum eta`, `k`, or `reflectance` | copper | The conductor, as for `conductor` |
| `float eta` (or a spectrum, `sellmeier`...) | 1.5 | The dielectric, as for `dielectric` |
| `float uscale`, `vscale` | 1 | How many times the map repeats over (u, v) |
| `float roughness` | 0 | The roughness of a facet: the half side of the square of its normals (0: a mirror; to 0.5) |
| `float footprint` | 1 | Scales the footprint of the pixel on the map |
| `float maxfootprint` | 32 | A footprint with a longer half side (texels) is evaluated one tile at a time |
| `integer bounces` | 16 | The most facets that light meets at a point; 1: one facet only (loses the rest) |
| `float displacement` | | As for any material |

## Where it comes from

Three papers, combined:

* **Wu, Luan, Hasan, Ramamoorthi, "Position-Normal Manifold for Efficient Glint Rendering on High-Resolution Normal
  Maps", SIGGRAPH 2025**: the distribution of the normals of a footprint. The texels are the vertices of a mesh of
  triangles; over a triangle the x and y of the normal are interpolated linearly. The density of the normals at a
  normal m is the sum, over the triangles whose three normals hold m, of the footprint at the point that has m, over
  the Jacobian of the triangle (twice its area among the normals). No convolution, no fit: closed form and exact. A
  hierarchy of the ranges of the normals of the cells (2 by 2 blocks, up to the whole map) finds the triangles.
  A triangle with a Jacobian below 1e-6 (a flat piece of the map) gets an equilateral triangle of normals of exactly
  that Jacobian, as in the paper: a flat piece is a mirror with a roughness of about a thousandth of a radian.
  Sampling: a point of the footprint, and its normal.
  *Not taken*: the cluster hierarchy (it replaces the map by a simpler one, which changes the result), and the
  analytic shadow-masking of the height field and its diffuse filtering (the microsurface here is the next one).
* **Chermain, Claux, Merillou, "Glint Rendering based on a Multiple-Scattering Patch BRDF", EGSR 2019** (and the code
  released with it, read before the work): the microsurface at a point. The facet of the mapped normal p = (x, y, z)
  comes with a facet of the mirrored normal s = (-x, -y, z), of equal area, so the mean normal is the normal of the
  surface, and nothing is lost below it (no black fringe, no darkening of a rough map).
  *Not taken*: their glint integrator (Gaussian lobes over mip-mapped slopes, superseded by the exact distribution) and
  their energy compensation (a diffuse or a smooth lobe scaled by a precomputed, approximate lost energy).
* **Zhao, Wang, Wang, Holzschuch, "A discrete microfacet model for transparent glints rendering", CVM 2025**: the aim,
  a glint BSDF that also transmits. *Not their method* (counting of procedural particles in an approximated cone of
  refraction): the distribution of the manifold is a distribution of normals, so a refraction is evaluated at the
  half vector of the refraction, exactly, as for any rough dielectric.

## The model

`D_W(m)` is the density of the normals of the footprint (the manifold). The two facets of a point have the area
`1 / (2 z)` each, so the distribution of the facets is

    D(m) = (D_W(m) + D_W(mirrored m)) / 2

A facet of the normal m is seen from w past the other facet of its point with

    G1(w, m) = min(1, 2 w.z m.z / (<w, m>+ + <w, mirrored m>+))

which is the same at every point whose facet has that normal. So the average over the footprint of the local model of
Chermain et al. with mirror facets has a closed form:

    f = F(wo . h) D(h) G1(wo, h) G1(wi, h) / (4 cos(theta_o) cos(theta_i))

and a refraction is the same with the half vector and the factors of a rough dielectric. This is derived here; the
paper evaluates the local model with facets of a small roughness and sums Gaussian lobes.

**Rough facets** (`roughness` r). The normal of a facet is the mapped normal plus an offset, uniform over the square
of the half side r in the x and y of the normals. Wu et al. leave roughness out because a Gaussian has no closed form
against the manifold; a square has one. Over a triangle the mapped normal is linear in the position, so the part of
the triangle whose normal lies in the square around the normal asked for is the triangle cut by four straight lines,
and by four more for the footprint: a polygon, whose area times the density of the footprint over the area of the
square is what the triangle adds. Exact, no table. There is no smallest triangle then: a flat flake has the square as
its distribution, and highlights have soft edges. With r = 0 the mirror facets and the clamp of Wu et al. remain.

**More than one facet.** Light that the other facet hides, or that a facet sends below the surface, meets the other
facet of the point: a walk between the two facets, as in microfacet-based normal mapping, until it leaves. With the
walk the surface loses nothing (white furnace below).

* A walk that leaves after **one facet** is a sample of the closed form above; its density is exactly the
  visible-normal sampling of the model, and the weight of the sample is the Fresnel factor alone.
* **Two facets, both reflections**, have a closed form too (derived here). Two reflections by a facet q and by its
  mirror image are a rotation about the level axis across q, by four times the tilt of q. So for a pair of directions
  the axis is across the level part of `wo + wi` (either way round, two candidates), and the tilt is a quarter of the
  angle that takes `-wo` to `wi` about it. The density of `wi` is the visible-normal density of q, times the
  probability that the reflected light meets the other facet, times the probability that it then leaves, over the
  Jacobian of `wi` by q (in closed form from the rotation). `f` and `PDF` include it, so a point light sees this light
  as well.
* A longer walk, and for a dielectric any walk that goes through a facet on its way, is a path of mirrors without a
  closed form for a pair of directions: a specular sample, found by the sampling of the BSDF only.

**Footprint.** The parallelogram of the pixel on the map, uniform, from the differentials that the camera gives for
the point (pbrt's approximation for a ray without differentials), for every ray. The differentials of the ray itself
are not used: the first point of a camera path would have them and the same point at the end of a light path would
have the approximation, a bidirectional integrator weights the two as one BSDF, and with a BSDF that depends on the
footprint the weights no longer add up (measured: `bdpt` 0.5% low before, below). A footprint with a half side above
`maxfootprint` is cut into equal tiles and an intersection evaluates one of them, chosen by a hash of the point: the
mean of the tiles is the whole footprint exactly (to 1e-15 in the test), and the cost is that of a tile.

**Polarisation.** Every facet is a mirror of a known normal: the Mueller matrix is that of the Fresnel equations at
the facet, in the frames of its plane, and for two facets or a walk the product. For one facet the Stokes vectors are
those of pbrt's rough conductor and dielectric for the same pair of directions, to the last digit. (With two facets the
second reflects light that the first has polarised, so the polarised result is not the scalar one times a matrix of
first element 1; this is what a polarised render is for. The matrix of a longer walk is kept by the BxDF from
`Sample_f` and returned when the integrator asks for the matrix of that pair.)

## Checks

`Glint.*` tests:

* `ManifoldSamplesWhatItEvaluates`: the density integrates to 1 (0.986 to 1.004 by a noisy estimate for three footprints, one over the
  edge of the map, one smaller than a texel; 1.0001 to 1.0003 with a roughness); the share of the samples in each of
  256 bins of normals against the integral of the density there.
* `FlatPieces`: inside a flake the density is the 8 triangles of the footprint on the smallest triangle of normals,
  2e6, exactly.
* `FootprintTiles`: the mean of the 12 tiles of a footprint against the whole footprint: equal to 2e-15.
* `OneFacet` (the common checks of the branch, `bounces` 1): reciprocity, albedo below 1, `Sample_f`, `PDF` and `f`
  in agreement. Conductor reciprocal to 4e-7; dielectric to 1e-2 at worst with mirror facets (the density has steps,
  and the half vector of a refraction is not the same to the last bit for the two orders of a pair) and to 1e-4 with
  rough facets.
* `TwoFacets`: the share of the light that leaves after exactly two facets, by the walk against the integral of the
  closed form and of its density, for a gentle and a steep map, with and without roughness (0.4883 +- 0.0012 against
  0.4867 +- 0.0027 in the steepest case); the two-facet term is reciprocal to 4e-4 .. 2e-3.
* `WhiteFurnace`: a conductor that reflects everything sends out 1.000000 of the light with the walk, from 0.45 to
  0.9999 with one facet; a dielectric 1.00000; with rough facets all but the facets past the horizon.
* `Polarised`: matrices for one facet, two facets and walks; one facet against pbrt's rough BxDFs.

Renders (a level plate with a map of bumps, a spherical light; mean of the plate), final code:

| Scene | Estimator | Mean |
|---|---|---|
| conductor | `path`, 3 seeds | 2.3830 +- 0.0061 |
| conductor | BSDF samples alone (`simplepath`), 3 seeds | 2.3736 +- 0.0039 |
| conductor | light samples alone (`simplepath`), 3 seeds | 2.405 +- 0.025 |
| conductor | `bdpt`, 3 seeds | 2.3783 +- 0.0097 |
| conductor | `volpath`, `production`, 1 seed | 2.3858, 2.3776 |
| conductor | `path`, `bdpt`, polarised, 1 seed | 2.3851, 2.3917 |
| conductor, `roughness` 0.03 | `path`, BSDF alone, light alone, 1 seed | 2.3812, 2.3619, 2.3821 |
| dielectric, `roughness` 0.04, large light | `path`, 8 seeds | 0.33061 +- 0.00032 |
| dielectric, same | `bdpt`, 16 seeds | 0.33003 +- 0.00046 |
| conductor, footprints of about 55 texels | whole footprint, 6 seeds | 2.3152 +- 0.0024 (72 s) |
| conductor, same | tiles of 16 texels, 6 seeds | 2.3200 +- 0.0063 (16 s) |

The samples of the BSDF never evaluate the distribution, and the samples of the light do nothing else: their agreement
is the check of the closed forms in a scene. Before the two-facet closed form the light samples alone were 5% short
(2.260); now they are not.

`bdpt` and the footprint: with the footprint taken from the ray, `bdpt` on the dielectric was 0.32889 +- 0.00089
against `path` 0.33067 +- 0.00032 (0.54% low, 1.9 standard errors, and low in two other sets of runs). With the
footprint a function of the point, 0.33003 +- 0.00046 against 0.33061 +- 0.00032 (0.18%, 1.0 standard error). `path`
does not change with the footprint (0.33067 and 0.33066), as it should not.

A sphere whose map repeats 600 by 300 times (footprints of hundreds of texels), 16 samples: 21 s with tiles; with the
whole footprint the render was stopped after 900 s.

## Limits

* A walk of three or more facets, and for a dielectric a walk of more than one facet with a refraction in it, is not
  seen by a light without size (it is sampled, not connected). For a conductor that is what is left after two facets
  (3% of the light on a very steep map from above; nothing to speak of on a gentle one).
* A mirror facet (`roughness` 0) and a light without size meet with probability zero by any method; the clamp of Wu et
  al. (a flat piece is a mirror of a thousandth of a radian) is what makes them meet at all. A roughness is the
  physical answer.
* The square of the roughness is a square: a facet is not a Beckmann or GGX microsurface.
* The tiles of a large footprint cost noise (2.6 times the standard deviation in the test above, for a quarter of the
  time); the cluster hierarchy of the paper would be the way to have the whole footprint fast.
* The two facets of a point do not know their neighbours: a walk stays at its point (the model of Chermain et al.,
  and of microfacet-based normal mapping).
* The footprint is pbrt's approximation from the camera, which is the smallest over the film and shrinks with the
  samples per pixel (as pbrt's ray differentials do): the material filters less than a pixel. That costs noise, not
  correctness.
