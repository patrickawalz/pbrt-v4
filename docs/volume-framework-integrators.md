# volpath and bdpt on the volume framework

Not in pbrt-v4. Since this change no integrator walks a medium by itself: a ray in a medium is given to the volume of
that medium (`Volume.Integrate()`, `productionvolume.cs`, after Fong et al., "Production Volume Rendering", SIGGRAPH
2017 course), which answers with the next point of the path and the weight of the step.

| Integrator | Before | Now |
|---|---|---|
| `production` | `Volume.Integrate()` | unchanged |
| `volpath` | its own loop of delta tracking (the shared walk, `cpu/pathwalk.cs`) | the path tracer of the framework: `VolPathIntegrator` is a `ProductionPathIntegrator` |
| `bdpt`, and `mlt` through it | the shared walk | the shared walk, whose ray in a medium is `Volume.Integrate()` |

## volpath

`Integrator "volpath"` and `Integrator "production"` are one path tracer (`cpu/productionpath.cs`). Every medium is
integrated the way its own parameters say: `scattering`, `tracker`, `transmittance`, `spectral`, `distancesampling`,
`lightsampling`, `doublescattering`, `oncemore`. Under `volpath` these were read and, except for `tracker` and
`transmittance`, ignored.

What the path tracer of the framework took over from volpath: `bool regularize`, the visible surface of the first hit
(for the denoising buffers) and the subsurface scattering of a BSSRDF.

What volpath lost, because it was the integrator and not the volume that did it:

| Parameter of `volpath` | Now |
|---|---|
| `bool equiangular`, `integer equiangularsamples`, `integer equiangulardepth` | A warning, ignored. The volumes sample their distances: `"string distancesampling" "equiangular"` or `"mis"` on the medium. That takes point, spot and area lights; a laser beam is not among them, so a beam in fog is found by the light samples of the scatterings alone under `volpath` (`bdpt` has its own equiangular strategy, with lasers) |
| `bool uniformdistance` | A warning, ignored. Not in the framework |
| `bool shadowaware` (the atmosphere medium) | A warning, ignored. Not in the framework yet |

## bdpt and mlt

A bidirectional renderer keeps the densities of a path in its vertices and weighs its strategies by them, so it cannot
take a volume as a closed box. It asks for the walk of collisions (`VolumeContext.tracked`) and makes the multiple
scattering volume that tracks (`MultiScatterHeterogeneousVolume`), whatever the medium. That volume

* reports every collision to the renderer (`VolumeServices.Collision`), with the weight of its walk so far: the camera
  subpaths gather the emission of a medium there, with the weights of the strategies, as before;
* leaves what the segment is to the spectral weights of the strategies in the context: `segU`, `segL`, `vtx`, which the
  walk kept by itself before;
* gives the coefficients of the medium at the scattering (`VolumeGeometry.properties`).

`WalkServices` (`cpu/pathwalk.cs`) is what the walk gives the volume: the hit that the walk found already, the random
numbers of the sampler, and no light samples (the strategies of the renderer are its light samples).

Limits in bdpt and mlt, each with a warning at the first such medium:

* `"string scattering"` other than `"multiple"` is taken as `"multiple"`, as it always was in bdpt;
* `"string spectral" "tracking"` is taken as `"hero"`. Spectral tracking has no wavelength that samples for the others,
  and the weights of the strategies are made of the densities of that wavelength: with it bdpt rendered 1.45% too
  bright in a test, so it is not used there.

`tracker` and `transmittance` are honoured as before.

## Checked

Renders before and after (Magi, a C# port of pbrt-v4; independent sampler). "Old" is the build before the change.

| Scene | old volpath | new volpath | old bdpt | new bdpt |
|---|---|---|---|---|
| sphere of a chromatic medium | 0.2039 | 0.2040 | 0.2033 | 0.2036 |
| SGGX fibres, specular | 0.1453 | 0.1450 | 0.1450 | 0.1454 |
| SGGX fibres, diffuse | 0.2312 | 0.2311 | 0.2315 | 0.2316 |
| density grid | 0.1906 | 0.1918 | 0.1913 | 0.1913 |
| Cornell box in fog | 0.3045 | 0.3056 | 0.3057 | 0.3057 |
| fog with surfaces | 0.3780 | 0.3801 | | |
| surfaces only (two scenes) | 0.5750, 0.5906 | 0.5753, 0.5908 | | |

* At high sample counts the density grid is 0.19130 (old volpath) against 0.19127 (new), the Cornell box in fog
  0.30521 against 0.30550.
* The new volpath has the pixels of the old `production` render of the same scene, to the bit.
* The atmosphere medium with the sun in view is dominated by fireflies in both; with the brightest 1% of the values
  clipped the two agree within 1% (4% in the blue of one sunset scene at 512 samples).
* bdpt: furnace 0.7290 before, 0.7288 after; a medium with `"spectral" "tracking"` 0.5463 against volpath's 0.5465;
  polarised (S1 of a Rayleigh medium) -0.00485 before, -0.00495 after, volpath -0.00487.
* mlt on an emissive grid: 0.03018 before, 0.03023 after.

The OSL volume tests compared an OSL medium with pbrt's homogeneous medium at the same seed and asked for the same
image to 2e-4, which held while both went through the same loop with the same random numbers. Now the homogeneous
medium is integrated in closed form and the OSL medium by tracking, so that assertion was removed; the comparison
against an independent render of the reference, which was there already, stays. Its limit on the means was 1%, which
the same random numbers made easy; the renders are independent now, and it is 1% beyond what two seeds of the reference
differ by (in the three scenes with fireflies those are 1.5, 1.6 and 2.9% apart at 64 samples, and the OSL render 1.1,
1.3 and 1.9% from the reference). At 1024 samples with the fireflies clipped, the camera in fog is 0.2994, 0.3309,
0.3488 (old volpath), 0.3002, 0.3322, 0.3509 (new, pbrt medium) and 0.2996, 0.3309, 0.3488 (new, OSL medium).

Tests: the render, media, polarisation, camera, light and OSL suites, 394 tests, pass.

## Not on pbr-progressive and pbr-simd yet

There `volpath` also assigns what it gathers to the light layers and leaves the stray light of the lights to the camera;
the path tracer of the framework does neither on those branches. Both have to go into it before `volpath` can become it
there.

## Found on the way, not changed

`bdpt` and `mlt` render no emission of a medium that is not sampled as a light (`"bool emission_nee"`): a scene lit by
a homogeneous or grid medium alone is black, where `volpath` renders it. That was so before this change.
