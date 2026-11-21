# Path-space Markov chain integrators (branch `pbr-mcmcbidir`)

Design notes and status. The file is updated as pieces land.

## Status

| Piece | State |
|---|---|
| `volpathmlt`: regeneration of the light-side tail with volpath's two strategies (mixture density) | implemented (`cpu/pathspace.cs`), tested |
| `volbidirmlt`: Veach's bidirectional mutation, any deleted range and every split, with media | implemented, tested (fog: see the open point) |
| lens perturbation (both integrators) | implemented, tested |
| caustic perturbation (both integrators) | implemented, tested |
| specular manifold core (`cpu/manifold.cs`): constraint, Jacobian, block solve, walk, area ratio | implemented, tested (`manifold_test.cs`) |
| manifold perturbation (both integrators; `"bool manifoldperturbation"`, off unless asked for) | implemented, tested |
| half vector space core (`cpu/manifold.cs`): world-space half vectors, Jacobian, Newton solve, change of variables | implemented, tested |
| half vector perturbations with the breakup (`"bool halfvectorperturbation"`, off unless asked for) | implemented, tested |
| `InvertSample` on `BxDF`, `BSDF`, the phase function and the microfacet distribution (approved) | implemented, tested (`invertsample_test.cs`) |
| fused state spaces: the small step of primary sample space as a path-space mutation (`"bool samplespaceperturbation"`) | implemented, tested |
| the polarised option (`"bool polarised"`) | implemented, tested |
| lights without an area (point, spot, goniometric, projection) | implemented, tested |
| half vector steps from ray differentials (Hanika 2015, section 3) | implemented, tested |
| multi-chain perturbation (`"bool multichainperturbation"`) | implemented, tested |
| specular next-event estimation (`"bool specularnee"`), and the plain Monte Carlo mode (`"bool montecarlo"`) | implemented, tested |
| emission of media | implemented, in validation |
| the Nelder-Mead solver (Granizo-Hidalgo 2024) and the slope-space triangle solver (Loubet 2020) for specular next-event (`"string sneesolver"`) | implemented, in validation |
| Jump Restore in path space (`volpathjrlt`, `volbidirjrlt`; Holl 2025) | implemented, tested (six runs sets, with and without the fused set) |
| specular polynomials, one bounce (Fan 2024; `"string sneesolver" "polynomial"`) | implemented, unit-tested, in image validation |
| Bernstein bounds for caustics (Fan 2025; `"string sneesolver" "bernstein"`, `cpu/causticbounds.cs`) | one specular vertex, point lights, triangle receivers: the precomputed position bounds and the sampling of triangles implemented, first test passes; the irradiance bound of the paper replaced by an estimate; two specular vertices not done |
| specular polynomials with two specular vertices (exact roots by a polish of the manifold walk) | not started |
| portal; distant, infinite and thermal lights; other cameras; exact stochastic BxDFs | not started |

Both integrators are `PathSpaceMLTIntegrator` (one core, a flag for the splits).

What `volpathmlt` does not do yet: lights other than diffuse area lights; emissive media; BxDFs whose
value is a stochastic estimate (layered and microsurface walks) are not exact; a connection through a homogeneous medium is a
ratio tracking estimate, not the closed form.

Parameters: `maxdepth` (5), `bootstrapsamples` (1000000), `integralsamples` (1000000), `chains` (1000), `mutationsperpixel`
(100), `largestepprobability` (0.3: the share of mutations that make a whole new path), `lensperturbation` (true),
`lensperturbationprobability` (0.3), `lensperturbationradius` (0.05 of the film), `causticperturbation` (true),
`causticperturbationprobability` (0.2), `causticperturbationangle` (0.1 rad), `manifoldperturbation` (false),
`manifoldperturbationprobability` (0.3; it uses the radius and the angle of the other two), `halfvectorperturbation`
(false), `halfvectorperturbationprobability` (0.3), `halfvectorperturbationangle` (0.02 rad), `samplespaceperturbation` (false),
`samplespaceperturbationprobability` (0.3), `sigma` (0.01), `seed` (0), `weightedseeds` (false: a chain
carries the weight of its bootstrap path instead of being chosen by it; unbiased for any bootstrap size, noisier), `diagnose`
(0; n > 0: the test of the chain over n mutations; -1: the test of the splits).

The integral of the luminance, b, is the mean weight of `integralsamples` bootstrap paths made with every split (for both
integrators). The estimator of `mlt` is not used for it: see the camera connection below.

Tests so far (96x72, means of whole images over independent runs against a converged reference, ratio +- standard error):

| Scene | Mutations | Runs | Ratio |
|---|---|---|---|
| diffuse box, area light | tail regeneration | 16 (300k bootstrap) | 0.9969 +- 0.0020 |
| diffuse box, area light | tail regeneration + lens | 16 (300k bootstrap) | 0.9982 +- 0.0015 |
| box filled with a homogeneous medium | tail regeneration | 8 (40k bootstrap) | 1.0004 +- 0.0064 |
| box filled with a homogeneous medium | tail regeneration + lens | 16 (300k bootstrap) | 0.9974 +- 0.0018 |
| box filled with a homogeneous medium | tail regeneration | 24 (2M bootstrap) | 1.0017 +- 0.0022, blocks within 1.7 s.e. |
| box with a glass sphere | tail regeneration + lens | 24 (2M bootstrap) | 1.0010 +- 0.0027, blocks within 1.2 s.e. |

Second round, after the rewrite on the shared core (16 runs each, 512 mutations per pixel, 1M bootstrap, 200 chains):

| Scene | Integrator | Ratio | Blocks |
|---|---|---|---|
| diffuse box | `volbidirmlt` | 0.9999 +- 0.0005 | within 1.6 s.e., sum of squares 8.9 / 12 |
| diffuse box | `volpathmlt` | 0.9996 +- 0.0005 | within 1.9 s.e., 7.7 / 12 |
| fog | `volbidirmlt` | 1.0000 +- 0.0011 | up to 3.4 s.e. (1.2% low), 34.8 / 12 with 1M bootstrap paths |
| fog | `volbidirmlt`, 6M bootstrap | 1.0009 +- 0.0010 | within 1.6 s.e., 8.9 / 12 |
| glass sphere | `volbidirmlt` | 1.0012 +- 0.0008 | within 2.2 s.e., 19.7 / 12 (reference noise not counted) |

Third round, with the caustic perturbation (lens 0.3, caustic 0.2, mutation 0.5; 16 runs each, 2M bootstrap):

