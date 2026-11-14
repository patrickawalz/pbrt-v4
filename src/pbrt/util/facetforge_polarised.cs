// (not in pbrt-v4, not in the fork) facetforge_polarised.cs
//
// The Mueller matrices of the Facet Forge walk.
//
// The walk is a chain of scatterings on facets, and what it estimates is a sum over the collisions: at each collision, what
// the facets met so far have done to the light, times what this facet sends towards the other direction (the next event), times
// the probability of leaving the microsurface. The scalar walk carries a weight for "what the facets met so far have done";
// here it carries a matrix as well:
//     chain_k = M_k ... M_2 M_1 / (p_k ... p_1),
// M_j the Mueller matrix of the scattering that was sampled at facet j, in the frames of its own plane of incidence (the
// product turns the frames between two facets), and p_j the probability with which that branch was chosen where the facet
// chooses (reflect or refract on a dielectric); the scalar weight is the same product with the unpolarised reflectances. The
// matrix of the estimate is then
//     sum_k chain_(k-1) N_k(to the other direction) G_k,
// N_k the matrix of the next event at facet k and G_k its scalar escape factor, as in the scalar sum; the closed form of the
// first collision is the same sum over the strata with the matrix of the facet at the half vector.
//
// The matrix of a facet:
//   conductor     the Fresnel reflection of the complex index, amplitudes and phases of the two polarisations
//   dielectric    reflection: the Fresnel reflection (with the phases of the total reflection from inside);
//                 refraction: the transmittances 1 - Fs and 1 - Fp, no phase
//   thin film     the Airy sum of the film for each polarisation (FrThinFilmPolarised), reflection and transmission with their
//                 phases; a film of weight w is the incoherent mean of the two surfaces, (1 - w) M + w M_film
//   mirror        the reflection of a perfect conductor (unit reflectances, its phases) times the reflectance
//   diffuse       a depolariser
//   a microsurface (two scales)   the matrix of the inner walk, in the frame of the facet
// Polarised light does not see the unpolarised reflectance that the walk chooses with, so the first element of a matrix is not
// exactly the scalar value after the first facet: the matrix returned is the sum over the scalar estimate of the same walk (the
// convention of BxDF.f(bsdf, ...)), and scalar times matrix is the unbiased Mueller estimate.

using System;
using Attenuation = pbrt.PolarisedLightSystem.Attenuation;

namespace pbrt
{
    public static unsafe partial class ff
    {
        public sealed class Polar
        {
            // The directions, in the space of the renderer, of the axes of the level (of the frame the walk of the level starts in)
            public Vector3f x, y, z;
            // Whether the direction the walk starts from is that of the camera
            public bool fromCamera;
            // The sum of the matrices of what the walk sent to the other direction (EvalLevel)
            public Attenuation sum;
            public bool hasSum;
            // The matrix of the facets met so far (of the whole path: SampleLevel)
            public Attenuation chain;
            public bool hasChain;
            // The matrix of the last specular next event (PhaseSingular)
            public Attenuation phase;
            public bool hasPhase;
            private Vector3f exit, enter;

            public Polar(Vector3f x, Vector3f y, Vector3f z, bool fromCamera)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.fromCamera = fromCamera;
            }

            // A vector of the level, which the walk negates while it is under the surface
            public Vector3f World(V3 v, bool outside)
            {
                Vector3f w = x * (Float)v.x + y * (Float)v.y + z * (Float)v.z;
                return outside ? w : -w;
            }

            // The level on a facet of this one: its axes are the tangent frame of the facet
            public Polar Inner(V3 a, V3 b, V3 n, bool outside)
            {
                return new Polar(World(a, outside), World(b, outside), World(n, outside), fromCamera);
            }

            public void Begin(V3 start, V3 other)
            {
                hasSum = false;
                hasChain = false;
                hasPhase = false;
                Vector3f fix = World(start, true), gen = World(other, true);
                exit = fromCamera ? fix : gen;
                enter = fromCamera ? -gen : -fix;
            }

            private static SampledSpectrum S(double v) { return new SampledSpectrum((Float)v); }

            // FrDielectricPolarised in double, as the scalar walk has its Fresnel term (DielectricR): next to the critical angle
            // the transmittance 1 - F of the single precision function is some percent off, and the first element of a matrix
            // has to be the scalar value
            private static void DielectricPolarised(double cosine, double eta, out Float Fs, out Float Fp, out Float Ds, out Float Dp)
            {
                double c = Math.Clamp(cosine, 0.0, 1.0);
                System.Numerics.Complex cosT = System.Numerics.Complex.Sqrt(new System.Numerics.Complex(1 - (1 - c * c) / (eta * eta), 0));
                if (cosT.Imaginary > 0)
                {
                    cosT = System.Numerics.Complex.Conjugate(cosT);
                }
                System.Numerics.Complex parallel = (eta * c - cosT) / (eta * c + cosT), perpendicular = (c - eta * cosT) / (c + eta * cosT);
                Fs = (Float)(perpendicular.Real * perpendicular.Real + perpendicular.Imaginary * perpendicular.Imaginary);
                Fp = (Float)(parallel.Real * parallel.Real + parallel.Imaginary * parallel.Imaginary);
                Ds = (Float)Math.Atan2(perpendicular.Imaginary, perpendicular.Real);
                Dp = (Float)Math.Atan2(parallel.Imaginary, parallel.Real);
            }

