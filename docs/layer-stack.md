# Flat stacks of layers

Three materials on one new BxDF, `LayerStackBxDF` (`src/pbrt/layerstack.cs`): any number of interfaces with a medium between
each two, evaluated by one walk through all of them.

Sources:

* Gamboa, Gruson, Nowrouzezahrai, "An Efficient Transport Estimator for Complex Layered Materials" (Eurographics 2020): the
  estimator of the value.
* Randrianandrasana, Callet, Lucas, "Transfer matrix based layered materials rendering" (SIGGRAPH 2021): the closed-form
  density.

## Why

pbrt's `LayeredBxDF` has two interfaces and one medium. More layers mean one layered BxDF inside another, and then every
evaluation of the outer one evaluates, samples and asks the density of the inner one, each of which is a random walk of
its own. The flat stack has no nesting.

## The materials

| Material | What it is |
|---|---|
| `layered` | New. Any number of dielectric interfaces, then a conductor base, a diffuse base, or nothing (the stack then transmits) |
| `coateddiffuse2` | `coateddiffuse` with all its parameters, on the flat stack |
| `coatedconductor2` | `coatedconductor` with all its parameters, on the flat stack |

```
Material "layered" "string interfaces" [ "dielectric" "dielectric" "conductor" ] "float eta" [ 1.5 1.3 ]
    "float roughness" [ 0 0.25 0.3 ] "float thickness" [ 0.5 0.2 ] "rgb absorption" [ 0.2 1.6 2.4  0 0 0 ]
```

| Parameter of `layered` | Default | Meaning |
|---|---|---|
| `string interfaces` | required | From the top: `dielectric` any number of times, then at most one `conductor` or `diffuse` |
| `float eta` | 1.5 | For each dielectric interface, the index of what is under it (above the first: 1) |
| `spectrum eta1`, `eta2`, ... | none | The index of one interface as a spectrum: it disperses (the path then keeps one wavelength) |
| `float roughness` | 0 | For each interface; `bool remaproughness` (true) |
| `bool multiscatter` | false | Rough interfaces follow the light from facet to facet (the walk on the microsurface) and lose nothing |
| `bool polarisedinside` | false | With an integrator without polarisation: keep the polarisation between the events inside the material. Also on `coateddiffuse2` and `coatedconductor2` |
| `spectrum reflectance` | 0.5 | Of a diffuse base, or the colour of a conductor base in place of its indices |
| `spectrum conductor.eta`, `conductor.k` | copper | Of a conductor base |
| `float thickness` | 0.01 | For each medium (one fewer than the interfaces) |
| `spectrum absorption` | 0 | Absorption coefficient of each medium, per unit of thickness |
| `float scattering` | 0 | Scattering coefficient of each medium |
| `spectrum albedo` | 1 | The colour a scattering gives the light |
| `float g` | 0 | Asymmetry of the phase function |
| `integer maxdepth`, `nsamples` | 1000000, 1 | Of the walk, which ends by roulette |

Every per-layer parameter can be given three ways: a list with one value per layer (one value: for all); a texture for all
layers; or, with the layer's number after the name, a value or texture for that layer (`"texture absorption1" "dye"`).

The interfaces of a stack can be any BxDF with a value and samples. `bragg` puts its mirrors in one, and `spongecake` its
layers of flakes over a dielectric.

## The value (Gamboa et al.)

One walk from `wo`, and at each of its vertices a connection to `wi` that crosses however many interfaces lie between the
vertex and the outside. The directions of the connection are sampled once per walk, from `wi` inwards, through one
interface after another (the chain). A vertex in a medium, or on the interface at its far side, is joined to the direction
the chain has in that medium. No path is made in two ways, so nothing is weighted against anything else.

Three things differ from the paper:

* **Mirrors.** The paper connects where a path last turns towards the outside. A path that turns at a mirror has no value
  there and would be lost. Here the chain turns at a mirror too and goes on, and ends at the first reflection that has a
  value or where it leaves; and a walk that has met specular events only is connected where an interface lets it through
  with a value. One flag on the walk decides it.
