# Unified materials and volumes (branch `experimental/unified-materials-volumes`)

Baseline: `main`, i.e. pbrt-v4's `VolPathIntegrator` and `BDPTIntegrator` with the medium carried in the `Ray` and a
`MediumInterface` on primitives. Nothing from `experimental/procedural-media` is assumed; textures drive volumes later
through the interface below.

References: Fong et al., *Production Volume Rendering* (SIGGRAPH 2017 course) sections 3.1-3.9 and 6; PolarisedUPBP
(`Scene/Media.hxx`, `Scene/Materials.hxx`, `Path/Bsdf.hxx`, `Path/BoundaryStack.hxx`, `Path/PriorityStack.hxx`,
`Path/PolarisedLightSystems.hxx`). LuxCoreRender's volume-as-material design is not re-read for this document.

## What the references agree on

| Idea | Fong et al. | PolarisedUPBP | Magi consequence |
| --- | --- | --- | --- |
| A material owns both the surface response and the volume | `Material::CreateBSDF`, `HasVolume`, `CreateVolume` (listing 1, 4) | `Material` plus per-id `AbstractMedium`; one `BSDF_LS` covers surface and medium (`kScatter` event) | `Material` gets `HasVolume()` / `CreateVolume(ctx)`; phase functions become a `BSDF` |
| The path knows which volumes it is inside of | `Ray::EnterMaterial` / `ExitMaterial` list, most capable volume on top (listing 5, section 3.9) | `BoundaryStack` priority stack, copied for shadow rays | `BoundaryStack` (done, see below) replaces `Ray.medium` |
| Volume integration is an interval query | `Volume::Integrate`, `Volume::Transmittance` (listing 1) | `SampleRay` / `RaySamplePdf` / `EvalAttenuation` / `EvalEmission` | one `Volume` per medium type with its own integrator module (see below) |
| Transmittance rays inherit the stack | section 3.4 | `Occluded(..., aBoundaryStack, ...)` | shadow rays take a copy of the stack |
| Overlap is resolved by priority and, if allowed, by summing extinction | `CanOverlap`, `GetOverlapPriority`, listing 17 | priority only | priority first; summed-extinction overlap later |
| Many volumes are aggregated into a spatial structure of integration intervals | octree with min/max per node (section 6.8) | none | procedural textures supply min/max per cell; one aggregate per scene |
| Polarisation is a change of the carried quantity, not of the algorithm | n/a | `Light`, `Importance`, `Attenuation` trio over Stokes / Mueller | keep every throughput product behind three small types so a Mueller version can replace them |

## Target interfaces (names follow pbrt-v4 where an equivalent exists)

```
Material            GetBSDF(ctx) / HasVolume() / GetVolume(ctx) / GetBoundaryPriority() / CanOverlap()
BSDF                one type for surface and phase: f, Sample_f, PDF, Flags (a phase BSDF has no shading frame normal)
Interaction         one struct for surface and medium points
Volume (per type)   integrator module: SampleCollision(ray, tMax, u, rng) -> event + weights
                    estimator modules: Transmittance(p0, p1), Density(p), Attenuation(p), Emission(p)
                    bounds: RegionBounds(box) -> (min, max) and optionally Gradient(p), used by textures
VolumeAggregate     octree of canonical cells, each with its overlapping volumes and (min, max); iterator yields intervals
BoundaryStack       the priority stack, one per path and per shadow ray
Shared helpers      the code both VolPath and BDPT call (below)
```

Area lights and volume lights share the `Light` interface that `VolumeEmissionLight` already uses on
(point sampling and forward NEE along a line). It is on this branch: see phase 6.

## Parity between VolPath and BDPT

The integrators differ only in how they combine events, so everything that decides *what an event is* moves into
shared helpers. PolarisedUPBP's renderers already share `Scene::Intersect`, the `BoundaryStack` and `BSDF_LS` this way:

1. `BoundaryStack` update at an interface (enter / exit / reflect), one function used by every path walk and by shadow rays.
2. Collision sampling in a volume interval (`Volume.SampleCollision`), including the ratio-tracking weights `r_u`, `r_l`.
3. Transmittance between two points (`Volume.Transmittance`), used by `SampleLd`, the BDPT connection and the light tracer.
4. Phase `BSDF` creation at a real scattering event.

A change is accepted when VolPath and BDPT still agree on a test scene within noise and the render before the change
is reproduced exactly with the same seed.

## Phases

1. **BoundaryStack** (done): `boundarystack.cs`, ported from `StaticPriorityStack2`, with tests against a reference model.
2. (done) `BoundaryStack` carried in `VolPathIntegrator`, in the BDPT vertices and in the shadow-ray code; `Ray.medium` removed.
   Same-seed renders of a one-medium and a nested-media scene are bit-identical to `main` for volpath and bdpt.
   One robustness rule came out of it: a collision sampled at the very end of a segment can leave the next ray a rounding
   error short of the boundary, so it misses the entry and later meets the exit; `MediumTracking.CrossSurface` then finds
   the medium it enters already on top and does not push it again (pbrt-v4 names the medium at every crossing and is not
   affected).
3. (first slice done) A shape takes `"integer mediumpriority"`; with it, `MediumInterface` is a volume boundary: crossing into the
   shape pushes the inside medium at that priority, crossing out pops it, the outside medium is ignored, and the medium of
   highest priority decides the segment (equal priority: entered last wins). Shapes without it keep pbrt's transition
   semantics. The nested test scene written with priorities is bit-identical to the same scene written with transitions;
   two overlapping spheres agree between volpath and bdpt (0.2-0.8 %) and change when the priorities are swapped.
   Still open: the priority on the material rather than the shape, camera / light starting media at a priority, and
   the summed-extinction overlap of Fong listing 17.
