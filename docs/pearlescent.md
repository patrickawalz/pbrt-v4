# Pearlescent material

`Material "pearlescent"` → `PearlescentBxDF` (`src/pbrt/bxdfs.cs`), platelet optics in `src/pbrt/util/pearlescent.cs`,
tests in `src/pbrt/pearlescent_test.cs`. Ported from the C++ fork (`util/pearlescent.h`, `util/pearlslab.h`,
`PearlescentBxDF`, `PearlescentMaterial`), after Guillén, Marco, Gutierrez, Jakob and Jarabo 2020, "A General Framework
for Pearlescent Materials".

## Model

A smooth dielectric interface (the container) over a slab of platelets over a Lambertian base.

- **Platelet**: coating | substrate | coating in the container, nothing absorbs. Its reflectance is the thin-film
  interference of the three films, averaged over a Gaussian distribution of the substrate thickness in closed form
  (no tables); what it does not reflect goes straight through.
- **Slab**: the platelets are microflakes with a Trowbridge-Reitz distribution of normals about the surface normal.
  `kappa` is the projected platelet area per unit volume times the slab thickness (for the paper's volume fraction
  C, slab thickness t and platelet thickness d: kappa ≈ C t / d).
- **Single scattering** (one platelet, or the base) is in closed form. **Multiple scattering** is a random walk with
  next-event estimation: reflections by platelets, by the base and by the inside of the interface, up to `maxevents`.
- **Polarisation**: the mirror reflection of the interface and the single reflection by a platelet (refraction in,
  films with the phase between s and p averaged over the thickness, refraction out) have their Mueller matrices; the
  base and paths of more events depolarise.

## Parameters

| | default |
|---|---|
| `float kappa` | 3 |
| `float roughness` / `uroughness` / `vroughness` (spread of the platelet normals, GGX alpha) | 0.2 |
| `spectrum reflectance` (base albedo) | 0.2 |
| `float containern` | 1.57 |
| `float coatn`, `coatabbe`, `coatthickness` (nm) | 2.5, 9, 60 |
| `float substraten`, `substrateabbe`, `substratethickness`, `substratestd` (nm) | 1.58, 63, 500, 60 |
| `string distribution` (`ggx`, `beckmann`, `studentt`, `charlie`: the platelet normals through `MicrofacetDistribution`) | not given: GGX written out in the BxDF |
| `float gamma` (Student-t) | 2 |
| `integer maxevents` (1: single scattering only) | 64 |
| `integer quadrature` (samples of the effective extinction) | 16 |

## What differs from the C++ fork

1. Total reflection at the substrate (container index above the substrate's, e.g. silica in a 1.555 container): the
   fork returns the reflectance of the coating on an infinite substrate (1); here the wave tunnels through the platelet.
2. One thickness (`substratestd` 0): the fork's series is cut at 80 terms and is off by up to 1e-2 near grazing
   incidence; here the amplitude is evaluated directly.
3. The series: the inner geometric sum is exact and the length follows the Gaussian damping. 3.6 times faster, error
   3e-5 → 3e-6.
4. The quadrature of the effective extinction is even in the variable in which the reflectance rises at grazing
   incidence: f against Sample_f from +2% to within 1%.
5. A walk decides reflect / pass at every platelet by the reflectance of one wavelength, chosen per walk; the weight
   of a wavelength is bounded by the number of wavelengths. The fork uses the mean reflectance at each platelet, and
   the weights grow without limit in a dense slab (coloured fireflies; standard error of the albedo at kappa 20:
   0.035 → 0.0016).
6. The stand-in pdf for MIS evaluates the platelet lobe inside the container (refracted directions).
7. Mueller matrices (not in the fork).

## Verification (`--test-filter "Pearlescent.*"`)

- Platelet against the characteristic matrix (`FrThinFilmPolarised`, three films, thickness averaged by quadrature),
  61 angles × 21 wavelengths × 5 stacks: Fs, Fp within 1.3e-5, phase terms within 7.3e-5.
- Energy, white base, unlimited events: mirror + slab = 1.0000 at kappa 0.5, 3, 20 and cosines 0.95, 0.5, 0.15.
  With 64 events: 0.993 at kappa 3, 0.76–0.84 at kappa 20 (raise `maxevents` for dense slabs).
- Albedo of the slab by Sample_f against f: within 1% (noise limited) for single scattering and for 64 events.
- Reciprocity: closed form 1.6e-7; with the walk within 3 sigma over 12 pairs.
- Matrices: sampled single reflection against f 1e-6; mirror against the Fresnel matrix 2e-7; physical to 1e-6.

## Platelet normals from a MicrofacetDistribution

The second constructor takes any `MicrofacetDistribution`: the slab needs `D`, `Sigma` (the platelets are two-sided:
`Sigma(w) + Sigma(-w)`) and `Sample_wm_SingleSided` (the side by the area it shows, then a visible normal). No
shadowing term: the extinction of the slab is it.

- GGX through the interface against GGX written out: thin slab (no quadrature) 1.1e-4; closed form with 1024
  quadrature points 3.5e-3; slab albedo with 64 events 0.4356 against 0.4361 (0.4 sigma).
- GGX, Beckmann, Student-t (alpha 0.2 and 0.05), Charlie 0.5: white base total 1.0000; Sample_f against f within 3
  sigma (at most 1.9%); reciprocal within 1.7 sigma.
- The extinction quadrature converges slower through the interface (16 points: closed form within 3.8% of the
  converged value at alpha 0.2, against 1.5% written out). Charlie, with normals all over the hemisphere, needs
  `quadrature` 256 (at 16: f 6% above Sample_f, not reciprocal).
- The direction of a walk is normalised after every reflection: a sampler whose normals are 4e-5 off unit length
  (Charlie) otherwise collapses the direction and traps the walk (3.5% of the energy lost).

## Not done

Rough interface, absorbing platelets, several strata, platelets tilted away from the surface normal; a path of two
or more events depolarises completely; the hero wavelengths are not terminated (not needed: every wavelength is carried).