* **Several ways for a run of rough events.** A path often ends with a run of scatterings on rough interfaces (through
  one, through the next, with mirrors between them). With one join per path, all but one of these were sampled from
  `wi` and the last was evaluated, which left large values where its lobe is narrow. Now the run is made in every way:
  the walk samples its first events, the chain the rest, and the event between them is evaluated; each way is weighed
  against the others by the densities both would have had (power heuristic, `Strategy()`). A run begins where the walk
  enters or at a reflection with a value, and takes in only interfaces whose samples have a density (dielectric,
  conductor, diffuse), so which paths are weighed does not depend on what was sampled. This is the bidirectional
  weighting of Guo, Hašan and Zhao ("Position-Free Monte Carlo Simulation for Arbitrary Layered BSDFs", 2018) kept to the
  ways that evaluate one event; Gamboa et al. weigh joins between two interfaces in their implementation. Spread of
  value over density, before and after: a rough coat on a mirror 1.50 and 0.81 (pbrt 1.37); two rough interfaces on a
  mirror 2.46 and 0.94 (pbrt, nested, 1.06); a transmitting slab 2.00 and 1.19. In renders at equal time: the slab had
  15 speckles and has 3; two rough coats on a mirror have a noise of 0.094 where it was 0.123.
* **Vertices in a medium.** The chain's weight carries the cosine of its direction, which belongs to a vertex on an
  interface. For a vertex in a medium it is divided out. (My first version did not; value and samples then differed by 3
  to 10% in every stack with a scattering medium, which is how it was found.)

## The density (Randrianandrasana et al.)

The density that the integrator weights samples with is a closed form, not a walk. It is the paper's two-flux model (its
section 5): each interface and medium has a 2x2 transfer matrix for the energy and one for the energy times the asymmetry
of a Henyey-Greenstein lobe. Their products give, for each interface, the energy and width of the lobe of the light that
went down to it and came back, with all the reflections between layers. The density is the mixture of those lobes (GGX for
reflection, Henyey-Greenstein for transmission) plus a diffuse part.

The paper renders with these lobes, which is approximate. Here they only weight unbiased estimates against each other. For
that any function of the two directions does: the weights of the two ways of sampling a direction add up to one whatever
the function is. So the estimate stays unbiased, and the paper's tables are not needed:

* the albedo of a rough interface and the total internal reflection are replaced by the Fresnel reflectance at the mean
  direction;
* a scattering medium gives its light to the diffuse lobe (the paper's six-flux model is not implemented).

## Polarisation

As in the renderer's other layered BxDF: every scattering of a path has its Mueller matrix (the Fresnel matrix of a
dielectric or a conductor at the facet that sends one direction to the other; a diffuse base and a scattering in a medium
depolarise). The matrices are composed along the walk and along the chain, and the matrix of the value is the sum of the
matrices of the paths over the sum of their scalar contributions.

## Tests (`LayerStack.*`, all pass)

**Value against samples**, seven stacks, two directions each, 200000 samples: all agree within the error. For example three
interfaces with a scattering medium: 0.2196 +- 0.0010 by the value, 0.2192 +- 0.0010 by the samples; a transmitting slab:
0.3736 +- 0.0042 against 0.3728 +- 0.0010.

**Against pbrt's layered BxDF**, the same layers: four coated stacks, and two three-interface stacks against one layered BxDF
inside another. The value agrees in each of four parts of the hemisphere, and all the samples agree (0.2618 against 0.2620,
0.7927 against 0.7926).

**Conservation**: smooth interfaces and nothing that absorbs return 1.0000, for two layers, three layers with a scattering
medium, and a slab.

**Polarisation** (`LayerStack.Polarised`): for six stacks, the first element of the matrix and the elements that polarise
unpolarised light, weighted by the value over each half of the hemisphere and by the samples, are those of pbrt's layered
BxDF with the same layers (by the samples to the fourth decimal: 0.7117 against 0.7116, 0.0400 against 0.0400). The scalar
value is identical with and without the matrix. A smooth coat on a mirror, where every path is known, reflects 0.9417,
0.9410 and 0.9417 at three angles against 0.9417, 0.9410 and 0.9413 with each polarisation summed over its own reflections.

