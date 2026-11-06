# Animated transforms with more than two keys

pbrt-v4 animates a transform between two keys (`ActiveTransform` and `TransformTimes`). Magi keeps that syntax, unchanged and bit for bit, and adds N keys, modelled on the keyframe animation of the original Mitsuba (`AnimatedTransform` and `AnimationTrack` in `mitsuba/core/track.h`) with the bounds of pbrt's `AnimatedTransform`.

## What is taken from where

| | Mitsuba 0.5/0.6 | pbrt-v4 | Magi |
| --- | --- | --- | --- |
| keys | any number, each with a time | two | any number >= 2, each with a time |
| decomposition of a key | polar decomposition: translation, rotation (quaternion), scale | `Transform::Decompose` (polar decomposition by iteration) | pbrt's `Decompose` |
| between two keys | `T` and `S` linear, `R` slerp, per track, the segment found by binary search; the quaternion sign is flipped to take "the short way" | the same for the one segment | the same per segment |
| outside of the keys | clamped | clamped | clamped |
| bounds | **sampling** the animation at 100 times (approximate, not conservative) | interval arithmetic on the derivative of the motion of a point, Newton refinement of the roots: conservative, tight | pbrt's method per segment, **union over the segments** |

`AnimatedTransform` (`src/pbrt/util/transform.cs`) keeps the first and last key in `startTransform`/`endTransform` and `startTime`/`endTime` as before, so code that uses two keys (lights, media and textures take the first key, with the warning they always gave) is untouched.

### Interpolation

For `time` between `keyTime[k]` and `keyTime[k+1]`, with `dt = (time - keyTime[k]) / (keyTime[k+1] - keyTime[k])`:

```
T = (1 - dt) T[k] + dt T[k+1]
R = Slerp(dt, R[k], R[k+1] or -R[k+1])      the sign that makes the dot product >= 0: the shortest arc
S = (1 - dt) S[k] + dt S[k+1]
transform = Translate(T) * Rotate(R) * S
```

The sign of the quaternion is chosen **per segment**, relative to the quaternion at the start of the segment, so every segment is exactly pbrt's two key animation between its two keys (tested bit for bit). A segment rotates by at most 180 degrees: a rotation of more than that needs an intermediate key. That is the point of N keys: a bar that turns by 270 degrees over the shutter interval, given as start and end only, is interpolated by 90 degrees the other way round; with keys every 90 degrees it follows the exact circle.

Exactly at a key time the key transform itself is returned (bit for bit). Where two consecutive keys are equal the segment is static and returns the key.

### Bounds

* `MotionBounds(Bounds3f)` and `BoundPointMotion(Point3f)` are the union over the segments of the bounds of the segment.
* `SegmentMotionBounds(k, box)` and `SegmentBoundPointMotion(k, p)` give the bounds of one segment. They are tighter than the union when the path turns (an L shaped path of three segments: the three boxes have 33 units of volume in the test, the union box 1331). The BVH uses the union (`AnimatedPrimitive.Bounds()`), because a pbrt BVH has no time dimension. A time-split BVH or one primitive per segment would use the per segment boxes; this is not done.
* The method of pbrt is exact up to rounding: in the tests the bounds of 24 random animations (2 to 8 keys, 10^5 times, 8 corners each) are on average 1.00003 times the volume of the sampled bounds, at worst 1.0002 times. Sampled points lie outside of the bounds only by rounding errors of the order of 10^-7 of the size of the bounds (the original two key code of pbrt has the same), never by more than 8 units in the last place.

### Limits (inherited from pbrt, the same for two keys and N keys)

