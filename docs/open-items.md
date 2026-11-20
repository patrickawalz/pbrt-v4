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
- Still by finite differences: float and spectrum Ptex, cloud, triplanar, shell map, SDF, SDF label, the wireframe textures,
  the MaterialX pattern textures, and of the spectrum textures `variation` and `strand colour` (differences in RGB). Images
  have a closed form only where the filter of the mip-map gives a slope, the spectrum grid unless it is made of nodes, OSL
  textures where the shader has a derivative group. (The header of texture_derivatives.cs is out of date on images and OSL.)
- `"string derivatives" "world"` on any float texture (uncommitted after 6655c0b): the differences are taken along the
  three axes of space, the point alone moved, and the gradient laid on the two directions of the surface; the step is the
  size of the pixel on the surface or `"float derivativestep"` (LuxCore's fixed distance). Default "surface", unchanged.
  Meant for the textures that are functions of the point (cloud, triplanar, SDF, SDF label, shell map). Forward
  differences, three evaluations. TextureDerivatives.WorldAxes: against a closed form 6.5e-4 with a step of 1e-3; on a
  parameterisation 2000 times too long with no footprint of texture coordinates 0.028 of the slope where the surface
  differences are off by 3.95; fBm against its closed form 0.039 of 4.31. Float textures only; not tried in a render on
  the five textures it is meant for (the render check is fBm on a sphere, where all modes agree).
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


## Production volumes after the course PDF: the missing volume types and the aggregate (uncommitted after 6655c0b)

The course PDF is now at hand (it was not when productionvolume.cs was written). Against its listings:

| Course | Here |
|---|---|
| Listing 1, Volume::Integrate / Transmittance | productionvolume.cs, as before |
| 2, EmptyVolume | new: `"string scattering" "none"` |
| 3, BeersLawVolume; 8, BeersLawHeterogeneousVolume | as before (the heterogeneous one by ratio tracking, not the 0 / 1 estimate of listing 8) |
| 10, 11, SingleScatter...Volume | as before; the light is sampled alone, the phase function is not (the course combines both) |
| 13, SingleScatterPDFVolume | new: `"string scattering" "singlepdf"`, `"float stepsize"` (0.1): a density marched along the ray from the transmittance, the scattering coefficient and the light without its shadow ray; transmittance and emission by quadrature. Biased by the step, as the course says |
| 14, 15, MultiScatter...Volume | as before |
| 16, emission | as before in the tracking volumes; by quadrature in the new one |
| 17, overlapping volumes | as before: the CompositeMedium of the stack, the collision given to one volume |
| 18, equiangular sampling with MIS | not done |
| 19, ratio tracking | the transmittance estimators of the media |
| 20, bounded subintervals from a k-d tree | pbrt's majorant grids |
| 24 - 26, the volume aggregate | new: volumeaggregate.cs |

The aggregate (section 6): `MakeNamedMedium "sky" "string type" "aggregate" "string members" [ "a" "b" ] "float threshold" 2
"integer maxdepth" 6`. An octree over the region of the members; each node lists the volumes that overlap it with the largest
density of each; divided while (max - min) diag > threshold or while larger than a volume it holds; the iterator of listing
24. In "production" it is a volume that integrates itself (AggregateVolume, the tracking of listing 26, the collision given
to one member by its scattering coefficient, ratio tracking for the shadow rays); the other integrators see the sum of the
members, each within its bounds. The members need no geometry and must have an extent of their own (grid, rgbgrid, nanovdb,
cloud, texture) and be declared before the aggregate. Media that a camera is in are named before WorldBegin and declared
after it (before it they would be placed in camera space).

Measured (VolumeAggregate.*, 4; ProductionVolume.*, 5): 29668 points on 4000 rays, none with an extinction above the majorant
of its interval, no gap between intervals; transmittance against quadrature 0.03236 / 0.03147, 0.04534 / 0.04482, 0.53115 /
0.53026 (errors 0.0004 to 0.0018); an aggregate of one volume against the volume by itself: scattered 0.67413 / 0.67507, to
the wall 0.14920 / 0.14870; three volumes against their sum tracked with pbrt's majorants: 0.77682 / 0.77710 and 0.03167 /
0.03265. The marched density against the closed form of a slab: 0.03166 +- 0.00003 / 0.03172 (1.8 sigma).
Render (three procedural clouds, 64 spp): cloud region 0.2994 (aggregate, production), 0.3000 (aggregate, volpath), 0.2997 and
0.2989 (three bounded volumes); 64, 97, 59 and 63 s.

Open:

- no gain in time in that render: the cloud medium has no grid of majorants, so every node is told the same density; and the
  extinction is 0.19 of the majorant in the mean with the 16^3 grids of the grid media. The minimum over a node is not known
  (0), so the heuristic is max * diag > threshold
- built once when the scene is made, not on the fly; no filter widths (pbrt's media are point sampled); animated members use
  their start transformation
- the aggregate is for multiple scattering only; it does not look at the "scattering" of its members
- equiangular sampling (listing 18), the phase-function half of the single-scattering MIS, contrast control (section 3.7),
  motion blur (section 5)
- no Mueller matrices in "production"
- bdpt, mlt and sppm were not run with an aggregate

## Production volumes: section 4 of the course (sampling), emission and contrast (uncommitted after 6655c0b)

All of it is chosen by parameters of the medium; the defaults are what was there before.

| Parameter | Values | What |
|---|---|---|
| `"string distancesampling"` | `"density"` (default), `"equiangular"`, `"mis"` | Where along the ray a single scattering volume gathers light (4.2). `EquiangularDensity` about a point of a light, which the renderer gives (`VolumeServices.GenerateLightPoint`: the light sampler's choice for the start of the ray, a point of it by `SampleLi`; none for lights at infinity). |
| `"string lightsampling"` | `"light"` (default), `"mis"` | The light at the point from the lights, or from the lights and the phase function with the power heuristic (listings 10, 11: `Volume.DirectLighting`, `VolumeServices.EvaluateLightSample`). |
| `"integer lightsamples"` | 1 | Points lit along one interval (4.1). The march of `singlepdf` is made once for all of them. |
| `"float contrast"` | 1 | Contrast control (3.7): the shadow rays of a multiple scattering volume use sigma_a + contrast^depth sigma_s. |

- Homogeneous: one of the two densities is drawn with probability 1/2 under `"mis"`, weight 1 / (mean of the two); the densities
  of the other wavelengths enter `r` as before (the equiangular one is the same for all).
- Marched (`singlepdf`): the equiangular point takes its transmittance from the march (4.2.4, decoupled ray marching).
- Tracked: `"equiangular"` is T~ sigma_s L_s / p_o with T~ a ratio-tracking estimate. `"mis"` takes both points, the course's
  listing 18 with its first weight corrected: sigma_s L_s sigma_t / (sigma_t + p_o) at the tracked point (per unit of its
  density) and T~ sigma_s L_s / (sigma_t + p_o) at the other. The sigma_t of the weights is the mean over the wavelengths: with
  the sigma_t of the wavelength that tracked, the weights depended on it and the result was biased for coloured media
  (0.0804 against 0.0785; found by the test). Without a light point, `"mis"` uses the uniform density (4.2.1).
- `Volume.Emission(rs, P0, P1)` (3.8) on every volume, the aggregate included. It is called where it adds something that is
  not counted already: along the direction that `"lightsampling" "mis"` samples from the phase function of a single
  scattering volume. Paths gather the emission of the volumes they cross in `Integrate()`; adding `Emission()` to the shadow
  rays, as the course suggests, would count it twice here.
- `Material.MakeHomogeneousVolume` accepts `"singlepdf"` and `"none"`.
- `singlepdf` in a homogeneous medium marches a ray without end as far as the transmittance stays above 1e-4 (it scattered
  nothing along such rays before: 0.113 against 0.157 in the fog render).

Tests (`productionsampling_test.cs`, 5, all pass): the density integrates to one in 7 configurations; point light in fog
against quadrature (0.078532) for the three volume kinds and three modes; deviation of one sample homogeneous 0.129 / 0.020 /
0.054 (density / equiangular / mis), tracked 0.139 / 0.090 / 0.072; 8 points lit; ray without end; tracking with uniform
against the slab's closed form (thin slab: deviation 0.0194 -> 0.0040); lights and phase function under a sky with g = 0.85
(0.20449 +- 0.00024 against 0.20432; deviation 1.26 -> 0.15); emission and contrast against closed forms.

Renders (`/renders/pbr/equi`, 16 spp, independent sampler; RMSE of the fog around the point light / the sphere light against a
reference):

| | density | equiangular | mis |
|---|---|---|---|
| homogeneous, single | 0.125 / 0.192 | 0.070 / 0.079 | 0.081 / 0.101 |
| homogeneous, singlepdf | 0.057 / 0.062 | 0.069 / 0.092 | 0.059 / 0.034 |
| cloud, single (tracked) | 0.152 / 0.289 | 0.128 / 0.309 | 0.085 / 0.173 |
| cloud, singlepdf | 0.051 / 0.109 | 0.102 / 0.177 | 0.062 / 0.123 |

Equiangular alone gains little in a tracked volume: its transmittance is a ratio-tracking estimate (use `"mis"`, or
`"transmittance" "residual"`). The marched density already contains the unshadowed light, so equiangular adds little there.

Not done from the course:
- 4.2.5: the stochastic choice between shading the surface and the volume; transparent surfaces folded into one CDF.
- 4.3: one CDF over all the segments of a path.
- 4.4.2 / 6: the aggregate still scatters any number of times only (no single scattering, equiangular or contrast in it).
- 5: motion blur of volumes (media are sampled at the time of the ray; nothing of sections 5.2 to 5.6 is implemented).
- 7: Hyperion's light cache points and in-scattering PDF.
- Emissive volumes are not sampled as lights (3.8, last paragraph).
- `EvaluateLightSample` reaches area lights only on surfaces that stop the ray; an emitter on an interface surface is skipped.

Found on the way: a camera medium declared after `WorldBegin` is looked up while the scene is still being read, and the render
fails or not by timing ("fog: medium is not defined"); declared before the camera it is placed without the camera's
transformation, which is right only for media without a position. Bounded media belong on a shape.

## Unbiased ray-marching transmittance (Kettunen et al. 2021): `"string transmittance" "raymarching"` (uncommitted after 6655c0b)

`RayMarchingEstimator` in `volumeintegrator.cs`, a `TransmittanceEstimator` beside `ratio`, `analytic`, `tracklength` and
`residual`; algorithm 5 of the paper (combed estimates of the optical depth with endpoint matching from 6 lookups, the
aggressive Bhanot-Kennedy roulette with p_Z = 0.9 and K = c = 2, every estimate the pivot in its turn through the elementary
symmetric means, the tuple size of algorithm 4 from the optical depth of the majorant along the ray). The biased variant
(algorithm 6) is not implemented. It runs through `TryEstimate`, so it serves `Volume.Track()` (the shadow rays of every
heterogeneous volume; in `SingleScatterHeterogeneousVolume` also the transmittance of the equiangular point and of the beam)
and the shadow rays of volpath. Not the aggregate (its own ratio tracking), not the shadow rays with `contrast` below 1.

- Every wavelength is estimated from the same lookups by its own series: the path probabilities are untouched.
- An estimate is not bound to [0, 1]; pixels can come out slightly negative (-0.044 at 16 spp in the cloud render).
- A homogeneous medium's ray without end gives 0; M is capped at 65536, the order at 24.

Test (`ProductionSampling.RayMarchingTransmittance`): a grid that varies, three rays, extinction x 2 / 12 / 60, every
wavelength against exp(-tau) by quadrature. Deviation at tau = 3.0: 0.00034 against 0.159 for ratio tracking, at 20.5
lookups against about 8.5; at tau = 1.5: 0.0013 against 0.334 (13.9 against 4.4 lookups). The equiangular point of the
tracked volume in the fog test: deviation 0.0905 -> 0.0198, the homogeneous volume's figure.

Cloud render (16 spp; RMSE of the fog left / right, time):

| | density | equiangular | mis |
|---|---|---|---|
| ratio | 0.152 / 0.289, 11.8 s | 0.128 / 0.309, 12.7 s | 0.085 / 0.173, 15.8 s |
| raymarching | 0.148 / 0.271, 29.7 s | 0.099 / 0.188, 33.2 s | 0.078 / 0.135, 40.3 s |

At equal time this is about even in that scene: the cloud medium has one majorant for its whole box, the tuple size follows
it, and the render is 2.6 times slower. A medium with a majorant grid (or a minorant, which the paper recommends subtracting
and which media do not give) would do better.

A flaw of the test doubles found on the way: `Volume.Track()` copies `rs.rng` and then asks `rs.GenerateRandomNumber()`;
services that draw that number from the same generator hand the tracker its first number twice (ratio tracking then read
0.2353 against 0.2169). The path integrator draws it from the sampler and is not affected; the doubles of
`productionsampling_test.cs` have their own generator now.

## Spectral and decomposition tracking (Kutz et al. 2017), beside what was there (uncommitted after 6655c0b)

Two switches of the medium; the defaults are unchanged.

- `"string tracker" "decomposition"` (`DecompositionTracker`, `volumeintegrator.cs`). The control component of a majorant
  segment is the smallest extinction there (`RayMajorantSegment.controlFraction`, from `MajorantGrid.minima`, which
  `GridMedium` fills with `SampledGrid.MinValue`). A collision of the majorant is the control component's with probability
  mu_c / mu_bar and is then made without a lookup (`Medium.ControlProperties`: the coefficients at density 1); otherwise it
  is a collision of the residual with the majorant mu_bar - mu_c. Same distribution as the paper's algorithm 2 (the
  superposition of two Poisson processes instead of the minimum of two distances). Every consumer of a tracker gets it
  (the production volumes, volpath's walks); transmittance walks use `ForTransmittance()`, the plain tracker. Only
  `GridMedium` without emission says what its control component is; any other medium is tracked as by `"majorant"`
  (extended to the aggregate, texture and NanoVDB media in the next section).
- `"string spectral" "tracking"` (`MultiScatterHeterogeneousVolume.IntegrateSpectral`): algorithm 4 with the majorant of the
  largest wavelength, the history-aware average probabilities of 5.1.2 and the reduced termination of 5.1.3 (no absorption
  event; emission by its expected value at every collision). The probabilities need the throughput of the path
  (`VolumeContext.throughput`, set by the path tracer): with the weight of the current ray alone a path's value reached 415.
  (Combined with decomposition, and in the single scattering volume and the aggregate, in the next section.)

Test `ProductionSampling.TrackersSideBySide` (walks into a sphere of smoke on a grid, 200000 each; what leaves unscattered
agrees with exp(-tau) for all three, what leaves at all agrees between them):

| smoke | | leaves | deviation | lookups a walk |
|---|---|---|---|---|
| grey, floor of density | delta | 0.6022 | 0.489 | 4.77 |
| | decomposition | 0.6036 | 0.489 | 0.81 |
| | spectral | 0.6030 | 0.248 | 7.48 |
| coloured, floor | delta | 0.4022 | 0.490 | 3.80 |
| | decomposition | 0.3999 | 0.490 | 0.64 |
| | spectral | 0.4013 | 0.249 | 28.8 |
| coloured, no floor | delta | 0.4646 | 0.499 | 4.77 |
| | decomposition | 0.4655 | 0.499 | 3.17 |
| | spectral | 0.4647 | 0.258 | 31.0 |

Render (`/renders/pbr/kutz`, coloured smoke with a floor on a 32^3 grid, 64 spp, RMSE of the ball against 1024 spp):
delta 0.0753 in 20.7 s; decomposition 0.0758 in 20.3 s; spectral 0.0677 in 27.4 s (RMSE^2 x time 0.117 / 0.117 / 0.126).
Decomposition saves five lookups in six and no time: a lookup of a small grid in memory costs little next to the rest of a
collision. Spectral tracking halves the deviation of a walk (it does not end walks by absorption) for 1.6 times the
lookups in grey smoke and 7 to 8 times in coloured smoke, where its majorant is that of the densest wavelength.

## Kutz et al. 2017: the rest of the paper (uncommitted after 6655c0b)

- **Spectral tracking is one walk**, `Volume.SpectralWalk`, used by `MultiScatterHeterogeneousVolume` and by
  `SingleScatterHeterogeneousVolume` (the tracked point; with `"distancesampling" "mis"` it is weighed against the other
  point as before). Options: `"string spectralprobabilities" "average"` (5.1.2, default) or `"maximum"` (5.1.1);
  `"bool spectralabsorption"` (absorption as an event that ends the walk, in the place of the reduced termination of 5.1.3).
  A majorant that is exceeded gives a negative null coefficient, which the weights carry with its sign (section 3.4).
- **Spectral with decomposition** (5.2; weighted decomposition tracking, algorithm 3, by wavelength): `"spectral" "tracking"`
  with `"tracker" "decomposition"`. The control component is the smallest density of the segment with the smallest absorption
  and scattering coefficients over the wavelengths; its collisions need no lookup and have the weight one.
- **Aggregate**: the nodes have the smallest density of each volume that wholly covers them (`AggregateMember.MinDensity`,
  `Iterator.MinDensity`), so `minExtinction` is no longer 0 (it also enters the subdivision heuristic, as the course has
  it). `"tracker" "decomposition"` on the aggregate: a control collision belongs to one volume by its share and looks up
  none. `"spectral" "tracking"` on the aggregate: the events are the scattering by each volume and the null collision; with
  both, the control component is achromatic as above.
- **Other media**: `TextureMedium` (minima from the texture's `RegionBounds` per voxel of the majorant grid; the voxels
  that the hierarchy refines have no control component inside the finer grids) and `NanoVDBMedium` (minimum of the voxels
  that a cell of the majorant grid reaches, zero where it reaches beyond the grid) give `ControlProperties` when they emit
  nothing. **The NanoVDB part is untested**: there is no fog volume among the assets here. `CloudMedium`: its density is
  clamped at zero wherever the noise is negative and has one majorant for its box, so there is no control component to
  take; `RGBGridMedium`: the coefficients vary by voxel and by wavelength, not by a density, and it is left as it is.

Tests (`productionsampling_test.cs`, now 9, all pass). `TrackersSideBySide`: six settings on four grids;
`TrackersInAggregateAndTexture`; `SpectralSingleScattering` (0.078532 by quadrature, all within error). Coloured smoke with
a floor of density (what leaves / deviation / grid lookups a walk):

| | leaves | deviation | lookups |
|---|---|---|---|
| delta, one wavelength samples | 0.4022 | 0.490 | 3.80 |
| decomposition | 0.3999 | 0.490 | 0.64 |
| spectral (average, reduced termination) | 0.4013 | 0.249 | 28.9 |
| spectral, absorption events | 0.3999 | 0.490 | 12.4 |
| spectral with decomposition | 0.4008 | 0.347 | 17.9 |
| spectral (maximum) | 0.351 +- 0.015 | 6.6 (largest value 2010) | 31.1 |

The maximum scheme does not follow the throughput: in a strongly coloured medium a walk that leaves has a probability of
the order of 2^-collisions and a weight to match. It is measured and not held to the others there; it is held to them in the
grey and the faintly coloured smoke (deviation 0.310 against 0.255 for the average). Aggregate of two grids: 7.72 lookups a
walk with delta, 2.50 with decomposition; spectral 42.4, with decomposition 26.2.

Renders (64 spp, RMSE of the ball, time, RMSE^2 x time):

| | grid | aggregate of two grids |
|---|---|---|
| delta | 0.0753, 19.9 s, 0.113 | 0.0817, 26.0 s, 0.174 |
| decomposition | 0.0758, 19.8 s, 0.114 | 0.0820, 26.5 s, 0.178 |
| spectral | 0.0677, 28.5 s, 0.131 | 0.0697, 37.3 s, 0.181 |
| spectral with decomposition | 0.0787, 26.5 s, 0.164 | 0.0773, 37.4 s, 0.223 |
| spectral, absorption events | 0.0836, 21.2 s, 0.148 | |
| spectral, maximum | 0.2567, 24.3 s, 1.60 | |

None of the new settings beats delta tracking with one sampling wavelength at equal time in these scenes (small grids in
memory, where a lookup is cheap).

## Kutz et al. 2017 against procedural media (uncommitted after 6655c0b)

`/renders/pbr/procedural`: a sphere of coloured medium, multiple scattering, 64 spp, independent sampler; RMSE of the ball
against a reference of 384 spp (192 for the fbm), time, RMSE^2 x time.

| medium | delta (what was there) | decomposition | spectral | spectral with decomposition |
|---|---|---|---|---|
| texture, 0.6 + 0.6 turbulence (8 octaves): a floor | 0.0798, 30.2 s, 0.192 | 0.0790, 29.0 s, 0.181 | 0.0656, 54.3 s, 0.234 | 0.0704, 51.1 s, 0.253 |
| texture, 0.25 + 1.5 fbm (8 octaves), clamped at zero: no floor | 0.0940, 60.6 s, 0.535 | 0.0940, 60.2 s, 0.532 | 0.0745, 110.0 s, 0.610 | 0.0745, 114.3 s, 0.634 |
| cloud | 0.0828, 27.3 s, 0.187 | 0.0828, 26.4 s, 0.181 | 0.0753, 56.4 s, 0.320 | 0.0753, 54.3 s, 0.308 |

- Decomposition: 4% less time and the same error where there is a floor (6% better in RMSE^2 x time, within what one render
  can tell). The control component is small here: the majorant comes from the bound of the turbulence, which is far above
  the values the texture takes, so most collisions are null collisions of the residual and are looked up all the same.
  Without a floor, and in the cloud (which gives no control component), the image is the same as delta tracking's.
- Spectral tracking: 10 to 20% less error for twice the time; behind delta tracking at equal time in all three (its majorant
  is that of the densest wavelength and its walks are not ended by absorption, so it evaluates the density about twice as
  often, and the evaluation is what costs in a procedural medium).
- Test: the texture medium of `TrackersInAggregateAndTexture` counts the evaluations of its density: 7.44 a walk with delta,
  6.77 with decomposition (a floor of 0.1 under a majorant of 1.1); spectral 34.6, with decomposition 32.7.

What would make decomposition pay in a procedural medium is a tighter majorant and minorant by region (the region bounds of
the noise textures: the improvement proposed earlier and not done), not the tracker.

## Transmittance estimators: next flight, local residual, MIS, power series (uncommitted after 6655c0b)

All `TransmittanceEstimator`s chosen by `"string transmittance"`; the default (`"ratio"`) is unchanged. All unbiased. All use
one majorant for every wavelength (the largest) with weights by wavelength, and leave the path probabilities alone.

| name | source | what |
|---|---|---|
| `nextflight`, `nextflightratio` | Cramer 1978; Novak et al. 2018, eq. 51, 52 | the flight through the majorant from the start and from every tentative collision; the first ends its walk as delta tracking does |
| `unidirectional` | Georgiev et al. 2019, 5.4 (reference code) | ratio tracking and the next flights of one walk, by the balance heuristic |
| `bidirectional` | the same | a walk from each end, every connection of the two and each walk alone, by the balance heuristic (`MajorantProfile`: the ray in the depth of its majorant) |
| `pseriescumulative`, `pseriescmf` | Georgiev et al. 2019, 5.2 (reference code) | prefix sums of the series of exp(tau_n), lookups in proportion to the majorant; left to ratio tracking beyond a majorant depth of 600 |
| `residuallocal` | Novak et al. 2014, 3.3, 5 | residual ratio tracking with a control for every voxel: the mean density of the voxel (`MajorantGrid.averages`, `RayMajorantSegment.averageFraction`; the middle of the bounds for a texture medium) |
| `residualtracking` | Novak et al. 2014, 3.2 | the binary form, with the smallest density of the voxel as the control |

Grid, NanoVDB and texture media give the controls (NanoVDB untested: no fog volume here); for any other medium the two
residual estimators are ratio tracking and the track-length estimator. `"residual"` (one control for the medium) is as it was.

Test `ProductionSampling.TransmittanceEstimators` (a grid ball at three densities and a grid of one density; every wavelength
against exp(-tau); all pass). Ball, tau = 2.95 (deviation, lookups, deviation^2 x lookups):
ratio 0.146, 4.3, 9.3e-2; tracklength 0.222, 1.5, 7.2e-2; nextflight 0.127, 4.3, 7.0e-2; nextflightratio 0.070, 9.3, 4.5e-2;
unidirectional 0.063, 9.3, 3.7e-2; residualtracking 0.058, 3.9, 1.3e-2; bidirectional 0.0152, 18.6, 4.3e-3;
pseriescumulative 0.0152, 19.1, 4.4e-3; pseriescmf 0.0152, 18.9, 4.4e-3; residuallocal 0.0115, 3.2, 4.2e-4;
raymarching 0.00017, 20.5, 5.9e-7. In the grid of one density the two residual estimators are exact without a lookup.

Render, coloured smoke on a grid, multiple scattering, 64 spp (RMSE of the ball, time, RMSE^2 x time): ratio 0.0753, 19.9 s,
0.113; residualtracking 0.0670, 19.8, 0.089; residuallocal 0.0663, 20.5, 0.090; nextflight 0.0697, 19.6, 0.095;
nextflightratio 0.0679, 21.2, 0.098; pseriescmf 0.0666, 22.0, 0.098; raymarching 0.0663, 22.4, 0.099; unidirectional 0.0674,
21.7, 0.099; pseriescumulative 0.0664, 22.6, 0.100; bidirectional 0.0673, 25.2, 0.114. On the three procedural scenes
(texture and cloud media, loose majorants) the next-flight pair and ray marching lowered the error by a few percent and cost
more time than they saved (ratio tracking 0.192 / 0.535 / 0.187 in RMSE^2 x time; nextflight 0.208 / 0.574 / 0.206).

## Double scattering in the single scattering volumes, joint importance sampling (uncommitted after 6655c0b)

`"string doublescattering" "none" | "plain" | "joint" | "mis"` (`Volume.DoubleScattering`; Georgiev et al. 2013 as in Novak et
al. 2018, 6.4, the closed forms of the isotropic case; the tables for other phase functions are not made, the phase
functions are in the weight). New services: `ChooseLight` (replaces `GenerateLightPoint`), `Connect`, `EvaluateLight`;
`VolumeContext.mayScatterTwice`. Test `ProductionSampling.DoubleScattering`: plain 0.022786 +- 0.000398, joint 0.022615 +-
0.000050, mis 0.022589 +- 0.000062; deviation 0.502 / 0.025 / 0.034.

Renders, two scatterings at most, 16 spp (RMSE, time): lights inside a homogeneous fog: path tracing 0.125, 5.1 s; plain
0.166, 6.5 s; joint 0.031, 6.4 s. Lights inside a cloud: 0.211, 9.5 s; 0.391, 17.4 s; 0.159, 15.5 s. With the lights outside
the medium (the three procedural scenes) "joint" alone is worse than "plain" (it puts the second point near the light, where
there is no medium); "mis" was added for that and rendered, but **its error there is not measured**: the references that
match what a single scattering volume gathers (no light that a surface scatters into the volume) were not rendered.

## The cloud medium as a texture: `Texture "name" "float" "cloud"` (`cloudmediumtexture.cs`, uncommitted after 6655c0b)

`CloudMediumTexture`: the density of `CloudMedium` (same parameters: "density", "wispiness", "frequency", "edgefalloff",
"uniform") with `RegionBounds` over a box: the altitude terms exactly, each octave of the noise by `pbrt.NoiseBounds` over the
box of its lookup points, grown by the displacement that the wispiness can give (`NoiseSlopeBound` 8.5, the largest slope of
the noise, derived in the source). A "texture" medium of it is the cloud with a majorant grid, minima and a control per
voxel, so decomposition tracking and the residual estimators work in it; `CloudMedium` itself is unchanged.

Test `ProductionSampling.CloudMediumTexture` (four settings, unit box): the density equals the cloud medium's at 20000 points
(difference 0); 0 of 240000 values outside the bounds of 1200 boxes; 43 to 47% of the boxes have a smallest density above
zero (the lower half of the cloud, where the second term of the density is); majorant depth along rays 1.56 to 1.95 against
the cloud medium's 1.94 to 1.95, of which 0.88 to 1.16 is the control. The majorant itself is only a little tighter: with
wispiness the box of the lookup points grows by 0.64 and only the first two octaves keep a bound below 1.

Render, the procedural cloud scene, 64 spp (RMSE of the ball, time, RMSE^2 x time): cloud medium 0.0828, 27.3 s, 0.187;
texture medium of the cloud texture: ratio 0.0853, 29.4, 0.214; residuallocal 0.0764, 28.1, 0.164; residualtracking 0.0758,
29.7, 0.171; tracker decomposition 0.0825, 24.3, 0.165; decomposition with residuallocal 0.0767, 26.0, 0.153. (That scene's
box is from -1 to 1 in height, so its lower part is the dense layer of the cloud.)

## A mean for textures: `FloatTexture.RegionMean` (uncommitted after 6655c0b)

`FloatTexture.RegionMean(Bounds3f region)`: the mean of the texture over a region, by default from the values at the centres
of a 4 x 4 x 4 division of it (64 evaluations, once per voxel when the medium is made); `FloatConstantTexture` overrides it
with its value. `TextureMedium` takes it (as a density, clamped between the smallest and the largest of the voxel) for
`MajorantGrid.averages`, the control of `"transmittance" "residuallocal"`. `"string control" "middle"` on a texture medium
gives the middle of the bounds back (`TextureMedium.UseMiddleControl`), which is what was there before.

Measured in `ProductionSampling.CloudMediumTexture`, residual ratio tracking through the cloud texture, four settings (the
same transmittance with both; deviation, lookups, deviation^2 x lookups):
mean 0.069, 6.8, 0.032 / middle 0.379, 3.9, 0.56; mean 0.058, 5.6, 0.019 / middle 0.630, 3.2, 1.25;
mean 0.106, 7.1, 0.080 / middle 1.72, 3.9, 11.5; mean 0.128, 6.0, 0.099 / middle 0.558, 3.5, 1.08.
The middle of the bounds leaves the smallest majorant for the residual and so the fewest lookups, but where the bounds are
loose the densities are far from it and the weights far from one; the mean costs 1.7 times the lookups and is 11 to 140
times better by deviation^2 x lookups. Render of the cloud scene (RMSE^2 x time): residuallocal 0.164 with either control
(0.0750 in 29.2 s with the mean, 0.0764 in 28.1 s with the middle); decomposition with residuallocal 0.143 with the mean
(0.0753, 25.2 s) against 0.153 with the middle.

## Equiangular sampling beside next-event estimation in the multiple scattering volumes (uncommitted after 6655c0b)

Miller, Georgiev and Jarosz 2019, section 5.2 / figure 7. `"string distancesampling" "mis" | "equiangular"` now also acts on
`MultiScatterHomogeneousVolume` and `MultiScatterHeterogeneousVolume` (default `"density"`: as before). On every ray the
volume also lights a point drawn from the equiangular density about a point of a light, from that light
(`Volume.EquiangularShare`); the collision that it returns carries what weighs the next-event estimation there
(`VolumeGeometry.direct`, `VolumeDirectWeight`), which the path tracer applies per light in `SampleLd` and to an area light
that the next ray hits. Weights: the balance heuristic over the path with its null collisions, mu_t W / (mu_t W + p_ea)
and p_ea / (mu_t W + p_ea), W the product of mu_n / mu_bar before the point (the transmittance itself in a homogeneous
volume), mu_t W as its mean over the wavelengths; with the decomposition tracker W is left out. Not in the aggregate, not
with `"spectral" "tracking"`.

- `ChooseLight` now takes the sampler of the lights half of the time and any light alike the other half (the light serves
  points all along the ray, and what is gathered about it is divided by the probability of the choice). This also applies
  to the single scattering volumes' equiangular point and to the joint importance sampling.
- Fixed: `VolumeIntegrator.Create` returned the shared default settings for a multiple scattering medium and dropped
  `"distancesampling"`.

Test `ProductionSampling.EquiangularBesideNextEvent` (light scattered once, quadrature 0.078532; deviation of one sample):
homogeneous density 0.139, equiangular 0.020, mis 0.032; tracked 0.167 / 0.090 / 0.080; the same with the decomposition
tracker. All within error.

Renders, lights inside the medium, 10 bounces, 16 spp (RMSE against 256 spp of next-event estimation alone, in brackets
without the 20 most deviant pixels; time): homogeneous fog: next-event alone 0.183 (0.138), 10.4 s; mis 0.145 (0.093), 20.8 s;
equiangular 2.56 (0.091), 22.0 s. Cloud: 0.305 (0.273), 9.8 s; mis 0.217 (0.208), 17.3 s; equiangular 0.279 (0.264), 17.2 s.
Lower error for about twice the time: about even at equal time in the fog, about 10% better in the cloud. "equiangular"
alone gave one pixel of 800 in the fog: about a point light the equiangular density leaves 1 / D (D the distance of the ray
from the light), whose variance does not converge, and without the collision's share nothing bounds it.

## Emission of volumes: sampled as a light everywhere, emission majorant, residual emission tracking (uncommitted after 6655c0b)

Villemin and Hery 2013 (Pixar memo 13-02), Simon et al. 2017 (line integration, forward next event estimation), Huang et al.
2021 (emission and scattering importance sampling, with the talk notes). What was there before: emission at every collision,
emission ratio tracking (`Volume.TrackEmission`), and `VolumeEmissionLight` (point and forward sampling) for `TextureMedium`
in volpath, BDPT and MLT only.

1. **The light for every emissive medium.** `VolumeEmissionLight` takes any `Medium` that answers `Medium.EmissionRegion`
   (default: its `AggregateRegion`) and `Medium.EmissionBoundUnit(box of the unit cube)`: `GridMedium`, `RGBGridMedium`,
   `NanoVDBMedium`, `TextureMedium` and `AggregateMedium` (one light for the aggregate: the sum of its members' bounds over
   the box of the members). `"bool emission_nee"`, `"bool emission_fnee"`, `"integer emission_nee_res"` are read for all of
   them (`Medium.CreateEmissionSampling`), the registry of the lights is on `Medium` (`EnableEmissionSampling`,
   `AddEmissionLights`, `EmissionLights`), `Medium.MajorantAt` has a default (the first segment of a ray from the point).
   volpath, BDPT and MLT pick the light up for these media as they did for the texture medium.
2. **The production path tracer.** Every emission light is sampled along lines, whatever `"emission_fnee"` says
   (`ProductionPathIntegrator.SampleEmission`): at every surface and volume point of the path, and at the points that the
   single scattering volumes light themselves (`VolumeServices.SampleEmission`, called from `Volume.DirectLighting`), a
   direction is drawn towards the emission and `Volume.EmissionOver` gathers along the ray, which stops once it has left the
   box of the emitting voxels. The emission that the path gathers on its own ray is the other strategy: every
   `Volume.Integrate` multiplies it by `VolumeContext.emissionWeight`, the balance heuristic in the density of directions
   (p_b against pmf x `PdfDirection`), set by the path tracer per medium; `EvaluateLightSample` gives the phase-sampled
   direction of a single scattering point its share the same way. The lights of the emission are chosen by their power, apart
   from the sampler of the lights. `EmissionOver` passes the densities of the path into the tracking of the emission (the
   mean over wavelengths of the product, not the product of the means).
3. **Emission-aware majorant**, `"float emissionmajorant" m` on a medium with a grid of majorants (grid, RGB grid, NanoVDB,
   texture without `"hierarchydepth"`); 0: off. `MajorantGrid.RaiseForEmission`: a voxel is raised to at least
   m x (largest majorant of the grid) x (its emission bound / the largest emission bound). Disney use max(mu_t, mu_a Le),
   which mixes units and needs a clamp; this form has neither.
4. **Residual emission tracking**, `"string emission" "residual"` (default `"ratio"`): `Volume.TrackEmissionResidual`. The
   mean extinction of each majorant cell is taken out as an exponential and the rest is ratio tracked, with collisions at the
   scalar majorant, per-wavelength factors, and roulette below 0.02. Not in the papers (the control of Novak et al. 2014
   given to emission tracking). It acts in `Volume.Emission` of the tracked volumes, i.e. on the lines of 2 and on
   phase-sampled directions; not on the emission that a path's own walk gathers, not with absorption alone, not in the
   aggregate.

Tests (`ProductionSampling.EmissionTracking`, `.EmissionLight`):
- residual against ratio, emission through fire in smoke, deviation of one line (lookups): density scale 0.5: 0.295 (1.04) /
  0.258 (1.18); scale 4: 0.181 (8.3) / 0.042 (8.0); scale 16: 0.0047 (33.1) / 0.0008 (8.6). Closed form in a constant grid:
  0.62918 +- 0.00082 against 0.62907.
- emission majorant, a hot place where the smoke is thinnest, deviation (lookups): off 0.114 (0.79); 1: 0.064 (0.98); 4: 0.035
  (1.88); the same mean.
- the light on a grid medium and on an aggregate of two: the density of directions integrates to 0.999 +- 0.001; emission
  arriving at a point by directions towards the emission against uniform directions, 3 million samples: 0.09192 +- 0.00006
  against 0.09184 +- 0.00010, 0.06790 +- 0.00003 against 0.06786 +- 0.00006.

Renders (production, independent sampler). Mean of the image, sampled against gathered only, several seeds: coloured smoke
that emits everywhere over a floor, 0.018639 +- 0.000042 against 0.018714 +- 0.000047; under single scattering fog 0.017135 +-
0.000051 ("mis"), 0.017171 +- 0.000039 ("light") against 0.017178 +- 0.000088; aggregate 0.023176 +- 0.000061 against 0.023436
+- 0.000181; texture medium 0.3571 +- 0.0010 against 0.3589 +- 0.0013; volpath on the grid medium 0.018559 (point), 0.018556
(lines) against 0.018543; small fire, 0.013061 +- 0.000219 against 0.013046 +- 0.000169, in multiple scattering fog 0.012944
+- 0.000121 against 0.013177 +- 0.000162.

RMSE over the pixels that do not see the emission, 64 spp unless said (time; RMSE^2 x time):
- hot place in thin smoke (reference 512 spp sampled + majorant): gathered 0.229 (8.7 s; 0.454), 256 spp 0.120 (34 s; 0.499);
  majorant 4: 0.091 (8.9 s; 0.073), 256 spp 0.045 (34 s; 0.068); sampled 0.031 (24 s; 0.023); sampled + majorant 0.013 (26 s;
  0.0043). Over the whole image the majorant alone is best (0.142 at 8.9 s): sampling does nothing for the pixels that see the
  emission.
- torch in fog (reference 1024 spp sampled): gathered 0.050 (8.4 s; 0.021), 256 spp 0.025 (30 s; 0.019); sampled 0.029 (29 s;
  0.024). About even at equal time.
- fire inside smoke (reference 1024 spp sampled): gathered 0.128 (8.4 s; 0.138), 256 spp 0.069 (30 s; 0.144); sampled, ratio
  0.072 (30 s; 0.157); sampled, residual 0.078 (28 s; 0.168). Even; the residual tracking saves time, not error, here.

Not done / known:
- point sampling of the emission in the production path tracer (lines only).
- the NanoVDB bound of the emission is the density of the cell wherever any temperature of the grid emits; untested (no data).
- `EvaluateLightSample` (the phase-sampled direction of a single scattering point, from before) still divides the emission
  by the mean density of the beam and then of the path, a product of means; `SampleEmission` uses the mean of the product.
- the probe-sphere scattering guidance of Huang et al. is not implemented.

## `adaptivegrid` beside `uniformgrid` (uncommitted after 6655c0b)

The refinement rule of Benyoub and Dupuy, "Adaptive Tetrahedral Grids for Volumetric Path-Tracing" (SIGGRAPH Talks 2025),
given to the majorants of our grid medium (`GridMedium.RefineMajorants`): the same data and lookups (nothing resampled,
unbiased), the 16^3 majorants as the first level, a voxel split into `"integer branching"`^3 (2) where (max - min) / mean of
the density is above `"float variation"` (0.5), down to `"integer maxdepth"` levels (3) or one data cell; every voxel has
its largest, smallest and mean density. `"bool adaptivewalk"` (false): whether the walk of a path also steps through the
refined majorants, or only what asks for them (the estimators of the transmittance, the trackers with a control).

The paper's own structure, a `tetrahedralgrid` medium (conforming longest-edge bisection, one density per tetrahedron,
regular tracking, exact transmittance), was implemented, tested and measured, and then removed at the user's request because
it resamples the data (biased against the source grid). What it measured: 26.2 s against 14.0 s for the cloud render below at
the same RMSE; 462 k tetrahedra and 44 MB for a 128^3 smooth cloud; a ray's transmittance 0.003 rms from the data.

A fix found on the way and kept: a walk whose majorant equals the extinction could choose a null collision by the rounding
of the probabilities and divide 0 by 0; `MultiScatterHeterogeneousVolume`, `SingleScatterHeterogeneousVolume` and the
aggregate now treat it as no event.

Tests (`AdaptiveGrids.Transmittance`; `AdaptiveGrids.Slices` writes pictures where GRID_SLICES names a folder):
transmittance of 200 rays against the data marched finely (deviation of one estimate; lookups; ns; deviation^2 x ns):
- 64^3 cloud with detail at the cell size, scale 24: grid ratio 0.150, 8.0, 9400, 212; grid residuallocal 0.154, 5.6, 4300,
  102; adaptive residuallocal 0.074, 2.0, 5800, 32; adaptive ratio 0.173, 4.6, 9200, 276.
- 128^3 smooth cloud, scale 12: grid residuallocal 0.139, 1.8, 1600, 32; adaptive residuallocal (241 k voxels) 0.041, 0.40,
  8400, 14.
Ratio tracking gains nothing from tighter majorants (fewer lookups, more variance and more segments).

Render (production, 96^3 cloud, sun and sky, depth 60, 240x180, 64 spp; render time without start-up; RMSE against the
grid at 1024 spp; RMSE^2 x time): uniformgrid ratio 14.0 s, 0.1152, 0.186; uniformgrid residuallocal 14.2 s, 0.1112, 0.176;
adaptivegrid 1 level, residuallocal 13.8 s, 0.1082, 0.162; the same with the walk 15.5 s, 0.1081, 0.181; adaptivegrid 3
levels 17.9 s, 0.1094, 0.214; with the walk 22.8 s, 0.1090, 0.271. In this scene the error is the path sampling's, not the
transmittance's: all are within 7% in RMSE and the time decides.

Not done: the hierarchy on NanoVDB and RGB grid media; data above 128^3; expensive lookups.

## `aggregate2`, and joint double scattering beside the path (uncommitted after be4385a)

**`aggregate2`** (`volumeaggregate2.cs`), beside `aggregate`, which is unchanged. The same octree answering as a medium:
`SampleRay` gives one segment per leaf (`AggregateMajorantIterator`: majorant, control and mean are the sums over the
leaf's volumes of the largest, smallest and mean density of each times its extinction; the control fraction is the smallest
over the wavelengths, the mean fraction their mean), `SamplePoint` the sum of the leaf's volumes that hold the point with
the mixture of their phase functions. So its volume is whichever `"scattering"` asks for and every tracker, estimator,
`"distancesampling"`, `"doublescattering"`, `"emission"` applies; `emission_nee` as for `aggregate`. The octree gained the
mean densities (`AggregateMember.MeanDensity`, `Node.meanDensity`). Not in `aggregate2`: control collisions without a
lookup (`"tracker" "decomposition"` falls back to the majorant tracker) and spectral tracking among the members.

**Joint double scattering at the path level** (`Volume.JointShare`; `"doublescattering" "joint"` or `"mis"` on a multiple
scattering volume, homogeneous or tracked). On every ray: c in proportion to 1 / |ac|, then direction and distance of b
jointly (Georgiev et al. 2013), b before any surface, lit from a light at a fixed position a. Weighed by the balance
heuristic over the densities of the whole path with null collisions (Miller et al. 2019) against the path (collision c,
phase function, collision b) and, with `"distancesampling"`, against the equiangular point at b: a three-way weight.
`VolumeJoint` carries the collision c to the next ray (`VolumeGeometry.joint`, `VolumeContext.joint`),
`VolumeDirectWeight.For` and `EquiangularShare` add the joint density. Only lights at a position (point, spot, ...): the
weights are about the one point such a light has. `VolumeIntegrator.Create` no longer drops `"doublescattering"` when
everything else is default.

Tests:
- `VolumeAggregate.Aggregate2Transmittance`: every estimator through `aggregate2` against quadrature, beside `aggregate`
  (deviation; lookups; deviation^2 x ns), two rays: `aggregate` 0.064, 11.0, 25.7 and 0.085, 11.8, 51.5; ratio 0.064, 11.0,
  31.8; residuallocal 0.0085, 8.3, 0.34 and 0.013, 9.2, 1.18; raymarching 0.0003, 23.5; bidirectional 0.011, 30.4, 1.78;
  pseriescmf 0.015, 29.4, 2.37; residualtracking 0.053, 7.7, 10.0; unidirectional 0.043, 15.2, 15.8; nextflight 0.080, 7.2,
  33.1; tracklength 0.157, 2.8, 52.5. Majorants bound the extinction at every wavelength. (The test double `Wall` gave a
  walk the same random number twice, as `Known` once did; it has its own generator now.)
- `ProductionSampling.JointBesideThePath`: light scattered once or twice, over two rays as the path tracer does it, in 18
  combinations (homogeneous / tracked / decomposition x distances density / mis / equiangular x with and without joint): the
  same mean; deviation, tracked: path 2.70, with MIS 0.76, joint 0.48, joint + MIS 0.28.

Renders (production, 64 spp, 240x180; time without start-up; RMSE against 768 spp of joint + MIS; RMSE^2 x time). The two
references of each scene (the old way at 1024-2048 spp, the new at 768) agree in mean to 1.0%, 0.45%, 0.35%.
- three overlapping grid puffs with a point light inside: `aggregate` 12.8 s, 1.338, 22.9; `aggregate2` defaults 13.6 s,
  1.267, 21.8; residuallocal 13.2 s, 1.234, 20.1; + equiangular MIS 18.5 s, 0.453, 3.80; + joint 18.3 s, 0.582, 6.19; + both
  21.7 s, 0.314, 2.14.
- lamp in homogeneous fog: path 8.4 s, 0.298, 0.745; equiangular MIS 11.4 s, 0.108, 0.134; joint 12.8 s, 0.212, 0.574; both
  15.7 s, 0.061, 0.059.
- lamp in grid smoke: path 9.6 s, 0.343, 1.127; equiangular MIS 11.6 s, 0.324, 1.217; joint 13.7 s, 0.332, 1.509; both
  14.6 s, 0.117, 0.198.

Not done: joint sampling for area lights (the pivot of the weights would have to be drawn as the strategy draws it); b in
another medium or behind a surface; the tabulated phase-function products of the paper.

## Once-more scattered next-event estimation (`"oncemore"`, uncommitted)

Hanika, Weidlich and Droske 2022. `"string oncemore" "omnee" | "omnee2"` on a multiple-scattering medium; off by default.
From a collision c of the volume, or a surface point with the volume on the side the path came from, one scattering b is
put between c and a light at a position a (`Volume.OnceMoreShare`, `OnceMoreShare2`, `OnceMoreScattering`, called from
`ProductionPath`). The deflection angle theta at b and the angle alpha at c parametrise b on the arc of the inscribed angle;
in (theta, alpha, phi) the two inverse squared distances cancel to 1 / s, so alpha and phi are uniform (the paper's
equation for t is the same thing) and the density per solid angle and length about c is p(theta) / (2 pi theta) s / |ba|^2.

- `omnee`: as published, turned round to connect to the light. theta from the phase function, forward half only; the
  transmittances and the phase function / BSDF at c stay in the weight.
- `omnee2`: theta over all angles (a quarter uniform, so theta / sin(theta) stays bounded); four candidates resampled by
  f(c -> b) x sigma_s(b) x phase(b) x exp(-mean extinction x length), for the BSDF / phase function at c, a varying sigma_s
  and the transmittance.

Both are weighed by the balance heuristic over whole-path densities against the path, the equiangular point and the joint
pair (`VolumeJoint.OnceMore`, `Jointly`; `OnceMoreFinish`), so what one leaves out another covers.

Test `ProductionSampling.OnceMoreBesideThePath`: 36 combinations (homogeneous / tracked g 0.9, decomposition g 0.3 x none /
omnee / omnee2 x equiangular x joint), same mean. Deviation, homogeneous g 0.9: path 13.5, omnee 1.18, omnee2 1.18;
decomposition g 0.3 without equiangular: path 2.73, omnee 2.20, omnee2 1.03.

Renders (64 spp, 240x180, RMSE^2 x time against path 1536-2048 spp + omnee2 640-768 spp, which agree in mean to 0.1-0.5%):
- haze g 0.92: path 9.65; equiangular MIS 8.69; joint + MIS 3.40; omnee 7.22; omnee + MIS 1.60; omnee2 + MIS 1.60.
- grid haze g 0.92: path 10.5; MIS 6.38; joint + MIS 67.8 (one firefly); omnee + MIS 2.35; omnee2 + MIS 2.36.
- glossy copper sphere in haze g 0.85: path 2.54; MIS 0.72; joint + MIS 0.84; omnee + MIS 0.62; omnee2 + MIS 0.60.
- fog g 0.3: path 0.94; MIS 0.20; joint + MIS 0.124; omnee 0.77; omnee2 0.59; omnee + MIS 0.121; omnee2 + MIS 0.119.

`omnee2` only beats `omnee` where nothing else covers the backward angles (no equiangular MIS, moderate g); with MIS the two
are equal within noise, and the resampling did not show on the glossy sphere. It costs about 10% more.

Not done: surfaces outside the medium that see the light through it (the floor's fireflies in these scenes); area lights;
b in another medium; aggregates; with several lights the weights assume both points would choose the same one.

## Glints (`glint` material, docs/glint.md)

1. The cluster hierarchy of Wu et al. 2025 for large footprints, as an option beside the tiles (it simplifies the map: an
   approximation, to be measured against the exact distribution).
2. Closed forms for the two-facet walks of a dielectric that have a refraction in them, and for three facets of a
   conductor: now found by the sampling of the BSDF only.
3. Wu et al.'s analytic filtering of a normal-mapped diffuse surface.
4. A procedural map of flakes (no image), and a diffuse or coated base under the facets.
