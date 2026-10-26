// pbrt is Copyright(c) 1998-2020 Matt Pharr, Wenzel Jakob, and Greg Humphreys.
// The pbrt source code is licensed under the Apache License, Version 2.0.
// SPDX: Apache-2.0
// interaction.h, interaction.cpp
//
// Interaction / MediumInteraction / SurfaceInteraction keep pbrt's inheritance, so they are classes
// (C# structs cannot inherit); the C-style casts in AsSurface()/AsMedium() become reference casts.
// Where C++ copies one by value the code calls Clone().

using System;

namespace pbrt
{

    // Interaction Definition
    public unsafe class Interaction
    {
        // Interaction Public Methods
        public Interaction() { }

        public Interaction(Point3fi pi, Normal3f n, Point2f uv, Vector3f wo, Float time)
        {
            this.pi = pi;
            this.n = n;
            this.uv = uv;
            this.wo = Globals.Normalize(wo);
            this.time = time;
        }

        public Point3f p() { return (Point3f)pi; }

        public bool IsSurfaceInteraction() { return n != new Normal3f(0, 0, 0); }
        public bool IsMediumInteraction() { return !IsSurfaceInteraction(); }

        public SurfaceInteraction AsSurface()
        {
            Globals.CHECK(IsSurfaceInteraction());
            return (SurfaceInteraction)this;
        }

        // used by medium ctor
        public Interaction(Point3f p, Vector3f wo, Float time, Medium medium)
        {
            this.pi = new Point3fi(p);
            this.time = time;
            this.wo = wo;
            this.medium = medium;
        }
        public Interaction(Point3f p, Normal3f n, Float time, Medium medium)
        {
            this.pi = new Point3fi(p);
            this.n = n;
            this.time = time;
            this.medium = medium;
        }
        public Interaction(Point3f p, Point2f uv)
        {
            this.pi = new Point3fi(p);
            this.uv = uv;
        }
        public Interaction(Point3fi pi, Normal3f n, Float time = 0, Point2f uv = default)
        {
            this.pi = pi;
            this.n = n;
            this.uv = uv;
            this.time = time;
        }
        public Interaction(Point3fi pi, Normal3f n, Point2f uv)
        {
            this.pi = pi;
            this.n = n;
            this.uv = uv;
        }
        public Interaction(Point3f p, Float time, Medium medium)
        {
            this.pi = new Point3fi(p);
            this.time = time;
            this.medium = medium;
        }
        public Interaction(Point3f p, MediumInterface mediumInterface)
        {
            this.pi = new Point3fi(p);
            this.mediumInterface = mediumInterface;
        }
        public Interaction(Point3f p, Float time, MediumInterface mediumInterface)
        {
            this.pi = new Point3fi(p);
            this.time = time;
            this.mediumInterface = mediumInterface;
        }

        public MediumInteraction AsMedium()
        {
            Globals.CHECK(IsMediumInteraction());
            return (MediumInteraction)this;
        }

        public override string ToString()
        {
            return Globals.StringPrintf(
                "[ Interaction pi: %s n: %s uv: %s wo: %s time: %s " +
                "medium: %s mediumInterface: %s ]",
                pi, n, uv, wo, time, medium != null ? medium.ToString() : "(nullptr)",
                mediumInterface != null ? mediumInterface.ToString() : "(nullptr)");
        }

        // (C++ copies Interaction objects by value)
        public Interaction Clone() { return (Interaction)MemberwiseClone(); }

        public Point3f OffsetRayOrigin(Vector3f w) { return Globals.OffsetRayOrigin(pi, n, w); }

        public Point3f OffsetRayOrigin(Point3f pt) { return OffsetRayOrigin(pt - p()); }

        public RayDifferential SpawnRay(Vector3f d)
        {
            return new RayDifferential(OffsetRayOrigin(d), d, time, GetMedium(d));
        }

        public Ray SpawnRayTo(Point3f p2)
        {
            Ray r = Globals.SpawnRayTo(pi, n, time, p2);
            r.medium = GetMedium(r.d);
            return r;
        }

        public Ray SpawnRayTo(Interaction it)
        {
            Ray r = Globals.SpawnRayTo(pi, n, time, it.pi, it.n);
            r.medium = GetMedium(r.d);
            return r;
        }

