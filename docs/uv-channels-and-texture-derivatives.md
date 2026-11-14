# UV channels and texture derivatives

## N texture coordinate sets (`src/pbrt/uvchannels.cs`)

- Mesh syntax (`trianglemesh`, `bilinearmesh`): `"point2 uv1"`, `"point2 uv2"`, ... by number, and `"point2 uv:name"` by
  name (a named channel takes the next free number). Both can be mixed. No limit on the count.
- Texture syntax (the `uv` mapping): `"integer uvchannel" 2` or `"string uvchannel" "name"`.
- Channel 0 is pbrt's `uv`, unchanged. The others cost nothing at a hit: the interaction keeps the primitive and where
  on it, and `SurfaceInteraction.UVChannel(channel, nameId, dpdu, dpdv)` interpolates a channel and computes its
  tangents on demand, from the texture coordinates alone (so it is right under instancing).
- A mesh without the channel, and any shape that is not a mesh, gives channel 0.
- Not yet: PLY files, and the meshes made by the quad, n-gon, polygon, subdivision and dicing code.

## Texture derivatives (`base/texture.cs`, `texture_derivatives.cs`)

```csharp
Float           FloatTexture.Evaluate(ctx, TextureDirections directions, Float* dfdu, Float* dfdv);
void            FloatTexture.EvaluateDerivatives(ctx, directions, Float* dfdu, Float* dfdv);
SampledSpectrum SpectrumTexture.Evaluate(ctx, lambda, directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv);
void            SpectrumTexture.EvaluateDerivatives(ctx, lambda, directions, SampledSpectrum* dfdu, SampledSpectrum* dfdv);
```

- A null pointer is a derivative that is not computed.
- `TextureDirections(dpdu, dpdv)`: the two tangent directions; the derivative "by u" is the change along `dpdu`.
- A texture overrides `ValueAndDerivatives()` (and `Derivatives()` where the slope is cheaper without the value). One
  that does not gets the finite difference of pbrt's `BumpMap()`, computed on the fly.
- A texture made of others asks each child and applies its own rule: closed forms and finite differences mix.
- `Option "texturederivatives" "finite"` forces finite differences everywhere (stock pbrt); `"bool
  analyticderivatives" false` on a texture forces them for that texture.
- `BumpMap()` now asks the displacement texture for its value and both derivatives.

Closed form: constant, bilerp, checkerboard (2D and 3D, with its filter), dots, fbm, wrinkled, windy, mix, direction
mix, scale, add, subtract, abs, clamp, divide, power, remap, rounding, modulo, compare, ramp; spectrum constant,
bilerp, checkerboard, dots, mix, direction mix, scale.

On the fallback: Ptex, OSL, cloud, triplanar, shell map, SDF, label (float); Ptex and OSL (spectrum).

## RGB textures (`texture_rgb.cs`)

- `RGBTexture`: a texture that returns a colour, with the same derivative interface (`RGB*` pointers) and fallback.
- `SpectrumRGBTexture`: the one adapter from a colour to a spectrum (albedo, unbounded or illuminant upsampling). It
  carries the colour's derivatives through the upsampling in closed form (`RGBUpsampling.Derivative`: the sigmoid of a
  polynomial whose coefficients are a trilinear interpolation of a table; computed in double).
- On the adapter: marble, spectrum ramp, spectrum image, spectrum grid, variation and strand colour. Their class
  names, constructors and scene syntax are unchanged.
  - Exact in RGB: marble, ramp, image (point, bilinear and trilinear filters), grid of voxels.
  - Finite difference in RGB, exact upsampling: variation, strand colour, image with the EWA filter, grid of nodes.
- An image of one channel has no colour space: the adapter returns the channel at every wavelength.
- Not on the adapter: Ptex and OSL spectrum textures (their conversions are not the three upsamplings).
- Not yet: `Texture "name" "rgb" ...` in scene files; RGB textures as inputs to OSL colour parameters and normal maps.

