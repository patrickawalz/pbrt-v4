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

* `"table"` (default): a table of the sky's luminance over the sphere (128 by 128, equal-area, with a floor). Without
  light below the horizon (no ground, or a black one) a direction that falls below is mirrored in the horizon, so no
  sample is without light (see "Sampling and bdpt" below; this holds for `"tgmm"` and for the solar disc too).
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

### Other stars

    LightSource "hosek" "vector3 sundir" [ -0.25 1 0.22 ] "float turbidity" 3 "float startemperature" 3500

`"float startemperature"` (K; absent or 0: the sun) gives the sky of an earth-like world of another star: the "alien
world" mode of the authors' implementation (Wilkie and Hosek, "Predicting Sky Dome Appearance on Earth-like Extrasolar
Worlds", SCCG 2013), ported from the same BSD source. The star is a blackbody; each of the eleven wavebands of the
sky and of the disc is scaled by what the star emits there over the solar spectrum the model was fitted with. The
disc gets the size at which the star sends as much to the world as the sun to the earth, times `"float
starintensity"` (default 1), so a cool star is large in the sky and a hot one small:

| Star | Disc across |
|---|---|
| the sun | 0.51 degrees |
| 3200 K | 3.0 degrees |
| 4200 K | 1.2 degrees |
| 9000 K | 0.21 degrees |

The authors' caveats hold: a blackbody star, the sun's limb darkening, the pattern of the sky unchanged by the size
of the disc, and intensities far from 1 are not what the model is for. A blackbody of 5800 K at intensity 1 is not
exactly the sun (luminous irradiance of a level ground 90.7 against 91.7). A sky of two stars is two `hosek` lights
whose intensities add up. A disc of 90 degrees across or more is an error.

Checks: 112 rows of `arhosekskymodelstate_alienworld_alloc_init` (sky radiance to 1e-11, radius of the disc to 1e-12,
solar radiance to 1e-9; `HosekSky.OtherStarIsTheAuthors`); the light by its own samples, and twice the intensity is
twice the light (`HosekSky.OtherStarLight`); the test scene under a star of 3500 K, mean of the lower half: `path`
5472.7, `volpath` 5471.6, `bdpt` 5466.8, `production` 5478.3. The tgmm fits are those of the sun's sky; they stay valid
as a sampling density. The polarisation heuristic is unchanged.

## `prague`: the sky of Wilkie, Vevoda et al.

    LightSource "prague" "string filename" "PragueSkyVisible.dat" "vector3 sundir" [ -0.5 -0.6 0.6 ]
        "float visibility" 60 "spectrum albedo" [ 300 0.3 900 0.3 ] "string polarisation" "dataset"

The fitted model of Wilkie et al. 2021 ("A fitted radiance and attenuation model for realistic atmospheres") with the
wide-spectrum datasets of Vevoda et al. 2022, for an observer on the ground. `pragueskymodel.cs` is a port of the
authors' reference implementation (Apache 2.0): the reader, the sky radiance, the solar disc, the polarisation and the
transmittance. `praguesky.cs` is the light; what it shares with `hosek` (sampling, ground, solar disc, polarised
emission) is in `skydomelight.cs`.

| Parameter | Default | |
|---|---|---|
| `string filename` | (required) | A dataset of the model (below) |
| `vector3 sundir` | | To the sun; down to 4.2 degrees below the horizon (twilight) |
| `vector3 up` | `0 0 1` | The zenith |
| `float visibility` | 59.4 | Meteorological range at the ground, km; the datasets hold 20 (hazy) to 131.8 (very clear) |
| `spectrum albedo` | 0.3 | Of the ground |
| `bool sun` | true | The solar disc: the sun above the atmosphere times the model's transmittance; no limb darkening |
| `bool ground` | false | A ground of the albedo below the horizon, lit by the sky and the sun |
| `float scale` | 1 | |
| `string polarisation` | `none` | `dataset`: the polarisation that the dataset holds |
| `float altitude` | 0 | Height of the observer above the ground, metres, to 15000; needs a dataset with altitudes |

Above the ground the model gives the light of every direction: below the horizon it is the ground and the air in
front of it, as the model has them, and `ground` is not used (an error with `altitude`).

