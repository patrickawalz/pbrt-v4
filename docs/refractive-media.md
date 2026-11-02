# Refractive radiative transfer (heterogeneous refractive index)

Goal: render media whose refractive index n(x) varies in space (heat haze, ultrasound lenses, GRIN optics), so that light
travels on curved paths, with both the volumetric path tracer and bdpt, polarised or not.

References: Pediredla et al., *Path Tracing Estimators for Refractive Radiative Transfer* (TOG 2020; reference
implementation MitsubaER, a Mitsuba 0.5 fork, so GPL-derived and **not** copied: the code here is written from the paper);
Fraboni et al., *Can You See the Heat? A Null-Scattering Approach for Refractive Volume Rendering* (SIGGRAPH 2023 talk).

## Design

* Pediredla is the core, because it serves both integrators: next-event estimation in a path tracer and the connections of
  bdpt are the same problem, finding the curved path between two points (a boundary value problem on the eikonal
  equation). Fraboni bends the ray at null events; it has no estimator for connections, and its own conclusion leaves the
  formulation to future work, so it is not used.
* The scattering coefficient may vary along the curve: the transmittance of the paper is `exp(-integral of mu_t(x(s)) ds)`
  in the arc length s, which null-collision tracking estimates along any parametrised path. The paper's code uses a
  constant coefficient; the heterogeneous case here is new and is validated against the constant case and a brute-force
  reference (stage 4).
* The majorant has to be a function of the position, not of the integrator step, so that the forward and the reverse
  walk of the same curve have the same transmittance (bdpt needs both).
* The derivative of a step (`SymplecticRayJacobian`) is the exact derivative of the step. Algorithm 2 of the paper evaluates
  n and its gradient at the end of the drift for the derivative of the drift; that differs from a finite difference of the
  integrator, and the test of the Jacobian rejects it.

## Stages

| Stage | Content | Status |
| --- | --- | --- |
| 1 | `RefractiveIndexField`: cubic B-spline grid with value, gradient and Hessian (`GridRefractiveIndexField`); analytic `LuneburgRefractiveIndexField` and `LinearRefractiveIndexField` for the tests | done (`refractiveindex.cs`) |
| 2 | `SymplecticRay` (algorithm 1) and `SymplecticRayJacobian` (algorithm 2, exact) | done (`refractiveray.cs`) |
| 3 | Connection solver: BFGS on the initial direction, the end point of the closest approach, the multiple-path estimate (algorithm 3) | done (`refractiveconnection.cs`), type-2 connections only |
| 4 | `RefractiveMedium`: bounded, distance sampling along the curve, `n_end^2 / n_start^2` on the throughput, type-1 connections | done (`refractivemedium.cs`) except the boundary stack and the scene wiring, which belong to stage 5 |
| 5 | Volpath: the walk, and next-event estimation through stage 3 | done for area lights and a non-polarised volpath (see below) |
| 6 | bdpt: the connections, the reverse densities from the end direction of the curved edge; equiangular sampling is off on refractive segments | 6 done (6a–6e); 7 done: polarised volpath and bdpt (see "Stage 6 design") |
| 7 | Polarisation: the Stokes frame follows the curve, no Fresnel at the bending, the end directions in `ConnectionMueller` | open |

## What the tests establish

The grid field matches a smooth analytic field (value 1.5e-6, gradient 3.7e-5, Hessian 6e-3 at most), is exactly linear for
a linear field also outside its bounds, and the analytic fields have gradients and Hessians equal to finite differences. The
integrator follows the closed-form path in `n = n0 + g z` (`n(x) = C cosh(g x / C + a)`) with an error of 0.036 times the step,
keeps `|v| = n` to the same order, conserves the transverse optical velocity to rounding, and focuses a parallel beam through the
Luneburg lens onto its surface point with an error of 2.6e-5 at the step 0.02. The Jacobian equals a finite difference of the
integrator to 6e-5 for an analytic field and for the spline. Each of seven deliberate breaks of the code (a basis weight, the
prefilter, the second half kick, the paper's variant of the derivative, the normalisation of the direction, the
extrapolation, a Hessian term) is caught by at least one test.

