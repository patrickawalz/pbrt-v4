// (not in pbrt-v4) sggxmedium.cs: a medium of microflakes.
//
// Heitz, Dupuy, Crassin, Dachsbacher, "The SGGX Microflake Distribution" (ACM Transactions on Graphics 34(4), 2015), as a
// volume in the manner of Mitsuba (its "sggx" phase function, whose projected area scales the extinction of the medium):
// the medium is filled with tiny two-sided mirrors whose normals follow the SGGX distribution of a symmetric matrix S.
// What a ray meets of them depends on its direction w: the projected area sigma(w) = sqrt(w^T S w). So
//
//   the extinction         sigma_t(w) = sigma(w) times that of the medium the flakes are in,
//   the phase function     p(wo -> wi) = D(h) / (4 sigma(wo)), h the half vector, D(m) = 1 / (pi sqrt(det S) (m^T S^-1 m)^2),
//   the sampling           a normal that wo sees (the distribution of visible normals, Heitz et al.'s listing), mirrored:
//                          its density is the phase function exactly.
//
// sigma_s(w) p(wo -> wi) is the same both ways, so the medium is reciprocal though neither factor is.
//
// Any medium becomes one of microflakes with the parameters below on its MakeNamedMedium: its sigma_a and sigma_s are then
// those for a projected area of 1 (the albedo is its sigma_s / (sigma_a + sigma_s), as before), and its phase function is
// replaced by that of the flakes.
//
//   "string microflakes"      "fibre": the flakes lie around fibres that run along the axis (a ray along the fibres meets
//                             little, a ray across them the most: cloth, hair, fur); "surface": the flakes are little
//                             facets whose normals are around the axis (a ray along the axis meets the most: a stack of
//                             sheets); "matrix": S itself, with "float sggx"
//   "vector3 flakeaxis"       the axis, in the space of the medium (0 0 1)
//   "float flakeroughness"    alpha (0.3): 1 is the same in every direction (the isotropic phase function 1 / (4 pi) and no
//                             dependence on the direction), toward 0 perfectly aligned fibres or facets. The matrix is
//                             (I - a a^T) + alpha^2 a a^T for fibres and alpha^2 (I - a a^T) + a a^T for facets, so the
//                             projected area is at most 1.
//   "float sggx"              for "matrix": Sxx Syy Szz Sxy Sxz Syz, in the space of the medium, positive definite
//   "string flakes"           "specular" (the default): the flakes are mirrors. "diffuse": each flake scatters as a matte
//                             facet. The phase function of diffuse flakes is an integral over the normals with no closed
//                             form (Heitz et al., section 5.2), so it is not evaluated: at a scattering one normal m that
//                             the arriving ray sees is drawn (from the point and the direction, so it is the same for
//                             everything that happens at that scattering), and the phase function of that scattering is
//                             the cosine lobe around m, <wi, m> / pi. Its mean over the normals is the phase function of
//                             the paper, so the estimate is unbiased, also with the samples toward the lights and with
//                             the bidirectional integrators (whose weights add to one whatever the normal is).
//   "string flakeaxisfield"   three float textures (by name) for the x, y and z of the axis at every point, in the space
//                             of the medium, of any length: an orientation field, in place of "flakeaxis"
//   "string flakeroughnessfield"  a float texture (by name) for the roughness at every point, in place of
//                             "flakeroughness". With a field the majorant of a ray is that of the medium inside (the
//                             projected area is at most 1), no longer scaled to the ray's own direction.
//   "spectrum flakek"         polarisation only: the flakes are mirrors of a conductor of the index 1 + i k (none given: a
//                             perfect mirror). The Mueller matrix of a scattering is that of the reflection by the flake,
//                             divided by its reflectance: the energy is the albedo of the medium, whatever k is.
//
// The majorant: along one ray the direction does not change, so the majorant of the ray is that of the medium inside times
// sigma of the ray's own direction: as tight as that of the medium inside.
//
// What asks for the properties at a point with no direction cannot be answered (Medium.SamplePoint without w): the samplers
// of emitting media and a few of the estimators of productionvolume.cs still do, and say so when they meet this medium.
// An emitting medium cannot be given microflakes for the same reason. Diffuse flakes depolarise. "matrix" is the same
// everywhere in the medium (the fields are for "fibre" and "surface").

