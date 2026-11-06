# Volume frameworks: exponential media and SGGX with selectable trackers (design note, no engine code)

Status: design note, **revision 4**. Nothing in this note changes engine code. Branch `feature/bdpt-incremental-mis`, written against commit `17655e3`+. Revisions 1 to 3 are in git history (revision 3 = commit 17655e3 with the Tungsten reading; revision 2 = 6c492d6).

Scope (revision 4, a scope change from the user): **in** are the exponential null-scattering framework, the Heitz SGGX microflake framework as Mitsuba 3 supports it, **many trackers and estimators selectable per volume and per query type**, the black box (a volume with its own integrator hidden behind its boundary), and the migration and test plan. **Out** (deferred, no hooks): non-exponential media (Bitterli, d'Eon), spatially correlated media (Jarabo), the refractive RTE (RRTE) and transient rendering. They are one paragraph in Appendix A with the cost of re-adding them; the Tungsten and d'Eon findings of revision 3 are kept in Appendix B because they are useful later.

## 0. How to read this note

Every factual claim carries a tag:

* **[C]** I read it in the code of this repository (file and line given; lines are of the worktree at this commit).
* **[M]** I read it in the Mitsuba source (github.com/mitsuba-renderer/mitsuba3 `master`, commit `aa01500`, 2026-10-13, and mitsuba2 `master`, commit `62863cb`, 2022-08-22; shallow clones read as text, never built or run). Paths relative to the repository root.
* **[T]** I read it in the Tungsten source (Appendix B only).
* **[P]** I obtained it from the paper or its page with a fetch tool. **The tool returns a summary of the page, not raw text**, so statements tagged [P] are what the summary says; I did not read any paper as raw text. Equation numbers are the summary's.
* **[D]** Derived by me from the code and the papers. Not in any paper, not tested. Treat as a hypothesis with a test attached (section 9).
* **[R]** Recalled from memory, not verified in this session.

What I could and could not read is in section 12.

## 1. What the scope change does to revision 3

| Revision 3 item | Revision 4 |
|---|---|
| `SegmentRecord` with forward and reverse free-flight densities | **Drops out.** In an exponential medium the distance densities telescope in the MIS ratios (section 3.2). It comes back only as an optional float per segment for estimators without an exact density (the "surrogate", section 6.3) |
| `EndpointKind` start and end on every segment | **Drops out** (needed only for laws whose density depends on how the flight started) |
| the `Adjoint` flag | stays dropped; a `BothDirections` flag survives for black boxes only |
| `OpticalDepth`/`InverseOpticalDepth` | drops out |
| the MIS-consistency rule "mask strategies through a `NoPdf` volume" | **revised, and I withdraw part of it** (section 6.3): masking is one of three options, not a requirement. Estimators without an evaluable density can take part in MIS with a *surrogate* density as long as the sampling weights stay exact, which is what UPG already does for its collision factor [C: `upg.cs:194-224`] |
| direction-aware `SamplePoint` | **stays and becomes central** (SGGX) |
| (new) | **one thing is left in the vertex density for SGGX**: the extinction factor at a scatter vertex (section 3.2) |
| (new) | per-volume parameters for six query types, validation at scene load, a surrogate policy |

## 2. Layers

```
 integrators (volpath, bdpt, bdptinc, mlt, upg, ...)      see only: segments, collisions, vertices with fwd/rev densities
        |
 PathWalk / shadow rays / connections                      one place that talks to a volume
        |
 VolumeIntegrator  (one per medium, the "black box")       = trackers + estimators per query type + surrogate policy + flags
        |
 Medium (data)     majorants, sigma_a, sigma_s, Le, phase (with ProjectedArea), grids, OSL shader
```

* Layer 1, **medium data**: classes as today, plus a direction-aware query (`SamplePoint(p, lambda, w, time)`) and a phase function that knows its projected area.
* Layer 2, **trackers and estimators**: how free flights are sampled and transmittance is estimated, one choice per query type per volume (section 5).
* Layer 3, **facade and black box**: the existing `VolumeIntegrator` carries the choices and the capability flags; a black box replaces the walk inside the volume (section 7).

There is no layer for the free-flight law any more: it is exponential everywhere, so the "transport model" of revisions 1 to 3 shrinks to a flag (`Exponential`, implicit).

## 3. The current code and the SGGX gap

### 3.1 What exists [C]

* `Medium` (`base/medium.cs:57-67`): `IsEmissive`, `SamplePoint(p, lambda[, time])`, `SampleRay(ray, tMax, lambda, buf)` returning a majorant iterator. `MediumProperties {sigma_a, sigma_s, phase, Le}` (`media.cs:303`). `PhaseFunction` is an abstract class (`base/medium.cs:24`) with `Sample_p(wo, u)` and `PDF`; the phase object is returned per point by `SamplePoint`, so a phase function can already carry local parameters.
* **There is no SGGX, microflake or projected-area code anywhere in `src/pbrt`** (search for `sggx`, `microflake`, case-insensitive: no hits). `OSLMedium` carries `[ThreadStatic] currentDirection/currentTime` because `SamplePoint` has no direction (`osl_volume.cs:215-216`).
* The per-volume integrator exists: `VolumeIntegrator {tracker, transmittance}` (`volumeintegrator.cs:268-331`), built from two scene strings, `"string tracker"` (`majorant` default | `global`) and `"string transmittance"` (`ratio` default | `analytic` | `tracklength` | `residual`) (`volumeintegrator.cs:297-298`, also read in `osl_volume.cs:839-840`). Estimators write `T_ray`, `r_l`, `r_u` (`TransmittanceEstimator.TryClosedForm/TryEstimate/Step/Finish`, lines 99-124).
* Equiangular sampling is an **integrator** switch, `"bool equiangular"` default false (`integrators.cs:3322, 5672`, `bdptincremental.cs:1484`), not a volume parameter.
* Emission: `MediumEmissionEnd`, `VolumeEmissionLight`, `SampleLiVolume`; UPG refuses emissive media at load (`upg.cs:1192-1195`).
* Boundaries: `BoundaryStack` with priority, `CompositeMedium` sums coefficients and majorants.
* Walk: `PathWalk.Run` (`cpu/pathwalk.cs`), `PathState` with `r_u/r_l/segU/segL/vtx`; bdpt `Vertex` 1344 B with `pdfFwd/pdfRev/delta`; bdptinc `VertexMIS` 84 B.
* Integrators with media: volpath, simplevolpath, bdpt, bdptinc, mlt (through the BDPT evaluator), upg (own extension on the bdpt walk). No medium code in sppm, path, lightpath, simplepath, randomwalk, ao [C, as in revision 2].

### 3.2 Why most of the non-exponential machinery drops out, and the one thing that does not [D]

The incremental MIS multiplies ratios `pdfRev(x_j)/pdfFwd(x_j)` over contiguous runs of vertices. For an exponential medium the density of a medium vertex contains `sigma_t(x_j) T(x_{j-1}, x_j)` in `pdfFwd(x_j)` and `sigma_t(x_j) T(x_j, x_{j+1})` in `pdfRev(x_j)`. In the product the transmittance of each segment appears once in a numerator and once in a denominator (`T` is symmetric), and `sigma_t(x_j)` appears in both. That is why `Vertex.ConvertDensity` and `Vertex.PDF` hold only angular densities and area/volume conversion (`integrators.cs:1859`) and the existing weight-equality and unbiasedness tests pass [C+D]. With null scattering the same holds for the null-collision factors, which are carried by `r_u/r_l`. This stays true for every exponential tracker that has an exact path density (section 5).

**SGGX breaks the cancellation of exactly one factor.** Along a straight segment the extinction is a scalar `sigma_t(x, w)`, and `sigma(w) = sigma(-w)` [P: Eq. 11], so segments are symmetric and their factors still telescope. At a scatter vertex the incoming and outgoing directions differ:

* the forward density of the vertex contains `sigma_s(x, w_in)` (collision) and the phase density `fp(w_in -> w_out)` is attached to the *next* vertex;
* the reverse density contains `sigma_s(x, w_out)` and `fp(w_out -> w_in)`.

Grouped per vertex, the ratio is `[sigma_s(x,w_out) fp(w_out->w_in)] / [sigma_s(x,w_in) fp(w_in->w_out)]`, and Heitz's reciprocity `sigma(w_in) fp(w_in->w_out) = sigma(w_out) fp(w_out->w_in)` [P: Eq. 8] makes it **exactly 1**. The code as it is (phase density only) would produce `sigma(w_in)/sigma(w_out)` instead, a wrong MIS ratio, which is a bias risk and not only a variance problem, because the weights would no longer sum to one. So the interface needs: **at a medium scatter vertex, `pdfFwd` is multiplied by `sigma_t(x, w_in)` and `pdfRev` by `sigma_t(x, w_out)`** (relative to a direction-independent reference such as the majorant), taken from the phase function's projected area at the vertex. It is one multiplication per medium vertex, no storage (the phase object at the vertex carries `S`), and exactly 1.0 for every phase function that is not direction dependent, so existing output stays bit-identical. The same grouping holds for `MixturePhaseFunction` and `CompositeMedium` if the mixture weights are evaluated per direction [D]. Test: section 9, phase 3 and 4.

Null collisions inside a segment keep `sigma_n(x, w) = sigma_maj - sigma_t(x, w)`, even in `w`, so `r_u/r_l` are unchanged if the majorant is direction independent, and also with a per-ray direction-dependent majorant provided *all strategies use the same majorant for the same segment* (which `sigma(w) = sigma(-w)` gives) [D].

## 4. SGGX: what the sources say

### 4.1 The paper [P] (fetched as a summary of the PDF, so equation numbers are the summary's)

* `S` is a symmetric positive-definite 3x3 matrix with six parameters (Sec. 4.2, Eqs. 9-10); `sigma(w) = sqrt(w^T S w)` (Eq. 11; defined as `int <w, wm> D(wm) dwm`, Eq. 4); `sigma_t(w) = rho sigma(w)`, `sigma_s(w) = alpha rho sigma(w)` (Sec. 3.1, Eqs. 2-3).
* Specular microflake phase: `fp(wi -> wo) = D(wh) / (4 sigma(wi))`, `wh` the half vector (Sec. 5.2, Eq. 24). Diffuse microflake phase: `fp(wi -> wo) = 1/(pi sigma(wi)) int <wo,wm> <wi,wm> D(wm) dwm`, **no analytic form**, with an unbiased Monte Carlo estimate by averaging `<wo, wm>/pi` over visible-normal samples (Eq. 25, Eq. 26, Sec. 5.4).
* Visible normals `D_wi(wm) = <wi,wm> D(wm)/sigma(wi)` (Eq. 21) are sampled by Algorithm 1 (basis around `wi`, Cholesky-derived vectors, cosine-hemisphere sample); specular: reflect `wi` about the normal; diffuse: cosine-sample around it; the sample weight is 1 (Sec. 5.1, 5.3).
* Energy conservation `int fp dwo = 1` (Eq. 7) and the unusual reciprocity `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)` (Eq. 8; no shadowing term, multiple scattering is left to the integrator, footnote 3).
* Interpolation: the six coefficients of `S` are interpolated linearly (a convex combination of SPD matrices is SPD); prefiltering is linear averaging of `S` (Sec. 4.4, Eqs. 19-20). Density is downsampled separately.
* The paper **does not discuss majorants, delta tracking or ratio tracking**; the integrator is "out of scope" and the results use Mitsuba's volumetric path tracer [P]. Compact storage keeps `sigma` values in [0, 1]; the summary shows no general bound `sigma(w) <= 1` [P].

### 4.2 Mitsuba 3 [M]

* **Phase function** `sggx` (`src/phase/sggx.cpp`): takes a `Volume` named `S` (constant volume with six values or grid volume with six channels, lines 18-24, 64); flags `Anisotropic | Microflake` (line 65-66). `sample` draws the visible normal with `sggx_sample` (line 86), `pdf = 0.25 * sggx_pdf(sampled_n, s) / sggx_projected_area(mi.wi, s)` (line 99-100), `wo = reflect(mi.wi, sampled_n)` (line 101), and returns **weight 1** (line 102): the specular variant of the paper (Eq. 24). `eval_pdf` returns the same value for eval and pdf, `pdf = 0.25 * sggx_pdf(normalize(wo + mi.wi), s) / sggx_projected_area(mi.wi, s)` (lines 106-121). **The diffuse variant is commented out** with the comment that it "requires some changes to the phase function interface" (lines 63, 88-103, 112-118): Mitsuba 3 has only the specular microflake.
* **Helpers** (`include/mitsuba/render/microflake.h`): `sggx_sample` (lines 70-97, the algorithm of the paper with a frame aligned to `wi`), `sggx_pdf` = the NDF `D` in closed form (lines 121-136), `sggx_projected_area = sqrt(wi^T S wi)` (lines 152-163). `S` is passed as `[Sxx, Syy, Szz, Sxy, Sxz, Syz]`; "the user needs to ensure that the parameters indeed represent a positive definite matrix" (lines 61-64): there is no check.
* **Phase interface** (`include/mitsuba/render/phase.h`): flag `Microflake = 0x04` (line 22), `projected_area(mi)` default 1 (lines 160-176), `max_projected_area()` default 1 (line 179). **`max_projected_area` is declared and never called** (grep over `src` and `include`: only the declaration and the binding).
* **Medium use of the projected area.** Homogeneous: `sigmat = sigma_t * scale; if (phase has Microflake) sigmat *= phase->projected_area(mi)` (`src/media/homogeneous.cpp:153-158`), and this same value is the majorant (`get_majorant`, line 161-165), so a homogeneous SGGX medium samples distances exactly with `rho sigma(w)` along each ray, and `sigma_n = 0`. Heterogeneous: the same multiplication in `get_scattering_coefficients` (`heterogeneous.cpp:190-191`), **but the majorant is `scale * max(sigma_t volume)` without the projected area** (`heterogeneous.cpp:163, 174, 178-181`) and `sigma_n = majorant - sigma_t(w)` (line 194). It is a valid majorant only if `sigma(w) <= 1` everywhere; nothing checks it, and a violation gives negative `sigma_n` and then negative null-collision probabilities in the integrators [M+D]. The direction of the ray reaches the medium through `mei.wi = -ray.d` (`src/render/medium.cpp:48-49`), set before `get_majorant` and `get_scattering_coefficients` are called (lines 62-79): it is the "Mitsuba way" of direction awareness, with no change to the volume interface.
* **Grid volume** `eval_6` requires exactly six channels and throws otherwise (`src/volumes/grid.cpp:407-420`); `interpolate_6` exists (line 564; I did not read it, the paper's rule is linear interpolation of the six coefficients [P]).
* **Tests in the repository** (`src/phase/tests/test_sggx.py`): creation and flags (test01), chi-square tests of the sampling against the pdf for `S = (0.15, 1, 0.8, 0, 0, 0)` and for a skewed `S = (1, 0.35, 0.32, 0.52, 0.44, 0.2)` (test02, test03). Binding tests for the three helpers (`src/render/tests/test_microflake.py`).
* **Mitsuba 2** (commit `62863cb`): only the hook exists, the `Microflake` flag, `projected_area` and `max_projected_area` in `include/mitsuba/render/phase.h:21, 136-154`; `src/phase` contains only `hg` and `isotropic`, and no medium calls `projected_area` [M: grep]. So the SGGX implementation is a Mitsuba 3 feature; "Mitsuba 2/3 support" in the sense of this note means the Mitsuba 3 code.
* **Integrators** (`src/integrators`): `volpath`, `volpathmis` and the surface integrators; **no bidirectional volume integrator**, so Mitsuba gives no evidence on the vertex-density question of section 3.2.
  * `Medium::sample_interaction` (`src/render/medium.cpp:42-82`) draws a free flight with **one majorant per ray** (the sampling channel's component, lines 62-71), delta-tracking style; `mei.sigma_s/sigma_n/sigma_t` come from `get_scattering_coefficients`.
  * `volpath` (`src/integrators/volpath.cpp`): a channel is chosen uniformly per path (lines 120-123); for spectral media the weight is `tr / tr_pdf` per segment (lines 236-239) and null collisions multiply `sigma_n / null_scatter_prob` (lines 252-253), for non-spectral media the throughput gets `sigma_s / sigma_t` at a real scatter (line 273); shadow rays do delta tracking with `transmittance *= sigma_n` (spectral) or `sigma_n / combined_extinction` (non-spectral) (lines 519-521), i.e. the ratio-tracking form in the non-spectral case. `has_spectral_extinction` is the per-medium switch (`homogeneous.cpp:143`, `heterogeneous.cpp:161`).
  * `volpathmis` (`volpathmis.cpp`): the spectral MIS of Miller et al. 2019 [M: the documentation block says so, doc block at the top of the file], with weight matrices `p_over_f`, `p_over_f_nee` (lines 152-157, 629-666) updated at free flights, null and real events (lines 283-327). It plays the role of `r_u/r_l` in our walk; I compared the two only at the level of the data they carry, not line by line [D].
  * None of the Mitsuba code uses a hierarchical majorant grid: the heterogeneous majorant is one global value (`heterogeneous.cpp:163`), against the `majorant` tracker of this repository (per-cell majorants) [M+C].

### 4.3 Consequences for us [D]

1. **Direction awareness.** Two ways: (M1) the Mitsuba way, the medium multiplies by `phase.ProjectedArea(w)` wherever it builds `sigma_t`; (M2) `SamplePoint(p, lambda, w, time)` returns scaled coefficients. Here `sigma_a` and `sigma_s` are read at many sites (`SampleT_maj` callbacks, `ShadowRay`, `TrSegments`, equiangular, emission, `MediumFactor`, OSL), so M1 would repeat the multiplication at each; M2 puts it inside the medium and the call sites only pass the ray direction. **M2 is recommended**, with a default-forwarding overload so that existing media are untouched.
2. **Majorant.** A valid direction-independent majorant is `rho_max * sqrt(lambda_max(S))` per cell; since `w^T S w` is linear in `S` and the trilinear interpolant is a convex combination of the corner matrices, `sigma(x, w)^2 <= max over the 8 corners of w^T S_c w <= lambda_max over the corners` [D]. So a per-cell bound from the corner matrices is valid. Mitsuba's global `max(sigma_t)` is valid only for `sigma <= 1`. We should check at load (`max over cells lambda_max(S) <= 1` or fold the factor into the majorant) and assert `sigma_n >= -eps` in debug builds. A tighter per-ray bound that uses the ray direction is possible (`SampleRay` receives the ray) but not needed first.
3. **Diffuse microflakes** have no closed-form pdf (Eq. 25), so the diffuse phase is an estimator without an evaluable density: sampling and the joint (normal, direction) weight are exact (weight 1), the value for connections is the unbiased estimate of Eq. 26, and the density in MIS needs the surrogate of section 6.3. A cheap positive surrogate is the specular pdf (`D(wh)/(4 sigma)` is positive for every direction since `D > 0`) [D].
4. **Emission** in an SGGX medium has no definition in the paper (`sigma_a(w) Le` is direction dependent, and a point sampled on a `VolumeEmissionLight` would need the direction). Revision 4 treats it as unsupported with an error at load.
5. **Polarisation:** the model is scalar. The existing code gives scalar phase functions the identity Mueller matrix [C: `cpu/polarisedpath.cs:29-44`]; an SGGX scatter in a polarised integrator is treated the same way (depolarising), with a one-time warning [D]. A specular flake is a Fresnel reflection with a real Mueller matrix; not designed here.
6. **Test shortcut:** for `S = I` the NDF is `D = 1/pi`, `sigma(w) = 1`, and the specular microflake phase is `D/(4 sigma) = 1/(4 pi)`: an isotropic phase. So an SGGX medium with `S = I` must equal the isotropic homogeneous medium of the same `rho` exactly in expectation [D: from Eqs. 11, 12, 24 as in the summary], a regression test of the whole stack.

## 5. The tracker and estimator zoo

Query types of a volume: **F** free-flight sampling in walks; **C** transmittance between two vertices (connections); **S** shadow-ray transmittance (next-event estimation); **D** distance sampling toward a light or vertex (equiangular, and its MIS with F); **E** emission (collision emission at walk collisions, and sampling of a volumetric emitter).

Classes of density: **ExactPdf** (the path density in null-scattering space is exact and enters `r_u/r_l`), **Deterministic** (a closed form, no sampling), **NoPdf** (the sampling is exact but the density of the sample cannot be evaluated). "Signed" says whether weights can be negative. The columns on the right say what happens with each consumer; "mask" and "surrogate" are the options of section 6.3.

| Estimator (query) | Source | Needs from the medium | Density class | Signed | Chromatic | bdpt / bdptinc | MLT | UPG | Polarised |
|---|---|---|---|---|---|---|---|---|---|
| **delta tracking** with null scattering (F) = the `majorant` tracker | Miller et al. 2019 Sec. 5.2 [P]; pbrt-v4 `SampleT_maj` [C: `media.cs:121`] | piecewise-constant majorant iterator `>= sigma_t`; hierarchical grid optional | ExactPdf in null-scattering space (the marginal free-flight pdf is "unknown" [P: STAR 4.2.1]) | no, unless `sigma_n < 0` | hero-wavelength `r_u/r_l` [C] | yes (today) | yes: RNG hashed from the geometry [C: `pathwalk.cs`] | yes: the tentative ray is the walk [C: `/tmp/upg_work/docs/upg.md`] | yes |
| **global-majorant tracking** (F) = `global` tracker | [C: `volumeintegrator.cs:41`] | one `sigma_maj` for the whole medium | ExactPdf | no | same | yes | yes | yes | yes |
| **ratio tracking** (C, S) | Novak et al. 2014 [P: abstract]; STAR 5.3.1 [P] | the majorant of the tracker | ExactPdf if the majorant is the walk's (null vertices are explicit) | only if `sigma_maj < sigma_t` | `r_l/r_u` [C] | yes, with the same majorant per segment | yes | the weights use a deterministic quadrature, not ratio tracking [C] | yes |
| **track-length / binary delta tracking** (C, S) = `tracklength` | STAR 5.2.2 [P] | majorant | ExactPdf (as delta tracking) | no (0/1) | `r_u/r_l` | yes | yes (hashed) | as ratio | yes |
| **residual ratio tracking** (C, S, F) = `residual` | Novak et al. 2014 [P: abstract only]; STAR 5.3.2 [P] | control `sigma_c` with a closed-form `T^c`, majorant for the residual `sigma_t - sigma_c` | NoPdf for connections unless it writes the walk's `r_l/r_u` (the code writes them with the residual majorant, which is not the walk's [C: `volumeintegrator.cs:227-257`, audit item of 5c in revision 2]) | weights `1 - (sigma_t - sigma_c)/sigma_r` can be negative | control must be wavelength-wise valid | strategies through it need the surrogate or the mask (6.3) | yes | surrogate | yes |
| **spectral tracking** (F) | Kutz et al. 2017 [P: Sec. 7] | one majorant for all wavelengths; event probabilities from the path throughput | NoPdf: "the PDF of the free-path sample cannot be evaluated exactly" and MIS only works when the same tracker is used both ways [P] | yes (`sigma_n/sigma_maj` ratios with throughput-derived probabilities) | its purpose; replaces the hero-wavelength ratios | walks only with the mask; or with a surrogate | yes (hashed) | not allowed in revision 4 (error at load) | yes |
| **decomposition tracking** (F, S) | Kutz et al. 2017 [P]; STAR 4.2.3 [P] | minorant `sigma_c` (closed-form homogeneous part) + residual majorant | NoPdf (residual pdf not explicit [P]) | yes | per-wavelength minorant | as spectral | yes | error | yes |
| **regular tracking / analytic homogeneous** (F, C, S) | STAR 4.1.2, 5.1.2 [P] | piecewise-constant extinction along the ray (a homogeneous medium, or a nearest-neighbour grid with a DDA) | Deterministic (closed-form inversion, `T = exp(-sigma L)`) | no | exact per wavelength: the ideal for the hero ratios | yes: exact densities, no null vertices | yes | yes | yes |
| **equiangular** (D) | Kulla and Fajardo 2012 [R]; STAR 6.3 [P] | the geometry; extinction only through the transmittance factor | Deterministic (pdf `~ 1/d^2` along the ray) | no | exact | yes, already (`EquiangularTerm` [C]); the integrator switch `equiangular` becomes a per-volume mode | yes | yes (as the segment `T`) | yes |
| **collision emission and volumetric emitter sampling** (E) | pbrt-v4 emission at collisions `T_maj sigma_a Le/...` [C: `integrators.cs:4806-4861`]; `VolumeEmissionLight` [C] | `sigma_a`, `Le`; a bound for the sampled emitter | ExactPdf (null-scattering) | no | per-wavelength | yes (`MediumEmissionEnd`) | yes | **not supported** [C: `upg.cs:1192`] | yes |
| *excluded*: ray marching and quadrature of the optical thickness | STAR 4.1.3, 5.1.3, 5.1.4 [P] | extinction samples | biased (Jensen) | no | n/a | n/a | n/a | n/a | n/a |

Notes.
* The Mitsuba equivalents: `volpath` is a single-sampling-channel delta tracker with spectral weights (`volpath.cpp:236-273`), its shadow rays are the spectral or ratio-tracking forms (`:519-521`); `volpathmis` is the spectral-MIS version in the sense of Miller et al. 2019 and corresponds to our `r_u/r_l` (4.2). Neither has decomposition, spectral-tracking-by-throughput, residual or regular tracking, equiangular or emission sampling in the files I looked at [M: not searched for exhaustively].
* "ExactPdf for ratio tracking" holds only if the null vertices it samples are the same random variables the walk would have sampled, i.e. the same majorant on the same segment. This is the audit item that revision 2 left open (5c.3) and phase 2 below tests.
* Signed weights and Russian roulette in `ShadowRay` interact (the roulette tests the magnitude of `T_ray`) [C]; the roulette needs a sign-aware rule if signed estimators are enabled.

## 6. Decisions

### 6.1 Per-volume and per-query choice: scene parameters

The facade `VolumeIntegrator {tracker, transmittance}` is extended. Option table:

| Option | What | For | Against |
|---|---|---|---|
| P1 one string for the whole stack | `"string volumeintegrator" "fong"` with presets | short | presets hide combinations; no per-query choice |
| **P2 one parameter per query type (recommended)** | extend the existing two strings with three more | already how `tracker` and `transmittance` work [C]; the presets can be added later as sugar | five strings per medium |
| P3 integrator-level choice | as `equiangular` is today | no change to media | the user's requirement is per volume; two volumes in one scene cannot differ |

**Parameters of a medium (`MakeNamedMedium`)**, with the defaults reproducing today's behaviour exactly:

| Parameter | Query | Values | Default |
|---|---|---|---|
| `"string tracker"` | F | `majorant` \| `global` \| `analytic` \| `spectral` \| `decomposition` | `majorant` [C] |
| `"string transmittance"` | C | `ratio` \| `analytic` \| `tracklength` \| `residual` \| `decomposition` | `ratio` [C] |
| `"string shadowtransmittance"` | S | same list | the value of `transmittance` (today both queries share it [C]) |
| `"string distancesampling"` | D | `tracker` \| `equiangular` \| `mis` | `tracker`; overridden to the integrator's `"bool equiangular"` when the medium does not set it |
| `"string emission"` | E | `collision` \| `light` \| `both` | `both` for an emissive medium (today's behaviour) |
| `"string surrogate"` | (MIS) | `mask` \| `quadrature` | `mask` (6.3) |
| `"string phase"` | | `hg` (default) \| `sggx` \| `sggxdiffuse` \| mixture | `hg` |
| `"string volumeintegrator"` | black box | name of a registered black box (section 7) | none |

Rules of validation at scene load (`ErrorExit(loc, ...)`, as `VolumeIntegrator.Create` does for unknown names [C: `volumeintegrator.cs:297-320`] and as UPG does for emissive media [C: `upg.cs:1192-1195`]); each is a clear error naming the volume, the two parameters and the reason:

1. `analytic` (tracker or transmittance) on a medium whose extinction is not constant along the ray (a trilinear grid): error; allowed on a homogeneous medium and, with a DDA, on a nearest-neighbour grid.
2. `spectral` or `decomposition` with `upg`: error (the tentative ray's outcome weight would be signed and throughput-dependent, and the collision factor needs an evaluable density).
3. `spectral`, `decomposition`, `residual` or `sggxdiffuse` with `bdpt`/`bdptinc`/`mlt` and `surrogate = mask`: allowed, warning once per volume that connections through the volume are masked.
4. `mlt` with any estimator that is not a deterministic function of its random draws and the geometry: error (all listed ones are deterministic given the hashed RNG [C], the black box is the exception).
5. `sggx` with `"emissive"`: error (4.3, item 4). `sggx` with `sppm`: error (no media code).
6. `sggx` with a medium type that cannot answer a directional query: error until it implements the overload (`OSLMedium` first needs its thread-statics folded into it).
7. `sggx` with `tracker = global`: allowed only if the global majorant bounds `rho sqrt(lambda_max(S))`; otherwise error naming the numbers; debug assertion `sigma_n >= -eps` for all trackers.
8. Overlapping volumes (`CompositeMedium`) with different `tracker`/`transmittance` settings: the composite uses the settings of the volume with the highest priority and warns; SGGX and non-SGGX volumes may overlap only through the directional `SamplePoint` of the composite (4.3 item 1).
9. Polarised integrators with `sggx`: warning (depolarising).

### 6.2 SGGX and microflake materials as pluggable phase models

| Option | What | Verdict |
|---|---|---|
| S1 phase function + medium data, as Mitsuba | `SGGXPhaseFunction` carries local `S`; `SamplePoint(p, lambda, w, time)` multiplies `rho` by `phase.ProjectedArea(w)` | **recommended**: the phase is already returned per point [C], and the medium stays a density/albedo/`S` grid |
| S2 a separate `SGGXMedium` that does everything | own `SamplePoint`, own phase | duplicates the grid, majorant and emission code |
| S3 a "microflake material" that owns the volume | `S` as a material property and the medium a plain grid | useful with the unified materials and volumes work, not read in this revision [R]; keep the phase interface identical so that S1 and S3 can share it |

The phase interface gains `ProjectedArea(Vector3f w)` (default 1), `MaxProjectedArea()` (default 1, **used** by the majorant code, unlike Mitsuba's) and a flag `DirectionDependent`. Models: **specular microflake** (`sggx`: the paper's Eq. 24, weight 1, pdf closed form, class ExactPdf) in phase 4; **diffuse microflake** (`sggxdiffuse`: sampling Algorithm 1 + cosine hemisphere, weight 1; value by the Eq. 26 estimator with a fixed sample count; class NoPdf, so MIS needs the specular-pdf surrogate or the mask) in phase 5. Interpolation of `S` by the six coefficients, prefilter by averaging, as the paper [P]. Mixed with HG in a `MixturePhaseFunction`: the mixture weights are evaluated per direction (3.2).

### 6.3 Estimators without an evaluable density: three options (revises the rule of revision 2)

MIS weights only need to sum to one over the strategies that can produce a path, and each strategy's *sampling* weight must be exact. Weights computed from *any* positive density function `p~_s(x)` of the path `x`, used identically by every strategy, give an unbiased estimator; they lose variance, not unbiasedness. The requirement is that the value `p~_t(x)` assumed for strategy `t` is the same number whichever strategy asks [D: this is Veach's partition-of-unity condition; UPG's `min(V p, 1)` is the same idea in the code [C: `docs/upg.md`, "p_c in the weights has the collision factor"]]. Revision 2 said strategies through a `NoPdf` volume must be masked. **That was too strong; masking is the safe option, not the only one.** Retraction noted.

| Option | What it does | Cost | Variance | Verdict |
|---|---|---|---|---|
| N1 mask | `StrategyAllowed` bit cleared, per path, for connection strategies through the volume; remaining strategies get the full weight | none | high for small lights seen through the volume | **default, first** |
| N2 surrogate by quadrature | every segment in the volume gets a deterministic marginal density `sigma_t(x) T~(a, b)` with `T~` by 8 midpoints (as `MediumFactor`, `upg.cs:200-224` [C]); stored as one float per segment (the "segment record" is back, for these volumes only) and used by all strategies in the weights; the volume's own estimators keep exact sampling weights | 8 `SamplePoint` per segment per path | depends on `T~`, typically small | recommended second, behind `"string surrogate" "quadrature"` |
| N3 same estimator both ways | require the same tracker for free flight and transmittance (Kutz's condition [P]) | limits combinations | exact | only for spectral tracking in volpath |

A path may cross volumes of different treatments: ExactPdf volumes keep the null-scattering densities, N2 volumes use marginal surrogates (the null vertices of an N2 volume are auxiliary variables: the weights depend only on the real vertices), N1 volumes mask. The per-path decision depends only on the path (which volumes it crosses), never on random state [D]. With MLT the same: the weight is a function of the path.

### 6.4 What is left of the interface (the answer to "say exactly")

| Item | Needed? |
|---|---|
| free-flight densities per segment in the vertex | **no** (exponential telescoping); optional float per segment for N2 volumes |
| endpoint kinds, birth/collision flags, memory variable | **no** |
| adjoint flag | **no** |
| `SamplePoint(p, lambda, w, time)` | **yes** (SGGX, and it removes the OSL thread-statics) |
| phase `ProjectedArea(w)`, `MaxProjectedArea()`, `DirectionDependent` | **yes** |
| extinction factor in `pdfFwd/pdfRev` at medium scatter vertices (3.2) | **yes**, one multiplication, skipped when not direction dependent |
| majorant bound that includes the projected area | **yes** (4.3 item 2) |
| per-query estimator choice, density class, surrogate policy | **yes** |
| capability flags `ExactSegmentDensity`, `Connectable`, `EmissionLight`, `BothDirections` | **yes** for black boxes only (7) |
| pull iterator for trackers | **no longer necessary**; kept optional for removing the delegate and closure per collision [C: `pathwalk.cs`] |

### 6.5 Performance

* The strategy loops stay straight-line: weights come from stored sums, the media are not called in them.
* Defaults reproduce today's trackers, so the default path pays nothing; a `DirectionDependent` flag test per medium vertex is a predictable branch.
* New cost lives where it is chosen: N2 (8 samples per segment), the diffuse microflake estimate (a fixed number of VNDF samples per evaluation), regular tracking with a DDA. I have not measured any of them [D]; phase 1 measures the dispatch.

## 7. The black box

A volume that carries its own integrator (any tracker, a procedural or precomputed model) behind its boundary. Exponential only; SGGX volumes are ordinary volumes, not black boxes.

**B1, delta boundary (first).** The boundary of the box is a pair of `delta` vertices, exactly like a specular surface: the existing rule gives `a_k = 0` for strategies that connect at a `delta` vertex (`bdptincremental.cs:89`, `:123` `!v.delta && !before.delta`, `:750` `!ptMinus.delta`) [C]. Connections do not cross the boundary: a shadow ray that reaches it is treated as occluded for the connection strategies, and light that passes through is found by walks (camera walks that exit the box and meet a light; light walks that enter it), like light seen through glass [D]. Contract of the box: `Enter(state) -> (exit state, weight, ok)` for the walk, an unbiased weight; a box that can be entered from both sides (`BothDirections`) is usable by light walks, a box that cannot is camera-walk-only, which `StrategyAllowed` expresses. MLT: the box must be a deterministic function of its random draws and the geometry, otherwise an error at load. Emission inside the box: the box reports `Le` along its walk and the gather is added with weight 1 (no competing strategy exists, as with specular light) [D].

**B2, evaluable.** The box exports capabilities and the strategies through it unlock one by one:

| Capability | What the box must give | What it unlocks |
|---|---|---|
| `Connectable` | `Transmittance(a, b)` as an unbiased estimate | connection strategies across the box |
| `ExactSegmentDensity` | a deterministic density for the walk through the box (or the N2 surrogate) | those strategies get MIS weights without the mask |
| `EmissionLight` | an emitter with an exact density | MIS for the emission gather |

An unbiased *estimate* of a density is not enough for the ratios: a ratio of unbiased estimates is not an unbiased estimate of the ratio. UPG gets around this for the reciprocal of a collision probability with Bernoulli trials (`E[N_b]/p_b = 1/p_c`, proof in `docs/upg.md` [C]) and it applies the trick only to the estimator's factor, never to the MIS weight, which uses the deterministic approximation `min(V p, 1)` [C: `docs/upg.md`]. Same here: a box with only a random density estimate takes the N2 surrogate for its weights and its unbiased estimate for the contribution; a box that offers neither is masked (N1). The pseudo-marginal reading ("could a Bernoulli-trial estimator carry stochastic segment densities in general") stays an open research question [D].

**Different estimators in one path (the MIS-consistency rule, revised).** Each volume has one treatment: ExactPdf (null-scattering densities), surrogate (N2), or mask (N1). A strategy is allowed on a path if no volume on the path masks it; its weight is `p~_s / sum_t p~_t` with the densities of the volumes' treatments. The first volume to be added with a new treatment must add a test that the weights sum to one on a path with mixed treatments (section 9).

**Nested and overlapping volumes.** The `BoundaryStack` priority decides which volume owns a region [C: `boundarystack.cs`]. A black box nested in another volume pushes on the stack like any medium; segments inside it never reach the outer volume's trackers. Volumes of the same exponential family overlap as `CompositeMedium` does (sum of coefficients and majorants [C]); the composite uses the settings of the highest priority volume (rule 8). A black box does not take part in a composite: it owns its region (priority), with a one-time warning.

**Emission.** Inside ordinary volumes: unchanged. SGGX: unsupported. Black box: above.

## 8. Compatibility matrix (reduced)

E exact (unbiased, MIS consistent), M exact but with masked connections through the volume, S surrogate weights (unbiased, higher variance), A approximate or a documented restriction, U unsupported. "today" is the code on this branch.

| Estimator / medium | volpath | bdpt | bdptinc | mlt | upg | polarised | chromatic |
|---|---|---|---|---|---|---|---|
| delta (`majorant`), exponential, HG | E (today) | E (today) | E (today) | E (today) | E (own ext.) | E for Rayleigh (today) | E hero-wavelength (today) |
| ratio / track-length, exponential | E | E | E | E | E | E | E |
| analytic / regular tracking, exponential | E | E | E | E | E | E | E, exact ratios |
| residual ratio | E | M or S (audit of 5c.3, revision 2) | M or S | E | S | E | E |
| spectral tracking | E | M or S | M or S | E | U | E | E (its purpose) |
| decomposition tracking | E | M or S | M or S | E | U | E | E |
| equiangular (distance sampling) | E | E (today) | E (today) | E | E | E | E |
| emission (collision, light) | E | E | E | E | U (today, error) | E | E |
| **SGGX specular, any exact tracker** | E [D] | E [D] with the extinction factor (3.2) | E [D] same | E [D] | A: `MediumFactor` needs the directional `sigma_t`; angular bound of the Bernoulli trials is `p_b = 1` (whole square) like the Rayleigh mixture [C: `docs/upg.md`] | A: depolarising | E [D] |
| SGGX diffuse | E (unbiased value, weight 1) | S or M | S or M | E | A | A | E |
| SGGX with emission | U | U | U | U | U | U | U |
| black box B1 | E | E, no connections through | E | E if deterministic | E as delta boundary | depends | depends |
| black box B2 (flags) | E | E per capability | E | E if deterministic | E | depends | depends |

## 9. Migration plan and test plan

Common rule: before each phase, record image hashes (fixed seed, in-process `RenderImage` of `bdptincremental_stat_test.cs`) of volpath, simplevolpath, bdpt, bdptinc, mlt on the media scenes of `BdptInc` (fog, foghg, fogchrom, foggrid, fograyleigh, fognested, fogemit, equiangular, polarised Rayleigh fog); phases that say "bit-identical" must reproduce them exactly [C: scenes exist].

| Phase | Change | Output | Tests |
|---|---|---|---|
| 0 | hash harness for the integrators above; weights-sum-to-one test for one path | none | the hashes themselves |
| 1 | `VolumeIntegrator` gains `shadowTransmittance`, `distanceSampling`, `surrogate`, density class and capability flags; scene parser and validation (6.1) with errors; defaults equal today | bit-identical | hashes; a test per validation rule that the right error appears; microbenchmark of the dispatch (ns per segment) |
| 2 | split the two estimator queries (C versus S); audit the `ResidualRatioEstimator` MIS question (5c.3 of revision 2); `"string distancesampling"` per volume replaces the integrator bool as a default only | bit-identical with defaults | weight equality bdpt versus bdptinc on grid media with `residual` shadow rays; unbiasedness (t-tests) of connections with each estimator against `ratio` |
| 3 | `SamplePoint(p, lambda, w, time)` with default forwarding; fold `OSLMedium` thread-statics into it; phase `ProjectedArea`/`DirectionDependent` and the extinction factor in `Vertex.PDF`/`ConvertDensity` (exactly 1 for non-directional phases) | bit-identical | hashes; a synthetic directional phase whose `sigma(w) fp` is symmetric: the MIS ratio at a vertex is 1.0 to float precision |
| 4 | **SGGX medium + specular microflake phase**, `S` grid volume (six channels, linear interpolation), majorant from `rho sqrt(lambda_max(S))`, load checks (SPD, bound) | new | see below |
| 5 | extra trackers: `spectral`, `decomposition`, `analytic`/regular for homogeneous and nearest-neighbour grids; `sggxdiffuse`; N2 surrogate | new | see below |
| 6 | black box B1, then B2 capabilities | new | wrapped exponential medium gives the same expectation as unwrapped (t-tests), nested-in-medium and medium-in-box scenes, MLT determinism test |

**Phase 4 tests (SGGX closed forms and consistency)** [D unless noted]:
1. `sigma(w)` against quadrature of `int <w, wm>_+ D(wm) dwm` with `D` of Eq. 12 [P], for `S = I` (`sigma = 1`, `D = 1/pi`), `S = diag(sx^2, sy^2, sz^2)` (`sigma` along each axis = `sx`, `sy`, `sz`), a rotated `S` (`sigma_{R S R^T}(R w) = sigma_S(w)`), the skewed `S` of Mitsuba's test03 [M], and the nearly-flat limit `S = z z^T + eps I` (`sigma(w) ~ |w.z|`); evenness `sigma(w) = sigma(-w)`.
2. `sigma_t(w) = rho sigma(w)` for the medium, and **homogeneous transmittance `exp(-rho sigma(w) L)`** through a purely absorbing slab for several directions, measured with `analytic`, `ratio`, `tracklength` and `majorant` against the closed form.
3. Phase: normalisation `int fp dwo = 1` by quadrature; reciprocity `sigma(wi) fp(wi->wo) = sigma(wo) fp(wo->wi)`; chi-square of the visible-normal sampling against the pdf (the two parameter sets of Mitsuba's tests [M]); `S = I` equals the isotropic phase.
4. `S = I` medium equals the isotropic homogeneous medium, in volpath, bdpt, bdptinc (t-tests); furnace: albedo 1 medium without absorption inside a uniform emitter returns `Le` for volpath, bdpt, bdptinc with a strongly anisotropic `S` (this catches a missing extinction factor, 3.2).
5. bdpt versus volpath on SGGX scenes (fibre-like and surface-like `S`, grid with varying `S` and `rho`): per-pixel-group t-tests; bdpt versus bdptinc weight equality on SGGX paths; weights sum to one on a path with SGGX vertices.
6. Majorant: a grid with `sigma(w) > 1` produces the load error (rule 7) and the debug assertion catches a forced violation.

**Phase 5 tests:** all trackers unbiased against each other on a grid medium: each of `majorant`, `global`, `spectral`, `decomposition`, and each of `ratio`, `tracklength`, `residual`, `decomposition` for S and C, against the reference `majorant + ratio` at high spp (t-tests); signed-weight handling (Russian roulette); the mixed-treatment path weights sum to one (6.3); `analytic` against the closed form on a homogeneous medium; the diffuse microflake against a high-sample-count brute-force value of Eq. 25 [P]; equiangular with SGGX (the weight uses `sigma_s(x, w) T`).

## 10. Risks and open questions

* The extinction-factor rule of 3.2 is my derivation [D] and Mitsuba contains no bidirectional integrator to corroborate it; the furnace and weight-sum tests of phase 4 are the proof.
* The residual-ratio MIS question (5c.3 of revision 2) is still unaudited [C+D].
* Signed estimators (decomposition, spectral) with Russian roulette, MLT targets and path filtering need a sign-aware rule.
* N2 surrogate quality: a poor surrogate raises variance without bias; the quadrature step count is a parameter (UPG uses 8 [C]).
* The directional majorant bound is conservative (`lambda_max`); a flat-fibre `S` gives a majorant much larger than most `sigma(w)`, so many null collisions. A per-ray tighter bound is possible [D].
* Thread safety: media keep no state; `OSLMedium` thread-statics must go with the directional `SamplePoint`.
* Diffuse microflakes: the value estimate has variance; a deterministic hashed estimate could serve as the surrogate density, the specular pdf is the cheaper choice [D].
* Polarisation with SGGX is not modelled (depolarising).
* The UPG angular bound for SGGX sampling (the cosine-hemisphere warp followed by a linear map of the visible-normal construction) was not derived; `p_b = 1` is correct and slower by `1/p_c` [D].

## 11. Recommendations in one list

1. Keep and extend the existing `VolumeIntegrator`: five query strings plus `surrogate` and `phase`, defaults equal to today (6.1).
2. SGGX as phase + medium data (S1), direction-aware `SamplePoint` (M2), per-cell majorant `rho sqrt(lambda_max)`, specular first, diffuse later (6.2, 4.3).
3. Add the extinction factor at medium scatter vertices; test it with the furnace and the `S = I` equivalence (3.2).
4. Estimators without an evaluable density: mask first (N1), surrogate by quadrature second (N2), never ratios of random estimates (6.3).
5. Black box = delta boundary first; capabilities unlock connection, density and emission (7).
6. Validate combinations at scene load with errors that name the volume and the reason (6.1).
7. Phases 0 to 3 are bit-identical refactors; SGGX, extra trackers and the black box follow (9).

## 12. What I could not verify

* No paper was read as raw text; all [P] statements, including the SGGX equations and the STAR table, come from a fetch tool that summarises. A direct fetch of the JCGT page was refused by the tool's allowlist; the NVIDIA PDF of the paper and the Dartmouth PDF of the STAR were fetched (the STAR summary covered the first 100000 of 104131 characters). Miller et al. 2019, Kutz et al. 2017 and Novak et al. 2014 were read in earlier revisions only as summaries or abstract pages (Novak 2014: abstract page; the PDF fetch failed).
* Mitsuba: I read `sggx.cpp`, `microflake.h`, `phase.h`, `homogeneous.cpp`, `heterogeneous.cpp`, `medium.cpp`, the SGGX parts of `volume.h`/`grid.cpp` (not `interpolate_6`), and grepped `volpath.cpp`/`volpathmis.cpp`; I did not read the Mitsuba 3 documentation pages for the media, the Python bindings beyond the tests, or any other branch. Mitsuba 2 was only grepped. Behaviour was inferred from code, never observed (nothing was built or run).
* The claim that the extinction factor at SGGX vertices is exactly what bdpt/bdptinc need (3.2), the surrogate rule (6.3), the majorant bound (4.3), the `S = I` equivalence and the quadrature check of `sigma` (9) are my derivations [D] and untested.
* The interaction of Russian roulette, signed weights and MLT was not tested.
* The unified materials and volumes documents and the per-entry-volume code were not read in this revision; the per-material statement in 6.2 is a design intent.
* The performance of every proposed estimator is an estimate; none was measured. UPG behaviour comes from `/tmp/upg_work` (`src/pbrt/upg.cs`, `docs/upg.md`) read-only.
* Revision-3 material (Tungsten, d'Eon) was not re-read for this revision; Appendix B repeats its conclusions with their tags.

## Appendix A. Deferred frameworks and the cost of re-adding them

Out of the spec, no hooks. **Non-exponential media** (Bitterli et al. 2018, d'Eon 2018): needs a free-flight law with the four functions of optical depth and the `sigmaBar` normalisation, a `SegmentRecord` with forward and reverse densities and start/end kinds, the channel-averaged density in the weights, an optical-depth inversion instead of tracking for heterogeneous media, and no overlap of different laws (Appendix B). **Spatially correlated media** (Jarabo et al. 2018): a new transport model and a path-integral formulation for bidirectional use that the authors do not give [P]; until then it lacks an exact density and stays out of bidirectional integrators. **RRTE** (Ament 2014; Pediredla et al. 2020; Fraboni et al. 2023): curved segments make the walk ask the model for exit points and tangents, a manual stack pop, per-vertex tangents and Jacobians, a connection solver with randomised restarts, a strategy mask for connections that cannot be curved, and an estimator class MLT must exclude; an implementation exists on `experimental/refractive-media` (reverted from this line in `1f6dcac`: 16 files, 4838 lines deleted, 68 added, about 800 lines in `integrators.cs` and 85 in `pathwalk.cs`) [C]. **Transient rendering:** a time dimension in the path integral and in the MIS densities; not analysed [R].

## Appendix B. Findings from the Tungsten and d'Eon reading (revision 3, kept for later)

Source: Tungsten `master` 88ea020, read as text, never built or run [T]; paths relative to `src/core/`.

* The law is four functions of the optical depth, `surfaceSurface/surfaceMedium/mediumSurface/mediumMedium`, plus `sigmaBar` and two inverse-CDF samplers (`transmittances/Transmittance.hpp:17-58`). The edge throughput `E(tau, startOnSurface, endOnSurface)` is `surfaceSurface`, `mediumMedium/sigmaBar` or `mediumSurface` for both mixed cases (lines 22-30), so `E` is symmetric under swapping the ends. A sampled collision has weight `E rho sigma_s sigmaBar / (rho sigma_t mediumPdf)` = albedo (`media/HomogeneousMedium.cpp:65-108`, `VoxelMedium.cpp:141-145`): the integrand carries `sigma_s sigmaBar` per medium vertex and `1/sigmaBar` per medium-to-medium edge [D].
* No adjoint flag on media: the reverse density is the same function on the reversed ray with the start and end kinds swapped (`media/Medium.cpp:45-51`, `PathVertex.cpp:162-163`); stored per edge (`PathEdge.hpp:15-16`). Camera and emitter vertices count as surfaces (`PathVertex.hpp:239-242`).
* Integrators: path tracer, light tracer, bidirectional path tracer and the photon walks support non-exponential media; MLT does not (`Medium::invertDistance` is `FAIL`, `Medium.cpp:40-43`). BDPT MIS multiplies the edge densities into the vertex densities and applies Veach's ratio recurrence (`LightPath.cpp:50-92, 145, 157`, `PathVertex.cpp:295-326`).
* Heterogeneous media: optical-depth inversion on a voxel DDA (`VoxelMedium.cpp:97-154`, `grids/VdbGrid.cpp:307-377, 379`); a stochastic residual-ratio optical depth is biased with a non-exponential law (no guard found in the files read). Chromatic: uniform channel choice, channel-averaged density (`VoxelMedium.cpp:138-141`).
* Overlap: none, one medium per point (`primitives/Primitive.hpp:177-183`).
* Reciprocity: the integrand is reciprocal by construction, the sampling densities are not; the repository has no test, so the TOG text versus technical report disagreement cannot be settled from the code.
* Tungsten defects noticed: stale `sample.exited` (`ExponentialMedium.cpp:133-134`); density at the ray start in `VoxelMedium::pdf` (line 188); end-flag mismatch between the two shadow-ray variants (`TraceBase.cpp:107` versus `:111`).
* d'Eon (arXiv:1803.03259) makes the same split between free paths from correlated and uncorrelated locations [P: abstract].