4. (done) The volume module of a medium type is pbrt's `Medium` subclass already: `SamplePoint` is its density / attenuation /
   emission estimator and `SampleRay` its integration (the majorant intervals: one for homogeneous, DDA for grid / RGB grid /
   cloud / NanoVDB). What was written out several times in the integrators moved to `mediumevents.cs`: the collision
   type (`MediumEvents.SampleEvent`, used by `SimpleVolPath`, `VolPath` and the BDPT random walk) and the ratio-tracking
   updates (`RatioTrackingStep`, `FinishSegment`, used by the BDPT / volpath shadow-ray transmittance and by `SampleLd`).
   Same-seed renders are still bit-identical to `main`. An analytic homogeneous transmittance was left out: it changes how
   the spectral MIS weights `r_l` / `r_u` are built, which has to be validated separately.
5. (done) Material-owned volumes and the phase function as a `BSDF`.
   * `Material "..." "string volume" "<medium>" "integer volumepriority" N`: the material owns the volume. The `interface`
     material is `null` in Magi, so the binding is read from the material's scene entity when the shape is built
     (`BasicScene.MakeMediumInterface`); a shape with such a material becomes a volume boundary as in phase 3. A glass sphere
     with `"string volume"` renders bit-identically to pbrt-v4's `MediumInterface` + `dielectric` on `main`.
   * `MediumInterface` stays valid input and keeps pbrt's transition meaning. It is not rewritten into a material volume:
     the two agree only when the medium named as outside is the one that surrounds the shape, and a rewrite would silently
     change scenes where it is not. When both are given the material wins and a warning is printed.
   * `BSDF(PhaseFunction)` / `PhaseBxDF` / `MediumInteraction.GetBSDF()`: one BSDF type for surface and medium points (no shading
     frame, no `wo.z == 0` rejection). `Vertex.f` / `Vertex.PDF`, the volpath and simplevolpath scattering, the BDPT random walk and
     `SampleLd` use it; same-seed renders are unchanged. `Interaction` was already one class with a surface and a medium
     subclass.
   * `Material.HasVolume()` / `ResolveVolume(findMedium, loc)` / `VolumePriority()` (Fong listing 1) are on the `Material` class; the scene sets
     them from `"string volume"` / `"integer volumepriority"` when it makes the material, and `MakeMediumInterface` asks the material.
     The `interface` material is an `InterfaceMaterial` object now (it only carries the volume while the scene is built); the
     primitives of an interface still get a null material, so nothing changes at render time (same-seed renders of the material,
     glass, priority, overlap volpath and bdpt scenes are bit-identical to before).
   * Overlap (Fong section 3.9, `CanOverlap` / `GetOverlapPriority` on the material): `"bool overlap"` and `"integer overlappriority"` next to
     `"string volume"`. Volumes of materials that overlap add up where a path is inside several of them (`CompositeMedium`: summed
     sigma_a / sigma_s, merged majorants, emission as the sum of sigma_a * Le, the collision's phase function chosen by sigma_s). A
     material that does not overlap hides what is below it as before. The composite runs the volume integrator of the child with the
     highest overlap priority. Limits: the mixture weights are the hero wavelength's (exact for equal phase functions or albedo
     ratios that do not depend on the wavelength), polarised rendering does not look inside a mixture, and a material whose volume is
     an emissive texture medium with a light cannot overlap. Checked: two overlapping spheres with equal g match a single
     summed medium (per-channel mean ratios 0.9996 to 1.0009, volpath and bdpt); the material route and the earlier flag route are
     bit-identical.
   * `Material.GetVolume(Interaction)` (Fong's `CreateVolume`, named after pbrt-v4's `GetBSDF`) (Fong listing 1, per point of entry): `"texture volumescale"` makes the material create its volume
     for each entry point, the named medium (`ScaledMedium`) with sigma_a and sigma_s scaled by the texture there. A constant scale is
     bit-identical to a medium with the coefficients multiplied. A scale that varies with the entry point is not defined independently
     of the strategy: volpath and bdpt differ by about 1.3% in red on a checkerboard-scaled sphere (reproducible across seeds, the
     noise is about 0.1%), because a shadow ray or a path built from the other end enters at another point. So bdpt, mlt and
     lightpath refuse such scenes, and volpath's shadow rays make their own entry, as Fong's transmittance rays do. Per-entry volumes
     cannot overlap (the composite cache would grow with every entry).