using System;

namespace pbrt
{
    // The SGGX distribution of a matrix S (symmetric, positive definite), in render space
    public unsafe struct SGGXMatrix
    {
        public Float xx, yy, zz, xy, xz, yz;

        public SGGXMatrix(Float xx, Float yy, Float zz, Float xy, Float xz, Float yz)
        {
            this.xx = xx;
            this.yy = yy;
            this.zz = zz;
            this.xy = xy;
            this.xz = xz;
            this.yz = yz;
        }

        // across (I - a a^T) + along a a^T, for the axis a of length 1
        public static SGGXMatrix Uniaxial(Vector3f a, Float across, Float along)
        {
            Float d = along - across;
            return new SGGXMatrix(across + d * a.x * a.x, across + d * a.y * a.y, across + d * a.z * a.z, d * a.x * a.y, d * a.x * a.z, d * a.y * a.z);
        }

        // S v
        public Vector3f Times(Vector3f v)
        {
            return new Vector3f(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
        }

        public Float Determinant()
        {
            return xx * (yy * zz - yz * yz) - xy * (xy * zz - yz * xz) + xz * (xy * yz - yy * xz);
        }

        // S^-1
        public SGGXMatrix Inverse()
        {
            Float inverse = 1 / Determinant();
            return new SGGXMatrix((yy * zz - yz * yz) * inverse, (xx * zz - xz * xz) * inverse, (xx * yy - xy * xy) * inverse, (xz * yz - xy * zz) * inverse,
                                  (xy * yz - xz * yy) * inverse, (xy * xz - xx * yz) * inverse);
        }

        // The projected area of the flakes seen along w (of length 1): sqrt(w^T S w)
        public Float ProjectedArea(Vector3f w)
        {
            return pbrt.SafeSqrt(pbrt.Dot(w, Times(w)));
        }

        // The largest projected area: the root of the largest eigenvalue of S (the eigenvalues of a symmetric 3 by 3 matrix in
        // closed form)
        public Float LargestProjectedArea()
        {
            double p1 = (double)xy * xy + (double)xz * xz + (double)yz * yz;
            if (p1 == 0)
            {
                return pbrt.SafeSqrt(Math.Max(xx, Math.Max(yy, zz)));
            }
            double q = ((double)xx + yy + zz) / 3;
            double p2 = (xx - q) * (xx - q) + (yy - q) * (yy - q) + (zz - q) * (zz - q) + 2 * p1;
            double p = Math.Sqrt(p2 / 6);
            double bxx = (xx - q) / p, byy = (yy - q) / p, bzz = (zz - q) / p, bxy = xy / p, bxz = xz / p, byz = yz / p;
            double r = (bxx * (byy * bzz - byz * byz) - bxy * (bxy * bzz - byz * bxz) + bxz * (bxy * byz - byy * bxz)) / 2;
            double phi = Math.Acos(Math.Clamp(r, -1.0, 1.0)) / 3;
            return (Float)Math.Sqrt(Math.Max(0.0, q + 2 * p * Math.Cos(phi)));
        }
    }

    // SGGXPhaseFunction Definition: the phase function of specular microflakes
    public sealed unsafe class SGGXPhaseFunction : PhaseFunction
    {
        // flakeK: the extinction coefficient of the conductor of the flakes at the wavelengths of the path
        public SGGXPhaseFunction(SGGXMatrix S, SampledSpectrum flakeK)
        {
            this.S = S;
            inverse = S.Inverse();
            rootDeterminant = pbrt.SafeSqrt(S.Determinant());
            this.flakeK = flakeK;
        }

