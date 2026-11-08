# Atmosphere medium and planet shape

A volumetric model of the Earth's atmosphere for the ordinary volume integrators. It replaces a fitted sky model (and its data
files) with the transport that such models are fitted to: single and multiple scattering in a spherical shell of air, aerosols and
ozone around a planet. Nothing is precomputed.

```
MakeNamedMedium "sky" "string type" "atmosphere" "float visibility" 50
MediumInterface "" "sky"                       # before the Camera (the camera is inside the atmosphere) and before the planet
Shape "planet" "float radius" 6360000 "point3 center" [0 -6360000 0]
LightSource "sun" "point3 from" [...] "point3 to" [0 0 0] "blackbody L" 5778 "float illuminance" 3   # a finite sun disk with limb darkening
```

Files: `src/pbrt/atmosphere.cs` (profile, medium, majorant iterator, phase function), `src/pbrt/mie.cs` (Mie solver and tabulated species), `src/pbrt/planet.cs`, `SunLight` in `src/pbrt/lights.cs`,
`src/pbrt/atmosphere_test.cs`.

## What it contains

| Part | Model | Status |
|---|---|---|
| Air number density | U.S. Standard Atmosphere 1976 layers (0 to 84.852 km geopotential), exponential tail above | standard model; checked against the tabulated density ratios |
| Rayleigh cross section | Bodhaine et al. 1999 with the King factor (300 ppm CO2) | vertical optical depth at 550 nm is 0.097, as published |
| Ozone | U.S. standard atmosphere profile (libRadtran `afglus`, column 344 DU) and the Serdyuchenko-Gorshelev cross sections at 11 temperatures, 193 to 293 K (IUP Bremen), interpolated at the temperature of the altitude; the majorant of a shell uses the larger of the two bracketing tables over the temperature range of the shell. 360 nm and up, 2 nm bins | profile from libRadtran 2.0.6, cross sections from IUP Bremen |
| NO2 and O4 | NO2: Bremen polynomial in temperature (C0 + C1 dT + C2 dT^2, clamped to its range of validity, to 760 nm) times the afglus profile; O2-O2 pair: Greenblatt cross section times n(O2)^2 | from libRadtran 2.0.6 files, read as data |
| Sun | finite disk (0.5334 deg diameter), linear limb darkening with a wavelength dependent coefficient (0.93 at 400 nm to 0.43 at 900 nm; approximate, not the Neckel-Labs table); irradiance equals that of the distant light with the same parameters (tested) | approximate coefficients |
| Aerosols | OPAC standard mass concentration profiles (libRadtran `continental_clean`, `continental_average`, `continental_polluted`; species inso, waso, soot), **as spheres** (also inso and soot, which are not spherical in reality). Extinction, albedo, asymmetry and the polarised phase function matrix are computed at start-up by the Mie solver of the engine (Bohren-Huffman, double precision, 600 radii, 48 wavelengths) from the OPAC refractive indices and log-normal size distributions (about 4 s on 2 cores); the extinction at the ground at 550 nm is 3.912 / visibility (Koschmieder, Rayleigh included) and the shape of the profile follows the visibility (see below) | tested against miepython tables to 5e-6 |
| Phase function | Rayleigh (with the King depolarisation) mixed with the tabulated Mie phase functions (about 785 angles, 0.01 deg at the forward peak) of the species; sampled by inverting the cumulative table | exact per wavelength (see below) |
| Planet | a sphere intersected in double precision | float quadrics have about 0.5 m error at 6.36e6 m |

Not modelled: clouds, the sulfate (suso) layer of the OPAC files above 12 km, ozone absorption below 360 nm (the table starts at 360 nm, the
lower end of the renderer's spectrum), **H2O and O2 line and band absorption** (needs a correlated-k data set, the libRadtran reptran files are
NetCDF and were not readable here), refraction (the paper does not model it either), **nonspherical INSO and soot** (spheres are used; the
dust-like particles of INSO are strongly nonspherical in reality, soot is a fractal aggregate; spheroid or Rayleigh-Debye-Gans tables could be
plugged in through the `MieSpecies` table interface), ground albedo and observer altitude parameters.

