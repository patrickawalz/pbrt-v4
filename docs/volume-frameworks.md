# Volume frameworks: a pluggable architecture (design note, no engine code)

Status: design note. Nothing in this note changes engine code. Branch `feature/bdpt-incremental-mis`, written against commit `4e3d77b`+.

## 0. How to read this note

Every factual claim carries a tag:

* **[C]** I read it in the code of this repository (file and line given; lines are of the worktree at this commit).
* **[P]** I obtained it from the paper or its page with a fetch tool. The tool returns a summary of the page, not raw text, so quotation marks below mean "the summary quotes", not "I checked the PDF". Where two versions of a paper disagree I say so.
* **[D]** Derived by me from the code and the papers. Not in any paper, not tested. Treat as a hypothesis with a test attached.
* **[R]** Recalled from memory, not verified in this session.

What I could and could not read is listed in section 11.

## 1. Corrections to the working hypothesis

1. **"Every volume has its own integrator" already exists in part.** `VolumeIntegrator` (`volumeintegrator.cs`) is attached to every `Medium` (`Medium.volumeIntegrator`, `media.cs:1413`) and is a pair `(VolumeTracker, TransmittanceEstimator)`, modelled on Fong et al., "Production Volume Rendering" [C]. Trackers: `majorant` (the medium's own majorant iterator), `global`. Transmittance estimators: `ratio`, `analytic` (Beer-Lambert for a homogeneous medium), `tracklength`, `residual` [C]. What is missing is the **transport model** (the free-flight law): the walk, `PathState`, `MediumEvents` and the shadow ray hard-code the exponential law around that pair (section 3).
2. **"RRTE" is already implemented, on another branch.** `experimental/refractive-media` (reverted from this line in commit `1f6dcac`, "Move refractive media (RRT) to experimental/refractive-media") contains `refractiveindex.cs`, `refractiveray.cs`, `refractiveconnection.cs`, `refractivemedium.cs`, tests, and `docs/refractive-media.md` [C]. It implements the estimators of Pediredla et al. 2020 (volpath and bdpt, polarised and not), written from the paper, not from MitsubaER [C, per its doc]. The Ament-Bergmann-Weiskopf 2014 paper is the *equation* (RRTE) with a photon-mapping solver (its Chapter 3, per the dissertation excerpt) [P]; the unbiased path estimators are Pediredla 2020 [P: project page]; Fraboni et al. 2023 is a third, different approach (refraction at null events) [P]. "RRTE" can therefore mean any of three things; I take it to mean *the RRTE of Ament 2014 solved with the estimators of Pediredla 2020*, which is what the repo built.
3. **The cost of RRTE in the current architecture is the argument for this note.** The refractive stage touched `integrators.cs` by about 800 lines and `pathwalk.cs` by 85, with `rayMedium as RefractiveMedium` branches inside the walk, a global `hasRefractive` flag, a `WalkContext.refractive` field, and `OnCurvedSegment` hooks [C: `git diff 1f6dcac 1f6dcac^`]. `OSLMedium` already carries `[ThreadStatic] currentDirection/currentTime` because `Medium.SamplePoint` has no direction [C: `osl_volume.cs:215-216`]. The experimental `procedural-media` branch adds spectral tracking, decomposition, adaptive and signed-null flags to `MediumProperties` and `if (mp.spectralTracking)` blocks in volpath only [C: `git show experimental/procedural-media:src/pbrt/media.cs`, `integrators.cs:4375-4440`]. Each framework or estimator added by special-casing costs an `if` in every integrator.
4. **The incremental MIS is estimator-agnostic only after a change.** The walk stores `pdfFwd/pdfRev` that contain the angular density and the area/volume conversion only (`Vertex.ConvertDensity` multiplies by `1/d^2` and `|cos|`, `integrators.cs:1859`; medium vertices are created with the phase density, `:2435`) [C]. The density of the *distance* (`sigma_t(x) T(x_{i-1}, x_i)`) is not stored. It drops out of every ratio because, in an exponential medium, the same factors appear in every strategy [D: my reading of why the existing weight-equality and unbiasedness tests pass; the one place it is kept is the emission end, `end.pdfFwd = ... * end.sigmaT0`, `:2415`]. For a non-exponential, direction-dependent-extinction or curved-path model this cancellation fails (section 4) and the distance densities must be stored per segment. The recurrence itself (`bdptincremental.cs`) is written in `pdfFwd/pdfRev/segU/segL/cumU` and needs no new term, only different inputs [C+D].
5. **"Signed weights conflict with MLT" is only partly right.** The current estimators draw their random numbers from `RNG(Hash(ray.o, tMax), Hash(ray.d))` (`pathwalk.cs`, `ShadowRay`) [C], so every estimator is already a deterministic function of the path geometry plus the sampler values, which is what MLT needs. Sign is a milder problem (the target needs `|f|`, the sign is carried) [D]. The real MLT conflicts are estimators that are not a function of the primary sample vector, e.g. the randomised multi-start solver of the RRTE connection [C: refractive doc, "randomized reinitializations" is Pediredla's, P].
6. **UPG uses Bernoulli trials, not Booth trials.** `upg.cs` calls the angular bound "the angular bound of the Bernoulli trials" [C]. Its media code is "OUR OWN extension (the paper is surface-only)" [C: `upg.cs:5`] and needs the distance density explicitly (`MediumFactor`: `sigma_s T` by a midpoint quadrature, `upg.cs:200`) [C], which is the same need as in section 4.
7. **SGGX is not a transport model.** It is medium *data* (direction-dependent extinction and a phase function), layer 2 of your three layers, orthogonal to the free-flight law [P+D, section 5.3].

## 2. Layers

```
 integrators (volpath, bdpt, bdptinc, mlt, upg, ...)      see only: segments, collisions, vertices with fwd/rev densities
        |
 PathWalk / shadow rays / connections                      one place that talks to a volume
        |
 VolumeIntegrator  (one per medium, the "black box")       = TransportModel + Tracker/Estimators + capability flags
        |
 Medium (data)     majorants, sigma_a, sigma_s, Le, phase, SGGX orientation, OSL shader, refractive index field
```

* Layer 1, **transport model**: free-flight law, segment geometry, what a "segment pdf" and "transmittance" are. Exponential, non-exponential (Bitterli), spatially correlated (Jarabo), refractive (RRTE), black box.
* Layer 2, **medium data**: unchanged classes, plus direction-aware `SamplePoint` for media that need it (SGGX, OSL).
* Layer 3, **estimator**: how layer 1 is realised numerically: delta, ratio, residual ratio, spectral, decomposition, regular tracking, curved stepping; equiangular and emission strategies are *uses* of a segment.

A volume's `VolumeIntegrator` declares **capabilities** (section 4c) so the integrators decide, per path, which strategies may include it.

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

### 4.2 Non-exponential (Bitterli et al. 2018)

[P: two versions fetched] The free-flight pdf is a medium property defined over optical depth; the edge throughput `E(x', x)` is the transmittance if the end is on a surface and the free-flight pdf if it is in the medium (TR Sec. 3.4), evaluated with an **adjoint flag** because the end of the edge depends on the direction of transport. Heterogeneity: an explicit decomposition into a 1D kernel and a 3D density field (Sec. 3.2); distance sampling by regular tracking / raymarching; delta tracking only for a subclass (pink noise), "unclear how to do this in the general case" (Sec. 5.1/7.1). A single global free-flight pdf: overlapping media with different transmittance functions are not supported (Sec. 7.1/8.1). Naive substitution of a non-exponential `T` into the classical path integral violates energy conservation (supplement Sec. 4).
Bidirectional: the **published TOG text** says the modified kernel is reciprocal and that path tracing, light tracing and bidirectional path tracing give identical images (Fig. 12 caption); the **earlier technical report** (TR2018-841 rev. 1) says the theory "is not reciprocal for non-exponential media" but that bidirectional methods remain usable "as long as quantities are evaluated in a consistent manner" (Sec. 3.8). I could not resolve this discrepancy from the summaries. Either way both versions say it is usable with bidirectional estimators. The supplement states `ff(x,y) = ff(y,x)` but `ff'(x,y) != ff'(y,x)`: **forward and reverse free-flight densities differ** [P].
Consequence for us [D]: per segment, `pdfFwd` and `pdfRev` must include the free-flight density in each direction, plus a flag for whether the segment starts at a scatterer or at a boundary/emitter (the "birth versus collision" distinction; d'Eon makes the same split: free paths between correlated particles versus from uncorrelated locations, arXiv:1803.03259 [P: abstract]).

### 4.3 Spatially correlated (Jarabo et al. 2018)

[P: abstract page] Generalised Boltzmann equation, gamma-distributed concentrations, analytic transmittance for positive correlation, implemented in Mitsuba with random-walk sampling and next-event estimation; "our framework is independent of the particular algorithm" but they give no bidirectional estimator and Bitterli et al. note that Jarabo's lacks a path integral formulation, which "makes integration with bidirectional methods difficult" [P: Bitterli TOG Sec. 1.2]. Conclusion: forward estimators only until a path-integral form is derived; plug it in as `ForwardOnly`.

### 4.4 Refractive RTE (Ament 2014 equation; Pediredla 2020 estimators; Fraboni 2023)

* Ament et al. 2014: the equation with curved rays from Hamiltonian optics (`dx/ds = omega`, `d omega/ds = (grad n - (omega . grad n) omega)/n`), a photon-mapping solver [P: dissertation excerpt; the 2014 TOG paper itself was not read].
* Pediredla et al. 2020: unbiased path tracing with NEE and bidirectional path tracing; the difficulty is the direct connection of two points (a boundary value problem on the eikonal equation), solved by gradient-based optimisation with randomised restarts to stay unbiased over multiple stationary paths [P: project page abstract].
* Fraboni et al. 2023: refractive delta tracking, refraction applied at null events; NEE by "refractive ratio tracking" with a Levenberg-Marquardt adjustment (specular manifold sampling style); no bidirectional methods; "complete refractive null-scattering formulation is left for future work" [P: the PDF]. (The repo's refractive doc says Fraboni "has no estimator for connections"; per the PDF it has an NEE one but no bidirectional one [P vs C].)
* In the repo [C: `docs/refractive-media.md`]: the throughput has no n^2 factors at interior vertices; the geometry term of an edge is `cos cos / J` with J the area Jacobian of the curve, asymmetric `J(a->b) = (n_a/n_b)^2 J(b->a)`; curved edges store tangents at both ends and both Jacobians; connections through the solver with a multiple-path count; strategies that cannot be curved are masked consistently out of the MIS sums (`CurvedConnectionAllowed`); equiangular off on refractive segments; area lights only (no infinite, delta, volume light from a vertex inside); polarisation by parallel transport of the frame; verified bdpt against volpath (z within 1-2.6 in the documented checks, with two retractions of earlier agreement claims).
* Geometry changes everything the walk does: the scene is no longer intersected along a straight ray, the exit point is found by a short ray at the end of the curve, the stack is popped by hand (`pathwalk.cs` of that branch) [C].

### 4.5 SGGX microflakes (Heitz et al. 2015)

[P: PDF] `S` a symmetric positive-definite 3x3 matrix; projected area `sigma(w) = sqrt(w^T S w)` (even: `sigma(w) = sigma(-w)`); `sigma_t(w) = rho sigma(w)`, `sigma_s(w) = alpha rho sigma(w)`; phase `fp(wi->wo) = (1/sigma(wi)) int p(wm, wi->wo) <wi, wm> D(wm) dwm`; the "unusual reciprocity" `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)` (no shadowing, only masking); importance sampling by the distribution of visible normals, weight always 1; the integrator "is independent and out of the scope"; their results use a forward path tracer. The paper does not discuss delta tracking, next-event estimation or bidirectional methods [P].
My derivation [D]: along a ray with fixed direction `w` the extinction is a scalar, so the exponential machinery applies with `sigma_t(x, w)`; a direction-independent majorant (`>= rho sqrt(lambda_max(S))`) keeps the null coefficient `sigma_n = sigma_maj - sigma_t(x, w)` direction-dependent but even in `w`, so the reversed path sees the same `sigma_n`. The vertex weight `sigma_s(wi) fp(wi->wo)` is symmetric by the reciprocity relation, so the throughput is symmetric. The *sampling* densities are not: forward uses `sigma_t(wi)`, reverse `sigma_t(wo)`, and `fp(wi->wo) != fp(wo->wi)`; so `pdfRev` must be evaluated with the reversed directions (as `Vertex.PDF` already does for the phase function) and the real/null event probabilities use the direction of the ray. This needs `SamplePoint` to take the direction. It is **not** a new transport model. Polarisation: the model is scalar [P]; in a polarised path it can only be treated as depolarising [D].

### 4.6 Estimators (layer 3)

| Estimator | Source | What it needs / gives |
|---|---|---|
| delta tracking | classic; null-scattering view [P: Miller 5.2] | exponential law, majorant; exact pdf in null-scattering space |
| ratio tracking | Novak 2014; = null-subpath sampling [P: Miller] | transmittance estimate; unbiased with negative `sigma_n` allowed [P: Novak abstract: handles negative densities] |
| residual ratio | Novak et al. 2014 [P: abstract page only] | control `sigma_c` in closed form, ratio tracking on the residual; the repo's `ResidualRatioEstimator` |
| spectral tracking | Kutz et al. 2017 [P] | one tracker, one majorant for all wavelengths, event probabilities from throughput; "the PDF of the free-path sample cannot be evaluated exactly", MIS only works if the same tracker is used for free-path and transmittance sampling, "does not generalize" (Sec. 7) |
| decomposition tracking | Kutz et al. 2017 [P] | homogeneous control + residual; weights can be negative |
| regular tracking / raymarching | Bitterli 2018 [P] | the only unbiased general distance sampler for non-exponential heterogeneous media |
| curved stepping | Pediredla 2020 [P], repo `SampleT_majCurved` [C] | collisions in arc length on a step grid; majorant must be a function of position (so forward and reverse agree) |
| equiangular, emission gather/NEE | Kulla-Fajardo, Fong [C: `equiangular.cs`] | uses of a segment, not estimators of the medium |

## 5. Decisions

### 5a. Plug-in granularity

| Option | What | For | Against |
|---|---|---|---|
| A1 monolith | one `IVolume` per framework doing everything | maximum freedom; RRTE fits | every framework re-implements majorant tracking, estimators, MIS bookkeeping; no reuse of grid/OSL/emission code |
| **A2 three layers (recommended)** | `TransportModel` (law + geometry) over `Medium` data, with `Tracker/TransmittanceEstimator` as today, bundled by the existing `VolumeIntegrator` | reuses all media; SGGX needs only a data-layer change; exponential model is the existing code moved behind an interface | the interface must be right for curved paths; combinations need validation (a compatibility table at scene build) |
| A3 status quo plus `as`-branches | what the refractive stage did | no refactor | +800 lines in `integrators.cs` per framework, `ifs` in every integrator, no way to add a framework without editing all of them (section 1.3) |
| A4 compile-time generics | `PathWalk<TModel>` | no virtual calls | the model is chosen per medium at run time and several can be in one scene; code size multiplies with integrators x models |

Dispatch: **one virtual call per segment** (and per collision through a pull iterator, below), not per strategy-loop step. Today there is a type-test chain per `SampleT_maj` call and a delegate invocation per collision with a closure allocated per walk iteration (`pathwalk.cs`, `new Func` capturing `ctx, hook, scattered, terminated`) [C]; a pull API (`NextEvent(ref state, ...)`) removes both and gives plug-ins their own state.
Per-medium history state: **transient in `WalkContext`** (a `SegmentState` struct of at most 32 bytes returned by `BeginSegment`), plus a **persistent `SegmentRecord` in the vertex** (about 12-16 bytes: free-flight density forward and reverse for the hero wavelength, origin kind, and an index into the vertex's existing `EdgeGeometry` for curved edges). Not in `PathState` globally (it is per walk, not per vertex, and MIS needs per-vertex values). Rejected: an opaque per-vertex blob owned by the model (variable size, defeats the pooled-vertex plan).

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

Each estimator declares a **class**: `ExactPdf` (delta, ratio with the same majorant, regular tracking by deterministic inversion, curved stepping with a position-only majorant), `Deterministic` (closed-form), `NoPdf` (residual ratio as a free-path sampler, spectral, decomposition, adaptive).

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
| spatially correlated (Jarabo) | E (their MC) [P] | U until a path-integral form exists [P] | U | U | U | U | U | ? |
| RRTE | E (branch, tests) | E (branch): area lights only, no equiangular | not implemented; feasible [D] if the curved-edge densities enter `pdfFwd/pdfRev` (they do in the branch) | U: the connection solver's randomised restarts are not a function of the primary sample vector unless seeded by geometry [D] | U | U | E (branch: parallel transport) | U for dispersive `n(lambda)` (curves diverge per wavelength) [D]; E for non-dispersive |
| black box (B1) | E | E, high variance (no connections inside) | E | E if the box is a deterministic function of its draws | E as a delta boundary | U | depends on the box | depends on the box |

Estimator x framework:

| | exponential | SGGX | non-exp homogeneous | non-exp heterogeneous | RRTE |
|---|---|---|---|---|---|
| delta tracking | E | E [D] | U (only a pink-noise subclass [P]) | U | A: Fraboni's refractive delta tracking, prototype [P]; Pediredla's constant-majorant stepping E (branch) |
| ratio tracking | E | E [D] | U; use closed form / quadrature (`Deterministic`) | U; regular tracking (`ExactPdf` by quadrature) | E (branch, curved ratio tracking) |
| residual ratio | E | E with a direction-independent control (min over `w`) [D] | U | U | U (control along curves not built) |
| spectral / decomposition | E in volpath; in bdpt only with the same tracker both ways [P]; signed weights | same | U | U | U |
| hero-wavelength r_u/r_l | E (today) | E | replaced by deterministic density ratios | same | E (branch) |
| equiangular | E | E [D] | A: needs the segment density in the weight | A | U (branch turns it off) |
| emission gather / NEE | E | E | A (volumetric emitter integral: supplement Sec. 5 [P], not studied) | A | E (branch; area and volume lights) |

### 5e. Performance

* The strategy loops stay straight-line: weights come from stored sums (`bdptincremental.cs`), the model is not called in them.
* Calls into the model: `BeginSegment` once per segment, `NextEvent` once per collision (today: one delegate call per collision + one closure allocation per walk iteration + a type-test chain per segment) [C]; a virtual call costs a few ns (typical figure, not measured here), against 50-400 us for a whole sample at depth 5-20 in the in-process benchmark [C: `docs/bdpt-incremental.md`]. I expect the plug-in layer to be neutral or slightly faster after the delegate removal, but **I have not measured it**; phase 1 includes that measurement.
* Memory: `SegmentRecord` of 12-16 bytes next to a 1344-byte `Vertex` (about +1%) and 84 bytes of `VertexMIS`. A model with no history leaves it zero.
* Models that are costly by nature: regular tracking (non-exponential heterogeneous), curved stepping and the connection solver (RRTE: several solver iterations per connection [C: doc]).

## 6. Interface sketch (sketch only: not compiled, names may change)

```csharp
public enum SegmentOrigin : byte
{
    Boundary = 0,   // a surface, a camera or a light: the free flight does not start at a scatterer
    Particle = 1    // a scatterer of the same medium
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
    Connectable = 1,        // Connect() succeeds for points inside the volume
    ExactSegmentDensity = 2,// Density() is exact (not an estimate), so strategies may be MIS-weighted
    Adjoint = 4,            // light subpaths may enter (reverse transport defined)
    EmissionLight = 8,      // exports a light with an exact density
    Equiangular = 16,
    Combinable = 32         // may be summed with other volumes of the same Family
}

// Transient per segment, at most 32 bytes, owned by the walk
public struct SegmentState
{
    public Point3f origin;
    public Vector3f direction;
    public Float length;
    public Float travelled;
    public int modelData0;
    public int modelData1;
}

// Persisted in the vertex that ends the segment
public struct SegmentRecord
{
    public Float freeFlightPdfForward;
    public Float freeFlightPdfReverse;
    public SegmentOrigin origin;
}

public struct SegmentEvent
{
    public SegmentEventKind kind;
    public Point3f position;
    public Vector3f tangent;                 // equals the ray direction on a straight segment
    public MediumProperties properties;      // sampled with the tangent for direction-dependent media
    public SampledSpectrum majorant;
    public SampledSpectrum majorantTransmittance;
}

public abstract class TransportModel
{
    public abstract string Family();
    public abstract VolumeCapabilities Capabilities();

    public abstract void BeginSegment(Medium medium, Ray ray, Float tMax, SegmentOrigin origin,
                                      SampledWavelengths lambda, out SegmentState state);

    // Pull: the next collision candidate of the segment, or the escape through its end
    public abstract SegmentEvent NextEvent(ref SegmentState state, ref RNG rng, Float u);

    // The densities of the segment between two points, for the hero wavelength and the ratios to the other wavelengths
    public abstract SegmentRecord Density(Medium medium, Interaction from, Interaction to, SegmentOrigin origin,
                                          SampledWavelengths lambda, out SampledSpectrum ratioUnidirectional,
                                          out SampledSpectrum ratioLight);

    public abstract SampledSpectrum Transmittance(Medium medium, Interaction from, Interaction to,
                                                  SampledWavelengths lambda, ref RNG rng,
                                                  out SampledSpectrum ratioUnidirectional,
                                                  out SampledSpectrum ratioLight);

    // Optional: false makes the endpoints delta vertices for the strategy loops
    public virtual bool TryConnect(Medium medium, Interaction from, Interaction to, out Float throughputFactor)
    {
        throughputFactor = 0;
        return false;
    }
}
```

`ratioUnidirectional` and `ratioLight` are today's `r_u` and `r_l` (and `segU`/`segL` in bdpt): the interface only names them. `VolumeIntegrator` gains the `TransportModel` and keeps `tracker` and `transmittance`; the exponential model implements `NextEvent` through the existing `SampleT_maj` code so that the random numbers are drawn in the same order (section 7, phase 1).

## 7. Migration plan (existing output bit-identical until a phase says otherwise)

Common rule: before each phase, record image hashes (fixed seed, in-process `RenderImage` of `bdptincremental_stat_test.cs`) of volpath, simplevolpath, bdpt, bdptinc, mlt on the media scenes of `BdptInc` (fog, foghg, fogchrom, foggrid, fograyleigh, fognested, fogemit, equiangular, polarised Rayleigh fog); the phases below that say "bit-identical" must reproduce them exactly.

| Phase | Change | Output | Test |
|---|---|---|---|
| 0 | hash harness for the integrators above; add the weights-sum-to-one test for a path (as the refractive branch did) | none | the hashes themselves |
| 1 | `TransportModel` + `ExponentialModel` (moves `SampleT_maj`, `MediumEvents`, `PathState` null-collision code behind the interface; pull iterator with a delegate adapter); remove the `as RefractiveMedium`-style need | bit-identical | hashes; microbenchmark of the segment call (ns per segment, old versus new) |
| 2 | direction-aware `SamplePoint(p, lambda, w, time)` default-forwarding; fold `OSLMedium`'s thread-statics into it; `SGGXMedium` | bit-identical for old media; new medium | SGGX against its closed forms: `sigma(w)` and `sigma_t`, homogeneous transmittance `exp(-rho sigma(w) L)`, phase normalisation (energy), the reciprocity relation `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)`, chi-square of visible-normal sampling; volpath/bdpt/bdptinc agreement (t-tests as `UnbiasedAgainstBdpt*`) |
| 3 | `SegmentRecord` in `Vertex`; `Density()` for the exponential model returns the current factors; bdptinc/bdpt read it; the `ResidualRatioEstimator` MIS question of 5c.3 gets its test | bit-identical (the record is zero for exponential) | hashes; weight equality; test of residual/analytic/tracklength connections against ratio on a grid medium |
| 4 | `NonExponentialModel` (homogeneous kernel first: closed form `T(d)`, inverse-CDF free flight; heterogeneous by regular tracking after) | new | analytic: free-flight pdf integrates to `1 - T`, mean free path; slab transmittance by PT, LT and BDPT agree (Bitterli's Fig. 12 claim reproduced as our test); energy conservation in a closed medium; fixed-seed bdptinc/bdpt weight sums to one with the stored densities |
| 5 | black box B1 (wrap a medium; delta boundary; `StrategyAllowed` per path) | new | the wrapped exponential medium gives the same expectation as the unwrapped (t-tests), lower strategy count, nested-in-medium and medium-in-box scenes |
| 6 | merge `experimental/refractive-media` behind `TransportModel`; port its tests; bdptinc support (curved edge densities into `pdfFwd/pdfRev`) | new | linear gradient `n = n0 + g z` with the closed-form path `n(x) = C cosh(g x / C + a)` (existing test), Luneburg focusing, bdpt against volpath (z within 2), weight-sums-to-one on curved paths (the branch's 1.2e-7 check), polarised torsion test |
| 7 | spectral and decomposition trackers as `NoPdf` estimators on exponential volumes | new | unbiasedness against ratio tracking on a grid; signed weights; the `StrategyAllowed` mask in bdptinc |
| 8 | Jarabo correlated model as `ForwardOnly` (optional) | new | their analytic transmittance |

## 8. Risks and open questions

* The two versions of Bitterli et al. disagree on reciprocity [P]; before phase 4, read the published text and the supplement and fix the adjoint convention with a numerical test (PT = LT = BDPT on a slab).
* Heterogeneous non-exponential media have no known unbiased fast distance sampler [P]; regular tracking cost is high. Closed-form segments between voxel boundaries (DDA) may help [D].
* Direction-dependent extinction in the null-scattering framework (SGGX) is my derivation [D], not in either paper; the test in phase 2 is the proof.
* Chromatic: for non-exponential and curved models the hero-wavelength ratios come from deterministic densities, which is better than null-collision ratios, but dispersive refraction splits paths by wavelength [D]; this is a real limit of RRTE.
* Estimators of class `NoPdf` force strategy masks and therefore high variance in bdpt; the benefit is only in walks.
* Signed weights and `Russian roulette` in `ShadowRay` interact (the roulette tests `T_ray` magnitude) [C]; needs a sign-aware rule.
* MLT: any model whose sampling is not a deterministic function of the primary sample vector is excluded; the repo's geometry-hashed RNG gives determinism for stochastic estimators but correlates the mutations [D].
* Pseudo-marginal (stochastic) segment densities: ratios of unbiased estimates are not unbiased estimates of the ratio; the rule in 5c avoids them rather than solving them. Whether an UPG-style trial estimator could carry them is an open research question.
* Thread safety: models keep state per segment, none per medium; `OSLMedium`'s thread-statics must go.
* Emission inside non-exponential media: the volumetric emitter path integral exists (supplement Sec. 5) but I did not read it [P: pointer only].
* Overlap of different laws is unsupported by the theory [P]; the priority rule is a restriction, not a solution.

## 9. Recommendations in one list

1. Three layers with the existing `VolumeIntegrator` as the facade (A2); one virtual call per segment, a pull iterator, transient `SegmentState` in the walk, a 12-16 byte `SegmentRecord` in the vertex.
2. Treat SGGX as medium data with a direction-aware `SamplePoint` (phase 2), not as a transport model.
3. Black box = delta boundary first (B1); capability flags promote volumes to connectable, exact-density, adjoint, emission-light.
4. Estimator classes `ExactPdf` / `Deterministic` / `NoPdf`; MIS only among strategies that can all state their density; `NoPdf` volumes mask strategies per path.
5. Merge the existing refractive work behind the interface instead of rewriting it; keep its tests as the contract.
6. Phase 0-3 are refactors that must be bit-identical; every later phase brings an analytic test and a t-test against a brute-force reference.

## 10. Phase-1 interface questions for the user

* Should a pull iterator replace the callback API outright (after the adapter has proved bit-identity), or stay as the exponential model's internal detail?
* Is a forward-only model (Jarabo) worth shipping with a documented restriction, or should every framework be bidirectional or absent?
* Do we accept the priority rule for overlapping volumes of different laws?

## 11. What I could not verify

* I read none of the papers as raw PDF text: all [P] statements come from a fetch tool that summarises. The Bitterli TOG and TR versions disagree on reciprocity (section 4.2); the SGGX phase function formulas were extracted from garbled text (Eq. 12 exponents unchecked).
* Pediredla 2020: abstract and project page only (the CMU page was blocked). Ament 2014: a dissertation excerpt (first 100k characters), not the TOG paper. Novak 2014: abstract page only (PDF fetch failed). Jarabo 2018: abstract page; Fraboni 2023: PDF summary; d'Eon 2018: abstract.
* Not read: Vicini et al. 2021 (non-exponential transmittance for scene representations, [R]), d'Eon's later parts, Pediredla's code, LuxCore's media code. `procedural-media` was inspected only at the places cited; it has 36 commits ahead of the merge base that I did not review.
* The behaviour of `sppm`/`path`/`lightpath`/`simplepath` in scenes with media was inferred from the code (no medium scattering), not by rendering.
* The claim that distance densities cancel in the exponential case is my reading of the code, corroborated by the weight-equality and unbiasedness tests of bdptinc, but not proven.
* No performance numbers for the proposed interface exist; section 5e is an estimate.
