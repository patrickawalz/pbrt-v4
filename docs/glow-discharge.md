# Glow discharge

The light of a gas in a discharge tube (a neon sign, a spectral lamp), as a medium: `src/pbrt/glowdischarge.cs`.
After Sivaram, Ramamoorthi and Li, "Modeling and Rendering Glow Discharge" (SIGGRAPH 2025), but rendered as any other
emitting volume, by the unbiased integrators that are already there, with line spectra that are sampled exactly.

```
MakeNamedMedium "sign" "string type" "glowdischarge" "string gas" "neon"
    "point3 P" [ -1 0 0  1 0 0 ] "float radius" 0.006 "float luminousflux" 60
AttributeBegin
  MediumInterface "sign" ""
  Material "interface"
  Shape ...            # any closed shape around the tube (or the glass of the tube itself)
AttributeEnd
```

Declare the medium after `WorldBegin` (before it, the points are taken in camera space, as for every medium of pbrt).

## Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `point3 P` | | Centre line of the tube, a polyline |
| `integer subdivide` | 0 | Catmull-Rom curve through `P`, this many segments per span |
| `float radius` | 0.01 | Inner radius of the tube (scene units) |
| `string gas` | `neon` | `neon`, `helium`, `hydrogen`, `mercury`, `sodium`, `argon` |
| `float lines` | | Own spectrum: pairs of wavelength (nm, air) and relative radiant strength; needs `float atomicmass` |
| `float radiantpower` | 1 | Watts per metre of tube in the listed lines between 360 and 830 nm |
| `float luminousflux` | | Lumens per metre of tube, instead of `radiantpower` |
| `float scale` | 1 | Multiplies the power |
| `float metersperunit` | 1 | Size of a scene unit |
| `float gastemperature` | 300 | Kelvin; the Doppler width of the lines |
| `float excitationtemperature` | 11604.5 | Kelvin (1 eV); the Boltzmann factor of the upper levels |
| `float controlsamples` | 8 | Collisions of the tracker across a diameter (variance only) |
| `float wavelengthfraction` | 0.5 | Part of the film's wavelengths that are taken from the lines; 0 turns it off |
| `bool emission_nee`, `integer emission_nee_res` | true, 32 | The emission is also a light (next event estimation) |
| `string model` | `column` | `column`: Schottky's positive column. `drift`: the paper's model of the particle densities |
| `string flow` | `laminar` | Drift model: `laminar` (along the tube) or `extruding` (from the wall toward the centre line) |
| `float ionization`, `recombination`, `attachment`, `ionscale` | 100, 1e-8, 0.25, 10 | Drift model: the paper's alpha, beta, eta, rho |
| `float driftspeed`, `driftdecay`, `driftfalloff` | 1, 0, 1 | Drift model: the speed, its decay per unit length along the tube, and the exponent of `(1 - (r/R)^2)` across it (laminar) |
| `bool speedtemperature` | false | Drift model: the excitation temperature goes as the square of the local speed (the paper's rule) |
| `float tolerance` | 1e-9 | Drift model: the tolerance of the integration of the densities |

The radiance has the units of the thermal emitters: W m^-2 sr^-1 nm^-1 times 683, so the Y of the film is cd/m^2.

## What is modelled

* **Where it emits.** By default the positive column, by Schottky's theory: the electron density across the tube is
  `J0(2.405 r / R)`, uniform along the tube, and the gas is excited in proportion to it. `J0` is its power series
  (no table). The ends are flat. The emission coefficient is
  `j(r, lambda) = P / (4 pi) * J0(2.405 r / R) / (1.3564 R^2) * phi(lambda)`; `1.3564 R^2` is the integral of the
  profile over the cross section, so a metre of straight tube radiates exactly `P`.
* **The spectrum.** `phi` is the sum of the lines, each a Gaussian of its Doppler width
  `sigma = lambda sqrt(k T / (m c^2))` (0.0007 nm for neon at 300 K). The lines stand to each other as
  `g_k A_ki h nu exp(-E_k / k T_exc)`.
* **Nothing else.** No scattering; no absorption beyond the one the Medium interface needs (below); unpolarised.

## How it is rendered

* **Emission through the Medium interface.** The interface gives emission as `sigma_a Le`. The medium has an absorption
  that is 10^-6 of its majorant and `Le = j / sigma_a`, so `sigma_a Le` is `j` exactly; what that absorption removes from
  light crossing the tube is below 10^-5. The majorant is a control majorant in a grid around the centre line: the
  trackers gather the emission at its collisions. No integrator was changed.
* **Wavelengths.** While a glow discharge is in the scene, `Film::SampleWavelengths` returns a mixture: a fraction of the
  wavelengths from the lines (line by its share of X+Y+Z, each gas an equal part, wavelength by the Gaussian), the rest
  as before, each with the density of the mixture. Everything else in the scene is still estimated without bias (the
  test below checks a smooth spectrum too). A scene without a glow discharge is rendered as before.
* **Light sampling.** The existing `VolumeEmissionLight`, with the profile as its density.

Changes outside the new file: `Film::SampleWavelengths` of the RGB/G-buffer and spectral films (four lines each); the
buckets of the spectral film are estimated with the density of the wavelengths while lines are sampled (they assume a
uniform density otherwise, as before); `SampledWavelengths.FromSamples` (new static method); the medium name in
`Medium.Create`; one line in the dispatch of `SampleT_maj`; and `Medium.EmissionForPower` (new virtual, default
`sigma_a Le`): what `VolumeEmissionLight.Phi` asks, so that a line spectrum, which a few fixed wavelengths never find,
tells the light samplers its power as the constant spectrum of the same luminance.

## The drift model (`"string model" "drift"`)

The paper's equations for the electrons and the positive and negative ions along a drift velocity (its equations 9
and 14), with the emission proportional to the speed times the electron density (its equation 4).

