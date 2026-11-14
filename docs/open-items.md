# Open items on the pbr-materials branch

What is left to do, as of 14 November 2026. Each section lists the work in the order it is meant to be done.

## To port from the C++ fork

1. `stackedcoatedconductor`.
2. `vcavity` (Lee et al. 2018) and the fork's transmissive variant. The conductor is parked on the branch `vcavity-backup`;
   check what is there first.
3. Phase functions: fog and cloud (Jendersie and d'Eon, HG + Draine, with `dropletdiameter`), Cornette-Shanks, tabulated and
   spectrally tabulated, the `depolarization` parameter, the `phasesampling` / `hero` options.
4. `polygrain` (the discrete polydisperse micrograin BSDF, `util/micrograin.h`).
5. `facetforge` and `facetcoateddiffuse` (`util/ffnum.h`, `util/facetforge.h`).
6. `diffraction` (Liu et al. 2025, Holzschuch and Pacanowski, the additive mode; `util/kirchhoff.h`).
7. `openpbr`, last: it composes the lobes above and needs `twofacet.h` and `surfgrad_microfacet.h` for its normal-mapped coat.

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