Values outside what the dataset holds are held at its ends. The radiance is in the units of the thermal emitters, so
the Y of the film is cd/m^2. A wavelength has the radiance of the 40 nm channel it is in: the model does not
interpolate between channels (the reference does not either).

### Datasets

The datasets are not in the repository. The authors publish them (Zenodo record 19140728, Apache 2.0); copies and
the files made from them are on the release `prague-sky-datasets-v1` of this repository.

| File | Channels | Altitudes | Polarisation | Size |
|---|---|---|---|---|
| `PragueSkyModelDatasetFull.dat` (on the release in two parts) | 11, 320 to 760 nm | 22, to 15 km | yes | 2397 MB |
| `PragueSkyModelDatasetSWIR.dat` | 55, 280 to 2480 nm | ground | yes | 574 MB |
| `PragueSkyModelDatasetGround.dat` | 11, 320 to 760 nm | ground | no | 108 MB |
| `PragueSkyVisible.dat`: the wide one trimmed to 360..830 nm | 12, 360 to 840 nm | ground | yes | 154 MB |
| `PragueSkyGround14.dat`: the wide one trimmed to 280..839 nm | 14, 280 to 840 nm | ground | yes | 173 MB |
| the full one and `PragueSkyGround14.dat` combined | 11 and 14 | 22 and ground | yes | 2571 MB |

A release file cannot be larger than 2 GiB, so the full dataset is there as two parts and the combined file is not
there at all:

    cat PragueSkyModelDatasetFull.dat.part1 PragueSkyModelDatasetFull.dat.part2 > PragueSkyModelDatasetFull.dat
    praguedata combine PragueSkyFullWide.dat PragueSkyModelDatasetFull.dat PragueSkyGround14.dat

(on Windows `copy /b part1 + part2 PragueSkyModelDatasetFull.dat`; its MD5 is dcfa2bd165cf803c1791c469e05d134c).

### Where to put a dataset

Download one file from the release page, <https://github.com/patrickawalz/pbrt-v4/releases/tag/prague-sky-datasets-v1>,
and put it in the directory of the program (where `pbrt.dll` / `pbrt.exe` and, for the viewer, `magiview.exe` are).
`PragueSkyVisible.dat` (154 MB) is the one for an observer on the ground, with polarisation. Do not rename it.

The light looks for the dataset in this order:

1. `"string filename"` as given: beside the scene file, or a whole path;
2. a file of that name in the directory of the program;
3. a file of that name in the directory named by the environment variable `PRAGUE_SKY_DATASETS`.

Without `"filename"` it takes the first of `PragueSkyFullWide.dat`, `PragueSkyVisible.dat`, `PragueSkyGround14.dat`,
`PragueSkyModelDatasetSWIR.dat`, `PragueSkyModelDatasetFull.dat`, `PragueSkyModelDatasetGround.dat` that is in one of the
three places (with `"altitude"` only the two that have altitudes, with `"polarisation"` not the last). So with a dataset
beside the program `LightSource "prague" "vector3 sundir" [ ... ]` is enough in a scene. When none is found the error
names the directories that were searched.

Every dataset of the table is read (each rendered from the same scene: the three wide ones and the two trimmed ones give
the same image, the two of 11 channels differ from it by under 1%).

The light reads only what it needs: the 16 configurations around its sun elevation, visibility, albedo and altitude,
for the channels the film can sample (360 to 830 nm), by seeking; nothing else is loaded.

### `praguedata` (`cmd/praguedata.cs`)

    praguedata info <file>
    praguedata trim <input> <output> [first nm] [last nm]
    praguedata combine <output> <input> <input> ...

* `info`: a line for each dataset of a file: channels, altitudes, sun elevations, visibilities, polarisation, size.
* `trim`: the input with only the channels that hold a wavelength of the range (default 360 to 830), everything
  else unchanged. The result is a dataset of the same format: the authors' reference implementation reads it, and the
  values are bit-identical to the whole dataset (`PragueSky.TrimKeepsTheChannels`).
