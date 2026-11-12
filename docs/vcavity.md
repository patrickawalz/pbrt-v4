# V-cavity microsurfaces

This document describes the V-cavity work on the `pbr-materials` branch: what was implemented, how each part was
verified, and what is known to be wrong. The code is in `src/pbrt/util/vcavity.cs` (the groove solver),
`src/pbrt/util/scattering.cs` (the V-cavity functions of `MicrofacetDistribution`) and `src/pbrt/bxdfs.cs`
(`VCavityConductorBxDF`, `VCavityDielectricBxDF`). The tests are `util/vcavity_test.cs`,
`util/scattering_microfacet_test.cs` and `bxdfcheck_test.cs`.

Status in one table:

| Part | Source | Status |
|---|---|---|
| Groove solver (`VCavity.Trace`) | our C++ fork, after Lee et al. 2018 | verified exact |
| `VCavityConductorBxDF` | our C++ fork, after Lee et al. 2018 | verified: reciprocal, energy conserving |
| V-cavity visible normals below the horizon | Tokuyoshi 2021; Student-t is our extension | verified |
| `VCavityDielectricBxDF` | ours | **experimental: not reciprocal, loses energy** |
| Periodic-profile reference | ours | reciprocal, energy conserving; the target for a correct dielectric |

No material uses any of these BxDFs yet.

## 1. The model

A V-cavity microsurface is a collection of symmetric V-shaped grooves. A groove is described by the normal `s` of one
of its two facets; the other facet has the normal mirrored across the macro normal. Grooves are drawn with the density
`D(s) (s . n)`, where `D` is any microfacet distribution. A ray that enters a groove stays in the plane spanned by its
direction and the groove axis, so the groove is a two-dimensional problem in its cross section: the ray's component
along the groove axis never changes on a mirror reflection.

References:

- J. H. Lee, A. Jarabo, D. S. Jeon, D. Gutierrez, M. H. Kim. "Practical Multiple Scattering for Rough Surfaces".
  ACM Trans. Graph. 37(6), 2018. The reflection model with every bounce count in closed form.
- Y. Tokuyoshi. "Unbiased VNDF Sampling for Backfacing Shading Normals". SIGGRAPH 2021 Talks, and its supplementary
  document. The single-sided normalisation of the visible normals.
- E. Heitz, E. d'Eon. "Importance Sampling Microfacet-Based BSDFs using the Distribution of Visible Normals". EGSR
  2014. V-cavity visible-normal sampling above the horizon.

The authors' Mitsuba plugin for Lee et al. is licensed for non-commercial use or under GPL version 3. Nothing here is
translated from it. The solver is the one written for our C++ pbrt-v4 fork (`util/vcavity.h`), ported as it is.

## 2. The groove solver

`VCavity.Trace(thetaI, tl, tr, maxN, outcomes)` takes the elevation of the incident direction in the cross section and
the tilts of the two facet normals (the groove may be non-symmetric), and returns every outcome: which facet is hit
first, the number of reflections, the exit elevation, and the fraction of the incident beam that takes that outcome.
The fractions are exact areas of the beam, found by unfolding the groove into a fan of mirror images in which a ray is
a straight line; nothing is estimated.

`ReachLeftFirst`, `ReachAll` and `WedgeBeam` give the related fractions that the dielectric needs: the part of the beam
that strikes a j-th wall, and the fate of a parallel beam that enters through a wall.

**Verification** (`VCavity.SolverAgainstTracedRays`). Rays are traced one at a time through the groove cross section,
with no unfolding. Seven grooves (symmetric and non-symmetric, tilts from 0.3 to 1.45 rad) at six elevations, 200,000
rays each:

- every outcome's probability agrees with the traced rays (80 outcomes, worst 1.8 standard deviations);
- exit elevations agree to 3e-15;
- the probabilities of a groove sum to 1 within 6e-16.

## 3. The conductor

`VCavityConductorBxDF(distribution, eta, k, maxBounces)`.

**Evaluation.** For a pair of directions the groove axis must be perpendicular to the sum of their projections on the
surface, which leaves two orientations. For each orientation, each first facet and each bounce count `n`, the groove
tilt that sends one direction to the other follows from the reflection geometry in closed form. Each such groove
contributes

    D(s) cos(alpha) sin(alpha) p_n / (2 n L)