        public Medium GetMedium(Vector3f w)
        {
            if (mediumInterface != null)
                return Globals.Dot(w, n) > 0 ? mediumInterface.outside : mediumInterface.inside;
            return medium;
        }

        public Medium GetMedium()
        {
            if (mediumInterface != null)
                Globals.DCHECK(mediumInterface.inside == mediumInterface.outside);
            return mediumInterface != null ? mediumInterface.inside : medium;
        }

        // Interaction Public Members
        public Point3fi pi;
        public Float time = 0;
        public Vector3f wo;
        public Normal3f n;
        public Point2f uv;
        public MediumInterface mediumInterface = null;
        public Medium medium = null;
    }

    // MediumInteraction Definition
    public unsafe class MediumInteraction : Interaction
    {
        // MediumInteraction Public Methods
        public MediumInteraction() { phase = null; }
        public MediumInteraction(Point3f p, Vector3f wo, Float time, Medium medium, PhaseFunction phase)
            : base(p, wo, time, medium)
        {
            this.phase = phase;
        }

        public override string ToString()
        {
            return Globals.StringPrintf(
                "[ MediumInteraction pi: %s n: %s uv: %s wo: %s time: %s " +
                "medium: %s mediumInterface: %s phase: %s ]",
                pi, n, uv, wo, time, medium != null ? medium.ToString() : "(nullptr)",
                mediumInterface != null ? mediumInterface.ToString() : "(nullptr)",
                phase != null ? phase.ToString() : "(nullptr)");
        }

        // MediumInteraction Public Members
        public PhaseFunction phase;
    }

    // SurfaceInteraction Definition
    public unsafe class SurfaceInteraction : Interaction
    {
        // SurfaceInteraction Public Types
        public struct Shading
        {
            public Normal3f n;
            public Vector3f dpdu, dpdv;
            public Normal3f dndu, dndv;
        }

        // SurfaceInteraction Public Methods
        public SurfaceInteraction() { }

        public SurfaceInteraction(Point3fi pi, Point2f uv, Vector3f wo, Vector3f dpdu, Vector3f dpdv, Normal3f dndu,
                                  Normal3f dndv, Float time, bool flipNormal)
            : base(pi, (Normal3f)Globals.Normalize(Globals.Cross(dpdu, dpdv)), uv, wo, time)
        {
            this.dpdu = dpdu;
            this.dpdv = dpdv;
            this.dndu = dndu;
            this.dndv = dndv;
            // Initialize shading geometry from true geometry
            shading.n = n;
            shading.dpdu = dpdu;
            shading.dpdv = dpdv;
            shading.dndu = dndu;
            shading.dndv = dndv;

            // Adjust normal based on orientation and handedness
            if (flipNormal)
            {
                n *= -1;
                shading.n *= -1;
            }
        }

        public SurfaceInteraction(Point3fi pi, Point2f uv, Vector3f wo, Vector3f dpdu, Vector3f dpdv, Normal3f dndu,
                                  Normal3f dndv, Float time, bool flipNormal, int faceIndex)
            : this(pi, uv, wo, dpdu, dpdv, dndu, dndv, time, flipNormal)
        {
            this.faceIndex = faceIndex;
        }

        public void SetShadingGeometry(Normal3f ns, Vector3f dpdus, Vector3f dpdvs, Normal3f dndus, Normal3f dndvs,
                                       bool orientationIsAuthoritative)
        {
            // Compute _shading.n_ for _SurfaceInteraction_
            shading.n = ns;
            Globals.DCHECK(shading.n != new Normal3f(0, 0, 0));
            if (orientationIsAuthoritative)
                n = Globals.FaceForward(n, shading.n);
            else
                shading.n = Globals.FaceForward(shading.n, n);

            // Initialize _shading_ partial derivative values
            shading.dpdu = dpdus;
            shading.dpdv = dpdvs;
            shading.dndu = dndus;
            shading.dndv = dndvs;
            while (Globals.LengthSquared(shading.dpdu) > 1e16f || Globals.LengthSquared(shading.dpdv) > 1e16f)
            {
                shading.dpdu /= 1e8f;
                shading.dpdv /= 1e8f;
            }
        }

