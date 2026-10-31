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
Material            CreateBSDF(ctx) / HasVolume() / CreateVolume(ctx) / GetBoundaryPriority() / CanOverlap()
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
`experimental/procedural-media` (point sampling and forward NEE along a line); it is brought over once the volume
interface exists.

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
   Not done: `Material.HasVolume()` / `CreateVolume()` as virtual methods (the null interface material has no object to carry them).
6. Volume aggregate with texture-supplied min / max.
7. Polarised light-system types.

## Decisions taken without asking

* Branch name `experimental/unified-materials-volumes`, from `main` at 9e6754a; nothing is pushed.
* `BoundaryStack` is a value type with inline storage (20 entries, as in PolarisedUPBP) so a shadow-ray copy needs no allocation.
* Stack overflow drops the top entry with a warning, as PolarisedUPBP does; this is a limit to revisit.

## Decided by the owner

* `Ray.medium` is dropped (phase 2), not kept as a cache.
* Volumes are bound by the material (Fong / PolarisedUPBP). pbrt's `MediumInterface` syntax stays as input: the parser
  rewrites it into the equivalent material volume, so existing scenes still load.

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
