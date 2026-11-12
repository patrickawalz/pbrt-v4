# Polarised light and the microfacet BxDFs

The polarised integrators carry a Mueller matrix along a path (`PolarisedLightSystem.Attenuation`). At a surface they
ask for the matrix of the scattering through `PolarisedPathIntegrator.SpecularAttenuation` (a sampled specular
direction) and `PolarisedPathIntegrator.RoughAttenuation` (any pair of directions). When neither answers, the
scattering is taken as depolarising.

This document covers what was added on the `pbr-materials` branch: a polarised form for every BxDF of the branch whose
light keeps its polarisation, including those where the light meets more than one microfacet.

## 1. Which BxDF does what

| BxDF | Polarised? | How |
|---|---|---|
| `ConductorBxDF`, `DielectricBxDF` (smooth and rough) | yes | as before: Fresnel matrix at the surface normal or at the half vector |
| `ThinFilmBxDF`, `ThinCoatBxDF` | yes | as before |
| `ThinDielectricBxDF` | yes, new | closed-form sum over the reflections inside the slab (section 4) |
| `MultiScatterConductorBxDF` | yes, new | matrices kept along the walk (section 3) |
| `MultiScatterDielectricBxDF` | yes, new | matrices kept along the walk (section 3) |
| `VCavityConductorBxDF` | yes, new | exact product over the reflections of each groove (section 2) |
| `DiffuseBxDF`, `EonDiffuseBxDF`, `VmfDiffuseBxDF`, `MultiScatterDiffuseBxDF`, `DiffuseTransmissionBxDF` | no | they depolarise; this is the correct answer for a diffuse lobe |
| `CoatedDiffuseBxDF`, `CoatedConductorBxDF` (`LayeredBxDF`) | **not yet** | the coat polarises, so these could; the layered random walk does not keep matrices |
| OSL closure BxDFs | **not yet** | the specular and microfacet closures could |
| `HairBxDF`, `MeasuredBxDF`, `NormalizedFresnelBxDF` | no | no polarised model or data |

## 2. The interface

`BxDF.Polarised(bsdf, wo, wi, weight, mode, out A)` is a virtual of the base class that returns false. A BxDF whose
light meets several microfacets overrides it. `RoughAttenuation` handles `ConductorBxDF` and `DielectricBxDF` itself,
as before, and passes everything else to this method.

`PolarisedFacet` (in `bxdfs.cs`) builds the matrix of one microfacet: the Fresnel matrix at the facet normal, with the
Stokes frames of the plane of that normal (`Attenuation.Scattered`). The matrices are not normalised.
`PolarisedFacet.Compose` appends a microfacet to a chain in the order the light meets them: for a path from the camera
(radiance mode) the walk starts at the camera side, so the light meets the newest microfacet first.

**The weight.** The integrator passes the scalar weight of the scattering and expects the first element of the
matrix to carry it. For one reflection the first element is exactly the weight. For several, the reflectance of the
chain for the light's actual polarisation is not the product of the scalar reflectances, so the result is

    A = weight * (sum of the matrices of the paths) / (sum of the scalar reflectances of the same paths)

and its first element is the weight times the ratio of the polarised reflectance to the scalar one. Where the BxDF is
next to nothing (both sums underflow) no matrix is returned.

**`VCavityConductorBxDF`.** The grooves that send one direction to the other are enumerated as for `f`. Each groove
has two known facets and a known sequence of reflections, so the matrix of each path is an exact product. The result
is a value, not an estimate.

## 3. The walks

`MultiScatterConductorBxDF.Polarised` and `MultiScatterDielectricBxDF.Polarised` repeat the walk of `f` for the same
pair of directions, with the same random numbers, and keep the matrix of every microfacet the walk meets. What each
microfacet would send to `wi` is the matrix of the microfacets before it times the matrix of the reflection (or
refraction) towards `wi`. For the dielectric, a microfacet that the walk reflected from or went through with the
probability of its scalar reflectance or transmittance has its matrix divided by that probability.

The result is an estimate, as `f` is. Because the walk is the one `f` used, the scalar sum in the denominator is the
value `f` returned for that pair.

For a direction drawn by `Sample_f`, the matrix comes from a separate walk for the sampled pair, not from the walk
that drew the direction; the sampled weight is scaled by that walk's ratio. This is a ratio of two estimates and is
slightly biased for a finite number of samples.

## 4. The thin dielectric slab

The light reflected by a slab is what the first interface reflects plus what returns after 1, 3, 5... reflections
inside; the light transmitted has had 0, 2, 4... of them. The orders add without interference and all share one plane
of incidence. With `R_s`, `R_p` the reflectances of the two polarisations and `r_s`, `r_p` the signed amplitudes of
the outer reflection:

    reflection:    R'_x = 2 R_x / (1 + R_x)           cross term  r_s r_p + (1 - R_s)(1 - R_p) r_s r_p / (1 - R_s R_p)
    transmission:  T'_x = (1 - R_x) / (1 + R_x)       cross term  (1 - R_s)(1 - R_p) / (1 - R_s R_p)

The cross term is smaller than `sqrt(R'_s R'_p)`: a slab depolarises a little (up to about 7 percent at index 1.5).
The matrix is divided by pbrt's scalar reflectance or transmittance of the slab, which is what chose the sample.

## 5. Verification

All in `polarised_microfacet_test.cs`.

| Check | Test | Result |
|---|---|---|
| With one reflection allowed, the matrix is that of pbrt's single-scattering BxDF for the same directions | `OneReflectionIsSingleScattering` | within 1e-6: walk conductor, V-cavity conductor, walk dielectric (reflection on both sides and transmission both ways) |
| A perfectly reflecting microsurface keeps the weight | `MirrorKeepsWeightAndMatricesArePhysical` | first element within 6e-8 of the weight |
| The matrix is physical: fully polarised light of any state leaves with a degree of polarisation of at most 1 | same | exceeded by at most 1e-6, on GGX, Beckmann and Student-t |
| V-cavity: the matrix for a path from the camera equals that of the same light followed from its source | `CameraPathIsLightPath` | within 2e-6 over 3,000 pairs; paths of several reflections change an element by up to 1.46, so the order of the reflections is exercised |
| Walks: the same, on the mean of many estimates | `WalkCameraPathIsLightPath` | within 3.0 standard deviations (conductor) and 2.7 (dielectric, with transmission pairs) |
| Thin slab: the closed form against 200 orders added one by one from the interface matrices | `ThinDielectricIsSumOfOrders` | within 4e-7; reflection plus transmission is 1 within 6e-8 |

The existing `Polarised.*` tests pass unchanged.

## 6. Not done

- The layered BxDFs (`CoatedDiffuseBxDF`, `CoatedConductorBxDF`) and the OSL closures.
- No polarised render was made with the new BxDFs: no material exposes them yet. The checks above are on the
  matrices.
- `MultiScatterConductorBxDF` and `VCavityConductorBxDF` still have no single-scattering counterpart on Beckmann or
  Student-t in the stock `ConductorBxDF`, which takes GGX only.