* `combine`: the inputs one after another in one file. The format has one set of channels for all its altitudes, so
  the full dataset cannot itself hold wider channels for the ground; a combined file holds both datasets whole. A
  reader of one dataset (the reference implementation, LuxCore) finds the first and ignores the rest: with the full
  dataset first, the combined file is still the full dataset to them (checked with the reference). The `prague` light
  takes, among the datasets that have the altitude of its observer, the one with the most channels of what the film
  samples: on the ground the wide channels, above it the full dataset. Each is used exactly as from its own file
  (`PragueSky.CombineKeepsTheDatasets`; renders from the combined file are identical pixel for pixel to those from the
  single files). A dataset without polarisation ends where its transmittance ends, so it can only be the last of a
  file; `combine` refuses another order.

### Polarisation

`"dataset"`: the dome is linearly polarised to the signed degree of the dataset, per channel: positive across the
plane of the sun and the viewing direction, negative in it (the real sky has both, with neutral points between). The
solar disc and the ground are unpolarised. A dataset without polarisation is an error with this option.

### Checks

* The model against the authors' code compiled as it is: 70 random rows on the wide dataset and 40 on the ground
  dataset agree: solar radiance and transmittance to 1e-9 relative, sky radiance and polarisation to 2e-6
  (`PragueSky.ModelIsTheAuthors...`). The tests look for the datasets in the directory `PRAGUE_SKY_DATASETS` and say
  so and pass without it.
* With the observer at an altitude and directions below the horizon: 80 rows on the full dataset, to the same
  tolerances (`PragueSky.ModelIsTheAuthorsOnTheFullDataset`). The light at 800 and 9000 m: density against samples,
  the measure of its samples the whole sphere, its own samples against uniform directions (`PragueSky.LightAloft`).
  The test scene at 3000 m, mean of the lower half: `path` 7141.7, `volpath` 7143.7, `bdpt` 7140.9, `production`
  7153.6.
* The light: density against samples, radiance of a sample against a ray and against the model, the light's own
  samples against uniform directions, sun at 41, 3 and -3 degrees, with and without sun and ground, both datasets
  (`PragueSky.LightSamplesWhatItEmits`); the disc (`PragueSky.SolarDisc`: 1.2073 +- 0.0031 by samples, 1.2068 by the
  model); the polarisation and its frame through `PolarisedEmission` (`PragueSky.LightPolarisation`).
* Integrators, the test scene, visibility 60, mean of the lower half of the image, five seeds each: `path`
  6688.5 +- 3.1, `bdpt` 6690.4 +- 2.1; one seed: `volpath` 6691.1, `production` 6695.1. Without the sun: `path` 1570.7,
  1570.4, 1568.8; `bdpt` 1568.9, 1572.2, 1570.9.
* Polarisation, the sky in level glass at Brewster's angle (as for `hosek`), luminance, polarised over unpolarised:

| Sun | Expected | `path` | `volpath` | `bdpt` | `production` |
|---|---|---|---|---|---|
| In the plane of incidence | 1.3463 | 1.3459 | 1.3459 | 1.3460 | 1.3459 |
| At the horizon to the side | 0.3085 | 0.3090 | 0.3090 | 0.3090 | 0.3090 |

  (The degree depends on the wavelength here: red 0.293, green 0.303, blue 0.365 for the second row, so the ratio
  has to be taken in luminance.)

A `prague` sky costs about twice the time of a `hosek` sky in these scenes (the evaluation of the model).

## Sampling and bdpt

Found with the `prague` light, and present in the `hosek` light as first committed: `bdpt` came out 0.9% darker than
`path` on surfaces lit by the sky. The table gave a small density to directions below the horizon, which carry no
light without a ground. `bdpt` (as in pbrt-v4) does not take its light samples (s = 1) for a camera sample whose walk
from the light did not start, and the walk does not start when the emitted ray carries no light; its weights assume
the light samples are always taken. The loss followed the share of dark directions (0.4% with no floor, from the
cells that straddle the horizon; 7.8% with a floor of 50%). `path`, `volpath`, `production` and `simplepath` were
never affected, and pbrt's own image light is not either, because its density is zero where its image is.

Fixed in the lights, not in `bdpt`: without light below the horizon a sampled direction that falls below is mirrored
in the horizon, and the density above is that of the direction plus that of its mirror image. Every sample then
carries light. After the fix `hosek`: `path` 1800.1, `bdpt` 1798.1 (before: 1800.2, 1783.9).

`bdpt` itself still has this behaviour for any light that can emit a ray without light with nonzero probability.
