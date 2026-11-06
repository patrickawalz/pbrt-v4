# Unbiased photon gathering (`Integrator "upg"`)

An implementation of *Unbiased Photon Gathering for Light Transport Simulation* (Qin, Sun, Hou, Guo, Zhou, SIGGRAPH Asia 2015) as an
integrator of Magi, combined with the bidirectional path tracer of `bdpt` by multiple importance sampling.

Everything in this document marked **(paper)** is from the paper. Everything marked **(ours)** is a choice of this implementation or an
extension of it. The code is `src/pbrt/upg.cs` (the technique), a small additive hook in `BDPT.MISSum` / `BDPT.MISWeight` /
`BDPT.ConnectBDPT` in `src/pbrt/cpu/integrators.cs` (with `upg == null`, i.e. for `bdpt`, no operation is added and the output is
bit-identical to before), and `src/pbrt/upg_test.cs` (the tests).

Phase 1 (surfaces) is described in sections 1 to 8. The sections on polarisation and on media are added with those features.

## 1. The algorithm

**(paper)** Vertex merging (photon mapping) is biased because it estimates the density of a path with a kernel. UPG keeps the
*connection* and makes it random instead. A path with `k` edges has vertices `x_0 .. x_k` (`x_0` on the light, `x_k` on the camera).
For a camera sub-path `z_1 .. z_t` and a light sub-path `y_1 .. y_s`, a bidirectional connection joins the end vertices of the two. UPG
joins the camera vertex `z_{t'-1}` to a photon `y_{s'}` that lies within a radius `d` of the camera sub-path's next vertex `z_{t'}`,
by a connection (a BSDF evaluation at both ends and a shadow ray) that is **taken with the probability `p_c`** that the *other* way of
sampling the path would have placed its vertex in that neighbourhood:

* `p_c` = the probability that sampling the next vertex of the sub-path from `z_{t'-1}` with the BSDF puts it inside the ball of
  radius `d` around the photon (paper Eq. 14).
* The estimate is `f_c / p_c` times the Bernoulli outcome, i.e. `f_c / p_c` when the connection is taken. The reciprocal `1 / p_c` is not
  known, but it can be *estimated without bias*: the number `N` of tentative rays (each one the BSDF's own sampling of the next
  vertex) up to and including the first one that lands in the ball is geometric with mean `1 / p_c`. This is the estimator of Booth
  (2007) for the reciprocal of a probability (paper Eq. 15).
* **Angular bound (paper Sec. 3.4).** Most tentative rays miss a small ball. They are drawn instead from the part of the
  sampling domain that can reach the ball: `N_b / p_b`, where `N_b` is the number of trials when the random numbers of the direction
  are drawn from a region of the unit square that contains every number whose sample is in the cone to the ball, and `p_b` the
  *measure* of that region. `E[N_b] / p_b = 1 / p_c` (proof below).

Per camera vertex `z` (index `e >= 1`) the renderer gathers all the photons `y` (a vertex of a light sub-path of the batch, index
`idx >= 1`) with `|y - z| < d` and evaluates the connection for them (paper Algorithm 1). The contribution is

```
C = alpha_L(y) f_c alpha_E  *  w  *  N_b / p_b  /  n_L           (paper Eq. 17, with the MIS weight w)
```

with `n_L` the number of light sub-paths per iteration (the number of sub-paths each camera sub-path can pair with).

### Why `p_b` must be analytic

`E[N_b] = p_b / p_c` needs `p_b` to be the *probability* of the region, not an estimate of it. If `p_b` were a random estimate, `1 / p_b`
is not unbiased for `1 / E[p_b]` and the bias does not go away with samples. Here the region is a rectangle `[u0Lo, u0Hi] x [u1Lo,
u1Lo + u1Len]` (the second coordinate may wrap around) of the BSDF's two random numbers and `p_b` is its area: a closed form (the test
`AngularBoundContainsTheCone` checks that the area is the empirical frequency of the numbers in it, and that the region contains
every number whose sample is in the cone).

## 2. What exists here

* **Light sub-paths** of an iteration: `n_L` (parameter `photons`, default the number of pixels), generated with the `bdpt` machinery
  (`BDPT.GenerateLightSubpath`). All their vertices after the light are *photons* and go in a hash grid whose cell size is the
  radius.