Stage 3: the derivative of the closest-approach error (including the change of the arc length s* with the direction) equals a
finite difference to 7e-5; the solver finds the straight line in a constant index and, through the Luneburg lens, a path that
the plain integrator retraces to 1.3e-4 of the target. With one path the estimate of algorithm 3 has the mean 0.977 (689 of
1500 searches found a path, the rest pointed away), consistent with the expected 1. Six deliberate breaks (the s* term, the
velocity term, the gradient term of its denominator, the crossing test, the count, the null-path rule) are caught. A loose
same-path test is not caught, because with one path every success is the same path: **the estimate with two or more stationary
paths is not tested**. Type-1 connections (an end point on the medium boundary) wait for stage 4.

Stage 4: `RefractiveMedium` wraps an ordinary medium (the coefficients), a `RefractiveIndexField` and a box, with a constant
majorant. `SampleT_majCurved` samples tentative collisions in the arc length on a step grid fixed from the start of the
path (partial steps give the collision points, so the curve does not depend on where the collisions fall), reports the point
and the tangent at each, and stops at the exit from the box (bisection on a partial step) or at the maximum length. Ratio
tracking with a coefficient that varies along a curved path (Luneburg lens, sigma = 0.6 + 0.4 sin(3x + 2z)) gives 0.29240
against the quadrature 0.29257 with 800000 samples; the collisions are Poisson in the arc length (1.589 against 1.6 with
8000 walks); the end point and direction equal those of the plain integrator to 7e-7, the exit from the box is exact to 1e-7,
and a connection to a point on the box (type-1) reaches it to 2e-6. Seven deliberate breaks are caught. Not done: the
majorant is one constant per medium, so a heterogeneous inner medium needs a user supplied bound (no automatic bound from
grids yet); `Medium.Create` has no "refractive" name yet, and `SampleT_maj` does not dispatch to this class (the integrators
call `SampleT_majCurved` explicitly in stage 5).

Stage 5, part 1 (the estimator of the next-event connection, before the integrators): `ConnectToPoint` finds a path from a
point inside the medium to a point anywhere (the field is `BoundedRefractiveIndexField`: the inner field in the box, index 1
outside, so the box boundary has to be index matched), and `RefractiveConnectionPath.areaJacobian` is dA/d(omega), the area
that a unit of solid angle of start directions covers on the plane perpendicular to the end direction (s^2 for a straight
path). The estimate of an integral over the directions at a point by sampling a point of a tilted emitting patch, finding the
curve and weighting by `count * cos / J * area` equals the estimate by sampling directions and following the curve (0.00233
+- 0.00003 against 0.00224 +- 0.00006, the Luneburg lens with a varying absorption). Dropping the count, the cosine or the
scale of |w| in J is caught. Not caught, because it does not change the result: the longitudinal part of the derivative (the
s* term) and the projection on the end direction in J; the transverse part is all that J uses.

The n^2 factors of the radiance telescope along a path whose camera and light are outside the medium (basic radiance L / n^2
is conserved along the curves and by scattering), so they are 1 for the paths here and matter only for endpoints or emission
inside the medium, which are not supported.

