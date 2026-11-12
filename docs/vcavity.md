# V-cavity microsurfaces

This document describes the V-cavity work on the `pbr-materials` branch: what was implemented, how each part was
verified, and what is known to be wrong. The code is in `src/pbrt/util/vcavity.cs` (the groove solver),
`src/pbrt/util/scattering.cs` (the V-cavity functions of `MicrofacetDistribution`) and `src/pbrt/bxdfs.cs`
(`VCavityConductorBxDF`). The tests are `util/vcavity_test.cs`,
`util/scattering_microfacet_test.cs` and `bxdfcheck_test.cs`.

Status in one table:

| Part | Source | Status |
|---|---|---|
| Groove solver (`VCavity.Trace`) | our C++ fork, after Lee et al. 2018 | verified exact |
| `VCavityConductorBxDF` | our C++ fork, after Lee et al. 2018 | verified: reciprocal, energy conserving |
| V-cavity visible normals below the horizon | Tokuyoshi 2021; Student-t is our extension | verified |
| Dielectric V-cavity | ours | **removed**: the one-valley model was not reciprocal and lost energy (section 5) |
| Periodic-profile reference | ours | reciprocal, energy conserving; the model a dielectric has to follow |
| Polarised `VCavityConductorBxDF` | ours | verified (`docs/polarised-microfacets.md`) |

No material uses these BxDFs yet.

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

## 5. The dielectric: removed, and how to implement it properly

A transmissive V-cavity BxDF was ported from our C++ fork, tested, and removed. This section records what was
learned, so that the next attempt starts from the right model.

### 5.1 What was tried

The fork's model was one valley (an air wedge) cut into a medium that fills everything below the walls. From outside,
a ray that refracts into the medium ends there. From inside, a ray meets one wall from below at a point uniform on it,
and either reflects back into the medium and ends, or refracts into the valley and is followed until it leaves.

Two references were written that follow rays one at a time with explicit intersections and Fresnel coin flips: one
for that single valley, and one for a periodic profile (a triangle wave of valleys and ridges, air above and medium
below, a ray followed until it leaves upwards or downwards).

| Question | Result (GGX roughness 0.5, index 1.5) |
|---|---|
| Did the BxDF's sampling follow its model? | Yes: exit-direction histograms matched the one-valley reference in 18 cases (worst cell 2.9 standard deviations). |
| Is the one-valley model reciprocal? | No. Patches of directions exchanged up to 42 percent more one way than the other (15 standard deviations); pair by pair, transmission differed by a median of 70 to 79 percent. |
| Does it conserve energy? | No. From inside the medium it kept as little as 83 percent of the rays. |
| Is the periodic profile reciprocal, and does it conserve energy? | Yes: reciprocal within noise (1.6 standard deviations), at least 99.97 percent of rays kept. |

The port was faithful; the model was the problem. Seen from the medium the surface is a row of ridges, and a ridge is
a wedge of medium with the same angle as a valley. The one-valley model left out three things:

1. A ray refracted into the medium can strike the other face of its ridge from inside, where it is totally reflected
   or refracts into the next valley.
2. A ray arriving from the medium at a shallow angle sees a ridge partly hidden by the next one, and lights only the
   part of the wall near the tip of the ridge.
3. A ray reflected off the medium side of a wall towards the surface stays inside its ridge and reflects again.

The reflection-only model of Lee et al. is unaffected: a mirror never lets a ray through a wall, so one valley and a
row of valleys are the same thing.

The periodic reference and its reciprocity test are kept in `util/vcavity_test.cs`
(`VCavity.PeriodicDielectricReferenceIsReciprocal`). The BxDF, the single-valley reference and the solver routines
that only it used (`ReachLeftFirst`, `ReachAll`, `WedgeBeam`) are gone; they are in the history at commit `116c6d4`.

### 5.2 The model to implement

The periodic profile: grooves of one tilt side by side, so that the cross section is a triangle wave. Each groove
tilt is still drawn with the density `D(s) (s . n)`, and a patch of surface has one tilt. A ray in the profile is
always in one of two kinds of wedge, a valley of air or a ridge of medium, and the two have the same angle. Crossing a
wall moves the ray from one kind to the other.

