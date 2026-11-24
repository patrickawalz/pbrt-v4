// (not in pbrt-v4) furbxdf.cs
//
// A fiber of hair or fur as a cylinder of layers: a cuticle (the surface, a dielectric with scales at a tilt), a cortex that
// absorbs, and a medulla that scatters, as in
//
//   Ling-Qi Yan, Henrik Wann Jensen, Ravi Ramamoorthi. 2017. An Efficient and Practical Near and Far Field Fur Reflectance
//   Model. ACM Trans. Graph. 36, 4, Article 67.
//
// with the index of refraction of the cortex and the medulla the same (theirs), but without their tables for what the
// medulla scatters: the light is followed through the cylinder by a random walk, as LayeredBxDF follows it through its
// layers. A walk starts where the ray meets the cylinder (at the offset h across it), is reflected or refracted at the
// surface (Fresnel's choice), loses to the absorption of each layer that it crosses, is scattered in the layers that scatter
// (Henyey-Greenstein), and leaves in a direction e with a weight. The roughness of the cuticle is what it is in HairBxDF:
// the direction of a walk is spread along the fiber by Mp (of variance v) and around it by a logistic (of scale s), so
//
//   f(wo, wi) = E[ weight Mp(wi; e) N(phi_i - phi_e) ] / |cos theta_i|
//
// over the walks from wo. A fiber of one layer that does not scatter is that of HairBxDF, with every order of reflection
// inside in its own direction (HairBxDF spreads those after the third evenly around the fiber).
//
// The layers are rings around the axis, from the middle out: `bounds` their outer radii (the last is 1, the surface),
// `absorption` and `scattering` of each for a length of one radius of the fiber. A pigment that changes with the depth into
// the fiber is so many rings (the "fur" material reads them from its texture at as many depths).
//
// The value and the density are estimates from a few walks, as those of LayeredBxDF are (pdfIsProportional).
//
// Two things beside the fiber of the paper:
//
// A cuticle of microfacets (UseMicrofacets, and with the light between the facets UseMicrofacetsOfHeitz and
// UseMicrofacetsOfFacetForge: the BxDFs of the renderer for a rough dielectric with multiple scattering). The surface is a rough dielectric (the DielectricBxDF of pbrt with the
// Trowbridge-Reitz distribution, about the normal of the scales), which reflects and lets light through in a lobe of its
// own, after the fibers of microfacets of Huang et al. 2022 (A Microfacet-based Hair Scattering Model): Mp and the logistic
// are not used. Each time the walk is at the surface, the light that the surface would send from there to wi is added (as
// LayeredBxDF adds the light from each vertex of its walk), and the walk goes on with a direction of the surface until one
// leaves the fiber.
//
// A cross-section that is an ellipse (Aspect, Orientation), as that of the hair of many people is (Huang et al. 2023,
// Scattering from Elliptical Hair Fibers Based on Microfacet): the fiber and its rings are the circles of the walk made
// narrower by Aspect across the direction Orientation around the axis; the walk is that of the circles, with the lengths
// and the normals of the ellipse. (The walk follows the rays through the ellipse; it is not the integration over the
// cross-section of that paper.)

using System;

namespace pbrt
{
    // FurBxDF Definition
    public sealed unsafe class FurBxDF : BxDF
    {
        // FurBxDF Public Methods
        public FurBxDF(Float h, Float eta, Float[] bounds, SampledSpectrum[] absorption, Float[] scattering, Float g, Float beta_m, Float beta_n, Float alpha,
                       int samples, int maxDepth)
        {
            this.h = h;
            this.eta = eta;
            this.bounds = bounds;
            this.absorption = absorption;
            this.scattering = scattering;
            this.g = g;
            this.samples = samples;
            this.maxDepth = maxDepth;
            // (the variances and the scale of HairBxDF)
            v0 = pbrt.Sqr(0.726f * beta_m + 0.812f * pbrt.Sqr(beta_m) + 3.7f * pbrt.Pow20(beta_m));
            s = 0.626657069f * (0.265f * beta_n + 1.194f * pbrt.Sqr(beta_n) + 5.372f * pbrt.Pow22(beta_n));
            sinAlpha = Math.Sin(alpha * Math.PI / 180);
            cosAlpha = Math.Cos(alpha * Math.PI / 180);
        }

