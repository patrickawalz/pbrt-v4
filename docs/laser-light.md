# Laser light

`LightSource "laser"`: a Gaussian beam (TEM00 times a beam quality factor) of one spectral line, optionally polarised.
Source: `src/pbrt/laserlight.cs`, `src/pbrt/spectrallines.cs`; tests: `src/pbrt/laserlight_test.cs` (suite `LaserLight`).

```
LightSource "laser" "point3 from" [ 0 0 0 ] "point3 to" [ 0 0 1 ] "float power" 0.005 "float wavelength" 532
    "float waist" 0.0005
```

## Parameters

| Parameter | Default | Meaning |
|---|---|---|
| `point3 from`, `to` | 0 0 0, 0 0 1 | The centre of the plane the beam leaves, and a point on its axis |
| `float power` | 0.001 | Radiant power of the beam, watts |
| `float scale` | 1 | Multiplies the power |
| `float wavelength` | 532 | Centre of the line, nm (360 to 830) |
| `float linewidth` | 1 | Full width at half maximum of the line, nm |
| `float waist` | 0.5 mm | Waist radius w0 (1/e^2 of the irradiance), scene units |
| `float m2` | 1 | Beam quality factor: the far-field half angle is `m2 lambda / (pi w0)` |
| `float divergence` | | The far-field half angle in radians, instead of `m2` |
| `float waistdistance` | 0 | Distance from the plane of the light to the waist (a focused beam: positive) |
| `float metersperunit` | 1 | Size of a scene unit |
| `string polarisation` | `none` | `none`, `linear`, `circular` |
| `vector3 polarisationaxis` | 0 0 1 | Linear: the direction of the electric field (taken across the beam) |
| `float polarisationdegree` | 1 | Polarised part of the power |
| `float wavelengthfraction` | 0.5 | Part of the film's wavelengths that are taken from the line; 0 turns it off |

The radiance has the units of the thermal emitters (W m^-2 sr^-1 nm^-1 times 683): the Y of the film is cd/m^2.

## Model

At its waist the beam is the radiance `L0 exp(-2|x|^2/w0^2) exp(-2|s|^2/theta0^2) / cos^4`, with `x` the point, `s` the
slope of the ray and `L0 = 4P / (pi^2 w0^2 theta0^2)`: the Wigner distribution of the mode. Its rays give the irradiance
`2P / (pi w(z)^2) exp(-2 rho^2 / w(z)^2)`, `w(z)^2 = w0^2 + z^2 theta0^2`, at every distance. Both Gaussians end at six
radii (e^-72 of the peak). Sampling toward a point and from the light is exact (closed form, no tables).

The light has no surface, so integrators see it as a light at a position (weight one for its light samples). It is not
one point, though: the equiangular sampling of `bdpt` and the pivots of the production volumes leave it out
(`LaserLight.IsPointLight`).

## The beam in a medium (`volpath`, `"bool equiangular" true`)

The collisions of a tracker seldom fall in the beam. With `equiangular` on, `volpath` also samples the scattering point
of each segment where the ray crosses the beam (`LaserLight.SampleDistance`): the square of the distance of the ray from
the axis is a quadratic of the distance along it, so the irradiance along the ray is about a Gaussian, which is sampled
in closed form on the segment; a tenth of the samples are uniform over it. It is weighed against the collisions of the
walk as equiangular sampling is, with the density of the light point that the connection samples in both. With a laser
among the lights, half of the choices of the light of a segment are uniform among them (seen from the start of a
segment a laser lights nothing).

Fog and a laser only, 64 by 32 pixels, 1024 spp, against `bdpt`:

| Run | Image mean | RMS error of a pixel over the mean | Time |
|---|---|---|---|
| `bdpt`, 512 spp, 10 seeds | 0.05319 +- 0.00003 | | 24 s |
| `volpath`, 3 seeds | 0.0525 +- 0.0034 | 2.9 to 5.6 | 8.8 s |
| `volpath` `equiangular`, 12 seeds | 0.05330 +- 0.00010 | 0.37 to 0.41 | 13.8 s |

Not in `bdpt`'s own equiangular option or in `production`: a laser is left out of both.

## Wavelengths

The laser tells the film its line (`SpectralLineSampling`); `Film::SampleWavelengths` of the RGB/G-buffer and spectral
films then returns a mixture: `wavelengthfraction` of the wavelengths from the line, the rest as before, each with the
density of the mixture. Unbiased for everything in the scene; a scene without a laser is rendered as before. The
smooth spectra of the scene get fewer wavelengths, so their colour noise rises.

Changes outside the new files: the two `SampleWavelengths` and the buckets of the spectral film (`film.cs`),
`SampledWavelengths.FromSamples` (`util/spectrum.cs`), the name in `Light.Create`, `PolarisedEmission.AtPoint`
(`thermalemission.cs`), and three tests for a point light in `cpu/integrators.cs` and `cpu/productionpath.cs`.

