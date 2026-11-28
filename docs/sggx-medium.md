# Microflake media (SGGX)

Heitz, Dupuy, Crassin, Dachsbacher, "The SGGX Microflake Distribution" (ACM Transactions on Graphics 34(4), 2015), as a
volume in the manner of Mitsuba. Code: `src/pbrt/sggxmedium.cs`. Not in pbrt-v4.

Any medium becomes a medium of microflakes with these parameters on its `MakeNamedMedium`:

    MakeNamedMedium "cloth" "string type" "homogeneous" "rgb sigma_a" [0.15 0.4 1.2] "rgb sigma_s" [6 6 6]
        "string microflakes" "fibre" "vector3 flakeaxis" [1 0 0] "float flakeroughness" 0.2

| Parameter | Default | |
|---|---|---|
| `string microflakes` | (none) | `fibre`: flakes around fibres that run along the axis. `surface`: facets whose normals are around the axis. `matrix`: the SGGX matrix itself |
| `vector3 flakeaxis` | `0 0 1` | The axis, in the space of the medium |
| `float flakeroughness` | 0.3 | 1: the same in every direction; toward 0: perfectly aligned |
| `float sggx` | | For `matrix`: Sxx Syy Szz Sxy Sxz Syz, positive definite |
| `spectrum flakek` | (perfect mirror) | Polarisation only: the flakes are mirrors of the conductor 1 + i k |
| `string flakes` | `specular` | `diffuse`: each flake scatters as a matte surface |
| `string flakeaxisfield` | | Three float textures (by name): the x, y and z of the axis at every point, in the space of the medium. In place of `flakeaxis`; where the three are zero the axis is `flakeaxis` |
| `string flakeroughnessfield` | | A float texture (by name): the roughness at every point (kept within 0.01 and 1). In place of `flakeroughness` |

The fields are for `fibre` and `surface`. The textures are looked up at the point alone (a texture of three dimensions:
`checkerboard` with `dimension` 3, `fbm`, `marble`, a 3D noise), and are defined as any float texture, before or after
the medium:

    Texture "axis-x" "float" "checkerboard" "integer dimension" 3 "float tex1" 1 "float tex2" 0
    Texture "axis-y" "float" "constant" "float value" 0
    Texture "axis-z" "float" "checkerboard" "integer dimension" 3 "float tex1" 0 "float tex2" 1
    MakeNamedMedium "cloth" "string type" "homogeneous" "rgb sigma_a" [0.15 0.4 1.2] "rgb sigma_s" [6 6 6]
        "string microflakes" "fibre" "string flakeaxisfield" ["axis-x" "axis-y" "axis-z"] "float flakeroughness" 0.2

What changes:

* The extinction depends on the direction of travel w: the medium's `sigma_a` and `sigma_s` are multiplied by the
  projected area of the flakes, sqrt(w^T S w), which is at most 1 for `fibre` and `surface`. The albedo is the medium's.
* The phase function is that of specular flakes, D(h) / (4 sigma(wo)), sampled exactly through the distribution of
  visible normals.
* With polarisation a scattering has the Mueller matrix of the reflection by the flake, divided by its reflectance.
* Diffuse flakes: the phase function of the paper is an integral over the normals that the arriving ray sees, without a
  closed form. It is not tabulated and not estimated with a fixed number of samples. At a scattering one normal m is
  drawn from those that the arriving ray sees, and the phase function of that scattering is the cosine lobe around it,
  max(0, wi . m) / pi, for sampling and for connections alike. Its mean over the normals is the phase function of the
  paper, so the render is unbiased; the cost is some more noise at connections. They depolarise.

How it is done:

* `Medium.SamplePoint(p, w, lambda, time)` is new: the properties at a point for light that travels along w. The
  integrators and the trackers call it with the direction of the ray that they follow; a medium that does not care
  about the direction answers as before. `SGGXMedium` wraps the medium and scales what it answers.
* Along one ray the direction is constant, so the majorant of the ray is that of the medium inside times the projected
  area for the ray: as tight as before. With a field the matrix changes along the ray, and the majorant is that of the
  medium inside (the projected area is at most 1): more null collisions where the flakes are seen edge on.
* The normal of a diffuse flake is drawn from a generator seeded with the point and the direction of arrival, when the
  phase function is first asked: the same vertex answers with the same lobe every time, which the weights of `bdpt` and
  `mlt` need.
* `bdpt` (and `mlt` through it) took the density of a sampled direction at a medium vertex for the reverse density
  too, as pbrt-v4 does. That holds for a phase function of the angle alone. The microflake phase functions have
  another density the other way round, so the walk now asks the phase function for it (`integrators.cs`,
  `OnPhaseSampled`); nothing changes for the other phase functions. Before this the diffuse furnace with `bdpt`
  rendered 0.984; the specular one was within noise of 1 either way.

Checked (`sggxmedium_test.cs`, and renders):

* the phase function integrates to 1 (within 4e-6) and the projected area is that of the normals; the samples have their
  density; projected area times phase function is the same both ways;
* a furnace (albedo 1 in a white environment) renders 1.000 with `volpath` and 0.9995 to 1.003 with `bdpt`;
* flakes of roughness 1 render as the same medium with the isotropic phase function (0.2057 against 0.2057);
* a fibre medium has the same mean with `volpath`, `bdpt`, `mlt` and `production` (0.1360, 0.1360, 0.1365, 0.1360), and with
  `volpath` and `bdpt` polarised;
* diffuse flakes: the mean lobe of flakes without a direction is the phase function of a matte sphere,
  2 / (3 pi^2) (sin a + (pi - a) cos a), within 2.4e-4; for six matrices it is the integral of the paper within 9e-5, and
  projected area times mean lobe is the same both ways within 1.2e-4;
* the diffuse furnace renders 0.9998 with `volpath` and 0.9995 +- 0.0005 with `bdpt` (three seeds); the diffuse fibre
  medium 0.2310, 0.2316, 0.2293, 0.2311 with `volpath`, `bdpt`, `mlt`, `production`;
* fields of constant textures render as the uniform medium (0.2190 against 0.2187); a checkerboard field renders 0.2687
  with `volpath` and 0.2685 with `bdpt` (diffuse: 0.5621, 0.5616). `mlt` at 256 mutations per pixel is within 1.5% in
  the mean of the image and 5% in its centre, to either side.

Not in it:

* what asks for the properties at a point without a direction: the samplers of emitting media and the estimators of
  `productionvolume.cs` that work from a point of the volume context. They stop with a message when they meet this
  medium; an emitting medium cannot be given microflakes.
* a field for `matrix`, and a field from a grid of the medium itself (nanovdb, `grid`): the fields are float textures.

Scenes: `scenes/sggx/sggx_{none,fibre_x,fibre_z,surface_z,fibre_x_diffuse,field,field_diffuse}.pbrt`, a block of the same
medium that differs only in its flakes.
