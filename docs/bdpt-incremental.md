# bdptinc: bidirectional path tracing with incremental (dVCM / dVC) MIS

`Integrator "bdptinc"` is registered next to `"bdpt"` and takes the same parameters. It is one engine: media (homogeneous, grid, texture, nested, chromatic, emissive), polarisation (Stokes/Mueller, sensor, analyser), every pbrt light, shape and material, equiangular sampling, emission ends, `strategies "unidirectional"`, visualizestrategies/weights. Unpolarised rendering is the same engine with `sensor == null` (as in `bdpt`). There is no merging, no mode switch and no second class. Code: `src/pbrt/cpu/bdptincremental.cs`. The old `bdpt` code is untouched; its output is unchanged.

## What is different: the MIS weight

`bdpt` (Veach) computes the balance-heuristic weight of every connection (s, t) by walking both subpaths backwards: O(s+t) per connection, with temporary patches (`ScopedAssignment`) of the end vertices.

`bdptinc` follows the non-Veach scheme of SmallVCM / LuxCoreRender BIDIRCPU / PolarisedUPBP. Each subpath vertex stores two partial sums, computed once when the vertex is generated, so a connection weight is `1 / (wLight + 1 + wCamera)` from the sums of the vertex before the end vertex and four patched reverse densities.
(LuxCore's `bidircputhread.cpp` was read by grep only: it keeps `dVCM`, `dVC`, `dVM` per vertex, updates them in the walk, and forms `1/(lightWeight + 1 + eyeWeight)`. We do not use its `dVM` merging terms.)

Recurrence (in pbrt area/volume-measure densities, `Vertex.pdfFwd/pdfRev`, not in SmallVCM's solid-angle form):

    T_k   = rev_k (dVCM_k + dVC_k)
    dVCM_k = a_k cumU_{k-1} segL_k / Remap0(fwd_k)         (divided by splatScale for the camera vertex k=1)
    dVC_k  = segU_k Remap0(rev_{k-1}) (dVCM_{k-1} + dVC_{k-1}) / Remap0(fwd_k)
    Remap0(f) = f != 0 ? f : 1

`a_k` is the delta/strategy-allowed flag. The sums are `SampledSpectrum` so the hero-wavelength spectral MIS of chromatic media (segU, segL, per-vertex products, cumU, connRu/connRl) stays exact: every term is a product of per-vertex factors. Equiangular terms use stored prefix products (`vtxProduct`, `vtxProductFrom2`, `segUProductFrom3`, `rhoProductBefore`). Emission ends are swapped into the camera array during evaluation. The infinite-light density correction is applied before the light walk, so the stored sums use final densities.

Storage: `VertexMIS` = 84 bytes per vertex next to the 1344 bytes of `Vertex` (+6%).

## Specialisation choice

Specialise like LuxCore = bdpt only: no merging/photon modes, no light-tracing-only or path-tracing-only modes, no per-sample integrator-type branches. The `unidirectional` strategy mask is a per-connection bool. Polarised/unpolarised is the `sensor` flag of the shared `PathState`. The walk, hook and sampler dimension order are shared with `bdpt` (a copied hook), so same-seed images are identical.

## What is exact, what is not

Exact (verified): the weight of every strategy equals `BDPT.MISWeight` to float precision (worst relative difference 3.2e-7 over all scenes below; tolerance 2e-4) for surfaces, specular chains, point/spot/distant/uniform-infinite lights, homogeneous / grid / chromatic / Rayleigh / nested media, an emissive medium (38 emission ends), equiangular (1 and 3 samples), unidirectional, polarised room and polarised Rayleigh fog.
Same-seed images old vs new: identical in all but one pixel (6e-8 absolute, chromatic equiangular).
Differences by design (no change of the estimator): the PDF of the camera vertex before the end vertex when t==2 is not evaluated (it is not a term); the infinite-light density patch is made before the walk.
Nothing found that the recurrence cannot express. Not tested: realistic camera / diffraction (inherited unchanged), the MLT evaluator (keeps using `BDPT.MISWeight`), OSL media.

## Parity checklist

See the table below (feature, old code, bdptinc handling, test).

| # | Feature | Old bdpt | bdptinc | Test |
|---|---------|----------|---------|------|
| 1 | Camera subpath walk, pdfFwd/pdfRev in area/volume measure | BDPT.GenerateCameraSubpath/RandomWalk + BDPTWalkHook | Copied hook (IncrementalWalkHook) on the same shared PathWalk; same sampler dimensions in the same order; adds the partial sums | W, I |
| 2 | Light subpath walk, light sampling (PowerLightSampler), SampleLe | GenerateLightSubpath | same, with the infinite-light densities applied before the walk (old: patched after) so the stored sums see the final densities | W, I |
| 3 | Connection strategies s,t incl. s=0 (hit emitter), s=1 (light sample), t=1 (camera sample, light tracing splat), general | ConnectBDPT | ConnectBDPT ported verbatim; only the MIS call differs | I |
| 4 | MIS: balance heuristic over strategies | MISWeight/MISSum (O(s+t) per connection, ScopedAssignment patches) | MISSum from stored dVCM/dVC of the vertex before the end vertex + 4 patched reverse pdfs: O(1) | W |
| 5 | Delta (specular) vertices excluded from strategies | `delta` flags, patched false at connection vertices | a_k flag inside dVCM_k; connection vertices use patched flag | W (specular scene) |
| 6 | Delta lights (point, spot, distant) | IsDeltaLight in light loop | BeginLightRoot / s=1 term | W |
| 7 | Infinite lights (uniform, image, portal), env map escape vertex | InfiniteLightDensity, path[0]/[1] pdf patch after walk, Vertex.Le | density applied before walk; same Le | W, I |
| 8 | Area lights incl. two-sided, alpha, textured, animated (time) | DiffuseAreaLight via si.areaLight; time from camera vertex 0 | unchanged (same Vertex methods); light subpath time from camera vertex 0 | I (animated scene) |
| 9 | Goniometric/projection/spot/point light types | Light.SampleLe / PDF_Le | unchanged (same calls) | I |
| 10 | Camera: perspective, realistic (lens), PDF_We, SampleWi, NoCameraRay path, diffraction splat | Integrator + RayIntegrator.AddCameraSplat | unchanged base class; NoCameraRay ported | I |
| 11 | splatScale (pbrt issue 347) on the i=1 camera term and the t=1 light terms | MISSum `ri /= splatScale` | folded in dVCM_1 and the t=1 light sum | W (t=1 strategies) |
| 12 | Media: homogeneous, grid, nanovdb, osl, nested (BoundaryStack / priority) | PathWalk (shared), Vertex.stack, TrSegments | same shared PathWalk; the stack is stored in the vertex as before | W, I |
| 13 | Chromatic media: hero-wavelength spectral MIS (segU, segL, vtx, cumU, connRu/connRl) | MISSum spectral products | dVCM, dVC and the products are SampledSpectrum; the recurrence is exact because every term is a product of per-vertex factors (see docs) | W (chromatic medium) |
| 14 | Emissive media as lights (VolumeEmissionLight, SampleLeVolume/SampleLiVolume, sigmaT0, emissionMaj) | GenerateLightSubpath, ConnectBDPT, MISSum i=0 | BeginLightRoot term (sigmaT0*emissionMaj/pdfFwd), s=1 term | W, I |
| 15 | gathermediumemission: camera subpath collisions in emissive media (emission ends) | BeginCollision, ConnectEmissionEnd | same; the end vertex is swapped into the camera array during the evaluation (no copy) | W, I |
| 16 | equiangular sampling (+ equiangularsamples, equiangulardepth) | SampleEquiangularVertex, EquiangularTerm | ported; the products over vertices read from stored prefix products (vtxProduct, vtxProductFrom2, segUProductFrom3, rhoProductBefore) | W (equiangular scene) |
| 17 | strategies "unidirectional" | StrategyAllowed in MISSum loops | mask applied where a term is formed at the connection; stored camera sums are zero in that mode (the only allowed terms are at the end vertices) | W |
| 18 | russianroulette (subpath and shadow-ray) | BDPTWalkHook.BeginIteration, TrSegments | copied | I |
| 19 | regularize | OnBSDF | copied | I |
| 20 | maxdepth | array sizes, depth checks | same | W |
| 21 | visualizestrategies / visualizeweights (per (s,t) films, maxdepth clamp 5) | BDPTIntegrator.Render/Li | ported; files are named bdptinc_dDD_sSS_tTT.exr | I |
| 22 | Polarised: Stokes light subpath (PLight), Mueller camera subpath (PAttenuation), ConnectionMueller, sensor/analyser/analyserangle/measure, camera optics (PolarisedSensor) | PathState + ConnectBDPT tail | same PathState/hook; ConnectBDPT tail copied; weights are scalar so unchanged | I (polarised scene, Stokes channels) |
| 23 | Rayleigh phase function Mueller in media | ConnectionMueller | copied | I |
| 24 | Pixel bounds, seed, spp, film splat for t=1 | RayIntegrator | inherited | I |
| 25 | Factory + parameters | `Integrator.Create` "bdpt" | "bdptinc" registered next to it with the same parameters | parse test |
| 26 | MLT evaluator using BDPT | mltevaluator.cs | not applicable (mlt keeps using BDPT.MISWeight) | - |
| 27 | UPG hook (BDPT.MISSum additive arg in feature/upg-integrator) | other branch | not touched; bdptinc has no UPG term | - |

## Tests (group BdptInc, `src/pbrt/bdptincremental_test.cs`, `bdptincremental_stat_test.cs`)

Weight equality (WeightsEqualBdpt Surfaces / SpecularAndLightTypes / Media / EmissiveMedium / Equiangular / Unidirectional / Polarised); same-seed images (Surfaces, Media, Polarised, AnimatedLight); unbiasedness t-tests with 96 bdpt vs 32 bdptinc images, |t|<4 per channel (largest seen 1.66), RMSE slope in (-0.6,-0.4) for intensity; PolarisedAnalyserChangesTheImage (non-vacuity of polarisation); WhiteFurnace (identical numbers for both integrators). Test-harness bugs found and fixed (no bug found in the integrator): `Fixture.Build` skipped `TextureMedium.ResolveTextures` and kept static emission lights between scenes; the first emissive scene did not light (density texture lives in texture space); distant/infinite scenes were black behind a ceiling; s3 image can be exactly black.

## Benchmarks

In-process, one thread, thread CPU time (clock_gettime), alternating old/new rounds, 9 rounds x 3000 samples (warm-up first), quiet machine (load 1-2 on 2 cores; the earlier loaded run gave the same picture with up to +-20% noise). Rooms 16x16, same scenes for both integrators; "grid" = uniform-grid medium, "fog" = homogeneous chromatic-free fog, "polarised" = dielectric+conductor room with the polarised sensor. The last two columns are from a separate split run (ProfileSplit: generate / connection evaluation / MIS weight, old `BDPT.MISWeight` against the new weight on identical inputs).

| scene | depth | bdpt us/sample (min-max) | bdptinc us/sample (min-max) | new/old (medians) | new/old (minima) | MIS weight share of old sample | ns per weight old / new |
|---|---|---|---|---|---|---|---|
| cornell | 5 | 54.23 (52.12-59.02) | 51.93 (51.17-57.41) | 0.958 | 0.982 | 10.4% | 2501 / 1174 |
| cornell | 10 | 132.45 (126.97-142.78) | 122.24 (116.65-129.48) | 0.923 | 0.919 | 11.9% | 1206 / 557 |
| cornell | 20 | 409.11 (394.45-428.15) | 338.81 (331.27-366.27) | 0.828 | 0.840 | 23.3% | 1669 / 548 |
| glossy | 5 | 58.20 (56.72-60.54) | 57.01 (55.73-65.36) | 0.980 | 0.983 | 7.1% | 911 / 503 |
| glossy | 10 | 145.26 (139.97-155.70) | 133.83 (129.33-136.39) | 0.921 | 0.924 | 14.0% | 1155 / 534 |
| glossy | 20 | 465.73 (452.43-480.49) | 373.55 (357.47-399.70) | 0.802 | 0.790 | 28.3% | 1568 / 595 |
| fog | 5 | 50.81 (49.59-52.64) | 50.29 (49.73-53.58) | 0.990 | 1.003 | 1.6% | 960 / 807 |
| fog | 10 | 107.81 (105.63-118.54) | 110.35 (106.69-113.39) | 1.024 | 1.010 | 3.5% | 1252 / 578 |
| fog | 20 | 237.51 (229.10-253.48) | 231.39 (224.64-238.64) | 0.974 | 0.981 | 7.0% | 1530 / 624 |
| grid | 5 | 70.21 (67.70-78.38) | 69.01 (67.38-71.66) | 0.983 | 0.995 | 3.5% | 1032 / 616 |
| grid | 10 | 158.40 (155.09-167.13) | 151.65 (149.27-155.19) | 0.957 | 0.962 | 7.3% | 1139 / 580 |
| grid | 20 | 434.23 (417.17-462.25) | 398.34 (373.28-425.71) | 0.917 | 0.895 | 13.9% | 1499 / 580 |
| polarised | 5 | 61.27 (58.87-67.53) | 60.15 (57.93-72.15) | 0.982 | 0.984 | 5.6% | 910 / 545 |
| polarised | 10 | 144.53 (137.66-155.45) | 135.45 (129.03-139.42) | 0.937 | 0.937 | 10.0% | 1056 / 567 |
| polarised | 20 | 397.72 (384.18-417.55) | 347.30 (334.69-362.10) | 0.873 | 0.871 | 19.5% | 1444 / 523 |

Reading: the weight call is 2.1-3.1x cheaper (cornell d20: 1669 -> 548 ns). Because it is 1.6-28% of an old sample, the whole-sample gain is 0-20%: about 17-20% at depth 20 in diffuse/glossy rooms, 8-13% in grid/polarised at depth 20, 4-8% at depth 10, 1-4% at depth 5 (within the noise of the spread), and nothing measurable in fog (fog d10: 1.024, inside the spread; the weight is only 3.5% of the sample there). No scene was slower beyond noise. The gain grows with depth (the old weight is O(s+t) per connection and the number of connections grows with depth squared); crossover: there is no depth where bdptinc loses beyond noise, the break-even (gain larger than noise) is around depth 8-10.

Equal-time RMSE: the same-seed images are identical (0 differing pixels in all surface/media/polarised scenes, 1 pixel at 6e-8 in one), so the error per sample is the same by construction (and the unbiasedness test shows the same RMSE-vs-spp columns, slope -0.47..-0.55). RMSE at equal time is therefore sqrt(t_new/t_old) times the old one: 0.91 (cornell d20), 0.96 (cornell d10), about 0.99 (d5). Measured, not assumed: same-seed identity; not measured: separate equal-time renders against a high-spp reference.

Memory: `new Vertex()` = 1344 bytes (Vertex + EndpointInteraction), `VertexMIS` = 84 bytes (+6% per stored vertex). Allocation per pixel sample: 85-460 KB depending on depth (old 87 KB vs new 85 KB at cornell d5; new is slightly lower because the ScopedAssignment/clone work is gone). Allocation potential: enlarging the gen0 budget (DOTNET_GCgen0size=512MB) changes sample time by less than 2%, so GC itself is cheap; a pooled / struct vertex array might still save the initialisation and cache cost of 1344-byte objects, but it was not implemented or measured (it would help old and new alike and needs the old Vertex type changed, which would risk the bit-identical output).


## Limits

Gains are bounded by the share of the MIS weight in the sample cost (1-28% in old bdpt), the rest (tracing, BSDF/phase evaluation, vertex allocation) is unchanged. The weight call itself is 2.1-3.1x cheaper. At depth 5 the difference is within noise.