## Validation

Spot on a diffuse floor (beam at 60 degrees, M^2 5, waist 1 m ahead), sum of Y times pixel area over the analytic
`rho 683 P / pi`, 64 spp: `path` 1.0005, `path` polarised 1.0005, `volpath` 1.0005, `production` 1.0005, `bdpt` 0.9996,
`lightpath` 1.0000, `mlt` 1.0046. Standard deviations of the spot 1.84 by 0.94 mm (expected 1.84 by 0.92 plus a pixel).

Glass plate at Brewster's angle, reflected light on a screen, `bdpt` polarised, depth 20, over the incident power:
s-polarised 0.2574 (exact 0.2577), unpolarised 0.1287 (0.1289), p-polarised 0.

Beam in fog alone, image mean over three seeds: `bdpt` 0.2233, `volpath` 0.2196 +- 0.005, `production` 0.2275 +- 0.004,
`mlt` 0.2237.

## `laser2`: any colour, or a slide

`LightSource "laser2"` (`src/pbrt/laserlight2.cs`, suite `LaserLight2`) is the same beam (`GaussianBeamLight`, the base
class of both lights) with any spectrum, or with an image across it.

```
LightSource "laser2" "point3 from" [ 0 0 0.3 ] "point3 to" [ 0 0 0 ] "rgb L" [ 1 0.15 0.6 ] "float power" 0.5
    "float waist" 0.002 "float divergence" 0.003
Texture "pattern" "spectrum" "checkerboard" "rgb tex1" [ 1 0.6 0.1 ] "rgb tex2" [ 0.1 0.3 1 ]
LightSource "laser2" ... "texture L" "pattern" "float imagewidth" 0.008 "vector3 up" [ 0 1 0 ]
LightSource "laser2" ... "string filename" "slide.png"
```

| Parameter | Default | Meaning |
|---|---|---|
| `spectrum L` / `rgb L` | the illuminant of the colour space | Spectrum of the beam |
| `float power` | 1 | Luminous power of the beam, in the photometric scale of pbrt's lights (as `power` of `spot`) |
| `float scale` | 1 | Multiplies it |
| `float waist` | 0.0005 | Waist radius, scene units |
| `float divergence` | 0.001 | Far-field half angle, radians, the same for all wavelengths |
| `float waistdistance` | 0 | As `laser` |
| `texture L` | | A named spectrum texture of the scene, as a slide in the plane of the light (square) |
| `string filename` | | An image as the slide, without a texture; not together with `L` |
| `float imagewidth` | four beam radii | Width of the slide, scene units; outside the slide nothing is let through |
| `vector3 up` | 0 1 0 | Top of the slide: seen along the beam with it up, it is upright |
| `polarisation`, `polarisationaxis`, `polarisationdegree`, `from`, `to` | | As `laser` |

A texture is asked at the point of the plane of the light in render space (so the textures of space work) with the
(u, v) of the slide, (0, 0) at its lower left. For this, lights have the named textures of the scene:
`Light.Create` takes them as a last, optional argument, and the scene makes its lights in `CreateLights`, after the
textures, where pbrt-v4 starts them while it reads the scene (`scene.cs`). A texture may be declared before or after
the light. Only `laser2` takes one so far.

No line is told to the film. With a slide, `power` is that of the beam before it, for a white slide. The slide is sharp
at the light and blurs by the distance times the divergence; the diffraction of its pattern is not modelled.

Spot on a diffuse floor, sum of Y times pixel area over `rho power / pi`, 64 spp: `path`, `volpath`, `production` and
polarised `path` 1.0008, `bdpt` 0.9994, `lightpath` 1.0000, `mlt` 1.0040. With a slide: `path` 0.3503, `bdpt` 0.3463,
`lightpath` 0.3484 of the white beam; the same image as an `imagemap` texture: `path` 0.3504 (the file: 0.3504), `bdpt`
and `lightpath` 0.3495. A checkerboard: `path` 0.4794, `bdpt` 0.4790, `lightpath` 0.4788. Marble: `path` 0.8294,
`lightpath` 0.8289. In fog alone: `bdpt` 0.04788 +- 0.00007, `volpath` with `equiangular`
0.04791 +- 0.00013.

## Limits

* Higher modes only through `m2`; no top-hat profile; no speckle or interference.
* A beam seen by way of mirrors or lenses needs `bdpt`, `lightpath` or `mlt`.
* `lightpath` has no `polarised` option.
* The lines are a process-wide list (as on the `glow-discharge` branch, whose `SpectralLineSampling` and `LineSpectrum`
  this file must be merged with).
