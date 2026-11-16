# Refractive volumes (branch `refractive-volumes`, from `pbr-materials` 05cf4d1)

Goal: a refractive (RRTE) volume that is one more volume of the production framework: heterogeneous index, attenuation,
transmission, emission, scattering, every tracker and estimator, every next-event variant, polarisation; unbiased.
Earlier attempt: `rrte-backup` (numerical integrator and connection solver inside the integrators; see its
`docs/refractive-media.md`).

## Design

1. **Field** (`refractivefield.cs`, done): n^2 linear in the tetrahedra of a lattice (`TetrahedralIndexField`). The light path
   in a tetrahedron is a parabola in sigma (ds = n dsigma), exactly; a face crossing is a quadratic root. `FromGrid(...,
   refine)` samples a grid's interpolation on a finer lattice. Cells without a gradient are crossed in one straight piece
   (`uniformDistance`).
2. **Tracking**: a curved ray presented to the trackers and estimators as a straight ray in sigma, with extinction mu n and
   majorant mu_bar n_max per piece (`RefractivePiece.largestIndex`).
3. **Volume**: a refractive volume in the production interface; `Integrate` returns the bent ray.
4. **Lights without a solver**: a volume may return a different ray than it was given (`Transmit`); the shadow walk follows
   the ray, a light sample counts if the ray lands on the light it was drawn for, weighed against phase / BSDF sampling by
   the densities of the direction of departure.
5. **Curved connection** (Newton with closed-form derivatives, one over the probability of each solution by repeated
   attempts): delta lights, equiangular, joint, once-more, emission point sampling.
6. **Polarisation**: a polarised mode of the production path tracer; each volume returns the rotation of the frame along
   its ray (closed form per parabola piece).

## Status

Done:
- the field and its paths (1);
- the refractive volume with the curve shown to the trackers and estimators as a straight ray (2);
- the shadow ray that follows what a volume bends, for area and infinite lights (4);
- the search for the path to a light at a point or from one direction (5, by differences, in `ProductionPath`), used by the
  path tracer's direct lighting and by the light sampling that the volumes ask the path tracer for;
- equiangular sampling (`"distancesampling"`) on a bent ray: the straight ray that stands for the curve draws its point
  about where the curve puts the point of the light (`CurvedRayMedium.Pivot`), and the weights at the collision use the
  same rule (`VolumeDirectWeight.curve`);
- a refractive medium that holds an aggregate (`"string heldmedium"`);
- the polarised path tracer (`Integrator "production" "bool polarised" true`, with `"float analyser"` and `"string measure"`
  as `polpath`), and the axis that is carried along a bent ray for it (`CurvedRayMedium.Carry`, `Attenuation.Transport`).

Not done:
- joint double scattering and once-more scattering on a bent ray (they are off there; they work on rays that the field
  leaves straight);
- the sampling of the emission of a refractive medium (`"emission_nee"`, `"emission_fnee"` are refused for it);
- in polarised mode the volumes gather no light of their own (equiangular, joint, once-more are off), and a
  single-scattering volume's own light and the sampled emission are added as unpolarised light;
- closed-form derivatives for the search (it uses differences, starts within half a radian of the straight direction, and
  stops after 64 searches); no speed work (a lamp inside a lens renders about 10 to 30 times slower than without one).

Scene syntax: `MakeNamedMedium "x" "string type" "refractive" "string medium" "homogeneous" | "uniformgrid" | "adaptivegrid"`
with the held medium's own parameters, and `"string indexfield" "grid"` (`"float index"`, `"integer inx/iny/inz"`,
`"integer refine"` 2) or `"bump"` / `"luneburg"` (`"float indexamplitude"`, `"float indexradius"`, `"point3 indexcenter"`,
`"integer indexcells"` 32); `"point3 ip0/ip1"`; `"bool surfacesinside"` (true). `Integrator "production" "bool
lightsampling" false` turns light sampling off (the reference for the tests below).

Pieces:
- `refractivemedium.cs`: `RefractiveMedium`, `CurvedPath`, `CurvedRayMedium` (+ iterator), `RefractiveVolume` (`Trace`,
  `Integrate`, `Transmit`). `Medium.MajorantOver(box)` for the held medium (homogeneous and grid media).