| Scene | Integrator | Ratio | Blocks |
|---|---|---|---|
| glass sphere | `volbidirmlt` | 1.0001 +- 0.0006 | within 2.1 s.e., 14.2 / 12 |
| glass sphere | `volpathmlt` | 0.9998 +- 0.0007 | within 1.0 s.e., 3.9 / 12 |
| diffuse box | `volbidirmlt` | 0.9993 +- 0.0005 | within 2.7 s.e. (0.7%), 27.6 / 12; the reference's own noise (about 0.2% a block) is not counted |

The chain test with all three mutations: glass, no class beyond 2 standard errors; fog, one of 27 at 2.5.

The test of the splits (`"integer diagnose" -1`): a split that can make every path of a length estimates the light of that
length on its own, so the mean weights of the splits of a length must agree. On the box and in the fog they do (box: within
0.2 to 3%, each within its error); with the glass sphere the splits that would connect at a specular vertex are lower, as they
must be, and l = 0 (which makes every path) is the largest.

The media around a light vertex come from the geometry (`BDPT.StackAtPoint`): an intersection does not record a medium that is
the same on both sides of its surface, so a walk from a light vertex that a camera walk had found started in no medium.

Seeding: the bootstrap is a self-normalised resampling, and with too few paths for the length of the chains the image blocks
drift (fog, 3.5M mutations: visible with 1M bootstrap paths, gone with 6M). A million is the default; short test renders need
more.

The camera connection: the camera's importance (`SampleWi`) is normalised over the pixels of the image, and camera rays are
sampled over the area the pixel filter reaches from (`SampleBounds`) with the weight one. The split that connects a light
subpath to the camera was 4.9% too strong until its importance was multiplied by pixel area / sample area (96x72 image,
radius 1.5 filter: 6912 / 7252). pbrt-v4's `mlt` combines its t = 1 strategy with the others the same way, unscaled.

Findings:

- The weights of this integrator's bootstrap paths have a long tail. With 40000 to 300000 of them the chains were seeded
  unevenly, and image blocks in the medium were 1.5 to 2.7% off at 2 to 3 standard errors, with a pattern that changed from test
  to test. With 2000000 bootstrap paths: fog, tail regeneration, 24 runs, 1.0017 +- 0.0022, all 12 blocks within 1.7
  standard errors (sum of squared deviations 11.3). The default is now 1000000 (they cost about 5 s per million on 2 cores).
- `"integer diagnose" n` tests the chain alone: bootstrap paths weighted by c / density have the target's distribution, so the
  weighted share of every class of paths (length, vertices in media, block of the film) must be the same after n mutations.
  16 million paths, 30 mutations: fog without the lens perturbation, largest change 1.9 standard errors; fog with it, 2.5;
  glass sphere (8 million), 1.7. The test does not see an error that the bootstrap weights and the acceptance share, so the
  images against a reference are still needed.
- The integral b must come from samples that differ between runs (it was shared between seeds at first, which hid its noise
  from the standard errors).

## Design of the bidirectional version (`volbidirmlt`)

Every vertex keeps, for the directions it has in its path (wCam to its camera-side neighbour, wLight to its light-side one):

    C     = f(wCam, wLight, Radiance) |ns . wLight|          (a phase function: its value; a specular lobe: the factor of its delta)
    stepR = C / pdf(wCam -> wLight, Radiance)                 sampling the light-side direction from the camera side
    D     = C |ng . wCam| / |ng . wLight|                      (a medium: C)
    stepI = D / pdf(wLight -> wCam, Importance)               sampling the camera-side direction from the light side

    light vertex:   D = Le |n . wCam|,  stepI = D / pdfDir,  position weight 1 / (P(light) pdfArea)
    camera vertex:  stepR = the weight of the camera ray;  C = We cos (for a connection to the camera)

and both stacks of media (on its camera-side and on its light-side segment). For a specular vertex the two densities and C come
from sampling the BSDF, in each mode, until the lobe of its kind comes up. With these the value over the density of any piece
of a path under any split is a product of stored terms:

    light-side walk vertices: stepI, the last one D;  camera-side walk vertices: stepR, the last one C;
    the connection: 1 / r^2 and segU / segL[0] of its segment;  every other segment: segU;  vtx at medium vertices.

The mutation deletes the vertices strictly between a and b (a from -1, the light vertex included, to b at most the camera),
makes n new ones with a split (l from the light side, n - l from the camera side) and accepts with the density of the mixture
over the splits, as `volpathmlt` does with its two. `volpathmlt` is the case a = -1, l in {0, 1}.

## Scope

Volumetric only: every integrator here follows paths through media as well as surfaces, on the vertices of the existing
volumetric `bdpt`. The existing `mlt`, `erpt`, `pmcer`, `jrlt`/`smmc`, `bdpt` and `volpath` are not changed and are what the new
integrators are compared against.

| Integrator | Chain | Source |
|---|---|---|
| `volpathmlt` | path space, proposals grown from the camera side only (the strategies of `volpath`: a walk and a light sample) | Veach and Guibas 1997 |
| `volbidirmlt` | path space, bidirectional mutation with every split, plus lens, caustic and multi-chain perturbations | Veach and Guibas 1997 |
| later, on the same core | manifold perturbation; half vector space light transport with the breakup mutation; fused state spaces (multiplexed MLT + path space); portal perturbation; specular manifold next-event estimation | Jakob 2012; Kaplanyan 2014, Hanika 2015; Otsu 2017; Otsu 2020; Zeltner 2020, Hanika 2015, Granizo-Hidalgo 2024, Loubet 2020 |

## Sources and licences

- Mitsuba 0.5 (`src/libbidir`, `src/integrators/mlt`, `erpt`): GPL v3. Read for its structure (path, mutators, `Q`, suitability,
  mutation record). No code is copied; the mutations are implemented from the papers.
- Lightmetrica v2 (`plugin/renderer_inversemap`) and `lm3_portal_mlt`: MIT. Reference for the fused state spaces and the portal
  perturbation.

## Code shape (pbrt-v4)

- A path is an array of the existing `Vertex`, as `ConnectBDPT` and `MISWeight` see it. New fields on `Vertex` are marked
  "(not in pbrt-v4)" and are not read by the existing bdpt.
- Free functions beside `GenerateCameraSubpath` / `ConnectBDPT`; integrators shaped like `MLTIntegrator` (`Create`, `Render`,
  `ToString`, `ParallelFor`, `ScratchBuffer`, the statistics and the progress reporter).
