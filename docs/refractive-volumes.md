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

Done since:
- the search for the path to a point or a direction carries its derivative along the ray in closed form
  (`CurvedPath.Carry`: within a piece the change of the point grows by sigma times the change of v; at a face the change of v
  takes (A_before - A_after) times the delay of the crossing), halves its steps until the miss falls, starts some searches
  anywhere on the sphere, and counts the repeated searches without a cap (Russian roulette past 24);
- the area across the ray at the target is taken across the ray as it arrives (it was taken on the plane across the
  straight line: too bright by one over the cosine between the two);
- the surfaces along a path are tested in chords of several pieces while the path has turned by less than 4e-4 rad;
- the emission of the held medium is sampled as a light (`"emission_nee"`): the direction drawn towards the emission is
  followed as the field bends it (`Volume.Gather`);
- once-more scattering (`"oncemore" "omnee" | "omnee2"`) on bent rays: b is put in the scene as before, the direction that
  reaches it is searched (`VolumeServices.ConnectBent`), the stretch is walked along that path (`Volume.ReachBent`), and
  the squared distance gives way to the area across the path per solid angle, in the estimate and in the densities of
  the rivals on both sides (`OnceMoreFinishBent`, `JointOnCurve`).

- a refractive medium as a member of an aggregate (one member at most): the aggregate is made of the medium that the member
  holds, with the others, and is held by a refractive medium of the member's field, so that the rays bend through all of
  it (three puffs, n up to 1.2: 0.97648 +- 0.00020 as a member against 0.97639 +- 0.00005 holding the aggregate).

Checked with a lamp beside (not inside) a scattering lens, where the means have a finite variance (64x64, 16 spp, sixteen
to forty seeds, under ten seconds a render; light sampling alone 0.13377 +- 0.00017 with the lens, n up to 1.1):

| strategies | mean |
|---|---|
| once-more `omnee` | 0.13373 +- 0.00018 |
| once-more `omnee2` | 0.13372 +- 0.00022 |
| joint (`"doublescattering" "joint"`) | 0.13399 +- 0.00021 |
| equiangular (`"distancesampling" "mis"`) | 0.13382 +- 0.00016 |
| all three | 0.13384 +- 0.00012 |
| all three, negligible bending (0.13458 +- 0.00020) | 0.13432 +- 0.00011 |

Two errors that this scene found (the earlier checks, with the lamp inside the medium, had 1 % of noise and passed them):
- the walk along a curve kept a copy of the renderer's random numbers and gave it back at its end, so what the renderer
  did for it meanwhile -- the shadow ray of a light sample -- used the numbers the walk had just used. Equiangular
  sampling on bent rays was 0.8 to 1.1 % too bright by it. The numbers are handed over and back at each call now;
- two searches for the same path were known for the same by their directions being within 1.4e-3 rad, which is less than
  the search can tell for a target near by: the count of the searches ran on, and once-more and joint scattering, whose
  connections are short, were up to 1.3 % too bright. The test goes by the tolerance of the search now.
With these, joint double scattering on bent rays (`JointShareBent`) is on, as is the equiangular point (`"bool
bentjoint"`, `"bool bentequiangular"` turn them off). The held medium no longer draws joint points itself inside a
refractive medium: it connected them along straight lines (1.2 % too bright).

Not done:
- in polarised mode the volumes gather no light of their own (equiangular, joint, once-more are off), and a
  single-scattering volume's own light and the sampled emission are added as unpolarised light;

## The refractive atmosphere

`MakeNamedMedium "sky" "string type" "refractiveatmosphere" ...` takes every parameter of `"atmosphere"` (it holds that
medium, so its air, aerosols, ozone and phase functions are the same code) and bends the rays:

- the index is that of air of the atmosphere's own density at each height, n - 1 = (n_s - 1) rho(h) / rho(0), with Edlen's
  refractivity of standard air n_s(lambda) (2.78e-4 at 550 nm);