* A key that swaps the handedness (negative determinant of the 3x3 part): `Decompose` keeps the sign in the rotation ("XXX TODO FIXME deal with flip" in pbrt) and the quaternion made from a reflection is not that of the key, so the animation jumps close to the key (the key itself is exact at the key time). Mitsuba negates rotation and scale together to avoid this; that would change the results of existing two key scenes, so it was not done. Use `ReverseOrientation` / a positive scale for animated objects.
* A singular key (a scale of zero) cannot be decomposed: it is a fatal error in the constructor (as for two keys). Nearly singular scales work.
* Translation and scale are interpolated linearly, rotation along the arc. A body that orbits a point therefore moves on the chords of the orbit, not on the circle: use enough keys.
* Animated textures and the other lights (point, spot, distant, infinite, projection, goniometric) are not supported (as for two keys): the first key is used with a warning. Area lights on animated shapes and emissive `texture` / `osl` media do move: see "Moving area lights and emissive media" below. Animated vertices (deformation motion blur) and time-varying parameters are not supported.

## Scene syntax

The two key syntax is unchanged:

```
TransformTimes 0 1                       # options block only
ActiveTransform StartTime
Rotate 0 0 0 1
ActiveTransform EndTime
Rotate 270 0 0 1
ActiveTransform All
```

N keys, as in RenderMan (`MotionBegin [ t0 t1 ... tn ]` ... `MotionEnd`):

```
MotionBegin [ 0 0.3333333 0.6666667 1 ]
  Rotate   0 0 0 1
  Rotate  90 0 0 1
  Rotate 180 0 0 1
  Rotate 270 0 0 1
MotionEnd
Shape "bilinearmesh" ...
```

* The block holds one transformation call per time, in the order of the times. The calls must be all of the same kind: `Identity`, `Translate`, `Rotate`, `Scale`, `LookAt`, `Transform` or `ConcatTransform`. Between `MotionBegin` and `MotionEnd` no other directive is allowed (an error: no `AttributeBegin`, `Shape`, `ObjectInstance`, `CoordSysTransform`, `ActiveTransform`, `Include`, `WorldBegin`, nested `MotionBegin`...).
* The times must increase strictly and there must be at least two. They are absolute times, the ones the camera shutter samples: a ray at time `t` (between `shutteropen` and `shutterclose` of the camera) uses the transformation at `t`; outside of the first and last time the transformation is clamped.
* Errors: the number of calls differs from the number of times, kinds that differ, times that do not increase, `MotionEnd` without `MotionBegin`, a missing `MotionEnd`, a nested block.

### How the block composes with the CTM

Let `CTM` be the current transformation before `MotionBegin`. The result of the block is a CTM with `n` keys at the block's times; the key `i` is the call `i` applied to the CTM **as it is at time `t_i`**:

* `Translate`, `Rotate`, `Scale`, `LookAt`, `ConcatTransform`: `key_i = CTM(t_i) * call_i`, post-multiplication, as the same call outside of a block (so `Translate 0 0 5` before the block is a fixed offset of all keys, applied last, in world space).
* `Transform`: `key_i = call_i` (it replaces the CTM, as outside of a block). `Identity`: `key_i = identity`.
* If the CTM before the block is itself animated (two keys through `ActiveTransform`, or N keys) it is evaluated at `t_i` (decomposed and interpolated like any animation); if it is static, `CTM(t_i)` is the CTM.
* `ActiveTransform` has no effect between `MotionBegin` and `MotionEnd` (the block gives the transformation of every key). After the block it works on the n keys: `StartTime` modifies the first key only, `EndTime` the last key only, `All` every key (the keys in between are changed only by `All`). With two keys this is the existing behaviour.
* `TransformTimes` (options block) sets the times of the two key CTM. A CTM that comes from a block carries its own times and ignores it.

### Scope

* **AttributeBegin/AttributeEnd**: the keys and their times are part of the graphics state: `AttributeEnd` restores the CTM that was current at `AttributeBegin`, with its keys. A block must be closed inside the attribute scope it was opened in (a block cannot contain `AttributeEnd`).
* **CoordinateSystem / CoordSysTransform**: a named coordinate system stores all the keys and times; `CoordSysTransform` restores them (a static system makes the CTM static again). `"camera"` is stored with the keys of the camera.
* **ObjectBegin/ObjectEnd**: shapes in an instance definition get the keys of the CTM at the shape (relative to the world, as for two keys).
* **ObjectInstance**: the instance gets the keys of the CTM at the `ObjectInstance` as an `AnimatedPrimitive` (the same code as two keys, `InstanceSceneEntity.renderFromInstanceAnim`).
* **Camera**: `MotionBegin` is allowed in the options block; the CTM at `Camera` is camera-from-world and its inverse is animated with the keys. `CameraTransform` moves every key to render space, in any render space (`Option "rendercoordsys"`), with the render space translation of the camera-world space taken from the middle of the first and last time, as with two keys. `GBufferFilm`, the OSL camera/coordinate systems use the keys too.

