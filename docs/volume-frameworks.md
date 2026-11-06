# Volume frameworks: a pluggable architecture (design note, no engine code)

Status: design note, revision 3 (revision 3 = section 4.2 rewritten from the Tungsten source; interface changes listed in 6.1). Nothing in this note changes engine code. Branch `feature/bdpt-incremental-mis`, written against commit `4e3d77b`+.

Scope (revision 2): **in** the spec are the exponential null-scattering framework, the non-exponential framework of Bitterli et al., SGGX microflakes, the estimators, and the black box. **Deferred** (not in the spec, no hooks in the interface): spatially correlated media (Jarabo et al.) and the refractive RTE (RRTE); see section 4.5 and section 6.1 for what adding them later would cost. Revision 1 is in git history (ae15c7d).

## 0. How to read this note

Every factual claim carries a tag:

* **[C]** I read it in the code of this repository (file and line given; lines are of the worktree at this commit).
* **[P]** I obtained it from the paper or its page with a fetch tool. The tool returns a summary of the page, not raw text, so quotation marks below mean "the summary quotes", not "I checked the PDF". Where two versions of a paper disagree I say so.
* **[D]** Derived by me from the code and the papers. Not in any paper, not tested. Treat as a hypothesis with a test attached.
* **[R]** Recalled from memory, not verified in this session.
* **[T]** I read it in the source of Benedikt Bitterli's Tungsten renderer (github.com/tunabrain/tungsten, branch `master`, commit `88ea020`, 2019-04-14; shallow clone read as text, never built or run). Paths are relative to `src/core/`. This is the reference implementation of the non-exponential framework, so [T] statements outrank [P] statements about it. "Tungsten does X" says what that code does, which is not always what the paper says it should do; where I found a divergence I say so. Branches `instancing` and `mis-rewrite` were not read.

What I could and could not read is listed in section 11.

## 1. Corrections to the working hypothesis