Stage 5, volpath (`pathwalk.cs`, `VolPathIntegrator`): the shared walk follows a curved path when the top of the boundary stack is a
`RefractiveMedium` (scene: `MakeNamedMedium "x" "string type" "refractive"` with `"string field" "luneburg"` or `"grid"`, the coefficients
of a homogeneous medium, `"float stepsize"`; the boundary shape of the medium must enclose exactly its region, have an
`interface` material, and the index must be 1 on it). The collisions are sampled by `SampleT_majCurved`; at the exit the
bounding surface is found by a short ray ending at the end of the path and crossing it pops the medium (a path shorter than 1e-4
of the diagonal pops the medium directly). From a scattering point inside the medium the area lights are found by next event
estimation along the curved path: `SampleLdRefractive` solves the connection, weights by `count * cos(end) / J / pdfArea` and
takes the transmittance of the curved part (ratio tracking in arc length) times a straight shadow ray from the boundary. The
two strategies are not combined by MIS: the curved NEE finds the area lights from such a vertex, the walk's own hit of an
area light from there has no weight (nothing is added), and the infinite lights are found by the walk alone with weight 1
(`refractivenee false` makes everything the walk's). A straight shadow ray through a refractive medium is blocked, and an
emission found after a curved segment from a vertex outside is added with weight 1, because the straight connection that the
light sampling would have used does not exist for it. lightpath refuses refractive media
(bdpt: stage 6; polarisation: stage 7). Not supported: delta lights and lights inside the medium from a vertex inside the medium (error), surfaces
inside the medium (error), a rotation in the transformation of the medium.

What the tests show: the estimate of the single scattering radiance through a Luneburg lens in a sphere, next to a spherical
light, is 2.983e-3 +- 2.4e-5 by next event estimation and 3.0095e-3 +- 8.0e-6 by the direction of the phase function alone
(difference 1 sigma). Correction of the first numbers of this stage (1.7835e-3 and 1.7776e-3, which agreed as well): the test
cast the same ray for every sample, and the walk seeds its random numbers from the ray (`Hash(ray.o, tMax)`), so all samples shared
them after the first collision and the standard errors were wrong, and so were the means. The test now moves the ray origin by
1e-4 per sample, as the film does. The 4 to 6 percent difference I put down to noise in an earlier configuration of the same
test probably had that cause too, but I have not rerun that configuration to confirm it. The agreement test is not sensitive to the
Jacobian and the count (dropping either changes the result by 2 to 5 percent here, the light covering a wide range of
directions); those are checked locally: `areaJacobian` equals a finite difference to 4e-4 (8.69 against 11.1 for the straight
line) and the count is checked in `SinglePathEstimateIsUnbiased`.

Medium emission and grids (after stage 5): the emission of the medium needs no special code in volpath: `BeginCollision` adds
`sigma_a Le` at every tentative collision of the curved walk, with the weights of the straight one (the emission is not a light,
so there is no MIS, and `Le` is the emitted basic radiance, so no n^2 factor). `RefractiveMedium` wraps any inner medium, and a
scene can scale the extinction and the emission of its homogeneous medium by grids: `"float density"` and `"float emission"`
(`nx * ny * nz` values over the bounds of the field, x fastest, trilinear, zero outside), implemented by `GridModulatedMedium`;
the majorant is the extinction times the largest density. `"rgb Le"` and `"float Lescale"` give the emission that the
grid scales. Test `EmissionAlongTheCurveMatchesQuadrature`: the volpath radiance along a camera ray through a sphere of a
constant medium, of a medium whose coefficients vary like sinusoids, and of the density and emission grids, in a straight path
(index 1) and through the Luneburg lens, equals the integral of `sigma_a Le exp(-optical depth)` on a finely traced curve; the
six cases agree within 1.5 standard errors (about 0.3 percent). The oblique ray makes the test sensitive to the interpolation in
z (a mutation that drops it fails; with the first, near planar ray it passed). Still unsupported: emission in the sense of light
sources inside the medium, `VolumeEmissionLight`, which the curved NEE ignores.

## Stage 6 design (bdpt through curved edges)

What is settled, with the checks that settle it:

* No n^2 factors at interior vertices: the basic radiance L / n^2 obeys the transport equation with the local solid angle, so the
  throughput of a path has no index factors except where an endpoint is inside the medium (not supported).
* The geometry term of an edge is `cos(start) cos(end) / J`, J = dA_perp(end) / dOmega(start) of the curve, which is d^2 in a
  straight edge. J is not symmetric: conservation of n^2 dA dOmega gives `J(a to b) = (n_a / n_b)^2 J(b to a)`. So the reverse
  densities of a vertex pair use J of the other direction through this relation, and a connection takes J in one fixed
  orientation (camera to light) for every strategy. `RefractiveConnection.TraceEdge` returns J for any edge, and the test
  `ReversedEdgeHasTheJacobianOfTheConservedThroughput` shows the relation: with steps of 0.02, 0.01, 0.005 and 0.0025 the
  two sides differ by 1.0e-2, 8e-3, 4e-3 and 1.4e-3 at most (relative), so it holds in the limit (these numbers are from the first order integrator that was replaced below; the test now passes with the second order one).
* One trace on the bounded field (index 1, gradient 0 outside) covers an edge that is straight, curved and straight, so the
  Jacobian of a whole edge needs no composition of pieces.

Integrator change found on the way (after the first commit of stage 6): the drift of the step divided by the index at the start of
the step (`x += s v / n(x)`), which makes the integrator first order in the step and not reversible. Against a fourth order
Runge-Kutta reference on the Luneburg ray (arc length 1.8, height 0.3) the position error with steps 0.04, 0.02, 0.01 and 0.005
was 5.5e-3, 2.9e-3, 1.5e-3 and 7.8e-4. The Hamiltonian |v| - n of the paths is separable, so the drift along `v / |v|` is the
Stoermer-Verlet step of it, second order and reversible: the same errors are 2.8e-5, 7.0e-6, 1.8e-6 and 4.4e-7. Both
`SymplecticRay.Step` and the exact derivative in `SymplecticRayJacobian.Step` use it now, and all earlier suites pass (the
finite difference tests of the derivative included). The first kick of a path is evaluated a little ahead of its start
(`SymplecticRay.StartNudge`, 1e-5), because a path that starts on the boundary of a medium has rounding put it on either side of
a gradient that jumps there.

Remaining limit: a field whose gradient jumps at the boundary of the region (the Luneburg lens) loses accuracy at the step that
crosses the boundary, because the step does not end there: a solved curve that crosses it differs from the walk's by up to
7e-3 in position and 0.02 in direction at step 0.05, and the connection solver then fails for about 10 percent of the edges that
start outside the lens. A field that is smooth across the boundary does not have this: `BumpRefractiveIndexField`
(`n = 1 + A (1 - (r / R)^2)^3`, C^2 at the sphere) is the lens of the bdpt tests, and the heat haze of the scenes has index 1 and
zero gradient at its boundary by construction.

Stage 6b (done): `BDPTWalkHook` records, for the edge that arrives at each vertex, whether it ran through a refractive medium
(`Vertex.curvedEdge`) and the edge (`Vertex.edge`: leaving and arriving directions, the area Jacobian in the direction of the
walk and of its reverse). The curve is solved between the camera side vertex and the light side one whichever way the walk went
(`RefractiveMedium.SolveEdge`, started from the walk's direction, so that it is the walk's curve to the tolerance of the solver),
and traced again from the walk's start if the solver fails. Tests (`RefractiveBDPTTests`, bump lens with a scattering medium):
for 5864 edges of camera subpaths the solved curve reaches the vertex within 1.4e-5, the arrival direction agrees with the
vertex's `wo` within 6.8e-6, the start direction is the camera ray's, there are no fallbacks, and the Jacobian agrees with a trace
with a tenth of the step to 9.4e-4 (the end point to 5.3e-4); for 297 edges of light subpaths the fine trace from the previous
vertex reaches the vertex within 2.6e-4 with the Jacobian within 3.4e-3, and the reversed trace the previous vertex within 6.2e-4
with the reverse Jacobian within 2.4e-3. The edges are recorded and nothing uses them yet.

Sub-stages: 6a the edge Jacobian and the reversal relation (done); 6b the camera and light walks of bdpt store, per vertex, the
tangents at both ends of the edge that arrives there and the two Jacobians (done); 6c `Vertex.PDF`, `ConvertDensity` and the MIS
remapping use them instead of the positions; 6d the connection through the solver (count, J, visibility along the polyline,
transmittance by ratio tracking along the curve, the spectral MIS ratios of the connection segment); 6e the test: bdpt against
volpath on the lens scene (same single scattering radiance within the standard error), and the refusal removed.

### Stage 6c: densities through curved edges

`EdgeGeometry` (curved flag, leaving direction, arriving direction, Jacobian) describes an edge as traversed from one vertex to the next. `Vertex.ConvertDensity`, `PDF`, `PDFLight` and `PDFLightOrigin` take the edges to the previous and next vertex: a curved edge uses its leaving direction for the BSDF/phase evaluation and its Jacobian in place of the squared distance, with the cosine taken against the arrival direction. The walk hook stores `pdfFwd` and `pdfRev` through them, and `MISSum` takes the connection edge (not yet curved; that is 6d). Test: for 2947 curved medium vertices `pdfFwd` equals the phase-function density over the edge Jacobian, and `Vertex.PDF` agrees.

### Stage 6d/6e: curved connections, bdpt enabled

`BDPT.CurvedConnection` connects a vertex inside a refractive medium to any other vertex with the solver (`SolveMultiple` count, curved transmittance by ratio tracking, straight leg from the bounding surface), and the contribution is f·f·beta·beta · count · (cos·cos / J). Used for s>=2,t>=2 and for light samples (s=1) to area lights. Strategies that cannot be curved are excluded consistently from the MIS sums: t=1 (camera sampling) and connections along a curved edge with neither end inside the medium (`CurvedConnectionAllowed`); such paths are found by the walks.

Verified: bdpt with s<=1 against volpath on the bump lens (0.000813 ± 1.0e-5 vs 0.000807 ± 1.0e-5); the weights of the walk-hit and the light-sample strategies of the same path add up to one (worst 1.2e-7, 459 curved paths; mutations of the stored density and the connection edge at the light vertex are caught, the one at the camera vertex is an equivalent mutation because t=1 is excluded); full bdpt against volpath on a grid-index lens with a backdrop and a sphere light (40x30, 256 spp): mean ratio 1.003, difference z = 0.97 (lens region z = 0.26).

The light point of the curved next event estimation (volpath `SampleLdRefractive` and bdpt s=1) is sampled by area (`DiffuseAreaLight.SampleArea`, density pdfPos·pmf, the density that `PDFLightOrigin` assumes). Retraction: an earlier version used `SampleLi`, which samples only the cap of the light that is visible along a straight line; a curved path reaches points beyond it, so both estimators lost that energy, and the agreement reported above ("within noise") hid a bias of about +1% (bdpt over volpath; the unweighted strategies of a large light differed by +3.2%, z = 2.4, and S1 by about 5%). With area sampling: unweighted strategies against the light sample, z = 0.2; full bdpt against volpath at depth 2 and 3, z = -1.0 and -0.3 (a 4-minute check that is not kept in the suite); polarised I, S1, S2 of volpath and bdpt within 1 sigma. `Vertex.Le` for an emission found after a curved edge uses the direction in which the edge arrives (it changed no measured number, since the strategies that use it are rarely the ones that carry the weight). Renders (32x24, 192 spp, grid lens with a backdrop and a sphere light, three independent seeds): bdpt over volpath +0.7%, +1.5%, +0.8% (z 0.6, 1.3, 0.7; combined about +1.0%, z 1.5). Retraction of the retraction: I later read these small positive differences, and a +8% difference in one depth-2 crop, as a regional bias of bdpt. Three independent seeds of the crop (32x24 left window, 256 spp, depth 2) show seed-to-seed spreads of 4%: volpath with curved NEE 0.04547 ± 0.00171, volpath without it 0.04842 ± 0.00089, unidirectional bdpt 0.04637 ± 0.00166, all within about 1.5 sigma. The unit tests agree: with the lens a box as in the renders, the unidirectional pair weighted total equals the walk (2.77e-5 ± 0.8e-6 against 2.87e-5 ± 0.9e-6) and all strategies together are at z = 2.6 with a connection (2,2) whose unweighted mean is too noisy to resolve (a scan at lens amplitudes 0.0005 to 0.15 gave z within 1 for the total). I found no bias that survives more samples.

A real pitfall I found on the way: a lens whose index is not 1 on its boundary (my first render-grid unit test bounded the grid with a sphere inside its box: |n-1| up to 0.10 there) has a step in n that the medium does not model; `RefractiveMedium` now warns when the field is more than 0.005 from 1 on the boundary. The render-grid unit tests use the box boundary of the renders.

Limits: area lights only (infinite, delta and volume lights error out), no equiangular sampling, lightpath still refuses, no surfaces inside the medium.

### Stage 7: polarisation

Light in an isotropic medium with a smoothly varying index keeps its polarisation along the ray without turning about it (Rytov's law, the parallel transport of the electric field), and there is no Fresnel interaction in the bulk. A curved edge therefore carries the Stokes vector between the frames at its two ends by the angle between the frame at one end and the other end's frame carried along the path. `PolarisationTransport` carries a vector of the transverse plane along the path with the steps of the integrator (a rotation about the normal of the old and new tangent at each step); `Attenuation.Transport` is the identity Mueller element between the frames (z along the light, x the carried vector at the entry and the exit). The walk flushes the transport before every vertex that follows a curved edge (`WalkContext.FlushTransport`), the curved connections of bdpt trace the connection edge with the same carry, and the connection Mueller matrices use the directions that the edge leaves along instead of the straight direction to the vertex.

Verified: the transported vector stays perpendicular and of unit length, converges with the step, is fixed out of the plane for a planar path, and for a path with torsion it turns relative to the normal by minus the integral of the torsion computed from the tangents alone (integral -0.7530, turned +0.7531); the polarised radiance of volpath (next event estimate) and of the sampled direction agree; bdpt with s<=1 agrees with volpath for I, S1 and S2 of a side-lit Rayleigh lens (S1 is -87% of I there; I 3.50e-5 ± 1.1e-6 against 3.64e-5 ± 1.3e-6 after the area-sampling fix); a scene symmetric about a plane measures no S2 (3.6e-7 ± 2.5e-7 against S1 = -3.6e-5). Dropping the carry changes S2 of that scene by 13% (3.8 sigma); the volpath/bdpt comparison cannot detect that, since both use the same carry, which is why the torsion test exists.


### Volume lights (emissive `TextureMedium`) with refractive media

Volpath: `SampleLdRefractive` handles `VolumeEmissionLight`: the point on the emitting volume is sampled by `SampleLiVolume` (density per volume), the curved connection to it is solved, and the contribution is beta·f·T·Le / (r_p·p_l·r_l) with p_l = pmf·pdfPoint·J/count (J converts the density per volume of the point to the direction at the start, dV = J·dω·ds). With `fnee` the light is sampled by a direction (`SampleDirection`), the curve is traced with null-collision tracking, then the FNEE line loop (`FneeAlongRay`) runs outside the medium; the straight-line direction density is a valid proposal for curved rays, and MIS with the phase direction (always positive) keeps it unbiased. A walk that reaches an emissive volume through a curved edge gathers its emission only where NEE could not have found it (the NEE-only policy from vertices in the lens). bdpt: the s=1 branch connects to a `VolumeEmissionLight` sample with `CurvedConnection`; camera subpaths gather `MediumEmissionEnd` vertices (s=0), and light subpaths can start inside the volume (`SampleLeVolume`). Test: `RefractiveVolPath.VolumeLightThroughTheLensAgreesAcrossStrategies` (gather only, point NEE, FNEE agree at 4 sigma). Renders: single-seed ref/point/fnee/bdpt differ by about 1%.

Depth convention: volpath (pbrt-v4's rule) gathers the emission of a medium only while depth < maxdepth, and its light sample of the medium has the same limit, so the emitting collision counts as a vertex; bdpt allowed one more scattering before such an emission. The mismatch looked like a bias that grew toward the dim lens pixels (bdpt over volpath +14% to +70% by column; polarised S1 +10%, z = 7, with the medium-emission gather switched off it was unchanged). `ConnectBDPT` now leaves out paths whose light end is a medium emission and that have more than maxdepth - 1 scatterings (whole paths, so no MIS weight changes). After it, S1 agrees (bdpt 0.00419 ± 0.00007 against volpath 0.00413 ± 0.00004) and the intensity in the lens columns is within +4% to +7% at depth 4 (bdpt 0.01860 ± 0.00006 over 3 seeds against 0.0173–0.0179 for volpath ref/NEE/FNEE); the single-seed depth-resolved ratios are +7% (depth 2) and -4% (depth 3), which is at the single-seed noise, so a small residual is not excluded.

Limits: the lens's own emission is not a `VolumeEmissionLight`; a point light inside the same refractive medium errors; the FNEE proposal uses straight lines; polarised bdpt with volume lights agrees with volpath for S1 (above); lightpath still refuses refractive media.
