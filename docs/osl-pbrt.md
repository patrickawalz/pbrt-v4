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

Volume closures (VDFs) are ignored.

## Textures

`Texture "name" "spectrum" "osl"` (albedo and unbounded variants are both created, so it can be used for a reflectance and for an unbounded value) and `"float"` run the shader at the texture evaluation context and read the output. The color of the output is converted as described above; the float version of a color output is its average. `Texture` space is the CTM at the directive.

## Limits

* A shape whose OSL material mentions an emission closure becomes a two sided `DiffuseAreaLight` whose radiance is the emission closure of the shader evaluated at the point (position, normal and uv are known there, the tangents are made up). It is sampled by the light sampler and used with MIS like any area light; its power and bounds come from the shader evaluated at 64 points of the shape. Emission that is not found this way (animated shapes) is added at camera/BSDF-sampled hits without MIS.
* `subsurface` is a diffuse approximation; VDFs are ignored.
* The ray types and the emission without area light are wired in `PathIntegrator`, `VolPathIntegrator` and SPPM; other integrators (BDPT, random walk) were not checked.
* Animated shapes are not registered in the object registry (`object:name` is empty for them, `"object"` space is identity).
* Point cloud functions return no points.
* A texture bound to a parameter is evaluated once per shading point, not per closure.
* Shader execution is per shading point with differentials from pbrt; `Dx`/`Dy` of values that are not functions of position/uv are zero.

## Tests and renders

`dotnet src/pbrt/cmd/bin/Release/net10.0/pbrt_test.dll --gtest_filter='OslPbrt*'`

* Parameter conversion, scene parsing, spectral modes.
* `ShaderGlobals` against hand-computed values with a probe shader (render space translation/rotation, object/shader/named/camera spaces, attributes, ray types, back faces, handedness, `trace`).
* BxDFs: chi-square sampling test (pbrt's `TestBSDF`) for each lobe type, f/pdf equal to `Sample_f`, energy <= 1, reciprocity, equality with pbrt's `DielectricBxDF`, white furnace for the diffuse lobes.
* End-to-end renders through `pbrt.dll`: matte against `diffuse`, MaterialX conductor against `conductor`, MaterialX rough and smooth glass and the classic glass against `dielectric`, a mirror against a smooth conductor, procedural textures (checkerboard, marble) on a sphere and plane, the OSL checkerboard against pbrt's, a layered and a mixed closure, an emitting sphere against an area light, and the spectral modes. Images with the mode, spp and seed burned in are written to `/tmp/osl_renders/<name>_<spp>spp_seed<seed>.png`; each variant is a separate image with the same camera, lights and positions.