### C# API

`new AnimatedTransform(Transform[] keys, Float[] times)` (throws `ArgumentException` unless the times increase strictly and there are two keys at least); the two key constructor is unchanged. The scene entities already hold an `AnimatedTransform` (`AnimatedShapeSceneEntity.renderFromObject`, `InstanceSceneEntity.renderFromInstanceAnim`, `CameraTransform`), so an N-key transform can be put into them without further change. `KeyCount`, `SegmentCount`, `GetKeyTransform(i)`, `GetKeyTime(i)`, `SegmentMotionBounds`, `SegmentBoundPointMotion` and `PreMultiply(Transform)` (all keys left-multiplied, the times kept; used for render-from-world) are new. `BasicSceneBuilder.MotionBegin(times)` / `MotionEnd()` are the `ParserTarget` entry points the parser calls.

## Tests and numbers

30 tests (`AnimatedTransformNKey.*`, `MotionBlock.*`); the whole suite (926 tests, `OslPbrt.RenderAllIntegrators` excluded) passes except the known `OperatorLeftShiftPrint.Basics`.

Micro benchmark (`AnimatedTransformNKey.BenchmarkInterpolateAndBounds`, same process, best of 5): `Interpolate` with two keys 592 ns old code, 591 ns new; `MotionBounds` (the BVH path, `AnimatedPrimitive.Bounds()`) with two keys 154 us old, 150 us new. Per extra key: `Interpolate` is flat (binary search over the keys, 610 to 670 ns from 2 to 32 keys); `MotionBounds` costs one segment more, about 110 to 140 us per segment (2 keys 85 us, 4 keys 365 us, 8 keys 734 us, 32 keys 4.2 ms), paid once at BVH build time.

Render check (`/tmp/ntransform_renders`, ground truth = mean of 16 static stills): mean absolute difference in linear radiance, N keys / 2 keys: spinning bar 0.0084 / 0.0353, bouncing ball 0.0060 / 0.0147, camera 0.0187 / 0.0716, instanced bars 0.0049 / 0.0115. For the bar, against 64 stills the 4-key render is 0.0045 (the noise of two seeds of the same render differ by 0.0059); the 16-still truth itself differs from the 64-still one by 0.0073, so most of the remaining 0.0084 is the time discretisation of the 16 stills.

`src/pbrt/animatedtransform_n_test.cs` (and, for the comparison, a copy of the old implementation in `animatedtransform_legacy_test.cs`): two keys bit for bit against the old code (24 000 interpolations and 2 000 bounds, 0 differences), keys reproduced exactly at key times, the 270 degree rotation (largest distance from the circle 6e-7, the two key path is 2 units of chord away at the middle), the union of the per segment bounds, conservative bounds with 10^5 times, parsing and its errors, a ray against a moving sphere through the BVH, a camera with three keys in both render spaces, and a micro benchmark.

## Moving area lights and emissive media

### What is supported

* **Area lights**: a `DiffuseAreaLight` (`AreaLightSource "diffuse"`) on a shape whose transformation is animated, with two keys (`ActiveTransform`) or N keys (`MotionBegin`), for every shape that can carry an area light (triangle, bilinear patch, sphere, disk, cylinder, curve...). The `ErrorExit` "Animated area lights are not supported" is gone. Emission, alpha, the image, `twosided` and `scale` are evaluated in the frame of the shape at the time of the sample or of the ray. A shape of an `ObjectBegin` definition that is animated carries no area light (the instance has none either, as before).
* **Emissive media**: `MakeNamedMedium` of type `"texture"` (`TextureMedium`, with its `VolumeEmissionLight`) and `"osl"` (`OSLMedium`) with an animated transformation. Every other medium type (`homogeneous`, `uniformgrid`, `rgbgrid`, `cloud`, `nanovdb`, `scaled`, ... ) keeps the start transformation and the warning `Animated transformation provided for a medium of type "x". Only the "texture" and "osl" media move; this one uses the start transform.`
* The other lights keep their warning and the start transformation.