- `productionvolume.cs`: `Volume.Transmit`; `VolumeContext.bent / arrival / stopAtPlane / geometryOnly / shadowBent`.
- `cpu/productionpath.cs`: the loop goes on with `ctx.arrival` after a bend; `departure` (the direction the path left its
  last point in, kept through surfaces that only bound volumes) is what the light densities of the heuristic are of;
  `ShadowTransmittance` -> `Landed` / `Follow`; `SolveConnection`, `BentDeltaLight`.

Tests (`RefractiveField.*` as below; `RefractiveVolume.*`):
- `Transmittance`: ratio, residuallocal, tracklength, nextflight, pseriescmf along four paths bent by up to 0.44 rad
  through a smoke grid, against quadrature along the curve; one path starts inside the bump (n 1.257: the factor
  (n_start / n_end)^2). All within their errors.
- `Walk`: the weight that reaches the end without a collision and the emission gathered, by the tracker and by spectral
  tracking, against quadrature.

Renders (`renders/pbr/rrte`):
- Furnace (radiance one from everywhere, a lens that scatters and absorbs nothing, n up to 1.3 or a Luneburg lens,
  2048 spp): 0.99987 +- 0.00010 against 0.99982 +- 0.00009 without refraction; area-light enclosure 1.99978 against 1.99994.
- Bent light sampling against no light sampling (sphere light of radius 1, lens n up to 1.25, scattering 0.3; three seeds
  each): lens region 0.04059 +- 0.00022 against 0.04103 +- 0.00027; whole image 0.02940 against 0.02932.
- Point light through a weak scattering lens against a sphere light of the same intensity (four seeds each): lens region
  0.04050 +- 0.00021 against 0.03948 +- 0.00105; floor 0.03347 against 0.03351.
- Floor under a clear lens (n up to 1.3), whole image against the scene without the lens: point light 0.02312 against
  0.02340; distant light 0.14038 against 0.14014 (one seed each, 64 spp; the caustic is noisy).

More checks:
- Equiangular MIS on bent rays (point light inside a scattering lens, n up to 1.2; three seeds each, 96x96, 32 spp):
  0.21758 +- 0.00025 against 0.21791 +- 0.00259 with the densities of the medium alone (ten times less error).
- The limit of no bending (amplitude 0.0002) against no refraction, same scene, equiangular MIS: 0.22723 +- 0.00286
  against 0.22527 +- 0.00075; at depth 1: 0.14994 against 0.14940.
- An aggregate (three grid puffs, uniform light) held by a refractive medium with negligible bending: `aggregate2`
  0.97708, `aggregate` 0.97772, against 0.97758 for the plain aggregate.
- Polarised production path against `polpath` (surfaces: glass and rough conductor spheres) and against polarised
  `volpath` (Rayleigh fog under a distant light), 256 spp: intensity 0.20752 / 0.20784 and 0.06368 / 0.06372; S1 -0.00083
  / -0.00083 and -0.05240 / -0.05245; analyser 0: 0.20669 / 0.20701 and 0.01128 / 0.01127; analyser 90: 0.20835 / 0.20866
  and 0.11608 / 0.11617.
- The same fog inside a refractive medium with negligible bending: S1 -0.05261, analyser 0 0.01125, analyser 90 0.11648.
- `RefractiveVolume.CarriedAxis`: the carried axis against a hundred small turns per piece, 3.7e-5; across the path 3e-8.
- `RefractiveVolume.Walk` also checks the total weight of the collisions against quadrature.

Found on the way:
- The bent light of a delta light did not take the weight against the equiangular point (65% too bright with
  `"distancesampling" "mis"`); fixed.
- The furnace did not catch a wrong `departure` (it was reset at every surface that only bounds a volume, so the light
  densities were those of the direction after the bend); the test against no light sampling did (12% low inside the lens).
- `new Transform()`'s inverse is not usable for a box: `GridMedium.MajorantOver` maps the corners with `ApplyInverse`.
- Along the axes of the lattice the image through a strong lens (32 cells) shows spokes at the rim: the worst-case paths
  of `HarmonicConvergence`. A finer lattice reduces them.
- Cost: the furnace with the lens takes about 5 times the one without (a path is traced whole before it is tracked, with
  lists allocated per ray).

Earlier status of step 1:

