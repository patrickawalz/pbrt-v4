# Polarisation in the integrators

Polarisation is an option of the integrators, `"bool polarised" true`, with the sensor parameters of `polpath`
(`"analyser"`, `"measure"`). There is no integrator to choose for it:

| Integrator | Polarised | Notes |
|---|---|---|
| `path` | yes | `"bool polarised"` makes it the polarised path tracer (`polpath` is the same integrator) |
| `volpath` | yes | with `"equiangular"` and `"uniformdistance"` (they were scalar only) |
| `production` | yes | with every strategy of its volumes (below) |
| `bdpt` | yes | with `"equiangular"` (it was scalar only) |
| `mlt` (bdpt estimator) | yes | through the connections of `bdpt`; measured through an `"analyser"` (a signed `"measure"` such as `s1` is not a quantity that Metropolis sampling can carry); not validated to the precision of the others |
| `randomwalk`, `simplepath`, `simplevolpath`, `lightpath`, `sppm` | no | the teaching integrators of pbrt-v4 and the biased photon mapper; scalar |

## What is shared

- `PolarisedPathIntegrator.ConnectionAttenuation`: the Mueller matrix of a pair of directions at a surface, with the
  scalar BSDF divided out: the matrix of the BxDF where it gives one, that of the rough conductor and dielectric of pbrt
  otherwise, a depolariser for the rest. Light samples in `polpath`, `volpath` and `production`, and the connections of
  `bdpt`, use it. (`bdpt` depolarised every surface connection before, and `volpath` did not ask the BxDFs for theirs.)
- `PolarisedEmission`: the Stokes vector of what a light emits. Lights are unpolarised except the glowing surfaces
  (`AreaLightSource "thermal"`). Every polarised integrator asks it at hits, at light samples and, in `bdpt`, at the
  start of the light subpaths.
- `PathState` (the walk of `volpath` and `bdpt`): the Mueller matrices of sampled bounces.

## The production path

The scalar path is untouched: each strategy still returns the spectrum that it adds. A strategy that knows how its
light was scattered records the same quantity as polarised light in `PolarisedGather` (`VolumeContext.polar`); when the
path adds the spectrum it takes what was recorded as the polarised part and the rest as unpolarised light. So a strategy
that records nothing keeps the right intensity, and the intensity of the polarised render is that of the scalar one.

Recorded: light samples at surfaces and in volumes (`SampleLd`), the sampled emission of media scattered at a point
(`SampleEmission`), single scattering inside a volume (`DirectLighting`: the light sample, the direction of the phase
function reaching a light, and the emission that it finds), `DoubleScattering`, `EquiangularShare`, `JointShare` and
once-more scattering from a collision or from a surface (`OnceMoreFinish`). The path keeps the product of the matrices of
its own scatterings.

## Tests

The furnace (a glowing sphere at 1400 K in a black enclosure at 1400 K: the sphere must vanish, I = 8465, S1 = S2 = 0):
the table is in `thermal-emission.md`.

A sphere of Rayleigh gas (`"float rayleigh" 1`) lit by a point light above it and seen from the side; mean over the
disc of the sphere, green channel, 256 samples (`bdpt` 64), independent sampler:

| Scene, depth | Integrator and volume settings | I | S1 | S1 / I |
|---|---|---|---|---|
| homogeneous, 1 | `volpath` | 0.0545 | -0.0504 | -0.926 |
| | `production`, `"scattering" "single"` | 0.0544 | -0.0504 | -0.926 |
| homogeneous, 2 | `volpath` | 0.0825 | -0.0674 | -0.817 |
| | `production`, single with `"doublescattering"` `"plain"` / `"joint"` / `"mis"` | 0.0827 / 0.0826 / 0.0827 | -0.0675 / -0.0674 / -0.0675 | -0.817 |
| homogeneous, 3 | `volpath` | 0.0967 | -0.0732 | -0.757 |
| | `production`, multiple scattering | 0.0970 | -0.0733 | -0.756 |
| | ... `"distancesampling" "mis"` / `"equiangular"` | 0.0970 / 0.0969 | -0.0734 / -0.0734 | -0.757 |
| | ... `"doublescattering" "joint"` | 0.0969 | -0.0733 | -0.756 |
| | ... `"oncemore" "omnee"` / `"omnee2"` | 0.0967 / 0.0971 | -0.0732 / -0.0734 | -0.757 / -0.756 |
| cloud, 3 | `volpath`, plain / `"equiangular"` | 0.0976 / 0.0985 | -0.0775 / -0.0782 | -0.794 |
| | `production`, plain / `"mis"` / joint `"mis"` / `"omnee2"` | 0.0981 / 0.0983 / 0.0983 / 0.0982 | -0.0779 / -0.0781 / -0.0783 / -0.0780 | -0.795 |
| with a floor, 3 | `volpath`, plain / equiangular / uniform / both | 0.1588 / 0.1591 / 0.1590 / 0.1585 | -0.0703 / -0.0704 / -0.0704 / -0.0706 | -0.443 |
| | `production`, `"omnee"` / `"mis"` + `"joint"` | 0.1587 / 0.1585 | -0.0702 / -0.0702 | -0.443 |
| | `bdpt`, plain / `"equiangular"` | 0.1583 / 0.1577 | -0.0693 / -0.0693 | -0.438 / -0.440 |
| lit by a glowing rough gold sphere, 3 | `volpath` / `bdpt` | 0.0148 / 0.0146 | 0.0083 / 0.0083 | 0.562 / 0.566 |
| | `production`, plain / `"mis"` + joint `"mis"` | 0.0148 / 0.0149 | 0.0083 / 0.0084 | 0.563 / 0.562 |

The atmosphere medium (sun at 30 degrees, depth 3, two seeds of 128 samples, mean of the frame): `volpath` I = 0.1226,
0.1503, 0.2084 and S1 = 0, 0.0007, 0.0030 for red, green and blue; `production` 0.1226, 0.1506, 0.2082 and 0, 0.0007, 0.0030.

The scenes are generated by `renders/pbr/glow/furnace/gen.py` and `renders/pbr/polvol/gen.py` (outside the repository).

## Found on the way (scalar)

- `bdpt` with `"equiangular"` sampled its equiangular vertex in a medium behind a boundary, where the other strategies
  do not count it: about 12% too much light for a bounded medium seen from outside. The vertex is now only sampled on
  the piece of the segment that starts at the vertex, as the weights have it (`PathWalkHook.OnBoundaryPassed`).
- `bdpt` renders an emissive homogeneous medium black when the scene has no other light. Not changed.
