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

The radiance is W m^-2 sr^-1 nm^-1 times 683 (the units of the thermal emitters and of `laser`): the Y of the film is
cd/m^2, a sunlit ground about 6000 and the solar disc about 10^9, more than a half-float image holds. The model has
eleven wavebands from 320 to 720 nm; from 720 to 760 nm it falls to nothing, as in the authors' code. The light is
not polarised.

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