- Mutations are small classes: `BidirectionalMutation`, `LensPerturbation`, `CausticPerturbation`, `MultiChainPerturbation`, later
  `ManifoldPerturbation`, `HalfVectorPerturbation`, `PortalPerturbation`. Each has `Sample(current, proposal, record)` and
  `Q(source, proposal, record)`; the acceptance calls `Q` forward and reversed, each times the probability that the mutation was
  chosen for that path.
- Parameters in pbrt's style and with the names `mlt` has: `maxdepth`, `bootstrapsamples`, `chains`, `mutationsperpixel`,
  `sigma`, `regularize`; one bool per mutation (`bidirectionalmutation`, `lensperturbation`, `causticperturbation`,
  `multichainperturbation`).

## Media: the state is a path of the null-collision formulation

pbrt-v4 samples collisions by delta tracking against a majorant and writes the path integral with the null collisions in it, so
every sampled path has a density in closed form (unlike Mitsuba's Woodcock mode, which is why Mitsuba needs its biased Simpson
quadrature for bidirectional methods). The chain keeps that formulation:

- The state is the path with its null collisions. Only their sufficient statistics are stored, per segment, in the fields the
  port's bdpt already has on the vertex a segment arrives at:
  - `segU[lambda]` = probability of the segment as a walk samples it, over the hero wavelength's (so `segU[0]` = 1, and the value of
    the segment over the hero walk's density is `segU`);
  - `segL[lambda]` = probability of the segment as a connection samples it (ratio tracking: every majorant event is a null
    collision), over the hero walk's density;
  - `vtx` = the scattering coefficients at a medium vertex over the hero's.
- With these, for the hero wavelength: a segment sampled by a walk has value / density = 1; a segment made by a connection has
  value / density = 1 / `segL[0]`. Both are exact for the stored null collisions, so the Metropolis ratio is exact (no estimate of
  a transmittance enters it).
- A mutation that re-traces or connects a segment samples new null collisions for it; they are part of the proposal and their
  density is in `Q` through the same ratios.
- The target is the hero wavelength's contribution (times the importance of the pixel, as `mlt`'s importance maps do); the other
  wavelengths ride along through `segU` and `vtx`, as in `volpath`.
- Reference for validation: the port's own `bdpt` and `volpath` on homogeneous, heterogeneous and chromatic media.

## Balance per split

Veach's bidirectional mutation sums the densities of every way to produce the same proposal. The first version balances each
way on its own (the split is part of the proposal, the reverse move deletes what was added and re-adds what was deleted with the
matching split), which needs value / density of one strategy at a time and no sum. The summed form is an efficiency improvement
for later; both are exact.

## Polarisation, spectrum, time

- The polarised option carries the Mueller matrices of the vertices and rebuilds the chain in order after a mutation (they do
  not commute); the target is the luminance of the result.
- Wavelengths and time are part of the state and have a small perturbation of their own.

## Seeding, target, accumulation

- Seeds are samples of the existing bdpt strategies (the bootstrap of `mlt`), kept as their vertices.
- Target: luminance, or the importance maps and the multi-stage schedule of `mlt` (`mltimportance.cs`).
- Both the current and the proposed path are deposited, weighted by the acceptance probability.
- `bool separatedirect`: direct lighting by plain Monte Carlo, outside the chain.

## Validation

- Each integrator: mean image against `bdpt` on small scenes (surfaces; homogeneous, heterogeneous and chromatic media), with the
  standard error from independent chains. Random or independent sampler only.
- Each mutation: `Q` forward and reversed agree on random pairs; the mutation with the bidirectional one converges to the
  reference.
- Later: the manifold solver on analytic cases and against finite differences; the inverse maps by round trips and their
  Jacobian against the sampling density.

## Build order

1. Walk from an interior vertex (`ExtendSubpath`), join two subpaths into one path, value and the per-strategy value / density
   of a path from its vertices; tested against `ConnectBDPT` and `MISWeight`.
2. `volbidirmlt` with the bidirectional mutation alone; then `volpathmlt` as its camera-side restriction.
3. Lens, caustic and multi-chain perturbations.
4. Manifold solver and manifold perturbation; half vector space; fused state spaces (needs an optional `InvertSample` on `BxDF`:
   not added without approval); portal perturbation; specular manifold next-event estimation.

## Open

- `InvertSample` on `BxDF` (an addition to a pbrt-v4 class) is needed only for the fused state spaces and waits for approval.
- The walk-based BxDFs (layered stack, Facet Forge, the Heitz walks, glint) have no inverse: a path through one stays in path
  space.
- Plain manifold next-event estimation is biased where several solutions exist; only the unbiased estimator of specular
  manifold sampling is planned.

## The specular manifold core (`cpu/manifold.cs`)

Conventions after Lightmetrica v2, `plugin/renderer_inversemap/manifoldutils.cpp` (MIT, Hisanari Otsu 2015), which follows Jakob
and Marschner 2012.

- A chain is x_0 (fixed), x_1 .. x_n (specular, on surfaces), x_n+1 (the end). `ManifoldVertex` has the position, the tangents
  of the surface itself (`dpdu`, `dpdv`: the vertex moves on it), the shading normal and its derivatives (the specular lobe is
  about that normal), the relative index (the side of the next vertex over that of the previous one) and whether the chain is
  reflected there.
- The constraint of a specular vertex is the generalised half vector normalize(wi + eta wo) along its two tangents.
  `Jacobian` gives its derivatives for the (u, v) of x_i-1, x_i, x_i+1; `Solve` is the block tridiagonal solve; `Offsets` the
  moves of the specular vertices for a move of the end; `AreaRatio` is |det(d x_1 / d x_n+1)| as a ratio of areas.
- `Walk`: move the end toward the target, solve for the offsets, move x_1 by a step, trace the chain again from x_0 through
  x_1, halve the step when the end moves away from the target; stop at a relative distance of 1e-4 or after 50 steps. The end
  either lands on the surface that the last ray hits, or, with an end plane, on a plane through the target: the last segment
  is then not traced, and the target can be anywhere (a point in a medium).
- Tests (spheres, whose derivatives are exact): the Jacobian against central differences (4.8e-4 relative); walks through a
  glass ball and off a mirror ball arrive within 5e-4 and find the chain that was traced to the target; the area ratio against
  the areas that four moves of the end span (glass 1.9457e-1 against 1.9456e-1, mirror 6.7557e-3 against 6.7559e-3).

