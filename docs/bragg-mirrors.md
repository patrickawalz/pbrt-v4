# Bragg mirrors

Structural colour from a cell of thin films repeated many times (a one-dimensional photonic crystal): jewel beetle shells,
dichroic filters. After Fourneau, Pacanowski and Barla, "Interactive Exploration of Vivid Material Iridescence using Bragg
Mirrors" (Computer Graphics Forum 2024). Code: `FrBraggPolarised` in `src/pbrt/fresnel_polarised.cs`, `src/pbrt/bragg.cs`.

## Two things were added

**1. `"integer layerrepeat"` and `"float layerk"` on every material with thin films** (`thinfilm`, the microsurfaces with
films, the facet forge, and the new `bragg`). The listed layers are one cell; the stack is that many cells. The cost is that
of one cell, whatever the count. `layerk` (numbers or spectra, one per film) makes the films absorb: on a conductor, on a
dielectric (what goes through is then less than what is not reflected), and in `bragg`.

```
Material "thinfilm" "float layereta" [ 1.5 1 ] "float layerthickness" [ 92 138 ] "integer layerrepeat" 20
    "float substrateeta" 1.5
```

**2. The material `"bragg"`**, the layered structure of the paper: a rough Bragg layer in a host medium over a diffuse base.

```
Material "bragg" "float layereta" [ 1 1.5 ] "float layerthickness" [ 125 375 ] "integer layerrepeat" 20
    "float roughness" 0.1 "bool remaproughness" false
```

| Parameter | Default | Meaning |
|---|---|---|
| `layereta`, `layerthickness`, ... | | The films of one cell, as for `thinfilm` (indices, nanometres, dispersion formulas) |
| `integer layerrepeat` | 1 | Number of cells |
| `float roughness`, `uroughness`, `vroughness` | 0 | Roughness of the layer; `remaproughness`, `distribution`, `gamma` as for `conductor` |
| `string base` | `diffuse` | `diffuse`: a diffuse base under the layer. `none`: the layer alone, a sheet that reflects some colours and passes the rest |
| `spectrum reflectance` | 0.05 | Reflectance of the base (a dark base shows the colours) |
| `float hosteta` | 1 | Index of the host medium around the layer; above 1 the host gets a smooth surface of its own |
| `integer maxdepth`, `nsamples` | 1000000, 1 | Of the random walk between the layers. The walks end by themselves, with roulette |
| `string scattering` | `multiple` | `multiple`: the light meets facet after facet until it leaves the layer. `single`: the paper's model, one facet |
| `integer scatterdepth` | 1000000 | Most facets a walk on the layer may meet |
| `bool polarisedinside` | false | With an integrator without polarisation: keep the polarisation between the events inside the material (see Limits) |
| `float layerk` or `spectrum layerk` | none | Extinction coefficients of the films, one per film: films that absorb |
| `integer mirrorfilms`, `mirrorrepeat` | none | Several mirrors: the number of films in each mirror's cell, and each mirror's number of cells. The first mirror is on top |

Two mirrors, one over the other:

```
Material "bragg" "float layereta" [ 1 1.5 1 1.5 ] "float layerthickness" [ 125 375 101 143 ]
    "integer mirrorfilms" [ 2 2 ] "integer mirrorrepeat" [ 20 20 ] "float roughness" 0.1
```

## What is computed

**The mirror.** The paper brings Yeh's closed form to rendering: the reflectance of N cells of two films, in a host of the
index of the first film, without N matrix products. Here the same result is derived for the general case: the transfer
matrix of one cell has determinant one, so its N-th power is `U_{N-1}(a) C - U_{N-2}(a) I` with `a` half its trace (the
identity of Chebyshev). That gives the complex reflection and transmission coefficients of both polarisations, for a cell
of any number of films, on any substrate (dielectric or conductor), from either side. In the paper's configuration it is
the paper's formula. Inside a band gap the power would overflow for many cells; it is carried with its exponent taken out.

Films may absorb: their index is then `n - i k`, and the transmittance is returned separately from the reflectance.

**The rough layer** (`BraggLayerBxDF`). A microsurface whose facets are Bragg mirrors, with the same index on both sides.
A facet reflects as a mirror or lets the light straight through, whatever its normal.

* Multiple scattering (default): the microsurface is walked as in Heitz, Hanika, d'Eon and Dachsbacher 2016. The light meets
  facet after facet, above the sheet or below it, until it leaves. Nothing is lost to shadowing.
