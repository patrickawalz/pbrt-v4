// (not in pbrt-v4) layereddistribution.cs
//
// LayeredDistribution: a microsurface whose distribution of normals changes with the height (d'Eon, Bitterli, Weidlich and
// Zeltner 2023, "Microfacet theory for non-uniform heightfields"; the "asymmetric" layering of Facet Forge). The heights are
// cut into strata, listed from the top down, each with a share of the heights and its own distribution. A ray meets the
// strata one after the other: with the depth measured by z = log C (C the part of the heights below), the extinction of a
// stratum along a direction w is 1 + Lambda_j(w) per unit of z, as on a Smith microsurface of that distribution.
//
// What a microfacet BxDF needs of it is closed form, and is given through the three members of MicrofacetDistribution that a
// distribution may override:
//   PDF(w, wm), Sample_wm(w, u)   the normals met first along w: a stratum with the probability that the ray reaches it and is
//                                 stopped in it, then a visible normal of that stratum
//   DG(wo, wi, wm)                D times the masking and shadowing of one scattering,
//                                   reflection:   sum_j D_j(wm) T_j (1 - exp(-k_j l_j)) / k_j,  k_j = 1 + Lambda_j(wo) + Lambda_j(wi)
//                                   refraction:   sum_j D_j(wm) P_j, P_j an incomplete beta function (the ray goes down to the
//                                                 stratum on one side and down from it on the other, Eqs. 12 to 21 of the paper)
//                                 with l_j the thickness of the stratum in z and T_j what is left above it.
// Seen from below the strata come in the other order. With one stratum the reflection is D G of pbrt, and the refraction has
// the beta function of the Smith model, Gamma(1 + Li) Gamma(1 + Lo) / Gamma(2 + Li + Lo), where pbrt has 1 / (1 + Li + Lo).
//
// D, Lambda and Sigma are those of all the normals together (the mixture): what is left for code that knows no strata.

using System;