        public override string ToString()
        {
            return Globals.StringPrintf(
                "[ SurfaceInteraction pi: %s n: %s uv: %s wo: %s time: %s " +
                "medium: %s mediumInterface: %s dpdu: %s dpdv: %s dndu: %s dndv: %s " +
                "shading.n: %s shading.dpdu: %s shading.dpdv: %s " +
                "shading.dndu: %s shading.dndv: %s material: %s " +
                "areaLight: %s dpdx: %s dpdy: %s dudx: %f dvdx: %f " +
                "dudy: %f dvdy: %f faceIndex: %d ]",
                pi, n, uv, wo, time, medium != null ? medium.ToString() : "(nullptr)",
                mediumInterface != null ? mediumInterface.ToString() : "(nullptr)", dpdu, dpdv, dndu, dndv, shading.n,
                shading.dpdu, shading.dpdv, shading.dndu, shading.dndv,
                material != null ? material.ToString() : "(nullptr)",
                areaLight != null ? areaLight.ToString() : "(nullptr)", dpdx, dpdy, dudx, dvdx, dudy, dvdy, faceIndex);
        }

        public void SetIntersectionProperties(Material mtl, Light area, MediumInterface primMediumInterface,
                                              Medium rayMedium)
        {
            material = mtl;
            areaLight = area;
            Globals.CHECK_GE(Globals.Dot(n, shading.n), 0f);
            // Set medium properties at surface intersection
            if (primMediumInterface != null && primMediumInterface.IsMediumTransition())
                mediumInterface = primMediumInterface;
            else
                medium = rayMedium;
        }

        public void ComputeDifferentials(RayDifferential ray, Camera camera, int samplesPerPixel)
        {
            if (Globals.GetOptions().disableTextureFiltering)
            {
                dudx = dudy = dvdx = dvdy = 0;
                dpdx = dpdy = new Vector3f(0, 0, 0);
                return;
            }
            if (ray.hasDifferentials && Globals.Dot(n, ray.rxDirection) != 0 && Globals.Dot(n, ray.ryDirection) != 0)
            {
                // Estimate screen-space change in $\pt{}$ using ray differentials
                // Compute auxiliary intersection points with plane, _px_ and _py_
                Float d = -Globals.Dot(n, (Vector3f)p());
                Float tx = (-Globals.Dot(n, (Vector3f)ray.rxOrigin) - d) / Globals.Dot(n, ray.rxDirection);
                Globals.DCHECK(!Globals.IsInf(tx) && !Globals.IsNaN(tx));
                Point3f px = ray.rxOrigin + tx * ray.rxDirection;
                Float ty = (-Globals.Dot(n, (Vector3f)ray.ryOrigin) - d) / Globals.Dot(n, ray.ryDirection);
                Globals.DCHECK(!Globals.IsInf(ty) && !Globals.IsNaN(ty));
                Point3f py = ray.ryOrigin + ty * ray.ryDirection;

                dpdx = px - p();
                dpdy = py - p();
            }
            else
            {
                // Approximate screen-space change in $\pt{}$ based on camera projection
                Vector3f ldpdx = default, ldpdy = default;
                camera.Approximate_dp_dxy(p(), n, time, samplesPerPixel, &ldpdx, &ldpdy);
                dpdx = ldpdx;
                dpdy = ldpdy;
            }
            // Estimate screen-space change in $(u,v)$
            // Compute $\transpose{\XFORM{A}} \XFORM{A}$ and its determinant
            Float ata00 = Globals.Dot(dpdu, dpdu), ata01 = Globals.Dot(dpdu, dpdv);
            Float ata11 = Globals.Dot(dpdv, dpdv);
            Float invDet = 1 / Globals.DifferenceOfProducts(ata00, ata11, ata01, ata01);
            invDet = Globals.IsFinite(invDet) ? invDet : 0f;

            // Compute $\transpose{\XFORM{A}} \VEC{b}$ for $x$ and $y$
            Float atb0x = Globals.Dot(dpdu, dpdx), atb1x = Globals.Dot(dpdv, dpdx);
            Float atb0y = Globals.Dot(dpdu, dpdy), atb1y = Globals.Dot(dpdv, dpdy);

            // Compute $u$ and $v$ derivatives with respect to $x$ and $y$
            dudx = Globals.DifferenceOfProducts(ata11, atb0x, ata01, atb1x) * invDet;
            dvdx = Globals.DifferenceOfProducts(ata00, atb1x, ata01, atb0x) * invDet;
            dudy = Globals.DifferenceOfProducts(ata11, atb0y, ata01, atb1y) * invDet;
            dvdy = Globals.DifferenceOfProducts(ata00, atb1y, ata01, atb0y) * invDet;

            // Clamp derivatives of $u$ and $v$ to reasonable values
            dudx = Globals.IsFinite(dudx) ? Globals.Clamp(dudx, -1e8f, 1e8f) : 0f;
            dvdx = Globals.IsFinite(dvdx) ? Globals.Clamp(dvdx, -1e8f, 1e8f) : 0f;
            dudy = Globals.IsFinite(dudy) ? Globals.Clamp(dudy, -1e8f, 1e8f) : 0f;
            dvdy = Globals.IsFinite(dvdy) ? Globals.Clamp(dvdy, -1e8f, 1e8f) : 0f;
        }