to the density of the outgoing direction, where `alpha` is the tilt, `p_n` the solver's fraction for that outcome and
`L` the length of the summed projections. `f` is the sum of these terms times the product of the Fresnel reflectances
along each path, divided by the cosine of the outgoing direction. It is a value, not an estimate.

**Sampling.** A groove is drawn with `Sample_wm` for the normal direction (density `D cos`), the solver lists its
outcomes, and one is picked by its fraction. The weight is the product of the Fresnel reflectances. The density is the
evaluation sum without the Fresnel terms.

**Verification.**

| Check | Test | Result |
|---|---|---|
| One bounce equals Cook–Torrance with the Torrance–Sparrow masking, `D G F / (4 cos cos)` | `VCavity.FirstReflectionIsCookTorrance` | within 2e-5, on GGX (isotropic and anisotropic), Beckmann, Student-t |
| Reciprocity, 3,000 pairs | `BxDFCheck.VCavityConductor` | within 7e-6 |
| Albedo of a perfectly reflecting microsurface, 24 bounces | same | 0.9993 to 1.0000 |
| The sampling density integrates to 1 | `VCavity.DensityIntegratesToOne` | 0.997 to 1.002, each within its error bar |

The density grows as `1 / L` around the direction that is `wo` turned half a turn about the normal. Directions uniform
over the sphere estimate its integral with unbounded variance, so the last test integrates in polar coordinates about
that point, where the area element cancels the singularity.

Not ported from the fork: the non-symmetric groove mode of the BxDF (the solver supports it and is verified for it).

## 4. Visible normals for a back-facing shading normal

When a direction is below the horizon of the shading normal but above the true surface, the usual V-cavity density of
visible normals does not integrate to 1. Tokuyoshi's single-sided model fixes the normalisation. With the masking

    G(w, m) = min(2 (m . n) |w . n| / (w . m), 1)      for w . m > 0

the density of visible normals is `G(w, m) D(m) (w . m) / (H(w) |w . n|)`, where `H(w)` is the probability that a ray
along `w` meets the microsurface (1 above the horizon). Below the horizon the density has two forms, on either side of
the slope `3 cot(theta)` along the direction.

`MicrofacetDistribution` has `G_VCavity`, `HitProbability_VCavity`, `PDF_VCavity` and `Sample_wm_VCavity`. They are
written once in the base class in terms of the slopes of the unit distribution (roughness 1), which is what any
distribution becomes when directions are stretched by its roughness. With `F` the cumulative distribution of the slope
along one axis and `S(x)` the integral of `(cos(theta) - s sin(theta)) P2(s)` below `x`,

    H(w) = 2 F(3 cot(theta)) + L (S(cot(theta)) - S(3 cot(theta))) / |w . n|

where `theta` and `L` are the polar angle and the length of the stretched direction. Each distribution supplies four
small functions (`UnitSlopeCdf`, `UnitSlopeDensity`, `UnitSlopeMoment`, `SampleUnitSlopeAcross`). The paper gives `H`
for GGX and Beckmann only; this form reproduces both and also covers Student-t, which is our extension.

**Verification** (`MicrofacetDistribution.VCavity`, `MicrofacetDistribution.VCavityHitProbability`).

- `H` agrees with the paper's closed forms for GGX and Beckmann to 6e-8.
- The density integrates to 1.00000 by quadrature in 24 cases (three distributions, isotropic and anisotropic, two
  directions above the horizon and two below).
- 400,000 sampled normals match the density cell by cell (worst cell 4.3 standard deviations, limit 5).

No BxDF calls these yet. They take effect once `BSDF` is given the geometric normal, so that a back-facing shading
normal can be told from the underside of a surface.

## 5. The dielectric: what was built, and why it is not correct

`VCavityDielectricBxDF(distribution, ior, maxBounces)` is our extension of the groove model to a refracting interface.
It is ported from the fork, where it was marked as not validated.

**Its model.** One valley (an air wedge) cut into a medium that fills everything below the walls.

- From outside, the beam fills the aperture. At each wall the ray reflects or refracts by the Fresnel reflectance. A
  ray that refracts into the medium ends there.
- From inside, the ray meets one wall from below, chosen by the area it shows to the ray, at a point uniform on the
  wall. It reflects back into the medium and ends, or refracts into the valley, where it reflects between the walls,
  refracts back into the medium, or leaves through the aperture.

Every path is a deterministic function of the groove tilt and azimuth. The azimuth is fixed by the conservation of the
index times the component along the groove axis; the tilt solves one scalar equation, bracketed on a grid and bisected;
the Jacobian is a central difference of the forward map.

