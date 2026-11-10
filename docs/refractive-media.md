# Refractive media and the unified volume interface (branch `production-rrte`)

References: Fong et al., *Production Volume Rendering* (SIGGRAPH 2017 course); Pediredla et al., *Path Tracing Estimators for
Refractive Radiative Transfer* (RRTE, SIGGRAPH Asia 2020).

A `refractive` medium has a spatially varying index of refraction, so light travels along curves. Paths through it are
traced with a symplectic integrator (`refractiveray.cs`), the points of a curve that meet a target are found by the
connection solver (`refractiveconnection.cs`), and the estimators follow RRTE: edges carry the area Jacobian
`J = dA_perp / d(omega)`, and a connection to a point counts the searches that find a path (Algorithm 3) as the estimate of
one over the probability of the path.

## What works

| Feature | tracervolpath | volpath | mlt (volpath estimator) | bdpt | tracerbdpt | tracerbdptonline |
| --- | --- | --- | --- | --- | --- | --- |
| Scattering and absorbing lens (any phase function, spectral sigma) | yes | yes | yes | yes | yes | yes |
| Area lights outside the lens | yes | yes | yes | yes | yes | yes |
| Point lights | yes | yes | yes | yes | yes | yes |
| Spot lights (outside the cone too: curved paths reach the position) | yes | yes | yes | yes | yes | yes |
| Infinite lights seen from inside the lens | yes | yes | yes | yes | yes | yes |
| Emission of the lens (`Le`) | yes | yes | yes | yes | yes | yes (delegates to `tracerbdpt`) |
| Surfaces and shapes outside the lens, nested and overlapping volumes | yes | yes | yes | yes | yes | yes |
| Surfaces inside the lens | yes | not supported (error) | not supported | yes | yes | yes |
| Per-entry volumes (`volumescale`) | yes | yes | yes | not supported (error) | yes | yes |

Light of every type is sampled from a vertex inside the lens along a curved path by one shared routine for the bidirectional
integrators (`BDPT.CurvedLightSample`): area lights by area, point and spot lights at their position, infinite lights at a
point far away along the sampled direction (the density per direction at the vertex is unchanged, through the exit-direction
Jacobian). For a light at infinity that is the only strategy that finds the path, so its multiple-importance weight is one,
camera subpaths that escape along a curve from a vertex inside the lens are dropped, and light subpaths that start at an
infinite light are cut when their first edge is curved.

## Validation

Every number is the mean of the image of a small scene (a Luneburg lens, sigma_s 1.5, sigma_a 0.02), compared with the
unidirectional tracer unless said otherwise. "Walk" is the path walk alone.

| Test | Result |
| --- | --- |
| Infinite light, 64 spp: tracervolpath / bdpt / tracerbdpt / tracerbdptonline | 0.5713 / 0.5713 / 0.5713 / 0.5710 |
| Area light, depth 4, 256 spp: tracervolpath / volpath / bdpt / tracerbdpt | 0.1302 / 0.1288 / 0.1288 / 0.1280 |
| Area light, depth 1, 256 spp: tracervolpath / volpath | 0.05948 / 0.0591 |
| Point light, depth 1: tracervolpath / bdpt / volpath | 0.03958 / 0.0396 / 0.0396 |
| Point light, depth 2, 512 spp: tracervolpath / volpath | 0.06247 / 0.0629 |
| Emissive lens, whole-image mean: tracervolpath / tracerbdpt / tracerbdptonline | 0.02234 for all |
| Spot light inside the cone equals the point light (tracervolpath, 128 spp) | 0.0874 / 0.0874 |

## Limitations (measured)

* **bdpt family bias with curved edges.** The bidirectional integrators with all strategies are high against the
  unidirectional estimators by about 2-3 % on a floor scene (1536 spp) and 6-8 % in a dense Cornell-box lens (depth 4, 256 spp).
  With only the unidirectional strategies they agree with `tracervolpath`, a straight sphere control is unbiased, and the
  Jacobians and the solver tolerance were ruled out, so the remaining suspect is the multiple-importance bookkeeping of
  strategies with surface vertices outside the lens and curved edges. A narrow spot light (where the light-subpath strategies
  carry most of the weight) shows it most: 0.0947 (bdpt) against 0.0874 (tracervolpath) at 128 spp. It is a bias of a few percent,
  not a missing feature; use `tracervolpath` as the reference when it matters.
* About 10 % of the curved edges of the walk fall back to the exact trace when the connection solver stalls.
* **Surfaces inside the lens** need the tracer-based integrators (`volpath` and `mlt` stop with an error).
* **`volumescale` materials** (volumes that depend on the point of entry) are not shared by paths built from both ends, so
  `bdpt` and `lightpath` refuse them.
* **`lightpath` and `simplevolpath`** do not support refractive media (a light tracer needs curved connections to the camera;
  `simplevolpath` is the pbrt teaching integrator without a boundary stack). Both stop with a message.
* **`bdpt` with `equiangular` sampling** and refractive media is refused.
* `tracerbdptonline` renders emissive refractive media through `tracerbdpt` (the incremental sums do not carry the emission
  ends).
* A delta light reached through a lens from a vertex outside it (a caustic of a point light) is not sampled: only paths
  that start or end inside the medium are connected.
* `HeterogeneousVolume.ExtinctionMinMax` is an estimate from a grid of points; `VolumeAggregate.GetIterator` (the octree of
  Fong section 6.8) is not implemented, and no integrator calls it.