### Semantics

* The time of a ray decides: the camera ray carries its time, every spawned ray keeps it, and `LightSampleContext.time` (new, filled from the interaction in every integrator: path, volpath, simplepath, bdpt, lightpath, sppm, mlt, randomwalk, ao and their OSL variants) is the time of the receiver. `SampleLi`, `PDF_Li`, `L` (emission at a hit, which `SurfaceInteraction.Le` calls with the time of the hit) and `SampleLe` all evaluate the emitter at that time, so the MIS weight of a BSDF sampled hit uses the density of the light at the time of the ray.
* The shape of an animated light stays in its own space (it is made with the identity). The light keeps the `AnimatedTransform`; each call maps the receiver into the shape space with the inverse of `Interpolate(time)`, samples or evaluates there and maps the result back. The solid angle density does not change under a rigid motion or a uniform scale, so no conversion is needed; the area of a uniformly scaled shape is multiplied by the square of the scale (`Phi`, `power`, the pdf of `SampleLe`).
* A light that does not move (an `AnimatedTransform` with equal keys) is the static light: `SetMotion` does nothing, the code path is the old one (one `animated != null` test) and the results are bit for bit those of the old code (also the renders, tested with `cmp`).
* Needs: all keys must be one rotation, one translation and **one uniform scale that is the same in every key** (`AnimatedTransform.IsSimilarity`). Otherwise (a squashing or changing scale) the light warns and uses the start transformation. As in pbrt, a scale on a non triangle shape (`HasScale`) still warns.
* Media: `renderFromMedium` of a medium is its first key. The density, the emission texture, the bounds and the majorant grid are defined in the medium as it is at the first key. A point of render space at time `t` is mapped back to the first pose with `renderFromMedium * Inverse(Interpolate(t))`, and `SampleRay` transforms the ray with the inverse of `Interpolate(ray.time)` so the majorant grid stays in medium space, which is exact for a rigid motion (tested: the extinction never exceeds the majorant of its segment, 20 000 rays at random times, 0 violations; `OSLMedium.Violations()` stays 0). A texture used as density must be defined with the CTM of the first key of the medium. The light of the emission (`VolumeEmissionLight`) samples a point of the volume at the time of the receiver and its bounds are the union over the shutter.
* OSL media: the shader is evaluated at the first pose point and direction (`sg.time` is the time of the ray); the world space `P` that a shader sees is the first pose position (the `"shader"` space is the medium, so shaders written in it move with the medium; a shader that reads world space does not).

### Bounds and light sampling: the conservative choice

The BVH light sampler has no time. The bounds of an animated area light are the union over the shutter and **do not depend on the time of the context**, so the probability of a light is the same in `Sample` and in `PMF` (the integrators call `PMF` for the MIS weights): no bias, tested with the BVH sampler at several times (the sum of the PMF is 1, the frequency of 10^6 draws equals the PMF).

* Spatial bounds: `AnimatedTransform.MotionBounds` of the bounds of the shape.
* Normal cone: the cone of the shape (axis and half angle in shape space) is rotated to the time where the accumulated rotation is half of the total; the half angle is the cone of the shape plus half the total rotation plus 1e-5 rad. A pure translation keeps the cone (widened by 1e-5 only); a total rotation of pi or more, or a scale that is not a similarity, gives the whole sphere. The cone is a bound, not the tightest cone: a spinning quad of 250 degrees has a cone of 150 degrees. The bounds make the importance of the BVH worse for a fast spinning light, never wrong.
* Check (`LightBoundsAreConservativeOverTheShutter`, 10^5 samples of points and normals on the moving shape at random times and at the key times, four shape kinds, five motions): no point outside of the box and no normal outside of the cone. Cone half angles: 37.5006 degrees for a 75 degree rotation, 100.001 for 200, 85.001 for 170, 150.001 for 300, 0.02 for a translation, 180 for the sphere.
* `SupportsEquiangular()` (the sampling of the distance toward an area light by volpath) is false for an animated light: the point sampled by area has no time. Volpath falls back to its other strategies.

