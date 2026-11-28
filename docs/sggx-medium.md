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

What changes:

* The extinction depends on the direction of travel w: the medium's `sigma_a` and `sigma_s` are multiplied by the
  projected area of the flakes, sqrt(w^T S w), which is at most 1 for `fibre` and `surface`. The albedo is the medium's.
* The phase function is that of specular flakes, D(h) / (4 sigma(wo)), sampled exactly through the distribution of
  visible normals.
* With polarisation a scattering has the Mueller matrix of the reflection by the flake, divided by its reflectance.

How it is done:

* `Medium.SamplePoint(p, w, lambda, time)` is new: the properties at a point for light that travels along w. The
  integrators and the trackers call it with the direction of the ray that they follow; a medium that does not care
  about the direction answers as before. `SGGXMedium` wraps the medium and scales what it answers.
* Along one ray the direction is constant, so the majorant of the ray is that of the medium inside times the projected
  area for the ray: as tight as before.

Checked (`sggxmedium_test.cs`, and renders):

* the phase function integrates to 1 (within 4e-6) and the projected area is that of the normals; the samples have their
  density; projected area times phase function is the same both ways;
* a furnace (albedo 1 in a white environment) renders 1.000 with `volpath` and 0.9995 to 1.003 with `bdpt`;
* flakes of roughness 1 render as the same medium with the isotropic phase function (0.2057 against 0.2057);
* a fibre medium has the same mean with `volpath`, `bdpt`, `mlt` and `production` (0.1360, 0.1360, 0.1365, 0.1360), and with
  `volpath` and `bdpt` polarised.

Not in it:

* what asks for the properties at a point without a direction: the samplers of emitting media and the estimators of
  `productionvolume.cs` that work from a point of the volume context. They stop with a message when they meet this
  medium; an emitting medium cannot be given microflakes.
* the diffuse flakes of the paper (their phase function has no closed form);
* a matrix that varies in the medium (an orientation field from a texture or a grid).

Scenes: `scenes/sggx/sggx_{none,fibre_x,fibre_z,surface_z}.pbrt`, a block of the same medium that differs only in its flakes.