        public SGGXMatrix Matrix() { return S; }

        // The distribution of the normals of the flakes: 1 / (pi sqrt(det S) (m^T S^-1 m)^2)
        public Float D(Vector3f m)
        {
            Float q = pbrt.Dot(m, inverse.Times(m));
            return 1 / (pbrt.Pi * rootDeterminant * q * q);
        }

        // D(h) / (4 sigma(wo)): both directions point away from the flake
        public override Float p(Vector3f wo, Vector3f wi)
        {
            Vector3f h = wo + wi;
            Float length2 = pbrt.LengthSquared(h);
            Float area = S.ProjectedArea(wo);
            if (length2 == 0 || area == 0)
            {
                return 0;
            }
            return D(h / std.sqrt(length2)) / (4 * area);
        }

        // A normal of the flakes that wo sees, with the density D(m) <wo, m> / sigma(wo) (Heitz et al. 2015, their listing)
        public Vector3f SampleNormal(Vector3f wo, Point2f u)
        {
            Float r = pbrt.SafeSqrt(u.x), phi = 2 * pbrt.Pi * u.y;
            Float x = r * std.cos(phi), y = r * std.sin(phi), z = pbrt.SafeSqrt(1 - x * x - y * y);
            Vector3f wk, wj;
            pbrt.CoordinateSystem(wo, &wk, &wj);
            Vector3f sk = S.Times(wk), sj = S.Times(wj), si = S.Times(wo);
            Float skk = pbrt.Dot(wk, sk), sjj = pbrt.Dot(wj, sj), sii = pbrt.Dot(wo, si);
            Float skj = pbrt.Dot(wk, sj), ski = pbrt.Dot(wk, si), sji = pbrt.Dot(wj, si);
            Float determinant = std.abs(skk * sjj * sii - skj * skj * sii - ski * ski * sjj - sji * sji * skk + 2 * skj * ski * sji);
            Float root = pbrt.SafeSqrt(determinant), inverseRoot = 1 / pbrt.SafeSqrt(sii), part = pbrt.SafeSqrt(sjj * sii - sji * sji);
            Vector3f mk = new Vector3f(root / part, 0, 0);
            Vector3f mj = new Vector3f(-inverseRoot * (ski * sji - skj * sii) / part, inverseRoot * part, 0);
            Vector3f mi = new Vector3f(inverseRoot * ski, inverseRoot * sji, inverseRoot * sii);
            Vector3f local = pbrt.Normalize(x * mk + y * mj + z * mi);
            return pbrt.Normalize(local.x * wk + local.y * wj + local.z * wo);
        }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            Vector3f m = SampleNormal(wo, u);
            Vector3f wi = pbrt.Normalize(-wo + 2 * pbrt.Dot(wo, m) * m);
            Float value = p(wo, wi);
            if (!(value > 0))
            {
                return default;
            }
            PhaseFunctionSample sample = new PhaseFunctionSample();
            sample.p = value;
            sample.wi = wi;
            sample.pdf = value;
            return sample;
        }

        public override Float PDF(Vector3f wo, Vector3f wi)
        {
            return p(wo, wi);
        }

