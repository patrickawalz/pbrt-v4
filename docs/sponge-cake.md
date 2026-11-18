# SpongeCake: layers of microflakes

Material `spongecake`, in `src/pbrt/spongecake.cs`. From Wang, Jin, Hašan, Yan, "SpongeCake: A Layered Microflake Surface
Appearance Model" (ACM Transactions on Graphics 42(1), 2022).

A surface is a stack of flat layers, each a homogeneous medium of microflakes: tiny two-sided mirrors whose normals follow
the SGGX distribution. Flattened ellipsoids give a surface-like layer, long thin ones give fibres. There is no interface
between the layers or above them, so light crosses the boundaries in a straight line.

```
Material "spongecake" "string layers" [ "fiber" "surface" ] "float roughness" [ 0.1 0.8 ] "float thickness" [ 1 5 ]
    "rgb albedo" [ 0.7 0.1 0.1  0.7 0.1 0.1 ]
```

| Parameter | Default | Meaning |
|---|---|---|
| `string layers` | required | `surface`, `fiber` or `hg` (a Henyey-Greenstein medium), one per layer, from the top |
| `float roughness` | 0.5 | Of the flakes of each layer; 1 is isotropic |
| `float thickness` | 1 | Optical thickness of each layer: its thickness times the density of its flakes |
| `spectrum albedo` | 0.8 | Of each layer |
| `spectrum f0` | 1 | Reflectance of the flakes at normal incidence |
| `float g` | 0 | Asymmetry of an `hg` layer |
| `vector3 orientation` | normal; x for fibres | Normal of the flakes or direction of the fibres, in the surface frame (x along dpdu, z the normal) |
| `float angle`, `float tilt` | none | Orientation in degrees. Fibres: turned by `angle` from x about the normal, lifted out of the surface by `tilt`. Flakes: the normal leant by `tilt` towards the direction at `angle` |
| `string orientationmap` | none | One image per layer (`""` for none), read as a normal map: the direction is `2 rgb - 1` |
| `string base` | `none` | `none`: the layers let light through. `diffuse`: with `spectrum reflectance` (0.5). `conductor`: with `conductor.eta` and `conductor.k` (copper) or `reflectance`, and `float conductor.roughness` (0: a mirror). `dielectric`: something that refracts, with `float eta` (1.5) and `float base.roughness` (0) |
| `bool polarisedinside` | false | With an integrator without polarisation: keep the polarisation between the flakes |
| `bool delta` | true | With the light that crosses the layers without meeting a flake |
| `string scattering` | `multiple` | `multiple`, or `single`: the paper's closed form alone |
| `integer maxdepth` | 1000000 | Events of a walk. The walks end by themselves, with roulette |
| `integer nsamples` | 1 | Walks per evaluation |

Every per-layer parameter can be given three ways: a list with one value per layer (one value: for all); a texture for all
layers (`"texture albedo" "dye"`); or, with the layer's number after the name, a value or texture for that layer
(`"texture angle1" "weave"`: the top layer). The last orientation given wins: list, then angle and tilt, then map.

## From the paper, as it is

* **Single scattering in closed form** (its equations 10 to 12 and 16): for each layer, the integral over the depth of the
  scattering of what reaches it along one direction and leaves along the other, times what the layers on the way let
  through (section 4.4). Reflection and transmission.
* **The SGGX flakes**: projected area, distribution, phase function `D(h) / (4 sigma)`, and sampling of the normals that a
  direction sees.
* **The delta transmission** and **a substrate under the layers** (section 4.5).
* **Henyey-Greenstein layers.**

## Not from the paper

* **Multiple scattering.** The paper replaces it with a second single-scattering lobe plus a Lambertian one, with parameters
  from a trained neural network. That is approximate and needs the network's weights. Here it is the random walk that the
  paper uses as its ground truth, added to the closed form of the first scattering. At every scattering after the first the
  walk is joined to the other direction; the join is exact because nothing bends the light on the way out. Unbiased.
* **A mirror under the layers.** A mirror has no value to join a walk to. The light that a flake sends down to the mirror
  and that the mirror reflects to the other direction is joined at the flake, along the one direction the mirror turns
  into it.
