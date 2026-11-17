# Glowing solids: `AreaLightSource "thermal"`

The light of a hot surface after A. Wilkie and A. Weidlich, *A Physically Plausible Model for Light Emission from Glowing
Solid Objects* (EGSR 2011), approach C. The code is in `thermalemission.cs`; the light itself is a `DiffuseAreaLight`
with a `ThermalEmission` (as the emission of an OSL material is), so everything that knows area lights keeps working.

## The model

By Kirchhoff's law a surface emits the radiance of a blackbody of its temperature times its absorbance:

    Le(x, w) = B(T(x)) (1 - integral of f(w, wi) |cos(wi)| dwi)

The integral is what a path that arrives along `-w` would collect from surroundings of the radiance `B`. A glowing object
inside a furnace of its own temperature therefore cannot be seen, whatever its material: this is the test of the
implementation (below). With the Mueller matrix `M` of the scattering the emitted Stokes vector is
`B (e0 - integral of M e0)`: the light of a smooth surface is polarised at right angles to the light that it reflects.

- A smooth conductor or dielectric: the Fresnel reflectance, in closed form, without noise.
- Any other material: one sample of the BSDF, with the weight `f |cos| / pdf`. The mean is the integral; nothing is
  tabulated. The random numbers come from a hash of the point and the direction (as in the `LayeredBxDF` of pbrt-v4),
  because the radiance of a light is asked for without a sampler.
- `B` is Planck's law in W m^-2 sr^-1 nm^-1 times 683 lm/W, the photometric scale of the other lights.

## Scene description

    Material "conductor" "spectrum eta" "metal-Au-eta" "spectrum k" "metal-Au-k"
    AreaLightSource "thermal" "float temperature" 1400
    Shape "sphere"

| Parameter | Default | Meaning |
|---|---|---|
| `float` or `texture temperature` | 1500 | kelvin |
| `float scale` | 1 | factor of the radiance |
| `bool twosided` | true | both sides emit |
| `bool opaque` | false | what the material transmits is absorbed behind the surface (a thick absorbing body: `B (1 - F)`); the reflection alone is sampled |
| `bool blackbody` | false | ignore the material: approach B of the paper, for comparisons |

A material that transmits (glass) emits nothing at a smooth lossless interface; what glows there is the medium inside.
`opaque` is for a body like the black quartz of the paper; put something black inside the shape, or the light that
enters is seen again on the way out.

## `AreaLightSource "thermal2"`: a temperature that varies over one shape

The same light, with the point of each light sample resampled by the temperature (resampled importance sampling):
`"integer candidates"` points (8) are drawn as the shape draws one, and one is kept in proportion to the radiance of a
blackbody of its temperature at 580 nm over its density. The estimate is unbiased for any number of candidates.

The density that the light reports (in `SampleLi` and in `PDF_Li`) is the plain one times `n t / (t + (n - 1) mean)`,
`t` the target at the point and `mean` its mean over the shape (256 points, once): about what resampling with `n`
candidates makes of the density. The integrators use it only in their weights against BSDF sampling, which stay
functions of the path that add to one. With the plain density in the weights the result is the same on a diffuse
receiver and three times noisier on a glossy one (32 candidates, hot corner).

A plate, 850 K with one corner at 1900 K (and a plate from 900 K to 1750 K, "gradient"), lighting a floor and a sphere;
`path`, depth 3, independent sampler, 360 x 220. Error: mean square error over the square of the mean, against the
average of two references of 2048 samples (one of each light); the pixels of the plate itself are left out.

| Scene | `thermal`, 16 spp | `thermal2` 8, 16 spp | `thermal2` 32, 16 spp | `thermal` in the time of 8 (28 spp) | `thermal` in the time of 32 (73 spp) |
|---|---|---|---|---|---|
| hot corner | 1.98 (4.4 s) | 0.271 (6.5 s) | 0.077 (14.9 s) | 1.18 | 0.458 |
| hot corner, glossy floor | 2.28 (5.8 s) | 0.328 (8.4 s) | 0.112 (14.9 s) | 1.30 | 0.512 |
| gradient | 0.245 (4.4 s) | 0.077 (6.4 s) | 0.059 (14.5 s) | 0.137 | 0.053 |

A light sample costs about 1.8 times as much with 8 candidates and 4.6 times with 32 (the whole render, in this scene).
For a hot spot the error falls with the number of candidates; for the smooth gradient 8 are enough and 32 gain nothing
over the plain light in the same time.

The two references agree: the means differ by -0.075% (noise 0.053%), -0.029% (0.020%) and -0.014% (0.057%), and the
per-pixel differences are those of their noise (0.0182 against 0.0177 expected, 0.00252 against 0.00252, 0.0204 against
0.0204). `ThermalEmission.ResampledLightIsUnbiased` compares the light sample, alone and with a direction of the cosine
under the weights, with a quadrature over the plate: run once with 24 and 8 million samples, 1.0008 +- 0.0005 for 8
candidates and 1.0002 +- 0.0004 for 32. The furnace passes with `thermal2` in `path`, `production`, `volpath` and
`bdpt`.

## What was changed outside the new file

- `SurfaceInteraction.GetBSDF(Vector3f, ref SampledWavelengths, ScratchBuffer)`: the BSDF of a point that no camera ray
  reached (no differentials). The existing `GetBSDF` is unchanged in behaviour.