Notes on the data:

* The `soot` column of the libRadtran files is zero in the boundary layer (0 and 1 km) and nonzero above 2 km. The profiles are
  therefore not monotone and the majorant of each shell is the maximum over the ends of the shell and the table nodes inside it.
* The Mie extinction gives an aerosol optical depth at 550 nm of 0.038, 0.088 and 0.193 for the three profiles with their own
  concentrations. The ground visibility (3.912 / (aerosol plus Rayleigh extinction at 550 nm)) of the three profiles is 130.4, 59.1 and
  27.6 km, and the sky model of Wilkie et al. (viewing distances 131.8, 59.4 and 27.6 km for the clean, average and polluted profiles)
  agrees within 1 %. The values I remembered for OPAC (0.06, 0.15, 0.4) belong to a different profile definition; libRadtran's files
  say they are "similar to OPAC, not exactly the same".
* `aerosolprofile "visibility"` (the default) follows the paper's behaviour that the haze layer at the ground thins as the visibility
  grows: the concentrations of the table nodes are interpolated, and extrapolated outside, linearly in the logarithm of the ground
  extinction between the clean, average and polluted tables, then scaled so that the ground extinction is exact. The three named profiles
  are available with their own shape, scaled to the visibility.
* The aerosol phase function is now the tabulated Mie phase function of each species (inso: asymmetry 0.83 at 550 nm). The paper uses
  Henyey-Greenstein with a lower ("blurry") INSO asymmetry to keep the circumsolar region smooth; here the aureole is the sharp Mie one.
* Licensing: libRadtran is GPL and the Bremen and OPAC data have their own terms. `tools/gen_atmosphere_tables.py` regenerates
  `atmosphere_data.cs` from a libRadtran tree; check the terms before the tables leave this repository.

## Parameters of the medium

`planetradius` (m, 6360000), `atmosphereheight` (m, 100000), `scale` (metres per scene unit, 1), `center` (scene units, `(0, -planetradius/scale, 0)`),
`visibility` (km, 50), `aerosolprofile` (`visibility`, the default, or `continental_clean`, `continental_average`, `continental_polluted`), `ozonescale`, `no2scale`, `o4scale` (1; 0 removes the gas).

The `sun` light takes `from`, `to`, `L`, `scale`, `illuminance` like `distant`, plus `angle` (full angular diameter in degrees, 0.5334) and `limbdarkening` (true). It is an infinite light, so camera rays see the disk and the integrators combine light and phase sampling.

## Majorants

The atmosphere is cut into 84 concentric shells (100 m up to 2 km, 500 m up to 10 km, 1 km up to 30 km, 2.5 km above). A ray is cut
at the shells in double precision and each piece has the sum of the maximum coefficients of the species in its shell (the air decreases with altitude; the ozone and aerosol profiles are
interpolated between table nodes, so the maximum is at an end of the shell or at a node inside it), times 1.0001. A line that crosses the planet gets the
way in and the way out and no segment inside the planet. The majorant is checked against sigma_t at 5 points per segment of
200 random rays (`MajorantBoundsAndCoversTheRay`).

## The phase function

The scattering at a point is Rayleigh from the air and aerosol scattering. Their shares depend on the wavelength (the air scatters
about lambda^-4). A pbrt phase function returns one number, so mixing at the hero wavelength was wrong for the other three
wavelengths of the sample: measured on single scattering it was up to +21 % at 450 nm near the sun and 4 to 8 % elsewhere
(measured on an earlier iteration of this code with a quadrature test that has since been replaced by `SpectralPhaseIsUnbiased`). `AtmospherePhase` samples the direction
with the shares of the hero wavelength and `PhaseBxDF` asks the phase function for the value of every wavelength
(`PhaseFunction.WavelengthDependent()` and `pSpectral()`, false and the scalar value for every other phase function, so nothing else
changes). The weight `f / pdf` of every wavelength then integrates to one and has the first moment of that wavelength's exact mixture
(`SpectralPhaseIsUnbiased`).