`Vertex.eta` keeps the relative index of a specular vertex (`BSDFSample.eta`; light side over camera side).

## The manifold perturbation

Jakob and Marschner 2012, section 6. Three vertices that are not specular: a, where the path is perturbed; b, the next one,
which moves; c, the one after, which stays. Any chain of specular vertices between a and b is traced again, as in the lens and
caustic perturbations; the chain between b and c is found by a walk on its manifold, from the chain of the current path (first
vertex c, end at the old b) to the new b. The perturbation goes toward the light or toward the camera, from any a that has a b
and a c; the place is chosen uniformly, and the perturbed path has the same places. At the camera the point on the film moves
(the lens kernel); elsewhere the direction turns (the caustic kernel). With c the camera, the point on the film is the one that
sees the last vertex of the chain.

- Toward the light from the camera with no chain between b and c it is the lens perturbation; toward the camera with b seen by
  the camera, the caustic perturbation. With a chain between b and c it moves paths that have specular vertices at both ends
  of a diffuse or medium vertex (a floor seen and lit through water), which neither of the two can.
- b can be in a medium: the end of the walk is on a plane through it.
- The move has to come back: after the walk, a walk from the new chain to the old b has to find the old chain (within 2e-3 of
  the scale of the chain), or the move is rejected. Walks are deterministic, so the test is the same in both directions.
- The segments of the chain between b and c are connections (ratio tracking), with their null collisions kept as for any
  other segment.
- Weight (the contribution over what the move samples, up to the symmetric kernel): toward the light,
  y(UnsampledValue) |ng(c) . wCam| M / prod segL; toward the camera, the same with the terms of a walk from the light for
  a .. b (`TowardCameraValue`) and no cosine. M (`ChainMeasure`) is the solid angle at the camera-side vertex of the chain over
  the area across the ray at its light-side vertex: |ng(x_1) . w| / r^2 times the area ratio of the chain from the camera side
  (1 / r^2 with no chain). The chain is always measured from its camera side, where the terms of the vertices are radiance
  terms: the two directions differ by the squared indices.

Tests (manifold perturbation in place of the other two, probability 0.5, 16 runs of 512 mutations per pixel, 2M bootstrap):

| Scene | Ratio | Blocks |
|---|---|---|
| glass sphere | 1.0002 +- 0.0010 | within 2.0 s.e., 9.8 / 12 |
| glass sphere in fog | 1.0004 +- 0.0015 | one block at 3.4 s.e. (+1.9%), 18.1 / 12; lens and caustic instead, same settings: 1.0001 +- 0.0014, one block at 4.0 s.e. (-2.5%), 25.9 / 12 (the seeding of the fog at 2M bootstrap, see above) |

| fog outside a clear glass sphere (paths cross between media), 6M bootstrap | 0.9992 +- 0.0011 | within 1.9 s.e., 14.2 / 12 |

Finding (media): `MediumTracking.CrossSurface()` takes the side a path arrives from out of the interaction's `wo`, which is the
side of the walk that made the vertex. A vertex of a chain found by the manifold walk is made from the other side, and its
stack of media was then not crossed (points of light in empty space, in a pool with scattering water). The stacks of the two
sides of a vertex are now made from its own two directions (`PathSpace.LightSideStack`, `CameraSideStack`), whichever walk
made it, and `ConnectSegment` takes the stack of the side that the segment leaves on. No earlier test had two different media
(the fog was on both sides of every surface): the scene above is the first.

Chain test, glass, the manifold perturbation alone: 30 mutations, none of 12 film classes beyond 2.4 s.e.; 120 mutations, none
beyond 2.3 (no drift with the number of mutations).

Finding (the pool scene: a rippled water surface over a diffuse floor, a small light above): every path to the floor has a
specular vertex at both ends, so only the walk from the camera that happens to hit the light makes one. The mean of the
bootstrap weights then has a long tail: with 1M paths b came out at 0.05 to 0.065 over five runs (and 0.049 with `mlt`), with
20M at 0.116 and 0.124; `volpath` and `bdpt` give 0.124. The images are right in shape and wrong in scale until
`integralsamples` is large enough. The specular next-event estimator will remove this.

## Half vector space (Kaplanyan 2014, Hanika 2015)

Core (`SpecularManifold.HalfVectorJacobian`, `SolveHalfVectors`, `HalfVectorMeasure`). A chain with fixed ends is given by the
half vectors of its inner vertices. They are taken as unit vectors of the world, not as coordinates in the tangent frame of
each vertex: the change of variables then needs the first derivatives of the positions only, and no field of tangent frames
(whose turning from point to point would otherwise have to be in the Jacobian). A specular vertex holds its half vector to its
normal; any other vertex holds it to a target. Newton's method, every step traced from the fixed first vertex (a vertex after
a specular one is where the specular ray lands), a step that does not bring the constraints closer halved. The change of
variables is |det J| / |det J_SS| over the areas of the tangents of the vertices that are not specular (J_SS: the block of the
specular vertices, which follow on their manifold).

Tests (spheres): Jacobian of a rough vertex and a mirror against central differences 3.2e-4; the solid angle of the half vector
over the area of a rough vertex 5.9188e-2 against 5.9181e-2 by four moves, and with a mirror after it 5.5269e-2 against
5.5264e-2; the solve finds another chain from its half vectors to 1e-7.

Perturbations (`ChainPerturbation`, which is also the manifold perturbation: one function, three modes):

- mode 1, the breakup (Hanika 2015, section 5): as the manifold perturbation, but c is any later vertex, and the chain between
  the breakup vertex b and c has vertices that are not specular; they keep their half vectors but for a small Gaussian step
  (0.1 of `halfvectorperturbationangle`). The part from a to b is the lens / caustic / multi-chain side, the part from b to c
  the half vector side.
- mode 2, half vector space alone: nothing is traced; two vertices stay and the half vectors of the chain between them take a
  Gaussian step (`halfvectorperturbationangle`, 0.02).
- The step of a half vector has a density that depends on the angle between the old and the new one only, so it is symmetric;
  the weight has, for the vertices that are not specular, the geometry between each two of them (cosine and `ChainMeasure`)
  over `HalfVectorMeasure`. The move has to come back, as for the manifold perturbation.
- Limits for now: the vertices with free half vectors reflect (no rough transmission); the chain is on surfaces (its ends can
  be anything, a point in a medium too); the step does not yet come from the ray differentials of Hanika 2015 section 3 (the
  breakup with the lens kernel gives the stratification on the film; the plane-plane domain for the size of the step is not
  in yet).