        // The Mueller matrix of the scattering from wo to wi, with the phase function divided out (its first element is 1):
        // the reflection by the flake whose normal is the half vector, a mirror of the conductor 1 + i k (a perfect mirror
        // without one). False where there is no flake that does it.
        public bool MuellerMatrix(Vector3f wo, Vector3f wi, bool fromCamera, out PolarisedLightSystem.Attenuation attenuation)
        {
            attenuation = default;
            Vector3f h = wo + wi;
            Float length2 = pbrt.LengthSquared(h);
            if (length2 < 1e-12f)
            {
                return false;
            }
            h = h / std.sqrt(length2);
            Float cosine = pbrt.AbsDot(wo, h);
            SampledSpectrum Fs = new SampledSpectrum(0f), Fp = new SampledSpectrum(0f), Ds = new SampledSpectrum(0f), Dp = new SampledSpectrum(0f);
            SampledSpectrum scale = new SampledSpectrum(0f);
            SampledSpectrum k = flakeK;
            for (int i = 0; i < pbrt.NSpectrumSamples; ++i)
            {
                Float fs, fp, ds, dp;
                pbrt.FrComplexPolarised(cosine, new pstd.complex(1, k[i]), out fs, out fp, out ds, out dp);
                Fs[i] = fs;
                Fp[i] = fp;
                Ds[i] = ds;
                Dp[i] = dp;
                Float F = 0.5f * (fs + fp);
                scale[i] = F > 0 ? 1 / F : 0;
            }
            attenuation = PolarisedLightSystem.Attenuation.Fresnel(scale, Fs, Fp, Ds, Dp).Scattered(wo, wi, h, fromCamera);
            return true;
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SGGXPhaseFunction S: %f %f %f %f %f %f ]", S.xx, S.yy, S.zz, S.xy, S.xz, S.yz);
        }

        // (a conductor of this extinction reflects all but a millionth: the perfect mirror)
        public const Float PerfectMirrorK = 1e6f;