6. (done for point and spot lights in volpath) Equiangular sampling (Fong section 4.2.2, `"bool equiangular" true`).
   * Each medium segment of a volpath walk also gets a scattering point chosen from a point light: the light by the light sampler
     from the start of the segment, the distance by the angle seen from the light (`EquiangularSampling`), then the connection. The
     transmittance to the point is ratio tracked with the walk's own tracker (`PathWalk` calls the new `BeginSegment` hook; the
     default is a no-op, so bdpt and mlt are untouched).
   * One MIS with the walk (a collision, then `SampleLd`) over the spectral ratios already tracked: for the same path the walk
     has probability `r_u * ru_seg * sigma_s * pmf` and equiangular `r_u0 * rl_seg * p_e * pmf`, both relative to the hero's connection;
     `PathState.segL` (now tracked for volpath too) gives the walk's side. Only `DeltaPosition` lights take part, every other light
     is found by the walk alone with the old weights. Polarised rendering does not use it. Without the flag the renders are
     bit-identical to before (volpath and bdpt).
   * Checked against a 1024 spp density-sampling reference with a point light in fog: homogeneous gray and chromatic, point plus
     spot light, and a heterogeneous grid (3-seed mean ratios 1.000 to 1.008, within the reference's own noise; the heterogeneous
     grid 1.0003, 1.0000, 1.0002). The per-seed RMSE is about half that of density sampling for the homogeneous scenes and 0.007
     against 0.009 for the grid, at about 1.5 times the time.
   * A finding that applies to every strategy that picks a point on the segment: pbrt-v4's grid density is interpolated between cell
     centres, so it is positive up to half a cell outside the bounds where the majorant iterator has no segment, and the walk can
     never collide there. Equiangular sampling treats a point outside the majorants as having no medium (`MajorantCovers`). Before
     that it was 14 to 19% too bright on a 4x4x4 grid, which showed up as a deterministic bias with a tiny RMSE.
   * Area lights (Fong section 4.2.3; Kulla and Fajardo 2012, "choose the light sample point first, then apply equi-angular sampling"):
     a point y on the light is chosen uniformly by area (the shape's `Sample(u)`, density 1 / area wherever the segment starts), then the
     distance by the angle seen from y, then the connection. Triangles, rectangular bilinear patches, disks, spheres and cylinders
     take part (`DiffuseAreaLight.SupportsEquiangular`); a non-rectangular or image-sampled patch does not (its density varies).
     Three strategies find the same path in the solid angle of the vertex: the walk's collision plus `SampleLd`, the phase-sampled
     direction that hits the light, and this one; `SampleLd` and `OnSurfaceHit` add its term to their denominators (the hook keeps a
     snapshot of the segment of the last scattering vertex, dropped at any surface bounce or later vertex).
   * Findings, in the order they were found: (1) the light sampler gives 0/0 for a light with no importance at the origin (one that
     faces away), which is a probability of zero here (`PmfOrZero`). (2) The first version sampled y in solid angle from the origin and
     the other strategies evaluated that density with the shape's `PDF(ctx, wi)`, an intersection test of the direction, which missed
     about 1% of the points `Sample` had chosen: +1% too bright at depth 8, 0 at depth 1, growing with depth. Uniform-by-area
     sampling has a density that needs no test (and costs the solid-angle sampling's lower noise for a large light close to the
     ray). (3) `BVHLightSampler.PMF` for a root that is a single light returned 1 where `Sample` returns no light (no importance at
     the point); a strategy that finds a path from another point (this one, from the start of the segment) needs the probability that
     `Sample` really has, so it is now zero there: -1.2% on a single triangle facing away before, +0.3% over four seeds after.
   * Checked at 24x24, 4096 spp, maxdepth 8, two seeds, equiangular over density sampling: quad 1.002 and 0.999, disk 0.999 and 1.000,
     a triangle facing the fog 0.9985 and 0.9986, a triangle facing away 1.006, 1.006, 0.997, 1.001, a two-triangle panel facing
     away 1.006 and 1.005 (seed spread about 0.3%); point light plus panel 1.003 (8192 spp).
   * Noise at the same number of samples: RMSE against a 2048 spp reference 0.0177 and 0.0174 against 0.0241 (gray), 0.0195 and
     0.0201 against 0.0271 (chromatic), 0.0039 against 0.0068 (grid), 96x96 at 64 spp, before the switch to uniform-area sampling. At
     equal time (about 1.5 times the cost per sample) a 256x256 scene with a small bright panel in fog gains only about 3% in
     high-pass noise; at equal samples 30%.
   * bdpt (`"bool equiangular" true`, not with `"polarised"`). The camera subpath gets the same extra sample at the first piece of
     each medium segment (`BDPTWalkHook.BeginSegment` -> `SampleEquiangularVertex`): a light by the context-free light sampler, a point
     on it, the distance by the angle seen from it, ratio tracking to the point, then the connection. Its vertex is put at the end of
     the camera subpath and weighed by `BDPT.MISSum` (the sum over the strategies, per wavelength, that `MISWeight` averages) plus its
     own term. The other way round, every strategy whose path has a scattering vertex x1 next to a point or area light y0 gets one more
     term in its sum (`EquiangularTerm`): the strategy with one light vertex (which is already in the sum, as the sample's own or
     as the loop's hypothetical one, `riOneLight`) with the segment that arrives at x1 as a sampled connection (`segL`; `connRl` for
     s = 2, the light subpath's `segL` of vertex 2 for s >= 3), without the scattering coefficient at x1 (`vtx`), and with the density
     of the distance over the hero's scattering coefficient there (`Vertex.sigmaS0`), as in volpath. The segment's first piece, its
     length and the distance of x1 are found by casting a ray from the neighbour z to x1 (`FirstPiece`): a path whose segment crosses a
     boundary between media has no equiangular term (the sample is only made for the first piece, so the density is zero).
     `MISWeight` is the inverse of the average of `MISSum`; without the flag the sums are the same and the renders bit-identical.
   * Checked at 24x24, three seeds, point light in fog, equiangular over plain bdpt: unidirectional strategies depth 3 (4096 spp)
     0.987 to 1.007; all strategies depth 3 (2048 spp) 0.999 to 1.002, depth 5 (1024 spp) 0.999 to 1.003; chromatic depth 3 0.997
     to 1.002. The noise gain is small: depth 5 pixel RMSE 0.0056 against 0.0064 for 12% more time, because bdpt's s = 1 and t = 1
     strategies already handle a point light well.
   * The light choice of equiangular sampling is among the lights it takes part with (volpath: a second light sampler over them; bdpt:
     a power sampler over them, with the ratio of its probability to the connection's in the weights); with none the flag is off.
     Before, a choice of an unsupported light (an infinite light, say) found nothing. Point light plus a uniform infinite light, 24x24,
     1024 spp, maxdepth 8: volpath mean ratio 1.002 +- 0.0075 over six seeds, noise 0.036 against 0.066 for 1.6 times the time;
     bdpt 1.003 and 0.999, noise 0.0114 against 0.0169 for 1.25 times the time. The renders are unchanged when every light takes part.
   * Uniform distance sampling with MIS (Fong section 4.2.1; volpath, `"bool uniformdistance" true`, independent of `equiangular`):
     a second sample per medium segment of finite length (`SampleSegmentPoint(..., uniform: true)`): the distance uniform over the
     segment, the light chosen from the point by the light sampler and connected as the walk's connection does, so it works for every
     light (infinite lights, unsupported area lights, and so on). The walk's connection (`SampleLd`), the phase-sampled direction that
     reaches the light (`OnSurfaceHit`, `OnEscape`) and equiangular sampling add its term to their denominators, and it has theirs.
     Density 1 / length, so a segment that leaves the scene (infinite) has no such sample. Checked on a sphere of fog lit by a point light
     plus an infinite light, 24x24, 1024 spp, three seeds: means within 0.1% of plain volpath (gray, sparse) and 0.3% (chromatic, dense);
     pixel noise gray sparse 0.0086 plain, 0.0039 uniform (1.2 times the time), 0.0034 equiangular (1.33), 0.0035 both (1.58); chromatic
     dense 0.027, 0.016 and 0.011 with both.
   * Multiple sampling (Fong section 4.1; `"integer equiangularsamples" N`, `"integer equiangulardepth" D`, volpath and bdpt): N
     equiangular samples per medium segment for the scatterings before depth D (default all), one after. With N samples the strategy's
     density in every denominator is N times as large (`EquiangularSegment.count`, `Integrator.EquiangularCount`), and each sample
     is not divided by N. Same results as N = 1 (the mean ratios agree within 0.2%; N = 1 is bit-identical to before). The noise
     does not pay for the time here: volpath 24x24, 1024 spp, N = 4 against 1: noise 0.0030 against 0.0039 for 1.75 times the time;
     bdpt N = 4 gives 0.0042 against 0.0045 for 1.3 times. The paper's gain is locality for large volume assets and an amortised PDF
     table, neither of which this tracker has. Section 4.2.1 (uniform distance sampling with MIS) is a separate, not done item.
   Not done: `CanOverlap()` (summed extinction) and a per-hit `GetVolume(ctx)` that depends on the intersection.
6. (done, apart from emission) Texture-driven volumes.
   * `FloatTexture` needs only `MinValue()` / `MaxValue()`; `RegionBounds(box)` (default: the global bounds) tightens them over a box
     and `Gradient(ctx)` (default: central difference) gives the derivative, which a texture can override with the analytic one.
     Noise bounds, the image range pyramid and the volume textures (grid, triplanar, cloud, ramp, math) come from
     `experimental/procedural-media`.
   * `MakeNamedMedium "m" "string type" "texture" "string density" "<float texture>" ...` is a `TextureMedium`: density = offset +
     scale * texture, clamped at zero, empty outside its bounds; the majorant grid (`majorantres`) is built from `RegionBounds`
     of each voxel, so nothing is stored per voxel.
   * The volume aggregate is optional (`hierarchydepth` > 0): a voxel whose sub-voxel majorants average below `refinethreshold` of
     its own is replaced by a `hierarchyres`^3 grid, recursively; the traversal is nested DDA. Measured on the fbm cloud scene
     (96x96, 64 spp, volpath): depth 1 renders about 10 % faster than the flat grid, depth 2 about 10 % slower and its setup takes
     3 s, so the default is 0.
   * Emission of a `TextureMedium` (`emission_texture`, `Lescale`) is a `VolumeEmissionLight` when `emission_nee` is set. VolPath
     samples it as a light: points of the volume by the emission bound (NEE), or with `emission_fnee` along a line through the
     medium (FNEE, Simon et al.), with the spectral MIS between the sampled emission and the collisions of tracking.
   * BDPT starts light subpaths in the emissive volume (the start stack is found from the boundaries between the camera and the
     point; a point outside the medium is a light subpath without throughput), and connects to it for s = 1 (including s = 1,
     t = 1). Every emissive medium becomes a light in BDPT whether or not `emission_nee` is set, and `emission_fnee` is ignored there.
   * BDPT camera subpaths also gather the emission of media (as VolPath does at every majorant collision, and as LuxCore and UPBP
     do): each collision in an emissive medium ends a path with an emitter vertex (`MediumEmissionEnd`, an s = 0 strategy), and the
     gathered emission and the light sample of the medium are two strategies of the same paths, combined by the MIS weights of
     `BDPT.MISWeight` (the emitter vertex has the density of a collision at the point, per volume: the extinction times the density
     of the direction, hence the factor `sigmaT0`). Option `"bool gathermediumemission"` of `bdpt` and `mlt` (default true); false
     leaves the s = 0 strategy out as before. Gathering is what finds paths that no connection can make (camera, specular surface,
     emissive medium): with a mirror floor the gather-off means are 13 to 15 % low and the gather-on means agree with VolPath.
     Checks (glow scene, 64x64, scalar): means agree with VolPath at maxdepth 0, 1, 2, 3, 5, 8, and at 12 against a 2048 spp
     reference (0.09972 vs 0.09979); error against that reference 0.0099 against 0.0129 without gathering at 256 spp for 10 % more
     time. A (1,1) strategy was weighted with the light vertex replaced by the sampled camera vertex until the two-light scene
     showed it (the weights then only cancel for a single light); MISWeight now replaces the camera vertex for t = 1.
   * MLT with the bdpt estimator takes the same lights, gathers at the t - 1st vertex of the camera subpath for its s = 0 states,
     and has the light sample of a medium as the second strategy of the paths of length 0.
     Checked against the free-path estimator of VolPath (absorbing and scattering emitter, with floor and point light): means agree
     within 1 %.
7. Polarised light-system types, ported from PolarisedUPBP (`polarisation.cs`, `polarisedlightsystem.cs`, `fresnel_polarised.cs`):
   `PolarisationFrame`, `StokesVector`, `MuellerMatrix`, and the *optimised* `Light` / `Importance` / `Attenuation` of
   `PolarisedLightSystems.hxx` (a scalar, a depolariser or a full matrix with its two frames, whichever the element needs).
   `FrDielectricPolarised()` / `FrComplexPolarised()` are written like `FrDielectric()` / `FrComplex()` and return UPBP's
   Fs, Fp, Ds, Dp for `Attenuation.Fresnel()`; the tests check them against UPBP's `FresnelReal()` / `FresnelComplex()` ported
   verbatim, and chains of 2-5 mixed events (Lambert, scalar, Fresnel, Rayleigh, polariser) against applying them one at a time.
   Convention (checked against the source of Mitsuba 3, `render/fresnel.h`, `render/mueller.h`, `bsdfs/conductor.cpp`):
   the amplitudes, their signs (Verdet), the conjugation of eta and of cos(theta_t) for total internal reflection are
   `fresnel_polarized()`'s, and `Attenuation.Fresnel()` builds `specular_reflection()` from them (UPBP's own `FresnelReal()` differs
   by diag(1, 1, -1, -1), a pi in Ds - Dp). The x axis of the Stokes frame of a surface event is the s axis, cross(normal,
   propagation direction), as in Mitsuba's BSDFs; UPBP's `Scattered()` put it in the plane of incidence, which turns Q by 90 degrees
   against the Fresnel matrix, so `Scattered()` here takes the s axis (media: the normal of the scattering plane). Not covered:
   Rayleigh scattering (no equivalent in Mitsuba's render/ headers).
   Two more deviations from UPBP: its `DepolarisingExit * Standard` product multiplies the unrotated first row (its rotated row
   is stored in the second row and never used), the port uses the rotated row, which the chain test needs; and its
   `Light::RepresentInDirection()` returns before rotating a light that has a frame, the port rotates. The integrators that use them
   (`polpath`, `volpath`, `bdpt`) are described under "Polarised integrators" below.

## Decisions taken without asking

* Branch name `experimental/unified-materials-volumes`, from `main` at 9e6754a.
* `BoundaryStack` is a value type with inline storage (20 entries, as in PolarisedUPBP) so a shadow-ray copy needs no allocation.
* Stack overflow drops the top entry with a warning, as PolarisedUPBP does; this is a limit to revisit.

## Decided by the owner

* `Ray.medium` is dropped (phase 2), not kept as a cache.
* Volumes are bound by the material (Fong / PolarisedUPBP). pbrt's `MediumInterface` syntax stays as input with its
  transition meaning (it is not rewritten into a material volume, see phase 5), so existing scenes still load.

## Notes for phase 2 and 3

* Phase 2 carries the stack in the integrators (a local `BoundaryStack` in `VolPathIntegrator.Li`, in the BDPT random walks and
  in the shadow-ray / connection code) and gives every `SurfaceInteraction` the media on its two sides, computed from the
  stack at the hit (Fong's incident and opposite volumes), so `Interaction.GetMedium(w)` keeps its signature and the code
  that calls it does not change.
* The `MediumInterface` rewrite is exact for closed watertight shapes. A shape that separates two different media without
  enclosing either (an open sheet with different media on its sides) has no stack equivalent; the parser keeps that case
  as a two-sided interface entry and reports it, instead of silently changing it.
* Camera and light-subpath starting media come from the stack the path starts with (the camera medium pushed at the
  global-medium priority).

## Polarised integrators (phase 7 follow-up)

- `polpath` (surface path), `volpath` and `bdpt` take `"bool polarised"` (`polpath` is always polarised), `"float analyser"` (angle in degrees of a linear polariser in front of the sensor, 0 = image vertical; the factor 2 keeps the unpolarised brightness) and `"string measure"` (`intensity`, `s1`, `s2`, `s3`). The unpolarised render is untouched.
- Smooth dielectrics and conductors apply the Mitsuba 3 Fresnel Mueller matrices; every other BSDF depolarises. Media take `"float rayleigh"` (fraction of Rayleigh scattering in the phase function, as PolarisedUPBP's Rayleigh coefficient); the Henyey-Greenstein part depolarises. The Rayleigh Mueller matrix has the sign of Mitsuba's Stokes convention (PolarisedUPBP writes the opposite one: its Stokes vectors measure I_y - I_x).
- BDPT keeps a Mueller matrix (camera subpath) or Stokes vector (light subpath) per vertex next to beta, and measures the polarisation relative to the scalar throughputs at the connection. It matches `polpath` on a water plane (0.6400 vs 0.6401 at an analyser of 0 degrees) and `volpath` on Rayleigh fog.
- `bdpt` takes `"bool russianroulette"` (default off): roulette on the subpaths and on the transmittance of the connections. Unbiased, but it made no difference in noise or time on the test scenes (their throughput stays near 1).

## Realistic camera: polarisation and bdpt

* **Polarisation** (`Camera.PolarisedSensor`, `RealisticCamera.RecordTransmission`). The lens system is a stack of dielectric interfaces. The polarised integrators trace the camera ray backwards through the system, record the transmission Mueller matrix of the Fresnel equations at every refraction (in the frames of the plane of incidence of that interface, the s axis first, as for a surface), multiply them, and measure the Stokes vector that comes out of the product on the film: the sensor frame is the light direction at the film, with the image up as `-y` of the film. The unpolarised render is untouched (it does not model Fresnel loss, as pbrt-v4's realistic camera does not), and the polarised render includes the instrumental polarisation of the lens (a few tenths of a per cent of S1 across the image of a diffuse scene, more for strongly polarised light). The Mueller matrix of the lens is checked against an independent Jones calculus (real field in three dimensions, own sphere intersections) for a double Gauss lens: S0, S1, S2 of four input states over 400 rays agree to 2e-4. The Stokes convention is Mitsuba 3's, whose S2 has the opposite sign of the textbook one; the test says so.
* **Importance** (`We`, `PDF_We`, `SampleWi`). By the radiance theorem the etendue `dA cos(theta) dw` of the film is that of the exit ray, so the weight of `GenerateRay()`, `T cos^4 / (pdf_pupil z^2)`, and the densities of its samples (film point uniform, pupil point uniform in the exit pupil bounds) make the importance a constant: `We = T / A_film` for the rays that `GenerateRay()` can produce (the ray traced back through the lens ends on the film, inside the exit pupil bounds of that film point, with the aperture image transmission `T`), zero for all others. The camera vertex is a point on the front element with the normal and the surface area of that sphere; the joint density of the point and the direction in that measure is `pdf_pupil z^2 |n . w| / (A_film cos^4 theta_film)`. (A first version used the area projected along the axis: the integral of the density over the rays that leave the lens then did not give the fraction of samples that make a ray, and the test of that integral found it. The measure has to be the surface's, with its own normal.) bdpt needs a position density and a direction density given the position: `SampleWi()` takes the point uniformly on the part of the front element within its aperture (`pdfPos`), and `pdfDir` is the joint density divided by that, so the strategies that sample the point (t = 1) and those that sample the ray (t > 1) are compared through the one joint density. Tests: the weight of `GenerateRay()` equals `We cos / (pdfPos pdfDir)` of its ray, and the film position of the ray is the sample's; the density integrates (over the front element and a cone of directions) to the fraction of film/pupil samples that give a ray (0.7313), and the importance to the mean weight of `GenerateRay()`.
* **Samples without a camera ray.** A lens system loses part of the film samples (0.73 of them give a ray in the double Gauss test), where a perspective camera always gives one. pbrt-v4's bdpt makes the light subpath inside `Li()`, which a sample without a ray never reaches, so the t = 1 strategies lost that part of their samples and bdpt was 27% dark in those strategies (and 3.8% dark overall at depth 1). `RayIntegrator.EvaluatePixelSample()` now calls `NoCameraRay()`, which bdpt overrides to make the light subpath and the connections to the camera (only t = 1 remain). Found by running each strategy alone: `(0,3)+(1,2)` and the light tracer alone were right, `(2,1)` alone was 0.73; after the change `(2,1)` alone is 1.007 and all strategies 1.001 of volpath (depth 1), and 1.0016 over 12 seeds at depth 5 (z = 1.3).
* The `bdpt` and `lightpath` integrators accept the camera (`mlt` does not yet).
* **Aspheric surfaces** (`asphericcoefficients`). Eight values per interface of the lens file, in the order of the file: the conic constant `k` and `A4, A6, ..., A16` of the sag `z(r) = c r^2 / (1 + sqrt(1 - (1 + k) c^2 r^2)) + A4 r^4 + ... + A16 r^16`, with `c` the curvature of the interface, `r` and `z` in millimetres; all zero is a sphere (and takes the old analytic path). The intersection is Newton's method in double precision, started at the base sphere, so the focus calculation, whose paraxial rays start a metre from the lens, is more accurate than the single-precision sphere's (a few microns in the position of the rays). The front element is where the camera vertices of bdpt are, so it is sampled uniformly by area from a table of the area against `r^2` and has the normal of the slope of the sag. Tests: the exit direction of rays through two aspheric interfaces of a double Gauss lens against an independent trace (bisection for the intersections, finite differences for the normals, 5e-5), spheres as the limit (1e-5), the area and normals of the front element against numerical integration, and the importance and density integrals and `weight = We cos / (pdfPos pdfDir)` on an aspheric front element. For a plano-convex singlet with the plane toward the scene, `k = -n^2` on the curved side focuses all heights to one point (checked numerically: the longitudinal aberration of the sphere is 6.6 mm of the 100 mm at the edge of the aperture, that of the hyperbola 0.000).
* **Dispersion** (`abbenumbers`). One value per interface: the Abbe number of the glass behind it (0 for none). The index follows the Cauchy relation through `n_d` with `n_F - n_C = (n_d - 1) / V_d`, so the file's index is the index of the d line (587.56 nm) and the focus and the focal length are those of the lens file. A dispersive camera traces the first wavelength of the sample and terminates the others (`GenerateRay`, `We` and `SampleWi`), as the dispersion of a dielectric does; the polarised measure takes the wavelength (`Camera.PolarisedSensor(ray, wavelength, ...)`), so that the Fresnel factors use the index of that wavelength. The exit pupil bounds are the union over the d line and 400 and 700 nm. `PDF_We` has no wavelength: it takes the first of those three at which the ray passes. It agrees with `We` and `GenerateRay` exactly at the d line and to a few per cent at the ends of the visible range (with a lens of flint glasses, V = 30), which changes the weights of the strategies of multiple importance sampling, not the expectation (they sum to one for any density). Tests: the exit direction at 450, 550 and 650 nm against an independent trace with its own Cauchy index, the first wavelength and the termination (also when no ray comes out), the importance at a wavelength, the importance integral at 430 and 680 nm, and bdpt against volpath (the mean of the closed emissive sphere, with a dispersive lens). The Abbe numbers of the lenses that pbrt ships are not in their files: assigning them (crown for low and flint for high indices) is a guess, and a rendered lens that does so shows chromatic aberration of that guess.
* **Glass models** (`glassnames`, `sellmeiercoefficients`, `cauchycoefficients`). Replace the two-term Cauchy of the Abbe number with the real dispersion curve of the glass. `glassnames` is one name per interface (`""` or `"none"` for no glass): `BK7`, `SF11` or `fused silica`. `sellmeiercoefficients` is 6 values per interface `B1 B2 B3 C1 C2 C3` (C in micrometres squared), `cauchycoefficients` 3 values `A B C` (micrometres, `n = A + B/l^2 + C/l^4`); all zero is no glass. Precedence: name, Sellmeier, Cauchy, Abbe number. The relation is offset so that the d line keeps the index of the lens file, `n = n_file + n_model(l) - n_model(l_d)`, so focus and focal length stay those of the lens file and only the chromatic shape comes from the glass. The formulas are those of the `SellmeierSpectrum` and `CauchySpectrum` of the C++ fork. Tests: catalogue `n_d` (1e-4) and Abbe numbers of the three glasses, a name against its coefficients (same exit direction), the d line unchanged, SF11 spreading blue and red more than BK7, an `A`-only Cauchy not dispersing, and bdpt against volpath with named glasses.
* **Diffraction** (`"string diffraction" "aperture"`, default `"none"`; `realisticcamera_diffraction.cs`). The image is the geometric image (the ray tracing of the lens) convolved with the point spread function of diffraction at the pupil of the film point: `h(xi) = |FT of P|^2`, with `P` the pupil function in the direction cosines of the rays that leave the film point (1 where a ray gets through the lens: the stop, its image from the `aperture` parameter, and the vignetting of the other elements). The transform is the Fraunhofer integral, exact in the film plane for the direction cosines; the obliquity and cos^4 factors across the pupil are left out. The wavelength only scales the function (`h_lambda(xi) = (550/lambda)^2 h_550(xi 550/lambda)`), so there is one table per film point (7 x 7 points, the density in between the bilinear mixture of the four around it; 96 x 96 pupil samples, a 512 x 512 transform, cells of about 0.15 Airy radii, a range of about 78 Airy radii: the tail beyond it is dropped and the rest renormalised, about 1%). The wavefront error of the lens is deliberately not in `P`: the traced rays already carry the aberrations, and a kernel with them would count them twice. The model is therefore geometric image convolved with the aperture kernel, exact when the lens is diffraction limited or when its aberrations are large against the wavelength, and an approximation in between. The mask is that of the d line (the vignetting of the other wavelengths of a dispersive lens is not in it). Each sample is splatted instead of added to its pixel (the film point is uniform in the pixel and the film's filter weights the splat, as for the splats of bdpt), and each wavelength of the sample goes to its own point: the film point plus a sample of the function at that wavelength, with random numbers of its own per wavelength. It works with every integrator that goes through `RayIntegrator` (volpath, polarised volpath, bdpt, with the light subpaths that connect to the camera); light displaced outside the film is lost, and the film does not receive light from outside its extent. Tests: the light inside a radius against the Airy encircled energy `1 - J0^2 - J1^2` at 450, 550 and 650 nm (1.5%), the smaller pupil of the corner of the film, a square stop that spreads light along its axes, and the mean of the closed emissive sphere with diffraction, for volpath and bdpt, against that without. The effect is only visible when the pixels are about the size of the Airy disc (1.22 lambda N, 3 to 10 micrometres): a full frame sensor at 24 megapixels or a small sensor.

## Signed distance field shapes (`Shape "sdf"`, `Texture "sdf"`; the counterpart of OctaneRender's Vectron)

Files: `sdf.cs` (nodes), `sdfparser.cs` (expression language), `sdfmesh.cs` (mesh and grid fields), `sdfshape.cs` (the shape), `sdfsampling.cs` (area sampling), `sdftexture.cs` (density texture), `sdf_test.cs`.

Octane's Vectron (OTOY docs; the intersection method is not documented) is an OSL shader that returns a signed distance inside a user supplied box. This is the same design on the CPU, with an expression language in place of OSL.

* **Fields.** A field is a tree of `SdfNode` in double precision, negative inside. Every node reports a Lipschitz bound, so sphere tracing and the culling of empty space stay safe for fields that are not exact distances (smooth blends, twists, displacement, an ellipsoid, a non-uniform scale). Primitives: `sphere box torus cylinder capsule cone plane ellipsoid octahedron mandelbulb menger gyroid mesh`. Operators: `union intersect subtract` (also `a | b`, `a & b`, `a - b`), `smoothunion smoothintersect smoothsubtract` (polynomial, cubic or exponential blend), `mix offset shell translate rotate rotateaxis scale twist bend repeat mirror elongate displace`. `displace` is fBm of pbrt's Perlin noise with a measured gradient bound.
* **Ray intersection.** The ray is moved to object space in double precision and clipped to the box (`pmin`, `pmax`). Sphere tracing with the over-relaxation of Keinert et al. (2014): steps are `omega` (default 1.2) times the safe radius, signed, so an overshoot is followed by a step back, and when two successive spheres do not overlap the step falls back to the unrelaxed one. A hit is `|f| <= epsilon` (relative to the box, 1e-8) followed by two Newton steps along the ray. Rays that start inside work (the sign of the start decides the side). Because `epsilon` is far below the spawn offset (`gamma(5) |p|` plus the residual), a ray leaving the surface does not hit it again. A ray that grazes a smooth surface converges slowly: `maxsteps` (8192) bounds it, and about 0.3% of grazing rays of a smooth union exhausted a 2048 step budget (all of them hit with more steps).
* **Shading geometry.** The normal is the normalized gradient (tetrahedral central difference, exact to rounding for the exact primitives). `dpdu`, `dpdv` are the tangent vectors dual to the gradients of (u, v) for a closed form mapping (`uvmapping`: `spherical`, `cylindrical`, `planar`, `box`), so a texture and bump mapping see consistent derivatives; `dndu`, `dndv` come from the normal along those vectors. `reverseorientation` and a handedness swap flip the normal as for the triangles.
* **Closed volume boundary.** The field is negative inside, so the shape works with `mediumpriority`, material volumes, dielectrics and interface materials like a sphere.
* **Area light.** `Area()` and `Sample()` use `SdfSurfaceSampler`: marching tetrahedra over the cells the surface can reach (octree culling by the Lipschitz bound; cells whose corner normals differ by more than about 6 degrees are cut into eight, 3 levels), edge vertices placed on the exact surface by Illinois root finding, each triangle's area re-measured on a `areasubdivisions` (3) subdivision with points projected to the surface, a triangle chosen by area, a point uniform in it, projected to the surface. The density is `1 / area` of the true surface (measured in render space, so transforms that scale are right). `PDF(ctx, wi)` is the density of the first hit, which is what MIS compares with the BSDF density at the same point.
* **Mesh to field (vectorize).** `mesh("file.ply", resolution, padding)`: exact distance with a BVH (closest point per triangle) signed by the angle weighted pseudonormal of the closest feature (exact for closed, consistently oriented meshes; a mesh with holes gets the wrong sign near the hole), baked on a grid with a cubic Catmull-Rom reconstruction (`resolution` 0 keeps the exact field without a grid). The field can then be offset, blended, shelled or displaced like any other.
* **Volumes.** `Texture "n" "float" "sdf" "string sdf" "..." "string mode" "solid" | "exponential" | "shell" | "distance"` turns the field into a density in [0, 1] for a `"texture"` medium. `RegionBounds` over a box is exact from the Lipschitz bound (a value cannot change faster than `L` times the half diagonal), so empty and full space are skipped by the majorant grid. A field that is not rigorous (the Mandelbulb) returns [0, 1] over every box unless `"bool trustbounds" true`.
* **Not done:** SDF emitters with a non-uniform radiance importance sampling (uniform by area only), VDB level sets as grid fields, a compiled evaluator (the tree is interpreted), and OSL-like user code.

## Per-volume integrators (`volumeintegrator.cs`)

As in Fong et al., every medium carries its own integrator, built from two shareable modules. The path estimators
(`volpath`, `bdpt`, the transmittance of `Integrator.Tr`) call `medium.volumeIntegrator` instead of a type switch.

| Module | Parameter | Choices |
|---|---|---|
| `VolumeTracker` (collision sampling) | `"string tracker"` | `majorant` (default, pbrt-v4 majorant iteration), `global` (one majorant for the whole ray) |
| `TransmittanceEstimator` (shadow rays) | `"string transmittance"` | `ratio` (default, ratio tracking), `analytic` (Beer-Lambert for homogeneous media, ratio tracking otherwise), `tracklength` (delta tracking: 1 without a real collision, else 0), `residual` (residual ratio tracking: the medium's minimum density, `Medium.ControlExtinction`, in closed form and ratio tracking of the rest; no control means plain ratio tracking; no Russian roulette) |

New modules subclass the abstract classes and are chosen in `VolumeIntegrator.Create`. Defaults are bit-identical to the
previous build (six volpath/bdpt regression scenes). Checks, 256 spp homogeneous scene: analytic vs ratio means 0.12183 vs
0.12175, 11% lower noise at equal time; `global` vs `majorant` on a uniform-density texture medium: means 0.57827 vs 0.57873.

## Shared path framework (`cpu/pathstate.cs`, `cpu/pathwalk.cs`, `cpu/mltevaluator.cs`)

After LuxCore's PathTracer and pbrt-v4's `BDPT::RandomWalk()`: the integrators share the walk and the throughput updates, and keep their own estimators.

- `PathState` holds the scalar throughput, the rescaled probabilities `r_u`/`r_l` of volpath, and the polarised mirror (Mueller matrix of a camera path, Stokes vector of a light path). Null collisions, real scattering, the end of a segment, surface and phase scattering, Russian roulette and the pass through a coat are written once, for scalar and polarised paths.
- `PathWalk.Run(WalkContext, PathWalkHook)` is the loop: intersect, follow the medium with the tracker of that medium, handle absorption, real scattering and null collisions, skip boundaries, sample the BSDF. The order in which it draws from the sampler is fixed, so a path stays a function of its sampler (what Metropolis needs). `BDPT.RandomWalk` (`BDPTWalkHook`) and `VolPathIntegrator.Li` (`VolPathWalkHook`) are hooks on it; the medium collision stream is seeded from the ray, with the event drawn from the sampler.
- `MLTPathEvaluator` is what Metropolis light transport mutates: `BDPTPathEvaluator` (a strategy of a given depth, one chain state per depth, polarised with the same sensor as `bdpt`) and `VolPathPathEvaluator` (a whole walk, one state). They share the draw of wavelengths and the camera ray. `Integrator "mlt" "string pathestimator" "bdpt"|"volpath"`.
- One MIS. bdpt's weight is `1 / average over wavelengths of the sum over strategies of the probability of the path under the strategy, relative to the hero wavelength's under the strategy of the sample`. The scalar part is pbrt-v4's vertex-density ratio chain; the spectral part is, for each segment, the null collisions as a walk samples it (`Vertex.segU`, volpath's `r_u` increments) or as a connection samples it (`segL`, `r_l`), and the scattering coefficients at the vertices (`vtx`). A connection's shadow ray returns the same factors (`Integrator.TrSegments`). Volpath's `(r_u + r_l).Average()` is this weight restricted to the strategies s = 0 and s = 1 (`bdpt "string strategies" "unidirectional"`), and without a medium every spectral factor is one, so the weight is pbrt-v4's. Medium emission is gathered with the majorant as its density, as volpath's gather does.
- Shared code: the walk (`PathWalk`), the throughput updates (`PathState`), the shadow ray (`Integrator.ShadowRay`, the loop of volpath's `SampleLd`; volpath's renders are bit-identical to those before sharing it) and the light-sampling context (`Integrator.LightContextAt`). Volpath is not rebuilt on bdpt's vertex records: bdpt restricted to volpath's strategies reaches volpath's noise (chromatic glow scene: RMSE 0.0145 for both) but costs about 1.5x the time per sample (10.1 s against 7 s at 256 spp), because it stores the subpath. Volpath keeps its streaming `r_u`/`r_l`, which is the same weight for s <= 1.

Checks (96x96 volumetric Cornell and wax scenes): volpath on the shared walk and bdpt on the shared walk are bit-identical to the previous builds (11 and 6 scenes, scalar, polarised, Rayleigh, emission). Volumetric Cornell box with a thin-coated marble sphere, 64 spp: volpath and bdpt agree to 0.01% polarised (analyser 0 and 90, with and without Rayleigh) and scalar. Both MLT estimators agree with them in luminance: ratio 0.9999 (volpath) and 1.003 (bdpt) with 6.4M bootstrap samples (the bdpt figure was 0.7 % high until the connections to the camera were scaled by the ratio of the pixel area to the sample area of the film, which the camera paths of the chains draw from; now 0.9967 at 6.4M and 1.0008 at 1.6M bootstrap samples against the 1024 spp reference, MLT volpath 0.9996); with 100k bootstrap samples the ratio moves by about 1% from the noise of the normalisation constant, so MLT means need a large bootstrap to be compared.

Efficiency on that scene (rmse against 1024 spp volpath, error^2 x time, lower is better): volpath 0.065, bdpt 0.154, MLT volpath 0.147, MLT bdpt 0.411. bdpt has 2.1x lower variance per sample than volpath but costs 5x as much (all s,t connections); MLT driven by bdpt needs more chains and bootstrap samples than MLT driven by volpath (96x96 wax scene, rmse 0.137 -> 0.0925 with 4x the bootstrap samples).

### Spectral MIS in bdpt: checks

Chromatic glow scene (coloured sigma_a and sigma_s on a procedural emissive medium, 48x48, 256 spp, against 3072 spp volpath; ratio per channel, RMSE): bdpt restricted to volpath's strategies 0.9966/0.9980/0.9992, 0.0551 -> 0.0145 (volpath 0.0145); full bdpt 1.0007/0.9987/0.9986, 0.0450 -> 0.0129. Homogeneous chromatic fog (closed-form transmittance) with a point and an area light, against 4096 spp volpath: volpath 0.0203, bdpt restricted 0.0211, full bdpt 0.0155, all within 0.3% per channel. Gray scenes (scalar, polarised intensity and analyser, gather on and off) match the previous bdpt within noise (gather on 1.0019 against 1.0033, RMSE 0.0121 against 0.0124).