**Density**: positive wherever the stack has a value; integrates to 0.93 to 1.00 over the sphere.

**Cost**, microseconds per call, flat against pbrt:

| Stack | Value | Sample | Density |
|---|---|---|---|
| Rough coat on a diffuse base | 1.79 / 2.25 | 0.84 / 1.26 | 0.25 / 0.58 |
| Rough coat on a mirror | 1.65 / 2.09 | 0.83 / 0.87 | 0.48 / 0.70 |
| Three interfaces, diffuse base (pbrt: nested) | 3.06 / 8.35 | 1.35 / 1.96 | 0.36 / 0.82 |
| Three interfaces, mirror base (pbrt: nested) | 2.38 / 9.64 | 1.06 / 1.69 | 0.61 / 1.91 |

**Renders** (220 by 220, path, independent sampler, 128 samples, seed 1):

| Pair | Mean difference | Noise (flat / pbrt) | Time (flat / pbrt) |
|---|---|---|---|
| `coateddiffuse2` / `coateddiffuse` | 0.01% | 0.0157 / 0.0157 | 18.9 s / 19.9 s |
| `coatedconductor2` / `coatedconductor` | 0.03% | 0.0230 / 0.0255 | 21.4 s / 22.4 s |
| the same two pairs, polarised integrator | as above | as above | 32.9 s / 35.0 s, 39.0 s / 40.9 s |

## Later tests (`LayerLimits.*`, all pass)

* **Walked interfaces** (`multiscatter`): value against samples for a walked rough coat on a diffuse base, on a walked rough
  metal, and smooth glass on a walked metal. At roughness 0.7 a rough coat on a metal returns 0.81 walked and 0.21 with
  one reflection per facet (0.86 and 0.56 at 0.3).
* **Polarisation inside**: see `bragg-mirrors.md`; a rough coat on a conductor 0.6751 against 0.6757 with the matrix.
* **Density**: positive for every sampled direction, also from inside beyond the critical angle.

Two errors found by these tests and fixed:

* The density was zero for light that leaves a rough dielectric from inside beyond the critical angle of its mean
  direction (the lobes have nothing there; the facets still let light out). The integrator divided by it: not-a-number
  pixels in any transmitting stack seen from inside at such an angle.
* The stack and an interface that walks (the layers of flakes) seeded their random numbers with the same directions, so the
  walk of the stack was not independent of the interface's: samples 2.5% off (the value was right). Only the new
  dielectric base of `spongecake` has such an interface first in a stack.

## Limits

Removed: constant layers (textures and dispersion now), the light that rough interfaces lost (`multiscatter`), the depth
cap, most of the speckles of the transmitting slab, and the noise of a mirror under one or two rough interfaces (the
weighed runs above; it was noisier per evaluation than pbrt's nested walk and is not now).

Left:

* **Runs through interfaces that walk are not weighed.** A Bragg layer, a layer of flakes or a `multiscatter` interface
  has no density for a pair of directions, so a path through one is still joined in one way.
* **Runs with a scattering in a medium are not weighed** either; the phase function is sampled from one end.
* **Two layers cost what pbrt's cost**: the value of a two-interface stack takes 1.25 to 1.75 times as long per call as
  pbrt's since the weighing (it was 0.8), and the renders take the same time (23 s against 24 s, 27 s against 27 s) with
  the same noise. The gain is in three layers and more (value 4.8 against 11.6 microseconds, 7.7 against 13.8).
* **The density is approximate** by design (see above). A poor density costs noise, not correctness. A lobe of the shape of
  a rough interface in place of the Henyey-Greenstein one for the transmission was tried: no difference in the renders.
* **A sphere of a transmitting stack still has speckles** (86 in the dispersive slab render, 139 before the weighed
  joins): the lamp seen through two rough surfaces, which a path tracer finds by chance.