* **Flake reflectance.** The paper uses Schlick's approximation. Here it is the Fresnel reflectance of the conductor that
  has `f0` at normal incidence (pbrt's rule for a conductor given by a colour), so the two polarisations exist.
* **Polarisation.** A flake is a mirror with its Fresnel matrix; the matrices are composed along the walk. An `hg` layer and
  a diffuse base depolarise.
* **The density** that weights samples is the paper's (the phase function of the layer where light is first scattered, by
  the probability that it is that layer), plus a constant part for what is scattered more than once. The samples
  themselves are the walk.

## Tests (`SpongeCake.*`, all pass)

**Flakes**: for both kinds at roughness 0.3, 0.6 and 1, the projected area equals the integral of the distribution over the
visible normals (0.7480 against 0.7503), the phase function integrates to one (0.996 to 1.001), and the moments of the
sampled directions are those of the phase function (-0.1718 against -0.1716).

**Single scattering**:

* Isotropic flakes in a deep layer against Chandrasekhar's half space, `a / (4 pi (cos + cos))`: largest relative difference
  3.5e-4.
* Exchange of the two directions, in reflection and transmission: largest relative difference 2.6e-5.
* The closed form against a walk that stops at its first scattering, four cakes, two directions, each side: all agree
  within the error (0.2686 +- 0.0010 against 0.2677 up, 0.0216 against 0.0219 down).

**Multiple scattering**: the value against the samples for the same cakes: all agree (0.3296 +- 0.0012 against 0.3320).
White flakes return 0.9996 to 0.9998 of the light, and 0.9991 on a white base (the flakes reflect 0.9999).

**Bases**: the same tests with a mirror under the flakes and with a rough conductor under fibres: value against samples
0.3981 +- 0.0015 against 0.3968 and 0.6904 +- 0.0027 against 0.6929.

**Polarisation**: one scattering polarises as the flake's Fresnel reflectances at the half vector, to 1.3e-7. With every
scattering, the first matrix element weighted by the value agrees with the one weighted by the samples (0.4289 +- 0.0014
against 0.4287 +- 0.0007).

**Renders** (220 by 220, path, independent sampler, 128 samples, seed 1, two cores):

| Render | Sphere mean (r, g, b) | Time |
|---|---|---|
| Fabric (fibres over flakes), multiple | 0.1935, 0.0131, 0.0170 | 39 s |
| Fabric, single scattering | 0.1187, 0.0169, 0.0169 | 24 s |
| Fabric, multiple, polarised integrator | 0.1935, 0.0131, 0.0170 | 94 s |
| Weave, fibre angle from a checkerboard texture | 0.2060, 0.0143, 0.0183 | 41 s |
| Weave, the same pattern as an orientation map image | 0.2056, 0.0142, 0.0183 | 41 s |
| Weave with a textured albedo | 0.1077, 0.0266, 0.0899 | 31 s |
| Red flakes on a copper mirror | 0.6001, 0.2514, 0.2175 | 23 s |
| Two fibre layers, no base | 0.3563, 0.3408, 0.1943 | 33 s |
| The same without the unscattered light | 0.3239, 0.3111, 0.1812 | 32 s |

The two weaves are the same pattern given two ways: their means differ by 0.23% and their pixels by 0.0044, under the noise
of one image (0.0143).

## Limits

* **Speckles through a sheet with no base come from the unscattered light**, not from the material's estimate: the sheet
  render has 6 speckles with it and none without (`"bool delta" false`). Light that crosses a surface without scattering
  cannot be found by sampling the lamp, because the shadow ray stops at the surface; the path tracer only finds the lamp
  through it by chance. The same holds for any transparent shell. Removing it needs shadow rays that pass through such
  surfaces, which is a change to the integrators, not to this material.
* **A base that refracts** was not supported. `"base" "dielectric"` is: the layers and the surface of the dielectric are
  the two interfaces of a flat stack (`layer-stack.md`). Against a walk between the two with their own samples and nothing
  of the stack (`LayerLimits.FlakesOnDielectricByHand`): reflected 0.2592 and transmitted 0.0352 by hand, 0.2588 and 0.0352
  by the stack's samples, 0.2593 and 0.0350 by its value; the same on rough glass and with fibres, from both sides, with
  and without polarisation. A sphere of it shows the speckles of the first limit (24 at 128 samples).
* **Without polarisation** the flakes reflected the mean of the two polarisations at every event. With
  `"bool polarisedinside" true` the layers return what unpolarised light gives (0.1904 against 0.1896 for flakes of
  reflectance 0.5: a small effect here).
* **The polarised scalar differs sample by sample from the unpolarised one** when scattering is multiple (the walk's
  roulette uses the matrix); their means agree.