- `SurfaceInteraction.Le`: a hit of a thermal light gives the light the whole surface of the hit.
- `DiffuseAreaLight`: `thermal`, `CreateThermal`, `L(SurfaceInteraction, ...)`, `LPolarised`; power and light bounds from
  the mean of the emission over the shape (16 points), so the light sampler knows the temperature and the emissivity of
  every shape (of every triangle of a mesh).
- `PolarisedEmission`: the Stokes vector of what a light emits, asked by every polarised integrator (`path`, `volpath`,
  `production`, `bdpt`, `mlt`) at hits, at light samples and at the start of light subpaths: see
  `polarisation-in-integrators.md`.
- No signature of pbrt-v4 was changed.

## Tests

`ThermalEmission.SmoothConductor`, `.CompletesTheFurnace`, `.OpaqueRoughDielectric`, `.PolarisedAcrossTheReflection`,
`.PolarisedGatherKeepsTheIntensity` (`thermalemission_test.cs`).

The furnace render: a sphere at 1400 K inside a black enclosure at 1400 K, 48 x 48 pixels, 256 samples, independent
sampler. Every material must give the radiance of the enclosure, 8465 in the red channel:

| Material | path | production | polpath: I, S1, S2 |
|---|---|---|---|
| gold, smooth | 8467 ± 5 | 8467 ± 5 | 8467, 0, 0 (exactly) |
| gold, roughness 0.3 | 8464 ± 15 | 8453 ± 13 | 8464, 0.2 ± 0.2, 0.2 ± 0.2 |
| copper, anisotropic | 8469 ± 15 | 8452 ± 13 | |
| diffuse | 8464 ± 6 | 8471 ± 7 | |
| coated diffuse | 8462 ± 9 | 8464 ± 9 | 8462, 4 ± 2, -4 ± 2 |
| rough glass, eta 1.5 | 8481 ± 18 | 8493 ± 19 | 8481, 1 ± 4, 0 ± 4 |
| quartz, opaque, smooth | 8463 ± 9 | 8475 ± 9 | 8463, 0 ± 2, 0 ± 2 |
| quartz, opaque, rough | 8463 ± 8 | 8464 ± 8 | 8463, 0 ± 2, 0 ± 2 |

`bdpt` gives the same for the three materials it was run on.

Polarised, with `"bool polarised" true` in each integrator (128 samples; `bdpt` 40): the intensity at the centre, and S1 in
the sector to the right of the sphere and above it, where an error does not cancel by symmetry:

| Material | `path` | `volpath` | `production` | `bdpt` |
|---|---|---|---|---|
| gold, roughness 0.3: I | 8471 ± 17 | 8466 ± 17 | 8437 ± 16 | 8480 ± 31 |
| S1 right, above | -0.4 ± 1.3, -0.5 ± 1.2 | -1.0 ± 1.2, -0.5 ± 1.1 | -2.4 ± 1.3, 0.9 ± 1.1 | -1.9 ± 2.4, 2.8 ± 2.1 |
| quartz, opaque, rough: I | 8460 ± 7 | 8460 ± 7 | 8455 ± 7 | 8464 ± 13 |
| S1 right, above | -14 ± 6, 1 ± 6 | -14 ± 6, 2 ± 6 | -4 ± 7, 1 ± 7 | -9 ± 12, 4 ± 11 |
| coated diffuse: I | 8469 ± 10 | 8472 ± 10 | 8463 ± 11 | 8455 ± 18 |
| S1 right, above | 12 ± 15, -10 ± 18 | 10 ± 15, -10 ± 17 | 21 ± 17, 8 ± 17 | 1 ± 30, 20 ± 32 |
| rough glass: I | 8462 ± 21 | 8475 ± 23 | 8481 ± 23 | 8433 ± 39 |
| S1 right, above | 2 ± 19, -27 ± 18 | 36 ± 20, -18 ± 17 | 16 ± 14, -18 ± 21 | 2 ± 36, 43 ± 34 |
| copper, anisotropic: I | 8479 ± 16 | 8472 ± 16 | 8438 ± 16 | 8463 ± 30 |
| S1 right, above | -2.1 ± 2.3, -0.4 ± 2.0 | -3.4 ± 2.3, -0.3 ± 1.9 | -3.8 ± 2.4, 3.7 ± 2.0 | -3.3 ± 3.9, 3.7 ± 4.2 |

For scale: the same rough gold sphere without its glow shows S1 = +47 and -46 in those sectors. The production path at
1024 samples gives I = 8466 ± 7 for rough gold (the path tracer 8460 ± 7).

## Limits

- The optical constants are those of the material as given. They change with the temperature, and most hot surfaces
  oxidise (sections 2.3 and 5 of the paper).
- Subsurface materials: the light that enters is counted as not absorbed (a warning).
- The side below an interface emits into that medium, `n^2 B`; the index is known for the `DielectricBxDF` only.
- A BSDF whose sample weights exceed one gives negative samples of the radiance; their mean is right.
- The polarised value of a sample is taken relative to its intensity (the two are one sample of the reflectance: the
  random numbers are hashed from the point and the direction as the integrator gave them), so an integrator must ask for
  both with the same point and direction.
- A light that moves uses a frame around the normal for the BSDF, not the tangents of the shape.
