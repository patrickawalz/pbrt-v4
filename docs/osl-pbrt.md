# OSL in pbrt (Magi)

pbrt can use Open Shading Language shaders (the C# port in `src/osl`) as materials and as textures. The dependency goes one way: `pbrt_lib` references `osl_lib`, never the reverse. The glue is in `src/pbrt/osl_*.cs`:

| File | Content |
|---|---|
| `osl_system.cs` | the shading system (one per process), shader lookup / on-the-fly compilation, image reading for the texture system, RGB to spectrum |
| `osl_scene.cs` | render space, camera, named coordinate systems, object registry, the aggregate for `trace()` |
| `osl_renderservices.cs` | `OSLRendererServices`: matrices, attributes, userdata, `trace`, point clouds (stubs) |
| `osl_shading.cs` | `ShaderGlobals` from a `SurfaceInteraction`, execution, closure tree to BxDF |
| `osl_closures.cs` | registration of all closures with the shading system |
| `osl_bxdfs.cs` | the BxDFs pbrt does not have (Oren-Nayar, Burley, Phong, Ward, sheen, Beckmann/GGX microfacet with OSL conventions, specular lobes) and the composite `OSLClosureBxDF` |
| `osl_material.cs` | scene parameters, `OSLMaterial`, `OSLFloatTexture`, `OSLSpectrumTexture` |
| `osl_test.cs` | tests (`--gtest_filter='OslPbrt*'`) |

## Scene file

```
Option "oslsearchpath" "/path/to/shaders:/other"     # also env OSL_SHADER_PATH; the directory of the scene file is always searched
Option "oslspectral"   "upsample"                      # "upsample" (default) | "unbounded" | "gray"
Option "oslemission"   "illuminant"                    # "illuminant" (default) | "unbounded"

Material "osl" "string shader" "matte" "rgb Cs" [0.8 0.3 0.2] "float Kd" 0.9
Texture "t" "spectrum" "osl" "string shader" "marble" "float scale" 3
Texture "f" "float"    "osl" "string shader" "fnoise"
```

`shader` is the name of a shader: `name.oso` is looked for first (scene directory, `searchpath` parameter, `oslsearchpath`), then `name.osl`, which is compiled on first use with the compiler of `osl_lib` (cached for the process, `stdosl.h` found next to the sources of the shaders or in the search path).

Parameters of an `osl` material or texture:

| Parameter | Meaning |
|---|---|
| `string shader` | one shader (one layer) |
| `string layers` | `["layer:shader", ...]` several layers of a group, in execution order |
| `string connect` | `["srclayer.param dstlayer.param", ...]` connections |
| `string groupspec` | a raw OSL group description (the syntax of `testshade --groupspec`); overrides the above |
| `string searchpath` | extra directories for this shader |
| `string output` | texture only: the output of the last layer that is the value (default `Cout` / `Fout`, then the first output of a suitable type) |
| `string layername` | name of the layer of a single shader (default: the shader name) |
| `float`, `integer`, `bool`, `string`, `rgb`, `point3`, `vector3`, `normal`, `point2`, `vector2` | shader parameters; `layer.param` addresses a layer, an unqualified name goes to the last layer. `bool` is an `int`. `rgb` is an OSL `color`. |
| `texture <param>` `"name"` | connects a named texture to the parameter (below) |

`spectrum` parameters are not converted to OSL (they are ignored with a warning). `type` (of `MakeNamedMaterial`), `displacement` and the hidden `__shaderspace` are not shader parameters.

### Texture connections

* A named OSL texture (`Texture ... "osl"`) is merged into the group: its layers are put in front of the material layers (names made unique) and its output is connected to the parameter. The shaders see each other, so noise, coordinate transformations and so on are evaluated once per shading point.
* Any other pbrt texture (image, checkerboard, ...) is bound as an interpolated parameter: pbrt evaluates it at the shading point and the shader reads it through `get_userdata`. A spectrum texture becomes a `color` (a noisy estimate, see below), a float texture a `float` or a gray `color`.

### Spaces

OSL's `"common"` space is pbrt's world space. pbrt renders in render space (camera-world by default); the glue converts positions and directions with `OSLScene.WorldFromRender`. Matrices follow the OSL (row vector) convention, so they are the transpose of pbrt's.

* `"object"`: the object-to-world transform of the shape; `"shader"`: the CTM at the `Material` directive (for textures the CTM at the `Texture` directive).
* `"camera"`, `"screen"`, `"NDC"`, `"raster"` come from the camera of the scene; any `CoordinateSystem "name"` can be used by name, including its motion.
* Attributes: `object:name` (the `"string name"` parameter of the shape, else the shape type), `object:surfacearea`, `camera:resolution`, `camera:projection`, `camera:fov`, `camera:pixelaspect`, `camera:clip`, `camera:shutter`, `shutter`, `raytype`.
* Userdata: bound textures, `uv`, `s`, `t`, `dPdu`, `dPdv`, `Ng`, `Ns`, `faceIndex`, `objectId`.
* `trace()` casts rays against the scene aggregate; `getmessage("trace", ...)` has `hit`, `hitdist`, `geom:name`, `P`, `N`, `Ng`, `u`, `v`.
* `ShaderGlobals`: `P`, `dPdx`, `dPdy` (pbrt's ray differentials), `I`, `N` and `Ng` (flipped towards the viewer on a back face, with `backfacing` set), `u`, `v`, `dudx`..`dvdy`, `dPdu`, `dPdv`, `time`, `dtime`, `flipHandedness` (the object transform swaps handedness), `raytype`.
* Ray types: camera rays are `camera`; a ray that continues from a surface carries `diffuse`/`glossy`/`reflection`/`refraction` from the lobe that was sampled (set by the path and volumetric path integrators).

## RGB to spectrum

pbrt is spectral and OSL shaders compute RGB. The conversion at the shader boundary is:

| Value | `upsample` (default) | `unbounded` | `gray` |
|---|---|---|---|
| closure weights, reflectances (all channels <= 1) | `RGBAlbedoSpectrum` | `RGBUnboundedSpectrum` | luminance, constant spectrum |
| closure weights above 1 | `RGBUnboundedSpectrum` | `RGBUnboundedSpectrum` | luminance |
| emission | `RGBIlluminantSpectrum` (the color space illuminant times the RGB spectrum) | `RGBUnboundedSpectrum` | luminance |

Emission is normalized as the lights of pbrt are (divided by the photometric value of the illuminant), so `emission()` with color `(4, 2.4, 0.8)` renders like `AreaLightSource "diffuse" "rgb L" [4 2.4 0.8]`. `Option "oslemission" "unbounded"` uses the unbounded spectrum instead of the illuminant (the white is then flat, not D65). The `gray` switch gives a monochrome render of the shader (useful to compare with other renderers without metamerism).

A spectrum texture that a shader reads as `color` (a pbrt texture bound to a parameter) is evaluated at the sampled wavelengths and projected to RGB through the XYZ matching functions; this is noisy per sample (each path sees different wavelengths), converging to the correct color.

## Closures

Closures are turned into BxDFs the way `testrender` does: the closure tree is walked, each primitive closure becomes a lobe with a spectral weight, and a lobe is chosen with a probability proportional to the average of its weight. `OSLClosureBxDF` sums `f` and `pdf` over the non-specular lobes; a chosen specular lobe returns `f = weight * f_lobe`, `pdf = probability * pdf_lobe`. A lobe has its own normal/tangent (the `N`/`U` of the closure) as a frame relative to the shading frame, and is mirrored when the direction is below that normal.

| Closure | BxDF |
|---|---|
| `diffuse` | `DiffuseBxDF` |
| `oren_nayar`, `burley` (also `diffuse_bsdf`/`oren_nayar_diffuse_bsdf`/`burley_diffuse_bsdf`) | `OSLOrenNayarBxDF`, `OSLBurleyDiffuseBxDF` |
| `translucent` | `DiffuseTransmissionBxDF` |
| `phong`, `ward` | `OSLPhongBxDF`, `OSLWardBxDF` (anisotropic) |
| `microfacet` (ggx, beckmann; reflection and/or refraction; `eta`) | `OSLMicrofacetBxDF`: pbrt's dielectric formulation with OSL distributions. A very smooth one becomes a specular lobe. |
| `reflection`, `refraction`, `transparent` | `OSLSpecularReflectionBxDF` (Fresnel with `eta`; `eta` 0 is a mirror), `OSLSpecularRefractionBxDF`, `OSLTransparentBxDF` |
| `emission`, `background`, `uniform_edf` | emission (see limits); `background` is ignored on surfaces |
| `holdout` | the surface absorbs and is marked in `OSLShadeResult.holdout` |
| `subsurface` | **approximation**: a diffuse lobe of the color (no BSSRDF) |
| `sheen` | `OSLSheenBxDF` |
| MaterialX: `oren_nayar_diffuse_bsdf`, `burley_diffuse_bsdf`, `translucent_bsdf`, `dielectric_bsdf`, `conductor_bsdf`, `generalized_schlick_bsdf`, `sheen_bsdf`, `layer`, `mix`-style weights | the lobes above. MaterialX roughness is `alpha = roughness^2`; its `eta` is inverted on back faces; conductors use pbrt's `ConductorBxDF`/Fresnel of complex IOR; `layer(top, base)` scales the base by `1 - albedo(top)`, where the albedo of a dielectric top is its Fresnel reflection at the viewing angle |

Conventions for the classic closures (as in `testrender`): the shader passes `eta` as is and the shader library flips it on back faces with `backfacing()`; `refraction` includes `1 - F` and `reflection` includes `F`. In radiance mode a transmission is scaled by `1/eta^2` like pbrt's dielectric.

Volume closures are used by the `osl` medium (see Volumes below); on a surface they are ignored.

## Volumes

`MakeNamedMedium "name" "string type" "osl" "string shader" "cloud"` makes a medium out of an OSL shader group of usage `volume`. The shader runs at each point that a volumetric integrator asks about; the closure it returns is turned into pbrt's medium properties (`sigma_a`, `sigma_s`, `Le`, phase function). The medium is used like any other: `MediumInterface` on shapes, the camera medium, nesting (it takes part in pbrt's medium tracking and priority handling like `homogeneous` or `uniformgrid`), and in `CompositeMedium`-style overlaps.

### Parameters

| Parameter | |
|---|---|
| `shader` | the shader (`.osl` is compiled with the port's compiler, or `.oso`) |
| `scale` | multiplies sigma_a and sigma_s (default 1) |
| `Lescale` | multiplies the emission (default 1) |
| `preset` | a named scattering preset of pbrt (`GetMediumScatteringProperties`): passed as `sigma_a` and `sigma_s` colors **only if the last layer declares parameters of these names** |
| `p0`, `p1` | bounds in medium space (default 0..1 when the shader is heterogeneous; a constant shader is unbounded unless bounds are given) |
| `majorant` (alias `sigma_t_max`) | an upper bound of the extinction (after `scale`) that replaces the grid (see below) |
| `majorantres` | the resolution of the majorant grid: one integer or three (default 16) |
| `majorantsamples` | the number of jittered shader samples per cell and axis (default 3) |
| `majorantmargin` | the safety factor of the sampled bound (default 1.25) |
| `checkmajorant` | `false` turns the violation counter off (default `true`) |
| `detecthomogeneous` | `false` turns the constant-shader detection off (default `true`) |
| `rayleigh` | Rayleigh fraction of the phase function, as in pbrt's media |
| everything else | shader parameters (as for OSL materials: `float`, `int`, `string`, `color` as `rgb`, `point3`, ...) |

The transform is the CTM at `MakeNamedMedium`: it is the medium space and the `"shader"` space of the group (so `transform("shader", P)` gives the coordinates that the bounds and the medium space use). `tracker` and `transmittance` are the integrator choices of pbrt's volumetric integrators, as for other media.

### Shader globals

`P` is the point in world space (as for surface shaders, render space is converted), `I` is the normalized direction of the ray (the direction in which light travels to the camera is `-I`), `time` is the time of the ray. `N` and `Ng` are `-I`, and there are no `u`, `v` or `dPdu`: a volume has no surface. `raytype` is `camera`. The ray direction comes from `SampleRay`, which the integrators call before they query points; a point query does not know a direction by itself.

### Closures

The weight of a closure is a coefficient per unit length of the scene, as in `testrender`: the shader multiplies the closure by what it computes. `pbrt_volume.h` (in `scenes/osl_volume/`) declares the three closures that `stdosl.h` lacks; it must be included by shaders that use them.

| Closure | Result |
|---|---|
| `henyey_greenstein(g) * sigma_s` | scattering with the HG function of anisotropy `g` |
| `isotropic() * sigma_s` | the same with `g = 0` |
| `absorption() * sigma_a` | absorption |
| `emission() * Le`, `uniform_edf` | emission. The weight is a radiance: the medium emits `sigma_a * Le`, as pbrt's media do (so an emitter needs absorption) |
| `anisotropic_vdf(albedo, extinction, g)` | `sigma_t = extinction`, `sigma_s = albedo * sigma_t`, `sigma_a` the rest, HG(`g`) |
| `medium_vdf(albedo, transmission_depth, transmission_color, g, ior, priority)` | `sigma_t = -ln(transmission_color) / transmission_depth` (testrender), `sigma_s = albedo * sigma_t`; black albedo and color is a vacuum. `ior` and `priority` are **not used**: the priority is that of pbrt's `MediumInterface` |

A weighted or summed closure scales and adds the coefficients. Several scattering closures give a mixture of their phase functions weighted by the channel average of the scattering coefficient (approximate when the lobes differ in color). The mixture is allocated per point; a single lobe uses a cached `HGPhaseFunction`. Surface closures and `transparent()` are ignored.

### Spectra

Coefficients are `RGBUnboundedSpectrum` in the `upsample` and `unbounded` modes (a coefficient is not an albedo); in `gray` mode it is the luminance. Emission is an `RGBIlluminantSpectrum` of the color space (the same normalization as pbrt's `Le` of media). `Option "oslspectral"` selects the mode as for surfaces. A chromatic medium has one grey majorant (the largest channel of the coefficient, `RGBUnboundedSpectrum::MaxValue()` as `RGBGridMedium` does) and per-wavelength `sigma_a`, `sigma_s`; the null collision coefficient of each wavelength is the difference, and the spectral MIS of volpath handles the rest.

### The majorant

Delta and ratio tracking need an upper bound of the extinction. A shader is a black box, so:

1. **Homogeneous.** The group is analysed after optimization. If it reads no varying global (`P`, `I`, `N`, `u`, `v`, `time`, ...) and no userdata, the closure is a constant: the medium is evaluated once, uses `DenselySampledSpectrum` and a `HomogeneousMajorantIterator`, and renders **identically** to pbrt's `homogeneous` medium (the test renders match to the last bit for the same seed). No shader runs while rendering. A constant shader with `p0`/`p1` fills the box.
2. **Majorant grid.** Otherwise a grid over the bounds (`majorantres`^3, 16 by default) is filled the first time the medium is used, from `majorantsamples`^3 jittered evaluations per cell and the values at the corners of the cell, times `majorantmargin`. Tracking uses pbrt's `DDAMajorantIterator`.
3. **Explicit bound.** `majorant` (or `sigma_t_max`) is a bound that you supply (extinction after `scale`); it is used as one cell and is trusted.

**The sampled bound is empirical.** A feature narrower than the sample spacing, or a shader that jumps (`cellnoise`, a step) can exceed it, and then null collisions have negative coefficients that are clamped to zero: the medium is a little too transparent where it is denser than its majorant. Every shader evaluation during a render is compared with the majorant of its cell; the number of violations is counted (`OSLMedium.Violations()`, the statistic `Media/OSL majorant violations`, and a warning with advice at the first). When it fires, raise `majorantmargin`, `majorantres` or `majorantsamples`, or give `majorant`. Shaders that depend on `time` (or other globals that the sampling does not vary) need an explicit `majorant`. The tests check that the estimators are unbiased with the grid and with an exact override, and that a too small override is reported.

### Threads and cost

Each thread has its own shading context (thread-static); a point query allocates nothing for single-lobe shaders (the phase function of one Henyey-Greenstein lobe is cached by `g`); a mixture of lobes allocates one `MixturePhaseFunction` per point. Single-thread cost of one `SamplePoint` on this machine: see Tests.

### Integrators

| Integrator | Support |
|---|---|
| `volpath` | full: delta tracking with spectral MIS, null collisions, emission, light sampling (NEE) and phase sampling with MIS. The tracker `ratio` for shadow rays and the analytic transmittance of homogeneous OSL media work as for other media |
| `simplevolpath` | works for scenes it supports at all: interface materials only (no surface scattering), no delta lights (area and infinite lights). It samples the phase function only |
| `bdpt` | scattering and absorption work. An emissive OSL medium is **not** sampled as a light (pbrt's `VolumeEmissionLight` handles pbrt media), so only emission found along camera subpaths counts |
| `mlt` | works (it drives the BDPT estimator); the same emission limit as `bdpt` |
| `path`, `simplepath`, `lightpath` | skip medium boundaries: no scattering or emission in any medium (as for every pbrt medium), the OSL medium is not run |
| `randomwalk`, `ao`, `sppm` | do not use media |

## Textures

`Texture "name" "spectrum" "osl"` (albedo and unbounded variants are both created, so it can be used for a reflectance and for an unbounded value) and `"float"` run the shader at the texture evaluation context and read the output. The color of the output is converted as described above; the float version of a color output is its average. `Texture` space is the CTM at the directive.

## Limits

* A shape whose OSL material mentions an emission closure becomes a two sided `DiffuseAreaLight` whose radiance is the emission closure of the shader evaluated at the point (position, normal and uv are known there, the tangents are made up). It is sampled by the light sampler and used with MIS like any area light; its power and bounds come from the shader evaluated at 64 points of the shape. Emission that is not found this way (animated shapes) is added at camera/BSDF-sampled hits without MIS.
* `subsurface` is a diffuse approximation. Volume closures (`medium_vdf`, `anisotropic_vdf`, `henyey_greenstein`, ...) in a surface material do not create a medium: attach an `osl` medium with `MediumInterface`.
* The path, volpath, simple path, random walk, BDPT, MLT, SPPM and light path integrators were run with OSL matte, conductor, glass and emitter scenes against the same scene with pbrt materials (see Tests). `simplevolpath` does not support surface scattering at all, with any material.
* Animated shapes are not registered in the object registry (`object:name` is empty for them, `"object"` space is identity).
* Point cloud functions return no points.
* A texture bound to a parameter is evaluated once per shading point, not per closure.
* Shader execution is per shading point with differentials from pbrt; `Dx`/`Dy` of values that are not functions of position/uv are zero.

## Layered and mixed closures

`layer(top, base)` is the MaterialX model: `f = f_top + (1 - E_top(wo)) * f_base`, no refraction into the layer, no interreflection (so it is not pbrt's `CoatedDiffuseBxDF`, which is darker: 0.30 against 0.52 for a 0.5 base at normal incidence). `E_top` is the Fresnel reflection of a dielectric top at the viewing angle. A sum of closures is a sum of lobes, as in OSL. Classic `microfacet` closures take `eta` as given: a shader must flip it with `backfacing()` as the glass shader of `testrender` does, otherwise the inside of an object is wrong.

### Tests and renders

`dotnet src/pbrt/cmd/bin/Release/net10.0/pbrt_test.dll --gtest_filter='OslPbrt*'` (`RenderAllIntegrators` takes about 20 minutes; `OSL_TEST_INTEGRATORS=path,bdpt` restricts it)

* Parameter conversion, scene parsing, spectral modes.
* `ShaderGlobals` against hand-computed values with a probe shader (render space translation/rotation, object/shader/named/camera spaces, attributes, ray types, back faces, handedness, `trace`).
* BxDFs: chi-square sampling test (pbrt's `TestBSDF`) for each lobe type, f/pdf equal to `Sample_f`, energy <= 1, reciprocity, equality with pbrt's `DielectricBxDF`, white furnace for the diffuse lobes.
* End-to-end renders through `pbrt.dll`: matte against `diffuse`, MaterialX conductor against `conductor`, MaterialX rough and smooth glass and the classic glass against `dielectric`, a mirror against a smooth conductor, procedural textures (checkerboard, marble) on a sphere and plane, the OSL checkerboard against pbrt's, a layered and a mixed closure, an emitting sphere against an area light, and the spectral modes. Images with the mode, spp and seed burned in are written to `/tmp/osl_renders/<name>_<spp>spp_seed<seed>.png`; each variant is a separate image with the same camera, lights and positions.

* Emission: an OSL emitter and an `AreaLightSource` sphere are rendered with light sampling only, BSDF sampling only and MIS, and against a 1024 spp reference.
* Layered/mixed: `f` of the closures against `f_top + (1 - E) f_base` and `0.5 DiffuseBxDF + 0.5 DielectricBxDF` built from pbrt's BxDFs, furnace tests, and a render of a mixed OSL closure against pbrt's `MixMaterial`.
* All integrators on matte, conductor, glass and emitter scenes, OSL against the pbrt material.

### Volume tests (`OslVolume*`, 35 tests, `osl_volume_test.cs`)

`dotnet src/pbrt/cmd/bin/Release/net10.0/pbrt_test.dll --gtest_filter='OslVolume*'` (about 12 minutes on two cores, mostly the renders)

* Coefficients of every closure, globals of a volume point (`P`, `I`, `time`, `N`, shader space), detection of homogeneous shaders (including a read of `P` that the optimizer removes).
* A constant shader against pbrt's `homogeneous`: spectra, majorant segments, phase function, closed-form transmittance, ratio tracking against `exp(-sigma_t d)` with z-scores; the same for emission.
* Henyey-Greenstein `f`/`Sample_p`/`PDF` against `HGPhaseFunction` for seven values of `g`, chi-square tests of the sampled directions (also of a mixture of three lobes).
* A Gaussian blob shader against a `GridMedium` (80^3) of the same function: 80 z-scores of transmittance on random rays (sum z^2 88.5 for 80, max |z| 2.0).
* Soundness of the majorants: random smooth shaders with a known bound, with the sampled grid and with the exact override, unbiased against the quadrature of `exp(-tau)`; the violation counter fires for a too small override and the estimate is then biased as expected; noise, snoise, pnoise, cellnoise and gabor shaders run in a volume; the cloud shader.
* Emission against the quadrature of `sigma_a Le T`; overlap of OSL and pbrt media; the spectral modes; eight threads on one medium give bit-identical values.
* Renders (`/tmp/osl_volume_renders`, `final/contact_sheet.png`): OSL against pbrt's homogeneous medium (absorbing, scattering, chromatic, emissive, camera in fog, nested, mixed OSL/pbrt, in a glass sphere): the images are **identical for the same seed**; the cloud against a 64^3 `GridMedium` of the shader within the noise floor (MAD of OSL against the reference with another seed compared with the MAD of two reference seeds); a flame; white furnaces (constant, cloud and blob with albedo 1: mean 1.000, with a control that is darker); the integrators `volpath`, `simplevolpath`, `bdpt`, `mlt`.

Cost of one point query, one thread (ns; two runs, the second with the machine loaded): `HomogeneousMedium` 132 / 142, `GridMedium` 80^3 606 / 481, OSL constant 157 / 69, OSL Gaussian blob 935 / 1530 (1.5x to 3.2x the grid), OSL fbm cloud with five noise octaves 1756 / 2312 (2.9x to 4.8x). One ratio-tracking transmittance ray (us): grid 4-5, OSL blob 7-9, OSL cloud 21-31.