* **Camera sub-paths** are the `bdpt` ones. For the camera vertex `z_e` the photons within the radius are gathered. The ordinary BDPT strategies
  are evaluated as well, with one light sub-path of the batch paired to the pixel (a clone: the MIS code assigns into the vertices).
* **Two candidate paths per (photon, camera vertex) pair (paper Sec. 4, Appendix B).** Keeping the photon `y` and removing `z_{t'}`
  gives a path whose end vertices are joined at `y -> z_{t'-1}` (the *shooter* is the camera vertex `z_{t'-1}`, mode Radiance, ball
  around the photon). Keeping `z_{t'}` and removing `y` gives a path joined at `z_{t'} <- y_{s'-1}` (the shooter is the light
  vertex, mode Importance, ball around the camera vertex). The paper chooses which to use from the *glossiness* of the two
  shooters (the less glossy one is removed, Appendix B). The choice is a function of the **path** (not of the technique that
  generated it), otherwise the MIS weights of two techniques that generate the same path would not be consistent. Here:
  `UpgMis.Variant`, the glossiness is the peak of the BSDF density around the mirror direction (and straight through) for the
  direction of arrival.
* **Tentative ray (ours, for the implementation).** Run exactly the bdpt random walk (`PathWalk`) for one vertex: it has the BSDF
  sampling, interface surfaces, the boundary stack and null collisions of the real walk, so the probability that the tentative ray
  hits the ball is the probability that the real walk would produce that vertex. A specular sampled direction counts as a failed trial
  (specular connections are excluded, below).
* **Gather radius** is fixed (parameter `radius`, default `0.1 * diagonal / sqrt(n_L)`). **(paper)** uses a fixed radius too; no
  shrinking.

## 3. Estimator proofs

**Booth.** Let the trials be independent with success probability `q = p_c / p_b` (the probability that a direction drawn from the
region lands in the ball: by construction the region contains the whole cone, so the successes are the same events as for the
unbounded sampler, `P(success | region) = P(success) / P(region)`). `N_b` is geometric: `E[N_b] = 1/q = p_b / p_c`, so
`E[N_b / p_b] = 1 / p_c`.

**Bound.** The region contains every random number whose sample is in the cone, so conditioning on the region does not remove any
success. Tentative rays use the real `BxDF.Sample_f` with the random numbers uniform in the region, i.e. the distribution of the
BxDF's own sampler conditioned on the region. The bound for a diffuse BSDF is the inverse of the concentric disk map of the cone;
for a rough conductor the inverse of the visible-normal sampling (`UpgBound.ConductorInverse`), with the images of the boundary of
the sampling domain and a grid, and a 3% margin to cover the sampling of the boundary; any other BxDF has the whole square
(`p_b = 1`, still unbiased, just slower).

**Truncation.** A run that is far out in the tail is ended after `maxtrials` trials (default 2^20) and counted. This is the only
place where the estimator is not exactly unbiased; the counter `truncated` of the statistics is zero in all the tests.

## 4. The MIS combination

**(paper)** UPG and BDPT are combined by multiple importance sampling with the power heuristic. For a path with `k` edges, the BDPT
strategies are `s = 0 .. k+1`; every UPG technique keeps one vertex `x_m`, `1 <= m <= k-1` (for each `m`, the variant chosen by the
path as in section 2).

**(ours: the bookkeeping)** The sum of the densities of all techniques is evaluated in the existing Veach-style backward-ratio loop
of `BDPT.MISSum` (the same loop, with the same spectral factors), by adding, for each strategy `j` (the number of light
vertices), the UPG techniques that share the sub-path densities of `j`:

```
p_A,m = n_L * p_BDPT(s = m+1) * pc_A,m        pc_A,m = min(V * pE(x_m), 1)     (variant: the light vertex x_m is kept)
p_B,m = n_L * p_BDPT(s = m)   * pc_B,m        pc_B,m = min(V * pL(x_m), 1)     (variant: the camera vertex x_m is kept)
```

