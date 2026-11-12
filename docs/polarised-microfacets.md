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
| `ThinFilmBxDF` (smooth), `ThinCoatBxDF` | yes | as before; a transmission through the film now keeps the phase difference of the layers |
| `ThinFilmBxDF` (rough) | yes, new | the matrix of the stack of layers at the half vector (section 6) |
| `ThinDielectricBxDF` | yes, new | closed-form sum over the reflections inside the slab (section 4) |
| `MultiScatterConductorBxDF` | yes, new | matrices kept along the walk (section 3) |
| `MultiScatterDielectricBxDF` | yes, new | matrices kept along the walk (section 3) |
| `DiffuseBxDF`, `EonDiffuseBxDF`, `VmfDiffuseBxDF`, `MultiScatterDiffuseBxDF`, `DiffuseTransmissionBxDF` | no | they depolarise; this is the correct answer for a diffuse lobe |
| `CoatedDiffuseBxDF`, `CoatedConductorBxDF` (`LayeredBxDF`) | yes, new | matrices kept along the layered random walk (section 8) |
| OSL closure BxDFs | **not yet** | the specular and microfacet closures could |
| `HairBxDF`, `MeasuredBxDF`, `NormalizedFresnelBxDF` | no | no polarised model or data |

## 2. The interface

A BxDF gives its matrix through two overloads of the functions it already has, so that one evaluation serves the
scalar value and the matrix:

- `f(bsdf, wo, wi, mode, out polarised, out matrix)` returns the value of `f` and the matrix of the pair of
  directions;
- `Sample_f(bsdf, wo, uc, u, mode, sampleFlags, out polarised, out matrix)` returns the sample and the matrix of the
  path that was sampled.

Both matrices have the weight of one: the integrator multiplies them by the scalar weight of the scattering. The
defaults of the base class call the scalar functions and report no matrix. `RoughAttenuation` still builds the matrix
of `ConductorBxDF` and `DielectricBxDF` itself, and asks the `f` overload of the BxDFs that have one
(`HasMuellerMatrix`).

The density is not returned with them: for the walks it is an estimate, and one taken from the walk that gave `f`
would tie the weight of the multiple importance sampling to the value it multiplies.

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


## 3. The walks

The `f` with a matrix of `MultiScatterConductorBxDF` and `MultiScatterDielectricBxDF` is the walk of `f` for the same
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
| With one reflection allowed, the matrix is that of pbrt's single-scattering BxDF for the same directions | `OneReflectionIsSingleScattering` | within 1e-6: walk conductor, walk dielectric (reflection on both sides and transmission both ways) |
| A perfectly reflecting microsurface keeps the weight | `MirrorKeepsWeightAndMatricesArePhysical` | first element within 6e-8 of the weight |
| The matrix is physical: fully polarised light of any state leaves with a degree of polarisation of at most 1 | same | exceeded by at most 1e-6, on GGX, Beckmann and Student-t |
| Walks: the same, on the mean of many estimates | `WalkCameraPathIsLightPath` | within 3.0 standard deviations (conductor) and 2.7 (dielectric, with transmission pairs) |
| Thin slab: the closed form against 200 orders added one by one from the interface matrices | `ThinDielectricIsSumOfOrders` | within 4e-7; reflection plus transmission is 1 within 6e-8 |

The existing `Polarised.*` tests pass unchanged.

## 6. The rough thin film

`ThinFilmBxDF` takes a `MicrofacetDistribution`. When it is not smooth the BxDF is the microfacet model of
`ConductorBxDF` (conductor substrate, one side) or `DielectricBxDF` (dielectric substrate, both sides and
transmission), with the reflectance of the stack of layers at each microfacet, per wavelength. The material is
`thinfilm` with `roughness`, `uroughness`, `vroughness`, `remaproughness`, `distribution` and `gamma`.

`FrThinFilmPolarised` allocates nothing, takes the layers in either order (the light may come from the substrate) and
also returns the phases of the two transmitted amplitudes. The layers are lossless, so the transmittances are
`1 - Fs` and `1 - Fp`, and the phase of a transmission is the same in both directions.

The `f` with a matrix of `ThinFilmBxDF` builds it as `RoughAttenuation` does for the bare surfaces: the Fresnel matrix of the
stack at the half vector, in the frames of the plane of that normal.

| Check | Test | Result |
|---|---|---|
| Transmitted phase difference against the characteristic matrix method; the same light the other way has the same reflectance and phases | `Polarised.ThinFilm` | within 1e-3 rad, 16 cases |
| Energy, reciprocity, sampling, at the limits of the bare rough BxDFs | `BxDFCheck.RoughThinFilmDielectric`, `RoughThinFilmConductor` | reciprocity 5e-4 and 2e-7; albedo at most 0.976 and 0.939 |
| A film of no thickness is the bare rough surface (`f`, `PDF`, matrix) | `BxDFCheck.RoughThinFilmOfNoThickness`, `PolarisedMicrofacet.RoughThinFilm` | f within 1e-7, matrix within 5e-5 |
| The matrix for a path from the camera is that of the same light from its source; it is physical | `PolarisedMicrofacet.RoughThinFilm` | within 6e-5 over 4,834 pairs; the layers change an element by up to 0.71 |