Tests (half vector perturbations in place of lens and caustic, probability 0.5, both modes; 16 runs, 512 mutations per pixel,
2M bootstrap):

| Scene | Ratio | Blocks |
|---|---|---|
| diffuse box | 0.9995 +- 0.0006 | within 0.6%; one at 3.0 s.e. (-0.3%, reference noise not counted), 16.2 / 12 |
| box with a rough metal floor (roughness 0.08) and a rough copper sphere (0.15) | 0.9996 +- 0.0007 | within 1.7 s.e., 7.7 / 12 |

At equal mutations on the glossy scene the half vector perturbations are no better than lens and caustic (noise of one run
over the image mean 0.092 against 0.088) and cost more per mutation: the scene has no transport that they alone reach, and the
step is a fixed angle, not yet the one from ray differentials. The manifold perturbation after the merge into
`ChainPerturbation` gives the same images to the last digit as before it (fog outside a clear glass sphere, 16 runs).

## Fused state spaces (Otsu 2017)

The paper keeps the state of multiplexed MLT (random numbers and a technique) and brings the path-space mutations to it through
the inverse of path sampling. Here the state is the path, and the mutation of multiplexed MLT is brought to it through the
same inverse: one chain has both kinds of mutation either way, and the inverse it needs is the same. The reason for this side:
the collisions of a segment (delta tracking with a seeded generator) have no inverse, and as part of a path they do not need
one.

Interface added to pbrt-v4 classes (approved; each marked "not in pbrt-v4"):

- `BxDF.InvertSample(wo, wi, mode)` returns `BxDFSampleInverse`: the two numbers `u`, and the interval of `uc` that takes the
  lobe of `wi` (the whole of [0, 1] when `uc` is not used). The default is unset: no inverse.
- `BSDF.InvertSample(woRender, wiRender, mode)`; `PhaseFunction.InvertSample_p(wo, wi)`;
  `MicrofacetDistribution.InvertSample_wm(w, wm, out u)`; `InvertHenyeyGreensteinSample`.
- With an inverse: diffuse, diffuse transmission, conductor and dielectric (smooth, and rough with the Trowbridge-Reitz
  distribution, whose spherical-cap sampling inverts in closed form), thin dielectric, Henyey-Greenstein (without the Rayleigh
  part). Without: the Beckmann, Student-t and vMF distributions (they sample by rejection), the layered and walk-based BxDFs.
- Test: 20000 random samples each, both sides and both lobes: every sample inverted, the direction sampled again from the
  inverse within 1.4e-5, `uc` always inside its interval.

The mutation (`SampleSpacePerturbation`): choose a split; the path is a sample of that split; invert the point on the film,
the direction that leaves the light and every scattering; move each number by a Gaussian step around the circle (`sigma`,
0.01, as in `mlt`); make the path of the moved numbers with the same split (`Regenerate` with a guide). The wavelengths, the
time, the point on the lens and the point on the light stay. What has no inverse is sampled anew: the collisions of every
segment, and a scattering without an inverse (then the new scattering must have none either, or the move has no way back and
is rejected; likewise the other way round). The weight is the contribution over the density of the split
(`KeptWeight * PieceWeight`), since the move's density is that density times a symmetric one. A number of the interval of a
lobe is drawn uniformly before the step, so a step across the boundary changes the lobe with the ratio of the lobes' own
probabilities.

Finding: the first version gave a specular lobe's inverse a fixed `u` (0.5, 0.5). A move from a specular vertex to a diffuse
one then had a density concentrated around the direction of that `u`, and the move back had none: the glass caustic came out
27% too bright (16 runs; the chain test over 40 mutations did not see it, the drift per mutation being small). A specular
inverse now says that any `u` gives the direction (`anyU`) and the chain takes one uniformly, as for `uc` in its interval.

Tests (the sample-space step in place of lens and caustic, probability 0.5; 16 runs of 512 mutations per pixel):

| Scene | Integrator | Ratio | Blocks |
|---|---|---|---|
| diffuse box | `volbidirmlt` | 0.9995 +- 0.0006 | within 0.6%; one at 2.9 s.e., 17.4 / 12 |
| rough metal floor and sphere | `volbidirmlt` | 0.9997 +- 0.0007 | within 1.2 s.e., 6.7 / 12 |
| glass sphere | `volpathmlt` | 1.0007 +- 0.0009 | within 1.9 s.e., 17.5 / 12 |
| fog outside a clear glass sphere | `volbidirmlt` | 0.9999 +- 0.0010 | within 1.9 s.e., 10.2 / 12 |