with `pE(x_m)` the density (per area) of sampling `x_m` from `x_{m+1}` given `x_{m+2}` (the camera direction) and `pL(x_m)` that of
sampling it from `x_{m-1}` given `x_{m-2}`, `V = pi d^2` (the area of the ball's cut of a surface). **(paper Eq. 24)** The true
`p_c` is an integral over the ball; the weights use `min(V p, 1)`, which is the *approximation* of the paper. This is allowed
because a MIS weight only needs to be a function of the path that sums to one over the techniques, it does not have to be the true
probability; the estimator itself uses the exact reciprocal.

The weight of a sample taken by technique `i` is `p_i^beta / sum_j p_j^beta` (`beta = 2`, parameter `misbeta`; `beta = 1` is the
balance heuristic). Its numerator is `1` for BDPT (relative to its own density) and `(n_L pc)^beta` for a UPG sample. Only the
scalar part of the ratios is raised to `beta`; the spectral factors that implement the single-sample hero-wavelength balance are not
(without media they are all 1).

With `upgonly`, a BDPT strategy samples only the paths that have no gathering technique (depth under `minupgdepth`, or no
vertex that can be kept); these need BDPT anyway.

## 5. Parameters

```
Integrator "upg" "integer maxdepth" 5
    "integer photons" 0          # light sub-paths per iteration; 0 = number of pixels
    "float radius" 0             # gather radius; 0 = 0.1 * scene diagonal / sqrt(photons)
    "integer minupgdepth" 2      # shortest path (in bounces = edges - 1) that has gathering techniques
    "integer maxupgdepth" 5      # longest; defaults to maxdepth
    "float misbeta" 2
    "bool upgonly" false         # gathering preferred: BDPT only samples the paths without a gathering technique
    "bool bdptonly" false        # no gathering (the bdpt strategies with the batch's light paths)
    "bool angularbound" true
    "integer maxtrials" 1048576
    "bool regularize" false
```

`russianroulette` is an error (the roulette of the camera sub-path changes the density of the vertex that the photons are gathered
for). Only the `perspective` camera is supported. One set of wavelengths is used per iteration, stratified over the iterations and
rotated by a number of the seed.

## 6. Limits (surfaces)

* **Specular vertices:** connections that involve a strictly specular vertex (delta BSDF, delta lights) are excluded
  from gathering, as in the paper (SDS paths and specular chains are the weak point of every bidirectional method but they are not
  *worse* here: they are sampled by BDPT's strategies that do not connect through the specular vertex).
* **Shallow paths:** every gather costs the Bernoulli trials of two candidate paths. For depth under 2 (direct light, one bounce) UPG
  has no technique and `minupgdepth` is 2; even depth 2 and 3 are better done by BDPT unless the connection is hard (caustics,
  glossy, small lights through apertures). The benchmark section shows the cost.
* **Cost grows with `n_L` and the radius:** the number of pairs per camera vertex is `n_L * pi d^2 * (photon density)`.
* **Non-bounded BxDFs** (dielectric, layered, OSL, ...) have `p_b = 1`: correct, but each gather costs `1 / p_c` trials.
* Heavy tails: the pixel distributions of the gathering are heavy-tailed (rare large values), as for every Russian-roulette
  estimator of a reciprocal.

## 7. Tests (`Upg.*`)

| Test | What it proves |
| --- | --- |
| `AngularBoundContainsTheCone` | 420 configurations (diffuse; conductor alpha 0.05, 0.3, 0.3 x 0.1, 0.8; 4 directions of arrival including the back side of the shading frame; 7 centers; 3 radii; 200000 random numbers each): no number in the cone is outside the bound, and the area `p_b` is the empirical frequency of the numbers in it |
| `ReciprocalEstimatorDiffuse` / `Glossy` | `E[N_b] / p_b = 1/p_c` with and without the bound, for the real tentative-ray code in a real scene, against `p_c` by quadrature (2D polar midpoint rule; the quadrature is checked against a coarser one and, for the diffuse floor below the center, against the closed form `d^2/(d^2+h^2)`). 40000 estimates each; z-scores below 2.3 |
| `MisWeightsOfAllTechniquesSumToOne` | on 196 real paths (camera sub-paths that hit the light, so every split exists), BDPT.MISWeight (the function that renders) of every BDPT strategy and every gathering technique, each presented to it as the split it belongs to, with the densities of the vertices of the side that samples them: the sum is 1 (worst `|sum - 1|` 3e-5), and every weight equals an independent computation from the raw densities (worst 1.4e-5), for `beta` 1, 2, 3 and `upgonly` |
| `UnbiasedRoomWithGlossyFloor` / `UnbiasedIndirectlyLitRoom` / `UnbiasedCausticLikeRoom` | UPG only and UPG + BDPT, each with a large (0.4) and a small (0.06) radius, 32 independent seeds each, against 96 images of a BDPT reference: the means agree (t statistics in section 8), and the RMSE of the average of `G` images falls with a log-log slope near -0.5 (the squared error of the reference's own noise is removed), i.e. there is no floor from a bias |
| `FurnaceSphere` | inside an emitting diffuse sphere of albedo 1/2 every pixel has the radiance `L_e (1 + 1/2 + ... + 1/2^5)` exactly; bdpt and the four UPG variants agree with it |

## 8. Results (phase 1, surfaces)

All numbers are from this machine (2 cores). Scenes: a Cornell-like room with a copper glossy floor (roughness 0.12, alpha 0.35), a red wall,
a diffuse sphere and an area light (`direct`); the same with the light facing up so that everything the camera sees is lit by bounces
(`indirect`); a small light over a nearly mirror floor of roughness 0.06 that casts a bright reflected patch (`caustic`); and a tiny
light over a floor of roughness 0.02 (`hard`). The test scenes are 12 x 12, the benchmark scenes 64 x 64; `maxdepth 5`.

### Bugs that the tests found (and that are fixed)

* A conductor shooter whose direction of arrival was on the back of the shading frame got the whole square as its bound
  (`wo.z < 0`): 280 trials per connection instead of 3. The sampler flips the visible normal into the upper hemisphere, so the sample for
  `wo` is minus the sample for `-wo`; the bound uses that. (The bound test now includes the back side.)
* Every seed used the same wavelength sets (`RadicalInverse(iteration)`), so the mean over seeds converged to the mean at those
  wavelengths, not over the spectrum: UPG was 0.8% too dark in the furnace (t = -10 to -50) and low in blue. The wavelength sets are
  now stratified over the iterations and rotated by a number of the seed.

### Unbiasedness: means against a long BDPT reference (32 images of 16 spp per variant, 96 reference images)

`t` is the difference of the means of the image means over the standard error of the difference. Largest |t| of any of the 36 (variant,
scene, channel) numbers: 1.37. The radius does not change the mean (two radii of the same technique: |t| <= 1.13).

| scene | variant | mean | reference | t (all) | t R / G / B | log-log slope of the RMSE |
| --- | --- | --- | --- | --- | --- | --- |
| room | upgonly r 0.4 | 0.04042 | 0.04036 | 0.06 | 0.23 / 0.04 / -0.09 | -0.54 |
| room | upgonly r 0.06 | 0.04080 | 0.04036 | 0.51 | 0.78 / 0.78 / -0.04 | -0.49 |
| room | upg+bdpt r 0.4 | 0.03996 | 0.04036 | -1.02 | -1.18 / -1.37 / -0.41 | -0.55 |
| room | upg+bdpt r 0.06 | 0.04024 | 0.04036 | -0.36 | -0.26 / -0.51 / -0.24 | -0.55 |
| indirect | upgonly r 0.4 | 0.41058 | 0.40941 | 0.21 | 0.43 / 0.25 / -0.02 | -0.55 |
| indirect | upgonly r 0.06 | 0.40783 | 0.40941 | -0.34 | 0.13 / -0.17 / -0.86 | -0.58 |
| indirect | upg+bdpt r 0.4 | 0.40433 | 0.40941 | -0.75 | -0.83 / -0.66 / -0.65 | -0.41 |
| indirect | upg+bdpt r 0.06 | 0.41429 | 0.40941 | 0.81 | 1.23 / 1.11 / 0.03 | -0.47 |
| caustic | upgonly r 0.4 | 0.04777 | 0.04841 | -0.63 | -0.98 / -1.13 / 0.07 | -0.46 |
| caustic | upgonly r 0.06 | 0.04778 | 0.04841 | -0.52 | 0.12 / -0.40 / -1.12 | -0.52 |
| caustic | upg+bdpt r 0.4 | 0.04820 | 0.04841 | -0.52 | -0.59 / -0.95 / -0.08 | -0.47 |
| caustic | upg+bdpt r 0.06 | 0.04844 | 0.04841 | 0.10 | 0.23 / -0.02 / 0.03 | -0.54 |

The slope is that of the RMSE of the average of `G` = 1, 2, 4, 8, 16 images against the reference (random partitions, the reference's own
noise removed); -1/2 means no floor. A bias would flatten the curve. `bias_experiment.png` plots them. The slope of the `indirect`
`upg+bdpt` large-radius row (-0.41) is the lowest; with 128 images of that variant the squared error of the average of `G` images
follows `sigma^2 / G` to within the sampling noise (ratios 1.00, 1.02, 1.02, 1.11 for `G` = 1, 2, 4, 8), so the low value is estimation noise
of a heavy-tailed variable from 32 images, not a floor.

Furnace (inside an emitting diffuse sphere of albedo 1/2, 48 images of 16 spp; exact radiance `L_e (1 + 1/2 + ... + 1/2^5)` with `L_e` measured
at depth 0): bdpt 1.9677 +- 0.0016, UPG only (r 0.5, 0.08) 1.9653, 1.9598; UPG + BDPT 1.9692, 1.9679; expected 1.9692 (|t| < 1.2 for all five).

The pixel-wise mean square z-score (reported by the tests) is 1.1 to 2.8 with 32 images and 1.04 with 160 images of the caustic scene: the
excess is the sample variance of 32 draws of a heavy-tailed variable, not a bias, which is why it is reported and not asserted.

### Cost (64 x 64, 20 s per variant on both cores)

| scene | samples per second: bdpt / UPG + BDPT | equal-time RMSE (3 seeds, emitter pixels excluded): bdpt | UPG only | UPG + BDPT |
| --- | --- | --- | --- | --- |
| direct | 27500 / 15500 | **0.0027** | 0.0230 | 0.0037 |
| indirect | 22200 / 15300 | **0.0495** | 0.0813 | 0.0559 |
| caustic | 32100 / 14500 | **0.0030** | 0.0329 | 0.0044 |
| hard (tiny light, narrow glossy floor) | 22000 / 11700 | **0.0101** | 0.1411 | 0.0154 (seeds 0.0076, 0.0151, 0.0234) |

**BDPT is the better choice in all four of these scenes at equal time.** UPG + BDPT is 1.1 to 1.6 times worse in RMSE (best on `indirect`),
UPG alone is much worse (it has no technique for the shallow paths and the heavy tailed gathering carries all the others). The
scenes are small and easy for BDPT; the paper's gains are for scenes in which BDPT's connections through glossy and near-specular
vertices are hopeless. A radius of the default size (`0.1 diag / sqrt(n_L)`, found by trying absolute radii on the caustic scene at equal time:
RMSE 0.0238 at 0.12, 0.0126 at 0.058, 0.0058 at 0.03, 0.0042 at 0.015, 0.0047 at 0.008; one seed each) matters: the cost grows with the number of pairs and the weights
`min(V p, 1)` give the gathering a small share where the radius is small anyway. With the best radius on the `hard` scene a single seed was
15% better than BDPT (0.0085 against 0.0101) but three seeds of the default radius are noisier than BDPT, so this is not a claim.

Where the time goes (UPG + BDPT, thread-seconds of 2 cores, 20 s run; `UPG_STATS=1` prints it): the BDPT strategies of the paired
light path, now with the MIS terms of the gathering techniques in every one of their weights, 19 to 25; the photon search 1.4 to 1.6 (36 to 45
million photon tests for 1.3 to 1.7 million gathers); the shadow rays of the gathering connections 0.02 to 0.1; the Bernoulli trials 0.2 to
0.8. So **the Bernoulli trials and shadow rays of UPG cost under 5% of the time**; the 1.5 to 2 times slower sample comes from the MIS
terms in the BDPT strategies and the photon pass.

Number of Bernoulli trials per connection (paper Fig. 9): mean 2.9 (indirect, hard) to 3.6 (direct, caustic); 42% to 45% of the
connections need one trial, and fewer than 0.15% need 101 or more; none was truncated in any run. Histogram, `indirect`: 1 trial 30545, 2 15184, 3 7951,
4 4433, 5 2693, 6 1670, ... 101+ 24 (of 67420 connections).

Angular bound (64 x 64, 8 spp, `upg`): with the bound the `direct` scene needs 3.9 trials per connection and 3.8 s in total; without it 72117 trials
per connection (34 runs truncated at 2^20 trials) and 147 s; the `caustic` scene 3.4 trials and 4.1 s against 59656 trials (41 truncated) and 225 s.
The trial cost with the bound is 0.04 thread-seconds against 279 without it.

### Limits

* BDPT wins at equal time on the four scenes measured (above); UPG is expected to pay where the connections of BDPT are poor, which these
  scenes do not show.
* No Russian roulette (error), perspective camera only, specular vertices excluded from the gathering.
* A gather is exact for the BxDFs with an angular bound (diffuse, rough conductor); dielectric, layered, OSL and other BxDFs use the
  whole square (`p_b = 1`), which is unbiased but slower in proportion to `1 / p_c`.
* The truncation of a run at `maxtrials` is the only inexact step (counted, zero in all measurements here).
* Not supported yet: polarisation (phase 1b), participating media (phase 2).

## 9. Polarisation (phase 1b)

`Integrator "upg"` accepts the parameters of polarised `bdpt` (`polarised`, `analyser`, `analyserangle`, `measure`) and builds the same sensor. The sub-paths carry the Mueller matrices (camera) and Stokes vectors (light) as in `bdpt`; the photons keep the Stokes state.

The connection of a photon `y` and an eye vertex `z` is evaluated by `BDPT.MeasureConnection`, in the order of `ConnectBDPT` (and of `ConnectVertices` of polarisedUPBP: camera throughput, then the connection factors, then the light throughput): the Stokes vector of the light side, the scattering matrix at the photon vertex towards `z`, the scattering matrix at `z` towards `y`, the Mueller matrix of the camera side, the sensor. The Booth factor `N_b/p_b`, the BSDF factor `f_c` and all MIS weights stay scalar, so the estimator is unbiased per Stokes channel by linearity. The angular bound is unaffected: only scalar BxDF values enter `p_b`, and the tentative ray is unpolarised.

Limits: only smooth dielectric/conductor events (and, in phase 2, Rayleigh media) polarise. A rough or diffuse connection vertex depolarises (Lambert = depolarising both), so the polarisation that reaches the sensor comes from the camera-side Mueller matrix; light sources are unpolarised. Consequently the signal is small in rooms with rough surfaces (S1 about 1e-5 of the intensity in the test scene), and the Stokes tests use random +-1 projections of the pixels besides the mean.

Tests (`Upg.Polarised*`): connection of a fixed vertex pair behind a dielectric mirror against the Fresnel equations of polarisedUPBP (S0 = scalar, sqrt(S1^2+S2^2) = D S0, S3 = 0, independent of the polarisation of the light side; 20 reflections); polarised UPG(+BDPT) against a 64-image polarised `bdpt` reference for intensity, S1, S2 at two radii (all |t| < 2.6); identity scene (S0 equals scalar UPG, same-seed images identical, S1..S3 exactly 0). Polarised `bdpt` and unpolarised UPG outputs are bit-identical to the versions before the change.

## 10. Participating media (phase 2; our own extension)

The paper is about surfaces. Everything in this section is ours; what is taken over unchanged from the paper is marked.

What is the paper's: the Russian-roulette connection with probability p_c, the Booth estimator N_b / p_b of 1/p_c, the min(V p, 1) approximation of p_c inside the MIS weights, the variant choice by the glossiness rule of Appendix B, the power heuristic.

What is ours:

* **Vertices in a medium are the same kind of vertex.** A tentative ray from a shooter in a medium is the walk of bdpt (the same path walker, `PathWalk`) stopped at its first event: the phase function samples the direction and the collisions of the medium (delta tracking with null collisions, so heterogeneous, grid and OSL media work unchanged) place the next scattering event; absorption ends the ray; surfaces of interfaces are crossed as in bdpt. A trial succeeds if the first vertex that has a scattering (a scattering event in the medium or a BSDF vertex) is inside the ball. So the probability that a trial succeeds is exactly the probability p_c that the sub-path has a vertex in the ball, in 3D for a medium vertex (ball of radius d, measure V = 4/3 pi d^3) and on the surface for a surface vertex (disc, V = pi d^2).
* **Angular bound for the phase function (Henyey-Greenstein and isotropic).** The sampling of the direction is cos(theta) = f(u_0) (monotone, theta from wo) and phi = 2 pi u_1, so the cone of half angle A around an axis at angle gamma from wo is inside the rectangle of the polar angles [gamma - A, gamma + A] (mapped through the inverse of f, by bisection) and the azimuths of half width asin(sin A / sin gamma) around the azimuth of the axis (all azimuths if the cone contains a pole of the frame of wo, either one). The area of the rectangle is p_b. A mixture with Rayleigh scattering uses the whole square (p_b = 1). Test: 756 configurations (with the 4 phase functions: isotropic, g = 0.6, -0.5, 0.9), none of the 200000 random numbers per configuration whose sample is in the cone is outside the rectangle.
* **p_c in the weights has the collision factor.** In the ratios of bdpt the density of a medium vertex is the density of the direction divided by the squared distance (the density of the distance cancels between the strategies that share the vertex), but p_c is an absolute probability, so it has the density of the distance of the collision: p_c = min(V * p_dir * sigma_s(x) T(neighbour -> x), 1). (The factor is sigma_s, not sigma_t: an event of absorption ends the tentative ray as it ends the walk of bdpt, so the density of a scattering event is sigma_s T.) T is a deterministic quadrature (8 midpoints, hero wavelength, the medium of the vertex for the whole segment), never a ratio-tracking estimate, so that the weight is a function of the path; it only changes the weights, not the expectation of the estimator. The estimator itself (the trials) has no approximation.
* **Emissive media are not supported** (the camera sub-paths of upg do not gather the emission of a medium): `Integrator "upg"` stops with an error if a medium emits (use bdpt).
* **Chromatic media.** p_c uses the hero wavelength only, so for strongly chromatic media the weights are not a strict partition over the wavelengths of the single-sample estimator (a small bias is possible); the tests use grey media.
* **Polarisation** in media: the connection at a vertex in the medium has the Mueller matrix of the phase function (Rayleigh scattering polarises), as in bdpt.

### Tests (media)

| test | what it checks |
|---|---|
| `MediumReciprocalEstimator` | infinite homogeneous medium, isotropic and Henyey-Greenstein phase functions: E[N_b]/p_b = 1/p_c with p_c from the quadrature of the cone and the chord (all |z| < 2.0 with 40000 runs; with 400000 runs of the bounded estimator |z| < 1.7) |
| `MediumMisWeightsSumToOne` | the weights of all techniques add to one on paths with vertices in the medium (150 paths, worst error 1.3e-5 for beta 2, 7.6e-6 for beta 1), and the weights agree with the raw-density computation that has the factor from the coefficients of the medium. Paths with a medium vertex closer than 0.25 to a neighbour are reported but not asserted: the density of a vertex that a view of the path recomputes from the geometry differs from the walk's by the offset of the origin of the ray (4e-4 at a distance of 0.1), 1.8e-4 worst sum |sum - 1| |
| `UnbiasedFogRoom`, `UnbiasedForwardScatteringFog`, `UnbiasedDenseFog`, `UnbiasedGridMedium`, `UnbiasedOslMedium`, `UnbiasedGlossyInFog` | the room filled with a homogeneous medium (isotropic; g = 0.6 with the indirectly lit room; dense, sigma_s 1.2), a grid medium (density varies by 5), an OSL medium (vtrig.osl), the glossy floor under the small light; each against 96 images of bdpt, 4 variants (UPG only / UPG+BDPT, two radii), the t statistics of the mean (all |t| < 2.0), the radii agree, the slope of the RMSE of the averaged images is -0.43 .. -0.56 |
| `MediumFurnaceSphere` | inside an emitting diffuse sphere (albedo 0.5) filled with a scattering medium without absorption the radiance is L_e / (1 - a): bdpt, UPG only, UPG+BDPT at two radii, |t| < 1.5 |
| `PolarisedRayleighFog` | polarised UPG(+BDPT) against polarised bdpt in a room filled with Rayleigh scattering medium |

### Cost

A ball in 3D is small: for a given radius the probability that a tentative ray ends in it is V p, with V = 4/3 pi d^3 and p the density per volume, so most pairs need many trials; the angular bound helps (p_b about 0.003 .. 0.1 in the infinite-medium cases, 8 .. 39 trials instead of 80 .. 10000). At equal samples per pixel UPG+BDPT has more noise than bdpt in the dense fog room at the default radius (96x96, 16 spp: relative RMSE 3.5 at r = 0.03 and 5.4 at r = 0.1 against 1.6 for bdpt, against a 256 spp bdpt reference), and a large radius (0.8) is too slow (474 s against 9 s). As for surfaces, bdpt wins at equal time on the scenes measured.