* The drift is given in the coordinates of the tube: along it (`laminar`, speed
  `v0 (1 - (r/R)^2)^falloff exp(-decay s)`) or toward the centre line (`extruding`, speed `v0 (r/R) exp(-decay s)`, whose
  divergence is constant, so there is no singularity on the axis). The integral curves are then known exactly and the
  paper's backward trace is not needed.
* The three equations are integrated along the curve by an adaptive Dormand-Prince 5(4) method to `tolerance`, where the
  paper takes a fixed number of Euler steps. A laminar drift without decay stops as soon as the densities have reached
  their steady state.
* `radiantpower` / `luminousflux` keep their meaning: the emission is divided by its integral over the tube, computed at
  creation by Gauss-Legendre quadrature (32 nodes) of the same solver.
* `speedtemperature`: the Boltzmann temperature of the lines at a point is `excitationtemperature (speed / v0)^2`, the
  paper's `T = m |mu|^2 / k`; the shares of the lines are normalised at every point.
* Cost: one integration of the densities per collision, about 35 microseconds; a laminar drift with decay is the
  slowest. `controlsamples` 4 halves it.

What is still simplified here against the paper: the drift is one of these two families, not an arbitrary vector field;
bends are treated in the coordinates of the tube, as if it were straight.

## Against the paper

| | Paper | Here |
|---|---|---|
| Integration along the ray | One uniform sample per ray | Null-collision tracking, unbiased |
| Particle densities | Backward trace and Euler steps, fixed counts | Exact curves, adaptive integration to a tolerance |
| Spectrum | Lines, Boltzmann shares | The same, with Doppler widths, sampled exactly |
| Light on the scene | BRDF sampling only | Next event estimation, MIS with the path |
| Integrators | One | `volpath`, `production`, `bdpt`, `mlt`, each with or without `"bool polarised"` where it has it |
| Brightness | Cross section constant, free scale | Watts or lumens per metre |
| Drift field | Arbitrary, from Bezier curves | Laminar or extruding in the tube's coordinates |
| Positive column | Not modelled | Schottky's profile (`"model" "column"`) |

## Validation

Independent sampler throughout.

**Positive column, bare straight tube** (neon, R = 0.05, 0.001 W/m, 64x64, 256 samples, box filter) against the analytic
image (chord integral of `J0` times the tristimulus sum of the lines):

| Run | Image total / analytic (R channel) |
|---|---|
| `volpath` | 0.9996 |
| `production` | 0.9998 |
| `bdpt` | 0.9995 |
| `volpath`, `"bool polarised" true` | 0.9996 |
| `volpath`, lines 2.4 nm wide, stock wavelength sampling (reference of the wavelength sampling) | 0.9976 |
| `volpath`, true line width, stock wavelength sampling | not converged (the lines are never found) |
| Spectral film: sum of the buckets times their width against the radiant power, analytic | 1.0026 |

