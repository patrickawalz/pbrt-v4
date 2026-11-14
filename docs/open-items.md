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
2 %. Both at once needs a walk whose probabilities are symmetric (the first scattering uses the correlated visibility of the
two directions, the later ones do not); a compensation lobe built from the albedo would need a table per set of grains.