        public void SkipIntersection(ref RayDifferential ray, Float t)
        {
            // *((Ray *)ray) = SpawnRay(ray->d);
            RayDifferential spawned = SpawnRay(ray.d);
            ray.o = spawned.o;
            ray.d = spawned.d;
            ray.time = spawned.time;
            ray.medium = spawned.medium;
            if (ray.hasDifferentials)
            {
                ray.rxOrigin = ray.rxOrigin + t * ray.rxDirection;
                ray.ryOrigin = ray.ryOrigin + t * ray.ryDirection;
            }
        }

        public RayDifferential SpawnRay(RayDifferential rayi, BSDF bsdf, Vector3f wi, BxDFFlags flags, Float eta)
        {
            RayDifferential rd = SpawnRay(wi);
            if (rayi.hasDifferentials)
            {
                // Compute ray differentials for specular reflection or transmission
                // Compute common factors for specular ray differentials
                Normal3f n = shading.n;
                Normal3f dndx = shading.dndu * dudx + shading.dndv * dvdx;
                Normal3f dndy = shading.dndu * dudy + shading.dndv * dvdy;
                Vector3f dwodx = -rayi.rxDirection - wo, dwody = -rayi.ryDirection - wo;

                if (flags == BxDFFlags.SpecularReflection)
                {
                    // Initialize origins of specular differential rays
                    rd.hasDifferentials = true;
                    rd.rxOrigin = p() + dpdx;
                    rd.ryOrigin = p() + dpdy;

                    // Compute differential reflected directions
                    Float dwoDotn_dx = Globals.Dot(dwodx, n) + Globals.Dot(wo, dndx);
                    Float dwoDotn_dy = Globals.Dot(dwody, n) + Globals.Dot(wo, dndy);
                    rd.rxDirection = wi - dwodx + 2 * (Vector3f)(Globals.Dot(wo, n) * dndx + dwoDotn_dx * n);
                    rd.ryDirection = wi - dwody + 2 * (Vector3f)(Globals.Dot(wo, n) * dndy + dwoDotn_dy * n);
                }
                else if (flags == BxDFFlags.SpecularTransmission)
                {
                    // Initialize origins of specular differential rays
                    rd.hasDifferentials = true;
                    rd.rxOrigin = p() + dpdx;
                    rd.ryOrigin = p() + dpdy;

                    // Compute differential transmitted directions
                    // Find oriented surface normal for transmission
                    if (Globals.Dot(wo, n) < 0)
                    {
                        n = -n;
                        dndx = -dndx;
                        dndy = -dndy;
                    }

                    // Compute partial derivatives of $\mu$
                    Float dwoDotn_dx = Globals.Dot(dwodx, n) + Globals.Dot(wo, dndx);
                    Float dwoDotn_dy = Globals.Dot(dwody, n) + Globals.Dot(wo, dndy);
                    Float mu = Globals.Dot(wo, n) / eta - Globals.AbsDot(wi, n);
                    Float dmudx = dwoDotn_dx * (1 / eta + 1 / Globals.Sqr(eta) * Globals.Dot(wo, n) / Globals.Dot(wi, n));
                    Float dmudy = dwoDotn_dy * (1 / eta + 1 / Globals.Sqr(eta) * Globals.Dot(wo, n) / Globals.Dot(wi, n));

                    rd.rxDirection = wi - eta * dwodx + (Vector3f)(mu * dndx + dmudx * n);
                    rd.ryDirection = wi - eta * dwody + (Vector3f)(mu * dndy + dmudy * n);
                }
            }
            // Squash potentially troublesome differentials
            if (Globals.LengthSquared(rd.rxDirection) > 1e16f || Globals.LengthSquared(rd.ryDirection) > 1e16f ||
                Globals.LengthSquared((Vector3f)rd.rxOrigin) > 1e16f || Globals.LengthSquared((Vector3f)rd.ryOrigin) > 1e16f)
                rd.hasDifferentials = false;

            return rd;
        }

