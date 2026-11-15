# Open items on the pbr-materials branch

What is left to do, as of 14 November 2026. Each section lists the work in the order it is meant to be done.

## To port from the C++ fork

1. `stackedcoatedconductor`.
2. `vcavity` (Lee et al. 2018) and the fork's transmissive variant. The conductor is parked on the branch `vcavity-backup`;
   check what is there first.
3. Phase functions: fog and cloud (Jendersie and d'Eon, HG + Draine, with `dropletdiameter`), Cornette-Shanks, tabulated and
   spectrally tabulated, the `depolarization` parameter, the `phasesampling` / `hero` options.
4. (done: `polygrain`, `facetforge`, `facetcoateddiffuse`)
6. `diffraction` (Liu et al. 2025, Holzschuch and Pacanowski, the additive mode; `util/kirchhoff.h`).
7. (done differently: `openpbr` is built on the lobes of this branch, see "OpenPBR, Maxwell, MaterialX" below. Of the fork's
   own `OpenPBRMaterial` what is not here: `twofacet.h` and `surfgrad_microfacet.h` for a normal map of the coat alone.)

Smaller pieces of the fork: the coat on area lights (`coatweight`, `coatior`, `coatcolor`, `coatdepth`, `coatalpha`); the
thin-film iridescence of Belcour and Barla on the standard materials (perhaps not wanted: Magi has the transfer-matrix
`thinfilm` material).

## Gaps in what is on the branch

### Feature lines (`cpu/featureline.cs`)
- Written from the idea of West, Georgiev and Hachisuka 2021 without the paper at hand: the stencil and the three line
  criteria are those of Choudhury and Parker 2009. To be checked against the paper.
- One line style for the scene: no colour or width per object, no line on a change of albedo or texture.
- Emitters are outlined like any other object.
- Surfaces only (built on `path`): no participating media. The lines are unpolarised.

### Files of n and k (`nkfile.cs`)
- Tested on files written from the descriptions of the formats only. To be tried on real Sopra and luxpop files.

### Hazy gloss (`hazygloss.cs`)
- The fixed index (`ior` in the perceptual mode) and the haze of the transmission (`transmissionhaziness`) have no Mueller
  matrix, and work with single scattering and the separate masking only.
- With `scatterdepth` above 1 the peak is no longer exactly that of the core; by how much is not measured.
- Not done: an optional limit on the haziness of a dielectric (or on the index it maps to); two lobes of different
  distributions (a Beckmann core with a GGX or Student-t halo), which the peak ratio as it is computed already allows.
- Not ported from the paper: the Blinn-Phong, Ashikhmin-Shirley and Ward forms, the models it compares with (SGD, ABC), the
  approximation for real-time rendering and compositing (its section 4.3).

### Thin films (`thinfilmlayers.cs`)
- One texture scales the thicknesses of all the layers: no texture per layer, no index that varies over the surface.
- The layers do not absorb (real indices).
- An index of 0 in the substrate gives a radiance that is not a number: there is no guard in the Fresnel term of the films.
  (A tabulated spectrum is 0 outside of its table.)

### Spectra and textures
- No texture of their own: band-stop (the Gaussian texture with a baseline of 1 and a negative scale), short-pass (`longpass`
  with `low` and `high` exchanged), equal energy and regular data.
- The emission of an area light is one spectrum: it cannot vary over the light but by an image.
- `thincoat` and `subsurface` take one index, without dispersion.
- Not done: spectra of lamps, the CIE D series, the normalised Gaussian of Octane.

### Wireframe (`wireframe.cs`)
- After a bounce the widths in pixels follow pbrt's approximate differentials.

### Polarisation
- No test of the Mueller matrices of the sheen, the retroreflection, the hazy gloss and the pearlescent phase function.
- The reflection of a normal map by the random walk depolarises; the analytic model loses contrast on strong maps (0.013
  against 0.071 for the classic normal map), which is believed to be the model and has not been isolated.

### Microfacet normal mapping
- The transmission (after the KAIST CS580 slides) is not reciprocal across the surface and has not been compared with their code.
- The option `normalmapreference` `geometric` is still there; the shading normal is the reference that is wanted.

### Pearlescent phase function
- Trowbridge-Reitz platelets only; a homogeneous medium and `volpath` only; weak with point lights.

### Texture derivatives
- Still by finite differences: float Ptex, cloud, triplanar, shell map, SDF, label, spectrum Ptex.
- `Texture "name" "rgb"` is not parsed; UV channels are not read from PLY files or generated meshes.
- The global bound of the bilinear float texture (reported, unchanged).

## Housekeeping
- The whole test suite has not been run since the changes to the integrators and the materials: only the suites near each
  change were run.

## Facet Forge

Done: the direct port (`util/facetforge.cs`, `facetforge.cs`: materials `facetforge`, `facetcoateddiffuse`), the two fixes of the
fork to `LayeredBxDF.f` that it needs (proportional pdfs in the MIS weights, a failed exit sample), blends of any number of
distributions on every material with a `distribution` (`distributionblend.cs`, `MixtureDistribution`), `LayeredDistribution`
(strata, one scattering in closed form through `DG` / `PDF` / `Sample_wm` of `MicrofacetDistribution`), `distribution` on
`coateddiffuse` and `coatedconductor`, `scatterdepth` on `coateddiffuse`.

Left:
- strata in the walk of Heitz (`multiscatterconductor`, `multiscatterdielectric`, `scatterdepth`): they refuse
  `layering asymmetric`; every reflection on strata is the `facetforge` material only
- facets of their own for each stratum, and two scales (a walk over any facet BxDF), outside `facetforge`
- the rest of the fork's `LayeredBxDF` (the direct transmission term, `ExitJacobian`): `HazyTransmissionBxDF` has its own
  straight term for now
- `facetforge`: its `f` and `PDF` are estimates (one walk per evaluation). It has Mueller matrices
  (`util/facetforge_polarised.cs`), except with `retroreflectivity`; a layered BxDF takes the matrices of a walked interface
  from the interface's own walk, at the cost of walking it twice in a polarised render
- the estimate of the transmission that starts in the rarer medium has a heavy tail (`densestart false`, the order of the fork)
- pearlescent, hazy gloss: the blend syntax is read by pearlescent only with a `distribution`; hazy gloss keeps its own two lobes

## Polygrain

Done: the direct port (`util/micrograin.cs`, `polygrain.cs`) and multiple scattering (`polygrain_walk.cs`, `scatterdepth`): the
position-free walk of Zhou et al. 2025 with the probabilities of the polydisperse model. One deviation from the fork in the
port: a direction sampled from a black bulk is no longer dropped (the grains reflect there).

Since then (polygrain_polarised.cs, `reciprocal`):
- `reciprocal` (default true): f is the mean of the walks from the two directions, exactly reciprocal; Sample_f takes a fifth
  of its samples by the cosine and evaluates the walk from there, so its mean is f. The price: the white furnace is within
  0.01 of 1 (0.990 to 1.008 measured, noise 0.003) where the walk from one direction gives 0.9998 to 1.0000. `reciprocal`
  false is that walk.
- the ray that goes down from a grain: the probability that it reaches the base is now exact in the model (the grains that
  cut the segment), a factor (1 - tau_j)^((h / r_j)^2) for each type on the earlier ratio. Against grains laid out the worst
  difference went from 0.023 to 0.013 - 0.017 (the tracing has a noise of 0.005).
- Mueller matrices: the Fresnel matrix of each mirror reflection on a grain, a depolariser for the diffuse ones, the bulk's own
  matrix. One scattering is the microfacet conductor of pbrt to 3e-7; sampled and evaluated agree.

Left:
- the Mueller matrices of the later scatterings differ between a camera path and the same light path by up to 6 % of f for
  the walk from one direction (the intensity by 2 %); with `reciprocal` the mean is returned. No reference for these elements
  beyond the first scattering.
- what is left against laid-out grains is the position-free step (a ray stopped on its way meets "a grain above" or "below",
  without the masking of that grain)
- no check against laid-out grains for anisotropic grains or several types (the test has one type of spheres)
- the rejection sampling of a grain gives up after 4096 candidates (grazing rays on sparse grains), and the walk ends there

## OpenPBR, Maxwell, MaterialX

Files: `combine.cs` (MixBxDF, StackBxDF, TintedBxDF, AnyBxDF, LobeMatrices), `openpbr.cs`, `maxwell.cs`, `materialx.cs`,
`lobes.cs` (GeneralizedSchlickBxDF, VelvetBxDF), and in `bxdfs.cs` the layered BxDF with a spectral absorbing medium
(`absorption`), a limit on the scatterings in its medium (`mediumOrder`), sampling of its reflection or transmission alone
(a layered BxDF as a layer of another), and its f / pdf pair kept as a ratio along a walk (pbrt's product underflowed in a
thick medium and overflowed on nearly smooth interfaces: the samples of a subsurface slab were 9 % short).

Measured (combine_test.cs, openpbr_test.cs, lobes_test.cs): Beer's law to 3e-4; single scattering of a slab against
Chandrasekhar within 1.5 sigma; OpenPBR defaults = pbrt's coated diffuse; white furnace 0.9998 - 1.0000 with the lobes that
follow every reflection (0.52 - 0.79 with one); coat_color 0.5 seen as 0.4998; subsurface_color 0.2 / 0.5 / 0.8 seen as
0.196 / 0.491 / 0.785; MaterialX standard_surface, gltf_pbr and a BSDF graph against the same lobes built directly.

Since then:
- the inside of the object is a volume of the material: `transmission_depth`, `transmission_scatter`, `subsurface_mode`
  "volume" (OpenPBR), `attenuation` (Maxwell), `Material.SetOwnVolume`
- `reciprocal` on OpenPBR and Maxwell: every stack evaluated from both directions. The earlier note that pbrt's layered walk is
  not reciprocal is withdrawn: over 40000 evaluations of four pairs its two directions agree within 1.4 sigma (worst 2.5 % of
  f, all noise); the chi2 of 2.9 was the heavy tail of the estimate. The option makes each evaluation reciprocal, not the mean.
- the thin film is the Airy sum per microfacet that the paper's appendix E sets out (it was already); under a coat the medium
  above the film is now the coat. Its check passes: a perfect metal under a film of 0.1, 0.4 and 1.5 micrometres returns
  1.0000.
- a specular lobe without a matrix of its own (the thin dielectric, a smooth thin film) keeps its polarisation inside a mix
  or a stack (6e-8 against the lobe alone)
- MaterialX: pattern nodes (materialx_patterns.cs), node definitions of the document, colour spaces srgb_texture and
  g22_rec709, images (tested), the hair BSDF

Left, OpenPBR:
- the volume parameters and the two weights that mix the volume are constants (no textures); dispersion by an Abbe number
- coat_darkening below 1; emission; F82-tint (not wanted); a normal map of the coat alone
- polarisation: the hazy dielectric held in a mix or a stack depolarises

Left, Maxwell: the rotation of the anisotropy, emitters and displacement per layer; `additive` conserves energy only if the
weights do.

Left, MaterialX: no standard library of node definitions (a node is known by its name) and no code generation; pattern nodes
beyond the list in materialx_patterns.cs; images are bilinear without mip maps; colour spaces other than the three; units;
EDF (pbrt-v4 emits through area lights of shapes, not materials), VDF, displacement and light shaders; the tints of the hair
BSDF; looks and material assignments.

Left, Filament: read for its lobes. Its standard model is a GGX lobe with the height-correlated Smith visibility and a
Schlick Fresnel term, a Lambert or Burley diffuse, and an energy compensation 1 + f0 (1 / E - 1) from a table; its clear coat
a second GGX lobe with the Kelemen visibility at a fixed index of 1.5; its cloth the Charlie or the Ashikhmin inverted
Gaussian distribution with the Neubelt visibility, and a wrapped diffuse for the subsurface. On this branch: GGX with either
Smith form, Burley and Charlie were there; the Ashikhmin / Neubelt cloth lobe is new (`VelvetBxDF`, albedo at most 0.67).
Not taken: the Kelemen visibility and the compensation factor (a coat is walked here, and the microsurface walks return
the energy exactly), the wrapped diffuse (not reciprocal).

## Volumes bound to materials (Fong et al., Production Volume Rendering)

Done (productionvolume.cs, cpu/productionpath.cs): the course's interface, `Volume.Integrate()` and `Volume.Transmittance()`
behind a material, and its path tracer, `Integrator "production"`, which knows of no collision. Six integrators (Beer's law,
single scattering, multiple scattering; homogeneous and heterogeneous), chosen by the medium with `"string scattering"`.
The volumes come from the materials and the BoundaryStack that were on the branch; overlaps are the CompositeMedium of the
stack. Against volpath on ten scenes (homogeneous, cloud, grid, behind glass, overlap, nesting, absorption, single scattering
at depth 1) the means of a region agree within 1.8 sigma; three unit tests against closed forms.

Left:
- written from the signatures of the course's listings and from memory of its section 3: the PDF could not be fetched
- OpenPBR and Maxwell interiors behind a dielectric: production is 1 to 2 % above volpath at 400 spp, within 1.4 sigma but of
  one sign in nine channels; not explained
- scalar: no Mueller matrices in "production" (polpath and volpath have them)
- single scattering samples the light alone (no phase-function sampling, no equiangular sampling)
- an aggregate of overlapping volumes is integrated the way of its member of the highest overlap priority; members with
  different integrators are not integrated each its own way
- bdpt and mlt do not use the interface (they keep the shared walk of cpu/pathwalk.cs); the old per-medium tracker and
  transmittance modules are what the heterogeneous integrators call
- emissive volumes as lights, motion blur, and the course's sections on tracking variants beyond what the medium's "tracker"
  and "transmittance" offer

## Polygrain: reciprocity and energy together

Not solved. `reciprocal` true: reciprocal, white furnace within 0.01 of 1. `reciprocal` false: 0.9998 - 1.0000, reciprocal to
2 %.

Why the present walk cannot have both. Its BSDF is defined by its own rules of transition, and those are not the same from
the two ends: the first scattering uses the visibility of the two directions together (one grain that hides both is counted
once, as the paper does), the later ones multiply separate one-sided probabilities; a ray stopped on its way meets "a grain
above" or "below" drawn without the masking of that grain; and each step forgets where the earlier segments of the path went.
Averaging the two directions restores reciprocity but not the energy that the forward rules conserve.

The redesign: trace the grains themselves, generated as the path meets them.
- The model of the paper is a real random geometry: grains of each type scattered over the base as a Poisson process. Its
  distribution of normals and its correlated visibility are exact statements about that geometry (the heights buried by other
  grains, the shadow areas).
- A path knows its own shape: heights, directions and lengths give every vertex its place relative to the first. What it does
  not know is where the other grains are, and those are independent of each other. So the grains along a new segment can be
  drawn when the segment is traced: a Poisson number of centres in the footprint of the segment, placed uniformly, kept if the
  grain meets the segment, and discarded if it would have blocked an earlier segment or buried an earlier vertex (thinning: the
  path so far has shown those places to be empty). The grains already hit are kept and tested as ordinary geometry. The
  nearest hit is the next vertex.
- This is an exact simulation of the model, not a position-free approximation. Every realisation is a real surface, so the
  mean is reciprocal, and with lossless grains and base nothing is lost: both properties by construction, with no option to
  choose between them. It should also close what is left against grains laid out (0.013 - 0.017), since that test becomes the
  same computation on a finite patch.
- Evaluation: the connection of a vertex to the other direction is a shadow ray through the same lazily drawn grains (free or
  not: an unbiased estimate of the conditional visibility). At the first vertex the closed form of the paper is kept, so the
  first scattering stays noise-free and identical to the paper.
- Sampling, the density and the Mueller matrices keep their present form (weights are products of reflectances).

To check before building on it: that the first scattering of the lazy tracer equals the closed form of the paper for several
types and for anisotropic grains (this is the claim that the paper's model is exactly this geometry). Costs: the number of
candidate grains grows as 1 / cos(theta) at grazing, so long segments are traced in pieces; the tests against the earlier
segments are quadratic in the number of scatterings; the shadow rays of the later vertices are noisier than a closed form.
The position-free walk could stay as a faster, approximate option.

## Status after commit 91d8bce (the seven items that were open)

1. Volumes bound to materials: done. `Volume.Integrate()` / `Volume.Transmittance()` after the course's listings, and
   `Integrator "production"`, which knows of no collision or tracker. Beer's law, single and multiple scattering, each
   homogeneous and heterogeneous, by `"string scattering" "multiple" | "single" | "absorption"` on the medium. Against volpath
   on ten scenes every region mean within 1.8 sigma; three unit tests against closed forms. The course PDF could not be
   fetched: the interface follows the signatures of its listings and memory of its section 3. Not in it: polarisation,
   equiangular sampling, overlapping volumes each integrated its own way (the member of the highest overlap priority decides),
   bdpt and mlt.
2. The inside of OpenPBR and Maxwell materials: done. `transmission_depth`, `transmission_scatter`, `subsurface_mode "volume"`,
   Maxwell `attenuation` with `scattering`; the medium is owned by the material. Open: these scenes are 1 - 2 % brighter in
   production than in volpath at 400 spp (within 1.4 sigma, the same sign in eight of nine channels), unexplained. The volume
   parameters are constants, not textures.
3. Reciprocity of stacks: the earlier claim was wrong. pbrt's layered walk is reciprocal in the mean (four pairs, 40000
   evaluations, within 1.4 sigma); the chi2 of 2.9 was the heavy tail of the estimate. `"bool reciprocal"` on OpenPBR and
   Maxwell makes each evaluation reciprocal at twice the cost.
4. Thin film: done. The Airy sum per microfacet is what the paper's appendix E describes; under a coat the medium above the
   film is now the coat. A perfect metal under films of 0.1, 0.4 and 1.5 micrometres returns 1.0000.
5. Polarisation through held lobes: mostly done. The thin dielectric and the smooth thin film keep their matrices in a mix or
   a stack (6e-8 against the lobe alone); the hazy dielectric still depolarises there.
6. MaterialX: further, not the library. About 60 pattern nodes, the document's own node definitions, the colour spaces
   srgb_texture and g22_rec709, the hair BSDF, images (tested). Missing: the standard library of node definitions, code
   generation, mip-mapped images, other colour spaces, EDFs.
7. Polygrain energy with reciprocity: not solved; the redesign is the section above.

Tests that ran and passed at that commit: ProductionVolume 3, Combine 6, OpenPBR 7, Lobes 5, PolyGrain 12, Polarised 24,
FacetForge 18, BxDFCheck 30, BSDF 9, Volume 4, BoundaryStack 7, TextureMedium 5, DistributionBlend 4. The whole suite was not
run.


## Exact polygrain (`Material "exactpolygrain"`, uncommitted after 91d8bce)

The redesign described above, built as a second implementation beside `polygrain`
(`exactpolygrain.cs`, `polygrain_access.cs`, `exactpolygrain_test.cs`, 8 tests).
Grains are drawn lazily along each ray piece and then kept as geometry, so the path
walks one consistent realisation of the Boolean model.

Measured (ExactPolyGrain.* tests):

- first scattering by tracing equals the paper's closed form (worst 3.4 sigma, <= 0.73 % of f)
- white furnace 1.0000 in all 18 cases
- reciprocity of f: worst 2.0 sigma (<= 0.9 % of f), i.e. noise
- against grains laid out and traced: worst 0.0073 (position-free walk 0.0175, one scattering 0.3168)
- Sample_f is exact; f() and PDF() miss one class of paths with pure-mirror grains:
  those that leave by two or more successive mirrors on grains the path already knows.
  Share of the albedo: 0.0012-0.0052 (largest for tau 0.85). The other known-grain
  connections (after a wide scattering; after a mirror on a drawn grain) are found by
  Newton on the normal of the known grain and match their sampled shares
  (0.0150 vs 0.0145, 0.0078 vs 0.0080, 0.0093 vs 0.0094, 0.0160 vs 0.0163).
  Closing it needs chains of known mirrors (nested solves over ordered grain pairs).
- solver recovery of traced reflections: one mirror 14908/14908, drawn-then-known 1886/1895.

Renders (64 spp, sphere region mean, exact relative to `polygrain` reciprocal off):
white +0.4 %, gold on red +0.3..1.3 %, two types +0.8..4.1 %, dense silver +1.6..1.7 %.

Open:

- cost with mirror grains: 187-231 s against 26-31 s for the walk (diffuse grains: 18 s against 26 s)
- no Mueller matrices
- PDF() is one unit walk mixed 0.9/0.1 with a uniform density (as pbrt's layered BxDF);
  without the floor the path integrator produced NaN weights
- known-mirror chains in f()/PDF() as above
- directions must be normalised in double precision before the solves (float-normalised
  wi made the convergence test fail; fixed, worth remembering for other solvers)


### Third attempt: chains of known mirrors by unbiased manifold sampling (`"bool chains"`, default true)

The gap of the second attempt (paths that leave by two or more successive mirrors on
grains the path already knows) is closed in expectation. After the last event with chance
in it (a wide scattering, or a mirror on a drawn grain) such a path is a root of an
equation with no list of roots to enumerate. A trial starts Newton from a random direction
towards a known mirror grain, takes whatever sequence of known mirrors that ray meets
(two or more; for a drawn grain the sequence may return to the drawn grain itself, which
was 3/4 of the share), and solves for that sequence. The root found is weighted by the
number of further trials until the same root is found again, which is an unbiased
estimate of 1 / p (Zeltner, Georgiev, Jakob 2020). `"bool chains" false` is the second attempt.

Measured:

- share of the albedo by such chains, dense mirror grains (tau 0.85), 1.5 M samples:
  by samples 0.00536 +- 0.00006, found by evaluation 0.00534 +- 0.00041
- albedo by samples against by evaluation: 0.4233 / 0.4203 +- 0.0022, 1.0000 / 0.9991 +- 0.0022,
  0.8053 / 0.8097 +- 0.0042, gold on grey 0.6548 / 0.6512 +- 0.0013 (2.3 sigma) and 0.7815 / 0.7806
- density integrates to 0.9959 and 0.9989 (second attempt 0.9907, 0.9965)
- render cost unchanged within 3 % (192 / 115 / 236 s against 187 / 112 / 231 s)
- renders brighter than the second attempt by 0.1-0.45 % (sphere mean), as the share predicts

Open:

- the estimator is heavy tailed: about 13 % of the roots need more than 64 trials, and at
  250 k samples the mean came out 22 % low (0.00424) with an error bar that did not show it;
  only at 1.5 M did it settle. Right in expectation, slow to converge, and a source of rare
  bright values in f(). Better first guesses (aim the reflection off the first grain at the
  second) would shorten the tail.
- trials are capped at 262144 (11 of 54850 roots reached it; their contribution is dropped)
- chains longer than 8 mirrors are dropped


## Opacity as traced presence (uncommitted after e96c794)

How LuxCore does it (read from its source; the history could not be searched in the partial clone):
`Material::GetPassThroughTransparency` compares one random number of the ray with the front or back
transparency texture, and `Scene::Intersect` goes on past the hit when it passes; a separate
`transparency.shadow` lets shadow rays through (LuxCore's own notes call that one biased). Its
`TriangleLight::Illuminate` returns the full radiance with the density 1 / triangle area and never asks
the transparency, while a ray that meets the emitter sees it only when the surface is there: the two
strategies estimate different things.

pbrt-v4 has the same kind of fault for a fractional alpha, measured here before the change: the primitive
hashed the ray and the light hashed the point, so a ray saw an emitter of alpha 0.5 at 0.25 of its
radiance (2.38 against 4.77), while the light sampler gave 0.5.

Now:

- `Material.GetOpacity()` (OpenPBR: `geometry_opacity`, with `"bool opacity_traced"` true by default; false
  is the old pass-through lobe of the BSDF). The scene multiplies it with the alpha of the shape; the
  product is the presence of the surface for camera, scattered and shadow rays alike.
- one test per point (a hash of the point) in the primitive and in the light.
- `DiffuseAreaLight.SetPresence`: the mean presence over the shape (1024 points) enters the power and the
  light sampler; a light given by its power keeps that power.

Measured (renders of the check scenes; tests Opacity.*, 3):

- emitter L 10 with opacity 0.5 against L 5 fully present: seen directly 4.6652 / 4.6652, floor 0.0591 / 0.0592
- power 100 with opacity or alpha 0.25 against fully present: floor 0.1878 / 0.1885, seen 14.90 / 14.85
- floor under a black sheet of opacity 0.5, small area light: expected 0.0510, traced 0.0510, BSDF lobe 0.0590
  (noise or bias not separated); point light: expected 0.0804, traced 0.0811, BSDF lobe 0.0010 (no light at all)

Open:

- a surface that is not thin walled: entry may be present and exit absent, so the interior medium is
  entered and never left (the paper: opacity below 1 only makes strict sense for thin walls)
- shadow rays are stochastic (unbiased, noisy); the paper's transparent-shadow transmittance is not used
- Mix and other wrapping materials do not forward the opacity of their members
- emission of OpenPBR itself is still the area light of the shape
- bdpt, mlt, sppm and the production integrator were not run with it


## Thin-walled mode brought to section 7 of the OpenPBR paper (uncommitted after e96c794)

thinwall.cs (ThinSheetBxDF, TranslucentEonBxDF, LeavingBxDF, OrientedBxDF), thinwall_test.cs (6 tests).

Done:

- the wall is the same from either side (OrientedBxDF: the directions are mirrored when wo is below; the
  paper's approximation, coat and fuzz on the side of wo, the light leaves from the base)
- thin dielectric: the ladder with the absorption inside, a = transmission_color^(1 / cos theta_t); checked
  against light followed bounce by bounce (9 cases, e.g. colour 0.5 at the normal: 0.0492 / 0.4610)
- rough thin dielectric: microfacet lobes; the transmitted one a reflection lobe mirrored below with the
  roughness times (eta - 1) / sqrt(2) (a small-slope derivation of ours; the paper says no model is settled)
- thin subsurface: Eq. 94, the shares by subsurface_scatter_anisotropy, EON lobes of base_diffuse_roughness

Two faults found on the way, both in committed code:

- light that left through the base of a stack kept the 1 / eta^2 of the dielectric interfaces above it
  (pbrt undoes it at the interface on the way out; a thin wall has none): thin-walled subsurface transmitted
  0.296 where 0.666 was due (white paper under a clear gloss: 0.3345 + 0.2954 = 0.63, now 0.3345 + 0.6655)
- LayeredBxDF.f had no term for light that crosses the layers with no event between the interfaces (pbrt's
  own bases do not transmit): transmission by values was 0.105 where the samples gave 0.481. This is not
  only thin walls: any OpenPBR or Maxwell stack over a transmitting base (coated glass) was short in f(),
  so in everything lit by light sampling. Now 0.4799 against 0.4807 and the like (ThinWall.SamplesAgreeWithValues).

Open:

- transmission through a wall with layers is not reciprocal (12.8 % mean, 31 % worst for paper; 16.4 % and
  37 % coated): the light meets the gloss and the coat on the side of wo only. The bare sheet is exact.
- the paper wants the albedo of the thin subsurface unaffected by the inter-reflections under its gloss; here
  they shift light from reflection to transmission (white, g 0: 0.33 / 0.67)
- emission from both sides is not in the material
- the rough glass cases of SamplesAgreeWithValues have error bars of 0.05 to 0.07 (uniform directions on a
  narrow lobe): they do not test much
- LayeredBxDF.PDF was not looked at for the same missing term
- two-sided thin walls (different properties per side) are future work in the paper and not done


### Polarisation of the thin wall

- ThinSheetBxDF has its Mueller matrix: the ladder for each polarisation (they keep to themselves between the
  faces) and the terms that keep the phase between them, with the absorption inside. The scalar sheet is now the
  mean of the two ladders (pbrt's thin dielectric takes the ladder of the mean reflectance; e.g. colour 0.5,
  cosine 0.2: 0.3595 / 0.1870 against 0.3631 / 0.1783 before).
- The light that a rough sheet lets through: the frame at wi is that of the reflection mirrored below the sheet
  (`Attenuation.Scattered` with a normal for each end). A choice that goes with the heuristic lobe.
- LeavingBxDF and OrientedBxDF pass the matrices of what they hold (`BSDF.Mirrored` for the mirrored directions).
  TranslucentEonBxDF depolarises, as every diffuse lobe.
- Nothing of this runs in a scalar render: the matrices are only built by the overloads that the polarised
  integrator calls.

Measured (ThinWall.Polarised): smooth clear sheet against the integrator's matrix for pbrt's thin dielectric,
worst element 2.4e-7 over 4000 samples; sampled against evaluated for the rough coloured sheet (s1 0.01899 /
0.01909), oriented (0.01564 / 0.01574), the OpenPBR wall of rough glass (-0.00887 / -0.00861) and of rough-coated
paper (-0.02396 / -0.02171, 1.2 sigma). polpath renders of three thin-walled scenes: no NaN, intensity equal to
the scalar renders.

Not opt-in (they are corrections, there is no switch back): the straight-through term of LayeredBxDF.f, the
1 / eta^2 of light leaving a thin wall, one presence test per point, the section 7 lobes inside thin-walled mode.
Opt-in or switchable: geometry_thin_walled itself (false by default), "bool opacity_traced" (true by default).

`"string thin_walled_lobes"`: "paper" (default, section 7) or "legacy" (pbrt's thin dielectric with
transmission_color as a tint, an even diffuse sheet that does not hear the anisotropy, the wall not the same from
below). The corrections stay in both: the straight-through term and the radiance factor of light that leaves through
the base (which, for the legacy wall seen from below, is the inverse: before, that side gained energy).
ThinWall.TheLobesBefore: legacy transmitted 0.4815 and 0.4830 with anisotropy 0.6, the paper's lobes 0.6550.