        public static string Name()
        {
            return "FurBxDF";
        }

        public override string ToString()
        {
            return pbrt.StringPrintf("[ FurBxDF h: %f eta: %f layers: %d g: %f samples: %d maxDepth: %d ]", h, eta, bounds.Length, g, samples, maxDepth);
        }

        public override void Regularize()
        {
        }

        public override BxDFFlags Flags()
        {
            return BxDFFlags.GlossyReflection;
        }

        public override SampledSpectrum f(Vector3f wo, Vector3f wi, TransportMode mode)
        {
            RNG rng = new RNG(pbrt.Hash(pbrt.GetOptions().seed, wo), pbrt.Hash(wi));
            SampledSpectrum sum = Estimate(wo, wi, ref rng, samples, out Float density);
            return pbrt.AbsCosTheta(wi) > 0 ? sum / pbrt.AbsCosTheta(wi) : sum;
        }

        public override Float PDF(Vector3f wo, Vector3f wi, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            // (the walks of f for the same pair of directions: where one of them comes near wi, the value and the density are
            // large together, and the weight that the density gives the value of another way of sampling is small with it.
            // The weights of the ways of sampling a pair still add up to one: each uses this density.)
            RNG rng = new RNG(pbrt.Hash(pbrt.GetOptions().seed, wo), pbrt.Hash(wi));
            Estimate(wo, wi, ref rng, samples, out Float density);
            // (with a part that is the same in every direction, as the estimate of LayeredBxDF has: a few walks can all miss
            // the direction)
            return pbrt.Lerp(0.9f, 1 / (4 * pbrt.Pi), density);
        }

        // The mean over `count` walks from wo of weight Mp N at wi (the value of the fiber times |cos theta_i|), and of Mp N
        // alone (the density of Sample_f at wi, but for the walks that are lost on the way)
        public SampledSpectrum Estimate(Vector3f wo, Vector3f wi, ref RNG rng, int count, out Float density)
        {
            wo = Turned(wo, -Orientation);
            wi = Turned(wi, -Orientation);
            if (surface != null)
            {
                SampledSpectrum total = new SampledSpectrum(0f);
                density = 0;
                for (int i = 0; i < count; ++i)
                {
                    RoughWalk(wo, ref rng, true, wi, ref total, ref density, out double ax, out double ay, out double az, out SampledSpectrum left);
                }
                density /= count;
                return total / count;
            }
            Float sinTheta_i = wi.x, cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            Float phi_i = (Float)Math.Atan2(wi.z, wi.y);
            SampledSpectrum sum = new SampledSpectrum(0f);
            density = 0;
            for (int i = 0; i < count; ++i)
            {
                Expected(wo, ref rng, sinTheta_i, cosTheta_i, phi_i, ref sum, ref density);
            }
            density /= count;
            return sum / count;
        }

