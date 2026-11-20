// (not in pbrt-v4) util/transformeddistribution.cs
//
// "Microsurface Transformations" (Atanasov, Koylazov, Dimov and Wilkie, EGSR 2022): a microsurface mapped by a linear
// transformation of the tangent plane,
//     M = [m00 m01 0; m10 m11 0; 0 0 1],
// is a microsurface again, and its distribution, masking and visible normals follow from those of the original in closed form:
//     D(h)      = |det M| D0(M^T h / |M^T h|) / |M^T h|^4                 (Eq. 22)
//     G1(v)     = G1_0(M^-1 v / |M^-1 v|)                                 (Eq. 12)
//     sigma(v)  = |M^-1 v| sigma0(M^-1 v / |M^-1 v|)                      (the projected area, in every direction)
//     a visible normal of v is M^-T u / |M^-T u|, with u a visible normal of the original for M^-1 v / |M^-1 v|.
// A diagonal matrix is the usual anisotropy (a stretch by s divides the roughness by s), a rotation turns the distribution, and
// a shear gives the skewed distributions that no pair of roughnesses does. The original may be any distribution, a mixture or
// strata of them included.

using System;

namespace pbrt
{
    public sealed unsafe class TransformedDistribution : MicrofacetDistribution
    {
        public TransformedDistribution(MicrofacetDistribution original, Float m00, Float m01, Float m10, Float m11)
        {
            this.original = original;
            this.m00 = m00;
            this.m01 = m01;
            this.m10 = m10;
            this.m11 = m11;
            Float d = m00 * m11 - m01 * m10;
            i00 = m11 / d;
            i01 = -m01 / d;
            i10 = -m10 / d;
            i11 = m00 / d;
            determinant = std.abs(d);
        }

        public MicrofacetDistribution Original()
        {
            return original;
        }

        public override Float D(Vector3f wm)
        {
            Float length;
            Vector3f u = NormalToOriginal(wm, out length);
            return determinant * original.D(u) / Sqr(Sqr(length));
        }

        public override Float Lambda(Vector3f w)
        {
            Float length;
            return original.Lambda(ToOriginal(w, out length));
        }

        public override Float Sigma(Vector3f w)
        {
            Float length;
            Vector3f v = ToOriginal(w, out length);
            return length * original.Sigma(v);
        }

        public override Vector3f Sample_wm_SingleSided(Vector3f w, Point2f u)
        {
            Float length;
            return NormalFromOriginal(original.Sample_wm_SingleSided(ToOriginal(w, out length), u));
        }

        public override Vector3f Sample_wm(Vector3f w, Point2f u)
        {
            Float length;
            return NormalFromOriginal(original.Sample_wm(ToOriginal(w, out length), u));
        }

        // The density of the original, with the solid angle of its normals: du = |det M| dh / |M^T h|^3
        public override Float PDF(Vector3f w, Vector3f wm)
        {
            Float length, normalLength;
            Vector3f v = ToOriginal(w, out length);
            Vector3f u = NormalToOriginal(wm, out normalLength);
            return original.PDF(v, u) * determinant / (normalLength * Sqr(normalLength));
        }

        public override Float DG(Vector3f wo, Vector3f wi, Vector3f wm)
        {
            Float length, normalLength;
            Vector3f vo = ToOriginal(wo, out length);
            Vector3f vi = ToOriginal(wi, out length);
            Vector3f u = NormalToOriginal(wm, out normalLength);
            return original.DG(vo, vi, u) * determinant / Sqr(Sqr(normalLength));
        }

        public override bool EffectivelySmooth()
        {
            return original.EffectivelySmooth();
        }

        public override void Regularize()
        {
            original.Regularize();
        }

        // The slopes of the transformed microsurface are M^-T times those of the original
        public override Float AlphaX()
        {
            return std.sqrt(Sqr(i00 * original.AlphaX()) + Sqr(i10 * original.AlphaY()));
        }

        public override Float AlphaY()
        {
            return std.sqrt(Sqr(i01 * original.AlphaX()) + Sqr(i11 * original.AlphaY()));
        }

        public override double UnitSlopeCdf(double x) { throw new NotSupportedException("TransformedDistribution has no V-cavity form"); }
        public override double UnitSlopeDensity(double x) { throw new NotSupportedException("TransformedDistribution has no V-cavity form"); }
        public override double UnitSlopeMoment(double x) { throw new NotSupportedException("TransformedDistribution has no V-cavity form"); }
        public override double SampleUnitSlopeAcross(double x, double u) { throw new NotSupportedException("TransformedDistribution has no V-cavity form"); }

        // M^-1 v, normalised
        private Vector3f ToOriginal(Vector3f v, out Float length)
        {
            Vector3f w = new Vector3f(i00 * v.x + i01 * v.y, i10 * v.x + i11 * v.y, v.z);
            length = Length(w);
            return w / length;
        }

        // M^T h, normalised
        private Vector3f NormalToOriginal(Vector3f h, out Float length)
        {
            Vector3f u = new Vector3f(m00 * h.x + m10 * h.y, m01 * h.x + m11 * h.y, h.z);
            length = Length(u);
            return u / length;
        }

        // M^-T u, normalised
        private Vector3f NormalFromOriginal(Vector3f u)
        {
            return Normalize(new Vector3f(i00 * u.x + i10 * u.y, i01 * u.x + i11 * u.y, u.z));
        }

        private MicrofacetDistribution original;
        private Float m00, m01, m10, m11;
        private Float i00, i01, i10, i11;
        private Float determinant;
    }
}