**Ground truth.** Two references were written, both in `util/vcavity_test.cs`, both following rays one at a time with
explicit intersections and Fresnel coin flips:

1. `ReferenceGroove`: one valley, by exactly the rules above.
2. `ReferencePeriodic`: a triangle wave of valleys and ridges, air above and medium below, where a ray is followed
   until it leaves upwards or downwards. A ridge of the medium is treated exactly as a valley of air is.

**What the references show.**

| Question | Test | Result |
|---|---|---|
| Does the BxDF's sampling do what its model says? | `VCavity.DielectricAgainstTracedRays` | yes: histograms of exit directions agree with reference 1 in 18 cases (worst cell 2.9 standard deviations), and the fraction of rays kept agrees to 0.001 |
| Is the BxDF's density consistent with its sampling? | `VCavity.DielectricDensityAgainstSampling` | broadly: band by band they agree within the noise of the test, with the density a few percent high in some bands (not resolved) |
| Is the model reciprocal? | `VCavity.DielectricModelReciprocity`, `BxDFCheck.VCavityDielectricReciprocityByCase` | **no** |
| Does the model conserve energy? | same | **no** |

The numbers, for GGX roughness 0.5 and index 1.5:

- Reflection with both directions above the surface: reciprocal to 2e-6. This is the conductor's code path.
- Transmission, pair by pair: the two directions of a pair differ by a median of 70 to 79 percent.
- Reflection with both directions in the medium: median difference 25 percent.
- Patches of directions (caps of 20 degrees), from rays alone with no density code: the one-valley model exchanges up
  to 42 percent more one way than the other (15 standard deviations). The periodic profile is reciprocal within noise
  (1.6 standard deviations).
- Rays kept: from outside 99.99 percent; from inside as low as 83 percent. The periodic profile keeps 99.97 percent or
  more.

**Why.** The model treats the two sides of the surface differently. Seen from the medium, the surface is a row of
ridges, and a ridge is a wedge of medium with the same angle as a valley. Three things follow that the one-valley
model leaves out:

1. A ray refracted into the medium can strike the other face of its ridge from inside, where it is totally reflected
   or refracts into the next valley. The model ends the ray instead.
2. A ray that arrives from the medium at a shallow angle sees a ridge partly hidden by the next one. The lit part of
   the wall is the part near the tip of the ridge; the model spreads the ray over the whole wall.
3. A ray reflected off the medium side of a wall towards the surface stays inside its ridge and reflects again. The
   model drops it, which is most of the lost energy.

The reflection-only model of Lee et al. is unaffected: a mirror never lets a ray through a wall, so one valley and a
row of valleys are the same thing.

## 6. Improving the dielectric

The periodic profile is the model to implement: reference 2 shows it is reciprocal and keeps all the energy, and it
reduces to the verified conductor when nothing is transmitted.

- **Sampling** is straightforward: follow one ray through the periodic profile, as the reference does.
- **Evaluation** is the work. The azimuth of the groove is still fixed by the pair of directions, and the tilt is
  still one unknown, but the paths are now sequences of reflections and refractions through alternating valleys and
  ridges. The plan is to compute, for one groove tilt, the complete list of outcomes (direction and probability) by
  splitting the beam at every wall, exactly as the solver does for a mirror groove, and then to find the tilts at
  which an outcome's direction passes through the wanted direction, as the current code does for its four path
  families.
- **Acceptance** is already written: the periodic reference, the patch reciprocity test, and the harness checks used
  for every other BxDF.

Until that is done the dielectric stays experimental and unused.

Smaller open items:

- The density of `VCavityDielectricBxDF` reads a few percent above its sampling in some bands. This may be noise or a
  root counted twice; it was not resolved because the model itself is to be replaced.
- The non-symmetric groove mode of the conductor is not ported.
- Evaluation cost, estimated from the run times of the tests and not profiled: roughly 20 microseconds for the
  conductor at 24 bounces and roughly 200 microseconds for the dielectric at 8. Both are far slower than the stock
  microfacet BxDFs.

## 7. Running the checks

    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "VCavity.*"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "MicrofacetDistribution.*"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "BxDFCheck.VCavityConductor"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "BxDFCheck.VCavityDielectric"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "BxDFCheck.VCavityDielectricReciprocityByCase"

Each test prints its measurements on lines that start with `MEASURE`.
