# Sky lights

## `hosek`: the sky and sun of Hosek and Wilkie

`LightSource "hosek"` (`src/pbrt/hoseksky.cs`, coefficients in `src/pbrt/hosekskydata.cs`, suite `HosekSky`): the
spectral sky-dome radiance model of Hosek and Wilkie (SIGGRAPH 2012) with their solar radiance function (IEEE CG&A
2013), as an infinite light. `HosekSkyModel` is a port of the spectral part of the authors' sample implementation,
version 1.4a (3-clause BSD; the notice is kept in the data file).

```
LightSource "hosek" "vector3 sundir" [ -0.5 -0.6 0.6 ] "float turbidity" 3 "spectrum albedo" [ 300 0.3 900 0.3 ]
```

| Parameter | Default | Meaning |
|---|---|---|
| `vector3 sundir` | 0 0 1 | Direction to the sun; must be above the horizon |
| `vector3 up` | 0 0 1 | The zenith |
| `float turbidity` | 3 | 1 (clear) to 10 (hazy) |
| `spectrum albedo` | 0.3 | Albedo of the ground, 0 to 1, taken at the eleven wavebands of the model |
| `bool sun` | true | With the solar disc (0.51 degrees, limb darkened) |
| `bool ground` | false | Below the horizon, a ground of the albedo lit by the sky and the sun, instead of nothing |
| `float scale` | 1 | Multiplies the radiance |
| `string sampling` | `table` | `table` or `tgmm` (below) |
| `string polarisation` | `none` | `wilkie2004`: the sky dome is polarised by the heuristic below |

The radiance is W m^-2 sr^-1 nm^-1 times 683 (the units of the thermal emitters and of `laser`): the Y of the film is
cd/m^2, a sunlit ground about 6000 and the solar disc about 10^9, more than a half-float image holds. The model has
eleven wavebands from 320 to 720 nm; from 720 to 760 nm it falls to nothing, as in the authors' code.

Sampling: a mixture of the solar disc (uniform) and the sky, by their shares of the light; unbiased, and found by the
light's samples and by the samples of surfaces alike. The sky is sampled by `"string sampling"`:

* `"table"` (default): a table of the sky's luminance over the sphere (128 by 128, equal-area, with a floor).
* `"tgmm"`: the truncated Gaussian mixtures of Vitsas, Vardis and Papaioannou (EGSR 2021): five Gaussians of azimuth
  and elevation, sampled in closed form by the error function. The fits are the authors' published ones for this
  model (turbidity 2 to 10, every third degree of the sun; `src/pbrt/hoseksamplingdata.cs`, Apache 2.0); the nearest
  is taken. Beside the paper: a twentieth of the samples are uniform over the sky, and a ground is sampled uniformly.

Table against mixture, test scene, `path`, 96 by 60 pixels:

| Scene | Mean, 2048 spp: table / tgmm | RMS error at 32 spp over the mean: table / tgmm |
|---|---|---|
| Sun at 37.5 degrees, with the disc | 8129.46 / 8129.32 | 0.0721 / 0.0720 |
| Same, without the disc | 5333.48 / 5333.34 | 0.0409 / 0.0411 |
| Sun at 6 degrees, without the disc | 2708.77 / 2708.61 | 0.0341 / 0.0345 |

The noise is the same; the mixture renders about 5% slower here (26.9 s against 25.7 s at 2048 spp) and keeps 25
numbers where the table keeps 16384. The largest weight a sample of the sky can have, over the mean, is 1.3 with the
mixture and 3.8 with the table (sun at 23 degrees, turbidity 3.5, one wavelength).

Validation: 111 values of the sky radiance and of the solar radiance agree with the authors' C code to 1e-11 and
1e-9. Test scene (ground, three objects, sun at 37.5 degrees, turbidity 3), image mean in cd/m^2: `path`, `volpath`
and polarised `path` 8126.9, `production` 8131.7, `bdpt` 8106.8; without the disc `path` 5334.8, `bdpt` 5324.1.
The sunlit ground of albedo 0.3 is 5900 cd/m^2, that is 62 klx.

### Polarisation

The Hosek model has no polarisation. `"string polarisation" "wilkie2004"` adds that of Wilkie, Ulbricht, Tobler, Zotti
and Purgathofer, "An Analytical Model for Skylight Polarisation" (EGSR 2004), a heuristic made to go with any sky
model: the light of the dome is linearly polarised at right angles to the plane of the sun and the viewing direction,
to the degree `exp(-(T - 0.6) / 4) / 1.2 * sin^2(gamma) / (1 + cos^2(gamma)) * (theta cos(theta) + (pi/2 - theta) I)`,
where `I` is an inverted, normalised Perez luminance with Preetham's coefficients. One degree for all wavelengths; no
neutral points; clamped to 0..1 (the formula can exceed 1 below a turbidity of about 2.5). The solar disc and the
ground stay unpolarised. Implemented from the paper; it agrees with the reduced formula that ART evaluates to 7e-6
(ART applies it to its Preetham sky only).

The largest degree along the sun's vertical (sun at 30 degrees): 0.79 at turbidity 2.5, 0.54 at 4, 0.25 at 7, 0.12
at 10, each within a few degrees of 90 degrees from the sun.

Integrators: a ray that leaves the scene asks `PolarisedEmission.AtInfinity`, a light sample `AtPoint`; `path`,
`volpath`, `bdpt` and `production` with `"bool polarised" true` carry it.

Check: the sky seen in level glass (n 1.5) at Brewster's angle, where only the light polarised across the plane of
incidence is reflected, turbidity 3, polarised sky over unpolarised sky:

| Sun | Expected | `path` | `volpath` | `bdpt` | `production` |
|---|---|---|---|---|---|
| In the plane of incidence, 90 degrees from the reflected direction (1 + D, D = 0.5182) | 1.5182 | 1.5178 | 1.5178 | 1.5178 | 1.5180 |
| At the horizon to the side (1 - D, D = 0.4360) | 0.5640 | 0.5644 | 0.5644 | 0.5644 | 0.5643 |

Tried and removed: the same formula with the radiance of the Hosek model, at each wavelength, in the place of
Preetham's luminance (the paper's "parent sky model"). It rendered correctly, but the degree hardly depended on the
wavelength, where it did the blue was the more polarised (the reverse of the real sky), and for a high sun in a clear
sky the formula went above 1 over part of the dome.