## 7. The cost of the matrices

The product of two Mueller matrices is computed one wavelength at a time on the values of the elements, and the
rotation between the frames of the two is applied in place. Measured per call: 3.4 microseconds before, under 0.1
after; the matrix of a multiple-scattering conductor walk went from 8.0 to 3.3 microseconds.

## 8. The layered BxDFs

The `f` with a matrix of `LayeredBxDF` runs the random walk of `f` for the same pair of directions (the two share one routine, so
the walk is the same walk) and keeps the matrix of every scattering, each with a first element of 1; the scalar
factors stay in the walk's weight. The result is the sum of the matrices of the paths over the sum of their scalar
contributions, times the weight, as for the microsurface walks.

- A dielectric or conductor interface polarises by the Fresnel equations at the normal that sends one direction to
  the other (the normal of the interface when it is smooth).
- A diffuse interface depolarises, and so does a scattering in the medium between the interfaces (the
  Henyey-Greenstein phase function has no polarised form).
- The reflection of a smooth top interface is not part of `f`: it comes with the sample that follows it, as does
  every path between a smooth coat and a smooth conductor (`Sample_f` with a matrix).

The scalar value of `f` is unchanged by this, bit for bit (checked on 3,000 pairs for four configurations).

| Check | Test | Result |
|---|---|---|
| Under a coat of index 1 the matrix is that of the conductor below | `Layered`, `SampledPath` | within 5e-7 (rough, 2,000 pairs) and 1e-6 (smooth, 3,000 sampled directions) |
| Over a black base only the coat reflects | `Layered` | within 6e-8 of the rough dielectric |
| Over a white diffuse base the first element stays the weight; the polarisation is diluted | `Layered` | within 3e-7; 0.095 on average against 0.512 for the coat alone |
| The matrices are physical | `Layered`, `SampledPath` | degree of polarisation above 1 by at most 6e-6 |

## 9. Thin films on the multiple-scattering microsurfaces

`MultiScatterConductorBxDF` and `MultiScatterDielectricBxDF` take a `ThinFilmStack`: the Fresnel terms of every
microfacet are then those of the stack, in `f`, in `Sample_f` and in the matrices. The dielectric walk chooses
between reflection and transmission by the reflectance averaged over the wavelengths and carries the rest as a weight
by wavelength. The materials `multiscatterconductor` and `multiscatterdielectric` take `layereta` and
`layerthickness`.

| Check | Test | Result |
|---|---|---|
| With one reflection allowed the matrix is that of the rough thin film BxDF | `PolarisedMicrofacet.MultiScatterThinFilm` | conductor 2e-7, dielectric 1e-7 |
| The dielectric with a film returns all the light; both stay reciprocal | `BxDFCheck.MultiScatterThinFilm` | albedo 1.0000; 2.6 and 2.7 sigma at worst |
| The matrices are physical | `PolarisedMicrofacet.MultiScatterThinFilm` | degree of polarisation above 1 by at most 6e-7 |

## 10. One evaluation for the value and the matrix

Before, the integrator asked for `f` (or `Sample_f`) and then for the matrix, which ran the random walk of a
multiple-scattering or layered BxDF again. Now the walk of `f` builds both, and a sample carries the matrix of its
own path. Polarised renders of a sphere (160 x 160, 256 samples, old and new timed back to back):

| Material | Before | After | |
|---|---|---|---|
| `conductor` (rough) | 15.6 s | 14.9 s | unchanged |
| `thinfilm` (rough) | 18.9 s | 16.3 s | 1.16x |
| `multiscatterconductor` | 24.3 s | 21.5 s | 1.13x |
| `multiscatterdielectric` | 45.1 s | 34.6 s | 1.30x |
| `multiscatterconductor` with a film | 41.0 s | 29.5 s | 1.39x |
| `coateddiffuse` | 30.5 s | 19.9 s | 1.54x |
| `coatedconductor` | 35.3 s | 21.7 s | 1.63x |

The matrix of a sampled path is also more correct than what it replaces. The old matrix of a sample was the scalar
weight of the sample times a ratio of two estimates from another walk, which is biased. A lossless dielectric sphere
in an even surround must return unpolarised light: its rim polarisation was 2.8% before and is 0.1% now.
`SampledAgainstEvaluated` checks that sampling paths and evaluating directions find the same polarised light.

## 11. Not done

- The OSL closures.
- A scattering in the medium of a layered BxDF is taken as depolarising.
- `SampleLd` of the volumetric path integrator still evaluates `f` and then asks for the matrix (two walks); the
  polarised path integrator and the shared path walk use the single evaluation.
- The scalar `LayeredBxDF` returns about 4% less light through `Sample_f` than through `f` (measured by the harness on
  the BxDF as it was before any of this work); not looked into yet.
- The V-cavity conductor (Lee et al. 2018) had a polarised form too (the exact product over the reflections of each
  groove). It was removed from this branch with the BxDF, which is kept on the branch `vcavity-backup`: an evaluation
  cost about 300 microseconds, most of it the products of the Mueller matrices of the reflections.