**Light on a floor**, against the analytic irradiance of the tube over the footprint of the camera (expected R channel
0.10446 +- 0.00009):

| Run | Rendered |
|---|---|
| `volpath` | 0.10436 +- 0.00025 |
| `volpath`, no next event estimation | 0.10442 +- 0.00033 |
| `bdpt` | 0.10444 +- 0.00044 |
| `volpath`, spectral film (its RGB) | 0.10436 +- 0.00025 |
| `mlt` | 0.10256 +- 0.0006 (pixel scatter; the error of the normalisation of MLT is not in it) |

**Drift model**: the solver against the closed form without recombination (1e-7) and against a fixed-step fourth order
reference written from the paper (2e-6), laminar, decaying and extruding; the integral over the volume and the tracked
emission along a ray against quadrature. Renders over a floor, 128 samples: `production` / `volpath` 1.0035 +- 0.0034,
`bdpt` / `volpath` 1.0021 +- 0.0024.

**Glass tube around the gas** (two dielectric cylinders, `maxdepth` 60): `production` / `volpath` 0.9998 +- 0.0016.
`bdpt` / `volpath` 1.0002 +- 0.0011 over the image and 0.9977 +- 0.0022 on the floor (1280 against 4096 samples).
This needed a correction in `bdpt` that is not specific to this medium: the density of a collision that gathers the
emission of a medium, after a specular bounce, was left at zero, which the weights take as one, so the majorant was
dropped and emission seen through glass was weighed too low against the light subpaths (the floor was 2.3% dark). It
is now the majorant along the ray. Without glass nothing changes (`bdpt` / `volpath` 1.0005 +- 0.0007).

Tests (`GlowDischarge.*`, six): the profile and its integral; the wavelength mixture (tristimulus sums of neon, mercury
and helium, a smooth spectrum, the ends of the random number); the inversion of the Gaussian; the tracked emission; the
drift solver; the power of the drift model.

## What is not exact, and what is missing

* **Level populations.** A Boltzmann factor with one excitation temperature is the paper's model and this one's. A real
  discharge is not in equilibrium: the populations follow electron-impact cross sections, which could not be obtained
  here. The default of 1 eV is a choice. Lines that share an upper level (the three visible mercury lines) do not depend
  on it.
* **Line lists.** Helium: complete between 380 and 730 nm (Wiese and Fuhr 2009, 23 multiplets). Hydrogen: Balmer alpha,
  beta, gamma from the same source. Neon: the 16 persistent lines of the NIST Handbook plus four more of Bridges and Wiese
  1970 (20% accuracy); the remaining weak 2p-1s lines have no transition probability in any source that could be reached.
  Mercury: no yellow doublet (577/579 nm). Argon: most blue lines missing; it warns. Every row was checked by the
  difference of its levels, which checks wavelengths and levels and not the transition probabilities. `float lines`
  takes any list.
* **Absorption.** The Medium interface gives emission as `sigma_a Le`, so an emitter needs some absorption: 10^-6 of the
  majorant, below 10^-5 across the tube. It cannot be made zero without changing the integrators. Real self-absorption
  of the lines (it matters for resonance lines) is not modelled: it needs the densities of the lower levels.
* **Line shape.** Doppler broadening only; helium multiplets are one line each. Invisible in an image.
* **Bends.** The source depends on the distance to the centre line. For a bend of constant curvature the power per unit
  length is still exact (the volume element of a torus adds a term in the cosine of the angle around the tube, which
  integrates to zero); at a corner of a polyline it is approximate. `subdivide` makes the corners small.
* **`bdpt` needs the emission light** (`emission_nee`, on by default): without it `bdpt` renders the medium black, as it
  does every emitting medium.
* **`path` and `sppm`** do not render media in pbrt, and render nothing here.
* **`mlt`**: agrees over the image (1.0008 +- 0.0025 with 2 million bootstrap samples), 1.8% low in the floor test.
* **Path length.** With a glass tube, `maxdepth` 5 loses 5% of the light and the integrators count depth differently
  (3% apart); use a large `maxdepth`.
* **Noise with several gases.** A camera sample has four wavelengths; with five gases in view each sample carries the
  lines of two or three of them. Unbiased, but it needs samples; `wavelengthfraction` 0.75 helps when the discharges are
  the only light.