        public BSDF GetBSDF(RayDifferential ray, ref SampledWavelengths lambda, Camera camera, ScratchBuffer scratchBuffer,
                            Sampler sampler)
        {
            // Estimate $(u,v)$ and position differentials at intersection point
            ComputeDifferentials(ray, camera, sampler.SamplesPerPixel());

            // Resolve _MixMaterial_ if necessary
            while (material is MixMaterial)
            {
                MixMaterial mix = (MixMaterial)material;
                material = mix.ChooseMaterial(new UniversalTextureEvaluator(), this);
            }

            // Return unset _BSDF_ if surface has a null material
            if (material == null)
                return default;

            // Evaluate normal or bump map, if present
            FloatTexture displacement = material.GetDisplacement();
            Image normalMap = material.GetNormalMap();
            if (displacement != null || normalMap != null)
            {
                // Get shading $\dpdu$ and $\dpdv$ using normal or bump map
                Vector3f dpdu, dpdv;
                dpdu = default;
                dpdv = default;
                if (normalMap != null)
                    Globals.NormalMap(normalMap, new NormalBumpEvalContext(this), &dpdu, &dpdv);
                else
                    Globals.BumpMap(new UniversalTextureEvaluator(), displacement, new NormalBumpEvalContext(this), &dpdu, &dpdv);

                Normal3f ns = (Normal3f)Globals.Normalize(Globals.Cross(dpdu, dpdv));
                SetShadingGeometry(ns, dpdu, dpdv, shading.dndu, shading.dndv, false);
            }

            // Return BSDF for surface interaction
            BSDF bsdf = material.GetBSDF(new UniversalTextureEvaluator(), new MaterialEvalContext(this), ref lambda, scratchBuffer);
            if (bsdf && Globals.GetOptions().forceDiffuse)
            {
                // Override _bsdf_ with diffuse equivalent
                SampledSpectrum r = bsdf.rho(wo, new pstd.span<Float>(new Float[] { sampler.Get1D() }),
                                             new pstd.span<Point2f>(new Point2f[] { sampler.Get2D() }));
                bsdf = new BSDF(shading.n, shading.dpdu, scratchBuffer.Alloc(new DiffuseBxDF(r)));
            }
            return bsdf;
        }

        public BSSRDF GetBSSRDF(RayDifferential ray, ref SampledWavelengths lambda, Camera camera, ScratchBuffer scratchBuffer)
        {
            // Resolve _MixMaterial_ if necessary
            while (material is MixMaterial)
            {
                MixMaterial mix = (MixMaterial)material;
                material = mix.ChooseMaterial(new UniversalTextureEvaluator(), this);
            }

            return material.GetBSSRDF(new UniversalTextureEvaluator(), new MaterialEvalContext(this), ref lambda, scratchBuffer);
        }

        public SampledSpectrum Le(Vector3f w, SampledWavelengths lambda)
        {
            return areaLight != null ? areaLight.L(p(), n, uv, w, lambda) : new SampledSpectrum(0f);
        }

        // (C++ copies SurfaceInteraction objects by value)
        public new SurfaceInteraction Clone() { return (SurfaceInteraction)MemberwiseClone(); }

        // SurfaceInteraction Public Members
        public Vector3f dpdu, dpdv;
        public Normal3f dndu, dndv;
        public Shading shading;
        public int faceIndex = 0;
        public Material material;
        public Light areaLight;
        public Vector3f dpdx, dpdy;
        public Float dudx = 0, dvdx = 0, dudy = 0, dvdy = 0;
    }

}  // namespace pbrt
