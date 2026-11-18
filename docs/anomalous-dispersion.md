# Anomalous dispersion

The index of refraction of a coloured transparent material, computed from its absorption: `src/pbrt/anomalousdispersion.cs`.
After Weidlich and Wilkie, "Anomalous Dispersion in Predictive Rendering" (EGSR 2009). This first version follows the paper
as closely as possible.

```
MakeNamedMedium "dye" "string type" "homogeneous" "gaussian sigma_a" [ 550 20 0.914 ] "spectrum sigma_s" [ 300 0 900 0 ]
AttributeBegin
  MediumInterface "dye" ""
  Material "dielectric" "float cauchy" [ 1.3199 0.003878 ]
      "gaussian absorption" [ 550 20 0.914 ] "float metersperunit" 1e-6
  Shape ...
AttributeEnd
```

The absorption of the medium inside the object and the `absorption` of the material are the same spectrum: one gives the
colour, the other bends the index.

## Parameters

They go with the index of any material that reads it through `ParametricSpectra.Index` (`dielectric`, `thindielectric`,
`coateddiffuse`, the thin-film substrate and so on, with the prefix of that index). `eta`, `cauchy` or `sellmeier` is then
the background index. Without `extinction` or `absorption` nothing changes.

| Parameter | Default | Meaning |
|---|---|---|
| `spectrum extinction` | | Extinction coefficient k, the imaginary part of the index (the paper's input) |
| `spectrum absorption` | | Or the absorption coefficient per scene unit, the medium's `sigma_a`; k = lambda alpha / (4 pi) |
| `float metersperunit` | 1 | Size of a scene unit, for `absorption` |
| `float absorptionscale` | 1 | Multiplies either one (the concentration) |
| `float dispersionrange` | 300 900 | Wavelengths of the integral, nm (the paper's example: 300 720) |
| `integer dispersionsamples` | 121 | Their number (5 nm apart, the paper's step) |

## What it computes

The paper's equation 12, the Kramers-Kronig relation in the wavelength over a finite range, with a background index for
everything outside it:

`n(l) = n_b(l) + (2 l^2 / pi) P integral k(l') / (l' (l^2 - l'^2)) dl'`

by Maclaurin's formula (its equations 15 and 16 and its appendix): at each of the evenly spaced wavelengths the sum runs
over the points of the other parity, so it never meets the pole. Between the wavelengths the correction is interpolated
linearly.

Two things differ from the paper:

* **No ray splitting.** The paper forks 45 rays of one wavelength each at the first dispersive surface. Here a path goes on
  with its first wavelength when it meets a surface whose index differs between its wavelengths, as for every other
  dispersive material of the renderer. Unbiased, no forking.
* **The sign of the integral.** As printed, equation 12 has its limits from the longest to the shortest wavelength; the
  appendix code sums in the other direction. The code's direction is the right one (the index is higher on the long side of
  a band), and is what is implemented; the Lorentz test below settles it.

## Validation

**The formula against a material known in closed form.** A Lorentz oscillator (band at 550 nm, 30 nm wide, k = 0.04 at its
peak, on a background of 1.333) has its index and absorption together as one complex function. The index from its
absorption alone, by the paper's formula, between 400 and 700 nm (the index swings from 1.3135 to 1.3536):

| Range and samples | Largest error of the index |
|---|---|
| 300 to 900 nm, 121 (the default) | 0.000021 |
| 300 to 720 nm, 85 (the paper's example) | 0.00025 |
| 100 to 20000 nm, 19901 | 0.00000016 |

The error is the truncation of the range, not the quadrature.

**Renders against Snell's law.** A camera looks through the thin edge of a 30 degree prism (1.6 micrometres of path) at a
distant slit, as in a spectroscope; the slit emits one narrow line per render, and the column of the line in the image is
compared with the column that Snell's law gives for the index of the material, computed outside the renderer. `volpath`,
2048 samples, independent sampler, 400 columns over 4 degrees:

| Prism | Columns span | Rendered minus predicted, mean | Largest |
|---|---|---|---|
| Dye, index from its absorption | 122 to 267 | -0.02 px | 0.19 px |
| Dye at a third of the concentration | 175 to 220 | -0.02 px | 0.05 px |
| Same dye, index of water | 171 to 211 | +0.01 px | 0.08 px |

With the index from the absorption the order of the wavelengths is the anomalous one: 525 nm is deviated least (column
267), 575 nm most (column 122), and 420 and 650 nm fall together between them (182 and 185).

**Integrators.** The white-light image of the dye prism, 512 samples against `volpath` at 2048: `production` total 1.006,
`bdpt` total 1.005, the centroid of the spectrum at column 186.9 in all three.

Tests: `AnomalousDispersion.LorentzBand`, `AnomalousDispersion.MaterialIndex`.

## What is not exact yet

* **The dye is synthetic.** The band is a Gaussian at 550 nm with the peak extinction of the paper's strongest rose bengal
  solution (0.04) on a Cauchy fit of water. The paper's measured curves are plots, not tables.
* **Truncation and the background.** Absorption outside the range of the integral is not seen; the background index stands
  for it. With the paper's range the error is ten times that of the default range.
* **Linear interpolation** of the correction between the samples, and of a tabulated absorption.
* **Only the real index changes.** The Fresnel reflectance still takes the index as real; the imaginary part (up to 0.04
  here) is not in it.
* **Geometric optics at a micrometre.** A dye this strong lets light through only over a micrometre or two (as the paper
  notes), where diffraction would matter in reality.
* **The spectral film with dispersion.** Seen while validating, not part of this work and not investigated further: in
  this dispersive scene every bucket of the spectral film held the whole spectrum instead of its own band. The likely cause
  is that the film adds the radiance of the wavelengths a path has dropped at a dispersive surface to their buckets. The
  RGB film is not affected, and the validation above uses it.