            // The matrix of a specular facet, with the unpolarised reflectance (or transmittance) as its first element. a is the
            // direction the walk comes from and b the one it leaves along, both pointing away from the facet, n the normal on
            // the side of a, cosine that of a with n; iorT / iorI is the index across the facet over that of the side of a.
            public Attenuation Matrix(Facet f, Vector3f a, Vector3f b, Vector3f n, double cosine, double iorI, double iorT, bool reflect)
            {
                Float cos = (Float)Math.Clamp(cosine, 0.0, 1.0);
                SampledSpectrum Fs = new SampledSpectrum(0f), Fp = new SampledSpectrum(0f), Ds = new SampledSpectrum(0f), Dp = new SampledSpectrum(0f);
                Attenuation plain, film = default;
                bool hasFilm = f.film.Enabled() && (f.kind == FacetKind.Conductor || f.kind == FacetKind.Dielectric);
                Float[] filmEta = hasFilm ? new Float[] { f.film.index } : null, filmThickness = hasFilm ? new Float[] { f.film.thickness } : null;
                if (f.hasStack && f.kind == FacetKind.Conductor)
                {
                    f.stack.Conductor(cos, f.eta, f.k, out Fs, out Fp, out Ds, out Dp);
                    return Attenuation.Fresnel(S(f.weight), Fs, Fp, Ds, Dp).Scattered(a, b, n, fromCamera);
                }
                if (f.hasStack && f.kind == FacetKind.Dielectric)
                {
                    double index = (double)f.eta[0], ratio = iorT / iorI;
                    bool outer = Math.Abs(ratio - index) <= Math.Abs(ratio - 1.0 / index);
                    f.stack.Dielectric(Math.Max(cos, 1e-6f) * (outer ? 1 : -1), (Float)index, out Fs, out Fp, out Ds, out Dp, out SampledSpectrum DsThrough,
                                       out SampledSpectrum DpThrough);
                    return (reflect ? Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp)
                                    : Attenuation.Fresnel(S(1), new SampledSpectrum(1f) - Fs, new SampledSpectrum(1f) - Fp, DsThrough, DpThrough))
                        .Scattered(a, b, n, fromCamera);
                }
                switch (f.kind)
                {
                case FacetKind.Conductor:
                {
                    for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                    {
                        pbrt.FrComplexPolarised(cos, new pstd.complex(f.eta[i] / (Float)iorI, f.k[i] / (Float)iorI), out Float fs, out Float fp, out Float ds,
                                                out Float dp);
                        Fs[i] = fs; Fp[i] = fp; Ds[i] = ds; Dp[i] = dp;
                    }
                    plain = Attenuation.Fresnel(S(f.weight), Fs, Fp, Ds, Dp).Scattered(a, b, n, fromCamera);
                    if (hasFilm)
                    {
                        for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                        {
                            pbrt.FrThinFilmPolarised(cos, (Float)iorI, filmEta, filmThickness, f.film.lambda[i], new pstd.complex(f.eta[i], f.k[i]), out Float fs,
                                                     out Float fp, out Float ds, out Float dp);
                            Fs[i] = fs; Fp[i] = fp; Ds[i] = ds; Dp[i] = dp;
                        }
                        film = Attenuation.Fresnel(S(f.weight), Fs, Fp, Ds, Dp).Scattered(a, b, n, fromCamera);
                    }
                    break;
                }
                case FacetKind.Dielectric:
                {
                    DielectricPolarised(cosine, iorT / iorI, out Float fs0, out Float fp0, out Float ds0, out Float dp0);
                    plain = (reflect ? Attenuation.Fresnel(S(1), fs0, fp0, ds0, dp0) : Attenuation.Fresnel(S(1), 1 - fs0, 1 - fp0, 0, 0))
                        .Scattered(a, b, n, fromCamera);
                    if (!reflect)
                    {
                        // (the unpolarised transmittance of the scalar walk, to the digit)
                        double scalar = 1 - DielectricR(cosine, iorT / iorI), mine = 1 - 0.5 * ((double)fs0 + fp0);
                        if (mine > 0 && scalar > 0)
                        {
                            plain = plain * S(scalar / mine);
                        }
                    }
                    if (hasFilm)
                    {
                        // (as FacetFrDielectric: the absolute indices of the two sides, from the ratio)
                        double ior = (double)f.eta[0], r = iorT / iorI;
                        bool fromOuter = Math.Abs(r - ior) <= Math.Abs(r - 1.0 / ior);
                        Float n1 = fromOuter ? 1 : (Float)ior, n3 = fromOuter ? (Float)ior : 1;
                        SampledSpectrum DsT = new SampledSpectrum(0f), DpT = new SampledSpectrum(0f);
                        for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
                        {
                            pbrt.FrThinFilmPolarised(cos, n1, filmEta, filmThickness, false, f.film.lambda[i], new pstd.complex(n3, 0), out Float fs, out Float fp,
                                                     out Float ds, out Float dp, out Float dsT, out Float dpT);
                            Fs[i] = fs; Fp[i] = fp; Ds[i] = ds; Dp[i] = dp; DsT[i] = dsT; DpT[i] = dpT;
                        }
                        film = (reflect ? Attenuation.Fresnel(S(1), Fs, Fp, Ds, Dp)
                                        : Attenuation.Fresnel(S(1), new SampledSpectrum(1f) - Fs, new SampledSpectrum(1f) - Fp, DsT, DpT))
                            .Scattered(a, b, n, fromCamera);
                    }
                    break;
                }
                case FacetKind.Mirror:
                {
                    // The phases of a perfect conductor, with reflectances of exactly one
                    pbrt.FrComplexPolarised(cos, new pstd.complex(1, 1e4f), out Float _, out Float _, out Float ds, out Float dp);
                    return Attenuation.Fresnel(f.R, new SampledSpectrum(1f), new SampledSpectrum(1f), new SampledSpectrum(ds), new SampledSpectrum(dp))
                        .Scattered(a, b, n, fromCamera);
                }
                default:
                    return Attenuation.Lambert(f.R);
                }
                if (!hasFilm)
                {
                    return plain;
                }
                if (f.film.weight >= 1)
                {
                    return film;
                }
                // A film on a part of the facets: the incoherent mean of the two surfaces
                Attenuation mixed = plain * S(1 - f.film.weight);
                mixed += film * S(f.film.weight);
                return mixed;
            }