Also closed form now: float image (`MIPMap.FilterSlope`: the slope of the filtered lookup with the pyramid level
held; EWA and multi-channel float lookups fall back), float grid of voxels, blackbody (Planck's law by temperature).

## Verification

- `UVChannels.*`: tangents of a channel against a step over the primitive (1e-4 on triangles), by name and number,
  through an instance; the mapping's coordinates, differentials and finite-difference step.
- `TextureDerivatives.*`: every closed form against central differences, 6000 comparisons each, on the uv, planar,
  spherical and cylindrical mappings; mixed closed-form / finite-difference composites; forced finite differences
  equal the stock formula exactly.
- Render: a checkerboard on `uv1 = (3u + 0.25, 2v + 0.5)` against the first set with the same scale and offset:
  99.2% of pixels identical, largest difference 0.004.
- `TextureDerivatives.Upsampling`: the upsampling's slope integrated along 400 lines of colours against the difference
  of the spectra at the ends, within 0.93% for the three spectrum types. `TextureDerivatives.FromRGB`: marble and ramp
  colours against central differences in RGB (within 0.5%), and the ramp's spectrum by the line integral (within 0.26%).
- `TextureDerivatives.ImagesGridsBlackbody`: float and RGB images with the point, bilinear and trilinear filters,
  inverted and not (within 2.2% of the slope beyond the reference's own error), the spectrum of an image by the line
  integral (0.07%), grids (1.6%, spectrum 0.16%), blackbody physical and normalised (2%).

## Bounds over a region

`RegionBounds(region, out lo, out hi)` (float) and `RegionMax(region)` / `MinValue()` (spectrum) give bounds of a
texture over a box of render space, computed from the texture's formula, for the majorant grids of the volume code.

- Mapped textures (bilerp, checkerboard, dots, direction mix) bound through the mapping of the box's corners.
- Noise textures (FBm, wrinkled, windy): per octave, the box scaled to that octave's frequency is clipped to the
  lattice cells it overlaps and the noise is bounded in each by interval arithmetic on the cell's gradients
  (`NoiseBounds`); `TurbulenceBounds` sums the octaves' magnitudes.
- Composites (mix, scale, add, subtract, abs, clamp, divide, power, remap, rounding, modulo, compare, ramp) combine
  the bounds of their inputs by interval arithmetic.
- Images, Ptex and OSL keep the global bound.

`TextureBounds.Regions`: 300 random boxes at three sizes for each texture, sampled densely: no value outside a bound.
The noise bounds are valid but loose: the values fill 14-46% of the wrinkled interval, 8-10% of windy, 7-63% of dots.

Found on the way, stock behaviour, unchanged: the global `MinValue()/MaxValue()` of the float bilerp texture are the
extremes of its four corners, which holds only while (s, t) stay in the unit square. A mapping that leaves it
extrapolates, and the regional bound (which follows the mapping) exceeded the global one by up to 428% in the test.

## OSL textures: OSL's own derivatives

An OSL texture is differentiated by OSL. The shader is compiled a second time with the output registered as a renderer
output with derivatives (`SymLocationDesc(..., derivs: true, SymArena.Outputs)`), which makes the optimizer carry
Dual2 values on everything the output depends on, through layers and connections. The two differentials OSL carries
(the "x" and "y" of `Dx()`/`Dy()`) are seeded with the two surface directions instead of the two pixels: `dPdx`, `dudx`,
`dvdx` are `directions.dpdu`, `duvdu` times the size of the pixel, likewise y. The value and both derivatives come back
from one execution in the output arena.

- The second group is built when a derivative is first asked for: a texture that is only evaluated keeps the shader
  without derivatives.
- The seeds keep the size of the pixel (pbrt's bump-mapping step) because a shader filters its `texture()` lookups with
  the same differentials; the output is divided by that step.
- Spectrum textures: the colour's derivatives through `OSLSystem.ToSpectrumDerivative` / `ToEmissionSpectrumDerivative`
  (the gray, unbounded and upsampled modes).
- Fallback to finite differences: an output that is not one float or three (an int, a struct), and a group given as one
  `groupspec` string (its output's type is not known before the group is optimized).
- `Dx()`/`Dy()` inside such a shader mean the two surface directions during a derivative evaluation, not the pixel.
- `FillGlobals` takes P and (u, v) from the texture context when it belongs to the intersection, so a shifted context
  moves the lookup. Bump mapping was not affected (its context carries no intersection); a context built from a
  `SurfaceInteraction` was.
- `OSLSystem.Query(shader)` reads the parameters of shaders compiled from source in memory. `OSLQuery.open` only finds
  `.oso` files, so the preset colours of `osl_volume.cs` were silently skipped for `.osl` sources; they use it now.

`TextureDerivatives.OSL`: against central differences, 6000 comparisons each: a float of u, v and P (1.4e-4 of the
slope, with and without a pixel footprint), noise (4.9e-4), a colour read as a float (2.2e-4), two connected layers
(9.9e-4); the spectrum by the line integral (albedo and unbounded 4.1e-4, illuminant 5.3e-5). Render (OSL bump on a
sphere, path, 64 spp): mean absolute difference to finite differences 0.011 at image means 0.3211 / 0.3212;
18.4 s with finite differences, 15.3 s with OSL's.

The OSL tests need `stdosl.h`: set `OSL_HOME` to a checkout of OpenShadingLanguage (all 62 `OslPbrt` tests pass with it).