- the field is asked for in closed form on a lattice that stores nothing (`"float indexcellacross"` 1e4 m,
  `"float indexcellup"` 200 m, `"float indexextent"` 1.5e6 m to each side of the top of the planet, `"float indextop"` 6e4 m);
  20 km by 500 m cells move the limbs of the setting sun by 1 arc second against 10 km by 250 m;
- `"bool dispersion"` (true): the n^2 - 1 of the field is scaled by n_s(lambda) / n_s(550 nm) for the wavelength of the path.
  The path tracer then follows the light seen along the camera ray once for each wavelength of the sample, and walks the
  scattered path with the first wavelength alone (its samples are all of that wavelength while it is walked);
- `Integrator "production" "bool seethrough" true`: the infinite lights along the camera ray come with an estimate of the
  transmittance (not only where the walk gets through); with `"string transmittance" "raymarching"` on the medium the sun
  through 30 air masses is smooth at 4 samples (ratio tracking is all or nothing there: the shells bound the air tightly);
- the atmosphere bounds its extinction over a piece of a curve by the heights that the piece spans (`MajorantOnPiece`).

Checks: `RefractiveField.AstronomicalRefraction` (36.15 arc minutes along the horizon against 36.17 from the integral for
an exponential atmosphere). In the renders the sun whose centre is 0.5 degrees below the horizontal stands on the sea
horizon of a viewer 30 m up, 27 arc minutes high (32 wide); at 1.0 degree below only a sliver is left.

The sun is sampled along bent rays (`BentSunLight`): the direction of the disc that was drawn is taken as a light from
that one direction, of the irradiance L / pdf, and the direction that leaves the point for it is searched; the paths that
a volume bends between a point and the sun count by that sample alone. Sky with a sun 30 degrees up, three seeds each
(means of R, G, B): plain atmosphere 0.1201 0.1462 0.1956, refractive without dispersion 0.1189 0.1447 0.1942, with
dispersion 0.128 +- 0.006, 0.144, 0.197.

Found on the way: the production path took the phase function's value at the first wavelength for all four wavelengths
of a sample (`p` where `pSpectral` was due). With the atmosphere, whose mix of air and aerosol scattering depends on the
wavelength, that sky came out 21 % too dark in red and 15 % too bright in blue; fixed in the path tracer's light sample
and scattered ray (the volumes' own light sampling and once-more scattering still take the one value).

`"integer raymarchinglookups"` (on the medium): the ray-marching estimator's own rule gave 14 lookups over the 1200 km of
a ray along the horizon, and rays below the horizontal had a spread of 2 to 4 times their mean; 0.12 with 48 or more.

Not done there: inversion layers and other weather (the standard atmosphere has none, so no mock-mirage flashes); the
limb darkening is the approximate one of the sun light; the final frames (light seen directly, `"maxdepth" 0`, which
measured 2500 times the sky beside it) were rendered before the lookups setting and are grainy below the horizontal.

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

Later checks (image means):
- Search: a lamp inside a lens (160x160, 32 spp) 84 s -> 36 s; the carried derivative against central differences at 400
  converged connections: median ratio of the determinants 1.0000; the spread carried along a path against the spread the
  search finds for the same two points: equal to four digits.
- Emission sampling in a lens (n up to 1.25), three seeds: sampled 0.03485 +- 0.00047, not sampled 0.03502 +- 0.00054;
  negligible bending 0.03608 +- 0.00027 against the plain medium 0.03562 +- 0.00043.
- Once-more scattering, lamp outside a scattering lens (n up to 1.1), twelve seeds: light sampling alone 0.13375 +-
  0.00028, `omnee` 0.13420 +- 0.00034, `omnee2` 0.13357 +- 0.00021; negligible bending 0.13400 / 0.13396; the plain medium
  0.13367 / 0.13440. (With the lamp inside the medium the inverse square of the distance gives the means no finite
  variance, and nothing can be read from them.)
- A point light seen through a lens focuses to caustics, where the exact spread goes to zero: bright isolated samples,
  which the derivative by differences had blurred.

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