1. **"Every volume has its own integrator" already exists in part.** `VolumeIntegrator` (`volumeintegrator.cs`) is attached to every `Medium` (`Medium.volumeIntegrator`, `media.cs:1413`) and is a pair `(VolumeTracker, TransmittanceEstimator)`, modelled on Fong et al., "Production Volume Rendering" [C]. Trackers: `majorant` (the medium's own majorant iterator), `global`. Transmittance estimators: `ratio`, `analytic` (Beer-Lambert for a homogeneous medium), `tracklength`, `residual` [C]. What is missing is the **transport model** (the free-flight law): the walk, `PathState`, `MediumEvents` and the shadow ray hard-code the exponential law around that pair (section 3).
2. **Deferred: RRTE exists on another branch.** `experimental/refractive-media` (reverted from this line in commit `1f6dcac`) holds an implementation of the Pediredla 2020 estimators; it needed about 800 lines in `integrators.cs` and 85 in `pathwalk.cs` through `as RefractiveMedium` special cases [C]. It is out of the spec for now (section 4.5). It is cited here only as evidence of what per-framework special-casing in the walk costs. `OSLMedium` already carries `[ThreadStatic] currentDirection/currentTime` because `Medium.SamplePoint` has no direction [C: `osl_volume.cs:215-216`], and the experimental `procedural-media` branch adds spectral tracking, decomposition, adaptive and signed-null flags to `MediumProperties` with `if (mp.spectralTracking)` blocks in volpath only [C: `git show experimental/procedural-media:src/pbrt/media.cs`, `integrators.cs:4375-4440`].
3. **The incremental MIS is estimator-agnostic only after a change.** The walk stores `pdfFwd/pdfRev` that contain the angular density and the area/volume conversion only (`Vertex.ConvertDensity` multiplies by `1/d^2` and `|cos|`, `integrators.cs:1859`; medium vertices are created with the phase density, `:2435`) [C]. The density of the *distance* (`sigma_t(x) T(x_{i-1}, x_i)`) is not stored. It drops out of every ratio because, in an exponential medium, the same factors appear in every strategy [D: my reading of why the existing weight-equality and unbiasedness tests pass; the one place it is kept is the emission end, `end.pdfFwd = ... * end.sigmaT0`, `:2415`]. For a non-exponential model this cancellation fails (section 4.2) and the distance densities must be stored per segment. For SGGX it still holds with a direction-independent majorant [D, section 4.3], but the phase density must be evaluated with the reversed directions. The recurrence itself (`bdptincremental.cs`) is written in `pdfFwd/pdfRev/segU/segL/cumU` and needs no new term, only different inputs [C+D].
4. **"Signed weights conflict with MLT" is only partly right.** The current estimators draw their random numbers from `RNG(Hash(ray.o, tMax), Hash(ray.d))` (`pathwalk.cs`, `ShadowRay`) [C], so every estimator is already a deterministic function of the path geometry plus the sampler values, which is what MLT needs. Sign is a milder problem (the target needs `|f|`, the sign is carried) [D]. The real MLT conflict is any component that is not a function of the primary sample vector, e.g. a black box with private randomness [D].
5. **UPG uses Bernoulli trials, not Booth trials.** `upg.cs` calls the angular bound "the angular bound of the Bernoulli trials" [C]. Its media code is "OUR OWN extension (the paper is surface-only)" [C: `upg.cs:5`] and needs the distance density explicitly (`MediumFactor`: `sigma_s T` by a midpoint quadrature, `upg.cs:200`) [C], which is the same need as in section 4.2.
6. **SGGX is not a transport model.** It is medium *data* (direction-dependent extinction and a phase function), layer 2 of your three layers, orthogonal to the free-flight law [P+D, section 5.3].

## 2. Layers

```
 integrators (volpath, bdpt, bdptinc, mlt, upg, ...)      see only: segments, collisions, vertices with fwd/rev densities
        |
 PathWalk / shadow rays / connections                      one place that talks to a volume
        |
 VolumeIntegrator  (one per medium, the "black box")       = TransportModel + Tracker/Estimators + capability flags
        |
 Medium (data)     majorants, sigma_a, sigma_s, Le, phase, SGGX orientation, OSL shader
```

* Layer 1, **transport model**: the free-flight law (what a "segment pdf" and "transmittance" are). Segments are straight lines. Exponential, non-exponential (Bitterli), black box.
* Layer 2, **medium data**: unchanged classes, plus direction-aware `SamplePoint` for media that need it (SGGX, OSL).
* Layer 3, **estimator**: how layer 1 is realised numerically: delta, ratio, residual ratio, spectral, decomposition, regular tracking; equiangular and emission strategies are *uses* of a segment.

A volume's `VolumeIntegrator` declares **capabilities** (sections 5b and 5c) so the integrators decide, per path, which strategies may include it.

## 3. The current code (what exists, what hard-codes exponential)

### 3.1 Classes [C]

* `Medium` (`base/medium.cs`): `IsEmissive`, `SamplePoint(p, lambda[, time])`, `SampleRay(ray, tMax, lambda, buf)` returning a `RayMajorantIterator` of `RayMajorantSegment {tMin, tMax, sigma_maj}`. `MediumProperties {sigma_a, sigma_s, phase, Le}` (`media.cs:303`). `PhaseFunction {p, Sample_p, PDF}`; `HGPhaseFunction` has a Rayleigh fraction (the polarising part), `MixturePhaseFunction`, `PhaseBxDF` wraps a phase function as a BxDF.
* Media: `HomogeneousMedium`, `GridMedium`, `RGBGridMedium`, `CloudMedium`, `NanoVDBMedium` (`media.cs`), `TextureMedium` (+ hierarchical majorants, emission light), `OSLMedium`, `CompositeMedium` (overlap: sums coefficients and majorants, `MixturePhaseFunction`), `ScaledMedium` (a material's `volumescale`).
* Boundaries: `BoundaryStack` (priority stack of `(material, medium)`, `MediumTracking.Current/CrossSurface`), `MediumInterface` with priority, `canOverlap`, `overlapPriority`.
* Distance sampling: `pbrt.SampleT_maj<Concrete, Iter>` (`media.cs:121`) with the type-test dispatch (`:197`), driven by a `Func<...>` callback per collision.
* Walk: `PathWalk.Run` (`cpu/pathwalk.cs`) shared by bdpt and bdptinc (and volpath's `PathState` logic); `PathWalkHook` (BeginSegment, BeginCollision, BeginScatter, OnScatterVertex, OnPhaseSampled, ...). `PathState` keeps `beta`, `r_u`, `r_l`, and for bdpt `segU/segL/vtx` (`cpu/pathstate.cs`). `MediumEvents.SampleEvent/RatioTrackingStep/FinishSegment` (`mediumevents.cs`).
* Shadow rays / connections: `Integrator.TrSegments`, `ShadowRay` (`integrators.cs:134`, `:160`) call `volumeIntegrator.transmittance.{TryClosedForm, TryEstimate, Step, Finish}`.
* bdpt: `Vertex` (1344 B with its `EndpointInteraction`) holds `pdfFwd/pdfRev/delta`, `stack`, `segU/segL/vtx/cumU`, `sigmaS0/sigmaT0/emission/emissionMaj`; emission ends (`MediumEmissionEnd`), equiangular sampling (`EquiangularSegment`, `EquiangularSampling`), medium emission lights (`VolumeEmissionLight`, `SampleLiVolume`). bdptinc adds `VertexMIS` (84 B).
* Polarisation: the Mueller matrix of a medium scatter exists only for the Rayleigh part of `HGPhaseFunction` (`cpu/polarisedpath.cs:29-44`); scalar phase functions do not touch the Stokes state.

### 3.2 Which integrator touches media

| Integrator | Media? | How [C] |
|---|---|---|
| `volpath` (`VolPathIntegrator`) | yes | own loop with `volumeIntegrator.tracker`, `PathState`-like `r_u/r_l`, `SampleLd` with `ShadowRay`, emission gather, equiangular, `VolumeEmissionLight` NEE/FNEE |
| `simplevolpath` | yes | own loop with the tracker callback (`integrators.cs:1309`) |
| `bdpt` | yes | `PathWalk` + `BDPTWalkHook`, `TrSegments` for connections, `MISWeight/MISSum` with the spectral products |
| `bdptinc` | yes | copy of the hook on the same walk; stored sums |
| `mlt` | via `BDPTPathEvaluator` | `mltevaluator.cs` uses `BDPT.ConnectBDPT/MISWeight` and the emission ends (6 media mentions) |
| `upg` (branch `feature/upg-integrator`) | own extension | tentative rays are the bdpt walk; `MediumFactor` quadrature; polarised |
| `sppm` | **no** | `SPPMIntegrator` (lines 3665-4262) contains no medium code at all; what an interface surface does there was not checked |
| `path`, `lightpath`, `simplepath` | no | their walks "skip over medium boundaries" (`integrators.cs:764, 937, 1085`) and ignore the media; `lightpath` also refuses per-entry volumes |
| `randomwalk`, `ao` | no | no medium code |

### 3.3 Everything that hard-codes the exponential law or delta tracking

| Where | What |
|---|---|
| `media.cs:155,164,169,189` `SampleT_maj` | `SampleExponential`, `FastExp(-dt sigma_maj)`: the free-flight law |
| `volumeintegrator.cs:70,76,80,89` `GlobalMajorantTracker` | same |
| `volumeintegrator.cs:157` `AnalyticTransmittanceEstimator` | `exp(-length (sigma_a + sigma_s))` |
| `volumeintegrator.cs:227-257` `ResidualRatioEstimator` | `exp(-dt sigma_c)` for the control, exponential residual steps |
| `mediumevents.cs` | `SampleEvent` (`sigma/sigma_maj` as probabilities), `RatioTrackingStep` (`pdf = T_maj[0] sigma_maj[0]`), `FinishSegment` (`T_maj/T_maj[0]`) |
| `pathstate.cs:105-175` | `NullCollision`, `RealScatter`, `EndSegment`: the r_u/r_l/segU/segL updates are the null-scattering weights |
| `pathwalk.cs` | event choice by `SampleEvent`, `ctx.st.RealScatter(T_maj_, mp.sigma_s)` |
| `integrators.cs:4806-4861, 5272-5309` | volpath: collision density `sigma_maj T_maj`, emission weights `T_maj sigma_a Le / ...`, `r_e` |
| `integrators.cs:2398-2415` | bdpt emission end: `pdfCollision = sigma_maj T_maj`, `end.pdfFwd * sigmaT0` |
| `integrators.cs:2818-2831, 4968-5027, 5618-5642` and the bdptinc copies | equiangular: pdf of the point is divided by `sigmaS0` etc. |
| `Vertex.ConvertDensity` and `PDF` | no distance density, relies on the cancellation (section 1.4) |
| `bdptincremental.cs` `FinishCameraVertex/FinishLightVertex`, `EquiangularTerm` | `segU/segL/cumU` are null-collision ratios, `vtxProduct` uses `sigma_s/sigma_s[0]` |
| `TextureMedium.ControlExtinction`, `CompositeMedium` | additive coefficients, sum of majorants (valid only if the volumes share the exponential law) |
| `upg.cs MediumFactor` | `sigma_s T` quadrature |

## 4. The frameworks: what each changes

For each: the path pdf / free-flight, phase, geometry; whether bidirectional strategies are covered.

### 4.1 Standard exponential with null scattering (baseline)

Free flight `p(t) = sigma_t(t) exp(-int sigma_t)`, `T(a,b) = T(b,a)`. Null scattering [P: Miller et al. 2019] turns the rejection steps of delta tracking into explicit path vertices, so that the pdf of a delta-tracked subpath is exact and cheap; ratio tracking is "sampling a null-scattering subpath between two real-scattering vertices"; BPT with delta-tracked subpaths and ratio-tracked connections has exact pdfs used for MIS (their Sec. 5.5). Their BPT is achromatic only ("does not employ spectral MIS and handles achromatic media only") [P]; the hero-wavelength version here (`segU/segL`, section 3) is an extension beyond that paper [C+P]. They do not derive VCM or photon beams [P]. Surfaces get no null scattering [P].

### 4.2 Non-exponential (Bitterli et al. 2018), read from Tungsten (revision 3)

Revision 2 of this section rested on fetch-tool summaries of two versions of the paper [P]. Revision 3 replaces it with a reading of the reference implementation [T]. Where the code settles a question the [P] reading is dropped; what the code cannot settle is said so.

**(1) The law and the edge throughput.** `Transmittance` (`transmittances/Transmittance.hpp:17-58`) is the whole free-flight law, as four functions of the optical depth `tau` of the segment: `surfaceSurface` (probability to reach a surface, starting on a surface), `surfaceMedium` (density of a collision, starting on a surface), `mediumSurface`, `mediumMedium`; plus `sigmaBar()` (= `surfaceMedium(0)/mediumSurface(0)`, line 54) and two samplers `sampleSurface`/`sampleMedium` (inverse CDF of `tau`). Everything else is built from them with two booleans, `startOnSurface` and `endOnSurface`:

* `eval(tau, start, end)` (lines 22-30) is the **edge throughput E**: `surfaceSurface(tau)` if both ends are on surfaces, `mediumMedium(tau)/sigmaBar()` if both are in the medium, and `mediumSurface(tau)` for **both** mixed cases. So `E` is symmetric under swapping start and end by construction [T].
* `surfaceProbability(tau, start)` (lines 35-38) is the discrete probability of a surface end; `mediumPdf(tau, start)` (lines 39-42) is the density of a collision end, per unit `tau`.
* Concrete laws: exponential (`transmittances/ExponentialTransmittance.cpp`), Erlang (`ErlangTransmittance.cpp:32-67`, `sigmaBar = lambda/2`), Davis (`DavisTransmittance.cpp:31-60`, `sigmaBar = 1`, `mediumSurface = surfaceMedium`); others in the same directory (double exponential, linear, quadratic, pulse, interpolated, Davis-Weinstein) not read in detail. For the two I read, `surfaceMedium = sigmaBar * mediumSurface` and `d/dtau surfaceSurface = -surfaceMedium`.
* The sampled collision carries the weight `E * rho * sigma_s * sigmaBar / (rho * sigma_t * mediumPdf)` (`media/HomogeneousMedium.cpp:65-108`, `media/VoxelMedium.cpp:141-145`), which is the albedo `sigma_s/sigma_t` for every start kind [D, checked algebraically against `eval` and `mediumPdf`]. The integrand of a path therefore has one factor `sigma_s * sigmaBar` per medium vertex and one factor `1/sigmaBar` per medium-to-medium edge, i.e. a net `sigmaBar` per run of medium vertices [D]. Any new estimator must use this convention, not the textbook `sigma_s` per vertex.
* **There is no adjoint flag on the medium.** The word `adjoint` occurs in Tungsten only for BSDFs and for light selection (`integrators/TraceBase.hpp:42,130,166,171`, `PathVertex.cpp:61,109,193`, `LightPath.hpp:16,55-69`). The transport direction enters the media code only through which end is called "start" [T]; this is the answer to the "adjoint flag" of the paper summaries.

**(2) Forward versus reverse density; birth versus collision.** The reverse density of an edge is **the same function called on the reversed ray with the two flags swapped**: `Medium::transmittanceAndPdfs` (`media/Medium.cpp:45-51`) returns `E(start,end)`, `pdf(ray, start, end)` and `pdf(reversed ray, end, start)`; the BDPT walk does the same for a freshly sampled edge (`integrators/bidirectional_path_tracer/PathVertex.cpp:162-163`). The two values are stored on the edge (`PathEdge.hpp:15-16`, `reverse()` swaps them at line 46-49). They differ because the start kind enters the density: for Erlang `surfaceMedium` and `mediumSurface` differ by `sigmaBar` [T], while `E` is the same. "Birth versus collision" is the **start kind**: `MediumState.firstScatter` (`media/Medium.hpp:30-46`; `reset()` at `integrators/TraceBase.cpp:563` when the walk is at a surface, `advance()` after each medium collision), and in the bidirectional code `PathVertex::onSurface()` = `type != MediumVertex` (`PathVertex.hpp:239-242`), so **camera and emitter vertices count as surfaces**, even if they sit inside a medium [T]. Hence the segment needs two kinds (start, end), not one `origin`: the end kind is known only after the sample (exit versus collision).

The pass-through surface case: a shadow ray that crosses a transparent surface is split there, and each part is evaluated as a new segment starting on a surface (`TraceBase.cpp:62-125`, `startsOnSurface = true` at line 117). For a non-exponential law this treats the transparent surface as a re-birth, which is right only if the surface really changes the medium. Two divergences inside this one function: the pdf variant passes `didHit || endsOnSurface` as the end kind (line 107) and the plain variant passes `endsOnSurface` (line 111), so for a ray through a transparent surface the two variants differ [T]. It is harmless for the exponential law (the flags are ignored).

**(3) Which integrators, and how MIS is computed.**

| Tungsten integrator | non-exponential media | how |
|---|---|---|
| path tracer (`integrators/path_tracer/PathTracer.cpp:28-140`) | yes | `sampleDistance` per segment; NEE and BSDF/phase sampling combined with the power heuristic on **angular pdfs only** (`TraceBase.cpp:282,350,378`); the distance part is deterministic in a shadow ray, so it needs no pdf in the weight [D] |
| light tracer (`light_tracer/LightTracer.cpp:40-100`) | yes | the same `sampleDistance` from the emitter (`state.reset()` at birth), `volumeLensSample`/`surfaceLensSample` use `generalizedShadowRay` with explicit flags (`TraceBase.cpp:167,197`) |
| bidirectional path tracer (`bidirectional_path_tracer/`) | yes | per-edge `pdfForward`/`pdfBackward` folded into the vertex pdfs (`LightPath.cpp:50-53,69-70`), converted to area/volume measure (`LightPath.cpp:81-92`), then Veach's ratio recurrence `pi *= pdfForward[i-1]/pdfBackward[i-1]` (`LightPath.cpp:145,157`). Connection edges get `E` and both pdfs from `generalizedShadowRayAndPdfs` (`LightPath.cpp:344-345,358-359,385-386`) |
| photon map, progressive photon map (`photon_map/PhotonTracer.cpp`) | walk yes; beams and volume photons evaluate `transmittance(..., true, false)` with fixed flags (lines 131,152,190,292) | density estimation: no free-flight MIS |
| Kelemen / multiplexed / reversible-jump MLT | **no for media**: `Medium::invertDistance` is `FAIL("not implemented")` (`media/Medium.cpp:40-43`) and no medium overrides it (`PathVertex.cpp:239-240` calls it) | |
| VCM | none in the repository | |

The BDPT MIS is the same recurrence as ours with `pdfFwd`/`pdfRev` per vertex, and the free-flight densities enter **multiplied into the vertex pdf**: `evalPdfs` (`PathVertex.cpp:295-326`) uses `nextEdge.pdfForward * bsdf/phase pdf` and `prevEdge->pdfBackward * ...`, then `/rSq` and the cosine. So the bookkeeping of the sketch (a forward and a reverse free-flight density per segment, multiplied into the vertex density) is what the reference does [T]. Chains of "forward" (pass-through) vertices are collapsed into one edge whose pdfs are the products (`LightPath.cpp:40-55`). Dirac media (`Transmittance::isDirac`, `Medium::isDirac` at `media/Medium.cpp:58-61`) make a segment non-connectable (`PathVertex.cpp:330-335`), which is the same idea as our delta boundary.

Chromatic media: `sampleDistance` picks one colour channel uniformly (`nextDiscrete(3)`) to sample `tau`, and the density is the **channel average** of the per-channel densities (`.avg()`, `VoxelMedium.cpp:138,141`; `HomogeneousMedium.cpp:94,96,126,128`), the weight being `E/pdf_avg`: one-sample MIS over channels. This is a deterministic density, so it feeds the MIS ratios directly [T]. For us this means: the hero-wavelength ratio of the sketch is the ratio of per-wavelength densities, and with a hero-wavelength-only sampler the density to use in the ratios is the average over wavelengths [D], which is not what the exponential null-collision code does today.

**(4) Distance sampling in heterogeneous media: no tracking, an optical-depth inversion.** The medium separates the 1D law from the 3D density exactly as the paper says, but the code never does delta tracking: `VoxelMedium::sampleDistance` (`media/VoxelMedium.cpp:97-154`) samples `tau` from the law, divides by `sigma_t` of the sampled channel, and inverts the accumulated density along the ray with `Grid::inverseOpticalDepth` (voxel DDA, `grids/VdbGrid.cpp:379`, `ExactNearest`/`ExactLinear`/`Raymarching` sample methods). The optical depth of an evaluation (`Grid::opticalDepth`, `VdbGrid.cpp:307-377`) is exact for `ExactNearest`/`ExactLinear`, a jittered fixed-step quadrature for `Raymarching`, and a **stochastic residual-ratio estimate** for `ResidualRatio` (lines 332-358). Exponential, atmospheric and homogeneous media have closed-form `inverseOpticalDepth` (`media/ExponentialMedium.cpp:95-103`, `media/AtmosphericMedium.cpp:113`). General case: no majorant-based sampler exists in the code; heterogeneous non-exponential media are limited to what the density field can integrate and invert, which is the "unclear in the general case" of the paper [T agrees with P]. The stochastic estimate is only valid inside a law of the form `exp(-tau)`, because it is then plugged into a nonlinear function; with another law it is biased, and I found no guard against that combination in the files I read [D; guard search incomplete]. For our design: the non-exponential model needs a **deterministic** `OpticalDepth(segment)` and `InverseOpticalDepth(segment, tau)`, not a tracker.

**(5) Overlapping media.** Not supported, and not by a priority rule either: `Primitive::selectMedium` (`primitives/Primitive.hpp:177-183`) simply returns the exterior or interior medium of the crossed primitive (or keeps the current one if the primitive overrides nothing), so a point is in exactly one medium, with no stack. The BDPT vertex stores one `_medium` pointer (`PathVertex.hpp:63`, `selectMedium` at `PathVertex.cpp:379-392`). Our boundary stack with priority is more than the reference has; the "may be summed only with the same family" rule of 5d stands [T confirms: no summation exists].

**(6) Reciprocity: what the code settles.** The integrand is reciprocal by construction: `E` depends on `tau` and the unordered pair of end kinds (`Transmittance.hpp:22-30`), and the medium vertex factor `sigma_s * sigmaBar` does not depend on direction. Sampling densities are **not** symmetric (`surfaceMedium != mediumSurface` in general), so each edge stores two densities, as above. That is what the published TOG text claims (modified kernel reciprocal; PT, LT and BDPT agree), and it is what the technical report's remedy ("quantities evaluated consistently", Sec. 3.8 [P]) describes; the report's "not reciprocal" would refer to the unmodified kernel, in which the asymmetry sits in `E`. **I cannot decide from the code which sentence of which paper is "right"**: the repository has no test scenes and no regression test for non-exponential media (a search for `erlang`/`davis` outside the transmittance files finds nothing), so there is no executable evidence that PT = LT = BDPT. What the code does settle is the convention an implementation must follow: symmetric `E` with `sigmaBar` normalisation, asymmetric stored densities. Our phase-4 slab test (PT = LT = BDPT) stays as the proof for our port.

**Code defects noticed (not our problem, but they limit "ground truth")** [T]: `ExponentialMedium::sampleDistance` calls `_transmittance->eval(..., sample.exited)` (line 133) before it assigns `sample.exited` (line 134), so it uses a stale flag (harmless only for the exponential law); `VoxelMedium::pdf` evaluates the density at the **ray start** (`_grid->density(p)`, line 188) although a medium-end collision is at the ray end, so the stored reverse density (`PathVertex.cpp:163`) and the density of a connection edge are wrong for varying density, while the forward density returned by `sampleDistance` (line 141) is right. The MIS weights stay unbiased only if every strategy sees the same stored value for the same edge; a walked edge (forward value from `sampleDistance`) and the same edge seen from a connection (value from `pdf()`) can differ, so I would not copy this function. Treat Tungsten as ground truth for the interface, not for every number.

### 4.3 SGGX microflakes (Heitz et al. 2015)

[P: PDF] `S` a symmetric positive-definite 3x3 matrix; projected area `sigma(w) = sqrt(w^T S w)` (even: `sigma(w) = sigma(-w)`); `sigma_t(w) = rho sigma(w)`, `sigma_s(w) = alpha rho sigma(w)`; phase `fp(wi->wo) = (1/sigma(wi)) int p(wm, wi->wo) <wi, wm> D(wm) dwm`; the "unusual reciprocity" `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)` (no shadowing, only masking); importance sampling by the distribution of visible normals, weight always 1; the integrator "is independent and out of the scope"; their results use a forward path tracer. The paper does not discuss delta tracking, next-event estimation or bidirectional methods [P].
My derivation [D]: along a ray with fixed direction `w` the extinction is a scalar, so the exponential machinery applies with `sigma_t(x, w)`; a direction-independent majorant (`>= rho sqrt(lambda_max(S))`) keeps the null coefficient `sigma_n = sigma_maj - sigma_t(x, w)` direction-dependent but even in `w`, so the reversed path sees the same `sigma_n`. The vertex weight `sigma_s(wi) fp(wi->wo)` is symmetric by the reciprocity relation, so the throughput is symmetric. The *sampling* densities are not: forward uses `sigma_t(wi)`, reverse `sigma_t(wo)`, and `fp(wi->wo) != fp(wo->wi)`; so `pdfRev` must be evaluated with the reversed directions (as `Vertex.PDF` already does for the phase function); the distance densities are the direction-independent majorant's and cancel as in the exponential case and the real/null event probabilities use the direction of the ray. This needs `SamplePoint` to take the direction. It is **not** a new transport model. Polarisation: the model is scalar [P]; in a polarised path it can only be treated as depolarising [D].

### 4.4 Estimators (layer 3)

| Estimator | Source | What it needs / gives |
|---|---|---|
| delta tracking | classic; null-scattering view [P: Miller 5.2] | exponential law, majorant; exact pdf in null-scattering space |
| ratio tracking | Novak 2014; = null-subpath sampling [P: Miller] | transmittance estimate; unbiased with negative `sigma_n` allowed [P: Novak abstract: handles negative densities] |
| residual ratio | Novak et al. 2014 [P: abstract page only] | control `sigma_c` in closed form, ratio tracking on the residual; the repo's `ResidualRatioEstimator` |
| spectral tracking | Kutz et al. 2017 [P] | one tracker, one majorant for all wavelengths, event probabilities from throughput; "the PDF of the free-path sample cannot be evaluated exactly", MIS only works if the same tracker is used for free-path and transmittance sampling, "does not generalize" (Sec. 7) |
| decomposition tracking | Kutz et al. 2017 [P] | homogeneous control + residual; weights can be negative |
| regular tracking / raymarching | Bitterli 2018 [P] | the only unbiased general distance sampler for non-exponential heterogeneous media |
| equiangular, emission gather/NEE | Kulla-Fajardo, Fong [C: `equiangular.cs`] | uses of a segment, not estimators of the medium |

### 4.5 Deferred: spatially correlated media and the refractive RTE

Not in the spec, no hooks in the interface of section 6. Pointers only:

* Spatially correlated media: Jarabo, Aliaga, Gutierrez, TOG 2018 (arXiv:1805.02651). Forward estimators only (their Mitsuba implementation uses random-walk sampling and next-event estimation); Bitterli et al. say it lacks a path-integral formulation, which "makes integration with bidirectional methods difficult" [P].
* Refractive RTE: the equation is Ament, Bergmann, Weiskopf, TOG 2014 (a photon-mapping solver) [P: dissertation excerpt]; unbiased path tracing, NEE and bidirectional estimators are Pediredla et al., TOG 2020 [P: project page]; Fraboni et al., SIGGRAPH 2023 talk, bend rays at null events and have NEE but no bidirectional estimator [P]. An implementation of the Pediredla estimators exists on `experimental/refractive-media` (volpath and bdpt, polarised; `docs/refractive-media.md` on that branch) [C].

What re-adding them would cost is in section 6.1.

## 5. Decisions

### 5a. Plug-in granularity

| Option | What | For | Against |
|---|---|---|---|
| A1 monolith | one `IVolume` per framework doing everything | maximum freedom | every framework re-implements majorant tracking, estimators, MIS bookkeeping; no reuse of grid/OSL/emission code |
| **A2 three layers (recommended)** | `TransportModel` (law + geometry) over `Medium` data, with `Tracker/TransmittanceEstimator` as today, bundled by the existing `VolumeIntegrator` | reuses all media; SGGX needs only a data-layer change; exponential model is the existing code moved behind an interface | combinations need validation (a compatibility table at scene build) |
| A3 status quo plus `as`-branches | special cases in the walk, as the refractive stage on its branch and the flags of `procedural-media` do | no refactor | about +800 lines in `integrators.cs` for one framework (the refractive branch), `ifs` in every integrator, no way to add a framework without editing all of them (section 1, item 2) |
| A4 compile-time generics | `PathWalk<TModel>` | no virtual calls | the model is chosen per medium at run time and several can be in one scene; code size multiplies with integrators x models |

Dispatch: **one virtual call per segment** (and per collision through a pull iterator, below), not per strategy-loop step. Today there is a type-test chain per `SampleT_maj` call and a delegate invocation per collision with a closure allocated per walk iteration (`pathwalk.cs`, `new Func` capturing `ctx, hook, scattered, terminated`) [C]; a pull API (`NextEvent(ref state, ...)`) removes both and gives plug-ins their own state.
Per-medium history state: **transient in `WalkContext`** (a `SegmentState` returned by `BeginSegment`: the progress inside the segment, about 16 bytes plus the majorant-iterator reference of the exponential model; the segment is the walk's straight ray, so no origin, direction or tangent is stored), plus a **persistent `SegmentRecord` in the vertex** (12 bytes: free-flight density forward and reverse for the hero wavelength, and the two endpoint kinds). Not in `PathState` globally (it is per walk, not per vertex, and MIS needs per-vertex values). Rejected: an opaque per-vertex blob owned by the model (variable size, defeats the pooled-vertex plan).

### 5b. The black box

| Option | What | Verdict |
|---|---|---|
| B1 **delta-boundary** | the volume is entered and left through boundary vertices flagged `delta`; inside, the model samples the walk and returns the weight; no connection to or through the interior | **universal fallback, recommended first** |
| B2 evaluable black box | exports `Connect`, exact `SegmentDensity`, emission light | per-volume upgrade through capability flags |
| B3 sub-renderer | the volume runs its own recursive or cached estimator and presents a radiance/transmittance table to the outside | only for volumes that are not light-transport at all (precomputed scattering tables); biased if cached; not a general mechanism |

B1 in MIS terms [D]: the existing specular-chain rule: `delta` vertices give `a_k = 0` (`bdptincremental.cs:89` and `:123` `!v.delta && !before.delta`, `:750` `!ptMinus.delta` for the connection side) so no strategy connects to the entry and exit vertices, and interior paths are found by walks only. This keeps the weights correct as long as the black box gives an unbiased weight for the exit state and reports `pdfRev = 0` at the exit as specular vertices do. Light subpaths entering the box need the model's **adjoint** mode (Bitterli's adjoint flag has the same shape [P]); a model without adjoint mode makes the box camera-walk-only, which `StrategyAllowed` can express per volume.
Emission inside a black box: the model reports `Le` along its walk and the gather is added with weight 1 (no competing strategy exists, as with specular light) [D]; if it also exports a `VolumeEmissionLight`-style sampler with exact pdf (capability `EmissionLight`) the existing MIS applies.
Overlap and nesting: Bitterli's theory supports one global free-flight pdf only [P], and `CompositeMedium` adds coefficients, which is only valid when the volumes share one law [C+D]. Rule: volumes with the same `TransportModel.Family` and `CombinableWith` (today: exponential) combine as `CompositeMedium` does; otherwise the priority stack decides (the existing `MediumInterface.priority`): the volume with the higher priority owns the region and the other is not seen there, with a one-time warning. A black box nested inside another volume pushes on the stack like any medium; segments inside it never reach the outer model.

### 5c. Estimator selection and the MIS-consistency rule

Two roles per volume: `Tracker` (distance sampling in walks) and `TransmittanceEstimator` (connections, shadow rays), as today, selected by the medium's parameters (`"string tracker"`, `"string transmittance"`), with defaults from the transport model. Equiangular and emission strategies are uses and stay in the integrators, gated by capability flags.

Each estimator declares a **class**: `ExactPdf` (delta, ratio with the same majorant, regular tracking by deterministic inversion), `Deterministic` (closed-form), `NoPdf` (residual ratio as a free-path sampler, spectral, decomposition, adaptive).

**Rule** [D, from Veach's requirement that the weights of the strategies that can produce a path sum to one over those strategies; Kutz et al. Sec. 7 says the same in the form "same tracker for free-path and transmittance" [P]]:
1. Strategies whose segment densities are all `ExactPdf` or `Deterministic` and are expressed in the same path space (same majorant per segment; same free-flight law) are weighted together by the balance heuristic as now.
2. A strategy that traverses a `NoPdf` volume has its `StrategyAllowed` bit **cleared for that path**, and the remaining strategies get the full weight. The choice depends only on the path (which volumes it crosses), never on random state, which keeps the weights a function of the path. Camera-walk-only is the typical outcome (as `strategies "unidirectional"` already is).
3. A shadow ray through a volume whose transmittance estimator uses a different majorant than the walk's tracker must not enter the r_u/r_l products; it is `Deterministic` or a separate unweighted term. Today's code does enter them (`ResidualRatioEstimator.TryEstimate` writes `r_l/r_u` with the residual majorant) and the walk uses `majorant`; whether that is exact for bdpt connections I did **not** audit; it needs a test (section 7, phase 3).

### 5d. Compatibility matrix (target, with today's status)

Legend: E exact (unbiased, MIS consistent), A approximate/biased or a documented restriction, U unsupported, `today` is what the code on this branch does.

| Framework | volpath | bdpt | bdptinc | mlt | upg | sppm | polarised | chromatic |
|---|---|---|---|---|---|---|---|---|
| exponential + null scattering | E (today) | E (today) | E (today, tests) | E (today, via BDPT evaluator) | own ext., not audited | U (no media code today) | E for Rayleigh phase (today); scalar phase passes Stokes unchanged [C] | E hero-wavelength (today) |
| SGGX (data layer) | E [D] | E [D] needs direction-aware `SamplePoint` and reversed-direction phase pdf | E [D] same | E [D] | A: `MediumFactor` needs the directional `sigma_t` | U | A: scalar model, depolarising [D] | E [D] |
| non-exponential homogeneous (Bitterli) | E [P] | E [P: both versions allow it] with stored fwd/rev free-flight densities [D] | E [D] recurrence unchanged, inputs change | E [D] inversion uses one sampler value | A: `MediumFactor` generalised to the model's density | U | U (no paper) [D] | E [D]: per-wavelength densities are deterministic, the null-collision r_u/r_l are replaced by their ratios |
| non-exponential heterogeneous | E but slow (regular tracking) [P] | E, same | E, same | E | A | U | U | E |
| black box (B1) | E | E, high variance (no connections inside) | E | E if the box is a deterministic function of its draws | E as a delta boundary | U | depends on the box | depends on the box |

Estimator x framework:

| | exponential | SGGX | non-exp homogeneous | non-exp heterogeneous |
|---|---|---|---|---|
| delta tracking | E | E [D] | U (only a pink-noise subclass [P]) | U |
| ratio tracking | E | E [D] | U; use closed form / quadrature (`Deterministic`) | U; regular tracking (`ExactPdf` by quadrature) |
| residual ratio | E | E with a direction-independent control (min over `w`) [D] | U | U |
| spectral / decomposition | E in volpath; in bdpt only with the same tracker both ways [P]; signed weights | same | U | U |
| hero-wavelength r_u/r_l | E (today) | E | replaced by deterministic density ratios | same |
| equiangular | E | E [D] | A: needs the segment density in the weight | A |
| emission gather / NEE | E | E | A (volumetric emitter integral: supplement Sec. 5 [P], not studied) | A |

### 5e. Performance

* The strategy loops stay straight-line: weights come from stored sums (`bdptincremental.cs`), the model is not called in them.
* Calls into the model: `BeginSegment` once per segment, `NextEvent` once per collision (today: one delegate call per collision + one closure allocation per walk iteration + a type-test chain per segment) [C]; a virtual call costs a few ns (typical figure, not measured here), against 50-400 us for a whole sample at depth 5-20 in the in-process benchmark [C: `docs/bdpt-incremental.md`]. I expect the plug-in layer to be neutral or slightly faster after the delegate removal, but **I have not measured it**; phase 1 includes that measurement.
* Memory: `SegmentRecord` of 12 bytes next to a 1344-byte `Vertex` (about +1%) and 84 bytes of `VertexMIS`. A model with no history leaves it zero.
* Models that are costly by nature: regular tracking (non-exponential heterogeneous).

## 6. Interface sketch (sketch only: not compiled, names may change)

```csharp
// Kind of one END of a segment (revision 3: two kinds per segment, as in Tungsten's startOnSurface/endOnSurface [T])
public enum EndpointKind : byte
{
    Surface = 0,    // a surface, a camera or a light (Tungsten counts camera and emitter vertices as surfaces)
    Medium = 1      // a scatterer of the medium
}

public enum SegmentEventKind : byte
{
    Collision = 0,
    Escape = 1
}

[Flags]
public enum VolumeCapabilities : uint
{
    None = 0,
    ExactSegmentDensity = 1, // Density() is exact, so strategies through the volume may be MIS-weighted
    Reserved2 = 2,           // (revision 3: the former Adjoint flag; Tungsten has none, reverse = swap the two endpoint kinds)
    Connectable = 4,         // strategies may connect to vertices inside; the segment is evaluated by Density/Transmittance
    EmissionLight = 8,       // exports a light with an exact density
    Equiangular = 16,
    Combinable = 32          // may be summed with other volumes of the same Family
}

// The progress inside one segment. The segment is the walk's straight ray, passed to every call.
public struct SegmentState
{
    public Float tMax;
    public Float travelled;
    public Float opticalDepth;          // accumulated optical depth (regular tracking)
    public Float opticalDepthTarget;    // the sampled free flight, by inverse CDF; 0 if the model does not use it
    public RayMajorantIterator majorants; // the exponential model: the existing iterator
}

// Persisted in the vertex that ends the segment
public struct SegmentRecord
{
    public Float freeFlightPdfForward;
    public Float freeFlightPdfReverse;
    public EndpointKind startKind;       // known before the sample
    public EndpointKind endKind;         // known after the sample: Escape -> Surface, Collision -> Medium
}

public struct SegmentEvent
{
    public SegmentEventKind kind;
    public Point3f position;
    public MediumProperties properties;      // sampled with the direction of the ray for direction-dependent media
    public SampledSpectrum majorant;
    public SampledSpectrum majorantTransmittance;
}

public abstract class TransportModel
{
    public abstract string Family();
    public abstract VolumeCapabilities Capabilities();

    public abstract void BeginSegment(Medium medium, in Ray ray, Float tMax, EndpointKind startKind,
                                      SampledWavelengths lambda, out SegmentState state);

    // Pull: the next collision candidate of the segment, or the escape through its end
    public abstract SegmentEvent NextEvent(Medium medium, in Ray ray, ref SegmentState state, ref RNG rng, Float u);

    // The densities of the straight segment between two points, for the hero wavelength, and the ratios to the
    // other wavelengths (today's r_u and r_l, segU and segL)
    // Revision 3: the REVERSE density is this same call with from/to and the two kinds swapped
    // (Tungsten: Medium::transmittanceAndPdfs, media/Medium.cpp:45-51); the walk stores both
    public abstract SegmentRecord Density(Medium medium, Interaction from, Interaction to,
                                          EndpointKind startKind, EndpointKind endKind,
                                          SampledWavelengths lambda, out SampledSpectrum ratioUnidirectional,
                                          out SampledSpectrum ratioLight);

    // The edge throughput E(start, end): symmetric under swapping the kinds [T]; for the exponential model the
    // kinds are ignored. Non-exponential models take the optical depth from OpticalDepth() below.
    // Non-exponential heterogeneous models only (deterministic, not a tracker; Tungsten's Grid::opticalDepth /
    // inverseOpticalDepth, grids/Grid.hpp:24-25): optical depth of a straight segment, and the point where it reaches tau
    public virtual Float OpticalDepth(Medium medium, in Ray ray, Float tMax) { throw new NotSupportedException(); }
    public virtual Float InverseOpticalDepth(Medium medium, in Ray ray, Float tMax, Float tau, out bool escaped) { throw new NotSupportedException(); }

    public abstract SampledSpectrum Transmittance(Medium medium, Interaction from, Interaction to,
                                                  EndpointKind startKind, EndpointKind endKind,
                                                  SampledWavelengths lambda, ref RNG rng,
                                                  out SampledSpectrum ratioUnidirectional,
                                                  out SampledSpectrum ratioLight);
}
```

`ratioUnidirectional` and `ratioLight` are today's `r_u` and `r_l` (and `segU`/`segL` in bdpt): the interface only names them. `VolumeIntegrator` gains the `TransportModel` and keeps `tracker` and `transmittance`; the exponential model implements `NextEvent` through the existing `SampleT_maj` code so that the random numbers are drawn in the same order (section 7, phase 1). A volume without `ExactSegmentDensity` is walked from the camera only; no separate "forward only" flag exists. A strategy mask per path (`StrategyAllowed`) stays the mechanism for `NoPdf` estimators and black boxes.

### 6.1 What changed from revision 1, what stays, and the cost of re-adding the deferred frameworks

**Revision 3 (Tungsten) interface changes, in one place:** (a) `SegmentOrigin` (one flag) becomes `EndpointKind` start and end on `SegmentRecord`, `BeginSegment`, `Density`, `Transmittance`; (b) the reverse density is the same `Density` call with from/to and kinds swapped, no separate reverse routine; (c) the `Adjoint` capability is dropped; (d) `OpticalDepth`/`InverseOpticalDepth` added for non-exponential heterogeneous media; (e) the transmittance has the three-case `E` with `sigmaBar` normalisation and the vertex factor `sigma_s * sigmaBar` for non-exponential laws; (f) chromatic MIS uses the wavelength-averaged density for that model. Unchanged: the three layers, the pull iterator question, capability flags `ExactSegmentDensity`/`Connectable`/`EmissionLight`/`Equiangular`/`Combinable`, the delta-boundary black box, estimator classes, phases 0-6.

Removed with RRTE and Jarabo (revision 1 had them): the tangent in `SegmentEvent`; `origin`, `direction` and `length` in `SegmentState` (the walk's ray is the geometry); the index into an `EdgeGeometry` in `SegmentRecord` (so no area Jacobians and no `J(a->b) = (n_a/n_b)^2 J(b->a)` asymmetry to store); `TryConnect` (the boundary-value solve of a curved connection, with its multiple-path count); the masks for connections that cannot be curved (`CurvedConnectionAllowed`); the manual stack popping at the end of a curved path; the estimator class that is not a function of the primary sample vector (the randomised multi-start solver); the `ForwardOnly` idea (a Jarabo model would simply lack `ExactSegmentDensity`); the dispersion limit of curved paths.

Stays, and why:

* **Three layers.** The non-exponential law differs from the medium data (layer 1 versus 2), SGGX is layer 2 only, and the estimators (layer 3) are shared by all. What shrank is layer 1: only the free-flight law, never the segment geometry.
* **`SegmentRecord` with both free-flight densities and the two endpoint kinds**, because the non-exponential law's density depends on the start kind and the reverse of an edge swaps the kinds [T] (revision 2 had one `origin` and an `Adjoint` flag from the paper summaries). Exponential and SGGX volumes leave it zero.
* **Capability flags.** `ExactSegmentDensity` is needed by the black box and by `NoPdf` estimators; the `Adjoint` flag of revision 2 is **dropped**: Tungsten has no such flag on media or transmittances, every law answers the reversed call by swapping the endpoint kinds, and a law that cannot (no exact density) is already excluded by `ExactSegmentDensity`; `Connectable` by the evaluable black box (B2); `Equiangular` and `EmissionLight` by non-exponential and black-box volumes; `Combinable` by the overlap rule.
* **Delta-boundary black box, the strategy mask and the MIS-consistency rule (5c).** Unchanged. The black box must still be a deterministic function of its draws to be allowed in MLT.
* **Direction-aware `SamplePoint`** (SGGX, and it removes OSL's thread-statics).

Corrections to the expectation:

* The pull iterator is **no longer necessary**; its justification is now the removal of the delegate and closure per collision and the state of regular tracking and of black boxes. It is a cleanliness and speed choice, and phase 1 may keep the callback adapter as the only API at first.
* `SegmentState` is smaller in the geometry fields but still carries progress (optical depth) and, for the exponential model, an iterator reference, so it is not obviously under the revision-1 figure of 32 bytes in memory layout; this does not matter, it is transient.
* There is **no separate `Connect()`**: for straight segments a connection to a vertex inside a connectable volume is `Density`/`Transmittance`. What the removal of curved paths takes away is the solver, not the need for an evaluable black box.
* The asymmetry that the curved model had (Jacobians) is replaced, not removed: the non-exponential law has forward and reverse densities that differ, stored in the same record.

What re-adding the deferred frameworks would cost (so that the decision is reversible; no hooks are designed in):

* **RRTE:** the walk assumes that the ray is straight (`ctx.ray` is intersected as a line and handed to the tracker); curved segments need the walk to ask the model for the exit point and the tangent at collisions, a stack pop done by hand, per-vertex tangents and Jacobians (the branch stores an `EdgeGeometry` and two Jacobians per vertex), a connection solver with randomised restarts, a strategy mask for connections that cannot be curved, an estimator class that MLT must exclude, and no support for dispersive indices. The branch is the starting point (the revert commit `1f6dcac` touched 16 files, 4838 lines deleted and 68 added, including the 800 + 85 lines of integrator and walk changes) and could be merged behind the model interface once the walk accepts non-straight segments. This is a change to the walk and the vertex, not only a new model.
* **Jarabo:** a new `TransportModel` plus a path-integral formulation for bidirectional use, which the authors do not give [P]; until then it lacks `ExactSegmentDensity`, which already keeps it out of every bidirectional strategy. Cost: one model class, no interface change.

## 7. Migration plan (existing output bit-identical until a phase says otherwise)

Common rule: before each phase, record image hashes (fixed seed, in-process `RenderImage` of `bdptincremental_stat_test.cs`) of volpath, simplevolpath, bdpt, bdptinc, mlt on the media scenes of `BdptInc` (fog, foghg, fogchrom, foggrid, fograyleigh, fognested, fogemit, equiangular, polarised Rayleigh fog); the phases below that say "bit-identical" must reproduce them exactly.

| Phase | Change | Output | Test |
|---|---|---|---|
| 0 | hash harness for the integrators above; add the weights-sum-to-one test for a path | none | the hashes themselves |
| 1 | `TransportModel` + `ExponentialModel` (moves `SampleT_maj`, `MediumEvents`, `PathState` null-collision code behind the interface; pull iterator with a delegate adapter, optional at first) | bit-identical | hashes; microbenchmark of the segment call (ns per segment, old versus new) |
| 2 | direction-aware `SamplePoint(p, lambda, w, time)` default-forwarding; fold `OSLMedium`'s thread-statics into it; `SGGXMedium` | bit-identical for old media; new medium | SGGX against its closed forms: `sigma(w)` and `sigma_t`, homogeneous transmittance `exp(-rho sigma(w) L)`, phase normalisation (energy), the reciprocity relation `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)`, chi-square of visible-normal sampling; volpath/bdpt/bdptinc agreement (t-tests as `UnbiasedAgainstBdpt*`) |
| 3 | `SegmentRecord` in `Vertex`; `Density()` for the exponential model returns the current factors; bdptinc/bdpt read it; the `ResidualRatioEstimator` MIS question of 5c.3 gets its test | bit-identical (the record is zero for exponential) | hashes; weight equality; test of residual/analytic/tracklength connections against ratio on a grid medium |
| 4 | `NonExponentialModel` (homogeneous kernel first: the four functions `surfaceSurface/surfaceMedium/mediumSurface/mediumMedium` + `sigmaBar` and the two inverse-CDF samplers as in Tungsten's `Transmittance`; heterogeneous by optical-depth inversion on the grid after, not by tracking) | new | analytic (also against Tungsten's closed forms for Erlang and Davis, section 4.2): `d/dtau surfaceSurface = -surfaceMedium`, `surfaceMedium = sigmaBar * mediumSurface`; the albedo identity of the sampled weight; free-flight pdf integrates to `1 - T`, mean free path; slab transmittance by PT, LT and BDPT agree (Bitterli's Fig. 12 claim reproduced as our test); energy conservation in a closed medium; fixed-seed bdptinc/bdpt weight sums to one with the stored densities |
| 5 | black box B1 (wrap a medium; delta boundary; `StrategyAllowed` per path) | new | the wrapped exponential medium gives the same expectation as the unwrapped (t-tests), lower strategy count, nested-in-medium and medium-in-box scenes |
| 6 | spectral and decomposition trackers as `NoPdf` estimators on exponential volumes | new | unbiasedness against ratio tracking on a grid; signed weights; the `StrategyAllowed` mask in bdptinc |

## 8. Risks and open questions

* Reciprocity of the non-exponential kernel: the reference code implements a symmetric `E` with `sigmaBar` normalisation and asymmetric stored densities (section 4.2 item 6); it ships no test, so whether the two paper versions disagree in substance is not settled by it. Phase 4 keeps the numerical test (PT = LT = BDPT on a slab) as the proof for our port.
* Heterogeneous non-exponential media have no known unbiased fast distance sampler [P]. Tungsten does not track: it integrates and inverts the optical depth along the ray (voxel DDA, closed forms for exponential and atmospheric profiles) [T], so the model needs deterministic `OpticalDepth`/`InverseOpticalDepth` of a segment, and a stochastic optical depth (residual ratio) is invalid with a non-exponential law (4.2 item 4). The cost is the DDA per segment, twice per edge when the reverse density is computed.
* Direction-dependent extinction in the null-scattering framework (SGGX) is my derivation [D], not in either paper; the test in phase 2 is the proof.
* Chromatic: Tungsten samples one channel uniformly and uses the channel-averaged density in the weights and MIS ratios [T]; for the non-exponential model our hero-wavelength ratios would come from deterministic densities, which is better than null-collision ratios [D], but the density that enters the MIS is then the wavelength average, not the hero's.
* Estimators of class `NoPdf` force strategy masks and therefore high variance in bdpt; the benefit is only in walks.
* Signed weights and `Russian roulette` in `ShadowRay` interact (the roulette tests `T_ray` magnitude) [C]; needs a sign-aware rule.
* MLT: a black box (or any `NoPdf` estimator) whose sampling is not a deterministic function of its random draws is excluded; the repo's geometry-hashed RNG gives determinism for stochastic estimators but correlates the mutations [D].
* Pseudo-marginal (stochastic) segment densities: ratios of unbiased estimates are not unbiased estimates of the ratio; the rule in 5c avoids them rather than solving them. Whether an UPG-style trial estimator could carry them is an open research question.
* Thread safety: models keep state per segment, none per medium; `OSLMedium`'s thread-statics must go.
* Emission inside non-exponential media: the volumetric emitter path integral exists (supplement Sec. 5) but I did not read it [P: pointer only].
* Overlap of different laws is unsupported by the theory [P] and by the reference code, which has one medium per point and no stack [T]; the priority rule is a restriction, not a solution.

## 9. Recommendations in one list

1. Three layers with the existing `VolumeIntegrator` as the facade (A2); straight segments only; a pull iterator (optional at first), a transient `SegmentState` of progress, a 12-byte `SegmentRecord` in the vertex.
2. Treat SGGX as medium data with a direction-aware `SamplePoint` (phase 2), not as a transport model.
3. Black box = delta boundary first (B1); capability flags promote volumes to connectable, exact-density, emission-light.
4. Estimator classes `ExactPdf` / `Deterministic` / `NoPdf`; MIS only among strategies that can all state their density; `NoPdf` volumes mask strategies per path.
5. Phases 0-3 are refactors that must be bit-identical; every later phase brings an analytic test and a t-test against a brute-force reference.
6. RRTE and Jarabo are deferred; section 6.1 says what re-adding them costs.

## 10. Phase-1 interface questions for the user

* Is the pull iterator wanted in phase 1, or should the callback API stay until the non-exponential model needs state?
* Do we accept the priority rule for overlapping volumes of different laws?

## 11. What I could not verify

* I read none of the papers as raw PDF text: all [P] statements come from a fetch tool that summarises. The Bitterli TOG and TR versions were never both read as text; the reciprocity question is addressed from the code in 4.2 item 6; the SGGX phase function formulas were extracted from garbled text (Eq. 12 exponents unchecked).
* For the deferred frameworks (pointers only): Pediredla 2020 abstract and project page only (the CMU page was blocked); Ament 2014 a dissertation excerpt (first 100k characters), not the TOG paper; Jarabo 2018 the abstract page; Fraboni 2023 the PDF summary. Novak 2014: abstract page only (PDF fetch failed); d'Eon 2018: abstract.
* Not read: Vicini et al. 2021 (non-exponential transmittance for scene representations, [R]), d'Eon's later parts, Pediredla's code, LuxCore's media code. `procedural-media` was inspected only at the places cited; it has 36 commits ahead of the merge base that I did not review.
* The behaviour of `sppm`/`path`/`lightpath`/`simplepath` in scenes with media was inferred from the code (no medium scattering), not by rendering.
* The claim that distance densities cancel in the exponential case is my reading of the code, corroborated by the weight-equality and unbiasedness tests of bdptinc, but not proven.
* No performance numbers for the proposed interface exist; section 5e is an estimate.
* Tungsten (revision 3): read as text only, never built or run, so every [T] statement is a code reading, not an observed behaviour. Read in full: `Transmittance.hpp`, `Medium.hpp/.cpp`, `HomogeneousMedium.cpp`, `VoxelMedium.cpp`, Exponential/Erlang/Davis transmittances, `TraceBase.cpp` (shadow rays, direct light), `PathTracer.cpp`, `PathEdge.hpp`, `PathVertex.cpp`, `LightPath.cpp` (MIS part), parts of `LightTracer.cpp`, `VdbGrid.cpp` (optical depth and inversion, first branches), `ExponentialMedium.cpp`. Only grepped: `PhotonTracer.cpp`, the other transmittance laws, `AtmosphericMedium.cpp`, `Primitive.hpp`. Not read: `BidirectionalPathTracer.cpp`, the MLT integrators, the `instancing` and `mis-rewrite` branches (a MIS rewrite may change item 3), the project's supplemental material and test scenes (none in the repository).
* From the code I could NOT establish: that PT, LT and BDPT agree for non-exponential media (no test in the repository); which paper sentence on reciprocity is correct; whether a guard prevents the `ResidualRatio` optical-depth mode with a non-exponential law; whether the divergences in 4.2 (stale `exited`, density at ray start in `VoxelMedium::pdf`, shadow-ray flag mismatch) affect Bitterli's published images.
* The net factor `sigmaBar` per run of medium vertices (4.2 item 1) is my algebra from the code, not a statement in a paper.