* Light that only went through facets leaves in its own direction (the paper's ballistic transmission): a specular sample.
* Light that was reflected at least once has a value, estimated by a walk from `wo`: at each facet, what that facet would
  reflect to `wi`, times what the facets on the way out let through. Unbiased, and the same for the same pair of directions.
* A walk chooses reflection or transmission by what it carries at each wavelength, so the sum over the wavelengths never
  grows: no wavelength leaves the layer with more than four times its share.
* Every facet event has its Mueller matrix; they are composed along the walk.
* `"string scattering" "single"` is the paper's model: the microfacet BRDF with the mirror's reflectance (its equation 17),
  and a transmission weighted by one visible facet's transmittance and the shadowing (its equation 22). It loses the light
  that shadowing takes. It is polarised: the matrix of the films at the one facet.

**The layered structure.** The host's surface, the mirrors and the base are the interfaces of one flat stack
(`LayerStackBxDF`, see `layer-stack.md`), evaluated by one walk, so every reflection between them is included. The host
between them is clear. Several mirrors are separate interfaces: the light between them does not interfere. (To have two
cells interfere, list their films in one mirror.)

The first version put one of pbrt's two-interface stacks inside another for each further layer. That was wrong for a
mirror inside the inner stack: value and samples differed (two rough mirrors: 0.69 against 0.55; a rough mirror over a
smooth one: 0.080 against 0.094). The single-mirror results reported then were not affected. The flat stack agrees for all
six combinations of mirrors, host and base (`Bragg.Stacks`), and its renders have the same sphere means as before within
0.3%.

**Two changes to pbrt's stack walk** (`LayeredBxDF` in `bxdfs.cs`), still used by `coateddiffuse` and `coatedconductor`:

* An interface can have both a value and specular directions (the rough Bragg layer, or a stack that contains one). pbrt asks
  whether an interface has anything specular and then treats it as purely specular. Under a host surface that made the
  value of the material zero, so a lamp was only found by chance. Such an interface is now treated as one with a value, and
  its specular samples are weighted as those of a specular interface.
* Before sampling an interface the walk tells it what the path carries at each wavelength (`carried`). Only the Bragg layer
  uses it. `f()` and `PDF()` do not depend on it.

## Against the paper

| | Paper | Here |
|---|---|---|
| Mirror reflectance | Yeh's closed form, two films, index-matched host | The same closed form for any cell, any substrate, with phases |
| RGB approximation of the spectrum | Yes (its section 3.2) | Not used: the renderer is spectral |
| Integrated transmissivity | Precomputed tables | No table: the walk on the facets, unbiased |
| Scattering between facets | No (one facet) | Yes by default; the paper's model as an option |
| Absorbing films | No | Yes |
| Polarisation | No | Yes |
| Single-reflection BRDF (its section 4.3) | Yes, the fast model | Not used: it drops the light reflected more than once |
| Layer stack | Position-free simulation as the reference | That simulation is the model |
| Number of Bragg layers | Several | Several |

## Validation

**Closed form against the explicit stack** (the cell written out N times and evaluated by the existing layer recursion):
4000 random stacks (1 to 3 films per cell, 1 to 24 cells, dielectric and conductor substrates, both sides, 590 of them in a
band gap). Largest difference of a reflectance: 0. Largest difference of a phase (reflection or transmission): 2.4e-7 rad.

**Closed form against the paper's formula** (its equations 2 and 8 to 10, written separately in the test), for the mirrors
of its first figure and two others, over all angles and 360 to 830 nm: largest difference 3e-8.

**A million cells**: finite, reflectance above 0.99 in the gap.

**The rough layer, one facet** (`single`; alpha 0.1 and 0.4, four directions each): reflection by its samples against
directions uniform over the hemisphere agree within the error; the weight of the transmission agrees with the paper's
integral over the facets; reciprocal.

**The rough layer, all facets**: it returns all the light at every wavelength (1.000). The part reflected at least once
agrees between samples and value (alpha 0.4, cosine 0.5: 0.3814 +- 0.0025 against 0.3775 +- 0.0045), and the density
reported is that of the samples. The value is the same from both ends within the error.

**Value against samples through the stack**, at every wavelength, for the layer, the layer on a base, and both under a host
surface (dark and white base), two directions each: all agree within the error (on a base, 900000 samples: 0.9670 +- 0.0043
against 0.9699 +- 0.0011). Largest sample weight 4.0 to 4.5.

**Conservation**: the layer over a white base returns 1.0000 at alpha 0, 0.1 and 0.4. Two mirrors without a base return
1.000, and flat they reflect `R1 + T1^2 R2 / (1 - R1 R2)` to 0.001.

**Absorbing films**: 3000 random stacks against the films written out one by one with characteristic matrices (another
formulation, in double precision): largest difference 3e-8. Without extinction, reflectance plus transmittance is 1. The
transmittance is the same from both sides; the reflectance is not. A film of the index of its surroundings transmits as
Beer's law says (0.63321, 0.46692, 0.21803 against 0.63321, 0.46692, 0.21801).

**Polarisation**: between two flat mirrors each polarisation keeps to itself. With polarisation the pair reflects 0.6397 at
668 nm; the exact value is 0.6393, and the means of the reflectances give 0.6886. A nearly flat layer polarises its
reflection as its films do (1688 of 1692 pairs within 0.03; the others are estimates from a walk that met two facets). The
scalar value is identical with and without the matrix.

**Renders** (220 by 220, path, independent sampler, 128 samples, seed 1, two cores):

* `thinfilm` with `layerrepeat` 20 against the same 40 films listed, 256 samples: identical in every pixel, 57 s against
  125 s.
* Multiple against single scattering at alpha 0.4: sphere mean 0.274 against 0.237; 32 s against 20 s at 32 samples.
* Polarised against unpolarised integrator: one mirror, channel means within 1.1%; two mirrors, they differ by -2.3%,
  +6.2% and -0.8%, as the test above says they should.

Tests: `Bragg.ClosedFormIsTheStack`, `ClosedFormIsThePaper`, `ManyCells`, `Absorption`, `RoughLayer`, `MultipleScattering`,
`ValueIsTheSamples`, `TwoMirrors`, `Polarised`, `PolarisedWeights`, `LayeredStructure`. The suites of the other layered and thin-film materials
pass (Combine, BoundaryStack, Pearlescent, OpenPBR, Lobes, FacetForgePolarised, PolarisedMicrofacet, Polarised, BxDFCheck,
BSDFSampling, Sheen, ThinWall, ThinFilmLayers, BSDFEnergyConservation). `HazyGloss.TransmissionHaze` fails, with the same
numbers before these changes.

## Polarisation between mirrors

A mirror reflects one polarisation more than the other. After a few reflections between two mirrors the light is nearly
all of that one, and a choice between reflection and transmission by the mean of the two reflectances leaves weights that
grow with every reflection: in the two-mirror render a sample carried up to 1794 times its share, and the image had
speckles of 200 times the local brightness. Three changes, all in the polarised path only:

* A stack that is evaluated with polarisation gives its layers the frame of the surface and the matrix of the path so far.
* The mirror chooses by what it reflects and lets through of that light (`BraggLayerBxDF.Polarised`).
* The walk between the layers moves the first element of its matrix into its scalar weight at every step, so that its
  roulette goes by what the walk carries and neither number runs out of range (`LayeredBxDF.Rebalance`).

A layer in this state follows the same walk whether it is asked for the scalar or for the matrix, which the stack asks one
after the other. After the change a sample carries at most 6.4 times its share (`Bragg.PolarisedWeights`), and the
two-mirror render has the noise of the unpolarised one (0.161 against 0.151, where it was 1.228). The single-mirror render
was never affected: its polarised and unpolarised images have the same outliers. I had reported speckles there from looking
at the image; the measurement does not show them.

## Limits

Removed since the first version:

* **Depth caps.** The walks ended at 64 events. They now end by roulette alone (the caps default to 1000000).
* **Without polarisation**, every reflection used the mean of the two polarisations, off by up to 0.05 in reflectance
  between two mirrors. `"bool polarisedinside" true` keeps the polarisation inside the material and returns what
  unpolarised light gives: two flat mirrors reflect 0.6386 at 668 nm (exact 0.6393; by the means 0.6886), and the two-mirror
  render equals the polarised integrator's to four digits. It costs what the polarised integrator costs (209 s against
  106 s), so it is an option.
