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
* Animated lights, media and textures are not supported (as for two keys): the first key is used with a warning. Animated area lights are an error. Animated vertices (deformation motion blur) and time-varying parameters are not supported.

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