Integrators that call `phase.p()` directly instead of the BSDF of the phase function would use the hero value; the integrators of this
tree get the scattering of a medium vertex from `MediumInteraction.GetBSDF()`, which wraps the phase function in a `PhaseBxDF`. The polarised path takes the Rayleigh share of each wavelength for the Mueller
matrix (`PolarisedPathIntegrator.PhaseAttenuation`), which is not tested.

Directions: the hero wavelength picks Rayleigh with its own share (the isotropic part with the depolarised fraction e = 3 gamma / (1 + 2 gamma),
else the pure Rayleigh lobe) or a species with its scattering weight, then the table node (the lower or upper wavelength node of the table in
proportion to the distance), then the angle by inverting the cumulative of the piecewise linear phase table. The density returned is exactly that of `p()`.

Polarisation: `Attenuation.Spherical` builds the Mueller matrix of the spherical aerosols from the table (P12/P11, P33/P11, P34/P11 combined with
the Rayleigh one by the scattering weights). The Stokes sign convention of the engine is opposite to the solver's for S12, which is flipped; the
S34 sign was not validated against the engine (circular polarisation is negligible in the sky). The polarised atmosphere path is not validated end to end.

## Tests (`Atmosphere.*`, 14)

* `StandardAtmosphereDensity`, `RayleighCoefficient`, `VerticalRayleighOpticalDepth`
* `OzoneColumnAndCoefficients`: the column is 344 DU; the extinction and scattering of the medium equal those of an independent table interpolation
  (within 0.8 %, the spectra are sampled every nanometre); the ground extinction at 550 nm is 3.912 / visibility
* `VisibilityBlendsProfiles`: at 131.8, 59.4 and 27.6 km the ratio of the aerosol extinction at 3 km to the ground one equals that of the clean,
  average and polluted tables (within 1 %), and the ground extinction is 3.912 / visibility
* `MajorantBoundsAndCoversTheRay`
* `MieRayleighLimit`, `MieAngularIntegralsMatchEfficiencies`: the solver against the Rayleigh limit and its own integrals of the matrix
* `MieSpeciesMatchTables`: the engine's polydisperse species against miepython tables: extinction 4e-6, albedo and asymmetry 5e-6
* `MieTablesAndSampling`: the mean cosine of the table, of its series and of 10^5 samples agree
* `SunDiskIrradianceAndSampling`: irradiance of the sampled disk equals the distant light (within 2e-3 and the statistical error), the density,
  `Le` and the sample agree, nothing outside the disk, the edge is darker than the centre (more in blue than in red)
* `PlanetIntersection`
* `SpectralPhaseIsUnbiased`
* `SingleScatteringMatchesQuadrature`: a delta tracking estimator with ratio tracking next event estimation through the medium
  (`SampleT_maj`) against a quadrature of the single scattering integral built from the independent table interpolation, for three
  camera rays at 550 nm. With 200 000 samples the differences were -1.2 % (3.1 standard errors), +0.2 % and +0.1 %. The test asserts
  4 standard errors plus 0.4 %.

## Not checked

* Agreement with the Prague sky model or any measured sky. The data are from libRadtran, but the aerosol Mie properties are computed here
  and the phase functions are Henyey-Greenstein, so differences of several percent in colour are expected.
* Multiple scattering has no independent reference here; the unidirectional and bidirectional estimators can be compared with each other.
* Twilight (the sun below the horizon) is rendered but converges slowly; the reference simulation of the paper needed tens of
  thousands of samples per pixel there.
* The polarised path with the atmosphere (aerosol Mueller matrices, S34 sign).
* Solar limb-darkening coefficients are approximate; H2O/O2 band absorption is missing, which matters in the red and near infrared.