### Per integrator

| integrator | moving area light | moving emissive medium | note |
| --- | --- | --- | --- |
| path | yes (tested) | n/a | MIS weight uses `PDF_Li` at the time of the hit |
| volpath | yes (tested) | yes (tested, `emission_nee` true or false) | `SampleT_maj`, `SamplePoint` and the emission light take the ray time |
| simplepath | yes (tested, all three strategies) | n/a | |
| bdpt | yes (tested) | not tested (the time is passed to the emission light calls) | the light subpath takes its time from `SampleLe`; the bdpt render above has no medium |
| lightpath | yes (`SampleLe` at a time drawn in the shutter) | not tested | |
| sppm, mlt, randomwalk, ao | time filled in the contexts; not tested with a moving light | not tested | |

### Tests and numbers

`src/pbrt/animatedlight_test.cs`, 22 tests in the group `AnimatedLight` (the 4 renders and the report each take seconds to minutes: 16 stills per image):

* Static light against the same light with a non moving `AnimatedTransform`: `SampleLi`, `L`, `PDF_Li`, `Bounds` bit for bit (all four shapes).
* A light that moves, at a time `t`, against the static light made with `Interpolate(t)`, for quad, triangle, sphere, disk, with 2 and 4 keys, one and two sided, at 5 times. `L`, `Phi` and the normal are equal to a few ulp (relative 2e-7 to 8e-7); the density of `SampleLi` agrees to 9e-6 (sphere), 8e-6 to 6e-4 (disk, triangle, one sided quad) and up to 1.6e-2 for the two sided quad and disk, where the static light itself changes by 0.57 when the receiver moves by 1e-5 (some receivers are almost in the plane of the emitter). The sampled point and direction of the spherical triangle and of the spherical rectangle are single precision limited: a static light against itself with the receiver moved by 1e-5 differs by more (up to 9e-2 in the point) than the moving light against the static one (up to 1.6e-2), so the test compares against that floor (a few times) and not against an absolute value. The sphere samples a cone whose frame depends on the orientation of the sphere, so it is compared on the surface, in the normal and in the density. `PDF_Li` along a sampled direction that is nearly parallel to the emitter (1/cos) is left out when the static light does not agree with itself (0 to 14 of 2000 samples per case, counted in the log); the other samples agree to 3.5e-4 or better. `PDF_Li` is defined only for directions that hit the emitter (pbrt's triangle and rectangle densities do not intersect), so those tests use directions that hit.
* `SampleLi` estimates of the irradiance of the moving emitter against a quadrature of the emitter at the time (z < 4.5), the pdf integrates to one over the directions that hit it, `SampleLe` and `PDF_Le` against the static light, alpha, scale of the keys.
* Light sampler: bounds, PMF and BVH frequencies, irradiance from several moving lights.
* Media: `TextureMedium` and `VolumeEmissionLight` moving against static (coefficients to 7e-6), majorant soundness, delta tracking against quadrature of the emission along rays (z < 4.5), the emission light against quadrature of E/d^2 over the volume at 3 times, the densities integrate to one, the bounds cover the shutter; the OSL `vglow` medium likewise (`Violations() == 0`). The majorant of a moving medium is never looser than the one of the static medium at that pose (a static medium bounds its rotated cells with their axis aligned box).
* Scenes: the 2 key and N key forms load and the emitter is where the key says (`AreaLightSource` + `ActiveTransform` / `MotionBegin`), a static scene stays static.
* Renders (`Render*`): the image of the moving emitter (two seeds, 128 spp, 64 x 48) against the mean of 16 stills at the times (i + 0.5)/16 with positions and angles written independently of `AnimatedTransform`, the noise floor the difference of the two seeds. Mean absolute differences (moving against stills; floor): bouncing ball path 0.092 (0.115), volpath 0.093 (0.115), simplepath 0.092 (0.114); spinning quad path 0.075 (0.100), volpath 0.075 (0.100), simplepath 0.095 (0.119), bdpt 0.073 (0.098); grid medium with NEE 0.042 (0.040), tracking only 0.050 (0.051); OSL medium 0.074 (0.065). An emitter that stays at its first pose is 0.27 to 0.88 away. The assertion is that the difference is under 1.5 times the floor, and over 3 times the floor for the frozen emitter. Simplepath with light sampling only, BSDF sampling only and both: the images agree within the noise (ball and a quad of two triangles).

Benchmarks (`AnimatedLightBenchmark.*`, the same file built in the old tree, 10^5 calls, best of 9 runs, three alternating sessions; the noise between sessions is 10 to 20 percent): static `SampleLi` old / new, quad 543 / 466 ns, triangle 478 / 377, sphere 286 / 231, disk 210 / 176; `L` 33 / 29 ns; `PDF_Li` quad 706 to 892 / 705 to 895, triangle 280 / 230 to 293, sphere 43 / 38 to 48, disk 469 / 409 to 485: **no slowdown** of the static path (the lowest values of the new code are the lowest, the highest are within the noise). Moving light (3 keys), `SampleLi`: quad 995 ns, triangle 1045, sphere 779, disk 800 (about 2 times the static cost: `Interpolate` of the transformation, its inverse, the transformation of the context and of the sample). A whole static path traced scene (256 x 192, 64 spp, a static quad light, 5 alternating runs): old 6.58 to 7.21 s, new 6.60 to 7.09 s, the images are bit for bit equal.

Renders (`/tmp/animlight_renders/final/contact_sheet.png`, 256 spp path / 128 spp volpath, seeds 1 and 2; truth = mean of 16 stills): mean absolute difference moving against truth (seed 1 / seed 2; noise floor seed against seed): 2 keys quad 0.049 / 0.051 (0.055), 4 keys quad 0.065 / 0.064 (0.068), 3 keys ball 0.085 / 0.085 (0.082), grid medium 0.053 / 0.053 (0.050), OSL medium 0.073 / 0.073 (0.066). The truth itself has the time discretisation of 16 stills (the visible emitter is 16 copies), which is part of the difference.

### Limits

* A scale that is not uniform, or that differs between keys: the light warns and uses the start transformation. A uniform scale that is the same in every key works.
* Animated shapes in an `ObjectBegin` definition get no area light. Shapes with an OSL emission shader (`osl-pbrt.md`) that are animated are not lights (the emission is added at BSDF sampled hits without MIS, as before).
* The `DensityGradient` of a `TextureMedium` is the one of the first pose. Textures do not move: only the medium transformation does (define the texture with the CTM of the first key).
* The bounds are conservative, not tight: a light that spins a lot has a wide cone (up to the whole sphere) and the BVH sampler is less good at choosing it. A time-split light BVH would be tighter and is not done.
* Equiangular sampling is off for animated area lights (above). Moving lights are not tested in sppm, mlt, randomwalk, ao.
* Found and **not changed** (it is in the code before this change): with `simplepath` a static two sided **bilinear patch** emitter gives an image with light sampling only that is 6 to 11 percent brighter than the one with BSDF sampling only (the same square as two triangles agrees to 0.1 percent; path with MIS is not affected visibly). The bias is in the light sampling of the bilinear patch (spherical rectangle sampling, not its motion): `ReportBilinearPatchLightSamplingAgainstBSDFSampling` prints the ratio (1.114 static, 1.113 moving). The strategy test therefore uses the triangle pair for the quad.
* `PDF_Li` of a light that moves is subject to the same single precision limits as the static one near grazing directions.