Not done: the inverse of the point on the light (needs an inverse of the shapes' area sampling), of the wavelengths and of
the lens.

## Polarisation (`"bool polarised" true`, with `analyser` and `measure` as for `bdpt`)

`SetMeasurement`: the Stokes vector of the emission (`BDPT.EmissionState`) goes through the Mueller matrix of every vertex from
the light to the camera, each with its scalar value divided out (`ConnectionAttenuation` for a pair of directions,
`SpecularAttenuation` for a specular lobe, `PhaseAttenuation` in a medium), times the scalar contribution, and the sensor
measures it (`MeasureCameraRay`). The chain follows the luminance of what is measured, which is the scalar luminance times a
factor of the path (`PathSpacePath.polarisation`). Every weight of every mutation is a contribution over a density, so the
acceptance takes the ratio of the two paths' factors, the bootstrap weight takes the factor, the film gets what is measured,
and no mutation changes. A Stokes parameter can be negative: the chain follows its size and the film gets the sign.

Test: glass sphere, analyser at 0, 16 runs against polarised `bdpt` (2048 spp): 0.9997 +- 0.0007, blocks within 0.9 s.e.,
4.4 / 12. The polarised reference differs from the unpolarised one by 5.7% in the block of the sphere's reflection, so the
test does see the polarisation.

## Half vector steps from ray differentials (Hanika 2015, section 3)

For the half vector mutation alone (mode 2) with the camera at an end of the chain. J is the Jacobian of the half vectors with
respect to the vertices; the block D_i of its inverse says how the vertex next to the camera moves with the half vector of
vertex i, and P (two small moves through `Camera.SampleWi`) how the point on the film moves with that vertex. The step of h_i
is S_i g, g a pair of unit Gaussians, with S_i the inverse of P D_i scaled to `halfvectorperturbationpixels` (2) over the
square root of the number of free half vectors: each half vector then moves the point on the film by about the same amount in
every direction. The singular values of S_i are clipped after the decomposition (the paper's figure 5d), to
[1e-5, 10 `halfvectorperturbationangle`]. The steps depend on the path, so the acceptance has the density of the move back
over that of the move, each evaluated for the half vectors that the chains really have (`LogStepDensity`).

Differences from the paper: the step is taken on the plane that touches the sphere at the current half vector (the plane-plane
domain is the plane that touches it at the normal); the clip is a fixed angle, not the width of the BSDF (the BxDF interface
has no roughness to ask for). `halfvectorperturbationpixels` 0 gives the fixed angle everywhere.

## Multi-chain perturbation (Veach 1997, section 5.3.3)

`MultiChainPerturbation`: the lens perturbation, continued. At a vertex that is not specular and is followed by a specular
chain, the direction that leaves it is the old one turned a little (the caustic kernel), and the walk goes on to the next
vertex that is not specular; the last one is connected to the vertex after it, which stays. Every turn is symmetric and every
traced vertex has its radiance terms, so the weight is the lens perturbation's.

## Lights without an area

Point, spot, goniometric and projection lights: the light vertex is the light's point; what it emits toward a direction is
what `SampleLi` gives at the distance 1; no cosine (`GeometricCosine` is 1); its density is the light sampler's probability;
its directions are sampled by `SampleLe` (always anew in the sample-space step). No walk arrives at it, so the split that
would (l = 0) has no density, and a path of one segment (the light seen by the camera) is left out, as `volpath` and `bdpt`
leave it out. The split test agrees over the splits for every length (box, point + spot light).

## Tests of the last four pieces (16 runs of 512 mutations per pixel)

| Scene | Mutations | Ratio | Blocks |
|---|---|---|---|
| rough metal floor and sphere | half vector with ray-differential steps (in place of lens, caustic) | 0.9996 +- 0.0007 | within 1.1 s.e., 4.1 / 12 |
| glass sphere | multi-chain in place of lens | 1.0006 +- 0.0010 | within 1.4 s.e., 5.8 / 12 |
| box, point + spot light | lens, caustic, manifold, sample-space | 0.9998 +- 0.0006 | within 2.3 s.e., 16.1 / 12 |
| the same in fog, `volbidirmlt` | the same | 1.0040 +- 0.0012 | up to 3.4 s.e., 36.0 / 12 |
| the same in fog, `volpathmlt` | the same | 1.0039 +- 0.0015 | one at 5.0 s.e., 38.3 / 12 |

The two fog runs share their estimate of b and are both 0.4% above the first `volpath` reference (image mean 0.17489). Two
more references give 0.17514 (`volpath`, another seed) and 0.17536 (`bdpt`): the references differ among themselves by as
much, and against their mean the runs are 0.27% high with 0.12% of their own error (box, 6M bootstrap, also at 16.1 / 12 in
its blocks without fog).

Noise of one run over the image mean on the glossy scene, equal mutations: lens + caustic 0.0880; half vector, fixed angle
0.0916; half vector, ray-differential steps 0.0915; the sample-space step 0.0724. The steps from ray differentials are right
and change nothing measurable here; the sample-space step is the one that helps.

## Specular next-event estimation (Zeltner 2020; the walk of Hanika 2015)

`"bool specularnee" true`. A bootstrap strategy (`SneePath`), for the seeds of the chains and for b; the mutations do not
change. A walk from the camera of a random number of segments reaches a vertex x (on a surface or in a medium); a point on a
light is sampled; a trial (`SneeTrial`) samples a direction at x by its BSDF, follows the ray through the specular surfaces it
meets (each lobe with its probability, up to 6 vertices) and walks on the manifold of that chain to the point on the light.
The probability that a trial finds a given chain is not known; its inverse is estimated without bias by the number of further
trials up to the first that finds the same chain again (the paper's algorithm 3). The weight is the contribution over the
densities of the walk and of the light point, with `ChainMeasure` for the solid angle at x over the area at the light, times
that number. The segments of the chain are connections (ratio tracking), so the chain can cross media.

- The density of the strategy is unknown, so it cannot be combined with the others by their densities. The paths it makes
  (a specular chain from the light to a vertex that is not specular and is not the camera, `InSneeClass`) are counted for it
  alone: half of the bootstrap paths are its own, and the other strategies' paths of that class are dropped. With the option
  on, caustics by light tracing are therefore not used for the seeds.
- A seed direction comes from the BSDF at x, not from a point sampled on the specular shapes as in the paper: no list of
  specular shapes is needed, at the price of trials that miss them.
- `sneetrials` (100000) stops an estimate; that is the only bias, and the number of stopped estimates is counted (0 in the
  pool).
- `"bool montecarlo" true` puts every bootstrap path on the film with its weight and runs no chain: a plain path-space Monte
  Carlo integrator with the same strategies (the splits of `volpathmlt` or `volbidirmlt`, and specular next-event estimation).

Tests (Monte Carlo mode, glass sphere, 16 runs of 256 samples per pixel): with specular next-event estimation
0.9990 +- 0.0011, blocks within 2.1 s.e., 15.5 / 12 (the caustic block 0.981 +- 0.015); without it (the mode alone)
1.0004 +- 0.0005, 16.2 / 12. Pool: Monte Carlo mode, 64 samples per pixel in 7 s, mean 0.1219 against 0.1210; MLT seeded by it
with the default 1M `integralsamples` gives 0.987 (clear) and 0.986 (scattering water) of the `volpath` means, where it gave
about a half without.

### The other two solvers (`"string sneesolver" "newton" | "neldermead" | "slopespace"`)

- `neldermead` (Granizo-Hidalgo and Holzschuch 2024): the trial is a Nelder-Mead search on the surface, with no derivative
  of the surface. Three directions sampled at x land on a specular surface: the first triangle. Every new point of the method
  is put back on the surface by the ray from x through it. The objective is the length of the half vector less the normal
  (the paper's equation 8); 50 steps and 1e-4, the paper's values. One specular vertex. The inverse probability is the same
  count of trials. As in the paper, the geometric term of the path found still comes from the derivatives (`ChainMeasure`).
- `slopespace` (Loubet et al. 2020, the case of a specular surface): no trials. In every triangle of the scene the slopes of
  the normal and of the half vector are taken linear between the corners, and the point where they are equal follows from two
  linear equations (the paper's equation 15); it is put on the surface by a ray from x and brought to the exact solution by
  the manifold walk. The solutions that several triangles lead to are counted once; one is taken uniformly and the inverse
  probability is their number. One specular vertex, on a triangle mesh (`SurfaceInteraction.onTriangle`,
  `Triangle.AllMeshes()`: two small additions to pbrt-v4 classes).
- Not done from Loubet 2020: the hierarchy with per-triangle energy bounds that prunes the triangles (every triangle is gone
  over, which is slow: 180 s where the Newton walk takes 7 s on the pool), and the slope-space integrals of rough (GGX)
  surfaces and of normal-mapped glints.
- The class of paths left to specular next-event estimation follows the solver (`InSneeClass`): one specular vertex for these
  two, on a triangle for `slopespace`.

- `polynomial` (Fan et al. 2024, "Specular Polynomials", one bounce; `cpu/specularpolynomial.cs`, from the paper, no code of
  the authors, whose archive is GPL): on a triangle with a normal interpolated between its corners, a specular vertex between
  two points is where two polynomials of its barycentric coordinates vanish: the coplanarity (degree 2) and the angle
  (degree 4 for a reflection, the product form; 6 for a refraction, the square form). v is eliminated by the resultant
  through the two roots of the quadratic; the resultant is sampled at Chebyshev points and all its real roots are isolated
  between the roots of its derivatives. pbrt's triangles interpolate their normals in this way, so a root is the chain: no
  walk, no trials. The caller keeps the roots that satisfy the law itself (the products and squares have others).
  Unit test: 2000 random triangles each for reflection and refraction, the chain among the roots in all of them, to 1.7e-5.
- Culling of the triangles for `polynomial`, both conservative: the six Bernstein coefficients of the coplanarity polynomial
  over the triangle (bilinear in a corner's position and a corner's normal: no polynomial is evaluated) must not have one
  sign; and the cone of the half vectors of the triangle must meet the cone of its normals. Without the second, the solver
  took 241 s where it now takes 12 s (64x48, 16 samples per pixel; the slope-space solver 13 s).
- From "Bernstein Bounds for Caustics" (Fan et al. 2025) only the property is used: a polynomial lies between its smallest
  and largest Bernstein coefficient. The paper's method (beams traced from the light through every triangle before the
  render, bounds of the position and of the irradiance of the caustic on the receivers, and the sampling of triangles by
  those bounds) is not implemented: it stores its bounds on receiving surfaces for one light, and here the receiver can be
  any point, in a medium too, and the light point is sampled anew for every path. Two bounces are not done either.

- `bernstein` (Fan et al. 2025, "Bernstein Bounds for Caustics"; `cpu/causticbounds.cs`, from the paper): a table built
  before the render, for every point light, specular triangle and receiving triangle. The point where the light of a piece of
  the specular triangle lands on the receiver's plane is a rational function of the coordinates on the specular triangle
  (for a refraction with the square root between a line and the line plus a remainder, the paper's remainder variable);
  the ratios of the Bernstein coefficients of its numerators and denominator give a box on the receiver. A piece whose
  denominator changes sign, or whose box is wider than a quarter of the receiver, is cut in four, up to three times. The
  boxes are kept by receiving triangle in a 32x32 grid. During the render a point of a receiving triangle gets the specular
  triangles whose boxes hold it; each is taken with the probability min(1, `sneetuples` (8) x its weight / the sum), its
  chains are found by the specular polynomials, and one chain is returned with the number found over the probability of
  its triangle. The class of paths: a point light, one specular vertex on a triangle, a point of a receiving triangle.
- Differences from the paper: the weight of a piece is the solid angle it subtends at the light over the area of its box,
  not the paper's irradiance bound by implicit differentiation (any probability above zero is unbiased; the bound would be
  tighter where a caustic focuses); the kind of a mesh (specular, index, receiver) comes from one ray onto its first
  triangle, so a mesh has one material and an index that does not depend on the wavelength; no hierarchy over the
  triangles; two specular vertices not done. `SurfaceInteraction` gained the triangle and the coordinates of a hit.
- First test (a point light over a rippled sheet of 4608 triangles, a floor of two, seen from under the sheet so that `bdpt`
  reaches the caustic; Monte Carlo mode, 64 samples per pixel, 96x72): the table has 2304 boxes and is built in 0.5 s;
  `bernstein` 0.2498 in 3.5 s; `polynomial` over all triangles 0.2498 in 17 s; `newton` 0.2494 in 3.0 s; `bdpt` 256 spp
  0.2533 in 20 s.

First renders (pool, Monte Carlo mode, 64 samples per pixel, means against 0.1210; specular polynomials 0.1206 in 134 s): Newton 0.1219 in 7 s; Nelder-Mead 0.1254
in 18 s; slope-space 0.1201 in 180 s.

## Emission of media

A walk from the camera can end where a medium emits: at an absorption (`TraceSegment` with `emissionEnds`, only for the last
segment of a split that walks to the light), the vertex is a light vertex with no light, its emission the medium's, the same
in every direction, with the absorption coefficient where a scattering vertex has the scattering coefficient. No strategy
starts from it (its densities from the light side are zero), as the port's `bdpt` gathers the emission of media from the
camera side only. The perturbations keep or reconnect it like a point light.

Finding: `volpath` gathers the emission of a medium while the depth is below `maxdepth`, one segment fewer than for the light
of a surface. With the longer paths counted the image was 1.5% brighter than `volpath` (16 runs, and the same in the Monte
Carlo mode; with the emission switched off the scene agreed, 0.9987 +- 0.0018). A path that ends in an emitting medium is now
at most `maxdepth` segments long: Monte Carlo mode, 6 runs, 0.9993 +- 0.0008.

## Jump Restore in path space (`volpathjrlt`, `volbidirjrlt`; Holl, Singh and Seidel 2025)

The same core and the same mutations, in the Restore framework in place of one long Metropolis chain per seed. A tour starts
from a new path of the bootstrap sampler (the global dynamics, of density u) and follows the chain of the mutations (the local
dynamics, which keep the target p; all of them, the bidirectional one too). A state is held for an exponential time of rate 1
and the tour is killed at the rate k = c u / p (the paper's equation 26), which is c over the weight that the bootstrap
sampler has for the state: `MaxLength() / InverseWeight(state)`, the quantity the core already has. The image is c over the
number of tours times the integral of the measurement over the tours (equation 32).

- No seeds are chosen from a table, so the seeding bias found above cannot occur, and no integral b scales the image: b (from
  `integralsamples`) only sets c = b / `tourlength` (30), so that a tour lasts about that many steps. Any c is unbiased.
- The time a state is held before either clock rings is taken at its expectation 1 / (1 + k), and the tour ends with the
  probability k / (1 + k): the exact conditional expectation, not the estimator of the Rao-Blackwellisation preprint (which
  is not unbiased and is not used).
- A sample of the bootstrap sampler that is no path is a tour of no length and is counted among the tours.
- Tours are independent: the loop over tours is the parallel loop.
- Specular next-event estimation cannot start a tour (its density is not known): the two options exclude each other.

Tests (16 runs of 512 mutations per pixel, lens and caustic perturbations and the bidirectional mutation, 200k integral
samples):

| Scene | Integrator | Ratio | Blocks |
|---|---|---|---|
| diffuse box | `volbidirjrlt` | 0.9976 +- 0.0018 | within 2.3 s.e., 16.7 / 12 |
| glass sphere | `volbidirjrlt` | 0.9963 +- 0.0024 | 13.5 / 12 |
| fog outside a clear glass sphere | `volbidirjrlt` | 0.9980 +- 0.0030 | 11.3 / 12 |
| glass sphere | `volpathjrlt` | 0.9974 +- 0.0030 | 15.6 / 12 |
| rough metal floor and sphere, fused set (sample-space step, manifold, half vector) | `volbidirjrlt` | 0.9995 +- 0.0012 | 9.1 / 12 |
| fog outside a clear glass sphere, fused set | `volbidirjrlt` | 0.9979 +- 0.0039 | 11.1 / 12 |

The mean of a run is noisier than with `volbidirmlt` (0.2 to 0.3% against 0.1%): the scale of the image comes from the tours
themselves. On the box, 30941 tours had 29.4 steps on average (`tourlength` 30), the longest 1012.

Side by side (fog outside a clear glass sphere, 192x144, 256 mutations per pixel; noise: the residual of a pixel against its
3x3 mean, over the image mean; times with other jobs on the cores): path-space Jump Restore with the fused set, 60 s, 0.212;
path-space Metropolis with the same set (6M bootstrap), 105 s, 0.215; the sample-space `jrlt`, 79 s, 0.147; `mlt`, 83 s,
0.143. On this scene, which has no transport that only the path-space mutations reach, the sample-space integrators are less
noisy at equal mutations; Jump Restore in path space matches the path-space Metropolis chain in noise without its bootstrap.

Also tested since: MLT seeded by specular next-event estimation on the fog scene, 16 runs, 1.0000 +- 0.0020, 26.6 / 12.

## Next

Users of the core, in order: the step sizes of half vector space from ray differentials (Hanika 2015, section 3); specular manifold next-event estimation with the unbiased reciprocal-probability estimator (Zeltner 2020; Hanika 2015 for the
walk; the Nelder-Mead solver of Granizo-Hidalgo 2024 as an alternative solver; Loubet 2020 for triangles). Then the
multi-chain perturbation, the portal perturbation (Otsu 2020), and the fused state spaces (Otsu 2017), which need
`InvertSample` on `BxDF` (not added without approval).

## Geometry-aware steps (after Otsu et al. 2018), option `geometryaware` (default false)

The widest turn of the caustic perturbation and the widest move of the lens perturbation are capped by a cone about the
perturbed segment in which nothing stands in the way (`VisibilityCone`: 4 rings of 3 probe rays, a fixed function of the
segment). The densities of the move differ both ways and are in the acceptance. Not the paper's cone estimate (which walks
the acceleration structure): probe rays.

Test, `door` scene of `scene.py` (light behind a wall with a slit 0.16 wide and 1 high), `volbidirjrlt`, 96x72,
512 mutations/pixel, 8 runs, bdpt 1024 spp reference:

| | mean / reference | relative RMSE | time for 8 runs |
|---|---|---|---|
| fixed steps | 0.989 +- 0.022 | 1.37 +- 0.06 | 192 s |
| geometryaware | 0.994 +- 0.017 | 1.19 +- 0.11 | 324 s |

Acceptance (256 mutations/pixel, one run): lens 0.648 -> 0.772, caustic 0.867 -> 0.923. The noise at equal mutations is
lower by about 13% (1.4 standard errors: not established) and the time is 1.7 times: a loss at equal time as implemented.
The cost is the probe rays (12 a cone, and the cone of every proposal). Temporary acceptance counters (`statCount`,
`statAccept`, `lensStat`) are still in `Mutate` and `LensPerturbation`.

The `geometryaware` experiment above was rolled back (the code is not in the tree; `/home/claude/scratch/geo_patch.py` has it).

## Delayed rejection (Tierney and Mira 1999; Rioux-Lavoie et al. 2020), option `delayedrejection` (default false)

After a rejection `Mutate` makes a second proposal: a lens move or a caustic turn `delayedrejectionscale` (0.125) times as
large, or a second regeneration of the same vertices. The first proposal is also a proposal of the same kind from the second
one, so its density from there is known: 1 / r^2 (lens), 1 / (theta sin theta) (caustic), the discrete choices
(regeneration). The other perturbations (manifold, half vector, multi-chain, sample space) have no second stage yet. The
splats of the Metropolis loop get the step given the outcome of the first stage.

Validation, `volbidirjrlt` + `delayedrejection`, 16 runs of 512 mutations/pixel: box 0.9999 +- 0.0016 (5.3 over 12 blocks),
fogball 0.9979 +- 0.0034 (13.1 over 12).

Fogball, 96x72, 256 mutations/pixel, 8 runs (relative RMSE to the reference, time of the 8 runs):
plain 0.294 +- 0.021, 108 s; + delayed rejection 0.275 +- 0.030, 146 s; fused 0.266 +- 0.003, 109 s;
fused + delayed rejection 0.238 +- 0.003, 135 s.

Second stage of the sample-space step (added after the above; `StepKernel`, `StepRecord`): a step `delayedrejectionscale`
times as small by the same split, only after a whole first proposal. Validation, fused `volbidirjrlt` + `delayedrejection`,
16 runs: fogball (512 mutations/pixel) 0.9981 +- 0.0042 (18.5 over 12 blocks), glossy (256) 0.9971 +- 0.0030 (6.5 over 12).
Fogball, 8 runs of 256: relative RMSE 0.2395 +- 0.0045, 135 s: the same as without this stage (0.238 +- 0.003, 135 s).
The tree before this stage is `/home/claude/scratch/pathspace_v4_dr.cs`.
