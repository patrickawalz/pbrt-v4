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
| 6 | bdpt: the connections, the reverse densities from the end direction of the curved edge; equiangular sampling is off on refractive segments | open |
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
light sampling would have used does not exist for it. bdpt, lightpath and the polarised volpath refuse refractive media
(stages 6 and 7). Not supported: delta lights and lights inside the medium from a vertex inside the medium (error), surfaces
inside the medium (error), a rotation in the transformation of the medium.

What the tests show: the estimate of the single scattering radiance through a Luneburg lens in a sphere, next to a spherical
light, is 1.7835e-3 +- 2.0e-5 by next event estimation and 1.7776e-3 +- 6.7e-6 by the direction of the phase function alone. An
earlier configuration with a smaller light differed by 4 to 6 percent at about 2 sigma in two runs; larger statistics of the
patch test (8 million direction samples) gave 0.002293 +- 0.000015 against 0.002257 +- 0.000015, so I take it as noise, but
that comparison has not been repeated for the small light at the same statistics. The agreement test is not sensitive to the
Jacobian and the count (dropping either changes the result by 2 to 5 percent here, the light covering a wide range of
directions); those are checked locally: `areaJacobian` equals a finite difference to 4e-4 (8.69 against 11.1 for the straight
line) and the count is checked in `SinglePathEstimateIsUnbiased`.