namespace pbrt
{
    public sealed unsafe class LayeredDistribution : MicrofacetDistribution
    {
        // The strata from the top down, with their shares of the heights (normalised here; a stratum without a share is dropped)
        public LayeredDistribution(MicrofacetDistribution[] strata, Float[] shares)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < strata.Length; ++i)
            {
                if (shares[i] > 0)
                {
                    sum += shares[i];
                    ++count;
                }
            }
            parts = new MicrofacetDistribution[count];
            this.shares = new double[count];
            int next = 0;
            for (int i = 0; i < strata.Length; ++i)
            {
                if (shares[i] > 0)
                {
                    parts[next] = strata[i];
                    this.shares[next] = shares[i] / sum;
                    ++next;
                }
            }
        }

        public int Count() { return parts.Length; }
        public MicrofacetDistribution Part(int i) { return parts[i]; }
        public double Share(int i) { return shares[i]; }

        public override Float D(Vector3f wm)
        {
            double sum = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += shares[i] * parts[i].D(wm);
            }
            return (Float)sum;
        }

        public override Float Lambda(Vector3f w)
        {
            double sum = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += shares[i] * parts[i].Lambda(w);
            }
            return (Float)sum;
        }

        public override Float Sigma(Vector3f w)
        {
            double sum = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += shares[i] * parts[i].Sigma(w);
            }
            return (Float)sum;
        }

        // The stratum that is met in the place `position` along a ray from above (below: the other order)
        private int Stratum(int position, bool fromAbove)
        {
            return fromAbove ? position : parts.Length - 1 - position;
        }

        // The part of the heights (of the side the ray comes from) below the top of the stratum at `position`: 1, then less
        private double Boundary(int position, bool fromAbove)
        {
            double above = 0;
            for (int k = 0; k < position; ++k)
            {
                above += shares[Stratum(k, fromAbove)];
            }
            return position >= parts.Length ? 0 : Math.Max(0.0, 1 - above);
        }

        // The probabilities that a ray along -w is stopped in each stratum, in the order it meets them
        private void FirstCollision(Vector3f up, bool fromAbove, double* probability)
        {
            double left = 1;
            for (int j = 0; j < parts.Length; ++j)
            {
                if (j == parts.Length - 1)
                {
                    probability[j] = left;
                    break;
                }
                double rate = 1 + parts[Stratum(j, fromAbove)].Lambda(up);
                double through = Math.Pow(Boundary(j + 1, fromAbove) / Boundary(j, fromAbove), rate);
                probability[j] = left * (1 - through);
                left *= through;
            }
        }

        public override Float PDF(Vector3f w, Vector3f wm)
        {
            bool fromAbove = w.z > 0;
            Vector3f up = fromAbove ? w : -w;
            double* probability = stackalloc double[parts.Length];
            FirstCollision(up, fromAbove, probability);
            double sum = 0;
            for (int j = 0; j < parts.Length; ++j)
            {
                if (probability[j] > 0)
                {
                    sum += probability[j] * parts[Stratum(j, fromAbove)].PDF_SingleSided(up, wm);
                }
            }
            return (Float)sum;
        }

        public override Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            bool fromAbove = w.z > 0;
            Vector3f up = fromAbove ? w : -w;
            double* probability = stackalloc double[parts.Length];
            FirstCollision(up, fromAbove, probability);
            double below = 0;
            for (int j = 0; j < parts.Length; ++j)
            {
                if (u[0] < below + probability[j] || j == parts.Length - 1)
                {
                    Float remapped = probability[j] > 0 ? std.min<Float>((Float)((u[0] - below) / probability[j]), OneMinusEpsilon) : 0;
                    return parts[Stratum(j, fromAbove)].Sample_wm_SingleSided(up, new Point2f(std.max<Float>(remapped, 0), u[1]));
                }
                below += probability[j];
            }
            return new Vector3f(0, 0, 1);
        }

        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            return Sample_wm(w, u);
        }

        public override Float DG(Vector3f wo, Vector3f wi, Vector3f wm)
        {
            if (wo.z == 0 || wi.z == 0)
            {
                return 0;
            }
            bool fromAbove = wo.z > 0;
            int n = parts.Length;
            if (wo.z * wi.z > 0)
            {
                // One reflection: down to the stratum along one direction and up from it along the other
                double sum = 0, left = 1;
                for (int j = 0; j < n; ++j)
                {
                    MicrofacetDistribution part = parts[Stratum(j, fromAbove)];
                    double rate = 1 + part.Lambda(wo) + part.Lambda(wi);
                    double through = j == n - 1 ? 0 : Math.Pow(Boundary(j + 1, fromAbove) / Boundary(j, fromAbove), rate);
                    sum += part.D(wm) * left * (1 - through) / rate;
                    left *= through;
                }
                return (Float)sum;
            }
            // One refraction: down to the stratum along wo, then down through the rest of the heights, which the other side
            // sees from its top
            double total = 0, logAbove = 0;
            for (int j = 0; j < n; ++j)
            {
                MicrofacetDistribution part = parts[Stratum(j, fromAbove)];
                double a = 1 + part.Lambda(wo), kappa = part.Lambda(wi);
                double low = Boundary(j + 1, fromAbove), high = Boundary(j, fromAbove);
                double beta = ff.IncBetaFull(a, kappa + 1, high) - ff.IncBetaFull(a, kappa + 1, low);
                double logc = logAbove - a * Math.Log(high);
                if (j < n - 1)
                {
                    logc -= kappa * ff.Log1p(-low);
                }
                for (int k = j + 1; k < n; ++k)
                {
                    logc += parts[Stratum(k, fromAbove)].Lambda(wi) * (ff.Log1p(-Boundary(k, fromAbove)) - ff.Log1p(-Boundary(k + 1, fromAbove)));
                }
                double value = part.D(wm) * Math.Exp(logc) * beta;
                if (Double.IsFinite(value))
                {
                    total += value;
                }
                if (j < n - 1)
                {
                    logAbove += a * (Math.Log(low) - Math.Log(high));
                }
            }
            return (Float)total;
        }

        public override bool EffectivelySmooth()
        {
            for (int i = 0; i < parts.Length; ++i)
            {
                if (!parts[i].EffectivelySmooth())
                {
                    return false;
                }
            }
            return true;
        }

        public override void Regularize()
        {
            for (int i = 0; i < parts.Length; ++i)
            {
                parts[i].Regularize();
            }
        }

        public override Float AlphaX()
        {
            double sum = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += shares[i] * Sqr(parts[i].AlphaX());
            }
            return (Float)Math.Sqrt(sum);
        }

        public override Float AlphaY()
        {
            double sum = 0;
            for (int i = 0; i < parts.Length; ++i)
            {
                sum += shares[i] * Sqr(parts[i].AlphaY());
            }
            return (Float)Math.Sqrt(sum);
        }

        // (the V-cavity masking is written for the slopes of one distribution)
        public override double UnitSlopeCdf(double x) { throw new NotSupportedException("LayeredDistribution has no V-cavity form"); }
        public override double UnitSlopeDensity(double x) { throw new NotSupportedException("LayeredDistribution has no V-cavity form"); }
        public override double UnitSlopeMoment(double x) { throw new NotSupportedException("LayeredDistribution has no V-cavity form"); }
        public override double SampleUnitSlopeAcross(double x, double u) { throw new NotSupportedException("LayeredDistribution has no V-cavity form"); }

        public override string ToString()
        {
            string text = "[ LayeredDistribution";
            for (int i = 0; i < parts.Length; ++i)
            {
                text += StringPrintf(" %f: %s", (Float)shares[i], parts[i]);
            }
            return text + " ]";
        }

        private MicrofacetDistribution[] parts;
        private double[] shares;
    }
}