        private SGGXMatrix S, inverse;
        private Float rootDeterminant;
        private SampledSpectrum flakeK;
    }

    // SGGXDiffusePhaseFunction Definition: the phase function of one scattering by diffuse microflakes. The normal of the
    // flake that scatters is drawn, when it is first needed, from the normals that the arriving ray sees; the phase function
    // is then the cosine lobe around it whatever it is asked from. arrival points back along the ray, of length 1.
    public sealed unsafe class SGGXDiffusePhaseFunction : PhaseFunction
    {
        public SGGXDiffusePhaseFunction(SGGXPhaseFunction flakes, Vector3f arrival, ulong seed)
        {
            this.flakes = flakes;
            this.arrival = arrival;
            this.seed = seed;
        }

        // The normal of the flake of this scattering
        public Vector3f Normal()
        {
            if (!drawn)
            {
                RNG rng = new RNG(seed);
                normal = flakes.SampleNormal(arrival, new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()));
                drawn = true;
            }
            return normal;
        }

        // <wi, m> / pi
        public override Float p(Vector3f wo, Vector3f wi)
        {
            return std.max<Float>(0, pbrt.Dot(wi, Normal())) * pbrt.InvPi;
        }

        public override pstd.optional<PhaseFunctionSample> Sample_p(Vector3f wo, Point2f u)
        {
            Vector3f m = Normal();
            Vector3f local = pbrt.SampleCosineHemisphere(u);
            Vector3f t1, t2;
            pbrt.CoordinateSystem(m, &t1, &t2);
            Vector3f wi = pbrt.Normalize(local.x * t1 + local.y * t2 + local.z * m);
            Float value = p(wo, wi);
            if (!(value > 0))
            {
                return default;
            }
            PhaseFunctionSample sample = new PhaseFunctionSample();
            sample.p = value;
            sample.wi = wi;
            sample.pdf = value;
            return sample;
        }

        public override Float PDF(Vector3f wo, Vector3f wi)
        {
            return p(wo, wi);
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SGGXDiffusePhaseFunction flakes: %s ]", flakes);
        }

        private SGGXPhaseFunction flakes;
        private Vector3f arrival, normal;
        private ulong seed;
        private bool drawn;
    }

    // SGGXMedium Definition: a medium with microflakes in it
    public sealed unsafe class SGGXMedium : Medium, IMajorantMedium<RayMajorantIterator>
    {
        public SGGXMedium(Medium medium, SGGXMatrix S, Spectrum flakeK, bool diffuse = false)
        {
            this.medium = medium;
            this.S = S;
            this.flakeK = flakeK;
            this.diffuse = diffuse;
            // (the flakes of a perfect mirror are the same at every wavelength: one phase function for the medium)
            phase = new SGGXPhaseFunction(S, new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
            largest = S.LargestProjectedArea();
            volumeIntegrator = medium.volumeIntegrator;
        }

        public Medium Inner() { return medium; }

        public SGGXMatrix Matrix() { return S; }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ SGGXMedium phase: %s medium: %s ]", phase, medium);
        }

        public override bool IsEmissive() { return false; }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda)
        {
            throw new InvalidOperationException(Undirected);
        }

        public override MediumProperties SamplePoint(Point3f p, SampledWavelengths lambda, Float time)
        {
            throw new InvalidOperationException(Undirected);
        }

        public override MediumProperties SamplePoint(Point3f p, Vector3f w, SampledWavelengths lambda, Float time)
        {
            MediumProperties mp = medium.SamplePoint(p, w, lambda, time);
            Float length2 = pbrt.LengthSquared(w);
            Vector3f direction = length2 > 0 ? w / std.sqrt(length2) : new Vector3f(0, 0, 1);
            SGGXMatrix here = S;
            SGGXPhaseFunction specular = phase;
            if (HasFields())
            {
                here = MatrixAt(p);
                specular = new SGGXPhaseFunction(here, flakeK != null ? flakeK.Sample(lambda) : new SampledSpectrum(SGGXPhaseFunction.PerfectMirrorK));
            }
            else if (flakeK != null)
            {
                specular = new SGGXPhaseFunction(here, flakeK.Sample(lambda));
            }
            Float area = length2 > 0 ? here.ProjectedArea(direction) : 0;
            mp.sigma_a = mp.sigma_a * area;
            mp.sigma_s = mp.sigma_s * area;
            if (diffuse)
            {
                // (the normal of the flake comes from the point and the direction: the same whoever asks about this scattering)
                mp.phase = new SGGXDiffusePhaseFunction(specular, -direction, pbrt.Hash(p.x, p.y, p.z, direction.x, direction.y, direction.z));
            }
            else
            {
                mp.phase = specular;
            }
            return mp;
        }

        public RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda)
        {
            return SampleRay(ray, tMax, lambda, new ScratchBuffer());
        }

        // Along the ray the direction is its own: the majorant of the medium inside times the projected area for it (a margin
        // for the rounding of the area at each point)
        public override RayMajorantIterator SampleRay(Ray ray, Float tMax, SampledWavelengths lambda, ScratchBuffer buf)
        {
            Float length2 = pbrt.LengthSquared(ray.d);
            Float area = length2 > 0 && !HasFields() ? S.ProjectedArea(ray.d / std.sqrt(length2)) : largest;
            return buf.Alloc(new ScaledMajorantIterator(medium.SampleRay(ray, tMax, lambda, buf), std.min(area * 1.0001f, largest * 1.0001f)));
        }

        public override SampledSpectrum ControlExtinction(SampledWavelengths lambda)
        {
            // (no control: the extinction depends on the direction)
            return new SampledSpectrum(0f);
        }

        public bool HasFields()
        {
            return axisNames != null || roughnessName != null;
        }

        // The matrix at a point (render space) of a medium with fields: the axis and the roughness of the textures there
        public SGGXMatrix MatrixAt(Point3f p)
        {
            TextureEvalContext ctx = new TextureEvalContext(p, default, default, default, default, 0, 0, 0, 0, 0);
            Vector3f axis = axisInRender;
            if (axisTextures != null)
            {
                Vector3f inMedium = new Vector3f(axisTextures[0].Evaluate(ctx), axisTextures[1].Evaluate(ctx), axisTextures[2].Evaluate(ctx));
                if (pbrt.LengthSquared(inMedium) > 0)
                {
                    axis = pbrt.Normalize(renderFromMedium.Apply(inMedium));
                }
            }
            Float alpha = roughnessTexture != null ? pbrt.Clamp(roughnessTexture.Evaluate(ctx), (Float)0.01, 1) : roughness;
            return fibres ? SGGXMatrix.Uniaxial(axis, 1, alpha * alpha) : SGGXMatrix.Uniaxial(axis, alpha * alpha, 1);
        }

        // The fields: the names of the float textures of the axis (three, or null) and of the roughness (or null), with what
        // takes their place where there is none; the textures themselves come with ResolveTextures()
        public void SetFields(string[] axisNames, string roughnessName, bool fibres, Vector3f axisInRender, Float roughness, Transform renderFromMedium)
        {
            this.axisNames = axisNames;
            this.roughnessName = roughnessName;
            this.fibres = fibres;
            this.axisInRender = axisInRender;
            this.roughness = roughness;
            this.renderFromMedium = renderFromMedium;
            // (the projected area of fibres and of facets is at most 1 whatever the axis and the roughness)
            largest = 1;
        }

        // The textures of the fields, once the textures of the scene exist
        public void ResolveTextures(NamedTextures textures, FileLoc loc)
        {
            if (axisNames != null)
            {
                axisTextures = new FloatTexture[3];
                for (int i = 0; i < 3; ++i)
                {
                    if (!textures.floatTextures.TryGetValue(axisNames[i], out axisTextures[i]))
                    {
                        pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: float texture not defined (\"flakeaxisfield\" of a medium of microflakes)", axisNames[i]));
                    }
                }
            }
            if (roughnessName != null && !textures.floatTextures.TryGetValue(roughnessName, out roughnessTexture))
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: float texture not defined (\"flakeroughnessfield\" of a medium of microflakes)", roughnessName));
            }
        }

        // The medium for the parameters of MakeNamedMedium, or the medium itself where they ask for no flakes
        public static Medium Wrap(Medium medium, ParameterDictionary parameters, Transform renderFromMedium, FileLoc loc, Allocator alloc)
        {
            string kind = parameters.GetOneString("microflakes", "");
            if (kind == "")
            {
                return medium;
            }
            if (medium.IsEmissive())
            {
                pbrt.ErrorExit(loc, "\"microflakes\": an emitting medium cannot have microflakes (what it emits toward a point has no direction yet).");
            }
            string flakes = parameters.GetOneString("flakes", "specular");
            if (flakes != "specular" && flakes != "diffuse")
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: flakes unknown (specular, diffuse).", flakes));
            }
            System.Collections.Generic.List<string> axisField = parameters.GetStringArray("flakeaxisfield");
            string roughnessField = parameters.GetOneString("flakeroughnessfield", "");
            if (axisField.Count != 0 && axisField.Count != 3)
            {
                pbrt.ErrorExit(loc, "\"flakeaxisfield\" takes the names of three float textures: the x, y and z of the axis.");
            }
            if (kind == "matrix" && (axisField.Count != 0 || roughnessField != ""))
            {
                pbrt.ErrorExit(loc, "\"microflakes\" \"matrix\" is the same everywhere: the fields are for \"fibre\" and \"surface\".");
            }
            Vector3f axisGiven = new Vector3f(0, 0, 1);
            Float roughnessGiven = 0.3f;
            SGGXMatrix inMedium;
            if (kind == "matrix")
            {
                System.Collections.Generic.List<Float> values = parameters.GetFloatArray("sggx");
                if (values.Count != 6)
                {
                    pbrt.ErrorExit(loc, "\"microflakes\" \"matrix\": \"float sggx\" takes six values, Sxx Syy Szz Sxy Sxz Syz.");
                }
                inMedium = new SGGXMatrix(values[0], values[1], values[2], values[3], values[4], values[5]);
            }
            else if (kind == "fibre" || kind == "surface")
            {
                Vector3f axis = parameters.GetOneVector3f("flakeaxis", new Vector3f(0, 0, 1));
                if (pbrt.LengthSquared(axis) == 0)
                {
                    pbrt.ErrorExit(loc, "\"flakeaxis\" has no length.");
                }
                Float alpha = parameters.GetOneFloat("flakeroughness", 0.3f);
                if (!(alpha > 0 && alpha <= 1))
                {
                    pbrt.ErrorExit(loc, "\"flakeroughness\" is over 0 and to 1.");
                }
                axis = pbrt.Normalize(axis);
                axisGiven = axis;
                roughnessGiven = alpha;
                inMedium = kind == "fibre" ? SGGXMatrix.Uniaxial(axis, 1, alpha * alpha) : SGGXMatrix.Uniaxial(axis, alpha * alpha, 1);
            }
            else
            {
                pbrt.ErrorExit(loc, pbrt.StringPrintf("%s: microflakes unknown (fibre, surface, matrix).", kind));
                return medium;
            }

            // To render space: R S R^T with the rotation of the medium (its three axes in render space, made of length 1: a
            // scale of the medium scales its coefficients, not the flakes)
            Vector3f ex = pbrt.Normalize(renderFromMedium.Apply(new Vector3f(1, 0, 0)));
            Vector3f ey = pbrt.Normalize(renderFromMedium.Apply(new Vector3f(0, 1, 0)));
            Vector3f ez = pbrt.Normalize(renderFromMedium.Apply(new Vector3f(0, 0, 1)));
            Vector3f sx = inMedium.xx * ex + inMedium.xy * ey + inMedium.xz * ez;
            Vector3f sy = inMedium.xy * ex + inMedium.yy * ey + inMedium.yz * ez;
            Vector3f sz = inMedium.xz * ex + inMedium.yz * ey + inMedium.zz * ez;
            // (S' = sum over i, j of S_ij e_i e_j^T: the columns sx, sy, sz against ex, ey, ez)
            SGGXMatrix S = new SGGXMatrix(sx.x * ex.x + sy.x * ey.x + sz.x * ez.x, sx.y * ex.y + sy.y * ey.y + sz.y * ez.y, sx.z * ex.z + sy.z * ey.z + sz.z * ez.z,
                                          sx.x * ex.y + sy.x * ey.y + sz.x * ez.y, sx.x * ex.z + sy.x * ey.z + sz.x * ez.z, sx.y * ex.z + sy.y * ey.z + sz.y * ez.z);
            if (!(S.Determinant() > 0) || !(S.xx > 0) || !(S.xx * S.yy - S.xy * S.xy > 0))
            {
                pbrt.ErrorExit(loc, "\"microflakes\": the SGGX matrix is not positive definite.");
            }
            Spectrum flakeK = parameters.GetOneSpectrum("flakek", null, SpectrumType.Unbounded, alloc);
            SGGXMedium created = new SGGXMedium(medium, S, flakeK, flakes == "diffuse");
            if (axisField.Count == 3 || roughnessField != "")
            {
                created.SetFields(axisField.Count == 3 ? axisField.ToArray() : null, roughnessField != "" ? roughnessField : null, kind == "fibre",
                                  pbrt.Normalize(renderFromMedium.Apply(axisGiven)), roughnessGiven, renderFromMedium);
            }
            return created;
        }

        private const string Undirected =
            "A medium of microflakes was asked for its properties at a point without the direction of travel (Medium.SamplePoint(p, w, lambda, time) " +
            "has it): the technique that asked does not carry microflakes yet.";

        private Medium medium;
        private SGGXMatrix S;
        private Spectrum flakeK;
        private bool diffuse;
        private SGGXPhaseFunction phase;
        private Float largest;
        // The fields
        private string[] axisNames;
        private string roughnessName;
        private FloatTexture[] axisTextures;
        private FloatTexture roughnessTexture;
        private bool fibres;
        private Vector3f axisInRender;
        private Float roughness;
        private Transform renderFromMedium;
    }
}
