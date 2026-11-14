# Surface-gradient bump mapping

Port of the fork's `surfgrad.h`, `bumpsurfgrad.h` and `surfgradmaterial.h` (Mikkelsen, "Surface Gradient-Based Bump
Mapping Framework", JCGT 9(3), 2020) to `surfgrad.cs`, `bumpsurfgrad.cs`, `surfgradmaterial.cs`.

A material opts in with `"string normalmapframework" "surfgrad"`. Its normal map, an optional second normal map and its
displacement texture then each become a surface gradient (a vector in the tangent plane); the gradients are scaled and
summed, and resolved once: `n' = normalize(n - gradient)`. Without the parameter nothing changes.

## Parameters (on any material)

| Parameter | Default | Meaning |
|---|---|---|
| `string normalmapframework` | `stock` | `surfgrad` turns the framework on |
| `float normalmapscale` | 1 | strength of `normalmap` (negative inverts) |
| `string normalmap2`, `float normalmap2scale` | none, 1 | second tangent-space normal map |
| `string normalmapblend` | `pd` | how the second map combines with the first: `pd`, `whiteout`, `udn`, `rnm` |
| `float displacementscale` | 1 | strength of `displacement` |
| `string displacementmode` | `bump` | `bump`: heights in world units (Eq. 7, what BumpMap() does); anything else: TBN style (Eq. 17) |
| `string coat.normalmap`, `float coat.normalmapscale` | none, 1 | normal map of the coat |
| `string coat.normalmode` | `legacy` | `inherit`: coat gradient = base + coat; `independent`: coat layers only. The normal goes to `SurfaceInteraction.coatN` / `MaterialEvalContext.coatN` for OpenPBR |

Each of `normalmap`, `normalmap2`, `displacement`, `coat.normalmap` takes its own texture coordinates:
`<name>uvchannel` (an integer, or a string for a named channel of the mesh), then `float <name>uvscale` and
`float <name>rotate` (degrees). Up to 4 distinct sets per material.

## Differences from the C++

- **Sets of texture coordinates** are real channels of the mesh (uvchannels.cs) followed by the affine map; the C++ only
  has the affine map of the first set (it has one uv per vertex).
- **Height derivatives** come from the texture derivative interface (closed form, OSL's, or the finite difference) instead
  of a forward difference; volume textures use `FloatTexture.Gradient()`, which now takes the closed form when the
  texture has one.
- **Scale-dependent heights** differentiate along the same (geometric) tangents the gradient is built from; the C++
  shifts the point along the shading tangents.
- **No registry**: the material holds its `SurfgradParams`, and its `BumpStack` is built once when the material is made
  instead of at every intersection.
- `ShortestArcRotate` is not ported (nothing calls it). Decal, triplanar, object-space normal map and volume layers are
  ported and reachable through `BumpStack`, but as in the C++ no scene parameter creates them yet.
- `NormalBumpEvalContext` carries the intersection, so a `displacement` texture mapped on another channel of texture
  coordinates now works in stock `BumpMap()` too (it silently used the first channel).

## Left-handed texture coordinates

pbrt's `NormalMap()` takes `n x dpdu` as the second tangent whichever way v runs. The framework's second tangent follows
v (Eq. 8). Where the coordinates are right-handed relative to the shading normal the two agree; where they are not
(a mirrored unwrap, `ReverseOrientation`), the framework gives the normal of the mirrored map. This is the paper's
behaviour and the fork's.

## Verification (`Surfgrad.*`)

- Tangent normal map against `NormalMap()`, 2000 random frames (non-unit, non-orthogonal tangents): 2.0e-7; left-handed
  frames against `NormalMap()` of the mirrored map: 2.1e-7. The rebuilt (dpdu, dpdv) give the resolved normal and keep
  their lengths.
- Scale-dependent height against `BumpMap()` on a flat surface: 2.5e-7. On a curved surface `BumpMap()` adds the
  `H dn/du` terms, which the framework leaves out.
- A function of the point as a height on the surface and as a volume texture: gradients equal within 2.6e-6; the
  gradient is in the tangent plane; a scaled layer scales its gradient exactly.
- Perturbed normal to a gradient and back: 2.0e-7. PD, whiteout, UDN and RNM blends against their formulas: 2.2e-7.
- Second set as an affine function of the first against a mesh channel holding the same coordinates, by number and by
  name: 1.5e-6 of the gradient.
- Renders (path, 64 spp, independent sampler, seed 1): normal-mapped and bump-mapped spheres, stock against surfgrad:
  image means 0.3152 / 0.3152 and 0.3178 / 0.3178.