            // PhaseSingular: the matrix of the next event at a specular facet, times its scalar factor. wi and wo are in the
            // frame of the level (not negated), h is the normal on the side of wi.
            public void Phase(Facet f, V3 wi, V3 wo, V3 h, double cosine, double eta, bool reflect, double factor)
            {
                hasPhase = false;
                if (f.kind == FacetKind.Diffuse || (f.kind != FacetKind.Dielectric && !reflect) || !(factor > 0) || Double.IsInfinity(factor))
                {
                    return;
                }
                phase = Matrix(f, World(wi, true), World(wo, true), World(h, true), cosine, 1.0, eta, reflect) * S(factor);
                hasPhase = true;
            }

            private void Add(Attenuation m)
            {
                if (!m.WithFrames(exit, enter) || m.HasNaNs())
                {
                    return;
                }
                if (hasSum)
                {
                    sum += m;
                }
                else
                {
                    sum = m;
                    hasSum = true;
                }
            }

            // The last specular next event, after the facets met so far (or alone: the closed form of the first collision)
            public void AddPhase(bool afterTheChain, double factor)
            {
                if (!hasPhase || !(factor > 0) || Double.IsInfinity(factor))
                {
                    return;
                }
                Add(PolarisedFacet.Compose(chain, afterTheChain && hasChain, phase, fromCamera) * S(factor));
            }

            // The next event at a facet that is not specular: a diffuse facet (ph its value) or the microsurface on the facet
            public void AddFacet(Polar inner, SampledSpectrum ph, double factor)
            {
                if (!(factor > 0) || Double.IsInfinity(factor))
                {
                    return;
                }
                if (inner != null)
                {
                    if (inner.hasSum)
                    {
                        Add(PolarisedFacet.Compose(chain, hasChain, inner.sum, fromCamera) * S(factor));
                    }
                    return;
                }
                if ((bool)ph)
                {
                    Add(PolarisedFacet.Compose(chain, hasChain, Attenuation.Lambert(ph), fromCamera) * S(factor));
                }
            }

            // The scattering that was sampled at a facet (FacetSampleFrame): the chain with one more facet. wi, wo and wm are
            // those of the walk (negated under the surface), wl and ol the same in the frame of the facet.
            public void Scattered(Stack s, int d, double iorI, double iorT, V3 wi, V3 wo, V3 wm, V3 wl, V3 ol, int layer, Polar inner, bool outside)
            {
                Attenuation next;
                if (d + 1 < s.n)
                {
                    if (inner == null || !inner.hasChain)
                    {
                        return;
                    }
                    next = inner.chain;
                }
                else
                {
                    Facet f = s.lv[d].FacetFor(layer);
                    Vector3f a = World(wi, outside), b = World(wo, outside), n = World(wm, outside);
                    bool reflect = ol.z > 0;
                    next = Matrix(f, a, b, n, wl.z, iorI, iorT, reflect);
                    if (f.kind == FacetKind.Dielectric)
                    {
                        // The branch was chosen with the unpolarised reflectance (FacetSample)
                        double FR = f.Filmed() ? (double)FacetFrDielectric(f, wl.z, iorI, iorT).Average() : DielectricR(wl.z, iorT / iorI);
                        double probability = reflect ? FR : 1 - FR;
                        if (!(probability > 0))
                        {
                            return;
                        }
                        next = next * S(1 / probability);
                    }
                }
                chain = PolarisedFacet.Compose(chain, hasChain, next, fromCamera);
                hasChain = true;
            }
        }
    }
}