        public override pstd.optional<BSDFSample> Sample_f(Vector3f wo, Float uc, Point2f u, TransportMode mode, BxDFReflTransFlags sampleFlags)
        {
            RNG rng = new RNG(pbrt.Hash(pbrt.GetOptions().seed, wo), pbrt.Hash(uc, u));
            wo = Turned(wo, -Orientation);
            if (surface != null)
            {
                // A walk through the rough surface: its direction and its weight, the density unknown
                SampledSpectrum none = new SampledSpectrum(0f);
                Float unused = 0;
                if (!RoughWalk(wo, ref rng, false, default, ref none, ref unused, out double ax, out double ay, out double az, out SampledSpectrum left))
                {
                    return default;
                }
                Vector3f leaving = Turned(new Vector3f((Float)ax, (Float)ay, (Float)az), Orientation);
                if (pbrt.AbsCosTheta(leaving) == 0)
                {
                    return default;
                }
                BSDFSample rough = new BSDFSample(left / pbrt.AbsCosTheta(leaving), leaving, 1, Flags());
                rough.pdfIsProportional = true;
                return rough;
            }
            if (!Walk(wo, ref rng, 0, out Float reflectance, out double ex, out double ey, out double ez, out SampledSpectrum weight, out int kind))
            {
                return default;
            }
            // The direction of the walk, spread along the fiber (Mp) and around it (the logistic), as HairBxDF samples them
            Float variance = Variance(kind);
            Float sinTheta_e = (Float)(-ex), cosTheta_e = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_e));
            Float cosTheta = 1 + variance * (Float)Math.Log(Math.Max(u[0], 1e-5f) + (1 - u[0]) * Math.Exp(-2 / variance));
            Float sinTheta = pbrt.SafeSqrt(1 - pbrt.Sqr(cosTheta));
            Float cosPhi = (Float)Math.Cos(2 * Math.PI * u[1]);
            Float sinTheta_i = -cosTheta * sinTheta_e + sinTheta * cosPhi * cosTheta_e;
            Float cosTheta_i = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_i));
            Float phi_i = (Float)Math.Atan2(ez, ey) + pbrt.SampleTrimmedLogistic(uc, s, -pbrt.Pi, pbrt.Pi);
            Float kernel = Kernel(sinTheta_i, cosTheta_i, phi_i, ex, ey, ez, kind);
            Vector3f wi = Turned(new Vector3f(sinTheta_i, cosTheta_i * (Float)Math.Cos(phi_i), cosTheta_i * (Float)Math.Sin(phi_i)), Orientation);
            if (!(kernel > 0) || pbrt.AbsCosTheta(wi) == 0)
            {
                return default;
            }
            BSDFSample sample = new BSDFSample(weight * kernel / pbrt.AbsCosTheta(wi), wi, kernel, Flags());
            sample.pdfIsProportional = true;
            return sample;
        }

        // A walk for the value at one direction: at the surface the light is not sent one way or the other by chance, but
        // the part that Fresnel sends out is counted there (with Mp N at the direction asked for) and the walk goes on inside
        // with the part that is reflected, so that only the scattering of the medulla is left to chance (a fiber that does
        // not scatter has no chance in it at all: the sum of HairBxDF over every order of reflection). sum: the weights times
        // Mp N; density: the chances of the walks of Sample_f times Mp N.
        private void Expected(Vector3f wo, ref RNG rng, Float sinTheta_i, Float cosTheta_i, Float phi_i, ref SampledSpectrum sum, ref Float density)
        {
            if (!Enter(wo, out double y, out double z, out double nx, out double ny, out double nz))
            {
                return;
            }
            double dx = -wo.x, dy = -wo.y, dz = -wo.z;
            if (!Usable(false, dx, dy, dz, nx, ny, nz))
            {
                Untilt(ref nx, ref ny, ref nz);
            }
            double cosine = -(dx * nx + dy * ny + dz * nz);
            if (!(cosine > 0))
            {
                return;
            }
            Float reflectance = pbrt.FrDielectric((Float)cosine, eta);
            double ex = dx + 2 * cosine * nx, ey = dy + 2 * cosine * ny, ez = dz + 2 * cosine * nz;
            if (ey * ny + ez * nz > 0)
            {
                Float kernel = reflectance * Kernel(sinTheta_i, cosTheta_i, phi_i, ex, ey, ez, 0);
                sum += new SampledSpectrum(kernel);
                density += kernel;
            }
            double sine2 = (1 - cosine * cosine) / ((double)eta * eta);
            double inside = Math.Sqrt(Math.Max(0, 1 - sine2));
            dx = dx / eta + (cosine / eta - inside) * nx;
            dy = dy / eta + (cosine / eta - inside) * ny;
            dz = dz / eta + (cosine / eta - inside) * nz;
            SampledSpectrum weight = new SampledSpectrum(1 - reflectance);
            Float chance = 1 - reflectance;
            int layer = bounds.Length - 1, reflections = 0, steps = 0;
            bool scattered = false;
            while (steps < maxDepth)
            {
                if (!Travel(ref y, ref z, ref dx, ref dy, ref dz, ref layer, ref weight, ref rng, ref scattered, ref steps))
                {
                    return;
                }
                Normal(ref y, ref z, out nx, out ny, out nz);
                if (!Usable(true, dx, dy, dz, nx, ny, nz))
                {
                    Untilt(ref nx, ref ny, ref nz);
                }
                cosine = dx * nx + dy * ny + dz * nz;
                if (!(cosine > 0))
                {
                    return;
                }
                reflectance = pbrt.FrDielectric((Float)cosine, 1 / eta);
                if (reflectance < 1)
                {
                    sine2 = (1 - cosine * cosine) * ((double)eta * eta);
                    double outside = Math.Sqrt(Math.Max(0, 1 - sine2));
                    ex = dx * eta + (outside - cosine * eta) * nx;
                    ey = dy * eta + (outside - cosine * eta) * ny;
                    ez = dz * eta + (outside - cosine * eta) * nz;
                    if (ey * ny + ez * nz > 0)
                    {
                        Float kernel = (1 - reflectance) * Kernel(sinTheta_i, cosTheta_i, phi_i, ex, ey, ez, scattered ? 3 : reflections == 0 ? 1 : 2);
                        sum += weight * kernel;
                        density += chance * kernel;
                    }
                }
                weight *= reflectance;
                chance *= reflectance;
                dx -= 2 * cosine * nx;
                dy -= 2 * cosine * ny;
                dz -= 2 * cosine * nz;
                ++reflections;
                if (!(dy * ny + dz * nz < 0) || !(weight.MaxComponentValue() > 1e-4f))
                {
                    return;
                }
            }
        }

        // One walk of the light that comes along -wo through the fiber: the direction (ex, ey, ez) in which it leaves, its
        // weight, and its kind: 0 reflected at the surface, 1 through the fiber, 2 reflected inside once or more, 3 scattered.
        // False for a walk that is lost (it does not leave within maxDepth, or its weight is nothing). start: 0 the walk is
        // reflected at the surface by Fresnel's chance; 1 it is reflected; 2 it goes in. reflectance: Fresnel's part there.
        public bool Walk(Vector3f wo, ref RNG rng, int start, out Float reflectance, out double ex, out double ey, out double ez, out SampledSpectrum weight, out int kind)
        {
            reflectance = 0;
            ex = 0;
            ey = 0;
            ez = 0;
            kind = 0;
            weight = new SampledSpectrum(1f);
            if (!Enter(wo, out double y, out double z, out double nx, out double ny, out double nz))
            {
                return false;
            }
            double dx = -wo.x, dy = -wo.y, dz = -wo.z;
            if (!Usable(false, dx, dy, dz, nx, ny, nz))
            {
                Untilt(ref nx, ref ny, ref nz);
            }
            double cosine = -(dx * nx + dy * ny + dz * nz);
            if (!(cosine > 0))
            {
                return false;
            }
            reflectance = pbrt.FrDielectric((Float)cosine, eta);
            if (start == 0 ? rng.Uniform<Float>() < reflectance : start == 1)
            {
                ex = dx + 2 * cosine * nx;
                ey = dy + 2 * cosine * ny;
                ez = dz + 2 * cosine * nz;
                return ey * ny + ez * nz > 0;
            }
            // Into the fiber
            double sine2 = (1 - cosine * cosine) / ((double)eta * eta);
            double inside = Math.Sqrt(Math.Max(0, 1 - sine2));
            dx = dx / eta + (cosine / eta - inside) * nx;
            dy = dy / eta + (cosine / eta - inside) * ny;
            dz = dz / eta + (cosine / eta - inside) * nz;
            int layer = bounds.Length - 1, reflections = 0;
            bool scattered = false;
            int steps = 0;
            while (steps < maxDepth)
            {
                if (!Travel(ref y, ref z, ref dx, ref dy, ref dz, ref layer, ref weight, ref rng, ref scattered, ref steps))
                {
                    return false;
                }
                // The surface, from inside
                Normal(ref y, ref z, out nx, out ny, out nz);
                if (!Usable(true, dx, dy, dz, nx, ny, nz))
                {
                    Untilt(ref nx, ref ny, ref nz);
                }
                cosine = dx * nx + dy * ny + dz * nz;
                if (!(cosine > 0))
                {
                    return false;
                }
                if (rng.Uniform<Float>() < pbrt.FrDielectric((Float)cosine, 1 / eta))
                {
                    dx -= 2 * cosine * nx;
                    dy -= 2 * cosine * ny;
                    dz -= 2 * cosine * nz;
                    ++reflections;
                    if (!(dy * ny + dz * nz < 0) || !(weight.MaxComponentValue() > 1e-5f))
                    {
                        return false;
                    }
                    continue;
                }
                // Out of the fiber
                sine2 = (1 - cosine * cosine) * ((double)eta * eta);
                double outside = Math.Sqrt(Math.Max(0, 1 - sine2));
                ex = dx * eta + (outside - cosine * eta) * nx;
                ey = dy * eta + (outside - cosine * eta) * ny;
                ez = dz * eta + (outside - cosine * eta) * nz;
                kind = scattered ? 3 : reflections == 0 ? 1 : 2;
                return ey * ny + ez * nz > 0;
            }
            return false;
        }

        // Whether the normal of the scales (tilted) serves for the ray d here: the ray comes to its front, its reflection stays
        // on the side of the surface that it came from, and what goes through goes to the other side. At a low angle to the
        // surface the tilt can say otherwise; the normal of the cylinder is used there (Untilt).
        private bool Usable(bool inside, double dx, double dy, double dz, double nx, double ny, double nz)
        {
            double cosine = dx * nx + dy * ny + dz * nz;
            if (inside ? !(cosine > 0) : !(cosine < 0))
            {
                return false;
            }
            // (the side of the surface: by the normal of the cylinder, across the axis)
            double mirror = (dy - 2 * cosine * ny) * ny + (dz - 2 * cosine * nz) * nz;
            if (inside ? !(mirror < 0) : !(mirror > 0))
            {
                return false;
            }
            double ratio = inside ? eta : 1 / (double)eta, magnitude = Math.Abs(cosine);
            double sine2 = (1 - magnitude * magnitude) * ratio * ratio;
            if (sine2 >= 1)
            {
                return true;
            }
            double beyond = Math.Sqrt(1 - sine2), sign = inside ? 1 : -1;
            double ry = dy * ratio + sign * (beyond - magnitude * ratio) * ny, rz = dz * ratio + sign * (beyond - magnitude * ratio) * nz;
            double through = ry * ny + rz * nz;
            return inside ? through > 0 : through < 0;
        }

        private static void Untilt(ref double nx, ref double ny, ref double nz)
        {
            double length = Math.Sqrt(ny * ny + nz * nz);
            nx = 0;
            ny /= length;
            nz /= length;
        }

        // The surface of microfacets, of the roughness (the alpha of Trowbridge and Reitz) given
        public void UseMicrofacets(Float roughness)
        {
            surface = new DielectricBxDF(eta, new TrowbridgeReitzDistribution(roughness, roughness));
        }

        // The surface of microfacets with the light that goes from one facet to another: the walk of Heitz et al. 2016
        // (MultiScatterDielectricBxDF), which loses no light at the surface
        public void UseMicrofacetsOfHeitz(Float roughness)
        {
            surface = new MultiScatterDielectricBxDF(new TrowbridgeReitzDistribution(roughness, roughness), new MicrosurfaceHeightUniform(), eta, 100);
        }

        // The same by FacetForge (FacetForgeBxDF: d'Eon 2023), with the distribution of Trowbridge and Reitz
        public void UseMicrofacetsOfFacetForge(Float roughness)
        {
            ff.Surface rough = new ff.Surface();
            rough.ndf.n = 1;
            rough.ndf.c[0].InitHeightfield(ff.Kind.GGX, roughness, roughness, 2);
            rough.ndf.c[0].w = 1;
            rough.facet.kind = ff.FacetKind.Dielectric;
            surface = new FacetForgeBxDF(rough, null, eta, true);
        }

        // The cross-section: the width across the direction Orientation (radians around the axis, from the second axis of the
        // BSDF) over the width along it, at most 1
        public Float Aspect = 1, Orientation;

        // The walk through the surface of microfacets. connect: at each time at the surface, what it sends to wi is added to
        // `value` (times the weight of the walk) and its density to `density`. The direction in which the walk leaves and its
        // weight; false for a walk that is lost.
        private bool RoughWalk(Vector3f wo, ref RNG rng, bool connect, Vector3f wi, ref SampledSpectrum value, ref Float density, out double ex, out double ey, out double ez,
                               out SampledSpectrum weight)
        {
            ex = 0;
            ey = 0;
            ez = 0;
            weight = new SampledSpectrum(1f);
            if (!Enter(wo, out double y, out double z, out double nx, out double ny, out double nz))
            {
                return false;
            }
            double dx = -wo.x, dy = -wo.y, dz = -wo.z;
            int layer = bounds.Length - 1, steps = 0;
            bool scattered = false, inside = false;
            while (steps < maxDepth)
            {
                if (inside)
                {
                    if (!Travel(ref y, ref z, ref dx, ref dy, ref dz, ref layer, ref weight, ref rng, ref scattered, ref steps))
                    {
                        return false;
                    }
                    Normal(ref y, ref z, out nx, out ny, out nz);
                }
                ++steps;
                // The frame of the surface here: the normal of the scales, and two directions across it
                double tx, ty, tz;
                if (Math.Abs(nx) < 0.9)
                {
                    tx = 0;
                    ty = nz;
                    tz = -ny;
                }
                else
                {
                    tx = -nz;
                    ty = 0;
                    tz = nx;
                }
                double length = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                tx /= length;
                ty /= length;
                tz /= length;
                double bx = ny * tz - nz * ty, by = nz * tx - nx * tz, bz = nx * ty - ny * tx;
                Vector3f from = new Vector3f((Float)(-(dx * tx + dy * ty + dz * tz)), (Float)(-(dx * bx + dy * by + dz * bz)), (Float)(-(dx * nx + dy * ny + dz * nz)));
                if (from.z == 0)
                {
                    return false;
                }
                if (connect)
                {
                    Vector3f to = new Vector3f((Float)(wi.x * tx + wi.y * ty + wi.z * tz), (Float)(wi.x * bx + wi.y * by + wi.z * bz), (Float)(wi.x * nx + wi.y * ny + wi.z * nz));
                    // (out of the fiber from here, and not through the fiber to the far side of this plane)
                    if (to.z > 0)
                    {
                        value += weight * surface.f(from, to, TransportMode.Importance) * to.z;
                        density += surface.PDF(from, to, TransportMode.Importance, BxDFReflTransFlags.All);
                    }
                }
                pstd.optional<BSDFSample> sampled = surface.Sample_f(from, rng.Uniform<Float>(), new Point2f(rng.Uniform<Float>(), rng.Uniform<Float>()), TransportMode.Importance,
                                                                     BxDFReflTransFlags.All);
                if (!sampled || !(sampled.value().pdf > 0))
                {
                    return false;
                }
                BSDFSample next = sampled.value();
                weight *= next.f * (pbrt.AbsCosTheta(next.wi) / next.pdf);
                dx = next.wi.x * tx + next.wi.y * bx + next.wi.z * nx;
                dy = next.wi.x * ty + next.wi.y * by + next.wi.z * ny;
                dz = next.wi.x * tz + next.wi.y * bz + next.wi.z * nz;
                if (next.wi.z > 0)
                {
                    // Out of the fiber
                    ex = dx;
                    ey = dy;
                    ez = dz;
                    return true;
                }
                inside = true;
                if (!(weight.MaxComponentValue() > 1e-5f))
                {
                    return false;
                }
            }
            return false;
        }

        // Where the ray along -wo at the offset h across the fiber meets the surface (y, z: in the circle that the ellipse is
        // made from), and the normal of the scales there
        private bool Enter(Vector3f wo, out double y, out double z, out double nx, out double ny, out double nz)
        {
            y = 0;
            z = 0;
            nx = 0;
            ny = 0;
            nz = 0;
            double across = Math.Sqrt((double)wo.y * wo.y + (double)wo.z * wo.z);
            if (!(across > 0))
            {
                return false;
            }
            double py = wo.y / across, pz = wo.z / across;
            // (across the ray, around the axis: the side of the offset h; the half width of the fiber seen from there)
            double ty = pz, tz = -py;
            double width = Math.Sqrt(ty * ty + (double)Aspect * Aspect * tz * tz);
            double oy = ty * h * width, oz = tz * h * width / Aspect;
            double dy = -py, dz = -pz / Aspect;
            double a = dy * dy + dz * dz, b = oy * dy + oz * dz, c = oy * oy + oz * oz - 1;
            double discriminant = b * b - a * c;
            if (discriminant < 0)
            {
                return false;
            }
            double distance = (-b - Math.Sqrt(discriminant)) / a;
            y = oy + distance * dy;
            z = oz + distance * dz;
            Normal(ref y, ref z, out nx, out ny, out nz);
            return true;
        }

        // The normal of the scales at the point (y, z) of the surface (which is put on the circle): that of the ellipse, tilted
        // along the fiber
        private void Normal(ref double y, ref double z, out double nx, out double ny, out double nz)
        {
            double length = Math.Sqrt(y * y + z * z);
            y /= length;
            z /= length;
            double my = y, mz = z / Aspect;
            double unit = Math.Sqrt(my * my + mz * mz);
            nx = sinAlpha;
            ny = my / unit * cosAlpha;
            nz = mz / unit * cosAlpha;
        }

        // From a point inside the fiber to its surface: through the rings, with their absorption and their scattering. False for
        // a walk that is lost.
        private bool Travel(ref double y, ref double z, ref double dx, ref double dy, ref double dz, ref int layer, ref SampledSpectrum weight, ref RNG rng,
                            ref bool scattered, ref int steps)
        {
            while (steps < maxDepth)
            {
                ++steps;
                // The next ring that the ray meets: the inner one of its layer, or else the outer one (in the circle: the
                // direction across the fiber made wider by the aspect; the lengths are those of the ray itself)
                double ez = dz / Aspect;
                double a = dy * dy + ez * ez, b = y * dy + z * ez, square = y * y + z * z;
                if (!(a > 1e-14))
                {
                    return false;
                }
                double distance = -1;
                int next = layer;
                if (layer > 0 && b < 0)
                {
                    double inner = bounds[layer - 1];
                    double discriminant = b * b - a * (square - inner * inner);
                    if (discriminant > 0)
                    {
                        distance = (-b - Math.Sqrt(discriminant)) / a;
                        next = layer - 1;
                    }
                }
                if (next == layer)
                {
                    double outer = bounds[layer];
                    distance = (-b + Math.Sqrt(Math.Max(0, b * b - a * (square - outer * outer)))) / a;
                    next = layer + 1;
                }
                distance = Math.Max(0, distance);
                // A scattering on the way
                if (scattering[layer] > 0)
                {
                    double flight = -Math.Log(1 - rng.Uniform<Float>()) / scattering[layer];
                    if (flight < distance)
                    {
                        weight *= pbrt.Exp(absorption[layer] * (Float)(-flight));
                        y += flight * dy;
                        z += flight * ez;
                        Scatter(ref dx, ref dy, ref dz, rng.Uniform<Float>(), rng.Uniform<Float>());
                        scattered = true;
                        if (!(weight.MaxComponentValue() > 1e-5f))
                        {
                            return false;
                        }
                        continue;
                    }
                }
                weight *= pbrt.Exp(absorption[layer] * (Float)(-distance));
                y += distance * dy;
                z += distance * ez;
                if (next < bounds.Length)
                {
                    layer = next;
                    continue;
                }
                return true;
            }
            return false;
        }

        // A direction turned by an angle around the axis of the fiber
        private static Vector3f Turned(Vector3f w, Float angle)
        {
            if (angle == 0)
            {
                return w;
            }
            Float cosine = (Float)Math.Cos(angle), sine = (Float)Math.Sin(angle);
            return new Vector3f(w.x, w.y * cosine - w.z * sine, w.y * sine + w.z * cosine);
        }

        // FurBxDF Private Methods

        // The variance along the fiber of a walk of a kind: those of HairBxDF for the walks that are not scattered, and that
        // of its reflection for those that are
        private Float Variance(int kind)
        {
            return kind == 1 ? 0.25f * v0 : kind == 2 ? 4 * v0 : v0;
        }

        // Mp N at the direction (theta_i, phi_i) of a walk that leaves along e
        private Float Kernel(Float sinTheta_i, Float cosTheta_i, Float phi_i, double ex, double ey, double ez, int kind)
        {
            Float sinTheta_e = (Float)(-ex), cosTheta_e = pbrt.SafeSqrt(1 - pbrt.Sqr(sinTheta_e));
            Float dphi = phi_i - (Float)Math.Atan2(ez, ey);
            while (dphi > pbrt.Pi)
            {
                dphi -= 2 * pbrt.Pi;
            }
            while (dphi < -pbrt.Pi)
            {
                dphi += 2 * pbrt.Pi;
            }
            return HairBxDF.Mp(cosTheta_i, cosTheta_e, sinTheta_i, sinTheta_e, Variance(kind)) * pbrt.TrimmedLogistic(dphi, s, -pbrt.Pi, pbrt.Pi);
        }

        // A new direction from the phase function of Henyey and Greenstein
        private void Scatter(ref double dx, ref double dy, ref double dz, double u0, double u1)
        {
            double cosine;
            if (Math.Abs(g) < 1e-3)
            {
                cosine = 1 - 2 * u0;
            }
            else
            {
                double term = (1 - g * g) / (1 + g - 2 * g * u0);
                cosine = (1 + g * g - term * term) / (2 * g);
            }
            double sine = Math.Sqrt(Math.Max(0, 1 - cosine * cosine)), phi = 2 * Math.PI * u1;
            // (two directions across the old one)
            double ax, ay, az;
            if (Math.Abs(dx) < 0.9)
            {
                ax = 0;
                ay = dz;
                az = -dy;
            }
            else
            {
                ax = -dz;
                ay = 0;
                az = dx;
            }
            double length = Math.Sqrt(ax * ax + ay * ay + az * az);
            ax /= length;
            ay /= length;
            az /= length;
            double bx = dy * az - dz * ay, by = dz * ax - dx * az, bz = dx * ay - dy * ax;
            double c = sine * Math.Cos(phi), d = sine * Math.Sin(phi);
            double x = cosine * dx + c * ax + d * bx, yy = cosine * dy + c * ay + d * by, zz = cosine * dz + c * az + d * bz;
            // (of unit length again: the rounding of many scatterings adds up)
            double unit = Math.Sqrt(x * x + yy * yy + zz * zz);
            dx = x / unit;
            dy = yy / unit;
            dz = zz / unit;
        }

        // FurBxDF Private Members
        private readonly Float h, eta, g, v0, s;
        private readonly Float[] bounds, scattering;
        private readonly SampledSpectrum[] absorption;
        private readonly double sinAlpha, cosAlpha;
        private readonly int samples, maxDepth;
        private BxDF surface;
    }
}