* **Polarised renders of several mirrors were slow**: 733 s for the two-mirror example. Now 216 s (flat stack, and one
  call for the value and the matrix of an interface that walks).
* **`layerk` was read by `bragg` only.** Now by every thin-film material (`LayerLimits.AbsorbingFilmsOnConductor`,
  `AbsorbingFilmsOnDielectric`: against the films written out, 3e-8, from both sides and beyond the critical angle).
* **The one-facet model was not polarised.** It is (`LayerLimits.BraggOneFacetPolarised`: 8e-8 against the films).

Left:

* **Between surfaces.** The integrator does not tell a material what the path carries at each wavelength, or its
  polarisation without `polarised`, so both are exact inside one material only. Light bouncing between two separate
  Bragg surfaces can still build a large weight at one wavelength: measured over eight surfaces, up to 71.7 times a
  wavelength's share (267.9 with polarisation). Telling the surface before it is sampled (a `Carry()` method on `BxDF`,
  empty by default, called at each integrator's sampling site) brought that to 3.9 and 4.0 with every mean unchanged; it
  was implemented, tested and taken out again, to leave the `BxDF` interface as it is.
* **Mirrors do not interfere with each other.** By design: each is an interface of its own, right when they are apart by
  much more than a wavelength. Films that should interfere go in one mirror.
* **Cost.** Multiple scattering takes 1.6 to 1.9 times as long as the one-facet model at alpha 0.4.