Two facts carry over from the mirror groove and make the problem tractable:

- **The groove azimuth is fixed by the pair of directions.** Every wall is parallel to the groove axis, so the index
  times the component of the direction along the axis is conserved through any number of reflections and refractions:
  `n_o o_u + n_i i_u = 0`, with `n` the index on the side of each direction. The axis is perpendicular to
  `(n_o wo + n_i wi)` projected on the surface, which leaves two orientations.
- **The tilt is one unknown.** For a given sequence of events (which wall, reflect or refract), the exit direction is a
  smooth function of the tilt, and the density of the exit direction is the density of the tilt divided by the
  Jacobian of that function.

### 5.3 Sampling

Follow one ray through the periodic profile, as `ReferencePeriodic` does: draw a groove, enter at a point uniform over
one period, and at each wall reflect or refract by the Fresnel reflectance until the ray leaves the band between the
valley bottoms and the ridge tops. The weight is 1 (times `1 / eta^2` for radiance carried across the interface).
This part can be taken from the reference almost as it is.

### 5.4 Evaluation

This is the work. For a pair of directions the value is

    f cos = sum over orientations, over event sequences q, over roots alpha of o_q(alpha) = wi of
            P_q(alpha) D(alpha) sin(alpha) cos(alpha) / |J_q(alpha)|

where `P_q(alpha)` is the probability that a ray in a groove of tilt `alpha` takes the sequence `q`: the fraction of
entry positions that produce that sequence of walls, times the Fresnel factors of its events.

1. **Outcome list for one tilt.** Write `Outcomes(alpha, wo)`, which returns every sequence with its direction and
   probability. It splits the beam at every wall: the entry positions that reach a wall form intervals, and each
   interval continues as a reflected beam and a refracted beam with the Fresnel weights. A beam that enters a ridge or
   a valley through a wall is a parallel beam uniform on an interval of that wall, which is what the removed
   `WedgeBeam` handled for a whole wall; it has to take an interval. Stop a branch when its probability falls below a
   threshold or its depth passes `maxBounces`.
   *Check:* the outcomes of one tilt against `ReferencePeriodic` for that tilt (probabilities and directions), the
   same way `VCavity.SolverAgainstTracedRays` checks the mirror solver.
2. **Roots in the tilt.** Scan the tilt on a grid. A sequence is identified by its string of events, so the same
   sequence can be followed from one grid point to the next; where the residual `(o_q(alpha) - wi) . t` changes sign,
   bisect. Where a sequence exists on one side of an interval only, bisect the boundary of its existence first. This is
   what the removed code did for its four families; here the families are whatever `Outcomes` returns.
3. **Jacobian.** A central difference of `o_q` in the tilt and in the azimuth, as before, or the analytic derivative
   (each event is a reflection or a refraction about a normal that turns with the tilt).
4. **Cost control.** The number of sequences grows with depth. Most of the energy is in short ones; order the
   branches by probability and cut the tail, and measure what the cut loses with the reference.

### 5.5 Acceptance

A dielectric V-cavity BxDF is correct when all of these hold:

- its sampled directions match `ReferencePeriodic` (histogram test, as was done for the single valley);
- its density integrates to the fraction of rays kept, which is 1 up to the depth cut;
- its density matches its sampling cell by cell;
- `f(wo, wi, Radiance) = f(wi, wo, Importance)` pair by pair, for reflection on both sides and for transmission;
- with a mirror in place of the dielectric it reproduces `VCavityConductorBxDF`.

### 5.6 Open items elsewhere

- The non-symmetric groove mode of the conductor is not ported.
- Evaluation cost of the conductor, estimated from the run times of the tests and not profiled: roughly 20
  microseconds at 24 bounces.

## 6. Running the checks

    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "VCavity.*"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "MicrofacetDistribution.*"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "BxDFCheck.VCavityConductor"
    dotnet cmd/bin/Release/net10.0/pbrt_test.dll --test-filter "PolarisedMicrofacet.*"

Each test prints its measurements on lines that start with `MEASURE`.
